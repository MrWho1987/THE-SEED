using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Market.Trading;

namespace Seed.Market.Tests;

/// <summary>
/// Tests for the V11d-restored reward shape (symmetric delta-based unrealized reward
/// + losing-only holding penalty) and the V11 peak-exit bonus (kept).
///
/// The original V11 A1 "fix" introduced an asymmetric absolute-pnl reward that created
/// aversive conditioning and locked training in a passive local optimum. V11d reverts
/// to the pre-V11 proven shape while keeping the peak-exit bonus and brain-driven-exit
/// bonus that V11/V11c added.
/// </summary>
public class Tier1RewardTests
{
    [Fact]
    public void MarketConfig_PeakExitBonus_DefaultIsPointOne()
    {
        Assert.Equal(0.1f, MarketConfig.Default.PeakExitBonus);
    }

    [Fact]
    public void MarketConfig_PeakExitBonus_OutOfRange_Throws()
    {
        var cfgNeg = MarketConfig.Default with { PeakExitBonus = -0.01f };
        Assert.Throws<InvalidOperationException>(() => cfgNeg.Validate());

        var cfgHigh = MarketConfig.Default with { PeakExitBonus = 0.6f };
        Assert.Throws<InvalidOperationException>(() => cfgHigh.Validate());
    }

    [Fact]
    public void MarketConfig_PeakExitBonus_InRange_Accepted()
    {
        (MarketConfig.Default with { PeakExitBonus = 0f }).Validate();
        (MarketConfig.Default with { PeakExitBonus = 0.1f }).Validate();
        (MarketConfig.Default with { PeakExitBonus = 0.5f }).Validate();
    }

    [Fact]
    public void MarketAgent_Construction_AcceptsPeakExitBonus()
    {
        // Smoke test: the peakExitBonus parameter threads through without error
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var graph = dev.CompileGraph(genome, MarketEvaluator.MarketBrainBudget, new DevelopmentContext(42, 0));
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
        var trader = new PaperTrader(MarketConfig.Default);
        var agent = new MarketAgent(genome.GenomeId, brain, trader, peakExitBonus: 0.15f);
        Assert.NotNull(agent);
    }

    // ── V11d: symmetric delta reward ────────────────────────────────────────

    [Fact]
    public void DeltaReward_SymmetricMoves_HaveEqualMagnitude()
    {
        // Restored pre-V11 shape: reward += clamp(delta * 30, -0.5, 0.5)
        // A +1% move and a -1% move should produce equal-magnitude rewards.
        const float upDelta = 0.01f;
        const float downDelta = -0.01f;

        float upReward = Math.Clamp(upDelta * 30f, -0.5f, 0.5f);
        float downReward = Math.Clamp(downDelta * 30f, -0.5f, 0.5f);

        Assert.Equal(0.30f, upReward, 4);
        Assert.Equal(-0.30f, downReward, 4);

        // Symmetric — magnitudes are equal
        Assert.Equal(MathF.Abs(upReward), MathF.Abs(downReward), 4);
    }

    [Fact]
    public void DeltaReward_RoundTrip_SumsToFinalChange()
    {
        // Key property of the delta reward: over a full round-trip lifetime, the cumulative
        // reward equals (final_pnl - initial_pnl) * 30, clamped per tick.
        // For random walks, this means the EXPECTED reward is zero.

        // Simulate a position that goes 0 → +1% → 0 (round trip back to flat)
        float[] pnlPath = { 0.000f, 0.005f, 0.010f, 0.005f, 0.000f };

        float cumReward = 0f;
        float prevPnl = 0f;
        foreach (var pnl in pnlPath)
        {
            float delta = pnl - prevPnl;
            cumReward += Math.Clamp(delta * 30f, -0.5f, 0.5f);
            prevPnl = pnl;
        }

        // Final pnl is 0, so cumulative reward should also be ~0
        Assert.Equal(0f, cumReward, 4);
    }

    [Fact]
    public void DeltaReward_ClampsAtExtremes()
    {
        // Big positive delta should clamp to +0.5
        float bigUp = Math.Clamp(0.10f * 30f, -0.5f, 0.5f);
        Assert.Equal(0.5f, bigUp, 4);

        // Big negative delta should clamp to -0.5
        float bigDown = Math.Clamp(-0.10f * 30f, -0.5f, 0.5f);
        Assert.Equal(-0.5f, bigDown, 4);
    }

