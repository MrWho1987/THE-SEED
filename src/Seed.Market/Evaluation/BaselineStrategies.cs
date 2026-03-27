using Seed.Market.Evolution;
using Seed.Market.Signals;
using Seed.Market.Trading;

namespace Seed.Market.Evaluation;

/// <summary>
/// Simple baseline trading strategies for comparison against evolved agents.
/// Each produces a FitnessBreakdown on the same data for apples-to-apples comparison.
/// </summary>
public static class BaselineStrategies
{
    public static FitnessBreakdown BuyAndHold(float[] prices, MarketConfig config)
    {
        var portfolio = new PortfolioState
        {
            Balance = config.InitialCapital,
            InitialBalance = config.InitialCapital,
            MaxEquity = config.InitialCapital,
        };

        decimal entryPrice = (decimal)prices[0];
        decimal size = config.InitialCapital * config.MaxPositionPct / entryPrice;
        decimal fee = entryPrice * size * config.TakerFee;
        portfolio.Balance -= fee;

        portfolio.OpenPositions.Add(new Position
        {
            Symbol = config.Symbols[0],
            Direction = TradeDirection.Long,
            EntryPrice = entryPrice,
            Size = size,
            OpenTime = DateTimeOffset.UtcNow,
            OpenTick = 0
        });

        for (int t = 0; t < prices.Length; t++)
            portfolio.RecordEquity((decimal)prices[t]);

        decimal finalPrice = (decimal)prices[^1];
        decimal pnl = (finalPrice - entryPrice) * size - fee;
        portfolio.Balance += pnl;
        portfolio.OpenPositions.Clear();
        portfolio.TradeHistory.Add(new ClosedTrade(
            config.Symbols[0], TradeDirection.Long, entryPrice, finalPrice,
            size, pnl, fee, prices.Length, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));

        return MarketFitness.ComputeDetailed(portfolio, finalPrice, config.ShrinkageK);
    }

    public static FitnessBreakdown SmaCrossover(
        float[] prices, MarketConfig config, int shortPeriod = 20, int longPeriod = 50)
    {
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        for (int t = longPeriod; t < prices.Length; t++)
        {
            float smaShort = Average(prices, t - shortPeriod, shortPeriod);
            float smaLong = Average(prices, t - longPeriod, longPeriod);

            decimal price = (decimal)prices[t];
            bool wantLong = smaShort > smaLong;

            if (wantLong && portfolio.OpenPositions.Count == 0)
            {
                var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
                trader.ProcessSignal(signal, portfolio, price, t);
            }
            else if (!wantLong && portfolio.OpenPositions.Any(p => p.Direction == TradeDirection.Long))
            {
                var signal = new TradingSignal(TradeDirection.Flat, 0f, 0f, true);
                trader.ProcessSignal(signal, portfolio, price, t);
            }

            portfolio.RecordEquity(price);
        }

        decimal finalPrice = (decimal)prices[^1];
        trader.CloseAllPositions(portfolio, finalPrice, prices.Length);
        return MarketFitness.ComputeDetailed(portfolio, finalPrice, config.ShrinkageK);
    }

    public static FitnessBreakdown RandomAgent(float[] prices, MarketConfig config, int seed = 42)
    {
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();
        var rng = new Random(seed);

        for (int t = 0; t < prices.Length; t++)
        {
            decimal price = (decimal)prices[t];
            float r = (float)rng.NextDouble();

            TradingSignal signal;
            if (r < 0.05f)
                signal = new TradingSignal(TradeDirection.Long, 0.3f, 0.9f, false);
            else if (r < 0.10f)
                signal = new TradingSignal(TradeDirection.Short, 0.3f, 0.9f, false);
            else if (r < 0.15f && portfolio.OpenPositions.Count > 0)
                signal = new TradingSignal(TradeDirection.Flat, 0f, 0f, true);
            else
                signal = new TradingSignal(TradeDirection.Flat, 0f, 0f, false);

            trader.ProcessSignal(signal, portfolio, price, t);
            portfolio.RecordEquity(price);
        }

        decimal finalPrice = (decimal)prices[^1];
        trader.CloseAllPositions(portfolio, finalPrice, prices.Length);
        return MarketFitness.ComputeDetailed(portfolio, finalPrice, config.ShrinkageK);
    }

    public static FitnessBreakdown MeanReversion(
        float[] prices, MarketConfig config, float buyRsi = 30f, float sellRsi = 70f, int rsiPeriod = 14)
    {
        var trader = new PaperTrader(config);
        var portfolio = trader.CreatePortfolio();

        for (int t = rsiPeriod; t < prices.Length; t++)
        {
            float rsi = ComputeRsi(prices, t, rsiPeriod);
            decimal price = (decimal)prices[t];

            if (rsi < buyRsi && portfolio.OpenPositions.Count == 0)
            {
                var signal = new TradingSignal(TradeDirection.Long, 0.5f, 0.9f, false);
                trader.ProcessSignal(signal, portfolio, price, t);
            }
            else if (rsi > sellRsi && portfolio.OpenPositions.Any(p => p.Direction == TradeDirection.Long))
            {
                var signal = new TradingSignal(TradeDirection.Flat, 0f, 0f, true);
                trader.ProcessSignal(signal, portfolio, price, t);
            }

            portfolio.RecordEquity(price);
        }

        decimal finalPrice = (decimal)prices[^1];
        trader.CloseAllPositions(portfolio, finalPrice, prices.Length);
        return MarketFitness.ComputeDetailed(portfolio, finalPrice, config.ShrinkageK);
    }

    private static float Average(float[] arr, int start, int count)
    {
        float sum = 0f;
        for (int i = start; i < start + count && i < arr.Length; i++)
            sum += arr[i];
        return sum / count;
    }

    private static float ComputeRsi(float[] prices, int index, int period)
    {
        float gainSum = 0, lossSum = 0;
        for (int i = index - period + 1; i <= index; i++)
        {
            float change = prices[i] - prices[i - 1];
            if (change > 0) gainSum += change;
            else lossSum -= change;
        }
        float avgGain = gainSum / period;
        float avgLoss = lossSum / period;
        if (avgLoss == 0) return 100f;
        float rs = avgGain / avgLoss;
        return 100f - 100f / (1f + rs);
    }
}
