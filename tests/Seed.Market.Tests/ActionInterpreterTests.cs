using Seed.Market.Trading;

namespace Seed.Market.Tests;

public class ActionInterpreterTests
{
    [Fact]
    public void StrongPositiveOutput_ProducesLong()
    {
        var signal = ActionInterpreter.Interpret([2f, 0.5f, 0.8f, 0f, 0f]);
        Assert.Equal(TradeDirection.Long, signal.Direction);
        Assert.True(signal.SizePct > 0);
        Assert.False(signal.ExitCurrent);
    }

    [Fact]
    public void StrongNegativeOutput_ProducesShort()
    {
        var signal = ActionInterpreter.Interpret([-2f, 0.5f, 0.5f, 0f, 0f]);
        Assert.Equal(TradeDirection.Short, signal.Direction);
    }

    [Fact]
    public void NearZeroOutput_ProducesFlat()
    {
        var signal = ActionInterpreter.Interpret([0.05f, 0.5f, 0.5f, 0f, 0f]);
        Assert.Equal(TradeDirection.Flat, signal.Direction);
    }

    [Fact]
    public void HighExitOutput_TriggersExit()
    {
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 3f, 0f]);
        Assert.True(signal.ExitCurrent);
    }

    [Fact]
    public void LowExitOutput_NoExit()
    {
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, -2f, 0f]);
        Assert.False(signal.ExitCurrent);
    }

    [Fact]
    public void SizeIsClamped01()
    {
        var signal = ActionInterpreter.Interpret([1f, 100f, 0f, 0f, 0f]);
        Assert.InRange(signal.SizePct, 0f, 1f);
    }

    [Fact]
    public void EmptyOutputDefaultsToFlatNoTrade()
    {
        var signal = ActionInterpreter.Interpret([]);
        Assert.Equal(TradeDirection.Flat, signal.Direction);
        Assert.False(signal.ExitCurrent);
    }

    [Fact]
    public void RawExitValue_EqualsSigmoidOfOutput3_HighValue()
    {
        // sigmoid(2) ≈ 0.8808 — above the 0.6 ExitThreshold
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 2.0f, 0f]);
        Assert.InRange(signal.RawExitValue, 0.880f, 0.882f);
        Assert.True(signal.ExitCurrent);
    }

    [Fact]
    public void RawExitValue_AtZero_ReturnsHalf()
    {
        // sigmoid(0) = 0.5 — below the 0.6 ExitThreshold
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f]);
        Assert.InRange(signal.RawExitValue, 0.499f, 0.501f);
        Assert.False(signal.ExitCurrent);
    }

    [Fact]
    public void RawExitValue_MissingOutput_DefaultsToZero()
    {
        // Only 3 outputs — no exit output at index [3]
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f]);
        Assert.Equal(0f, signal.RawExitValue);
        Assert.False(signal.ExitCurrent);
    }

    // ── Tier 1.1 Leverage tests ─────────────────────────────────────────────────

    [Fact]
    public void Leverage_WithMaxLeverage1_AlwaysOne()
    {
        // With MaxLeverage=1, the leverage output is ignored and all trades run at 1x.
        var highConfidence = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 10f], maxLeverage: 1.0f);
        Assert.Equal(1.0f, highConfidence.Leverage, 3);

        var lowConfidence = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, -10f], maxLeverage: 1.0f);
        Assert.Equal(1.0f, lowConfidence.Leverage, 3);
    }

    [Fact]
    public void Leverage_WithMaxLeverage3_HighConfidence_NearCeiling()
    {
        // sigmoid(10) ≈ 0.99995 → leverage ≈ 1 + 0.99995 × (3-1) = 2.9999
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 10f], maxLeverage: 3.0f);
        Assert.InRange(signal.Leverage, 2.99f, 3.01f);
    }

    [Fact]
    public void Leverage_WithMaxLeverage3_LowConfidence_NearOne()
    {
        // sigmoid(-10) ≈ 0.00005 → leverage ≈ 1 + 0.00005 × 2 ≈ 1.0001
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, -10f], maxLeverage: 3.0f);
        Assert.InRange(signal.Leverage, 1.0f, 1.01f);
    }

    [Fact]
    public void Leverage_WithMaxLeverage3_ZeroOutput_Midpoint()
    {
        // sigmoid(0) = 0.5 → leverage = 1 + 0.5 × 2 = 2.0 exactly
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 0f], maxLeverage: 3.0f);
        Assert.Equal(2.0f, signal.Leverage, 3);
    }

    [Fact]
    public void Leverage_MissingOutput_DefaultsToOne()
    {
        // Only 5 outputs (v1-style, no leverage) → leverage = 1.0 (default safe)
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f], maxLeverage: 3.0f);
        Assert.Equal(1.0f, signal.Leverage, 3);
    }

    [Fact]
    public void RawSizePct_CapturedSeparatelyFromClampedSizePct()
    {
        // Verify both the raw sigmoid and the clamped [0,1] value are exposed.
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 0f]);
        // sigmoid(0) = 0.5 for rawSize
        Assert.Equal(0.5f, signal.RawSizePct, 3);
        Assert.Equal(0.5f, signal.SizePct, 3);
    }

    [Fact]
    public void RawLeverage_Sigmoid_BeforeScaling()
    {
        // rawLeverage should be the pre-scaled sigmoid value, not the scaled leverage
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 0f], maxLeverage: 5.0f);
        Assert.Equal(0.5f, signal.RawLeverage, 3);
        // Scaled leverage = 1 + 0.5 × (5-1) = 3.0
        Assert.Equal(3.0f, signal.Leverage, 3);
    }
}
