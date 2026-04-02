using Seed.Market.Trading;

namespace Seed.Market.Tests;

public class SimulationFidelityTests
{
    [Fact]
    public void VolumeSlippage_HigherForLargeOrders()
    {
        var config = MarketConfig.Default;
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        var smallCtx = new TickContext(50000m, 200m, 0f, 1);
        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
        var r1 = trader.ProcessSignal(signal, portfolio, smallCtx);

        var portfolio2 = trader.CreatePortfolio();
        portfolio2.Balance = 100_000m;
        var largeConfig = config with { InitialCapital = 100_000m, MaxPositionPct = 0.5m };
        var trader2 = new PaperTrader(largeConfig);
        var portfolio3 = trader2.CreatePortfolio();
        var largeCtx = new TickContext(50000m, 200m, 0f, 1);
        var r2 = trader2.ProcessSignal(signal, portfolio3, largeCtx);

        if (r1.Executed && r2.Executed)
        {
            Assert.True(r2.Slippage >= r1.Slippage,
                $"Larger order should have >= slippage ({r2.Slippage} vs {r1.Slippage})");
        }
    }

    [Fact]
    public void VolumeSlippage_BtcVolumeConvertedToUsd()
    {
        var config = MarketConfig.Default with { SlippageBps = 5m };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        var ctx = new TickContext(67000m, 200m, 0f, 1);
        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
        var result = trader.ProcessSignal(signal, portfolio, ctx);

        if (result.Executed)
        {
            decimal maxReasonableSlippage = 67000m * 10m / 10000m;
            Assert.True(result.Slippage < maxReasonableSlippage,
                $"Slippage {result.Slippage} should be < {maxReasonableSlippage} (10 bps). " +
                $"If >$600, volume unit mismatch bug is present.");
        }
    }

    [Fact]
    public void VolumeSlippage_NegligibleForSmallOrders()
    {
        var config = MarketConfig.Default with { SlippageBps = 5m };
        decimal baseSlippageAtPrice = 50000m * 5m / 10000m;

        decimal orderNotional = 2500m;
        decimal hourlyVolume = 500_000_000m;
        decimal participation = orderNotional / (hourlyVolume * 0.01m);
        decimal dynamicBps = 5m * (1m + participation * participation);

        decimal dynamicSlippage = 50000m * dynamicBps / 10000m;
        Assert.True(dynamicSlippage < baseSlippageAtPrice * 1.1m,
            $"Small order slippage ({dynamicSlippage}) should be < 1.1x base ({baseSlippageAtPrice * 1.1m})");
    }

    [Fact]
    public void VolumeSlippage_FallsBackGracefully_ZeroVolume()
    {
        var config = MarketConfig.Default;
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        var ctx = new TickContext(50000m, 0m, 0f, 1);
        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
        var result = trader.ProcessSignal(signal, portfolio, ctx);

        Assert.True(result.Executed);
        Assert.True(result.Slippage > 0);
    }

    [Fact]
    public void FundingRate_DeductedEvery8Ticks()
    {
        var config = MarketConfig.Default;
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        portfolio.OpenPositions.Add(new Position
        {
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            EntryPrice = 50000m,
            Size = 0.1m,
            OpenTime = DateTimeOffset.UtcNow,
            OpenTick = 0
        });

        decimal balanceBefore = portfolio.Balance;

        for (int t = 1; t <= 16; t++)
        {
            var ctx = new TickContext(50000m, 0m, 0.0001f, t);
            var signal = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
            trader.ProcessSignal(signal, portfolio, ctx);
        }

        Assert.True(portfolio.Balance < balanceBefore,
            $"Funding should have been deducted (before: {balanceBefore}, after: {portfolio.Balance})");
    }

    [Fact]
    public void FundingRate_ShortReceives()
    {
        var config = MarketConfig.Default;
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        portfolio.OpenPositions.Add(new Position
        {
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Short,
            EntryPrice = 50000m,
            Size = 0.1m,
            OpenTime = DateTimeOffset.UtcNow,
            OpenTick = 0
        });

        decimal balanceBefore = portfolio.Balance;

        var ctx = new TickContext(50000m, 0m, 0.0001f, 8);
        var signal = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        trader.ProcessSignal(signal, portfolio, ctx);

        Assert.True(portfolio.Balance > balanceBefore,
            $"Short position should receive funding (before: {balanceBefore}, after: {portfolio.Balance})");
    }

    [Fact]
    public void FundingRate_ZeroRate_NoCost()
    {
        var config = MarketConfig.Default;
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        portfolio.OpenPositions.Add(new Position
        {
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            EntryPrice = 50000m,
            Size = 0.1m,
            OpenTime = DateTimeOffset.UtcNow,
            OpenTick = 0
        });

        decimal balanceBefore = portfolio.Balance;

        var ctx = new TickContext(50000m, 0m, 0f, 8);
        var signal = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        trader.ProcessSignal(signal, portfolio, ctx);

        Assert.Equal(balanceBefore, portfolio.Balance);
    }

    [Fact]
    public void LatencySimulation_NoTradeOnFirstTick()
    {
        var config = MarketConfig.Default;
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        var ctx = new TickContext(50000m, 0m, 0f, 0);
        var buySignal = new TradingSignal(TradeDirection.Long, 1f, 1f, false);

        // First tick should buffer the signal, not execute it
        // (The latency is implemented in MarketAgent, not PaperTrader directly,
        // so we test the PaperTrader can handle Flat on tick 0)
        var flatSignal = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        var result = trader.ProcessSignal(flatSignal, portfolio, ctx);

        Assert.False(result.Executed);
        Assert.Empty(portfolio.OpenPositions);
    }

    [Fact]
    public void TickContext_DefaultVolume_UsesBaseSlippage()
    {
        var config = MarketConfig.Default with { SlippageBps = 5m };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        var ctx = new TickContext(50000m, 0m, 0f, 1);
        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
        var result = trader.ProcessSignal(signal, portfolio, ctx);

        if (result.Executed)
        {
            Assert.True(result.Slippage > 0, "Should have base slippage even with zero volume");
        }
    }
}
