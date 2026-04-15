using System.Text.Json;
using Seed.Market.Signals;

namespace Seed.Market.Data;

/// <summary>
/// Fetches Fear & Greed Index from alternative.me (free, no auth).
///
/// V14 refactor: dropped News/Reddit fetching — the News/Reddit slots (25-29) were
/// repurposed for Deribit options signals (see DeribitFeed). SentimentMomentum at
/// slot 30 is now derived from the Fear & Greed Index only.
/// </summary>
public sealed class SentimentFeed : IDataFeed
{
    public string Name => "Sentiment";
    public TimeSpan Interval => TimeSpan.FromMinutes(5);
    public bool IsHealthy { get; private set; } = true;
    public DateTimeOffset LastFetch { get; private set; }

    private float _prevFearGreed;

    public async Task<FeedResult> FetchAsync(HttpClient client, CancellationToken ct = default)
    {
        try
        {
            var signals = new List<(int, float)>();

            var fgJson = await client.GetStringAsync(
                "https://api.alternative.me/fng/?limit=2&format=json", ct);

            // Fear & Greed
            var fgDoc = JsonSerializer.Deserialize<JsonElement>(fgJson);
            var fgData = fgDoc.GetProperty("data");
            float fgValue = float.Parse(fgData[0].GetProperty("value").GetString()!);
            float fgYesterday = fgData.GetArrayLength() > 1
                ? float.Parse(fgData[1].GetProperty("value").GetString()!)
                : fgValue;
            float fgChange = fgValue - fgYesterday;

            signals.Add((SignalIndex.FearGreedIndex, fgValue));
            signals.Add((SignalIndex.FearGreedChange, fgChange));

            float momentum = fgValue - _prevFearGreed;
            if (_prevFearGreed == 0) momentum = 0;
            _prevFearGreed = fgValue;
            signals.Add((SignalIndex.SentimentMomentum, momentum));

            IsHealthy = true;
            LastFetch = DateTimeOffset.UtcNow;
            return new FeedResult(true, signals.ToArray());
        }
        catch (Exception ex)
        {
            IsHealthy = false;
            return new FeedResult(false, [], ex.Message);
        }
    }

    public void Dispose() { }
}
