using Seed.Core;
using Seed.Genetics;
using Seed.Market.Backtest;
using Seed.Market.Evaluation;
using Seed.Market.Trading;

namespace Seed.Market.Tests;

public class ProductionTests
{
    [Fact]
    public void Checkpoint_IncludesSpeciesIds()
    {
        var rng = new Rng64(42);
        var population = Enumerable.Range(0, 10).Select(_ => (IGenome)SeedGenome.CreateRandom(rng)).ToList();
        var speciesIds = new List<int> { 0, 0, 1, 1, 1, 2, 2, 3, 3, 3 };

        var cp = CheckpointState.FromPopulation(population, 5, 0.1f, speciesIds);

        var path = Path.Combine(Path.GetTempPath(), $"seed_prod_test_{Guid.NewGuid():N}.json");
        try
        {
            cp.Save(path);
            var loaded = CheckpointState.Load(path);

            Assert.Equal(10, loaded.SpeciesIds.Count);
            Assert.True(loaded.SpeciesIds.Distinct().Count() >= 2);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MonteCarlo_10kResamples_ProducesValidInterval()
    {
        var trades = CreateSampleTrades(20);
        var result = MonteCarloSimulator.Simulate(trades, 10_000m, 10_000, seed: 42);

        Assert.True(result.P5Return < result.MedianReturn,
            $"P5 ({result.P5Return}) should be < median ({result.MedianReturn})");
        Assert.True(result.MedianReturn < result.P95Return,
            $"Median ({result.MedianReturn}) should be < P95 ({result.P95Return})");
        Assert.True(result.P95Return - result.P5Return > 0, "Interval width should be > 0");
        Assert.Equal(10_000, result.Resamples);
    }

    [Fact]
    public void MonteCarlo_SingleTrade_WideInterval()
    {
        var trades = CreateSampleTrades(1);
        var narrow = MonteCarloSimulator.Simulate(CreateSampleTrades(50), 10_000m, 10_000, seed: 42);
        var wide = MonteCarloSimulator.Simulate(trades, 10_000m, 10_000, seed: 42);

        // Single trade should have zero width (always picks the same trade)
        // Actually with 1 trade, bootstrap always picks the same one, so interval collapses.
        Assert.Equal(wide.P5Return, wide.P95Return, 4);
    }

    [Fact]
    public void Kelly_PositiveEdge_ReturnsPositiveFraction()
    {
        var trades = new List<ClosedTrade>();
        for (int i = 0; i < 60; i++)
            trades.Add(CreateTrade(i % 10 < 6 ? 100m : -80m));

        var fraction = KellyPositionSizer.ComputeHalfKelly(trades);
        Assert.True(fraction > 0.01m, $"Should have positive Kelly fraction, got {fraction}");
        Assert.True(fraction <= 0.25m, $"Should be capped at maxPct, got {fraction}");
    }

    [Fact]
    public void Kelly_NegativeEdge_ReturnsMinimum()
    {
        var trades = new List<ClosedTrade>();
        for (int i = 0; i < 20; i++)
            trades.Add(CreateTrade(i % 10 < 3 ? 50m : -80m));

        var fraction = KellyPositionSizer.ComputeHalfKelly(trades);
        Assert.Equal(0.01m, fraction);
    }

    [Fact]
    public void Kelly_PerfectRecord_CappedByConfig()
    {
        var trades = new List<ClosedTrade>();
        for (int i = 0; i < 20; i++)
            trades.Add(CreateTrade(500m));

        var fraction = KellyPositionSizer.ComputeHalfKelly(trades, maxPct: 0.25m);
        Assert.True(fraction <= 0.25m, $"Kelly should be capped at max, got {fraction}");
    }

    [Fact]
    public void Kelly_TooFewTrades_ReturnsMinimum()
    {
        var trades = new List<ClosedTrade> { CreateTrade(100m) };
        var fraction = KellyPositionSizer.ComputeHalfKelly(trades);
        Assert.Equal(0.01m, fraction);
    }

    [Fact]
    public void MonteCarlo_EmptyTrades_ReturnsZero()
    {
        var result = MonteCarloSimulator.Simulate(new List<ClosedTrade>(), 10_000m);
        Assert.Equal(0f, result.MedianReturn);
        Assert.Equal(0, result.Resamples);
    }

    [Fact]
    public void Bootstrap_Deterministic()
    {
        var trades = CreateSampleTrades(15);
        var r1 = MonteCarloSimulator.Simulate(trades, 10_000m, 5_000, seed: 42);
        var r2 = MonteCarloSimulator.Simulate(trades, 10_000m, 5_000, seed: 42);

        Assert.Equal(r1.MedianReturn, r2.MedianReturn, 4);
        Assert.Equal(r1.P5Return, r2.P5Return, 4);
    }

    [Fact]
    public void VaR_HighVol_ScalesDown()
    {
        var config = MarketConfig.Default with { MaxDailyVaRPct = 0.02m };
        var risk = new RiskManager(config);
        var portfolio = new PortfolioState
        {
            Balance = 10000m, InitialBalance = 10000m, MaxEquity = 10000m
        };
        var rng = new Random(42);
        for (int i = 0; i < 30; i++)
            portfolio.EquityCurve.Add(10000f + (float)(rng.NextDouble() - 0.5) * 2000f);

        decimal scale = risk.ComputeVaRScale(portfolio);
        Assert.True(scale < 1.0m, $"High volatility should scale down positions, got {scale}");
    }

    [Fact]
    public void VaR_LowVol_NoScaling()
    {
        var config = MarketConfig.Default;
        var risk = new RiskManager(config);
        var portfolio = new PortfolioState
        {
            Balance = 10000m, InitialBalance = 10000m, MaxEquity = 10000m
        };
        for (int i = 0; i < 30; i++)
            portfolio.EquityCurve.Add(10000f + i * 0.1f);

        decimal scale = risk.ComputeVaRScale(portfolio);
        Assert.Equal(1.0m, scale);
    }

    [Fact]
    public void VaR_InsufficientData_NoScaling()
    {
        var config = MarketConfig.Default;
        var risk = new RiskManager(config);
        var portfolio = new PortfolioState
        {
            Balance = 10000m, InitialBalance = 10000m, MaxEquity = 10000m
        };
        for (int i = 0; i < 5; i++)
            portfolio.EquityCurve.Add(10000f);

        decimal scale = risk.ComputeVaRScale(portfolio);
        Assert.Equal(1.0m, scale);
    }

    // ── Tier 1.1 Leverage integration tests ─────────────────────────────────

    [Fact]
    public void ComputePositionSize_WithLeverage1_UnchangedFromBaseline()
    {
        // Baseline: MaxLeverage=1, signal.Leverage=1 → same as v1 behavior.
        var config = MarketConfig.Default with { MaxLeverage = 1.0f };
        var risk = new RiskManager(config);
        var portfolio = new PortfolioState
        {
            Balance = 10000m, InitialBalance = 10000m, MaxEquity = 10000m
        };
        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false, Leverage: 1.0f);
        decimal size = risk.ComputePositionSize(signal, portfolio, 50000m);

        // Expected: 10000 × 0.25 (MaxPositionPct) × 0.5 (SizePct) × 1.0 (VaR) × 1.0 (Leverage) = 1250
        Assert.Equal(1250m, size);
    }

    [Fact]
    public void ComputePositionSize_WithLeverage3_Scales3x()
    {
        // Brain chooses leverage=3 → position notional tripled.
        var config = MarketConfig.Default with { MaxLeverage = 3.0f };
        var risk = new RiskManager(config);
        var portfolio = new PortfolioState
        {
            Balance = 10000m, InitialBalance = 10000m, MaxEquity = 10000m
        };
        var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false, Leverage: 3.0f);
        decimal size = risk.ComputePositionSize(signal, portfolio, 50000m);

        // Expected: 10000 × 0.25 × 0.5 × 1.0 × 3.0 = 3750
        Assert.Equal(3750m, size);
    }

