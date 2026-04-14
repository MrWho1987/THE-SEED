namespace Seed.Core;

/// <summary>
/// Context for brain development (genome to graph compilation).
/// </summary>
public readonly record struct DevelopmentContext(
    ulong RunSeed,
    int GenerationIndex
);

/// <summary>
/// Context for brain step execution.
/// </summary>
public readonly record struct BrainStepContext(
    int Tick
);

/// <summary>
/// Context for brain learning update.
/// </summary>
public readonly record struct BrainLearnContext(
    int Tick,
    float ElapsedHours = 0f
);

/// <summary>
/// Observatory event types (append-only).
/// </summary>
public enum ObsEventType
{
    GenerationStart = 0,
    GenerationEnd = 1,
    GenomeEvaluated = 2,
    SpeciesAssigned = 3,
    BrainExported = 4,
    EpisodeComplete = 5
}

/// <summary>
/// Observatory event record.
/// </summary>
public readonly record struct ObsEvent(
    ObsEventType Type,
    int GenerationIndex,
    Guid GenomeId,
    string PayloadJson
);

