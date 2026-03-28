using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market;
using Seed.Market.Agents;
using Seed.Market.Backtest;
using Seed.Market.Data;
using Seed.Market.Evaluation;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Market.Trading;
using Seed.Observatory;

var configPath = args.Length > 0 ? args[0] : "market-config.default.json";
var config = File.Exists(configPath)
    ? MarketConfig.Load(configPath)
    : MarketConfig.Default;

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║          THE SEED — MARKET EVOLUTION ENGINE                  ║");
Console.WriteLine($"║  Mode: {config.Mode,-52}║");
Console.WriteLine($"║  Capital: ${config.InitialCapital,-48:N0}║");
Console.WriteLine($"║  Population: {config.PopulationSize,-46}║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");

Directory.CreateDirectory(config.OutputDirectory);

switch (config.Mode)
{
    case ExecutionMode.Backtest:
        await RunBacktest(config);
        break;
    case ExecutionMode.Paper:
        await RunPaper(config);
        break;
    case ExecutionMode.Live:
        RunLive(config);
        break;
    case ExecutionMode.Compare:
        await RunCompare(config);
        break;
    case ExecutionMode.Ablation:
        await RunAblation(config);
        break;
    case ExecutionMode.StressTest:
        await RunStressTest(config);
        break;
    case ExecutionMode.MonteCarlo:
        await RunMonteCarlo(config);
        break;
    case ExecutionMode.Ensemble:
        Console.WriteLine("[ENSEMBLE] Ensemble mode requires checkpoint with species IDs. Use paper mode for now.");
        break;
    case ExecutionMode.NeuroAblation:
        await RunNeuroAblation(config);
        break;
}

