using System.Globalization;
using System.Text.Json;
using Seed.Market.Indicators;
using Seed.Market.Signals;

namespace Seed.Market.Backtest;

/// <summary>
/// Downloads and caches supplemental historical data from free APIs,
/// then aligns it to hourly candle timestamps for backtest enrichment.
/// Fills ~42 additional signal slots beyond what BTC candles alone provide.
/// </summary>
public sealed class HistoricalSignalEnricher
{
    private readonly string _cacheDir;
    private readonly HttpClient _client;
    private readonly string? _coinGeckoApiKey;

    public HistoricalSignalEnricher(string cacheDir, string? coinGeckoApiKey = null)
    {
        _cacheDir = cacheDir;
        _coinGeckoApiKey = coinGeckoApiKey;
        Directory.CreateDirectory(_cacheDir);
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.Add("User-Agent", "SeedMarket/1.0");
    }

    /// <summary>
    /// Download all supplemental data and return per-slot arrays aligned to BTC candle timestamps.
    /// Returns a dictionary of signal-index -> float[n] where n = btcCandles.Length.
    /// </summary>
    public async Task<Dictionary<int, float[]>> EnrichAsync(
        TechnicalIndicators.Candle[] btcCandles, DateTimeOffset start, DateTimeOffset end)
    {
        int n = btcCandles.Length;
        var timestamps = btcCandles.Select(c => c.Time).ToArray();
        var result = new Dictionary<int, float[]>();

        var btcCloses = btcCandles.Select(c => c.Close).ToArray();

        // Phase 1: ETH candles + multi-asset derived
        try
        {
            Console.WriteLine("[ENRICH] Downloading ETH candles...");
            var ethCandles = await DownloadBinanceCandles("ETHUSDT", start, end);
            var aligned = AlignCandlesToTimestamps(ethCandles, timestamps);
            AddEthSignals(result, btcCandles, aligned, n);
            AddMultiAssetSignals(result, btcCloses, aligned, n);
            Console.WriteLine($"[ENRICH] ETH + multi-asset: {CountSlots(result)} slots");
        }
        catch (Exception ex) { Console.WriteLine($"[ENRICH] ETH failed: {ex.Message}"); }

        // BTC volume ratio (slot 5) - computed from BTC candles alone
        AddBtcVolumeRatio(result, btcCandles, n);

        // Phase 2: Macro (Yahoo Finance)
        try
        {
            Console.WriteLine("[ENRICH] Downloading macro data...");
            await AddMacroSignals(result, timestamps, btcCloses, n);
            Console.WriteLine($"[ENRICH] +Macro: {CountSlots(result)} slots");
        }
        catch (Exception ex) { Console.WriteLine($"[ENRICH] Macro failed: {ex.Message}"); }

        // Phase 3: On-chain (blockchain.info)
        try
        {
            Console.WriteLine("[ENRICH] Downloading on-chain data...");
            await AddOnChainSignals(result, timestamps, n);
            Console.WriteLine($"[ENRICH] +On-chain: {CountSlots(result)} slots");
        }
        catch (Exception ex) { Console.WriteLine($"[ENRICH] On-chain failed: {ex.Message}"); }

        // Phase 4: Fear & Greed (alternative.me)
        try
        {
            Console.WriteLine("[ENRICH] Downloading Fear & Greed...");
            await AddFearGreedSignals(result, timestamps, n);
            Console.WriteLine($"[ENRICH] +Fear/Greed: {CountSlots(result)} slots");
        }
        catch (Exception ex) { Console.WriteLine($"[ENRICH] Fear/Greed failed: {ex.Message}"); }

        // Phase 5: Stablecoin (CoinGecko)
        try
        {
            Console.WriteLine("[ENRICH] Downloading stablecoin/market data...");
            await AddStablecoinSignals(result, timestamps, n);
            Console.WriteLine($"[ENRICH] +Stablecoin: {CountSlots(result)} slots");
        }
        catch (Exception ex) { Console.WriteLine($"[ENRICH] Stablecoin failed: {ex.Message}"); }

        // Phase 6: Funding rates (Binance Futures)
        try
        {
            Console.WriteLine("[ENRICH] Downloading funding rates...");
            await AddFundingRateSignals(result, timestamps, start, end, n);
            Console.WriteLine($"[ENRICH] +Funding: {CountSlots(result)} slots");
        }
        catch (Exception ex) { Console.WriteLine($"[ENRICH] Funding failed: {ex.Message}"); }

        // Phase 7: Derivatives (OI, L/S ratio, taker ratio)
        try
        {
            Console.WriteLine("[ENRICH] Downloading derivatives data...");
            await AddDerivativesSignals(result, timestamps, start, end, n);
            Console.WriteLine($"[ENRICH] +Derivatives: {CountSlots(result)} slots");
        }
        catch (Exception ex) { Console.WriteLine($"[ENRICH] Derivatives failed: {ex.Message}"); }

        Console.WriteLine($"[ENRICH] Complete: {CountSlots(result)} enrichment slots populated");
        return result;
    }

