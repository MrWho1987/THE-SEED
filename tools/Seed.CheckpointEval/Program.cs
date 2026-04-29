using System.Globalization;
using System.Text.Json;
using Seed.CheckpointEval;
using Seed.Genetics;
using Seed.Market;
using Seed.Market.Backtest;

// Parse CLI args
string? checkpointPath = null;
string? configPath = null;
string? outputDir = null;
string? endDateStr = null;
string? cacheDir = null;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--checkpoint":
            checkpointPath = args[++i];
            break;
        case "--config":
            configPath = args[++i];
            break;
        case "--output":
            outputDir = args[++i];
            break;
        case "--end-date":
            endDateStr = args[++i];
            break;
        case "--cache-dir":
            cacheDir = args[++i];
            break;
        case "-h":
        case "--help":
            PrintUsage();
            return 0;
    }
}

if (checkpointPath == null || configPath == null)
{
    Console.Error.WriteLine("[FATAL] --checkpoint and --config are required.");
    PrintUsage();
    return 1;
}

if (!File.Exists(checkpointPath))
{
    Console.Error.WriteLine($"[FATAL] Checkpoint not found: {checkpointPath}");
    return 1;
}

if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"[FATAL] Config not found: {configPath}");
    return 1;
}

outputDir ??= Path.Combine(Path.GetDirectoryName(checkpointPath)!, "..", "analysis");

// Parse end date — required for determinism matching training's fixed pipeline end
DateTimeOffset fixedEnd;
if (!string.IsNullOrEmpty(endDateStr))
{
    if (!DateTimeOffset.TryParse(endDateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out fixedEnd))
    {
        Console.Error.WriteLine($"[FATAL] Invalid --end-date format: {endDateStr}. Use ISO 8601 (e.g. 2026-04-20T21:36:00Z)");
        return 1;
    }
}
else
{
    // Default: current time minus 1 hour (matches RunBacktest default)
    // WARNING: This will produce different validation windows than training if training used a pipeline-fixed date
    fixedEnd = DateTimeOffset.UtcNow.AddHours(-1);
    Console.WriteLine($"[WARN] No --end-date provided; using {fixedEnd:u}. For reproducibility, pass the pipeline end date used by training.");
}

// Load config
var configJson = File.ReadAllText(configPath);
var config = JsonSerializer.Deserialize<MarketConfig>(configJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
if (config == null)
{
    Console.Error.WriteLine($"[FATAL] Failed to deserialize config: {configPath}");
    return 1;
}

// Override cache dir: prefer --cache-dir, fall back to pipeline_shared_cache if it exists (matches pipeline default)
if (cacheDir != null)
{
    config = config with { DataCacheDirectory = cacheDir };
}
else if (Directory.Exists("pipeline_shared_cache"))
{
    config = config with { DataCacheDirectory = Path.GetFullPath("pipeline_shared_cache") };
    Console.WriteLine($"[INFO] Using detected pipeline_shared_cache at: {config.DataCacheDirectory}");
}

Console.WriteLine($"=== Seed Checkpoint Analyzer ===");
Console.WriteLine($"Checkpoint: {checkpointPath}");
Console.WriteLine($"Config:     {configPath}");
Console.WriteLine($"Output:     {outputDir}");
Console.WriteLine($"End date:   {fixedEnd:u}");
Console.WriteLine();

var evaluator = new CheckpointEvaluator(config);
var scores = await evaluator.EvaluateCheckpointAsync(checkpointPath, fixedEnd);

// Build genome lookup for writing outputs
var cp = CheckpointState.Load(checkpointPath);
var genomesById = new Dictionary<Guid, SeedGenome>();
foreach (var j in cp.GenomeJsons)
{
    var g = SeedGenome.FromJson(j);
    genomesById[g.GenomeId] = g;
}
foreach (var a in cp.ArchiveState)
{
    var g = SeedGenome.FromJson(a.GenomeJson);
    if (!genomesById.ContainsKey(g.GenomeId))
        genomesById[g.GenomeId] = g;
}

CheckpointEvaluator.SaveOutputs(outputDir, scores, cp, genomesById);

Console.WriteLine();
Console.WriteLine("=== DONE ===");
if (scores.Count > 0)
{
    Console.WriteLine($"Best ValFit: {scores[0].ValFit:F4} (GenomeId: {scores[0].GenomeId})");
}
return 0;

static void PrintUsage()
{
    Console.WriteLine("Seed.CheckpointEval — evaluates every genome in a saved checkpoint against the fixed validation window.");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project tools/Seed.CheckpointEval -- --checkpoint <path> --config <path> [--output <dir>] [--end-date <iso>]");
    Console.WriteLine();
    Console.WriteLine("Arguments:");
    Console.WriteLine("  --checkpoint <path>  Path to checkpoint_NNNN.json (required)");
    Console.WriteLine("  --config <path>      Path to market-config.phaseN.json (required)");
    Console.WriteLine("  --output <dir>       Output directory (default: <checkpoint-dir>/../analysis)");
    Console.WriteLine("  --end-date <iso>     Pipeline end date for determinism (e.g., 2026-04-20T21:36:00Z)");
    Console.WriteLine("  --cache-dir <path>   Data cache directory (default: pipeline_shared_cache if present)");
    Console.WriteLine();
    Console.WriteLine("Outputs:");
    Console.WriteLine("  best_market_genome.json        — top-1 by validation fitness");
    Console.WriteLine("  top10_val_genomes.json         — top 10 with metrics");
    Console.WriteLine("  ensemble_champions.json        — all archive elites");
    Console.WriteLine("  archive_val_scores.json        — per-species validation scores");
    Console.WriteLine("  population_val_scores.json     — per-genome validation scores");
    Console.WriteLine("  analysis_summary.txt           — human-readable summary");
}
