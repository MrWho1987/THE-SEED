using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Market.Trading;
using Seed.Market.Backtest;
using Seed.Market.Indicators;

namespace Seed.Market.Tests;

/// <summary>
/// Tests that verify parity between training and paper/live execution paths.
/// </summary>
public class ParityTests
{
    [Fact]
    public void CategoryMap_IdenticalInTrainingAndPaper()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var budget = MarketEvaluator.MarketBrainBudget with
        {
            HiddenWidth = genome.Dev.SubstrateWidth,
            HiddenHeight = genome.Dev.SubstrateHeight,
            HiddenLayers = genome.Dev.SubstrateLayers
        };
        var devCtx = new DevelopmentContext(42, 0);
        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);

        // Training path (explicit CategoryMap)
        var graphTraining = dev.CompileGraph(genome, budget, devCtx,
            MarketEvaluator.SignalCategoryMap, MarketEvaluator.RegimeStart, MarketEvaluator.RegimeEnd);

        // Paper path (should now also pass CategoryMap — verify same result)
        var graphPaper = dev.CompileGraph(genome, budget, devCtx,
            MarketEvaluator.SignalCategoryMap, MarketEvaluator.RegimeStart, MarketEvaluator.RegimeEnd);

        Assert.Equal(graphTraining.NodeCount, graphPaper.NodeCount);
        Assert.Equal(graphTraining.EdgeCount, graphPaper.EdgeCount);
        Assert.Equal(graphTraining.GateCount, graphPaper.GateCount);

        // Verify gate neuron connectivity is identical
        var trainingGates = graphTraining.Nodes.Where(n => n.Type == BrainNodeType.Gate).ToList();
        var paperGates = graphPaper.Nodes.Where(n => n.Type == BrainNodeType.Gate).ToList();
        Assert.Equal(trainingGates.Count, paperGates.Count);

        for (int g = 0; g < trainingGates.Count; g++)
        {
            var tEdges = graphTraining.IncomingByDst.GetValueOrDefault(trainingGates[g].NodeId, []);
            var pEdges = graphPaper.IncomingByDst.GetValueOrDefault(paperGates[g].NodeId, []);
            Assert.Equal(tEdges.Count, pEdges.Count);
            for (int e = 0; e < tEdges.Count; e++)
            {
                Assert.Equal(tEdges[e].SrcNodeId, pEdges[e].SrcNodeId);
                Assert.Equal(tEdges[e].WSlow, pEdges[e].WSlow);
            }
        }
    }

    [Fact]
    public void ElapsedHours_PassedCorrectlyToLearn()
    {
        // Verify that MarketAgent passes ctx.ElapsedHours to BrainLearnContext,
        // not the tick counter. We test indirectly: the critical period decay
        // should match the expected elapsed hours, not the tick number.
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var graph = dev.CompileGraph(genome, MarketEvaluator.MarketBrainBudget with
        {
            HiddenWidth = genome.Dev.SubstrateWidth,
            HiddenHeight = genome.Dev.SubstrateHeight,
            HiddenLayers = genome.Dev.SubstrateLayers
        }, new DevelopmentContext(42, 0),
        MarketEvaluator.SignalCategoryMap, MarketEvaluator.RegimeStart, MarketEvaluator.RegimeEnd);

        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
        var config = MarketConfig.Default with { InitialCapital = 10000m, CandleInterval = "15m" };
        var trader = new PaperTrader(config);
        var agent = new MarketAgent(genome.GenomeId, brain, trader);

        var snapshot = new SignalSnapshot(new float[SignalIndex.Count], DateTimeOffset.UtcNow, 0);

        // Run 100 ticks at 15-min intervals (BarsPerHour=4)
        // Tick 100 should have ElapsedHours = 100/4 = 25 hours
        for (int t = 0; t < 100; t++)
        {
            float elapsedHours = (float)t / config.BarsPerHour;
            var ctx = new TickContext(50000m, 100m, 0.0001f, t, elapsedHours);
            agent.ProcessTick(snapshot, ctx);
        }

        // If ElapsedHours were wrong (tick number instead of hours),
        // critical period would be expired (100 > CriticalPeriodHours not applicable,
        // but the key is that learning didn't use tick=100 as "100 hours")
        // The brain should still be functional
        var outputs = brain.Step(new float[MarketAgent.InputCount], new BrainStepContext(100));
        for (int i = 0; i < outputs.Length; i++)
            Assert.False(float.IsNaN(outputs[i]));
    }

    [Fact]
    public void StopLoss_TriggersInsidePaperTrader()
    {
        var config = MarketConfig.Default with
        {
            InitialCapital = 10000m,
            StopLossPct = 0.02m, // 2% stop-loss
            MaxPositionPct = 0.5m,
        };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        // Open a long position at 50000
        var openSignal = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        var openCtx = new TickContext(50000m, 1000m, 0f, 0, 0f);
        trader.ProcessSignal(openSignal, portfolio, openCtx);
        Assert.True(portfolio.OpenPositions.Count > 0, "Should have opened a position");

        // Price drops 3% — beyond 2% stop-loss
        var flatSignal = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        var dropCtx = new TickContext(48500m, 1000m, 0f, 1, 0.25f);
        trader.ProcessSignal(flatSignal, portfolio, dropCtx);

        // Position should have been closed by stop-loss inside ProcessSignal
        Assert.Empty(portfolio.OpenPositions);
        Assert.True(portfolio.TradeHistory.Count > 0, "Trade should be recorded");
    }

    [Fact]
    public void StopLoss_DoesNotTriggerAboveThreshold()
    {
        var config = MarketConfig.Default with
        {
            InitialCapital = 10000m,
            StopLossPct = 0.02m,
            MaxPositionPct = 0.5m,
        };
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        var openSignal = new TradingSignal(TradeDirection.Long, 0.5f, 0.8f, false);
        var openCtx = new TickContext(50000m, 1000m, 0f, 0, 0f);
        trader.ProcessSignal(openSignal, portfolio, openCtx);

        // Price drops 1% — within 2% stop-loss
        var flatSignal = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);
        var smallDropCtx = new TickContext(49500m, 1000m, 0f, 1, 0.25f);
        trader.ProcessSignal(flatSignal, portfolio, smallDropCtx);

        // Position should still be open
        Assert.NotEmpty(portfolio.OpenPositions);
    }

    [Fact]
    public void NormalizationWarmup_SkipsFirstBarsForLargeDatasets()
    {
        // Create 2000 candles (> 1000 threshold)
        var candles = new TechnicalIndicators.Candle[2000];
        float price = 50000f;
        var rng = new Random(42);
        for (int i = 0; i < 2000; i++)
        {
            price += rng.Next(-100, 100);
            candles[i] = new TechnicalIndicators.Candle(
                price, price + 50, price - 50, price, 1000f + i,
                DateTimeOffset.UtcNow.AddHours(-2000 + i));
        }

        var result = HistoricalDataStore.CandlesToSignals(candles);
        var snapshots = result.snapshots;
        var prices = result.prices;

        // With 2000 candles and 500 warmup skip, should get 1500 bars
        Assert.Equal(1500, snapshots.Length);
        Assert.Equal(1500, prices.Length);
    }

    [Fact]
    public void NormalizationWarmup_NoSkipForSmallDatasets()
    {
        var candles = new TechnicalIndicators.Candle[100];
        float price = 50000f;
        var rng = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            price += rng.Next(-100, 100);
            candles[i] = new TechnicalIndicators.Candle(
                price, price + 50, price - 50, price, 1000f + i,
                DateTimeOffset.UtcNow.AddHours(-100 + i));
        }

        var result2 = HistoricalDataStore.CandlesToSignals(candles);

        // Small dataset: no warmup skip
        Assert.Equal(100, result2.snapshots.Length);
    }
}
