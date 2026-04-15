namespace Seed.Market.Trading;

/// <summary>
/// Simulated trade execution with realistic slippage (volume-dependent),
/// funding rate costs, and fee modeling.
///
/// V14 additions:
/// - Fill-probability model for limit-vs-market urgency (C1)
/// - PartialClose capability (C2)
/// - Trailing stops with peak tracking (C3)
/// - Take-profit targets (C4)
/// - Brain-controlled stop-loss override (C5)
/// - Multi-position (same-direction pyramiding up to MaxConcurrentPositions) (C6)
/// - Structured CloseReason attribution (D3)
/// - MaxConcurrentSeen tracking for diversification fitness (H2)
/// </summary>
public sealed class PaperTrader : ITrader
{
    private readonly MarketConfig _config;
    private readonly RiskManager _risk;
    private float _lastFundingHour = -8f;
    private float _lastResetHour = 0f;

    /// <summary>
    /// Exposes the MarketConfig passed at construction so agents can read thresholds
    /// (e.g. StopLossPct, KillSwitchDrawdownPct) when computing self-awareness signals.
    /// </summary>
    public MarketConfig Config => _config;

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
                    foreach (var pos in portfolio.OpenPositions.ToList())
                        ClosePosition(portfolio, pos, ctx, reason: CloseReason.KillSwitch);
                    return new TradeResult(false, 0, 0, 0, 0, "Kill switch: positions closed");
                }
            }
        }

        ApplyFundingRates(portfolio, ctx);

        // V14 C3/C4/C5: walk all open positions and check brain-controlled exits.
        // Order: trailing stop → take profit → brain SL override → config SL.
        // We iterate via ToList() to allow mutation.
        foreach (var pos in portfolio.OpenPositions.ToList())
        {
            // Trailing stop
            if (pos.TrailingStopEnabled && pos.TrailingStopDistance > 0f)
            {
                bool isLong = pos.Direction == TradeDirection.Long;

                if (pos.PeakUnrealizedPrice == 0m)
                    pos.PeakUnrealizedPrice = pos.EntryPrice;
                if (isLong && ctx.Price > pos.PeakUnrealizedPrice)
                    pos.PeakUnrealizedPrice = ctx.Price;
                else if (!isLong && ctx.Price < pos.PeakUnrealizedPrice)
                    pos.PeakUnrealizedPrice = ctx.Price;

                decimal trailPrice = isLong
                    ? pos.PeakUnrealizedPrice * (1m - (decimal)pos.TrailingStopDistance)
                    : pos.PeakUnrealizedPrice * (1m + (decimal)pos.TrailingStopDistance);
                bool breached = isLong ? ctx.Price <= trailPrice : ctx.Price >= trailPrice;
                if (breached)
                {
                    ClosePosition(portfolio, pos, ctx, reason: CloseReason.TrailingStop);
                    continue;
                }
            }

            // Take profit
            if (pos.TakeProfitPrice > 0m)
            {
                bool tpHit = pos.Direction == TradeDirection.Long
                    ? ctx.Price >= pos.TakeProfitPrice
                    : ctx.Price <= pos.TakeProfitPrice;
                if (tpHit)
                {
                    ClosePosition(portfolio, pos, ctx, reason: CloseReason.TakeProfit);
                    continue;
                }
            }

            // Brain SL override — explicit price. BrainStopLoss is brain-driven so it
            // qualifies for the smart-exit bonus (separate from config-default StopLoss).
            if (pos.StopLossPrice > 0m)
            {
                bool isLong = pos.Direction == TradeDirection.Long;
                bool slHit = isLong ? ctx.Price <= pos.StopLossPrice : ctx.Price >= pos.StopLossPrice;
                if (slHit)
                {
                    ClosePosition(portfolio, pos, ctx, reason: CloseReason.BrainStopLoss);
                    continue;
                }
            }
            else if (_config.StopLossPct > 0)
            {
                // Config-driven % stop loss as fallback (reactive, not brain-driven)
                decimal unrealizedPct = pos.UnrealizedPnlPct(ctx.Price) / 100m;
                if (unrealizedPct <= -(_config.StopLossPct))
                {
                    ClosePosition(portfolio, pos, ctx, reason: CloseReason.StopLoss);
                    continue;
                }
            }
        }

        // V14 C2: partial close (applies only if position is still open and no exit signal)
        if (signal.PartialCloseFrac > ActionInterpreter.PartialCloseDeadzone
            && portfolio.OpenPositions.Count > 0
            && !signal.ExitCurrent)
        {
            var pos = portfolio.OpenPositions[0];
            return PartialClose(portfolio, pos, signal.PartialCloseFrac, ctx);
        }

        // Explicit exit signal closes the most-recent open position
        if (signal.ExitCurrent && portfolio.OpenPositions.Count > 0)
            return ClosePosition(portfolio, portfolio.OpenPositions[0], ctx, reason: CloseReason.ExitSignal);

        if (signal.Direction == TradeDirection.Flat)
            return new TradeResult(false, 0, 0, 0, 0);

        // Direction-flip: close opposite-direction positions before opening new one
        var existing = portfolio.OpenPositions.FirstOrDefault(p =>
            p.Direction != signal.Direction && p.Direction != TradeDirection.Flat);
        if (existing != null)
        {
            var closeResult = ClosePosition(portfolio, existing, ctx, reason: CloseReason.DirectionFlip);
            if (!closeResult.Executed) return closeResult;
        }

        // V14 C6: multi-position — allow same-direction pyramiding up to the concurrency cap
        if (portfolio.OpenPositions.Count >= _config.MaxConcurrentPositions)
            return new TradeResult(false, 0, 0, 0, 0, "Max concurrent positions reached");

        var (allowed, reason2) = _risk.CheckTrade(signal, portfolio, ctx.Price);
        if (!allowed)
            return new TradeResult(false, 0, 0, 0, 0, reason2);

        return OpenPosition(signal, portfolio, ctx);
    }

    private TradeResult OpenPosition(
        TradingSignal signal, PortfolioState portfolio, TickContext ctx)
    {
        decimal notional = _risk.ComputePositionSize(signal, portfolio, ctx.Price);
        if (notional <= 0)
            return new TradeResult(false, 0, 0, 0, 0, "Position size too small");

        // V14 C1: Urgency-conditioned fill-probability model
        //   urgency ≥ 0.8 → MARKET: 100% fill, full taker slippage
        //   urgency < 0.8 → LIMIT: fill probability 35% + (u/0.8)*55% = 35% @ 0, 90% @ 0.8
        // Deterministic roll uses a hash of (tick, trade count) — no wall clock.
        bool isMarket = signal.Urgency >= 0.8f;
        float fillProb = isMarket ? 1.0f : 0.35f + (signal.Urgency / 0.8f) * 0.55f;

        uint hash = (uint)(ctx.TickIndex * 2654435761u + (uint)portfolio.TradeHistory.Count);
        float roll = (hash % 10000u) / 10000f;
        if (roll > fillProb)
            return new TradeResult(false, 0, 0, 0, 0, "Limit order unfilled");

        decimal size = notional / ctx.Price;

        decimal volumeUsd = ctx.BarVolume * ctx.Price;
        decimal dynamicSlippageBps = ComputeDynamicSlippage(notional, volumeUsd);

        // Limit orders get small price improvement (fill at mid or better);
        // market orders pay full taker slippage.
        decimal slippage = isMarket
            ? ctx.Price * dynamicSlippageBps / 10000m
            : -ctx.Price * dynamicSlippageBps * 0.3m / 10000m;

        decimal fillPrice = signal.Direction == TradeDirection.Long
            ? ctx.Price + slippage
            : ctx.Price - slippage;

        decimal feeRate = isMarket ? _config.TakerFee : _config.MakerFee;
        decimal fee = fillPrice * size * feeRate;

        portfolio.Balance -= fee;
        portfolio.DailyPnl -= fee;

        // V14 C4: Take-profit target from brain output
        decimal tpPrice = 0m;
        if (signal.TakeProfitOffset > 0.001f)
        {
            tpPrice = signal.Direction == TradeDirection.Long
                ? fillPrice * (1m + (decimal)signal.TakeProfitOffset)
                : fillPrice * (1m - (decimal)signal.TakeProfitOffset);
        }

        // V14 C5: Stop-loss override from brain output, clamped to [0.5%, 5%]
        decimal slPrice = 0m;
        if (signal.StopLossOverride > 0.001f)
        {
            float clamped = Math.Clamp(signal.StopLossOverride, 0.005f, 0.05f);
            slPrice = signal.Direction == TradeDirection.Long
                ? fillPrice * (1m - (decimal)clamped)
                : fillPrice * (1m + (decimal)clamped);
        }

        portfolio.OpenPositions.Add(new Position
        {
            Symbol = _config.Symbols[0],
            Direction = signal.Direction,
            EntryPrice = fillPrice,
            Size = size,
            InitialSize = size,
            OpenTime = DateTimeOffset.UtcNow,
            OpenTick = ctx.TickIndex,
            Leverage = signal.Leverage,
            TrailingStopEnabled = signal.EnableTrailingStop,
            TrailingStopDistance = signal.TrailingStopDistance,
            TakeProfitPrice = tpPrice,
            StopLossPrice = slPrice,
            PeakUnrealizedPrice = fillPrice
        });

        // V14 H2: track max concurrent positions seen
        if (portfolio.OpenPositions.Count > portfolio.MaxConcurrentSeen)
            portfolio.MaxConcurrentSeen = portfolio.OpenPositions.Count;

        return new TradeResult(true, fillPrice, size, fee, slippage);
    }

    private TradeResult ClosePosition(
        PortfolioState portfolio, Position position, TickContext ctx,
        CloseReason reason = CloseReason.DirectionFlip)
    {
        decimal volumeUsd = ctx.BarVolume * ctx.Price;
        decimal dynamicSlippageBps = ComputeDynamicSlippage(position.Size * ctx.Price, volumeUsd);
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
            DateTimeOffset.UtcNow,
            Leverage: position.Leverage,
            Reason: reason
        ));

        return new TradeResult(true, fillPrice, position.Size, fee, slippage);
    }

    /// <summary>
    /// V14 C2: Partial close — reduces the position by the given fraction without removing it.
    /// Records a ClosedTrade for the closed portion with CloseReason.PartialClose.
    /// </summary>
    private TradeResult PartialClose(
        PortfolioState portfolio, Position position, float fraction, TickContext ctx)
    {
        fraction = Math.Clamp(fraction, 0.1f, 1.0f);
        decimal closeSize = position.Size * (decimal)fraction;
        if (closeSize <= 0m)
            return new TradeResult(false, 0, 0, 0, 0, "Partial close size too small");

        decimal volumeUsd = ctx.BarVolume * ctx.Price;
        decimal dynamicSlippageBps = ComputeDynamicSlippage(closeSize * ctx.Price, volumeUsd);
        decimal slippage = ctx.Price * dynamicSlippageBps / 10000m;
        decimal fillPrice = position.Direction == TradeDirection.Long
            ? ctx.Price - slippage
            : ctx.Price + slippage;

        decimal fee = fillPrice * closeSize * _config.TakerFee;

        decimal pnl;
        try
        {
            pnl = position.Direction == TradeDirection.Long
                ? (fillPrice - position.EntryPrice) * closeSize
                : (position.EntryPrice - fillPrice) * closeSize;
            pnl -= fee;
            portfolio.Balance += pnl;
            portfolio.DailyPnl += pnl;
        }
        catch (OverflowException)
        {
            pnl = 0m;
        }

        // Reduce remaining size on the open position
        position.Size -= closeSize;

        portfolio.TradeHistory.Add(new ClosedTrade(
            position.Symbol,
            position.Direction,
            position.EntryPrice,
            fillPrice,
            closeSize,
            pnl,
            fee,
            ctx.TickIndex - position.OpenTick,
            position.OpenTime,
            DateTimeOffset.UtcNow,
            Leverage: position.Leverage,
            Reason: CloseReason.PartialClose
        ));

        // If fully closed due to rounding, remove from open positions
        if (position.Size <= 0.0000001m)
            portfolio.OpenPositions.Remove(position);

        return new TradeResult(true, fillPrice, closeSize, fee, slippage);
    }

    private decimal ComputeDynamicSlippage(decimal orderNotional, decimal barVolumeUsd)
    {
        decimal hourlyVolume = barVolumeUsd * _config.BarsPerHour;
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

    public TradeResult ForceClose(PortfolioState portfolio, Position position, TickContext ctx)
        => ClosePosition(portfolio, position, ctx, reason: CloseReason.StopLoss);

    public void CloseAllPositions(PortfolioState portfolio, decimal currentPrice, int currentTick)
    {
        var ctx = new TickContext(currentPrice, 0m, 0f, currentTick);
        foreach (var pos in portfolio.OpenPositions.ToList())
            ClosePosition(portfolio, pos, ctx, reason: CloseReason.EndOfSession);
    }
}
