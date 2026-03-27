using Seed.Core;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Data;
using Seed.Market.Evolution;
using Seed.Market.Indicators;
using Seed.Market.Signals;
using Seed.Market.Trading;
using Seed.Brain;
using Seed.Development;
using Seed.Observatory;

namespace Seed.Market.Tests;

public class IntegrationTests
{
    [Fact]
    public void FullPipeline_DataToTradeToFitness()
    {
        // 1. Create synthetic market data (simulates DataAggregator output)
        var normalizer = new SignalNormalizer();
        var snapshots = new SignalSnapshot[100];
        var prices = new float[100];
        float price = 50000f;
        var rng = new Random(42);
        for (int i = 0; i < 100; i++)
        {
            price *= 1f + (float)(rng.NextDouble() - 0.498) * 0.02f;
            prices[i] = price;
            var raw = new float[SignalIndex.Count];
            raw[SignalIndex.BtcPrice] = price;
            raw[SignalIndex.BtcReturn1h] = i > 0 ? (price - prices[i - 1]) / prices[i - 1] : 0f;
            raw[SignalIndex.BtcVolume1h] = 500f;
            raw[SignalIndex.Rsi14] = 50f;
            snapshots[i] = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddHours(i), i);
        }

        // 2. Create a genome and compile it into a brain
        var seedRng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(seedRng);
        var developer = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var graph = developer.CompileGraph(genome, DevelopmentBudget.Default, new DevelopmentContext(42, 0));

        Assert.Equal(MarketAgent.InputCount, graph.InputCount);
        Assert.Equal(MarketAgent.OutputCount, graph.OutputCount);

        // 3. Wire brain to market agent
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
        var config = MarketConfig.Default;
        var trader = new PaperTrader(config);
        var agent = new MarketAgent(genome.GenomeId, brain, trader);

        // 4. Run agent through market data
        for (int t = 0; t < snapshots.Length; t++)
            agent.ProcessTick(snapshots[t], (decimal)prices[t]);

        // 5. Close positions and compute fitness
        trader.CloseAllPositions(agent.Portfolio, (decimal)prices[^1], agent.Tick);
        var fitness = MarketFitness.ComputeDetailed(agent.Portfolio, (decimal)prices[^1]);

        // Verify the pipeline produced a valid result
        Assert.False(float.IsNaN(fitness.Fitness));
        Assert.False(float.IsInfinity(fitness.Fitness));
        Assert.True(agent.Tick == 100);
    }

    [Fact]
    public void BrainIO_Matches88InputAnd4Output()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(88, 4);
        var graph = dev.CompileGraph(genome, DevelopmentBudget.Default, new DevelopmentContext(42, 0));

        Assert.Equal(88, graph.InputCount);
        Assert.Equal(4, graph.OutputCount);

        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
        var inputs = new float[88];
        var outputs = brain.Step(inputs, new BrainStepContext(0));

        Assert.Equal(4, outputs.Length);
    }

    [Fact]
    public void VaderSentiment_ScoresHeadlines()
    {
        Assert.True(VaderSentiment.Score("Bitcoin surges to all-time high") > 0);
        Assert.True(VaderSentiment.Score("Crypto market crashes amid liquidation") < 0);
        Assert.InRange(VaderSentiment.Score("Bitcoin trades sideways"), -0.3f, 0.3f);
    }

    [Fact]
    public void TechnicalIndicators_ComputeFromCandles()
    {
        var candles = new TechnicalIndicators.Candle[30];
        float price = 50000f;
        var rng = new Random(42);
        for (int i = 0; i < 30; i++)
        {
            price *= 1f + (float)(rng.NextDouble() - 0.5) * 0.02f;
            candles[i] = new TechnicalIndicators.Candle(
                price * 0.99f, price * 1.01f, price * 0.98f, price, 1000f,
                DateTimeOffset.UtcNow.AddHours(i));
        }

        var signals = TechnicalIndicators.Compute(candles);
        Assert.True(signals.Length > 0);

        var rsiSignal = signals.First(s => s.Index == SignalIndex.Rsi14);
        Assert.InRange(rsiSignal.Value, 0f, 100f);
    }

    [Fact]
    public void ActionInterpreter_RoundTrips()
    {
        // Brain outputs → TradingSignal → should preserve intent
        float[] longSignal = [2f, 0.7f, 0.8f, 0.1f];
        var signal = ActionInterpreter.Interpret(longSignal);
        Assert.Equal(TradeDirection.Long, signal.Direction);
        Assert.True(signal.SizePct > 0.5f);
        Assert.False(signal.ExitCurrent);

        float[] exitSignal = [0f, 0f, 0f, 5f];
        var exit = ActionInterpreter.Interpret(exitSignal);
        Assert.True(exit.ExitCurrent);
    }
}
