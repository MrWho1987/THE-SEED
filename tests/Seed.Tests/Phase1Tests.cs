using Seed.Core;
using Seed.Genetics;
using Seed.Development;
using Seed.Brain;
using Seed.Agents;
using Seed.Worlds;
using Seed.Evolution;
using Seed.Observatory;

namespace Seed.Tests;

public class Phase1Tests
{
    [Fact]
    public void CloneGenome_PreservesId_WhenNoNewIdProvided()
    {
        var rng = new Rng64(42);
        var original = SeedGenome.CreateRandom(rng);

        var clone = (SeedGenome)original.CloneGenome();

        Assert.Equal(original.GenomeId, clone.GenomeId);
        Assert.Equal(original.Cppn.Connections.Count, clone.Cppn.Connections.Count);
    }

    [Fact]
    public void CloneGenome_UsesNewId_WhenProvided()
    {
        var rng = new Rng64(42);
        var original = SeedGenome.CreateRandom(rng);
        var newId = Guid.NewGuid();

        var clone = (SeedGenome)original.CloneGenome(newId);

        Assert.Equal(newId, clone.GenomeId);
        Assert.NotEqual(original.GenomeId, clone.GenomeId);
    }

    [Fact]
    public void Evaluator_UsesArenaRounds_FromContext()
    {
        var rng = new Rng64(42);
        var population = new List<IGenome>();
        for (int i = 0; i < 4; i++)
            population.Add(SeedGenome.CreateRandom(rng));

        var agentConfig = AgentConfig.Default;
        var evaluator = new Evaluator(agentConfig.TotalSensorCount, ContinuousWorld.ActuatorCount);

        var ctx = new EvaluationContext(
            RunSeed: 42,
            GenerationIndex: 0,
            WorldBundleKey: 0,
            DevelopmentBudget: new DevelopmentBudget(),
            RuntimeBudget: new RuntimeBudget(MaxTicksPerEpisode: 50),
            WorldBudget: new WorldBudget(),
            ArenaRounds: 2
        );

        var results = evaluator.EvaluateArena(population, ctx);

        Assert.Equal(4, results.Count);
        foreach (var r in results.Values)
            Assert.Equal(2, r.PerWorld.Length);
    }

    [Fact]
    public void BrainRuntime_UsesMicroStepsPerTick()
    {
        // Build a known-recurrent graph: input(0) -> hidden(2) -> output(1), hidden(2) -> hidden(2) self-loop
        var nodes = new List<BrainNode>
        {
            new(0, BrainNodeType.Input, 0f, 0f, 0, new NodeMetadata()),
            new(1, BrainNodeType.Output, 1f, 0f, 2, new NodeMetadata()),
            new(2, BrainNodeType.Hidden, 0.5f, 0f, 1, new NodeMetadata()),
        };
        var incoming = new Dictionary<int, List<BrainEdge>>
        {
            [0] = new(),
            [1] = new() { new BrainEdge(2, 1, 0.8f, 0.8f, 1f, new EdgeMetadata()) },
            [2] = new()
            {
                new BrainEdge(0, 2, 0.9f, 0.9f, 1f, new EdgeMetadata()),
                new BrainEdge(2, 2, 0.5f, 0.5f, 1f, new EdgeMetadata()), // self-loop
            },
        };
        var graph = new BrainGraph(nodes, incoming, 1, 1, 3);
        var learn = LearningParams.Default;
        var stable = StabilityParams.Default;

        var brain1 = new BrainRuntime(graph, learn, stable, microStepsPerTick: 1);
        var brain2 = new BrainRuntime(graph, learn, stable, microStepsPerTick: 5);
        brain1.Reset();
        brain2.Reset();

        var inputs = new float[] { 1.0f };

        float[] out1 = Array.Empty<float>(), out2 = Array.Empty<float>();
        for (int t = 0; t < 5; t++)
        {
            var stepCtx = new BrainStepContext(t);
            out1 = brain1.Step(inputs, in stepCtx).ToArray();
            out2 = brain2.Step(inputs, in stepCtx).ToArray();
        }

        bool anyDifferent = false;
        for (int i = 0; i < out1.Length; i++)
        {
            if (MathF.Abs(out1[i] - out2[i]) > 1e-6f)
                anyDifferent = true;
        }
        Assert.True(anyDifferent, "Different micro-step counts should produce different outputs with recurrent connections");
    }

