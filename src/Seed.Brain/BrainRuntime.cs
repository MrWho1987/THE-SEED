using Seed.Core;

namespace Seed.Brain;

public readonly record struct BrainDiagnostics(
    float MeanAbsActivation,
    float SaturationRate,
    float MeanAbsWeightFast,
    float MeanAbsWeightSlow,
    int ActiveEdgeCount,
    int TotalEdges
);

/// <summary>
/// Runtime execution of a sparse recurrent brain with modulated learning,
/// modulatory edges, synaptic delays, and ablation support.
/// </summary>
public sealed class BrainRuntime : IBrain
{
    private const float SaturationThreshold = 0.95f;
    private const float EmaDecay = 0.99f;
    private const float ActiveEdgeEpsilon = 1e-6f;

    private readonly BrainGraph _graph;
    private readonly LearningParams _learn;
    private readonly StabilityParams _stable;
    private readonly AblationConfig _ablation;
    private readonly int _microStepsPerTick;

    private readonly float[] _activations;
    private readonly float[] _prevActivations;
    private readonly float[][] _eligibility;
    private readonly float[] _wFast;
    private readonly float[] _wSlow;
    private readonly int[] _edgeOffsets;

    private readonly float[] _tau;
    private readonly float[] _localModulation;

    private readonly float[][] _activationHistory;
    private int _historyHead;
    private readonly int _maxDelay;

    private readonly float[] _meanAbsActivation;

    private long _saturatedCount;
    private long _totalActivations;

    private readonly int _gateCount;
    private readonly int _gateStartIndex;
    private readonly int[] _signalCategoryMap;

    public BrainRuntime(BrainGraph graph, LearningParams learn, StabilityParams stable,
        int microStepsPerTick = 3, AblationConfig? ablation = null)
    {
        _graph = graph;
        _learn = learn;
        _stable = stable;
        _ablation = ablation ?? AblationConfig.Default;
        _microStepsPerTick = microStepsPerTick;

        int nodeCount = graph.Nodes.Count;
        for (int i = 0; i < nodeCount; i++)
        {
            if (graph.Nodes[i].NodeId < 0 || graph.Nodes[i].NodeId >= nodeCount)
                throw new ArgumentException($"NodeIds must be contiguous [0, {nodeCount}). Found {graph.Nodes[i].NodeId}");
        }

        _gateCount = graph.GateCount;
        _signalCategoryMap = graph.SignalCategoryMap;
        _gateStartIndex = _gateCount > 0
            ? graph.Nodes.First(n => n.Type == BrainNodeType.Gate).NodeId
            : -1;

        _activations = new float[nodeCount];
        _prevActivations = new float[nodeCount];
        _meanAbsActivation = new float[nodeCount];
        _localModulation = new float[nodeCount];
        
        _tau = new float[nodeCount];
        for (int i = 0; i < nodeCount; i++)
            _tau[graph.Nodes[i].NodeId] = graph.Nodes[i].Meta.TimeConstant;

        int totalEdges = 0;
        int maxDelay = 0;
        _edgeOffsets = new int[nodeCount + 1];
        foreach (var node in graph.Nodes.OrderBy(n => n.NodeId))
        {
            _edgeOffsets[node.NodeId] = totalEdges;
            if (graph.IncomingByDst.TryGetValue(node.NodeId, out var edges))
            {
                totalEdges += edges.Count;
                for (int i = 0; i < edges.Count; i++)
                {
                    if (edges[i].Meta.Delay > maxDelay)
                        maxDelay = edges[i].Meta.Delay;
                }
            }
        }
        _edgeOffsets[nodeCount] = totalEdges;

        _wFast = new float[totalEdges];
        _wSlow = new float[totalEdges];
        _eligibility = new float[nodeCount][];

        foreach (var node in graph.Nodes)
        {
            int offset = _edgeOffsets[node.NodeId];
            if (graph.IncomingByDst.TryGetValue(node.NodeId, out var edges))
            {
                _eligibility[node.NodeId] = new float[edges.Count];
                for (int i = 0; i < edges.Count; i++)
                {
                    _wFast[offset + i] = edges[i].WFast;
                    _wSlow[offset + i] = edges[i].WSlow;
                }
            }
            else
            {
                _eligibility[node.NodeId] = Array.Empty<float>();
            }
        }

        _maxDelay = maxDelay;
        if (_maxDelay > 0)
        {
            _activationHistory = new float[_maxDelay + 1][];
            for (int i = 0; i <= _maxDelay; i++)
                _activationHistory[i] = new float[nodeCount];
        }
        else
        {
            _activationHistory = Array.Empty<float[]>();
        }
        _historyHead = 0;
    }

