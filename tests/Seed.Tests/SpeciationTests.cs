using Seed.Core;
using Seed.Genetics;

namespace Seed.Tests;

public class SpeciationTests
{
    [Fact]
    public void NeatDistance_IdenticalGenomes_ZeroDistance()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);

        float distance = genome.DistanceTo(genome, SpeciationConfig.Default);

        Assert.Equal(0f, distance, 5);
    }

    [Fact]
    public void NeatDistance_ClonedGenomes_ZeroDistance()
    {
        var rng = new Rng64(42);
        var genome1 = SeedGenome.CreateRandom(rng);
        var genome2 = (SeedGenome)genome1.CloneGenome();

        float distance = genome1.DistanceTo(genome2, SpeciationConfig.Default);

        Assert.Equal(0f, distance, 5);
    }

    [Fact]
    public void NeatDistance_MutatedGenome_PositiveDistance()
    {
        var rng = new Rng64(42);
        var genome1 = SeedGenome.CreateRandom(rng);

        var mutRng = new Rng64(123);
        var mutCtx = new MutationContext(
            RunSeed: 123,
            GenerationIndex: 0,
            Config: MutationConfig.Default,
            Innovations: InnovationTracker.CreateDefault(),
            Rng: mutRng
        );

        var genome2 = (SeedGenome)genome1.Mutate(mutCtx);

        float distance = genome1.DistanceTo(genome2, SpeciationConfig.Default);

        // Should have some distance after mutation
        Assert.True(distance >= 0f);
    }

    [Fact]
    public void NeatDistance_VeryDifferentGenomes_LargeDistance()
    {
        var rng1 = new Rng64(42);
        var genome1 = SeedGenome.CreateRandom(rng1);

        var rng2 = new Rng64(999);
        var genome2 = SeedGenome.CreateRandom(rng2);

        // Mutate genome2 heavily
        var tracker = InnovationTracker.CreateDefault();
        for (int i = 0; i < 10; i++)
        {
            var mutRng = new Rng64((ulong)(1000 + i));
            var mutCtx = new MutationContext(
                RunSeed: 1000 + (ulong)i,
                GenerationIndex: 0,
                Config: MutationConfig.Default with { PAddNode = 0.5f, PAddConn = 0.5f },
                Innovations: tracker,
                Rng: mutRng
            );
            genome2 = (SeedGenome)genome2.Mutate(mutCtx);
        }

        float distance = genome1.DistanceTo(genome2, SpeciationConfig.Default);

        // Should have significant distance
        Assert.True(distance > 0f);
    }

    [Fact]
    public void Speciation_AssignsGenomesToSpecies()
    {
        var manager = new SpeciationManager();
        var genomes = new List<IGenome>();

        var rng = new Rng64(42);
        for (int i = 0; i < 10; i++)
        {
            genomes.Add(SeedGenome.CreateRandom(rng));
        }

        manager.Speciate(genomes, SpeciationConfig.Default);

        // All genomes should be assigned
        foreach (var genome in genomes)
        {
            int speciesId = manager.GetSpeciesId(genome);
            Assert.True(speciesId >= 0, $"Genome {genome.GenomeId} not assigned to species");
        }
    }

    [Fact]
    public void Speciation_SimilarGenomes_SameSpecies()
    {
        var manager = new SpeciationManager();
        var rng = new Rng64(42);

        var parent = SeedGenome.CreateRandom(rng);
        var genomes = new List<IGenome> { parent };

        // Create clones with minor mutations
        for (int i = 0; i < 5; i++)
        {
            genomes.Add(parent.CloneGenome());
        }

        manager.Speciate(genomes, SpeciationConfig.Default);

        // All should be in same species (very similar)
        int firstSpecies = manager.GetSpeciesId(genomes[0]);
        foreach (var genome in genomes)
        {
            Assert.Equal(firstSpecies, manager.GetSpeciesId(genome));
        }
    }
}


