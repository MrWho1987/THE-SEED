using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Backtest;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Market.Trading;

namespace Seed.Market.Tests;

/// <summary>
/// Diagnostic tests that load saved genomes and analyze their actual behavior.
/// These run INDEPENDENTLY of training to answer the "unknowns" about what
/// the evolved agents actually do.
/// </summary>
public class GenomeDiagnosticTests
{
    private const string GenomePath = @"C:\Users\ederg\source\repos\THE-SEED\output_phase2\checkpoints\best_val_gen_0750.json";

    /// <summary>
    /// Unknown 1: Are outputs 6-10 being used by the best genome?
    /// Loads the gen 750 validation-best genome, runs it through synthetic data,
    /// and reports per-output mean/std + close reason histogram.
    /// </summary>
    [Fact]
    public void GenomeDiagnostic_OutputUsageAndCloseReasons()
    {
        if (!File.Exists(GenomePath))
        {
            // Skip if genome not available (CI environments)
            return;
        }

        var json = File.ReadAllText(GenomePath);
        var genome = SeedGenome.FromJson(json);

        var config = MarketConfig.Default with
        {
            InitialCapital = 10_000m,
            MaxPositionPct = 0.08m,
            MaxLeverage = 125f,
            StopLossPct = 0.02m,
            CandleInterval = "15m",
        };

        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var budget = MarketEvaluator.MarketBrainBudget with
        {
            HiddenWidth = genome.Dev.SubstrateWidth,
            HiddenHeight = genome.Dev.SubstrateHeight,
            HiddenLayers = genome.Dev.SubstrateLayers
        };
        var devCtx = new DevelopmentContext(42, 0);
        var graph = dev.CompileGraph(genome, budget, devCtx,
            MarketEvaluator.SignalCategoryMap, MarketEvaluator.RegimeStart, MarketEvaluator.RegimeEnd);

        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
        var trader = new PaperTrader(config);
        var agent = new MarketAgent(genome.GenomeId, brain, trader,
            maxLeverage: config.MaxLeverage,
            explicitExitBonus: config.ExplicitExitBonus,
            peakExitBonus: config.PeakExitBonus);

        // Load REAL market data from cache (last 2000 bars = validation-like window)
        var cachePath = @"C:\Users\ederg\source\repos\THE-SEED\pipeline_shared_cache\BTCUSDT_20221027_20260415_15m.jsonl";
        SignalSnapshot[] snapshots;
        float[] prices, volumes, funding;
        if (File.Exists(cachePath))
        {
            var allCandles = LoadCandlesFromCache(cachePath);
            // Use last 8000 bars (~83 days, similar to eval window size)
            var window = allCandles.Skip(Math.Max(0, allCandles.Length - 8000)).ToArray();
            (snapshots, prices, volumes, funding) = HistoricalDataStore.CandlesToSignals(window, enrichment: null, barsPerHour: 4);
        }
        else
        {
            // Fallback to synthetic if no cache
            (snapshots, prices, volumes, funding) = BuildSyntheticData(500, seed: 42);
        }

        // Run agent through all ticks
        for (int t = 0; t < snapshots.Length; t++)
        {
            float rawP = prices[t];
            if (float.IsNaN(rawP) || float.IsInfinity(rawP) || rawP <= 0f) continue;
            decimal price = (decimal)rawP;
            float elapsedHours = (float)t / config.BarsPerHour;
            var ctx = new TickContext(price, (decimal)volumes[t], funding[t], t, elapsedHours);
            agent.ProcessTick(snapshots[t], ctx);
            agent.Portfolio.RecordEquity(price);
        }

        // ── Report: Output Usage ──
        var obs = agent.GetOutputObservation();
        Console.WriteLine($"=== GENOME DIAGNOSTIC: {Path.GetFileName(GenomePath)} ===");
        Console.WriteLine($"Brain: {graph.NodeCount} nodes, {graph.EdgeCount} edges");
        Console.WriteLine($"Substrate: {genome.Dev.SubstrateWidth}x{genome.Dev.SubstrateHeight}x{genome.Dev.SubstrateLayers}");
        Console.WriteLine($"Ticks processed: {obs.TickCount}");
        Console.WriteLine();

        string[] outputNames = ["dir", "size", "urgency", "exit", "predict",
                                "leverage", "partialClose", "trailEnable",
                                "trailDist", "tpOffset", "slOverride"];

        Console.WriteLine("OUTPUT USAGE (raw brain values, pre-ActionInterpreter):");
        Console.WriteLine($"  {"Output",-14} {"Mean",8} {"Std",8} {"Active?",8}");
        for (int i = 0; i < Math.Min(obs.Means.Length, outputNames.Length); i++)
        {
            bool active = obs.Stds[i] > 0.05f;
            Console.WriteLine($"  {outputNames[i],-14} {obs.Means[i],8:F4} {obs.Stds[i],8:F4} {(active ? "YES" : "dormant"),8}");
        }

        // ── Report: Close Reasons ──
        Console.WriteLine();
        Console.WriteLine("CLOSE REASON HISTOGRAM:");
        var reasons = new int[Enum.GetValues<CloseReason>().Length];
        foreach (var trade in agent.Portfolio.TradeHistory)
            reasons[(int)trade.Reason]++;

        int totalTrades = agent.Portfolio.TradeHistory.Count;
        int brainDriven = 0;
        foreach (CloseReason r in Enum.GetValues<CloseReason>())
        {
            int count = reasons[(int)r];
            if (count > 0)
            {
                bool isBD = r.IsBrainDrivenExit();
                if (isBD) brainDriven += count;
                Console.WriteLine($"  {r,-18} {count,4} ({(float)count / Math.Max(1, totalTrades):P0}) {(isBD ? " ★ brain-driven" : "")}");
            }
        }
        Console.WriteLine($"  {"TOTAL",-18} {totalTrades,4}");
        Console.WriteLine($"  Brain-driven: {brainDriven}/{totalTrades} ({(float)brainDriven / Math.Max(1, totalTrades):P0})");

        // ── Report: Equity Curve Shape ──
        Console.WriteLine();
        var curve = agent.Portfolio.EquityCurve;
        if (curve.Count > 0)
        {
            float startEq = curve[0];
            float endEq = curve[^1];
            float peakEq = curve.Max();
            float minEq = curve.Min();
            float finalReturn = startEq > 0 ? (endEq - startEq) / startEq * 100f : 0f;

            // Count positive ticks (equity went up from previous bar)
            int upTicks = 0;
            for (int i = 1; i < curve.Count; i++)
                if (curve[i] > curve[i - 1]) upTicks++;
            float upTickRatio = (float)upTicks / Math.Max(1, curve.Count - 1);

            // Max drawdown
            float maxDD = 0f;
            float peak = curve[0];
            for (int i = 1; i < curve.Count; i++)
            {
                if (curve[i] > peak) peak = curve[i];
                float dd = (peak - curve[i]) / peak;
                if (dd > maxDD) maxDD = dd;
            }

            Console.WriteLine("EQUITY CURVE:");
            Console.WriteLine($"  Start: ${startEq:F2}  End: ${endEq:F2}  Peak: ${peakEq:F2}  Min: ${minEq:F2}");
            Console.WriteLine($"  Return: {finalReturn:F2}%  MaxDD: {maxDD:P1}  UpTickRatio: {upTickRatio:P1}");
            Console.WriteLine($"  Trades: {totalTrades}  WR: {agent.Portfolio.WinRate:P0}");

            // Sample equity at 10 equally-spaced points
            Console.Write("  Curve sample: ");
            for (int p = 0; p < 10; p++)
            {
                int idx = (int)((float)p / 9 * (curve.Count - 1));
                Console.Write($"{curve[idx]:F0} ");
            }
            Console.WriteLine();
        }

        // Basic assertions to ensure the genome is functional
        Assert.True(obs.TickCount > 0, "Genome must process ticks");
        Assert.Equal(11, obs.Means.Length);
    }

