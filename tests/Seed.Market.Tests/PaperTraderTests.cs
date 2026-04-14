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

    // ── Tier 1.3 Explicit-exit tracking tests ────────────────────────────────

    [Fact]
    public void ExitSignal_SetsClosedByExitSignalFlag()
    {
        // When brain signals ExitCurrent=true, the closed trade must flag ClosedByExitSignal=true.
        var trader = new PaperTrader(Cfg);
        var portfolio = trader.CreatePortfolio();

        var open = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        trader.ProcessSignal(open, portfolio, 50_000m, 0);

        var exit = new TradingSignal(TradeDirection.Flat, 0f, 0f, true);  // ExitCurrent=true
        trader.ProcessSignal(exit, portfolio, 52_000m, 10);

        Assert.Single(portfolio.TradeHistory);
        Assert.True(portfolio.TradeHistory[0].ClosedByExitSignal,
            "Trade closed via explicit exit signal must have ClosedByExitSignal=true");
    }

    [Fact]
    public void DirectionFlip_ClosedByExitSignalIsFalse()
    {
        // Direction flip (LONG → SHORT) closes the existing position, but NOT via explicit exit.
        var trader = new PaperTrader(Cfg);
        var portfolio = trader.CreatePortfolio();

        var longSig = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        trader.ProcessSignal(longSig, portfolio, 50_000m, 0);

        var flipToShort = new TradingSignal(TradeDirection.Short, 0.5f, 0.8f, false);  // opposite direction, no exit flag
        trader.ProcessSignal(flipToShort, portfolio, 50_100m, 10);

        Assert.Single(portfolio.TradeHistory);
        Assert.False(portfolio.TradeHistory[0].ClosedByExitSignal,
            "Direction-flip close must NOT have ClosedByExitSignal=true");
    }

    [Fact]
    public void StopLoss_ClosedByExitSignalIsFalse()
    {
        // Stop-loss force-close is NOT an explicit exit signal close.
        var cfg = Cfg with { StopLossPct = 0.02m };
        var trader = new PaperTrader(cfg);
        var portfolio = trader.CreatePortfolio();

        var longSig = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        trader.ProcessSignal(longSig, portfolio, 50_000m, 0);

        // Drop price >2% to trigger stop loss on next tick
        var holdSig = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        trader.ProcessSignal(holdSig, portfolio, 48_500m, 10);  // -3% = stop triggers

        Assert.Single(portfolio.TradeHistory);
        Assert.False(portfolio.TradeHistory[0].ClosedByExitSignal,
            "Stop-loss close must NOT have ClosedByExitSignal=true");
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
