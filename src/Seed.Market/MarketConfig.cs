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

    // S8 — Stale-position penalty. When > 0 and an open position's age (ticks since open)
    // exceeds StaleThresholdTicks, MarketAgent.ComputeReward subtracts
    // StalePenaltyPerTick × (age − threshold) per tick. Linear ramp, no clamp. Defaults are
    // 0 (disabled) for legacy compatibility; ceiling-test config sets non-zero values to
    // pressure population away from holding stale positions and toward learning the
    // explicit-exit output.
    public int StaleThresholdTicks { get; init; } = 0;
    public float StalePenaltyPerTick { get; init; } = 0f;

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

    // T1 — Continuous fitness annealing replaces the legacy 9 scalar fitness weight fields.
    // Multi-phase pipeline is gone; selection criteria now interpolate smoothly across
    // generations via this schedule instead of jumping at phase boundaries. Per-waypoint
    // 9-weight sum must equal 1.0 (validated). At least 2 waypoints required, first at gen=0,
    // sorted ascending. <see cref="GetWeightsAt"/> linearly interpolates between waypoints.
    //
    // T3/T4/L3-S5 will extend WeightWaypoint with BehavioralDiversity, DirFlipDominance, and
    // Stability columns respectively, raising the per-waypoint sum to 12 weights.
    public List<WeightWaypoint> WeightSchedule { get; init; } = DefaultSchedule();

    private static List<WeightWaypoint> DefaultSchedule() =>
    [
        // Default schedule: constant weights from gen 0 to 1_000_000. Interpolation reduces
        // to a no-op (both endpoints identical) so behavior matches the legacy scalar defaults
        // for any caller that doesn't override the schedule.
        new WeightWaypoint(Gen: 0,
            Sharpe: 0.22f, Sortino: 0.13f, Return: 0.20f, DrawdownDuration: 0.13f,
            CVaR: 0.17f, Calmar: 0.05f, InfoRatio: 0.05f, FeeDrag: 0.03f,
            Diversification: 0.02f),
        new WeightWaypoint(Gen: 1_000_000,
            Sharpe: 0.22f, Sortino: 0.13f, Return: 0.20f, DrawdownDuration: 0.13f,
            CVaR: 0.17f, Calmar: 0.05f, InfoRatio: 0.05f, FeeDrag: 0.03f,
            Diversification: 0.02f),
    ];

    /// <summary>
    /// Linearly interpolates the fitness weights at a given generation. Clamps to first
    /// and last waypoints outside the schedule range. Bit-equivalent to a linear segment
    /// lookup; pure function (no state).
    /// </summary>
    public WeightWaypoint GetWeightsAt(int generation)
    {
        var s = WeightSchedule;
        if (s.Count == 0)
            throw new InvalidOperationException("WeightSchedule is empty");
        if (generation <= s[0].Gen) return s[0];
        if (generation >= s[^1].Gen) return s[^1];
        for (int i = 0; i < s.Count - 1; i++)
        {
            if (generation >= s[i].Gen && generation <= s[i + 1].Gen)
            {
                int span = s[i + 1].Gen - s[i].Gen;
                if (span <= 0) return s[i + 1];
                float t = (float)(generation - s[i].Gen) / span;
                return WeightWaypoint.Lerp(s[i], s[i + 1], t, generation);
            }
        }
        return s[^1];  // unreachable given the bounds above, but keeps the compiler happy
    }
    // S2 Phase A — tightened defaults to push selection pressure off the inactive plateau.
    // Phase 4 minimal observed ~60% of the population scoring exactly at the inactivity
    // penalty for the entire run; doubling the penalty + raising the active-threshold to 5
    // makes inactivity strictly worse than even a single losing active trade for many
    // genomes, which gives selection a gradient to work with. Old defaults preserved as
    // explicit values in any config that needs the legacy behavior.
    public float InactivityPenalty { get; init; } = -0.20f;
    public int MinTradesForActive { get; init; } = 5;
    public float WindowConsistencyWeight { get; init; } = 0f;
    public float ActivityBonusScale { get; init; } = 0f;
    public float RatioClampMax { get; init; } = 10f;
    public float ReturnFloor { get; init; } = -0.50f;
    public float OpenPositionPenalty { get; init; } = 0.05f;

    // ── Species Diversity ──
    public int TargetSpeciesMin { get; init; } = 10;
    // S4 — TargetSpeciesMax tightened 50 → 20 to favor a smaller, deeper-explored set of
    // species over a sprawling shallow population. Phase 4 minimal saw 30+ species none of
    // which had time to mature; aiming for ~20 gives each species more breeding budget.
    public int TargetSpeciesMax { get; init; } = 20;
    public float CompatibilityAdjustRate { get; init; } = 0.1f;
    // S4 — Replaces the hard-coded `Math.Min(10.0f, ...)` upper bound on the compatibility
    // threshold. With a fast-mutating population the threshold pinned at 10 within ~50 gens
    // and lost its ability to compress oversized species. The new soft cap (30.0) plus the
    // adaptive halving above 70% of max lets the controller settle smoothly.
    public float CompatibilityThresholdMax { get; init; } = 30.0f;
    public int MinOffspringPerSpecies { get; init; } = 1;
    public int MinSpeciesSizeForElitism { get; init; } = 2;
    public int StagnationLimit { get; init; } = 25;
    // S4 — MinStagnationImprovement raised 0.005 → 0.02 so floating-point drift in overfit
    // champions can no longer indefinitely reset the stagnation counter. Only meaningful
    // improvements (≥ 0.02 fitness units) count.
    public float MinStagnationImprovement { get; init; } = 0.02f;
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

    // S9 — Deploy gate: minimum stddev of the V11 action outputs (indices 5..10:
    // leverage/partialClose/trailEnable/trailDist/tpOffset/slOverride) across the eval window.
    // A genome with one or more dead V11 outputs (stddev < this) is flagged DEPLOY-BLOCKED by
    // the analyzer — its action layer never explored, so live behavior may be undefined under
    // regimes the training window didn't expose. Advisory by default; strict filtering is
    // CLI-flag-gated in the analyzer.
    public float DeployOutputStdMin { get; init; } = 0.01f;

    // S6 — When true, every checkpoint save fires a non-blocking `dotnet run --project
    // tools/Seed.CheckpointEval` subprocess that evaluates that checkpoint against the
    // validation window and writes analyzer outputs to AutoAnalyzeOutputDir/checkpoint_NNNN/.
    // A lockfile in the checkpoint dir prevents stacking subprocesses if checkpoints fire
    // faster than the analyzer completes. Default off — opt-in for ceiling-test runs only.
    public bool AutoAnalyzeOnCheckpoint { get; init; } = false;

    // S6 — Output root for the auto-analyzer subprocess. Per-checkpoint subdirectories named
    // `analysis_NNNN/` are written here. Defaults to `<OutputDirectory>/auto_analyses/` when null.
    public string? AutoAnalyzeOutputDir { get; init; }

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
        // T1 — WeightSchedule invariants:
        //   1. ≥ 2 waypoints (interpolation needs both endpoints)
        //   2. First waypoint Gen must be 0 (interpolator clamps to first below this)
        //   3. Waypoints sorted strictly ascending by Gen (no duplicate generations)
        //   4. Each waypoint's 9 weights sum to 1.0 ± 0.01
        if (WeightSchedule == null || WeightSchedule.Count < 2)
            throw new InvalidOperationException(
                $"WeightSchedule must have ≥ 2 waypoints (got {WeightSchedule?.Count ?? 0}). T1 hard cutover: scalar weights are removed.");
        if (WeightSchedule[0].Gen != 0)
            throw new InvalidOperationException(
                $"WeightSchedule[0].Gen must be 0 (got {WeightSchedule[0].Gen}). The first waypoint anchors the schedule.");
        for (int i = 1; i < WeightSchedule.Count; i++)
        {
            if (WeightSchedule[i].Gen <= WeightSchedule[i - 1].Gen)
                throw new InvalidOperationException(
                    $"WeightSchedule waypoints must be strictly ascending by Gen. " +
                    $"Index {i - 1} has Gen={WeightSchedule[i - 1].Gen}, index {i} has Gen={WeightSchedule[i].Gen}.");
        }
        foreach (var wp in WeightSchedule)
        {
            float sum = wp.Sum();
            if (MathF.Abs(sum - 1.0f) > 0.01f)
                throw new InvalidOperationException(
                    $"WeightSchedule waypoint at Gen={wp.Gen}: 9 weights sum to {sum:F4}, must equal 1.0 ± 0.01.");
        }

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

