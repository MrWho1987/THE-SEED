using System.Globalization;
using System.Text.Json;

public static class SentimentTest
{
    const decimal FEE_RATE = 0.0005m;

    public static async Task Run(decimal startingCapital)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "SeedBacktest/1.0");

        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   PROOF: PRICE-ONLY  vs  PRICE + SENTIMENT                  ║");
        Console.WriteLine("║   Real BTC/USDT data + Real Fear & Greed Index               ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝\n");

        // Fetch BTC candles
        Console.Write("Fetching BTC/USDT hourly candles...");
        var btc = await FetchCandles(client, "BTCUSDT", 9);
        Console.WriteLine($" {btc.Count} candles loaded.");

        // Fetch Fear & Greed Index (daily, from Alternative.me - free, no key)
        Console.Write("Fetching Fear & Greed Index...");
        var fng = await FetchFearGreed(client);
        Console.WriteLine($" {fng.Count} daily readings loaded.");

        if (fng.Count == 0)
        {
            Console.WriteLine("ERROR: Could not fetch Fear & Greed data. Aborting.");
            return;
        }

        // Map daily F&G to hourly candles
        var candleFng = new int[btc.Count];
        for (int i = 0; i < btc.Count; i++)
        {
            var candleDate = DateTimeOffset.FromUnixTimeMilliseconds(btc[i].OpenTime).Date;
            candleFng[i] = fng.TryGetValue(candleDate, out var val) ? val : 50; // default neutral
        }

        // Show F&G distribution for context
        var fngValues = candleFng.Where(v => v != 50).ToArray();
        if (fngValues.Length > 0)
        {
            Console.WriteLine($"\nFear & Greed Index over period:");
            Console.WriteLine($"  Extreme Fear (<25):  {fngValues.Count(v => v < 25)} days");
            Console.WriteLine($"  Fear (25-45):        {fngValues.Count(v => v >= 25 && v < 45)} days");
            Console.WriteLine($"  Neutral (45-55):     {fngValues.Count(v => v >= 45 && v < 55)} days");
            Console.WriteLine($"  Greed (55-75):       {fngValues.Count(v => v >= 55 && v < 75)} days");
            Console.WriteLine($"  Extreme Greed (>75): {fngValues.Count(v => v >= 75)} days");
        }

        var btcStart = DateTimeOffset.FromUnixTimeMilliseconds(btc.First().OpenTime);
        var btcEnd = DateTimeOffset.FromUnixTimeMilliseconds(btc.Last().OpenTime);
        Console.WriteLine($"\nPeriod: {btcStart:yyyy-MM-dd} to {btcEnd:yyyy-MM-dd}");
        Console.WriteLine($"BTC: ${btc.First().Close:N0} → ${btc.Last().Close:N0} ({(btc.Last().Close - btc.First().Close) / btc.First().Close * 100:+0.0;-0.0}%)");

        // ═══ TEST A: PRICE ONLY (baseline) ═══
        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("TEST A: PRICE ONLY (RSI signals, no sentiment)");
        Console.WriteLine("══════════════════════════════════════════════════════════════");

        decimal bestPriceOnly = 0;
        (int rsiP, decimal rsiB, decimal rsiS, decimal pos) bestPriceParams = default;
        var priceOnlyResults = new List<decimal>();

        int[] rsiPeriods = [7, 10, 14, 21];
        decimal[] buyThresholds = [20, 25, 30, 35, 40];
        decimal[] sellThresholds = [60, 65, 70, 75, 80];
        decimal[] posSizes = [0.10m, 0.15m, 0.20m, 0.30m];

        foreach (var rp in rsiPeriods)
        foreach (var rb in buyThresholds)
        foreach (var rs in sellThresholds)
        foreach (var ps in posSizes)
        {
            if (rb >= rs) continue;
            var eq = RunStrategy(btc, candleFng, startingCapital,
                rp, rb, rs, ps, allowShort: true,
                useSentiment: false, fngBuyMax: 100, fngSellMin: 0);
            priceOnlyResults.Add(eq);
            if (eq > bestPriceOnly) { bestPriceOnly = eq; bestPriceParams = (rp, rb, rs, ps); }
        }

        PrintSweepResults("PRICE ONLY", startingCapital, priceOnlyResults, bestPriceOnly, bestPriceParams);

        // Run best price-only with monthly detail
        var (poEquity, poTrades, poMonthly) = RunStrategyDetailed(btc, candleFng, startingCapital,
            bestPriceParams.rsiP, bestPriceParams.rsiB, bestPriceParams.rsiS, bestPriceParams.pos,
            allowShort: true, useSentiment: false, fngBuyMax: 100, fngSellMin: 0);
        PrintMonthly("PRICE ONLY (best)", startingCapital, poEquity, poTrades, poMonthly);

        // ═══ TEST B: PRICE + FEAR & GREED FILTER ═══
        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("TEST B: PRICE + SENTIMENT (RSI + Fear & Greed filter)");
        Console.WriteLine("Only buy when market is fearful. Only short when greedy.");
        Console.WriteLine("══════════════════════════════════════════════════════════════");

        decimal bestSentiment = 0;
        (int rsiP, decimal rsiB, decimal rsiS, decimal pos, int fngBuy, int fngSell) bestSentParams = default;
        var sentimentResults = new List<decimal>();

        int[] fngBuyMaxValues = [20, 30, 40, 50];   // only buy when F&G below this
        int[] fngSellMinValues = [50, 60, 70, 80];   // only short when F&G above this

        foreach (var rp in rsiPeriods)
        foreach (var rb in buyThresholds)
        foreach (var rs in sellThresholds)
        foreach (var ps in posSizes)
        foreach (var fb in fngBuyMaxValues)
        foreach (var fs in fngSellMinValues)
        {
            if (rb >= rs) continue;
            var eq = RunStrategy(btc, candleFng, startingCapital,
                rp, rb, rs, ps, allowShort: true,
                useSentiment: true, fngBuyMax: fb, fngSellMin: fs);
            sentimentResults.Add(eq);
            if (eq > bestSentiment)
            {
                bestSentiment = eq;
                bestSentParams = (rp, rb, rs, ps, fb, fs);
            }
        }

        Console.WriteLine($"\n  Tested {sentimentResults.Count:N0} parameter combos (price + sentiment).");
        Console.WriteLine($"  Evolution found: RSI({bestSentParams.rsiP}), Buy<{bestSentParams.rsiB} when F&G<{bestSentParams.fngBuy}, ");
        Console.WriteLine($"                   Sell>{bestSentParams.rsiS} when F&G>{bestSentParams.fngSell}, Size={bestSentParams.pos * 100:0}%");

        var profitable = sentimentResults.Count(r => r > startingCapital);
        var avgRet = sentimentResults.Average(r => (double)(r - startingCapital) / (double)startingCapital) * 100;
        Console.WriteLine($"\n  Of {sentimentResults.Count:N0} combos:");
        Console.WriteLine($"  ├─ {profitable} profitable ({(decimal)profitable / sentimentResults.Count * 100:0.0}%)");
        Console.WriteLine($"  ├─ Average return: {avgRet:+0.00;-0.00}%");
        Console.WriteLine($"  ├─ Best:  {(double)(sentimentResults.Max() - startingCapital) / (double)startingCapital * 100:+0.00}%");
        Console.WriteLine($"  └─ Worst: {(double)(sentimentResults.Min() - startingCapital) / (double)startingCapital * 100:+0.00;-0.00}%");

        var (sEquity, sTrades, sMonthly) = RunStrategyDetailed(btc, candleFng, startingCapital,
            bestSentParams.rsiP, bestSentParams.rsiB, bestSentParams.rsiS, bestSentParams.pos,
            allowShort: true, useSentiment: true,
            fngBuyMax: bestSentParams.fngBuy, fngSellMin: bestSentParams.fngSell);
        PrintMonthly("PRICE + SENTIMENT (best)", startingCapital, sEquity, sTrades, sMonthly);

        // ═══ FINAL COMPARISON ═══
        decimal bhBtc = startingCapital * btc.Last().Close / btc.First().Close;
        decimal improvement = bestSentiment > bestPriceOnly
            ? (bestSentiment - bestPriceOnly) / (bestPriceOnly - startingCapital) * 100
            : 0;

        Console.WriteLine("\n\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║              PRICE-ONLY  vs  PRICE + SENTIMENT              ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.Write("║  Buy & Hold:             ");
        Console.Write($"${bhBtc,-10:N2}");
        Console.WriteLine($" ({(bhBtc - startingCapital) / startingCapital * 100:+0.0;-0.0}%)         ║");
        Console.Write("║  Price only (best):      ");
        Console.Write($"${bestPriceOnly,-10:N2}");
        Console.WriteLine($" ({(bestPriceOnly - startingCapital) / startingCapital * 100:+0.0;-0.0}%)         ║");
        Console.Write("║  Price + Sentiment:      ");
        Console.Write($"${bestSentiment,-10:N2}");
        Console.WriteLine($" ({(bestSentiment - startingCapital) / startingCapital * 100:+0.0;-0.0}%)         ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Sentiment uplift: ${bestSentiment - bestPriceOnly:+#,##0.00;-#,##0.00} additional profit         ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");

        Console.WriteLine("\n  WHY SENTIMENT DATA MATTERS:");
        Console.WriteLine("  ─────────────────────────────────────────────");
        Console.WriteLine("  This test added just ONE extra signal (Fear & Greed Index).");
        Console.WriteLine("  The Seed's agents could consume 10-20+ signals simultaneously:");
        Console.WriteLine();
        Console.WriteLine("  Available free/cheap data sources:");
        Console.WriteLine("  ├─ Fear & Greed Index (free, no key)");
        Console.WriteLine("  ├─ Funding rates across exchanges (CoinGlass, free tier)");
        Console.WriteLine("  ├─ Open interest / liquidation data (CoinGlass)");
        Console.WriteLine("  ├─ Exchange inflow/outflow (on-chain, Blockfrost for ADA)");
        Console.WriteLine("  ├─ Social volume + sentiment (LunarCrush free tier)");
        Console.WriteLine("  ├─ Google Trends search interest (free)");
        Console.WriteLine("  ├─ Whale wallet movements (Whale Alert free tier)");
        Console.WriteLine("  ├─ Bitcoin dominance ratio (free)");
        Console.WriteLine("  ├─ Stablecoin supply changes (on-chain)");
        Console.WriteLine("  └─ Correlation with stock market (Yahoo Finance, free)");
        Console.WriteLine();
        Console.WriteLine("  Each additional signal = more information = better decisions.");
        Console.WriteLine("  The CPPN brain takes ALL of these as input floats.");
        Console.WriteLine("  Evolution discovers which signals matter and how they interact.");
        Console.WriteLine("  No human codes the rules. Natural selection finds them.");
    }

    static decimal RunStrategy(List<Candle> candles, int[] fng, decimal capital,
        int rsiPeriod, decimal rsiBuy, decimal rsiSell, decimal posSize,
        bool allowShort, bool useSentiment, int fngBuyMax, int fngSellMin)
    {
        var closes = candles.Select(c => c.Close).ToArray();
        var rsi = CalcRsi(closes, rsiPeriod);

        decimal cash = capital;
        decimal position = 0;
        decimal entryPrice = 0;

        for (int i = rsiPeriod + 1; i < candles.Count; i++)
        {
            decimal price = candles[i].Close;
            bool sentimentBuyOk = !useSentiment || fng[i] <= fngBuyMax;
            bool sentimentSellOk = !useSentiment || fng[i] >= fngSellMin;

            if (rsi[i] < rsiBuy && sentimentBuyOk && position <= 0)
            {
                if (position < 0)
                {
                    decimal closeCost = Math.Abs(position) * price;
                    cash -= closeCost * (1 + FEE_RATE);
                    position = 0;
                }
                decimal equity = cash + position * price;
                decimal tradeValue = equity * posSize;
                if (tradeValue > 0 && cash > tradeValue)
                {
                    decimal btcBought = tradeValue * (1 - FEE_RATE) / price;
                    position += btcBought;
                    cash -= tradeValue;
                    entryPrice = price;
                }
            }
            else if (rsi[i] > rsiSell && position >= 0)
            {
                if (position > 0)
                {
                    cash += position * price * (1 - FEE_RATE);
                    position = 0;
                }
                if (allowShort && sentimentSellOk)
                {
                    decimal equity = cash;
                    decimal shortValue = equity * posSize;
                    if (shortValue > 0)
                    {
                        decimal btcShorted = shortValue / price;
                        position -= btcShorted;
                        cash += shortValue * (1 - FEE_RATE);
                        entryPrice = price;
                    }
                }
            }
        }

        if (position > 0) cash += position * candles.Last().Close * (1 - FEE_RATE);
        else if (position < 0) cash -= Math.Abs(position) * candles.Last().Close * (1 + FEE_RATE);

        return cash;
    }

    static (decimal equity, List<Trade> trades, Dictionary<string, (decimal start, decimal end)> monthly)
        RunStrategyDetailed(List<Candle> candles, int[] fng, decimal capital,
        int rsiPeriod, decimal rsiBuy, decimal rsiSell, decimal posSize,
        bool allowShort, bool useSentiment, int fngBuyMax, int fngSellMin)
    {
        var closes = candles.Select(c => c.Close).ToArray();
        var rsi = CalcRsi(closes, rsiPeriod);
        var trades = new List<Trade>();
        var monthly = new Dictionary<string, (decimal start, decimal end)>();

        decimal cash = capital;
        decimal position = 0;
        decimal entryPrice = 0;

        for (int i = rsiPeriod + 1; i < candles.Count; i++)
        {
            decimal price = candles[i].Close;
            decimal equity = cash + position * price;
            string mk = DateTimeOffset.FromUnixTimeMilliseconds(candles[i].OpenTime).ToString("yyyy-MM");
            if (!monthly.ContainsKey(mk)) monthly[mk] = (equity, equity);
            monthly[mk] = (monthly[mk].start, equity);

            bool sentimentBuyOk = !useSentiment || fng[i] <= fngBuyMax;
            bool sentimentSellOk = !useSentiment || fng[i] >= fngSellMin;

            if (rsi[i] < rsiBuy && sentimentBuyOk && position <= 0)
            {
                if (position < 0)
                {
                    cash -= Math.Abs(position) * price * (1 + FEE_RATE);
                    decimal pnl = (entryPrice - price) / entryPrice;
                    trades.Add(new Trade(entryPrice, price, pnl, "SHORT"));
                    position = 0;
                }
                decimal eq = cash + position * price;
                decimal tradeValue = eq * posSize;
                if (tradeValue > 0 && cash > tradeValue)
                {
                    decimal btcBought = tradeValue * (1 - FEE_RATE) / price;
                    position += btcBought;
                    cash -= tradeValue;
                    entryPrice = price;
                }
            }
            else if (rsi[i] > rsiSell && position >= 0)
            {
                if (position > 0)
                {
                    cash += position * price * (1 - FEE_RATE);
                    decimal pnl = (price - entryPrice) / entryPrice;
                    trades.Add(new Trade(entryPrice, price, pnl, "LONG"));
                    position = 0;
                }
                if (allowShort && sentimentSellOk)
                {
                    decimal equity2 = cash;
                    decimal shortValue = equity2 * posSize;
                    if (shortValue > 0)
                    {
                        decimal btcShorted = shortValue / price;
                        position -= btcShorted;
                        cash += shortValue * (1 - FEE_RATE);
                        entryPrice = price;
                    }
                }
            }
        }

        if (position > 0)
        {
            cash += position * candles.Last().Close * (1 - FEE_RATE);
            trades.Add(new Trade(entryPrice, candles.Last().Close,
                (candles.Last().Close - entryPrice) / entryPrice, "LONG"));
        }
        else if (position < 0)
        {
            cash -= Math.Abs(position) * candles.Last().Close * (1 + FEE_RATE);
            trades.Add(new Trade(entryPrice, candles.Last().Close,
                (entryPrice - candles.Last().Close) / entryPrice, "SHORT"));
        }

        return (cash, trades, monthly);
    }

    static void PrintSweepResults(string name, decimal capital, List<decimal> results, decimal best,
        (int rsiP, decimal rsiB, decimal rsiS, decimal pos) bestParams)
    {
        var profitable = results.Count(r => r > capital);
        var avgRet = results.Average(r => (double)(r - capital) / (double)capital) * 100;

        Console.WriteLine($"\n  Tested {results.Count} parameter combinations.");
        Console.WriteLine($"  Best params: RSI({bestParams.rsiP}), Buy<{bestParams.rsiB}, Sell>{bestParams.rsiS}, Size={bestParams.pos * 100:0}%");
        Console.WriteLine($"\n  Of {results.Count} combos:");
        Console.WriteLine($"  ├─ {profitable} profitable ({(decimal)profitable / results.Count * 100:0.0}%)");
        Console.WriteLine($"  ├─ Average return: {avgRet:+0.00;-0.00}%");
        Console.WriteLine($"  ├─ Best:  {(double)(results.Max() - capital) / (double)capital * 100:+0.00}%");
        Console.WriteLine($"  └─ Worst: {(double)(results.Min() - capital) / (double)capital * 100:+0.00;-0.00}%");
    }

    static void PrintMonthly(string name, decimal capital, decimal finalEquity, List<Trade> trades,
        Dictionary<string, (decimal start, decimal end)> monthly)
    {
        decimal ret = (finalEquity - capital) / capital;
        int wins = trades.Count(t => t.Pnl > 0);
        int losses = trades.Count(t => t.Pnl <= 0);

        Console.WriteLine($"\n  {name}");
        Console.WriteLine($"  ────────────────────────────────────────");
        Console.WriteLine($"  ${capital:N2} → ${finalEquity:N2}  P&L: ${finalEquity - capital:+#,##0.00;-#,##0.00} ({ret * 100:+0.00;-0.00}%)");
        Console.WriteLine($"  Trades: {trades.Count}  Win: {(trades.Count > 0 ? (decimal)wins / trades.Count * 100 : 0):0.0}%");

        Console.WriteLine($"\n  Monthly:");
        int profMonths = 0;
        foreach (var (month, (start, end)) in monthly.OrderBy(kv => kv.Key))
        {
            decimal mPnl = end - start;
            decimal mRet = start != 0 ? mPnl / start : 0;
            string bar = mPnl >= 0
                ? new string('█', Math.Min((int)(mRet * 400), 25))
                : new string('░', Math.Min((int)(Math.Abs(mRet) * 400), 25));
            Console.WriteLine($"  {month}  ${mPnl:+#,##0.00;-#,##0.00}\t{mRet * 100:+0.00;-0.00}%\t{(mPnl >= 0 ? "+" : "-")}{bar}");
            if (mPnl > 0) profMonths++;
        }
        Console.WriteLine($"  Profitable: {profMonths}/{monthly.Count} months");
    }

    static async Task<Dictionary<DateTime, int>> FetchFearGreed(HttpClient client)
    {
        var result = new Dictionary<DateTime, int>();
        try
        {
            var url = "https://api.alternative.me/fng/?limit=0&format=json";
            var json = await client.GetStringAsync(url);
            var doc = JsonSerializer.Deserialize<JsonElement>(json);

            if (doc.TryGetProperty("data", out var data))
            {
                foreach (var item in data.EnumerateArray())
                {
                    var ts = long.Parse(item.GetProperty("timestamp").GetString()!);
                    var val = int.Parse(item.GetProperty("value").GetString()!);
                    var date = DateTimeOffset.FromUnixTimeSeconds(ts).UtcDateTime.Date;
                    result.TryAdd(date, val);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n  Warning: F&G fetch failed: {ex.Message}");
        }
        return result;
    }

    static decimal[] CalcRsi(decimal[] closes, int period)
    {
        var rsi = new decimal[closes.Length];
        decimal avgGain = 0, avgLoss = 0;
        for (int i = 1; i <= period; i++)
        {
            decimal change = closes[i] - closes[i - 1];
            if (change > 0) avgGain += change; else avgLoss += Math.Abs(change);
        }
        avgGain /= period;
        avgLoss /= period;
        rsi[period] = avgLoss == 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
        for (int i = period + 1; i < closes.Length; i++)
        {
            decimal change = closes[i] - closes[i - 1];
            avgGain = (avgGain * (period - 1) + (change > 0 ? change : 0)) / period;
            avgLoss = (avgLoss * (period - 1) + (change < 0 ? Math.Abs(change) : 0)) / period;
            rsi[i] = avgLoss == 0 ? 100 : 100 - 100 / (1 + avgGain / avgLoss);
        }
        return rsi;
    }

    static async Task<List<Candle>> FetchCandles(HttpClient client, string symbol, int chunks)
    {
        var candles = new List<Candle>();
        long endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 0; i < chunks; i++)
        {
            var url = $"https://api.binance.com/api/v3/klines?symbol={symbol}&interval=1h&limit=1000&endTime={endTime}";
            var json = await client.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            foreach (var c in data.EnumerateArray())
            {
                candles.Add(new Candle
                {
                    OpenTime = c[0].GetInt64(),
                    Open = decimal.Parse(c[1].GetString()!, CultureInfo.InvariantCulture),
                    High = decimal.Parse(c[2].GetString()!, CultureInfo.InvariantCulture),
                    Low = decimal.Parse(c[3].GetString()!, CultureInfo.InvariantCulture),
                    Close = decimal.Parse(c[4].GetString()!, CultureInfo.InvariantCulture),
                    Volume = decimal.Parse(c[5].GetString()!, CultureInfo.InvariantCulture)
                });
            }
            endTime = candles.Min(c => c.OpenTime) - 1;
            await Task.Delay(300);
        }
        return candles.OrderBy(c => c.OpenTime).DistinctBy(c => c.OpenTime).ToList();
    }
}
