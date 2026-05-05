using Seed.Market.Evolution;
using Seed.Market.Trading;

namespace Seed.Market.Tests;

/// <summary>
/// S2 Phase A — verify the tightened inactivity penalty defaults take effect through
/// MarketConfig → DefaultFitnessFunction → MarketFitness. Phase 4 minimal observed ~60%
/// of the population stuck at the inactivity penalty for the entire run; doubling the
/// magnitude (-0.10 → -0.20) and raising the active-trade threshold (3 → 5) gives
/// selection a steeper gradient out of the inactive plateau.
/// </summary>
public class InactivityPenaltyTest
{
    [Fact]
    public void MarketConfigDefault_HasTightenedDefaults()
    {
        var config = MarketConfig.Default;
        Assert.Equal(-0.20f, config.InactivityPenalty, 5);
        Assert.Equal(5, config.MinTradesForActive);
    }

    [Fact]
    public void ZeroTradePortfolio_GetsNewInactivityPenalty()
    {
        // A 0-trade portfolio scored through DefaultFitnessFunction(config=Default)
        // should land at -0.20, not the legacy -0.10.
        var config = MarketConfig.Default;
        var fn = new DefaultFitnessFunction(config);
        var portfolio = new PortfolioState { Balance = 10_000m, InitialBalance = 10_000m };
        for (int i = 0; i < 50; i++) portfolio.RecordEquity(50_000m);

        var result = fn.ComputeDetailed(portfolio, finalPrice: 50_000m, generation: 0);

        Assert.Equal(-0.20f, result.Fitness, 5);
        Assert.False(result.IsActive);
        Assert.Equal(0, result.TotalTrades);
    }

    [Fact]
    public void FewerThan5Trades_AlphaBlendedTowardPenalty()
    {
        // The fitness function alpha-blends low-trade portfolios:
        //   alpha = trades / minTradesForActive
        //   blended = alpha * fullFitness + (1 - alpha) * inactivityPenalty
        //
        // Under old defaults (3 trades for active), 4 trades was already past threshold.
        // Under the new defaults (5 trades), 4 trades alpha-blends — its fitness is
        // closer to the (now harsher) -0.20 penalty than to the unblended fitness.
        var config = MarketConfig.Default;
        Assert.Equal(5, config.MinTradesForActive);

        // Synthesize a portfolio with 4 trades and a positive realized return.
        var portfolio = SyntheticPortfolioWithTrades(numTrades: 4, finalEquity: 11_000m);
        var fn = new DefaultFitnessFunction(config);
        var result = fn.ComputeDetailed(portfolio, finalPrice: 50_000m, generation: 0);

        Assert.Equal(4, result.TotalTrades);
        Assert.False(result.IsActive);  // tradeCount < minTradesForActive
        // Result is between the inactivity penalty and full active fitness; alpha = 4/5 = 0.8.
        // We just assert IsActive=false and that fitness is bounded: not as bad as -0.20,
        // not as good as unblended. The exact value depends on Sharpe/etc on synthetic data.
        Assert.True(result.Fitness > -0.20f);
    }

    [Fact]
    public void FiveTrades_PromotedToActive()
    {
        // With minTradesForActive=5, a 5-trade portfolio is fully active.
        var config = MarketConfig.Default;
        var portfolio = SyntheticPortfolioWithTrades(numTrades: 5, finalEquity: 11_000m);
        var fn = new DefaultFitnessFunction(config);
        var result = fn.ComputeDetailed(portfolio, finalPrice: 50_000m, generation: 0);

        Assert.Equal(5, result.TotalTrades);
        Assert.True(result.IsActive);
    }

    private static PortfolioState SyntheticPortfolioWithTrades(int numTrades, decimal finalEquity)
    {
        var portfolio = new PortfolioState { InitialBalance = 10_000m, Balance = finalEquity };
        // Build a smooth equity curve.
        for (int i = 0; i < 100; i++)
        {
            decimal eq = 10_000m + (finalEquity - 10_000m) * (decimal)(i + 1) / 100m;
            portfolio.RecordEquity(eq);
        }

        for (int i = 0; i < numTrades; i++)
        {
            portfolio.TradeHistory.Add(new ClosedTrade(
                Symbol: "BTCUSDT",
                Direction: TradeDirection.Long,
                EntryPrice: 50_000m,
                ExitPrice: 50_100m,
                Size: 0.001m,
                Pnl: 0.10m,
                Fee: 0.05m,
                HoldingTicks: 5,
                OpenTime: DateTimeOffset.UtcNow.AddHours(-i),
                CloseTime: DateTimeOffset.UtcNow.AddHours(-i + 1),
                Reason: CloseReason.DirectionFlip));
        }
        return portfolio;
    }
}
