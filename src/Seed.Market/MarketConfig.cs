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
    public decimal MaxDailyVaRPct { get; init; } = 0.05m;
    public decimal MaxEquityMultiplier { get; init; } = 100m;

    // ── Evolution ──
    public int PopulationSize { get; init; } = 50;
    public int Generations { get; init; } = 100;
    public int TrainingWindowHours { get; init; } = 720;
    public int ValidationWindowHours { get; init; } = 168;
    public int EvalWindowHours { get; init; } = 500;
    public int EvalWindowCount { get; init; } = 3;
    public int RollingStepHours { get; init; } = 24;
    public ulong RunSeed { get; init; } = 42;

    // ── Fitness ──
    public float ShrinkageK { get; init; } = 10f;
    public float FitnessSharpeWeight { get; init; } = 0.45f;
    public float FitnessSortinoWeight { get; init; } = 0.15f;
    public float FitnessReturnWeight { get; init; } = 0.20f;
    public float FitnessDrawdownDurationWeight { get; init; } = 0.10f;
    public float FitnessCVaRWeight { get; init; } = 0.10f;
    public float InactivityPenalty { get; init; } = -0.1f;
    public int MinTradesForActive { get; init; } = 3;
    public float WindowConsistencyWeight { get; init; } = 0f;
    public float ActivityBonusScale { get; init; } = 0f;
    public float RatioClampMax { get; init; } = 10f;
    public float ReturnFloor { get; init; } = -0.50f;

    // ── Species Diversity ──
    public int TargetSpeciesMin { get; init; } = 10;
    public int TargetSpeciesMax { get; init; } = 50;
    public float CompatibilityAdjustRate { get; init; } = 0.1f;
    public int MinOffspringPerSpecies { get; init; } = 1;
    public int MinSpeciesSizeForElitism { get; init; } = 2;
    public int StagnationLimit { get; init; } = 25;
    public float DiversityBonusScale { get; init; } = 0.02f;
    public int DiversityKNeighbors { get; init; } = 5;

    // ── Brain Development ──
    public int MaxBrainNodes { get; init; } = 200;
    public int MaxBrainEdges { get; init; } = 2000;

    // ── Data Feeds ──
    public int SpotPollMs { get; init; } = 5000;
    public int FuturesPollMs { get; init; } = 15000;
    public int SentimentPollMs { get; init; } = 300_000;
    public int OnChainPollMs { get; init; } = 3_600_000;
    public int MacroPollMs { get; init; } = 3_600_000;

    // ── API Keys (optional) ──
    public string? CoinGeckoApiKey { get; init; }

    // ── Execution Mode ──
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExecutionMode Mode { get; init; } = ExecutionMode.Backtest;
    public bool ConfirmLive { get; init; } = false;

    // ── Output ──
    public string OutputDirectory { get; init; } = "output_market";
    public string? DataCacheDirectory { get; init; }

    // ── Validation ──
    public int ValidationIntervalGens { get; init; } = 10;
    public int EarlyStopPatience { get; init; } = 5;
    public bool EarlyStopEnabled { get; init; } = false;
    public bool WalkForwardEnabled { get; init; } = true;
    public float WalkForwardMinValFitness { get; init; } = -0.05f;
    public int WalkForwardMaxStallGens { get; init; } = 50;
    public int WindowStabilityGens { get; init; } = 1;

    // ── Paper Trading ──
    public string? GenomePath { get; init; }
    public string? TradeLogPath { get; init; }
    public int DisplayIntervalMs { get; init; } = 10_000;
    public int CheckpointIntervalGens { get; init; } = 10;

    public string ResolvedGenomePath =>
        GenomePath ?? Path.Combine(OutputDirectory, "best_market_genome.json");
    public string ResolvedTradeLogPath =>
        TradeLogPath ?? Path.Combine(OutputDirectory, "trades.jsonl");

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
    Live,
    Compare,
    Ablation,
    StressTest,
    MonteCarlo,
    Ensemble,
    NeuroAblation
}
