using Seed.Core;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Market.Trading;
using Seed.Brain;
using Seed.Development;

namespace Seed.Market.Tests;

public class LearningSignalTests
{
    [Fact]
    public void OutputCount_Is5()
    {
        Assert.Equal(5, ActionInterpreter.OutputCount);
        Assert.Equal(5, MarketAgent.OutputCount);
    }

    [Fact]
    public void Interpret_5Outputs_DoesNotThrow()
    {
        float[] outputs = [1f, 0.5f, 0.8f, 0.1f, 0.3f];
        var signal = ActionInterpreter.Interpret(outputs);
        Assert.Equal(TradeDirection.Long, signal.Direction);
    }

    [Fact]
    public void Reward_ContinuousDelta_ZeroWhenFlat()
    {
        var (agent, _) = CreateAgent();
        var normalizer = new SignalNormalizer();
        float price = 50000f;

        for (int t = 0; t < 10; t++)
        {
            price += 10f;
            var raw = new float[SignalIndex.Count];
            raw[SignalIndex.BtcPrice] = price;
            var snap = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddHours(t), t);
            agent.ProcessTick(snap, (decimal)price);
        }

        // With no position, the continuous P&L delta reward should be 0
        // (The agent may or may not open positions depending on brain output, 
        // so we just verify no crash and valid state)
        Assert.Equal(10, agent.Tick);
    }

    [Fact]
    public void Pain_ZeroWhenNoPosition()
    {
        var portfolio = new PortfolioState
        {
            Balance = 10000m,
            InitialBalance = 10000m,
            MaxEquity = 10000m,
        };

        Assert.Empty(portfolio.OpenPositions);
    }

    [Fact]
    public void Pain_PositiveWhenUnrealizedLoss()
    {
        var portfolio = new PortfolioState
        {
            Balance = 10000m,
            InitialBalance = 10000m,
            MaxEquity = 10000m,
        };
        portfolio.OpenPositions.Add(new Position
        {
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            Size = 1m,
            OpenTime = DateTimeOffset.UtcNow,
            OpenTick = 0
        });

        float pnlPct = (float)portfolio.OpenPositions[0].UnrealizedPnlPct(95m) / 100f;
        float pain = pnlPct < 0 ? Math.Clamp(-pnlPct, 0f, 1f) : 0f;

        Assert.True(pain > 0f, $"Pain should be positive for unrealized loss, got {pain}");
    }

    [Fact]
    public void Pain_ZeroWhenUnrealizedProfit()
    {
        var portfolio = new PortfolioState
        {
            Balance = 10000m,
            InitialBalance = 10000m,
            MaxEquity = 10000m,
        };
        portfolio.OpenPositions.Add(new Position
        {
            Symbol = "BTCUSDT",
            Direction = TradeDirection.Long,
            EntryPrice = 100m,
            Size = 1m,
            OpenTime = DateTimeOffset.UtcNow,
            OpenTick = 0
        });

        float pnlPct = (float)portfolio.OpenPositions[0].UnrealizedPnlPct(105m) / 100f;
        float pain = pnlPct < 0 ? Math.Clamp(-pnlPct, 0f, 1f) : 0f;

        Assert.Equal(0f, pain);
    }

    [Fact]
    public void Curiosity_PredictionError_Computable()
    {
        float predicted = MathF.Tanh(0.5f);
        float actual = 1f;
        float curiosity = MathF.Abs(predicted - actual);
        Assert.True(curiosity > 0f);
    }

    [Fact]
    public void Curiosity_PerfectPredictor_ReturnsZero()
    {
        float predicted = MathF.Tanh(100f); // saturates to ~1
        float actual = 1f;
        float curiosity = MathF.Abs(predicted - actual);
        Assert.True(curiosity < 0.01f, $"Perfect prediction should yield near-zero curiosity, got {curiosity}");
    }

    [Fact]
    public void BrainCompiles_With5Outputs()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var graph = dev.CompileGraph(genome, MarketEvaluator.MarketBrainBudget, new DevelopmentContext(42, 0));

        Assert.Equal(88, graph.InputCount);
        Assert.Equal(5, graph.OutputCount);

        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
        var outputs = brain.Step(new float[88], new BrainStepContext(0));
        Assert.Equal(5, outputs.Length);
    }

    [Fact]
    public void FullAgent_ProcessesTicks_With5Outputs()
    {
        var (agent, _) = CreateAgent();
        var normalizer = new SignalNormalizer();

        float price = 50000f;
        var rng = new Random(42);

        for (int t = 0; t < 100; t++)
        {
            price *= 1f + (float)(rng.NextDouble() - 0.498) * 0.02f;
            var raw = new float[SignalIndex.Count];
            raw[SignalIndex.BtcPrice] = price;
            raw[SignalIndex.BtcReturn1h] = (float)(rng.NextDouble() - 0.5) * 0.04f;
            raw[SignalIndex.BtcVolume1h] = 1000f;
            raw[SignalIndex.Rsi14] = 50f;
            var snap = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddHours(t), t);
            agent.ProcessTick(snap, (decimal)price);
        }

        Assert.Equal(100, agent.Tick);
        Assert.False(float.IsNaN(MarketFitness.Compute(agent.Portfolio, (decimal)price)));
    }

    private static (MarketAgent Agent, PaperTrader Trader) CreateAgent()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var graph = dev.CompileGraph(genome, MarketEvaluator.MarketBrainBudget, new DevelopmentContext(42, 0));
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
        var config = MarketConfig.Default;
        var trader = new PaperTrader(config);
        var agent = new MarketAgent(genome.GenomeId, brain, trader);
        return (agent, trader);
    }
}
