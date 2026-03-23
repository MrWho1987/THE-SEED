namespace Seed.Core;

/// <summary>
/// Controls brain development resolution and sparsity.
/// </summary>
public readonly record struct DevelopmentBudget(
    int HiddenWidth = 12,
    int HiddenHeight = 12,
    int HiddenLayers = 2,
    int TopKIn = 12,
    int MaxOut = 16,
    int LocalNeighborhoodRadius = 2,
    int GlobalCandidateSamplesPerNeuron = 16,
    int MaxSynapticDelay = 5
)
{
    public int TotalHiddenNeurons => HiddenWidth * HiddenHeight * HiddenLayers;
    public static DevelopmentBudget Default => new(12, 12, 2, 12, 16, 2, 16, 5);
}

/// <summary>
/// Controls episode length and brain update frequency.
/// </summary>
public readonly record struct RuntimeBudget(
    int MaxTicksPerEpisode = 1500,
    int MicroStepsPerTick = 3
)
{
    public static RuntimeBudget Default => new(1500, 3);
}

/// <summary>
/// Controls evolutionary population parameters.
/// </summary>
public readonly record struct PopulationBudget(
    int PopulationSize = 128,
    int ArenaRounds = 4,
    int ElitesPerSpecies = 1,
    int MinSpeciesSizeForElitism = 5
)
{
    public static PopulationBudget Default => new(128, 4, 1, 5);
}

/// <summary>
/// Controls world size and content density.
/// </summary>
public readonly record struct WorldBudget(
    int WorldWidth = 64,
    int WorldHeight = 64,
    float ObstacleDensity = 0.12f,
    float HazardDensity = 0.04f,
    int FoodCount = 25,
    int FoodClusters = 0,
    float FoodEnergyAmplitude = 0f,
    int FoodEnergyPeriod = 500,
    float RoundJitter = 0f
)
{
    public static WorldBudget Default => new(64, 64, 0.12f, 0.04f, 25);

    public WorldBudget Jitter(ref Rng64 rng)
    {
        if (RoundJitter <= 0) return this;
        float j = RoundJitter;
        return new WorldBudget(
            WorldWidth: Math.Max(16, (int)(WorldWidth * (1f + j * rng.NextFloat(-1f, 1f)))),
            WorldHeight: Math.Max(16, (int)(WorldHeight * (1f + j * rng.NextFloat(-1f, 1f)))),
            ObstacleDensity: Math.Max(0f, ObstacleDensity * (1f + j * rng.NextFloat(-1f, 1f))),
            HazardDensity: Math.Max(0f, HazardDensity * (1f + j * rng.NextFloat(-1f, 1f))),
            FoodCount: Math.Max(5, (int)(FoodCount * (1f + j * rng.NextFloat(-1f, 1f)))),
            FoodClusters: FoodClusters,
            FoodEnergyAmplitude: FoodEnergyAmplitude,
            FoodEnergyPeriod: FoodEnergyPeriod,
            RoundJitter: 0f
        );
    }
}

/// <summary>
/// Controls parallelism.
/// </summary>
public readonly record struct ComputeBudget(
    int MaxWorkerThreads = 0
)
{
    public int EffectiveWorkerCount => MaxWorkerThreads > 0 
        ? MaxWorkerThreads 
        : Math.Max(1, Environment.ProcessorCount);
    public static ComputeBudget Default => new(0);
}

/// <summary>
/// All budgets combined for convenience.
/// </summary>
public readonly record struct AllBudgets(
    DevelopmentBudget Development,
    RuntimeBudget Runtime,
    PopulationBudget Population,
    WorldBudget World,
    ComputeBudget Compute
)
{
    public static AllBudgets Default => new(
        DevelopmentBudget.Default,
        RuntimeBudget.Default,
        PopulationBudget.Default,
        new WorldBudget(
            WorldWidth: 64,
            WorldHeight: 64,
            ObstacleDensity: 0.12f,
            HazardDensity: 0.04f,
            FoodCount: 25,
            FoodClusters: 3,
            FoodEnergyAmplitude: 0.4f,
            FoodEnergyPeriod: 500,
            RoundJitter: 0.15f
        ),
        ComputeBudget.Default
    );
}


