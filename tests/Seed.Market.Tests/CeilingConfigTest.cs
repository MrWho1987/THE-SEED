namespace Seed.Market.Tests;

/// <summary>
/// L4 — Pin the ceiling-test config (market-config.ceiling.json) so changes that break
/// validation are caught at test time, not at the start of a 22-day training run.
/// Loads the actual file from the repo root, validates per-waypoint sums, and asserts
/// the L0–L3 fixes are wired in.
/// </summary>
public class CeilingConfigTest
{
    private static MarketConfig LoadCeiling()
    {
        // Repo root: tests bin sits at tests/Seed.Market.Tests/bin/Debug/net8.0/...
        // Walk up to find market-config.ceiling.json.
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 10 && dir != null; i++)
        {
            string candidate = Path.Combine(dir, "market-config.ceiling.json");
            if (File.Exists(candidate)) return MarketConfig.Load(candidate);
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("market-config.ceiling.json not found walking up from " + AppContext.BaseDirectory);
    }

    [Fact]
    public void CeilingConfig_LoadsAndValidates()
    {
        var cfg = LoadCeiling();
        // Validate is called by Load; if we got here, the schedule's per-waypoint sums all
        // pass the 1.0 ± 0.01 check.
        Assert.Equal(6000, cfg.Generations);
        Assert.Equal(200, cfg.PopulationSize);
        Assert.Equal(5, cfg.WeightSchedule.Count);
        Assert.Equal(0, cfg.WeightSchedule[0].Gen);
        Assert.Equal(6000, cfg.WeightSchedule[^1].Gen);
    }

    [Fact]
    public void CeilingConfig_WaypointSums_AllEqualOne()
    {
        var cfg = LoadCeiling();
        for (int i = 0; i < cfg.WeightSchedule.Count; i++)
        {
            float sum = cfg.WeightSchedule[i].Sum();
            Assert.True(MathF.Abs(sum - 1.0f) <= 0.01f,
                $"Waypoint {i} (gen={cfg.WeightSchedule[i].Gen}) sum = {sum:F4} (must be 1.0 ± 0.01)");
        }
    }

    [Fact]
    public void CeilingConfig_OptsIntoAllL0L3Fixes()
    {
        var cfg = LoadCeiling();

        // S2 — tightened inactivity defaults
        Assert.Equal(-0.20f, cfg.InactivityPenalty, 5);
        Assert.Equal(5, cfg.MinTradesForActive);

        // S4 — speciation redesign
        Assert.Equal(20, cfg.TargetSpeciesMax);
        Assert.Equal(30.0f, cfg.CompatibilityThresholdMax, 5);
        Assert.Equal(0.02f, cfg.MinStagnationImprovement, 5);

        // S8 — stale-position penalty enabled
        Assert.Equal(50, cfg.StaleThresholdTicks);
        Assert.Equal(0.005f, cfg.StalePenaltyPerTick, 5);

        // S6 — auto-analyzer enabled
        Assert.True(cfg.AutoAnalyzeOnCheckpoint);

        // S3 — OverfitAction = AdvanceWindow
        Assert.Equal(OverfitAction.AdvanceWindow, cfg.OverfitAction);

        // S1 — full-pop walk-forward enabled
        Assert.Equal(50, cfg.WalkForwardFullPopGens);

        // S9 — deploy gate threshold
        Assert.Equal(0.01f, cfg.DeployOutputStdMin, 5);

        // B4/B5 — best-val protection + top-5 WF
        Assert.True(cfg.ProtectBestValInPop);
        Assert.Equal(5, cfg.WalkForwardTopN);
    }

    [Fact]
    public void CeilingConfig_AnnealingTransitions_HitTargetWeights()
    {
        var cfg = LoadCeiling();

        // gen 0: return-heavy curriculum start
        var w0 = cfg.GetWeightsAt(0);
        Assert.Equal(0.40f, w0.Return, 3);
        Assert.Equal(0.10f, w0.Sharpe, 3);
        Assert.Equal(0.0f, w0.Stability, 3);

        // gen 6000: stability + niching + dirFlip-penalty fully engaged
        var w6000 = cfg.GetWeightsAt(6000);
        Assert.Equal(0.05f, w6000.Stability, 3);
        Assert.Equal(0.05f, w6000.BehavioralDiversity, 3);
        Assert.Equal(0.04f, w6000.DirFlipDominance, 3);

        // Mid-run interpolation: at gen 750 (midpoint of segment 0..1500), Return interpolates
        // between 0.40 and 0.25 → 0.325.
        var w750 = cfg.GetWeightsAt(750);
        Assert.Equal(0.325f, w750.Return, 2);
    }
}
