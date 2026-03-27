using Seed.Core;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Backtest;
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

    [Fact]
    public void PaperTradingPipeline_GenomeToLiveTicks()
    {
        // Simulates the full paper trading pipeline:
        // genome JSON → deserialize → compile brain → create agent → process ticks → log trades

        // 1. Create and serialize a genome (simulates backtest output)
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var json = genome.ToJson();

        // 2. Deserialize (simulates paper mode loading from disk)
        var restored = SeedGenome.FromJson(json);
        Assert.Equal(genome.GenomeId, restored.GenomeId);
        Assert.Equal(genome.Cppn.Nodes.Count, restored.Cppn.Nodes.Count);

        // 3. Compile brain using the same budget as MarketEvaluator
        var developer = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var devCtx = new DevelopmentContext(42, 0);
        var graph = developer.CompileGraph(restored, MarketEvaluator.MarketBrainBudget, devCtx);
        Assert.Equal(88, graph.InputCount);
        Assert.Equal(4, graph.OutputCount);

        // 4. Create agent (same wiring as paper mode)
        var brain = new BrainRuntime(graph, restored.Learn, restored.Stable, 1);
        var config = MarketConfig.Default;
        var trader = new PaperTrader(config);
        var agent = new MarketAgent(restored.GenomeId, brain, trader);

        // 5. Feed synthetic market data (simulates live DataAggregator ticks)
        var normalizer = new SignalNormalizer();
        float price = 50000f;
        var dataRng = new Random(123);
        for (int t = 0; t < 200; t++)
        {
            price *= 1f + (float)(dataRng.NextDouble() - 0.498) * 0.02f;
            var raw = new float[SignalIndex.Count];
            raw[SignalIndex.BtcPrice] = price;
            raw[SignalIndex.BtcReturn1h] = (float)(dataRng.NextDouble() - 0.5) * 0.04f;
            raw[SignalIndex.BtcVolume1h] = 500f + (float)dataRng.NextDouble() * 200f;
            raw[SignalIndex.FearGreedIndex] = 50f;
            raw[SignalIndex.Rsi14] = 30f + (float)dataRng.NextDouble() * 40f;
            var snapshot = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddMinutes(t * 5), t);
            agent.ProcessTick(snapshot, (decimal)price);
        }

        // 6. Close remaining positions (same as paper mode shutdown)
        trader.CloseAllPositions(agent.Portfolio, (decimal)price, agent.Tick);

        // 7. Verify pipeline produced valid results
        Assert.Equal(200, agent.Tick);
        Assert.False(float.IsNaN(MarketFitness.Compute(agent.Portfolio, (decimal)price)));
        Assert.Empty(agent.Portfolio.OpenPositions);
    }

    [Fact]
    public void TradeLogger_WritesAndCloses()
    {
        var logPath = Path.Combine(Path.GetTempPath(), $"seed_test_{Guid.NewGuid():N}.jsonl");
        try
        {
            using (var logger = new TradeLogger(logPath))
            {
                logger.LogTrade(new ClosedTrade(
                    "BTCUSDT", TradeDirection.Long,
                    50000m, 51000m, 0.1m, 100m, 3m, 10,
                    DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow));
            }

            var lines = File.ReadAllLines(logPath);
            Assert.True(lines.Length >= 3); // session_start + trade + session_end
            Assert.Contains("session_start", lines[0]);
            Assert.Contains("trade", lines[1]);
            Assert.Contains("session_end", lines[2]);
        }
        finally
        {
            if (File.Exists(logPath)) File.Delete(logPath);
        }
    }

    [Fact]
    public void Checkpoint_SaveAndRestore()
    {
        var rng = new Rng64(42);
        var population = new List<IGenome>();
        for (int i = 0; i < 5; i++)
            population.Add(SeedGenome.CreateRandom(rng));

        var checkpoint = Backtest.CheckpointState.FromPopulation(population, 10, 0.05f);

        var path = Path.Combine(Path.GetTempPath(), $"seed_test_cp_{Guid.NewGuid():N}.json");
        try
        {
            checkpoint.Save(path);
            var loaded = Backtest.CheckpointState.Load(path);

            Assert.Equal(10, loaded.Generation);
            Assert.Equal(0.05f, loaded.BestFitness);
            Assert.Equal(5, loaded.GenomeJsons.Count);

            var restored = loaded.RestorePopulation();
            Assert.Equal(5, restored.Count);
            Assert.Equal(population[0].GenomeId, restored[0].GenomeId);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void MarketEvaluator_UsesSharedBudget()
    {
        Assert.Equal(16, MarketEvaluator.MarketBrainBudget.HiddenWidth);
        Assert.Equal(3, MarketEvaluator.MarketBrainBudget.HiddenLayers);
        Assert.Equal(16, MarketEvaluator.MarketBrainBudget.TopKIn);
    }
}
