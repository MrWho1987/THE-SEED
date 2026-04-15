using Seed.Market.Trading;

namespace Seed.Market.Tests;

public class PaperTraderTests
{
    private static MarketConfig Cfg => new()
    {
        InitialCapital = 10_000m,
        MakerFee = 0.0004m,
        TakerFee = 0.0006m,
        SlippageBps = 5m,
        MaxPositionPct = 0.25m,
        MaxDailyLossPct = 0.05m,
        KillSwitchDrawdownPct = 0.15m,
        MaxConcurrentPositions = 3
    };

    [Fact]
    public void NewPortfolio_HasCorrectBalance()
    {
        var trader = new PaperTrader(Cfg);
        var portfolio = trader.CreatePortfolio();
        Assert.Equal(10_000m, portfolio.Balance);
        Assert.Empty(portfolio.OpenPositions);
    }

    [Fact]
    public void OpenLong_CreatesPosition()
    {
        var trader = new PaperTrader(Cfg);
        var portfolio = trader.CreatePortfolio();
        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);

        var result = trader.ProcessSignal(signal, portfolio, 50_000m, 0);

        Assert.True(result.Executed);
        Assert.Single(portfolio.OpenPositions);
        Assert.Equal(TradeDirection.Long, portfolio.OpenPositions[0].Direction);
        Assert.True(result.Fee > 0);
    }

    [Fact]
    public void ClosePosition_RecordsPnl()
    {
        var trader = new PaperTrader(Cfg);
        var portfolio = trader.CreatePortfolio();
        var open = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        trader.ProcessSignal(open, portfolio, 50_000m, 0);

        var close = new TradingSignal(TradeDirection.Flat, 0f, 0f, true);
        var result = trader.ProcessSignal(close, portfolio, 52_000m, 10);

        Assert.True(result.Executed);
        Assert.Empty(portfolio.OpenPositions);
        Assert.Single(portfolio.TradeHistory);
        Assert.True(portfolio.TradeHistory[0].Pnl > 0);
    }

    [Fact]
    public void LosingTrade_ReducesBalance()
    {
        var trader = new PaperTrader(Cfg);
        var portfolio = trader.CreatePortfolio();
        var open = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        trader.ProcessSignal(open, portfolio, 50_000m, 0);

        var close = new TradingSignal(TradeDirection.Flat, 0f, 0f, true);
        trader.ProcessSignal(close, portfolio, 48_000m, 10);

        Assert.True(portfolio.Balance < 10_000m);
        Assert.True(portfolio.TradeHistory[0].Pnl < 0);
    }

    [Fact]
    public void FlatSignal_NoTrade()
    {
        var trader = new PaperTrader(Cfg);
        var portfolio = trader.CreatePortfolio();
        var signal = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);

        var result = trader.ProcessSignal(signal, portfolio, 50_000m, 0);

        Assert.False(result.Executed);
        Assert.Empty(portfolio.OpenPositions);
    }

    [Fact]
    public void FeesAreApplied()
    {
        var trader = new PaperTrader(Cfg);
        var portfolio = trader.CreatePortfolio();
        var signal = new TradingSignal(TradeDirection.Long, 1f, 1f, false);

        var result = trader.ProcessSignal(signal, portfolio, 50_000m, 0);

        Assert.True(result.Fee > 0);
        Assert.True(portfolio.Balance < 10_000m);
    }

    [Fact]
    public void ShortTrade_ProfitsOnPriceDrop()
    {
        var trader = new PaperTrader(Cfg);
        var portfolio = trader.CreatePortfolio();
        var open = new TradingSignal(TradeDirection.Short, 0.5f, 0.8f, false);
        trader.ProcessSignal(open, portfolio, 50_000m, 0);

        var close = new TradingSignal(TradeDirection.Flat, 0f, 0f, true);
        trader.ProcessSignal(close, portfolio, 48_000m, 10);

        Assert.True(portfolio.TradeHistory[0].Pnl > 0);
    }

    [Fact]
    public void KillSwitch_BlocksFurtherTrades()
    {
        var cfg = Cfg with { KillSwitchDrawdownPct = 0.01m };
        var trader = new PaperTrader(cfg);
        var portfolio = trader.CreatePortfolio();

        var open = new TradingSignal(TradeDirection.Long, 1f, 1f, false);
        trader.ProcessSignal(open, portfolio, 50_000m, 0);

        var close = new TradingSignal(TradeDirection.Flat, 0f, 0f, true);
        trader.ProcessSignal(close, portfolio, 40_000m, 10);

        var second = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        var result = trader.ProcessSignal(second, portfolio, 40_000m, 20);

        Assert.False(result.Executed);
    }

    // ── Brain-driven exit tracking tests ─────────────────────────────────────
    // V14 rename: ClosedByExitSignal is now derived from Reason via IsBrainDrivenExit.
    // Any of the brain's action outputs (3, 6-10) that drive the close qualifies.

    [Fact]
    public void ExitSignal_MarksBrainDrivenExit()
    {
        // When brain signals ExitCurrent=true, the closed trade must be marked brain-driven.
        var trader = new PaperTrader(Cfg);
        var portfolio = trader.CreatePortfolio();

        var open = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        trader.ProcessSignal(open, portfolio, 50_000m, 0);

        var exit = new TradingSignal(TradeDirection.Flat, 0f, 0f, true);  // ExitCurrent=true
        trader.ProcessSignal(exit, portfolio, 52_000m, 10);

        Assert.Single(portfolio.TradeHistory);
        Assert.Equal(CloseReason.ExitSignal, portfolio.TradeHistory[0].Reason);
        Assert.True(portfolio.TradeHistory[0].IsBrainDrivenExit,
            "ExitSignal close must be marked as brain-driven");
    }

    [Fact]
    public void DirectionFlip_IsNotBrainDrivenExit()
    {
        // Direction flip (LONG → SHORT) closes the existing position, but NOT via explicit exit.
        var trader = new PaperTrader(Cfg);
        var portfolio = trader.CreatePortfolio();

        var longSig = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        trader.ProcessSignal(longSig, portfolio, 50_000m, 0);

        var flipToShort = new TradingSignal(TradeDirection.Short, 0.5f, 0.8f, false);  // opposite direction, no exit flag
        trader.ProcessSignal(flipToShort, portfolio, 50_100m, 10);

        Assert.Single(portfolio.TradeHistory);
        Assert.Equal(CloseReason.DirectionFlip, portfolio.TradeHistory[0].Reason);
        Assert.False(portfolio.TradeHistory[0].IsBrainDrivenExit,
            "Direction-flip close must NOT be marked as brain-driven");
    }

    [Fact]
    public void ProtectiveStopLoss_IsNotBrainDrivenExit()
    {
        // Config-default stop-loss force-close is NOT a brain-driven exit.
        var cfg = Cfg with { StopLossPct = 0.02m };
        var trader = new PaperTrader(cfg);
        var portfolio = trader.CreatePortfolio();

        var longSig = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        trader.ProcessSignal(longSig, portfolio, 50_000m, 0);

        // Drop price >2% to trigger stop loss on next tick
        var holdSig = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        trader.ProcessSignal(holdSig, portfolio, 48_500m, 10);  // -3% = stop triggers

        Assert.Single(portfolio.TradeHistory);
        Assert.Equal(CloseReason.StopLoss, portfolio.TradeHistory[0].Reason);
        Assert.False(portfolio.TradeHistory[0].IsBrainDrivenExit,
            "Config-default stop-loss close must NOT be marked as brain-driven");
    }

    [Fact]
    public void BrainStopLoss_IsBrainDrivenExit()
    {
        // Brain-set SL override DOES qualify as brain-driven — brain explicitly set the stop.
        var cfg = Cfg with { StopLossPct = 0.10m };  // config default 10% so it won't fire first
        var trader = new PaperTrader(cfg);
        var portfolio = trader.CreatePortfolio();

        var open = new TradingSignal(
            TradeDirection.Long, 0.5f, 0.8f, false,
            StopLossOverride: 0.01f);  // brain says "1% stop"
        trader.ProcessSignal(open, portfolio, 50_000m, 0);

        var hold = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        trader.ProcessSignal(hold, portfolio, 49_200m, 10);  // -1.6% triggers brain SL

        Assert.Single(portfolio.TradeHistory);
        Assert.Equal(CloseReason.BrainStopLoss, portfolio.TradeHistory[0].Reason);
        Assert.True(portfolio.TradeHistory[0].IsBrainDrivenExit,
            "Brain-set SL override close must be marked as brain-driven");
    }

    [Fact]
    public void TakeProfit_IsBrainDrivenExit()
    {
        var cfg = Cfg with { StopLossPct = 0m };
        var trader = new PaperTrader(cfg);
        var portfolio = trader.CreatePortfolio();

        var open = new TradingSignal(
            TradeDirection.Long, 0.5f, 0.8f, false,
            TakeProfitOffset: 0.02f);  // 2% TP
        trader.ProcessSignal(open, portfolio, 50_000m, 0);

        var hold = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        trader.ProcessSignal(hold, portfolio, 51_100m, 10);  // above TP trigger

        Assert.Single(portfolio.TradeHistory);
        Assert.Equal(CloseReason.TakeProfit, portfolio.TradeHistory[0].Reason);
        Assert.True(portfolio.TradeHistory[0].IsBrainDrivenExit,
            "TakeProfit close must be marked as brain-driven");
    }

    [Fact]
    public void TrailingStop_IsBrainDrivenExit()
    {
        var cfg = Cfg with { StopLossPct = 0m };
        var trader = new PaperTrader(cfg);
        var portfolio = trader.CreatePortfolio();

        var open = new TradingSignal(
            TradeDirection.Long, 0.5f, 0.8f, false,
            EnableTrailingStop: true,
            TrailingStopDistance: 0.01f);
        trader.ProcessSignal(open, portfolio, 50_000m, 0);

        var hold = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        trader.ProcessSignal(hold, portfolio, 52_500m, 10);  // peak track
        trader.ProcessSignal(hold, portfolio, 51_800m, 11);  // trail breach

        Assert.Single(portfolio.TradeHistory);
        Assert.Equal(CloseReason.TrailingStop, portfolio.TradeHistory[0].Reason);
        Assert.True(portfolio.TradeHistory[0].IsBrainDrivenExit,
            "TrailingStop close must be marked as brain-driven");
    }

    [Fact]
    public void PartialClose_IsBrainDrivenExit()
    {
        var cfg = Cfg with { StopLossPct = 0m };
        var trader = new PaperTrader(cfg);
        var portfolio = trader.CreatePortfolio();

        var open = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        trader.ProcessSignal(open, portfolio, 50_000m, 0);

        var partial = new TradingSignal(
            TradeDirection.Flat, 0f, 0f, false,
            PartialCloseFrac: 0.5f);
        trader.ProcessSignal(partial, portfolio, 50_500m, 5);

        Assert.Single(portfolio.TradeHistory);
        Assert.Equal(CloseReason.PartialClose, portfolio.TradeHistory[0].Reason);
        Assert.True(portfolio.TradeHistory[0].IsBrainDrivenExit,
            "PartialClose must be marked as brain-driven");
    }

    [Fact]
    public void Position_StoresLeverageFromSignal()
    {
        var trader = new PaperTrader(Cfg with { MaxLeverage = 3.0f });
        var portfolio = trader.CreatePortfolio();

        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false, Leverage: 2.5f);
        trader.ProcessSignal(signal, portfolio, 50_000m, 0);

        Assert.Single(portfolio.OpenPositions);
        Assert.Equal(2.5f, portfolio.OpenPositions[0].Leverage);
    }
}
