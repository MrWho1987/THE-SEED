using Seed.Market.Agents;

namespace Seed.Market.Tests;

/// <summary>
/// S8 — Stale-position penalty: linear ramp subtracted from per-tick reward once an
/// open position's age exceeds <c>StaleThresholdTicks</c>. Pressures the population
/// toward learning explicit-exit signals (output[3] + outputs 6..10) instead of relying
/// on direction-flip closures, which Phase 4 minimal observed got pop[149] stuck holding
/// 12+ hour positions with rawExit dead at 0.500.
/// </summary>
public class StalePositionPenaltyTest
{
    [Fact]
    public void Penalty_IsZero_BelowThreshold()
    {
        // Age 5 < threshold 10 → no penalty.
        var p = MarketAgent.ComputeStalePenalty(currentTick: 105, openTick: 100, threshold: 10, perTick: 0.005f);
        Assert.Equal(0f, p, 6);
    }

    [Fact]
    public void Penalty_IsZero_AtThreshold()
    {
        // Age == threshold → no penalty (exclusive boundary; "exceeds" means strictly greater).
        var p = MarketAgent.ComputeStalePenalty(currentTick: 110, openTick: 100, threshold: 10, perTick: 0.005f);
        Assert.Equal(0f, p, 6);
    }

    [Fact]
    public void Penalty_GrowsLinearly_AboveThreshold()
    {
        // Age 11 → penalty = 0.005 × 1 = 0.005
        // Age 15 → penalty = 0.005 × 5 = 0.025
        // Age 30 → penalty = 0.005 × 20 = 0.100
        var p1 = MarketAgent.ComputeStalePenalty(111, 100, 10, 0.005f);
        var p5 = MarketAgent.ComputeStalePenalty(115, 100, 10, 0.005f);
        var p20 = MarketAgent.ComputeStalePenalty(130, 100, 10, 0.005f);

        Assert.Equal(0.005f, p1, 6);
        Assert.Equal(0.025f, p5, 6);
        Assert.Equal(0.100f, p20, 6);
    }

    [Fact]
    public void Penalty_Disabled_WhenThresholdIsZero()
    {
        // Default config (threshold = 0) → penalty disabled regardless of age.
        var p = MarketAgent.ComputeStalePenalty(currentTick: 9999, openTick: 0, threshold: 0, perTick: 1.0f);
        Assert.Equal(0f, p, 6);
    }

    [Fact]
    public void Penalty_Disabled_WhenPerTickIsZero()
    {
        // perTick = 0 → penalty disabled.
        var p = MarketAgent.ComputeStalePenalty(currentTick: 9999, openTick: 0, threshold: 10, perTick: 0f);
        Assert.Equal(0f, p, 6);
    }

    [Fact]
    public void DefaultConfig_HasDisabledStalePenalty()
    {
        // Default config keeps S8 disabled (threshold = 0, perTick = 0). Ceiling-test config
        // opts in by setting non-zero values explicitly.
        var config = MarketConfig.Default;
        Assert.Equal(0, config.StaleThresholdTicks);
        Assert.Equal(0f, config.StalePenaltyPerTick, 6);
    }

    [Fact]
    public void CeilingTestRecommendedSetting_ProducesExpectedRamp()
    {
        // Plan recommends StaleThresholdTicks=50, StalePenaltyPerTick=0.005 for ceiling test.
        // At 100 ticks (50 above threshold), penalty should be 0.005 × 50 = 0.25.
        var p = MarketAgent.ComputeStalePenalty(currentTick: 100, openTick: 0, threshold: 50, perTick: 0.005f);
        Assert.Equal(0.25f, p, 5);
    }
}
