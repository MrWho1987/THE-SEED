namespace Seed.Market.Data;

/// <summary>
/// A single data source that fills a subset of the 88-signal vector.
/// Each feed owns a contiguous range of signal indices.
/// </summary>
public interface IDataFeed : IDisposable
{
    string Name { get; }
    TimeSpan Interval { get; }
    bool IsHealthy { get; }
    DateTimeOffset LastFetch { get; }

    Task<FeedResult> FetchAsync(HttpClient client, CancellationToken ct = default);
}

public readonly record struct FeedResult(
    bool Success,
    (int Index, float Value)[] Signals,
    string? Error = null
);
