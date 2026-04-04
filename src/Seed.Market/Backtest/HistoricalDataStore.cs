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
        {
            var cached = LoadCandlesFromCache(cacheFile);
            if (cached != null) return cached;
        }

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
    /// Technical indicators are pre-computed in O(n) across the full history,
    /// then assembled per-bar and normalized.
    /// Optional enrichment data fills additional signal slots (macro, on-chain, etc.).
    /// </summary>
    public static (SignalSnapshot[] snapshots, float[] prices, float[] rawVolumes, float[] rawFundingRates) CandlesToSignals(
        TechnicalIndicators.Candle[] candles,
        Dictionary<int, float[]>? enrichment = null)
    {
        int n = candles.Length;
        var normalizer = new SignalNormalizer();
        var snapshots = new SignalSnapshot[n];
        var prices = new float[n];
        var rawVolumes = new float[n];
        var rawFundingRates = new float[n];

        var closes = new float[n];
        var highs = new float[n];
        var lows = new float[n];
        var volumes = new float[n];

        for (int i = 0; i < n; i++)
        {
            closes[i] = candles[i].Close;
            highs[i] = candles[i].High;
            lows[i] = candles[i].Low;
            volumes[i] = candles[i].Volume;
            prices[i] = candles[i].Close;
            rawVolumes[i] = candles[i].Volume;
        }

        if (enrichment != null && enrichment.TryGetValue(SignalIndex.FundingRate, out var fundingArr))
        {
            for (int i = 0; i < n && i < fundingArr.Length; i++)
                rawFundingRates[i] = fundingArr[i];
        }

        var ema12 = TechnicalIndicators.ComputeEmaArray(closes, 12);
        var ema26 = TechnicalIndicators.ComputeEmaArray(closes, 26);
        var macdLine = new float[n];
        for (int i = 0; i < n; i++) macdLine[i] = ema12[i] - ema26[i];
        var macdSignalArr = TechnicalIndicators.ComputeEmaArray(macdLine, 9);
        var rsiArr = TechnicalIndicators.ComputeRsiArray(closes, 14);
        var atrArr = TechnicalIndicators.ComputeAtrArray(highs, lows, closes, 14);
        var bbArr = TechnicalIndicators.ComputeBollingerArray(closes, 20, 2f);
        var obvSlopeArr = TechnicalIndicators.ComputeObvSlopeArray(closes, volumes, 14);
        var vwapArr = TechnicalIndicators.ComputeVwapArray(candles, 24);

        var rollingVol = ComputeRollingVolatility(closes, 24);

        for (int i = 0; i < n; i++)
        {
            var raw = new float[SignalIndex.Count];
            var c = candles[i];

            raw[SignalIndex.BtcPrice] = c.Close;
            raw[SignalIndex.BtcReturn1h] = i > 0 && closes[i - 1] > 0
                ? (c.Close - closes[i - 1]) / closes[i - 1] : 0f;
            raw[SignalIndex.BtcVolume1h] = c.Volume;

            if (i >= 4)
                raw[SignalIndex.BtcReturn4h] = closes[i - 4] > 0
                    ? (c.Close - closes[i - 4]) / closes[i - 4] : 0f;
            if (i >= 24)
                raw[SignalIndex.BtcReturn24h] = closes[i - 24] > 0
                    ? (c.Close - closes[i - 24]) / closes[i - 24] : 0f;

            if (i >= 26)
            {
                raw[SignalIndex.Rsi14] = rsiArr[i];
                raw[SignalIndex.Ema12] = ema12[i];
                raw[SignalIndex.Ema26] = ema26[i];
                raw[SignalIndex.MacdLine] = macdLine[i];
                raw[SignalIndex.MacdSignal] = macdSignalArr[i];
                raw[SignalIndex.BollingerUpper] = bbArr[i].Upper;
                raw[SignalIndex.BollingerLower] = bbArr[i].Lower;
                raw[SignalIndex.BollingerWidth] = c.Close > 0
                    ? (bbArr[i].Upper - bbArr[i].Lower) / c.Close : 0f;
                raw[SignalIndex.Atr14] = atrArr[i];
                raw[SignalIndex.Vwap] = vwapArr[i];
                raw[SignalIndex.VwapDeviation] = vwapArr[i] > 0
                    ? (c.Close - vwapArr[i]) / vwapArr[i] : 0f;
                raw[SignalIndex.ObvSlope] = obvSlopeArr[i];
            }

            var timeSignals = TimeEncoding.Compute(c.Time);
            foreach (var (idx, val) in timeSignals)
                raw[idx] = val;

            if (enrichment != null)
            {
                foreach (var (slot, arr) in enrichment)
                {
                    if (i < arr.Length && arr[i] != 0f)
                        raw[slot] = arr[i];
                }
            }

            float vol = rollingVol[i];
            raw[SignalIndex.RegimeVolatility] = MathF.Min(vol / 0.05f, 1f);

            float momentum24h = raw[SignalIndex.BtcReturn24h];
            raw[SignalIndex.RegimeTrend] = Math.Clamp(momentum24h / 0.10f, -1f, 1f);

            float prevVol = i > 0 ? rollingVol[i - 1] : vol;
            raw[SignalIndex.RegimeChange] = Math.Clamp((vol - prevVol) / 0.02f, -1f, 1f);

            float vixChange = raw[SignalIndex.VixChange];
            float liqLong = raw[SignalIndex.LiquidationLong1h];
            float liqShort = raw[SignalIndex.LiquidationShort1h];
            float fundingAbs = MathF.Abs(raw[SignalIndex.FundingRate]);
            float volPct = raw[SignalIndex.RegimeVolatility];
            raw[SignalIndex.MarketStress] = Math.Clamp(
                MathF.Abs(vixChange) * 2f + (liqLong + liqShort) * 0.5f + fundingAbs * 10f + volPct * 0.5f,
                0f, 1f);

            snapshots[i] = normalizer.Normalize(raw, c.Time, i);
        }

        return (snapshots, prices, rawVolumes, rawFundingRates);
    }

    private static float[] ComputeRollingVolatility(float[] closes, int window)
    {
        int n = closes.Length;
        var vol = new float[n];
        for (int i = 1; i < n; i++)
        {
            int start = Math.Max(1, i - window + 1);
            float sumR = 0f, sumR2 = 0f;
            int count = 0;
            for (int j = start; j <= i; j++)
            {
                if (closes[j - 1] <= 0f) continue;
                float r = (closes[j] - closes[j - 1]) / closes[j - 1];
                sumR += r;
                sumR2 += r * r;
                count++;
            }
            if (count > 1)
            {
                float mean = sumR / count;
                float variance = sumR2 / count - mean * mean;
                vol[i] = variance > 0 ? MathF.Sqrt(variance) : 0f;
            }
        }
        return vol;
    }

    private static TechnicalIndicators.Candle[]? LoadCandlesFromCache(string path)
    {
        var result = File.ReadAllLines(path)
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
        if (result.Length == 0)
        {
            Console.WriteLine($"[CACHE] Deleting empty cache file: {Path.GetFileName(path)}");
            File.Delete(path);
            return null;
        }
        return result;
    }

    private static void SaveCandlesToCache(string path, TechnicalIndicators.Candle[] candles)
    {
        if (candles.Length == 0) return;
        var lines = candles.Select(c =>
            string.Create(CultureInfo.InvariantCulture,
                $"{c.Open},{c.High},{c.Low},{c.Close},{c.Volume},{c.Time.ToUnixTimeMilliseconds()}"));
        File.WriteAllLines(path, lines);
    }

    private static float Pf(JsonElement el) =>
        float.Parse(el.GetString()!, CultureInfo.InvariantCulture);
}
