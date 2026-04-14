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

    public BrainGraph CompileGraph(SeedGenome genome, in DevelopmentBudget budget, in DevelopmentContext ctx,
        int[]? signalCategoryMap = null, int regimeSignalStart = -1, int regimeSignalEnd = -1)
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

        // Gate neurons (regime-gating layer, layer 1 when enabled)
        bool hasGates = budget.GateNeuronCount > 0;
        int gateLayerOffset = hasGates ? 1 : 0;
        int totalLayers = budget.HiddenLayers + 1 + gateLayerOffset; // for CPPN layer normalization

        int gateStart = nodeId;
        if (hasGates)
        {
            for (int g = 0; g < budget.GateNeuronCount; g++)
            {
                nodes.Add(new BrainNode(
                    NodeId: nodeId++,
                    Type: BrainNodeType.Gate,
                    X: 0.5f,
                    Y: (float)g / Math.Max(1, budget.GateNeuronCount - 1),
                    Layer: 1,
                    Meta: new NodeMetadata()
                ));
            }
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
                        Layer: layer + 1 + gateLayerOffset,
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
                Layer: budget.HiddenLayers + 1 + gateLayerOffset,
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

        // For each destination node (non-input, non-gate), query CPPN for candidate edges
        var nonInputNodes = nodes.Where(n => n.Type != BrainNodeType.Input && n.Type != BrainNodeType.Gate).ToList();

        // Reusable buffer for CPPN inputs
        var cppnInput = new float[CppnInputIndex.Count];
        
        // Accumulate tau and moduleTag values per destination node
        var tauSums = new Dictionary<int, (float sum, int count)>();
        var moduleTagSums = new Dictionary<int, (float sum, int count)>();

        const float MemoryGateThreshold = -0.3f;
        const float ModulatoryGateThreshold = 0.7f;
        const float DelayActivationThreshold = 0.3f;

        foreach (var dstNode in nonInputNodes)
        {
            var candidates = new List<(int srcId, float score, float weight, float tau, float gate, float delay, float moduleTag)>();

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
                cppnInput[CppnInputIndex.Li] = (float)srcNode.Layer / totalLayers;
                cppnInput[CppnInputIndex.Xj] = dstNode.X;
                cppnInput[CppnInputIndex.Yj] = dstNode.Y;
                cppnInput[CppnInputIndex.Lj] = (float)dstNode.Layer / totalLayers;
                cppnInput[CppnInputIndex.Dx] = dx;
                cppnInput[CppnInputIndex.Dy] = dy;
                cppnInput[CppnInputIndex.Dist] = dist;

                var outputs = genome.Cppn.Evaluate(cppnInput);
                float connScore = outputs[CppnOutputIndex.C];
                float weight = outputs[CppnOutputIndex.W];
                float tauRaw = outputs.Length > CppnOutputIndex.Tau ? outputs[CppnOutputIndex.Tau] : 0f;
                float gateRaw = outputs.Length > CppnOutputIndex.Gate ? outputs[CppnOutputIndex.Gate] : 0f;
                float delayRaw = outputs.Length > CppnOutputIndex.Delay ? outputs[CppnOutputIndex.Delay] : 0f;
                float moduleTagRaw = outputs.Length > CppnOutputIndex.ModuleTag ? outputs[CppnOutputIndex.ModuleTag] : 0f;

                if (connScore >= genome.Dev.ConnectionThreshold)
                {
                    candidates.Add((srcNode.NodeId, connScore, weight, tauRaw, gateRaw, delayRaw, moduleTagRaw));
                }
            }

            candidates = candidates
                .OrderByDescending(c => c.score)
                .ThenBy(c => c.srcId)
                .ToList();

            int taken = 0;
            float tauAccum = 0f;
            int tauCount = 0;
            float moduleTagAccum = 0f;
            int moduleTagCount = 0;

            foreach (var (srcId, score, weight, tau, gate, delay, moduleTag) in candidates)
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

                EdgeType edgeType;
                if (gate > ModulatoryGateThreshold)
                    edgeType = EdgeType.Modulatory;
                else if (gate < MemoryGateThreshold)
                    edgeType = EdgeType.Memory;
                else
                    edgeType = EdgeType.Normal;
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
                    Meta: new EdgeMetadata(edgeType, delayTicks, PlasticityProfileId: (int)edgeType)
                ));

                outgoingCount[srcId]++;
                taken++;
                tauAccum += tau;
                tauCount++;
                moduleTagAccum += moduleTag;
                moduleTagCount++;
            }
            
            if (tauCount > 0)
                tauSums[dstNode.NodeId] = (tauAccum, tauCount);
            if (moduleTagCount > 0)
                moduleTagSums[dstNode.NodeId] = (moduleTagAccum, moduleTagCount);

            incoming[dstNode.NodeId] = incoming[dstNode.NodeId]
                .OrderBy(e => e.SrcNodeId)
                .ToList();
        }

        // Assign full metadata to each non-input node
        for (int i = 0; i < nodes.Count; i++)
        {
            var n = nodes[i];
            if (n.Type == BrainNodeType.Input) continue;

            // TimeConstant from Tau (existing logic)
            float tc = 1.0f;
            if (tauSums.TryGetValue(n.NodeId, out var ts) && ts.count > 0)
            {
                float tauAvg = ts.sum / ts.count;
                float sig = 1f / (1f + MathF.Exp(-tauAvg));
                tc = 1.0f + sig * 9.0f; // range [1, 10]
            }

            // ModuleId from ModuleTag
            int moduleId = 0;
            if (moduleTagSums.TryGetValue(n.NodeId, out var ms) && ms.count > 0)
            {
                float tagAvg = ms.sum / ms.count;
                float tagSig = 1f / (1f + MathF.Exp(-tagAvg));
                moduleId = Math.Clamp((int)MathF.Floor(tagSig * budget.ModuleCount), 0, budget.ModuleCount - 1);
            }

            // PlasticityProfileId = mode of incoming edge types
            int nodeProfileId = 0;
            if (incoming.TryGetValue(n.NodeId, out var nodeEdges) && nodeEdges.Count > 0)
            {
                var profileCounts = new int[3]; // Normal=0, Modulatory=1, Memory=2
                foreach (var e in nodeEdges)
                    profileCounts[e.Meta.PlasticityProfileId]++;
                int maxCount = profileCounts[0];
                for (int p = 1; p < profileCounts.Length; p++)
                {
                    if (profileCounts[p] > maxCount)
                    {
                        maxCount = profileCounts[p];
                        nodeProfileId = p;
                    }
                }
            }

            nodes[i] = n with { Meta = new NodeMetadata(
                RegionId: n.Layer,
                ModuleId: moduleId,
                TimeConstant: tc,
                PlasticityProfileId: nodeProfileId
            )};
        }

        // Wire gate neurons: each gate receives connections from regime input signals only
        int[] effectiveCategoryMap = signalCategoryMap ?? Array.Empty<int>();
        if (hasGates)
        {
            // Use caller-provided category map or build a default one
            if (effectiveCategoryMap.Length == 0)
            {
                effectiveCategoryMap = new int[_sensorCount];
                for (int s = 0; s < _sensorCount; s++)
                    effectiveCategoryMap[s] = s * budget.GateNeuronCount / _sensorCount;
            }

            // Regime input signal indices
            var regimeInputIds = new List<int>();
            if (regimeSignalStart >= 0 && regimeSignalEnd >= regimeSignalStart)
            {
                for (int s = regimeSignalStart; s <= regimeSignalEnd && s < _sensorCount; s++)
                    regimeInputIds.Add(s);
            }
            else
            {
                // Default: last 4 inputs before risk awareness
                int start = Math.Max(0, _sensorCount - 12);
                for (int s = start; s < start + 4 && s < _sensorCount; s++)
                    regimeInputIds.Add(s);
            }

            for (int g = 0; g < budget.GateNeuronCount; g++)
            {
                var gateNode = nodes[gateStart + g];
                foreach (int regimeId in regimeInputIds)
                {
                    var regimeNode = nodes[regimeId];

                    float dx = gateNode.X - regimeNode.X;
                    float dy = gateNode.Y - regimeNode.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);

                    cppnInput[CppnInputIndex.Xi] = regimeNode.X;
                    cppnInput[CppnInputIndex.Yi] = regimeNode.Y;
                    cppnInput[CppnInputIndex.Li] = (float)regimeNode.Layer / totalLayers;
                    cppnInput[CppnInputIndex.Xj] = gateNode.X;
                    cppnInput[CppnInputIndex.Yj] = gateNode.Y;
                    cppnInput[CppnInputIndex.Lj] = (float)gateNode.Layer / totalLayers;
                    cppnInput[CppnInputIndex.Dx] = dx;
                    cppnInput[CppnInputIndex.Dy] = dy;
                    cppnInput[CppnInputIndex.Dist] = dist;

                    var gateOutputs = genome.Cppn.Evaluate(cppnInput);
                    float w = DeterministicHelpers.Clamp(
                        gateOutputs[CppnOutputIndex.W] * genome.Dev.InitialWeightScale,
                        -genome.Stable.WeightMaxAbs, genome.Stable.WeightMaxAbs);

                    incoming[gateNode.NodeId].Add(new BrainEdge(
                        SrcNodeId: regimeId,
                        DstNodeId: gateNode.NodeId,
                        WSlow: w, WFast: w,
                        PlasticityGain: 1f,
                        Meta: new EdgeMetadata(EdgeType.Normal, 0)
                    ));
                }
            }
        }

        return new BrainGraph(
            nodes,
            incoming,
            _sensorCount,
            _actuatorCount,
            ModulatorIndex.Count,
            gateCount: hasGates ? budget.GateNeuronCount : 0,
            signalCategoryMap: effectiveCategoryMap
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
            if (node.Type == BrainNodeType.Output || node.Type == BrainNodeType.Gate)
                continue; // Don't use outputs or gates as sources for hidden/output nodes

            float dx = MathF.Abs(dst.X - node.X) * Math.Max(1, budget.HiddenWidth - 1);
            float dy = MathF.Abs(dst.Y - node.Y) * Math.Max(1, budget.HiddenHeight - 1);
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
            .Where(n => n.Type != BrainNodeType.Output && n.Type != BrainNodeType.Gate && !candidates.Contains(n.NodeId))
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

