namespace Seed.Market.Trading;

public enum TradeDirection
{
    Flat = 0,
    Long = 1,
    Short = -1
}

public enum OrderType
{
    Market,
    Limit
}

/// <summary>
/// Reasons a position can be closed. Used for attribution, reward shaping, and
/// distinguishing brain-driven "smart exits" from reactive/protective closes.
///
/// Brain-driven (each is a distinct action output): ExitSignal, TakeProfit, TrailingStop,
/// BrainStopLoss, PartialClose. The reward system grants a small bootstrap bonus
/// whenever one of these fires, so the brain learns to USE outputs 3 and 6-10.
///
/// Reactive/protective (not brain-driven): DirectionFlip (no explicit exit), StopLoss
/// (config fallback), KillSwitch (safety layer), EndOfSession (forced close).
/// </summary>
public enum CloseReason
{
    DirectionFlip,     // flipped to opposite direction — not a smart exit
    ExitSignal,        // brain's explicit exit output[3] fired
    StopLoss,          // protective config-default SL hit (not brain-driven)
    TakeProfit,        // brain-set TP target (output[9]) hit
    TrailingStop,      // brain-enabled trailing stop (output[7]/[8]) swept
    KillSwitch,        // portfolio-level kill switch (not brain-driven)
    EndOfSession,      // forced close at backtest end
    PartialClose,      // brain-initiated partial size reduction (output[6])
    BrainStopLoss      // brain-set SL override (output[10]) hit
}

/// <summary>
/// Returns true if the close was driven by the brain explicitly USING one of its
/// action outputs (3 or 6-10). Used to bootstrap learning of smart-exit behaviors.
/// </summary>
public static class CloseReasonExtensions
{
    public static bool IsBrainDrivenExit(this CloseReason reason) => reason switch
    {
        CloseReason.ExitSignal => true,
        CloseReason.TakeProfit => true,
        CloseReason.TrailingStop => true,
        CloseReason.BrainStopLoss => true,
        CloseReason.PartialClose => true,
        _ => false
    };
}

public sealed class Position
{
    public string Symbol { get; init; } = "BTCUSDT";
    public TradeDirection Direction { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal Size { get; set; }                // V14: mutable for partial closes
    public decimal InitialSize { get; init; }        // V14: original size at open time
    public decimal StopPrice { get; init; }
    public DateTimeOffset OpenTime { get; init; }
    public int OpenTick { get; init; }
    public float Leverage { get; init; } = 1.0f;    // leverage applied at position open (for analytics)

    // V14 brain-controlled exit management
    public bool TrailingStopEnabled { get; init; }
    public float TrailingStopDistance { get; init; }     // 0.005 = 0.5% trail distance
    public decimal PeakUnrealizedPrice { get; set; }     // tracks peak for trailing stop
    public decimal TakeProfitPrice { get; init; }        // 0 = no TP set
    public decimal StopLossPrice { get; init; }          // 0 = use config default

    public decimal UnrealizedPnl(decimal currentPrice) =>
        Direction == TradeDirection.Long
            ? (currentPrice - EntryPrice) * Size
            : (EntryPrice - currentPrice) * Size;

    public decimal UnrealizedPnlPct(decimal currentPrice) =>
        EntryPrice > 0
            ? UnrealizedPnl(currentPrice) / (EntryPrice * Size) * 100m
            : 0m;
}

public readonly record struct TradingSignal(
    TradeDirection Direction,
    float SizePct,                     // 0-1, fraction of max allowed position
    float Urgency,                     // 0-1, higher = market order, lower = limit
    bool ExitCurrent,                  // close existing position
    float RawExitValue = 0f,           // sigmoid(outputs[3]); ExitCurrent == (RawExitValue > ExitThreshold)
    float Leverage = 1.0f,             // per-trade leverage multiplier from outputs[5], in [1, MaxLeverage]
    float RawSizePct = 0f,             // sigmoid(outputs[1]) before clamping; diagnostic for dormancy detection
    float RawLeverage = 0f,            // sigmoid(outputs[5]) before scaling; diagnostic
    // V14 action-space expansion (outputs 6-10):
    float PartialCloseFrac = 0f,       // [0, 1] — closes this fraction of the current position when > 0.2
    bool EnableTrailingStop = false,   // whether to attach a trailing stop on open
    float TrailingStopDistance = 0f,   // [0.005, 0.10] log-scaled trail distance
    float TakeProfitOffset = 0f,       // [0.005, 0.15] log-scaled TP offset from entry
    float StopLossOverride = 0f        // [0.005, 0.05] log-scaled SL override; 0 = use config default
);

public readonly record struct TradeResult(
    bool Executed,
    decimal FillPrice,
    decimal Size,
    decimal Fee,
    decimal Slippage,
    string? Error = null
);

public sealed class PortfolioState
{
    public decimal Balance { get; set; }
    public decimal InitialBalance { get; init; }
    public List<Position> OpenPositions { get; } = [];
    public List<ClosedTrade> TradeHistory { get; } = [];
    public List<float> EquityCurve { get; } = [];
    public decimal DailyPnl { get; set; }
    public decimal MaxEquity { get; set; }
    public decimal MaxDrawdown { get; set; }
    public DateTimeOffset LastResetDay { get; set; }
    public bool KillSwitchTriggered { get; set; }
    public int MaxConcurrentSeen { get; set; }  // Max concurrent positions observed (for diversification fitness)

    public decimal Equity(decimal currentPrice)
    {
        decimal unrealized = 0;
        foreach (var p in OpenPositions)
            unrealized += p.UnrealizedPnl(currentPrice);
        return Balance + unrealized;
    }

    public void RecordEquity(decimal currentPrice)
    {
        EquityCurve.Add((float)Equity(currentPrice));
    }

    public decimal TotalPnl => Balance - InitialBalance;
    public int TotalTrades => TradeHistory.Count;
    public int WinningTrades => TradeHistory.Count(t => t.Pnl > 0);
    public float WinRate => TotalTrades > 0 ? (float)WinningTrades / TotalTrades : 0f;
}

public readonly record struct TickContext(
    decimal Price,
    decimal BarVolume,
    float FundingRate,
    int TickIndex,
    float ElapsedHours = 0f
);

public readonly record struct ClosedTrade(
    string Symbol,
    TradeDirection Direction,
    decimal EntryPrice,
    decimal ExitPrice,
    decimal Size,
    decimal Pnl,
    decimal Fee,
    int HoldingTicks,
    DateTimeOffset OpenTime,
    DateTimeOffset CloseTime,
    float Leverage = 1.0f,                           // leverage used at time of open (for analytics)
    CloseReason Reason = CloseReason.DirectionFlip   // structured close reason; drives brain-driven-exit bonus
)
{
    /// <summary>
    /// Derived helper: was this trade closed by the brain deliberately using one of its
    /// action outputs? (output[3] explicit exit, output[6] partial, output[7/8] trail,
    /// output[9] TP, output[10] SL override). Used by reward shaping to bootstrap learning
    /// of the V11 action-space outputs.
    /// </summary>
    public bool IsBrainDrivenExit => Reason.IsBrainDrivenExit();
}
