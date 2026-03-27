namespace Seed.Market.Trading;

/// <summary>
/// Simulated trade execution with realistic slippage and fee modeling.
/// Used for backtesting and paper trading modes.
/// </summary>
public sealed class PaperTrader
{
    private readonly MarketConfig _config;
    private readonly RiskManager _risk;

    public PaperTrader(MarketConfig config)
    {
        _config = config;
        _risk = new RiskManager(config);
    }

    public PortfolioState CreatePortfolio() => new()
    {
        Balance = _config.InitialCapital,
        InitialBalance = _config.InitialCapital,
        MaxEquity = _config.InitialCapital,
        LastResetDay = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Process a trading signal against the current portfolio state.
    /// Returns the trade result (may be no-op if risk limits block it).
    /// </summary>
    public TradeResult ProcessSignal(
        TradingSignal signal, PortfolioState portfolio, decimal currentPrice, int currentTick)
    {
        // Daily reset check
        if ((DateTimeOffset.UtcNow - portfolio.LastResetDay).TotalHours >= 24)
            _risk.ResetDaily(portfolio);

        _risk.UpdateWatermark(portfolio, currentPrice);

        // Handle exit signal first
        if (signal.ExitCurrent && portfolio.OpenPositions.Count > 0)
            return ClosePosition(portfolio, portfolio.OpenPositions[0], currentPrice, currentTick);

        // If signal is flat or exit-only, no new trade
        if (signal.Direction == TradeDirection.Flat)
            return new TradeResult(false, 0, 0, 0, 0);

        // Check if we already have a position in the opposite direction
        var existing = portfolio.OpenPositions.FirstOrDefault(p =>
            p.Direction != signal.Direction && p.Direction != TradeDirection.Flat);
        if (existing != null)
        {
            var closeResult = ClosePosition(portfolio, existing, currentPrice, currentTick);
            if (!closeResult.Executed) return closeResult;
        }

        // Check if already holding same direction
        if (portfolio.OpenPositions.Any(p => p.Direction == signal.Direction))
            return new TradeResult(false, 0, 0, 0, 0);

        // Risk check
        var (allowed, reason) = _risk.CheckTrade(signal, portfolio, currentPrice);
        if (!allowed)
            return new TradeResult(false, 0, 0, 0, 0, reason);

        // Open new position
        return OpenPosition(signal, portfolio, currentPrice, currentTick);
    }

    private TradeResult OpenPosition(
        TradingSignal signal, PortfolioState portfolio, decimal currentPrice, int currentTick)
    {
        decimal notional = _risk.ComputePositionSize(signal, portfolio, currentPrice);
        if (notional <= 0)
            return new TradeResult(false, 0, 0, 0, 0, "Position size too small");

        decimal size = notional / currentPrice;

        // Apply slippage
        decimal slippagePct = _config.SlippageBps / 10000m;
        decimal slippage = currentPrice * slippagePct;
        decimal fillPrice = signal.Direction == TradeDirection.Long
            ? currentPrice + slippage
            : currentPrice - slippage;

        // Apply fee (taker for market, maker for limit based on urgency)
        decimal feeRate = signal.Urgency > 0.5f ? _config.TakerFee : _config.MakerFee;
        decimal fee = fillPrice * size * feeRate;

        portfolio.Balance -= fee;
        portfolio.DailyPnl -= fee;

        portfolio.OpenPositions.Add(new Position
        {
            Symbol = _config.Symbols[0],
            Direction = signal.Direction,
            EntryPrice = fillPrice,
            Size = size,
            OpenTime = DateTimeOffset.UtcNow,
            OpenTick = currentTick
        });

        return new TradeResult(true, fillPrice, size, fee, slippage);
    }

    private TradeResult ClosePosition(
        PortfolioState portfolio, Position position, decimal currentPrice, int currentTick)
    {
        decimal slippagePct = _config.SlippageBps / 10000m;
        decimal slippage = currentPrice * slippagePct;
        decimal fillPrice = position.Direction == TradeDirection.Long
            ? currentPrice - slippage
            : currentPrice + slippage;

        decimal feeRate = _config.TakerFee;
        decimal fee = fillPrice * position.Size * feeRate;

        decimal pnl = position.Direction == TradeDirection.Long
            ? (fillPrice - position.EntryPrice) * position.Size
            : (position.EntryPrice - fillPrice) * position.Size;

        pnl -= fee;

        portfolio.Balance += pnl;
        portfolio.DailyPnl += pnl;
        portfolio.OpenPositions.Remove(position);

        portfolio.TradeHistory.Add(new ClosedTrade(
            position.Symbol,
            position.Direction,
            position.EntryPrice,
            fillPrice,
            position.Size,
            pnl,
            fee,
            currentTick - position.OpenTick,
            position.OpenTime,
            DateTimeOffset.UtcNow
        ));

        return new TradeResult(true, fillPrice, position.Size, fee, slippage);
    }

    /// <summary>
    /// Force-close all open positions (used by kill switch or end-of-backtest).
    /// </summary>
    public void CloseAllPositions(PortfolioState portfolio, decimal currentPrice, int currentTick)
    {
        foreach (var pos in portfolio.OpenPositions.ToList())
            ClosePosition(portfolio, pos, currentPrice, currentTick);
    }
}