// ─────────────────────────────────────────────────────────────────────────────
// BACKTEST MODE — with checkpointing and resume support
// ─────────────────────────────────────────────────────────────────────────────
static async Task RunBacktest(MarketConfig config)
{
    Console.WriteLine("\n[BACKTEST] Downloading historical data...");

    var runner = new BacktestRunner(config);
    var end = DateTimeOffset.UtcNow.AddHours(-1);
    var start = end.AddHours(-config.TrainingWindowHours - config.ValidationWindowHours);

    var (snapshots, prices) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);
    Console.WriteLine($"[BACKTEST] Loaded {snapshots.Length} candles ({start:yyyy-MM-dd} to {end:yyyy-MM-dd})");

    int trainLen = Math.Min(config.TrainingWindowHours, snapshots.Length - config.ValidationWindowHours);
    int valStart = trainLen;
    int valEnd = snapshots.Length;

    var trainSnapshots = snapshots[..trainLen];
    var trainPrices = prices[..trainLen];
    var valSnapshots = snapshots[valStart..valEnd];
    var valPrices = prices[valStart..valEnd];

    Console.WriteLine($"[BACKTEST] Training: {trainLen}h, Validation: {valEnd - valStart}h");

    var observatory = new FileObservatory(Path.Combine(config.OutputDirectory, "events.jsonl"));
    var evolution = new MarketEvolution(config, observatory);

    var checkpointDir = Path.Combine(config.OutputDirectory, "checkpoints");
    Directory.CreateDirectory(checkpointDir);

    int startGen = 0;
    float bestEverFitness = float.MinValue;

    var latestCheckpoint = CheckpointState.FindLatest(checkpointDir);
    if (latestCheckpoint != null)
    {
        Console.WriteLine($"[BACKTEST] Resuming from checkpoint: {Path.GetFileName(latestCheckpoint)}");
        var cp = CheckpointState.Load(latestCheckpoint);
        var restored = cp.RestorePopulation();
        evolution.InitializeFrom(restored, cp.Generation);
        startGen = cp.Generation;
        bestEverFitness = cp.BestFitness;
        Console.WriteLine($"[BACKTEST] Restored generation {cp.Generation}, best fitness {cp.BestFitness:F4}");
    }
    else
    {
        evolution.Initialize();
    }

    Console.WriteLine($"\n{"Gen",-5} {"Best",8} {"Mean",8} {"Sharpe",8} {"Return",8} {"WR",6} {"Trades",7} {"Species",8} {"ValFit",8}");
    Console.WriteLine(new string('─', 74));

    int evalWindow = Math.Min(config.EvalWindowHours, trainLen);
    var valEvaluator = new MarketEvaluator(config);

    float bestValFitness = float.MinValue;
    string? bestValGenomePath = null;
    var validationHistory = new List<(int Gen, float TrainFit, float ValFit)>();
    int consecutiveValDeclines = 0;

    int k = Math.Max(1, config.EvalWindowCount);
    int walkForwardOffset = 0;
    int stallCount = 0;

    for (int gen = startGen; gen < config.Generations; gen++)
    {
        GenerationReport report;

        int remainingLen = Math.Max(50, trainLen - walkForwardOffset);
        if (walkForwardOffset + remainingLen > trainLen)
            remainingLen = trainLen - walkForwardOffset;
        if (remainingLen < 1) remainingLen = 1;
        var wfSnaps = trainSnapshots[walkForwardOffset..(walkForwardOffset + remainingLen)];
        var wfPrices = trainPrices[walkForwardOffset..(walkForwardOffset + remainingLen)];
        int wfEvalWindow = Math.Min(evalWindow, remainingLen);

        if (k <= 1)
        {
            int maxOff = Math.Max(1, remainingLen - wfEvalWindow);
            int offset = config.WalkForwardEnabled ? 0 : (gen * config.RollingStepHours) % maxOff;
            int windowEnd = Math.Min(offset + wfEvalWindow, remainingLen);
            var windowSnaps = wfSnaps[offset..windowEnd];
            var windowPrices = wfPrices[offset..windowEnd];
            if (windowSnaps.Length < 50) { windowSnaps = wfSnaps; windowPrices = wfPrices; }
            report = evolution.RunGeneration(windowSnaps, windowPrices);
        }
        else
        {
            var diverseWindows = RegimeDetector.SelectDiverseWindows(
                wfPrices, remainingLen, wfEvalWindow, k, gen, config.RunSeed);
            var windowList = new (SignalSnapshot[], float[])[diverseWindows.Length];
            for (int w = 0; w < diverseWindows.Length; w++)
            {
                var (off, len, _) = diverseWindows[w];
                int end2 = Math.Min(off + len, remainingLen);
                if (end2 - off < 50) { off = 0; end2 = Math.Min(wfEvalWindow, remainingLen); }
                windowList[w] = (wfSnaps[off..end2], wfPrices[off..end2]);
            }
            report = evolution.RunGeneration(windowList);
        }

        string valFitStr = "";
        bool isValGen = config.ValidationIntervalGens > 0 &&
                        gen > 0 && gen % config.ValidationIntervalGens == 0 &&
                        valSnapshots.Length >= 50;

        if (isValGen)
        {
            var bestGenome = evolution.GetBestGenome();
            if (bestGenome != null)
            {
                var valResult = valEvaluator.EvaluateSingle(bestGenome, valSnapshots, valPrices, gen);
                float valFit = valResult.Fitness.Fitness;
                valFitStr = $"{valFit,8:F4}";
                validationHistory.Add((gen, report.BestFitness, valFit));

                if (valFit > bestValFitness)
                {
                    bestValFitness = valFit;
                    bestValGenomePath = Path.Combine(checkpointDir, $"best_val_gen_{gen:D4}.json");
                    File.WriteAllText(bestValGenomePath, bestGenome.ToJson());
                    consecutiveValDeclines = 0;
                }
                else
                {
                    consecutiveValDeclines++;
                }

                if (consecutiveValDeclines >= config.EarlyStopPatience &&
                    validationHistory.Count >= config.EarlyStopPatience)
                {
                    bool trainImproving = validationHistory[^1].TrainFit >= validationHistory[^config.EarlyStopPatience].TrainFit;
                    if (trainImproving)
                    {
                        Console.WriteLine($"  [OVERFIT] Validation declining for {consecutiveValDeclines} checks while training improves");
                        if (config.EarlyStopEnabled)
                        {
                            Console.WriteLine("  [EARLY STOP] Halting training due to overfitting");
                            break;
                        }
                    }
                }

                if (config.WalkForwardEnabled)
                {
                    int maxWfOffset = Math.Max(0, trainLen - evalWindow);
                    if (valFit >= config.WalkForwardMinValFitness)
                    {
                        walkForwardOffset = Math.Min(walkForwardOffset + config.RollingStepHours, maxWfOffset);
                        stallCount = 0;
                        Console.WriteLine($"  [WALK-FWD] Passed ({valFit:F4}), advanced to {walkForwardOffset}h");
                    }
                    else
                    {
                        stallCount++;
                        Console.WriteLine($"  [WALK-FWD] Failed ({valFit:F4}), stalled {stallCount}/{config.WalkForwardMaxStallGens}");
                        if (stallCount >= config.WalkForwardMaxStallGens)
                        {
                            walkForwardOffset = Math.Min(walkForwardOffset + config.RollingStepHours, maxWfOffset);
                            stallCount = 0;
                            Console.WriteLine($"  [WALK-FWD] Force-advanced after max stall");
                        }
                    }
                }
            }
        }

        Console.WriteLine(
            $"{gen,-5} {report.BestFitness,8:F4} {report.MeanFitness,8:F4} " +
            $"{report.BestSharpe,8:F2} {report.BestReturn,7:P1} {report.BestWinRate,5:P0} " +
            $"{report.BestTrades,7} {report.SpeciesCount,8} {valFitStr}");

        if (report.BestFitness > bestEverFitness)
        {
            bestEverFitness = report.BestFitness;
            var bestGenome = evolution.GetBestGenome();
            if (bestGenome != null)
            {
                var genomePath = Path.Combine(checkpointDir, $"best_gen_{gen:D4}.json");
                File.WriteAllText(genomePath, bestGenome.ToJson());
            }
        }

        if (config.CheckpointIntervalGens > 0 &&
            (gen + 1) % config.CheckpointIntervalGens == 0)
        {
            var cp = CheckpointState.FromPopulation(evolution.Population, gen + 1, bestEverFitness, evolution.GetSpeciesIds());
            cp.Save(Path.Combine(checkpointDir, $"checkpoint_{gen + 1:D4}.json"));
            Console.WriteLine($"  [checkpoint saved: gen {gen + 1}]");
        }
    }

    // Final validation run on full population
    Console.WriteLine($"\n{"═══ VALIDATION ═══",-74}");
    var valResults = new BacktestRunner(config)
        .Evaluate(evolution.Population, valSnapshots, valPrices, config.Generations);

    var bestValResult = valResults.Values.OrderByDescending(r => r.Fitness.Fitness).First();
    Console.WriteLine($"  Best validation fitness: {bestValResult.Fitness.Fitness:F4}");
    Console.WriteLine($"  Sharpe (adjusted): {bestValResult.Fitness.AdjustedSharpe:F2}");
    Console.WriteLine($"  Return: {bestValResult.Fitness.ReturnPct:P2}");
    Console.WriteLine($"  Trades: {bestValResult.Fitness.TotalTrades}, Win rate: {bestValResult.Fitness.WinRate:P0}");
    Console.WriteLine($"  Max drawdown: {bestValResult.Fitness.MaxDrawdown:P2}");

    // Save the best genome by VALIDATION fitness for deployment
    if (bestValGenomePath != null && File.Exists(bestValGenomePath))
    {
        File.Copy(bestValGenomePath, config.ResolvedGenomePath, overwrite: true);
        Console.WriteLine($"\n  Best genome (by validation) saved to: {config.ResolvedGenomePath}");
    }
    else
    {
        var finalBest = evolution.GetBestGenome();
        if (finalBest != null)
        {
            File.WriteAllText(config.ResolvedGenomePath, finalBest.ToJson());
            Console.WriteLine($"\n  Best genome (by training) saved to: {config.ResolvedGenomePath}");
        }
    }

    // Save training-best separately for analysis
    var trainingBest = evolution.GetBestGenome();
    if (trainingBest != null)
    {
        var trainBestPath = Path.Combine(config.OutputDirectory, "best_training_genome.json");
        File.WriteAllText(trainBestPath, trainingBest.ToJson());
    }

    Console.WriteLine("\n[BACKTEST] Complete.");
}