    private static int CountSlots(Dictionary<int, float[]> d) => d.Count;

    // ── Phase 1: ETH + Multi-Asset ─────────────────────────────────────

    private static void AddEthSignals(
        Dictionary<int, float[]> result,
        TechnicalIndicators.Candle[] btc,
        TechnicalIndicators.Candle[] eth, int n)
    {
        var ethPrice = new float[n];
        var ethReturn = new float[n];
        var ethBtcRatio = new float[n];
        var ethVolume = new float[n];

        for (int i = 0; i < n; i++)
        {
            ethPrice[i] = eth[i].Close;
            ethReturn[i] = i > 0 && eth[i - 1].Close > 0
                ? (eth[i].Close - eth[i - 1].Close) / eth[i - 1].Close : 0f;
            ethBtcRatio[i] = btc[i].Close > 0 ? eth[i].Close / btc[i].Close : 0f;
            ethVolume[i] = eth[i].Volume;
        }

        result[SignalIndex.EthPrice] = ethPrice;
        result[SignalIndex.EthReturn1h] = ethReturn;
        result[SignalIndex.EthBtcRatio] = ethBtcRatio;
        result[SignalIndex.EthVolume1h] = ethVolume;
    }

    private static void AddMultiAssetSignals(
        Dictionary<int, float[]> result,
        float[] btcCloses, TechnicalIndicators.Candle[] eth, int n)
    {
        var ethCloses = new float[n];
        for (int i = 0; i < n; i++) ethCloses[i] = eth[i].Close;

        var btcReturns = ComputeReturns(btcCloses);
        var ethReturns = ComputeReturns(ethCloses);

        var spread = new float[n];
        var corr = new float[n];
        var btcVol = new float[n];
        var ethVol = new float[n];
        var volRatio = new float[n];
        var btcMom = new float[n];
        var ethMom = new float[n];
        var momDiv = new float[n];

        const int window = 24;
        for (int i = 0; i < n; i++)
        {
            spread[i] = btcCloses[i] > 0 && ethCloses[i] > 0
                ? ethCloses[i] / btcCloses[i] : 0f;

            if (i >= window)
            {
                corr[i] = RollingCorrelation(btcReturns, ethReturns, i, window);
                btcVol[i] = RollingStdDev(btcReturns, i, window);
                ethVol[i] = RollingStdDev(ethReturns, i, window);
                volRatio[i] = ethVol[i] > 0 ? btcVol[i] / ethVol[i] : 1f;
            }

            btcMom[i] = i >= 12 && btcCloses[i - 12] > 0
                ? (btcCloses[i] - btcCloses[i - 12]) / btcCloses[i - 12] : 0f;
            ethMom[i] = i >= 12 && ethCloses[i - 12] > 0
                ? (ethCloses[i] - ethCloses[i - 12]) / ethCloses[i - 12] : 0f;
            momDiv[i] = btcMom[i] - ethMom[i];
        }

        result[SignalIndex.BtcEthSpread] = spread;
        result[SignalIndex.BtcEthCorrelation] = corr;
        result[SignalIndex.BtcVolatility] = btcVol;
        result[SignalIndex.EthVolatility] = ethVol;
        result[SignalIndex.VolatilityRatio] = volRatio;
        result[SignalIndex.BtcMomentum] = btcMom;
        result[SignalIndex.EthMomentum] = ethMom;
        result[SignalIndex.MomentumDivergence] = momDiv;
    }

