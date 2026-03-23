using Seed.Core;
using Seed.Genetics;
using Seed.Brain;

namespace Seed.Development;

/// <summary>
/// Compiles a SeedGenome (CPPN) into a sparse recurrent BrainGraph.
/// </summary>
public sealed class BrainDeveloper
{
    private readonly int _sensorCount;
    private readonly int _actuatorCount;

    public BrainDeveloper(int sensorCount, int actuatorCount)
    {
        _sensorCount = sensorCount;
        _actuatorCount = actuatorCount;
    }

    public BrainGraph CompileGraph(SeedGenome genome, in DevelopmentBudget budget, in DevelopmentContext ctx)
    {
        var nodes = new List<BrainNode>();
        var incoming = new Dictionary<int, List<BrainEdge>>();

        int nodeId = 0;

        // Input nodes
        for (int i = 0; i < _sensorCount; i++)
        {
            nodes.Add(new BrainNode(
                NodeId: nodeId++,
                Type: BrainNodeType.Input,
                X: 0f,
                Y: (float)i / Math.Max(1, _sensorCount - 1),
                Layer: 0,
                Meta: new NodeMetadata()
            ));
        }

        // Hidden nodes on a grid
        int hiddenStart = nodeId;
        for (int layer = 0; layer < budget.HiddenLayers; layer++)
        {
            for (int y = 0; y < budget.HiddenHeight; y++)
            {
                for (int x = 0; x < budget.HiddenWidth; x++)
                {
                    nodes.Add(new BrainNode(
                        NodeId: nodeId++,
                        Type: BrainNodeType.Hidden,
                        X: (float)x / Math.Max(1, budget.HiddenWidth - 1),
                        Y: (float)y / Math.Max(1, budget.HiddenHeight - 1),
                        Layer: layer + 1,
                        Meta: new NodeMetadata()
                    ));
                }
            }
        }
        int hiddenEnd = nodeId;

        // Output nodes
        int outputStart = nodeId;
        for (int i = 0; i < _actuatorCount; i++)
        {
            nodes.Add(new BrainNode(
                NodeId: nodeId++,
                Type: BrainNodeType.Output,
                X: 1f,
                Y: (float)i / Math.Max(1, _actuatorCount - 1),
                Layer: budget.HiddenLayers + 1,
                Meta: new NodeMetadata()
            ));
        }

        // Initialize incoming edge lists
        foreach (var node in nodes)
        {
            incoming[node.NodeId] = new List<BrainEdge>();
        }

        // Track outgoing counts for MaxOut constraint
        var outgoingCount = new Dictionary<int, int>();
        foreach (var node in nodes)
        {
            outgoingCount[node.NodeId] = 0;
        }

        // For each destination node (non-input), query CPPN for candidate edges
        var nonInputNodes = nodes.Where(n => n.Type != BrainNodeType.Input).ToList();

        // Reusable buffer for CPPN inputs
        var cppnInput = new float[CppnInputIndex.Count];
        
        // Accumulate tau values per destination node
        var tauSums = new Dictionary<int, (float sum, int count)>();

        const float ModulatoryGateThreshold = 0.7f;
        const float DelayActivationThreshold = 0.3f;

        foreach (var dstNode in nonInputNodes)
        {
            var candidates = new List<(int srcId, float score, float weight, float tau, float gate, float delay)>();

            var candidateSources = GetCandidateSources(
                dstNode, nodes, budget, ctx.RunSeed);

            foreach (var srcNode in candidateSources)
            {
                if (srcNode.NodeId == dstNode.NodeId)
                    continue;

                float dx = dstNode.X - srcNode.X;
                float dy = dstNode.Y - srcNode.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);

                cppnInput[CppnInputIndex.Xi] = srcNode.X;
                cppnInput[CppnInputIndex.Yi] = srcNode.Y;
                cppnInput[CppnInputIndex.Li] = (float)srcNode.Layer / (budget.HiddenLayers + 1);
                cppnInput[CppnInputIndex.Xj] = dstNode.X;
                cppnInput[CppnInputIndex.Yj] = dstNode.Y;
                cppnInput[CppnInputIndex.Lj] = (float)dstNode.Layer / (budget.HiddenLayers + 1);
                cppnInput[CppnInputIndex.Dx] = dx;
                cppnInput[CppnInputIndex.Dy] = dy;
                cppnInput[CppnInputIndex.Dist] = dist;

                var outputs = genome.Cppn.Evaluate(cppnInput);
                float connScore = outputs[CppnOutputIndex.C];
                float weight = outputs[CppnOutputIndex.W];
                float tauRaw = outputs.Length > CppnOutputIndex.Tau ? outputs[CppnOutputIndex.Tau] : 0f;
                float gateRaw = outputs.Length > CppnOutputIndex.Gate ? outputs[CppnOutputIndex.Gate] : 0f;
                float delayRaw = outputs.Length > CppnOutputIndex.Delay ? outputs[CppnOutputIndex.Delay] : 0f;

                if (connScore >= genome.Dev.ConnectionThreshold)
                {
                    candidates.Add((srcNode.NodeId, connScore, weight, tauRaw, gateRaw, delayRaw));
                }
            }

            candidates = candidates
                .OrderByDescending(c => c.score)
                .ThenBy(c => c.srcId)
                .ToList();

            int taken = 0;
            float tauAccum = 0f;
            int tauCount = 0;
            
            foreach (var (srcId, score, weight, tau, gate, delay) in candidates)
            {
                if (taken >= budget.TopKIn)
                    break;

                if (outgoingCount[srcId] >= budget.MaxOut)
                    continue;

                float w0 = DeterministicHelpers.Clamp(
                    weight * genome.Dev.InitialWeightScale,
                    -genome.Stable.WeightMaxAbs,
                    genome.Stable.WeightMaxAbs
                );

                EdgeType edgeType = gate > ModulatoryGateThreshold ? EdgeType.Modulatory : EdgeType.Normal;
                float plasticityGain = 2f / (1f + MathF.Exp(-gate));

                int delayTicks = 0;
                if (delay > DelayActivationThreshold && budget.MaxSynapticDelay > 0)
                {
                    float fraction = (delay - DelayActivationThreshold) / (1f - DelayActivationThreshold);
                    delayTicks = Math.Clamp((int)MathF.Round(fraction * budget.MaxSynapticDelay), 1, budget.MaxSynapticDelay);
                }

                incoming[dstNode.NodeId].Add(new BrainEdge(
                    SrcNodeId: srcId,
                    DstNodeId: dstNode.NodeId,
                    WSlow: w0,
                    WFast: w0,
                    PlasticityGain: plasticityGain,
                    Meta: new EdgeMetadata(edgeType, delayTicks)
                ));

                outgoingCount[srcId]++;
                taken++;
                tauAccum += tau;
                tauCount++;
            }
            
            if (tauCount > 0)
                tauSums[dstNode.NodeId] = (tauAccum, tauCount);

            incoming[dstNode.NodeId] = incoming[dstNode.NodeId]
                .OrderBy(e => e.SrcNodeId)
                .ToList();
        }

