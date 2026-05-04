using System.Globalization;
using System.Text.Json;
using Seed.Core;
using Seed.Genetics;
using Seed.Market;
using Seed.Market.Backtest;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Observatory;

// Seed.MetricAudit — S10 forensics tool.
//
// Compares 4 evaluation paths for the same set of genomes against the same window.
// Used to diagnose the divergence between in-training fitness reporting and
// analyzer (CheckpointEval) fitness reporting.
//
// Path B (analyzer):     MarketEvaluator.EvaluateSingle  — single-thread per genome.
// Path C (parallel):     MarketEvaluator.Evaluate        — Parallel.For over the batch.
// Path A (single-win):   MarketEvolution.RunGeneration   — population path, EvalWindowCount=1.
// Path D (multi-win):    MarketEvolution.RunGeneration   — population path, K sub-windows averaged
//                                                          + WindowConsistencyWeight (production training).
//
// Hypothesis (from V1-Final-Assessment + plan):
//   * If A == B == C at single-window: state path is sound. Production divergence is purely
//     the multi-window-vs-single difference (Path D vs Path B). Fix = analyzer match-training mode.
//   * If A == C != B: parallel-vs-sequential leaks state in BrainDeveloper or shared scratch.
//     Fix = per-call BrainDeveloper or guard the leak source.
//   * If A != C:      MarketEvolution wrapper has an additional leak (e.g., diversity bonus,
//                     archive update, speciation). Fix at the wrapper.
//
// Usage:
//   dotnet run --project tools/Seed.MetricAudit -- \
//     --checkpoint <path-to-checkpoint_NNNN.json> \
//     --config <path-to-market-config.phaseN.json> \
//     --end-date 2026-04-28T00:00:00Z \
//     [--num-genomes 5] [--output <dir>] [--cache-dir <path>]

string? checkpointPath = null;
string? configPath = null;
string? outputDir = null;
string? endDateStr = null;
string? cacheDir = null;
int numGenomes = 5;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--checkpoint": checkpointPath = args[++i]; break;
        case "--config":     configPath     = args[++i]; break;
        case "--output":     outputDir      = args[++i]; break;
        case "--end-date":   endDateStr     = args[++i]; break;
        case "--cache-dir":  cacheDir       = args[++i]; break;
        case "--num-genomes": numGenomes    = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
        case "-h":
        case "--help":
            PrintUsage();
            return 0;
    }
}

if (checkpointPath == null || configPath == null || endDateStr == null)
{
    Console.Error.WriteLine("[FATAL] --checkpoint, --config, and --end-date are required.");
    PrintUsage();
    return 1;
}

if (!File.Exists(checkpointPath)) { Console.Error.WriteLine($"[FATAL] Checkpoint not found: {checkpointPath}"); return 1; }
if (!File.Exists(configPath))     { Console.Error.WriteLine($"[FATAL] Config not found: {configPath}"); return 1; }

if (!DateTimeOffset.TryParse(endDateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fixedEnd))
{
    Console.Error.WriteLine($"[FATAL] Invalid --end-date format: {endDateStr}. Use ISO 8601 (e.g. 2026-04-28T00:00:00Z)");
    return 1;
}

outputDir ??= Path.Combine(Path.GetDirectoryName(checkpointPath)!, "..", "metric_audit");
Directory.CreateDirectory(outputDir);

