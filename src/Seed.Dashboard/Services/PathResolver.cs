using System.IO;
using System.Text.Json;

namespace Seed.Dashboard.Services;

public static class PathResolver
{
    private static string? _projectRoot;

    public static string ProjectRoot => _projectRoot ??= FindProjectRoot();

    public static string Resolve(string relativePath) =>
        Path.GetFullPath(Path.Combine(ProjectRoot, relativePath));

    /// <summary>
    /// Parses all market-config*.json at the project root, extracts each
    /// OutputDirectory value, resolves it relative to the project root,
    /// and returns only directories that actually exist on disk.
    /// </summary>
    public static string[] DiscoverOutputDirs()
    {
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var cfgFile in Directory.GetFiles(ProjectRoot, "market-config*.json"))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(cfgFile));
                    if (doc.RootElement.TryGetProperty("outputDirectory", out var od))
                    {
                        var val = od.GetString();
                        if (string.IsNullOrWhiteSpace(val)) continue;
                        var resolved = Path.IsPathRooted(val) ? val : Resolve(val);
                        if (Directory.Exists(resolved))
                            dirs.Add(resolved);
                    }
                }
                catch { }
            }
        }
        catch { }
        return dirs.OrderBy(d => d).ToArray();
    }

    private static string FindProjectRoot()
    {
        foreach (var start in new[]
        {
            AppDomain.CurrentDomain.BaseDirectory,
            Environment.CurrentDirectory
        })
        {
            var dir = start;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "Seed.sln")))
                    return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }
        return Environment.CurrentDirectory;
    }
}
