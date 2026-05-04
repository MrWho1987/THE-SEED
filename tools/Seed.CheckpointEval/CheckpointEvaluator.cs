using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Seed.Core;
using Seed.Genetics;
using Seed.Market;
using Seed.Market.Agents;
using Seed.Market.Backtest;
using Seed.Market.Evolution;
using Seed.Market.Signals;

namespace Seed.CheckpointEval;

public sealed record GenomeScore(
    string Source,
    int SourceIndex,
    Guid GenomeId,
    int? SpeciesId,
    float TrainingFit,
    float ValFit,
    float ReturnPct,
    float AdjustedSharpe,
    float Sortino,
    float WinRate,
    float MaxDrawdown,
    int TotalTrades);

/// <summary>
/// Controls how the analyzer evaluates each genome:
/// <list type="bullet">
/// <item><c>SingleWindow</c> — one EvaluateSingle call on the entire window. Cheap,
/// directly comparable to in-training ValFit (which uses the same path).</item>
/// <item><c>MatchTraining</c> — split the window into <c>EvalWindowCount</c> sub-windows
/// via <see cref="MarketEvolution.BuildEvalWindows"/> and average via
/// <see cref="MarketEvolution.AverageBreakdowns"/> with <c>WindowConsistencyWeight</c>.
/// Reproduces the in-training training-fitness number exactly. Use this when comparing
/// against the per-gen reported BestFitness (multi-window-averaged) instead of ValFit.</item>
/// </list>
/// </summary>
public enum AnalyzerMode
{
    SingleWindow,
    MatchTraining
}

public sealed class CheckpointEvaluator
{
    private readonly MarketConfig _config;
    private readonly MarketEvaluator _evaluator;
    private readonly AnalyzerMode _mode;

    public CheckpointEvaluator(MarketConfig config, AnalyzerMode mode = AnalyzerMode.SingleWindow)
    {
        _config = config;
        _evaluator = new MarketEvaluator(config);
        _mode = mode;
    }

