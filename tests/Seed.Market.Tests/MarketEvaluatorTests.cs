using Seed.Core;
using Seed.Genetics;
using Seed.Market.Evolution;
using Seed.Market.Signals;

namespace Seed.Market.Tests;

public class MarketEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsResultForEveryGenome()
    {
        var config = MarketConfig.Default with
        {
            InitialCapital = 10_000m,
            PopulationSize = 5,
            MaxBrainNodes = 100,
            MaxBrainEdges = 500
        };
        var evaluator = new MarketEvaluator(config);
        var rng = new Rng64(42);

        var population = Enumerable.Range(0, 5)
            .Select(_ => (IGenome)SeedGenome.CreateRandom(rng))
            .ToList();

        var (snapshots, prices, rawVols, rawFund) = CreateSyntheticData(200);

        var results = evaluator.Evaluate(population, snapshots, prices, rawVols, rawFund, 0);

        Assert.Equal(5, results.Count);
        foreach (var genome in population)
            Assert.True(results.ContainsKey(genome.GenomeId));
    }

    [Fact]
    public void Evaluate_IsDeterministic()
    {
        var config = MarketConfig.Default with
        {
            InitialCapital = 10_000m,
            PopulationSize = 3,
            MaxBrainNodes = 100,
            MaxBrainEdges = 500,
            RunSeed = 123
        };
        var eval1 = new MarketEvaluator(config);
        var eval2 = new MarketEvaluator(config);

        var rng1 = new Rng64(42);
        var rng2 = new Rng64(42);
        var pop1 = Enumerable.Range(0, 3).Select(_ => (IGenome)SeedGenome.CreateRandom(rng1)).ToList();
        var pop2 = Enumerable.Range(0, 3).Select(_ => (IGenome)SeedGenome.CreateRandom(rng2)).ToList();

        var (snaps, prices, rawVols, rawFund) = CreateSyntheticData(100);

        var res1 = eval1.Evaluate(pop1, snaps, prices, rawVols, rawFund, 0);
        var res2 = eval2.Evaluate(pop2, snaps, prices, rawVols, rawFund, 0);

        for (int i = 0; i < 3; i++)
        {
            var id1 = pop1[i].GenomeId;
            var id2 = pop2[i].GenomeId;
            Assert.Equal(res1[id1].Fitness.Fitness, res2[id2].Fitness.Fitness, 4);
        }
    }

    [Fact]
    public void InactiveAgents_GetPenalized()
    {
        var config = MarketConfig.Default with
        {
            InitialCapital = 10_000m,
            MaxBrainNodes = 50,
            MaxBrainEdges = 100
        };
        var evaluator = new MarketEvaluator(config);
        var rng = new Rng64(999);
        var population = new List<IGenome> { SeedGenome.CreateRandom(rng) };

        // Very short history -- barely enough for any trades
        var (snaps, prices, rawVols, rawFund) = CreateSyntheticData(30);
        var results = evaluator.Evaluate(population, snaps, prices, rawVols, rawFund, 0);

        var result = results.Values.First();
        // Agent with minimal brain likely doesn't make enough trades
        Assert.True(result.Fitness.Fitness <= 0f || result.Fitness.TotalTrades >= 3);
    }

    private static (SignalSnapshot[] snapshots, float[] prices, float[] rawVolumes, float[] rawFundingRates) CreateSyntheticData(int length)
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
            rawFundingRates[i] = 0.0001f * ((float)rng.NextDouble() - 0.5f);

            var raw = new float[SignalIndex.Count];
            raw[SignalIndex.BtcPrice] = price;
            raw[SignalIndex.BtcReturn1h] = i > 0 ? (price - prices[i - 1]) / prices[i - 1] : 0f;
            raw[SignalIndex.BtcVolume1h] = rawVolumes[i];
            raw[SignalIndex.FearGreedIndex] = 50f + (float)(rng.NextDouble() - 0.5) * 40f;
            raw[SignalIndex.Rsi14] = 50f + (float)(rng.NextDouble() - 0.5) * 30f;

            snapshots[i] = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddHours(i), i);
        }

        return (snapshots, prices, rawVolumes, rawFundingRates);
    }
}
