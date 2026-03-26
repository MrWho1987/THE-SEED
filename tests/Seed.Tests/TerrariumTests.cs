using Seed.Core;
using Seed.Worlds;
using Seed.Genetics;
using Seed.Brain;
using Seed.Development;
using Seed.Agents;

namespace Seed.Tests;

public class TerrariumTests
{
    private static readonly WorldBudget TestBudget = new(64, 64, 0f, 0f, 20, FoodClusters: 3);

    [Fact]
    public void SpawnAgent_ReusesDeadSlot()
    {
        var arena = new SharedArena();
        var budget = new WorldBudget(16, 16, 0f, 2.0f, 0);
        arena.Reset(42, budget, 4);

        int initialCount = arena.AgentCount;
        Assert.Equal(4, initialCount);

        var actions = new float[initialCount][];
        for (int i = 0; i < initialCount; i++)
            actions[i] = new float[ContinuousWorld.ActuatorCount];

        for (int t = 0; t < 10000; t++)
        {
            arena.StepAll(actions);
            if (!arena.AgentAlive(0)) break;
        }

        if (!arena.AgentAlive(0))
        {
            int idx = arena.SpawnAgent(8f, 8f, 0f, ContinuousWorld.OffspringEnergy);
            Assert.Equal(0, idx);
            Assert.Equal(initialCount, arena.AgentCount);
            Assert.True(arena.AgentAlive(0));
            Assert.Equal(ContinuousWorld.OffspringEnergy, arena.AgentEnergy(0));
        }
    }

    [Fact]
    public void SpawnAgent_GrowsWhenAllAlive()
    {
        var arena = new SharedArena();
        arena.Reset(42, new WorldBudget(64, 64, 0f, 0f, 50), 4);

        Assert.Equal(4, arena.AgentCount);
        for (int i = 0; i < 4; i++)
            Assert.True(arena.AgentAlive(i));

        int idx = arena.SpawnAgent(32f, 32f, 0f, ContinuousWorld.OffspringEnergy);
        Assert.Equal(4, idx);
        Assert.Equal(5, arena.AgentCount);
        Assert.True(arena.AgentAlive(4));
    }

    [Fact]
    public void StepAll_WorksAfterSpawn()
    {
        var arena = new SharedArena();
        arena.Reset(42, new WorldBudget(64, 64, 0f, 0f, 50), 2);

        int idx = arena.SpawnAgent(32f, 32f, 0f, 1.0f);
        Assert.Equal(2, idx);
        Assert.Equal(3, arena.AgentCount);

        for (int t = 0; t < 100; t++)
        {
            var actions = new float[arena.AgentCount][];
            for (int i = 0; i < arena.AgentCount; i++)
                actions[i] = new float[] { 0.5f, 0.1f, 0, 0, 0, 0 };
            arena.StepAll(actions);
        }

        bool anyAlive = false;
        for (int i = 0; i < arena.AgentCount; i++)
        {
            if (arena.AgentAlive(i))
            {
                anyAlive = true;
                Assert.True(arena.AgentAge(i) > 0, $"Agent {i} should have age > 0 after stepping");
            }
        }
        Assert.True(anyAlive, "At least one agent should still be alive after 100 ticks");
    }

    [Fact]
    public void DeductEnergy_ReducesParentEnergy()
    {
        var arena = new SharedArena();
        arena.Reset(42, new WorldBudget(64, 64, 0f, 0f, 50), 2);

        float before = arena.AgentEnergy(0);
        arena.DeductEnergy(0, ContinuousWorld.ReproductionCost);
        float after = arena.AgentEnergy(0);

        Assert.Equal(before - ContinuousWorld.ReproductionCost, after, 5);
    }

    [Fact]
    public void IsValidSpawnPosition_RejectsOutOfBounds()
    {
        var arena = new SharedArena();
        arena.Reset(42, new WorldBudget(64, 64, 0f, 0f, 10), 2);

        Assert.False(arena.IsValidSpawnPosition(0f, 0f));
        Assert.False(arena.IsValidSpawnPosition(64f, 64f));
        Assert.False(arena.IsValidSpawnPosition(-1f, 32f));
        Assert.True(arena.IsValidSpawnPosition(32f, 32f));
    }

    [Fact]
    public void AgentAge_IncrementsPerTick()
    {
        var arena = new SharedArena();
        arena.Reset(42, new WorldBudget(64, 64, 0f, 0f, 50), 2);

        Assert.Equal(0, arena.AgentAge(0));

        var actions = new float[2][];
        for (int i = 0; i < 2; i++) actions[i] = new float[ContinuousWorld.ActuatorCount];

        arena.StepAll(actions);
        Assert.Equal(1, arena.AgentAge(0));

        for (int t = 0; t < 9; t++) arena.StepAll(actions);
        Assert.Equal(10, arena.AgentAge(0));
    }

