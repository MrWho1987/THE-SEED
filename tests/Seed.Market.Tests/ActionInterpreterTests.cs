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
}
