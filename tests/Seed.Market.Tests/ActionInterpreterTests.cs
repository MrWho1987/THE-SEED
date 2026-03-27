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
}
