using Seed.Market.Evolution;
using Seed.Market.Trading;

namespace Seed.Market.Tests;

/// <summary>
/// Unit tests for Tier 1 fitness term additions (B1-B4) and 9-component weight sum validation (B5).
/// </summary>
public class Tier1FitnessTests
{
    // ── B1: FeeDrag ─────────────────────────────────────────────────────────

    [Fact]
    public void FeeDrag_ZeroTrades_ReturnsZero()
    {
        var portfolio = new PortfolioState { Balance = 10000m, InitialBalance = 10000m };
        Assert.Equal(0f, MarketFitness.ComputeFeeDrag(portfolio));
    }

    [Fact]
    public void FeeDrag_Accumulates_AsFractionOfInitialBalance()
    {
        var portfolio = new PortfolioState { Balance = 10000m, InitialBalance = 10000m };
        portfolio.TradeHistory.Add(new ClosedTrade(
            "BTCUSDT", TradeDirection.Long, 50000m, 50100m, 0.01m,
            10m, 100m, 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        portfolio.TradeHistory.Add(new ClosedTrade(
            "BTCUSDT", TradeDirection.Long, 50000m, 50200m, 0.01m,
            20m, 50m, 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        // Total fees = 150, initial = 10000 → feeDrag = 0.015
        Assert.Equal(0.015f, MarketFitness.ComputeFeeDrag(portfolio), 4);
    }

    [Fact]
    public void FeeDrag_ZeroInitialBalance_ReturnsZero()
    {
        var portfolio = new PortfolioState { Balance = 0m, InitialBalance = 0m };
        portfolio.TradeHistory.Add(new ClosedTrade(
            "BTCUSDT", TradeDirection.Long, 50000m, 50100m, 0.01m,
            10m, 100m, 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        Assert.Equal(0f, MarketFitness.ComputeFeeDrag(portfolio));
    }

    // ── B2: Calmar ──────────────────────────────────────────────────────────

    [Fact]
    public void Calmar_ZeroDrawdown_GuardedFromDivideByZero()
    {
        // Max drawdown of 0 should be floored to 0.001 to avoid infinity
        float calmar = MarketFitness.ComputeCalmar(0.5f, 0f);
        Assert.False(float.IsInfinity(calmar));
        Assert.Equal(20f, calmar);  // clamped to maxCap
    }

    [Fact]
    public void Calmar_NormalCase_ReturnsLogReturnOverDrawdown()
    {
        // logReturn=0.1, drawdown=0.05 → calmar=2.0
        float calmar = MarketFitness.ComputeCalmar(0.1f, 0.05f);
        Assert.Equal(2.0f, calmar, 4);
    }

    [Fact]
    public void Calmar_NegativeReturn_ReturnsNegative()
    {
        float calmar = MarketFitness.ComputeCalmar(-0.1f, 0.05f);
        Assert.Equal(-2.0f, calmar, 4);
    }

    [Fact]
    public void Calmar_ClampedToMaxCap()
    {
        // Extreme logReturn/drawdown should be clamped
        float calmar = MarketFitness.ComputeCalmar(1000f, 0.01f);
        Assert.Equal(20f, calmar);  // clamped to maxCap=20
    }

    // ── B3: Information Ratio ──────────────────────────────────────────────

    [Fact]
    public void InfoRatio_ZeroStd_ReturnsZero()
    {
        Assert.Equal(0f, MarketFitness.ComputeInfoRatio(0.1f, 0.05f, 0f));
    }

    [Fact]
    public void InfoRatio_BeatsHodl_Positive()
    {
        // strategy=0.2, hodl=0.1, std=0.05 → (0.2-0.1)/0.05 = 2.0
        float ir = MarketFitness.ComputeInfoRatio(0.2f, 0.1f, 0.05f);
        Assert.Equal(2.0f, ir, 4);
    }

    [Fact]
    public void InfoRatio_LosesToHodl_Negative()
    {
        float ir = MarketFitness.ComputeInfoRatio(0.05f, 0.10f, 0.05f);
        Assert.Equal(-1.0f, ir, 4);
    }

    [Fact]
    public void InfoRatio_Clamped()
    {
        float ir = MarketFitness.ComputeInfoRatio(100f, 0f, 0.01f);
        Assert.Equal(10f, ir);  // clamped to maxCap
    }

    // ── B4: Diversification ────────────────────────────────────────────────

    [Fact]
    public void Diversification_NoConcurrentPositions_ReturnsZero()
    {
        var portfolio = new PortfolioState { MaxConcurrentSeen = 0 };
        Assert.Equal(0f, MarketFitness.ComputeDiversification(portfolio));
    }

    [Fact]
    public void Diversification_SinglePosition_ReturnsZero()
    {
        var portfolio = new PortfolioState { MaxConcurrentSeen = 1 };
        Assert.Equal(0f, MarketFitness.ComputeDiversification(portfolio));
    }

    [Fact]
    public void Diversification_MaxConcurrent_ReturnsOne()
    {
        var portfolio = new PortfolioState { MaxConcurrentSeen = 3 };
        float div = MarketFitness.ComputeDiversification(portfolio, maxConcurrent: 3);
        Assert.Equal(1f, div, 4);
    }

    [Fact]
    public void Diversification_BetweenMinAndMax_ReturnsInRange()
    {
        var portfolio = new PortfolioState { MaxConcurrentSeen = 2 };
        float div = MarketFitness.ComputeDiversification(portfolio, maxConcurrent: 3);
        Assert.True(div > 0f && div < 1f, $"Expected 0 < div < 1, got {div}");
    }

    // ── B5: Weight re-normalization ────────────────────────────────────────

    [Fact]
    public void MarketConfig_DefaultWeights_SumToOne()
    {
        // Default weights must pass validation
        MarketConfig.Default.Validate();
    }

    [Fact]
    public void MarketConfig_InvalidWeights_ThrowsOnValidate()
    {
        var cfg = MarketConfig.Default with { FitnessSharpeWeight = 0.99f };  // sum no longer 1.0
        Assert.Throws<InvalidOperationException>(() => cfg.Validate());
    }

    [Fact]
    public void MarketConfig_NineComponentWeights_SumToOne()
    {
        var cfg = MarketConfig.Default with
        {
            FitnessSharpeWeight = 0.22f,
            FitnessSortinoWeight = 0.13f,
            FitnessReturnWeight = 0.20f,
            FitnessDrawdownDurationWeight = 0.13f,
            FitnessCVaRWeight = 0.17f,
            FitnessCalmarWeight = 0.05f,
            FitnessInfoRatioWeight = 0.05f,
            FitnessFeeDragWeight = 0.03f,
            FitnessDiversificationWeight = 0.02f,
        };
        cfg.Validate();  // should not throw
    }

    [Fact]
    public void FitnessBreakdown_NewFieldsPopulated()
    {
        // Run full ComputeDetailed and verify new fields are populated (non-default)
        var portfolio = new PortfolioState { Balance = 11000m, InitialBalance = 10000m };
        for (int i = 0; i < 20; i++)
        {
            portfolio.TradeHistory.Add(new ClosedTrade(
                "BTCUSDT", TradeDirection.Long, 50000m, 50100m, 0.01m,
                50m, 0.5m, 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        for (int i = 0; i < 100; i++)
            portfolio.EquityCurve.Add(10000f + i * 10f);
        portfolio.MaxConcurrentSeen = 2;
        portfolio.MaxDrawdown = 0.05m;

        var breakdown = MarketFitness.ComputeDetailed(portfolio, 50000m, hodlReturn: 0.05f);

        Assert.True(breakdown.FeeDrag > 0f, "FeeDrag should accumulate from trade fees");
        Assert.NotEqual(0f, breakdown.Calmar);  // rising curve with positive return has positive calmar
        Assert.True(breakdown.Diversification > 0f, "Diversification should reflect MaxConcurrentSeen");
    }
}