    public void Reset()
    {
        Array.Clear(_activations);
        Array.Clear(_prevActivations);
        Array.Clear(_meanAbsActivation);
        Array.Clear(_localModulation);

        foreach (var e in _eligibility)
        {
            if (e != null) Array.Clear(e);
        }

        for (int i = 0; i < _wFast.Length; i++)
            _wFast[i] = _wSlow[i];

        if (_maxDelay > 0)
        {
            for (int i = 0; i < _activationHistory.Length; i++)
                Array.Clear(_activationHistory[i]);
            _historyHead = 0;
        }

        _saturatedCount = 0;
        _totalActivations = 0;
    }

    public ReadOnlySpan<float> Step(ReadOnlySpan<float> inputs, in BrainStepContext ctx)
    {
        Array.Copy(_activations, _prevActivations, _activations.Length);

        for (int i = 0; i < _graph.InputCount && i < inputs.Length; i++)
            _activations[i] = inputs[i];

        // Gate pre-pass: compute gate activations and scale inputs
        if (_gateCount > 0)
        {
            for (int g = 0; g < _gateCount; g++)
            {
                int gateId = _gateStartIndex + g;
                if (_graph.IncomingByDst.TryGetValue(gateId, out var gateEdges))
                {
                    float sum = 0f;
                    int offset = _edgeOffsets[gateId];
                    for (int e = 0; e < gateEdges.Count; e++)
                        sum += _activations[gateEdges[e].SrcNodeId] * _wFast[offset + e];
                    _activations[gateId] = 1f / (1f + MathF.Exp(-sum)); // sigmoid [0, 1]
                }
            }

            // Apply gating to input signals
            if (_ablation.RegimeGatingEnabled && _signalCategoryMap.Length > 0)
            {
                for (int i = 0; i < _graph.InputCount && i < _signalCategoryMap.Length; i++)
                {
                    int gateIdx = _signalCategoryMap[i];
                    if (gateIdx >= 0 && gateIdx < _gateCount)
                        _activations[i] *= _activations[_gateStartIndex + gateIdx];
                }
            }
        }

        int steps = _ablation.RecurrenceEnabled ? _microStepsPerTick : 1;
        for (int micro = 0; micro < steps; micro++)
            UpdateActivations();

        TrackStability();

        if (_maxDelay > 0)
        {
            Array.Copy(_activations, _activationHistory[_historyHead], _activations.Length);
            _historyHead = (_historyHead + 1) % _activationHistory.Length;
        }

        int outputStart = _graph.InputCount + 
            (_graph.NodeCount - _graph.InputCount - _graph.OutputCount);
        
        return _activations.AsSpan(outputStart, _graph.OutputCount);
    }

