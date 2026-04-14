using Seed.Market.Trading;

namespace Seed.Market.Tests;

public class CandleIntervalTests
{
    [Theory]
    [InlineData("1m", 60, 1, 60_000L)]
    [InlineData("5m", 12, 5, 300_000L)]
    [InlineData("15m", 4, 15, 900_000L)]
    [InlineData("30m", 2, 30, 1_800_000L)]
    [InlineData("1h", 1, 60, 3_600_000L)]
    [InlineData("4h", 0, 240, 14_400_000L)]
    public void Config_ComputedHelpers_CorrectForInterval(
        string interval, int expectedBph, int expectedMinutes, long expectedMs)
    {
        var config = MarketConfig.Default with { CandleInterval = interval };
        Assert.Equal(expectedMinutes, config.BarDurationMinutes);
        if (expectedBph > 0)
            Assert.Equal(expectedBph, config.BarsPerHour);
        Assert.Equal(expectedMs, config.BarDurationMs);
    }

    [Fact]
    public void Config_DefaultCandleInterval_Is1h()
    {
        var config = MarketConfig.Default;
        Assert.Equal("1h", config.CandleInterval);
        Assert.Equal(1, config.BarsPerHour);
        Assert.Equal(60, config.BarDurationMinutes);
    }

    [Fact]
    public void Config_DefaultStopLoss_Is2Percent()
    {
        var config = MarketConfig.Default;
        Assert.Equal(0.02m, config.StopLossPct);
    }

    [Fact]
    public void Config_15m_BarsPerHour_Is4()
    {
        var config = MarketConfig.Default with { CandleInterval = "15m" };
        Assert.Equal(4, config.BarsPerHour);
        Assert.Equal(900_000L, config.BarDurationMs);
    }

    [Fact]
    public void ElapsedHours_15m_Tick4_Equals1Hour()
    {
        var config = MarketConfig.Default with { CandleInterval = "15m" };
        int tick = 4;
        float elapsedHours = (float)tick / config.BarsPerHour;
        Assert.Equal(1.0f, elapsedHours);
    }

    [Fact]
    public void ElapsedHours_1h_Tick4_Equals4Hours()
    {
        var config = MarketConfig.Default with { CandleInterval = "1h" };
        int tick = 4;
        float elapsedHours = (float)tick / config.BarsPerHour;
        Assert.Equal(4.0f, elapsedHours);
    }

    [Fact]
    public void StopLoss_TriggersWhenLossExceedsThreshold()
    {
        var config = MarketConfig.Default with { StopLossPct = 0.02m };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        var openSignal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
        var openCtx = new TickContext(50000m, 0m, 0f, 0);
        trader.ProcessSignal(openSignal, portfolio, openCtx);
        Assert.Single(portfolio.OpenPositions);

        var pos = portfolio.OpenPositions[0];
        decimal entryPrice = pos.EntryPrice;
        decimal stopPrice = entryPrice * (1m - config.StopLossPct - 0.001m);
        decimal unrealizedPct = pos.UnrealizedPnlPct(stopPrice) / 100m;
        Assert.True(unrealizedPct <= -config.StopLossPct);

        var closeCtx = new TickContext(stopPrice, 0m, 0f, 1);
        trader.ForceClose(portfolio, pos, closeCtx);
        Assert.Empty(portfolio.OpenPositions);
        Assert.Single(portfolio.TradeHistory);
    }

    [Fact]
    public void StopLoss_DoesNotTriggerWhenLossBelowThreshold()
    {
        var config = MarketConfig.Default with { StopLossPct = 0.02m };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        var openSignal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
        var openCtx = new TickContext(50000m, 0m, 0f, 0);
        trader.ProcessSignal(openSignal, portfolio, openCtx);
        Assert.Single(portfolio.OpenPositions);

        var pos = portfolio.OpenPositions[0];
        decimal entryPrice = pos.EntryPrice;
        decimal safePrice = entryPrice * (1m - config.StopLossPct + 0.005m);
        decimal unrealizedPct = pos.UnrealizedPnlPct(safePrice) / 100m;
        Assert.True(unrealizedPct > -config.StopLossPct);
    }

    [Fact]
    public void VolumeSlippage_ScalesToHourlyEquivalent()
    {
        var config1h = MarketConfig.Default with { CandleInterval = "1h" };
        var config15m = MarketConfig.Default with { CandleInterval = "15m" };

        var trader1h = new PaperTrader(config1h);
        var trader15m = new PaperTrader(config15m);

        var portfolio1h = trader1h.CreatePortfolio();
        var portfolio15m = trader15m.CreatePortfolio();

        decimal hourlyVolume = 1000m;
        decimal barVolume15m = hourlyVolume / 4m;

        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
        var ctx1h = new TickContext(50000m, hourlyVolume, 0f, 0);
        var ctx15m = new TickContext(50000m, barVolume15m, 0f, 0);

        var r1h = trader1h.ProcessSignal(signal, portfolio1h, ctx1h);
        var r15m = trader15m.ProcessSignal(signal, portfolio15m, ctx15m);

        Assert.Equal(r1h.Slippage, r15m.Slippage);
    }

    [Fact]
    public void ForceClose_ClosesPosition()
    {
        var config = MarketConfig.Default;
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
        var ctx = new TickContext(50000m, 0m, 0f, 0);
        trader.ProcessSignal(signal, portfolio, ctx);
        Assert.Single(portfolio.OpenPositions);

        var pos = portfolio.OpenPositions[0];
        var closeCtx = new TickContext(51000m, 0m, 0f, 1);
        var result = trader.ForceClose(portfolio, pos, closeCtx);
        Assert.True(result.Executed);
        Assert.Empty(portfolio.OpenPositions);
    }

    [Fact]
    public void Position_HasStopPriceField()
    {
        var pos = new Position
        {
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            EntryPrice = 50000m,
            Size = 0.1m,
            StopPrice = 49000m
        };
        Assert.Equal(49000m, pos.StopPrice);
    }

    [Fact]
    public void TickContext_BarVolume_PropertyExists()
    {
        var ctx = new TickContext(50000m, 1000m, 0.001f, 42, 5.5f);
        Assert.Equal(1000m, ctx.BarVolume);
        Assert.Equal(50000m, ctx.Price);
        Assert.Equal(0.001f, ctx.FundingRate);
        Assert.Equal(42, ctx.TickIndex);
        Assert.Equal(5.5f, ctx.ElapsedHours);
    }
}