    [Fact]
    public void DynamicArena_DeterministicWithSameSeed()
    {
        var budget = new WorldBudget(64, 64, 0.05f, 0.02f, 20);

        ArenaAgent[] Run(ulong seed)
        {
            var arena = new SharedArena();
            arena.Reset(seed, budget, 4);
            arena.SpawnAgent(32f, 32f, 0f, 0.8f);

            var actions = new float[arena.AgentCount][];
            for (int i = 0; i < arena.AgentCount; i++)
                actions[i] = new float[] { 0.5f, 0.1f, 0, 0, 0, 0 };

            for (int t = 0; t < 50; t++)
                arena.StepAll(actions);
            return arena.Agents.ToArray();
        }

        var r1 = Run(42);
        var r2 = Run(42);

        Assert.Equal(r1.Length, r2.Length);
        for (int i = 0; i < r1.Length; i++)
        {
            Assert.Equal(r1[i].X, r2[i].X);
            Assert.Equal(r1[i].Y, r2[i].Y);
            Assert.Equal(r1[i].Energy, r2[i].Energy);
            Assert.Equal(r1[i].Alive, r2[i].Alive);
            Assert.Equal(r1[i].Age, r2[i].Age);
        }
    }

    // --- Phase 2: Reproduction and Population Dynamics ---

    [Fact]
    public void Reproduction_RequiresEnergyThreshold()
    {
        var arena = new SharedArena();
        arena.Reset(42, new WorldBudget(64, 64, 0f, 0f, 50), 2);

        Assert.Equal(ContinuousWorld.InitialEnergy, arena.AgentEnergy(0));
        Assert.True(arena.AgentEnergy(0) < ContinuousWorld.ReproductionThreshold,
            "Initial energy (1.0) should be below reproduction threshold (2.0)");
    }

    [Fact]
    public void Reproduction_CooldownPreventsChainSplit()
    {
        var arena = new SharedArena();
        arena.Reset(42, new WorldBudget(64, 64, 0f, 0f, 50), 2);

        int lastReproTick = arena.AgentLastReproTick(0);
        Assert.True(arena.Tick - lastReproTick >= ContinuousWorld.ReproductionCooldown,
            $"Newly placed agents should have cooldown satisfied. Tick={arena.Tick}, LastRepro={lastReproTick}");

        var actions = new float[arena.AgentCount][];
        for (int i = 0; i < arena.AgentCount; i++)
            actions[i] = new float[ContinuousWorld.ActuatorCount];
        for (int t = 0; t < 10; t++) arena.StepAll(actions);

        arena.DeductEnergy(0, 0f);
        int newLastReproTick = arena.AgentLastReproTick(0);
        Assert.Equal(arena.Tick, newLastReproTick);
        Assert.True(arena.Tick - newLastReproTick < ContinuousWorld.ReproductionCooldown,
            "Agent that just 'reproduced' should be within cooldown");
    }

    [Fact]
    public void Reproduction_OffspringHasCorrectEnergy()
    {
        var arena = new SharedArena();
        arena.Reset(42, new WorldBudget(64, 64, 0f, 0f, 50), 2);

        int childIdx = arena.SpawnAgent(32f, 32f, 0f, ContinuousWorld.OffspringEnergy);
        Assert.Equal(ContinuousWorld.OffspringEnergy, arena.AgentEnergy(childIdx), 5);
    }

    [Fact]
    public void Reproduction_ParentGenomeUnchanged()
    {
        var rng = new Rng64(42);
        var parent = SeedGenome.CreateRandom(rng);
        int parentConnsBefore = parent.Cppn.Connections.Count;
        var parentId = parent.GenomeId;

        var child = (SeedGenome)parent.CloneGenome();
        var innovations = InnovationTracker.CreateDefault();
        var mutCtx = new MutationContext(42, 0, MutationConfig.Default, innovations, new Rng64(123));
        child = (SeedGenome)child.Mutate(mutCtx);

        Assert.Equal(parentConnsBefore, parent.Cppn.Connections.Count);
        Assert.Equal(parentId, parent.GenomeId);
        Assert.NotEqual(parent.GenomeId, child.GenomeId);
    }

    [Fact]
    public void Reproduction_OffspringGenomeDiffers()
    {
        var rng = new Rng64(42);
        var parent = SeedGenome.CreateRandom(rng);
        var innovations = InnovationTracker.CreateDefault();
        var mutCtx = new MutationContext(42, 0, MutationConfig.Default, innovations, new Rng64(999));
        var child = (SeedGenome)parent.CloneGenome().Mutate(mutCtx);

        Assert.NotEqual(parent.GenomeId, child.GenomeId);
    }

