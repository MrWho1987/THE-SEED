using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Seed.Market;

namespace Seed.Dashboard.Services;

public sealed record AnalysisResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    string? ExperimentJsonPath,
    Dictionary<string, JsonElement>? Metrics);

public sealed class AnalysisService
{
    private const string BuildDirName = "build_analysis";
    private const string ProjectRelPath = "src/Seed.Market.App";

    public async Task<AnalysisResult> RunAnalysisAsync(
        MarketConfig baseConfig,
        string genomePath,
        ExecutionMode mode,
        CancellationToken ct = default)
    {
        var root = PathResolver.ProjectRoot;
        var buildDir = Path.GetFullPath(Path.Combine(root, BuildDirName));
        var projectDir = Path.GetFullPath(Path.Combine(root, ProjectRelPath));

        var absGenomePath = Path.GetFullPath(genomePath);
        var absOutputDir = Path.IsPathRooted(baseConfig.OutputDirectory)
            ? baseConfig.OutputDirectory
            : Path.GetFullPath(Path.Combine(root, baseConfig.OutputDirectory));

        var analysisConfig = baseConfig with
        {
            Mode = mode,
            GenomePath = absGenomePath,
            OutputDirectory = absOutputDir
        };

        var tempConfigPath = Path.Combine(Path.GetTempPath(), $"seed_analysis_{Guid.NewGuid():N}.json");
        analysisConfig.Save(tempConfigPath);

        var runStart = DateTime.UtcNow;

        try
        {
            await EnsureBuild(projectDir, buildDir, ct);

            var dllPath = Path.Combine(buildDir, "Seed.Market.App.dll");
            if (!File.Exists(dllPath))
                throw new FileNotFoundException(
                    "Analysis engine DLL not found after build. Delete build_analysis/ and retry.", dllPath);

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{dllPath}\" \"{tempConfigPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = root
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            try
            {
                await proc.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(entireProcessTree: true); } catch { }
                throw;
            }

            string? expJsonPath = null;
            Dictionary<string, JsonElement>? metrics = null;

            var expDir = Path.Combine(absOutputDir, "experiments");
            if (Directory.Exists(expDir))
            {
                var newest = Directory.GetFiles(expDir, "*.json")
                    .Select(f => new FileInfo(f))
                    .Where(f => f.LastWriteTimeUtc >= runStart.AddSeconds(-2))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .FirstOrDefault();

                if (newest != null)
                {
                    expJsonPath = newest.FullName;
                    var json = await File.ReadAllTextAsync(newest.FullName, ct);
                    metrics = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                }
            }

            return new AnalysisResult(proc.ExitCode, stdout.ToString(), stderr.ToString(), expJsonPath, metrics);
        }
        finally
        {
            try { File.Delete(tempConfigPath); } catch { }
        }
    }

    private static async Task EnsureBuild(string projectDir, string buildDir, CancellationToken ct)
    {
        var dllPath = Path.Combine(buildDir, "Seed.Market.App.dll");
        if (File.Exists(dllPath))
            return;

        Directory.CreateDirectory(buildDir);

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"build \"{projectDir}\" -o \"{buildDir}\" --verbosity quiet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet build");
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException($"Build failed (exit {proc.ExitCode}):\n{err}");
        }
    }
}
