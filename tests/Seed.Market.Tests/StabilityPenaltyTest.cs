using Seed.Market.Evolution;

namespace Seed.Market.Tests;

/// <summary>
/// L3-S5 — Cross-window stability penalty. In multi-window training evaluation
/// (EvalWindowCount &gt; 1), each genome's per-window <c>AdjustedSharpe</c> dispersion
/// (stddev / |mean|) is computed and a fitness penalty proportional to <c>Stability</c>
/// weight is subtracted. Discourages strategies that profit only on one regime.
/// Single-window mode (EvalWindowCount = 1) produces 0 dispersion → no penalty.
/// </summary>
public class StabilityPenaltyTest
{
    [Fact]
    public void Dispersion_IsZero_ForSingleBreakdown()
    {
        var b = new List<FitnessBreakdown>
        {
            MakeBreakdown(adjustedSharpe: 2.5f),
        };
        Assert.Equal(0f, MarketEvolution.ComputeCrossWindowDispersion(b), 5);
    }

    [Fact]
    public void Dispersion_IsZero_ForIdenticalAdjustedSharpes()
    {
        // 3 windows, all AdjustedSharpe = 2.0 → dispersion = 0 / 2 = 0.
        var b = new List<FitnessBreakdown>
        {
            MakeBreakdown(adjustedSharpe: 2.0f),
            MakeBreakdown(adjustedSharpe: 2.0f),
            MakeBreakdown(adjustedSharpe: 2.0f),
        };
        Assert.Equal(0f, MarketEvolution.ComputeCrossWindowDispersion(b), 5);
    }

    [Fact]
    public void Dispersion_GrowsWithVariance()
    {
        // [1, 2, 3] → mean = 2, std = sqrt(2/3) ≈ 0.8165, dispersion = 0.4082
        var b = new List<FitnessBreakdown>
        {
            MakeBreakdown(adjustedSharpe: 1.0f),
            MakeBreakdown(adjustedSharpe: 2.0f),
            MakeBreakdown(adjustedSharpe: 3.0f),
        };
        float dispersion = MarketEvolution.ComputeCrossWindowDispersion(b);
        Assert.Equal(0.4082f, dispersion, 3);
    }

    [Fact]
    public void Dispersion_HandlesNearZeroMean_WithoutBlowup()
    {
        // Mean ≈ 0 should not produce infinity. Helper uses max(|mean|, 1e-3) as denominator.
        var b = new List<FitnessBreakdown>
        {
            MakeBreakdown(adjustedSharpe: -0.5f),
            MakeBreakdown(adjustedSharpe: 0.5f),
        };
        float dispersion = MarketEvolution.ComputeCrossWindowDispersion(b);
        Assert.True(float.IsFinite(dispersion));
        Assert.True(dispersion > 100f, "near-zero mean with non-zero variance should produce large but finite dispersion");
    }

    [Fact]
    public void DefaultWaypoint_HasZeroStabilityWeight()
    {
        var w = MarketConfig.Default.GetWeightsAt(0);
        Assert.Equal(0f, w.Stability, 5);
    }

    [Fact]
    public void ConstantSchedule_AcceptsStability()
    {
        // 9 base + 1 stability summing to 1.0
        var s = WeightWaypoint.ConstantSchedule(
            sharpe: 0.20f, sortino: 0.10f, returnWeight: 0.15f, ddDuration: 0.10f, cvar: 0.15f,
            calmar: 0.05f, infoRatio: 0.05f, feeDrag: 0.05f, diversification: 0.10f,
            behavioralDiversity: 0f, dirFlipDominance: 0f, stability: 0.05f);
        Assert.Equal(0.05f, s[0].Stability, 5);
        Assert.Equal(1.0f, s[0].Sum(), 5);
    }

    private static FitnessBreakdown MakeBreakdown(float adjustedSharpe) => new(
        Fitness: adjustedSharpe, ReturnPct: 0f, MaxDrawdown: 0f,
        TotalTrades: 10, WinRate: 0.5f, NetPnl: 0f, IsActive: true,
        RawSharpe: adjustedSharpe, AdjustedSharpe: adjustedSharpe,
        Sortino: adjustedSharpe, AdjustedSortino: adjustedSharpe,
        CVaR5: -0.01f, MaxDrawdownDuration: 0.1f, ShrinkageConfidence: 0.5f);
}
