using Seed.Genetics;
using Seed.Market.Backtest;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Observatory;

namespace Seed.Market.Tests;

/// <summary>
/// Tests for Fix 1 — canonical save path.
///
/// The canonical save logic in Program.cs RunBacktest looks up
/// <c>bestValResult.GenomeId</c> in <c>evolution.Population</c> and writes that genome
/// to <c>config.ResolvedGenomePath</c>. The full save path is exercised by the
/// integration smoke test (market-config.smoke.json); these unit tests cover the
/// invariants the save logic depends on.
/// </summary>
public class PhaseEndSaveTests
{
    [Fact]
    public void BacktestRunner_Evaluate_ReturnsDict_KeyedByPopulationGenomeIds()
    {
        // Precondition for Fix 1: the dict returned from BacktestRunner.Evaluate
        // is keyed by GenomeId, and each key must correspond to a genome currently
        // in evolution.Population (so FirstOrDefault(g => g.GenomeId == result.GenomeId)
        // can resolve the genome at end-of-phase).
        var (evo, config) = SmallEvolvedPopulation(popSize: 10, gens: 2, runSeed: 42);
        var (snapshots, prices, volumes, funding) = CreateSyntheticData(length: 200);

        var runner = new BacktestRunner(config);
        var valResults = runner.Evaluate(evo.Population, snapshots, prices, volumes, funding, 0);

        Assert.NotEmpty(valResults);

        var populationIds = evo.Population.Select(g => g.GenomeId).ToHashSet();
        foreach (var resultId in valResults.Keys)
        {
            Assert.Contains(resultId, populationIds);
        }
    }

    [Fact]
    public void BacktestRunner_Evaluate_OrderingByFitness_PicksHighestFromValResults()
    {
        // The canonical save uses valResults.Values.OrderByDescending(...).First();
        // verify this consistently picks the genome with the highest validation fitness.
        var (evo, config) = SmallEvolvedPopulation(popSize: 10, gens: 2, runSeed: 42);
        var (snapshots, prices, volumes, funding) = CreateSyntheticData(length: 200);

        var runner = new BacktestRunner(config);
        var valResults = runner.Evaluate(evo.Population, snapshots, prices, volumes, funding, 0);

        var bestByOrdering = valResults.Values.OrderByDescending(r => r.Fitness.Fitness).First();
        var maxFitness = valResults.Values.Max(r => r.Fitness.Fitness);

        Assert.Equal(maxFitness, bestByOrdering.Fitness.Fitness);

        // The looked-up population genome (Fix 1's bestValPopGenome) must exist.
        var bestValPopGenome = evo.Population.FirstOrDefault(g => g.GenomeId == bestByOrdering.GenomeId) as SeedGenome;
        Assert.NotNull(bestValPopGenome);
    }

    // -------------- helpers (mirror InjectionAndTopNTests so tests are independent) --------------

    private static (MarketEvolution evo, MarketConfig cfg) SmallEvolvedPopulation(int popSize, int gens, ulong runSeed)
    {
        var config = MarketConfig.Default with
        {
            PopulationSize = popSize,
            Generations = gens,
            InitialCapital = 10_000m,
            RunSeed = runSeed
        };

        var observatory = new FileObservatory(Path.Combine(Path.GetTempPath(), $"seed_test_{Guid.NewGuid()}.jsonl"));
        var evo = new MarketEvolution(config, observatory);
        evo.Initialize();

        var (snapshots, prices, volumes, funding) = CreateSyntheticData(length: 300);
        for (int g = 0; g < gens; g++)
            evo.RunGeneration(snapshots, prices, volumes, funding);

        return (evo, config);
    }

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
