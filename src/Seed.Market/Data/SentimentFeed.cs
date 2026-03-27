using System.Text.Json;
using System.Xml.Linq;
using Seed.Market.Indicators;
using Seed.Market.Signals;

namespace Seed.Market.Data;

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

            var fgTask = client.GetStringAsync(
                "https://api.alternative.me/fng/?limit=2&format=json", ct);
            var rssTask = FetchRssHeadlines(client, ct);
            var redditTask = FetchRedditSentiment(client, ct);

            await Task.WhenAll(fgTask, rssTask, redditTask);

            // Fear & Greed
            var fgDoc = JsonSerializer.Deserialize<JsonElement>(fgTask.Result);
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

            // News RSS
            var (newsSentiment, newsCount) = rssTask.Result;
            signals.Add((SignalIndex.NewsHeadlineSentiment, newsSentiment));
            signals.Add((SignalIndex.NewsVolume, newsCount));

            // Reddit
            var (redditSentiment, redditVolume, bullBear) = redditTask.Result;
            signals.Add((SignalIndex.RedditSentiment, redditSentiment));
            signals.Add((SignalIndex.RedditPostVolume, redditVolume));
            signals.Add((SignalIndex.SocialBullBearRatio, bullBear));

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

    private static async Task<(float sentiment, float count)> FetchRssHeadlines(
        HttpClient client, CancellationToken ct)
    {
        float totalSentiment = 0;
        int count = 0;

        string[] feeds =
        [
            "https://cointelegraph.com/rss",
            "https://www.coindesk.com/arc/outboundfeeds/rss/"
        ];

        foreach (var url in feeds)
        {
            try
            {
                var xml = await client.GetStringAsync(url, ct);
                var doc = XDocument.Parse(xml);
                var items = doc.Descendants("item").Take(10);
                foreach (var item in items)
                {
                    string title = item.Element("title")?.Value ?? "";
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    totalSentiment += VaderSentiment.Score(title);
                    count++;
                }
            }
            catch { /* individual feed failure is non-fatal */ }
        }

        float avg = count > 0 ? totalSentiment / count : 0f;
        return (avg, count);
    }

    private static async Task<(float sentiment, float volume, float bullBear)> FetchRedditSentiment(
        HttpClient client, CancellationToken ct)
    {
        float totalSentiment = 0;
        int count = 0;
        int bullish = 0, bearish = 0;

        string[] subs = ["cryptocurrency", "bitcoin"];

        foreach (var sub in subs)
        {
            try
            {
                var json = await client.GetStringAsync(
                    $"https://www.reddit.com/r/{sub}/hot.json?limit=25", ct);
                var doc = JsonSerializer.Deserialize<JsonElement>(json);
                var posts = doc.GetProperty("data").GetProperty("children");

                for (int i = 0; i < posts.GetArrayLength(); i++)
                {
                    var data = posts[i].GetProperty("data");
                    string title = data.GetProperty("title").GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    float score = VaderSentiment.Score(title);
                    totalSentiment += score;
                    count++;

                    if (score > 0.05f) bullish++;
                    else if (score < -0.05f) bearish++;
                }
            }
            catch { }
        }

        float avg = count > 0 ? totalSentiment / count : 0f;
        int total = bullish + bearish;
        float bbRatio = total > 0 ? (float)bullish / total : 0.5f;
        return (avg, count, bbRatio);
    }

    public void Dispose() { }
}
