namespace Seed.Genetics;

/// <summary>
/// CPPN node type.
/// </summary>
public enum CppnNodeType
{
    Input = 0,
    Hidden = 1,
    Output = 2
}

/// <summary>
/// CPPN activation functions.
/// </summary>
public enum ActivationFn
{
    Identity = 0,
    Tanh = 1,
    Sigmoid = 2,
    Sin = 3,
    Gauss = 4
}

/// <summary>
/// CPPN node.
/// </summary>
public sealed record CppnNode(
    int NodeId,
    CppnNodeType Type,
    ActivationFn Activation,
    float Bias
)
{
    /// <summary>
    /// Apply activation function.
    /// </summary>
    public float Activate(float x) => Activation switch
    {
        ActivationFn.Identity => x,
        ActivationFn.Tanh => MathF.Tanh(x),
        ActivationFn.Sigmoid => 1f / (1f + MathF.Exp(-x)),
        ActivationFn.Sin => MathF.Sin(x),
        ActivationFn.Gauss => MathF.Exp(-x * x),
        _ => x
    };
}

/// <summary>
/// CPPN connection (gene).
/// </summary>
public sealed record CppnConnection(
    int InnovationId,
    int SrcNodeId,
    int DstNodeId,
    float Weight,
    bool Enabled
);

