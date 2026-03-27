namespace Seed.Market.Trading;

/// <summary>
/// Enforces risk limits before every trade, monitors portfolio health,
/// and optionally scales position sizes based on VaR.
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

        if (portfolio.DailyPnl < -(portfolio.InitialBalance * _config.MaxDailyLossPct))
            return (false, $"Daily loss limit exceeded: {portfolio.DailyPnl:F2}");

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

        if (portfolio.OpenPositions.Count >= _config.MaxConcurrentPositions)
            return (false, $"Max concurrent positions ({_config.MaxConcurrentPositions}) reached");

        decimal maxSize = equity * _config.MaxPositionPct;
        decimal requestedSize = maxSize * (decimal)signal.SizePct;
        if (requestedSize > maxSize)
            return (false, $"Position size {requestedSize:F2} exceeds max {maxSize:F2}");

        return (true, null);
    }

    public decimal ComputePositionSize(TradingSignal signal, PortfolioState portfolio, decimal currentPrice)
    {
        decimal equity = portfolio.Equity(currentPrice);
        decimal maxNotional = equity * _config.MaxPositionPct;
        decimal requested = maxNotional * (decimal)signal.SizePct;

        decimal varScale = ComputeVaRScale(portfolio);
        requested *= varScale;

        return Math.Min(requested, maxNotional);
    }

    /// <summary>
    /// Parametric VaR(95%) scaling. Returns 1.0 when VaR is within limits,
    /// less than 1.0 when VaR exceeds the configured threshold.
    /// </summary>
    public decimal ComputeVaRScale(PortfolioState portfolio)
    {
        var curve = portfolio.EquityCurve;
        if (curve.Count < 24)
            return 1.0m;

        int start = curve.Count - 24;
        float sumR = 0f, sumR2 = 0f;
        int n = 0;

        for (int i = start + 1; i < curve.Count; i++)
        {
            float prev = curve[i - 1];
            if (prev <= 0f) continue;
            float r = (curve[i] - prev) / prev;
            sumR += r;
            sumR2 += r * r;
            n++;
        }

        if (n < 2) return 1.0m;

        float mean = sumR / n;
        float variance = sumR2 / n - mean * mean;
        if (variance <= 0f) return 1.0m;

        float std = MathF.Sqrt(variance);
        float var95 = -(mean - 1.645f * std);

        if (var95 <= 0f) return 1.0m;
        if (var95 <= (float)_config.MaxDailyVaRPct) return 1.0m;

        return Math.Clamp((decimal)((float)_config.MaxDailyVaRPct / var95), 0.1m, 1.0m);
    }

    public void ResetDaily(PortfolioState portfolio)
    {
        portfolio.DailyPnl = 0;
        portfolio.LastResetDay = DateTimeOffset.UtcNow;
    }

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
