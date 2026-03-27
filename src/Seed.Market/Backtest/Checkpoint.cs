using System.Text.Json;
using System.Text.Json.Serialization;
using Seed.Core;
using Seed.Genetics;

namespace Seed.Market.Backtest;

/// <summary>
/// Serializable snapshot of an evolution run's state, allowing resume after interruption.
/// </summary>
public sealed class CheckpointState
{
    public int Generation { get; init; }
    public float BestFitness { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public List<string> GenomeJsons { get; init; } = [];
    public List<int> SpeciesIds { get; init; } = [];

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        var json = JsonSerializer.Serialize(this, JsonOpts);
        File.WriteAllText(path, json);
    }

    public static CheckpointState Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<CheckpointState>(json, JsonOpts)
            ?? throw new InvalidOperationException($"Failed to deserialize checkpoint: {path}");
    }

    public static CheckpointState FromPopulation(
        IReadOnlyList<IGenome> population, int generation, float bestFitness,
        IReadOnlyList<int>? speciesIds = null)
    {
        return new CheckpointState
        {
            Generation = generation,
            BestFitness = bestFitness,
            Timestamp = DateTimeOffset.UtcNow,
            GenomeJsons = population.Select(g => g.ToJson()).ToList(),
            SpeciesIds = speciesIds?.ToList() ?? []
        };
    }

    public List<IGenome> RestorePopulation()
    {
        return GenomeJsons.Select(j => (IGenome)SeedGenome.FromJson(j)).ToList();
    }

    /// <summary>
    /// Find the latest checkpoint file in the given directory, if any.
    /// </summary>
    public static string? FindLatest(string checkpointDir)
    {
        if (!Directory.Exists(checkpointDir)) return null;

        return Directory.GetFiles(checkpointDir, "checkpoint_*.json")
            .OrderByDescending(f => f)
            .FirstOrDefault();
    }
}
