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
    int MaxSynapticDelay = 5,
    int ModuleCount = 8,
    int GateNeuronCount = 0,
    int MinOutputConnectivity = 1  // v2: guarantee output neurons get at least N incoming edges
)
{
    public int TotalHiddenNeurons => HiddenWidth * HiddenHeight * HiddenLayers;
    public static DevelopmentBudget Default => new(12, 12, 2, 12, 16, 2, 16, 5, 8, 0, 1);
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
    public static PopulationBudget Default => new(256, 6, 2, 5);
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



