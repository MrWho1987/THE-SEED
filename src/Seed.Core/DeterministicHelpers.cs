namespace Seed.Core;

/// <summary>
/// Helpers for deterministic operations on collections.
/// Never rely on Dictionary iteration order - always use these helpers.
/// </summary>
public static class DeterministicHelpers
{
    /// <summary>
    /// Get dictionary keys in sorted order.
    /// </summary>
    public static IEnumerable<TKey> SortedKeys<TKey, TValue>(this Dictionary<TKey, TValue> dict)
        where TKey : notnull, IComparable<TKey>
    {
        var keys = dict.Keys.ToList();
        keys.Sort();
        return keys;
    }

    /// <summary>
    /// Get dictionary entries in key-sorted order.
    /// </summary>
    public static IEnumerable<KeyValuePair<TKey, TValue>> SortedEntries<TKey, TValue>(
        this Dictionary<TKey, TValue> dict)
        where TKey : notnull, IComparable<TKey>
    {
        var keys = dict.Keys.ToList();
        keys.Sort();
        foreach (var key in keys)
            yield return new KeyValuePair<TKey, TValue>(key, dict[key]);
    }

    /// <summary>
    /// Stable order by Guid (canonical byte comparison).
    /// </summary>
    public static IOrderedEnumerable<T> StableOrderByGuid<T>(this IEnumerable<T> source, Func<T, Guid> selector)
    {
        return source.OrderBy(x => selector(x));
    }

    /// <summary>
    /// Stable order by multiple keys.
    /// </summary>
    public static IOrderedEnumerable<T> StableOrderBy<T, TKey1, TKey2>(
        this IEnumerable<T> source,
        Func<T, TKey1> keySelector1,
        Func<T, TKey2> keySelector2)
        where TKey1 : IComparable<TKey1>
        where TKey2 : IComparable<TKey2>
    {
        return source.OrderBy(keySelector1).ThenBy(keySelector2);
    }

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

    /// <summary>
    /// Compute fitness score from aggregate metrics.
    /// F = mean - lambdaVar * sqrt(variance) + lambdaWorst * worst
    /// </summary>
    public static float ComputeFitnessScore(
        float mean,
        float variance,
        float worst,
        float lambdaVar = 0.10f,
        float lambdaWorst = 0.20f)
    {
        return mean - lambdaVar * MathF.Sqrt(variance) + lambdaWorst * worst;
    }

    /// <summary>
    /// Compute fitness score for a single episode from its component metrics.
    /// </summary>
    public static float ComputeEpisodeFitness(
        int survivalTicks,
        float netEnergyDelta,
        int foodCollected,
        float energySpent,
        float instabilityPenalty,
        float distanceTraveled,
        int maxTicks)
    {
        float survivalScore = (float)survivalTicks / maxTicks;
        float stabilityBonus = 1f - instabilityPenalty;

        float result = survivalScore * 60f +
                      netEnergyDelta * 50f +
                      stabilityBonus * 10f;
        return float.IsNaN(result) || float.IsInfinity(result) ? 0f : result;
    }

    /// <summary>
    /// Aggregate episode metrics into a FitnessAggregate.
    /// </summary>
    public static FitnessAggregate AggregateFitness(
        ReadOnlySpan<EpisodeMetrics> episodes,
        float lambdaVar = 0.10f,
        float lambdaWorst = 0.20f)
    {
        if (episodes.Length == 0)
            return new FitnessAggregate(0, 0, 0, 0);

        float sum = 0;
        float sumSq = 0;
        float worst = float.MaxValue;

        foreach (var e in episodes)
        {
            sum += e.Fitness;
            sumSq += e.Fitness * e.Fitness;
            if (e.Fitness < worst)
                worst = e.Fitness;
        }

        float mean = sum / episodes.Length;
        float variance = (sumSq / episodes.Length) - (mean * mean);
        if (variance < 0) variance = 0; // numerical safety

        float score = ComputeFitnessScore(mean, variance, worst, lambdaVar, lambdaWorst);

        return new FitnessAggregate(mean, variance, worst, score);
    }
}


