using System.Text.Json;
using Seed.Market.Signals;

namespace Seed.Market.Data;

public sealed class MacroFeed : IDataFeed
{
    public string Name => "Macro";
    public TimeSpan Interval => TimeSpan.FromHours(1);
    public bool IsHealthy { get; private set; } = true;
    public DateTimeOffset LastFetch { get; private set; }

    private float _prevSp500;
    private float _prevVix;
    private float _prevDxy;
    private float _prevGold;
    private float _prevTreasury;

    public async Task<FeedResult> FetchAsync(HttpClient client, CancellationToken ct = default)
    {
        try
        {
            var signals = new List<(int, float)>();

            (string sym, string name)[] symbols =
            [
                ("^GSPC", "SP500"),
                ("^VIX", "VIX"),
                ("DX-Y.NYB", "DXY"),
                ("GC=F", "Gold"),
                ("^TNX", "Treasury10Y")
            ];

            var tasks = symbols.Select(s =>
                client.GetStringAsync(
                    $"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(s.sym)}?range=5d&interval=1d",
                    ct)).ToArray();

            await Task.WhenAll(tasks);

            float sp500 = ExtractClose(tasks[0].Result);
            float vix = ExtractClose(tasks[1].Result);
            float dxy = ExtractClose(tasks[2].Result);
            float gold = ExtractClose(tasks[3].Result);
            float treasury = ExtractClose(tasks[4].Result);

            float sp500Ret = _prevSp500 > 0 ? (sp500 - _prevSp500) / _prevSp500 : 0f;
            float vixChg = vix - _prevVix;
            float dxyChg = _prevDxy > 0 ? (dxy - _prevDxy) / _prevDxy : 0f;
            float goldRet = _prevGold > 0 ? (gold - _prevGold) / _prevGold : 0f;
            float treasuryChg = treasury - _prevTreasury;

            signals.Add((SignalIndex.Sp500Return, sp500Ret));
            signals.Add((SignalIndex.Vix, vix));
            signals.Add((SignalIndex.VixChange, vixChg));
            signals.Add((SignalIndex.DxyIndex, dxy));
            signals.Add((SignalIndex.DxyChange, dxyChg));
            signals.Add((SignalIndex.GoldPrice, gold));
            signals.Add((SignalIndex.GoldReturn, goldRet));
            signals.Add((SignalIndex.Treasury10Y, treasury));
            signals.Add((SignalIndex.TreasuryChange, treasuryChg));

            // BTC-S&P500 correlation placeholder (needs rolling window, computed in TechnicalIndicators)
            signals.Add((SignalIndex.BtcSp500Correlation, 0f));

            _prevSp500 = sp500;
            _prevVix = vix;
            _prevDxy = dxy;
            _prevGold = gold;
            _prevTreasury = treasury;

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

    private static float ExtractClose(string json)
    {
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var closes = doc.GetProperty("chart").GetProperty("result")[0]
            .GetProperty("indicators").GetProperty("quote")[0]
            .GetProperty("close");

        for (int i = closes.GetArrayLength() - 1; i >= 0; i--)
        {
            if (closes[i].ValueKind != JsonValueKind.Null)
                return (float)closes[i].GetDouble();
        }
        return 0f;
    }

    public void Dispose() { }
}
