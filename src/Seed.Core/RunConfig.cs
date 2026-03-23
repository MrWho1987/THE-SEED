using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seed.Core;

/// <summary>
/// Master configuration for a training run.
/// Serializable to/from JSON for reproducibility.
/// </summary>
public sealed record RunConfig(
    ulong RunSeed,
    int MaxGenerations,
    AllBudgets Budgets,
    SpeciationConfig Speciation,
    MutationConfig Mutation,
    FitnessConfig Fitness,
    AblationConfig Ablations
)
{
    /// <summary>
    /// Default local-mode configuration.
    /// </summary>
    public static RunConfig Default => new(
        RunSeed: 42UL,
        MaxGenerations: 100,
        Budgets: AllBudgets.Default,
        Speciation: SpeciationConfig.Default,
        Mutation: MutationConfig.Default,
        Fitness: FitnessConfig.Default,
        Ablations: AblationConfig.Default
    );

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static RunConfig FromJson(string json) =>
        JsonSerializer.Deserialize<RunConfig>(json, JsonOptions)
        ?? throw new InvalidOperationException("Failed to deserialize RunConfig");

    public static RunConfig LoadFromFile(string path) =>
        FromJson(File.ReadAllText(path));

    public void SaveToFile(string path) =>
        File.WriteAllText(path, ToJson());
}

/// <summary>
/// Modulator indices (fixed positions in the modulator vector).
/// </summary>
public static class ModulatorIndex
{
    public const int Reward = 0;
    public const int Pain = 1;
    public const int Curiosity = 2;
    public const int Count = 3;
}


