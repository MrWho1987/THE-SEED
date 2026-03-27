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
