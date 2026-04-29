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

    // MaxLeverage: ceiling for the brain's leverage output. Brain output[5] is log-scaled to
    // [1, MaxLeverage] via ActionInterpreter. Default 1.0f disables leverage entirely (the
    // brain's leverage output has no effect). Production training uses 125 to match Binance
    // BTCUSDT retail maximum. The brain learns to calibrate leverage vs position size.
    public float MaxLeverage { get; init; } = 1.0f;

    // ExplicitExitBonus: reward bonus applied when a trade closes via the brain's explicit
    // exit output (output[3]) vs direction-flip / stop-loss / kill-switch. Conservative default
    // is 0.02 (2% of the ±1 reward range). Tips evolution toward developing connectivity to
    // output[3] without warping overall reward signal. Configurable so we can increase if
    // explicit exits fail to emerge.
    public float ExplicitExitBonus { get; init; } = 0.02f;

    // PeakExitBonus: reward bonus scaled by captureRatio = realizedPct / peakUnrealizedPct
    // when a profitable position is closed. Encourages exiting winners near their peak
    // instead of giving back gains. Default 0.1 (10% of the ±1 reward range).
    public float PeakExitBonus { get; init; } = 0.1f;

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
    public float FitnessSharpeWeight { get; init; } = 0.22f;
    public float FitnessSortinoWeight { get; init; } = 0.13f;
    public float FitnessReturnWeight { get; init; } = 0.20f;
    public float FitnessDrawdownDurationWeight { get; init; } = 0.13f;
    public float FitnessCVaRWeight { get; init; } = 0.17f;
    public float FitnessCalmarWeight { get; init; } = 0.05f;
    public float FitnessInfoRatioWeight { get; init; } = 0.05f;
    public float FitnessFeeDragWeight { get; init; } = 0.03f;
    public float FitnessDiversificationWeight { get; init; } = 0.02f;
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

    // B4 — When true and a new best-val is found, inject a copy into the population
    // (replacing the lowest training-fitness member) to protect it from evolutionary loss.
    // Default false for backward compatibility; enable in Phase 4 config only.
    public bool ProtectBestValInPop { get; init; } = false;

    // B5 — Number of top-by-training-fitness genomes to evaluate against validation at each
    // walk-forward check. The maximum ValFit across the N decides pass/fail. Default 1 (old
    // behavior). Setting to 5 gives the population a much broader chance to show generalization.
    public int WalkForwardTopN { get; init; } = 1;

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
                  + FitnessDrawdownDurationWeight + FitnessCVaRWeight
                  + FitnessCalmarWeight + FitnessInfoRatioWeight
                  + FitnessFeeDragWeight + FitnessDiversificationWeight;
        if (MathF.Abs(sum - 1.0f) > 0.01f)
            throw new InvalidOperationException(
                $"Fitness weights must sum to 1.0 across 9 components, got {sum:F3} " +
                $"(Sharpe={FitnessSharpeWeight}, Sortino={FitnessSortinoWeight}, Return={FitnessReturnWeight}, " +
                $"DD={FitnessDrawdownDurationWeight}, CVaR={FitnessCVaRWeight}, Calmar={FitnessCalmarWeight}, " +
                $"InfoRatio={FitnessInfoRatioWeight}, FeeDrag={FitnessFeeDragWeight}, Diversification={FitnessDiversificationWeight})");

        if (BarsPerHour <= 0)
            throw new InvalidOperationException(
                $"BarsPerHour must be > 0 (derived from CandleInterval '{CandleInterval}'), got {BarsPerHour}");

        if (MaxLeverage < 1.0f || MaxLeverage > 125.0f)
            throw new InvalidOperationException(
                $"MaxLeverage must be in [1, 125] (Binance BTCUSDT retail max), got {MaxLeverage}. " +
                $"Use 1.0 for no leverage; 125.0 for full Binance range.");

        if (ExplicitExitBonus < 0f || ExplicitExitBonus > 0.5f)
            throw new InvalidOperationException(
                $"ExplicitExitBonus must be in [0, 0.5], got {ExplicitExitBonus}. " +
                $"Recommended default: 0.02. Above 0.5 risks warping training reward.");

        if (PeakExitBonus < 0f || PeakExitBonus > 0.5f)
            throw new InvalidOperationException(
                $"PeakExitBonus must be in [0, 0.5], got {PeakExitBonus}. " +
                $"Recommended default: 0.1. Above 0.5 risks warping training reward.");
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
