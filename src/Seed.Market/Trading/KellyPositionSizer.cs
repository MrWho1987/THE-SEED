namespace Seed.Market.Trading;

/// <summary>
/// Computes half-Kelly position sizing from trade history.
/// </summary>
public static class KellyPositionSizer
{
    /// <summary>
    /// Compute half-Kelly fraction, clamped to [minPct, maxPct].
    /// </summary>
    public static decimal ComputeHalfKelly(
        IReadOnlyList<ClosedTrade> recentTrades,
        decimal minPct = 0.01m,
        decimal maxPct = 0.25m)
    {
        if (recentTrades.Count < 5)
            return minPct;

        int wins = recentTrades.Count(t => t.Pnl > 0);
        float winRate = (float)wins / recentTrades.Count;

        float avgWin = recentTrades.Where(t => t.Pnl > 0).Select(t => (float)t.Pnl).DefaultIfEmpty(0f).Average();
        float avgLoss = recentTrades.Where(t => t.Pnl <= 0).Select(t => MathF.Abs((float)t.Pnl)).DefaultIfEmpty(1f).Average();

        if (avgLoss <= 0f) avgLoss = 1f;
        float winLossRatio = avgWin / avgLoss;

        float kellyFraction = winRate - (1f - winRate) / winLossRatio;
        float halfKelly = MathF.Max(0f, kellyFraction / 2f);

        return Math.Clamp((decimal)halfKelly, minPct, maxPct);
    }
}
