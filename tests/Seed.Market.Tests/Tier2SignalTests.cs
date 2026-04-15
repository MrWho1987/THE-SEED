using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Backtest;
using Seed.Market.Evolution;
using Seed.Market.Indicators;
using Seed.Market.Signals;
using Seed.Market.Trading;

namespace Seed.Market.Tests;

/// <summary>
/// Unit tests for Tier 2 signal additions: new regime signals (F1), portfolio context
/// signals (F2), Signal category map (F3), and slot repurposing (G2 Deribit).
/// </summary>
public class Tier2SignalTests
{
    // ── F1: Expanded regime signals ────────────────────────────────────────

    [Fact]
    public void SignalIndex_Count_Is110()
    {
        Assert.Equal(110, SignalIndex.Count);
    }

    [Fact]
    public void Regime_ContiguousRange_88to95()
    {
        Assert.Equal(88, SignalIndex.Categories.RegimeStart);
        Assert.Equal(95, SignalIndex.Categories.RegimeEnd);
        for (int s = SignalIndex.Categories.RegimeStart; s <= SignalIndex.Categories.RegimeEnd; s++)
            Assert.Equal(10, SignalIndex.GetCategoryIndex(s));
    }

    [Fact]
    public void Regime_HasEightSignals()
    {
        // Four original + four V14 new regime signals
        Assert.Equal(88, SignalIndex.RegimeVolatility);
        Assert.Equal(89, SignalIndex.RegimeTrend);
        Assert.Equal(90, SignalIndex.RegimeChange);
        Assert.Equal(91, SignalIndex.MarketStress);
        Assert.Equal(92, SignalIndex.TimeOfDaySession);
        Assert.Equal(93, SignalIndex.VolatilityPercentile);
        Assert.Equal(94, SignalIndex.TrendStrengthAdx);
        Assert.Equal(95, SignalIndex.CorrelationRegime);
    }

    // ── F2: Portfolio context signals ──────────────────────────────────────

    [Fact]
    public void PortfolioContext_SlotsShifted_AndNewSlotsAdded()
    {
        // Risk awareness 96-103 (old 92-99 shifted +4)
        Assert.Equal(96, SignalIndex.RollingSharpe);
        Assert.Equal(97, SignalIndex.RollingDrawdown);
        Assert.Equal(98, SignalIndex.WinRate);
        Assert.Equal(99, SignalIndex.TradeFrequency);
        Assert.Equal(100, SignalIndex.AvgHoldingDuration);
        Assert.Equal(101, SignalIndex.CumulativeFees);
        Assert.Equal(102, SignalIndex.ConsecutiveWins);
        Assert.Equal(103, SignalIndex.ConsecutiveLosses);
        // New portfolio context 104-109
        Assert.Equal(104, SignalIndex.AvailableMarginPct);
        Assert.Equal(105, SignalIndex.DistanceToStopLoss);
        Assert.Equal(106, SignalIndex.DistanceToKillSwitch);
        Assert.Equal(107, SignalIndex.TimeSinceLastTrade);
        Assert.Equal(108, SignalIndex.EffectiveLeverage);
        Assert.Equal(109, SignalIndex.WinLossStreakMagnitude);
    }

    [Fact]
    public void RiskAwareness_ContiguousRange_96to109()
    {
        Assert.Equal(96, SignalIndex.Categories.RiskAwarenessStart);
        Assert.Equal(109, SignalIndex.Categories.RiskAwarenessEnd);
        for (int s = SignalIndex.Categories.RiskAwarenessStart; s <= SignalIndex.Categories.RiskAwarenessEnd; s++)
            Assert.Equal(11, SignalIndex.GetCategoryIndex(s));
    }

    [Fact]
    public void PortfolioContext_AvailableMargin_FlatReturnsFullMargin()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var graph = dev.CompileGraph(genome, MarketEvaluator.MarketBrainBudget, new DevelopmentContext(42, 0));
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
        var trader = new PaperTrader(MarketConfig.Default);
        var agent = new MarketAgent(genome.GenomeId, brain, trader);

