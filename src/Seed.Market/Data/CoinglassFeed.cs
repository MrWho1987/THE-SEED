using System.Globalization;
using System.Text.Json;
using Seed.Market.Signals;

namespace Seed.Market.Data;

/// <summary>
/// Fetches liquidation data, exchange balance, and exchange net flow from Coinglass API.
/// Requires a paid API key (Hobbyist+ plan).
/// </summary>
public sealed class CoinglassFeed : IDataFeed
{
    private const string BaseUrl = "https://open-api-v4.coinglass.com";
    private const float LiquidationScaleUsd = 10_000_000f; // 10M USD = signal 1.0
    private const float NetFlowScaleBtc = 5_000f; // 5K BTC daily flow = signal 1.0
    private const float CirculatingSupply = 20_000_000f; // ~20M BTC

    public string Name => "Coinglass";
    public TimeSpan Interval => TimeSpan.FromHours(4); // Hobbyist minimum resolution
    public bool IsHealthy { get; private set; }
    public DateTimeOffset LastFetch { get; private set; }

    private readonly string _apiKey;
    private float _prevExchangeBalance;
    private readonly List<string> _warnings = new();

    public IReadOnlyList<string> Warnings => _warnings;

    public CoinglassFeed(string apiKey)
    {
        _apiKey = apiKey;
    }

    public async Task<FeedResult> FetchAsync(HttpClient client, CancellationToken ct = default)
    {
        var signals = new List<(int, float)>();
        _warnings.Clear();
        bool anySuccess = false;

        try
        {
            // Liquidation data (4h window)
            try
            {
                var liqJson = await FetchWithKey(client,
                    $"{BaseUrl}/api/futures/liquidation/exchange-list?symbol=BTC&range=4h", ct);
                var liqDoc = JsonSerializer.Deserialize<JsonElement>(liqJson);

                if (liqDoc.TryGetProperty("data", out var liqData) && liqData.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in liqData.EnumerateArray())
                    {
                        if (entry.GetProperty("exchange").GetString() == "All")
                        {
                            float longLiq = (float)entry.GetProperty("longLiquidation_usd").GetDouble();
                            float shortLiq = (float)entry.GetProperty("shortLiquidation_usd").GetDouble();

                            signals.Add((SignalIndex.LiquidationLong1h,
                                Math.Clamp(longLiq / LiquidationScaleUsd, 0f, 1f)));
                            signals.Add((SignalIndex.LiquidationShort1h,
                                Math.Clamp(shortLiq / LiquidationScaleUsd, 0f, 1f)));
                            anySuccess = true;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _warnings.Add($"Liquidation fetch failed: {ex.Message}");
            }

            // Exchange balance (supply on exchanges) and net flow
            try
            {
                var balJson = await FetchWithKey(client,
                    $"{BaseUrl}/api/exchange/balance/chart?symbol=BTC&range=1d", ct);
                var balDoc = JsonSerializer.Deserialize<JsonElement>(balJson);

                if (balDoc.TryGetProperty("data", out var balData))
                {
                    float totalBalance = ComputeLatestTotalBalance(balData);

                    if (totalBalance > 0)
                    {
                        // Supply on exchanges as fraction of circulating supply
                        signals.Add((SignalIndex.SupplyOnExchanges,
                            Math.Clamp(totalBalance / CirculatingSupply, 0f, 1f)));

                        // Net flow = change in exchange balance (positive = inflow = bearish)
                        if (_prevExchangeBalance > 0)
                        {
                            float netFlow = totalBalance - _prevExchangeBalance;
                            signals.Add((SignalIndex.ExchangeNetFlow,
                                Math.Clamp(netFlow / NetFlowScaleBtc, -1f, 1f)));
                        }
                        _prevExchangeBalance = totalBalance;
                        anySuccess = true;
                    }
                }
            }
            catch (Exception ex)
            {
                _warnings.Add($"Exchange balance fetch failed: {ex.Message}");
            }

            IsHealthy = anySuccess;
            if (anySuccess) LastFetch = DateTimeOffset.UtcNow;

            if (_warnings.Count > 0)
                Console.Error.WriteLine($"[CoinglassFeed] Warnings: {string.Join("; ", _warnings)}");

            return new FeedResult(anySuccess, signals.ToArray(),
                anySuccess ? null : string.Join("; ", _warnings));
        }
        catch (Exception ex)
        {
            IsHealthy = false;
            _warnings.Add($"CoinglassFeed failed: {ex.Message}");
            Console.Error.WriteLine($"[CoinglassFeed] ERROR: {ex.Message}");
            return new FeedResult(false, [], ex.Message);
        }
    }

    private async Task<string> FetchWithKey(HttpClient client, string url, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("CG-API-KEY", _apiKey);
        var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static float ComputeLatestTotalBalance(JsonElement balData)
    {
        // Real Coinglass v4 response format for /api/exchange/balance/chart:
        //   { "code":"0", "msg":"success", "data": [ { "time_list":[...],
        //       "price_list":[...], "data_map": { "binance":[...], "okx":[...], ... } } ] }
        // "data" is an ARRAY containing a single object. The exchange balances live under
        // "data_map" (exchange name → array of balances aligned with time_list).
        // "price_list" at the top of the object is the BTC price series, not balance.

        JsonElement root;
        if (balData.ValueKind == JsonValueKind.Array)
        {
            if (balData.GetArrayLength() == 0) return 0f;
            root = balData[0];
        }
        else if (balData.ValueKind == JsonValueKind.Object)
        {
            // Legacy/compat: some tiers or older endpoints may return data as an object directly.
            root = balData;
        }
        else
        {
            return 0f;
        }

        if (root.ValueKind != JsonValueKind.Object) return 0f;
        if (!root.TryGetProperty("time_list", out var timeList)) return 0f;
        if (timeList.ValueKind != JsonValueKind.Array || timeList.GetArrayLength() == 0) return 0f;

        // Prefer data_map; fall back to scanning the object (some older format variants).
        JsonElement exchangeMap;
        if (root.TryGetProperty("data_map", out var dataMap) && dataMap.ValueKind == JsonValueKind.Object)
        {
            exchangeMap = dataMap;
        }
        else
        {
            // Fallback: root itself as a map, skipping metadata fields.
            exchangeMap = root;
        }

        float total = 0f;
        foreach (var prop in exchangeMap.EnumerateObject())
        {
            // Skip known non-exchange fields in fallback mode
            if (prop.Name is "time_list" or "price_list" or "data_map") continue;

            var exchangeData = prop.Value;
            // Each exchange's value can be:
            //   (a) an Array of balances aligned with time_list  (data_map format)
            //   (b) an Object with "price_list": [...] inside  (legacy format)
            JsonElement balanceArray;
            if (exchangeData.ValueKind == JsonValueKind.Array)
            {
                balanceArray = exchangeData;
            }
            else if (exchangeData.ValueKind == JsonValueKind.Object
                     && exchangeData.TryGetProperty("price_list", out var nested)
                     && nested.ValueKind == JsonValueKind.Array)
            {
                balanceArray = nested;
            }
            else
            {
                continue;
            }

            int len = balanceArray.GetArrayLength();
            if (len == 0) continue;

            // Walk backward from the latest entry, skipping nulls (some exchanges have sparse data)
            for (int i = len - 1; i >= 0; i--)
            {
                var v = balanceArray[i];
                if (v.ValueKind == JsonValueKind.Number)
                {
                    total += (float)v.GetDouble();
                    break;
                }
                if (v.ValueKind != JsonValueKind.Null) break;
            }
        }

        return total;
    }

    public void Dispose() { }
}
