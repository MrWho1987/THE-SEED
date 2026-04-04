using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seed.Market.Backtest;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DataSourceStatus { Success, Failed, Timeout, Empty, Cached }

public sealed record DataSourceEntry(
    string Name,
    DataSourceStatus Status,
    int RowCount,
    string? CacheFile = null,
    string? Error = null);

public sealed class EnrichmentReport
{
    public List<DataSourceEntry> Sources { get; } = new();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string DateRange { get; init; } = "";

    private static readonly JsonSerializerOptions ManifestJsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public void PrintSummary()
    {
        const int totalW = 58;
        string border = new('═', totalW);

        Console.WriteLine($"╔{border}╗");
        Console.WriteLine($"║  {"DATA ENRICHMENT REPORT".PadRight(totalW - 2)}║");
        Console.WriteLine($"╠{"".PadRight(28, '═')}╦{"".PadRight(10, '═')}╦{"".PadRight(totalW - 28 - 10 - 2, '═')}╣");
        Console.WriteLine($"║ {"Source",-26} ║ {"Status",-8} ║ {"Rows",-16} ║");
        Console.WriteLine($"╠{"".PadRight(28, '═')}╬{"".PadRight(10, '═')}╬{"".PadRight(totalW - 28 - 10 - 2, '═')}╣");

        foreach (var s in Sources)
        {
            string status = s.Status.ToString().ToUpperInvariant();
            string rows = s.RowCount > 0 ? s.RowCount.ToString("N0").PadLeft(8) : "       0";
            string detail = s.Error != null ? $" [{Truncate(s.Error, 8)}]" : "";
            Console.WriteLine($"║ {Truncate(s.Name, 26).PadRight(26)} ║ {status.PadRight(8)} ║ {(rows + detail).PadRight(totalW - 28 - 10 - 2 - 2)} ║");
        }

        int successCount = Sources.Count(s => s.Status is DataSourceStatus.Success or DataSourceStatus.Cached);
        Console.WriteLine($"╠{"".PadRight(28, '═')}╩{"".PadRight(10, '═')}╩{"".PadRight(totalW - 28 - 10 - 2, '═')}╣");
        string summary = $"Sources OK: {successCount}/{Sources.Count}  |  Range: {DateRange}";
        Console.WriteLine($"║ {summary.PadRight(totalW - 2)}║");
        Console.WriteLine($"╚{border}╝");
    }

    public void SaveManifest(string directory)
    {
        Directory.CreateDirectory(directory);
        var manifest = new ManifestDocument
        {
            Timestamp = Timestamp,
            DateRange = DateRange,
            Sources = Sources.ToList()
        };
        var json = JsonSerializer.Serialize(manifest, ManifestJsonOpts);
        File.WriteAllText(Path.Combine(directory, "data_manifest.json"), json);
    }

    public static EnrichmentReport? LoadManifest(string directory)
    {
        var path = Path.Combine(directory, "data_manifest.json");
        if (!File.Exists(path)) return null;
        var doc = JsonSerializer.Deserialize<ManifestDocument>(
            File.ReadAllText(path), ManifestJsonOpts);
        if (doc == null) return null;
        var report = new EnrichmentReport
        {
            Timestamp = doc.Timestamp,
            DateRange = doc.DateRange
        };
        report.Sources.AddRange(doc.Sources);
        return report;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";

    private sealed class ManifestDocument
    {
        public DateTimeOffset Timestamp { get; set; }
        public string DateRange { get; set; } = "";
        public List<DataSourceEntry> Sources { get; set; } = new();
    }
}
