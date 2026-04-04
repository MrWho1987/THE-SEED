using Seed.Market.Backtest;

namespace Seed.Market.Tests;

public class DataPipelineTests : IDisposable
{
    private readonly string _tempDir;

    public DataPipelineTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "seed_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void EnrichmentReport_SaveAndLoad_RoundTrips()
    {
        var report = new EnrichmentReport
        {
            Timestamp = new DateTimeOffset(2025, 6, 1, 12, 0, 0, TimeSpan.Zero),
            DateRange = "2018-01-01 to 2025-06-01"
        };
        report.Sources.Add(new DataSourceEntry("ETH + Multi-Asset", DataSourceStatus.Success, 8760));
        report.Sources.Add(new DataSourceEntry("Macro (Yahoo)", DataSourceStatus.Failed, 0, Error: "HTTP 429"));
        report.Sources.Add(new DataSourceEntry("Derivatives", DataSourceStatus.Timeout, 0, Error: "60s timeout"));
        report.Sources.Add(new DataSourceEntry("Funding Rates", DataSourceStatus.Cached, 4380));

        report.SaveManifest(_tempDir);

        var loaded = EnrichmentReport.LoadManifest(_tempDir);
        Assert.NotNull(loaded);
        Assert.Equal(report.Sources.Count, loaded.Sources.Count);
        Assert.Equal(report.DateRange, loaded.DateRange);

        Assert.Equal("ETH + Multi-Asset", loaded.Sources[0].Name);
        Assert.Equal(DataSourceStatus.Success, loaded.Sources[0].Status);
        Assert.Equal(8760, loaded.Sources[0].RowCount);

        Assert.Equal("Macro (Yahoo)", loaded.Sources[1].Name);
        Assert.Equal(DataSourceStatus.Failed, loaded.Sources[1].Status);
        Assert.Equal("HTTP 429", loaded.Sources[1].Error);

        Assert.Equal(DataSourceStatus.Timeout, loaded.Sources[2].Status);
        Assert.Equal(DataSourceStatus.Cached, loaded.Sources[3].Status);
        Assert.Equal(4380, loaded.Sources[3].RowCount);
    }

    [Fact]
    public void EnrichmentReport_PrintSummary_WritesToConsole()
    {
        var report = new EnrichmentReport
        {
            DateRange = "2020-01-01 to 2025-01-01"
        };
        report.Sources.Add(new DataSourceEntry("ETH + Multi-Asset", DataSourceStatus.Success, 8760));
        report.Sources.Add(new DataSourceEntry("On-Chain", DataSourceStatus.Failed, 0, Error: "timeout"));

        var sw = new StringWriter();
        Console.SetOut(sw);
        try
        {
            report.PrintSummary();
        }
        finally
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
        }

        string output = sw.ToString();
        Assert.Contains("DATA ENRICHMENT REPORT", output);
        Assert.Contains("ETH + Multi-Asset", output);
        Assert.Contains("SUCCESS", output);
        Assert.Contains("On-Chain", output);
        Assert.Contains("FAILED", output);
        Assert.Contains("Sources OK: 1/2", output);
    }

    [Fact]
    public void EnrichmentReport_LoadManifest_ReturnsNull_WhenNotExists()
    {
        var result = EnrichmentReport.LoadManifest(_tempDir);
        Assert.Null(result);
    }

    [Fact]
    public void LoadTimeseriesCache_EmptyFile_ReturnsNullAndDeletes()
    {
        var path = Path.Combine(_tempDir, "empty.jsonl");
        File.WriteAllText(path, "");
        Assert.True(File.Exists(path));

        var result = HistoricalSignalEnricher.LoadTimeseriesCache(path);

        Assert.Null(result);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LoadTimeseriesCache_WhitespaceOnly_ReturnsNullAndDeletes()
    {
        var path = Path.Combine(_tempDir, "whitespace.jsonl");
        File.WriteAllText(path, "  \n\n  \n");
        Assert.True(File.Exists(path));

        var result = HistoricalSignalEnricher.LoadTimeseriesCache(path);

        Assert.Null(result);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LoadTimeseriesCache_ValidFile_ReturnsData()
    {
        var path = Path.Combine(_tempDir, "valid.jsonl");
        File.WriteAllLines(path, new[]
        {
            "1609459200000,100.5",
            "1609462800000,101.2",
            "1609466400000,99.8"
        });

        var result = HistoricalSignalEnricher.LoadTimeseriesCache(path);

        Assert.NotNull(result);
        Assert.Equal(3, result.Count);
        Assert.Equal(1609459200000L, result[0].UnixMs);
        Assert.Equal(100.5f, result[0].Value, 0.01f);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveTimeseriesCache_EmptyData_DoesNotCreateFile()
    {
        var path = Path.Combine(_tempDir, "should_not_exist.jsonl");
        var emptyData = new List<(long UnixMs, float Value)>();

        HistoricalSignalEnricher.SaveTimeseriesCache(path, emptyData);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void SaveTimeseriesCache_NonEmptyData_CreatesFile()
    {
        var path = Path.Combine(_tempDir, "should_exist.jsonl");
        var data = new List<(long UnixMs, float Value)>
        {
            (1609459200000L, 50000f),
            (1609462800000L, 50100f)
        };

        HistoricalSignalEnricher.SaveTimeseriesCache(path, data);

        Assert.True(File.Exists(path));
        var loaded = HistoricalSignalEnricher.LoadTimeseriesCache(path);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Count);
    }

    [Fact]
    public void LoadCandleCache_EmptyFile_ReturnsNullAndDeletes()
    {
        var path = Path.Combine(_tempDir, "empty_candles.jsonl");
        File.WriteAllText(path, "\n\n");

        var result = HistoricalSignalEnricher.LoadCandleCache(path);

        Assert.Null(result);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void BacktestRunner_UsesDataCacheDirectory_WhenSet()
    {
        var customCacheDir = Path.Combine(_tempDir, "custom_cache");
        var config = MarketConfig.Default with
        {
            OutputDirectory = Path.Combine(_tempDir, "output"),
            DataCacheDirectory = customCacheDir
        };

        var runner = new BacktestRunner(config);

        Assert.Equal(customCacheDir, runner.CacheDir);
        Assert.True(Directory.Exists(customCacheDir));
    }

    [Fact]
    public void BacktestRunner_DefaultsToOutputDir_WhenDataCacheDirNull()
    {
        var outputDir = Path.Combine(_tempDir, "output_default");
        var config = MarketConfig.Default with
        {
            OutputDirectory = outputDir
        };

        var runner = new BacktestRunner(config);

        Assert.Equal(Path.Combine(outputDir, "data_cache"), runner.CacheDir);
    }
}
