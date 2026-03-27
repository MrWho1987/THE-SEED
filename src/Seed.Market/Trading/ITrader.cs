namespace Seed.Market.Trading;

/// <summary>
/// Abstraction over trade execution. Allows swapping PaperTrader/LiveTrader/EnsembleTrader.
/// </summary>
public interface ITrader
{
    PortfolioState CreatePortfolio();
    TradeResult ProcessSignal(TradingSignal signal, PortfolioState portfolio, TickContext ctx);
    void CloseAllPositions(PortfolioState portfolio, decimal currentPrice, int currentTick);
}
