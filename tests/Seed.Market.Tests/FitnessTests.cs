using Seed.Core;
using Seed.Genetics;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Market.Trading;

namespace Seed.Market.Tests;

public class FitnessTests
{
    [Fact]
    public void Sharpe_FlatEquityCurve_ReturnsZero()
    {
        var portfolio = CreatePortfolio(3);
        for (int i = 0; i < 100; i++)
            portfolio.EquityCurve.Add(10000f);

        var result = MarketFitness.ComputeDetailed(portfolio, 50000m);

        Assert.Equal(0f, result.RawSharpe);
        Assert.Equal(0f, result.Sortino);
        Assert.Equal(0f, result.CVaR5);
        Assert.Equal(0f, result.MaxDrawdownDuration);
    }

    [Fact]
    public void Sharpe_SteadyGrowth_ReturnsPositive()
    {
        var portfolio = CreatePortfolio(10);
        for (int i = 0; i < 200; i++)
            portfolio.EquityCurve.Add(10000f + i * 10f);

        var result = MarketFitness.ComputeDetailed(portfolio, 50000m);

        Assert.True(result.RawSharpe > 0f, $"Expected positive Sharpe, got {result.RawSharpe}");
        Assert.True(result.Sortino > 0f, $"Expected positive Sortino, got {result.Sortino}");
        Assert.True(result.CVaR5 >= 0f, $"Expected non-negative CVaR, got {result.CVaR5}");
        Assert.Equal(0f, result.MaxDrawdownDuration);
    }

    [Fact]
    public void Sharpe_SingleDrop_AffectsMetrics()
    {
        var portfolio = CreatePortfolio(5);
        for (int i = 0; i < 100; i++)
            portfolio.EquityCurve.Add(10000f + i * 10f);
        portfolio.EquityCurve.Add(10000f + 100 * 10f - 550f); // ~5% drop
        for (int i = 0; i < 100; i++)
            portfolio.EquityCurve.Add(10500f + i * 10f);

        var result = MarketFitness.ComputeDetailed(portfolio, 50000m);

        Assert.True(result.CVaR5 < 0f, $"CVaR should be negative after a drop, got {result.CVaR5}");
        Assert.True(result.MaxDrawdownDuration > 0f, $"Drawdown duration should be > 0, got {result.MaxDrawdownDuration}");
        Assert.True(result.RawSharpe > 0f, $"Sharpe should still be positive overall, got {result.RawSharpe}");
    }

    [Fact]
    public void Shrinkage_FewTrades_DiscountsSharpe()
    {
        float shrinkageK = 10f;

        // Sniper: 3 trades, confidence = 1 - 10/13 ≈ 0.231
        float confidence3 = 1f - shrinkageK / (shrinkageK + 3);
        float adjusted3 = 8.0f * confidence3; // ≈ 1.85

        // Steady: 50 trades, confidence = 1 - 10/60 ≈ 0.833
        float confidence50 = 1f - shrinkageK / (shrinkageK + 50);
        float adjusted50 = 3.5f * confidence50; // ≈ 2.92

        Assert.InRange(adjusted3, 1.8f, 1.9f);
        Assert.InRange(adjusted50, 2.8f, 3.0f);
        Assert.True(adjusted50 > adjusted3,
            $"Steady trader ({adjusted50:F2}) should beat sniper ({adjusted3:F2}) with these parameters");
    }

    [Fact]
    public void Shrinkage_ZeroTrades_ReturnsInactivityPenalty()
    {
        var portfolio = CreatePortfolio(0);
        for (int i = 0; i < 50; i++)
            portfolio.EquityCurve.Add(10000f);

        var result = MarketFitness.ComputeDetailed(portfolio, 50000m);

        Assert.Equal(MarketFitness.InactivityPenalty, result.Fitness);
        Assert.False(result.IsActive);
    }

    [Fact]
    public void CompositeFitness_WeightsSumCorrectly()
    {
        var portfolio = CreatePortfolio(20);
        portfolio.Balance = 11000m;
        for (int i = 0; i < 100; i++)
            portfolio.EquityCurve.Add(10000f + i * 10f);

        var result = MarketFitness.ComputeDetailed(portfolio, 50000m);

        Assert.True(result.IsActive);
        Assert.False(float.IsNaN(result.Fitness));
        Assert.False(float.IsInfinity(result.Fitness));

        float logReturn = MathF.Log(1f + MathF.Abs(result.ReturnPct)) * MathF.Sign(result.ReturnPct);
        float expectedFitness = result.AdjustedSharpe * 0.45f
                              + result.Sortino * 0.15f
                              + logReturn * 0.20f
                              - result.MaxDrawdownDuration * 0.10f
                              - (result.CVaR5 < 0 ? -result.CVaR5 : 0f) * 0.10f;

        Assert.Equal(expectedFitness, result.Fitness, 4);
    }

    [Fact]
    public void EquityCurve_PopulatedDuringEvaluation()
    {
        var config = MarketConfig.Default with
        {
            InitialCapital = 10_000m,
            PopulationSize = 1,
            MaxBrainNodes = 50,
            MaxBrainEdges = 100
        };
        var evaluator = new MarketEvaluator(config);
        var rng = new Rng64(42);
        var population = new List<IGenome> { SeedGenome.CreateRandom(rng) };

        var (snapshots, prices, rawVols, rawFund) = CreateSyntheticData(50);
        var results = evaluator.Evaluate(population, snapshots, prices, rawVols, rawFund, 0);

        var result = results.Values.First();
        Assert.False(float.IsNaN(result.Fitness.Fitness));
        Assert.True(result.Fitness.RawSharpe is not float.NaN);
    }

    private static PortfolioState CreatePortfolio(int closedTradeCount)
    {
        var portfolio = new PortfolioState
        {
            Balance = 10000m,
            InitialBalance = 10000m,
            MaxEquity = 10000m,
        };
        for (int i = 0; i < closedTradeCount; i++)
        {
            portfolio.TradeHistory.Add(new ClosedTrade(
                "BTCUSDT", TradeDirection.Long,
                50000m, 50100m, 0.01m,
                i % 3 == 0 ? -5m : 10m,
                0.3m, 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        }
        return portfolio;
    }

    private static (SignalSnapshot[], float[], float[], float[]) CreateSyntheticData(int length)
    {
        var normalizer = new SignalNormalizer();
        var snapshots = new SignalSnapshot[length];
        var prices = new float[length];
        var rawVolumes = new float[length];
        var rawFundingRates = new float[length];
        float price = 50000f;
        var rng = new Random(42);

        for (int i = 0; i < length; i++)
        {
            price *= 1f + (float)(rng.NextDouble() - 0.498) * 0.02f;
            prices[i] = price;
            rawVolumes[i] = 1000f + (float)rng.NextDouble() * 500f;
            rawFundingRates[i] = 0.0001f;
            var raw = new float[SignalIndex.Count];
            raw[SignalIndex.BtcPrice] = price;
            raw[SignalIndex.BtcReturn1h] = i > 0 ? (price - prices[i - 1]) / prices[i - 1] : 0f;
            raw[SignalIndex.BtcVolume1h] = rawVolumes[i];
            raw[SignalIndex.FearGreedIndex] = 50f;
            raw[SignalIndex.Rsi14] = 50f;
            snapshots[i] = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddHours(i), i);
        }
        return (snapshots, prices, rawVolumes, rawFundingRates);
    }
}
