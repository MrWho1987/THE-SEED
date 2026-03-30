using Seed.Core;
using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Observatory;

namespace Seed.Market.Tests;

public class EvolutionSmokeTests
{
    [Fact]
    public void FiveGenerations_ProducesReport()
    {
        var config = MarketConfig.Default with
        {
            PopulationSize = 10,
            Generations = 5,
            InitialCapital = 10_000m,
            RunSeed = 42
        };

        var observatory = new FileObservatory(
            Path.Combine(Path.GetTempPath(), $"seed_test_{Guid.NewGuid()}.jsonl"));
        var evo = new MarketEvolution(config, observatory);
        evo.Initialize();

        Assert.Equal(10, evo.Population.Count);

        var (snapshots, prices, rawVols, rawFund) = CreateSyntheticData(300);

        GenerationReport? lastReport = null;
        for (int g = 0; g < 5; g++)
        {
            lastReport = evo.RunGeneration(snapshots, prices, rawVols, rawFund);
            Assert.True(lastReport.Value.PopulationSize > 0);
            Assert.True(lastReport.Value.SpeciesCount > 0);
        }

        Assert.NotNull(lastReport);
        Assert.Equal(5, evo.Generation);
        Assert.NotNull(evo.GetBestGenome());
    }

    [Fact]
    public void SpeciesEmergeDuringEvolution()
    {
        var config = MarketConfig.Default with
        {
            PopulationSize = 20,
            RunSeed = 123
        };

        var observatory = new FileObservatory(
            Path.Combine(Path.GetTempPath(), $"seed_test_{Guid.NewGuid()}.jsonl"));
        var evo = new MarketEvolution(config, observatory);
        evo.Initialize();

        var (snapshots, prices, rawVols, rawFund) = CreateSyntheticData(200);

        for (int g = 0; g < 10; g++)
            evo.RunGeneration(snapshots, prices, rawVols, rawFund);

        Assert.True(evo.SpeciesCount >= 1, "At least one species should exist");
    }

    private static (SignalSnapshot[], float[], float[], float[]) CreateSyntheticData(int length)
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
            raw[SignalIndex.Rsi14] = 50f + (float)(rng.NextDouble() - 0.5) * 30f;
            snapshots[i] = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddHours(i), i);
        }
        return (snapshots, prices, rawVolumes, rawFundingRates);
    }
}
