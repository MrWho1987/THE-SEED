using Seed.Market.Agents;
using Seed.Market.Evolution;

namespace Seed.Market.Tests;

/// <summary>
/// T3 — Behavioral niching. Genomes whose output-behavior vector
/// (<see cref="OutputObservation.Means"/>) is far from the population centroid receive a
/// fitness bonus = <c>BehavioralDiversity × tanh(Euclidean distance)</c>. The tanh
/// saturates the bonus to prevent unbounded credit for outliers; the weight comes from
/// the current-generation <see cref="WeightWaypoint.BehavioralDiversity"/> in the schedule.
/// Default weight is 0 so existing configs are unaffected; ceiling-test config opts in.
/// </summary>
public class BehavioralNichingTest
{
    [Fact]
    public void Centroid_OfEqualVectors_EqualsTheVector()
    {
        var means = new float[] { 0.1f, 0.2f, 0.3f };
        var stds = new float[] { 0.05f, 0.05f, 0.05f };
        var evals = new Dictionary<Guid, MarketEvalResult>();
        for (int i = 0; i < 5; i++)
        {
            var id = Guid.NewGuid();
            evals[id] = new MarketEvalResult(
                GenomeId: id,
                Fitness: new FitnessBreakdown(Fitness: 1f, ReturnPct: 0f, MaxDrawdown: 0f,
                    TotalTrades: 1, WinRate: 0f, NetPnl: 0f, IsActive: true,
                    RawSharpe: 0f, AdjustedSharpe: 0f, Sortino: 0f, AdjustedSortino: 0f,
                    CVaR5: 0f, MaxDrawdownDuration: 0f, ShrinkageConfidence: 0f),
                OutputObs: new OutputObservation(Means: (float[])means.Clone(), Stds: stds, TickCount: 100));
        }
        var (centroid, dim) = MarketEvolution.ComputePopulationOutputCentroid(evals);
        Assert.Equal(3, dim);
        Assert.Equal(0.1f, centroid[0], 5);
        Assert.Equal(0.2f, centroid[1], 5);
        Assert.Equal(0.3f, centroid[2], 5);
    }

    [Fact]
    public void Centroid_AveragesAcrossPopulation()
    {
        // Two genomes with means [0,0,0] and [1,1,1] → centroid = [0.5, 0.5, 0.5]
        var evals = new Dictionary<Guid, MarketEvalResult>();
        var idA = Guid.NewGuid();
        var idB = Guid.NewGuid();
        evals[idA] = MakeEval(idA, means: new[] { 0f, 0f, 0f });
        evals[idB] = MakeEval(idB, means: new[] { 1f, 1f, 1f });

        var (centroid, dim) = MarketEvolution.ComputePopulationOutputCentroid(evals);
        Assert.Equal(3, dim);
        Assert.Equal(0.5f, centroid[0], 5);
        Assert.Equal(0.5f, centroid[1], 5);
        Assert.Equal(0.5f, centroid[2], 5);
    }

    [Fact]
    public void Centroid_EmptyOrAllNullObs_ReturnsEmpty()
    {
        // No genomes with valid OutputObs → centroid empty.
        var evals = new Dictionary<Guid, MarketEvalResult>();
        var id = Guid.NewGuid();
        evals[id] = new MarketEvalResult(
            GenomeId: id,
            Fitness: new FitnessBreakdown(0, 0, 0, 0, 0, 0, false, 0, 0, 0, 0, 0, 0, 0),
            OutputObs: null);
        var (centroid, dim) = MarketEvolution.ComputePopulationOutputCentroid(evals);
        Assert.Equal(0, dim);
        Assert.Empty(centroid);
    }

    [Fact]
    public void DefaultWaypoint_HasZeroBehavioralDiversityWeight()
    {
        // The legacy 9-weight default schedule has BehavioralDiversity = 0 so existing
        // configs see no niching. Ceiling-test config opts in by setting > 0.
        var cfg = MarketConfig.Default;
        var w = cfg.GetWeightsAt(0);
        Assert.Equal(0f, w.BehavioralDiversity, 5);
    }

    [Fact]
    public void ConstantSchedule_DefaultsBehavioralDiversityToZero()
    {
        var s = WeightWaypoint.ConstantSchedule(
            sharpe: 0.5f, sortino: 0.5f, returnWeight: 0f, ddDuration: 0f, cvar: 0f,
            calmar: 0f, infoRatio: 0f, feeDrag: 0f, diversification: 0f);
        Assert.Equal(0f, s[0].BehavioralDiversity, 5);
        Assert.Equal(1.0f, s[0].Sum(), 5);
    }

    [Fact]
    public void ConstantSchedule_AcceptsBehavioralDiversity()
    {
        // Schedule with niching weight = 0.05; remaining 9 weights sum to 0.95.
        var s = WeightWaypoint.ConstantSchedule(
            sharpe: 0.20f, sortino: 0.10f, returnWeight: 0.20f, ddDuration: 0.10f, cvar: 0.15f,
            calmar: 0.05f, infoRatio: 0.05f, feeDrag: 0.05f, diversification: 0.05f,
            behavioralDiversity: 0.05f);
        Assert.Equal(0.05f, s[0].BehavioralDiversity, 5);
        Assert.Equal(1.0f, s[0].Sum(), 5);
    }

    [Fact]
    public void Validate_AcceptsTenWeightSchedule_SummingToOne()
    {
        var cfg = MarketConfig.Default with
        {
            WeightSchedule = WeightWaypoint.ConstantSchedule(
                sharpe: 0.20f, sortino: 0.10f, returnWeight: 0.20f, ddDuration: 0.10f, cvar: 0.15f,
                calmar: 0.05f, infoRatio: 0.05f, feeDrag: 0.05f, diversification: 0.05f,
                behavioralDiversity: 0.05f)
        };
        cfg.Validate();  // no throw
    }

    private static MarketEvalResult MakeEval(Guid id, float[] means)
    {
        var stds = new float[means.Length];
        return new MarketEvalResult(
            GenomeId: id,
            Fitness: new FitnessBreakdown(Fitness: 1f, ReturnPct: 0f, MaxDrawdown: 0f,
                TotalTrades: 1, WinRate: 0f, NetPnl: 0f, IsActive: true,
                RawSharpe: 0f, AdjustedSharpe: 0f, Sortino: 0f, AdjustedSortino: 0f,
                CVaR5: 0f, MaxDrawdownDuration: 0f, ShrinkageConfidence: 0f),
            OutputObs: new OutputObservation(Means: means, Stds: stds, TickCount: 100));
    }
}
