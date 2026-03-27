namespace Seed.Market.Trading;

/// <summary>
/// Enforces risk limits before every trade and monitors portfolio health.
/// Can trigger a kill switch that halts all trading.
/// </summary>
public sealed class RiskManager
{
    private readonly MarketConfig _config;

    public RiskManager(MarketConfig config)
    {
        _config = config;
    }

    public (bool Allowed, string? Reason) CheckTrade(
        TradingSignal signal, PortfolioState portfolio, decimal currentPrice)
    {
        if (portfolio.KillSwitchTriggered)
            return (false, "Kill switch active — trading halted");

        // Check daily loss limit
        if (portfolio.DailyPnl < -(portfolio.InitialBalance * _config.MaxDailyLossPct))
            return (false, $"Daily loss limit exceeded: {portfolio.DailyPnl:F2}");

        // Check drawdown kill switch
        decimal equity = portfolio.Equity(currentPrice);
        if (portfolio.MaxEquity > 0)
        {
            decimal drawdown = (portfolio.MaxEquity - equity) / portfolio.MaxEquity;
            if (drawdown > _config.KillSwitchDrawdownPct)
            {
                portfolio.KillSwitchTriggered = true;
                return (false, $"Kill switch: drawdown {drawdown:P1} exceeds {_config.KillSwitchDrawdownPct:P1}");
            }
        }

        if (signal.ExitCurrent)
            return (true, null);

        if (signal.Direction == TradeDirection.Flat)
            return (true, null);

        // Check max concurrent positions
        if (portfolio.OpenPositions.Count >= _config.MaxConcurrentPositions)
            return (false, $"Max concurrent positions ({_config.MaxConcurrentPositions}) reached");

        // Check position size limit
        decimal maxSize = equity * _config.MaxPositionPct;
        decimal requestedSize = maxSize * (decimal)signal.SizePct;
        if (requestedSize > maxSize)
            return (false, $"Position size {requestedSize:F2} exceeds max {maxSize:F2}");

        return (true, null);
    }

    /// <summary>
    /// Compute the actual position size in quote currency, respecting limits.
    /// </summary>
    public decimal ComputePositionSize(TradingSignal signal, PortfolioState portfolio, decimal currentPrice)
    {
        decimal equity = portfolio.Equity(currentPrice);
        decimal maxNotional = equity * _config.MaxPositionPct;
        decimal requested = maxNotional * (decimal)signal.SizePct;
        return Math.Min(requested, maxNotional);
    }

    /// <summary>
    /// Reset daily P&L tracking (call at market close or midnight UTC).
    /// </summary>
    public void ResetDaily(PortfolioState portfolio)
    {
        portfolio.DailyPnl = 0;
        portfolio.LastResetDay = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Update max equity watermark for drawdown tracking.
    /// </summary>
    public void UpdateWatermark(PortfolioState portfolio, decimal currentPrice)
    {
        decimal equity = portfolio.Equity(currentPrice);
        if (equity > portfolio.MaxEquity)
            portfolio.MaxEquity = equity;

        decimal drawdown = portfolio.MaxEquity > 0
            ? (portfolio.MaxEquity - equity) / portfolio.MaxEquity
            : 0m;
        if (drawdown > portfolio.MaxDrawdown)
            portfolio.MaxDrawdown = drawdown;
    }
}