    private static void AddBtcVolumeRatio(
        Dictionary<int, float[]> result, TechnicalIndicators.Candle[] btc, int n)
    {
        var ratio = new float[n];
        float sum = 0;
        var window = new Queue<float>();
        for (int i = 0; i < n; i++)
        {
            float vol = btc[i].Volume;
            window.Enqueue(vol);
            sum += vol;
            while (window.Count > 24) sum -= window.Dequeue();
            float avg = sum / window.Count;
            ratio[i] = avg > 0 ? vol / avg : 1f;
        }
        result[SignalIndex.BtcVolumeRatio] = ratio;
    }

    // ── Phase 2: Macro (Yahoo Finance) ──────────────────────────────────

    private async Task AddMacroSignals(
        Dictionary<int, float[]> result, DateTimeOffset[] ts, float[] btcCloses, int n)
    {
        (string sym, string name)[] symbols =
        [
            ("^GSPC", "sp500"), ("^VIX", "vix"), ("DX-Y.NYB", "dxy"),
            ("GC=F", "gold"), ("^TNX", "treasury10y")
        ];

        var series = new Dictionary<string, float[]>();
        foreach (var (sym, name) in symbols)
        {
            var daily = await FetchYahooDaily(sym, name);
            series[name] = ForwardFillToHourly(daily, ts, n, publicationDelayHours: 16);
            await Task.Delay(200);
        }

        var sp = series["sp500"];
        var vix = series["vix"];
        var dxy = series["dxy"];
        var gold = series["gold"];
        var treas = series["treasury10y"];

        result[SignalIndex.Sp500Return] = ComputeReturns(sp);
        result[SignalIndex.Vix] = vix;
        result[SignalIndex.VixChange] = ComputeDeltas(vix);
        result[SignalIndex.DxyIndex] = dxy;
        result[SignalIndex.DxyChange] = ComputeReturns(dxy);
        result[SignalIndex.GoldPrice] = gold;
        result[SignalIndex.GoldReturn] = ComputeReturns(gold);
        result[SignalIndex.Treasury10Y] = treas;
        result[SignalIndex.TreasuryChange] = ComputeDeltas(treas);

        var btcRet = ComputeReturns(btcCloses);
        var spRet = result[SignalIndex.Sp500Return];
        var corrArr = new float[n];
        for (int i = 720; i < n; i++)
            corrArr[i] = RollingCorrelation(btcRet, spRet, i, 720);
        result[SignalIndex.BtcSp500Correlation] = corrArr;
    }

    private async Task<List<(long UnixMs, float Value)>> FetchYahooDaily(string symbol, string name)
    {
        var cache = Path.Combine(_cacheDir, $"yahoo_{name}_daily.jsonl");
        if (File.Exists(cache))
            return LoadTimeseriesCache(cache);

        var url = $"https://query1.finance.yahoo.com/v8/finance/chart/" +
                  $"{Uri.EscapeDataString(symbol)}?range=max&interval=1d";
        var json = await _client.GetStringAsync(url);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);

        var chartResult = doc.GetProperty("chart").GetProperty("result")[0];
        var timestamps = chartResult.GetProperty("timestamp");
        var closes = chartResult.GetProperty("indicators").GetProperty("quote")[0].GetProperty("close");

        var data = new List<(long, float)>();
        for (int i = 0; i < timestamps.GetArrayLength(); i++)
        {
            long t = timestamps[i].GetInt64() * 1000;
            if (closes[i].ValueKind == JsonValueKind.Null) continue;
            data.Add((t, (float)closes[i].GetDouble()));
        }

