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
    int Tick
);

/// <summary>
/// Context for body reset.
/// </summary>
public readonly record struct BodyResetContext(
    ulong AgentSeed
);

/// <summary>
/// Current body state snapshot.
/// </summary>
public readonly record struct BodyState(
    float Energy,
    bool Alive
);

/// <summary>
/// Signals from world to body.
/// </summary>
public readonly record struct WorldSignals(
    float EnergyDelta,           // can be negative for movement cost / hazards
    int FoodCollectedThisStep,
    float HazardPenalty          // >= 0
);

/// <summary>
/// Result of a world step.
/// </summary>
public readonly record struct WorldStepResult(
    bool Done,
    WorldSignals Signals,
    float[] Modulators,          // [Reward, Pain, Curiosity]
    WorldStepInfo Info
);

/// <summary>
/// Reserved info for observability/debug.
/// </summary>
public readonly record struct WorldStepInfo(
    int Reserved0 = 0,
    int Reserved1 = 0
);

/// <summary>
/// Metrics from a single episode evaluation.
/// </summary>
public readonly record struct EpisodeMetrics(
    int SurvivalTicks,
    float NetEnergyDelta,
    int FoodCollected,
    float EnergySpent,
    float InstabilityPenalty,
    float Fitness
);

/// <summary>
/// Aggregated fitness across a world bundle.
/// </summary>
public readonly record struct FitnessAggregate(
    float MeanFitness,
    float VarianceFitness,
    float WorstFitness,
    float Score
);

/// <summary>
/// Full evaluation result for a genome.
/// </summary>
public sealed record GenomeEvaluationResult(
    Guid GenomeId,
    IGenome Genome,
    EpisodeMetrics[] PerWorld,
    FitnessAggregate Aggregate
);

/// <summary>
/// Context for genome evaluation.
/// </summary>
public readonly record struct EvaluationContext(
    ulong RunSeed,
    int GenerationIndex,
    int WorldBundleKey,
    DevelopmentBudget DevelopmentBudget,
    RuntimeBudget RuntimeBudget,
    WorldBudget WorldBudget,
    int ModulatorCount = 3,     // Reward, Pain, Curiosity
    AblationConfig? Ablations = null,
    int ArenaRounds = 4,
    FitnessConfig? FitnessConfig = null
)
{
    /// <summary>
    /// Get effective ablation config (defaults if not provided).
    /// </summary>
    public AblationConfig EffectiveAblations => Ablations ?? AblationConfig.Default;
    public FitnessConfig EffectiveFitnessConfig => FitnessConfig ?? Core.FitnessConfig.Default;
}

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

