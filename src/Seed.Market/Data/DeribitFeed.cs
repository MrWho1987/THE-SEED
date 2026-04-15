using System.Text.Json;
using Seed.Market.Signals;

namespace Seed.Market.Data;

/// <summary>
/// Fetches BTC options-based sentiment signals from Deribit's public API (no auth required).
///
/// V14: Primary signal populated is DeribitIVPercentile via a rolling 30-day DVOL buffer.
/// Other Deribit slots (put/call ratio, skew, max pain) are exposed as fields for live use
/// but default to 0 in backtest (no free historical source).
/// </summary>
public sealed class DeribitFeed : IDataFeed
{
    private const string BaseUrl = "https://www.deribit.com/api/v2";
    private const int RollingBufferCapacity = 30 * 24;  // 30 days of hourly samples

    public string Name => "Deribit";
    public TimeSpan Interval => TimeSpan.FromMinutes(5);
    public bool IsHealthy { get; private set; }
    public DateTimeOffset LastFetch { get; private set; }

    private readonly Queue<float> _dvolBuffer = new();
    private readonly List<string> _warnings = new();

    public IReadOnlyList<string> Warnings => _warnings;

    public async Task<FeedResult> FetchAsync(HttpClient client, CancellationToken ct = default)
    {
        _warnings.Clear();
        var signals = new List<(int, float)>();
        bool anySuccess = false;

        // 1) Current DVOL (volatility index)
        try
        {
            var dvolJson = await client.GetStringAsync(
                $"{BaseUrl}/public/get_volatility_index_data" +
                $"?currency=BTC&start_timestamp={DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeMilliseconds()}" +
                $"&end_timestamp={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}&resolution=3600",
                ct);
            var doc = JsonSerializer.Deserialize<JsonElement>(dvolJson);
            if (doc.TryGetProperty("result", out var resultObj)
                && resultObj.TryGetProperty("data", out var arr)
                && arr.ValueKind == JsonValueKind.Array
                && arr.GetArrayLength() > 0)
            {
                // Latest DVOL close
                var latest = arr[arr.GetArrayLength() - 1];
                float dvol = (float)latest[4].GetDouble();

                // Maintain rolling buffer
                _dvolBuffer.Enqueue(dvol);
                while (_dvolBuffer.Count > RollingBufferCapacity)
                    _dvolBuffer.Dequeue();

                // Percentile rank of current DVOL within buffer
                float percentile = ComputePercentile(dvol);
                signals.Add((SignalIndex.DeribitIVPercentile, percentile));
                anySuccess = true;
            }
        }
        catch (Exception ex)
        {
            _warnings.Add($"DVOL fetch failed: {ex.Message}");
        }

        // 2) Put/Call ratio from book summary
        try
        {
            var bsJson = await client.GetStringAsync(
                $"{BaseUrl}/public/get_book_summary_by_currency?currency=BTC&kind=option", ct);
            var bsDoc = JsonSerializer.Deserialize<JsonElement>(bsJson);

            if (bsDoc.TryGetProperty("result", out var summaryArr)
                && summaryArr.ValueKind == JsonValueKind.Array)
            {
                float putVol = 0f, callVol = 0f;
                float putOi = 0f, callOi = 0f;
                foreach (var inst in summaryArr.EnumerateArray())
                {
                    if (!inst.TryGetProperty("instrument_name", out var nameProp)) continue;
                    string name = nameProp.GetString() ?? "";
                    bool isPut = name.EndsWith("-P", StringComparison.OrdinalIgnoreCase);
                    bool isCall = name.EndsWith("-C", StringComparison.OrdinalIgnoreCase);

                    float vol24h = inst.TryGetProperty("volume", out var v)
                        && v.ValueKind == JsonValueKind.Number ? (float)v.GetDouble() : 0f;
                    float oi = inst.TryGetProperty("open_interest", out var oiP)
                        && oiP.ValueKind == JsonValueKind.Number ? (float)oiP.GetDouble() : 0f;

                    if (isPut) { putVol += vol24h; putOi += oi; }
                    else if (isCall) { callVol += vol24h; callOi += oi; }
                }

                float pcRatio = (putVol + callVol) > 0f ? putVol / (putVol + callVol) : 0.5f;
                float pcOiRatio = (putOi + callOi) > 0f ? putOi / (putOi + callOi) : 0.5f;

                signals.Add((SignalIndex.DeribitPutCallRatio, pcRatio));
                signals.Add((SignalIndex.DeribitPutCallOI, pcOiRatio));
                anySuccess = true;
            }
        }
        catch (Exception ex)
        {
            _warnings.Add($"Book summary fetch failed: {ex.Message}");
        }

        IsHealthy = anySuccess;
        if (anySuccess) LastFetch = DateTimeOffset.UtcNow;

        if (_warnings.Count > 0)
            Console.Error.WriteLine($"[DeribitFeed] Warnings: {string.Join("; ", _warnings)}");

        return new FeedResult(anySuccess, signals.ToArray(),
            anySuccess ? null : string.Join("; ", _warnings));
    }

    private float ComputePercentile(float current)
    {
        if (_dvolBuffer.Count < 2) return 0.5f;
        int below = 0;
        foreach (var v in _dvolBuffer)
            if (v < current) below++;
        return (float)below / (_dvolBuffer.Count - 1);
    }

    public void Dispose() { }
}