    /// <summary>
    /// Quick check: does the brain's direction output fire when fed randomized full-signal
    /// inputs? If yes, the genome CAN trade — it just needs the right signal patterns
    /// (which my diagnostic is missing because enrichment data isn't loaded).
    /// </summary>
    [Fact]
    public void GenomeDiagnostic_CanDirectionFire_WithRandomSignals()
    {
        if (!File.Exists(GenomePath)) return;

        var json = File.ReadAllText(GenomePath);
        var genome = SeedGenome.FromJson(json);

        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var budget = MarketEvaluator.MarketBrainBudget with
        {
            HiddenWidth = genome.Dev.SubstrateWidth,
            HiddenHeight = genome.Dev.SubstrateHeight,
            HiddenLayers = genome.Dev.SubstrateLayers
        };
        var graph = dev.CompileGraph(genome, budget, new DevelopmentContext(42, 0),
            MarketEvaluator.SignalCategoryMap, MarketEvaluator.RegimeStart, MarketEvaluator.RegimeEnd);
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);

        var rng = new Random(42);
        int dirFired = 0;
        int exitFired = 0;
        int totalTicks = 500;

        float maxAbsDir = 0f;
        for (int t = 0; t < totalTicks; t++)
        {
            var signals = new float[SignalIndex.Count];
            // Fill ALL 110 signals with random values in [-1, 1]
            for (int s = 0; s < signals.Length; s++)
                signals[s] = (float)(rng.NextDouble() * 2 - 1);

            var snap = new SignalSnapshot(signals, DateTimeOffset.UtcNow, t);
            var outputs = brain.Step(signals, new BrainStepContext(t));

            float dirVal = outputs.Length > 0 ? outputs[0] : 0f;
            float exitVal = outputs.Length > 3 ? outputs[3] : 0f;

            if (MathF.Abs(dirVal) > maxAbsDir) maxAbsDir = MathF.Abs(dirVal);
            if (MathF.Abs(dirVal) > 0.15f) dirFired++;
            if (exitVal > 0f) exitFired++;  // raw tanh > 0 means sigmoid > 0.5
        }