    private void UpdateActivations()
    {
        foreach (var node in _graph.Nodes)
        {
            if (node.Type == BrainNodeType.Input || node.Type == BrainNodeType.Gate)
                continue;

            if (!_graph.IncomingByDst.TryGetValue(node.NodeId, out var edges))
                continue;

            float sum = 0f;
            float modSum = 0f;
            int offset = _edgeOffsets[node.NodeId];

            float scale = 1f;
            if (_ablation.HomeostasisEnabled &&
                _stable.HomeostasisStrength > 0 && _meanAbsActivation[node.NodeId] > 0)
            {
                float diff = _meanAbsActivation[node.NodeId] - _stable.ActivationTarget;
                scale = MathF.Exp(-_stable.HomeostasisStrength * diff);
            }

            float normFactor = 1f;
            if (_stable.EnableIncomingNormalization && edges.Count > 0)
            {
                float sumSq = 0f;
                for (int i = 0; i < edges.Count; i++)
                {
                    float w = _wFast[offset + i];
                    sumSq += w * w;
                }
                float rms = MathF.Sqrt(sumSq / edges.Count + _stable.IncomingNormEps);
                normFactor = 1f / rms;
            }

            for (int i = 0; i < edges.Count; i++)
            {
                float w = _wFast[offset + i] * scale * normFactor;
                int srcId = edges[i].SrcNodeId;

                float srcAct;
                int delay = edges[i].Meta.Delay;
                if (_ablation.SynapticDelaysEnabled && delay > 0 && _maxDelay > 0)
                {
                    int histIdx = ((_historyHead - delay) % _activationHistory.Length + _activationHistory.Length) % _activationHistory.Length;
                    srcAct = _activationHistory[histIdx][srcId];
                }
                else
                {
                    srcAct = _activations[srcId];
                }

                if (_ablation.ModulatoryEdgesEnabled && edges[i].Meta.EdgeType == EdgeType.Modulatory)
                    modSum += srcAct * w;
                else
                    sum += srcAct * w;
            }

            _localModulation[node.NodeId] = _ablation.ModulatoryEdgesEnabled ? MathF.Tanh(modSum) : 0f;

            float raw = MathF.Tanh(sum);
            float t = _tau[node.NodeId];
            if (t > 1f)
                _activations[node.NodeId] = (1f - 1f / t) * _prevActivations[node.NodeId] + (1f / t) * raw;
            else
                _activations[node.NodeId] = raw;
        }
    }

    private void TrackStability()
    {
        foreach (var node in _graph.Nodes)
        {
            if (node.Type == BrainNodeType.Input)
                continue;

            float absAct = MathF.Abs(_activations[node.NodeId]);
            _meanAbsActivation[node.NodeId] =
                EmaDecay * _meanAbsActivation[node.NodeId] + (1f - EmaDecay) * absAct;

            _totalActivations++;
            if (absAct > SaturationThreshold)
                _saturatedCount++;
        }
    }

    public void Learn(ReadOnlySpan<float> modulators, in BrainLearnContext ctx)
    {
        if (!_ablation.LearningEnabled)
            return;

        float M = 0f;
        if (modulators.Length > ModulatorIndex.Reward)
            M += _learn.AlphaReward * modulators[ModulatorIndex.Reward];
        if (modulators.Length > ModulatorIndex.Pain)
            M += _learn.AlphaPain * modulators[ModulatorIndex.Pain];
        if (modulators.Length > ModulatorIndex.Curiosity)
            M += _learn.AlphaCuriosity * modulators[ModulatorIndex.Curiosity];
        if (modulators.Length > ModulatorIndex.Risk)
            M += _learn.AlphaRisk * modulators[ModulatorIndex.Risk];

        float etaScale = _learn.CriticalPeriodHours > 0f
            ? MathF.Max(0.1f, 1f - ctx.ElapsedHours / _learn.CriticalPeriodHours)
            : 1f;

        foreach (var node in _graph.Nodes)
        {
            if (node.Type == BrainNodeType.Input)
                continue;

            if (!_graph.IncomingByDst.TryGetValue(node.NodeId, out var edges))
                continue;

            int offset = _edgeOffsets[node.NodeId];
            float aj = _activations[node.NodeId];
            float localMod = _localModulation[node.NodeId];

            for (int i = 0; i < edges.Count; i++)
            {
                int srcId = edges[i].SrcNodeId;
                float ai = _activations[srcId];

                float product = DeterministicHelpers.Clamp(ai * aj, -1f, 1f);
                _eligibility[node.NodeId][i] = 
                    _learn.EligibilityDecay * _eligibility[node.NodeId][i] + product;

                float effectiveM;
                if (_ablation.ModulatoryEdgesEnabled)
                {
                    var etype = edges[i].Meta.EdgeType;
                    if (etype == EdgeType.Modulatory)
                        effectiveM = M;
                    else if (_ablation.MemoryEdgesEnabled && etype == EdgeType.Memory)
                        effectiveM = 1f;
                    else
                        effectiveM = M * (1f + localMod);
                }
                else
                {
                    effectiveM = M;
                }

                float dw = _learn.Eta * etaScale * effectiveM * _eligibility[node.NodeId][i] * edges[i].PlasticityGain;
                if (float.IsNaN(dw) || float.IsInfinity(dw)) dw = 0f;
                _wFast[offset + i] = DeterministicHelpers.Clamp(
                    _wFast[offset + i] + dw,
                    -_stable.WeightMaxAbs,
                    _stable.WeightMaxAbs
                );
            }
        }

        for (int i = 0; i < _wFast.Length; i++)
        {
            float slow = (1f - _learn.BetaConsolidate) * _wSlow[i] + 
                          _learn.BetaConsolidate * _wFast[i];
            float fast = (1f - _learn.GammaRecall) * _wFast[i] + 
                          _learn.GammaRecall * _wSlow[i];
            _wSlow[i] = float.IsNaN(slow) ? 0f : slow;
            _wFast[i] = float.IsNaN(fast) ? 0f : fast;
        }
    }

