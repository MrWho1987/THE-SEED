using System.Text.Json;
using Seed.Market.Signals;

namespace Seed.Market.Data;

public sealed class OnChainFeed : IDataFeed
{
    public string Name => "OnChain";
    public TimeSpan Interval => TimeSpan.FromHours(1);
    public bool IsHealthy { get; private set; } = true;
    public DateTimeOffset LastFetch { get; private set; }

    private float _prevHashRate;

    public async Task<FeedResult> FetchAsync(HttpClient client, CancellationToken ct = default)
    {
        try
        {
            var signals = new List<(int, float)>();
            const string baseUrl = "https://api.blockchain.info/charts";
            const string opts = "?timespan=30days&format=json&rollingAverage=8hours";

            var hashTask = client.GetStringAsync($"{baseUrl}/hash-rate{opts}", ct);
            var addrTask = client.GetStringAsync($"{baseUrl}/n-unique-addresses{opts}", ct);
            var txVolTask = client.GetStringAsync($"{baseUrl}/estimated-transaction-volume-usd{opts}", ct);
            var diffTask = client.GetStringAsync($"{baseUrl}/difficulty?timespan=60days&format=json", ct);
            var minerTask = client.GetStringAsync($"{baseUrl}/miners-revenue{opts}", ct);
            var mempoolTask = client.GetStringAsync($"{baseUrl}/mempool-size{opts}", ct);

            await Task.WhenAll(hashTask, addrTask, txVolTask, diffTask, minerTask, mempoolTask);

            float hash = GetLatestValue(hashTask.Result);
            float hashChange = _prevHashRate > 0 ? (hash - _prevHashRate) / _prevHashRate : 0f;
            _prevHashRate = hash;

            signals.Add((SignalIndex.HashRate, hash));
            signals.Add((SignalIndex.HashRateChange, hashChange));
            signals.Add((SignalIndex.ActiveAddresses, GetLatestValue(addrTask.Result)));
            signals.Add((SignalIndex.TransactionVolume, GetLatestValue(txVolTask.Result)));
            signals.Add((SignalIndex.MempoolSize, GetLatestValue(mempoolTask.Result)));
            signals.Add((SignalIndex.MinerRevenue, GetLatestValue(minerTask.Result)));
            signals.Add((SignalIndex.MiningDifficulty, GetLatestValue(diffTask.Result)));

            // Exchange net flow and NVT require premium APIs; placeholder with derived values
            float txVol = GetLatestValue(txVolTask.Result);
            float nvt = txVol > 0 ? GetLatestValue(hashTask.Result) / txVol : 0f;
            signals.Add((SignalIndex.NvtRatio, nvt));
            signals.Add((SignalIndex.ExchangeNetFlow, 0f));
            signals.Add((SignalIndex.SupplyOnExchanges, 0f));

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

    private static float GetLatestValue(string json)
    {
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var values = doc.GetProperty("values");
        int len = values.GetArrayLength();
        if (len == 0) return 0f;
        return (float)values[len - 1].GetProperty("y").GetDouble();
    }

    public void Dispose() { }
}