/// <summary>
/// CPPN network structure.
/// </summary>
public sealed record CppnNetwork(
    List<CppnNode> Nodes,
    List<CppnConnection> Connections,
    int NextNodeId
)
{
    // Cached evaluation structures (lazily built)
    private float[]? _activationBuf;
    private (int srcIdx, float weight)[][]? _incomingByIdx;
    private int[]? _topoOrder;       // null if cyclic
    private int[]? _nonInputIndices;
    private int[]? _outputIndices;
    private Dictionary<int, int>? _idToIdx;

    public static CppnNetwork CreateMinimal(int inputCount, int outputCount, Seed.Core.Rng64 rng)
    {
        var nodes = new List<CppnNode>();
        var connections = new List<CppnConnection>();

        for (int i = 0; i < inputCount; i++)
            nodes.Add(new CppnNode(i, CppnNodeType.Input, ActivationFn.Identity, 0f));

        for (int i = 0; i < outputCount; i++)
            nodes.Add(new CppnNode(inputCount + i, CppnNodeType.Output, ActivationFn.Tanh, 0f));

        int innovId = 0;
        for (int src = 0; src < inputCount; src++)
            for (int dst = inputCount; dst < inputCount + outputCount; dst++)
                connections.Add(new CppnConnection(innovId++, src, dst, rng.NextFloat(-1f, 1f), true));

        return new CppnNetwork(nodes, connections, inputCount + outputCount);
    }

    private void EnsureCached()
    {
        if (_idToIdx != null) return;

        int n = Nodes.Count;
        _idToIdx = new Dictionary<int, int>(n);
        for (int i = 0; i < n; i++)
            _idToIdx.TryAdd(Nodes[i].NodeId, i);

        _activationBuf = new float[n];

        // Build adjacency lists indexed by destination node array-index
        var incoming = new List<(int srcIdx, float weight)>[n];
        for (int i = 0; i < n; i++) incoming[i] = new();

        foreach (var conn in Connections)
        {
            if (!conn.Enabled) continue;
            if (_idToIdx.TryGetValue(conn.SrcNodeId, out int si) &&
                _idToIdx.TryGetValue(conn.DstNodeId, out int di))
            {
                incoming[di].Add((si, conn.Weight));
            }
        }
        _incomingByIdx = incoming.Select(l => l.ToArray()).ToArray();

        // Identify non-input and output indices
        var nonInput = new List<int>();
        var outputs = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (Nodes[i].Type != CppnNodeType.Input)
                nonInput.Add(i);
            if (Nodes[i].Type == CppnNodeType.Output)
                outputs.Add(i);
        }
        _nonInputIndices = nonInput.ToArray();
        _outputIndices = outputs.ToArray();

        // Topological sort via Kahn's algorithm (only over non-input nodes)
        _topoOrder = TryTopologicalSort(n, _incomingByIdx, _nonInputIndices);
    }

    private static int[]? TryTopologicalSort(int nodeCount, (int srcIdx, float weight)[][] incoming, int[] nonInputIndices)
    {
        var nonInputSet = new HashSet<int>(nonInputIndices);
        var inDegree = new int[nodeCount];
        foreach (int ni in nonInputIndices)
        {
            foreach (var (srcIdx, _) in incoming[ni])
            {
                if (nonInputSet.Contains(srcIdx))
                    inDegree[ni]++;
            }
        }

        var queue = new Queue<int>();
        foreach (int ni in nonInputIndices)
        {
            if (inDegree[ni] == 0)
                queue.Enqueue(ni);
        }

        var order = new List<int>(nonInputIndices.Length);
        while (queue.Count > 0)
        {
            int cur = queue.Dequeue();
            order.Add(cur);

            foreach (int ni in nonInputIndices)
            {
                foreach (var (srcIdx, _) in incoming[ni])
                {
                    if (srcIdx == cur)
                    {
                        inDegree[ni]--;
                        if (inDegree[ni] == 0)
                            queue.Enqueue(ni);
                    }
                }
            }
        }

        return order.Count == nonInputIndices.Length ? order.ToArray() : null;
    }

    public float[] Evaluate(ReadOnlySpan<float> inputs)
    {
        EnsureCached();

        var act = _activationBuf!;

        // Set input activations, zero others
        int inputIdx = 0;
        for (int i = 0; i < Nodes.Count; i++)
        {
            if (Nodes[i].Type == CppnNodeType.Input)
                act[i] = inputIdx < inputs.Length ? inputs[inputIdx++] : 0f;
            else
                act[i] = 0f;
        }

        if (_topoOrder != null)
        {
            // Acyclic: single-pass in topological order
            foreach (int idx in _topoOrder)
            {
                float sum = Nodes[idx].Bias;
                foreach (var (srcIdx, weight) in _incomingByIdx![idx])
                    sum += act[srcIdx] * weight;
                act[idx] = Nodes[idx].Activate(sum);
            }
        }
        else
        {
            // Cyclic: iterative settling with convergence check
            for (int iter = 0; iter < 10; iter++)
            {
                float maxDelta = 0f;
                foreach (int idx in _nonInputIndices!)
                {
                    float sum = Nodes[idx].Bias;
                    foreach (var (srcIdx, weight) in _incomingByIdx![idx])
                        sum += act[srcIdx] * weight;
                    float newVal = Nodes[idx].Activate(sum);
                    float delta = MathF.Abs(newVal - act[idx]);
                    if (delta > maxDelta) maxDelta = delta;
                    act[idx] = newVal;
                }
                if (maxDelta < 1e-4f) break;
            }
        }

        // Collect outputs
        var outputs = new float[_outputIndices!.Length];
        for (int i = 0; i < _outputIndices.Length; i++)
            outputs[i] = act[_outputIndices[i]];
        return outputs;
    }

    public CppnNetwork DeepCopy()
    {
        return new CppnNetwork(
            Nodes.Select(n => n with { }).ToList(),
            Connections.Select(c => c with { }).ToList(),
            NextNodeId
        );
    }
}

/// <summary>
/// CPPN input indices (geometry of candidate edge i->j).
/// </summary>
public static class CppnInputIndex
{
    public const int Xi = 0;      // source X coordinate
    public const int Yi = 1;      // source Y coordinate  
    public const int Li = 2;      // source layer
    public const int Xj = 3;      // destination X coordinate
    public const int Yj = 4;      // destination Y coordinate
    public const int Lj = 5;      // destination layer
    public const int Dx = 6;      // delta X
    public const int Dy = 7;      // delta Y
    public const int Dist = 8;    // euclidean distance
    public const int Count = 9;
}

/// <summary>
/// CPPN output indices.
/// </summary>
public static class CppnOutputIndex
{
    public const int C = 0;           // connection score
    public const int W = 1;           // initial weight
    public const int Delay = 2;       // reserved for V2
    public const int Tau = 3;         // reserved for V2
    public const int ModuleTag = 4;   // reserved for V2
    public const int Gate = 5;        // reserved for V2
    public const int Count = 6;
}

