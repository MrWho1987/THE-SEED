namespace Seed.Core;

/// <summary>
/// Helpers for deterministic operations on collections.
/// Never rely on Dictionary iteration order - always use these helpers.
/// </summary>
public static class DeterministicHelpers
{
    /// <summary>
    /// Deterministic shuffle using Fisher-Yates with provided RNG.
    /// </summary>
    public static void DeterministicShuffle<T>(this T[] array, ref Rng64 rng)
    {
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    /// <summary>
    /// Deterministic shuffle using Fisher-Yates with provided RNG.
    /// </summary>
    public static void DeterministicShuffle<T>(this List<T> list, ref Rng64 rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>
    /// Sample without replacement using Fisher-Yates partial shuffle.
    /// </summary>
    public static T[] DeterministicSample<T>(this T[] source, int count, ref Rng64 rng)
    {
        if (count >= source.Length)
            return source.ToArray();

        var copy = source.ToArray();
        for (int i = 0; i < count; i++)
        {
            int j = rng.NextInt(copy.Length - i) + i;
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy[..count];
    }

    /// <summary>
    /// Clamp a float to a range.
    /// </summary>
    public static float Clamp(float value, float min, float max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    /// <summary>
    /// Clamp an int to a range.
    /// </summary>
    public static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

}


