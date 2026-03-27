using System.Text.Json;
using Seed.Market.Signals;

namespace Seed.Market.Data;

public sealed class StablecoinFeed : IDataFeed
{
    public string Name => "Stablecoin";
    public TimeSpan Interval => TimeSpan.FromHours(1);
    public bool IsHealthy { get; private set; } = true;
    public DateTimeOffset LastFetch { get; private set; }

    private float _prevUsdtMcap;
    private float _prevUsdcMcap;

    public async Task<FeedResult> FetchAsync(HttpClient client, CancellationToken ct = default)
    {
        try
        {
            var signals = new List<(int, float)>();

            var globalTask = client.GetStringAsync(
                "https://api.coingecko.com/api/v3/global", ct);
            var stableTask = client.GetStringAsync(
                "https://api.coingecko.com/api/v3/simple/price?ids=tether,usd-coin&vs_currencies=usd&include_market_cap=true", ct);

            await Task.WhenAll(globalTask, stableTask);

            // Global data
            var globalDoc = JsonSerializer.Deserialize<JsonElement>(globalTask.Result);
            var data = globalDoc.GetProperty("data");
            float btcDom = (float)data.GetProperty("market_cap_percentage").GetProperty("btc").GetDouble();
            float totalMcap = (float)data.GetProperty("total_market_cap").GetProperty("usd").GetDouble();

            signals.Add((SignalIndex.BtcDominance, btcDom));
            signals.Add((SignalIndex.TotalMarketCap, totalMcap));

            // Stablecoin market caps
            var stableDoc = JsonSerializer.Deserialize<JsonElement>(stableTask.Result);
            float usdtMcap = (float)stableDoc.GetProperty("tether").GetProperty("usd_market_cap").GetDouble();
            float usdcMcap = (float)stableDoc.GetProperty("usd-coin").GetProperty("usd_market_cap").GetDouble();

            float stableFlow = 0f;
            if (_prevUsdtMcap > 0 && _prevUsdcMcap > 0)
            {
                float prevTotal = _prevUsdtMcap + _prevUsdcMcap;
                float currTotal = usdtMcap + usdcMcap;
                stableFlow = prevTotal > 0 ? (currTotal - prevTotal) / prevTotal : 0f;
            }

            signals.Add((SignalIndex.UsdtMarketCap, usdtMcap));
            signals.Add((SignalIndex.UsdcMarketCap, usdcMcap));
            signals.Add((SignalIndex.StablecoinFlowDelta, stableFlow));

            // Altseason index: ETH market cap % relative to BTC
            float ethDom = 0f;
            if (data.GetProperty("market_cap_percentage").TryGetProperty("eth", out var ethProp))
                ethDom = (float)ethProp.GetDouble();
            float altseason = btcDom > 0 ? ethDom / btcDom : 0f;
            signals.Add((SignalIndex.AltseasonIndex, altseason));

            _prevUsdtMcap = usdtMcap;
            _prevUsdcMcap = usdcMcap;

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
