namespace Seed.Market.Tests;

/// <summary>
/// T1 — Hard cutover: 9 scalar fitness weight fields removed from MarketConfig; replaced
/// by a required <see cref="MarketConfig.WeightSchedule"/> of <see cref="WeightWaypoint"/>s.
/// <see cref="MarketConfig.GetWeightsAt"/> linearly interpolates between consecutive
/// waypoints by generation. Per-waypoint sum must equal 1.0 ± 0.01 (validated). At least
/// 2 waypoints, first at gen=0, sorted strictly ascending.
/// </summary>
public class WeightScheduleTest
{
    [Fact]
    public void DefaultSchedule_PassesValidation()
    {
        var cfg = MarketConfig.Default;
        cfg.Validate();  // no throw
        Assert.True(cfg.WeightSchedule.Count >= 2);
        Assert.Equal(0, cfg.WeightSchedule[0].Gen);
        Assert.True(cfg.WeightSchedule.All(w => MathF.Abs(w.Sum() - 1.0f) <= 0.01f));
    }

    [Fact]
    public void GetWeightsAt_BeforeFirstWaypoint_ReturnsFirst()
    {
        var cfg = MarketConfig.Default with
        {
            WeightSchedule =
            [
                new WeightWaypoint(Gen: 100, Sharpe: 0.3f, Sortino: 0.7f),
                new WeightWaypoint(Gen: 1000, Sharpe: 0.5f, Sortino: 0.5f),
            ]
        };
        var w = cfg.GetWeightsAt(generation: 50);
        Assert.Equal(0.3f, w.Sharpe, 5);
        Assert.Equal(0.7f, w.Sortino, 5);
    }

    [Fact]
    public void GetWeightsAt_AfterLastWaypoint_ReturnsLast()
    {
        var cfg = MarketConfig.Default with
        {
            WeightSchedule =
            [
                new WeightWaypoint(Gen: 0, Sharpe: 0.3f, Sortino: 0.7f),
                new WeightWaypoint(Gen: 1000, Sharpe: 0.5f, Sortino: 0.5f),
            ]
        };
        var w = cfg.GetWeightsAt(generation: 9999);
        Assert.Equal(0.5f, w.Sharpe, 5);
        Assert.Equal(0.5f, w.Sortino, 5);
    }

    [Fact]
    public void GetWeightsAt_AtMidpoint_LinearlyInterpolates()
    {
        // Gen 0: Sharpe=0.2, Sortino=0.8. Gen 1000: Sharpe=0.8, Sortino=0.2.
        // Halfway (gen=500): Sharpe=0.5, Sortino=0.5.
        var cfg = MarketConfig.Default with
        {
            WeightSchedule =
            [
                new WeightWaypoint(Gen: 0, Sharpe: 0.2f, Sortino: 0.8f),
                new WeightWaypoint(Gen: 1000, Sharpe: 0.8f, Sortino: 0.2f),
            ]
        };
        var w = cfg.GetWeightsAt(generation: 500);
        Assert.Equal(0.5f, w.Sharpe, 4);
        Assert.Equal(0.5f, w.Sortino, 4);
    }

    [Fact]
    public void GetWeightsAt_PreservesSumAcrossInterpolation()
    {
        // Two waypoints both summing to 1.0; interpolated waypoints must also sum to 1.0
        // (linear combination of two unit vectors stays unit).
        var cfg = MarketConfig.Default with
        {
            WeightSchedule =
            [
                new WeightWaypoint(Gen: 0,
                    Sharpe: 0.50f, Sortino: 0.20f, Return: 0.20f, DrawdownDuration: 0f, CVaR: 0.10f,
                    Calmar: 0f, InfoRatio: 0f, FeeDrag: 0f, Diversification: 0f),
                new WeightWaypoint(Gen: 1000,
                    Sharpe: 0.20f, Sortino: 0.30f, Return: 0.10f, DrawdownDuration: 0.10f, CVaR: 0.20f,
                    Calmar: 0.05f, InfoRatio: 0.03f, FeeDrag: 0.01f, Diversification: 0.01f),
            ]
        };
        cfg.Validate();
        for (int gen = 0; gen <= 1000; gen += 50)
        {
            var w = cfg.GetWeightsAt(gen);
            Assert.Equal(1.0f, w.Sum(), 4);
        }
    }

