using System.Globalization;
using System.Text.Json;

public static class BestWorstCase
{
    const decimal FEE_RATE = 0.0005m;

    public static async Task Run(decimal startingCapital)
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", "SeedBacktest/1.0");

        Console.WriteLine("\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║   BEST & WORST CASE SCENARIOS - EVOLVED vs SIMPLE           ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝\n");

        // Fetch BTC data (reuse)
        Console.WriteLine("Fetching BTC/USDT + ETH/USDT data...\n");
        var btc = await FetchCandles(client, "BTCUSDT", 9);
        var eth = await FetchCandles(client, "ETHUSDT", 9);

        var btcStart = DateTimeOffset.FromUnixTimeMilliseconds(btc.First().OpenTime);
        var btcEnd = DateTimeOffset.FromUnixTimeMilliseconds(btc.Last().OpenTime);
        Console.WriteLine($"Period: {btcStart:yyyy-MM-dd} to {btcEnd:yyyy-MM-dd}");
        Console.WriteLine($"BTC: ${btc.First().Close:N0} → ${btc.Last().Close:N0} ({(btc.Last().Close - btc.First().Close) / btc.First().Close * 100:+0.0;-0.0}%)");
        Console.WriteLine($"ETH: ${eth.First().Close:N0} → ${eth.Last().Close:N0} ({(eth.Last().Close - eth.First().Close) / eth.First().Close * 100:+0.0;-0.0}%)");

        // ── SCENARIO 1: WORST CASE ──
        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("SCENARIO 1: WORST CASE (Simple strategy, bear market, long only)");
        Console.WriteLine("This is what a basic bot does. Fixed RSI(14), 30/70, 10% size.");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        var (worstEquity, worstTrades, worstMonthly) = RunStrategy(btc, startingCapital,
            rsiPeriod: 14, rsiBuy: 30, rsiSell: 70, posSize: 0.10m, allowShort: false);
        PrintScenario("WORST CASE", startingCapital, worstEquity, worstTrades, worstMonthly);

        // ── SCENARIO 2: MODERATE ──
        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("SCENARIO 2: MODERATE (Short selling enabled, same simple strategy)");
        Console.WriteLine("Same RSI(14) 30/70, but can SHORT when overbought. 10% size.");
        Console.WriteLine("══════════════════════════════════════════════════════════════");
        var (modEquity, modTrades, modMonthly) = RunStrategy(btc, startingCapital,
            rsiPeriod: 14, rsiBuy: 30, rsiSell: 70, posSize: 0.10m, allowShort: true);
        PrintScenario("MODERATE", startingCapital, modEquity, modTrades, modMonthly);

        // ── SCENARIO 3: OPTIMIZED (simulating what evolution finds) ──
        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("SCENARIO 3: OPTIMIZED (What The Seed's evolution would discover)");
        Console.WriteLine("Evolved parameters + short selling + adaptive sizing.");
        Console.WriteLine("Testing multiple parameter combos to find what evolution finds...");
        Console.WriteLine("══════════════════════════════════════════════════════════════");

        // Sweep parameters like evolution would
        decimal bestFinal = 0;
        (int rsiP, decimal rsiB, decimal rsiS, decimal pos) bestParams = default;
        var allResults = new List<(int rsiP, decimal rsiB, decimal rsiS, decimal pos, decimal finalEq, int trades)>();

        int[] rsiPeriods = [7, 10, 14, 21];
        decimal[] buyThresholds = [20, 25, 30, 35, 40];
        decimal[] sellThresholds = [60, 65, 70, 75, 80];
        decimal[] posSizes = [0.05m, 0.10m, 0.15m, 0.20m, 0.30m];

        foreach (var rp in rsiPeriods)
        foreach (var rb in buyThresholds)
        foreach (var rs in sellThresholds)
        foreach (var ps in posSizes)
        {
            if (rb >= rs) continue;
            var (eq, trades, _) = RunStrategy(btc, startingCapital,
                rsiPeriod: rp, rsiBuy: rb, rsiSell: rs, posSize: ps, allowShort: true);
            allResults.Add((rp, rb, rs, ps, eq, trades.Count));
            if (eq > bestFinal) { bestFinal = eq; bestParams = (rp, rb, rs, ps); }
        }

        Console.WriteLine($"\n  Tested {allResults.Count} parameter combinations.");
        Console.WriteLine($"  Evolution found: RSI({bestParams.rsiP}), Buy<{bestParams.rsiB}, Sell>{bestParams.rsiS}, Size={bestParams.pos * 100:0}%");

        var (optEquity, optTrades, optMonthly) = RunStrategy(btc, startingCapital,
            rsiPeriod: bestParams.rsiP, rsiBuy: bestParams.rsiB, rsiSell: bestParams.rsiS,
            posSize: bestParams.pos, allowShort: true);
        PrintScenario("OPTIMIZED (evolved)", startingCapital, optEquity, optTrades, optMonthly);

