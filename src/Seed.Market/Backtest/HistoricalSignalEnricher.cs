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
    private readonly string? _coinglassApiKey;
    private readonly string _interval;
    private readonly int _barsPerHour;

    public HistoricalSignalEnricher(string cacheDir, string? coinGeckoApiKey = null,
        string interval = "1h", int barsPerHour = 1, string? coinglassApiKey = null)
    {
        _cacheDir = cacheDir;
        _coinGeckoApiKey = coinGeckoApiKey;
        _coinglassApiKey = coinglassApiKey;
        _interval = interval;
        _barsPerHour = barsPerHour;
        Directory.CreateDirectory(_cacheDir);
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _client.DefaultRequestHeaders.Add("User-Agent", "SeedMarket/1.0");
    }

    /// <summary>
    /// Download all supplemental data and return per-slot arrays aligned to BTC candle timestamps,
    /// along with an EnrichmentReport tracking the status of each data source.
    /// </summary>
    public async Task<(Dictionary<int, float[]> Data, EnrichmentReport Report)> EnrichAsync(
        TechnicalIndicators.Candle[] btcCandles, DateTimeOffset start, DateTimeOffset end)
    {
        int n = btcCandles.Length;
        var timestamps = btcCandles.Select(c => c.Time).ToArray();
        var result = new Dictionary<int, float[]>();
        var report = new EnrichmentReport
        {
            Timestamp = DateTimeOffset.UtcNow,
            DateRange = $"{start:yyyy-MM-dd} to {end:yyyy-MM-dd}"
        };

        var btcCloses = btcCandles.Select(c => c.Close).ToArray();

        // 1: ETH candles + multi-asset derived
        try
        {
            Console.WriteLine("[ENRICH] Downloading ETH candles...");
            var ethCandles = await DownloadBinanceCandles("ETHUSDT", start, end);
            var aligned = AlignCandlesToTimestamps(ethCandles, timestamps);
            AddEthSignals(result, btcCandles, aligned, n);
            AddMultiAssetSignals(result, btcCloses, aligned, n);
            Console.WriteLine($"[ENRICH] ETH + multi-asset: {CountSlots(result)} slots");
            report.Sources.Add(new("ETH + Multi-Asset", DataSourceStatus.Success, ethCandles.Length));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENRICH] ETH failed: {ex.Message}");
            report.Sources.Add(new("ETH + Multi-Asset", DataSourceStatus.Failed, 0, Error: ex.Message));
        }

        AddBtcVolumeRatio(result, btcCandles, n);

        // 2: Macro (Yahoo Finance)
        try
        {
            Console.WriteLine("[ENRICH] Downloading macro data...");
            int slotsBefore = CountSlots(result);
            await AddMacroSignals(result, timestamps, btcCloses, n);
            int slotsAdded = CountSlots(result) - slotsBefore;
            Console.WriteLine($"[ENRICH] +Macro: {CountSlots(result)} slots");
            report.Sources.Add(new("Macro (Yahoo)", DataSourceStatus.Success, slotsAdded));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENRICH] Macro failed: {ex.Message}");
            report.Sources.Add(new("Macro (Yahoo)", DataSourceStatus.Failed, 0, Error: ex.Message));
        }

        // 3: On-chain (blockchain.info)
        try
        {
            Console.WriteLine("[ENRICH] Downloading on-chain data...");
            int slotsBefore = CountSlots(result);
            await AddOnChainSignals(result, timestamps, n);
            int slotsAdded = CountSlots(result) - slotsBefore;
            Console.WriteLine($"[ENRICH] +On-chain: {CountSlots(result)} slots");
            report.Sources.Add(new("On-Chain (blockchain.info)", DataSourceStatus.Success, slotsAdded));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENRICH] On-chain failed: {ex.Message}");
            report.Sources.Add(new("On-Chain (blockchain.info)", DataSourceStatus.Failed, 0, Error: ex.Message));
        }

        // 4: Fear & Greed (alternative.me)
        try
        {
            Console.WriteLine("[ENRICH] Downloading Fear & Greed...");
            int slotsBefore = CountSlots(result);
            await AddFearGreedSignals(result, timestamps, n);
            int slotsAdded = CountSlots(result) - slotsBefore;
            Console.WriteLine($"[ENRICH] +Fear/Greed: {CountSlots(result)} slots");
            report.Sources.Add(new("Fear & Greed (alternative.me)", DataSourceStatus.Success, slotsAdded));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENRICH] Fear/Greed failed: {ex.Message}");
            report.Sources.Add(new("Fear & Greed (alternative.me)", DataSourceStatus.Failed, 0, Error: ex.Message));
        }

        // 5: Stablecoin (CoinGecko)
        try
        {
            Console.WriteLine("[ENRICH] Downloading stablecoin/market data...");
            int slotsBefore = CountSlots(result);
            await AddStablecoinSignals(result, timestamps, n);
            int slotsAdded = CountSlots(result) - slotsBefore;
            Console.WriteLine($"[ENRICH] +Stablecoin: {CountSlots(result)} slots");
            report.Sources.Add(new("Stablecoin (CoinGecko)", DataSourceStatus.Success, slotsAdded));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENRICH] Stablecoin failed: {ex.Message}");
            report.Sources.Add(new("Stablecoin (CoinGecko)", DataSourceStatus.Failed, 0, Error: ex.Message));
        }

        // 6: Funding rates (Binance Futures)
        try
        {
            Console.WriteLine("[ENRICH] Downloading funding rates...");
            int slotsBefore = CountSlots(result);
            await AddFundingRateSignals(result, timestamps, start, end, n);
            int slotsAdded = CountSlots(result) - slotsBefore;
            Console.WriteLine($"[ENRICH] +Funding: {CountSlots(result)} slots");
            report.Sources.Add(new("Funding Rates (Binance)", DataSourceStatus.Success, slotsAdded));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENRICH] Funding failed: {ex.Message}");
            report.Sources.Add(new("Funding Rates (Binance)", DataSourceStatus.Failed, 0, Error: ex.Message));
        }

        // 7: Derivatives (OI, L/S ratio, taker ratio)
        try
        {
            Console.WriteLine("[ENRICH] Downloading derivatives data...");
            int slotsBefore = CountSlots(result);
            var derivativesTask = AddDerivativesSignals(result, timestamps, start, end, n);
            if (await Task.WhenAny(derivativesTask, Task.Delay(TimeSpan.FromSeconds(60))) == derivativesTask)
            {
                await derivativesTask;
                int slotsAdded = CountSlots(result) - slotsBefore;
                Console.WriteLine($"[ENRICH] +Derivatives: {CountSlots(result)} slots");
                report.Sources.Add(new("Derivatives (Binance)", DataSourceStatus.Success, slotsAdded));
            }
            else
            {
                Console.WriteLine("[ENRICH] Derivatives download timed out after 60s, skipping");
                report.Sources.Add(new("Derivatives (Binance)", DataSourceStatus.Timeout, 0,
                    Error: "Download timed out after 60s"));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENRICH] Derivatives failed: {ex.Message}");
            report.Sources.Add(new("Derivatives (Binance)", DataSourceStatus.Failed, 0, Error: ex.Message));
        }

        // V14 G1: Binance futures historical klines → FuturesPremium
        try
        {
            Console.WriteLine("[ENRICH] Downloading futures klines for FuturesPremium...");
            int slotsBefore = CountSlots(result);
            await AddFuturesPremium(result, timestamps, btcCloses, start, end, n);
            int slotsAdded = CountSlots(result) - slotsBefore;
            Console.WriteLine($"[ENRICH] +FuturesPremium: {slotsAdded} slots");
            report.Sources.Add(new("Futures Klines (Binance)", DataSourceStatus.Success, slotsAdded));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENRICH] FuturesPremium failed: {ex.Message}");
            report.Sources.Add(new("Futures Klines (Binance)", DataSourceStatus.Failed, 0, Error: ex.Message));
        }

        // V14 G2: Deribit DVOL historical → DeribitIVPercentile
        try
        {
            Console.WriteLine("[ENRICH] Downloading Deribit DVOL (IV index)...");
            int slotsBefore = CountSlots(result);
            await AddDeribitIvPercentile(result, timestamps, start, end, n);
            int slotsAdded = CountSlots(result) - slotsBefore;
            Console.WriteLine($"[ENRICH] +Deribit IV: {slotsAdded} slots");
            report.Sources.Add(new("Deribit DVOL", DataSourceStatus.Success, slotsAdded));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENRICH] Deribit failed: {ex.Message}");
            report.Sources.Add(new("Deribit DVOL", DataSourceStatus.Failed, 0, Error: ex.Message));
        }

        // Coinglass: Liquidation history + Exchange balance
        if (!string.IsNullOrEmpty(_coinglassApiKey))
        {
            try
            {
                Console.WriteLine("[ENRICH] Downloading Coinglass liquidation + exchange balance...");
                int slotsAdded = await EnrichFromCoinglass(result, timestamps, n, start, end);
                Console.WriteLine($"[ENRICH] +Coinglass: {slotsAdded} slots");
                report.Sources.Add(new("Coinglass (Liquidation + Exchange)", DataSourceStatus.Success, slotsAdded));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ENRICH] Coinglass failed: {ex.Message}");
                report.Sources.Add(new("Coinglass", DataSourceStatus.Failed, 0, Error: ex.Message));
            }
        }
        else
        {
            Console.WriteLine("[ENRICH] No CoinglassApiKey — skipping liquidation + exchange balance enrichment");
            report.Sources.Add(new("Coinglass", DataSourceStatus.Empty, 0, Error: "No API key configured"));
        }

        Console.WriteLine($"[ENRICH] Complete: {CountSlots(result)} enrichment slots populated");
        return (result, report);
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

    private void AddMultiAssetSignals(
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

        int window = 24 * _barsPerHour;
        int momLookback = 12 * _barsPerHour;
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

            btcMom[i] = i >= momLookback && btcCloses[i - momLookback] > 0
                ? (btcCloses[i] - btcCloses[i - momLookback]) / btcCloses[i - momLookback] : 0f;
            ethMom[i] = i >= momLookback && ethCloses[i - momLookback] > 0
                ? (ethCloses[i] - ethCloses[i - momLookback]) / ethCloses[i - momLookback] : 0f;
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

    private void AddBtcVolumeRatio(
        Dictionary<int, float[]> result, TechnicalIndicators.Candle[] btc, int n)
    {
        int windowSize = 24 * _barsPerHour;
        var ratio = new float[n];
        float sum = 0;
        var window = new Queue<float>();
        for (int i = 0; i < n; i++)
        {
            float vol = btc[i].Volume;
            window.Enqueue(vol);
            sum += vol;
            while (window.Count > windowSize) sum -= window.Dequeue();
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
            series[name] = ForwardFillToBars(daily, ts, n, publicationDelayHours: 16);
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
        int corrWindow = 720 * _barsPerHour;
        var corrArr = new float[n];
        for (int i = corrWindow; i < n; i++)
            corrArr[i] = RollingCorrelation(btcRet, spRet, i, corrWindow);
        result[SignalIndex.BtcSp500Correlation] = corrArr;
    }

    private async Task<List<(long UnixMs, float Value)>> FetchYahooDaily(string symbol, string name)
    {
        var cache = Path.Combine(_cacheDir, $"yahoo_{name}_daily.jsonl");
        if (File.Exists(cache))
        {
            var cached = LoadTimeseriesCache(cache);
            if (cached != null) return cached;
        }

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
            var aligned = ForwardFillToBars(daily, ts, n, publicationDelayHours: 24);
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
        {
            var cached = LoadTimeseriesCache(cache);
            if (cached != null) return cached;
        }

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
        var aligned = ForwardFillToBars(daily, ts, n, publicationDelayHours: 24);

        result[SignalIndex.FearGreedIndex] = aligned;

        int deltaWindow = 24 * _barsPerHour;
        var change = new float[n];
        var momentum = new float[n];
        for (int i = deltaWindow; i < n; i++)
            change[i] = aligned[i] - aligned[i - deltaWindow];
        for (int i = 1; i < n; i++)
            momentum[i] = aligned[i] - aligned[i - 1];

        result[SignalIndex.FearGreedChange] = change;
        result[SignalIndex.SentimentMomentum] = momentum;
    }

    private async Task<List<(long UnixMs, float Value)>> FetchFearGreed()
    {
        var cache = Path.Combine(_cacheDir, "feargreed.jsonl");
        if (File.Exists(cache))
        {
            var cached = LoadTimeseriesCache(cache);
            if (cached != null) return cached;
        }

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

        var btcMcapAligned = ForwardFillToBars(btcMcap, ts, n, publicationDelayHours: 1);
        var ethMcapAligned = ForwardFillToBars(ethMcap, ts, n, publicationDelayHours: 1);
        var usdtAligned = ForwardFillToBars(usdtMcap, ts, n, publicationDelayHours: 1);
        var usdcAligned = ForwardFillToBars(usdcMcap, ts, n, publicationDelayHours: 1);

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

            int flowLookback = 24 * _barsPerHour;
            if (i >= flowLookback)
            {
                float prevStable = usdtAligned[i - flowLookback] + usdcAligned[i - flowLookback];
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
        {
            var cached = LoadTimeseriesCache(cache);
            if (cached != null) return cached;
        }

        string? json = null;
        foreach (var days in new[] { "max", "365" })
        {
            var url = $"https://api.coingecko.com/api/v3/coins/{coinId}/market_chart?vs_currency=usd&days={days}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(_coinGeckoApiKey))
                request.Headers.Add("x-cg-demo-api-key", _coinGeckoApiKey);
            using var response = await _client.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                json = await response.Content.ReadAsStringAsync();
                break;
            }
            Console.WriteLine($"[ENRICH] CoinGecko {coinId} days={days}: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        if (json == null)
            throw new HttpRequestException($"CoinGecko: all day ranges failed for {coinId} (apiKey={(_coinGeckoApiKey != null ? "set" : "NULL")})");
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

        result[SignalIndex.FundingRate] = InterpolateFundingToBars(btcFunding, ts, n);
        result[SignalIndex.EthFundingRate] = InterpolateFundingToBars(ethFunding, ts, n);
    }

    private async Task<List<(long UnixMs, float Value)>> FetchFundingRates(
        string symbol, string name, DateTimeOffset start, DateTimeOffset end)
    {
        var cache = Path.Combine(_cacheDir, $"funding_{name}.jsonl");
        if (File.Exists(cache))
        {
            var cached = LoadTimeseriesCache(cache);
            if (cached != null) return cached;
        }

        var data = new List<(long, float)>();
        long startMs = start.ToUnixTimeMilliseconds();
        long endMs = end.ToUnixTimeMilliseconds();
        const int limit = 1000;

        while (startMs < endMs)
        {
            try
            {
                var url = $"https://fapi.binance.com/fapi/v1/fundingRate?symbol={symbol}" +
                          $"&startTime={startMs}&endTime={endMs}&limit={limit}";
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

                long newStart = data[^1].Item1 + 1;
                if (newStart <= startMs || count < limit) break;
                startMs = newStart;
                await Task.Delay(100);
            }
            catch { break; }
        }

        SaveTimeseriesCache(cache, data);
        return data;
    }

    private static float[] InterpolateFundingToBars(
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

    // ── V14 G1: Binance Futures Klines → FuturesPremium ─────────────────

    private async Task AddFuturesPremium(
        Dictionary<int, float[]> result, DateTimeOffset[] ts, float[] btcCloses,
        DateTimeOffset start, DateTimeOffset end, int n)
    {
        var futuresCandles = await FetchBinanceFuturesKlines("BTCUSDT", "btc_futures", start, end);
        var futuresCloses = AlignTimeseriesToBars(futuresCandles, ts, n);

        var premium = new float[n];
        for (int i = 0; i < n; i++)
        {
            float spot = btcCloses[i];
            float future = futuresCloses[i];
            if (spot > 0f && future > 0f)
                premium[i] = (future - spot) / spot;
        }
        result[SignalIndex.FuturesPremium] = premium;
    }

    private async Task<List<(long UnixMs, float Close)>> FetchBinanceFuturesKlines(
        string symbol, string cacheName, DateTimeOffset start, DateTimeOffset end)
    {
        var cache = Path.Combine(_cacheDir, $"{cacheName}_{start:yyyyMMdd}_{end:yyyyMMdd}_{_interval}.jsonl");
        if (File.Exists(cache))
        {
            var cached = LoadTimeseriesCache(cache);
            if (cached != null) return cached;
        }

        var data = new List<(long, float)>();
        long startMs = start.ToUnixTimeMilliseconds();
        long endMs = end.ToUnixTimeMilliseconds();

        while (startMs < endMs)
        {
            try
            {
                // Binance futures klines endpoint; max 1500 candles per request
                var url = $"https://fapi.binance.com/fapi/v1/klines?symbol={symbol}" +
                          $"&interval={_interval}&startTime={startMs}&endTime={endMs}&limit=1500";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var json = await _client.GetStringAsync(url, cts.Token);
                var arr = JsonSerializer.Deserialize<JsonElement>(json);
                int count = arr.GetArrayLength();
                if (count == 0) break;

                for (int i = 0; i < count; i++)
                {
                    var k = arr[i];
                    long t = k[0].GetInt64();
                    float close = float.Parse(k[4].GetString()!, CultureInfo.InvariantCulture);
                    data.Add((t, close));
                }

                long newStart = arr[count - 1][6].GetInt64() + 1;  // closeTime + 1
                if (newStart <= startMs || count < 1500) break;
                startMs = newStart;
                await Task.Delay(200);
            }
            catch { break; }
        }

        SaveTimeseriesCache(cache, data);
        return data;
    }

    // ── V14 G2: Deribit Historical Volatility / DVOL → IV percentile ────

    private async Task AddDeribitIvPercentile(
        Dictionary<int, float[]> result, DateTimeOffset[] ts,
        DateTimeOffset start, DateTimeOffset end, int n)
    {
        var dvol = await FetchDeribitDvol(start, end);
        if (dvol.Count == 0)
        {
            Console.WriteLine("[ENRICH] Deribit DVOL returned no data");
            return;
        }

        var dvolAligned = ForwardFillToBars(dvol, ts, n);

        // 30-day rolling percentile of DVOL
        int pctWindow = 30 * 24 * _barsPerHour;  // 30 days in bars
        var ivPct = new float[n];
        for (int i = 0; i < n; i++)
        {
            int start2 = Math.Max(0, i - pctWindow + 1);
            int count = i - start2 + 1;
            if (count < 2) { ivPct[i] = 0.5f; continue; }

            float current = dvolAligned[i];
            int below = 0;
            for (int j = start2; j <= i; j++)
                if (dvolAligned[j] < current) below++;
            ivPct[i] = (float)below / (count - 1);
        }
        result[SignalIndex.DeribitIVPercentile] = ivPct;
    }

    private async Task<List<(long UnixMs, float Value)>> FetchDeribitDvol(DateTimeOffset start, DateTimeOffset end)
    {
        var cache = Path.Combine(_cacheDir, $"deribit_dvol_{start:yyyyMMdd}_{end:yyyyMMdd}.jsonl");
        if (File.Exists(cache))
        {
            var cached = LoadTimeseriesCache(cache);
            if (cached != null) return cached;
        }

        var data = new List<(long, float)>();
        long startMs = start.ToUnixTimeMilliseconds();
        long endMs = end.ToUnixTimeMilliseconds();
        const long ninetyDaysMs = 90L * 24 * 60 * 60 * 1000;
        long cursorStart = startMs;

        while (cursorStart < endMs)
        {
            long cursorEnd = Math.Min(cursorStart + ninetyDaysMs, endMs);
            try
            {
                // Deribit public volatility index endpoint. Resolution 3600s = 1h.
                var url = "https://www.deribit.com/api/v2/public/get_volatility_index_data" +
                          $"?currency=BTC&start_timestamp={cursorStart}&end_timestamp={cursorEnd}&resolution=3600";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var json = await _client.GetStringAsync(url, cts.Token);
                var doc = JsonSerializer.Deserialize<JsonElement>(json);

                if (doc.TryGetProperty("result", out var resultObj)
                    && resultObj.TryGetProperty("data", out var arr)
                    && arr.ValueKind == JsonValueKind.Array)
                {
                    int count = arr.GetArrayLength();
                    for (int i = 0; i < count; i++)
                    {
                        // Deribit returns [timestamp_ms, open, high, low, close]
                        long t = arr[i][0].GetInt64();
                        float close = (float)arr[i][4].GetDouble();
                        data.Add((t, close));
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ENRICH] Deribit DVOL chunk failed: {ex.Message}");
            }

            cursorStart = cursorEnd + 1;
            await Task.Delay(500);
        }

        SaveTimeseriesCache(cache, data);
        return data;
    }

    /// <summary>
    /// Align an irregular timeseries (e.g., futures klines) to target bar timestamps
    /// by carrying forward the most recent value.
    /// </summary>
    private static float[] AlignTimeseriesToBars(
        List<(long UnixMs, float Value)> source, DateTimeOffset[] ts, int n)
    {
        var result = new float[n];
        if (source.Count == 0) return result;

        int sIdx = 0;
        float current = source[0].Item2;
        for (int i = 0; i < n; i++)
        {
            long tMs = ts[i].ToUnixTimeMilliseconds();
            while (sIdx < source.Count - 1 && source[sIdx + 1].Item1 <= tMs)
            {
                sIdx++;
                current = source[sIdx].Item2;
            }
            result[i] = current;
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
                result[slot] = ForwardFillToBars(data, ts, n);
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
            var oiAligned = ForwardFillToBars(oiData, ts, n);
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
            result[SignalIndex.EthOpenInterest] = ForwardFillToBars(ethOi, ts, n);
        }
        catch (Exception ex) { Console.WriteLine($"[ENRICH] ETH OI failed: {ex.Message}"); }
    }

    private async Task<List<(long UnixMs, float Value)>> FetchBinanceFuturesData(
        string endpoint, string symbol, string cacheName, DateTimeOffset start, DateTimeOffset end,
        string valueProperty = "longShortRatio")
    {
        var cache = Path.Combine(_cacheDir, $"{cacheName}.jsonl");
        if (File.Exists(cache))
        {
            var cached = LoadTimeseriesCache(cache);
            if (cached != null) return cached;
        }

        var data = new List<(long, float)>();
        long startMs = start.ToUnixTimeMilliseconds();
        long endMs = end.ToUnixTimeMilliseconds();
        const int limit = 500;

        while (startMs < endMs)
        {
            try
            {
                var url = $"https://fapi.binance.com{endpoint}?symbol={symbol}" +
                          $"&period={_interval}&startTime={startMs}&endTime={endMs}&limit={limit}";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var json = await _client.GetStringAsync(url, cts.Token);
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

                long newStart = data[^1].Item1 + 1;
                if (newStart <= startMs || count < limit) break;
                startMs = newStart;
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
        {
            var cached = LoadTimeseriesCache(cache);
            if (cached != null) return cached;
        }

        var data = new List<(long, float)>();
        long startMs = start.ToUnixTimeMilliseconds();
        long endMs = end.ToUnixTimeMilliseconds();
        const int limit = 500;

        while (startMs < endMs)
        {
            try
            {
                var url = $"https://fapi.binance.com/futures/data/openInterestHist?symbol={symbol}" +
                          $"&period={_interval}&startTime={startMs}&endTime={endMs}&limit={limit}";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var json = await _client.GetStringAsync(url, cts.Token);
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

                long newStart = data[^1].Item1 + 1;
                if (newStart <= startMs || count < limit) break;
                startMs = newStart;
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
            $"{symbol}_{start:yyyyMMdd}_{end:yyyyMMdd}_{_interval}.jsonl");

        if (File.Exists(cache))
        {
            var cached = LoadCandleCache(cache);
            if (cached != null) return cached;
        }

        var candles = new List<TechnicalIndicators.Candle>();
        long startMs = start.ToUnixTimeMilliseconds();
        long endMs = end.ToUnixTimeMilliseconds();

        while (startMs < endMs)
        {
            var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}" +
                      $"&interval={_interval}&startTime={startMs}&endTime={endMs}&limit=1000";
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
    /// Forward-fill a daily (or irregular) time series to align with bar timestamps.
    /// </summary>
    private static float[] ForwardFillToBars(
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

    internal static void SaveTimeseriesCache(string path, List<(long UnixMs, float Value)> data)
    {
        if (data.Count == 0) return;
        var lines = data.Select(d =>
            string.Create(CultureInfo.InvariantCulture, $"{d.Item1},{d.Item2}"));
        File.WriteAllLines(path, lines);
    }

    internal static List<(long UnixMs, float Value)>? LoadTimeseriesCache(string path)
    {
        var result = File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l =>
            {
                var parts = l.Split(',');
                return (UnixMs: long.Parse(parts[0]),
                        Value: float.Parse(parts[1], CultureInfo.InvariantCulture));
            }).ToList();
        if (result.Count == 0)
        {
            Console.WriteLine($"[CACHE] Deleting empty cache file: {Path.GetFileName(path)}");
            File.Delete(path);
            return null;
        }
        return result;
    }

    private static void SaveCandleCache(string path, TechnicalIndicators.Candle[] candles)
    {
        if (candles.Length == 0) return;
        var lines = candles.Select(c =>
            string.Create(CultureInfo.InvariantCulture,
                $"{c.Open},{c.High},{c.Low},{c.Close},{c.Volume},{c.Time.ToUnixTimeMilliseconds()}"));
        File.WriteAllLines(path, lines);
    }

    internal static TechnicalIndicators.Candle[]? LoadCandleCache(string path)
    {
        var result = File.ReadAllLines(path)
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
        if (result.Length == 0)
        {
            Console.WriteLine($"[CACHE] Deleting empty cache file: {Path.GetFileName(path)}");
            File.Delete(path);
            return null;
        }
        return result;
    }

    private static float Pf(JsonElement el) =>
        float.Parse(el.GetString()!, CultureInfo.InvariantCulture);

    // ── Coinglass Enrichment ─────────────────────────────────────

    private async Task<int> EnrichFromCoinglass(
        Dictionary<int, float[]> result,
        DateTimeOffset[] timestamps, int n,
        DateTimeOffset start, DateTimeOffset end)
    {
        const string baseUrl = "https://open-api-v4.coinglass.com";
        const float liquidationScale = 10_000_000f;
        const float netFlowScale = 5_000f;
        const float circulatingSupply = 20_000_000f;
        int slotsAdded = 0;

        // 1. Liquidation history (4h interval)
        try
        {
            var liqLong = new float[n];
            var liqShort = new float[n];
            long startMs = start.ToUnixTimeMilliseconds();
            long endMs = end.ToUnixTimeMilliseconds();

            var liqJson = await CoinglassFetch(
                $"{baseUrl}/api/futures/liquidation/aggregated-history?symbol=BTC&interval=4h&limit=1000&exchange_list=Binance&start_time={startMs}&end_time={endMs}");

            var liqDoc = JsonSerializer.Deserialize<JsonElement>(liqJson);
            if (liqDoc.TryGetProperty("data", out var liqData) && liqData.ValueKind == JsonValueKind.Array)
            {
                var liqPoints = new List<(long time, float longUsd, float shortUsd)>();
                foreach (var entry in liqData.EnumerateArray())
                {
                    long t = entry.GetProperty("time").GetInt64();
                    float longVal = (float)entry.GetProperty("aggregated_long_liquidation_usd").GetDouble();
                    float shortVal = (float)entry.GetProperty("aggregated_short_liquidation_usd").GetDouble();
                    liqPoints.Add((t, longVal, shortVal));
                }

                // Forward-fill to bar timestamps
                int pi = 0;
                for (int i = 0; i < n; i++)
                {
                    long barMs = timestamps[i].ToUnixTimeMilliseconds();
                    while (pi < liqPoints.Count - 1 && liqPoints[pi + 1].time <= barMs)
                        pi++;
                    if (pi < liqPoints.Count && liqPoints[pi].time <= barMs)
                    {
                        liqLong[i] = Math.Clamp(liqPoints[pi].longUsd / liquidationScale, 0f, 1f);
                        liqShort[i] = Math.Clamp(liqPoints[pi].shortUsd / liquidationScale, 0f, 1f);
                    }
                }

                result[SignalIndex.LiquidationLong1h] = liqLong;
                result[SignalIndex.LiquidationShort1h] = liqShort;
                slotsAdded += 2;
            }
            await Task.Delay(200); // Rate limit courtesy
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENRICH] Coinglass liquidation history failed: {ex.Message}");
        }

        // 2. Exchange balance (daily) → SupplyOnExchanges + ExchangeNetFlow
        try
        {
            var supplyOnExch = new float[n];
            var netFlow = new float[n];

            var balJson = await CoinglassFetch(
                $"{baseUrl}/api/exchange/balance/chart?symbol=BTC&range=1d");

            var balDoc = JsonSerializer.Deserialize<JsonElement>(balJson);
            if (balDoc.TryGetProperty("data", out var balData) &&
                balData.TryGetProperty("time_list", out var timeList))
            {
                // Sum all exchange balances per timestamp
                int timeCount = timeList.GetArrayLength();
                var dailyBalances = new (long time, float balance)[timeCount];

                for (int t = 0; t < timeCount; t++)
                    dailyBalances[t].time = timeList[t].GetInt64();

                // Sum across all exchanges for each timestamp
                foreach (var prop in balData.EnumerateObject())
                {
                    if (prop.Name == "time_list") continue;
                    if (!prop.Value.TryGetProperty("price_list", out var priceList)) continue;
                    int len = priceList.GetArrayLength();
                    for (int t = 0; t < Math.Min(len, timeCount); t++)
                    {
                        if (priceList[t].ValueKind == JsonValueKind.Number)
                            dailyBalances[t].balance += (float)priceList[t].GetDouble();
                    }
                }

                // Forward-fill daily balances to bar timestamps
                int pi = 0;
                float prevBal = 0f;
                for (int i = 0; i < n; i++)
                {
                    long barMs = timestamps[i].ToUnixTimeMilliseconds();
                    while (pi < dailyBalances.Length - 1 && dailyBalances[pi + 1].time <= barMs)
                        pi++;
                    if (pi < dailyBalances.Length && dailyBalances[pi].time <= barMs)
                    {
                        float bal = dailyBalances[pi].balance;
                        supplyOnExch[i] = Math.Clamp(bal / circulatingSupply, 0f, 1f);

                        if (prevBal > 0)
                            netFlow[i] = Math.Clamp((bal - prevBal) / netFlowScale, -1f, 1f);
                        prevBal = bal;
                    }
                }

                result[SignalIndex.SupplyOnExchanges] = supplyOnExch;
                result[SignalIndex.ExchangeNetFlow] = netFlow;
                slotsAdded += 2;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ENRICH] Coinglass exchange balance failed: {ex.Message}");
        }

        return slotsAdded;
    }

    private async Task<string> CoinglassFetch(string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("CG-API-KEY", _coinglassApiKey);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
