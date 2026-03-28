using System.IO;
using System.Text.Json;

namespace Seed.Dashboard.Services;

public record GenomeFileInfo(
    string FullPath, string FileName, string Directory, string Category,
    DateTime Modified, long SizeBytes);

public class GenomeService
{
    public List<GenomeFileInfo> ScanGenomes(params string[] outputDirs)
    {
        var results = new List<GenomeFileInfo>();
        foreach (var dir in outputDirs)
        {
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.GetFiles(dir, "*.json"))
            {
                var fi = new FileInfo(file);
                if (fi.Name.StartsWith("market-config")) continue;
                results.Add(new GenomeFileInfo(
                    fi.FullName, fi.Name, dir, "Best",
                    fi.LastWriteTime, fi.Length));
            }

            var cpDir = Path.Combine(dir, "checkpoints");
            if (Directory.Exists(cpDir))
            {
                foreach (var file in Directory.GetFiles(cpDir, "*.json"))
                {
                    var fi = new FileInfo(file);
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
