namespace Seed.Market.Tests;

/// <summary>
/// Pure-function verification of the reward shape hypotheses before committing to a fix.
///
/// Claim 1: V11 asymmetric reward creates NEGATIVE expected value for random trading
///          (vs pre-V11 symmetric delta which has ZERO expected value).
///
/// Claim 2: The V11 deadzones on outputs 6-10 are too permissive — random sigmoid outputs
///          (centered at 0.5) activate them at very high rates, causing position churn.
///
/// These tests replicate the reward accumulation math directly without running a full
/// MarketAgent, so we can reason about first principles.
/// </summary>
public class RewardShapeVerificationTests
{
    // ── Claim 1: Reward asymmetry creates aversive conditioning ─────────────

    /// <summary>
    /// V11 reward shape (what the stuck run 2 is using):
    ///   per-tick while position open:
    ///     if pnlPct > 0: reward += min(pnlPct * 2, 0.1)
    ///     if pnlPct < 0: reward -= min(-pnlPct * 5, 0.15)
    /// </summary>
    private static float V11UnrealizedReward(float pnlPct)
    {
        if (pnlPct > 0)
            return MathF.Min(pnlPct * 2f, 0.1f);
        else
            return -MathF.Min(-pnlPct * 5f, 0.15f);
    }

    /// <summary>
    /// Pre-V11 reward shape (symmetric delta):
    ///   per-tick: reward += clamp(delta * 30, -0.5, 0.5)  where delta = pnlPct_now - pnlPct_prev
    /// </summary>
    private static float PreV11DeltaReward(float delta)
    {
        return Math.Clamp(delta * 30f, -0.5f, 0.5f);
    }

    [Fact]
    public void V11Reward_SymmetricOne_PercentMoves_IsAsymmetric()
    {
        // A 1% profitable move vs a 1% losing move — the magnitudes should be equal
        // under pre-V11 but asymmetric under V11.

        float profit = V11UnrealizedReward(0.01f);   // +1%
        float loss   = V11UnrealizedReward(-0.01f);  // -1%

        Assert.Equal(0.02f, profit, 4);   // 0.01 * 2
        Assert.Equal(-0.05f, loss, 4);    // -(0.01 * 5)

        // Ratio: loss is 2.5x more painful than profit is rewarding
        Assert.True(MathF.Abs(loss) / profit > 2.0f);
    }

    [Fact]
    public void V11Reward_RandomTrader_HasNegativeExpectedValue()
    {
        // Simulate a random trader who takes positions that hover at random pnlPct each tick.
        // Each position is open for N ticks, at a fixed "random walk" unrealized P&L.
        // Half of positions end profitable (+1% avg), half end losing (-1% avg).

        int positionLifetime = 10;
        int numTrades = 1000;
        var rng = new Random(42);

        float cumulativeReward = 0f;
        for (int trade = 0; trade < numTrades; trade++)
        {
            // Simulate 10 ticks with small random walks on unrealized P&L
            // Mean: 0 (truly random), std: 0.005 (0.5% per tick)
            float pnlPct = 0f;
            for (int t = 0; t < positionLifetime; t++)
            {
                pnlPct += (float)(rng.NextDouble() - 0.5) * 0.01f;  // ±0.5% per tick
                cumulativeReward += V11UnrealizedReward(pnlPct);
            }
        }

        float avgRewardPerTrade = cumulativeReward / numTrades;

        // Claim: V11 gives strongly negative expected reward for random trading
        Assert.True(avgRewardPerTrade < -0.05f,
            $"V11 expected reward per random trade should be << 0, got {avgRewardPerTrade:F4}");
    }

    [Fact]
    public void PreV11Reward_RandomTrader_HasNearZeroExpectedValue()
    {
        // Same random trader, pre-V11 delta reward
        int positionLifetime = 10;
        int numTrades = 1000;
        var rng = new Random(42);

        float cumulativeReward = 0f;
        for (int trade = 0; trade < numTrades; trade++)
        {
            float pnlPct = 0f;
            float prevPnlPct = 0f;
            for (int t = 0; t < positionLifetime; t++)
            {
                pnlPct += (float)(rng.NextDouble() - 0.5) * 0.01f;
                float delta = pnlPct - prevPnlPct;
                cumulativeReward += PreV11DeltaReward(delta);
                prevPnlPct = pnlPct;
            }
        }

        float avgRewardPerTrade = cumulativeReward / numTrades;

        // Claim: pre-V11 delta reward has zero expected value for random trading
        Assert.True(MathF.Abs(avgRewardPerTrade) < 0.05f,
            $"Pre-V11 expected reward per random trade should be ~0, got {avgRewardPerTrade:F4}");
    }

    [Fact]
    public void V11Reward_FiftyFiftyWinLoss_IsNegative()
    {
        // Cleanest test: exactly 50% of trades end at +1%, 50% at -1%, held 10 ticks each.
        int numTrades = 100;
        int lifetime = 10;

        float sumReward = 0f;
        for (int t = 0; t < numTrades; t++)
        {
            float finalPnl = (t % 2 == 0) ? 0.01f : -0.01f;
            // Assume linear price movement from 0 → finalPnl over lifetime ticks
            for (int tick = 1; tick <= lifetime; tick++)
            {
                float currentPnl = finalPnl * ((float)tick / lifetime);
                sumReward += V11UnrealizedReward(currentPnl);
            }
        }

        float perTrade = sumReward / numTrades;

        // Expected: per-trade should be distinctly negative
        Assert.True(perTrade < 0f,
            $"50/50 random trader should be negative under V11, got {perTrade:F4}");
    }

