namespace Seed.Market.Trading;

/// <summary>
/// Simulated trade execution with realistic slippage (volume-dependent),
/// funding rate costs, and fee modeling.
/// </summary>
public sealed class PaperTrader : ITrader
{
    private readonly MarketConfig _config;
    private readonly RiskManager _risk;
    private float _lastFundingHour = -8f;
    private float _lastResetHour = 0f;

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

    public TradeResult ProcessSignal(
        TradingSignal signal, PortfolioState portfolio, decimal currentPrice, int currentTick)
    {
        return ProcessSignal(signal, portfolio,
            new TickContext(currentPrice, 0m, 0f, currentTick));
    }

    public TradeResult ProcessSignal(
        TradingSignal signal, PortfolioState portfolio, TickContext ctx)
    {
        if (ctx.ElapsedHours - _lastResetHour >= 24f)
        {
            _risk.ResetDaily(portfolio);
            _lastResetHour = ctx.ElapsedHours;
        }

        _risk.UpdateWatermark(portfolio, ctx.Price);

        if (!portfolio.KillSwitchTriggered && portfolio.OpenPositions.Count > 0)
        {
            decimal equity = portfolio.Equity(ctx.Price);
            if (portfolio.MaxEquity > 0)
            {
                decimal drawdown = (portfolio.MaxEquity - equity) / portfolio.MaxEquity;
                if (drawdown > _config.KillSwitchDrawdownPct)
                {
                    portfolio.KillSwitchTriggered = true;
                    var closedCount = portfolio.OpenPositions.Count;
                    foreach (var pos in portfolio.OpenPositions.ToList())
                        ClosePosition(portfolio, pos, ctx);
                    return new TradeResult(false, 0, 0, 0, 0, "Kill switch: positions closed");
                }
            }
        }

        ApplyFundingRates(portfolio, ctx);

        if (signal.ExitCurrent && portfolio.OpenPositions.Count > 0)
            return ClosePosition(portfolio, portfolio.OpenPositions[0], ctx);

        if (signal.Direction == TradeDirection.Flat)
            return new TradeResult(false, 0, 0, 0, 0);

        var existing = portfolio.OpenPositions.FirstOrDefault(p =>
            p.Direction != signal.Direction && p.Direction != TradeDirection.Flat);
        if (existing != null)
        {
            var closeResult = ClosePosition(portfolio, existing, ctx);
            if (!closeResult.Executed) return closeResult;
        }

        if (portfolio.OpenPositions.Any(p => p.Direction == signal.Direction))
            return new TradeResult(false, 0, 0, 0, 0);

        var (allowed, reason) = _risk.CheckTrade(signal, portfolio, ctx.Price);
        if (!allowed)
            return new TradeResult(false, 0, 0, 0, 0, reason);

        return OpenPosition(signal, portfolio, ctx);
    }

    private TradeResult OpenPosition(
        TradingSignal signal, PortfolioState portfolio, TickContext ctx)
    {
        decimal notional = _risk.ComputePositionSize(signal, portfolio, ctx.Price);
        if (notional <= 0)
            return new TradeResult(false, 0, 0, 0, 0, "Position size too small");

        decimal size = notional / ctx.Price;

        decimal dynamicSlippageBps = ComputeDynamicSlippage(notional, ctx.HourlyVolume);
        decimal slippage = ctx.Price * dynamicSlippageBps / 10000m;
        decimal fillPrice = signal.Direction == TradeDirection.Long
            ? ctx.Price + slippage
            : ctx.Price - slippage;

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
            OpenTick = ctx.TickIndex
        });

        return new TradeResult(true, fillPrice, size, fee, slippage);
    }

    private TradeResult ClosePosition(
        PortfolioState portfolio, Position position, TickContext ctx)
    {
        decimal dynamicSlippageBps = ComputeDynamicSlippage(position.Size * ctx.Price, ctx.HourlyVolume);
        decimal slippage = ctx.Price * dynamicSlippageBps / 10000m;
        decimal fillPrice = position.Direction == TradeDirection.Long
            ? ctx.Price - slippage
            : ctx.Price + slippage;

        decimal feeRate = _config.TakerFee;
        decimal fee = fillPrice * position.Size * feeRate;

        decimal pnl;
        try
        {
            pnl = position.Direction == TradeDirection.Long
                ? (fillPrice - position.EntryPrice) * position.Size
                : (position.EntryPrice - fillPrice) * position.Size;
            pnl -= fee;
            portfolio.Balance += pnl;
            portfolio.DailyPnl += pnl;
        }
        catch (OverflowException)
        {
            pnl = 0;
        }
        portfolio.OpenPositions.Remove(position);

        portfolio.TradeHistory.Add(new ClosedTrade(
            position.Symbol,
            position.Direction,
            position.EntryPrice,
            fillPrice,
            position.Size,
            pnl,
            fee,
            ctx.TickIndex - position.OpenTick,
            position.OpenTime,
            DateTimeOffset.UtcNow
        ));

        return new TradeResult(true, fillPrice, position.Size, fee, slippage);
    }

    private decimal ComputeDynamicSlippage(decimal orderNotional, decimal hourlyVolume)
    {
        if (hourlyVolume <= 0m)
            return _config.SlippageBps;

        try
        {
            decimal participation = orderNotional / (hourlyVolume * 0.01m);
            if (participation > 100m) participation = 100m;
            decimal multiplier = Math.Min(1m + participation * participation, 20m);
            return _config.SlippageBps * multiplier;
        }
        catch (OverflowException)
        {
            return _config.SlippageBps * 20m;
        }
    }

    private void ApplyFundingRates(PortfolioState portfolio, TickContext ctx)
    {
        if (ctx.FundingRate == 0f)
            return;

        int prevFundingSlot = (int)(_lastFundingHour / 8f);
        int currFundingSlot = (int)(ctx.ElapsedHours / 8f);
        if (currFundingSlot <= prevFundingSlot)
            return;

        _lastFundingHour = ctx.ElapsedHours;

        foreach (var pos in portfolio.OpenPositions)
        {
            try
            {
                decimal fundingCost = pos.Size * pos.EntryPrice * (decimal)ctx.FundingRate;
                if (pos.Direction == TradeDirection.Long)
                    portfolio.Balance -= fundingCost;
                else
                    portfolio.Balance += fundingCost;
                portfolio.DailyPnl -= (pos.Direction == TradeDirection.Long ? fundingCost : -fundingCost);
            }
            catch (OverflowException) { }
        }
    }

    public void CloseAllPositions(PortfolioState portfolio, decimal currentPrice, int currentTick)
    {
        var ctx = new TickContext(currentPrice, 0m, 0f, currentTick);
        foreach (var pos in portfolio.OpenPositions.ToList())
            ClosePosition(portfolio, pos, ctx);
    }
}
