using Seed.Market.Indicators;
using Seed.Market.Signals;

namespace Seed.Market.Data;

/// <summary>
/// Orchestrates all data feeds, merges results into a SignalSnapshot,
/// and provides the normalized float[88] vector for brain consumption.
/// Each feed runs on its own interval; stale data is carried forward.
/// </summary>
public sealed class DataAggregator : IDisposable
{
    private readonly IDataFeed[] _feeds;
    private readonly HttpClient _client;
    private readonly SignalNormalizer _normalizer;
    private readonly float[] _rawSignals = new float[SignalIndex.Count];
    private readonly DateTimeOffset[] _lastFeedUpdate;
    private readonly List<TechnicalIndicators.Candle> _candleHistory = [];
    private long _tick;
    private readonly object _lock = new();
    private float _prevCumulativeVolume;
    private long _lastBarPeriod = -1;
    private readonly long _barDurationMs;
    private readonly int _barsPerHour;
    private readonly int _maxCandleHistory;

    public DateTimeOffset LastTickTime { get; private set; }

    /// <summary>Raw (un-normalized) BTC price from the last tick -- for portfolio tracking in paper/live mode.</summary>
    public float LastRawBtcPrice { get; private set; }

    /// <summary>Raw bar BTC volume from the last tick.</summary>
    public float LastRawVolume { get; private set; }

    /// <summary>Raw funding rate from the last tick.</summary>
    public float LastRawFundingRate { get; private set; }

    private readonly Queue<float> _barBtcReturns = new();
    private readonly Queue<float> _barEthReturns = new();
    private readonly Queue<float> _barSpxReturns = new();
    private readonly Queue<float> _barBtcPrices = new();
    private readonly Queue<float> _barEthPrices = new();
    private float _lastBarBtcPrice;
    private float _lastBarEthPrice;
    private long _derivedLastPeriod = -1;
    private float _prevRegimeVolatility;
    private readonly int _corrWindow;
    private readonly int _momentumLookback;

    public DataAggregator(MarketConfig? config = null)
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.Add("User-Agent", "SeedMarket/1.0");

        _barsPerHour = config?.BarsPerHour ?? 1;
        _barDurationMs = config?.BarDurationMs ?? 3_600_000L;
        _corrWindow = 168 * _barsPerHour;
        _momentumLookback = 12 * _barsPerHour;
        _maxCandleHistory = 200 * _barsPerHour;

        string interval = config?.CandleInterval ?? "1h";
        var feeds = new List<IDataFeed>
        {
            new BinanceSpotFeed(interval, _barsPerHour),
            new BinanceFuturesFeed(interval),
            new SentimentFeed(),
            new OnChainFeed(),
            new MacroFeed(),
            new StablecoinFeed(),
            new DeribitFeed(),  // V14: options-based sentiment (DVOL, put/call)
        };

        if (!string.IsNullOrEmpty(config?.CoinglassApiKey))
            feeds.Add(new CoinglassFeed(config.CoinglassApiKey));
        else
            Console.Error.WriteLine("[DataAggregator] WARNING: No CoinglassApiKey configured — liquidation, exchange flow, and supply signals will be zero");

        _feeds = feeds.ToArray();

