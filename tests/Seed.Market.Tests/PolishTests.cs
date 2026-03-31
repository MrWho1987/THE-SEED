using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Market.Trading;

namespace Seed.Market.Tests;

public class PolishTests
{
    [Fact]
    public void IFitnessFunction_Injectable()
    {
        var alwaysOne = new ConstantFitness(1.0f);
        var portfolio = new PortfolioState
        {
            Balance = 10000m,
            InitialBalance = 10000m,
            MaxEquity = 10000m,
        };
        for (int i = 0; i < 10; i++)
        {
            portfolio.TradeHistory.Add(new ClosedTrade(
                "BTCUSDT", TradeDirection.Long, 50000m, 50100m, 0.01m,
                10m, 0.3m, 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            portfolio.EquityCurve.Add(10000f + i * 10f);
        }

        var result = alwaysOne.ComputeDetailed(portfolio, 50000m);
        Assert.Equal(1.0f, result.Fitness);
    }

    [Fact]
    public void ITrader_Swappable()
    {
        var rejectAll = new RejectAllTrader();
        var portfolio = rejectAll.CreatePortfolio();

        for (int t = 0; t < 100; t++)
        {
            var signal = new TradingSignal(TradeDirection.Long, 1f, 1f, false);
            var ctx = new TickContext(50000m, 0m, 0f, t);
            rejectAll.ProcessSignal(signal, portfolio, ctx);
        }

        Assert.Equal(0, portfolio.TotalTrades);
        Assert.Empty(portfolio.OpenPositions);
    }

    [Fact]
    public void DefaultFitnessFunction_MatchesStaticMethod()
    {
        var portfolio = new PortfolioState
        {
            Balance = 11000m,
            InitialBalance = 10000m,
            MaxEquity = 11000m,
        };
        for (int i = 0; i < 10; i++)
        {
            portfolio.TradeHistory.Add(new ClosedTrade(
                "BTCUSDT", TradeDirection.Long, 50000m, 50100m, 0.01m,
                10m, 0.3m, 5, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
            portfolio.EquityCurve.Add(10000f + i * 100f);
        }

        var staticResult = MarketFitness.ComputeDetailed(portfolio, 50000m, 10f);
        var interfaceResult = new DefaultFitnessFunction(10f).ComputeDetailed(portfolio, 50000m);

        Assert.Equal(staticResult.Fitness, interfaceResult.Fitness, 4);
    }

    [Fact]
    public void GoldenReference_Determinism()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(4, 2);
        var graph = dev.CompileGraph(genome, DevelopmentBudget.Default, new DevelopmentContext(42, 0));
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);

        var inputs = new float[4];
        for (int i = 0; i < 4; i++) inputs[i] = 0.5f;

        // Run 100 steps deterministically
        float[] output1 = [];
        for (int t = 0; t < 100; t++)
        {
            var ctx = new BrainStepContext(t);
            var o = brain.Step(inputs, ctx);
            if (t == 99) output1 = o.ToArray();
        }

        // Run again with fresh brain from same genome
        var brain2 = new BrainRuntime(
            dev.CompileGraph(genome, DevelopmentBudget.Default, new DevelopmentContext(42, 0)),
            genome.Learn, genome.Stable, 1);

        float[] output2 = [];
        for (int t = 0; t < 100; t++)
        {
            var ctx2 = new BrainStepContext(t);
            var o = brain2.Step(inputs, ctx2);
            if (t == 99) output2 = o.ToArray();
        }

        Assert.Equal(output1.Length, output2.Length);
        for (int i = 0; i < output1.Length; i++)
            Assert.Equal(output1[i], output2[i], 6);
    }

    [Fact]
    public void CartPoleAgent_CompileAndStep()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);

        // Cart-pole: 4 inputs (pos, vel, angle, angular_vel), 1 output (force)
        var dev = new BrainDeveloper(4, 1);
        var graph = dev.CompileGraph(genome, DevelopmentBudget.Default, new DevelopmentContext(42, 0));

        Assert.Equal(4, graph.InputCount);
        Assert.Equal(1, graph.OutputCount);

        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);

