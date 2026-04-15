using Seed.Market.Trading;

namespace Seed.Market.Tests;

/// <summary>
/// Unit tests for Tier 3 action-space expansion: 11-output ActionInterpreter (D1),
/// TradingSignal/Position extensions (D2), CloseReason enum (D3),
/// fill-probability (C1), partial close (C2), trailing stop (C3), TP (C4),
/// SL override (C5), multi-position (C6), and MaxConcurrentSeen tracking (H2).
/// </summary>
public class Tier3ActionSpaceTests
{
    // ── D1: ActionInterpreter 11 outputs ───────────────────────────────────

    [Fact]
    public void ActionInterpreter_OutputCount_Is11()
    {
        Assert.Equal(11, ActionInterpreter.OutputCount);
    }

    [Fact]
    public void ActionInterpreter_ShortOutputs_NoThrow()
    {
        // Fewer than 11 outputs should gracefully default the missing ones
        float[] shortOutputs = [0.2f, 0.5f, 0.5f];
        var signal = ActionInterpreter.Interpret(shortOutputs);
        Assert.Equal(TradeDirection.Long, signal.Direction);
        Assert.Equal(0f, signal.PartialCloseFrac);
        Assert.False(signal.EnableTrailingStop);
    }

    [Fact]
    public void ActionInterpreter_PartialClose_DeadZone_Suppresses()
    {
        // V11d: PartialCloseDeadzone = 0.8 → sigmoid output below that yields 0
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, -10f, 0f, 0f, 0f, 0f]; // sigmoid(-10) ≈ 0
        var signal = ActionInterpreter.Interpret(outputs);
        Assert.Equal(0f, signal.PartialCloseFrac);
    }

    [Fact]
    public void ActionInterpreter_PartialClose_AboveThreshold_Activates()
    {
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 10f, 0f, 0f, 0f, 0f]; // sigmoid(10) ≈ 1
        var signal = ActionInterpreter.Interpret(outputs);
        Assert.True(signal.PartialCloseFrac > ActionInterpreter.PartialCloseDeadzone);
    }

    [Fact]
    public void ActionInterpreter_TrailingStop_EnableThreshold()
    {
        // Below 0.5 → disabled
        float[] outputsOff = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 0f, -10f, 0f, 0f, 0f];
        var signalOff = ActionInterpreter.Interpret(outputsOff);
        Assert.False(signalOff.EnableTrailingStop);

        // Above 0.5 → enabled
        float[] outputsOn = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 0f, 10f, 0f, 0f, 0f];
        var signalOn = ActionInterpreter.Interpret(outputsOn);
        Assert.True(signalOn.EnableTrailingStop);
    }

    [Fact]
    public void ActionInterpreter_TrailingStop_LogScaleRange()
    {
        // Mid output (0.5) for trail distance → geometric mean of [0.005, 0.10] ≈ 0.0224
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 0f, 10f, 0f, 0f, 0f]; // enable=on, dist=mid
        var signal = ActionInterpreter.Interpret(outputs);
        Assert.True(signal.EnableTrailingStop);
        // Verify the trail distance is within the log-scaled range [0.005, 0.10]
        Assert.InRange(signal.TrailingStopDistance, 0.003f, 0.12f);
    }

    [Fact]
    public void ActionInterpreter_TpOffset_LogScaleRange()
    {
        // With tp output strong positive → approach max tp offset 15%
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 10f, 0f];
        var signal = ActionInterpreter.Interpret(outputs);
        Assert.InRange(signal.TakeProfitOffset, 0.10f, 0.20f);
    }

    [Fact]
    public void ActionInterpreter_SlOverride_LogScaleRange()
    {
        float[] outputs = [0.5f, 0.5f, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 10f];
        var signal = ActionInterpreter.Interpret(outputs);
        Assert.InRange(signal.StopLossOverride, 0.03f, 0.07f);
    }

    // ── D3: CloseReason enum ───────────────────────────────────────────────

    [Fact]
    public void CloseReason_HasAllNineValues()
    {
        var reasons = Enum.GetValues<CloseReason>();
        Assert.Equal(9, reasons.Length);
        Assert.Contains(CloseReason.DirectionFlip, reasons);
        Assert.Contains(CloseReason.ExitSignal, reasons);
        Assert.Contains(CloseReason.StopLoss, reasons);
        Assert.Contains(CloseReason.BrainStopLoss, reasons);
        Assert.Contains(CloseReason.TakeProfit, reasons);
        Assert.Contains(CloseReason.TrailingStop, reasons);
        Assert.Contains(CloseReason.KillSwitch, reasons);
        Assert.Contains(CloseReason.EndOfSession, reasons);
        Assert.Contains(CloseReason.PartialClose, reasons);
    }

    // ── C1: Fill probability ───────────────────────────────────────────────

    [Fact]
    public void FillProbability_MarketOrder_AlwaysFills()
    {
        var config = MarketConfig.Default with { MaxPositionPct = 0.5m };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();
        portfolio.MaxEquity = 10000m;

        // urgency=0.9 → market order → 100% fill
        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
        var ctx = new TickContext(50000m, 1000m, 0f, 0, 0f);
        var result = trader.ProcessSignal(signal, portfolio, ctx);
        Assert.True(result.Executed, $"Market order must fill, got error: {result.Error}");
    }

    [Fact]
    public void FillProbability_LimitOrder_MayOrMayNotFill()
    {
        // urgency=0.1 → limit order → 35% + (0.1/0.8)*55% = 42% fill probability
        var config = MarketConfig.Default with { MaxPositionPct = 0.5m };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();
        portfolio.MaxEquity = 10000m;

        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.1f, false);
        // Deterministic so same tick => same result
        var ctx = new TickContext(50000m, 1000m, 0f, 0, 0f);
        var first = trader.ProcessSignal(signal, portfolio, ctx);
        // We don't assert executed=true or false, but the call must be valid
        Assert.True(first.Executed || !string.IsNullOrEmpty(first.Error));
    }

    [Fact]
    public void FillProbability_Deterministic()
    {
        // Same config, same tick, same trade count → same fill decision
        var config = MarketConfig.Default with { MaxPositionPct = 0.5m };
        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.3f, false);
        var ctx = new TickContext(50000m, 1000m, 0f, 42, 0f);

        var traderA = new PaperTrader(config);
        var portA = traderA.CreatePortfolio();
        portA.MaxEquity = 10000m;
        var resultA = traderA.ProcessSignal(signal, portA, ctx);

        var traderB = new PaperTrader(config);
        var portB = traderB.CreatePortfolio();
        portB.MaxEquity = 10000m;
        var resultB = traderB.ProcessSignal(signal, portB, ctx);

        Assert.Equal(resultA.Executed, resultB.Executed);
    }

    // ── C2: Partial close ──────────────────────────────────────────────────

    [Fact]
    public void PartialClose_ReducesPositionSize()
    {
        var config = MarketConfig.Default with { MaxPositionPct = 0.5m };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();
        portfolio.MaxEquity = 10000m;

        // Open a position
        var openSignal = new TradingSignal(TradeDirection.Long, 1.0f, 0.9f, false);
        trader.ProcessSignal(openSignal, portfolio, new TickContext(50000m, 1000m, 0f, 0, 0f));
        Assert.Single(portfolio.OpenPositions);
        decimal initialSize = portfolio.OpenPositions[0].Size;

        // V11d: deadzone raised to 0.8, so partial close needs frac > 0.8 to fire
        var partialSignal = new TradingSignal(
            TradeDirection.Flat, 0f, 0f, false,
            PartialCloseFrac: 0.9f);
        trader.ProcessSignal(partialSignal, portfolio, new TickContext(50500m, 1000m, 0f, 1, 0f));

        Assert.Single(portfolio.OpenPositions);
        decimal newSize = portfolio.OpenPositions[0].Size;
        Assert.True(newSize < initialSize, $"Size should decrease: {newSize} vs {initialSize}");
        Assert.True(newSize > 0m, "Size should still be positive after partial close");
        Assert.Single(portfolio.TradeHistory);
        Assert.Equal(CloseReason.PartialClose, portfolio.TradeHistory[0].Reason);
    }

    // ── C3: Trailing stop ──────────────────────────────────────────────────

    [Fact]
    public void TrailingStop_ClosesOnBreach()
    {
        var config = MarketConfig.Default with { MaxPositionPct = 0.5m, StopLossPct = 0m };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();
        portfolio.MaxEquity = 10000m;

        // Open with trailing stop enabled at 1% distance
        var openSignal = new TradingSignal(
            TradeDirection.Long, 1.0f, 0.9f, false,
            EnableTrailingStop: true,
            TrailingStopDistance: 0.01f);
        var openResult = trader.ProcessSignal(openSignal, portfolio, new TickContext(50000m, 1000m, 0f, 0, 0f));
        Assert.True(openResult.Executed);
        Assert.Single(portfolio.OpenPositions);
        Assert.True(portfolio.OpenPositions[0].TrailingStopEnabled);

        // Price rises 5% → peak tracks
        var flatSignal = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        trader.ProcessSignal(flatSignal, portfolio, new TickContext(52500m, 1000m, 0f, 1, 0f));
        Assert.Single(portfolio.OpenPositions);

        // Price drops from 52500 → 51900 (more than 1% trail) → should trigger trailing stop
        trader.ProcessSignal(flatSignal, portfolio, new TickContext(51800m, 1000m, 0f, 2, 0f));
        Assert.Empty(portfolio.OpenPositions);
        Assert.Single(portfolio.TradeHistory);
        Assert.Equal(CloseReason.TrailingStop, portfolio.TradeHistory[0].Reason);
    }

    // ── C4: Take-profit ────────────────────────────────────────────────────

    [Fact]
    public void TakeProfit_ClosesAtTarget()
    {
        var config = MarketConfig.Default with { MaxPositionPct = 0.5m, StopLossPct = 0m };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();
        portfolio.MaxEquity = 10000m;

        // Open with 2% TP
        var openSignal = new TradingSignal(
            TradeDirection.Long, 1.0f, 0.9f, false,
            TakeProfitOffset: 0.02f);
        trader.ProcessSignal(openSignal, portfolio, new TickContext(50000m, 1000m, 0f, 0, 0f));
        Assert.Single(portfolio.OpenPositions);

        // Price rises to 50000 * 1.02 = 51000
        var flatSignal = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        trader.ProcessSignal(flatSignal, portfolio, new TickContext(51100m, 1000m, 0f, 1, 0f));

        Assert.Empty(portfolio.OpenPositions);
        Assert.Single(portfolio.TradeHistory);
        Assert.Equal(CloseReason.TakeProfit, portfolio.TradeHistory[0].Reason);
    }

    // ── C5: Stop-loss override ────────────────────────────────────────────

    [Fact]
    public void StopLossOverride_UsedWhenSet()
    {
        var config = MarketConfig.Default with { MaxPositionPct = 0.5m, StopLossPct = 0.10m };  // config=10%
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();
        portfolio.MaxEquity = 10000m;

        // Open with tighter 1% brain-controlled SL override
        var openSignal = new TradingSignal(
            TradeDirection.Long, 1.0f, 0.9f, false,
            StopLossOverride: 0.01f);
        trader.ProcessSignal(openSignal, portfolio, new TickContext(50000m, 1000m, 0f, 0, 0f));

        // Price drops 1.5% → brain's SL should fire (config's 10% wouldn't)
        var flatSignal = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        trader.ProcessSignal(flatSignal, portfolio, new TickContext(49200m, 1000m, 0f, 1, 0f));

        Assert.Empty(portfolio.OpenPositions);
        Assert.Single(portfolio.TradeHistory);
        // Brain-set SL uses BrainStopLoss (distinct from config-default StopLoss)
        Assert.Equal(CloseReason.BrainStopLoss, portfolio.TradeHistory[0].Reason);
        Assert.True(portfolio.TradeHistory[0].IsBrainDrivenExit,
            "BrainStopLoss must be marked as brain-driven");
    }

    // ── C6: Multi-position ─────────────────────────────────────────────────

    [Fact]
    public void MultiPosition_AllowsSameDirectionPyramiding()
    {
        var config = MarketConfig.Default with
        {
            MaxConcurrentPositions = 3,
            MaxPositionPct = 0.1m,
            StopLossPct = 0m
        };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();
        portfolio.MaxEquity = 10000m;

        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);

        trader.ProcessSignal(signal, portfolio, new TickContext(50000m, 1000m, 0f, 0, 0f));
        trader.ProcessSignal(signal, portfolio, new TickContext(50100m, 1000m, 0f, 1, 0f));
        trader.ProcessSignal(signal, portfolio, new TickContext(50200m, 1000m, 0f, 2, 0f));

        // All three should be open (same direction allowed)
        Assert.Equal(3, portfolio.OpenPositions.Count);
        Assert.Equal(3, portfolio.MaxConcurrentSeen);
    }

    [Fact]
    public void MultiPosition_RejectsBeyondMax()
    {
        var config = MarketConfig.Default with
        {
            MaxConcurrentPositions = 2,
            MaxPositionPct = 0.1m,
            StopLossPct = 0m
        };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();
        portfolio.MaxEquity = 10000m;

        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);

        trader.ProcessSignal(signal, portfolio, new TickContext(50000m, 1000m, 0f, 0, 0f));
        trader.ProcessSignal(signal, portfolio, new TickContext(50100m, 1000m, 0f, 1, 0f));
        var third = trader.ProcessSignal(signal, portfolio, new TickContext(50200m, 1000m, 0f, 2, 0f));

        Assert.Equal(2, portfolio.OpenPositions.Count);
        Assert.False(third.Executed);
        Assert.Contains("Max concurrent", third.Error ?? "");
    }

    // ── H2: MaxConcurrentSeen tracking ─────────────────────────────────────

    [Fact]
    public void MaxConcurrentSeen_TracksPeak()
    {
        var config = MarketConfig.Default with
        {
            MaxConcurrentPositions = 3,
            MaxPositionPct = 0.1m,
            StopLossPct = 0m
        };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();
        portfolio.MaxEquity = 10000m;

        var openSignal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
        var exitSignal = new TradingSignal(TradeDirection.Flat, 0f, 0f, true);

        trader.ProcessSignal(openSignal, portfolio, new TickContext(50000m, 1000m, 0f, 0, 0f));
        trader.ProcessSignal(openSignal, portfolio, new TickContext(50100m, 1000m, 0f, 1, 0f));
        Assert.Equal(2, portfolio.MaxConcurrentSeen);

        // Close one
        trader.ProcessSignal(exitSignal, portfolio, new TickContext(50200m, 1000m, 0f, 2, 0f));
        Assert.Equal(2, portfolio.MaxConcurrentSeen);  // peak retained

        // Open two more → peak should move to 3
        trader.ProcessSignal(openSignal, portfolio, new TickContext(50300m, 1000m, 0f, 3, 0f));
        trader.ProcessSignal(openSignal, portfolio, new TickContext(50400m, 1000m, 0f, 4, 0f));
        Assert.Equal(3, portfolio.MaxConcurrentSeen);
    }
}