// ─────────────────────────────────────────────────────────────────────────────
// PAPER MODE — full genome-to-live-trading pipeline
// ─────────────────────────────────────────────────────────────────────────────
static async Task RunPaper(MarketConfig config)
{
    Console.WriteLine("\n[PAPER] Starting paper trading with live data...");

    var genomePath = config.ResolvedGenomePath;
    if (!File.Exists(genomePath))
    {
        Console.WriteLine($"[PAPER] No trained genome found at: {genomePath}");
        Console.WriteLine("[PAPER] Run backtest first to produce a trained genome.");
        return;
    }

    Console.WriteLine($"[PAPER] Loading genome from: {genomePath}");
    var genomeJson = File.ReadAllText(genomePath);
    var genome = SeedGenome.FromJson(genomeJson);
    Console.WriteLine($"[PAPER] Genome loaded: {genome.GenomeId}");
    Console.WriteLine($"[PAPER]   CPPN nodes: {genome.Cppn.Nodes.Count}, connections: {genome.Cppn.Connections.Count}");

    Console.WriteLine("[PAPER] Compiling brain...");
    var developer = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
    var devCtx = new DevelopmentContext(config.RunSeed, 0);
    var paperBudget = MarketEvaluator.MarketBrainBudget with
    {
        HiddenWidth = genome.Dev.SubstrateWidth,
        HiddenHeight = genome.Dev.SubstrateHeight,
        HiddenLayers = genome.Dev.SubstrateLayers
    };
    var graph = developer.CompileGraph(genome, paperBudget, devCtx);
    var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
    Console.WriteLine($"[PAPER] Brain compiled: {graph.NodeCount} neurons, {graph.EdgeCount} synapses");

    var trader = new PaperTrader(config);
    var agent = new MarketAgent(genome.GenomeId, brain, trader);

    Console.WriteLine("[PAPER] Connecting to live data feeds...");
    using var aggregator = new DataAggregator(config);
    using var tradeLog = new TradeLogger(config.ResolvedTradeLogPath);

    Console.WriteLine($"[PAPER] Trade log: {config.ResolvedTradeLogPath}");
    Console.WriteLine("[PAPER] Paper trading active. Press Ctrl+C to stop.\n");

    Console.WriteLine($"{"Tick",-7} {"Price",11} {"Pos",6} {"Unrl P&L",10} {"Equity",12} {"Trades",7} {"WR",5} {"rSharpe",8} {"rDD",6}");
    Console.WriteLine(new string('─', 82));
    var rolling = new RollingMetrics(100);

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    int tick = 0;
    int prevTradeCount = 0;
    var lastDisplay = DateTimeOffset.MinValue;
    var lastHeartbeat = DateTimeOffset.MinValue;

    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var snapshot = await aggregator.TickAsync(cts.Token);
            decimal price = (decimal)aggregator.LastRawBtcPrice;

            if (price <= 0)
            {
                await Task.Delay(config.SpotPollMs, cts.Token);
                continue;
            }

            agent.ProcessTick(snapshot, price);
            agent.Portfolio.RecordEquity(price);
            rolling.Add((float)agent.Portfolio.Equity(price));

            if (agent.Portfolio.TradeHistory.Count > prevTradeCount)
            {
                for (int i = prevTradeCount; i < agent.Portfolio.TradeHistory.Count; i++)
                    tradeLog.LogTrade(agent.Portfolio.TradeHistory[i]);
                prevTradeCount = agent.Portfolio.TradeHistory.Count;
            }

            var now = DateTimeOffset.UtcNow;
            if ((now - lastDisplay).TotalMilliseconds >= config.DisplayIntervalMs)
            {
                lastDisplay = now;
                var portfolio = agent.Portfolio;
                decimal equity = portfolio.Equity(price);
                decimal unrealized = equity - portfolio.Balance;
                string pos = portfolio.OpenPositions.Count > 0
                    ? portfolio.OpenPositions[0].Direction == TradeDirection.Long ? "LONG" : "SHORT"
                    : "FLAT";

                Console.WriteLine(
                    $"{tick,-7} ${price,10:N2} {pos,6} " +
                    $"{unrealized,9:+#,##0.00;-#,##0.00;0.00} " +
                    $"${equity,10:N2} {portfolio.TotalTrades,7} " +
                    $"{portfolio.WinRate,4:P0} {rolling.RollingSharpe,8:F2} {rolling.RollingDrawdown,5:P1}");

                if (portfolio.KillSwitchTriggered)
                {
                    Console.WriteLine("\n  [KILL SWITCH] Max drawdown exceeded — trading halted, monitoring continues.");
                }
            }

            // Heartbeat logging
            if ((DateTimeOffset.UtcNow - lastHeartbeat).TotalSeconds >= 60)
            {
                var hb = $"{{\"ts\":\"{DateTimeOffset.UtcNow:O}\",\"equity\":{agent.Portfolio.Equity(price):F2}," +
                         $"\"trades\":{agent.Portfolio.TotalTrades},\"positions\":{agent.Portfolio.OpenPositions.Count}}}";
                Directory.CreateDirectory(config.OutputDirectory);
                File.AppendAllText(Path.Combine(config.OutputDirectory, "heartbeat.jsonl"), hb + "\n");
                lastHeartbeat = DateTimeOffset.UtcNow;
            }

            // Feed staleness detection
            if ((DateTimeOffset.UtcNow - aggregator.LastTickTime).TotalMinutes > 5)
                Console.WriteLine($"  [STALE] Data feed not updated for {(DateTimeOffset.UtcNow - aggregator.LastTickTime).TotalMinutes:F0} minutes");

            tick++;
            await Task.Delay(config.SpotPollMs, cts.Token);
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"[PAPER] Feed error: {ex.Message}");
            await Task.Delay(5000, cts.Token);
        }
    }

    decimal finalPrice = agent.Portfolio.OpenPositions.Count > 0
        ? agent.Portfolio.OpenPositions[0].EntryPrice
        : config.InitialCapital;
    trader.CloseAllPositions(agent.Portfolio, finalPrice, tick);

    Console.WriteLine($"\n{"═══ SESSION SUMMARY ═══",-64}");
    Console.WriteLine($"  Ticks processed: {tick}");
    Console.WriteLine($"  Total trades:    {agent.Portfolio.TotalTrades}");
    Console.WriteLine($"  Win rate:        {agent.Portfolio.WinRate:P0}");
    Console.WriteLine($"  Net P&L:         {agent.Portfolio.TotalPnl:+#,##0.00;-#,##0.00;0.00}");
    Console.WriteLine($"  Max drawdown:    {agent.Portfolio.MaxDrawdown:P2}");
    Console.WriteLine($"  Final balance:   ${agent.Portfolio.Balance:N2}");
    Console.WriteLine($"  Trade log:       {config.ResolvedTradeLogPath}");
    Console.WriteLine("\n[PAPER] Stopped.");
}

