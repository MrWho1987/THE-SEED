using Seed.Core;

namespace Seed.Genetics;

/// <summary>
/// Key for connection innovation lookup.
/// </summary>
public readonly record struct ConnectionKey(int SrcNodeId, int DstNodeId);

/// <summary>
/// Innovation info for a node split mutation.
/// </summary>
public readonly record struct SplitInnovation(
    int NewNodeId,
    int InnovSrcToNew,
    int InnovNewToDst
);

/// <summary>
/// Tracks structural innovations for NEAT-style alignment across the population.
/// Must be used single-threaded in reproduction order for determinism.
/// </summary>
public sealed class InnovationTracker : IInnovationTracker
{
    private int _nextInnovationId;
    private int _nextCppnNodeId;

    private readonly Dictionary<ConnectionKey, int> _connInnov = new();
    private readonly Dictionary<int, SplitInnovation> _splitInnov = new();

    public int NextInnovationId => _nextInnovationId;
    public int NextCppnNodeId => _nextCppnNodeId;

    public InnovationTracker(int initialNextInnovationId, int initialNextCppnNodeId)
    {
        _nextInnovationId = initialNextInnovationId;
        _nextCppnNodeId = initialNextCppnNodeId;
    }

    /// <summary>
    /// Create a tracker initialized for a standard CPPN.
    /// </summary>
    public static InnovationTracker CreateDefault()
    {
        // CPPN inputs: 9 (xi, yi, li, xj, yj, lj, dx, dy, dist) → NodeIds 0..8
        // CPPN outputs: 6 (c, w, delay, tau, module_tag, gate) → NodeIds 9..14
        // Initial connections: 9 * 6 = 54
        return new InnovationTracker(
            initialNextInnovationId: 54,
            initialNextCppnNodeId: 15
        );
    }

    public int GetOrCreateConnectionInnovation(int srcNodeId, int dstNodeId)
    {
        var key = new ConnectionKey(srcNodeId, dstNodeId);
        if (_connInnov.TryGetValue(key, out int innov))
            return innov;

        innov = _nextInnovationId++;
        _connInnov[key] = innov;
        return innov;
    }

    public (int NewNodeId, int InnovSrcToNew, int InnovNewToDst) GetOrCreateSplitInnovation(int oldConnInnovationId)
    {
        if (_splitInnov.TryGetValue(oldConnInnovationId, out var split))
            return (split.NewNodeId, split.InnovSrcToNew, split.InnovNewToDst);

        split = new SplitInnovation(
            NewNodeId: _nextCppnNodeId++,
            InnovSrcToNew: _nextInnovationId++,
            InnovNewToDst: _nextInnovationId++
        );
        _splitInnov[oldConnInnovationId] = split;
        return (split.NewNodeId, split.InnovSrcToNew, split.InnovNewToDst);
    }
}


