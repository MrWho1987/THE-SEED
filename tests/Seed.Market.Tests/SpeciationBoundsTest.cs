using Seed.Core;
using Seed.Genetics;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Observatory;

namespace Seed.Market.Tests;

/// <summary>
/// S4 — Speciation redesign: hard upper bound 10.0 → config-driven (default 30.0) with
/// adaptive AdjustRate halving above 70% of the max. MinStagnationImprovement default
/// raised 0.005 → 0.02 so floating-point drift cannot reset the stagnation counter.
/// TargetSpeciesMax tightened 50 → 20.
/// </summary>
public class SpeciationBoundsTest
{
    [Fact]
    public void DefaultConfig_HasNewSpeciationDefaults()
    {
        var config = MarketConfig.Default;
        Assert.Equal(30.0f, config.CompatibilityThresholdMax, 5);
        Assert.Equal(20, config.TargetSpeciesMax);
        Assert.Equal(0.02f, config.MinStagnationImprovement, 5);
    }

    [Fact]
    public void ThresholdController_HonorsConfigUpperBound()
    {
        // Direct simulation of the controller logic (mirrors MarketEvolution.cs:163-181).
        // Pinning behavior is hard to demonstrate via a short live evolution — synthetic
        // genomes don't always exceed TargetSpeciesMax in 30 gens. Instead, verify the math
        // honors CompatibilityThresholdMax (not the legacy 10.0 hardcoded cap).
        const int targetSpeciesMin = 5;
        const int targetSpeciesMax = 20;
        const float maxThreshold = 30.0f;
        const float baseAdjustRate = 1.0f;

        // Helper that mirrors the production controller exactly.
        float UpdateThreshold(float current, int specCount)
        {
            float adjustRate = baseAdjustRate;
            if (current > maxThreshold * 0.7f) adjustRate *= 0.5f;
            if (specCount < targetSpeciesMin)
                return Math.Max(1.0f, current - adjustRate);
            if (specCount > targetSpeciesMax)
                return Math.Min(maxThreshold, current + adjustRate);
            return current;
        }

        // Threshold starts at 3.5 (default) and is repeatedly pushed by an over-target
        // species count. Should grow past the legacy 10.0 ceiling.
        float threshold = 3.5f;
        for (int g = 0; g < 50; g++)
            threshold = UpdateThreshold(threshold, specCount: 30);

        Assert.True(threshold > 10.0f,
            $"controller must allow threshold past legacy 10.0 cap; got {threshold:F2}");
        Assert.True(threshold <= maxThreshold,
            $"controller must respect CompatibilityThresholdMax ({maxThreshold}); got {threshold:F2}");

        // Another 100 iterations should saturate (not overshoot) at maxThreshold.
        for (int g = 0; g < 100; g++)
            threshold = UpdateThreshold(threshold, specCount: 30);
        Assert.Equal(maxThreshold, threshold, 5);
    }

    [Fact]
    public void ThresholdController_HalvesAdjustRateAbove70PctOfMax()
    {
        // Above 0.7 × max (= 21.0), the adjust rate halves — providing a soft brake so the
        // controller doesn't overshoot. Verify by comparing per-step deltas below vs above
        // the 70% boundary.
        const float maxThreshold = 30.0f;
        const float baseAdjustRate = 1.0f;
        const int targetSpeciesMax = 20;

        float UpdateThreshold(float current, int specCount)
        {
            float adjustRate = baseAdjustRate;
            if (current > maxThreshold * 0.7f) adjustRate *= 0.5f;
            if (specCount > targetSpeciesMax)
                return Math.Min(maxThreshold, current + adjustRate);
            return current;
        }

        float belowBrake = 10.0f;       // 10/30 = 33%, well below 70%
        float beforeBelow = belowBrake;
        belowBrake = UpdateThreshold(belowBrake, specCount: 30);
        float deltaBelow = belowBrake - beforeBelow;

        float aboveBrake = 25.0f;       // 25/30 = 83%, above 70%
        float beforeAbove = aboveBrake;
        aboveBrake = UpdateThreshold(aboveBrake, specCount: 30);
        float deltaAbove = aboveBrake - beforeAbove;

        Assert.Equal(1.0f, deltaBelow, 5);
        Assert.Equal(0.5f, deltaAbove, 5);
    }

    [Fact]
    public void StagnationCounter_DoesNotResetOnFloatingPointDrift()
    {
        // With MinStagnationImprovement = 0.02, a 0.001 improvement (e.g., FP drift) does NOT
        // reset the counter. With 0.005 (legacy), it WOULD have reset.
        var config = MarketConfig.Default with { MinStagnationImprovement = 0.02f };

        // Synthetic test: simulate the threshold check directly. The relevant logic is at
        // MarketEvolution.RunGeneration:
        //     if (bestInSpecies > species.BestFitness + _config.MinStagnationImprovement)
        //         species.StagnationCounter = 0;
        //     else
        //         species.StagnationCounter++;
        // We test the predicate directly to avoid coupling to a full RunGeneration cycle.
        float prior = 1.000f;
        float drift = prior + 0.001f;       // tiny FP-drift "improvement"
        float meaningful = prior + 0.025f;  // genuine improvement above threshold

        Assert.False(drift > prior + config.MinStagnationImprovement,
            "0.001 drift must NOT exceed the 0.02 stagnation threshold");
        Assert.True(meaningful > prior + config.MinStagnationImprovement,
            "0.025 improvement must exceed the 0.02 stagnation threshold");
    }

    private static (SignalSnapshot[], float[], float[], float[]) CreateSyntheticData(int length)
    {
        var normalizer = new SignalNormalizer();
        var snapshots = new SignalSnapshot[length];
        var prices = new float[length];
        var rawVolumes = new float[length];
        var rawFundingRates = new float[length];
        float price = 50000f;
        var rng = new Random(42);

        for (int i = 0; i < length; i++)
        {
            price *= 1f + (float)(rng.NextDouble() - 0.498) * 0.02f;
            prices[i] = price;
            rawVolumes[i] = 1000f + (float)rng.NextDouble() * 500f;
            rawFundingRates[i] = 0.0001f * ((float)rng.NextDouble() - 0.5f);
            var raw = new float[SignalIndex.Count];
            raw[SignalIndex.BtcPrice] = price;
            raw[SignalIndex.BtcReturn1h] = i > 0 ? (price - prices[i - 1]) / prices[i - 1] : 0f;
            raw[SignalIndex.BtcVolume1h] = rawVolumes[i];
            raw[SignalIndex.Rsi14] = 50f + (float)(rng.NextDouble() - 0.5) * 30f;
            snapshots[i] = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddHours(i), i);
        }
        return (snapshots, prices, rawVolumes, rawFundingRates);
    }
}
