using Seed.Market.Trading;

namespace Seed.Market.Evolution;

/// <summary>
/// Abstraction over fitness computation. Allows injecting alternative fitness functions.
/// </summary>
public interface IFitnessFunction
{
    FitnessBreakdown ComputeDetailed(PortfolioState portfolio, decimal finalPrice, float hodlReturn = 0f);
}

/// <summary>
/// Default implementation using MarketFitness with Bayesian shrinkage and configurable weights.
/// </summary>
public sealed class DefaultFitnessFunction : IFitnessFunction
{
    private readonly float _shrinkageK;
    private readonly float _wSharpe, _wSortino, _wReturn, _wDdDuration, _wCVaR;
    private readonly float _wCalmar, _wInfoRatio, _wFeeDrag, _wDiversification;
    private readonly float _inactivityPenalty;
    private readonly int _minTradesForActive;
    private readonly float _activityBonusScale;
    private readonly float _ratioClampMax;
    private readonly float _returnFloor;
    private readonly int _barsPerHour;

    public DefaultFitnessFunction(MarketConfig config)
    {
        _shrinkageK = config.ShrinkageK;
        _wSharpe = config.FitnessSharpeWeight;
        _wSortino = config.FitnessSortinoWeight;
        _wReturn = config.FitnessReturnWeight;
        _wDdDuration = config.FitnessDrawdownDurationWeight;
        _wCVaR = config.FitnessCVaRWeight;
        _wCalmar = config.FitnessCalmarWeight;
        _wInfoRatio = config.FitnessInfoRatioWeight;
        _wFeeDrag = config.FitnessFeeDragWeight;
        _wDiversification = config.FitnessDiversificationWeight;
        _inactivityPenalty = config.InactivityPenalty;
        _minTradesForActive = config.MinTradesForActive;
        _activityBonusScale = config.ActivityBonusScale;
        _ratioClampMax = config.RatioClampMax;
        _returnFloor = config.ReturnFloor;
        _barsPerHour = config.BarsPerHour;
    }

    public DefaultFitnessFunction(float shrinkageK = 10f)
    {
        _shrinkageK = shrinkageK;
        _wSharpe = 0.22f;
        _wSortino = 0.13f;
        _wReturn = 0.20f;
        _wDdDuration = 0.13f;
        _wCVaR = 0.17f;
        _wCalmar = 0.05f;
        _wInfoRatio = 0.05f;
        _wFeeDrag = 0.03f;
        _wDiversification = 0.02f;
        _inactivityPenalty = MarketFitness.DefaultInactivityPenalty;
        _minTradesForActive = MarketFitness.DefaultMinTradesForActive;
        _activityBonusScale = 0f;
        _ratioClampMax = 10f;
        _returnFloor = -0.50f;
        _barsPerHour = 1;
    }

    public FitnessBreakdown ComputeDetailed(PortfolioState portfolio, decimal finalPrice, float hodlReturn = 0f)
    {
        return MarketFitness.ComputeDetailed(portfolio, finalPrice, _shrinkageK,
            _wSharpe, _wSortino, _wReturn, _wDdDuration, _wCVaR,
            _wCalmar, _wInfoRatio, _wFeeDrag, _wDiversification,
            _inactivityPenalty, _minTradesForActive, _activityBonusScale,
            _ratioClampMax, _returnFloor, _barsPerHour, hodlReturn);
    }
}
