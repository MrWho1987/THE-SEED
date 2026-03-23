using Seed.Core;

namespace Seed.Tests;

public class RngTests
{
    [Fact]
    public void SplitMix64_ProducesExpectedSequence()
    {
        // Known answer test for SplitMix64
        ulong x = 0UL;
        ulong v1 = Rng64.SplitMix64(ref x);
        ulong v2 = Rng64.SplitMix64(ref x);
        ulong v3 = Rng64.SplitMix64(ref x);

        // These are the expected values from the reference implementation
        Assert.Equal(0xE220A8397B1DCDAFUL, v1);
        Assert.Equal(0x6E789E6AA1B965F4UL, v2);
        Assert.Equal(0x06C45D188009454FUL, v3);
    }

    [Fact]
    public void Rng64_SameSeedProducesSameSequence()
    {
        var rng1 = new Rng64(12345);
        var rng2 = new Rng64(12345);

        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(rng1.NextU64(), rng2.NextU64());
        }
    }

    [Fact]
    public void Rng64_DifferentSeedsProduceDifferentSequences()
    {
        var rng1 = new Rng64(12345);
        var rng2 = new Rng64(54321);

        // Very unlikely to match
        Assert.NotEqual(rng1.NextU64(), rng2.NextU64());
    }

    [Fact]
    public void Rng64_NextFloat01_InRange()
    {
        var rng = new Rng64(42);

        for (int i = 0; i < 1000; i++)
        {
            float f = rng.NextFloat01();
            Assert.True(f >= 0f && f < 1f, $"NextFloat01 out of range: {f}");
        }
    }

    [Fact]
    public void Rng64_NextInt_InRange()
    {
        var rng = new Rng64(42);

        for (int max = 1; max <= 100; max++)
        {
            for (int i = 0; i < 100; i++)
            {
                int v = rng.NextInt(max);
                Assert.True(v >= 0 && v < max, $"NextInt({max}) out of range: {v}");
            }
        }
    }

    [Fact]
    public void Rng64_NextGaussian_HasReasonableDistribution()
    {
        var rng = new Rng64(42);

        double sum = 0;
        double sumSq = 0;
        int n = 10000;

        for (int i = 0; i < n; i++)
        {
            float g = rng.NextGaussian(0f, 1f);
            sum += g;
            sumSq += g * g;
        }

        double mean = sum / n;
        double variance = sumSq / n - mean * mean;

        // Should be close to mean=0, variance=1
        Assert.True(Math.Abs(mean) < 0.05, $"Gaussian mean too far from 0: {mean}");
        Assert.True(Math.Abs(variance - 1.0) < 0.1, $"Gaussian variance too far from 1: {variance}");
    }

    [Fact]
    public void DeriveSeed_SamInputsProduceSameOutput()
    {
        ulong seed1 = SeedDerivation.DeriveSeed(42, SeedDerivation.DOMAIN_WORLD, 1, 2, 3);
        ulong seed2 = SeedDerivation.DeriveSeed(42, SeedDerivation.DOMAIN_WORLD, 1, 2, 3);

        Assert.Equal(seed1, seed2);
    }

    [Fact]
    public void DeriveSeed_DifferentDomainsProduceDifferentOutputs()
    {
        ulong seed1 = SeedDerivation.DeriveSeed(42, SeedDerivation.DOMAIN_WORLD, 1, 2, 3);
        ulong seed2 = SeedDerivation.DeriveSeed(42, SeedDerivation.DOMAIN_AGENT, 1, 2, 3);

        Assert.NotEqual(seed1, seed2);
    }

    [Fact]
    public void DeriveSeed_DifferentParamsProduceDifferentOutputs()
    {
        ulong seed1 = SeedDerivation.DeriveSeed(42, SeedDerivation.DOMAIN_WORLD, 1, 2, 3);
        ulong seed2 = SeedDerivation.DeriveSeed(42, SeedDerivation.DOMAIN_WORLD, 1, 2, 4);

        Assert.NotEqual(seed1, seed2);
    }
}


