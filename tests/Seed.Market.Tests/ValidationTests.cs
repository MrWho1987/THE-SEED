using Seed.Core;
using Seed.Genetics;
using Seed.Market.Evolution;
using Seed.Market.Signals;

namespace Seed.Market.Tests;

public class ValidationTests
{
    [Fact]
    public void EvaluateSingle_ReturnsValidBreakdown()
    {
        var config = MarketConfig.Default with
        {
            InitialCapital = 10_000m,
            MaxBrainNodes = 50,
            MaxBrainEdges = 100
        };
        var evaluator = new MarketEvaluator(config);
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);

        var (snapshots, prices) = CreateSyntheticData(100);
        var result = evaluator.EvaluateSingle(genome, snapshots, prices, 0);

        Assert.False(float.IsNaN(result.Fitness.Fitness));
        Assert.False(float.IsInfinity(result.Fitness.Fitness));
        Assert.False(float.IsNaN(result.Fitness.RawSharpe));
    }

    [Fact]
    public void ValidationCheck_TriggersAtCorrectInterval()
    {
        int interval = 5;
        var triggered = new List<int>();

        for (int gen = 0; gen < 20; gen++)
        {
            bool isValGen = interval > 0 && gen > 0 && gen % interval == 0;
            if (isValGen) triggered.Add(gen);
        }

        Assert.Contains(5, triggered);
        Assert.Contains(10, triggered);
        Assert.Contains(15, triggered);
        Assert.DoesNotContain(0, triggered);
        Assert.DoesNotContain(1, triggered);
        Assert.DoesNotContain(4, triggered);
    }

    [Fact]
    public void OverfitDetection_FlagsWhenValDeclines()
    {
        var history = new List<(int Gen, float TrainFit, float ValFit)>
        {
            (5,  0.05f, 0.10f),  // initial: val improves, sets bestVal
            (10, 0.10f, 0.08f),  // decline 1
            (15, 0.15f, 0.06f),  // decline 2
            (20, 0.20f, 0.05f),  // decline 3
            (25, 0.25f, 0.04f),  // decline 4
            (30, 0.30f, 0.03f),  // decline 5
        };

        int patience = 5;
        int consecutiveDeclines = 0;
        float bestVal = float.MinValue;

        foreach (var (gen, trainFit, valFit) in history)
        {
            if (valFit > bestVal)
            {
                bestVal = valFit;
                consecutiveDeclines = 0;
            }
            else
            {
                consecutiveDeclines++;
            }
        }

        bool trainImproving = history[^1].TrainFit >= history[^patience].TrainFit;
        bool overfitDetected = consecutiveDeclines >= patience && trainImproving;

        Assert.True(overfitDetected, $"Should detect overfitting (declines={consecutiveDeclines}, patience={patience})");
    }

    [Fact]
    public void OverfitDetection_DoesNotFlagWhenBothImprove()
    {
        var history = new List<(int Gen, float TrainFit, float ValFit)>
        {
            (10, 0.10f, 0.08f),
            (20, 0.15f, 0.10f),
            (30, 0.20f, 0.12f),
            (40, 0.25f, 0.15f),
            (50, 0.30f, 0.18f),
        };

        int consecutiveDeclines = 0;
        float bestVal = float.MinValue;

        foreach (var (_, _, valFit) in history)
        {
            if (valFit > bestVal)
            {
                bestVal = valFit;
                consecutiveDeclines = 0;
            }
            else
            {
                consecutiveDeclines++;
            }
        }

        Assert.Equal(0, consecutiveDeclines);
    }

    [Fact]
    public void BestGenomeSelection_UsesValidationFitness()
    {
        float bestValFitness = float.MinValue;
        string? bestValGenomeId = null;

        var candidates = new[]
        {
            (Id: "A", TrainFit: 0.5f, ValFit: 0.3f),
            (Id: "B", TrainFit: 0.4f, ValFit: 0.4f),
        };

        foreach (var c in candidates)
        {
            if (c.ValFit > bestValFitness)
            {
                bestValFitness = c.ValFit;
                bestValGenomeId = c.Id;
            }
        }

        Assert.Equal("B", bestValGenomeId);
    }

    [Fact]
    public void WalkForward_AdvancesOnPass()
    {
        int walkForwardOffset = 0;
        int stallCount = 0;
        int rollingStepHours = 24;
        float minValFitness = -0.05f;
        int trainLen = 1000;
        int evalWindow = 500;
        int maxWfOffset = Math.Max(0, trainLen - evalWindow);

        float valFit = 0.1f;
        if (valFit >= minValFitness)
        {
            walkForwardOffset = Math.Min(walkForwardOffset + rollingStepHours, maxWfOffset);
            stallCount = 0;
        }

        Assert.Equal(24, walkForwardOffset);
        Assert.Equal(0, stallCount);
    }

    [Fact]
    public void WalkForward_StallsOnFail()
    {
        int walkForwardOffset = 100;
        int stallCount = 2;
        float minValFitness = -0.05f;

        float valFit = -0.2f;
        if (valFit < minValFitness)
            stallCount++;

        Assert.Equal(100, walkForwardOffset);
        Assert.Equal(3, stallCount);
    }

    [Fact]
    public void WalkForward_ForceAdvancesAfterMaxStall()
    {
        int walkForwardOffset = 100;
        int stallCount = 49;
        int maxStallGens = 50;
        int rollingStepHours = 24;
        int trainLen = 1000;
        int evalWindow = 500;
        int maxWfOffset = Math.Max(0, trainLen - evalWindow);

        float valFit = -0.3f;
        float minValFitness = -0.05f;

        if (valFit < minValFitness)
        {
            stallCount++;
            if (stallCount >= maxStallGens)
            {
                walkForwardOffset = Math.Min(walkForwardOffset + rollingStepHours, maxWfOffset);
                stallCount = 0;
            }
        }

        Assert.Equal(124, walkForwardOffset);
        Assert.Equal(0, stallCount);
    }

    [Fact]
    public void WalkForward_DataSliceBoundary()
    {
        int trainLen = 1000;
        int walkForwardOffset = 500;
        int evalWindow = 600;

        int remainingLen = Math.Max(50, trainLen - walkForwardOffset);
        int wfEvalWindow = Math.Min(evalWindow, remainingLen);

        Assert.Equal(500, remainingLen);
        Assert.Equal(500, wfEvalWindow);
        Assert.True(wfEvalWindow <= remainingLen, "Eval window must not exceed remaining data");
    }

    private static (SignalSnapshot[], float[]) CreateSyntheticData(int length)
    {
        var normalizer = new SignalNormalizer();
        var snapshots = new SignalSnapshot[length];
        var prices = new float[length];
        float price = 50000f;
        var rng = new Random(42);

        for (int i = 0; i < length; i++)
        {
            price *= 1f + (float)(rng.NextDouble() - 0.498) * 0.02f;
            prices[i] = price;
            var raw = new float[SignalIndex.Count];
            raw[SignalIndex.BtcPrice] = price;
            raw[SignalIndex.BtcReturn1h] = i > 0 ? (price - prices[i - 1]) / prices[i - 1] : 0f;
            raw[SignalIndex.BtcVolume1h] = 1000f;
            raw[SignalIndex.Rsi14] = 50f;
            snapshots[i] = normalizer.Normalize(raw, DateTimeOffset.UtcNow.AddHours(i), i);
        }
        return (snapshots, prices);
    }
}
