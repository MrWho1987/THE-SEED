using System.Text.Json;
using Seed.Core;
using Seed.Genetics;
using Seed.Market.Backtest;
using Seed.Market.Evaluation;
using Seed.Market.Evolution;
using Seed.Market.Trading;

namespace Seed.Market.Tests;

public class CliTests
{
    [Fact]
    public void ExperimentTracker_WritesValidJson()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"seed_cli_test_{Guid.NewGuid():N}");
        try
        {
            using (var tracker = new ExperimentTracker(tmpDir, MarketConfig.Default, "test"))
            {
                tracker.RecordMetric("fitness", 0.42f);
                tracker.RecordMetric("trades", 15);
                tracker.RecordMetric("sharpe", 1.23f);
            }

            var expDir = Path.Combine(tmpDir, "experiments");
            Assert.True(Directory.Exists(expDir));
            var files = Directory.GetFiles(expDir, "*.json");
            Assert.Single(files);

            var json = File.ReadAllText(files[0]);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.True(root.TryGetProperty("experimentId", out _));
            Assert.True(root.TryGetProperty("mode", out var mode));
            Assert.Equal("test", mode.GetString());
            Assert.True(root.TryGetProperty("completedUtc", out _));
            Assert.True(root.TryGetProperty("fitness", out _));
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void CompareMode_AllBaselinesProduceResults()
    {
        var prices = new float[300];
        var rng = new Random(42);
        float p = 50000f;
        for (int i = 0; i < 300; i++)
        {
            p *= 1f + (float)(rng.NextDouble() - 0.48) * 0.02f;
            prices[i] = p;
        }

        var config = MarketConfig.Default;
        var bh = BaselineStrategies.BuyAndHold(prices, config);
        var sma = BaselineStrategies.SmaCrossover(prices, config);
        var rnd = BaselineStrategies.RandomAgent(prices, config);
        var mr = BaselineStrategies.MeanReversion(prices, config);

        var results = new[] { bh, sma, rnd, mr };
        Assert.Equal(4, results.Length);
        foreach (var r in results)
        {
            Assert.False(float.IsNaN(r.Fitness));
            Assert.False(float.IsInfinity(r.Fitness));
        }
    }

    [Fact]
    public void StressTest_HigherFees_DegradesFitness()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);

        var config1x = MarketConfig.Default with { InitialCapital = 10_000m };
        var config5x = config1x with
        {
            MakerFee = config1x.MakerFee * 5,
            TakerFee = config1x.TakerFee * 5,
            SlippageBps = config1x.SlippageBps * 5
        };

        var eval1 = new MarketEvaluator(config1x);
        var eval5 = new MarketEvaluator(config5x);

        var (snaps, prices) = CreateSyntheticData(200);
        var r1 = eval1.EvaluateSingle(genome, snaps, prices, 0);
        var r5 = eval5.EvaluateSingle(genome, snaps, prices, 0);

        Assert.True(r5.Fitness.Fitness <= r1.Fitness.Fitness,
            $"5x fees ({r5.Fitness.Fitness:F4}) should not exceed 1x ({r1.Fitness.Fitness:F4})");
    }

    [Fact]
    public void CheckpointSavesSpeciesIds()
    {
        var rng = new Rng64(42);
        var pop = Enumerable.Range(0, 10).Select(_ => (IGenome)SeedGenome.CreateRandom(rng)).ToList();
        var speciesIds = new List<int> { 0, 0, 1, 1, 1, 2, 2, 3, 3, 3 };

        var path = Path.Combine(Path.GetTempPath(), $"seed_cli_cp_{Guid.NewGuid():N}.json");
        try
        {
            var cp = CheckpointState.FromPopulation(pop, 10, 0.5f, speciesIds);
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
    public void FitnessWeights_Configurable()
    {
        var portfolio = new PortfolioState
        {
            Balance = 11000m, InitialBalance = 10000m, MaxEquity = 11000m
        };
        for (int i = 0; i < 20; i++)
        {
            portfolio.TradeHistory.Add(new ClosedTrade(
                "BTCUSDT", TradeDirection.Long, 50000m, 50100m, 0.01m,
                10m, 0.3m, 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            portfolio.EquityCurve.Add(10000f + i * 50f);
        }

        var defaultResult = MarketFitness.ComputeDetailed(portfolio, 50000m, 10f);
        var heavySharpe = MarketFitness.ComputeDetailed(portfolio, 50000m, 10f,
            wSharpe: 0.90f, wSortino: 0.025f, wReturn: 0.025f,
            wDrawdownDuration: 0.025f, wCVaR: 0.025f);

        Assert.NotEqual(defaultResult.Fitness, heavySharpe.Fitness);
    }

    [Fact]
    public void NeuroAblation_AllConfigsProduceValidFitness()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);

        var configurations = new LearningParams[]
        {
            genome.Learn,
            genome.Learn with { AlphaReward = 0f },
            genome.Learn with { AlphaPain = 0f },
            genome.Learn with { AlphaCuriosity = 0f },
            genome.Learn with { AlphaReward = 0f, AlphaPain = 0f },
            genome.Learn with { AlphaPain = 0f, AlphaCuriosity = 0f },
        };

        var config = MarketConfig.Default;
        var developer = new Seed.Development.BrainDeveloper(
            Seed.Market.Agents.MarketAgent.InputCount,
            Seed.Market.Agents.MarketAgent.OutputCount);
        var devCtx = new DevelopmentContext(42, 0);
        var budget = Seed.Market.Evolution.MarketEvaluator.MarketBrainBudget;

        var (snaps, prices) = CreateSyntheticData(100);

        foreach (var lp in configurations)
        {
            var graph = developer.CompileGraph(genome, budget, devCtx);
            var brain = new Seed.Brain.BrainRuntime(graph, lp, genome.Stable, 1);
            var trader = new Seed.Market.Trading.PaperTrader(config);
            var agent = new Seed.Market.Agents.MarketAgent(genome.GenomeId, brain, trader);

            for (int t = 0; t < snaps.Length; t++)
                agent.ProcessTick(snaps[t], (decimal)prices[t]);

            trader.CloseAllPositions(agent.Portfolio, (decimal)prices[^1], snaps.Length);
            float fitness = MarketFitness.ComputeDetailed(agent.Portfolio, (decimal)prices[^1]).Fitness;
            Assert.False(float.IsNaN(fitness), $"NaN fitness with {lp}");
            Assert.False(float.IsInfinity(fitness), $"Inf fitness with {lp}");
        }
    }

    private static (Seed.Market.Signals.SignalSnapshot[], float[]) CreateSyntheticData(int length)
    {
        var normalizer = new Seed.Market.Signals.SignalNormalizer();
        var snapshots = new Seed.Market.Signals.SignalSnapshot[length];
        var prices = new float[length];
        float price = 50000f;
        var rng = new Random(42);

        for (int i = 0; i < length; i++)
        {
            price *= 1f + (float)(rng.NextDouble() - 0.498) * 0.02f;
            prices[i] = price;
            var raw = new float[Seed.Market.Signals.SignalIndex.Count];
            raw[Seed.Market.Signals.SignalIndex.BtcPrice] = price;
            raw[Seed.Market.Signals.SignalIndex.BtcReturn1h] = i > 0 ? (price - prices[i - 1]) / prices[i - 1] : 0f;
            raw[Seed.Market.Signals.SignalIndex.BtcVolume1h] = 1000f;
            snapshots[i] = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddHours(i), i);
        }
        return (snapshots, prices);
    }
}
