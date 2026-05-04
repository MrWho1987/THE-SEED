using Seed.CheckpointEval;
using Seed.Market.Agents;

namespace Seed.Market.Tests;

/// <summary>
/// S9 — Deploy gate: flags genomes whose V11 expanded action outputs (indices 5..10:
/// lv/prt/tre/trd/tp/sl) had near-zero stddev across the eval window. A dead V11 output
/// means the agent never explored that channel — so live behavior is undefined for any
/// regime that would activate it. These tests pin the threshold semantics.
/// </summary>
public class DeployGateTest
{
    [Fact]
    public void HealthyOutputs_PassGate()
    {
        // All 11 outputs healthy: stddev > 0.05 each.
        var means = new float[11];
        var stds = Enumerable.Repeat(0.10f, 11).ToArray();
        var obs = new OutputObservation(means, stds, TickCount: 1000);

        var (blocked, reason) = CheckpointEvaluator.ComputeDeployGate(obs, threshold: 0.01f);
        Assert.False(blocked);
        Assert.Null(reason);
    }

    [Fact]
    public void DeadV11Output_BlocksGate()
    {
        // Output index 5 (leverage/lv) frozen at 0; others healthy. Should block.
        var means = new float[11];
        var stds = Enumerable.Repeat(0.10f, 11).ToArray();
        stds[5] = 0.0001f;  // dead: well below 0.01 threshold
        var obs = new OutputObservation(means, stds, TickCount: 1000);

        var (blocked, reason) = CheckpointEvaluator.ComputeDeployGate(obs, threshold: 0.01f);
        Assert.True(blocked);
        Assert.NotNull(reason);
        Assert.Contains("lv", reason);
    }

    [Fact]
    public void MultipleDeadV11Outputs_AllListedInReason()
    {
        // Indices 5 (lv), 7 (tre), 10 (sl) all dead.
        var means = new float[11];
        var stds = Enumerable.Repeat(0.10f, 11).ToArray();
        stds[5] = 0.0f;
        stds[7] = 0.0f;
        stds[10] = 0.0f;
        var obs = new OutputObservation(means, stds, TickCount: 1000);

        var (blocked, reason) = CheckpointEvaluator.ComputeDeployGate(obs, threshold: 0.01f);
        Assert.True(blocked);
        Assert.NotNull(reason);
        Assert.Contains("lv", reason);
        Assert.Contains("tre", reason);
        Assert.Contains("sl", reason);
    }

    [Fact]
    public void DeadStandardOutput_DoesNotBlock()
    {
        // Output 0 (direction) frozen — but standard outputs (0..4) are not in the V11 gate set.
        var means = new float[11];
        var stds = Enumerable.Repeat(0.10f, 11).ToArray();
        stds[0] = 0.0f;
        stds[3] = 0.0f;
        var obs = new OutputObservation(means, stds, TickCount: 1000);

        var (blocked, reason) = CheckpointEvaluator.ComputeDeployGate(obs, threshold: 0.01f);
        Assert.False(blocked);
        Assert.Null(reason);
    }

    [Fact]
    public void NullObs_DoesNotBlock()
    {
        // No observation captured: returns unblocked (defensive, no false positives).
        var (blocked, reason) = CheckpointEvaluator.ComputeDeployGate(null, threshold: 0.01f);
        Assert.False(blocked);
        Assert.Null(reason);
    }

    [Fact]
    public void Threshold_IsExclusiveBoundary()
    {
        // Stddev exactly at threshold should NOT block (the rule is < threshold).
        // Stddev just below threshold SHOULD block.
        var means = new float[11];

        var stdsAt = Enumerable.Repeat(0.10f, 11).ToArray();
        stdsAt[5] = 0.01f;  // exactly at threshold
        var (blockedAt, _) = CheckpointEvaluator.ComputeDeployGate(
            new OutputObservation(means, stdsAt, 1000), threshold: 0.01f);
        Assert.False(blockedAt);

        var stdsBelow = Enumerable.Repeat(0.10f, 11).ToArray();
        stdsBelow[5] = 0.0099f;  // just below
        var (blockedBelow, _) = CheckpointEvaluator.ComputeDeployGate(
            new OutputObservation(means, stdsBelow, 1000), threshold: 0.01f);
        Assert.True(blockedBelow);
    }
}