// ─────────────────────────────────────────────────────────────────────────────
// LIVE MODE — safety-gated, not yet implemented
// ─────────────────────────────────────────────────────────────────────────────
static void RunLive(MarketConfig config)
{
    if (!config.ConfirmLive)
    {
        Console.WriteLine("[LIVE] SAFETY GATE: Set confirmLive=true in config to enable live trading.");
        Console.WriteLine("[LIVE] This will execute REAL trades with REAL money.");
        return;
    }

    Console.WriteLine("[LIVE] Live trading not yet implemented. Use paper mode for now.");
}

// ─────────────────────────────────────────────────────────────────────────────
// COMPARE MODE — evolved agent vs baselines
// ─────────────────────────────────────────────────────────────────────────────
static async Task RunCompare(MarketConfig config)
{
    using var tracker = new ExperimentTracker(config.OutputDirectory, config, "compare");
    Console.WriteLine("\n[COMPARE] Loading data and genome...");

    var runner = new BacktestRunner(config);
    var end = DateTimeOffset.UtcNow.AddHours(-1);
    var start = end.AddHours(-config.TrainingWindowHours);
    var (snapshots, prices) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);

    var genomePath = config.ResolvedGenomePath;
    if (!File.Exists(genomePath))
    {
        Console.WriteLine("[COMPARE] No trained genome found. Run backtest first.");
        return;
    }

    var evaluator = new MarketEvaluator(config);
    var genome = SeedGenome.FromJson(File.ReadAllText(genomePath));

    int windowSize = Math.Min(config.EvalWindowHours, snapshots.Length / 4);
    int numWindows = Math.Min(20, snapshots.Length / windowSize);

    var evolvedFitnesses = new float[numWindows];
    var bhFitnesses = new float[numWindows];
    var smaFitnesses = new float[numWindows];
    var rndFitnesses = new float[numWindows];
    var mrFitnesses = new float[numWindows];

    for (int w = 0; w < numWindows; w++)
    {
        int offset = w * windowSize;
        var wSnaps = snapshots[offset..(offset + windowSize)];
        var wPrices = prices[offset..(offset + windowSize)];

        var result = evaluator.EvaluateSingle(genome, wSnaps, wPrices, w);
        evolvedFitnesses[w] = result.Fitness.Fitness;
        bhFitnesses[w] = BaselineStrategies.BuyAndHold(wPrices, config).Fitness;
        smaFitnesses[w] = BaselineStrategies.SmaCrossover(wPrices, config).Fitness;
        rndFitnesses[w] = BaselineStrategies.RandomAgent(wPrices, config, seed: w).Fitness;
        mrFitnesses[w] = BaselineStrategies.MeanReversion(wPrices, config).Fitness;
    }

    Console.WriteLine($"\n{"Strategy",-20} {"Mean Fit",10} {"vs Evolved p",14} {"Cohen d",10}");
    Console.WriteLine(new string('─', 56));

    void PrintRow(string name, float[] fits)
    {
        float mean = fits.Average();
        var (_, pVal, d) = StatisticalTests.PairedTTest(evolvedFitnesses, fits);
        Console.WriteLine($"{name,-20} {mean,10:F4} {pVal,14:F4} {d,10:F2}");
    }

    Console.WriteLine($"{"Evolved Agent",-20} {evolvedFitnesses.Average(),10:F4} {"—",14} {"—",10}");
    PrintRow("Buy & Hold", bhFitnesses);
    PrintRow("SMA 20/50", smaFitnesses);
    PrintRow("Random", rndFitnesses);
    PrintRow("Mean Reversion", mrFitnesses);

    tracker.RecordMetric("evolvedMeanFitness", evolvedFitnesses.Average());
    tracker.RecordMetric("windows", numWindows);
}

