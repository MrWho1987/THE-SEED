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

    // ── Leverage tests (tanh-based log-scale mapping) ──────────────────────────

    [Fact]
    public void Leverage_WithMaxLeverage1_AlwaysOne()
    {
        // With MaxLeverage=1, the leverage output collapses: MaxLev^positiveSignal = 1^x = 1.
        var highConfidence = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 10f], maxLeverage: 1.0f);
        Assert.Equal(1.0f, highConfidence.Leverage, 3);

        var lowConfidence = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, -10f], maxLeverage: 1.0f);
        Assert.Equal(1.0f, lowConfidence.Leverage, 3);
    }

    [Fact]
    public void Leverage_WithMaxLeverage125_DormantOutput_ReturnsOne()
    {
        // Dormant output (neuron = 0): tanh(0) = 0 → max(0, 0) = 0 → 125^0 = 1.
        // This is the key safe-default behavior: a dormant leverage neuron yields 1x, NOT some
        // midpoint value that would be catastrophic at high MaxLeverage.
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 0f], maxLeverage: 125f);
        Assert.Equal(1.0f, signal.Leverage, 3);
    }

    [Fact]
    public void Leverage_WithMaxLeverage125_NegativeOutput_ReturnsOne()
    {
        // Negative output: tanh(-10) ≈ -1 → max(0, -1) = 0 → 125^0 = 1.
        // "Brain is not confident" → no leverage, safe default.
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, -10f], maxLeverage: 125f);
        Assert.Equal(1.0f, signal.Leverage, 3);
    }

    [Fact]
    public void Leverage_WithMaxLeverage125_FullPositive_ReachesCeiling()
    {
        // Strong positive output: tanh(10) ≈ 1 → positiveSignal ≈ 1 → 125^1 = 125.
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 10f], maxLeverage: 125f);
        Assert.InRange(signal.Leverage, 124.9f, 125.01f);
    }

    [Fact]
    public void Leverage_LogScale_IntermediatePositiveSmoothlyScales()
    {
        // tanh(atanh(0.5)) = 0.5 → 125^0.5 = sqrt(125) ≈ 11.18
        // Use atanh(0.5) ≈ 0.5493 as input.
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 0.5493f], maxLeverage: 125f);
        Assert.InRange(signal.Leverage, 11.0f, 11.4f);  // allow small tanh precision error
    }

    [Fact]
    public void Leverage_MissingOutput_DefaultsToOne()
    {
        // Only 5 outputs (no leverage output) → positiveSignal = 0 → leverage = 1.
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f], maxLeverage: 125f);
        Assert.Equal(1.0f, signal.Leverage, 3);
    }

    [Fact]
    public void RawSizePct_CapturedSeparatelyFromClampedSizePct()
    {
        // Verify both the raw sigmoid and the clamped [0,1] value are exposed.
        var signal = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 0f]);
        Assert.Equal(0.5f, signal.RawSizePct, 3);
        Assert.Equal(0.5f, signal.SizePct, 3);
    }

    [Fact]
    public void RawLeverage_StoredAsPositiveSignal_ClampedToZero()
    {
        // RawLeverage stores max(0, tanh(output[5])) — the signal before log scaling.
        // Dormant: 0, negative: 0, positive: tanh value.
        var dormant = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 0f], maxLeverage: 125f);
        Assert.Equal(0f, dormant.RawLeverage, 3);

        var negative = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, -5f], maxLeverage: 125f);
        Assert.Equal(0f, negative.RawLeverage, 3);

        // tanh(atanh(0.5)) = 0.5
        var midPositive = ActionInterpreter.Interpret([0f, 0f, 0f, 0f, 0f, 0.5493f], maxLeverage: 125f);
        Assert.InRange(midPositive.RawLeverage, 0.49f, 0.51f);
    }
}
