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

public sealed class Position
{
    public string Symbol { get; init; } = "BTCUSDT";
    public TradeDirection Direction { get; init; }
    public decimal EntryPrice { get; init; }
    public decimal Size { get; init; }
    public decimal StopPrice { get; init; }
    public DateTimeOffset OpenTime { get; init; }
    public int OpenTick { get; init; }
    public float Leverage { get; init; } = 1.0f;  // leverage applied when the position opened (v2+)

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
    float SizePct,           // 0-1, fraction of max allowed position
    float Urgency,           // 0-1, higher = market order, lower = limit
    bool ExitCurrent,        // close existing position
    float RawExitValue = 0f, // sigmoid(outputs[3]); ExitCurrent == (RawExitValue > ExitThreshold)
    float Leverage = 1.0f,   // per-trade leverage multiplier from outputs[5], in [1, MaxLeverage]
    float RawSizePct = 0f,   // sigmoid(outputs[1]) before clamping; diagnostic for dormancy detection
    float RawLeverage = 0f   // sigmoid(outputs[5]) before scaling; diagnostic
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
    bool ClosedByExitSignal = false,  // true = closed via brain's explicit exit output; false = flip/stop/kill
    float Leverage = 1.0f             // leverage used at time of open (for analytics)
);
