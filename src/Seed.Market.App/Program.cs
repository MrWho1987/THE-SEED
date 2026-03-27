using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market;
using Seed.Market.Agents;
using Seed.Market.Backtest;
using Seed.Market.Data;
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

    var (snapshots, prices) = await runner.LoadData(config.Symbols[0], start, end);
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

    Console.WriteLine($"\n{"Gen",-5} {"Best",8} {"Mean",8} {"Return",8} {"WR",6} {"Trades",7} {"Species",8}");
    Console.WriteLine(new string('─', 56));

    int evalWindow = Math.Min(config.EvalWindowHours, trainLen);

    for (int gen = startGen; gen < config.Generations; gen++)
    {
        int maxOffset = Math.Max(1, trainLen - evalWindow);
        int offset = (gen * config.RollingStepHours) % maxOffset;
        int windowEnd = Math.Min(offset + evalWindow, trainLen);
        var windowSnaps = trainSnapshots[offset..windowEnd];
        var windowPrices = trainPrices[offset..windowEnd];

        if (windowSnaps.Length < 50)
        {
            windowSnaps = trainSnapshots;
            windowPrices = trainPrices;
        }

        var report = evolution.RunGeneration(windowSnaps, windowPrices);

        Console.WriteLine(
            $"{gen,-5} {report.BestFitness,8:F4} {report.MeanFitness,8:F4} " +
            $"{report.BestReturn,7:P1} {report.BestWinRate,5:P0} " +
            $"{report.BestTrades,7} {report.SpeciesCount,8}");

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
            var cp = CheckpointState.FromPopulation(evolution.Population, gen + 1, bestEverFitness);
            cp.Save(Path.Combine(checkpointDir, $"checkpoint_{gen + 1:D4}.json"));
            Console.WriteLine($"  [checkpoint saved: gen {gen + 1}]");
        }
    }

    // Validation run
    Console.WriteLine($"\n{"═══ VALIDATION ═══",-56}");
    var valResults = new BacktestRunner(config)
        .Evaluate(evolution.Population, valSnapshots, valPrices, config.Generations);

    var bestVal = valResults.Values.OrderByDescending(r => r.Fitness.Fitness).First();
    Console.WriteLine($"  Best validation fitness: {bestVal.Fitness.Fitness:F4}");
    Console.WriteLine($"  Return: {bestVal.Fitness.ReturnPct:P2}");
    Console.WriteLine($"  Trades: {bestVal.Fitness.TotalTrades}, Win rate: {bestVal.Fitness.WinRate:P0}");
    Console.WriteLine($"  Max drawdown: {bestVal.Fitness.MaxDrawdown:P2}");

    var finalBest = evolution.GetBestGenome();
    if (finalBest != null)
    {
        var genomePath = config.ResolvedGenomePath;
        File.WriteAllText(genomePath, finalBest.ToJson());
        Console.WriteLine($"\n  Best genome saved to: {genomePath}");
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
    var graph = developer.CompileGraph(genome, MarketEvaluator.MarketBrainBudget, devCtx);
    var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
    Console.WriteLine($"[PAPER] Brain compiled: {graph.NodeCount} neurons, {graph.EdgeCount} synapses");

    var trader = new PaperTrader(config);
    var agent = new MarketAgent(genome.GenomeId, brain, trader);

    Console.WriteLine("[PAPER] Connecting to live data feeds...");
    using var aggregator = new DataAggregator(config);
    using var tradeLog = new TradeLogger(config.ResolvedTradeLogPath);

    Console.WriteLine($"[PAPER] Trade log: {config.ResolvedTradeLogPath}");
    Console.WriteLine("[PAPER] Paper trading active. Press Ctrl+C to stop.\n");

    Console.WriteLine($"{"Tick",-7} {"Price",11} {"Pos",6} {"Unrl P&L",10} {"Equity",12} {"Trades",7} {"WR",5}");
    Console.WriteLine(new string('─', 64));

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    int tick = 0;
    int prevTradeCount = 0;
    var lastDisplay = DateTimeOffset.MinValue;

    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var snapshot = await aggregator.TickAsync(cts.Token);
            decimal price = (decimal)snapshot.Signals[SignalIndex.BtcPrice];

            if (price <= 0)
            {
                await Task.Delay(config.SpotPollMs, cts.Token);
                continue;
            }

            agent.ProcessTick(snapshot, price);

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
                    $"{portfolio.WinRate,4:P0}");

                if (portfolio.KillSwitchTriggered)
                {
                    Console.WriteLine("\n  [KILL SWITCH] Max drawdown exceeded — trading halted, monitoring continues.");
                }
            }

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