    public int PruneWeakEdges(float threshold)
    {
        int pruned = 0;
        for (int i = 0; i < _wFast.Length; i++)
        {
            if (MathF.Abs(_wFast[i]) < threshold)
            {
                _wFast[i] = 0f;
                pruned++;
            }
        }
        return pruned;
    }

    public ReadOnlySpan<float> GetActivations() => _activations;

    public IBrainGraph ExportGraph()
    {
        var newIncoming = new Dictionary<int, List<BrainEdge>>();

        foreach (var node in _graph.Nodes)
        {
            if (!_graph.IncomingByDst.TryGetValue(node.NodeId, out var edges))
            {
                newIncoming[node.NodeId] = new List<BrainEdge>();
                continue;
            }

            int offset = _edgeOffsets[node.NodeId];
            var newEdges = new List<BrainEdge>();

            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i];
                newEdges.Add(e with
                {
                    WSlow = _wSlow[offset + i],
                    WFast = _wFast[offset + i]
                });
            }

            newIncoming[node.NodeId] = newEdges;
        }

        return new BrainGraph(
            _graph.Nodes.ToList(),
            newIncoming,
            _graph.InputCount,
            _graph.OutputCount,
            _graph.ModulatorCount,
            _graph.Reserved
        );
    }

    public float GetInstabilityPenalty()
    {
        if (_totalActivations == 0)
            return 0f;

        return (float)_saturatedCount / _totalActivations;
    }

    public BrainDiagnostics GetDiagnostics()
    {
        int hiddenCount = Math.Max(1, _graph.NodeCount - _graph.InputCount);
        float sumAct = 0f;
        int satCount = 0;
        foreach (var node in _graph.Nodes)
        {
            if (node.Type == BrainNodeType.Input) continue;
            float abs = MathF.Abs(_activations[node.NodeId]);
            sumAct += abs;
            if (abs > SaturationThreshold) satCount++;
        }

        float sumWf = 0f, sumWs = 0f;
        int active = 0;
        for (int i = 0; i < _wFast.Length; i++)
        {
            sumWf += MathF.Abs(_wFast[i]);
            sumWs += MathF.Abs(_wSlow[i]);
            if (MathF.Abs(_wFast[i]) > ActiveEdgeEpsilon) active++;
        }

        return new BrainDiagnostics(
            MeanAbsActivation: sumAct / hiddenCount,
            SaturationRate: (float)satCount / hiddenCount,
            MeanAbsWeightFast: _wFast.Length > 0 ? sumWf / _wFast.Length : 0f,
            MeanAbsWeightSlow: _wSlow.Length > 0 ? sumWs / _wSlow.Length : 0f,
            ActiveEdgeCount: active,
            TotalEdges: _wFast.Length
        );
    }

}
