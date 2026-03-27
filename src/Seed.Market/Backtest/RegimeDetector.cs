using Seed.Market.Signals;

namespace Seed.Market.Backtest;

public enum MarketRegime
{
    Bull,
    Bear,
    Sideways,
    HighVolatility
}

/// <summary>
/// Classifies a data window by market regime using price action heuristics.
/// </summary>
public static class RegimeDetector
{
    public static MarketRegime Classify(float[] prices, int startIdx, int length)
    {
        if (length < 20) return MarketRegime.Sideways;

        int end = Math.Min(startIdx + length, prices.Length);
        int n = end - startIdx;
        if (n < 20) return MarketRegime.Sideways;

        float first = prices[startIdx];
        float last = prices[end - 1];
        float totalReturn = first > 0 ? (last - first) / first : 0f;

        float sumAbsReturn = 0f;
        float maxPrice = float.MinValue, minPrice = float.MaxValue;
        for (int i = startIdx + 1; i < end; i++)
        {
            if (prices[i - 1] > 0)
                sumAbsReturn += MathF.Abs((prices[i] - prices[i - 1]) / prices[i - 1]);
            if (prices[i] > maxPrice) maxPrice = prices[i];
            if (prices[i] < minPrice) minPrice = prices[i];
        }

        float avgAbsReturn = sumAbsReturn / (n - 1);
        float range = first > 0 ? (maxPrice - minPrice) / first : 0f;

        if (avgAbsReturn > 0.015f || range > 0.30f)
            return MarketRegime.HighVolatility;

        if (totalReturn > 0.05f)
            return MarketRegime.Bull;

        if (totalReturn < -0.05f)
            return MarketRegime.Bear;

        return MarketRegime.Sideways;
    }

    /// <summary>
    /// Select K diverse windows from the training data, preferring regime diversity.
    /// </summary>
    public static (int Offset, int Length, MarketRegime Regime)[] SelectDiverseWindows(
        float[] prices, int trainLen, int windowSize, int k, int generation, ulong seed)
    {
        if (k <= 0 || trainLen < windowSize)
            return [];

        int perWindow = windowSize / k;
        if (perWindow < 20) perWindow = Math.Min(windowSize, trainLen);

        int maxOffset = Math.Max(1, trainLen - perWindow);
        var candidates = new List<(int Offset, MarketRegime Regime)>();

        var rng = new Random((int)(seed ^ (uint)generation));
        int attempts = Math.Max(k * 3, 10);

        for (int a = 0; a < attempts; a++)
        {
            int offset = rng.Next(0, maxOffset);
            var regime = Classify(prices, offset, perWindow);
            candidates.Add((offset, regime));
        }

        var selected = new List<(int Offset, int Length, MarketRegime Regime)>();
        var usedRegimes = new HashSet<MarketRegime>();

        // First pass: pick one from each unique regime
        foreach (var regime in Enum.GetValues<MarketRegime>())
        {
            if (selected.Count >= k) break;
            var match = candidates.FirstOrDefault(c => c.Regime == regime && !IsOverlapping(c.Offset, perWindow, selected));
            if (match != default)
            {
                selected.Add((match.Offset, perWindow, match.Regime));
                usedRegimes.Add(match.Regime);
            }
        }

        // Second pass: fill remaining from candidates (prefer non-overlapping)
        foreach (var c in candidates)
        {
            if (selected.Count >= k) break;
            if (!IsOverlapping(c.Offset, perWindow, selected))
                selected.Add((c.Offset, perWindow, c.Regime));
        }

        // Fallback: deterministic offsets if we couldn't find enough
        while (selected.Count < k)
        {
            int offset = (selected.Count * maxOffset / k + generation * 17) % maxOffset;
            var regime = Classify(prices, offset, perWindow);
            selected.Add((offset, perWindow, regime));
        }

        return selected.Take(k).ToArray();
    }

    private static bool IsOverlapping(int offset, int length,
        List<(int Offset, int Length, MarketRegime Regime)> existing)
    {
        foreach (var (o, l, _) in existing)
        {
            if (offset < o + l && o < offset + length)
                return true;
        }
        return false;
    }
}
