using System.Globalization;
using System.Text.Json;
using Seed.Market.Indicators;
using Seed.Market.Signals;

namespace Seed.Market.Backtest;

/// <summary>
/// Downloads, caches, and serves historical candle data for backtesting.
/// Data is stored as JSONL files, one per symbol per date range.
/// </summary>
public sealed class HistoricalDataStore
{
    private readonly string _cacheDir;
    private readonly HttpClient _client;

    public HistoricalDataStore(string cacheDir = "data_cache")
    {
        _cacheDir = cacheDir;
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.Add("User-Agent", "SeedMarket/1.0");
        Directory.CreateDirectory(_cacheDir);
    }

    /// <summary>
    /// Fetch hourly BTC/USDT candles for the given date range.
    /// Uses cache when available, otherwise downloads from Binance.
    /// </summary>
    public async Task<TechnicalIndicators.Candle[]> FetchCandles(
        string symbol, DateTimeOffset start, DateTimeOffset end)
    {
        var cacheFile = Path.Combine(_cacheDir,
            $"{symbol}_{start:yyyyMMdd}_{end:yyyyMMdd}_1h.jsonl");

        if (File.Exists(cacheFile))
            return LoadCandlesFromCache(cacheFile);

        var candles = await DownloadCandles(symbol, start, end);
        SaveCandlesToCache(cacheFile, candles);
        return candles;
    }

    private async Task<TechnicalIndicators.Candle[]> DownloadCandles(
        string symbol, DateTimeOffset start, DateTimeOffset end)
    {
        var candles = new List<TechnicalIndicators.Candle>();
        long startMs = start.ToUnixTimeMilliseconds();
        long endMs = end.ToUnixTimeMilliseconds();

        while (startMs < endMs)
        {
            var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}" +
                      $"&interval=1h&startTime={startMs}&endTime={endMs}&limit=1000";

            var json = await _client.GetStringAsync(url);
            var arr = JsonSerializer.Deserialize<JsonElement>(json);
            int count = arr.GetArrayLength();
            if (count == 0) break;

            for (int i = 0; i < count; i++)
            {
                var k = arr[i];
                candles.Add(new TechnicalIndicators.Candle(
                    Open: Pf(k[1]),
                    High: Pf(k[2]),
                    Low: Pf(k[3]),
                    Close: Pf(k[4]),
                    Volume: Pf(k[5]),
                    Time: DateTimeOffset.FromUnixTimeMilliseconds(k[0].GetInt64())
                ));
            }

            startMs = arr[count - 1][6].GetInt64() + 1; // closeTime + 1
            await Task.Delay(100); // rate limit courtesy
        }

        return candles.ToArray();
    }

    /// <summary>
    /// Convert candle array to SignalSnapshot array.
    /// Technical indicators are computed from the candle history.
    /// Only fills price/volume and technical indicator slots.
    /// </summary>
    public static (SignalSnapshot[] snapshots, float[] prices) CandlesToSignals(
        TechnicalIndicators.Candle[] candles)
    {
        var normalizer = new SignalNormalizer();
        var snapshots = new SignalSnapshot[candles.Length];
        var prices = new float[candles.Length];

        float prevClose = 0;

        for (int i = 0; i < candles.Length; i++)
        {
            var raw = new float[SignalIndex.Count];
            var c = candles[i];
            prices[i] = c.Close;

            raw[SignalIndex.BtcPrice] = c.Close;
            raw[SignalIndex.BtcReturn1h] = prevClose > 0 ? (c.Close - prevClose) / prevClose : 0f;
            raw[SignalIndex.BtcVolume1h] = c.Volume;

            if (i >= 4)
                raw[SignalIndex.BtcReturn4h] = candles[i - 4].Close > 0
                    ? (c.Close - candles[i - 4].Close) / candles[i - 4].Close : 0f;
            if (i >= 24)
                raw[SignalIndex.BtcReturn24h] = candles[i - 24].Close > 0
                    ? (c.Close - candles[i - 24].Close) / candles[i - 24].Close : 0f;

            // Technical indicators from candle history
            if (i >= 26)
            {
                var slice = candles.AsSpan(0, i + 1);
                var techSignals = TechnicalIndicators.Compute(slice);
                foreach (var (idx, val) in techSignals)
                    raw[idx] = val;
            }

            // Time encoding
            var timeSignals = TimeEncoding.Compute(c.Time);
            foreach (var (idx, val) in timeSignals)
                raw[idx] = val;

            snapshots[i] = normalizer.Normalize(raw, c.Time, i);
            prevClose = c.Close;
        }

        return (snapshots, prices);
    }

    private static TechnicalIndicators.Candle[] LoadCandlesFromCache(string path)
    {
        var lines = File.ReadAllLines(path);
        return lines
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l =>
            {
                var parts = l.Split(',');
                return new TechnicalIndicators.Candle(
                    float.Parse(parts[0], CultureInfo.InvariantCulture),
                    float.Parse(parts[1], CultureInfo.InvariantCulture),
                    float.Parse(parts[2], CultureInfo.InvariantCulture),
                    float.Parse(parts[3], CultureInfo.InvariantCulture),
                    float.Parse(parts[4], CultureInfo.InvariantCulture),
                    DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(parts[5]))
                );
            }).ToArray();
    }

    private static void SaveCandlesToCache(string path, TechnicalIndicators.Candle[] candles)
    {
        var lines = candles.Select(c =>
            string.Create(CultureInfo.InvariantCulture,
                $"{c.Open},{c.High},{c.Low},{c.Close},{c.Volume},{c.Time.ToUnixTimeMilliseconds()}"));
        File.WriteAllLines(path, lines);
    }

    private static float Pf(JsonElement el) =>
        float.Parse(el.GetString()!, CultureInfo.InvariantCulture);
}