        for (int t = 0; t < 50; t++)
        {
            float[] obs = [0.1f * t, 0.01f * t, 0.05f * t, -0.02f * t];
            var outputs = brain.Step(obs, new BrainStepContext(t));

            Assert.Equal(1, outputs.Length);
            Assert.False(float.IsNaN(outputs[0]), $"Cart-pole output is NaN at tick {t}");
            Assert.True(outputs[0] >= -1f && outputs[0] <= 1f,
                $"Cart-pole output {outputs[0]} out of tanh range at tick {t}");
        }
    }

    [Fact]
    public void BrainDeveloper_DifferentIOCounts_AllCompile()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var devCtx = new DevelopmentContext(42, 0);

        int[][] configs = [[2, 1], [10, 3], [88, 5], [4, 2]];
        foreach (var cfg in configs)
        {
            var dev = new BrainDeveloper(cfg[0], cfg[1]);
            var graph = dev.CompileGraph(genome, DevelopmentBudget.Default, devCtx);
            Assert.Equal(cfg[0], graph.InputCount);
            Assert.Equal(cfg[1], graph.OutputCount);

            var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
            var outputs = brain.Step(new float[cfg[0]], new BrainStepContext(0));
            Assert.Equal(cfg[1], outputs.Length);
        }
    }

    [Fact]
    public void EvolvableSubstrate_MutationChanges()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var innovations = InnovationTracker.CreateDefault();
        bool changed = false;

        for (int i = 0; i < 100; i++)
        {
            var mutCtx = new MutationContext(42, i, MutationConfig.Default, innovations, new Rng64((ulong)(i + 1)));
            var mutated = (SeedGenome)genome.Mutate(mutCtx);
            if (mutated.Dev.SubstrateWidth != 16 || mutated.Dev.SubstrateHeight != 16 || mutated.Dev.SubstrateLayers != 3)
            {
                changed = true;
                break;
            }
        }
        Assert.True(changed, "At least one mutation should change substrate dimensions over 100 tries");
    }

    [Fact]
    public void EvolvableSubstrate_BoundsEnforced()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var innovations = InnovationTracker.CreateDefault();
        var atMin = new SeedGenome(genome.GenomeId, genome.Cppn, genome.Dev with { SubstrateWidth = 4, SubstrateHeight = 4, SubstrateLayers = 1 }, genome.Learn, genome.Stable, genome.Reserved);

        for (int i = 0; i < 100; i++)
        {
            var mutCtx = new MutationContext(42, i, MutationConfig.Default, innovations, new Rng64((ulong)(i + 500)));
            var mutated = (SeedGenome)atMin.Mutate(mutCtx);
            Assert.True(mutated.Dev.SubstrateWidth >= 4, $"Width {mutated.Dev.SubstrateWidth} below 4");
            Assert.True(mutated.Dev.SubstrateHeight >= 4, $"Height {mutated.Dev.SubstrateHeight} below 4");
            Assert.True(mutated.Dev.SubstrateLayers >= 1, $"Layers {mutated.Dev.SubstrateLayers} below 1");
        }
    }

    [Fact]
    public void EvolvableSubstrate_NonDefaultCompiles()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var custom = new SeedGenome(genome.GenomeId, genome.Cppn, genome.Dev with { SubstrateWidth = 8, SubstrateHeight = 8, SubstrateLayers = 2 }, genome.Learn, genome.Stable, genome.Reserved);

        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var budget = MarketEvaluator.MarketBrainBudget with { HiddenWidth = 8, HiddenHeight = 8, HiddenLayers = 2 };
        var graph = dev.CompileGraph(custom, budget, new DevelopmentContext(42, 0));

        Assert.Equal(SignalIndex.Count, graph.InputCount);
        Assert.Equal(5, graph.OutputCount);

        var brain = new BrainRuntime(graph, custom.Learn, custom.Stable, 1);
        var ctx = new BrainStepContext(0);
        var outputs = brain.Step(new float[88], ctx);
        Assert.Equal(5, outputs.Length);
    }

    private sealed class ConstantFitness : IFitnessFunction
    {
        private readonly float _value;
        public ConstantFitness(float value) => _value = value;
        public FitnessBreakdown ComputeDetailed(PortfolioState portfolio, decimal finalPrice)
        {
            return new FitnessBreakdown(
                Fitness: _value, ReturnPct: 0, MaxDrawdown: 0,
                TotalTrades: portfolio.TotalTrades, WinRate: 0, NetPnl: 0,
                IsActive: true, RawSharpe: 0, AdjustedSharpe: 0,
                Sortino: 0, AdjustedSortino: 0, CVaR5: 0, MaxDrawdownDuration: 0, ShrinkageConfidence: 0);
        }
    }

    [Fact]
    public void PaperMode_UsesGenomeSubstrate()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var customGenome = new SeedGenome(genome.GenomeId, genome.Cppn,
            genome.Dev with { SubstrateWidth = 8, SubstrateHeight = 8, SubstrateLayers = 2 },
            genome.Learn, genome.Stable, genome.Reserved);

        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);

        var defaultBudget = MarketEvaluator.MarketBrainBudget;
        var customBudget = defaultBudget with
        {
            HiddenWidth = customGenome.Dev.SubstrateWidth,
            HiddenHeight = customGenome.Dev.SubstrateHeight,
            HiddenLayers = customGenome.Dev.SubstrateLayers
        };

        var defaultGraph = dev.CompileGraph(genome, defaultBudget, new DevelopmentContext(42, 0));
        var customGraph = dev.CompileGraph(customGenome, customBudget, new DevelopmentContext(42, 0));

        Assert.NotEqual(defaultGraph.NodeCount, customGraph.NodeCount);
    }

    [Fact]
    public void MarketEvaluator_UsesInjectedFitness()
    {
        var config = MarketConfig.Default with { PopulationSize = 3 };
        var constantFitness = new ConstantFitness(1.0f);
        var evaluator = new MarketEvaluator(config, constantFitness);

        var rng = new Rng64(42);
        var population = Enumerable.Range(0, 3).Select(_ => (IGenome)SeedGenome.CreateRandom(rng)).ToList();
        var normalizer = new SignalNormalizer();
        var snaps = new SignalSnapshot[50];
        var prices = new float[50];
        float price = 50000f;
        var dataRng = new Random(42);
        for (int i = 0; i < 50; i++)
        {
            price *= 1f + (float)(dataRng.NextDouble() - 0.498) * 0.02f;
            prices[i] = price;
            var raw = new float[SignalIndex.Count];
            raw[SignalIndex.BtcPrice] = price;
            snaps[i] = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddHours(i), i);
        }

        var rawVols = new float[50];
        var rawFund = new float[50];
        for (int i = 0; i < 50; i++) { rawVols[i] = 1000f; rawFund[i] = 0.0001f; }

        var results = evaluator.Evaluate(population, snaps, prices, rawVols, rawFund, 0);
        foreach (var r in results.Values)
            Assert.Equal(1.0f, r.Fitness.Fitness);
    }

    [Fact]
    public void GoldenReference_StableAcrossRuns()
    {
        var rng1 = new Rng64(42);
        var genome1 = SeedGenome.CreateRandom(rng1);
        var dev = new BrainDeveloper(4, 2);
        var graph1 = dev.CompileGraph(genome1, DevelopmentBudget.Default, new DevelopmentContext(42, 0));
        var brain1 = new BrainRuntime(graph1, genome1.Learn, genome1.Stable, 1);

        var inputs = new float[] { 0.5f, 0.5f, 0.5f, 0.5f };
        float[] run1Output = [];
        for (int t = 0; t < 100; t++)
        {
            var ctx = new BrainStepContext(t);
            var o = brain1.Step(inputs, ctx);
            if (t == 99) run1Output = o.ToArray();
        }

        Assert.Equal(2, run1Output.Length);
        for (int i = 0; i < run1Output.Length; i++)
        {
            Assert.False(float.IsNaN(run1Output[i]), $"Output {i} is NaN");
            Assert.InRange(run1Output[i], -1f, 1f);
        }

        // Second independent run from scratch
        var rng2 = new Rng64(42);
        var genome2 = SeedGenome.CreateRandom(rng2);
        var graph2 = dev.CompileGraph(genome2, DevelopmentBudget.Default, new DevelopmentContext(42, 0));
        var brain2 = new BrainRuntime(graph2, genome2.Learn, genome2.Stable, 1);

        float[] run2Output = [];
        for (int t = 0; t < 100; t++)
        {
            var ctx = new BrainStepContext(t);
            var o = brain2.Step(inputs, ctx);
            if (t == 99) run2Output = o.ToArray();
        }

        Assert.Equal(run1Output.Length, run2Output.Length);
        for (int i = 0; i < run1Output.Length; i++)
            Assert.Equal(run1Output[i], run2Output[i]);
    }

    private sealed class RejectAllTrader : ITrader
    {
        public PortfolioState CreatePortfolio() => new()
        {
            Balance = 10000m,
            InitialBalance = 10000m,
            MaxEquity = 10000m,
            LastResetDay = DateTimeOffset.UtcNow
        };

        public TradeResult ProcessSignal(TradingSignal signal, PortfolioState portfolio, TickContext ctx)
            => new(false, 0, 0, 0, 0, "Rejected");

        public void CloseAllPositions(PortfolioState portfolio, decimal currentPrice, int currentTick) { }
    }
}