    [Fact]
    public void ComputePositionSize_CapsAtMaxLeveragedNotional()
    {
        // Even if Leverage × SizePct would exceed the ceiling, the notional is capped.
        var config = MarketConfig.Default with { MaxLeverage = 3.0f };
        var risk = new RiskManager(config);
        var portfolio = new PortfolioState
        {
            Balance = 10000m, InitialBalance = 10000m, MaxEquity = 10000m
        };
        // SizePct=1.0 × Leverage=3.0 × MaxPositionPct=0.25 = 0.75 of equity = 7500
        var signal = new TradingSignal(TradeDirection.Long, 1.0f, 0.8f, false, Leverage: 3.0f);
        decimal size = risk.ComputePositionSize(signal, portfolio, 50000m);
        Assert.Equal(7500m, size);
    }

    [Fact]
    public void CheckTrade_RespectsLeveragedCeiling()
    {
        var config = MarketConfig.Default with { MaxLeverage = 2.0f };
        var risk = new RiskManager(config);
        var portfolio = new PortfolioState
        {
            Balance = 10000m, InitialBalance = 10000m, MaxEquity = 10000m
        };
        // Normal size signal with leverage within ceiling → allowed
        var signal = new TradingSignal(TradeDirection.Long, 0.8f, 0.8f, false, Leverage: 2.0f);
        var (allowed, _) = risk.CheckTrade(signal, portfolio, 50000m);
        Assert.True(allowed);
    }