    [Fact]
    public void PreV11Reward_FiftyFiftyWinLoss_IsZero()
    {
        // Same 50/50 test under pre-V11 delta shape
        int numTrades = 100;
        int lifetime = 10;

        float sumReward = 0f;
        for (int t = 0; t < numTrades; t++)
        {
            float finalPnl = (t % 2 == 0) ? 0.01f : -0.01f;
            float prevPnl = 0f;
            for (int tick = 1; tick <= lifetime; tick++)
            {
                float currentPnl = finalPnl * ((float)tick / lifetime);
                sumReward += PreV11DeltaReward(currentPnl - prevPnl);
                prevPnl = currentPnl;
            }
        }

        float perTrade = sumReward / numTrades;

        // Expected: per-trade should be ~0 (delta reward is zero-sum over round trips)
        Assert.True(MathF.Abs(perTrade) < 0.02f,
            $"50/50 random trader should be ~0 under pre-V11, got {perTrade:F4}");
    }

    // ── Claim 2: Deadzones are too permissive for random brains ─────────────
    //
    // V11 new outputs (6-10) go through sigmoid. Random CPPN produces values near 0
    // before sigmoid, so sigmoid output ≈ 0.5. Activation thresholds:
    //   output[6] PartialClose > 0.2  → random activation ~ 1 - CDF(logit(0.2))
    //   output[7] TrailEnable  > 0.5  → random activation 50%
    //   output[8] TrailDist  (always read if enable active)
    //   output[9] TP offset    > 0.1  → random activation ~ 1 - CDF(logit(0.1))
    //   output[10] SL override > 0.1  → random activation ~ 1 - CDF(logit(0.1))
    //
    // A random brain's raw output is approximately Gaussian with mean 0 and small stddev.
    // Let's assume stddev = 0.5 (weak random CPPN).
    // sigmoid(0) = 0.5, sigmoid(1) ≈ 0.73, sigmoid(-1) ≈ 0.27.

    [Fact]
    public void RandomBrainActivation_Sigmoid_ExceedsDeadzone()
    {
        // Sample raw outputs from N(0, 0.5) — approximates a weak random CPPN.
        // Pass through sigmoid and measure how often each deadzone is crossed.
        int samples = 100_000;
        var rng = new Random(42);

        int partialCount = 0, trailEnableCount = 0, tpCount = 0, slCount = 0;
        for (int i = 0; i < samples; i++)
        {
            float rawOutput = (float)(Sample(rng) * 0.5);
            float sigmoid = 1f / (1f + MathF.Exp(-rawOutput));

            if (sigmoid > 0.2f) partialCount++;   // PartialClose deadzone
            if (sigmoid > 0.5f) trailEnableCount++; // TrailEnable threshold
            if (sigmoid > 0.1f) tpCount++;        // TP deadzone
            if (sigmoid > 0.1f) slCount++;        // SL deadzone
        }

        float partialRate = (float)partialCount / samples;
        float trailRate   = (float)trailEnableCount / samples;
        float tpRate      = (float)tpCount / samples;
        float slRate      = (float)slCount / samples;

        // Claim: random brains activate outputs 6-10 at VERY high rates
        Assert.True(partialRate > 0.5f, $"PartialClose random activation = {partialRate:P1} (expected > 50%)");
        Assert.True(trailRate > 0.4f,   $"TrailEnable random activation = {trailRate:P1} (expected > 40%)");
        Assert.True(tpRate > 0.7f,      $"TP random activation = {tpRate:P1} (expected > 70%)");
        Assert.True(slRate > 0.7f,      $"SL random activation = {slRate:P1} (expected > 70%)");

        // Output the actual rates so we can see the damage
        Console.WriteLine($"[DEADZONE ANALYSIS] Random-brain activation rates:");
        Console.WriteLine($"  PartialClose (>0.2): {partialRate:P1}");
        Console.WriteLine($"  TrailEnable  (>0.5): {trailRate:P1}");
        Console.WriteLine($"  TakeProfit   (>0.1): {tpRate:P1}");
        Console.WriteLine($"  StopLoss ovr (>0.1): {slRate:P1}");
    }

    [Fact]
    public void CorrectDeadzone_0point8_KeepsRandomBrainsDormant()
    {
        // Proposed fix: raise deadzones to 0.8 so random brains stay dormant.
        // Sigmoid(0.5*raw) > 0.8 requires raw > 2*logit(0.8) = 2*1.386 = 2.77
        // For a Gaussian with stddev=0.5, P(raw > 2.77) ≈ 0 (way out in the tail).

        int samples = 100_000;
        var rng = new Random(42);

        int activated = 0;
        for (int i = 0; i < samples; i++)
        {
            float rawOutput = (float)(Sample(rng) * 0.5);
            float sigmoid = 1f / (1f + MathF.Exp(-rawOutput));
            if (sigmoid > 0.8f) activated++;
        }

        float rate = (float)activated / samples;
        Assert.True(rate < 0.05f,
            $"Deadzone at 0.8 should keep random brains dormant (<5%), got {rate:P1}");

        Console.WriteLine($"[DEADZONE FIX] With deadzone 0.8, random activation: {rate:P1}");
    }

    /// <summary>
    /// Box-Muller transform for standard normal sample.
    /// </summary>
    private static double Sample(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();
        double u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }
}
