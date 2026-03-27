using Seed.Market.Trading;

namespace Seed.Market.Evolution;

/// <summary>
/// Computes fitness for a market agent based on portfolio performance.
/// Primary metric is net profit; penalties for excessive drawdown and inactivity.
/// </summary>
public static class MarketFitness
{
    public const float DrawdownPenaltyWeight = 0.3f;
    public const float InactivityPenalty = -0.1f;
    public const int MinTradesForActive = 3;

    public static float Compute(PortfolioState portfolio, decimal finalPrice)
    {
        // Close any remaining positions for accurate P&L
        decimal equity = portfolio.Equity(finalPrice);
        decimal pnl = equity - portfolio.InitialBalance;
        float returnPct = portfolio.InitialBalance > 0
            ? (float)(pnl / portfolio.InitialBalance)
            : 0f;

        // Inactivity check: agents that never trade are penalized
        if (portfolio.TotalTrades < MinTradesForActive)
            return InactivityPenalty;

        // Drawdown penalty: large drawdowns reduce fitness
        float drawdownPenalty = (float)portfolio.MaxDrawdown * DrawdownPenaltyWeight;

        // Risk-adjusted return (Sharpe-like)
        float fitness = returnPct - drawdownPenalty;

        return fitness;
    }

    /// <summary>
    /// Compute a more detailed fitness breakdown for logging.
    /// </summary>
    public static FitnessBreakdown ComputeDetailed(PortfolioState portfolio, decimal finalPrice)
    {
        decimal equity = portfolio.Equity(finalPrice);
        decimal pnl = equity - portfolio.InitialBalance;
        float returnPct = portfolio.InitialBalance > 0
            ? (float)(pnl / portfolio.InitialBalance)
            : 0f;

        float drawdownPenalty = (float)portfolio.MaxDrawdown * DrawdownPenaltyWeight;
        bool active = portfolio.TotalTrades >= MinTradesForActive;
        float fitness = active ? returnPct - drawdownPenalty : InactivityPenalty;

        return new FitnessBreakdown(
            Fitness: fitness,
            ReturnPct: returnPct,
            DrawdownPenalty: drawdownPenalty,
            MaxDrawdown: (float)portfolio.MaxDrawdown,
            TotalTrades: portfolio.TotalTrades,
            WinRate: portfolio.WinRate,
            NetPnl: (float)pnl,
            IsActive: active
        );
    }
}

public readonly record struct FitnessBreakdown(
    float Fitness,
    float ReturnPct,
    float DrawdownPenalty,
    float MaxDrawdown,
    int TotalTrades,
    float WinRate,
    float NetPnl,
    bool IsActive
);
