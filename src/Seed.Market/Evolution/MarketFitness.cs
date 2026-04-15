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
    public const float DefaultInactivityPenalty = -0.1f;
    public const int DefaultMinTradesForActive = 3;

    private static float GetAnnualizationFactor(int barsPerHour) =>
        MathF.Sqrt(8760f * Math.Max(1, barsPerHour));

    public static float Compute(PortfolioState portfolio, decimal finalPrice, float shrinkageK = 10f)
    {
        return ComputeDetailed(portfolio, finalPrice, shrinkageK).Fitness;
    }

    public static FitnessBreakdown ComputeDetailed(
        PortfolioState portfolio, decimal finalPrice, float shrinkageK = 10f,
        float wSharpe = 0.22f, float wSortino = 0.13f, float wReturn = 0.20f,
        float wDrawdownDuration = 0.13f, float wCVaR = 0.17f,
        float wCalmar = 0.05f, float wInfoRatio = 0.05f,
        float wFeeDrag = 0.03f, float wDiversification = 0.02f,
        float inactivityPenalty = -0.1f, int minTradesForActive = 3,
        float activityBonusScale = 0f,
        float ratioClampMax = 10f, float returnFloor = -0.50f,
        int barsPerHour = 1, float hodlReturn = 0f)
    {
        decimal equity = portfolio.Equity(finalPrice);
        decimal pnl = equity - portfolio.InitialBalance;
        float returnPct = portfolio.InitialBalance > 0
            ? (float)(pnl / portfolio.InitialBalance)
            : 0f;

        int tradeCount = portfolio.TotalTrades;

        if (tradeCount == 0)
        {
            return new FitnessBreakdown(
                Fitness: inactivityPenalty,
                ReturnPct: returnPct,
                MaxDrawdown: (float)portfolio.MaxDrawdown,
                TotalTrades: 0,
                WinRate: 0f,
                NetPnl: (float)pnl,
                IsActive: false,
                RawSharpe: 0f,
                AdjustedSharpe: 0f,
                Sortino: 0f,
                AdjustedSortino: 0f,
                CVaR5: 0f,
                MaxDrawdownDuration: 0f,
                ShrinkageConfidence: 0f,
                Calmar: 0f,
                InfoRatio: 0f,
                FeeDrag: 0f,
                Diversification: 0f);
        }

        var curve = portfolio.EquityCurve;
        float annFactor = GetAnnualizationFactor(barsPerHour);
        float rawSharpe = ComputeSharpe(curve, annFactor);
        float sortino = ComputeSortino(curve, annFactor, ratioClampMax);
        float cvar5 = ComputeCVaR(curve, 0.05f);
        float maxDdDuration = ComputeMaxDrawdownDuration(curve);

        float confidence = 1f - shrinkageK / (shrinkageK + tradeCount);
        float clampScale = Math.Min(1f, (float)tradeCount / (minTradesForActive * 3f));
        float effectiveClamp = ratioClampMax * clampScale;
        float adjustedSharpe = Math.Clamp(rawSharpe * confidence, -effectiveClamp, effectiveClamp);

        float sortinoClean = float.IsNaN(sortino) || float.IsInfinity(sortino) ? 0f : sortino;
        float adjustedSortino = Math.Clamp(sortinoClean * confidence, -effectiveClamp, effectiveClamp);
        float cvarPenalty = cvar5 < 0f ? -cvar5 : 0f;

        float logReturn = MathF.Log(1f + MathF.Abs(returnPct)) * MathF.Sign(returnPct);

        // New fitness terms (B1-B4)
        float feeDrag = ComputeFeeDrag(portfolio);
        float calmar = ComputeCalmar(logReturn, (float)portfolio.MaxDrawdown);
        float strategyStd = ComputeReturnsStd(curve);
        float infoRatio = ComputeInfoRatio(returnPct, hodlReturn, strategyStd);
        float diversification = ComputeDiversification(portfolio);

        // Apply shrinkage confidence to the ratio-based terms so a 2-trade lucky run
        // doesn't dominate the blend. FeeDrag and Diversification use raw values.
        float adjustedCalmar = Math.Clamp(calmar * confidence, -effectiveClamp, effectiveClamp);
        float adjustedInfoRatio = Math.Clamp(infoRatio * confidence, -effectiveClamp, effectiveClamp);

        float fullFitness = adjustedSharpe * wSharpe
                      + adjustedSortino * wSortino
                      + logReturn * wReturn
                      - maxDdDuration * wDrawdownDuration
                      - cvarPenalty * wCVaR
                      + adjustedCalmar * wCalmar
                      + adjustedInfoRatio * wInfoRatio
                      - feeDrag * wFeeDrag
                      + diversification * wDiversification;

        if (float.IsNaN(fullFitness) || float.IsInfinity(fullFitness))
            fullFitness = inactivityPenalty;

        float fitness;
        bool isActive;
        if (tradeCount >= minTradesForActive)
        {
            fitness = fullFitness;
            isActive = true;
        }
        else
        {
            float alpha = (float)tradeCount / minTradesForActive;
            fitness = alpha * fullFitness + (1f - alpha) * inactivityPenalty;
            isActive = false;
        }

        if (tradeCount > 0 && activityBonusScale > 0f)
        {
            float rawBonus = MathF.Log(1f + tradeCount) * activityBonusScale;
            float maxBonus = MathF.Log(1f + minTradesForActive * 3f) * activityBonusScale;
            fitness += Math.Min(rawBonus, maxBonus);
        }

        if (returnPct <= returnFloor)
            fitness = Math.Min(fitness, inactivityPenalty);

        return new FitnessBreakdown(
            Fitness: fitness,
            ReturnPct: returnPct,
            MaxDrawdown: (float)portfolio.MaxDrawdown,
            TotalTrades: tradeCount,
            WinRate: portfolio.WinRate,
            NetPnl: (float)pnl,
            IsActive: isActive,
            RawSharpe: rawSharpe,
            AdjustedSharpe: adjustedSharpe,
            Sortino: sortinoClean,
            AdjustedSortino: adjustedSortino,
            CVaR5: cvar5,
            MaxDrawdownDuration: maxDdDuration,
            ShrinkageConfidence: confidence,
            Calmar: adjustedCalmar,
            InfoRatio: adjustedInfoRatio,
            FeeDrag: feeDrag,
            Diversification: diversification);
    }

    /// <summary>
    /// Sum of all trade fees as fraction of initial balance. Penalizes strategies that
    /// churn through capital without generating enough edge to cover costs.
    /// </summary>
    public static float ComputeFeeDrag(PortfolioState portfolio)
    {
        if (portfolio.InitialBalance <= 0m) return 0f;
        decimal totalFees = 0m;
        foreach (var t in portfolio.TradeHistory) totalFees += t.Fee;
        return (float)(totalFees / portfolio.InitialBalance);
    }

    /// <summary>
    /// Calmar ratio approximation: log-return divided by max drawdown. Guards divide-by-zero
    /// and clamps to [-maxCap, maxCap] for stability. Rewards strategies with high return
    /// relative to peak-to-trough drawdown.
    /// </summary>
    public static float ComputeCalmar(float logReturn, float maxDrawdown, float maxCap = 20f)
    {
        if (maxDrawdown <= 0.001f) maxDrawdown = 0.001f;  // avoid divide-by-zero
        return Math.Clamp(logReturn / maxDrawdown, -maxCap, maxCap);
    }

    /// <summary>
    /// Information ratio vs HODL baseline: (strategy return - hodl return) / strategy std.
    /// Rewards strategies that beat buy-and-hold after accounting for their own volatility.
    /// </summary>
    public static float ComputeInfoRatio(float strategyReturn, float hodlReturn, float strategyStd, float maxCap = 10f)
    {
        if (strategyStd <= 0.0001f) return 0f;
        return Math.Clamp((strategyReturn - hodlReturn) / strategyStd, -maxCap, maxCap);
    }

    /// <summary>
    /// Standard deviation of per-tick returns from an equity curve. Used as denominator
    /// for the Information Ratio.
    /// </summary>
    public static float ComputeReturnsStd(List<float> equityCurve)
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
        return variance > 0f ? MathF.Sqrt(variance) : 0f;
    }

    /// <summary>
    /// Normalized diversification bonus: log(1 + maxConcurrentSeen) / log(1 + maxConcurrent).
    /// Returns value in [0, 1]. Rewards strategies that utilize multiple concurrent positions,
    /// not just single-position traders.
    /// </summary>
    public static float ComputeDiversification(PortfolioState portfolio, int maxConcurrent = 3)
    {
        if (portfolio.MaxConcurrentSeen <= 1 || maxConcurrent <= 1) return 0f;
        int observed = Math.Min(portfolio.MaxConcurrentSeen, maxConcurrent);
        return MathF.Log(1f + observed) / MathF.Log(1f + maxConcurrent);
    }

    public static float ComputeSharpe(List<float> equityCurve, float annualizationFactor = 93.54f)
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
        return mean / std * annualizationFactor;
    }

    public static float ComputeSortino(List<float> equityCurve, float annualizationFactor = 93.54f, float maxCap = 20f)
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
        if (negCount == 0) return mean > 0 ? maxCap : 0f;

        float downsideDeviation = MathF.Sqrt(sumNegSq / n);
        if (downsideDeviation <= 0f) return 0f;

        return mean / downsideDeviation * annualizationFactor;
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
    float AdjustedSortino,
    float CVaR5,
    float MaxDrawdownDuration,
    float ShrinkageConfidence,
    float Calmar = 0f,
    float InfoRatio = 0f,
    float FeeDrag = 0f,
    float Diversification = 0f
);