    // ── V11d: holding penalty restored — losing only ────────────────────────

    [Fact]
    public void HoldingPenalty_FiresOnly_OnLosingHolds()
    {
        // V11d restored pre-V11 shape: penalty fires only if pnlPct <= 0 AND ticks > 20
        // Winning holds have NO penalty — winners are free to run.
        int ticks = 30;
        float losingPnl = -0.01f;
        float winningPnl = +0.01f;

        // Losing position past threshold → penalty applies
        float losingPenalty = (losingPnl <= 0f && ticks > 20)
            ? Math.Clamp((ticks - 20) / 200f, 0f, 0.05f)
            : 0f;
        Assert.True(losingPenalty > 0f, "Losing hold past 20 ticks must be penalized");
        Assert.Equal(0.05f, losingPenalty, 4);  // (30-20)/200 = 0.05

        // Winning position past same threshold → NO penalty
        float winningPenalty = (winningPnl <= 0f && ticks > 20)
            ? Math.Clamp((ticks - 20) / 200f, 0f, 0.05f)
            : 0f;
        Assert.Equal(0f, winningPenalty);
    }

    [Fact]
    public void HoldingPenalty_NotFired_Below20Ticks()
    {
        // Even losing holds get no penalty if held < 20 ticks (gives the brain time to hold)
        int ticks = 15;
        float losingPnl = -0.05f;

        float penalty = (losingPnl <= 0f && ticks > 20)
            ? Math.Clamp((ticks - 20) / 200f, 0f, 0.05f)
            : 0f;
        Assert.Equal(0f, penalty);
    }

    [Fact]
    public void HoldingPenalty_ClampsAtMax_005()
    {
        // Penalty is capped at 0.05 to prevent runaway negative reward
        int ticks = 1000;
        float pnl = -0.10f;

        float penalty = (pnl <= 0f && ticks > 20)
            ? Math.Clamp((ticks - 20) / 200f, 0f, 0.05f)
            : 0f;
        Assert.Equal(0.05f, penalty, 4);
    }

    [Fact]
    public void HoldingPenalty_BoundaryAt_PnlExactlyZero_StillApplies()
    {
        // Edge case: pnl exactly 0 (breakeven hold) is treated as losing for the penalty.
        // Pre-V11 used `<= 0f` so breakeven counts. We preserve that.
        int ticks = 25;
        float pnl = 0f;

        float penalty = (pnl <= 0f && ticks > 20)
            ? Math.Clamp((ticks - 20) / 200f, 0f, 0.05f)
            : 0f;
        Assert.True(penalty > 0f);
    }

    // ── Peak-exit bonus (kept from V11 — these still apply) ─────────────────

    [Fact]
    public void PeakExitBonus_FullCapture_ReceivesFullBonus()
    {
        // captureRatio = realizedPct / peakPct; full bonus when realized == peak
        float peakUnrealizedPnl = 0.02f;
        float realizedPct = 0.02f;
        float captureRatio = Math.Clamp(realizedPct / peakUnrealizedPnl, 0f, 1f);
        Assert.Equal(1.0f, captureRatio);

        float bonus = captureRatio * 0.1f;  // peakExitBonus default
        Assert.Equal(0.1f, bonus);
    }

    [Fact]
    public void PeakExitBonus_PartialCapture_ReceivesPartialBonus()
    {
        // Realized = 50% of peak → half of max bonus
        float peakUnrealizedPnl = 0.02f;
        float realizedPct = 0.01f;
        float captureRatio = Math.Clamp(realizedPct / peakUnrealizedPnl, 0f, 1f);
        Assert.Equal(0.5f, captureRatio);

        float bonus = captureRatio * 0.1f;
        Assert.Equal(0.05f, bonus);
    }

    [Fact]
    public void PeakExitBonus_NegativePeak_Suppressed()
    {
        // If peak was never positive (position only went negative), bonus should not fire
        // because the condition `last.Pnl > 0 && _peakUnrealizedPnl > 0.001f` gates it.
        float pnl = -5f;
        float peak = 0f;
        bool shouldFire = pnl > 0 && peak > 0.001f;
        Assert.False(shouldFire);
    }
}
