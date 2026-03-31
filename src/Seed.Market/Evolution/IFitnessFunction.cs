using Seed.Market.Trading;

namespace Seed.Market.Evolution;

/// <summary>
/// Abstraction over fitness computation. Allows injecting alternative fitness functions.
/// </summary>
public interface IFitnessFunction
{
    FitnessBreakdown ComputeDetailed(PortfolioState portfolio, decimal finalPrice);
}

/// <summary>
/// Default implementation using MarketFitness with Bayesian shrinkage and configurable weights.
/// </summary>
public sealed class DefaultFitnessFunction : IFitnessFunction
{
    private readonly float _shrinkageK;
    private readonly float _wSharpe, _wSortino, _wReturn, _wDdDuration, _wCVaR;
    private readonly float _inactivityPenalty;
    private readonly int _minTradesForActive;
    private readonly float _activityBonusScale;
    private readonly float _ratioClampMax;
    private readonly float _returnFloor;

    public DefaultFitnessFunction(MarketConfig config)
    {
        _shrinkageK = config.ShrinkageK;
        _wSharpe = config.FitnessSharpeWeight;
        _wSortino = config.FitnessSortinoWeight;
        _wReturn = config.FitnessReturnWeight;
        _wDdDuration = config.FitnessDrawdownDurationWeight;
        _wCVaR = config.FitnessCVaRWeight;
        _inactivityPenalty = config.InactivityPenalty;
        _minTradesForActive = config.MinTradesForActive;
        _activityBonusScale = config.ActivityBonusScale;
        _ratioClampMax = config.RatioClampMax;
        _returnFloor = config.ReturnFloor;
    }

    public DefaultFitnessFunction(float shrinkageK = 10f)
    {
        _shrinkageK = shrinkageK;
        _wSharpe = 0.45f;
        _wSortino = 0.15f;
        _wReturn = 0.20f;
        _wDdDuration = 0.10f;
        _wCVaR = 0.10f;
        _inactivityPenalty = MarketFitness.DefaultInactivityPenalty;
        _minTradesForActive = MarketFitness.DefaultMinTradesForActive;
        _activityBonusScale = 0f;
        _ratioClampMax = 10f;
        _returnFloor = -0.50f;
    }

    public FitnessBreakdown ComputeDetailed(PortfolioState portfolio, decimal finalPrice)
    {
        return MarketFitness.ComputeDetailed(portfolio, finalPrice, _shrinkageK,
            _wSharpe, _wSortino, _wReturn, _wDdDuration, _wCVaR,
            _inactivityPenalty, _minTradesForActive, _activityBonusScale,
            _ratioClampMax, _returnFloor);
    }
}
