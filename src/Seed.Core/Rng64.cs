using System.Runtime.CompilerServices;

namespace Seed.Core;

/// <summary>
/// Deterministic PRNG using xoshiro256** algorithm.
/// Produces identical sequences given the same seed across .NET runtimes on the same ISA.
/// </summary>
public struct Rng64
{
    private ulong _s0, _s1, _s2, _s3;

    /// <summary>
    /// Initialize the PRNG from a single 64-bit seed using SplitMix64.
    /// </summary>
    public Rng64(ulong seed)
    {
        ulong x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

    /// <summary>
    /// Generate the next 64-bit unsigned integer using xoshiro256**.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong NextU64()
    {
        ulong result = RotL(_s1 * 5UL, 7) * 9UL;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;

        _s2 ^= t;
        _s3 = RotL(_s3, 45);

        return result;
    }

    /// <summary>
    /// Generate a float in [0, 1) using top 24 bits for deterministic conversion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat01()
    {
        uint u = (uint)(NextU64() >> 40); // top 24 bits
        return u * (1.0f / 16777216.0f);  // 2^24
    }

    /// <summary>
    /// Generate a double in [0, 1) using top 53 bits for deterministic conversion.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double NextDouble01()
    {
        ulong u = NextU64() >> 11; // top 53 bits
        return u * (1.0 / 9007199254740992.0); // 2^53
    }

    /// <summary>
    /// Generate an unbiased integer in [0, exclusiveMax) using rejection sampling.
    /// </summary>
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0)
            throw new ArgumentOutOfRangeException(nameof(exclusiveMax), "Must be positive.");

        uint bound = (uint)exclusiveMax;
        uint threshold = (uint)(0x100000000UL % bound);
        
        while (true)
        {
            uint r = (uint)NextU64();
            if (r >= threshold)
                return (int)(r % bound);
        }
    }

    /// <summary>
    /// Generate a Gaussian random number using Box-Muller transform.
    /// </summary>
    public float NextGaussian(float mean = 0f, float stdDev = 1f)
    {
        // Box-Muller transform
        float u1 = NextFloat01();
        float u2 = NextFloat01();
        
        // Avoid log(0)
        while (u1 <= float.Epsilon)
            u1 = NextFloat01();

        float z0 = MathF.Sqrt(-2.0f * MathF.Log(u1)) * MathF.Cos(2.0f * MathF.PI * u2);
        return mean + z0 * stdDev;
    }

    /// <summary>
    /// Generate a uniform float in [min, max).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat(float min, float max)
    {
        return min + NextFloat01() * (max - min);
    }

    /// <summary>
    /// SplitMix64 for seed expansion. Used internally and for DeriveSeed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong SplitMix64(ref ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        ulong z = x;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}


