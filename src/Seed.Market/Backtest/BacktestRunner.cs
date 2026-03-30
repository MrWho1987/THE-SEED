using Seed.Core;
using Seed.Market.Evolution;
using Seed.Market.Signals;

namespace Seed.Market.Backtest;

/// <summary>
/// Orchestrates backtesting: downloads data, slices into training/validation windows,
/// and runs the MarketEvaluator against each window.
/// </summary>
public sealed class BacktestRunner
{
    private readonly MarketConfig _config;
    private readonly HistoricalDataStore _store;
    private readonly MarketEvaluator _evaluator;

    public BacktestRunner(MarketConfig config)
    {
        _config = config;
        _store = new HistoricalDataStore(
            Path.Combine(config.OutputDirectory, "data_cache"));
        _evaluator = new MarketEvaluator(config);
    }

    /// <summary>
    /// Load historical data for the given date range and return evaluation-ready arrays.
    /// When enrich=true, downloads supplemental data (macro, on-chain, sentiment, etc.).
    /// </summary>
    public async Task<(SignalSnapshot[] snapshots, float[] prices, float[] rawVolumes, float[] rawFundingRates)> LoadData(
        string symbol, DateTimeOffset start, DateTimeOffset end, bool enrich = false)
    {
        var candles = await _store.FetchCandles(symbol, start, end);

        Dictionary<int, float[]>? enrichment = null;
        if (enrich)
        {
            var enricher = new HistoricalSignalEnricher(
                Path.Combine(_config.OutputDirectory, "data_cache"),
                _config.CoinGeckoApiKey);
            enrichment = await enricher.EnrichAsync(candles, start, end);
        }

        return HistoricalDataStore.CandlesToSignals(candles, enrichment);
    }

    /// <summary>
    /// Evaluate a population on a specific data window.
    /// </summary>
    public Dictionary<Guid, MarketEvalResult> Evaluate(
        IReadOnlyList<IGenome> population,
        SignalSnapshot[] history,
        float[] prices,
        float[] rawVolumes,
        float[] rawFundingRates,
        int generationIndex)
    {
        return _evaluator.Evaluate(population, history, prices, rawVolumes, rawFundingRates, generationIndex);
    }

    /// <summary>
    /// Split data into training and validation windows for a rolling backtest.
    /// Each window is shifted by `stepHours` from the previous one.
    /// </summary>
    public static (int trainStart, int trainEnd, int valStart, int valEnd)[] CreateRollingWindows(
        int totalLength, int trainHours, int valHours, int stepHours)
    {
        var windows = new List<(int, int, int, int)>();
        int windowSize = trainHours + valHours;

        for (int start = 0; start + windowSize <= totalLength; start += stepHours)
        {
            windows.Add((start, start + trainHours, start + trainHours, start + windowSize));
        }

        return windows.ToArray();
    }
}