    [Fact]
    public void PopulationRegulation_DoesNotExplode()
    {
        var arena = new SharedArena();
        var budget = new WorldBudget(64, 64, 0.05f, 0.02f, 50, FoodClusters: 3,
            AmbientEnergyRate: 0.00015f, CorpseEnergyBase: 0.3f);
        int initialPop = 20;
        arena.Reset(42, budget, initialPop);

        var rng = new Rng64(42);
        var developer = new BrainDeveloper(AgentConfig.Default.TotalSensorCount, ContinuousWorld.ActuatorCount);
        var innovations = InnovationTracker.CreateDefault();
        var genomes = new SeedGenome[initialPop];
        for (int i = 0; i < initialPop; i++)
            genomes[i] = SeedGenome.CreateRandom(rng);

        int maxPop = 0;
        bool anyDeath = false;

        for (int tick = 0; tick < 5000; tick++)
        {
            int n = arena.AgentCount;
            var actions = new float[n][];
            for (int i = 0; i < n; i++)
                actions[i] = new float[] { 0.5f, 0.1f, 0, 0, 0, 0 };

            bool[] aliveBefore = new bool[n];
            for (int i = 0; i < n; i++) aliveBefore[i] = arena.AgentAlive(i);

            arena.StepAll(actions);

            for (int i = 0; i < n; i++)
                if (aliveBefore[i] && !arena.AgentAlive(i)) anyDeath = true;

            int births = 0;
            for (int i = 0; i < arena.AgentCount && births < ContinuousWorld.MaxBirthsPerTick; i++)
            {
                if (!arena.AgentAlive(i)) continue;
                if (arena.AgentEnergy(i) < ContinuousWorld.ReproductionThreshold) continue;
                if (arena.AgentAge(i) < ContinuousWorld.MinReproductionAge) continue;
                if (tick - arena.AgentLastReproTick(i) < ContinuousWorld.ReproductionCooldown) continue;

                float px = arena.AgentX(i), py = arena.AgentY(i);
                float sx = px + ContinuousWorld.AgentRadius * 3f;
                float sy = py;
                if (!arena.IsValidSpawnPosition(sx, sy)) continue;

                arena.SpawnAgent(sx, sy, 0f, ContinuousWorld.OffspringEnergy);
                arena.DeductEnergy(i, ContinuousWorld.ReproductionCost);
                births++;
            }

            int alive = 0;
            for (int i = 0; i < arena.AgentCount; i++)
                if (arena.AgentAlive(i)) alive++;
            if (alive > maxPop) maxPop = alive;
        }

        Assert.True(maxPop < 200, $"Population should not explode. Max was {maxPop}");
        Assert.True(anyDeath, "At least one agent should have died (starvation is real)");
    }

    [Fact]
    public void TotalExtinction_ReseedRecovers()
    {
        var arena = new SharedArena();
        var budget = new WorldBudget(16, 16, 0f, 2.0f, 0);
        arena.Reset(42, budget, 2);

        var actions = new float[arena.AgentCount][];
        for (int i = 0; i < arena.AgentCount; i++)
            actions[i] = new float[ContinuousWorld.ActuatorCount];

        for (int t = 0; t < 20000; t++)
        {
            arena.StepAll(actions);
            bool anyAlive = false;
            for (int i = 0; i < arena.AgentCount; i++)
                if (arena.AgentAlive(i)) { anyAlive = true; break; }
            if (!anyAlive) break;
        }

        bool allDead = true;
        for (int i = 0; i < arena.AgentCount; i++)
            if (arena.AgentAlive(i)) { allDead = false; break; }

        if (allDead)
        {
            for (int i = 0; i < 8; i++)
            {
                var seedRng = new Rng64(SeedDerivation.AgentSeed(42, 99999, i));
                float x = seedRng.NextFloat(3f, budget.WorldWidth - 3f);
                float y = seedRng.NextFloat(3f, budget.WorldHeight - 3f);
                arena.SpawnAgent(x, y, seedRng.NextFloat(0f, MathF.PI * 2f), ContinuousWorld.OffspringEnergy);
            }

            int aliveAfterReseed = 0;
            for (int i = 0; i < arena.AgentCount; i++)
                if (arena.AgentAlive(i)) aliveAfterReseed++;

            Assert.Equal(8, aliveAfterReseed);
        }
    }

    [Fact]
    public void FullPipeline_CloneMutateDevelopRun()
    {
        var rng = new Rng64(42);
        var parent = SeedGenome.CreateRandom(rng);
        var innovations = InnovationTracker.CreateDefault();
        var developer = new BrainDeveloper(AgentConfig.Default.TotalSensorCount, ContinuousWorld.ActuatorCount);

        var child = (SeedGenome)parent.CloneGenome();
        var mutCtx = new MutationContext(42, 0, MutationConfig.Default, innovations, new Rng64(123));
        child = (SeedGenome)child.Mutate(mutCtx);

        var devCtx = new DevelopmentContext(42, 0);
        var brain = developer.CompileGraph(child, new DevelopmentBudget(), devCtx);
        Assert.NotNull(brain);

        var runtime = new BrainRuntime(brain, child.Learn, child.Stable, 3);
        runtime.Reset();

        var inputs = new float[AgentConfig.Default.TotalSensorCount];
        var outputs = runtime.Step(inputs, new BrainStepContext(0));
        Assert.Equal(ContinuousWorld.ActuatorCount, outputs.Length);

        Assert.Equal(0f, runtime.GetInstabilityPenalty());
    }
}