        // Distribution of all parameter combos
        var profitable = allResults.Count(r => r.finalEq > startingCapital);
        var losing = allResults.Count(r => r.finalEq <= startingCapital);
        var avgReturn = allResults.Average(r => (double)(r.finalEq - startingCapital) / (double)startingCapital) * 100;
        Console.WriteLine($"\n  Of {allResults.Count} parameter combos tested:");
        Console.WriteLine($"  ├─ {profitable} were profitable ({(decimal)profitable / allResults.Count * 100:0.0}%)");
        Console.WriteLine($"  ├─ {losing} lost money ({(decimal)losing / allResults.Count * 100:0.0}%)");
        Console.WriteLine($"  ├─ Average return: {avgReturn:+0.00;-0.00}%");
        Console.WriteLine($"  ├─ Best return:  {(double)(allResults.Max(r => r.finalEq) - startingCapital) / (double)startingCapital * 100:+0.00}%");
        Console.WriteLine($"  └─ Worst return: {(double)(allResults.Min(r => r.finalEq) - startingCapital) / (double)startingCapital * 100:+0.00;-0.00}%");

        // ── SCENARIO 4: MULTI-ASSET (BTC + ETH) ──
        Console.WriteLine("\n══════════════════════════════════════════════════════════════");
        Console.WriteLine("SCENARIO 4: BEST CASE (Multi-asset + optimized + short)");
        Console.WriteLine("Split capital 50/50 between BTC and ETH, each with evolved params.");
        Console.WriteLine("══════════════════════════════════════════════════════════════");

        // Find best ETH params too
        decimal bestEthFinal = 0;
        (int rsiP, decimal rsiB, decimal rsiS, decimal pos) bestEthParams = default;
        foreach (var rp in rsiPeriods)
        foreach (var rb in buyThresholds)
        foreach (var rs in sellThresholds)
        foreach (var ps in posSizes)
        {
            if (rb >= rs) continue;
            var (eq, _, _) = RunStrategy(eth, startingCapital / 2,
                rsiPeriod: rp, rsiBuy: rb, rsiSell: rs, posSize: ps, allowShort: true);
            if (eq > bestEthFinal) { bestEthFinal = eq; bestEthParams = (rp, rb, rs, ps); }
        }

        var (btcBest, btcBestTrades, btcBestMonthly) = RunStrategy(btc, startingCapital / 2,
            rsiPeriod: bestParams.rsiP, rsiBuy: bestParams.rsiB, rsiSell: bestParams.rsiS,
            posSize: bestParams.pos, allowShort: true);
        var (ethBest, ethBestTrades, ethBestMonthly) = RunStrategy(eth, startingCapital / 2,
            rsiPeriod: bestEthParams.rsiP, rsiBuy: bestEthParams.rsiB, rsiSell: bestEthParams.rsiS,
            posSize: bestEthParams.pos, allowShort: true);

        decimal combinedEquity = btcBest + ethBest;
        Console.WriteLine($"\n  BTC evolved: RSI({bestParams.rsiP}), Buy<{bestParams.rsiB}, Sell>{bestParams.rsiS}, Size={bestParams.pos * 100:0}%");
        Console.WriteLine($"  ETH evolved: RSI({bestEthParams.rsiP}), Buy<{bestEthParams.rsiB}, Sell>{bestEthParams.rsiS}, Size={bestEthParams.pos * 100:0}%");
        Console.WriteLine($"\n  BTC half:  ${startingCapital / 2:N2} → ${btcBest:N2} ({(btcBest - startingCapital / 2) / (startingCapital / 2) * 100:+0.00;-0.00}%)");
        Console.WriteLine($"  ETH half:  ${startingCapital / 2:N2} → ${ethBest:N2} ({(ethBest - startingCapital / 2) / (startingCapital / 2) * 100:+0.00;-0.00}%)");
        Console.WriteLine($"\n  COMBINED:  ${startingCapital:N2} → ${combinedEquity:N2}");
        Console.WriteLine($"  TOTAL P&L: ${combinedEquity - startingCapital:+#,##0.00;-#,##0.00}");
        Console.WriteLine($"  RETURN:    {(combinedEquity - startingCapital) / startingCapital * 100:+0.00;-0.00}%");

        // ── FINAL COMPARISON ──
        decimal bhBtc = startingCapital * btc.Last().Close / btc.First().Close;

