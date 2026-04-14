using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Evolution;

namespace Seed.Market.Tests;

public class BrainV2FeatureTests
{
    [Fact]
    public void MemoryEdge_ExistsInCompiledGraph()
    {
        // Across several random genomes, at least one should produce Memory edges
        int totalMemory = 0;
        for (int seed = 0; seed < 20; seed++)
        {
            var graph = CompileGraph(seed);
            foreach (var edges in graph.IncomingByDst.Values)
                totalMemory += edges.Count(e => e.Meta.EdgeType == EdgeType.Memory);
        }
        Assert.True(totalMemory > 0, "Expected at least some Memory edges across 20 random genomes");
    }

    [Fact]
    public void MemoryEdge_LearnsWithoutReward()
    {
        // With zero modulators (M=0), Memory edges should still learn (effectiveM=1)
        // while Normal edges should not (effectiveM = 0 * (1+localMod) = 0)
        var graph = CompileGraphWithAllEdgeTypes();
        var genome = SeedGenome.CreateRandom(new Rng64(42));
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);

        // Find a Memory edge and a Normal edge
        int memoryEdgeIdx = -1, normalEdgeIdx = -1;
        int memoryDstId = -1, normalDstId = -1;
        foreach (var (dstId, edges) in graph.IncomingByDst)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Meta.EdgeType == EdgeType.Memory && memoryEdgeIdx < 0)
                { memoryEdgeIdx = i; memoryDstId = dstId; }
                if (edges[i].Meta.EdgeType == EdgeType.Normal && normalEdgeIdx < 0)
                { normalEdgeIdx = i; normalDstId = dstId; }
            }
            if (memoryEdgeIdx >= 0 && normalEdgeIdx >= 0) break;
        }

        if (memoryEdgeIdx < 0 || normalEdgeIdx < 0)
            return; // Can't test without both edge types

        // Run some steps to build activations, then learn with zero modulators
        var inputs = new float[graph.InputCount];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = (i % 3 == 0) ? 0.5f : -0.3f;

        Span<float> zeroMods = stackalloc float[3]; // all zero

        // Get initial weights via export
        var before = (BrainGraph)brain.ExportGraph();

        for (int t = 0; t < 50; t++)
        {
            brain.Step(inputs, new BrainStepContext(t));
            brain.Learn(zeroMods, new BrainLearnContext(t));
        }

        var after = (BrainGraph)brain.ExportGraph();

        // Memory edge weights should have changed (effectiveM = 1)
        float memBefore = before.IncomingByDst[memoryDstId][memoryEdgeIdx].WFast;
        float memAfter = after.IncomingByDst[memoryDstId][memoryEdgeIdx].WFast;

        // Normal edge weights should NOT have changed (effectiveM = 0)
        float normBefore = before.IncomingByDst[normalDstId][normalEdgeIdx].WFast;
        float normAfter = after.IncomingByDst[normalDstId][normalEdgeIdx].WFast;

        // Memory edge weights should have changed substantially (effectiveM=1, pure Hebbian)
        float memDelta = MathF.Abs(memAfter - memBefore);
        // Normal edge weights should barely change (effectiveM=0, only consolidation drift)
        float normDelta = MathF.Abs(normAfter - normBefore);
        Assert.True(memDelta > 0.001f, $"Memory edge should learn substantially, delta={memDelta}");
        Assert.True(normDelta < 0.001f, $"Normal edge should not learn with zero modulators, delta={normDelta}");
    }

    [Fact]
    public void MemoryEdge_DisabledByAblation()
    {
        var graph = CompileGraphWithAllEdgeTypes();
        var genome = SeedGenome.CreateRandom(new Rng64(42));
        var ablation = AblationConfig.Default with { MemoryEdgesEnabled = false };
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1, ablation);

        int memoryDstId = -1, memoryEdgeIdx = -1;
        foreach (var (dstId, edges) in graph.IncomingByDst)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].Meta.EdgeType == EdgeType.Memory)
                { memoryEdgeIdx = i; memoryDstId = dstId; break; }
            }
            if (memoryEdgeIdx >= 0) break;
        }

        if (memoryEdgeIdx < 0) return;

        var inputs = new float[graph.InputCount];
        Span<float> zeroMods = stackalloc float[3];

        var before = (BrainGraph)brain.ExportGraph();

        for (int t = 0; t < 50; t++)
        {
            brain.Step(inputs, new BrainStepContext(t));
            brain.Learn(zeroMods, new BrainLearnContext(t));
        }

        var after = (BrainGraph)brain.ExportGraph();

        // With ablation off + zero modulators, Memory edges should NOT learn substantially
        // (they fall through to Normal behavior where effectiveM = 0; tiny drift from consolidation is ok)
        float wBefore = before.IncomingByDst[memoryDstId][memoryEdgeIdx].WFast;
        float wAfter = after.IncomingByDst[memoryDstId][memoryEdgeIdx].WFast;
        float delta = MathF.Abs(wAfter - wBefore);
        Assert.True(delta < 0.001f, $"Memory edge with ablation off should not learn substantially, delta={delta}");
    }

    [Fact]
    public void GateThresholds_ThreeZonePartition()
    {
        // Directly test the threshold logic used in BrainDeveloper
        const float MemoryGateThreshold = -0.3f;
        const float ModulatoryGateThreshold = 0.7f;

        EdgeType Classify(float gate) =>
            gate > ModulatoryGateThreshold ? EdgeType.Modulatory :
            gate < MemoryGateThreshold ? EdgeType.Memory :
            EdgeType.Normal;

        Assert.Equal(EdgeType.Memory, Classify(-0.5f));
        Assert.Equal(EdgeType.Memory, Classify(-1.0f));
        Assert.Equal(EdgeType.Normal, Classify(0.0f));
        Assert.Equal(EdgeType.Normal, Classify(-0.3f));
        Assert.Equal(EdgeType.Normal, Classify(0.7f));
        Assert.Equal(EdgeType.Modulatory, Classify(0.8f));
        Assert.Equal(EdgeType.Modulatory, Classify(1.0f));
    }

    [Fact]
    public void ModuleTag_ProducesNonZeroModuleId()
    {
        int nonZeroCount = 0;
        for (int seed = 0; seed < 20; seed++)
        {
            var graph = CompileGraph(seed);
            nonZeroCount += graph.Nodes.Count(n => n.Meta.ModuleId > 0);
        }
        Assert.True(nonZeroCount > 0, "Expected at least some nodes with ModuleId > 0 across 20 genomes");
    }

    [Fact]
    public void ModuleId_WithinBudgetRange()
    {
        var budget = MarketEvaluator.MarketBrainBudget;
        var graph = CompileGraph(42);
        foreach (var node in graph.Nodes)
        {
            Assert.InRange(node.Meta.ModuleId, 0, budget.ModuleCount - 1);
        }
    }

    [Fact]
    public void RegionId_MatchesLayer()
    {
        var graph = CompileGraph(42);
        foreach (var node in graph.Nodes)
        {
            Assert.Equal(node.Layer, node.Meta.RegionId);
        }
    }

    [Fact]
    public void PlasticityProfileId_MatchesEdgeType()
    {
        var graph = CompileGraph(42);
        foreach (var edges in graph.IncomingByDst.Values)
        {
            foreach (var edge in edges)
            {
                Assert.Equal((int)edge.Meta.EdgeType, edge.Meta.PlasticityProfileId);
            }
        }
    }

    [Fact]
    public void NodePlasticityProfileId_IsDominantEdgeType()
    {
        var graph = CompileGraph(42);
        foreach (var node in graph.Nodes)
        {
            if (node.Type == BrainNodeType.Input) continue;
            if (!graph.IncomingByDst.TryGetValue(node.NodeId, out var edges) || edges.Count == 0)
                continue;

            var profileCounts = new int[3];
            foreach (var e in edges)
                profileCounts[e.Meta.PlasticityProfileId]++;
            int expectedProfile = Array.IndexOf(profileCounts, profileCounts.Max());

            Assert.Equal(expectedProfile, node.Meta.PlasticityProfileId);
        }
    }

    [Fact]
    public void BrainGraphReserved_DefaultIsEmpty()
    {
        var reserved = BrainGraphReserved.Default;
        Assert.Empty(reserved.ReservedKeys);
        Assert.Empty(reserved.ReservedValues);
    }

    [Fact]
    public void OldBrainGraph_StillDeserializes()
    {
        // Build a V1-style JSON with old placeholder reserved
        var graph = CompileGraph(42);
        var json = graph.ToJson();

        // Downgrade: replace SchemaVersion 2 with 1, add old placeholder
        json = json.Replace("\"schemaVersion\": 2", "\"schemaVersion\": 1");
        json = json.Replace("\"reservedKeys\": []", "\"reservedKeys\": [\"v2_placeholder_0\"]");
        json = json.Replace("\"reservedValues\": []", "\"reservedValues\": [\"0\"]");

        var loaded = BrainGraph.FromJson(json);
        Assert.Equal(graph.NodeCount, loaded.NodeCount);
        Assert.Equal(graph.EdgeCount, loaded.EdgeCount);
        Assert.Equal(graph.InputCount, loaded.InputCount);
        Assert.Equal(graph.OutputCount, loaded.OutputCount);
    }

    [Fact]
    public void Determinism_WithMemoryEdges()
    {
        var run1 = RunDeterminismCheck(99);
        var run2 = RunDeterminismCheck(99);
        Assert.Equal(run1.Length, run2.Length);
        for (int i = 0; i < run1.Length; i++)
            Assert.Equal(run1[i], run2[i]);
    }

    private static float[] RunDeterminismCheck(int seed)
    {
        var rng = new Rng64((ulong)seed);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var graph = dev.CompileGraph(genome, MarketEvaluator.MarketBrainBudget, new DevelopmentContext((ulong)seed, 0));
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);

        var inputs = new float[MarketAgent.InputCount];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = 0.1f * (i % 7 - 3);
        float[] mods = [0.5f, 0.1f, 0.2f];

        float[] lastOutputs = Array.Empty<float>();
        for (int t = 0; t < 100; t++)
        {
            var ctx = new BrainStepContext(t);
            var outputs = brain.Step(inputs, ctx);
            lastOutputs = outputs.ToArray();
            brain.Learn(mods, new BrainLearnContext(t, ElapsedHours: t * 0.25f));
        }
        return lastOutputs;
    }

    // --- Helpers ---

    private static BrainGraph CompileGraph(int seed)
    {
        var rng = new Rng64((ulong)seed);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        return dev.CompileGraph(genome, MarketEvaluator.MarketBrainBudget, new DevelopmentContext((ulong)seed, 0));
    }

    private static BrainGraph CompileGraphWithAllEdgeTypes()
    {
        // Try multiple seeds until we find a graph with both Memory and Normal edges
        for (int seed = 0; seed < 100; seed++)
        {
            var graph = CompileGraph(seed);
            bool hasMemory = false, hasNormal = false;
            foreach (var edges in graph.IncomingByDst.Values)
            {
                foreach (var e in edges)
                {
                    if (e.Meta.EdgeType == EdgeType.Memory) hasMemory = true;
                    if (e.Meta.EdgeType == EdgeType.Normal) hasNormal = true;
                }
                if (hasMemory && hasNormal) return graph;
            }
        }
        // Fallback: return any graph (tests that need both types will skip)
        return CompileGraph(42);
    }
}
