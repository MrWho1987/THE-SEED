using Seed.Core;
using Seed.Genetics;
using Seed.Development;
using Seed.Brain;
using Seed.Agents;
using Seed.Evolution;

namespace Seed.Tests;

public class BrainAliveTests
{
    [Fact]
    public void ModulatoryEdge_AffectsLearningLocally()
    {
        // Two brains: one with a modulatory edge, one with the same edge as normal.
        // After learning, the normal edge's weight delta should differ.
        var nodes = new List<BrainNode>
        {
            new(0, BrainNodeType.Input, 0f, 0f, 0, new NodeMetadata()),
            new(1, BrainNodeType.Input, 0.2f, 0f, 0, new NodeMetadata()),
            new(2, BrainNodeType.Hidden, 0.5f, 0.5f, 1, new NodeMetadata()),
            new(3, BrainNodeType.Output, 1f, 0.5f, 2, new NodeMetadata()),
        };

        // Brain A: node 1 -> node 2 is Modulatory, node 0 -> node 2 is Normal
        var edgesA = new Dictionary<int, List<BrainEdge>>
        {
            [0] = new(),
            [1] = new(),
            [2] = new()
            {
                new BrainEdge(0, 2, 0.5f, 0.5f, 1f, new EdgeMetadata(EdgeType.Normal, 0)),
                new BrainEdge(1, 2, 0.5f, 0.5f, 1f, new EdgeMetadata(EdgeType.Modulatory, 0)),
            },
            [3] = new()
            {
                new BrainEdge(2, 3, 0.5f, 0.5f, 1f, new EdgeMetadata(EdgeType.Normal, 0)),
            },
        };

        // Brain B: same but node 1 -> node 2 is Normal (no modulatory)
        var edgesB = new Dictionary<int, List<BrainEdge>>
        {
            [0] = new(),
            [1] = new(),
            [2] = new()
            {
                new BrainEdge(0, 2, 0.5f, 0.5f, 1f, new EdgeMetadata(EdgeType.Normal, 0)),
                new BrainEdge(1, 2, 0.5f, 0.5f, 1f, new EdgeMetadata(EdgeType.Normal, 0)),
            },
            [3] = new()
            {
                new BrainEdge(2, 3, 0.5f, 0.5f, 1f, new EdgeMetadata(EdgeType.Normal, 0)),
            },
        };

        var graphA = new BrainGraph(nodes, edgesA, 2, 1, 3);
        var graphB = new BrainGraph(nodes, edgesB, 2, 1, 3);

        var learn = LearningParams.Default;
        var stable = StabilityParams.Default;

        var brainA = new BrainRuntime(graphA, learn, stable);
        var brainB = new BrainRuntime(graphB, learn, stable);
        brainA.Reset();
        brainB.Reset();

        float[] inputs = [1.0f, 0.8f]; // strong modulatory source activation
        float[] modulators = [1.0f, 0f, 0f]; // positive reward

        for (int t = 0; t < 10; t++)
        {
            var ctx = new BrainStepContext(t);
            brainA.Step(inputs, in ctx);
            brainB.Step(inputs, in ctx);
            brainA.Learn(modulators, new BrainLearnContext(t));
            brainB.Learn(modulators, new BrainLearnContext(t));
        }

        // Export and compare: the normal edge (0->2) should have different weights
        var exportA = (BrainGraph)brainA.ExportGraph();
        var exportB = (BrainGraph)brainB.ExportGraph();

        float wA = exportA.IncomingByDst[2][0].WFast;
        float wB = exportB.IncomingByDst[2][0].WFast;

        Assert.NotEqual(wA, wB, 4);
    }

    [Fact]
    public void SynapticDelay_ShiftsTemporalResponse()
    {
        // Single edge from input 0 to hidden node, delay=2.
        // Pulse input at tick 0, then zeros. Hidden activation should be zero at ticks 0-1.
        var nodes = new List<BrainNode>
        {
            new(0, BrainNodeType.Input, 0f, 0f, 0, new NodeMetadata()),
            new(1, BrainNodeType.Hidden, 0.5f, 0.5f, 1, new NodeMetadata()),
            new(2, BrainNodeType.Output, 1f, 0.5f, 2, new NodeMetadata()),
        };

        var edges = new Dictionary<int, List<BrainEdge>>
        {
            [0] = new(),
            [1] = new()
            {
                new BrainEdge(0, 1, 1.0f, 1.0f, 1f, new EdgeMetadata(EdgeType.Normal, 2)),
            },
            [2] = new()
            {
                new BrainEdge(1, 2, 1.0f, 1.0f, 1f, new EdgeMetadata(EdgeType.Normal, 0)),
            },
        };

        var graph = new BrainGraph(nodes, edges, 1, 1, 3);
        var brain = new BrainRuntime(graph, LearningParams.Default, StabilityParams.Default, microStepsPerTick: 1);
        brain.Reset();

        float[] pulse = [1.0f];
        float[] zeros = [0.0f];

        // Tick 0: pulse input
        var stepCtx0 = new BrainStepContext(0);
        brain.Step(pulse, in stepCtx0);
        float hiddenTick0 = brain.GetActivations()[1];

        // Tick 1: zero input
        var stepCtx1 = new BrainStepContext(1);
        brain.Step(zeros, in stepCtx1);
        float hiddenTick1 = brain.GetActivations()[1];

        // Tick 2: zero input but delayed pulse should arrive
        var stepCtx2 = new BrainStepContext(2);
        brain.Step(zeros, in stepCtx2);
        float hiddenTick2 = brain.GetActivations()[1];

        Assert.Equal(0f, hiddenTick0, 5);
        Assert.Equal(0f, hiddenTick1, 5);
        Assert.True(MathF.Abs(hiddenTick2) > 0.01f,
            $"Hidden node should be nonzero at tick 2 due to delayed pulse, was {hiddenTick2}");
    }

