using System.Text;
using System.Text.Json;
using Seed.Core;

namespace Seed.Observatory;

/// <summary>
/// File-based observatory that writes JSONL events and exports.
/// </summary>
public sealed class FileObservatory : IObservatory, IDisposable
{
    private readonly string _outputDir;
    private readonly StreamWriter _eventLog;
    private readonly object _lock = new();
    private bool _disposed;

    public FileObservatory(string outputDir)
    {
        _outputDir = outputDir;
        Directory.CreateDirectory(outputDir);

        var eventPath = Path.Combine(outputDir, "events.jsonl");
        _eventLog = new StreamWriter(eventPath, append: false, Encoding.UTF8)
        {
            AutoFlush = false
        };
    }

    public void OnEvent(in ObsEvent e)
    {
        var line = JsonSerializer.Serialize(new
        {
            timestamp = DateTime.UtcNow.ToString("O"),
            type = e.Type.ToString(),
            generation = e.GenerationIndex,
            genomeId = e.GenomeId.ToString(),
            payload = e.PayloadJson
        });

        lock (_lock)
        {
            _eventLog.WriteLine(line);
        }
    }

    public void Flush()
    {
        lock (_lock)
        {
            _eventLog.Flush();
        }
    }

    /// <summary>
    /// Export a brain graph to JSON file.
    /// </summary>
    public void ExportBrain(IBrainGraph graph, string filename)
    {
        var path = Path.Combine(_outputDir, filename);
        File.WriteAllText(path, graph.ToJson());
    }

    /// <summary>
    /// Export a genome to JSON file.
    /// </summary>
    public void ExportGenome(IGenome genome, string filename)
    {
        var path = Path.Combine(_outputDir, filename);
        File.WriteAllText(path, genome.ToJson());
    }

    /// <summary>
    /// Write generation summary.
    /// </summary>
    public void WriteGenerationSummary(int generation, GenerationSummary summary)
    {
        var path = Path.Combine(_outputDir, $"gen_{generation:D4}_summary.json");
        File.WriteAllText(path, JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _eventLog.Dispose();
    }
}

/// <summary>
/// Summary of a generation for logging.
/// </summary>
public sealed record GenerationSummary(
    int Generation,
    int PopulationSize,
    int SpeciesCount,
    float BestFitness,
    float MeanFitness,
    float WorstFitness,
    Guid BestGenomeId
);

/// <summary>
/// Null observatory for testing or when logging is disabled.
/// </summary>
public sealed class NullObservatory : IObservatory
{
    public static readonly NullObservatory Instance = new();

    public void OnEvent(in ObsEvent e) { }
    public void Flush() { }
}