        _lastFeedUpdate = new DateTimeOffset[_feeds.Length];
        _normalizer = new SignalNormalizer();
    }

    /// <summary>
    /// Fetch all feeds that are due for an update and return a normalized snapshot.
    /// Safe to call frequently -- only stale feeds are re-fetched.
    /// </summary>
    public async Task<SignalSnapshot> TickAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        LastTickTime = now;
        var tasks = new List<Task<(int feedIdx, FeedResult result)>>();

        for (int i = 0; i < _feeds.Length; i++)
        {
            if (now - _lastFeedUpdate[i] >= _feeds[i].Interval)
            {
                int idx = i;
                tasks.Add(Task.Run(async () =>
                {
                    var result = await _feeds[idx].FetchAsync(_client, ct);
                    return (idx, result);
                }, ct));
            }
        }

        if (tasks.Count > 0)
        {
            var results = await Task.WhenAll(tasks);

            lock (_lock)
            {
                foreach (var (feedIdx, result) in results)
                {
                    if (result.Success)
                    {
                        foreach (var (index, value) in result.Signals)
                        {
                            if (index >= 0 && index < SignalIndex.Count)
                                _rawSignals[index] = value;
                        }
                        _lastFeedUpdate[feedIdx] = now;
                    }
                }

                // Update candle history for technical indicators
                UpdateCandleHistory(now);

                // Compute technical indicators from candle history
                if (_candleHistory.Count >= 26)
                {
                    var techSignals = TechnicalIndicators.Compute(
                        _candleHistory.ToArray().AsSpan());
                    foreach (var (index, value) in techSignals)
                        _rawSignals[index] = value;
                }

                // Compute temporal encoding
                var timeSignals = TimeEncoding.Compute(now);
                foreach (var (index, value) in timeSignals)
                    _rawSignals[index] = value;

                ComputeDerivedSignals();
                ZeroMaskLiveOnlySignals();
            }
        }

        SignalSnapshot snapshot;
        lock (_lock)
        {
            LastRawBtcPrice = _rawSignals[SignalIndex.BtcPrice];
            LastRawVolume = _rawSignals[SignalIndex.BtcVolume1h];
            LastRawFundingRate = _rawSignals[SignalIndex.FundingRate];
            var health = EvaluateHealth();
            snapshot = _normalizer.Normalize(_rawSignals, now, _tick++);
            snapshot = new SignalSnapshot(snapshot.Signals, snapshot.Timestamp, snapshot.TickNumber, health);
        }

        return snapshot;
    }

    /// <summary>
    /// Build a snapshot from pre-computed raw signals (for backtest/historical replay).
    /// Bypasses API fetching entirely.
    /// </summary>
    public SignalSnapshot TickFromRaw(float[] rawSignals, DateTimeOffset timestamp)
    {
        lock (_lock)
        {
            return _normalizer.Normalize(rawSignals, timestamp, _tick++);
        }
    }

    private void UpdateCandleHistory(DateTimeOffset now)
    {
        float price = _rawSignals[SignalIndex.BtcPrice];
        float cumulativeVol = _rawSignals[SignalIndex.BtcVolume1h];
        if (price <= 0) return;

        float volumeIncrement = cumulativeVol - _prevCumulativeVolume;
        if (volumeIncrement < 0) volumeIncrement = cumulativeVol;
        _prevCumulativeVolume = cumulativeVol;

        long currentPeriod = now.ToUnixTimeMilliseconds() / _barDurationMs;
        bool newBar = _candleHistory.Count == 0 || currentPeriod != _lastBarPeriod;

        if (newBar)
        {
            _lastBarPeriod = currentPeriod;
            _candleHistory.Add(new TechnicalIndicators.Candle(
                price, price, price, price, volumeIncrement, now));

            while (_candleHistory.Count > _maxCandleHistory)
                _candleHistory.RemoveAt(0);
        }
        else
        {
            var last = _candleHistory[^1];
            _candleHistory[^1] = last with
            {
                High = MathF.Max(last.High, price),
                Low = MathF.Min(last.Low, price),
                Close = price,
                Volume = last.Volume + volumeIncrement
            };
        }
    }

    private SignalHealth EvaluateHealth()
    {
        int healthy = _feeds.Count(f => f.IsHealthy);
        if (healthy == _feeds.Length) return SignalHealth.Full;
        if (healthy >= _feeds.Length / 2) return SignalHealth.Partial;
        return SignalHealth.Stale;
    }

    public string GetStatusReport()
    {
        var lines = _feeds.Select((f, i) =>
            $"  {f.Name,-20} {(f.IsHealthy ? "OK" : "FAIL"),-6} last: {_lastFeedUpdate[i]:HH:mm:ss}");
        return $"DataAggregator Status (tick {_tick}):\n{string.Join("\n", lines)}";
    }

    public void Reset()
    {
        lock (_lock)
        {
            Array.Clear(_rawSignals);
            Array.Clear(_lastFeedUpdate);
            _candleHistory.Clear();
            _normalizer.Reset();
            _tick = 0;
        }
    }

    /// <summary>
    /// Signals with no reliable historical source are zeroed so the brain
    /// never trains on data it won't have in deployment (or vice versa).
    ///
    /// V14 notes:
    /// - BidAskSpread/OrderImbalance remain live-only (WebSocket order book)
    /// - News/Reddit slots (25-29) are now Deribit options, populated in both paths
    /// - SentimentMomentum is live-only (derived from Fear & Greed delta)
    /// - FuturesPremium: computed by BinanceFuturesFeed / BinanceFuturesHistorical, available in both paths
    /// </summary>
    private void ZeroMaskLiveOnlySignals()
    {
        _rawSignals[SignalIndex.BtcBidAskSpread] = 0f;
        _rawSignals[SignalIndex.BtcOrderImbalance] = 0f;
        _rawSignals[SignalIndex.SentimentMomentum] = 0f;
    }

    private void ComputeDerivedSignals()
    {
        float btcPrice = _rawSignals[SignalIndex.BtcPrice];
        float ethPrice = _rawSignals[SignalIndex.EthPrice];

        _rawSignals[SignalIndex.BtcEthSpread] = btcPrice > 0 && ethPrice > 0
            ? ethPrice / btcPrice : 0f;

        long currentPeriod = LastTickTime.ToUnixTimeMilliseconds() / _barDurationMs;
        bool newBar = _derivedLastPeriod != -1 && currentPeriod != _derivedLastPeriod;

        if (newBar && btcPrice > 0)
        {
            float btcRet = _lastBarBtcPrice > 0
                ? (btcPrice - _lastBarBtcPrice) / _lastBarBtcPrice : 0f;
            float ethRet = _lastBarEthPrice > 0
                ? (ethPrice - _lastBarEthPrice) / _lastBarEthPrice : 0f;
            float spxRet = _rawSignals[SignalIndex.Sp500Return];

            _barBtcReturns.Enqueue(btcRet); if (_barBtcReturns.Count > _corrWindow) _barBtcReturns.Dequeue();
            _barEthReturns.Enqueue(ethRet); if (_barEthReturns.Count > _corrWindow) _barEthReturns.Dequeue();
            _barSpxReturns.Enqueue(spxRet); if (_barSpxReturns.Count > _corrWindow) _barSpxReturns.Dequeue();
            _barBtcPrices.Enqueue(btcPrice); if (_barBtcPrices.Count > _corrWindow) _barBtcPrices.Dequeue();
            _barEthPrices.Enqueue(ethPrice); if (_barEthPrices.Count > _corrWindow) _barEthPrices.Dequeue();

            _lastBarBtcPrice = btcPrice;
            _lastBarEthPrice = ethPrice;
        }
        else if (_derivedLastPeriod == -1 && btcPrice > 0)
        {
            _lastBarBtcPrice = btcPrice;
            _lastBarEthPrice = ethPrice;
        }
        _derivedLastPeriod = currentPeriod;

        if (_barBtcReturns.Count < 2) return;

        var btcArr = _barBtcReturns.ToArray();
        var ethArr = _barEthReturns.ToArray();
        var spxArr = _barSpxReturns.ToArray();

        _rawSignals[SignalIndex.BtcSp500Correlation] = PearsonCorrelation(btcArr, spxArr);
        _rawSignals[SignalIndex.BtcEthCorrelation] = PearsonCorrelation(btcArr, ethArr);
        _rawSignals[SignalIndex.BtcVolatility] = StdDev(btcArr);
        _rawSignals[SignalIndex.EthVolatility] = StdDev(ethArr);
        _rawSignals[SignalIndex.VolatilityRatio] = _rawSignals[SignalIndex.EthVolatility] > 0
            ? _rawSignals[SignalIndex.BtcVolatility] / _rawSignals[SignalIndex.EthVolatility] : 1f;

        if (_barBtcPrices.Count >= _momentumLookback)
        {
            var btcP = _barBtcPrices.ToArray();
            var ethP = _barEthPrices.ToArray();
            int lookback = Math.Min(_momentumLookback, btcP.Length);
            float btcOld = btcP[btcP.Length - lookback];
            float ethOld = ethP[ethP.Length - lookback];
            _rawSignals[SignalIndex.BtcMomentum] = btcOld > 0 ? (btcPrice - btcOld) / btcOld : 0f;
            _rawSignals[SignalIndex.EthMomentum] = ethOld > 0 ? (ethPrice - ethOld) / ethOld : 0f;
        }
        _rawSignals[SignalIndex.MomentumDivergence] =
            _rawSignals[SignalIndex.BtcMomentum] - _rawSignals[SignalIndex.EthMomentum];

        ComputeRegimeSignals(btcArr);
    }

    private void ComputeRegimeSignals(float[] btcReturns)
    {
        float vol = _rawSignals[SignalIndex.BtcVolatility];
        float volPercentile = MathF.Min(vol / 0.05f, 1f);
        _rawSignals[SignalIndex.RegimeVolatility] = volPercentile;

        float ret24h = _rawSignals[SignalIndex.BtcReturn24h];
        _rawSignals[SignalIndex.RegimeTrend] = Math.Clamp(ret24h / 0.10f, -1f, 1f);

        float volDelta = vol - _prevRegimeVolatility;
        _rawSignals[SignalIndex.RegimeChange] = Math.Clamp(volDelta / 0.02f, -1f, 1f);
        _prevRegimeVolatility = vol;

        float vixChange = _rawSignals[SignalIndex.VixChange];
        float liqLong = _rawSignals[SignalIndex.LiquidationLong1h];
        float liqShort = _rawSignals[SignalIndex.LiquidationShort1h];
        float fundingAbs = MathF.Abs(_rawSignals[SignalIndex.FundingRate]);
        float stress = Math.Clamp(
            MathF.Abs(vixChange) * 2f + (liqLong + liqShort) * 0.5f + fundingAbs * 10f + volPercentile * 0.5f,
            0f, 1f);
        _rawSignals[SignalIndex.MarketStress] = stress;
    }

    private static float PearsonCorrelation(float[] a, float[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        if (n < 2) return 0f;
        float meanA = 0f, meanB = 0f;
        for (int i = 0; i < n; i++) { meanA += a[i]; meanB += b[i]; }
        meanA /= n; meanB /= n;
        float cov = 0f, varA = 0f, varB = 0f;
        for (int i = 0; i < n; i++)
        {
            float da = a[i] - meanA, db = b[i] - meanB;
            cov += da * db; varA += da * da; varB += db * db;
        }
        float denom = MathF.Sqrt(varA * varB);
        return denom > 0 ? cov / denom : 0f;
    }

    private static float StdDev(float[] arr)
    {
        if (arr.Length < 2) return 0f;
        float mean = 0f;
        for (int i = 0; i < arr.Length; i++) mean += arr[i];
        mean /= arr.Length;
        float sumSq = 0f;
        for (int i = 0; i < arr.Length; i++) sumSq += (arr[i] - mean) * (arr[i] - mean);
        return MathF.Sqrt(sumSq / arr.Length);
    }

    public void Dispose()
    {
        foreach (var feed in _feeds)
            feed.Dispose();
        _client.Dispose();
    }
}