        SaveTimeseriesCache(cache, data);
        return data;
    }

    // ── Phase 3: On-Chain (blockchain.info) ─────────────────────────────

    private async Task AddOnChainSignals(
        Dictionary<int, float[]> result, DateTimeOffset[] ts, int n)
    {
        (string chart, int slot)[] charts =
        [
            ("hash-rate", SignalIndex.HashRate),
            ("n-unique-addresses", SignalIndex.ActiveAddresses),
            ("estimated-transaction-volume-usd", SignalIndex.TransactionVolume),
            ("mempool-size", SignalIndex.MempoolSize),
            ("miners-revenue", SignalIndex.MinerRevenue),
            ("difficulty", SignalIndex.MiningDifficulty),
        ];

        float[]? hashRate = null, txVol = null;
        foreach (var (chart, slot) in charts)
        {
            var daily = await FetchBlockchainChart(chart);
            var aligned = ForwardFillToHourly(daily, ts, n, publicationDelayHours: 24);
            result[slot] = aligned;

            if (slot == SignalIndex.HashRate) hashRate = aligned;
            if (slot == SignalIndex.TransactionVolume) txVol = aligned;

            await Task.Delay(10_000);
        }

        if (hashRate != null)
            result[SignalIndex.HashRateChange] = ComputeReturns(hashRate);

        if (hashRate != null && txVol != null)
        {
            var nvt = new float[n];
            for (int i = 0; i < n; i++)
                nvt[i] = txVol[i] > 0 ? hashRate[i] / txVol[i] : 0f;
            result[SignalIndex.NvtRatio] = nvt;
        }
    }

    private async Task<List<(long UnixMs, float Value)>> FetchBlockchainChart(string chart)
    {
        var cache = Path.Combine(_cacheDir, $"blockchain_{chart}.jsonl");
        if (File.Exists(cache))
            return LoadTimeseriesCache(cache);

        var url = $"https://api.blockchain.info/charts/{chart}?timespan=all&format=json";
        var json = await _client.GetStringAsync(url);
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var values = doc.GetProperty("values");

        var data = new List<(long, float)>();
        for (int i = 0; i < values.GetArrayLength(); i++)
        {
            long t = values[i].GetProperty("x").GetInt64() * 1000;
            float v = (float)values[i].GetProperty("y").GetDouble();
            data.Add((t, v));
        }

        SaveTimeseriesCache(cache, data);
        return data;
    }

    // ── Phase 4: Fear & Greed (alternative.me) ──────────────────────────

    private async Task AddFearGreedSignals(
        Dictionary<int, float[]> result, DateTimeOffset[] ts, int n)
    {
        var daily = await FetchFearGreed();
        var aligned = ForwardFillToHourly(daily, ts, n, publicationDelayHours: 24);

        result[SignalIndex.FearGreedIndex] = aligned;

        var change = new float[n];
        var momentum = new float[n];
        for (int i = 24; i < n; i++)
            change[i] = aligned[i] - aligned[i - 24];
        for (int i = 1; i < n; i++)
            momentum[i] = aligned[i] - aligned[i - 1];

        result[SignalIndex.FearGreedChange] = change;
        result[SignalIndex.SentimentMomentum] = momentum;
    }

    private async Task<List<(long UnixMs, float Value)>> FetchFearGreed()
    {
        var cache = Path.Combine(_cacheDir, "feargreed.jsonl");
        if (File.Exists(cache))
            return LoadTimeseriesCache(cache);

        var json = await _client.GetStringAsync("https://api.alternative.me/fng/?limit=0&format=json");
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var dataArr = doc.GetProperty("data");

        var data = new List<(long, float)>();
        for (int i = 0; i < dataArr.GetArrayLength(); i++)
        {
            long t = long.Parse(dataArr[i].GetProperty("timestamp").GetString()!) * 1000;
            float v = float.Parse(dataArr[i].GetProperty("value").GetString()!);
            data.Add((t, v));
        }

        data.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        SaveTimeseriesCache(cache, data);
        return data;
    }

    // ── Phase 5: Stablecoin (CoinGecko) ─────────────────────────────────

    private async Task AddStablecoinSignals(
        Dictionary<int, float[]> result, DateTimeOffset[] ts, int n)
    {
        var btcMcap = await FetchCoinGeckoMarketChart("bitcoin", "bitcoin_mcap");
        await Task.Delay(3000);
        var ethMcap = await FetchCoinGeckoMarketChart("ethereum", "ethereum_mcap");
        await Task.Delay(3000);
        var usdtMcap = await FetchCoinGeckoMarketChart("tether", "tether_mcap");
        await Task.Delay(3000);
        var usdcMcap = await FetchCoinGeckoMarketChart("usd-coin", "usdc_mcap");

        var btcMcapAligned = ForwardFillToHourly(btcMcap, ts, n, publicationDelayHours: 1);
        var ethMcapAligned = ForwardFillToHourly(ethMcap, ts, n, publicationDelayHours: 1);
        var usdtAligned = ForwardFillToHourly(usdtMcap, ts, n, publicationDelayHours: 1);
        var usdcAligned = ForwardFillToHourly(usdcMcap, ts, n, publicationDelayHours: 1);

        result[SignalIndex.UsdtMarketCap] = usdtAligned;
        result[SignalIndex.UsdcMarketCap] = usdcAligned;

        var totalMcap = new float[n];
        var flowDelta = new float[n];
        var dominance = new float[n];
        var altseason = new float[n];

        for (int i = 0; i < n; i++)
        {
            float btcM = btcMcapAligned[i];
            float ethM = ethMcapAligned[i];
            float stableTotal = usdtAligned[i] + usdcAligned[i];

            float approxTotal = btcM + ethM + stableTotal;
            totalMcap[i] = approxTotal > 0 ? approxTotal : btcM;

            if (approxTotal > 0)
                dominance[i] = btcM / approxTotal * 100f;

            if (btcM > 0)
                altseason[i] = ethM / btcM;

            if (i >= 24)
            {
                float prevStable = usdtAligned[i - 24] + usdcAligned[i - 24];
                flowDelta[i] = prevStable > 0 ? (stableTotal - prevStable) / prevStable : 0f;
            }
        }

        result[SignalIndex.TotalMarketCap] = totalMcap;
        result[SignalIndex.StablecoinFlowDelta] = flowDelta;
        result[SignalIndex.BtcDominance] = dominance;
        result[SignalIndex.AltseasonIndex] = altseason;
    }

    private async Task<List<(long UnixMs, float Value)>> FetchCoinGeckoMarketChart(string coinId, string name)
    {
        var cache = Path.Combine(_cacheDir, $"coingecko_{name}.jsonl");
        if (File.Exists(cache))
            return LoadTimeseriesCache(cache);

        string json;
        foreach (var days in new[] { "max", "365" })
        {
            var url = $"https://api.coingecko.com/api/v3/coins/{coinId}/market_chart?vs_currency=usd&days={days}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(_coinGeckoApiKey))
                request.Headers.Add("x-cg-demo-api-key", _coinGeckoApiKey);
            var response = await _client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                json = await response.Content.ReadAsStringAsync();
                goto parse;
            }
        }
        throw new HttpRequestException("CoinGecko: all day ranges failed");
        parse:
        var doc = JsonSerializer.Deserialize<JsonElement>(json);
        var mcaps = doc.GetProperty("market_caps");

        var data = new List<(long, float)>();
        for (int i = 0; i < mcaps.GetArrayLength(); i++)
        {
            long t = mcaps[i][0].GetInt64();
            float v = (float)mcaps[i][1].GetDouble();
            data.Add((t, v));
        }

        SaveTimeseriesCache(cache, data);
        return data;
    }

    // ── Phase 6: Funding Rates (Binance Futures) ────────────────────────

    private async Task AddFundingRateSignals(
        Dictionary<int, float[]> result, DateTimeOffset[] ts,
        DateTimeOffset start, DateTimeOffset end, int n)
    {
        var btcFunding = await FetchFundingRates("BTCUSDT", "btc_funding", start, end);
        var ethFunding = await FetchFundingRates("ETHUSDT", "eth_funding", start, end);

        result[SignalIndex.FundingRate] = InterpolateFundingToHourly(btcFunding, ts, n);
        result[SignalIndex.EthFundingRate] = InterpolateFundingToHourly(ethFunding, ts, n);
    }

    private async Task<List<(long UnixMs, float Value)>> FetchFundingRates(
        string symbol, string name, DateTimeOffset start, DateTimeOffset end)
    {
        var cache = Path.Combine(_cacheDir, $"funding_{name}.jsonl");
        if (File.Exists(cache))
            return LoadTimeseriesCache(cache);

        var data = new List<(long, float)>();
        long startMs = start.ToUnixTimeMilliseconds();
        long endMs = end.ToUnixTimeMilliseconds();

        while (startMs < endMs)
        {
            try
            {
                var url = $"https://fapi.binance.com/fapi/v1/fundingRate?symbol={symbol}" +
                          $"&startTime={startMs}&endTime={endMs}&limit=1000";
                var json = await _client.GetStringAsync(url);
                var arr = JsonSerializer.Deserialize<JsonElement>(json);
                int count = arr.GetArrayLength();
                if (count == 0) break;

                for (int i = 0; i < count; i++)
                {
                    long t = arr[i].GetProperty("fundingTime").GetInt64();
                    float rate = float.Parse(
                        arr[i].GetProperty("fundingRate").GetString()!,
                        CultureInfo.InvariantCulture);
                    data.Add((t, rate));
                }

                startMs = data[^1].Item1 + 1;
                await Task.Delay(100);
            }
            catch { break; }
        }

        SaveTimeseriesCache(cache, data);
        return data;
    }

    private static float[] InterpolateFundingToHourly(
        List<(long UnixMs, float Value)> funding, DateTimeOffset[] ts, int n)
    {
        var result = new float[n];
        if (funding.Count == 0) return result;

        int fIdx = 0;
        float currentRate = funding[0].Item2;

        for (int i = 0; i < n; i++)
        {
            long tMs = ts[i].ToUnixTimeMilliseconds();
            while (fIdx < funding.Count - 1 && funding[fIdx + 1].Item1 <= tMs)
            {
                fIdx++;
                currentRate = funding[fIdx].Item2;
            }
            result[i] = currentRate;
        }

        return result;
    }

    // ── Phase 7: Derivatives Enrichment ─────────────────────────────────

    private async Task AddDerivativesSignals(
        Dictionary<int, float[]> result, DateTimeOffset[] ts,
        DateTimeOffset start, DateTimeOffset end, int n)
    {
        var endpoints = new (string Endpoint, string Param, int Slot, string CacheName, string ValueProp)[]
        {
            ("/futures/data/globalLongShortAccountRatio", "BTCUSDT", SignalIndex.LongShortRatio, "btc_ls_ratio", "longShortRatio"),
            ("/futures/data/takerlongshortRatio", "BTCUSDT", SignalIndex.TakerBuySellRatio, "btc_taker_ratio", "buySellRatio"),
            ("/futures/data/topLongShortAccountRatio", "BTCUSDT", SignalIndex.TopTraderLongShort, "btc_top_ls", "longShortRatio"),
        };

        foreach (var (endpoint, symbol, slot, cacheName, valueProp) in endpoints)
        {
            try
            {
                var data = await FetchBinanceFuturesData(endpoint, symbol, cacheName, start, end, valueProp);
                result[slot] = ForwardFillToHourly(data, ts, n);
                await Task.Delay(200);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ENRICH] Derivatives {cacheName} failed: {ex.Message}");
            }
        }

        try
        {
            var oiData = await FetchOpenInterestHistory("BTCUSDT", "btc_oi", start, end);
            var oiAligned = ForwardFillToHourly(oiData, ts, n);
            result[SignalIndex.OpenInterest] = oiAligned;

            var oiChange = new float[n];
            for (int i = 1; i < n; i++)
                oiChange[i] = oiAligned[i - 1] > 0 ? (oiAligned[i] - oiAligned[i - 1]) / oiAligned[i - 1] : 0f;
            result[SignalIndex.OiChange1h] = oiChange;
        }
        catch (Exception ex) { Console.WriteLine($"[ENRICH] BTC OI failed: {ex.Message}"); }

        try
        {
            var ethOi = await FetchOpenInterestHistory("ETHUSDT", "eth_oi", start, end);
            result[SignalIndex.EthOpenInterest] = ForwardFillToHourly(ethOi, ts, n);
        }
        catch (Exception ex) { Console.WriteLine($"[ENRICH] ETH OI failed: {ex.Message}"); }
    }

    private async Task<List<(long UnixMs, float Value)>> FetchBinanceFuturesData(
        string endpoint, string symbol, string cacheName, DateTimeOffset start, DateTimeOffset end,
        string valueProperty = "longShortRatio")
    {
        var cache = Path.Combine(_cacheDir, $"{cacheName}.jsonl");
        if (File.Exists(cache))
            return LoadTimeseriesCache(cache);

        var data = new List<(long, float)>();
        long startMs = start.ToUnixTimeMilliseconds();
        long endMs = end.ToUnixTimeMilliseconds();

        while (startMs < endMs)
        {
            try
            {
                var url = $"https://fapi.binance.com{endpoint}?symbol={symbol}" +
                          $"&period=1h&startTime={startMs}&endTime={endMs}&limit=500";
                var json = await _client.GetStringAsync(url);
                var arr = JsonSerializer.Deserialize<JsonElement>(json);
                int count = arr.GetArrayLength();
                if (count == 0) break;

                for (int i = 0; i < count; i++)
                {
                    long t = arr[i].GetProperty("timestamp").GetInt64();
                    float val = float.Parse(
                        arr[i].GetProperty(valueProperty).GetString()!,
                        CultureInfo.InvariantCulture);
                    data.Add((t, val));
                }

                startMs = data[^1].Item1 + 1;
                await Task.Delay(200);
            }
            catch { break; }
        }

        SaveTimeseriesCache(cache, data);
        return data;
    }

    private async Task<List<(long UnixMs, float Value)>> FetchOpenInterestHistory(
        string symbol, string cacheName, DateTimeOffset start, DateTimeOffset end)
    {
        var cache = Path.Combine(_cacheDir, $"{cacheName}.jsonl");
        if (File.Exists(cache))
            return LoadTimeseriesCache(cache);

        var data = new List<(long, float)>();
        long startMs = start.ToUnixTimeMilliseconds();
        long endMs = end.ToUnixTimeMilliseconds();

        while (startMs < endMs)
        {
            try
            {
                var url = $"https://fapi.binance.com/futures/data/openInterestHist?symbol={symbol}" +
                          $"&period=1h&startTime={startMs}&endTime={endMs}&limit=500";
                var json = await _client.GetStringAsync(url);
                var arr = JsonSerializer.Deserialize<JsonElement>(json);
                int count = arr.GetArrayLength();
                if (count == 0) break;

                for (int i = 0; i < count; i++)
                {
                    long t = arr[i].GetProperty("timestamp").GetInt64();
                    float val = float.Parse(
                        arr[i].GetProperty("sumOpenInterest").GetString()!,
                        CultureInfo.InvariantCulture);
                    data.Add((t, val));
                }

                startMs = data[^1].Item1 + 1;
                await Task.Delay(200);
            }
            catch { break; }
        }

        SaveTimeseriesCache(cache, data);
        return data;
    }

    // ── Binance Candle Downloader (reusable for ETH) ────────────────────

    private async Task<TechnicalIndicators.Candle[]> DownloadBinanceCandles(
        string symbol, DateTimeOffset start, DateTimeOffset end)
    {
        var cache = Path.Combine(_cacheDir,
            $"{symbol}_{start:yyyyMMdd}_{end:yyyyMMdd}_1h.jsonl");

        if (File.Exists(cache))
            return LoadCandleCache(cache);

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
                    Pf(k[1]), Pf(k[2]), Pf(k[3]), Pf(k[4]), Pf(k[5]),
                    DateTimeOffset.FromUnixTimeMilliseconds(k[0].GetInt64())));
            }

            startMs = arr[count - 1][6].GetInt64() + 1;
            await Task.Delay(100);
        }

        var result = candles.ToArray();
        SaveCandleCache(cache, result);
        return result;
    }

    // ── Alignment Utilities ─────────────────────────────────────────────

    /// <summary>
    /// Align ETH candles to BTC candle timestamps. If ETH has gaps, carry forward.
    /// </summary>
    private static TechnicalIndicators.Candle[] AlignCandlesToTimestamps(
        TechnicalIndicators.Candle[] source, DateTimeOffset[] targetTimestamps)
    {
        int n = targetTimestamps.Length;
        var result = new TechnicalIndicators.Candle[n];
        int sIdx = 0;
        var last = source.Length > 0 ? source[0] : new TechnicalIndicators.Candle(0, 0, 0, 0, 0, DateTimeOffset.MinValue);

        for (int i = 0; i < n; i++)
        {
            long target = targetTimestamps[i].ToUnixTimeMilliseconds();
            while (sIdx < source.Length - 1 && source[sIdx + 1].Time.ToUnixTimeMilliseconds() <= target)
                sIdx++;
            if (sIdx < source.Length)
                last = source[sIdx];
            result[i] = last;
        }

        return result;
    }

    /// <summary>
    /// Forward-fill a daily (or irregular) time series to align with hourly candle timestamps.
    /// </summary>
    private static float[] ForwardFillToHourly(
        List<(long UnixMs, float Value)> daily, DateTimeOffset[] ts, int n,
        int publicationDelayHours = 0)
    {
        var result = new float[n];
        if (daily.Count == 0) return result;

        long delayMs = (long)publicationDelayHours * 3_600_000L;
        int dIdx = 0;
        float current = daily[0].Item2;

        for (int i = 0; i < n; i++)
        {
            long tMs = ts[i].ToUnixTimeMilliseconds();
            while (dIdx < daily.Count - 1 && daily[dIdx + 1].Item1 + delayMs <= tMs)
            {
                dIdx++;
                current = daily[dIdx].Item2;
            }
            if (daily[dIdx].Item1 + delayMs <= tMs)
                current = daily[dIdx].Item2;
            result[i] = current;
        }

        return result;
    }

    // ── Math Utilities ──────────────────────────────────────────────────

    private static float[] ComputeReturns(float[] data)
    {
        var r = new float[data.Length];
        for (int i = 1; i < data.Length; i++)
            r[i] = data[i - 1] > 0 ? (data[i] - data[i - 1]) / data[i - 1] : 0f;
        return r;
    }

    private static float[] ComputeDeltas(float[] data)
    {
        var d = new float[data.Length];
        for (int i = 1; i < data.Length; i++)
            d[i] = data[i] - data[i - 1];
        return d;
    }

    private static float RollingCorrelation(float[] x, float[] y, int end, int window)
    {
        int start = end - window;
        if (start < 0) return 0f;

        float sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0, sumY2 = 0;
        for (int i = start; i < end; i++)
        {
            sumX += x[i]; sumY += y[i];
            sumXY += x[i] * y[i];
            sumX2 += x[i] * x[i];
            sumY2 += y[i] * y[i];
        }

        float n = window;
        float num = n * sumXY - sumX * sumY;
        float den = MathF.Sqrt((n * sumX2 - sumX * sumX) * (n * sumY2 - sumY * sumY));
        return den > 1e-10f ? num / den : 0f;
    }

    private static float RollingStdDev(float[] data, int end, int window)
    {
        int start = end - window;
        if (start < 0) return 0f;

        float sum = 0, sumSq = 0;
        for (int i = start; i < end; i++)
        {
            sum += data[i];
            sumSq += data[i] * data[i];
        }

        float mean = sum / window;
        float variance = sumSq / window - mean * mean;
        return MathF.Sqrt(MathF.Max(0, variance));
    }

    // ── Cache Utilities ─────────────────────────────────────────────────

    private static void SaveTimeseriesCache(string path, List<(long UnixMs, float Value)> data)
    {
        var lines = data.Select(d =>
            string.Create(CultureInfo.InvariantCulture, $"{d.Item1},{d.Item2}"));
        File.WriteAllLines(path, lines);
    }

    private static List<(long UnixMs, float Value)> LoadTimeseriesCache(string path)
    {
        return File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l =>
            {
                var parts = l.Split(',');
                return (UnixMs: long.Parse(parts[0]),
                        Value: float.Parse(parts[1], CultureInfo.InvariantCulture));
            }).ToList();
    }

    private static void SaveCandleCache(string path, TechnicalIndicators.Candle[] candles)
    {
        var lines = candles.Select(c =>
            string.Create(CultureInfo.InvariantCulture,
                $"{c.Open},{c.High},{c.Low},{c.Close},{c.Volume},{c.Time.ToUnixTimeMilliseconds()}"));
        File.WriteAllLines(path, lines);
    }

    private static TechnicalIndicators.Candle[] LoadCandleCache(string path)
    {
        return File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l =>
            {
                var p = l.Split(',');
                return new TechnicalIndicators.Candle(
                    float.Parse(p[0], CultureInfo.InvariantCulture),
                    float.Parse(p[1], CultureInfo.InvariantCulture),
                    float.Parse(p[2], CultureInfo.InvariantCulture),
                    float.Parse(p[3], CultureInfo.InvariantCulture),
                    float.Parse(p[4], CultureInfo.InvariantCulture),
                    DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(p[5])));
            }).ToArray();
    }

    private static float Pf(JsonElement el) =>
        float.Parse(el.GetString()!, CultureInfo.InvariantCulture);
}
