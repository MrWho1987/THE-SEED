using Seed.Market.Trading;

namespace Seed.Market.Tests;

/// <summary>
/// V11e output-learnability test: verifies that outputs 6-10 are REACHABLE through the
/// FULL brain→ActionInterpreter pipeline, not just ActionInterpreter in isolation.
///
/// The brain applies tanh (max ±1.0), then ActionInterpreter applies sigmoid → max 0.731.
/// V11d's deadzone of 0.80 was ABOVE this ceiling (proven in DeadzoneMathTests).
/// V11e lowered deadzones to 0.70, which IS reachable: sigmoid(tanh(1.25)) ≈ 0.70.
///
/// Tests verify both:
/// - ActionInterpreter-level reachability (raw outputs from brain go through sigmoid)
/// - Pipeline ceiling awareness (raw > 1.0 is impossible from a real brain)
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
        // NOTE: raw=3 bypasses brain's tanh (max 1.0). These test ActionInterpreter only.
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 3f, 3f, 3f, 3f, 3f];
        var signal = ActionInterpreter.Interpret(outputs);

        Assert.True(signal.PartialCloseFrac > 0f);
        Assert.True(signal.EnableTrailingStop);
        Assert.True(signal.TrailingStopDistance > 0f);
        Assert.True(signal.TakeProfitOffset > 0f);
        Assert.True(signal.StopLossOverride > 0f);
    }

    // ── V11e: Pipeline reachability tests ─────────────────────────────────
    // These verify outputs are reachable through the ACTUAL brain→interpreter pipeline
    // where brain output = tanh(x) → max 1.0, then sigmoid(tanh) → max 0.731.

    [Fact]
    public void Pipeline_MaxBrainOutput_ExceedsDeadzone()
    {
        // The brain's max output is tanh(∞) = 1.0.
        // sigmoid(1.0) = 0.731. Deadzone is 0.70. 0.731 > 0.70 ✓
        float maxBrainOutput = MathF.Tanh(100f); // ≈ 1.0
        float sigmoid = 1f / (1f + MathF.Exp(-maxBrainOutput));

        Assert.True(sigmoid > ActionInterpreter.PartialCloseDeadzone,
            $"sigmoid(tanh(∞)) = {sigmoid:F4} must exceed deadzone {ActionInterpreter.PartialCloseDeadzone}");
        Assert.True(sigmoid > ActionInterpreter.TrailEnableThreshold,
            $"sigmoid(tanh(∞)) = {sigmoid:F4} must exceed trail threshold {ActionInterpreter.TrailEnableThreshold}");
        Assert.True(sigmoid > ActionInterpreter.TpDeadzone,
            $"sigmoid(tanh(∞)) = {sigmoid:F4} must exceed TP deadzone {ActionInterpreter.TpDeadzone}");
        Assert.True(sigmoid > ActionInterpreter.SlDeadzone,
            $"sigmoid(tanh(∞)) = {sigmoid:F4} must exceed SL deadzone {ActionInterpreter.SlDeadzone}");
    }

    [Fact]
    public void Pipeline_RealisticBrainOutput_CanActivate()
    {
        // A brain with weighted_sum = 1.5 → tanh(1.5) = 0.905 → sigmoid(0.905) = 0.712
        // 0.712 > 0.70 → activates at deadzone 0.70
        float realisticOutput = MathF.Tanh(1.5f); // 0.905
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f,
                           realisticOutput, realisticOutput, realisticOutput,
                           realisticOutput, realisticOutput];
        var signal = ActionInterpreter.Interpret(outputs);

        Assert.True(signal.PartialCloseFrac > 0f,
            $"tanh(1.5) = {realisticOutput:F3} → sigmoid = {1f / (1f + MathF.Exp(-realisticOutput)):F3} should activate partial close at 0.70");
        Assert.True(signal.EnableTrailingStop);
    }

    [Fact]
    public void Pipeline_ModerateBrainOutput_StaysDormant()
    {
        // A brain with weighted_sum = 0.5 → tanh(0.5) = 0.462 → sigmoid(0.462) = 0.614
        // 0.614 < 0.70 → stays dormant
        float moderateOutput = MathF.Tanh(0.5f); // 0.462
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f,
                           moderateOutput, moderateOutput, moderateOutput,
                           moderateOutput, moderateOutput];
        var signal = ActionInterpreter.Interpret(outputs);

        Assert.Equal(0f, signal.PartialCloseFrac);
        Assert.False(signal.EnableTrailingStop);
        Assert.Equal(0f, signal.TakeProfitOffset);
        Assert.Equal(0f, signal.StopLossOverride);
    }
}
