using Seed.Core;

namespace Seed.Brain;

/// <summary>
/// Runtime execution of a sparse recurrent brain with modulated learning,
/// modulatory edges, and synaptic delays.
/// </summary>
public sealed class BrainRuntime : IBrain
{
    private readonly BrainGraph _graph;
    private readonly LearningParams _learn;
    private readonly StabilityParams _stable;
    private readonly int _microStepsPerTick;

    // Runtime state
    private readonly float[] _activations;
    private readonly float[] _prevActivations;
    private readonly float[][] _eligibility; // per destination, aligned with incoming edges
    private readonly float[] _wFast;         // flat array of fast weights
    private readonly float[] _wSlow;         // flat array of slow weights
    private readonly int[] _edgeOffsets;     // start index in flat arrays per destination

    // Time constants for leaky integration (tau >= 1; tau=1 means instant)
    private readonly float[] _tau;

    // Modulatory edge state: per-node local modulation signal in [-1, 1]
    private readonly float[] _localModulation;

    // Synaptic delay state: circular buffer of past activations
    private readonly float[][] _activationHistory;
    private int _historyHead;
    private readonly int _maxDelay;

    // Homeostasis state
    private readonly float[] _meanAbsActivation;

    // Stability tracking
    private int _saturatedCount;
    private int _totalActivations;

    public BrainRuntime(BrainGraph graph, LearningParams learn, StabilityParams stable, int microStepsPerTick = 3)
    {
        _graph = graph;
        _learn = learn;
        _stable = stable;
        _microStepsPerTick = microStepsPerTick;

        int nodeCount = graph.Nodes.Count;
        _activations = new float[nodeCount];
        _prevActivations = new float[nodeCount];
        _meanAbsActivation = new float[nodeCount];
        _localModulation = new float[nodeCount];
        
        _tau = new float[nodeCount];
        for (int i = 0; i < nodeCount; i++)
            _tau[graph.Nodes[i].NodeId] = graph.Nodes[i].Meta.TimeConstant;

        // Count total edges, build offset map, and find max delay
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

        // Initialize weights from graph
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

        // Allocate delay buffer only if any edge has delay > 0
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

    /// <summary>
    /// Get current node activations for visualization.
    /// </summary>
    public ReadOnlySpan<float> GetActivations() => _activations;
    
    public void Reset()
    {
        Array.Clear(_activations);
        Array.Clear(_prevActivations);
        Array.Clear(_meanAbsActivation);
        Array.Clear(_localModulation);

        // Reset eligibility traces
        foreach (var e in _eligibility)
        {
            if (e != null) Array.Clear(e);
        }

        // Reset weights to initial (wSlow)
        for (int i = 0; i < _wFast.Length; i++)
        {
            _wFast[i] = _wSlow[i];
        }

        // Reset delay buffer
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
        // Copy previous activations
        Array.Copy(_activations, _prevActivations, _activations.Length);

        // Set input activations
        for (int i = 0; i < _graph.InputCount && i < inputs.Length; i++)
        {
            _activations[i] = inputs[i];
        }

        // Multi-step recurrent update
        for (int micro = 0; micro < _microStepsPerTick; micro++)
        {
            UpdateActivations();
        }

        // Track stability
        TrackStability();

        // Save end-of-tick snapshot to delay buffer
        if (_maxDelay > 0)
        {
            Array.Copy(_activations, _activationHistory[_historyHead], _activations.Length);
            _historyHead = (_historyHead + 1) % _activationHistory.Length;
        }

        // Return output activations
        int outputStart = _graph.InputCount + 
            (_graph.NodeCount - _graph.InputCount - _graph.OutputCount);
        
        return _activations.AsSpan(outputStart, _graph.OutputCount);
    }

    private void UpdateActivations()
    {
        foreach (var node in _graph.Nodes)
        {
            if (node.Type == BrainNodeType.Input)
                continue;

            if (!_graph.IncomingByDst.TryGetValue(node.NodeId, out var edges))
                continue;

            float sum = 0f;
            float modSum = 0f;
            int offset = _edgeOffsets[node.NodeId];

            // Compute homeostasis scale
            float scale = 1f;
            if (_stable.HomeostasisStrength > 0 && _meanAbsActivation[node.NodeId] > 0)
            {
                float diff = _meanAbsActivation[node.NodeId] - _stable.ActivationTarget;
                scale = MathF.Exp(-_stable.HomeostasisStrength * diff);
            }

            // Optional incoming normalization (only over normal edges for stability)
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

                // Source activation: delayed or current
                float srcAct;
                int delay = edges[i].Meta.Delay;
                if (delay > 0 && _maxDelay > 0)
                {
                    int histIdx = ((_historyHead - delay) % _activationHistory.Length + _activationHistory.Length) % _activationHistory.Length;
                    srcAct = _activationHistory[histIdx][srcId];
                }
                else
                {
                    srcAct = _activations[srcId];
                }

                if (edges[i].Meta.EdgeType == EdgeType.Modulatory)
                    modSum += srcAct * w;
                else
                    sum += srcAct * w;
            }

            _localModulation[node.NodeId] = MathF.Tanh(modSum);

            // Tanh activation with optional leaky integration
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
        const float saturationThreshold = 0.95f;
        const float emaDecay = 0.99f;

        foreach (var node in _graph.Nodes)
        {
            if (node.Type == BrainNodeType.Input)
                continue;

            float absAct = MathF.Abs(_activations[node.NodeId]);
            _meanAbsActivation[node.NodeId] = 
                emaDecay * _meanAbsActivation[node.NodeId] + (1f - emaDecay) * absAct;

            _totalActivations++;
            if (absAct > saturationThreshold)
                _saturatedCount++;
        }
    }

    public void Learn(ReadOnlySpan<float> modulators, in BrainLearnContext ctx)
    {
        // Compute combined global modulator
        float M = 0f;
        if (modulators.Length > ModulatorIndex.Reward)
            M += _learn.AlphaReward * modulators[ModulatorIndex.Reward];
        if (modulators.Length > ModulatorIndex.Pain)
            M += _learn.AlphaPain * modulators[ModulatorIndex.Pain];
        if (modulators.Length > ModulatorIndex.Curiosity)
            M += _learn.AlphaCuriosity * modulators[ModulatorIndex.Curiosity];

        // Update eligibility and weights
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

                // Update eligibility trace
                float product = DeterministicHelpers.Clamp(ai * aj, -1f, 1f);
                _eligibility[node.NodeId][i] = 
                    _learn.EligibilityDecay * _eligibility[node.NodeId][i] + product;

                // Effective modulator: normal edges get local modulation, modulatory edges use raw M
                float effectiveM = edges[i].Meta.EdgeType == EdgeType.Modulatory
                    ? M
                    : M * (1f + localMod);

                float dw = _learn.Eta * effectiveM * _eligibility[node.NodeId][i] * edges[i].PlasticityGain;
                if (float.IsNaN(dw) || float.IsInfinity(dw)) dw = 0f;
                _wFast[offset + i] = DeterministicHelpers.Clamp(
                    _wFast[offset + i] + dw,
                    -_stable.WeightMaxAbs,
                    _stable.WeightMaxAbs
                );
            }
        }

        // Two-speed consolidation
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
}
