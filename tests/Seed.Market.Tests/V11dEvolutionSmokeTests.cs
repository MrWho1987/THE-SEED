using Seed.Core;
using Seed.Genetics;
using Seed.Market.Backtest;
using Seed.Market.Evolution;
using Seed.Market.Indicators;
using Seed.Market.Signals;
using Seed.Observatory;

namespace Seed.Market.Tests;

/// <summary>
/// V11d regression test: a deterministic mini-evolution must find a profitable genome
/// within 15 generations on synthetic data. If the reward shape ever regresses to
/// aversive conditioning (or any other change suppresses exploration), this test will
/// catch it before we waste hours/days on a stuck full training.
///
/// Synthetic data is a 500-bar mild-trend random walk: enough variance to give random
/// brains opportunities to profit, but small enough to run in seconds.
/// </summary>
public class V11dEvolutionSmokeTests
{
    [Fact]
    public void MiniEvolution_FindsProfitableGenome_Within15Gens()
    {
        // Configure for a fast smoke: pop 30, 15 gens, 500-bar synthetic window.
        var config = MarketConfig.Default with
        {
            PopulationSize = 30,
            Generations = 15,
            InitialCapital = 10_000m,
            MaxPositionPct = 0.08m,            // V11d Phase 1 discovery sizing
            MaxLeverage = 1f,                  // simpler — no leverage for the smoke
            StopLossPct = 0.02m,
            MinTradesForActive = 1,            // any trade counts
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
            RunSeed = 42,
            CandleInterval = "15m",
        };

        // Synthetic price series: gentle upward trend with noise (random walk + drift)
        var (snapshots, prices, volumes, funding) = BuildSyntheticData(500, seed: 42);

        var evolution = new MarketEvolution(config, NullObservatory.Instance);
        evolution.Initialize();

        float maxFitness = float.NegativeInfinity;
        int firstPositiveGen = -1;

        for (int gen = 0; gen < config.Generations; gen++)
        {
            var report = evolution.RunGeneration(snapshots, prices, volumes, funding);

            if (report.BestFitness > maxFitness)
                maxFitness = report.BestFitness;

            if (report.BestFitness > 0f && firstPositiveGen < 0)
                firstPositiveGen = gen;

            if (firstPositiveGen >= 0 && report.BestFitness > 0.05f)
                break;  // success — early exit
        }

        Assert.True(maxFitness > 0f,
            $"Evolution must find a positive-fitness genome within {config.Generations} gens. " +
            $"Got max fitness {maxFitness:F4}, first positive at gen {firstPositiveGen}");
    }

    [Fact]
    public void MiniEvolution_PopulationStaysDiverse_NotAllInactive()
    {
        // After 10 gens, at least 30% of the population should be ACTIVE (have trades).
        // This guards against the passive-trap where the entire population converges to
        // do-nothing.
        var config = MarketConfig.Default with
        {
            PopulationSize = 30,
            Generations = 10,
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
            RunSeed = 42,
            CandleInterval = "15m",
        };

        var (snapshots, prices, volumes, funding) = BuildSyntheticData(500, seed: 42);

        var evolution = new MarketEvolution(config, NullObservatory.Instance);
        evolution.Initialize();

        GenerationReport finalReport = default;
        for (int gen = 0; gen < config.Generations; gen++)
            finalReport = evolution.RunGeneration(snapshots, prices, volumes, funding);

        int active = config.PopulationSize - finalReport.InactiveCount;
        float activeFraction = (float)active / config.PopulationSize;

        Assert.True(activeFraction >= 0.30f,
            $"After {config.Generations} gens at least 30% of population should be active. " +
            $"Got {activeFraction:P0} active ({active}/{config.PopulationSize})");
    }

    /// <summary>
    /// Build a 500-bar synthetic candle series with a mild upward trend + random walk
    /// noise, then convert via HistoricalDataStore.CandlesToSignals for evaluation.
    /// </summary>
    private static (SignalSnapshot[] snapshots, float[] prices, float[] volumes, float[] funding)
        BuildSyntheticData(int n, int seed)
    {
        var rng = new Random(seed);
        var candles = new TechnicalIndicators.Candle[n];
        var startTime = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        float price = 50_000f;
        for (int i = 0; i < n; i++)
        {
            float drift = 5f;  // mild upward drift
            float noise = (float)(rng.NextDouble() - 0.5) * 200f;  // ±$100 random walk
            float open = price;
            price = price + drift + noise;
            float high = MathF.Max(open, price) + (float)rng.NextDouble() * 50f;
            float low = MathF.Min(open, price) - (float)rng.NextDouble() * 50f;
            float volume = 100f + (float)rng.NextDouble() * 50f;

            candles[i] = new TechnicalIndicators.Candle(
                Open: open, High: high, Low: low, Close: price, Volume: volume,
                Time: startTime.AddMinutes(15 * i));
        }

        var (snapshots, prices, volumes, funding) =
            HistoricalDataStore.CandlesToSignals(candles, enrichment: null, barsPerHour: 4);

        return (snapshots, prices, volumes, funding);
    }
}
