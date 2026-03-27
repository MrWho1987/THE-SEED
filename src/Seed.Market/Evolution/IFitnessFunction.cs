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

    public DefaultFitnessFunction(MarketConfig config)
    {
        _shrinkageK = config.ShrinkageK;
        _wSharpe = config.FitnessSharpeWeight;
        _wSortino = config.FitnessSortinoWeight;
        _wReturn = config.FitnessReturnWeight;
        _wDdDuration = config.FitnessDrawdownDurationWeight;
        _wCVaR = config.FitnessCVaRWeight;
    }

    public DefaultFitnessFunction(float shrinkageK = 10f)
    {
        _shrinkageK = shrinkageK;
        _wSharpe = 0.45f;
        _wSortino = 0.15f;
        _wReturn = 0.20f;
        _wDdDuration = 0.10f;
        _wCVaR = 0.10f;
    }

    public FitnessBreakdown ComputeDetailed(PortfolioState portfolio, decimal finalPrice)
    {
        return MarketFitness.ComputeDetailed(portfolio, finalPrice, _shrinkageK,
            _wSharpe, _wSortino, _wReturn, _wDdDuration, _wCVaR);
    }
}
