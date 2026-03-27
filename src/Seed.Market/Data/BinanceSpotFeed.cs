using System.Globalization;
using System.Text.Json;
using Seed.Market.Signals;

namespace Seed.Market.Data;

public sealed class BinanceSpotFeed : IDataFeed
{
    public string Name => "BinanceSpot";
    public TimeSpan Interval => TimeSpan.FromSeconds(5);
    public bool IsHealthy { get; private set; } = true;
    public DateTimeOffset LastFetch { get; private set; }

    private float _prevBtcClose;
    private float _prevEthClose;
    private float _btcClose4hAgo;
    private float _btcClose24hAgo;
    private readonly Queue<float> _btcVolumes = new();
    private const int VolWindowSize = 24;

    public async Task<FeedResult> FetchAsync(HttpClient client, CancellationToken ct = default)
    {
        try
        {
            var signals = new List<(int, float)>();

            var btcKlineTask = client.GetStringAsync(
                "https://api.binance.com/api/v3/klines?symbol=BTCUSDT&interval=1h&limit=25", ct);
            var ethKlineTask = client.GetStringAsync(
                "https://api.binance.com/api/v3/klines?symbol=ETHUSDT&interval=1h&limit=2", ct);
            var depthTask = client.GetStringAsync(
                "https://api.binance.com/api/v3/depth?symbol=BTCUSDT&limit=20", ct);

            await Task.WhenAll(btcKlineTask, ethKlineTask, depthTask);

            // BTC klines
            var btcArr = JsonSerializer.Deserialize<JsonElement>(btcKlineTask.Result);
            int btcLen = btcArr.GetArrayLength();
            var latest = btcArr[btcLen - 1];
            float btcClose = ParseFloat(latest[4]);
            float btcVol = ParseFloat(latest[5]);

            float btcClose4h = btcLen >= 5 ? ParseFloat(btcArr[btcLen - 5][4]) : btcClose;
            float btcClose24h = btcLen >= 25 ? ParseFloat(btcArr[btcLen - 25][4]) : btcClose;

            float ret1h = _prevBtcClose > 0 ? (btcClose - _prevBtcClose) / _prevBtcClose : 0f;
            float ret4h = btcClose4h > 0 ? (btcClose - btcClose4h) / btcClose4h : 0f;
            float ret24h = btcClose24h > 0 ? (btcClose - btcClose24h) / btcClose24h : 0f;

            _btcVolumes.Enqueue(btcVol);
            while (_btcVolumes.Count > VolWindowSize) _btcVolumes.Dequeue();
            float avgVol = _btcVolumes.Count > 0 ? _btcVolumes.Average() : btcVol;
            float volRatio = avgVol > 0 ? btcVol / avgVol : 1f;

            signals.Add((SignalIndex.BtcPrice, btcClose));
            signals.Add((SignalIndex.BtcReturn1h, ret1h));
            signals.Add((SignalIndex.BtcReturn4h, ret4h));
            signals.Add((SignalIndex.BtcReturn24h, ret24h));
            signals.Add((SignalIndex.BtcVolume1h, btcVol));
            signals.Add((SignalIndex.BtcVolumeRatio, volRatio));

            _prevBtcClose = btcClose;
            _btcClose4hAgo = btcClose4h;
            _btcClose24hAgo = btcClose24h;

            // ETH klines
            var ethArr = JsonSerializer.Deserialize<JsonElement>(ethKlineTask.Result);
            int ethLen = ethArr.GetArrayLength();
            var ethLatest = ethArr[ethLen - 1];
            float ethClose = ParseFloat(ethLatest[4]);
            float ethVol = ParseFloat(ethLatest[5]);
            float ethRet1h = _prevEthClose > 0 ? (ethClose - _prevEthClose) / _prevEthClose : 0f;
            float ethBtcRatio = btcClose > 0 ? ethClose / btcClose : 0f;

            signals.Add((SignalIndex.EthPrice, ethClose));
            signals.Add((SignalIndex.EthReturn1h, ethRet1h));
            signals.Add((SignalIndex.EthBtcRatio, ethBtcRatio));
            signals.Add((SignalIndex.EthVolume1h, ethVol));

            _prevEthClose = ethClose;

            // Order book depth
            var depth = JsonSerializer.Deserialize<JsonElement>(depthTask.Result);
            var bids = depth.GetProperty("bids");
            var asks = depth.GetProperty("asks");
            float bestBid = ParseFloat(bids[0][0]);
            float bestAsk = ParseFloat(asks[0][0]);
            float spread = bestAsk > 0 ? (bestAsk - bestBid) / bestAsk : 0f;

            float bidVol = 0f, askVol = 0f;
            for (int i = 0; i < bids.GetArrayLength(); i++) bidVol += ParseFloat(bids[i][1]);
            for (int i = 0; i < asks.GetArrayLength(); i++) askVol += ParseFloat(asks[i][1]);
            float totalVol = bidVol + askVol;
            float imbalance = totalVol > 0 ? (bidVol - askVol) / totalVol : 0f;

            signals.Add((SignalIndex.BtcBidAskSpread, spread));
            signals.Add((SignalIndex.BtcOrderImbalance, imbalance));

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

    private static float ParseFloat(JsonElement el) =>
        float.Parse(el.GetString()!, CultureInfo.InvariantCulture);

    public void Dispose() { }
}
