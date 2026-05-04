using Seed.Core;
using Seed.Genetics;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Observatory;

namespace Seed.Market.Tests;

/// <summary>
/// S10 metric divergence test (CI pin). The in-training fitness pipeline
/// (<see cref="MarketEvolution.RunGeneration(System.ValueTuple{Seed.Market.Signals.SignalSnapshot[],float[],float[],float[]}[])"/>)
/// and the analyzer's MatchTraining mode (manual <c>EvaluateSingle</c> loop +
/// <see cref="MarketEvolution.AverageBreakdowns"/>) must produce bit-equivalent
/// per-genome fitness on the same window list. If this drifts, the analyzer can no
/// longer reproduce in-training training fitness — investigate before continuing.
///
/// Forensics performed by <c>tools/Seed.MetricAudit/</c> (2026-05-05) confirmed
/// that production single-window paths (B = EvaluateSingle, C = Evaluate parallel,
/// A = MarketEvolution single-window) all agree exactly. The only structural divergence
/// is multi-window averaging (Path D vs B), which is correct production behavior.
/// </summary>
public class MetricDivergenceTest
{
    [Fact]
    public void AnalyzerMatchTraining_EqualsInTraining_MultiWindow()
    {
        var config = MarketConfig.Default with
        {
            PopulationSize = 5,
            EvalWindowCount = 3,
            WindowConsistencyWeight = 0.10f,
            DiversityBonusScale = 0f,  // disable diversity bonus to isolate multi-window math
            RunSeed = 42,
            InitialCapital = 10_000m
        };
        var rng = new Rng64(123);
        var genomes = Enumerable.Range(0, 5).Select(_ => (IGenome)SeedGenome.CreateRandom(rng)).ToList();
        var (snapshots, prices, rawVols, rawFund) = CreateSyntheticData(300);

        // Build the same K sub-windows used by both paths
        var windowList = MarketEvolution.BuildEvalWindows(
            snapshots, prices, rawVols, rawFund,
            k: config.EvalWindowCount, generation: 0, runSeed: config.RunSeed);
        Assert.Equal(config.EvalWindowCount, windowList.Length);

        // ─── In-training path: MarketEvolution.RunGeneration with multi-window list ───
        var evolution = new MarketEvolution(config, NullObservatory.Instance);
        evolution.InitializeFrom(genomes, generation: 0);
        evolution.RunGeneration(windowList);
        var trainingResults = evolution.Evaluations;

        // ─── Analyzer MatchTraining path: per-window EvaluateSingle + AverageBreakdowns ───
        var evaluator = new MarketEvaluator(config);
        var analyzerResults = new Dictionary<Guid, FitnessBreakdown>();
        foreach (var g in genomes)
        {
            var breakdowns = new List<FitnessBreakdown>(windowList.Length);
            foreach (var (snaps, p, vols, funding) in windowList)
            {
                var r = evaluator.EvaluateSingle(g, snaps, p, vols, funding, generationIndex: 0);
                breakdowns.Add(r.Fitness);
            }
            analyzerResults[g.GenomeId] = MarketEvolution.AverageBreakdowns(breakdowns, config.WindowConsistencyWeight);
        }

        // ─── Assert per-genome fitness matches within 1e-5 ───
        foreach (var g in genomes)
        {
            var trainFit = trainingResults[g.GenomeId].Fitness.Fitness;
            var anaFit = analyzerResults[g.GenomeId].Fitness;
            Assert.True(MathF.Abs(trainFit - anaFit) < 1e-5f,
                $"Genome {g.GenomeId}: in-training {trainFit:F6} != analyzer MatchTraining {anaFit:F6} (Δ={MathF.Abs(trainFit - anaFit):E2})");
        }
    }

    [Fact]
    public void AnalyzerSingleWindow_EqualsInTrainingValFit_OnSameWindow()
    {
        // ValFit path: in-training calls EvaluateSingle once on val window. Analyzer in
        // SingleWindow mode also calls EvaluateSingle once. Two MarketEvaluator instances
        // configured identically must produce bit-equal fitness for the same genome+window.
        var config = MarketConfig.Default with
        {
            PopulationSize = 5,
            RunSeed = 42,
            InitialCapital = 10_000m
        };
        var rng = new Rng64(123);
        var genomes = Enumerable.Range(0, 5).Select(_ => (IGenome)SeedGenome.CreateRandom(rng)).ToList();
        var (snapshots, prices, rawVols, rawFund) = CreateSyntheticData(300);

        var evaluator1 = new MarketEvaluator(config);
        var evaluator2 = new MarketEvaluator(config);

        foreach (var g in genomes)
        {
            var r1 = evaluator1.EvaluateSingle(g, snapshots, prices, rawVols, rawFund, generationIndex: 0);
            var r2 = evaluator2.EvaluateSingle(g, snapshots, prices, rawVols, rawFund, generationIndex: 0);
            Assert.Equal(r1.Fitness.Fitness, r2.Fitness.Fitness, 5);
        }
    }

    [Fact]
    public void EvaluateSingle_AndEvaluate_Parallel_AgreeForSameGenome()
    {
        // Forensics confirmed B == C: parallel Evaluate produces identical fitness to
        // sequential EvaluateSingle. This pins that property — if a future change
        // introduces a parallel-only state leak, this test fires.
        var config = MarketConfig.Default with
        {
            PopulationSize = 5,
            RunSeed = 42,
            InitialCapital = 10_000m
        };
        var rng = new Rng64(123);
        var genomes = Enumerable.Range(0, 5).Select(_ => (IGenome)SeedGenome.CreateRandom(rng)).ToList();
        var (snapshots, prices, rawVols, rawFund) = CreateSyntheticData(300);

        var evaluator = new MarketEvaluator(config);
        var parallelResults = evaluator.Evaluate(genomes, snapshots, prices, rawVols, rawFund, 0);
        foreach (var g in genomes)
        {
            var sequential = evaluator.EvaluateSingle(g, snapshots, prices, rawVols, rawFund, 0);
            var parallel = parallelResults[g.GenomeId];
            Assert.Equal(sequential.Fitness.Fitness, parallel.Fitness.Fitness, 5);
        }
    }

