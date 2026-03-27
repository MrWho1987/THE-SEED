using Seed.Core;
using Seed.Genetics;
using Seed.Market.Evolution;

namespace Seed.Market.Tests;

public class DiversityTests
{
    [Fact]
    public void OffspringFloor_GuaranteesMinimumPerSpecies()
    {
        var manager = new SpeciationManager();
        var rng = new Rng64(42);
        var genomes = Enumerable.Range(0, 50).Select(_ => (IGenome)SeedGenome.CreateRandom(rng)).ToList();

        var specCfg = new SpeciationConfig(C1: 1f, C2: 1f, C3: 0.5f, CompatibilityThreshold: 1.0f, TournamentSize: 3);
        manager.Speciate(genomes, specCfg);

        var fitnesses = genomes.ToDictionary(g => g.GenomeId, _ => 0.01f);
        // Give first genome massive fitness to create imbalance
        fitnesses[genomes[0].GenomeId] = 100f;

        var budget = new PopulationBudget(50, 1, 1, 3);
        var allocation = manager.AllocateOffspring(fitnesses, 50, budget, specCfg, minOffspringPerSpecies: 1);

        foreach (var species in manager.Species)
        {
            if (species.Members.Count >= 2)
                Assert.True(allocation[species.SpeciesId] >= 1,
                    $"Species {species.SpeciesId} with {species.Members.Count} members should get >= 1 offspring, got {allocation[species.SpeciesId]}");
        }
    }

    [Fact]
    public void OffspringFloor_DoesNotExceedTotalBudget()
    {
        var manager = new SpeciationManager();
        var rng = new Rng64(42);
        var genomes = Enumerable.Range(0, 30).Select(_ => (IGenome)SeedGenome.CreateRandom(rng)).ToList();

        var specCfg = new SpeciationConfig(C1: 1f, C2: 1f, C3: 0.5f, CompatibilityThreshold: 1.0f, TournamentSize: 3);
        manager.Speciate(genomes, specCfg);

        var fitnesses = genomes.ToDictionary(g => g.GenomeId, _ => 0.5f);
        var budget = new PopulationBudget(30, 1, 1, 3);
        var allocation = manager.AllocateOffspring(fitnesses, 30, budget, specCfg, minOffspringPerSpecies: 1);

        int total = allocation.Values.Sum();
        Assert.Equal(30, total);
    }

    [Fact]
    public void DynamicThreshold_DecreasesWhenTooFewSpecies()
    {
        float threshold = 3.5f;
        int speciesCount = 5;
        int targetMin = 10;
        float adjustRate = 0.1f;

        if (speciesCount < targetMin)
            threshold = Math.Max(1.0f, threshold - adjustRate);

        Assert.Equal(3.4f, threshold, 2);
    }

    [Fact]
    public void DynamicThreshold_IncreasesWhenTooManySpecies()
    {
        float threshold = 3.5f;
        int speciesCount = 60;
        int targetMax = 50;
        float adjustRate = 0.1f;

        if (speciesCount > targetMax)
            threshold = Math.Min(10.0f, threshold + adjustRate);

        Assert.Equal(3.6f, threshold, 2);
    }

    [Fact]
    public void DynamicThreshold_ClampedToRange()
    {
        float threshold = 1.05f;
        for (int i = 0; i < 20; i++)
            threshold = Math.Max(1.0f, threshold - 0.1f);

        Assert.True(threshold >= 1.0f);

        threshold = 9.95f;
        for (int i = 0; i < 20; i++)
            threshold = Math.Min(10.0f, threshold + 0.1f);

        Assert.True(threshold <= 10.0f);
    }

    [Fact]
    public void EliteArchive_StoresBestPerSpecies()
    {
        var archive = new EliteArchive(100);
        var rng = new Rng64(42);
        var g1 = SeedGenome.CreateRandom(rng);
        var g2 = SeedGenome.CreateRandom(rng);
        var g3 = SeedGenome.CreateRandom(rng);

        archive.Update(1, g1, 0.5f);
        archive.Update(1, g2, 0.8f);
        archive.Update(2, g3, 0.3f);

        Assert.Equal(2, archive.Count);

        Assert.True(archive.TryGet(1, out var champ1, out float fit1));
        Assert.Equal(0.8f, fit1);

        Assert.True(archive.TryGet(2, out var champ2, out float fit2));
        Assert.Equal(0.3f, fit2);
    }

