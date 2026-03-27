using System.Globalization;
using System.Text.Json;
using Seed.Market.Signals;

namespace Seed.Market.Data;

public sealed class BinanceFuturesFeed : IDataFeed
{
    public string Name => "BinanceFutures";
    public TimeSpan Interval => TimeSpan.FromSeconds(15);
    public bool IsHealthy { get; private set; } = true;
    public DateTimeOffset LastFetch { get; private set; }

    private float _prevOi;

    public async Task<FeedResult> FetchAsync(HttpClient client, CancellationToken ct = default)
    {
        try
        {
            var signals = new List<(int, float)>();
            const string baseUrl = "https://fapi.binance.com";

            var fundingTask = client.GetStringAsync($"{baseUrl}/fapi/v1/fundingRate?symbol=BTCUSDT&limit=1", ct);
            var oiTask = client.GetStringAsync($"{baseUrl}/fapi/v1/openInterest?symbol=BTCUSDT", ct);
            var lsTask = client.GetStringAsync(
                $"{baseUrl}/futures/data/globalLongShortAccountRatio?symbol=BTCUSDT&period=1h&limit=1", ct);
            var takerTask = client.GetStringAsync(
                $"{baseUrl}/futures/data/takerlongshortRatio?symbol=BTCUSDT&period=1h&limit=1", ct);
            var topTask = client.GetStringAsync(
                $"{baseUrl}/futures/data/topLongShortPositionRatio?symbol=BTCUSDT&period=1h&limit=1", ct);
            var ethFundingTask = client.GetStringAsync($"{baseUrl}/fapi/v1/fundingRate?symbol=ETHUSDT&limit=1", ct);
            var ethOiTask = client.GetStringAsync($"{baseUrl}/fapi/v1/openInterest?symbol=ETHUSDT", ct);
            var premiumTask = client.GetStringAsync($"{baseUrl}/fapi/v1/premiumIndex?symbol=BTCUSDT", ct);

            await Task.WhenAll(fundingTask, oiTask, lsTask, takerTask, topTask,
                ethFundingTask, ethOiTask, premiumTask);

            // BTC Funding Rate
            var fundArr = JsonSerializer.Deserialize<JsonElement>(fundingTask.Result);
            float funding = Pf(fundArr[0].GetProperty("fundingRate"));
            signals.Add((SignalIndex.FundingRate, funding));

            // BTC Open Interest
            var oiDoc = JsonSerializer.Deserialize<JsonElement>(oiTask.Result);
            float oi = Pf(oiDoc.GetProperty("openInterest"));
            float oiChange = _prevOi > 0 ? (oi - _prevOi) / _prevOi : 0f;
            signals.Add((SignalIndex.OpenInterest, oi));
            signals.Add((SignalIndex.OiChange1h, oiChange));
            _prevOi = oi;

            // Long/Short Ratio
            var lsArr = JsonSerializer.Deserialize<JsonElement>(lsTask.Result);
            float lsRatio = Pf(lsArr[0].GetProperty("longShortRatio"));
            signals.Add((SignalIndex.LongShortRatio, lsRatio));

            // Taker Buy/Sell
            var takerArr = JsonSerializer.Deserialize<JsonElement>(takerTask.Result);
            float takerRatio = Pf(takerArr[0].GetProperty("buySellRatio"));
            signals.Add((SignalIndex.TakerBuySellRatio, takerRatio));

            // Top Trader Long/Short
            var topArr = JsonSerializer.Deserialize<JsonElement>(topTask.Result);
            float topRatio = Pf(topArr[0].GetProperty("longShortRatio"));
            signals.Add((SignalIndex.TopTraderLongShort, topRatio));

            // Liquidations -- Binance doesn't expose aggregated liquidation data via REST;
            // placeholder 0 until WebSocket implementation in Phase 6
            signals.Add((SignalIndex.LiquidationLong1h, 0f));
            signals.Add((SignalIndex.LiquidationShort1h, 0f));

            // Futures Premium (mark - index spread)
            var premDoc = JsonSerializer.Deserialize<JsonElement>(premiumTask.Result);
            float markPrice = Pf(premDoc.GetProperty("markPrice"));
            float indexPrice = Pf(premDoc.GetProperty("indexPrice"));
            float premium = indexPrice > 0 ? (markPrice - indexPrice) / indexPrice : 0f;
            signals.Add((SignalIndex.FuturesPremium, premium));

            // ETH derivatives
            var ethFundArr = JsonSerializer.Deserialize<JsonElement>(ethFundingTask.Result);
            float ethFunding = Pf(ethFundArr[0].GetProperty("fundingRate"));
            signals.Add((SignalIndex.EthFundingRate, ethFunding));

            var ethOiDoc = JsonSerializer.Deserialize<JsonElement>(ethOiTask.Result);
            float ethOi = Pf(ethOiDoc.GetProperty("openInterest"));
            signals.Add((SignalIndex.EthOpenInterest, ethOi));

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

    private static float Pf(JsonElement el) =>
        float.Parse(el.GetString()!, CultureInfo.InvariantCulture);

    public void Dispose() { }
}
