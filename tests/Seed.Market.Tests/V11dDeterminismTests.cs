using Seed.Genetics;
using Seed.Market.Backtest;
using Seed.Market.Evolution;
using Seed.Market.Indicators;
using Seed.Market.Signals;
using Seed.Observatory;

namespace Seed.Market.Tests;

/// <summary>
/// V11d determinism regression: same RunSeed must produce bit-identical evolution
/// trajectories. Validates Fix 4 (Speciation representative now uses first-inserted
/// member instead of Guid.NewGuid() ordering).
///
/// Before Fix 4, runs with the same seed diverged at gen 3 due to non-deterministic
/// species representative selection, leading to wildly different fitness trajectories.
/// </summary>
public class V11dDeterminismTests
{
    [Fact]
    public void SameSeed_TwoRuns_ProduceBitIdenticalReports_Gen0to5()
    {
        var (snapshots, prices, volumes, funding) = BuildSyntheticData(500, seed: 42);

        var reportsA = RunEvolution(snapshots, prices, volumes, funding, runSeed: 42, gens: 6);
        var reportsB = RunEvolution(snapshots, prices, volumes, funding, runSeed: 42, gens: 6);

        Assert.Equal(reportsA.Count, reportsB.Count);
        for (int g = 0; g < reportsA.Count; g++)
        {
            Assert.Equal(reportsA[g].BestFitness, reportsB[g].BestFitness);
            Assert.Equal(reportsA[g].MeanFitness, reportsB[g].MeanFitness);
            Assert.Equal(reportsA[g].SpeciesCount, reportsB[g].SpeciesCount);
            Assert.Equal(reportsA[g].InactiveCount, reportsB[g].InactiveCount);
            Assert.Equal(reportsA[g].TotalTrades, reportsB[g].TotalTrades);
        }
    }

    [Fact]
    public void DifferentSeed_TwoRuns_ProduceDifferentTrajectories()
    {
        // Sanity check: a different seed should produce a different trajectory
        // (otherwise we have a determinism issue elsewhere — outputs not depending
        // on the seed).
        var (snapshots, prices, volumes, funding) = BuildSyntheticData(500, seed: 42);

        var reportsA = RunEvolution(snapshots, prices, volumes, funding, runSeed: 42, gens: 6);
        var reportsB = RunEvolution(snapshots, prices, volumes, funding, runSeed: 43, gens: 6);

        bool anyDifferent = false;
        for (int g = 0; g < reportsA.Count; g++)
        {
            if (reportsA[g].BestFitness != reportsB[g].BestFitness ||
                reportsA[g].MeanFitness != reportsB[g].MeanFitness)
            {
                anyDifferent = true;
                break;
            }
        }
        Assert.True(anyDifferent,
            "Different seeds must produce different evolution trajectories");
    }

    private static List<GenerationReport> RunEvolution(
        SignalSnapshot[] snapshots, float[] prices, float[] volumes, float[] funding,
        ulong runSeed, int gens)
    {
        var config = MarketConfig.Default with
        {
            PopulationSize = 30,
            Generations = gens,
            InitialCapital = 10_000m,
            MaxPositionPct = 0.08m,
            MaxLeverage = 1f,
            StopLossPct = 0.02m,
            MinTradesForActive = 1,
            ActivityBonusScale = 0.05f,
            InactivityPenalty = -0.10f,
            FitnessSharpeWeight = 0.10f,
            FitnessSortinoWeight = 0.05f,
            FitnessReturnWeight = 0.40f,
            FitnessDrawdownDurationWeight = 0.15f,
            FitnessCVaRWeight = 0.17f,
            FitnessCalmarWeight = 0.05f,
            FitnessInfoRatioWeight = 0.03f,
            FitnessFeeDragWeight = 0.03f,
            FitnessDiversificationWeight = 0.02f,
            ShrinkageK = 2f,
            RatioClampMax = 10f,
            ReturnFloor = -0.50f,
            RunSeed = runSeed,
            CandleInterval = "15m",
        };

        var evolution = new MarketEvolution(config, NullObservatory.Instance);
        evolution.Initialize();

        var reports = new List<GenerationReport>();
        for (int g = 0; g < gens; g++)
            reports.Add(evolution.RunGeneration(snapshots, prices, volumes, funding));
        return reports;
    }

    private static (SignalSnapshot[] snapshots, float[] prices, float[] volumes, float[] funding)
        BuildSyntheticData(int n, int seed)
    {
        var rng = new Random(seed);
        var candles = new TechnicalIndicators.Candle[n];
        var startTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        float price = 50_000f;
        for (int i = 0; i < n; i++)
        {
            float drift = 5f;
            float noise = (float)(rng.NextDouble() - 0.5) * 200f;
            float open = price;
            price = price + drift + noise;
            float high = MathF.Max(open, price) + (float)rng.NextDouble() * 50f;
            float low = MathF.Min(open, price) - (float)rng.NextDouble() * 50f;
            float volume = 100f + (float)rng.NextDouble() * 50f;

            candles[i] = new TechnicalIndicators.Candle(
                Open: open, High: high, Low: low, Close: price, Volume: volume,
                Time: startTime.AddMinutes(15 * i));
        }

        return HistoricalDataStore.CandlesToSignals(candles, enrichment: null, barsPerHour: 4);
    }
}
