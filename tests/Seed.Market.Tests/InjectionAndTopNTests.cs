using Seed.Core;
using Seed.Genetics;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Observatory;

namespace Seed.Market.Tests;

/// <summary>
/// Tests for B4 (InjectGenomeIntoPopulation) and B5 (GetTopNByTrainingFitness) new methods.
/// </summary>
public class InjectionAndTopNTests
{
    [Fact]
    public void GetTopNByTrainingFitness_ReturnsCount_WithinValidRange()
    {
        // After RunGeneration() completes, _population is the post-Reproduce offspring;
        // _evaluations holds the prior gen's fitness data. GetTopN matches only the
        // overlap (elites preserved through Reproduce), so counts can be small in tiny
        // test populations. Real Phase 4 training (popSize 200, 10-30 species, multiple
        // elites per species) produces much larger overlap.
        var (evo, _) = SmallEvolvedPopulation(popSize: 10, gens: 2, runSeed: 42);

        var top1 = evo.GetTopNByTrainingFitness(1);
        var top5 = evo.GetTopNByTrainingFitness(5);
        var top20 = evo.GetTopNByTrainingFitness(20); // more than pop

        Assert.True(top1.Count <= 1, "top-1 should return 0-1 genomes");
        Assert.True(top1.Count >= 1, "at least one elite should always survive Reproduce");
        Assert.True(top5.Count >= 1 && top5.Count <= 10, $"top-5 should return 1..10 genomes, got {top5.Count}");
        Assert.True(top20.Count >= top5.Count, "larger N should include at least as many as smaller N");
        Assert.True(top20.Count <= 10, "top-N should be capped at population size");
    }

    [Fact]
    public void GetTopNByTrainingFitness_ReturnsEmpty_WhenZeroOrNegative()
    {
        var (evo, _) = SmallEvolvedPopulation(popSize: 5, gens: 1, runSeed: 42);

        Assert.Empty(evo.GetTopNByTrainingFitness(0));
        Assert.Empty(evo.GetTopNByTrainingFitness(-1));
    }

    [Fact]
    public void GetTopNByTrainingFitness_FirstIsInCurrentPopulation()
    {
        var (evo, _) = SmallEvolvedPopulation(popSize: 10, gens: 2, runSeed: 42);

        var topOne = evo.GetTopNByTrainingFitness(1);

        Assert.NotEmpty(topOne);
        // The returned genome must be a member of the current population (by GenomeId)
        Assert.Contains(evo.Population, g => g.GenomeId == topOne[0].GenomeId);
    }

    [Fact]
    public void InjectGenomeIntoPopulation_ReplacesWorst_AndPopulationSizeUnchanged()
    {
        var (evo, _) = SmallEvolvedPopulation(popSize: 10, gens: 2, runSeed: 42);

        var initialCount = evo.Population.Count;
        var best = (SeedGenome?)evo.GetBestGenome();
        Assert.NotNull(best);

        // Build a fresh genome to inject (with a unique ID, independent from current pop).
        var rng = new Rng64(12345);
        var fresh = SeedGenome.CreateRandom(rng);

        var injected = evo.InjectGenomeIntoPopulation(fresh);

        Assert.True(injected, "Injection should succeed when population is populated");
        Assert.Equal(initialCount, evo.Population.Count);
        Assert.Contains(evo.Population, g => g.GenomeId == fresh.GenomeId);
    }

    [Fact]
    public void InjectGenomeIntoPopulation_ThrowsOnDuplicateGenomeId()
    {
        // Fix 2 — defensive guard: injecting a genome with a GenomeId already in the
        // population must throw, not silently corrupt _evaluations / Reproduce state.
        var (evo, _) = SmallEvolvedPopulation(popSize: 10, gens: 2, runSeed: 42);

        // Build a clone of an existing pop member that PRESERVES the source GenomeId
        // (this is what the un-fixed Track B B4 code did via SeedGenome.FromJson(ToJson)).
        var existing = (SeedGenome)evo.Population[0];
        var dupClone = SeedGenome.FromJson(existing.ToJson());
        Assert.Equal(existing.GenomeId, dupClone.GenomeId);

        var ex = Assert.Throws<InvalidOperationException>(
            () => evo.InjectGenomeIntoPopulation(dupClone));
        Assert.Contains("duplicate GenomeId", ex.Message);
    }

    [Fact]
    public void InjectGenomeIntoPopulation_AcceptsCloneWithFreshId_NoDuplicates()
    {
        // Fix 2 — the proper protection-clone path: deep-copy network with a fresh
        // GenomeId via CloneGenome(newId). Population must remain duplicate-free.
        var (evo, _) = SmallEvolvedPopulation(popSize: 10, gens: 2, runSeed: 42);

        var existing = (SeedGenome)evo.Population[0];
        var freshId = Guid.NewGuid();
        var cleanClone = (SeedGenome)existing.CloneGenome(freshId);

        Assert.NotEqual(existing.GenomeId, cleanClone.GenomeId);
        Assert.True(evo.InjectGenomeIntoPopulation(cleanClone));

        // Critical invariant: no duplicate GenomeIds anywhere in the population.
        var distinctIds = evo.Population.Select(g => g.GenomeId).Distinct().Count();
        Assert.Equal(evo.Population.Count, distinctIds);
        Assert.Contains(evo.Population, g => g.GenomeId == freshId);
    }

    [Fact]
    public void InjectGenomeIntoPopulation_SucceedsWithFallback_WhenNoEvaluations()
    {
        var config = MarketConfig.Default with { PopulationSize = 5, RunSeed = 42 };
        var observatory = new FileObservatory(Path.Combine(Path.GetTempPath(), $"seed_test_{Guid.NewGuid()}.jsonl"));
        var evo = new MarketEvolution(config, observatory);
        evo.Initialize();

        var rng = new Rng64(99);
        var fresh = SeedGenome.CreateRandom(rng);

        // No generations run → no evaluations; method falls back to last-slot replacement
        var injected = evo.InjectGenomeIntoPopulation(fresh);

        Assert.True(injected);
        Assert.Contains(evo.Population, g => g.GenomeId == fresh.GenomeId);
    }

    [Fact]
    public void GetTopNByTrainingFitness_LargerNIncludesSmallerN()
    {
        // Larger pop (30) gives more elite overlap for a stable subset check
        var (evo, _) = SmallEvolvedPopulation(popSize: 30, gens: 3, runSeed: 42);

        var top3 = evo.GetTopNByTrainingFitness(3);
        var top10 = evo.GetTopNByTrainingFitness(10);

        Assert.NotEmpty(top3);
        Assert.True(top10.Count >= top3.Count);

        // Every id in top-3 must appear somewhere in top-10 (subset invariant of ranked selection)
        var top10Ids = top10.Select(g => g.GenomeId).ToHashSet();
        foreach (var g in top3)
            Assert.Contains(g.GenomeId, top10Ids);

        // No duplicates in the returned list
        Assert.Equal(top10.Count, top10.Select(g => g.GenomeId).Distinct().Count());
    }

    // -------------- helpers --------------

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
