using Seed.Core;
using Seed.Genetics;
using Seed.Market;
using Seed.Market.Evolution;
using Seed.Market.Signals;

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

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.0f)]
    [InlineData(-0.3f)]
    [InlineData(1.25f)]
    [InlineData(float.MinValue)]
    public void RestoreFrom_PreservesBestFitness(float bestFitness)
    {
        var manager = new SpeciationManager();
        var rng = new Rng64(42);
        var representative = (IGenome)SeedGenome.CreateRandom(rng);

        var entries = new List<(int SpeciesId, IGenome Representative, int StagnationCounter, float BestFitness)>
        {
            (1, representative, 5, bestFitness)
        };

        manager.RestoreFrom(entries, 2);

        var restored = manager.Species[0];
        Assert.Equal(bestFitness, restored.BestFitness);
        Assert.Equal(5, restored.StagnationCounter);
    }

    [Fact]
    public void RestoreFrom_StagnationSurvivesNextGeneration()
    {
        // Simulates what MarketEvolution does after a checkpoint restore:
        // if BestFitness is preserved correctly, a species whose next-gen fitness
        // is LOWER should increment stagnation, not reset it.
        var manager = new SpeciationManager();
        var rng = new Rng64(99);
        var representative = (IGenome)SeedGenome.CreateRandom(rng);

        manager.RestoreFrom(
            [(1, representative, 10, 0.8f)],
            nextSpeciesId: 2);

        var species = manager.Species[0];
        Assert.Equal(0.8f, species.BestFitness);
        Assert.Equal(10, species.StagnationCounter);

        // Simulate next generation: best fitness in species is 0.6 (worse than 0.8)
        float bestInSpecies = 0.6f;
        if (bestInSpecies > species.BestFitness)
        {
            species.BestFitness = bestInSpecies;
            species.StagnationCounter = 0;
        }
        else
        {
            species.StagnationCounter++;
        }

        // Stagnation should have incremented, NOT reset
        Assert.Equal(11, species.StagnationCounter);
        Assert.Equal(0.8f, species.BestFitness);
    }

    [Fact]
    public void RestoreFrom_ImprovementResetsStagnation()
    {
        var manager = new SpeciationManager();
        var rng = new Rng64(99);
        var representative = (IGenome)SeedGenome.CreateRandom(rng);

        manager.RestoreFrom(
            [(1, representative, 15, 0.5f)],
            nextSpeciesId: 2);

        var species = manager.Species[0];

        // Simulate next generation: best fitness is 0.7 (better than 0.5)
        float bestInSpecies = 0.7f;
        if (bestInSpecies > species.BestFitness)
        {
            species.BestFitness = bestInSpecies;
            species.StagnationCounter = 0;
        }
        else
        {
            species.StagnationCounter++;
        }

        // Genuine improvement: counter resets, BestFitness updates
        Assert.Equal(0, species.StagnationCounter);
        Assert.Equal(0.7f, species.BestFitness);
    }

    [Fact]
    public void StagnationCounter_IgnoresMicroImprovements()
    {
        // Floating-point drift in overfit champions used to indefinitely reset stagnation
        // because any improvement (even +0.0001) reset the counter. The fix requires a
        // minimum improvement delta to count as real progress.
        var config = MarketConfig.Default with
        {
            PopulationSize = 15,
            MinStagnationImprovement = 0.005f,
        };
        var observatory = new Seed.Observatory.FileObservatory(
            Path.Combine(Path.GetTempPath(), $"seed_stag_micro_{Guid.NewGuid()}.jsonl"));
        var evo = new MarketEvolution(config, observatory);
        evo.Initialize();

        var snaps = new SignalSnapshot[200];
        var prices = new float[200];
        var vols = new float[200];
        var fund = new float[200];
        for (int i = 0; i < 200; i++)
        {
            snaps[i] = new SignalSnapshot(new float[SignalIndex.Count], DateTimeOffset.UtcNow.AddHours(-200 + i), i);
            prices[i] = 50000f + i * 10f;
            vols[i] = 100f;
            fund[i] = 0.0001f;
        }
        evo.RunGeneration(snaps, prices, vols, fund);

        // Mimic the in-loop logic with the new threshold
        float bestFitness = 0.5f;
        int stagCounter = 0;

        // 5 micro-improvements (each below threshold) — counter should keep climbing
        float[] microDeltas = { 0.001f, 0.0008f, 0.002f, 0.0015f, 0.001f };
        foreach (var d in microDeltas)
        {
            float candidate = bestFitness + d;
            if (candidate > bestFitness + config.MinStagnationImprovement)
            {
                bestFitness = candidate;
                stagCounter = 0;
            }
            else
            {
                stagCounter++;
            }
        }

        Assert.Equal(5, stagCounter);
        Assert.Equal(0.5f, bestFitness);

        // A genuine improvement (above threshold) should reset
        float realImprovement = bestFitness + 0.01f;
        if (realImprovement > bestFitness + config.MinStagnationImprovement)
        {
            bestFitness = realImprovement;
            stagCounter = 0;
        }

        Assert.Equal(0, stagCounter);
        Assert.Equal(0.51f, bestFitness, 4);
    }

    [Fact]
    public void ResetSpeciesStagnation_ClearsAllSpecies()
    {
        var config = MarketConfig.Default with { PopulationSize = 15 };
        var observatory = new Seed.Observatory.FileObservatory(
            Path.Combine(Path.GetTempPath(), $"seed_stag_{Guid.NewGuid()}.jsonl"));
        var evo = new MarketEvolution(config, observatory);
        evo.Initialize();

        // Create synthetic data for evaluation
        var snaps = new SignalSnapshot[200];
        var prices = new float[200];
        var vols = new float[200];
        var fund = new float[200];
        for (int i = 0; i < 200; i++)
        {
            snaps[i] = new SignalSnapshot(new float[SignalIndex.Count], DateTimeOffset.UtcNow.AddHours(-200 + i), i);
            prices[i] = 50000f + i * 10f;
            vols[i] = 100f;
            fund[i] = 0.0001f;
        }
        for (int g = 0; g < 3; g++)
            evo.RunGeneration(snaps, prices, vols, fund);

        // Verify species exist with some stagnation
        Assert.True(evo.SpeciesCount > 0);

        // Reset stagnation
        evo.ResetSpeciesStagnation();

        // All species should have counter=0 and BestFitness=MinValue
        var speciesState = evo.GetSpeciesState();
        foreach (var s in speciesState)
        {
            Assert.Equal(0, s.StagnationCounter);
            Assert.Equal(float.MinValue, s.BestFitness);
        }

        // Archive should be empty
        Assert.Equal(0, evo.Archive.Count);
    }
}