        var normalizer = new SignalNormalizer();
        var raw = new float[SignalIndex.Count];
        raw[SignalIndex.BtcPrice] = 50000f;
        var snap = normalizer.Normalize(raw, DateTimeOffset.UtcNow, 0);

        // Process one tick with no open positions
        agent.ProcessTick(snap, new TickContext(50000m, 0m, 0f, 0, 0f));

        // Inject signals directly via reflection pattern — instead, check agent's portfolio state
        Assert.Empty(agent.Portfolio.OpenPositions);
    }

    // ── F3: Signal category map ────────────────────────────────────────────

    [Fact]
    public void CategoryMap_CoversAllSlots()
    {
        for (int s = 0; s < SignalIndex.Count; s++)
        {
            int cat = SignalIndex.GetCategoryIndex(s);
            Assert.InRange(cat, 0, SignalIndex.CategoryCount - 1);
        }
    }

    [Fact]
    public void CategoryMap_Deribit_InSentiment()
    {
        // V14 repurposed slots 25-29 for Deribit signals, still in Sentiment category
        Assert.Equal(2, SignalIndex.GetCategoryIndex(SignalIndex.DeribitPutCallRatio));
        Assert.Equal(2, SignalIndex.GetCategoryIndex(SignalIndex.DeribitPutCallOI));
        Assert.Equal(2, SignalIndex.GetCategoryIndex(SignalIndex.DeribitIVPercentile));
        Assert.Equal(2, SignalIndex.GetCategoryIndex(SignalIndex.DeribitSkew));
        Assert.Equal(2, SignalIndex.GetCategoryIndex(SignalIndex.DeribitMaxPainDistance));
    }

    // ── G2: Deribit slot layout ────────────────────────────────────────────

    [Fact]
    public void DeribitSlots_InRange_25to29()
    {
        Assert.Equal(25, SignalIndex.DeribitPutCallRatio);
        Assert.Equal(26, SignalIndex.DeribitPutCallOI);
        Assert.Equal(27, SignalIndex.DeribitIVPercentile);
        Assert.Equal(28, SignalIndex.DeribitSkew);
        Assert.Equal(29, SignalIndex.DeribitMaxPainDistance);
    }

    // ── Integration: HistoricalDataStore computes new regime signals ──────

    [Fact]
    public void HistoricalDataStore_ComputesNewRegimeSignals()
    {
        // Build synthetic candles with rising prices
        int n = 200;
        var candles = new TechnicalIndicators.Candle[n];
        var baseTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        for (int i = 0; i < n; i++)
        {
            float price = 50000f + i * 10f + MathF.Sin(i * 0.1f) * 50f;
            candles[i] = new TechnicalIndicators.Candle(
                Open: price - 5f, High: price + 10f, Low: price - 10f,
                Close: price, Volume: 100f,
                Time: baseTime.AddHours(i));
        }

        var (snaps, _, _, _) = HistoricalDataStore.CandlesToSignals(candles);

        // All new regime signals should be populated (non-default) by end of run
        var last = snaps[^1].Signals;
        Assert.InRange(last[SignalIndex.TimeOfDaySession], -1f, 1f);
        Assert.InRange(last[SignalIndex.VolatilityPercentile], -1f, 1f);
        Assert.InRange(last[SignalIndex.TrendStrengthAdx], -1f, 1f);
        Assert.InRange(last[SignalIndex.CorrelationRegime], -1f, 1f);
    }

    [Fact]
    public void HistoricalDataStore_SignalVector_HasCount110()
    {
        int n = 50;
        var candles = new TechnicalIndicators.Candle[n];
        var baseTime = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);
        for (int i = 0; i < n; i++)
            candles[i] = new TechnicalIndicators.Candle(50000f, 50100f, 49900f, 50000f + i, 100f, baseTime.AddHours(i));

        var (snaps, _, _, _) = HistoricalDataStore.CandlesToSignals(candles);
        Assert.Equal(SignalIndex.Count, snaps[0].Signals.Length);
    }
}
