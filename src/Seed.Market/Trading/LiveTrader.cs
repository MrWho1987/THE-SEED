namespace Seed.Market.Trading;

/// <summary>
/// Real Binance order execution. Gated behind MarketConfig.ConfirmLive.
/// Wraps PaperTrader for position tracking; only the execution path is live.
/// Phase 6 implementation -- currently delegates to PaperTrader with a warning.
/// </summary>
public sealed class LiveTrader
{
    private readonly MarketConfig _config;
    private readonly PaperTrader _fallback;

    public LiveTrader(MarketConfig config)
    {
        _config = config;
        _fallback = new PaperTrader(config);

        if (!config.ConfirmLive)
            throw new InvalidOperationException(
                "Live trading requires ConfirmLive=true in config. " +
                "This is a safety gate. Set it explicitly when you are ready.");
    }

    public PortfolioState CreatePortfolio() => _fallback.CreatePortfolio();

    public TradeResult ProcessSignal(
        TradingSignal signal, PortfolioState portfolio, decimal currentPrice, int currentTick)
    {
        // TODO: Replace with actual Binance REST API calls in Phase 6.
        // For now, use paper trader with a log warning.
        Console.WriteLine($"[LIVE] Would execute: {signal.Direction} {signal.SizePct:P0} at {currentPrice}");
        return _fallback.ProcessSignal(signal, portfolio, currentPrice, currentTick);
    }

    public void CloseAllPositions(PortfolioState portfolio, decimal currentPrice, int currentTick)
    {
        _fallback.CloseAllPositions(portfolio, currentPrice, currentTick);
    }
}