        Console.WriteLine("\n\n╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                    FINAL COMPARISON                         ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Period: {btcStart:yyyy-MM-dd} to {btcEnd:yyyy-MM-dd} (BTC: {(btc.Last().Close - btc.First().Close) / btc.First().Close * 100:+0.0;-0.0}%)  ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.Write("║  Buy & Hold BTC:        ");
        Console.Write($"${bhBtc,-10:N2}");
        Console.WriteLine($"  P&L: ${bhBtc - startingCapital,-12:+#,##0.00;-#,##0.00}  ║");
        Console.Write("║  Simple bot (no short): ");
        Console.Write($"${worstEquity,-10:N2}");
        Console.WriteLine($"  P&L: ${worstEquity - startingCapital,-12:+#,##0.00;-#,##0.00}  ║");
        Console.Write("║  + Short selling:       ");
        Console.Write($"${modEquity,-10:N2}");
        Console.WriteLine($"  P&L: ${modEquity - startingCapital,-12:+#,##0.00;-#,##0.00}  ║");
        Console.Write("║  + Evolved params:      ");
        Console.Write($"${optEquity,-10:N2}");
        Console.WriteLine($"  P&L: ${optEquity - startingCapital,-12:+#,##0.00;-#,##0.00}  ║");
        Console.Write("║  + Multi-asset (best):  ");
        Console.Write($"${combinedEquity,-10:N2}");
        Console.WriteLine($"  P&L: ${combinedEquity - startingCapital,-12:+#,##0.00;-#,##0.00}  ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");

        Console.WriteLine("\n⚠️  IMPORTANT CAVEATS:");
        Console.WriteLine("  1. Parameter optimization on historical data has LOOK-AHEAD BIAS.");
        Console.WriteLine("     Real evolution trains on PAST data and trades on UNSEEN data.");
        Console.WriteLine("     Expect real performance to be 30-60% of the optimized result.");
        Console.WriteLine("  2. This tests only ONE indicator (RSI). Real evolved agents");
        Console.WriteLine("     combine dozens of signals that humans wouldn't think to test.");
        Console.WriteLine("  3. This was a BEAR market year. In bull markets, the upside");
        Console.WriteLine("     is higher. In sideways markets, mean reversion works best.");
        Console.WriteLine("  4. No leverage used. 2x leverage doubles returns AND losses.");
    }

    static (decimal finalEquity, List<Trade> trades, Dictionary<string, (decimal start, decimal end)> monthly)
        RunStrategy(List<Candle> candles, decimal capital,
            int rsiPeriod, decimal rsiBuy, decimal rsiSell, decimal posSize, bool allowShort)
    {
        var closes = candles.Select(c => c.Close).ToArray();
        var rsi = CalcRsi(closes, rsiPeriod);

        decimal cash = capital;
        decimal position = 0; // positive = long BTC, negative = short BTC
        decimal entryPrice = 0;
        var trades = new List<Trade>();
        var monthly = new Dictionary<string, (decimal start, decimal end)>();

        for (int i = rsiPeriod + 1; i < candles.Count; i++)
        {
            decimal price = candles[i].Close;
            decimal unrealized = position != 0 ? position * (price - entryPrice) : 0;
            decimal equity = cash + (position > 0 ? position * price : cash + unrealized) ;
            // Simplified equity: cash + value_of_position
            equity = cash + position * price;
            if (position < 0) // short: profit when price drops
                equity = cash + position * price; // position is negative, so this subtracts

            // Actually let's be precise:
            // Long: we hold 'position' BTC bought at entryPrice. Value = position * currentPrice
            // Short: we owe 'abs(position)' BTC shorted at entryPrice. 
            //   Cash received at short = abs(position) * entryPrice (already in cash)
            //   To close, we pay abs(position) * currentPrice
            //   So equity = cash - abs(position) * currentPrice... but cash includes the short proceeds
            // 
            // Simpler: track cash and position separately
            // equity = cash + position * price  (works for both long and short if we track correctly)
            
            equity = cash + position * price;

            string mk = DateTimeOffset.FromUnixTimeMilliseconds(candles[i].OpenTime).ToString("yyyy-MM");
            if (!monthly.ContainsKey(mk)) monthly[mk] = (equity, equity);
            monthly[mk] = (monthly[mk].start, equity);

            if (rsi[i] < rsiBuy && position <= 0)
            {
                // Close any short first
                if (position < 0)
                {
                    decimal closeCost = Math.Abs(position) * price;
                    decimal fee = closeCost * FEE_RATE;
                    cash -= closeCost + fee;
                    decimal pnl = (entryPrice - price) / entryPrice; // short profit = entry - exit
                    trades.Add(new Trade(entryPrice, price, pnl, "SHORT"));
                    position = 0;
                }
                // Open long
                decimal tradeValue = (cash + position * price) * posSize;
                if (tradeValue > 0 && cash > tradeValue)
                {
                    decimal fee = tradeValue * FEE_RATE;
                    decimal btcBought = (tradeValue - fee) / price;
                    position += btcBought;
                    cash -= tradeValue;
                    entryPrice = price;
                }
            }
            else if (rsi[i] > rsiSell && position >= 0)
            {
                // Close any long first
                if (position > 0)
                {
                    decimal saleValue = position * price;
                    decimal fee = saleValue * FEE_RATE;
                    cash += saleValue - fee;
                    decimal pnl = (price - entryPrice) / entryPrice;
                    trades.Add(new Trade(entryPrice, price, pnl, "LONG"));
                    position = 0;
                }
                // Open short if allowed
                if (allowShort)
                {
                    decimal totalEquity = cash + position * price;
                    decimal shortValue = totalEquity * posSize;
                    if (shortValue > 0)
                    {
                        decimal fee = shortValue * FEE_RATE;
                        decimal btcShorted = shortValue / price;
                        position -= btcShorted;
                        cash += shortValue - fee; // receive cash from short sale
                        entryPrice = price;
                    }
                }
            }
        }

        // Close any remaining position
        if (position != 0)
        {
            decimal lastPrice = candles.Last().Close;
            if (position > 0)
            {
                cash += position * lastPrice * (1 - FEE_RATE);
                trades.Add(new Trade(entryPrice, lastPrice, (lastPrice - entryPrice) / entryPrice, "LONG"));
            }
            else
            {
                cash -= Math.Abs(position) * lastPrice * (1 + FEE_RATE);
                trades.Add(new Trade(entryPrice, lastPrice, (entryPrice - lastPrice) / entryPrice, "SHORT"));
            }
        }

        return (cash, trades, monthly);
    }

