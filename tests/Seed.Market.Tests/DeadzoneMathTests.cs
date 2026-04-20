namespace Seed.Market.Tests;

/// <summary>
/// Mathematical verification of the deadzone reachability ceiling.
///
/// The full pipeline for outputs 6-10 is:
///   BrainRuntime:     tanh(weighted_sum) → output in [-1, +1]
///   ActionInterpreter: sigmoid(brain_output) → value in [0.269, 0.731]
///   Deadzone check:   value > threshold?
///
/// This means sigmoid(tanh(x)) has a HARD CEILING at sigmoid(1.0) ≈ 0.731.
/// ANY deadzone above 0.731 is MATHEMATICALLY UNREACHABLE regardless of
/// brain weights, training time, or evolution pressure.
/// </summary>
public class DeadzoneMathTests
{
    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));

    [Fact]
    public void SigmoidOfTanh_HasHardCeiling()
    {
        // tanh(x) approaches 1.0 as x → ∞
        // sigmoid(1.0) is the theoretical maximum of sigmoid(tanh(x))
        float ceiling = Sigmoid(MathF.Tanh(1000f)); // tanh(1000) ≈ 1.0
        float exactCeiling = Sigmoid(1.0f);

        Console.WriteLine($"sigmoid(tanh(1000)) = {ceiling:F6}");
        Console.WriteLine($"sigmoid(1.0)        = {exactCeiling:F6}");
        Console.WriteLine($"Theoretical ceiling  = 0.731059");

        Assert.True(ceiling < 0.732f, $"Ceiling should be ~0.731, got {ceiling}");
        Assert.True(ceiling > 0.730f, $"Ceiling should be ~0.731, got {ceiling}");
    }

    [Fact]
    public void Deadzone_0point8_IsUnreachable()
    {
        // The V11d deadzone of 0.8 is ABOVE the 0.731 ceiling
        float ceiling = Sigmoid(1.0f); // 0.731
        float deadzone = 0.8f;

        Console.WriteLine($"V11d deadzone: {deadzone}");
        Console.WriteLine($"Max reachable: {ceiling:F4}");
        Console.WriteLine($"Gap: {deadzone - ceiling:F4} (UNREACHABLE)");

        Assert.True(deadzone > ceiling,
            "CONFIRMED: deadzone 0.8 > sigmoid(tanh(∞)) = 0.731 — outputs 6-10 are PERMANENTLY DISABLED");
    }

    [Fact]
    public void Deadzone_0point7_IsReachable()
    {
        // Deadzone 0.70 requires tanh(x) > logit(0.70) = 0.847
        // This requires weighted_sum > atanh(0.847) ≈ 1.25
        // Achievable with strong evolved weights.

        float threshold = 0.70f;
        float requiredTanh = MathF.Log(threshold / (1f - threshold)); // logit
        float requiredWeightedSum = 0.5f * MathF.Log((1f + requiredTanh) / (1f - requiredTanh)); // atanh

        Console.WriteLine($"Deadzone 0.70:");
        Console.WriteLine($"  Requires tanh(x) > {requiredTanh:F4}");
        Console.WriteLine($"  Requires weighted_sum > {requiredWeightedSum:F4}");
        Console.WriteLine($"  sigmoid(tanh({requiredWeightedSum:F2})) = {Sigmoid(MathF.Tanh(requiredWeightedSum)):F4}");

        // Verify it IS reachable
        float maxBrainOutput = MathF.Tanh(requiredWeightedSum + 0.5f); // some margin
        float sigmoidValue = Sigmoid(maxBrainOutput);
        Assert.True(sigmoidValue >= threshold,
            $"Deadzone 0.70 should be reachable with weighted_sum > {requiredWeightedSum:F2}");
    }

    [Fact]
    public void RandomBrainActivation_At0point7_IsNearZero()
    {
        // Random CPPN brain: output weights ~ N(0, σ) where σ ≈ 0.1-0.3
        // tanh(small_value) ≈ small_value → sigmoid(small) ≈ 0.5
        // P(sigmoid(tanh(X)) > 0.7 | X ~ N(0, 0.3)) ≈ 0

        int samples = 100_000;
        var rng = new Random(42);
        int activated = 0;

        for (int i = 0; i < samples; i++)
        {
            // Simulate random brain output: weighted sum ~ N(0, 0.3)
            double x = SampleNormal(rng) * 0.3;
            float brainOutput = MathF.Tanh((float)x);
            float sigmoidVal = Sigmoid(brainOutput);
            if (sigmoidVal > 0.70f) activated++;
        }

        float rate = (float)activated / samples;
        Console.WriteLine($"Random activation at deadzone 0.70: {rate:P4} ({activated}/{samples})");

        Assert.True(rate < 0.01f,
            $"Random activation should be <1% at deadzone 0.70, got {rate:P2}");
    }

    [Fact]
    public void TrainedBrainActivation_At0point7_IsReachable()
    {
        // A trained brain with intentional strong weights can push tanh output toward 1.0
        // With weighted_sum = 2.0: tanh(2.0) = 0.964 → sigmoid(0.964) = 0.724 > 0.70 ✓

        float strongWeightedSum = 2.0f;
        float brainOutput = MathF.Tanh(strongWeightedSum);
        float sigmoidVal = Sigmoid(brainOutput);

        Console.WriteLine($"Trained brain, weighted_sum=2.0:");
        Console.WriteLine($"  tanh(2.0) = {brainOutput:F4}");
        Console.WriteLine($"  sigmoid({brainOutput:F4}) = {sigmoidVal:F4}");
        Console.WriteLine($"  > 0.70? {sigmoidVal > 0.70f}");

        Assert.True(sigmoidVal > 0.70f);
    }

    [Fact]
    public void DeadzoneTable_ShowsReachabilityByThreshold()
    {
        // Print a reference table of which deadzones are reachable
        Console.WriteLine("DEADZONE REACHABILITY TABLE:");
        Console.WriteLine($"  {"Deadzone",10} {"Needs tanh>",12} {"Needs sum>",11} {"Reachable?",11} {"Random act.",12}");

        float[] thresholds = [0.50f, 0.55f, 0.60f, 0.65f, 0.70f, 0.73f, 0.75f, 0.80f];
        float ceiling = Sigmoid(1.0f); // 0.731

        foreach (float t in thresholds)
        {
            float logitVal = MathF.Log(t / (1f - t));
            bool reachable = t < ceiling;
            string reqSum = reachable
                ? $"{0.5f * MathF.Log((1f + logitVal) / (1f - logitVal)):F3}"
                : "∞ (impossible)";

            // Estimate random activation (N(0, 0.3) weighted sum)
            string randAct = reachable ? EstimateRandomActivation(t) : "0.0000%";

            Console.WriteLine($"  {t,10:F2} {logitVal,12:F4} {reqSum,11} {(reachable ? "YES" : "NO ★"),11} {randAct,12}");
        }
    }

    private static string EstimateRandomActivation(float threshold)
    {
        int samples = 50_000;
        var rng = new Random(42);
        int activated = 0;
        for (int i = 0; i < samples; i++)
        {
            float x = (float)(SampleNormal(rng) * 0.3);
            if (Sigmoid(MathF.Tanh(x)) > threshold) activated++;
        }
        return $"{(float)activated / samples:P4}";
    }

    private static double SampleNormal(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
