using Seed.Core;
using Seed.Genetics;
using Seed.Market.Evolution;
using Seed.Market.Signals;

namespace Seed.Market.Tests;

/// <summary>
/// S11 — End-of-phase evaluation must include the elite archive in addition to the live
/// population. Phase 4 minimal post-mortem found that the analyzer surfaced an archive
/// elite (sp.6 +0.9456 ValFit) that was 41% better than the population-only best
/// (pop[126] +0.6714) — but the in-training final eval only scanned the population, so
/// the canonical deploy path saved the inferior genome. These tests pin the union+dedup
/// semantics that fix this.
/// </summary>
public class EndPhaseEvalCoverageTest
{
    [Fact]
    public void UnionDedup_KeepsArchiveGenomesNotInPopulation()
    {
        // Population of 3, archive holds 2 elites — one of which has the same GenomeId as
        // a population member (must be deduped), one of which is unique (must appear in union).
        var rng = new Rng64(42);
        var popG1 = SeedGenome.CreateRandom(rng);
        var popG2 = SeedGenome.CreateRandom(rng);
        var popG3 = SeedGenome.CreateRandom(rng);
        var archiveG_unique = SeedGenome.CreateRandom(rng);

        var population = new List<IGenome> { popG1, popG2, popG3 };
        // Archive: one duplicate of popG2, one unique. Mimics Program.cs's union+dedup pattern.
        var archiveGenomes = new List<IGenome> { popG2, archiveG_unique };

        var union = population
            .Concat(archiveGenomes)
            .GroupBy(g => g.GenomeId)
            .Select(grp => grp.First())
            .ToList();

        Assert.Equal(4, union.Count);
        Assert.Contains(union, g => g.GenomeId == popG1.GenomeId);
        Assert.Contains(union, g => g.GenomeId == popG2.GenomeId);
        Assert.Contains(union, g => g.GenomeId == popG3.GenomeId);
        Assert.Contains(union, g => g.GenomeId == archiveG_unique.GenomeId);
    }

    [Fact]
    public void UnionEval_BeatsPopulationOnly_WhenArchiveHasStrongerGenome()
    {
        // Synthetic scenario: a stronger genome lives in the archive only (the population
        // bred it away, but the elite archive snapshotted it earlier). Evaluate population-only
        // and union-with-archive, then assert the union score is at least as good — and when
        // archive is strictly better than every population member, the union best is from the
        // archive.
        var config = MarketConfig.Default with
        {
            PopulationSize = 5,
            RunSeed = 42,
            InitialCapital = 10_000m
        };
        var (snapshots, prices, rawVols, rawFund) = CreateSyntheticData(300);

        var rng = new Rng64(123);
        var allCandidates = Enumerable.Range(0, 12)
            .Select(_ => (IGenome)SeedGenome.CreateRandom(rng))
            .ToList();
        var evaluator = new MarketEvaluator(config);
        var allResults = evaluator.Evaluate(allCandidates, snapshots, prices, rawVols, rawFund, 0);

        // Pick a genome with strictly the highest fitness (margin > 1e-4 over rank 2)
        // for unambiguous "archive is best" semantics.
        var ranked = allResults.OrderByDescending(kv => kv.Value.Fitness.Fitness).ToList();
        if (ranked[0].Value.Fitness.Fitness - ranked[1].Value.Fitness.Fitness < 1e-4f)
        {
            // No clear best — skip strict ID assertion, just check the union doesn't lose
            // info. (xUnit Skip not used because we still want to verify the union semantics.)
            var nonStrictUnion = allCandidates.Take(8)
                .GroupBy(g => g.GenomeId).Select(grp => grp.First()).ToList();
            var nonStrictResults = evaluator.Evaluate(nonStrictUnion, snapshots, prices, rawVols, rawFund, 0);
            Assert.Equal(nonStrictUnion.Count, nonStrictResults.Count);
            return;
        }

        var archiveOnlyId = ranked[0].Key;
        var archiveOnly = allCandidates.First(g => g.GenomeId == archiveOnlyId);
        var population = ranked.Skip(1).Take(5)
            .Select(kv => allCandidates.First(g => g.GenomeId == kv.Key))
            .ToList();

        // S11 union: pop + archive, dedup.
        var union = population.Concat(new[] { archiveOnly })
            .GroupBy(g => g.GenomeId).Select(grp => grp.First()).ToList();
        Assert.Equal(6, union.Count);

        var unionResults = evaluator.Evaluate(union, snapshots, prices, rawVols, rawFund, 0);
        var bestUnion = unionResults.OrderByDescending(kv => kv.Value.Fitness.Fitness).First();

        var popOnlyResults = evaluator.Evaluate(population, snapshots, prices, rawVols, rawFund, 0);
        var bestPopOnly = popOnlyResults.OrderByDescending(kv => kv.Value.Fitness.Fitness).First();

        // Determinism: same genome evaluated twice should produce same fitness (within 5dp).
        Assert.Equal(allResults[archiveOnlyId].Fitness.Fitness, unionResults[archiveOnlyId].Fitness.Fitness, 5);

        // Union best is the archive genome (strictly highest by ≥ 1e-4 margin guaranteed above).
        Assert.Equal(archiveOnlyId, bestUnion.Key);

        // Population-only eval misses it.
        Assert.NotEqual(archiveOnlyId, bestPopOnly.Key);

        // Union best fitness > population-only best fitness (strict, since archive is strictly better).
        Assert.True(bestUnion.Value.Fitness.Fitness > bestPopOnly.Value.Fitness.Fitness,
            $"union ({bestUnion.Value.Fitness.Fitness:F6}) should strictly beat pop-only ({bestPopOnly.Value.Fitness.Fitness:F6})");
    }

    [Fact]
    public void UnionEval_PopulationBest_WhenArchiveIsWeaker()
    {
        // Inverse: when the population's best beats every archive elite, union-eval still
        // picks the population genome. S11 doesn't penalize a healthy population.
        var config = MarketConfig.Default with
        {
            PopulationSize = 5,
            RunSeed = 42,
            InitialCapital = 10_000m
        };
        var (snapshots, prices, rawVols, rawFund) = CreateSyntheticData(300);

        var rng = new Rng64(123);
        var allCandidates = Enumerable.Range(0, 8)
            .Select(_ => (IGenome)SeedGenome.CreateRandom(rng))
            .ToList();
        var evaluator = new MarketEvaluator(config);
        var allResults = evaluator.Evaluate(allCandidates, snapshots, prices, rawVols, rawFund, 0);
        var ranked = allResults.OrderByDescending(kv => kv.Value.Fitness.Fitness).ToList();

        // Population gets the top 5; archive gets the bottom 3 (all weaker).
        var population = ranked.Take(5)
            .Select(kv => allCandidates.First(g => g.GenomeId == kv.Key))
            .ToList();
        var archive = ranked.Skip(5)
            .Select(kv => allCandidates.First(g => g.GenomeId == kv.Key))
            .ToList();

        var union = population.Concat(archive)
            .GroupBy(g => g.GenomeId).Select(grp => grp.First()).ToList();
        Assert.Equal(8, union.Count);

        var unionResults = evaluator.Evaluate(union, snapshots, prices, rawVols, rawFund, 0);
        var bestUnion = unionResults.OrderByDescending(kv => kv.Value.Fitness.Fitness).First();

        // Best should be the original top-1 (a population member).
        Assert.Equal(ranked[0].Key, bestUnion.Key);
        Assert.Contains(population, g => g.GenomeId == bestUnion.Key);
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