    public async Task<List<GenomeScore>> EvaluateCheckpointAsync(
        string checkpointPath,
        DateTimeOffset fixedEnd,
        CancellationToken ct = default)
    {
        Console.WriteLine($"[ANALYZER] Loading checkpoint: {checkpointPath}");
        var cp = CheckpointState.Load(checkpointPath);
        Console.WriteLine($"[ANALYZER] Checkpoint gen={cp.Generation}, bestFit={cp.BestFitness:F4}, pop={cp.GenomeJsons.Count}, archive={cp.ArchiveState.Count}");

        Console.WriteLine("[ANALYZER] Loading historical data + enrichment (cache-first)...");
        var runner = new BacktestRunner(_config);
        var start = fixedEnd.AddHours(-_config.TrainingWindowHours - _config.ValidationWindowHours);
        var (snapshots, prices, rawVolumes, rawFundingRates, _) = await runner.LoadData(_config.Symbols[0], start, fixedEnd, enrich: true);

        int bph = _config.BarsPerHour;
        int trainBars = _config.TrainingWindowHours * bph;
        int valBars = _config.ValidationWindowHours * bph;
        int trainLen = Math.Min(trainBars, snapshots.Length - valBars);
        int valStart = trainLen;
        int valEnd = snapshots.Length;

        var valSnapshots = snapshots[valStart..valEnd];
        var valPrices = prices[valStart..valEnd];
        var valRawVolumes = rawVolumes[valStart..valEnd];
        var valRawFunding = rawFundingRates[valStart..valEnd];

        Console.WriteLine($"[ANALYZER] Loaded {snapshots.Length} bars; validation window = {valSnapshots.Length} bars ({valSnapshots.Length / bph}h)");

        // Build genome list from population + archive, deduped by GenomeId
        var items = new List<(string Source, int SourceIndex, int? SpeciesId, float TrainingFit, SeedGenome Genome)>();
        var seenIds = new HashSet<Guid>();

        for (int i = 0; i < cp.GenomeJsons.Count; i++)
        {
            var g = SeedGenome.FromJson(cp.GenomeJsons[i]);
            if (!seenIds.Add(g.GenomeId)) continue;
            items.Add(("pop", i, null, float.NaN, g));
        }

        for (int i = 0; i < cp.ArchiveState.Count; i++)
        {
            var entry = cp.ArchiveState[i];
            var g = SeedGenome.FromJson(entry.GenomeJson);
            if (!seenIds.Add(g.GenomeId)) continue;
            items.Add(("archive", i, entry.SpeciesId, entry.Fitness, g));
        }

        Console.WriteLine($"[ANALYZER] Evaluating {items.Count} unique genomes against validation window...");

        var scores = new List<GenomeScore>(items.Count);
        var progressLock = new object();
        int done = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Console.WriteLine($"[ANALYZER] Mode: {_mode}{(_mode == AnalyzerMode.MatchTraining ? $" (k={_config.EvalWindowCount}, consistency={_config.WindowConsistencyWeight:F3})" : "")}");

        Parallel.ForEach(items,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1),
                CancellationToken = ct
            },
            item =>
            {
                var result = _mode == AnalyzerMode.MatchTraining
                    ? EvaluateMatchTraining(item.Genome, valSnapshots, valPrices, valRawVolumes, valRawFunding, cp.Generation)
                    : _evaluator.EvaluateSingle(item.Genome, valSnapshots, valPrices, valRawVolumes, valRawFunding, cp.Generation);
                var score = new GenomeScore(
                    item.Source,
                    item.SourceIndex,
                    item.Genome.GenomeId,
                    item.SpeciesId,
                    item.TrainingFit,
                    result.Fitness.Fitness,
                    result.Fitness.ReturnPct,
                    result.Fitness.AdjustedSharpe,
                    result.Fitness.Sortino,
                    result.Fitness.WinRate,
                    result.Fitness.MaxDrawdown,
                    result.Fitness.TotalTrades);
                lock (scores)
                {
                    scores.Add(score);
                    done++;
                    if (done % 25 == 0)
                        Console.WriteLine($"[ANALYZER] {done}/{items.Count} evaluated ({sw.Elapsed.TotalSeconds:F0}s elapsed)");
                }
            });

        sw.Stop();
        Console.WriteLine($"[ANALYZER] Complete: {scores.Count} genomes in {sw.Elapsed.TotalSeconds:F1}s");

        // Sort descending by ValFit
        scores = scores.OrderByDescending(s => s.ValFit).ToList();
        return scores;
    }

    /// <summary>
    /// Evaluates a single genome using the multi-window training fitness pipeline:
    /// splits the window into K sub-windows via <see cref="MarketEvolution.BuildEvalWindows"/>,
    /// runs <c>EvaluateSingle</c> on each, and averages via <see cref="MarketEvolution.AverageBreakdowns"/>
    /// with the configured <c>WindowConsistencyWeight</c>. Bit-equivalent to the per-genome
    /// fitness produced inside <c>MarketEvolution.RunGeneration(windows[])</c>.
    /// </summary>
    private MarketEvalResult EvaluateMatchTraining(
        IGenome genome, SignalSnapshot[] history, float[] prices,
        float[] rawVolumes, float[] rawFundingRates, int generationIndex)
    {
        int k = Math.Max(1, _config.EvalWindowCount);
        if (k <= 1)
            return _evaluator.EvaluateSingle(genome, history, prices, rawVolumes, rawFundingRates, generationIndex);

        var windows = MarketEvolution.BuildEvalWindows(history, prices, rawVolumes, rawFundingRates, k, generationIndex, _config.RunSeed);
        var breakdowns = new List<FitnessBreakdown>(windows.Length);
        OutputObservation? lastObs = null;
        int[]? lastCloseReasonCounts = null;
        foreach (var (snaps, p, vols, funding) in windows)
        {
            var r = _evaluator.EvaluateSingle(genome, snaps, p, vols, funding, generationIndex);
            breakdowns.Add(r.Fitness);
            lastObs = r.OutputObs;
            lastCloseReasonCounts = r.CloseReasonCounts;
        }
        var avg = MarketEvolution.AverageBreakdowns(breakdowns, _config.WindowConsistencyWeight);
        return new MarketEvalResult(genome.GenomeId, avg, lastObs, lastCloseReasonCounts);
    }

    public static void SaveOutputs(
        string outputDir,
        List<GenomeScore> scores,
        CheckpointState cp,
        IReadOnlyDictionary<Guid, SeedGenome> genomesById)
    {
        Directory.CreateDirectory(outputDir);
        var indentedOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        // 1. best_market_genome.json — top-1 by ValFit
        if (scores.Count > 0)
        {
            var bestId = scores[0].GenomeId;
            if (genomesById.TryGetValue(bestId, out var bestGenome))
            {
                File.WriteAllText(Path.Combine(outputDir, "best_market_genome.json"), bestGenome.ToJson());
            }
        }

        // 2. top10_val_genomes.json — metrics bundle
        var top10 = scores.Take(10).ToList();
        File.WriteAllText(
            Path.Combine(outputDir, "top10_val_genomes.json"),
            JsonSerializer.Serialize(new
            {
                checkpointGeneration = cp.Generation,
                validationWindowBars = "see analysis_summary.txt",
                top10
            }, indentedOpts));

        // 3. ensemble_champions.json — all archive elites (species champions)
        var championsJson = cp.ArchiveState.Select(a => a.GenomeJson).ToArray();
        File.WriteAllText(
            Path.Combine(outputDir, "ensemble_champions.json"),
            JsonSerializer.Serialize(new
            {
                checkpointGeneration = cp.Generation,
                speciesCount = cp.ArchiveState.Count,
                champions = championsJson
            }, indentedOpts));

        // 4. archive_val_scores.json — per-species
        var archiveRows = scores.Where(s => s.Source == "archive").ToList();
        File.WriteAllText(
            Path.Combine(outputDir, "archive_val_scores.json"),
            JsonSerializer.Serialize(archiveRows, indentedOpts));

        // 5. population_val_scores.json — per-genome
        var popRows = scores.Where(s => s.Source == "pop").ToList();
        File.WriteAllText(
            Path.Combine(outputDir, "population_val_scores.json"),
            JsonSerializer.Serialize(popRows, indentedOpts));

        // 6. analysis_summary.txt — human-readable headline
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== Checkpoint Analysis Summary ===");
        sb.AppendLine($"Checkpoint generation: {cp.Generation}");
        sb.AppendLine($"Population size:       {cp.GenomeJsons.Count}");
        sb.AppendLine($"Archive elites:        {cp.ArchiveState.Count}");
        sb.AppendLine($"Unique genomes eval'd: {scores.Count}");
        sb.AppendLine();
        sb.AppendLine("=== Top 10 by Validation Fitness ===");
        sb.AppendLine($"{"Rank",4} {"Source",-9} {"Idx",4} {"Species",7} {"TrainFit",10} {"ValFit",10} {"Ret%",7} {"Sharpe",7} {"WR%",6} {"DD%",6} {"Trd",5}");
        for (int i = 0; i < Math.Min(10, scores.Count); i++)
        {
            var s = scores[i];
            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,4} {1,-9} {2,4} {3,7} {4,10:F4} {5,10:F4} {6,7:P1} {7,7:F2} {8,6:P0} {9,6:P1} {10,5}",
                i + 1, s.Source, s.SourceIndex, s.SpeciesId?.ToString() ?? "-",
                s.TrainingFit, s.ValFit, s.ReturnPct, s.AdjustedSharpe, s.WinRate, s.MaxDrawdown, s.TotalTrades));
        }
        sb.AppendLine();
        if (scores.Count > 0)
        {
            var best = scores[0];
            sb.AppendLine("=== Best by Validation ===");
            sb.AppendLine($"GenomeId:   {best.GenomeId}");
            sb.AppendLine($"Source:     {best.Source} (index {best.SourceIndex}, species {best.SpeciesId?.ToString() ?? "-"})");
            sb.AppendLine($"ValFit:     {best.ValFit:F4}");
            sb.AppendLine($"Return:     {best.ReturnPct:P2}");
            sb.AppendLine($"Sharpe:     {best.AdjustedSharpe:F2}");
            sb.AppendLine($"WinRate:    {best.WinRate:P1}");
            sb.AppendLine($"MaxDD:      {best.MaxDrawdown:P2}");
            sb.AppendLine($"Trades:     {best.TotalTrades}");
        }
        File.WriteAllText(Path.Combine(outputDir, "analysis_summary.txt"), sb.ToString());

        Console.WriteLine($"[ANALYZER] Outputs saved to: {outputDir}");
        Console.WriteLine($"  - best_market_genome.json");
        Console.WriteLine($"  - top10_val_genomes.json");
        Console.WriteLine($"  - ensemble_champions.json");
        Console.WriteLine($"  - archive_val_scores.json");
        Console.WriteLine($"  - population_val_scores.json");
        Console.WriteLine($"  - analysis_summary.txt");
    }
}