    [Fact]
    public void Validate_RejectsLessThan2Waypoints()
    {
        var cfg1 = MarketConfig.Default with { WeightSchedule = [] };
        Assert.Throws<InvalidOperationException>(() => cfg1.Validate());

        var cfg2 = MarketConfig.Default with
        {
            WeightSchedule = [new WeightWaypoint(Gen: 0, Sharpe: 1.0f)]
        };
        Assert.Throws<InvalidOperationException>(() => cfg2.Validate());
    }

    [Fact]
    public void Validate_RejectsFirstWaypointGenNotZero()
    {
        var cfg = MarketConfig.Default with
        {
            WeightSchedule =
            [
                new WeightWaypoint(Gen: 100, Sharpe: 1.0f),
                new WeightWaypoint(Gen: 200, Sharpe: 1.0f),
            ]
        };
        Assert.Throws<InvalidOperationException>(() => cfg.Validate());
    }

    [Fact]
    public void Validate_RejectsOutOfOrderWaypoints()
    {
        var cfg = MarketConfig.Default with
        {
            WeightSchedule =
            [
                new WeightWaypoint(Gen: 0,    Sharpe: 1.0f),
                new WeightWaypoint(Gen: 1000, Sharpe: 1.0f),
                new WeightWaypoint(Gen: 500,  Sharpe: 1.0f),  // out of order
            ]
        };
        Assert.Throws<InvalidOperationException>(() => cfg.Validate());
    }

    [Fact]
    public void Validate_RejectsDuplicateWaypointGens()
    {
        var cfg = MarketConfig.Default with
        {
            WeightSchedule =
            [
                new WeightWaypoint(Gen: 0,   Sharpe: 1.0f),
                new WeightWaypoint(Gen: 100, Sharpe: 1.0f),
                new WeightWaypoint(Gen: 100, Sharpe: 1.0f),  // duplicate
            ]
        };
        Assert.Throws<InvalidOperationException>(() => cfg.Validate());
    }

    [Fact]
    public void Validate_RejectsWaypointWithNonUnitSum()
    {
        var cfg = MarketConfig.Default with
        {
            WeightSchedule =
            [
                new WeightWaypoint(Gen: 0,    Sharpe: 0.50f),  // sum = 0.5 (invalid)
                new WeightWaypoint(Gen: 1000, Sharpe: 1.00f),
            ]
        };
        Assert.Throws<InvalidOperationException>(() => cfg.Validate());
    }

    [Fact]
    public void ConstantSchedule_ProducesValidTwoWaypointSchedule()
    {
        var schedule = WeightWaypoint.ConstantSchedule(
            sharpe: 0.22f, sortino: 0.13f, returnWeight: 0.20f, ddDuration: 0.13f,
            cvar: 0.17f, calmar: 0.05f, infoRatio: 0.05f, feeDrag: 0.03f,
            diversification: 0.02f);
        Assert.Equal(2, schedule.Count);
        Assert.Equal(0, schedule[0].Gen);
        Assert.True(schedule[1].Gen > schedule[0].Gen);
        Assert.Equal(1.0f, schedule[0].Sum(), 5);
        Assert.Equal(1.0f, schedule[1].Sum(), 5);
    }

    [Fact]
    public void Lerp_AtZero_ReturnsA_AtOne_ReturnsB()
    {
        var a = new WeightWaypoint(Gen: 0, Sharpe: 0.3f, Sortino: 0.7f);
        var b = new WeightWaypoint(Gen: 100, Sharpe: 0.8f, Sortino: 0.2f);

        var atZero = WeightWaypoint.Lerp(a, b, t: 0f, gen: 0);
        Assert.Equal(0.3f, atZero.Sharpe, 5);
        Assert.Equal(0.7f, atZero.Sortino, 5);

        var atOne = WeightWaypoint.Lerp(a, b, t: 1f, gen: 100);
        Assert.Equal(0.8f, atOne.Sharpe, 5);
        Assert.Equal(0.2f, atOne.Sortino, 5);
    }
}
