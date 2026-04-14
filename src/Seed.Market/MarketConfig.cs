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

    // MaxLeverage: ceiling for the brain's leverage output. Brain output[5] is scaled to [1, MaxLeverage].
    // Default 1.0f disables leverage (backward-compat with v1 genomes that have no leverage output).
    // v2 phase configs will set this per-phase (1x → 1x → 2x → 3x → 3x) to curriculum-ramp leverage.
    public float MaxLeverage { get; init; } = 1.0f;

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
    public float OpenPositionPenalty { get; init; } = 0.05f;

    // ── Species Diversity ──
    public int TargetSpeciesMin { get; init; } = 10;
    public int TargetSpeciesMax { get; init; } = 50;
    public float CompatibilityAdjustRate { get; init; } = 0.1f;
    public int MinOffspringPerSpecies { get; init; } = 1;
    public int MinSpeciesSizeForElitism { get; init; } = 2;
    public int StagnationLimit { get; init; } = 25;
    public float MinStagnationImprovement { get; init; } = 0.005f;
    public float DiversityBonusScale { get; init; } = 0.02f;
    public int DiversityKNeighbors { get; init; } = 5;

    // ── Brain Development ──
    public int MaxBrainNodes { get; init; } = 200;
    public int MaxBrainEdges { get; init; } = 2000;

    // ── Data Feeds ──
    public string CandleInterval { get; init; } = "1h";
    public int SpotPollMs { get; init; } = 5000;
    public int FuturesPollMs { get; init; } = 15000;
    public int SentimentPollMs { get; init; } = 300_000;
    public int OnChainPollMs { get; init; } = 3_600_000;
    public int MacroPollMs { get; init; } = 3_600_000;

    // ── Protective Stop-Loss ──
    public decimal StopLossPct { get; init; } = 0.02m;

    // ── Computed Interval Helpers (not serialized) ──
    [JsonIgnore] public int BarDurationMinutes => CandleInterval switch
    {
        "1m" => 1, "3m" => 3, "5m" => 5, "15m" => 15, "30m" => 30,
        "1h" => 60, "2h" => 120, "4h" => 240, _ => 60
    };
    [JsonIgnore] public int BarsPerHour => 60 / BarDurationMinutes;
    [JsonIgnore] public long BarDurationMs => BarDurationMinutes * 60_000L;

    // ── API Keys (optional, never commit real keys) ──
    public string? CoinGeckoApiKey { get; init; }
    public string? CoinglassApiKey { get; init; }
    public string? BinanceApiKey { get; init; }
    public string? BinanceApiSecret { get; init; }

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
        var config = JsonSerializer.Deserialize<MarketConfig>(json, JsonOpts) ?? new MarketConfig();
        config.Validate();
        return config;
    }

    public void Validate()
    {
        float sum = FitnessSharpeWeight + FitnessSortinoWeight + FitnessReturnWeight
                  + FitnessDrawdownDurationWeight + FitnessCVaRWeight;
        if (MathF.Abs(sum - 1.0f) > 0.01f)
            throw new InvalidOperationException(
                $"Fitness weights must sum to 1.0, got {sum:F3} " +
                $"(Sharpe={FitnessSharpeWeight}, Sortino={FitnessSortinoWeight}, Return={FitnessReturnWeight}, " +
                $"DD={FitnessDrawdownDurationWeight}, CVaR={FitnessCVaRWeight})");

        if (BarsPerHour <= 0)
            throw new InvalidOperationException(
                $"BarsPerHour must be > 0 (derived from CandleInterval '{CandleInterval}'), got {BarsPerHour}");

        if (MaxLeverage < 1.0f || MaxLeverage > 10.0f)
            throw new InvalidOperationException(
                $"MaxLeverage must be in [1, 10], got {MaxLeverage}. " +
                $"Use 1.0 for no leverage; 3.0 recommended for leveraged training.");
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
