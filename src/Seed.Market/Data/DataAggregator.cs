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

    public DateTimeOffset LastTickTime { get; private set; }

    private readonly Queue<float> _btcReturns = new();
    private readonly Queue<float> _ethReturns = new();
    private readonly Queue<float> _spxReturns = new();
    private readonly Queue<float> _btcPrices = new();
    private readonly Queue<float> _ethPrices = new();
    private const int DerivedWindow = 30;

    public DataAggregator(MarketConfig? config = null)
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.Add("User-Agent", "SeedMarket/1.0");

        _feeds =
        [
            new BinanceSpotFeed(),
            new BinanceFuturesFeed(),
            new SentimentFeed(),
            new OnChainFeed(),
            new MacroFeed(),
            new StablecoinFeed()
        ];

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
            }
        }

        SignalSnapshot snapshot;
        lock (_lock)
        {
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
        float vol = _rawSignals[SignalIndex.BtcVolume1h];
        if (price <= 0) return;

        if (_candleHistory.Count == 0 ||
            (now - _candleHistory[^1].Time).TotalMinutes >= 55)
        {
            _candleHistory.Add(new TechnicalIndicators.Candle(
                price, price, price, price, vol, now));

            while (_candleHistory.Count > 200)
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
                Volume = last.Volume + vol
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

    private void ComputeDerivedSignals()
    {
        float btcRet = _rawSignals[SignalIndex.BtcReturn1h];
        float ethRet = _rawSignals[SignalIndex.EthReturn1h];
        float spxRet = _rawSignals[SignalIndex.Sp500Return];
        float btcPrice = _rawSignals[SignalIndex.BtcPrice];
        float ethPrice = _rawSignals[SignalIndex.EthPrice];

        _btcReturns.Enqueue(btcRet); if (_btcReturns.Count > DerivedWindow) _btcReturns.Dequeue();
        _ethReturns.Enqueue(ethRet); if (_ethReturns.Count > DerivedWindow) _ethReturns.Dequeue();
        _spxReturns.Enqueue(spxRet); if (_spxReturns.Count > DerivedWindow) _spxReturns.Dequeue();
        _btcPrices.Enqueue(btcPrice); if (_btcPrices.Count > DerivedWindow) _btcPrices.Dequeue();
        _ethPrices.Enqueue(ethPrice); if (_ethPrices.Count > DerivedWindow) _ethPrices.Dequeue();

        if (_btcReturns.Count < 2) return;

        var btcArr = _btcReturns.ToArray();
        var ethArr = _ethReturns.ToArray();
        var spxArr = _spxReturns.ToArray();

        _rawSignals[SignalIndex.BtcSp500Correlation] = PearsonCorrelation(btcArr, spxArr);
        _rawSignals[SignalIndex.BtcEthCorrelation] = PearsonCorrelation(btcArr, ethArr);
        _rawSignals[SignalIndex.BtcVolatility] = StdDev(btcArr);
        _rawSignals[SignalIndex.EthVolatility] = StdDev(ethArr);
        _rawSignals[SignalIndex.VolatilityRatio] = _rawSignals[SignalIndex.EthVolatility] > 0
            ? _rawSignals[SignalIndex.BtcVolatility] / _rawSignals[SignalIndex.EthVolatility] : 1f;

        _rawSignals[SignalIndex.BtcEthSpread] = btcPrice > 0 && ethPrice > 0
            ? ethPrice / btcPrice : 0f;

        if (_btcPrices.Count >= 24)
        {
            var btcP = _btcPrices.ToArray();
            var ethP = _ethPrices.ToArray();
            int lookback = Math.Min(24, btcP.Length);
            float btcOld = btcP[btcP.Length - lookback];
            float ethOld = ethP[ethP.Length - lookback];
            _rawSignals[SignalIndex.BtcMomentum] = btcOld > 0 ? (btcPrice - btcOld) / btcOld : 0f;
            _rawSignals[SignalIndex.EthMomentum] = ethOld > 0 ? (ethPrice - ethOld) / ethOld : 0f;
        }
        _rawSignals[SignalIndex.MomentumDivergence] =
            _rawSignals[SignalIndex.BtcMomentum] - _rawSignals[SignalIndex.EthMomentum];
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
