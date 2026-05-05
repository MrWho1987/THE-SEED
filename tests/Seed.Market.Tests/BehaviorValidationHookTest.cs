using Seed.Market.Agents;
using Seed.Market.Evolution;

namespace Seed.Market.Tests;

/// <summary>
/// T5 — Behavior-validation hooks. Pure-function helpers that the checkpoint block in
/// Program.cs uses to detect:
///   • Output-deadness: per-output stddev across population stuck near 0.
///   • Mode collapse: population output-means clustered near the centroid (low variance).
/// Hooks log [BEHAVIOR-WARN] lines after K consecutive checkpoints below threshold;
/// these tests exercise the underlying math.
/// </summary>
public class BehaviorValidationHookTest
{
    [Fact]
    public void GetPopulationOutputStds_AveragesAcrossGenomes()
    {
        // 3 genomes; output 0 stddev = [0.1, 0.2, 0.3] → mean 0.2; output 1 = [0.5, 0.5, 0.5] → 0.5.
        var evals = new Dictionary<Guid, MarketEvalResult>();
        AddObs(evals, means: new[] { 1f, 2f }, stds: new[] { 0.1f, 0.5f });
        AddObs(evals, means: new[] { 1f, 2f }, stds: new[] { 0.2f, 0.5f });
        AddObs(evals, means: new[] { 1f, 2f }, stds: new[] { 0.3f, 0.5f });

        var stds = MarketEvolution.GetPopulationOutputStds(evals);
        Assert.Equal(2, stds.Length);
        Assert.Equal(0.2f, stds[0], 5);
        Assert.Equal(0.5f, stds[1], 5);
    }

    [Fact]
    public void GetPopulationOutputStds_EmptyEvaluations_ReturnsEmpty()
    {
        var evals = new Dictionary<Guid, MarketEvalResult>();
        var stds = MarketEvolution.GetPopulationOutputStds(evals);
        Assert.Empty(stds);
    }

    [Fact]
    public void GetPopulationOutputStds_DetectsDeadOutput()
    {
        // Output index 5 stuck at near-zero across the population → deadness.
        var evals = new Dictionary<Guid, MarketEvalResult>();
        for (int i = 0; i < 5; i++)
        {
            var stds = new float[11];
            for (int j = 0; j < 11; j++) stds[j] = 0.10f;
            stds[5] = 0.001f;  // dead V11 output (lv)
            AddObs(evals, means: new float[11], stds: stds);
        }
        var popStds = MarketEvolution.GetPopulationOutputStds(evals);
        Assert.Equal(11, popStds.Length);
        Assert.True(popStds[5] < 0.01f, $"output 5 should read as dead; got {popStds[5]:F4}");
        Assert.True(popStds[6] >= 0.01f, $"output 6 should read as healthy; got {popStds[6]:F4}");
    }

    [Fact]
    public void ComputePopulationOutputVariance_HighWhenSpread_LowWhenConverged()
    {
        // Spread population: means are very different across genomes → high variance.
        var spread = new Dictionary<Guid, MarketEvalResult>();
        AddObs(spread, means: new[] { 0f, 0f, 0f }, stds: new[] { 0.1f, 0.1f, 0.1f });
        AddObs(spread, means: new[] { 1f, 1f, 1f }, stds: new[] { 0.1f, 0.1f, 0.1f });
        AddObs(spread, means: new[] { -1f, -1f, -1f }, stds: new[] { 0.1f, 0.1f, 0.1f });
        float spreadVar = MarketEvolution.ComputePopulationOutputVariance(spread);
        Assert.True(spreadVar > 0.5f, $"spread population should have high variance; got {spreadVar:F4}");

        // Converged population: all means at the same point → variance = 0.
        var converged = new Dictionary<Guid, MarketEvalResult>();
        for (int i = 0; i < 5; i++)
            AddObs(converged, means: new[] { 0.5f, 0.5f, 0.5f }, stds: new[] { 0.05f, 0.05f, 0.05f });
        float convergedVar = MarketEvolution.ComputePopulationOutputVariance(converged);
        Assert.Equal(0f, convergedVar, 5);
    }

    [Fact]
    public void ComputePopulationOutputVariance_EmptyEvaluations_ReturnsZero()
    {
        var v = MarketEvolution.ComputePopulationOutputVariance(new Dictionary<Guid, MarketEvalResult>());
        Assert.Equal(0f, v, 5);
    }

    private static void AddObs(Dictionary<Guid, MarketEvalResult> evals, float[] means, float[] stds)
    {
        var id = Guid.NewGuid();
        evals[id] = new MarketEvalResult(
            GenomeId: id,
            Fitness: new FitnessBreakdown(Fitness: 1f, ReturnPct: 0f, MaxDrawdown: 0f,
                TotalTrades: 1, WinRate: 0f, NetPnl: 0f, IsActive: true,
                RawSharpe: 0f, AdjustedSharpe: 0f, Sortino: 0f, AdjustedSortino: 0f,
                CVaR5: 0f, MaxDrawdownDuration: 0f, ShrinkageConfidence: 0f),
            OutputObs: new OutputObservation(Means: means, Stds: stds, TickCount: 100));
    }
}
