namespace Seed.Market.Evaluation;

/// <summary>
/// Bootstrap resampling and statistical significance testing for trade results.
/// </summary>
public static class StatisticalTests
{
    public readonly record struct ConfidenceInterval(float P5, float Median, float P95);

    /// <summary>
    /// Bootstrap resample trade P&Ls to estimate confidence interval on total return.
    /// </summary>
    public static ConfidenceInterval BootstrapReturn(
        IReadOnlyList<float> tradePnls, int resamples = 10_000, int? seed = null)
    {
        if (tradePnls.Count == 0)
            return new ConfidenceInterval(0f, 0f, 0f);

        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        var totalReturns = new float[resamples];

        for (int r = 0; r < resamples; r++)
        {
            float total = 0f;
            for (int t = 0; t < tradePnls.Count; t++)
                total += tradePnls[rng.Next(tradePnls.Count)];
            totalReturns[r] = total;
        }

        Array.Sort(totalReturns);

        int p5Idx = (int)(resamples * 0.05);
        int medIdx = resamples / 2;
        int p95Idx = (int)(resamples * 0.95);

        return new ConfidenceInterval(
            totalReturns[p5Idx],
            totalReturns[medIdx],
            totalReturns[p95Idx]);
    }

    /// <summary>
    /// Paired t-test for two sets of returns measured on the same windows.
    /// Returns (tStatistic, pValue, cohensD).
    /// </summary>
    public static (float TStat, float PValue, float CohensD) PairedTTest(
        float[] returnsA, float[] returnsB)
    {
        if (returnsA.Length != returnsB.Length || returnsA.Length < 2)
            return (0f, 1f, 0f);

        int n = returnsA.Length;
        var diffs = new float[n];
        float sumD = 0f;

        for (int i = 0; i < n; i++)
        {
            diffs[i] = returnsA[i] - returnsB[i];
            sumD += diffs[i];
        }

        float meanD = sumD / n;
        float ssD = 0f;
        for (int i = 0; i < n; i++)
            ssD += (diffs[i] - meanD) * (diffs[i] - meanD);

        float stdD = MathF.Sqrt(ssD / (n - 1));
        if (stdD == 0f) return (float.PositiveInfinity, 0f, float.PositiveInfinity);

        float tStat = meanD / (stdD / MathF.Sqrt(n));
        float pValue = ApproximateTwoTailedP(tStat, n - 1);
        float cohensD = meanD / stdD;

        return (tStat, pValue, cohensD);
    }

    private static float ApproximateTwoTailedP(float t, int df)
    {
        float absT = MathF.Abs(t);
        float x = df / (df + absT * absT);
        float p = 0.5f * RegularizedIncompleteBeta(df / 2.0f, 0.5f, x);
        return MathF.Min(1f, 2f * p);
    }

    private static float RegularizedIncompleteBeta(float a, float b, float x)
    {
        if (x <= 0) return 0f;
        if (x >= 1) return 1f;
        return x < (a + 1f) / (a + b + 2f)
            ? BetaCF(a, b, x) * MathF.Exp(a * MathF.Log(x) + b * MathF.Log(1f - x) - LogBeta(a, b)) / a
            : 1f - BetaCF(b, a, 1f - x) * MathF.Exp(b * MathF.Log(1f - x) + a * MathF.Log(x) - LogBeta(a, b)) / b;
    }

    private static float BetaCF(float a, float b, float x)
    {
        const int maxIter = 200;
        float qab = a + b, qap = a + 1f, qam = a - 1f;
        float c = 1f, d = 1f - qab * x / qap;
        if (MathF.Abs(d) < 1e-30f) d = 1e-30f;
        d = 1f / d;
        float h = d;

        for (int m = 1; m <= maxIter; m++)
        {
            int m2 = 2 * m;
            float aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1f + aa * d; if (MathF.Abs(d) < 1e-30f) d = 1e-30f;
            c = 1f + aa / c; if (MathF.Abs(c) < 1e-30f) c = 1e-30f;
            d = 1f / d; h *= d * c;

            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1f + aa * d; if (MathF.Abs(d) < 1e-30f) d = 1e-30f;
            c = 1f + aa / c; if (MathF.Abs(c) < 1e-30f) c = 1e-30f;
            d = 1f / d;
            float del = d * c;
            h *= del;
            if (MathF.Abs(del - 1f) < 3e-7f) break;
        }
        return h;
    }

    private static float LogBeta(float a, float b) =>
        LogGamma(a) + LogGamma(b) - LogGamma(a + b);

    private static float LogGamma(float x)
    {
        double[] c = { 76.18009173, -86.50532033, 24.01409822, -1.231739516, 0.00120858003, -0.00000536382 };
        double y = x, tmp = x + 5.5;
        tmp -= (x + 0.5) * Math.Log(tmp);
        double ser = 1.000000000190015;
        for (int j = 0; j < 6; j++) ser += c[j] / ++y;
        return (float)(-tmp + Math.Log(2.5066282746310005 * ser / x));
    }
}
