namespace Seed.Core;

/// <summary>
/// Genome interface - the evolvable recipe.
/// </summary>
public interface IGenome
{
    Guid GenomeId { get; }
    string GenomeType { get; }
    
    string ToJson();
    IGenome CloneGenome(Guid? newId = null);
    IGenome Mutate(in MutationContext ctx);
    float DistanceTo(IGenome other, in SpeciationConfig cfg);
}

/// <summary>
/// Context for mutation operations.
/// </summary>
public readonly record struct MutationContext(
    ulong RunSeed,
    int GenerationIndex,
    MutationConfig Config,
    IInnovationTracker Innovations,
    Rng64 Rng
);

/// <summary>
/// Tracks structural innovations for NEAT-style alignment.
/// </summary>
public interface IInnovationTracker
{
    int NextInnovationId { get; }
    int NextCppnNodeId { get; }
    
    int GetOrCreateConnectionInnovation(int srcNodeId, int dstNodeId);
    (int NewNodeId, int InnovSrcToNew, int InnovNewToDst) GetOrCreateSplitInnovation(int oldConnInnovationId);
}

/// <summary>
/// Brain interface - runtime neural controller.
/// </summary>
public interface IBrain
{
    ReadOnlySpan<float> Step(ReadOnlySpan<float> inputs, in BrainStepContext ctx);
    void Learn(ReadOnlySpan<float> modulators, in BrainLearnContext ctx);
    void Reset();

    /// <summary>
    /// Read-only access to the current activation state of all nodes.
    /// </summary>
    ReadOnlySpan<float> GetActivations();

    /// <summary>
    /// Snapshot the brain's current learned weights into an exportable graph.
    /// </summary>
    IBrainGraph ExportGraph();

    /// <summary>
    /// Fraction of node activations that are saturated (|a| > 0.95).
    /// Returns 0 if no activations have been tracked yet.
    /// </summary>
    float GetInstabilityPenalty();
}

/// <summary>
/// Brain graph export interface.
/// </summary>
public interface IBrainGraph
{
    int InputCount { get; }
    int OutputCount { get; }
    int ModulatorCount { get; }
    int NodeCount { get; }
    int EdgeCount { get; }
    
    string ToJson();
}

/// <summary>
/// Observatory interface - metrics and replay logging.
/// </summary>
public interface IObservatory
{
    void OnEvent(in ObsEvent e);
    void Flush();
}