        Console.WriteLine($"=== DIRECTION FIRE TEST (random full signals) ===");
        Console.WriteLine($"  Dir fired: {dirFired}/{totalTicks} ({(float)dirFired / totalTicks:P1})");
        Console.WriteLine($"  Max |dir|: {maxAbsDir:F4} (needs > 0.15 to trade)");
        Console.WriteLine($"  Exit fired: {exitFired}/{totalTicks} ({(float)exitFired / totalTicks:P1})");

        // We just want to know if direction CAN fire, not assert it always does
        Console.WriteLine($"  Conclusion: direction {(dirFired > 0 ? "CAN fire" : "NEVER fires")} with random signals");
    }

    private static Seed.Market.Indicators.TechnicalIndicators.Candle[] LoadCandlesFromCache(string path)
    {
        return File.ReadAllLines(path)
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l =>
            {
                var parts = l.Split(',');
                return new Seed.Market.Indicators.TechnicalIndicators.Candle(
                    float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[4], System.Globalization.CultureInfo.InvariantCulture),
                    DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(parts[5]))
                );
            }).ToArray();
    }

    private static (SignalSnapshot[] snapshots, float[] prices, float[] volumes, float[] funding)
        BuildSyntheticData(int n, int seed)
    {
        var rng = new Random(seed);
        var candles = new Seed.Market.Indicators.TechnicalIndicators.Candle[n];
        var startTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        float price = 50_000f;
        for (int i = 0; i < n; i++)
        {
            float drift = 5f;
            float noise = (float)(rng.NextDouble() - 0.5) * 200f;
            float open = price;
            price = price + drift + noise;
            float high = MathF.Max(open, price) + (float)rng.NextDouble() * 50f;
            float low = MathF.Min(open, price) - (float)rng.NextDouble() * 50f;
            float volume = 100f + (float)rng.NextDouble() * 50f;
            candles[i] = new Seed.Market.Indicators.TechnicalIndicators.Candle(
                Open: open, High: high, Low: low, Close: price, Volume: volume,
                Time: startTime.AddMinutes(15 * i));
        }
        return HistoricalDataStore.CandlesToSignals(candles, enrichment: null, barsPerHour: 4);
    }
}
