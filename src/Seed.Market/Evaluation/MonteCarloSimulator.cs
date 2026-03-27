using Seed.Market.Trading;

namespace Seed.Market.Evaluation;

/// <summary>
/// Bootstrap resampling of closed trades to produce confidence intervals on returns.
/// </summary>
public static class MonteCarloSimulator
{
    public readonly record struct SimulationResult(
        float P5Return,
        float MedianReturn,
        float P95Return,
        float MeanReturn,
        int Resamples
    );

    public static SimulationResult Simulate(
        IReadOnlyList<ClosedTrade> trades,
        decimal initialCapital,
        int resamples = 10_000,
        int? seed = null)
    {
        if (trades.Count == 0)
            return new SimulationResult(0f, 0f, 0f, 0f, 0);

        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        var totalReturns = new float[resamples];

        for (int r = 0; r < resamples; r++)
        {
            decimal balance = initialCapital;
            for (int t = 0; t < trades.Count; t++)
            {
                var trade = trades[rng.Next(trades.Count)];
                balance += trade.Pnl;
            }
            totalReturns[r] = initialCapital > 0
                ? (float)((balance - initialCapital) / initialCapital)
                : 0f;
        }

        Array.Sort(totalReturns);

        return new SimulationResult(
            P5Return: totalReturns[(int)(resamples * 0.05)],
            MedianReturn: totalReturns[resamples / 2],
            P95Return: totalReturns[(int)(resamples * 0.95)],
            MeanReturn: totalReturns.Average(),
            Resamples: resamples
        );
    }
}