/// <summary>
/// T1 — A single anchor in the fitness <see cref="MarketConfig.WeightSchedule"/>. Replaces
/// the legacy 9 scalar fitness weight fields. <see cref="MarketConfig.GetWeightsAt"/>
/// linearly interpolates between consecutive waypoints by generation; the per-waypoint
/// 9-weight sum is validated to equal 1.0 ± 0.01.
///
/// Future extensions (per plan):
///   T3 adds <c>BehavioralDiversity</c> column → 10-weight sum.
///   T4 adds <c>DirFlipDominance</c> column → 11-weight sum.
///   L3-S5 adds <c>Stability</c> column → 12-weight sum.
/// </summary>
public sealed record WeightWaypoint(
    int Gen,
    float Sharpe = 0f,
    float Sortino = 0f,
    float Return = 0f,
    float DrawdownDuration = 0f,
    float CVaR = 0f,
    float Calmar = 0f,
    float InfoRatio = 0f,
    float FeeDrag = 0f,
    float Diversification = 0f)
{
    /// <summary>Sum of all 9 weights at this waypoint. Validated to equal 1.0 ± 0.01.</summary>
    public float Sum() =>
        Sharpe + Sortino + Return + DrawdownDuration + CVaR
        + Calmar + InfoRatio + FeeDrag + Diversification;

    /// <summary>
    /// Linear interpolation between two waypoints. <paramref name="t"/> ∈ [0, 1] selects
    /// <paramref name="a"/> at 0 and <paramref name="b"/> at 1. Pure function.
    /// </summary>
    public static WeightWaypoint Lerp(WeightWaypoint a, WeightWaypoint b, float t, int gen) => new(
        Gen: gen,
        Sharpe:           a.Sharpe           + t * (b.Sharpe           - a.Sharpe),
        Sortino:          a.Sortino          + t * (b.Sortino          - a.Sortino),
        Return:           a.Return           + t * (b.Return           - a.Return),
        DrawdownDuration: a.DrawdownDuration + t * (b.DrawdownDuration - a.DrawdownDuration),
        CVaR:             a.CVaR             + t * (b.CVaR             - a.CVaR),
        Calmar:           a.Calmar           + t * (b.Calmar           - a.Calmar),
        InfoRatio:        a.InfoRatio        + t * (b.InfoRatio        - a.InfoRatio),
        FeeDrag:          a.FeeDrag          + t * (b.FeeDrag          - a.FeeDrag),
        Diversification:  a.Diversification  + t * (b.Diversification  - a.Diversification));

    /// <summary>
    /// Builds a 2-waypoint <see cref="MarketConfig.WeightSchedule"/> with constant weights
    /// across all generations. Test/code helper for the common "no annealing, just fixed
    /// weights" case. Validation still requires the 9 weights to sum to 1.0 ± 0.01.
    /// </summary>
    public static List<WeightWaypoint> ConstantSchedule(
        float sharpe, float sortino, float returnWeight, float ddDuration, float cvar,
        float calmar, float infoRatio, float feeDrag, float diversification) =>
    [
        new WeightWaypoint(Gen: 0,
            Sharpe: sharpe, Sortino: sortino, Return: returnWeight, DrawdownDuration: ddDuration,
            CVaR: cvar, Calmar: calmar, InfoRatio: infoRatio, FeeDrag: feeDrag,
            Diversification: diversification),
        new WeightWaypoint(Gen: 1_000_000,
            Sharpe: sharpe, Sortino: sortino, Return: returnWeight, DrawdownDuration: ddDuration,
            CVaR: cvar, Calmar: calmar, InfoRatio: infoRatio, FeeDrag: feeDrag,
            Diversification: diversification),
    ];
}