// ─────────────────────────────────────────────────────────────────────────────
// ABLATION MODE — disable components one at a time
// ─────────────────────────────────────────────────────────────────────────────
static async Task RunAblation(MarketConfig config)
{
    using var tracker = new ExperimentTracker(config.OutputDirectory, config, "ablation");
    Console.WriteLine("\n[ABLATION] Loading data and genome...");

    var runner = new BacktestRunner(config);
    var end = DateTimeOffset.UtcNow.AddHours(-1);
    var start = end.AddHours(-config.ValidationWindowHours);
    var (snapshots, prices) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);

    var genomePath = config.ResolvedGenomePath;
    if (!File.Exists(genomePath))
    {
        Console.WriteLine("[ABLATION] No trained genome found.");
        return;
    }

    var genome = SeedGenome.FromJson(File.ReadAllText(genomePath));
    var developer = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
    var devCtx = new DevelopmentContext(config.RunSeed, 0);
    var sg = genome;
    var budget = MarketEvaluator.MarketBrainBudget with
    {
        HiddenWidth = sg.Dev.SubstrateWidth,
        HiddenHeight = sg.Dev.SubstrateHeight,
        HiddenLayers = sg.Dev.SubstrateLayers
    };

    float EvalWithAblation(AblationConfig abl)
    {
        var graph = developer.CompileGraph(sg, budget, devCtx);
        var brain = new BrainRuntime(graph, sg.Learn, sg.Stable, 1, abl);
        var trader = new PaperTrader(config);
        var agent = new MarketAgent(sg.GenomeId, brain, trader, abl);
        for (int t = 0; t < snapshots.Length; t++)
        {
            decimal price = (decimal)prices[t];
            if (price <= 0) continue;
            agent.ProcessTick(snapshots[t], price);
            agent.Portfolio.RecordEquity(price);
        }
        trader.CloseAllPositions(agent.Portfolio, (decimal)prices[^1], snapshots.Length);
        return MarketFitness.ComputeDetailed(agent.Portfolio, (decimal)prices[^1], config.ShrinkageK).Fitness;
    }

    float baseline = EvalWithAblation(AblationConfig.Default);

    var ablations = new (string Name, AblationConfig Config)[]
    {
        ("No Learning", AblationConfig.Default with { LearningEnabled = false }),
        ("No Curiosity", AblationConfig.Default with { CuriosityEnabled = false }),
        ("No Homeostasis", AblationConfig.Default with { HomeostasisEnabled = false }),
        ("No Modulatory", AblationConfig.Default with { ModulatoryEdgesEnabled = false }),
        ("No Delays", AblationConfig.Default with { SynapticDelaysEnabled = false }),
        ("No Recurrence", AblationConfig.Default with { RecurrenceEnabled = false }),
    };

    Console.WriteLine($"\n{"Component",-20} {"Fitness",10} {"Delta",10} {"Impact",10}");
    Console.WriteLine(new string('─', 52));
    Console.WriteLine($"{"Full Model",-20} {baseline,10:F4} {"—",10} {"—",10}");

    foreach (var (name, abl) in ablations)
    {
        float fit = EvalWithAblation(abl);
        float delta = fit - baseline;
        string impact = delta < -0.01f ? "HELPS" : delta > 0.01f ? "HURTS" : "neutral";
        Console.WriteLine($"{name,-20} {fit,10:F4} {delta,10:F4} {impact,10}");
        tracker.RecordMetric($"ablation_{name}", fit);
    }
    tracker.RecordMetric("baseline", baseline);
}