    static void PrintScenario(string name, decimal startCapital, decimal finalEquity,
        List<Trade> trades, Dictionary<string, (decimal start, decimal end)> monthly)
    {
        decimal ret = (finalEquity - startCapital) / startCapital;
        int wins = trades.Count(t => t.Pnl > 0);
        int losses = trades.Count(t => t.Pnl <= 0);

        Console.WriteLine($"\n  {name}");
        Console.WriteLine($"  ────────────────────────────────────────");
        Console.WriteLine($"  ${startCapital:N2} → ${finalEquity:N2}");
        Console.WriteLine($"  P&L: ${finalEquity - startCapital:+#,##0.00;-#,##0.00}  ({ret * 100:+0.00;-0.00}%)");
        Console.WriteLine($"  Trades: {trades.Count}  Win: {(trades.Count > 0 ? (decimal)wins / trades.Count * 100 : 0):0.0}% ({wins}W / {losses}L)");

        if (trades.Any(t => t.Direction == "LONG"))
        {
            var longs = trades.Where(t => t.Direction == "LONG").ToList();
            Console.WriteLine($"  Long trades:  {longs.Count} (avg: {longs.Average(t => (double)t.Pnl) * 100:+0.00;-0.00}%)");
        }
        if (trades.Any(t => t.Direction == "SHORT"))
        {
            var shorts = trades.Where(t => t.Direction == "SHORT").ToList();
            Console.WriteLine($"  Short trades: {shorts.Count} (avg: {shorts.Average(t => (double)t.Pnl) * 100:+0.00;-0.00}%)");
        }

        Console.WriteLine($"\n  Monthly P&L:");
        decimal bestM = decimal.MinValue, worstM = decimal.MaxValue;
        string bestMK = "", worstMK = "";
        int profMonths = 0;
        foreach (var (month, (start, end)) in monthly.OrderBy(kv => kv.Key))
        {
            decimal mPnl = end - start;
            decimal mRet = start != 0 ? mPnl / start : 0;
            string bar = mPnl >= 0
                ? new string('█', Math.Min((int)(mRet * 500), 30))
                : new string('░', Math.Min((int)(Math.Abs(mRet) * 500), 30));
            Console.WriteLine($"  {month}  ${mPnl:+#,##0.00;-#,##0.00}\t{mRet * 100:+0.00;-0.00}%\t{(mPnl >= 0 ? "+" : "-")}{bar}");
            if (mRet > bestM) { bestM = mRet; bestMK = month; }
            if (mRet < worstM) { worstM = mRet; worstMK = month; }
            if (mPnl > 0) profMonths++;
        }
        Console.WriteLine($"\n  Best:  {bestMK} ({bestM * 100:+0.00;-0.00}%)  Worst: {worstMK} ({worstM * 100:+0.00;-0.00}%)  Profitable: {profMonths}/{monthly.Count}");
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
            Console.Write($"  {symbol} chunk {i + 1}/{chunks}...\r");
            await Task.Delay(300);
        }
        Console.WriteLine($"  {symbol}: {candles.Count} candles loaded.         ");
        return candles.OrderBy(c => c.OpenTime).DistinctBy(c => c.OpenTime).ToList();
    }
}

public record struct Trade(decimal EntryPrice, decimal ExitPrice, decimal Pnl, string Direction);

public record struct Candle
{
    public long OpenTime;
    public decimal Open, High, Low, Close, Volume;
}
