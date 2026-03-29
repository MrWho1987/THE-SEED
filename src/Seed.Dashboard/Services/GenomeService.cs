using System.IO;
using System.Text.Json;

namespace Seed.Dashboard.Services;

public record GenomeFileInfo(
    string FullPath, string FileName, string Directory, string Category,
    DateTime Modified, long SizeBytes, float? Fitness = null);

public class GenomeService
{
    private static bool IsGenomeFile(string name) =>
        name.StartsWith("best_market", StringComparison.Ordinal) ||
        name.StartsWith("best_training", StringComparison.Ordinal);

    private static bool IsCheckpointFile(string name) =>
        name.StartsWith("checkpoint_", StringComparison.Ordinal) ||
        name.StartsWith("best_gen_", StringComparison.Ordinal) ||
        name.StartsWith("best_val_", StringComparison.Ordinal);

    public List<GenomeFileInfo> ScanGenomes(params string[] outputDirs)
    {
        var results = new List<GenomeFileInfo>();
        foreach (var dir in outputDirs)
        {
            if (!Directory.Exists(dir)) continue;

            float? dirFitness = ReadScoresFitness(dir);

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var fi = new FileInfo(file);
                if (!IsGenomeFile(fi.Name)) continue;
                float? fitness = dirFitness;
                results.Add(new GenomeFileInfo(
                    fi.FullName, fi.Name, dir, "Best",
                    fi.LastWriteTime, fi.Length, fitness));
            }

            var cpDir = Path.Combine(dir, "checkpoints");
            if (Directory.Exists(cpDir))
            {
                foreach (var file in Directory.GetFiles(cpDir, "*.json"))
                {
                    var fi = new FileInfo(file);
                    if (!IsCheckpointFile(fi.Name)) continue;
                    string category = fi.Name.StartsWith("checkpoint") ? "Checkpoint"
                        : fi.Name.StartsWith("best_val") ? "Best Validation"
                        : fi.Name.StartsWith("best_gen") ? "Best Generation"
                        : "Other";
                    results.Add(new GenomeFileInfo(
                        fi.FullName, fi.Name, dir, category,
                        fi.LastWriteTime, fi.Length));
                }
            }
        }
        return results.OrderByDescending(g => g.Modified).ToList();
    }

    private static float? ReadScoresFitness(string dir)
    {
        var scoresPath = Path.Combine(dir, "genome_scores.json");
        if (!File.Exists(scoresPath)) return null;
        try
        {
            var doc = JsonDocument.Parse(File.ReadAllText(scoresPath));
            if (doc.RootElement.TryGetProperty("bestFitness", out var val))
                return val.GetSingle();
        }
        catch { }
        return null;
    }

    public List<string> GetGenomeDropdownItems(params string[] outputDirs)
    {
        var items = new List<string>();
        foreach (var dir in outputDirs)
        {
            var bestMarket = Path.Combine(dir, "best_market_genome.json");
            if (File.Exists(bestMarket)) items.Add(bestMarket);
            var bestTraining = Path.Combine(dir, "best_training_genome.json");
            if (File.Exists(bestTraining)) items.Add(bestTraining);
        }
        return items;
    }
}