// ─────────────────────────────────────────────────────────────────────────────
// STRESS TEST MODE — fee/slippage multipliers
// ─────────────────────────────────────────────────────────────────────────────
static async Task RunStressTest(MarketConfig config)
{
    using var tracker = new ExperimentTracker(config.OutputDirectory, config, "stress-test");
    Console.WriteLine("\n[STRESS] Loading data and genome...");

    var runner = new BacktestRunner(config);
    var end = DateTimeOffset.UtcNow.AddHours(-1);
    var start = end.AddHours(-config.TrainingWindowHours);
    var (snapshots, prices) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);

    var genomePath = config.ResolvedGenomePath;
    if (!File.Exists(genomePath))
    {
        Console.WriteLine("[STRESS] No trained genome found.");
        return;
    }

    Console.WriteLine($"\n{"Multiplier",-12} {"Fitness",10} {"Return",10} {"Trades",8}");
    Console.WriteLine(new string('─', 44));

    foreach (var mult in new[] { 1m, 2m, 3m, 5m })
    {
        var stressConfig = config with
        {
            MakerFee = config.MakerFee * mult,
            TakerFee = config.TakerFee * mult,
            SlippageBps = config.SlippageBps * mult
        };
        var evaluator = new MarketEvaluator(stressConfig);
        var genome = SeedGenome.FromJson(File.ReadAllText(genomePath));
        var result = evaluator.EvaluateSingle(genome, snapshots, prices, 0);

        Console.WriteLine($"{mult + "x",-12} {result.Fitness.Fitness,10:F4} {result.Fitness.ReturnPct,9:P1} {result.Fitness.TotalTrades,8}");
        tracker.RecordMetric($"stress_{mult}x", result.Fitness.Fitness);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// MONTE CARLO MODE — bootstrap confidence intervals
// ─────────────────────────────────────────────────────────────────────────────
static async Task RunMonteCarlo(MarketConfig config)
{
    using var tracker = new ExperimentTracker(config.OutputDirectory, config, "monte-carlo");
    Console.WriteLine("\n[MC] Loading data and genome...");

    var runner = new BacktestRunner(config);
    var end = DateTimeOffset.UtcNow.AddHours(-1);
    var start = end.AddHours(-config.TrainingWindowHours);
    var (snapshots, prices) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);

    var genomePath = config.ResolvedGenomePath;
    if (!File.Exists(genomePath))
    {
        Console.WriteLine("[MC] No trained genome found.");
        return;
    }

    var evaluator = new MarketEvaluator(config);
    var genome = SeedGenome.FromJson(File.ReadAllText(genomePath));
    var evalResult = evaluator.EvaluateSingle(genome, snapshots, prices, 0);

    Console.WriteLine($"[MC] Agent made {evalResult.Fitness.TotalTrades} trades. Bootstrapping 10,000 resamples...");

    var tradePnls = new List<float>();
    // Re-run to get trade history (EvaluateSingle doesn't expose it directly)
    var developer = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
    var sg = (SeedGenome)genome;
    var budget = MarketEvaluator.MarketBrainBudget with
    {
        HiddenWidth = sg.Dev.SubstrateWidth,
        HiddenHeight = sg.Dev.SubstrateHeight,
        HiddenLayers = sg.Dev.SubstrateLayers
    };
    var graph = developer.CompileGraph(sg, budget, new DevelopmentContext(config.RunSeed, 0));
    var brain = new BrainRuntime(graph, sg.Learn, sg.Stable, 1);
    var trader = new PaperTrader(config);
    var agent = new MarketAgent(sg.GenomeId, brain, trader);

    for (int t = 0; t < snapshots.Length; t++)
    {
        decimal price = (decimal)prices[t];
        if (price <= 0) continue;
        agent.ProcessTick(snapshots[t], price);
    }
    trader.CloseAllPositions(agent.Portfolio, (decimal)prices[^1], snapshots.Length);

    foreach (var trade in agent.Portfolio.TradeHistory)
        tradePnls.Add((float)trade.Pnl);

    var ci = StatisticalTests.BootstrapReturn(tradePnls, 10_000, seed: 42);

    Console.WriteLine($"\n  Trades:          {tradePnls.Count}");
    Console.WriteLine($"  5th percentile:  {ci.P5:F2}");
    Console.WriteLine($"  Median:          {ci.Median:F2}");
    Console.WriteLine($"  95th percentile: {ci.P95:F2}");
    Console.WriteLine($"  Width:           {ci.P95 - ci.P5:F2}");

    tracker.RecordMetric("trades", tradePnls.Count);
    tracker.RecordMetric("ci_p5", ci.P5);
    tracker.RecordMetric("ci_median", ci.Median);
    tracker.RecordMetric("ci_p95", ci.P95);
}

