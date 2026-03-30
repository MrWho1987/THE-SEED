using Seed.Market.Trading;

namespace Seed.Market.Evolution;

/// <summary>
/// Computes fitness for a market agent using risk-adjusted metrics:
/// Bayesian-shrinkage Sharpe, Sortino, CVaR, drawdown duration.
/// Style-agnostic: does not penalize concentration. Shrinkage lets
/// evolution discover whether sniper or steady strategies work best.
/// </summary>
public static class MarketFitness
{
    public const float InactivityPenalty = -0.1f;
    public const int MinTradesForActive = 3;
    private const float AnnualizationFactor = 93.54f; // sqrt(8760)

    public static float Compute(PortfolioState portfolio, decimal finalPrice, float shrinkageK = 10f)
    {
        return ComputeDetailed(portfolio, finalPrice, shrinkageK).Fitness;
    }

    public static FitnessBreakdown ComputeDetailed(
        PortfolioState portfolio, decimal finalPrice, float shrinkageK = 10f,
        float wSharpe = 0.45f, float wSortino = 0.15f, float wReturn = 0.20f,
        float wDrawdownDuration = 0.10f, float wCVaR = 0.10f)
    {
        decimal equity = portfolio.Equity(finalPrice);
        decimal pnl = equity - portfolio.InitialBalance;
        float returnPct = portfolio.InitialBalance > 0
            ? (float)(pnl / portfolio.InitialBalance)
            : 0f;

        int tradeCount = portfolio.TotalTrades;
        bool active = tradeCount >= MinTradesForActive;

        if (!active)
        {
            return new FitnessBreakdown(
                Fitness: InactivityPenalty,
                ReturnPct: returnPct,
                MaxDrawdown: (float)portfolio.MaxDrawdown,
                TotalTrades: tradeCount,
                WinRate: portfolio.WinRate,
                NetPnl: (float)pnl,
                IsActive: false,
                RawSharpe: 0f,
                AdjustedSharpe: 0f,
                Sortino: 0f,
                CVaR5: 0f,
                MaxDrawdownDuration: 0f,
                ShrinkageConfidence: 0f);
        }

        var curve = portfolio.EquityCurve;
        float rawSharpe = ComputeSharpe(curve);
        float sortino = ComputeSortino(curve);
        float cvar5 = ComputeCVaR(curve, 0.05f);
        float maxDdDuration = ComputeMaxDrawdownDuration(curve);

        float confidence = 1f - shrinkageK / (shrinkageK + tradeCount);
        float adjustedSharpe = rawSharpe * confidence;

        float sortinoComponent = float.IsNaN(sortino) || float.IsInfinity(sortino) ? 0f : sortino;
        float cvarPenalty = cvar5 < 0f ? -cvar5 : 0f;

        float logReturn = MathF.Log(1f + MathF.Abs(returnPct)) * MathF.Sign(returnPct);
        float fitness = adjustedSharpe * wSharpe
                      + sortinoComponent * wSortino
                      + logReturn * wReturn
                      - maxDdDuration * wDrawdownDuration
                      - cvarPenalty * wCVaR;

        if (float.IsNaN(fitness) || float.IsInfinity(fitness))
            fitness = InactivityPenalty;

        return new FitnessBreakdown(
            Fitness: fitness,
            ReturnPct: returnPct,
            MaxDrawdown: (float)portfolio.MaxDrawdown,
            TotalTrades: tradeCount,
            WinRate: portfolio.WinRate,
            NetPnl: (float)pnl,
            IsActive: true,
            RawSharpe: rawSharpe,
            AdjustedSharpe: adjustedSharpe,
            Sortino: sortinoComponent,
            CVaR5: cvar5,
            MaxDrawdownDuration: maxDdDuration,
            ShrinkageConfidence: confidence);
    }

    public static float ComputeSharpe(List<float> equityCurve)
    {
        if (equityCurve.Count < 2) return 0f;

        int n = equityCurve.Count - 1;
        float sumR = 0f, sumR2 = 0f;

        for (int i = 1; i <= n; i++)
        {
            float prev = equityCurve[i - 1];
            if (prev == 0f) continue;
            float r = (equityCurve[i] - prev) / MathF.Abs(prev);
            sumR += r;
            sumR2 += r * r;
        }

        float mean = sumR / n;
        float variance = sumR2 / n - mean * mean;
        if (variance <= 0f) return 0f;

        float std = MathF.Sqrt(variance);
        return mean / std * AnnualizationFactor;
    }

    public static float ComputeSortino(List<float> equityCurve)
    {
        if (equityCurve.Count < 2) return 0f;

        int n = equityCurve.Count - 1;
        float sumR = 0f;
        float sumNegSq = 0f;
        int negCount = 0;

        for (int i = 1; i <= n; i++)
        {
            float prev = equityCurve[i - 1];
            if (prev == 0f) continue;
            float r = (equityCurve[i] - prev) / MathF.Abs(prev);
            sumR += r;
            if (r < 0f)
            {
                sumNegSq += r * r;
                negCount++;
            }
        }

        float mean = sumR / n;
        if (negCount == 0) return mean > 0 ? AnnualizationFactor : 0f;

        float downsideDeviation = MathF.Sqrt(sumNegSq / n);
        if (downsideDeviation <= 0f) return 0f;

        return mean / downsideDeviation * AnnualizationFactor;
    }

    public static float ComputeCVaR(List<float> equityCurve, float percentile)
    {
        if (equityCurve.Count < 2) return 0f;

        int n = equityCurve.Count - 1;
        var returns = new float[n];

        for (int i = 0; i < n; i++)
        {
            float prev = equityCurve[i];
            returns[i] = prev != 0f
                ? (equityCurve[i + 1] - prev) / MathF.Abs(prev)
                : 0f;
        }

        Array.Sort(returns);

        int tailCount = Math.Max(1, (int)(n * percentile));
        float sum = 0f;
        for (int i = 0; i < tailCount; i++)
            sum += returns[i];

        return sum / tailCount;
    }

    public static float ComputeMaxDrawdownDuration(List<float> equityCurve)
    {
        if (equityCurve.Count < 2) return 0f;

        float peak = equityCurve[0];
        int currentDuration = 0;
        int maxDuration = 0;

        for (int i = 1; i < equityCurve.Count; i++)
        {
            if (equityCurve[i] >= peak)
            {
                peak = equityCurve[i];
                currentDuration = 0;
            }
            else
            {
                currentDuration++;
                if (currentDuration > maxDuration)
                    maxDuration = currentDuration;
            }
        }

        return (float)maxDuration / equityCurve.Count;
    }
}

public readonly record struct FitnessBreakdown(
    float Fitness,
    float ReturnPct,
    float MaxDrawdown,
    int TotalTrades,
    float WinRate,
    float NetPnl,
    bool IsActive,
    float RawSharpe,
    float AdjustedSharpe,
    float Sortino,
    float CVaR5,
    float MaxDrawdownDuration,
    float ShrinkageConfidence
);
