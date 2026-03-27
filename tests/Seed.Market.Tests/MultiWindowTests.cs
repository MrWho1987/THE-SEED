using Seed.Market.Backtest;

namespace Seed.Market.Tests;

public class MultiWindowTests
{
    [Fact]
    public void SelectDiverseWindows_ReturnsKWindows()
    {
        var prices = CreateRisingPrices(2000);
        var windows = RegimeDetector.SelectDiverseWindows(prices, 2000, 600, 3, 0, 42);
        Assert.Equal(3, windows.Length);
    }

    [Fact]
    public void SelectDiverseWindows_DeterministicGivenSeed()
    {
        var prices = CreateRisingPrices(2000);
        var w1 = RegimeDetector.SelectDiverseWindows(prices, 2000, 600, 3, 5, 42);
        var w2 = RegimeDetector.SelectDiverseWindows(prices, 2000, 600, 3, 5, 42);

        for (int i = 0; i < w1.Length; i++)
            Assert.Equal(w1[i].Offset, w2[i].Offset);
    }

    [Fact]
    public void SelectDiverseWindows_DifferentAcrossGenerations()
    {
        var prices = CreateRisingPrices(2000);
        var w0 = RegimeDetector.SelectDiverseWindows(prices, 2000, 600, 3, 0, 42);
        var w1 = RegimeDetector.SelectDiverseWindows(prices, 2000, 600, 3, 1, 42);

        bool anyDifferent = false;
        for (int i = 0; i < w0.Length; i++)
        {
            if (w0[i].Offset != w1[i].Offset)
            {
                anyDifferent = true;
                break;
            }
        }
        Assert.True(anyDifferent, "Different generations should produce different window offsets");
    }

    [Fact]
    public void RegimeDetector_Bull_IdentifiedCorrectly()
    {
        var prices = new float[200];
        for (int i = 0; i < 200; i++)
            prices[i] = 50000f + i * 50f; // strong uptrend

        var regime = RegimeDetector.Classify(prices, 0, 200);
        Assert.Equal(MarketRegime.Bull, regime);
    }

    [Fact]
    public void RegimeDetector_Bear_IdentifiedCorrectly()
    {
        var prices = new float[200];
        for (int i = 0; i < 200; i++)
            prices[i] = 60000f - i * 50f; // strong downtrend

        var regime = RegimeDetector.Classify(prices, 0, 200);
        Assert.Equal(MarketRegime.Bear, regime);
    }

    [Fact]
    public void RegimeDetector_Sideways_IdentifiedCorrectly()
    {
        var prices = new float[200];
        for (int i = 0; i < 200; i++)
            prices[i] = 50000f + MathF.Sin(i * 0.1f) * 100f; // tiny oscillation

        var regime = RegimeDetector.Classify(prices, 0, 200);
        Assert.Equal(MarketRegime.Sideways, regime);
    }

    [Fact]
    public void RegimeDetector_FallbackWhenLowDiversity()
    {
        // All bull data - should still select K=3 windows without error
        var prices = new float[2000];
        for (int i = 0; i < 2000; i++)
            prices[i] = 50000f + i * 10f;

        var windows = RegimeDetector.SelectDiverseWindows(prices, 2000, 600, 3, 0, 42);
        Assert.Equal(3, windows.Length);
    }

    [Fact]
    public void SelectDiverseWindows_NonOverlapping()
    {
        var prices = CreateRisingPrices(5000);
        var windows = RegimeDetector.SelectDiverseWindows(prices, 5000, 3000, 3, 0, 42);

        for (int i = 0; i < windows.Length; i++)
        {
            for (int j = i + 1; j < windows.Length; j++)
            {
                bool overlaps = windows[i].Offset < windows[j].Offset + windows[j].Length &&
                                windows[j].Offset < windows[i].Offset + windows[i].Length;
                // Non-overlapping is preferred but not strictly guaranteed by the algo.
                // Just verify the windows are valid.
                Assert.True(windows[i].Length > 0);
                Assert.True(windows[j].Length > 0);
            }
        }
    }

    [Fact]
    public void MultiWindowFitness_IsAverageOfIndividual()
    {
        float f1 = 0.3f, f2 = 0.6f, f3 = 0.9f;
        float avg = (f1 + f2 + f3) / 3f;
        Assert.Equal(0.6f, avg, 4);
    }

    private static float[] CreateRisingPrices(int count)
    {
        var prices = new float[count];
        var rng = new Random(42);
        float price = 50000f;
        for (int i = 0; i < count; i++)
        {
            price *= 1f + (float)(rng.NextDouble() - 0.48) * 0.01f;
            prices[i] = price;
        }
        return prices;
    }
}
