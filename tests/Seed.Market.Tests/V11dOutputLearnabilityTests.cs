using Seed.Market.Trading;

namespace Seed.Market.Tests;

/// <summary>
/// V11d output-learnability test: verifies that outputs 6-10 (partialClose, trailEnable,
/// trailDist, tpOffset, slOverride) are REACHABLE under intentional brain signal, not
/// merely "dormant by default". The deadzone fix (raised to 0.8) makes random brains
/// stay dormant, but the brain must still be able to push these outputs above 0.8 once
/// it learns to.
///
/// This test feeds explicit raw outputs to ActionInterpreter.Interpret and verifies that
/// strong-positive raw values DO activate each output 6-10.
/// </summary>
public class V11dOutputLearnabilityTests
{
    [Fact]
    public void PartialClose_Reachable_WithStrongPositiveSignal()
    {
        // Raw output of +3 → sigmoid(3) ≈ 0.953 > 0.8 → activates
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 3f, 0f, 0f, 0f, 0f];
        var signal = ActionInterpreter.Interpret(outputs);
        Assert.True(signal.PartialCloseFrac > 0f,
            "Strong positive raw output[6] (sigmoid > 0.8) must activate partial close");
        Assert.True(signal.PartialCloseFrac > ActionInterpreter.PartialCloseDeadzone,
            $"PartialCloseFrac {signal.PartialCloseFrac:F3} must exceed deadzone {ActionInterpreter.PartialCloseDeadzone}");
    }

    [Fact]
    public void TrailEnable_Reachable_WithStrongPositiveSignal()
    {
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 0f, 3f, 0f, 0f, 0f];
        var signal = ActionInterpreter.Interpret(outputs);
        Assert.True(signal.EnableTrailingStop,
            "Strong positive raw output[7] (sigmoid > 0.8) must enable trailing stop");
    }

    [Fact]
    public void TrailDist_Reachable_WithStrongPositiveSignal()
    {
        // Both enable AND distance need to be strong positive
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 0f, 3f, 3f, 0f, 0f];
        var signal = ActionInterpreter.Interpret(outputs);
        Assert.True(signal.EnableTrailingStop);
        Assert.True(signal.TrailingStopDistance > 0f,
            "Strong positive raw output[8] must produce a non-zero trail distance");
        // Range check: should be in log-scaled [0.005, 0.10] band
        Assert.InRange(signal.TrailingStopDistance, 0.005f, 0.15f);
    }

    [Fact]
    public void TpOffset_Reachable_WithStrongPositiveSignal()
    {
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 3f, 0f];
        var signal = ActionInterpreter.Interpret(outputs);
        Assert.True(signal.TakeProfitOffset > 0f,
            "Strong positive raw output[9] (sigmoid > 0.8) must produce a TP offset");
        Assert.InRange(signal.TakeProfitOffset, 0.005f, 0.20f);
    }

    [Fact]
    public void SlOverride_Reachable_WithStrongPositiveSignal()
    {
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 3f];
        var signal = ActionInterpreter.Interpret(outputs);
        Assert.True(signal.StopLossOverride > 0f,
            "Strong positive raw output[10] (sigmoid > 0.8) must produce an SL override");
        Assert.InRange(signal.StopLossOverride, 0.005f, 0.07f);
    }

    [Fact]
    public void RandomBrain_OutputsAt05_StayDormant()
    {
        // The DEFAULT random brain produces outputs near 0 → sigmoid ≈ 0.5
        // All outputs 6-10 should stay dormant.
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f];
        var signal = ActionInterpreter.Interpret(outputs);

        Assert.Equal(0f, signal.PartialCloseFrac);
        Assert.False(signal.EnableTrailingStop);
        Assert.Equal(0f, signal.TrailingStopDistance);
        Assert.Equal(0f, signal.TakeProfitOffset);
        Assert.Equal(0f, signal.StopLossOverride);
    }

    [Fact]
    public void AllNewOutputs_CoActivatable_WithStrongSignals()
    {
        // The ultimate agent uses ALL of outputs 6-10 simultaneously when warranted.
        // Verify they don't conflict with each other when all are strong-positive.
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 3f, 3f, 3f, 3f, 3f];
        var signal = ActionInterpreter.Interpret(outputs);

        Assert.True(signal.PartialCloseFrac > 0f);
        Assert.True(signal.EnableTrailingStop);
        Assert.True(signal.TrailingStopDistance > 0f);
        Assert.True(signal.TakeProfitOffset > 0f);
        Assert.True(signal.StopLossOverride > 0f);
    }
}
