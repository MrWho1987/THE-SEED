using System.Text.Json;
using System.Text.Json.Serialization;
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

var configPath = "market-config.default.json";
DateTimeOffset? cliEndDate = null;

// T2 — Multi-phase pipeline mode (--pipeline) is removed. The plan replaces it with a
// single-config WeightSchedule-driven run; phase boundaries were a source of population
// shock (TP1) and discontinuous selection. Use --config <path> with a non-trivial
// WeightSchedule for continuous fitness annealing.
if (args.Length >= 2 && args[0] == "--config")
    configPath = args[1];
else if (args.Length >= 1 && !args[0].StartsWith("-"))
    configPath = args[0];

// Optional --end-date "ISO-8601" flag, parsed independently of mode.
// When provided, pipeline mode pins all phases to this end-date instead of UtcNow-1h,
// allowing Phase N+1 to run on the same data window as analyzer runs of Phase N.
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--end-date")
    {
        if (DateTimeOffset.TryParse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            cliEndDate = parsed;
        }
        else
        {
            Console.Error.WriteLine($"[FATAL] Invalid --end-date value: '{args[i + 1]}'. Expected ISO-8601 (e.g., 2026-04-20T21:36:00Z).");
            return;
        }
    }
}

if (!File.Exists(configPath))
{
    Console.WriteLine($"[ERROR] Config file not found: {Path.GetFullPath(configPath)}");
    return;
}
var config = MarketConfig.Load(configPath);

// Pre-allocate thread pool for maximum CPU utilization during parallel evaluation
ThreadPool.SetMinThreads(Environment.ProcessorCount, Environment.ProcessorCount);

Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║          THE SEED — MARKET EVOLUTION ENGINE                  ║");
Console.WriteLine($"║  Config: {Path.GetFileName(configPath),-50}║");
Console.WriteLine($"║  Mode: {config.Mode,-52}║");
Console.WriteLine($"║  Capital: ${config.InitialCapital,-48:N0}║");
Console.WriteLine($"║  Population: {config.PopulationSize,-46}║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");

Directory.CreateDirectory(config.OutputDirectory);