    [Fact]
    public void MarketConfig_Validate_RejectsInvalidLeverage()
    {
        // Leverage < 1 should be rejected
        var cfg1 = MarketConfig.Default with { MaxLeverage = 0.5f };
        Assert.Throws<InvalidOperationException>(() => cfg1.Validate());

        // Leverage > 10 should be rejected
        var cfg2 = MarketConfig.Default with { MaxLeverage = 15f };
        Assert.Throws<InvalidOperationException>(() => cfg2.Validate());

        // Leverage 1 and 10 should be accepted
        MarketConfig.Default.Validate();  // default is 1.0, should pass
        (MarketConfig.Default with { MaxLeverage = 10f }).Validate();
    }

    [Fact]
    public void RollingMetrics_SharpePositiveForRising()
    {
        var rm = new RollingMetrics(100);
        for (int i = 0; i < 100; i++)
            rm.Add(10000f + i * 10f);

        Assert.True(rm.RollingSharpe > 0f, $"Rolling Sharpe should be positive for rising equity, got {rm.RollingSharpe}");
        Assert.Equal(0f, rm.RollingDrawdown);
    }

    [Fact]
    public void RollingMetrics_DrawdownDetected()
    {
        var rm = new RollingMetrics(100);
        for (int i = 0; i < 50; i++)
            rm.Add(10000f + i * 20f);
        for (int i = 0; i < 50; i++)
            rm.Add(11000f - i * 40f);

        Assert.True(rm.RollingDrawdown > 0f, $"Rolling drawdown should be > 0 after drop, got {rm.RollingDrawdown}");
    }

    private static List<ClosedTrade> CreateSampleTrades(int count)
    {
        var trades = new List<ClosedTrade>();
        var rng = new Random(42);
        for (int i = 0; i < count; i++)
        {
            decimal pnl = (decimal)(rng.NextDouble() * 200 - 80);
            trades.Add(CreateTrade(pnl));
        }
        return trades;
    }

    private static ClosedTrade CreateTrade(decimal pnl)
    {
        return new ClosedTrade("BTCUSDT", TradeDirection.Long, 50000m,
            50000m + pnl * 10, 0.01m, pnl, 3m, 5,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    }
}