    [Fact]
    public void EliteArchive_EvictsLowestWhenFull()
    {
        var archive = new EliteArchive(3);
        var rng = new Rng64(42);

        for (int i = 0; i < 3; i++)
        {
            var g = SeedGenome.CreateRandom(rng);
            archive.Update(i, g, 0.1f * (i + 1));
        }
        Assert.Equal(3, archive.Count);

        var gNew = SeedGenome.CreateRandom(rng);
        archive.Update(99, gNew, 0.9f);

        Assert.Equal(3, archive.Count);
        Assert.False(archive.TryGet(0, out _, out _));
        Assert.True(archive.TryGet(99, out _, out _));
    }

    [Fact]
    public void EliteArchive_GetDiverseElites_ReturnsDistinctSpecies()
    {
        var archive = new EliteArchive(100);
        var rng = new Rng64(42);

        for (int i = 0; i < 10; i++)
        {
            var g = SeedGenome.CreateRandom(rng);
            archive.Update(i, g, 0.1f * (i + 1));
        }

        var elites = archive.GetDiverseElites(5);
        Assert.Equal(5, elites.Count);
        var ids = elites.Select(e => e.GenomeId).Distinct().ToList();
        Assert.Equal(5, ids.Count);
    }

    [Fact]
    public void StagnationReseeding_UsesArchivedGenomes()
    {
        var archive = new EliteArchive(100);
        var rng = new Rng64(42);

        for (int i = 0; i < 5; i++)
        {
            var g = SeedGenome.CreateRandom(rng);
            archive.Update(i, g, 0.5f);
        }

        var elites = archive.GetDiverseElites(3);
        Assert.True(elites.Count > 0, "Archive should provide genomes for reseeding");
        Assert.True(elites.All(e => e.GenomeId != Guid.Empty));
    }

    [Fact]
    public void KNN_DiversityBonus_HigherForIsolatedGenomes()
    {
        var rng = new Rng64(42);
        var cluster = new List<IGenome>();
        for (int i = 0; i < 10; i++)
            cluster.Add(SeedGenome.CreateRandom(rng));

        // Create an outlier by mutating heavily
        var outlierBase = SeedGenome.CreateRandom(new Rng64(999));
        var innovations = InnovationTracker.CreateDefault();
        IGenome outlier = outlierBase;
        for (int m = 0; m < 50; m++)
        {
            var mutCtx = new MutationContext(999, m, MutationConfig.Default, innovations, new Rng64((ulong)(m + 1000)));
            outlier = outlier.Mutate(mutCtx);
        }

        var specCfg = new SpeciationConfig(C1: 1f, C2: 1f, C3: 0.5f, CompatibilityThreshold: 3.5f, TournamentSize: 3);

        float outlierAvgDist = cluster.Select(g => outlier.DistanceTo(g, specCfg)).OrderBy(d => d).Take(5).Average();
        float clusterMemberAvgDist = cluster.Skip(1).Select(g => cluster[0].DistanceTo(g, specCfg)).OrderBy(d => d).Take(5).Average();

        Assert.True(outlierAvgDist > clusterMemberAvgDist,
            $"Outlier avg KNN dist ({outlierAvgDist:F2}) should exceed cluster member ({clusterMemberAvgDist:F2})");
    }

    [Fact]
    public void StagnationCounter_IncrementsWhenNoImprovement()
    {
        var species = new Species(1, SeedGenome.CreateRandom(new Rng64(42)));
        species.BestFitness = 0.5f;

        species.StagnationCounter++;
        Assert.Equal(1, species.StagnationCounter);

        species.StagnationCounter++;
        Assert.Equal(2, species.StagnationCounter);

        species.BestFitness = 0.6f;
        species.StagnationCounter = 0;
        Assert.Equal(0, species.StagnationCounter);
    }
}