        // Assign tau-derived TimeConstant to each non-input node
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.Type == BrainNodeType.Input) continue;
            
            float tc = 1.0f; // default: instant (same as current behavior)
            if (tauSums.TryGetValue(n.NodeId, out var ts) && ts.count > 0)
            {
                float tauAvg = ts.sum / ts.count;
                float sig = 1f / (1f + MathF.Exp(-tauAvg));
                tc = 1.0f + sig * 9.0f; // range [1, 10]
            }
            nodes[i] = n with { Meta = n.Meta with { TimeConstant = tc } };
        }

        return new BrainGraph(
            nodes,
            incoming,
            _sensorCount,
            _actuatorCount,
            ModulatorIndex.Count
        );
    }

    private List<BrainNode> GetCandidateSources(
        BrainNode dst,
        List<BrainNode> allNodes,
        in DevelopmentBudget budget,
        ulong runSeed)
    {
        var candidates = new HashSet<int>();

        // Local neighborhood
        foreach (var node in allNodes)
        {
            if (node.Type == BrainNodeType.Output)
                continue; // Don't use outputs as sources

            float dx = MathF.Abs(dst.X - node.X) * budget.HiddenWidth;
            float dy = MathF.Abs(dst.Y - node.Y) * budget.HiddenHeight;
            float layerDiff = MathF.Abs(dst.Layer - node.Layer);

            // Local if within radius and at most 1 layer away
            if (dx <= budget.LocalNeighborhoodRadius &&
                dy <= budget.LocalNeighborhoodRadius &&
                layerDiff <= 1)
            {
                candidates.Add(node.NodeId);
            }
        }

        // Global random sample
        var rng = new Rng64(SeedDerivation.DevelopmentSeed(runSeed, dst.NodeId));
        var eligibleNodes = allNodes
            .Where(n => n.Type != BrainNodeType.Output && !candidates.Contains(n.NodeId))
            .ToArray();

        if (eligibleNodes.Length > 0)
        {
            var sampled = eligibleNodes.DeterministicSample(
                budget.GlobalCandidateSamplesPerNeuron, ref rng);
            foreach (var node in sampled)
            {
                candidates.Add(node.NodeId);
            }
        }

        return allNodes.Where(n => candidates.Contains(n.NodeId)).ToList();
    }
}