// Load config
var configJson = File.ReadAllText(configPath);
var config = JsonSerializer.Deserialize<MarketConfig>(configJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
if (config == null) { Console.Error.WriteLine($"[FATAL] Failed to deserialize config: {configPath}"); return 1; }

if (cacheDir != null)
    config = config with { DataCacheDirectory = cacheDir };
else if (Directory.Exists("pipeline_shared_cache"))
    config = config with { DataCacheDirectory = Path.GetFullPath("pipeline_shared_cache") };
else if (Directory.Exists("archive/V1-PrefinalFix/cache/pipeline_shared_cache"))
    config = config with { DataCacheDirectory = Path.GetFullPath("archive/V1-PrefinalFix/cache/pipeline_shared_cache") };

Console.WriteLine("=== Seed.MetricAudit ===");
Console.WriteLine($"Checkpoint:   {checkpointPath}");
Console.WriteLine($"Config:       {configPath}");
Console.WriteLine($"End date:     {fixedEnd:u}");
Console.WriteLine($"Cache dir:    {config.DataCacheDirectory ?? "(default)"}");
Console.WriteLine($"Num genomes:  {numGenomes}");
Console.WriteLine($"Output dir:   {outputDir}");
Console.WriteLine();

// Load checkpoint
var cp = CheckpointState.Load(checkpointPath);
Console.WriteLine($"[AUDIT] Loaded checkpoint gen={cp.Generation}, pop={cp.GenomeJsons.Count}, archive={cp.ArchiveState.Count}");

// Pick first N unique genomes from population — small, deterministic sample for forensics.
var seenIds = new HashSet<Guid>();
var testGenomes = new List<SeedGenome>();
foreach (var json in cp.GenomeJsons)
{
    var g = SeedGenome.FromJson(json);
    if (!seenIds.Add(g.GenomeId)) continue;
    testGenomes.Add(g);
    if (testGenomes.Count >= numGenomes) break;
}
Console.WriteLine($"[AUDIT] Selected {testGenomes.Count} genomes for forensics");
Console.WriteLine();

// Load val window data — same path as analyzer for direct comparability.
Console.WriteLine("[AUDIT] Loading historical data + enrichment (cache-first)...");
var runner = new BacktestRunner(config);
var start = fixedEnd.AddHours(-config.TrainingWindowHours - config.ValidationWindowHours);
var (snapshots, prices, rawVolumes, rawFundingRates, _) = await runner.LoadData(config.Symbols[0], start, fixedEnd, enrich: true);

int bph = config.BarsPerHour;
int trainBars = config.TrainingWindowHours * bph;
int valBars = config.ValidationWindowHours * bph;
int trainLen = Math.Min(trainBars, snapshots.Length - valBars);
int valStart = trainLen;
int valEnd = snapshots.Length;

var valSnapshots = snapshots[valStart..valEnd];
var valPrices = prices[valStart..valEnd];
var valRawVolumes = rawVolumes[valStart..valEnd];
var valRawFunding = rawFundingRates[valStart..valEnd];

Console.WriteLine($"[AUDIT] Loaded {snapshots.Length} bars; val window = {valSnapshots.Length} bars ({valSnapshots.Length / bph}h)");
Console.WriteLine();

int genIdx = cp.Generation;

// ─── Path B: analyzer baseline (EvaluateSingle, single-thread per genome) ───
Console.WriteLine("[AUDIT] Path B: MarketEvaluator.EvaluateSingle (single-thread)...");
var pathB = new Dictionary<Guid, MarketEvalResult>();
{
    var evaluator = new MarketEvaluator(config);
    foreach (var g in testGenomes)
        pathB[g.GenomeId] = evaluator.EvaluateSingle(g, valSnapshots, valPrices, valRawVolumes, valRawFunding, genIdx);
}

// ─── Path C: parallel batch evaluation (Evaluate, Parallel.For) ───
Console.WriteLine("[AUDIT] Path C: MarketEvaluator.Evaluate (parallel batch)...");
var pathC = new Dictionary<Guid, MarketEvalResult>();
{
    var evaluator = new MarketEvaluator(config);
    var results = evaluator.Evaluate(testGenomes.Cast<IGenome>().ToList(),
        valSnapshots, valPrices, valRawVolumes, valRawFunding, genIdx);
    foreach (var (id, r) in results) pathC[id] = r;
}

// ─── Path A: in-training single-window (MarketEvolution wrapper, no diversity, no consistency) ───
Console.WriteLine("[AUDIT] Path A: MarketEvolution.RunGeneration (single window, no diversity bonus)...");
var pathA = new Dictionary<Guid, MarketEvalResult>();
{
    var configA = config with
    {
        EvalWindowCount = 1,
        WindowConsistencyWeight = 0f,
        DiversityBonusScale = 0f,
    };
    var evolution = new MarketEvolution(configA, NullObservatory.Instance);
    evolution.InitializeFrom(testGenomes.Cast<IGenome>().ToList(), genIdx);
    evolution.RunGeneration(valSnapshots, valPrices, valRawVolumes, valRawFunding);
    foreach (var (id, r) in evolution.Evaluations) pathA[id] = r;
}

// ─── Path D: in-training multi-window (MarketEvolution wrapper, production-like) ───
//
// Splits the val window into K sub-windows via RegimeDetector.SelectDiverseWindows
// (same mechanism production training uses on the train window) and lets MarketEvolution
// average across them with the configured WindowConsistencyWeight.
Console.WriteLine("[AUDIT] Path D: MarketEvolution.RunGeneration (multi-window averaged)...");
var pathD = new Dictionary<Guid, MarketEvalResult>();
int kWindows = Math.Max(1, config.EvalWindowCount);
{
    // For Path D, also disable diversity bonus so its effect doesn't muddy the multi-window
    // signal we're trying to measure. WindowConsistencyWeight is preserved from the config.
    var configD = config with { DiversityBonusScale = 0f };
    var evolution = new MarketEvolution(configD, NullObservatory.Instance);
    evolution.InitializeFrom(testGenomes.Cast<IGenome>().ToList(), genIdx);

    var windowList = MarketEvolution.BuildEvalWindows(
        valSnapshots, valPrices, valRawVolumes, valRawFunding, kWindows, genIdx, config.RunSeed);
    evolution.RunGeneration(windowList);
    foreach (var (id, r) in evolution.Evaluations) pathD[id] = r;
}

// ─── Compare ───
Console.WriteLine();
Console.WriteLine("=== RESULTS ===");
Console.WriteLine();
Console.WriteLine($"{"GenomeId",-38} {"Path B",-12} {"Path C",-12} {"Path A",-12} {"Path D",-12} {"|C-B|",-10} {"|A-B|",-10} {"|D-B|",-10}");
Console.WriteLine(new string('─', 130));

const float TOL = 1e-5f;
int divCB = 0, divAB = 0, divDB = 0;
double sumAbsCB = 0, sumAbsAB = 0, sumAbsDB = 0;
double maxAbsCB = 0, maxAbsAB = 0, maxAbsDB = 0;

var rows = new List<object>();

foreach (var g in testGenomes)
{
    float fitB = pathB[g.GenomeId].Fitness.Fitness;
    float fitC = pathC[g.GenomeId].Fitness.Fitness;
    float fitA = pathA[g.GenomeId].Fitness.Fitness;
    float fitD = pathD[g.GenomeId].Fitness.Fitness;

    float dCB = MathF.Abs(fitC - fitB);
    float dAB = MathF.Abs(fitA - fitB);
    float dDB = MathF.Abs(fitD - fitB);

    if (dCB > TOL) divCB++;
    if (dAB > TOL) divAB++;
    if (dDB > TOL) divDB++;

    sumAbsCB += dCB; sumAbsAB += dAB; sumAbsDB += dDB;
    if (dCB > maxAbsCB) maxAbsCB = dCB;
    if (dAB > maxAbsAB) maxAbsAB = dAB;
    if (dDB > maxAbsDB) maxAbsDB = dDB;

    Console.WriteLine($"{g.GenomeId,-38} {fitB,12:F6} {fitC,12:F6} {fitA,12:F6} {fitD,12:F6} {dCB,10:E2} {dAB,10:E2} {dDB,10:E2}");

    rows.Add(new
    {
        genomeId = g.GenomeId.ToString(),
        pathB = new { fitness = fitB, breakdown = pathB[g.GenomeId].Fitness },
        pathC = new { fitness = fitC, breakdown = pathC[g.GenomeId].Fitness },
        pathA = new { fitness = fitA, breakdown = pathA[g.GenomeId].Fitness },
        pathD = new { fitness = fitD, breakdown = pathD[g.GenomeId].Fitness },
        deltas = new { CminusB = fitC - fitB, AminusB = fitA - fitB, DminusB = fitD - fitB }
    });
}

Console.WriteLine(new string('─', 130));
int n = testGenomes.Count;
Console.WriteLine($"{"Divergent (>1e-5):",-38} {"",-12} {"",-12} {"",-12} {"",-12} {divCB + "/" + n,-10} {divAB + "/" + n,-10} {divDB + "/" + n,-10}");
Console.WriteLine($"{"Mean |Δ|:",-38} {"",-12} {"",-12} {"",-12} {"",-12} {sumAbsCB / n,10:E2} {sumAbsAB / n,10:E2} {sumAbsDB / n,10:E2}");
Console.WriteLine($"{"Max |Δ|:",-38} {"",-12} {"",-12} {"",-12} {"",-12} {maxAbsCB,10:E2} {maxAbsAB,10:E2} {maxAbsDB,10:E2}");
Console.WriteLine();

Console.WriteLine("=== DIAGNOSIS ===");
if (divCB == 0 && divAB == 0 && divDB == 0)
{
    Console.WriteLine("All four paths agree within 1e-5. No divergence detected (multi-window settings may be 1-window in this config).");
}
else
{
    if (divCB > 0)
        Console.WriteLine($"  C != B: parallel-vs-sequential leak ({divCB}/{n} genomes diverge, max |Δ|={maxAbsCB:E2}). Likely shared BrainDeveloper or static array mutation.");
    else
        Console.WriteLine($"  C == B: parallel evaluation is sound (no shared-state leak).");

    if (divAB > 0 && divCB == 0)
        Console.WriteLine($"  A != B (but C == B): MarketEvolution wrapper introduces divergence ({divAB}/{n}, max |Δ|={maxAbsAB:E2}). Suspect ApplyDiversityBonus or fitness post-processing.");
    else if (divAB == 0)
        Console.WriteLine($"  A == B: in-training single-window path matches analyzer (no wrapper leak).");

    if (divDB > 0)
        Console.WriteLine($"  D != B: multi-window averaging ({divDB}/{n}, max |Δ|={maxAbsDB:E2}). Expected and structural — analyzer needs MatchTraining mode for full equivalence.");
    else
        Console.WriteLine($"  D == B: multi-window path agrees with single-window (config has EvalWindowCount=1 or ConsistencyWeight=0).");
}

// Save full breakdown JSON
var report = new
{
    checkpointPath,
    configPath,
    fixedEnd = fixedEnd.ToString("u"),
    cpGeneration = cp.Generation,
    valWindowBars = valSnapshots.Length,
    kWindows,
    windowConsistencyWeight = config.WindowConsistencyWeight,
    diversityBonusScale = config.DiversityBonusScale,
    summary = new
    {
        n,
        divergentCminusB = divCB, divergentAminusB = divAB, divergentDminusB = divDB,
        meanAbsCminusB = sumAbsCB / n, meanAbsAminusB = sumAbsAB / n, meanAbsDminusB = sumAbsDB / n,
        maxAbsCminusB = maxAbsCB,    maxAbsAminusB = maxAbsAB,    maxAbsDminusB = maxAbsDB,
    },
    rows
};
var reportPath = Path.Combine(outputDir, "metric_audit_report.json");
File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions
{
    WriteIndented = true,
    NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals
}));
Console.WriteLine();
Console.WriteLine($"Full report: {reportPath}");
return 0;

static void PrintUsage()
{
    Console.WriteLine("Seed.MetricAudit — S10 forensics tool. Compares 4 fitness-evaluation paths on the same window.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/Seed.MetricAudit -- \\");
    Console.WriteLine("    --checkpoint <path-to-checkpoint_NNNN.json> \\");
    Console.WriteLine("    --config <path-to-market-config.phaseN.json> \\");
    Console.WriteLine("    --end-date 2026-04-28T00:00:00Z \\");
    Console.WriteLine("    [--num-genomes 5] [--output <dir>] [--cache-dir <path>]");
    Console.WriteLine();
    Console.WriteLine("Paths compared:");
    Console.WriteLine("  B: MarketEvaluator.EvaluateSingle  — single-thread per genome (analyzer baseline)");
    Console.WriteLine("  C: MarketEvaluator.Evaluate        — Parallel.For over the batch");
    Console.WriteLine("  A: MarketEvolution.RunGeneration   — wrapper, EvalWindowCount=1, no diversity");
    Console.WriteLine("  D: MarketEvolution.RunGeneration   — wrapper, multi-window averaged (production)");
}
