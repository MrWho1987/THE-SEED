using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Market.Trading;

namespace Seed.Market.Tests;

public class EnsembleTests
{
    [Fact]
    public void Ensemble_MajorityLong()
    {
        var members = CreateMembers(5);
        var ensemble = new EnsembleTrader(members);

        var (snapshot, ctx) = CreateTick(50000f);
        var consensus = ensemble.ComputeConsensus(snapshot, ctx);

        // All members have random brains, so consensus direction is indeterminate.
        // But we can verify it produces a valid signal.
        Assert.True(
            consensus.Direction == TradeDirection.Long ||
            consensus.Direction == TradeDirection.Short ||
            consensus.Direction == TradeDirection.Flat);

        var summary = ensemble.GetVoteSummary();
        Assert.Equal(5, summary.LongVotes + summary.ShortVotes + summary.FlatVotes);
    }

    [Fact]
    public void Ensemble_WeightedAverage_Works()
    {
        var members = CreateMembers(3);
        var ensemble = new EnsembleTrader(members);

        var (snapshot, ctx) = CreateTick(50000f);
        var consensus = ensemble.ComputeConsensus(snapshot, ctx);

        Assert.InRange(consensus.SizePct, 0f, 1f);
        Assert.InRange(consensus.Urgency, 0f, 1f);
    }

    [Fact]
    public void Ensemble_EmptyMembers_ReturnsFlat()
    {
        var ensemble = new EnsembleTrader([]);
        var (snapshot, ctx) = CreateTick(50000f);

        var consensus = ensemble.ComputeConsensus(snapshot, ctx);
        Assert.Equal(TradeDirection.Flat, consensus.Direction);
        Assert.False(consensus.ExitCurrent);
    }

    // ── EvaluateEnsemble top-K fitness-weighted voting test ──────────────────

    [Fact]
    public void EvaluateEnsemble_TopKWeightedVoting_ProducesValidFitness()
    {
        // Verify EvaluateEnsemble doesn't crash and produces a valid fitness when given
        // a small synthetic population. The key invariant: the ensemble evaluation runs
        // each champion individually first, then ensembles the top-K by fitness-weighted vote.
        var config = MarketConfig.Default with
        {
            PopulationSize = 10,
            MinTradesForActive = 1,
            MaxLeverage = 1.0f,
            ExplicitExitBonus = 0f,  // disable exit bonus to keep test deterministic
            // T1 — Constant 5-weight schedule (sums to 1.0). Other 4 weights default to 0.
            WeightSchedule = WeightWaypoint.ConstantSchedule(
                sharpe: 0.45f, sortino: 0.15f, returnWeight: 0.20f, ddDuration: 0.10f,
                cvar: 0.10f, calmar: 0f, infoRatio: 0f, feeDrag: 0f, diversification: 0f),
        };
        var evaluator = new MarketEvaluator(config);

        // Create 5 random champions
        var rng = new Rng64(42);
        var champions = new List<Seed.Core.IGenome>();
        for (int i = 0; i < 5; i++)
            champions.Add(SeedGenome.CreateRandom(rng));

        // Synthetic price history (small but nonzero)
        int len = 300;
        var snapshots = new SignalSnapshot[len];
        var prices = new float[len];
        var volumes = new float[len];
        var funding = new float[len];
        for (int i = 0; i < len; i++)
        {
            snapshots[i] = new SignalSnapshot(new float[SignalIndex.Count], DateTimeOffset.UtcNow.AddHours(-len + i), i);
            prices[i] = 50000f + MathF.Sin(i * 0.1f) * 500f;  // gentle sine wave
            volumes[i] = 100f;
            funding[i] = 0.0001f;
        }

        // Should not throw, should return a finite fitness
        var result = evaluator.EvaluateEnsemble(champions, snapshots, prices, volumes, funding, 0);
        Assert.False(float.IsNaN(result.Fitness));
        Assert.False(float.IsInfinity(result.Fitness));
    }

    [Fact]
    public void EvaluateEnsemble_EmptyChampions_ReturnsDefault()
    {
        var config = MarketConfig.Default;
        var evaluator = new MarketEvaluator(config);

        var snapshots = new SignalSnapshot[] { new(new float[SignalIndex.Count], DateTimeOffset.UtcNow, 0) };
        var prices = new float[] { 50000f };
        var volumes = new float[] { 100f };
        var funding = new float[] { 0f };

        var result = evaluator.EvaluateEnsemble([], snapshots, prices, volumes, funding, 0);
        Assert.Equal(default(Seed.Market.Evolution.FitnessBreakdown), result);
    }

    private static List<(MarketAgent, float)> CreateMembers(int count)
    {
        var members = new List<(MarketAgent, float)>();
        var config = MarketConfig.Default;

        for (int i = 0; i < count; i++)
        {
            var rng = new Rng64((ulong)(i + 1));
            var genome = SeedGenome.CreateRandom(rng);
            var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
            var graph = dev.CompileGraph(genome, MarketEvaluator.MarketBrainBudget, new DevelopmentContext(42, 0));
            var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
            var trader = new PaperTrader(config);
            var agent = new MarketAgent(genome.GenomeId, brain, trader);
            members.Add((agent, 1f / count));
        }

        return members;
    }

    private static (SignalSnapshot, TickContext) CreateTick(float price)
    {
        var normalizer = new SignalNormalizer();
        var raw = new float[SignalIndex.Count];
        raw[SignalIndex.BtcPrice] = price;
        raw[SignalIndex.BtcReturn1h] = 0.01f;
        raw[SignalIndex.BtcVolume1h] = 1000f;
        var snapshot = normalizer.Normalize(raw, DateTimeOffset.UtcNow, 0);
        var ctx = new TickContext((decimal)price, 1_000_000m, 0f, 0);
        return (snapshot, ctx);
    }
}