// ─────────────────────────────────────────────────────────────────────────────
// NEURO-ABLATION MODE — test each modulator channel independently
// ─────────────────────────────────────────────────────────────────────────────
static async Task RunNeuroAblation(MarketConfig config)
{
    using var tracker = new ExperimentTracker(config.OutputDirectory, config, "neuro-ablation");
    Console.WriteLine("\n[NEURO] Loading data and genome...");

    var runner = new BacktestRunner(config);
    var end = DateTimeOffset.UtcNow.AddHours(-1);
    var start = end.AddHours(-config.ValidationWindowHours);
    var (snapshots, prices) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);

    var genomePath = config.ResolvedGenomePath;
    if (!File.Exists(genomePath))
    {
        Console.WriteLine("[NEURO] No trained genome found.");
        return;
    }

    var genome = SeedGenome.FromJson(File.ReadAllText(genomePath));
    var developer = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
    var devCtx = new DevelopmentContext(config.RunSeed, 0);
    var budget = MarketEvaluator.MarketBrainBudget with
    {
        HiddenWidth = genome.Dev.SubstrateWidth,
        HiddenHeight = genome.Dev.SubstrateHeight,
        HiddenLayers = genome.Dev.SubstrateLayers
    };

    float EvalWithLearnParams(LearningParams lp)
    {
        var graph = developer.CompileGraph(genome, budget, devCtx);
        var brain = new BrainRuntime(graph, lp, genome.Stable, 1);
        var trader = new PaperTrader(config);
        var agent = new MarketAgent(genome.GenomeId, brain, trader);
        for (int t = 0; t < snapshots.Length; t++)
        {
            decimal price = (decimal)prices[t];
            if (price <= 0) continue;
            agent.ProcessTick(snapshots[t], price);
            agent.Portfolio.RecordEquity(price);
        }
        trader.CloseAllPositions(agent.Portfolio, (decimal)prices[^1], snapshots.Length);
        return MarketFitness.ComputeDetailed(agent.Portfolio, (decimal)prices[^1], config.ShrinkageK).Fitness;
    }

    var configurations = new (string Name, LearningParams Params)[]
    {
        ("All active (baseline)", genome.Learn),
        ("No reward", genome.Learn with { AlphaReward = 0f }),
        ("No pain", genome.Learn with { AlphaPain = 0f }),
        ("No curiosity", genome.Learn with { AlphaCuriosity = 0f }),
        ("Only curiosity", genome.Learn with { AlphaReward = 0f, AlphaPain = 0f }),
        ("Only reward", genome.Learn with { AlphaPain = 0f, AlphaCuriosity = 0f }),
    };

    float baseline = float.NaN;

    Console.WriteLine($"\n{"Configuration",-30} {"Fitness",10} {"Delta",10}");
    Console.WriteLine(new string('─', 52));

    foreach (var (name, lp) in configurations)
    {
        float fit = EvalWithLearnParams(lp);
        if (float.IsNaN(baseline)) baseline = fit;
        float delta = fit - baseline;
        string deltaStr = name == configurations[0].Name ? "—" : $"{delta:+0.0000;-0.0000;0.0000}";
        Console.WriteLine($"{name,-30} {fit,10:F4} {deltaStr,10}");
        tracker.RecordMetric($"neuro_{name}", fit);
    }

    Console.WriteLine($"\n  Interpretation:");
    Console.WriteLine($"  - Largest negative delta = most important modulator channel");
    Console.WriteLine($"  - Near-zero delta = that channel adds little value");
    Console.WriteLine($"  - Positive delta = that channel is actively hurting (noise)");
}