    [Fact]
    public void PlasticityGain_VariesAcrossEdges()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var developer = new BrainDeveloper(AgentConfig.Default.TotalSensorCount, 5);
        var devCtx = new DevelopmentContext(42, 0);
        var budget = DevelopmentBudget.Default;

        var graph = developer.CompileGraph(genome, budget, devCtx);

        var allEdges = graph.IncomingByDst.Values.SelectMany(e => e).ToList();
        Assert.True(allEdges.Count > 0, "Graph should have edges");

        float minPG = allEdges.Min(e => e.PlasticityGain);
        float maxPG = allEdges.Max(e => e.PlasticityGain);

        Assert.True(maxPG - minPG > 0.01f,
            $"PlasticityGain should vary across edges: min={minPG}, max={maxPG}");
    }

    [Fact]
    public void DelayThreshold_MostEdgesHaveNoDelay()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var developer = new BrainDeveloper(AgentConfig.Default.TotalSensorCount, 5);
        var devCtx = new DevelopmentContext(42, 0);
        var budget = DevelopmentBudget.Default;

        var graph = developer.CompileGraph(genome, budget, devCtx);

        var allEdges = graph.IncomingByDst.Values.SelectMany(e => e).ToList();
        Assert.True(allEdges.Count > 0, "Graph should have edges");

        int zeroDelay = allEdges.Count(e => e.Meta.Delay == 0);
        int withDelay = allEdges.Count(e => e.Meta.Delay > 0);
        float zeroRatio = (float)zeroDelay / allEdges.Count;

        Assert.True(zeroDelay > 0,
            "Threshold gating should leave some edges with delay=0");
        Assert.True(withDelay > 0,
            "Some edges should have delay > 0 for temporal processing");
        Assert.True(allEdges.All(e => e.Meta.Delay <= DevelopmentBudget.Default.MaxSynapticDelay),
            "No edge should exceed MaxSynapticDelay");
    }

    [Fact]
    public void Determinism_WithAllNewFeatures()
    {
        float[] RunBrain(ulong seed)
        {
            var rng = new Rng64(seed);
            var genome = SeedGenome.CreateRandom(rng);
            var developer = new BrainDeveloper(AgentConfig.Default.TotalSensorCount, 5);
            var devCtx = new DevelopmentContext(seed, 0);
            var budget = DevelopmentBudget.Default;
            var graph = developer.CompileGraph(genome, budget, devCtx);

            var brain = new BrainRuntime(graph, LearningParams.Default, StabilityParams.Default);
            brain.Reset();

            var inputs = new float[AgentConfig.Default.TotalSensorCount];
            for (int i = 0; i < inputs.Length; i++)
                inputs[i] = (float)(i + 1) / inputs.Length;

            float[] modulators = [0.5f, -0.1f, 0.3f];

            for (int t = 0; t < 50; t++)
            {
                var stepCtx = new BrainStepContext(t);
                brain.Step(inputs, in stepCtx);
                brain.Learn(modulators, new BrainLearnContext(t));
            }

            return brain.GetActivations().ToArray();
        }

        var acts1 = RunBrain(123);
        var acts2 = RunBrain(123);

        Assert.Equal(acts1.Length, acts2.Length);
        for (int i = 0; i < acts1.Length; i++)
            Assert.Equal(acts1[i], acts2[i], 6);
    }

    // ===== Phase 2: Crossover Tests =====

    [Fact]
    public void Crossover_ProducesValidGenome()
    {
        var rng1 = new Rng64(100);
        var rng2 = new Rng64(200);
        var parent1 = SeedGenome.CreateRandom(rng1);
        var parent2 = SeedGenome.CreateRandom(rng2);

        // Mutate a few times so they diverge
        var tracker = InnovationTracker.CreateDefault();
        for (int i = 0; i < 5; i++)
        {
            var mutRng = new Rng64((ulong)(300 + i));
            var mutCtx = new MutationContext(42, 0, MutationConfig.Default, tracker, mutRng);
            parent1 = (SeedGenome)parent1.Mutate(mutCtx);
            mutRng = new Rng64((ulong)(400 + i));
            mutCtx = new MutationContext(42, 0, MutationConfig.Default, tracker, mutRng);
            parent2 = (SeedGenome)parent2.Mutate(mutCtx);
        }

        var crossRng = new Rng64(500);
        var child = SeedGenome.Crossover(parent1, parent2, ref crossRng);

        Assert.NotNull(child);
        Assert.NotEqual(parent1.GenomeId, child.GenomeId);

        var developer = new BrainDeveloper(AgentConfig.Default.TotalSensorCount, 5);
        var devCtx = new DevelopmentContext(42, 0);
        var graph = developer.CompileGraph(child, DevelopmentBudget.Default, devCtx);

        Assert.NotNull(graph);
        Assert.True(graph.NodeCount > 0);
    }

    [Fact]
    public void Crossover_AlignsByInnovationId()
    {
        var rng1 = new Rng64(10);
        var rng2 = new Rng64(20);
        var parent1 = SeedGenome.CreateRandom(rng1);
        var parent2 = SeedGenome.CreateRandom(rng2);

        // Add extra connections to parent1 only via mutation
        var tracker = InnovationTracker.CreateDefault();
        for (int i = 0; i < 10; i++)
        {
            var mutRng = new Rng64((ulong)(30 + i));
            var mutCtx = new MutationContext(42, 0,
                MutationConfig.Default with { PAddConn = 1.0f, PAddNode = 0.5f },
                tracker, mutRng);
            parent1 = (SeedGenome)parent1.Mutate(mutCtx);
        }

        int p1ConnCount = parent1.Cppn.Connections.Count;
        int p2ConnCount = parent2.Cppn.Connections.Count;
        Assert.True(p1ConnCount > p2ConnCount, "Parent1 should have more connections after mutation");

        // parent1 is "fitter"
        var crossRng = new Rng64(50);
        var child = SeedGenome.Crossover(parent1, parent2, ref crossRng);

        // Child should have all of parent1's disjoint/excess genes
        var childInnovs = new HashSet<int>(child.Cppn.Connections.Select(c => c.InnovationId));
        var p1Innovs = new HashSet<int>(parent1.Cppn.Connections.Select(c => c.InnovationId));
        var p2Innovs = new HashSet<int>(parent2.Cppn.Connections.Select(c => c.InnovationId));

        // All disjoint/excess from fitter (parent1) should be in child
        var p1Only = p1Innovs.Except(p2Innovs);
        foreach (var innov in p1Only)
            Assert.Contains(innov, childInnovs);
    }

    [Fact]
    public void Crossover_FitterParentDominates()
    {
        var rng1 = new Rng64(60);
        var rng2 = new Rng64(70);
        var parent1 = SeedGenome.CreateRandom(rng1);
        var parent2 = SeedGenome.CreateRandom(rng2);

        // Add unique genes to parent2 only
        var tracker = InnovationTracker.CreateDefault();
        for (int i = 0; i < 10; i++)
        {
            var mutRng = new Rng64((ulong)(80 + i));
            var mutCtx = new MutationContext(42, 0,
                MutationConfig.Default with { PAddConn = 1.0f, PAddNode = 0.5f },
                tracker, mutRng);
            parent2 = (SeedGenome)parent2.Mutate(mutCtx);
        }

        // parent1 is fitter but has fewer genes
        var crossRng = new Rng64(90);
        var child = SeedGenome.Crossover(parent1, parent2, ref crossRng);

        var childInnovs = new HashSet<int>(child.Cppn.Connections.Select(c => c.InnovationId));
        var p1Innovs = new HashSet<int>(parent1.Cppn.Connections.Select(c => c.InnovationId));
        var p2Innovs = new HashSet<int>(parent2.Cppn.Connections.Select(c => c.InnovationId));

        // Disjoint/excess from weaker (parent2) should NOT be in child
        var p2Only = p2Innovs.Except(p1Innovs);
        foreach (var innov in p2Only)
            Assert.DoesNotContain(innov, childInnovs);
    }

    [Fact]
    public void Crossover_DisabledGeneInheritance()
    {
        // Both parents share a matching gene. Disable it in one parent.
        // Run many crossovers: ~75% of children should have it disabled.
        var rng1 = new Rng64(110);
        var parent1 = SeedGenome.CreateRandom(rng1);
        var parent2 = (SeedGenome)parent1.CloneGenome(Guid.NewGuid());

        // Disable the first connection in parent2
        var p2Cppn = parent2.Cppn.DeepCopy();
        var conn0 = p2Cppn.Connections[0];
        p2Cppn.Connections[0] = conn0 with { Enabled = false };
        parent2 = parent2 with { Cppn = p2Cppn };

        int targetInnov = conn0.InnovationId;
        int disabledCount = 0;
        int trials = 200;

        for (int i = 0; i < trials; i++)
        {
            var crossRng = new Rng64((ulong)(1000 + i));
            var child = SeedGenome.Crossover(parent1, parent2, ref crossRng);
            var childConn = child.Cppn.Connections.FirstOrDefault(c => c.InnovationId == targetInnov);
            if (childConn != null && !childConn.Enabled)
                disabledCount++;
        }

        float disabledRate = (float)disabledCount / trials;
        Assert.True(disabledRate > 0.60f && disabledRate < 0.90f,
            $"Disabled rate should be ~75%, got {disabledRate * 100:F1}%");
    }
}
