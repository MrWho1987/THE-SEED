using Seed.Market.Evolution;
using Seed.Market.Trading;

namespace Seed.Market.Tests;

/// <summary>
/// T4 — DirFlip dominance penalty: when a genome's close-reason histogram is more than
/// 70% DirectionFlip (reactive, not brain-driven), fitness is penalized proportional to
/// the excess. Penalty saturates at 1.0 when ratio = 1.0 (every close is dirFlip).
/// Encourages the population to develop non-dirFlip exits — brain-driven exits via
/// outputs 3, 6-10. Combined with T4's removal of end-of-session auto-close, this
/// closes the train/live environment gap (TP7).
/// </summary>
public class DirFlipDominanceTest
{
    [Fact]
    public void Penalty_IsZero_BelowThreshold()
    {
        // 6 dirFlip + 4 brain-driven = 60% dirFlip < 70% threshold → no penalty.
        var counts = new int[Enum.GetValues<CloseReason>().Length];
        counts[(int)CloseReason.DirectionFlip] = 6;
        counts[(int)CloseReason.ExitSignal] = 4;
        Assert.Equal(0f, MarketEvolution.ComputeDirFlipDominancePenalty(counts), 5);
    }

    [Fact]
    public void Penalty_IsZero_AtThreshold()
    {
        // 7 dirFlip + 3 brain = exactly 70% → no penalty (exclusive boundary).
        var counts = new int[Enum.GetValues<CloseReason>().Length];
        counts[(int)CloseReason.DirectionFlip] = 7;
        counts[(int)CloseReason.ExitSignal] = 3;
        Assert.Equal(0f, MarketEvolution.ComputeDirFlipDominancePenalty(counts), 5);
    }

    [Fact]
    public void Penalty_GrowsLinearly_AboveThreshold()
    {
        // 8 / 10 = 80% → excess = 0.10 / 0.30 ≈ 0.333
        var counts = new int[Enum.GetValues<CloseReason>().Length];
        counts[(int)CloseReason.DirectionFlip] = 8;
        counts[(int)CloseReason.ExitSignal] = 2;
        Assert.Equal(0.333f, MarketEvolution.ComputeDirFlipDominancePenalty(counts), 3);

        // 95% → excess = 0.25 / 0.30 ≈ 0.833
        counts[(int)CloseReason.DirectionFlip] = 19;
        counts[(int)CloseReason.ExitSignal] = 1;
        Assert.Equal(0.833f, MarketEvolution.ComputeDirFlipDominancePenalty(counts), 3);

        // 100% → penalty saturates at 1.0
        counts[(int)CloseReason.DirectionFlip] = 20;
        counts[(int)CloseReason.ExitSignal] = 0;
        Assert.Equal(1.0f, MarketEvolution.ComputeDirFlipDominancePenalty(counts), 5);
    }

    [Fact]
    public void Penalty_IsZero_WithFewClosesEvenIfAllDirFlip()
    {
        // < 5 closes → no penalty (insufficient sample to draw conclusion).
        var counts = new int[Enum.GetValues<CloseReason>().Length];
        counts[(int)CloseReason.DirectionFlip] = 4;  // all dirFlip but only 4 trades
        Assert.Equal(0f, MarketEvolution.ComputeDirFlipDominancePenalty(counts), 5);
    }

    [Fact]
    public void Penalty_IsZero_WhenNoCloses()
    {
        var counts = new int[Enum.GetValues<CloseReason>().Length];
        Assert.Equal(0f, MarketEvolution.ComputeDirFlipDominancePenalty(counts), 5);
        Assert.Equal(0f, MarketEvolution.ComputeDirFlipDominancePenalty(null!), 5);
    }

    [Fact]
    public void DefaultWaypoint_HasZeroDirFlipDominanceWeight()
    {
        var cfg = MarketConfig.Default;
        var w = cfg.GetWeightsAt(0);
        Assert.Equal(0f, w.DirFlipDominance, 5);
    }

    [Fact]
    public void ConstantSchedule_AcceptsDirFlipDominance()
    {
        // Schedule with dirFlip penalty = 0.03; remaining 9 weights sum to 0.97.
        var s = WeightWaypoint.ConstantSchedule(
            sharpe: 0.20f, sortino: 0.10f, returnWeight: 0.20f, ddDuration: 0.10f, cvar: 0.15f,
            calmar: 0.05f, infoRatio: 0.05f, feeDrag: 0.05f, diversification: 0.07f,
            behavioralDiversity: 0f, dirFlipDominance: 0.03f);
        Assert.Equal(0.03f, s[0].DirFlipDominance, 5);
        Assert.Equal(1.0f, s[0].Sum(), 5);
    }
}
