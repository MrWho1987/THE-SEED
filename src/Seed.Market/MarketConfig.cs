using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seed.Market;

public sealed record MarketConfig
{
    // ── Capital & Fees ──
    public decimal InitialCapital { get; init; } = 10_000m;
    public decimal MakerFee { get; init; } = 0.0004m;
    public decimal TakerFee { get; init; } = 0.0006m;
    public decimal SlippageBps { get; init; } = 5m;  // basis points

    // ── Symbols ──
    public string[] Symbols { get; init; } = ["BTCUSDT", "ETHUSDT"];

    // ── Risk Management ──
    public decimal MaxPositionPct { get; init; } = 0.25m;
    public decimal MaxDailyLossPct { get; init; } = 0.05m;
    public decimal KillSwitchDrawdownPct { get; init; } = 0.15m;
    public int MaxConcurrentPositions { get; init; } = 3;

    // ── Evolution ──
    public int PopulationSize { get; init; } = 50;
    public int Generations { get; init; } = 100;
    public int TrainingWindowHours { get; init; } = 720; // 30 days
    public int ValidationWindowHours { get; init; } = 168; // 7 days
    public int RollingStepHours { get; init; } = 24;
    public ulong RunSeed { get; init; } = 42;

    // ── Brain Development ──
    public int MaxBrainNodes { get; init; } = 200;
    public int MaxBrainEdges { get; init; } = 2000;

    // ── Data Feeds ──
    public int SpotPollMs { get; init; } = 5000;
    public int FuturesPollMs { get; init; } = 15000;
    public int SentimentPollMs { get; init; } = 300_000;
    public int OnChainPollMs { get; init; } = 3_600_000;
    public int MacroPollMs { get; init; } = 3_600_000;

    // ── Execution Mode ──
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExecutionMode Mode { get; init; } = ExecutionMode.Backtest;
    public bool ConfirmLive { get; init; } = false;

    // ── Output ──
    public string OutputDirectory { get; init; } = "output_market";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
    };

    public static MarketConfig Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MarketConfig>(json, JsonOpts) ?? new MarketConfig();
    }

    public void Save(string path)
    {
        var json = JsonSerializer.Serialize(this, JsonOpts);
        File.WriteAllText(path, json);
    }

    public static MarketConfig Default => new();
}

public enum ExecutionMode
{
    Backtest,
    Paper,
    Live
}
