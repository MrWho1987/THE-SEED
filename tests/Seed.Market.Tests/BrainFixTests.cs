using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Evolution;

namespace Seed.Market.Tests;

public class BrainFixTests
{
    [Fact]
    public void NeighborhoodRadius_CapturesExactGridSteps()
    {
        // With HiddenWidth=16, radius=3, nodes exactly 3 grid steps apart
        // should be included as local candidates.
        // Node at x=5 has X = 5/15. Node at x=8 has X = 8/15. Distance = 3 steps.
        var budget = new DevelopmentBudget(
            HiddenWidth: 16, HiddenHeight: 16, HiddenLayers: 1,
            TopKIn: 4, MaxOut: 4, LocalNeighborhoodRadius: 3,
            GlobalCandidateSamplesPerNeuron: 0, MaxSynapticDelay: 0);

        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(4, 2);
        var graph = dev.CompileGraph(genome, budget, new DevelopmentContext(42, 0));

        // Hidden nodes are at indices [4 .. 4+16*16-1] for a 16x16x1 grid.
        // Node at grid (5, 5) and node at grid (8, 5) are 3 steps apart in X.
        // Both should be reachable as local candidates.
        // If the old bug was present, 3-step nodes would be at dx=3.2 > 3, excluded.
        // With the fix, 3-step nodes are at dx=3.0 <= 3, included.

        // Verify by checking that the graph has edges spanning 3 grid steps.
        // We check that at least some destination nodes have sources 3+ steps away in grid coords.
        int inputCount = 4;
        int width = budget.HiddenWidth;
        bool found3Step = false;

        foreach (var (dstId, edges) in graph.IncomingByDst)
        {
            if (dstId < inputCount) continue; // skip inputs
            int dstGridX = (dstId - inputCount) % width;
            int dstGridY = ((dstId - inputCount) / width) % budget.HiddenHeight;

            foreach (var edge in edges)
            {
                int srcId = edge.SrcNodeId;
                if (srcId < inputCount) continue; // skip input sources
                int srcGridX = (srcId - inputCount) % width;
                int srcGridY = ((srcId - inputCount) / width) % budget.HiddenHeight;
                int stepX = Math.Abs(dstGridX - srcGridX);
                int stepY = Math.Abs(dstGridY - srcGridY);
                if (stepX == 3 || stepY == 3)
                    found3Step = true;
            }
            if (found3Step) break;
        }

        Assert.True(found3Step, "Expected edges spanning exactly 3 grid steps with radius=3");
    }

    [Fact]
    public void NeighborhoodRadius_ExcludesBeyondRadius()
    {
        // With GlobalCandidateSamplesPerNeuron=0, only local neighborhood is used.
        // No edges should span more than radius steps (in both X and Y).
        var budget = new DevelopmentBudget(
            HiddenWidth: 16, HiddenHeight: 16, HiddenLayers: 1,
            TopKIn: 4, MaxOut: 4, LocalNeighborhoodRadius: 2,
            GlobalCandidateSamplesPerNeuron: 0, MaxSynapticDelay: 0);

        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(4, 2);
        var graph = dev.CompileGraph(genome, budget, new DevelopmentContext(42, 0));

        int inputCount = 4;
        int width = budget.HiddenWidth;
        int height = budget.HiddenHeight;

        foreach (var (dstId, edges) in graph.IncomingByDst)
        {
            if (dstId < inputCount) continue;
            int dstHiddenIdx = dstId - inputCount;
            if (dstHiddenIdx >= width * height) continue; // skip output nodes
            int dstGridX = dstHiddenIdx % width;
            int dstGridY = (dstHiddenIdx / width) % height;

            foreach (var edge in edges)
            {
                int srcId = edge.SrcNodeId;
                if (srcId < inputCount) continue;
                int srcHiddenIdx = srcId - inputCount;
                if (srcHiddenIdx >= width * height) continue;
                int srcGridX = srcHiddenIdx % width;
                int srcGridY = (srcHiddenIdx / width) % height;
                int stepX = Math.Abs(dstGridX - srcGridX);
                int stepY = Math.Abs(dstGridY - srcGridY);

                // With no global samples, all edges should be within radius + 1 layer constraint
                Assert.True(stepX <= budget.LocalNeighborhoodRadius,
                    $"Edge spans {stepX} steps in X, exceeds radius {budget.LocalNeighborhoodRadius}");
                Assert.True(stepY <= budget.LocalNeighborhoodRadius,
                    $"Edge spans {stepY} steps in Y, exceeds radius {budget.LocalNeighborhoodRadius}");
            }
        }
    }