switch (config.Mode)
{
    case ExecutionMode.Backtest:
        await RunBacktest(config, configPath, cliEndDate);
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
static async Task RunBacktest(MarketConfig config, string configPath, DateTimeOffset? fixedEnd = null, bool resetStagnation = false)
{
    Console.WriteLine("\n[BACKTEST] Downloading historical data...");

    var runner = new BacktestRunner(config);
    var end = fixedEnd ?? DateTimeOffset.UtcNow.AddHours(-1);
    var start = end.AddHours(-config.TrainingWindowHours - config.ValidationWindowHours);

    var (snapshots, prices, rawVolumes, rawFundingRates, enrichReport) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);
    enrichReport?.PrintSummary();
    enrichReport?.SaveManifest(runner.CacheDir);
    Console.WriteLine($"[BACKTEST] Loaded {snapshots.Length} candles ({start:yyyy-MM-dd} to {end:yyyy-MM-dd})");

    int bph = config.BarsPerHour;
    int trainBars = config.TrainingWindowHours * bph;
    int valBars = config.ValidationWindowHours * bph;
    int trainLen = Math.Min(trainBars, snapshots.Length - valBars);
    int valStart = trainLen;
    int valEnd = snapshots.Length;

    var trainSnapshots = snapshots[..trainLen];
    var trainPrices = prices[..trainLen];
    var trainRawVolumes = rawVolumes[..trainLen];
    var trainRawFunding = rawFundingRates[..trainLen];
    var valSnapshots = snapshots[valStart..valEnd];
    var valPrices = prices[valStart..valEnd];
    var valRawVolumes = rawVolumes[valStart..valEnd];
    var valRawFunding = rawFundingRates[valStart..valEnd];

    Console.WriteLine($"[BACKTEST] Training: {trainLen} bars ({trainLen / bph}h), Validation: {valEnd - valStart} bars ({(valEnd - valStart) / bph}h)");

    var observatory = new FileObservatory(Path.Combine(config.OutputDirectory, "events.jsonl"));
    var evolution = new MarketEvolution(config, observatory);

    var checkpointDir = Path.Combine(config.OutputDirectory, "checkpoints");
    Directory.CreateDirectory(checkpointDir);

    int startGen = 0;
    float bestEverFitness = float.MinValue;
    int walkForwardOffset = 0;
    int stallCount = 0;
    int lastInnovationId = 0;

    var latestCheckpoint = CheckpointState.FindLatest(checkpointDir);
    if (latestCheckpoint != null)
    {
        Console.WriteLine($"[BACKTEST] Resuming from checkpoint: {Path.GetFileName(latestCheckpoint)}");
        var cp = CheckpointState.Load(latestCheckpoint);
        var restored = cp.RestorePopulation();

        var speciesState = cp.SpeciesState
            .Select(s => (s.SpeciesId, (IGenome)SeedGenome.FromJson(s.RepresentativeGenomeJson), s.StagnationCounter, s.BestFitness))
            .ToList();
        var archiveState = cp.ArchiveState
            .Select(a => (a.SpeciesId, (IGenome)SeedGenome.FromJson(a.GenomeJson), a.Fitness))
            .ToList();

        evolution.InitializeFrom(restored, cp.Generation,
            cp.NextInnovationId, cp.NextCppnNodeId, cp.CompatibilityThreshold,
            speciesState, cp.NextSpeciesId, archiveState);
        startGen = cp.Generation;
        bestEverFitness = cp.BestFitness;
        walkForwardOffset = cp.WalkForwardOffset;
        stallCount = cp.StallCount;
        lastInnovationId = cp.NextInnovationId;
        Console.WriteLine($"[BACKTEST] Restored generation {cp.Generation}, best fitness {cp.BestFitness:F4}");
        Console.WriteLine($"[BACKTEST] Walk-forward offset: {cp.WalkForwardOffset}h, stall count: {cp.StallCount}");
        Console.WriteLine($"[BACKTEST] Restored {cp.SpeciesState.Count} species, archive: {cp.ArchiveState.Count} elites");

        if (resetStagnation)
        {
            evolution.ResetSpeciesStagnation();
            Console.WriteLine("[BACKTEST] Phase transition: reset species stagnation and cleared archive");
        }
    }
    else
    {
        evolution.Initialize();
    }

    Console.WriteLine($"\n{"Gen",-5} {"Best",8} {"Mean",8} {"Med",8} {"Sharpe",8} {"Sortino",8} {"Return",8} {"WR",5} {"Trades",7} {"DD%",6} {"CVaR",7} {"Sp",3} {"Inact%",6} {"ValFit",8} {"Delta",9} {"Time",6}");
    Console.WriteLine(new string('─', 130));

    int evalWindow = Math.Min(config.EvalWindowHours * bph, trainLen);
    var valEvaluator = new MarketEvaluator(config);

    float bestValFitness = float.MinValue;
    string? bestValGenomePath = null;
    var validationHistory = new List<(int Gen, float TrainFit, float ValFit)>();
    int consecutiveValDeclines = 0;

    int k = Math.Max(1, config.EvalWindowCount);

    float prevBestFitness = float.MinValue;
    var genStopwatch = new System.Diagnostics.Stopwatch();

    // S6 — Auto-analyzer subprocess tracker. Holds the most recent fire-and-forget
    // `dotnet run --project tools/Seed.CheckpointEval` process so the next checkpoint
    // can decide whether to start a new one (only if previous has exited). Prevents
    // subprocess stacking when checkpoint cadence beats analyzer runtime.
    System.Diagnostics.Process? autoAnalyzeProcess = null;
    int autoAnalyzeFailCount = 0;

    string autoAnalyzeOutputRoot = config.AutoAnalyzeOutputDir
        ?? Path.Combine(config.OutputDirectory, "auto_analyses");

    for (int gen = startGen; gen < config.Generations; gen++)
    {
        genStopwatch.Restart();
        GenerationReport report;

        if (walkForwardOffset >= trainLen) break;
        int remainingLen = trainLen - walkForwardOffset;
        var wfSnaps = trainSnapshots[walkForwardOffset..(walkForwardOffset + remainingLen)];
        var wfPrices = trainPrices[walkForwardOffset..(walkForwardOffset + remainingLen)];
        var wfRawVols = trainRawVolumes[walkForwardOffset..(walkForwardOffset + remainingLen)];
        var wfRawFund = trainRawFunding[walkForwardOffset..(walkForwardOffset + remainingLen)];
        int wfEvalWindow = Math.Min(evalWindow, remainingLen);

        if (k <= 1)
        {
            int maxOff = Math.Max(1, remainingLen - wfEvalWindow);
            int offset = config.WalkForwardEnabled ? 0 : (gen * config.RollingStepHours * bph) % maxOff;
            int windowEnd = Math.Min(offset + wfEvalWindow, remainingLen);
            var windowSnaps = wfSnaps[offset..windowEnd];
            var windowPrices = wfPrices[offset..windowEnd];
            var windowRawVols = wfRawVols[offset..windowEnd];
            var windowRawFund = wfRawFund[offset..windowEnd];
            if (windowSnaps.Length < 50) { windowSnaps = wfSnaps; windowPrices = wfPrices; windowRawVols = wfRawVols; windowRawFund = wfRawFund; }
            report = evolution.RunGeneration(windowSnaps, windowPrices, windowRawVols, windowRawFund);
        }
        else
        {
            int windowEpoch = config.WindowStabilityGens > 1
                ? gen / config.WindowStabilityGens
                : gen;
            var diverseWindows = RegimeDetector.SelectDiverseWindows(
                wfPrices, remainingLen, wfEvalWindow, k, windowEpoch, config.RunSeed);
            var windowList = new (SignalSnapshot[], float[], float[], float[])[diverseWindows.Length];
            for (int w = 0; w < diverseWindows.Length; w++)
            {
                var (off, len, _) = diverseWindows[w];
                int end2 = Math.Min(off + len, remainingLen);
                if (end2 - off < 50) { off = 0; end2 = Math.Min(wfEvalWindow, remainingLen); }
                windowList[w] = (wfSnaps[off..end2], wfPrices[off..end2], wfRawVols[off..end2], wfRawFund[off..end2]);
            }
            report = evolution.RunGeneration(windowList);
        }

        string valFitStr = "";
        bool isValGen = config.ValidationIntervalGens > 0 &&
                        gen > 0 && gen % config.ValidationIntervalGens == 0 &&
                        valSnapshots.Length >= 50;

        if (isValGen)
        {
            // B5 — Evaluate top-N training-best genomes on validation; pick the one with highest ValFit.
            // Old behavior preserved when WalkForwardTopN <= 1.
            int wfTopN = Math.Max(1, config.WalkForwardTopN);
            List<IGenome> topCandidates = wfTopN > 1
                ? evolution.GetTopNByTrainingFitness(wfTopN)
                : (evolution.GetBestGenome() is { } only ? new List<IGenome> { only } : new List<IGenome>());

            if (topCandidates.Count > 0)
            {
                // Parallel eval to amortize cost; preserves determinism since EvaluateSingle is deterministic.
                var topNValResults = new MarketEvalResult[topCandidates.Count];
                Parallel.For(0, topCandidates.Count,
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Min(topCandidates.Count, Environment.ProcessorCount) },
                    idx => { topNValResults[idx] = valEvaluator.EvaluateSingle(topCandidates[idx], valSnapshots, valPrices, valRawVolumes, valRawFunding, gen); });

                int bestIdx = 0;
                for (int ci = 1; ci < topNValResults.Length; ci++)
                    if (topNValResults[ci].Fitness.Fitness > topNValResults[bestIdx].Fitness.Fitness) bestIdx = ci;

                var bestGenome = topCandidates[bestIdx];
                float valFit = topNValResults[bestIdx].Fitness.Fitness;
                valFitStr = $"{valFit,8:F4}";
                validationHistory.Add((gen, report.BestFitness, valFit));

                if (wfTopN > 1)
                {
                    var all = string.Join(" ", topNValResults.Select(r => r.Fitness.Fitness.ToString("F3")));
                    Console.WriteLine($"  [WF-TOPN] Evaluated {topNValResults.Length}: [{all}] → best ValFit {valFit:F4}");
                }

                if (valFit > bestValFitness)
                {
                    bestValFitness = valFit;
                    bestValGenomePath = Path.Combine(checkpointDir, $"best_val_gen_{gen:D4}.json");
                    File.WriteAllText(bestValGenomePath, bestGenome.ToJson());
                    consecutiveValDeclines = 0;

                    // B4 — Inject a deep-copied clone of the best-val genome (with a fresh
                    // deterministic GenomeId) back into the population, replacing the worst-
                    // training-fit member. Same network structure, new identity — protects the
                    // genome from evolutionary loss without creating a duplicate-ID corruption.
                    // The new ID is derived deterministically from (RunSeed, gen, source ID hash)
                    // so the same training run always produces the same injection IDs.
                    if (config.ProtectBestValInPop && bestGenome is SeedGenome sg)
                    {
                        // Domain tag for protection-clone ID derivation. Chosen to not collide
                        // with SeedDerivation's existing domain constants.
                        const ulong DOMAIN_PROTECT_INJECT = 0x50524F5445435401UL; // "PROTECT\1"
                        ulong sourceIdLo = (ulong)(uint)sg.GenomeId.GetHashCode();
                        ulong seedA = SeedDerivation.DeriveSeed(config.RunSeed, DOMAIN_PROTECT_INJECT, (ulong)gen, sourceIdLo, 0);
                        ulong seedB = SeedDerivation.DeriveSeed(config.RunSeed, DOMAIN_PROTECT_INJECT, (ulong)gen, sourceIdLo, 1);
                        var idBytes = new byte[16];
                        BitConverter.TryWriteBytes(idBytes.AsSpan(0, 8), seedA);
                        BitConverter.TryWriteBytes(idBytes.AsSpan(8, 8), seedB);
                        var newGenomeId = new Guid(idBytes);

                        var clone = sg.CloneGenome(newGenomeId);
                        if (evolution.InjectGenomeIntoPopulation(clone))
                            Console.WriteLine($"  [PROTECT] Best-val genome cloned (new id {newGenomeId.ToString()[..8]}…) into population");
                    }
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
                    int stepBars = config.RollingStepHours * bph;
                    if (valFit >= config.WalkForwardMinValFitness)
                    {
                        walkForwardOffset = Math.Min(walkForwardOffset + stepBars, maxWfOffset);
                        stallCount = 0;
                        Console.WriteLine($"  [WALK-FWD] Passed ({valFit:F4}), advanced to {walkForwardOffset} bars");
                    }
                    else
                    {
                        stallCount++;
                        Console.WriteLine($"  [WALK-FWD] Failed ({valFit:F4}), stalled {stallCount}/{config.WalkForwardMaxStallGens}");
                        if (stallCount >= config.WalkForwardMaxStallGens)
                        {
                            walkForwardOffset = Math.Min(walkForwardOffset + stepBars, maxWfOffset);
                            stallCount = 0;
                            Console.WriteLine($"  [WALK-FWD] Force-advanced after max stall");
                        }
                    }
                }
            }
        }

        genStopwatch.Stop();
        float inactPct = report.PopulationSize > 0 ? (float)report.InactiveCount / report.PopulationSize * 100f : 0f;
        float fitDelta = report.BestFitness - prevBestFitness;
        string deltaStr = prevBestFitness > float.MinValue ? $"{fitDelta:+0.0000;-0.0000}" : "     —";
        prevBestFitness = report.BestFitness;

        Console.WriteLine(
            $"{gen,-5} {report.BestFitness,8:F4} {report.MeanFitness,8:F4} {report.MedianFitness,8:F4} " +
            $"{report.BestSharpe,8:F2} {report.BestSortino,8:F2} {report.BestReturn,8:P1} {report.BestWinRate,5:P0} " +
            $"{report.BestTrades,7} {report.BestMaxDrawdown,6:P1} {report.BestCVaR5,7:F4} " +
            $"{report.SpeciesCount,3} {inactPct,5:F0}% {valFitStr} {deltaStr} {genStopwatch.Elapsed.TotalSeconds,5:F1}s");

        if (report.NaNFitnessCount > 0)
            Console.Error.WriteLine($"  [WARNING] {report.NaNFitnessCount} genome(s) produced NaN fitness this generation");
        if (report.WorstFitness < -0.5f)
            Console.Error.WriteLine($"  [WARNING] Worst fitness {report.WorstFitness:F4} — severe underperformance in population");

        var (bestActive, posCount, negCount, minRet, maxRet) = evolution.GetActiveStats();
        if (bestActive is { } ba)
            Console.WriteLine($"  [active] best:{ba.Fitness:F4} ret:{ba.ReturnPct:P1} trd:{ba.TotalTrades} wr:{ba.WinRate:P0} dd:{ba.MaxDrawdown:P1} ddDur:{ba.MaxDrawdownDuration:P1} | pos:{posCount} neg:{negCount} retRange:[{minRet:P1}..{maxRet:P1}]");

        // V11d Fix 7: population-wide return distribution + best-genome output stats + close reasons
        var (popPos, popZero, popNeg) = evolution.GetPopulationReturnDistribution();
        Console.WriteLine($"  [returns] pos:{popPos} zero:{popZero} neg:{popNeg}");

        var bestEvalResult = evolution.GetBestEvalResult();
        if (bestEvalResult is { } ber)
        {
            if (ber.OutputObs is { } obs && obs.Means.Length >= 11 && obs.TickCount > 0)
            {
                // Compact one-line summary: μ:σ for each of 11 outputs (dir/size/urg/exit/pred/lev/partial/trail/dist/tp/sl)
                Console.WriteLine(
                    $"  [outputs] dir:{obs.Means[0]:F2}:{obs.Stds[0]:F2} sz:{obs.Means[1]:F2}:{obs.Stds[1]:F2} " +
                    $"urg:{obs.Means[2]:F2}:{obs.Stds[2]:F2} ex:{obs.Means[3]:F2}:{obs.Stds[3]:F2} " +
                    $"pr:{obs.Means[4]:F2}:{obs.Stds[4]:F2} lv:{obs.Means[5]:F2}:{obs.Stds[5]:F2} " +
                    $"prt:{obs.Means[6]:F2}:{obs.Stds[6]:F2} tre:{obs.Means[7]:F2}:{obs.Stds[7]:F2} " +
                    $"trd:{obs.Means[8]:F2}:{obs.Stds[8]:F2} tp:{obs.Means[9]:F2}:{obs.Stds[9]:F2} " +
                    $"sl:{obs.Means[10]:F2}:{obs.Stds[10]:F2}");
            }
            if (ber.CloseReasonCounts is { } crc && crc.Sum() > 0)
            {
                int total = crc.Sum();
                Console.WriteLine(
                    $"  [closes] total:{total} | dirFlip:{crc[(int)CloseReason.DirectionFlip]} " +
                    $"exit:{crc[(int)CloseReason.ExitSignal]} cfgSL:{crc[(int)CloseReason.StopLoss]} " +
                    $"brainSL:{crc[(int)CloseReason.BrainStopLoss]} TP:{crc[(int)CloseReason.TakeProfit]} " +
                    $"trail:{crc[(int)CloseReason.TrailingStop]} partial:{crc[(int)CloseReason.PartialClose]} " +
                    $"kill:{crc[(int)CloseReason.KillSwitch]} eos:{crc[(int)CloseReason.EndOfSession]}");
            }
        }

        bool isDetailGen = config.CheckpointIntervalGens > 0 && (gen + 1) % config.CheckpointIntervalGens == 0;
        if (isDetailGen)
        {
            int innovDelta = report.InnovationId - lastInnovationId;
            lastInnovationId = report.InnovationId;
            Console.WriteLine(
                $"  [detail] DDDur:{report.BestMaxDrawdownDuration:P1}  PopTrd:{report.TotalTrades}  " +
                $"MedTrd:{report.MedianTradesPerAgent:F0}  ActAg:{report.TradingAgentCount}  MaxTrd:{report.MaxTradesPerAgent}  " +
                $"MaxStag:{report.MaxSpeciesStagnation}/{config.StagnationLimit}  CtAdj:{report.CompatibilityThreshold:F2}  " +
                $"Arch:{report.ArchiveSize}  Edges:{report.BestBrainActiveEdges}/{report.BestBrainTotalEdges}  " +
                $"Sat:{report.BestBrainSaturation:P0}  Innov:+{innovDelta}  Shrk:{report.BestShrinkageConfidence:F2}");
        }

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
            var cpSpeciesState = evolution.GetSpeciesState()
                .Select(s => new SpeciesCheckpointEntry(s.SpeciesId, s.RepresentativeJson, s.StagnationCounter, s.BestFitness))
                .ToList();
            var cpArchiveState = evolution.Archive.Champions
                .Select(kv => new ArchiveCheckpointEntry(kv.Key, kv.Value.Genome.ToJson(), kv.Value.Fitness))
                .ToList();

            var cp = CheckpointState.FromPopulation(evolution.Population, gen + 1, bestEverFitness,
                evolution.GetSpeciesIds(),
                evolution.Innovations.NextInnovationId, evolution.Innovations.NextCppnNodeId,
                evolution.CompatibilityThreshold,
                walkForwardOffset, stallCount,
                cpSpeciesState, evolution.NextSpeciesId, cpArchiveState);
            string cpPath = Path.Combine(checkpointDir, $"checkpoint_{gen + 1:D4}.json");
            cp.Save(cpPath);
            Console.WriteLine($"  [checkpoint saved: gen {gen + 1}]");

            // S6 — fire-and-forget analyzer subprocess if enabled and prior is finished.
            if (config.AutoAnalyzeOnCheckpoint)
            {
                if (autoAnalyzeProcess != null && !autoAnalyzeProcess.HasExited)
                {
                    Console.WriteLine($"  [auto-analyze SKIP gen {gen + 1}: prior subprocess (pid {autoAnalyzeProcess.Id}) still running]");
                }
                else
                {
                    if (autoAnalyzeProcess != null && autoAnalyzeProcess.ExitCode != 0)
                    {
                        autoAnalyzeFailCount++;
                        Console.WriteLine($"  [auto-analyze WARN: prior subprocess exited with code {autoAnalyzeProcess.ExitCode} (consecutive fails: {autoAnalyzeFailCount})]");
                    }
                    else if (autoAnalyzeProcess != null)
                    {
                        autoAnalyzeFailCount = 0;  // success resets the counter
                    }
                    autoAnalyzeProcess?.Dispose();

                    string analysisOutDir = Path.Combine(autoAnalyzeOutputRoot, $"analysis_{gen + 1:D4}");
                    Directory.CreateDirectory(analysisOutDir);
                    string analysisLogPath = Path.Combine(autoAnalyzeOutputRoot, $"analysis_{gen + 1:D4}_run.log");

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "dotnet",
                        WorkingDirectory = Directory.GetCurrentDirectory(),
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };
                    psi.ArgumentList.Add("run");
                    psi.ArgumentList.Add("--project");
                    psi.ArgumentList.Add("tools/Seed.CheckpointEval");
                    psi.ArgumentList.Add("--no-build");
                    psi.ArgumentList.Add("--");
                    psi.ArgumentList.Add("--checkpoint"); psi.ArgumentList.Add(cpPath);
                    psi.ArgumentList.Add("--config");     psi.ArgumentList.Add(configPath);
                    psi.ArgumentList.Add("--output");     psi.ArgumentList.Add(analysisOutDir);
                    if (fixedEnd.HasValue)
                    {
                        psi.ArgumentList.Add("--end-date");
                        psi.ArgumentList.Add(fixedEnd.Value.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                    }
                    if (!string.IsNullOrEmpty(config.DataCacheDirectory))
                    {
                        psi.ArgumentList.Add("--cache-dir");
                        psi.ArgumentList.Add(config.DataCacheDirectory);
                    }

                    try
                    {
                        var proc = System.Diagnostics.Process.Start(psi)
                            ?? throw new InvalidOperationException("Process.Start returned null");
                        // Pipe stdout+stderr to per-checkpoint log file (async, fire-and-forget).
                        var logFile = new StreamWriter(analysisLogPath, append: false) { AutoFlush = true };
                        proc.OutputDataReceived += (_, e) => { if (e.Data != null) logFile.WriteLine(e.Data); };
                        proc.ErrorDataReceived  += (_, e) => { if (e.Data != null) logFile.WriteLine($"[STDERR] {e.Data}"); };
                        proc.BeginOutputReadLine();
                        proc.BeginErrorReadLine();
                        proc.Exited += (_, _) => { try { logFile.Dispose(); } catch { } };
                        proc.EnableRaisingEvents = true;
                        autoAnalyzeProcess = proc;
                        Console.WriteLine($"  [auto-analyze STARTED gen {gen + 1}: pid {proc.Id} → {analysisLogPath}]");
                    }
                    catch (Exception ex)
                    {
                        autoAnalyzeFailCount++;
                        Console.Error.WriteLine($"  [auto-analyze FAIL gen {gen + 1}: {ex.Message} (consecutive fails: {autoAnalyzeFailCount})]");
                    }
                }
            }
        }
    }

    // S11 — Final validation run includes BOTH the live population AND the elite archive.
    // The archive holds species champions that the population may have bred away by phase end
    // (Phase 4 minimal post-mortem found archive sp.6 +0.9456 was the true best, hidden behind
    // pop[126] +0.6714 because end-phase eval only scanned the population). Union by GenomeId,
    // dedup, evaluate together, take best — guaranteeing no archived champion is overlooked.
    var valCandidates = evolution.Population
        .Concat(evolution.Archive.Champions.Values.Select(c => c.Genome))
        .GroupBy(g => g.GenomeId)
        .Select(grp => grp.First())
        .ToList();
    var candidateById = valCandidates.ToDictionary(g => g.GenomeId);
    int popCount = evolution.Population.Count;
    int archiveCount = evolution.Archive.Champions.Count;
    int unionCount = valCandidates.Count;

    Console.WriteLine($"\n{"═══ VALIDATION ═══",-74}");
    Console.WriteLine($"  Evaluating union: {popCount} pop + {archiveCount} archive elites = {unionCount} unique candidates");
    var valResults = new BacktestRunner(config)
        .Evaluate(valCandidates, valSnapshots, valPrices, valRawVolumes, valRawFunding, config.Generations);

    var bestValResult = valResults.Values.OrderByDescending(r => r.Fitness.Fitness).First();
    bool bestFromArchive = !evolution.Population.Any(g => g.GenomeId == bestValResult.GenomeId);
    Console.WriteLine($"  Best validation fitness: {bestValResult.Fitness.Fitness:F4} (source: {(bestFromArchive ? "ARCHIVE" : "population")})");
    Console.WriteLine($"  Sharpe (adjusted): {bestValResult.Fitness.AdjustedSharpe:F2}");
    Console.WriteLine($"  Return: {bestValResult.Fitness.ReturnPct:P2}");
    Console.WriteLine($"  Trades: {bestValResult.Fitness.TotalTrades}, Win rate: {bestValResult.Fitness.WinRate:P0}");
    Console.WriteLine($"  Max drawdown: {bestValResult.Fitness.MaxDrawdown:P2}");

    // Resolve the actual best-val genome from the union (population OR archive).
    var bestValPopGenome = candidateById.GetValueOrDefault(bestValResult.GenomeId) as SeedGenome;

    // B3 — Save top-5 by validation across the union (population + archive).
    var top5ByVal = valResults.Values.OrderByDescending(r => r.Fitness.Fitness).Take(5).ToList();
    for (int i = 0; i < top5ByVal.Count; i++)
    {
        if (candidateById.TryGetValue(top5ByVal[i].GenomeId, out var g) && g is SeedGenome sg)
        {
            var p = Path.Combine(config.OutputDirectory, $"top5_val_genome_rank{i + 1:D2}.json");
            File.WriteAllText(p, sg.ToJson());
        }
    }

    var champions = evolution.GetSpeciesChampions();
    float ensembleFitnessRecord = 0f;
    float ensembleReturnPctRecord = 0f;
    float ensembleSharpeRecord = 0f;
    int ensembleTradesRecord = 0;
    if (champions.Count >= 2)
    {
        Console.WriteLine($"\n{"═══ ENSEMBLE ═══",-74}");
        Console.WriteLine($"  Species champions: {champions.Count}");
        var ensembleResult = valEvaluator.EvaluateEnsemble(champions, valSnapshots, valPrices, valRawVolumes, valRawFunding, config.Generations);
        Console.WriteLine($"  Ensemble fitness: {ensembleResult.Fitness:F4}");
        Console.WriteLine($"  Ensemble return: {ensembleResult.ReturnPct:P2}");
        Console.WriteLine($"  Ensemble Sharpe: {ensembleResult.AdjustedSharpe:F2}");
        Console.WriteLine($"  Ensemble trades: {ensembleResult.TotalTrades}");
        ensembleFitnessRecord = ensembleResult.Fitness;
        ensembleReturnPctRecord = ensembleResult.ReturnPct;
        ensembleSharpeRecord = ensembleResult.AdjustedSharpe;
        ensembleTradesRecord = ensembleResult.TotalTrades;
    }

    // B2 — Save species-champion ensemble at phase end for future loading / deployment
    if (champions.Count > 0)
    {
        var ensembleBundle = new
        {
            phase = config.OutputDirectory,
            generation = config.Generations,
            championCount = champions.Count,
            champions = champions.Cast<SeedGenome>().Select(c => c.ToJson()).ToArray(),
            ensembleFitness = ensembleFitnessRecord,
            ensembleReturnPct = ensembleReturnPctRecord,
            ensembleSharpe = ensembleSharpeRecord,
            ensembleTrades = ensembleTradesRecord,
        };
        var ensemblePath = Path.Combine(config.OutputDirectory, "ensemble_champions.json");
        var ensembleOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        File.WriteAllText(ensemblePath, JsonSerializer.Serialize(ensembleBundle, ensembleOpts));
        Console.WriteLine($"  Ensemble champions saved to: {ensemblePath}");
    }

    // Save the full-population best-val genome (the one whose stats were printed above)
    // to the canonical deploy path. This is the single source of truth for deployment.
    // Two-tier fallback handles edge cases without ambiguity.
    if (bestValPopGenome != null)
    {
        File.WriteAllText(config.ResolvedGenomePath, bestValPopGenome.ToJson());
        Console.WriteLine($"\n  Best genome (by validation, full-pop) saved to: {config.ResolvedGenomePath}");
    }
    else if (bestValGenomePath != null && File.Exists(bestValGenomePath))
    {
        File.Copy(bestValGenomePath, config.ResolvedGenomePath, overwrite: true);
        Console.WriteLine($"\n  Best genome (in-training tracked, fallback) saved to: {config.ResolvedGenomePath}");
    }
    else
    {
        var finalBest = evolution.GetBestGenome();
        if (finalBest != null)
        {
            File.WriteAllText(config.ResolvedGenomePath, finalBest.ToJson());
            Console.WriteLine($"\n  Best genome (by training, last-resort fallback) saved to: {config.ResolvedGenomePath}");
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

    // B6 — Signal-count validation: ensure agent's expected input size matches live signal producer.
    // Prevents silent brain/feed mismatch that would otherwise produce undefined behavior.
    int agentInputCount = MarketAgent.InputCount;
    int aggregatorSignalCount = SignalIndex.Count;
    if (agentInputCount != aggregatorSignalCount)
    {
        Console.Error.WriteLine($"[FATAL] Signal-count mismatch: agent expects {agentInputCount}, SignalIndex provides {aggregatorSignalCount}.");
        Console.Error.WriteLine("[FATAL] A genome trained under a different signal layout cannot be safely deployed. Aborting.");
        return;
    }
    Console.WriteLine($"[PAPER] Signal-count check passed: {aggregatorSignalCount} signals.");

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
    var graph = developer.CompileGraph(genome, paperBudget, devCtx,
        MarketEvaluator.SignalCategoryMap, MarketEvaluator.RegimeStart, MarketEvaluator.RegimeEnd);
    var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
    Console.WriteLine($"[PAPER] Brain compiled: {graph.NodeCount} neurons, {graph.EdgeCount} synapses");

    var trader = new PaperTrader(config);
    var agent = new MarketAgent(genome.GenomeId, brain, trader, maxLeverage: config.MaxLeverage, explicitExitBonus: config.ExplicitExitBonus, peakExitBonus: config.PeakExitBonus);

    Console.WriteLine("[PAPER] Connecting to live data feeds...");
    using var aggregator = new DataAggregator(config);
    using var tradeLog = new TradeLogger(config.ResolvedTradeLogPath);

    Console.WriteLine($"[PAPER] Trade log: {config.ResolvedTradeLogPath}");
    Console.WriteLine($"[PAPER] Brain decisions run every {config.CandleInterval} (matching training regime).");
    Console.WriteLine($"[PAPER] Stop-loss: {(config.StopLossPct > 0 ? $"{config.StopLossPct:P1} per position" : "disabled")}");
    Console.WriteLine("[PAPER] Paper trading active. Press Ctrl+C to stop.\n");

    Console.WriteLine($"{"Feed",-7} {"Price",11} {"Pos",6} {"Entry",11} {"Unrl%",7} {"Net P&L",10} {"Equity",12} {"Trades",7} {"WR",5} {"Exit",5} {"ExitRaw",8} {"SizeRaw",8} {"Lev",6} {"GateMin",8} {"GateMax",8} {"rSharpe",8} {"rDD",6} {"Elapsed",8}");
    Console.WriteLine(new string('─', 150));
    var rolling = new RollingMetrics(100);

    var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

    int feedTick = 0;
    int decisionTick = 0;
    long lastBarPeriod = -1;
    int prevTradeCount = 0;
    var lastDisplay = DateTimeOffset.MinValue;
    var lastHeartbeat = DateTimeOffset.MinValue;
    var sessionStart = DateTimeOffset.UtcNow;

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

            long currentBarPeriod = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / config.BarDurationMs;
            bool isDecisionTick = lastBarPeriod == -1 || currentBarPeriod != lastBarPeriod;

            if (isDecisionTick)
            {
                lastBarPeriod = currentBarPeriod;
                float elapsedHours = (float)(DateTimeOffset.UtcNow - sessionStart).TotalHours;
                var ctx = new TickContext(price, (decimal)aggregator.LastRawVolume, aggregator.LastRawFundingRate, decisionTick, elapsedHours);
                agent.ProcessTick(snapshot, ctx);

                Console.WriteLine($"  [BRAIN] Decision #{decisionTick} (every {config.CandleInterval}) at {DateTimeOffset.UtcNow:HH:mm:ss} UTC | price ${price:N2}");
                decisionTick++;

                // Log every position opened this tick — handles direction flips (close+open in same tick)
                foreach (var pos in agent.Portfolio.OpenPositions)
                {
                    if (pos.OpenTick == ctx.TickIndex)
                        Console.WriteLine(
                            $"  >>> OPENED {pos.Direction} @ ${pos.EntryPrice:N2} | Size {pos.Size:F6} BTC");
                }

                if (agent.Portfolio.TradeHistory.Count > prevTradeCount)
                {
                    for (int i = prevTradeCount; i < agent.Portfolio.TradeHistory.Count; i++)
                    {
                        var closed = agent.Portfolio.TradeHistory[i];
                        Console.WriteLine(
                            $"  >>> CLOSED {closed.Direction} | Entry ${closed.EntryPrice:N2} -> Exit ${closed.ExitPrice:N2} | " +
                            $"P&L {closed.Pnl:+#,##0.00;-#,##0.00} | Fee {closed.Fee:F2} | Held {closed.HoldingTicks} ticks");
                        tradeLog.LogTrade(closed);
                    }
                    prevTradeCount = agent.Portfolio.TradeHistory.Count;
                }
            }

            // Stop-loss now handled inside PaperTrader.ProcessSignal()

            agent.Portfolio.RecordEquity(price);
            rolling.Add((float)agent.Portfolio.Equity(price));

            var now = DateTimeOffset.UtcNow;
            if ((now - lastDisplay).TotalMilliseconds >= config.DisplayIntervalMs)
            {
                lastDisplay = now;
                var portfolio = agent.Portfolio;
                decimal equity = portfolio.Equity(price);
                string posStr = portfolio.OpenPositions.Count > 0
                    ? portfolio.OpenPositions[0].Direction == TradeDirection.Long ? "LONG" : "SHORT"
                    : "FLAT";
                string entry = portfolio.OpenPositions.Count > 0
                    ? $"${portfolio.OpenPositions[0].EntryPrice,9:N2}" : $"{"---",11}";
                string unrlPct = portfolio.OpenPositions.Count > 0
                    ? $"{(float)portfolio.OpenPositions[0].UnrealizedPnlPct(price) / 100f,6:+0.0%;-0.0%}" : $"{"",7}";
                string exitFlag = agent.LastGeneratedSignal.ExitCurrent ? " EXIT" : "     ";
                var elapsed = now - sessionStart;
                string elapsedStr = $"{(int)elapsed.TotalHours}:{elapsed.Minutes:D2}";

                var lastSig = agent.LastGeneratedSignal;
                var bDiag = brain.GetDiagnostics();
                Console.WriteLine(
                    $"{feedTick,-7} ${price,10:N2} {posStr,6} {entry} {unrlPct} " +
                    $"{portfolio.TotalPnl,9:+#,##0.00;-#,##0.00;0.00} " +
                    $"${equity,10:N2} {portfolio.TotalTrades,7} " +
                    $"{portfolio.WinRate,4:P0} {exitFlag} {lastSig.RawExitValue,8:F4} {lastSig.RawSizePct,8:F4} {lastSig.Leverage,6:F2} {bDiag.GateMin,8:F4} {bDiag.GateMax,8:F4} {rolling.RollingSharpe,8:F2} {rolling.RollingDrawdown,5:P1} {elapsedStr,8}");

                if (portfolio.KillSwitchTriggered)
                {
                    Console.WriteLine("\n  [KILL SWITCH] Max drawdown exceeded — trading halted, monitoring continues.");
                }
            }

            if ((DateTimeOffset.UtcNow - lastHeartbeat).TotalSeconds >= 60)
            {
                var sig = agent.LastGeneratedSignal;
                var hbDiag = brain.GetDiagnostics();
                string dir = sig.Direction == TradeDirection.Long ? "Long" : sig.Direction == TradeDirection.Short ? "Short" : "Flat";
                var hb = $"{{\"ts\":\"{DateTimeOffset.UtcNow:O}\",\"equity\":{agent.Portfolio.Equity(price):F2}," +
                         $"\"balance\":{agent.Portfolio.Balance:F2}," +
                         $"\"trades\":{agent.Portfolio.TotalTrades},\"positions\":{agent.Portfolio.OpenPositions.Count}," +
                         $"\"pnl\":{agent.Portfolio.TotalPnl:F2},\"maxDD\":{(float)agent.Portfolio.MaxDrawdown:F4}," +
                         $"\"health\":\"{snapshot.Health}\",\"dir\":\"{dir}\",\"exit\":{(sig.ExitCurrent ? "true" : "false")}," +
                         $"\"rawExit\":{sig.RawExitValue:F4},\"rawSize\":{sig.RawSizePct:F4}," +
                         $"\"leverage\":{sig.Leverage:F4},\"rawLeverage\":{sig.RawLeverage:F4}," +
                         $"\"gateCount\":{hbDiag.GateCount},\"gateMean\":{hbDiag.GateMean:F4}," +
                         $"\"gateMin\":{hbDiag.GateMin:F4},\"gateMax\":{hbDiag.GateMax:F4}," +
                         $"\"satRate\":{hbDiag.SaturationRate:F4}," +
                         $"\"rSharpe\":{rolling.RollingSharpe:F2},\"rDD\":{rolling.RollingDrawdown:F4}}}";
                Directory.CreateDirectory(config.OutputDirectory);
                File.AppendAllText(Path.Combine(config.OutputDirectory, "heartbeat.jsonl"), hb + "\n");
                lastHeartbeat = DateTimeOffset.UtcNow;
            }

            if ((DateTimeOffset.UtcNow - aggregator.LastTickTime).TotalMinutes > 5)
                Console.WriteLine($"  [STALE] Data feed not updated for {(DateTimeOffset.UtcNow - aggregator.LastTickTime).TotalMinutes:F0} minutes");

            if (snapshot.Health != Seed.Market.Signals.SignalHealth.Full)
                Console.WriteLine($"  [SIGNAL] Health: {snapshot.Health} — some feeds degraded");

            feedTick++;
            await Task.Delay(config.SpotPollMs, cts.Token);
        }
        catch (OperationCanceledException) { break; }
        catch (Exception ex)
        {
            Console.WriteLine($"[PAPER] Feed error: {ex.Message}");
            await Task.Delay(5000, cts.Token);
        }
    }

    decimal lastMarketPrice = (decimal)aggregator.LastRawBtcPrice;
    if (lastMarketPrice <= 0) lastMarketPrice = config.InitialCapital;
    trader.CloseAllPositions(agent.Portfolio, lastMarketPrice, decisionTick);

    var sessionDuration = DateTimeOffset.UtcNow - sessionStart;
    Console.WriteLine($"\n{"═══ SESSION SUMMARY ═══",-64}");
    Console.WriteLine($"  Session time:    {sessionDuration.TotalHours:F1}h");
    Console.WriteLine($"  Feed ticks:      {feedTick}");
    Console.WriteLine($"  Brain decisions: {decisionTick}");
    Console.WriteLine($"  Total trades:    {agent.Portfolio.TotalTrades}");
    Console.WriteLine($"  Win rate:        {agent.Portfolio.WinRate:P0}");
    Console.WriteLine($"  Net P&L:         {agent.Portfolio.TotalPnl:+#,##0.00;-#,##0.00;0.00}");
    Console.WriteLine($"  Return:          {(agent.Portfolio.InitialBalance > 0 ? agent.Portfolio.TotalPnl / agent.Portfolio.InitialBalance : 0m):P2}");
    Console.WriteLine($"  Peak equity:     ${agent.Portfolio.MaxEquity:N2}");
    Console.WriteLine($"  Max drawdown:    {agent.Portfolio.MaxDrawdown:P2}");
    Console.WriteLine($"  Final balance:   ${agent.Portfolio.Balance:N2}");
    Console.WriteLine($"  Rolling Sharpe:  {rolling.RollingSharpe:F2}");
    if (agent.Portfolio.TradeHistory.Count > 0)
    {
        var trades = agent.Portfolio.TradeHistory;
        Console.WriteLine($"  Avg P&L/trade:   {trades.Average(t => (double)t.Pnl):+#,##0.00;-#,##0.00}");
        Console.WriteLine($"  Best trade:      {trades.Max(t => t.Pnl):+#,##0.00;-#,##0.00}");
        Console.WriteLine($"  Worst trade:     {trades.Min(t => t.Pnl):+#,##0.00;-#,##0.00}");
        Console.WriteLine($"  Avg hold ticks:  {trades.Average(t => t.HoldingTicks):F0}");
    }
    var diag = brain.GetDiagnostics();
    Console.WriteLine($"  Brain sat rate:  {diag.SaturationRate:P1}");
    Console.WriteLine($"  Brain wt drift:  fast={diag.MeanAbsWeightFast:F4} slow={diag.MeanAbsWeightSlow:F4}");
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
    var (snapshots, prices, rawVolumes, rawFundingRates, _) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);

    var genomePath = config.ResolvedGenomePath;
    if (!File.Exists(genomePath))
    {
        Console.WriteLine("[COMPARE] No trained genome found. Run backtest first.");
        return;
    }

    var evaluator = new MarketEvaluator(config);
    var genome = SeedGenome.FromJson(File.ReadAllText(genomePath));

    int windowSize = Math.Min(config.EvalWindowHours * config.BarsPerHour, snapshots.Length / 4);
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
        var wRawVols = rawVolumes[offset..(offset + windowSize)];
        var wRawFund = rawFundingRates[offset..(offset + windowSize)];

        var result = evaluator.EvaluateSingle(genome, wSnaps, wPrices, wRawVols, wRawFund, w);
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
    var (snapshots, prices, rawVolumes, rawFundingRates, _) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);

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
        var graph = developer.CompileGraph(sg, budget, devCtx,
            MarketEvaluator.SignalCategoryMap, MarketEvaluator.RegimeStart, MarketEvaluator.RegimeEnd);
        var brain = new BrainRuntime(graph, sg.Learn, sg.Stable, 1, abl);
        var trader = new PaperTrader(config);
        var agent = new MarketAgent(sg.GenomeId, brain, trader, abl, maxLeverage: config.MaxLeverage, explicitExitBonus: config.ExplicitExitBonus, peakExitBonus: config.PeakExitBonus);
        for (int t = 0; t < snapshots.Length; t++)
        {
            decimal price = (decimal)prices[t];
            if (price <= 0) continue;
            var ctx = new TickContext(price, (decimal)rawVolumes[t], rawFundingRates[t], t, (float)t);
            agent.ProcessTick(snapshots[t], ctx);
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
    var (snapshots, prices, rawVolumes, rawFundingRates, _) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);

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
        var result = evaluator.EvaluateSingle(genome, snapshots, prices, rawVolumes, rawFundingRates, 0);

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
    var (snapshots, prices, rawVolumes, rawFundingRates, _) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);

    var genomePath = config.ResolvedGenomePath;
    if (!File.Exists(genomePath))
    {
        Console.WriteLine("[MC] No trained genome found.");
        return;
    }

    var evaluator = new MarketEvaluator(config);
    var genome = SeedGenome.FromJson(File.ReadAllText(genomePath));
    var evalResult = evaluator.EvaluateSingle(genome, snapshots, prices, rawVolumes, rawFundingRates, 0);

    Console.WriteLine($"[MC] Agent made {evalResult.Fitness.TotalTrades} trades. Bootstrapping 10,000 resamples...");

    var tradePnls = new List<float>();
    var developer = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
    var sg = (SeedGenome)genome;
    var budget = MarketEvaluator.MarketBrainBudget with
    {
        HiddenWidth = sg.Dev.SubstrateWidth,
        HiddenHeight = sg.Dev.SubstrateHeight,
        HiddenLayers = sg.Dev.SubstrateLayers
    };
    var graph = developer.CompileGraph(sg, budget, new DevelopmentContext(config.RunSeed, 0),
        MarketEvaluator.SignalCategoryMap, MarketEvaluator.RegimeStart, MarketEvaluator.RegimeEnd);
    var brain = new BrainRuntime(graph, sg.Learn, sg.Stable, 1);
    var trader = new PaperTrader(config);
    var agent = new MarketAgent(sg.GenomeId, brain, trader, maxLeverage: config.MaxLeverage, explicitExitBonus: config.ExplicitExitBonus, peakExitBonus: config.PeakExitBonus);

    for (int t = 0; t < snapshots.Length; t++)
    {
        decimal price = (decimal)prices[t];
        if (price <= 0) continue;
        var ctx = new TickContext(price, (decimal)rawVolumes[t], rawFundingRates[t], t, (float)t);
        agent.ProcessTick(snapshots[t], ctx);
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
    var (snapshots, prices, rawVolumes, rawFundingRates, _) = await runner.LoadData(config.Symbols[0], start, end, enrich: true);

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
        var graph = developer.CompileGraph(genome, budget, devCtx,
            MarketEvaluator.SignalCategoryMap, MarketEvaluator.RegimeStart, MarketEvaluator.RegimeEnd);
        var brain = new BrainRuntime(graph, lp, genome.Stable, 1);
        var trader = new PaperTrader(config);
        var agent = new MarketAgent(genome.GenomeId, brain, trader, maxLeverage: config.MaxLeverage, explicitExitBonus: config.ExplicitExitBonus, peakExitBonus: config.PeakExitBonus);
        for (int t = 0; t < snapshots.Length; t++)
        {
            decimal price = (decimal)prices[t];
            if (price <= 0) continue;
            var ctx = new TickContext(price, (decimal)rawVolumes[t], rawFundingRates[t], t, (float)t);
            agent.ProcessTick(snapshots[t], ctx);
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

// T2 — Multi-phase RunPipeline removed. The 4-phase pipeline created discontinuous fitness
// changes at phase boundaries (TP1) and forced ResetSpeciesStagnation that cleared the
// elite archive (TP3). The plan replaces it with a single WeightSchedule-driven run:
//   dotnet run --project src/Seed.Market.App -- --config market-config.ceiling.json
// Schedules with multiple waypoints provide continuous fitness annealing without phase
// boundaries. MarketEvolution.ResetSpeciesStagnation remains as a public method but is no
// longer called from any production codepath; it can be invoked by tests/diagnostic tools.
