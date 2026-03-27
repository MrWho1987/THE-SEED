using System.Text.Json;

namespace Seed.Market.Evaluation;

/// <summary>
/// Writes structured experiment metadata to JSON for reproducibility.
/// </summary>
public sealed class ExperimentTracker : IDisposable
{
    private readonly Dictionary<string, object> _metrics = new();
    private readonly string _outputPath;
    private readonly DateTimeOffset _started;
    private readonly string _experimentId;
    private bool _completed;

    public string ExperimentId => _experimentId;

    public ExperimentTracker(string outputDir, MarketConfig config, string mode)
    {
        _experimentId = Guid.NewGuid().ToString("N")[..12];
        _started = DateTimeOffset.UtcNow;

        var expDir = Path.Combine(outputDir, "experiments");
        Directory.CreateDirectory(expDir);
        _outputPath = Path.Combine(expDir, $"{_experimentId}.json");

        _metrics["experimentId"] = _experimentId;
        _metrics["mode"] = mode;
        _metrics["startedUtc"] = _started.ToString("O");
        _metrics["config"] = new
        {
            config.PopulationSize,
            config.Generations,
            config.TrainingWindowHours,
            config.ValidationWindowHours,
            config.EvalWindowHours,
            config.EvalWindowCount,
            config.InitialCapital,
            config.ShrinkageK,
            config.RunSeed
        };
    }

    public void RecordMetric(string key, object value)
    {
        _metrics[key] = value;
    }

    public void Complete()
    {
        if (_completed) return;
        _completed = true;
        _metrics["completedUtc"] = DateTimeOffset.UtcNow.ToString("O");
        _metrics["durationSeconds"] = (DateTimeOffset.UtcNow - _started).TotalSeconds;

        var json = JsonSerializer.Serialize(_metrics, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_outputPath, json);
    }

    public void Dispose()
    {
        if (!_completed) Complete();
    }
}
