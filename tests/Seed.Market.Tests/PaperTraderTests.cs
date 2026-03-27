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
}
