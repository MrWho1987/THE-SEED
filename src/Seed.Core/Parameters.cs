namespace Seed.Core;

/// <summary>
/// Development parameters that control CPPN-to-brain compilation.
/// These are evolvable genome fields (within bounded ranges).
/// </summary>
public sealed record DevelopmentParams(
    int TopKInMin = 6,
    int TopKInMax = 16,
    int MaxOutMin = 8,
    int MaxOutMax = 24,
    float ConnectionThreshold = 0.20f,
    float InitialWeightScale = 1.0f,
    float GlobalSampleRate = 0.02f,
    int SubstrateWidth = 16,
    int SubstrateHeight = 16,
    int SubstrateLayers = 3
)
{
    public static DevelopmentParams Default => new();
}

/// <summary>
/// Learning parameters that control plasticity.
/// </summary>
public sealed record LearningParams(
    float Eta = 0.01f,                      // learning rate
    float EligibilityDecay = 0.95f,         // λ
    float AlphaReward = 1.0f,               // αR
    float AlphaPain = -1.0f,                // αP (negative to convert pain to punishment)
    float AlphaCuriosity = 0.25f,           // αC
    float BetaConsolidate = 0.01f,          // β (slow weight update rate)
    float GammaRecall = 0.01f,              // γ (fast weight recall from slow)
    int CriticalPeriodTicks = 1000          // ticks over which Eta decays to 10%
)
{
    public static LearningParams Default => new();
}

/// <summary>
/// Stability parameters that prevent runaway activations and weights.
/// </summary>
public sealed record StabilityParams(
    float WeightMaxAbs = 3.0f,
    float HomeostasisStrength = 0.01f,
    float ActivationTarget = 0.15f,
    float IncomingNormEps = 1e-5f,
    bool EnableIncomingNormalization = true
)
{
    public static StabilityParams Default => new();
}

/// <summary>
/// Speciation configuration for NEAT-style distance and species assignment.
/// </summary>
public sealed record SpeciationConfig(
    float C1 = 1.0f,                        // excess gene coefficient
    float C2 = 1.0f,                        // disjoint gene coefficient
    float C3 = 0.4f,                        // weight difference coefficient
    float CompatibilityThreshold = 3.0f,    // species boundary
    float ShareSigma = 3.0f,                // fitness sharing sigma
    float ShareAlpha = 1.0f,                // fitness sharing exponent
    int TournamentSize = 3                  // tournament selection size for parent selection
)
{
    public static SpeciationConfig Default => new();
}

/// <summary>
/// Mutation configuration with per-operator probabilities and magnitudes.
/// Tuned to reduce destructive mutations and preserve beneficial genomes.
/// </summary>
public sealed record MutationConfig(
    float PWeightMutate = 0.50f,            // 80% → 50% (less disruptive)
    float PWeightReset = 0.10f,
    float SigmaWeight = 0.10f,              // 0.20 → 0.10 (smaller perturbations)
    float WeightResetMax = 1.00f,
    float PBiasMutate = 0.30f,
    float SigmaBias = 0.10f,
    float PAddConn = 0.05f,                 // 0.10 → 0.05 (slower structural growth)
    float PAddNode = 0.02f,                 // 0.03 → 0.02 (slower structural growth)
    float WInitMax = 1.00f,
    float PParamMutate = 0.20f,
    float SigmaParam = 0.05f,
    float PCrossover = 0.35f                // 35% of non-elite offspring via crossover
)
{
    public static MutationConfig Default => new();
}

/// <summary>
/// Fitness aggregation configuration.
/// </summary>
public sealed record FitnessConfig(
    float LambdaVar = 0.10f,                // penalty for variance
    float LambdaWorst = 0.20f               // bonus for worst-case performance
)
{
    public static FitnessConfig Default => new();
}

/// <summary>
/// Ablation flags for debugging and analysis.
/// </summary>
public sealed record AblationConfig(
    bool LearningEnabled = true,
    bool CuriosityEnabled = true,
    bool HomeostasisEnabled = true,
    bool EvolutionEnabled = true,
    bool RandomActionsEnabled = false,
    bool PredictionErrorCuriosity = false,
    bool ModulatoryEdgesEnabled = true,
    bool SynapticDelaysEnabled = true,
    bool RecurrenceEnabled = true
)
{
    public static AblationConfig Default => new();
}