    [Fact]
    public void BuildEvalWindows_ProducesExactlyKSubWindows()
    {
        var (snaps, prices, vols, funding) = CreateSyntheticData(300);
        var windows = MarketEvolution.BuildEvalWindows(snaps, prices, vols, funding, k: 3, generation: 0, runSeed: 42);
        Assert.Equal(3, windows.Length);
        foreach (var w in windows)
        {
            Assert.True(w.Snaps.Length >= 50, "each sub-window should have at least 50 bars");
            Assert.Equal(w.Snaps.Length, w.Prices.Length);
            Assert.Equal(w.Snaps.Length, w.RawVolumes.Length);
            Assert.Equal(w.Snaps.Length, w.RawFundingRates.Length);
        }
    }

    [Fact]
    public void BuildEvalWindows_K1_ReturnsFullWindow()
    {
        var (snaps, prices, vols, funding) = CreateSyntheticData(200);
        var windows = MarketEvolution.BuildEvalWindows(snaps, prices, vols, funding, k: 1, generation: 0, runSeed: 42);
        Assert.Single(windows);
        Assert.Equal(snaps.Length, windows[0].Snaps.Length);
    }

    [Fact]
    public void AverageBreakdowns_NoConsistencyPenalty_ReturnsMean()
    {
        // 3 breakdowns with fitness 1.0, 2.0, 3.0 → mean = 2.0
        var breakdowns = new List<FitnessBreakdown>
        {
            MakeBreakdown(fitness: 1.0f, returnPct: 0.10f),
            MakeBreakdown(fitness: 2.0f, returnPct: 0.20f),
            MakeBreakdown(fitness: 3.0f, returnPct: 0.30f),
        };
        var avg = MarketEvolution.AverageBreakdowns(breakdowns, consistencyWeight: 0f);
        Assert.Equal(2.0f, avg.Fitness, 5);
        // ReturnPct also averaged
        Assert.Equal(0.20f, avg.ReturnPct, 5);
    }

    [Fact]
    public void AverageBreakdowns_AppliesConsistencyPenalty()
    {
        // 3 breakdowns: fitness 1.0, 2.0, 3.0 → mean = 2.0, std = sqrt((1+0+1)/3) ≈ 0.8165
        // With consistencyWeight=0.10: adjusted = 2.0 - 0.10 * 0.8165 ≈ 1.91835
        var breakdowns = new List<FitnessBreakdown>
        {
            MakeBreakdown(fitness: 1.0f, returnPct: 0.10f),
            MakeBreakdown(fitness: 2.0f, returnPct: 0.20f),
            MakeBreakdown(fitness: 3.0f, returnPct: 0.30f),
        };
        var avg = MarketEvolution.AverageBreakdowns(breakdowns, consistencyWeight: 0.10f);
        float expectedStd = MathF.Sqrt((1f + 0f + 1f) / 3f);
        float expected = 2.0f - 0.10f * expectedStd;
        Assert.Equal(expected, avg.Fitness, 5);
    }

    [Fact]
    public void AverageBreakdowns_SingleBreakdown_ReturnsItUnchanged()
    {
        var single = MakeBreakdown(fitness: 1.5f, returnPct: 0.15f);
        var avg = MarketEvolution.AverageBreakdowns(new List<FitnessBreakdown> { single }, consistencyWeight: 0.50f);
        Assert.Equal(single.Fitness, avg.Fitness, 5);
    }

    private static FitnessBreakdown MakeBreakdown(float fitness, float returnPct) => new(
        Fitness: fitness, ReturnPct: returnPct, MaxDrawdown: 0.05f,
        TotalTrades: 10, WinRate: 0.5f, NetPnl: 100f, IsActive: true,
        RawSharpe: fitness, AdjustedSharpe: fitness, Sortino: fitness, AdjustedSortino: fitness,
        CVaR5: -0.01f, MaxDrawdownDuration: 0.1f, ShrinkageConfidence: 0.5f);

    private static (SignalSnapshot[], float[], float[], float[]) CreateSyntheticData(int length)
    {
        var normalizer = new SignalNormalizer();
        var snapshots = new SignalSnapshot[length];
        var prices = new float[length];
        var rawVolumes = new float[length];
        var rawFundingRates = new float[length];
        float price = 50000f;
        var rng = new Random(42);

        for (int i = 0; i < length; i++)
        {
            price *= 1f + (float)(rng.NextDouble() - 0.498) * 0.02f;
            prices[i] = price;
            rawVolumes[i] = 1000f + (float)rng.NextDouble() * 500f;
            rawFundingRates[i] = 0.0001f * ((float)rng.NextDouble() - 0.5f);
            var raw = new float[SignalIndex.Count];
            raw[SignalIndex.BtcPrice] = price;
            raw[SignalIndex.BtcReturn1h] = i > 0 ? (price - prices[i - 1]) / prices[i - 1] : 0f;
            raw[SignalIndex.BtcVolume1h] = rawVolumes[i];
            raw[SignalIndex.Rsi14] = 50f + (float)(rng.NextDouble() - 0.5) * 30f;
            snapshots[i] = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddHours(i), i);
        }
        return (snapshots, prices, rawVolumes, rawFundingRates);
    }
}
