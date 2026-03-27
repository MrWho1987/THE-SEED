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

    public void Dispose()
    {
        foreach (var feed in _feeds)
            feed.Dispose();
        _client.Dispose();
    }
}
