using Seed.Market.Trading;

namespace Seed.Market.Evolution;

/// <summary>
/// Abstraction over fitness computation. Allows injecting alternative fitness functions.
/// Generation is required so the implementation can interpolate weights from the
/// <see cref="MarketConfig.WeightSchedule"/> at the current point in training.
/// </summary>
public interface IFitnessFunction
{
    FitnessBreakdown ComputeDetailed(PortfolioState portfolio, decimal finalPrice, int generation, float hodlReturn = 0f);
}

/// <summary>
/// Default implementation. Reads the <see cref="MarketConfig.WeightSchedule"/> at construction
/// and looks up interpolated weights per call. Bayesian shrinkage on ratio-based terms is
/// applied inside <see cref="MarketFitness.ComputeDetailed"/>.
/// </summary>
public sealed class DefaultFitnessFunction : IFitnessFunction
{
    private readonly MarketConfig _config;
    private readonly float _shrinkageK;
    private readonly float _inactivityPenalty;
    private readonly int _minTradesForActive;
    private readonly float _activityBonusScale;
    private readonly float _ratioClampMax;
    private readonly float _returnFloor;
    private readonly int _barsPerHour;

    public DefaultFitnessFunction(MarketConfig config)
    {
        _config = config;
        _shrinkageK = config.ShrinkageK;
        _inactivityPenalty = config.InactivityPenalty;
        _minTradesForActive = config.MinTradesForActive;
        _activityBonusScale = config.ActivityBonusScale;
        _ratioClampMax = config.RatioClampMax;
        _returnFloor = config.ReturnFloor;
        _barsPerHour = config.BarsPerHour;
    }

    public FitnessBreakdown ComputeDetailed(PortfolioState portfolio, decimal finalPrice, int generation, float hodlReturn = 0f)
    {
        var w = _config.GetWeightsAt(generation);
        return MarketFitness.ComputeDetailed(portfolio, finalPrice, _shrinkageK,
            w.Sharpe, w.Sortino, w.Return, w.DrawdownDuration, w.CVaR,
            w.Calmar, w.InfoRatio, w.FeeDrag, w.Diversification,
            _inactivityPenalty, _minTradesForActive, _activityBonusScale,
            _ratioClampMax, _returnFloor, _barsPerHour, hodlReturn);
    }
}
