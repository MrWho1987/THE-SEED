using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Signals;
using Seed.Market.Trading;

namespace Seed.Market.Evolution;

/// <summary>
/// Evaluates a population of genomes by replaying historical market data.
/// Each genome is compiled into a brain, wired to a MarketAgent, and
/// run through the same historical window. Fitness = net profit after fees.
/// </summary>
public sealed class MarketEvaluator
{
    public static readonly DevelopmentBudget MarketBrainBudget = new(
        HiddenWidth: 16,
        HiddenHeight: 16,
        HiddenLayers: 3,
        TopKIn: 16,
        MaxOut: 20,
        LocalNeighborhoodRadius: 3,
        GlobalCandidateSamplesPerNeuron: 24,
        MaxSynapticDelay: 3);

    private readonly MarketConfig _config;
    private readonly BrainDeveloper _developer;

    public MarketEvaluator(MarketConfig config)
    {
        _config = config;
        _developer = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
    }

    /// <summary>
    /// Evaluate all genomes against a historical data window.
    /// Returns genome ID → fitness mapping.
    /// </summary>
    public Dictionary<Guid, MarketEvalResult> Evaluate(
        IReadOnlyList<IGenome> population,
        SignalSnapshot[] history,
        float[] rawPrices,
        int generationIndex)
    {
        if (history.Length == 0 || rawPrices.Length == 0)
            throw new ArgumentException("History and prices must not be empty");

        var devCtx = new DevelopmentContext(_config.RunSeed, generationIndex);
        var devBudget = MarketBrainBudget;

        // Compile all brains in parallel
        var entries = new (IGenome Genome, BrainGraph Graph)[population.Count];
        Parallel.For(0, population.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            i =>
            {
                var sg = (SeedGenome)population[i];
                entries[i] = (sg, _developer.CompileGraph(sg, devBudget, devCtx));
            });

        // Run each agent through the historical window
        var results = new MarketEvalResult[population.Count];
        Parallel.For(0, population.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            i =>
            {
                results[i] = RunAgent(entries[i], history, rawPrices);
            });

        var dict = new Dictionary<Guid, MarketEvalResult>();
        for (int i = 0; i < population.Count; i++)
            dict[entries[i].Genome.GenomeId] = results[i];
        return dict;
    }

    private MarketEvalResult RunAgent(
        (IGenome Genome, BrainGraph Graph) entry,
        SignalSnapshot[] history,
        float[] rawPrices)
    {
        var sg = (SeedGenome)entry.Genome;
        var brain = new BrainRuntime(entry.Graph, sg.Learn, sg.Stable, 1);
        var trader = new PaperTrader(_config);
        var agent = new MarketAgent(sg.GenomeId, brain, trader);

        for (int t = 0; t < history.Length; t++)
        {
            decimal price = (decimal)rawPrices[t];
            if (price <= 0) continue;
            agent.ProcessTick(history[t], price);
        }

        // Close any remaining positions at final price
        decimal finalPrice = (decimal)rawPrices[^1];
        trader.CloseAllPositions(agent.Portfolio, finalPrice, agent.Tick);

        var breakdown = MarketFitness.ComputeDetailed(agent.Portfolio, finalPrice);
        return new MarketEvalResult(sg.GenomeId, breakdown);
    }
}

public readonly record struct MarketEvalResult(
    Guid GenomeId,
    FitnessBreakdown Fitness
);
