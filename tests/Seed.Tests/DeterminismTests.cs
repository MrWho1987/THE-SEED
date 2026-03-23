using Seed.Core;
using Seed.Worlds;
using Seed.Agents;
using Seed.Genetics;
using Seed.Development;
using Seed.Brain;

namespace Seed.Tests;

public class DeterminismTests
{
    [Fact]
    public void World_SameSeed_ProducesSameLayout()
    {
        var world1 = new ContinuousWorld();
        var world2 = new ContinuousWorld();

        var budget = new WorldBudget();

        world1.Reset(12345, budget);
        world2.Reset(12345, budget);

        // Agent positions should be identical
        Assert.Equal(world1.AgentX, world2.AgentX);
        Assert.Equal(world1.AgentY, world2.AgentY);
        Assert.Equal(world1.AgentHeading, world2.AgentHeading);
    }

    [Fact]
    public void World_DifferentSeeds_ProducesDifferentLayouts()
    {
        var world1 = new ContinuousWorld();
        var world2 = new ContinuousWorld();

        var budget = new WorldBudget();

        world1.Reset(12345, budget);
        world2.Reset(54321, budget);

        // Very unlikely to have same positions
        bool sameX = Math.Abs(world1.AgentX - world2.AgentX) < 0.001f;
        bool sameY = Math.Abs(world1.AgentY - world2.AgentY) < 0.001f;

        Assert.False(sameX && sameY);
    }

    [Fact]
    public void GenomeSerialization_Roundtrip()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);

        string json = genome.ToJson();
        var restored = SeedGenome.FromJson(json);

        Assert.Equal(genome.GenomeId, restored.GenomeId);
        Assert.Equal(genome.Cppn.Nodes.Count, restored.Cppn.Nodes.Count);
        Assert.Equal(genome.Cppn.Connections.Count, restored.Cppn.Connections.Count);
    }

    [Fact]
    public void BrainDevelopment_SameGenome_ProducesSameGraph()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);

        var agentConfig = AgentConfig.Default;
        var developer = new BrainDeveloper(agentConfig.TotalSensorCount, ContinuousWorld.ActuatorCount);

        var budget = new DevelopmentBudget();
        var ctx = new DevelopmentContext(42, 0);

        var graph1 = developer.CompileGraph(genome, budget, ctx);
        var graph2 = developer.CompileGraph(genome, budget, ctx);

        Assert.Equal(graph1.NodeCount, graph2.NodeCount);
        Assert.Equal(graph1.EdgeCount, graph2.EdgeCount);
    }

    [Fact]
    public void BrainStep_Deterministic()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);

        var agentConfig = AgentConfig.Default;
        var developer = new BrainDeveloper(agentConfig.TotalSensorCount, ContinuousWorld.ActuatorCount);

        var budget = new DevelopmentBudget();
        var ctx = new DevelopmentContext(42, 0);
        var graph = developer.CompileGraph(genome, budget, ctx);

        var brain1 = new BrainRuntime(graph, genome.Learn, genome.Stable);
        var brain2 = new BrainRuntime(graph, genome.Learn, genome.Stable);

        brain1.Reset();
        brain2.Reset();

        var inputs = new float[agentConfig.TotalSensorCount];
        for (int i = 0; i < inputs.Length; i++)
            inputs[i] = 0.5f;

        var outputs1 = brain1.Step(inputs, new BrainStepContext(0)).ToArray();
        var outputs2 = brain2.Step(inputs, new BrainStepContext(0)).ToArray();

        Assert.Equal(outputs1.Length, outputs2.Length);
        for (int i = 0; i < outputs1.Length; i++)
        {
            Assert.Equal(outputs1[i], outputs2[i], 5);
        }
    }

    [Fact]
    public void Episode_SameSeed_ProducesSameResult()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);

        var agentConfig = AgentConfig.Default;
        var developer = new BrainDeveloper(agentConfig.TotalSensorCount, ContinuousWorld.ActuatorCount);

        var devBudget = new DevelopmentBudget();
        var devCtx = new DevelopmentContext(42, 0);
        var graph = developer.CompileGraph(genome, devBudget, devCtx);

        var worldBudget = new WorldBudget();
        var runtimeBudget = new RuntimeBudget(MaxTicksPerEpisode: 100);

        // Run episode 1
        var world1 = new ContinuousWorld();
        world1.Reset(12345, worldBudget);
        var body1 = new AgentBody(world1, agentConfig);
        body1.Reset(new BodyResetContext(12345));
        var brain1 = new BrainRuntime(graph, genome.Learn, genome.Stable);
        brain1.Reset();

        float[] sensors1 = new float[agentConfig.TotalSensorCount];
        float energy1 = 0f;

        for (int tick = 0; tick < runtimeBudget.MaxTicksPerEpisode; tick++)
        {
            body1.ReadSensors(sensors1);
            var outputs = brain1.Step(sensors1, new BrainStepContext(tick));
            var result = world1.Step(outputs);
            body1.ApplyWorldSignals(result.Signals);
            brain1.Learn(result.Modulators, new BrainLearnContext(tick));
            if (result.Done) break;
        }
        energy1 = body1.GetState().Energy;

        // Run episode 2 with same seeds
        var world2 = new ContinuousWorld();
        world2.Reset(12345, worldBudget);
        var body2 = new AgentBody(world2, agentConfig);
        body2.Reset(new BodyResetContext(12345));
        var brain2 = new BrainRuntime(graph, genome.Learn, genome.Stable);
        brain2.Reset();

        float[] sensors2 = new float[agentConfig.TotalSensorCount];
        float energy2 = 0f;

        for (int tick = 0; tick < runtimeBudget.MaxTicksPerEpisode; tick++)
        {
            body2.ReadSensors(sensors2);
            var outputs = brain2.Step(sensors2, new BrainStepContext(tick));
            var result = world2.Step(outputs);
            body2.ApplyWorldSignals(result.Signals);
            brain2.Learn(result.Modulators, new BrainLearnContext(tick));
            if (result.Done) break;
        }
        energy2 = body2.GetState().Energy;

        // Results should be identical
        Assert.Equal(energy1, energy2, 5);
    }
}