    [Fact]
    public void AggregateFitness_UsesProvidedLambdas()
    {
        var episodes = new EpisodeMetrics[]
        {
            new(100, 5f, 3, 2f, 0.1f, 50f),
            new(80, 3f, 2, 3f, 0.2f, 30f),
            new(60, 1f, 1, 4f, 0.3f, 10f),
        };

        var agg1 = DeterministicHelpers.AggregateFitness(episodes, lambdaVar: 0f, lambdaWorst: 0f);
        var agg2 = DeterministicHelpers.AggregateFitness(episodes, lambdaVar: 1f, lambdaWorst: 1f);

        Assert.NotEqual(agg1.Score, agg2.Score);
    }

    [Fact]
    public void BrainRuntime_PlasticityGain_ScalesLearning()
    {
        var nodes = new List<BrainNode>
        {
            new(0, BrainNodeType.Input, 0f, 0f, 0, new NodeMetadata()),
            new(1, BrainNodeType.Output, 1f, 0f, 1, new NodeMetadata()),
        };

        var zeroGainEdge = new BrainEdge(0, 1, 0.5f, 0.5f, PlasticityGain: 0.0f, new EdgeMetadata());
        var normalGainEdge = new BrainEdge(0, 1, 0.5f, 0.5f, PlasticityGain: 1.0f, new EdgeMetadata());

        var incomingZero = new Dictionary<int, List<BrainEdge>>
        {
            [0] = new List<BrainEdge>(),
            [1] = new List<BrainEdge> { zeroGainEdge },
        };
        var incomingNormal = new Dictionary<int, List<BrainEdge>>
        {
            [0] = new List<BrainEdge>(),
            [1] = new List<BrainEdge> { normalGainEdge },
        };

        var graphZero = new BrainGraph(nodes, incomingZero, 1, 1, 3);
        var graphNormal = new BrainGraph(nodes, incomingNormal, 1, 1, 3);

        var learn = LearningParams.Default with { Eta = 0.1f };
        var stable = StabilityParams.Default;

        var brainZero = new BrainRuntime(graphZero, learn, stable);
        var brainNormal = new BrainRuntime(graphNormal, learn, stable);

        brainZero.Reset();
        brainNormal.Reset();

        var inputs = new float[] { 1.0f };
        brainZero.Step(inputs, new BrainStepContext(0));
        brainNormal.Step(inputs, new BrainStepContext(0));

        var modulators = new float[] { 1.0f, 0f, 0f };
        brainZero.Learn(modulators, new BrainLearnContext(0));
        brainNormal.Learn(modulators, new BrainLearnContext(0));

        var exportedZero = (BrainGraph)brainZero.ExportGraph();
        var exportedNormal = (BrainGraph)brainNormal.ExportGraph();

        float wFastZero = exportedZero.IncomingByDst[1][0].WFast;
        float wFastNormal = exportedNormal.IncomingByDst[1][0].WFast;

        Assert.Equal(0.5f, wFastZero, 5);
        Assert.NotEqual(0.5f, wFastNormal, 5);
    }

    [Fact]
    public void EvolutionLoop_SpeciesCount_AtLeastOne()
    {
        var config = RunConfig.Default with
        {
            MaxGenerations = 1,
            Budgets = AllBudgets.Default with
            {
                Population = new PopulationBudget(PopulationSize: 16, ArenaRounds: 2),
                Runtime = new RuntimeBudget(MaxTicksPerEpisode: 50)
            }
        };

        var observatory = new NullObservatory();
        var loop = new EvolutionLoop(config, observatory);
        loop.Initialize();
        loop.RunGeneration();

        Assert.True(loop.SpeciesCount >= 1, $"Expected at least 1 species, got {loop.SpeciesCount}");
    }
}
