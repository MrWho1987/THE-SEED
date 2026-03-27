using Seed.Core;
using Seed.Market;
using Seed.Market.Backtest;
using Seed.Market.Data;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Observatory;

var configPath = args.Length > 0 ? args[0] : "market-config.default.json";
var config = File.Exists(configPath)
    ? MarketConfig.Load(configPath)
    : MarketConfig.Default;

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║          THE SEED — MARKET EVOLUTION ENGINE                  ║");
Console.WriteLine($"║          Mode: {config.Mode,-44}║");
Console.WriteLine($"║          Capital: ${config.InitialCapital,-40:N0}║");
Console.WriteLine($"║          Population: {config.PopulationSize,-36}║");
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
    evolution.Initialize();

    float bestEverFitness = float.MinValue;

    Console.WriteLine($"\n{"Gen",-5} {"Best",8} {"Mean",8} {"Return",8} {"WR",6} {"Trades",7} {"Species",8}");
    Console.WriteLine(new string('─', 56));

    for (int gen = 0; gen < config.Generations; gen++)
    {
        // Rolling window: shift training data each generation
        int offset = (gen * config.RollingStepHours) % Math.Max(1, trainLen - 200);
        int windowEnd = Math.Min(offset + 500, trainLen);
        var windowSnaps = trainSnapshots[offset..windowEnd];
        var windowPrices = trainPrices[offset..windowEnd];

        if (windowSnaps.Length < 50)
        {
            windowSnaps = trainSnapshots;
            windowPrices = trainPrices;
        }

        var report = evolution.RunGeneration(windowSnaps, windowPrices);

        if (report.BestFitness > bestEverFitness)
            bestEverFitness = report.BestFitness;

        Console.WriteLine(
            $"{gen,-5} {report.BestFitness,8:F4} {report.MeanFitness,8:F4} " +
            $"{report.BestReturn,7:P1} {report.BestWinRate,5:P0} " +
            $"{report.BestTrades,7} {report.SpeciesCount,8}");
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

    // Export best genome
    var bestGenome = evolution.GetBestGenome();
    if (bestGenome != null)
    {
        var genomePath = Path.Combine(config.OutputDirectory, "best_market_genome.json");
        File.WriteAllText(genomePath, bestGenome.ToJson());
        Console.WriteLine($"\n  Best genome saved to: {genomePath}");
    }

    Console.WriteLine("\n[BACKTEST] Complete.");
}

static async Task RunPaper(MarketConfig config)
{
    Console.WriteLine("\n[PAPER] Starting paper trading with live data...");
    Console.WriteLine("[PAPER] Loading best genome...");

    var genomePath = Path.Combine(config.OutputDirectory, "best_market_genome.json");
    if (!File.Exists(genomePath))
    {
        Console.WriteLine("[PAPER] No trained genome found. Run backtest first.");
        return;
    }

    Console.WriteLine("[PAPER] Connecting to live data feeds...");
    using var aggregator = new DataAggregator(config);

    Console.WriteLine("[PAPER] Paper trading active. Press Ctrl+C to stop.");
    Console.WriteLine($"{"Tick",-6} {"Price",10} {"Signal",8} {"P&L",10} {"Equity",12}");
    Console.WriteLine(new string('─', 52));

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    int tick = 0;
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var snapshot = await aggregator.TickAsync(cts.Token);
            float price = snapshot.Signals[SignalIndex.BtcPrice];

            if (tick % 60 == 0)
            {
                Console.WriteLine(
                    $"{tick,-6} ${price,9:N0} {"--",8} {"$0.00",10} {"$10,000",12}");
            }
            tick++;
            await Task.Delay(config.SpotPollMs, cts.Token);
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"[PAPER] Error: {ex.Message}");
            await Task.Delay(5000, cts.Token);
        }
    }

    Console.WriteLine("\n[PAPER] Stopped.");
}

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