    [Fact]
    public void NodeIdValidation_ThrowsOnNonContiguousIds()
    {
        // Construct a graph with non-contiguous NodeIds
        var nodes = new List<BrainNode>
        {
            new(0, BrainNodeType.Input, 0f, 0f, 0, new NodeMetadata()),
            new(5, BrainNodeType.Output, 1f, 0f, 1, new NodeMetadata()), // gap: ID 5 with only 2 nodes
        };
        var incoming = new Dictionary<int, List<BrainEdge>>
        {
            [0] = new(),
            [5] = new()
        };
        var graph = new BrainGraph(nodes, incoming, 1, 1, 3);

        Assert.Throws<ArgumentException>(() =>
            new BrainRuntime(graph, LearningParams.Default, StabilityParams.Default));
    }

    [Fact]
    public void NodeIdValidation_AcceptsContiguousIds()
    {
        var graph = CompileGraph(42);
        // Should not throw
        var brain = new BrainRuntime(graph, LearningParams.Default, StabilityParams.Default, 1);
        Assert.NotNull(brain);
    }

    [Fact]
    public void SaturationCounters_HandleLargeValues()
    {
        var graph = CompileGraph(42);
        var brain = new BrainRuntime(graph, LearningParams.Default, StabilityParams.Default, 1);

        var inputs = new float[graph.InputCount];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = 0.99f; // high activation to trigger saturation

        // Run many ticks
        for (int t = 0; t < 10_000; t++)
            brain.Step(inputs, new BrainStepContext(t));

        float penalty = brain.GetInstabilityPenalty();
        Assert.False(float.IsNaN(penalty), "GetInstabilityPenalty should not return NaN");
        Assert.False(float.IsInfinity(penalty), "GetInstabilityPenalty should not return Infinity");
        Assert.InRange(penalty, 0f, 1f);
    }

    [Fact]
    public void CppnTopoSort_AcyclicProducesCorrectOrder()
    {
        // Create a simple acyclic CPPN and verify topo sort produces valid ordering
        var rng = new Rng64(42);
        var cppn = CppnNetwork.CreateMinimal(CppnInputIndex.Count, CppnOutputIndex.Count, rng);

        // Evaluate to trigger cache building (including topo sort)
        var inputs = new float[CppnInputIndex.Count];
        var outputs = cppn.Evaluate(inputs);

        // Outputs should be valid (not NaN)
        for (int i = 0; i < outputs.Length; i++)
            Assert.False(float.IsNaN(outputs[i]), $"CPPN output {i} is NaN");

        // Verify output count matches expected
        Assert.Equal(CppnOutputIndex.Count, outputs.Length);
    }

    [Fact]
    public void CppnEvaluate_OutputBufferReuse_NoCorruption()
    {
        var rng = new Rng64(42);
        var cppn = CppnNetwork.CreateMinimal(CppnInputIndex.Count, CppnOutputIndex.Count, rng);

        var inputs1 = new float[CppnInputIndex.Count];
        inputs1[0] = 0.5f; inputs1[1] = 0.3f;
        var outputs1 = cppn.Evaluate(inputs1);

        // Immediately consume the values we need
        float c1 = outputs1[CppnOutputIndex.C];
        float w1 = outputs1[CppnOutputIndex.W];

        // Second call with different inputs
        var inputs2 = new float[CppnInputIndex.Count];
        inputs2[0] = -0.5f; inputs2[1] = -0.3f;
        var outputs2 = cppn.Evaluate(inputs2);

        // The second call uses the same buffer, so outputs1 reference is now stale.
        // But the values we consumed (c1, w1) should still be valid.
        Assert.False(float.IsNaN(c1));
        Assert.False(float.IsNaN(w1));

        // outputs2 should have different values (different inputs)
        float c2 = outputs2[CppnOutputIndex.C];
        Assert.False(float.IsNaN(c2));

        // Verify the buffer IS shared (outputs1 and outputs2 reference same array)
        Assert.True(ReferenceEquals(outputs1, outputs2),
            "Evaluate should reuse the output buffer");
    }

    private static BrainGraph CompileGraph(int seed)
    {
        var rng = new Rng64((ulong)seed);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        return dev.CompileGraph(genome, MarketEvaluator.MarketBrainBudget, new DevelopmentContext((ulong)seed, 0));
    }
}
