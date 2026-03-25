namespace Seed.Dashboard.Models;

/// <summary>
/// Lightweight snapshot of an agent for dashboard rendering.
/// </summary>
public sealed record AgentDto(
    int Id,
    float X,
    float Y,
    float Heading,
    float Energy,
    bool Alive,
    float Speed,
    int SpeciesId,
    float Signal0,
    float Signal1,
    float ShareReceived,
    float AttackReceived
);

/// <summary>
/// Lightweight snapshot of food for dashboard rendering.
/// </summary>
public sealed record FoodDto(int Id, float X, float Y, float Value, bool IsCorpse);

/// <summary>
/// Lightweight snapshot of an obstacle for dashboard rendering.
/// </summary>
public sealed record ObstacleDto(float X, float Y, float Width, float Height);

/// <summary>
/// Lightweight snapshot of a hazard for dashboard rendering.
/// </summary>
public sealed record HazardDto(float X, float Y, float Width, float Height, float Damage);

/// <summary>
/// Complete world state for a single frame.
/// </summary>
public sealed record WorldFrameDto(
    int Tick,
    int Generation,
    int WorldIndex,
    float WorldWidth,
    float WorldHeight,
    AgentDto[] Agents,
    FoodDto[] Food,
    ObstacleDto[] Obstacles,
    HazardDto[] Hazards,
    float FoodEnergyMultiplier,
    float LightLevel
);

/// <summary>
/// Brain node snapshot for visualization.
/// </summary>
public sealed record BrainNodeDto(
    int Id,
    string Type, // "Input", "Hidden", "Output", "Modulator"
    float X,
    float Y,
    float Activation,
    string? Label
);

/// <summary>
/// Brain edge snapshot for visualization.
/// </summary>
public sealed record BrainEdgeDto(
    int From,
    int To,
    float Weight,
    string Type, // "Standard", "Modulatory", "Plastic"
    int Delay,
    float WSlow = 0f,
    float WFast = 0f,
    float PlasticityGain = 0f
);

/// <summary>
/// Complete brain state for a selected agent.
/// </summary>
public sealed record BrainSnapshotDto(
    int AgentId,
    BrainNodeDto[] Nodes,
    BrainEdgeDto[] Edges
);

/// <summary>
/// Generation summary for fitness charts.
/// </summary>
public sealed record GenerationStatsDto(
    int Generation,
    float BestFitness,
    float MeanFitness,
    float WorstFitness,
    int SpeciesCount,
    int PopulationSize,
    int ModulatoryEdgeCount = 0,
    float AvgDelay = 0f,
    float AvgDistanceTraveled = 0f,
    float AvgFoodCollected = 0f,
    float AvgSurvivalTicks = 0f,
    SpeciesInfoDto[]? SpeciesBreakdown = null
);

/// <summary>
/// Simulation status for control panel.
/// </summary>
public sealed record SimulationStatusDto(
    bool IsRunning,
    bool IsPaused,
    int CurrentGeneration,
    int CurrentTick,
    int CurrentRound,
    float Speed,
    int PopulationSize,
    int SpeciesCount,
    int AliveCount,
    int MaxTicksPerRound = 300,
    int ArenaRounds = 4,
    bool OverridesActive = false
);

public sealed record RoundMetricsDto(
    int Round,
    int SurvivalTicks,
    float NetEnergyDelta,
    int FoodCollected,
    float DistanceTraveled,
    float Fitness
);

public sealed record SelectedAgentDetailsDto(
    int AgentId,
    int ConnectionCount,
    int HiddenNodeCount,
    int TotalNodeCount,
    int SurvivalTicks,
    int FoodCollected,
    float NetEnergyDelta,
    float DistanceTraveled,
    float InstabilityPenalty,
    float ModReward,
    float ModPain,
    float ModCuriosity,
    RoundMetricsDto[] RoundHistory,
    float? AggregatedFitness
);

public sealed record SpeciesInfoDto(int SpeciesId, int MemberCount, float MeanFitness);

public sealed record WorldOverrideDto(
    int? FoodCount = null,
    float? AmbientEnergyRate = null,
    float? CorpseEnergyBase = null,
    int? DayNightPeriod = null,
    int? SeasonPeriod = null,
    float? HazardDamageMultiplier = null,
    float? FoodQualityVariation = null,
    float? LightLevelOverride = null
);
