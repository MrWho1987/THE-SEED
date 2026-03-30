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
    private readonly IFitnessFunction _fitnessFunction;

    public MarketEvaluator(MarketConfig config, IFitnessFunction? fitnessFunction = null)
    {
        _config = config;
        _developer = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        _fitnessFunction = fitnessFunction ?? new DefaultFitnessFunction(config);
    }

    /// <summary>
    /// Evaluate all genomes against a historical data window.
    /// Returns genome ID → fitness mapping.
    /// </summary>
    public Dictionary<Guid, MarketEvalResult> Evaluate(
        IReadOnlyList<IGenome> population,
        SignalSnapshot[] history,
        float[] rawPrices,
        float[] rawVolumes,
        float[] rawFundingRates,
        int generationIndex)
    {
        if (history.Length == 0 || rawPrices.Length == 0)
            throw new ArgumentException("History and prices must not be empty");
        if (history.Length != rawPrices.Length || history.Length != rawVolumes.Length || history.Length != rawFundingRates.Length)
            throw new ArgumentException($"Array length mismatch: history={history.Length}, prices={rawPrices.Length}, volumes={rawVolumes.Length}, funding={rawFundingRates.Length}");

        var devCtx = new DevelopmentContext(_config.RunSeed, generationIndex);

        var entries = new (IGenome Genome, BrainGraph Graph)[population.Count];
        Parallel.For(0, population.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            i =>
            {
                var sg = (SeedGenome)population[i];
                var genomeBudget = MarketBrainBudget with
                {
                    HiddenWidth = sg.Dev.SubstrateWidth,
                    HiddenHeight = sg.Dev.SubstrateHeight,
                    HiddenLayers = sg.Dev.SubstrateLayers
                };
                entries[i] = (sg, _developer.CompileGraph(sg, genomeBudget, devCtx));
            });

        var results = new MarketEvalResult[population.Count];
        Parallel.For(0, population.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            i =>
            {
                results[i] = RunAgent(entries[i], history, rawPrices, rawVolumes, rawFundingRates);
            });

        var dict = new Dictionary<Guid, MarketEvalResult>();
        for (int i = 0; i < population.Count; i++)
            dict[entries[i].Genome.GenomeId] = results[i];
        return dict;
    }

    /// <summary>
    /// Evaluate a single genome (used for periodic validation checks).
    /// </summary>
    public MarketEvalResult EvaluateSingle(
        IGenome genome, SignalSnapshot[] history, float[] rawPrices,
        float[] rawVolumes, float[] rawFundingRates, int generationIndex)
    {
        var devCtx = new DevelopmentContext(_config.RunSeed, generationIndex);
        var sg = (SeedGenome)genome;
        var genomeBudget = MarketBrainBudget with
        {
            HiddenWidth = sg.Dev.SubstrateWidth,
            HiddenHeight = sg.Dev.SubstrateHeight,
            HiddenLayers = sg.Dev.SubstrateLayers
        };
        var graph = _developer.CompileGraph(sg, genomeBudget, devCtx);
        return RunAgent((sg, graph), history, rawPrices, rawVolumes, rawFundingRates);
    }

    private MarketEvalResult RunAgent(
        (IGenome Genome, BrainGraph Graph) entry,
        SignalSnapshot[] history,
        float[] rawPrices,
        float[] rawVolumes,
        float[] rawFundingRates)
    {
        var sg = (SeedGenome)entry.Genome;
        var brain = new BrainRuntime(entry.Graph, sg.Learn, sg.Stable, 1);
        var trader = new PaperTrader(_config);
        var agent = new MarketAgent(sg.GenomeId, brain, trader);

        for (int t = 0; t < history.Length; t++)
        {
            float rawP = rawPrices[t];
            if (float.IsNaN(rawP) || float.IsInfinity(rawP) || rawP <= 0f) continue;
            decimal price = (decimal)rawP;

            float rawV = rawVolumes[t];
            decimal vol = (float.IsNaN(rawV) || float.IsInfinity(rawV)) ? 0m : (decimal)rawV;
            var ctx = new TickContext(price, vol, rawFundingRates[t], t, (float)t);

            agent.ProcessTick(history[t], ctx);
            agent.Portfolio.RecordEquity(price);
        }

        float lastP = rawPrices[^1];
        decimal finalPrice = (float.IsNaN(lastP) || float.IsInfinity(lastP) || lastP <= 0f)
            ? agent.Portfolio.InitialBalance : (decimal)lastP;
        trader.CloseAllPositions(agent.Portfolio, finalPrice, agent.Tick);

        var breakdown = _fitnessFunction.ComputeDetailed(agent.Portfolio, finalPrice);
        return new MarketEvalResult(sg.GenomeId, breakdown);
    }

    public FitnessBreakdown EvaluateEnsemble(
        IReadOnlyList<IGenome> champions, SignalSnapshot[] history, float[] rawPrices,
        float[] rawVolumes, float[] rawFundingRates, int generationIndex)
    {
        if (champions.Count == 0)
            return default;

        var devCtx = new DevelopmentContext(_config.RunSeed, generationIndex);
        var agents = new List<(MarketAgent Agent, PaperTrader Trader)>();

        foreach (var genome in champions)
        {
            var sg = (SeedGenome)genome;
            var genomeBudget = MarketBrainBudget with
            {
                HiddenWidth = sg.Dev.SubstrateWidth,
                HiddenHeight = sg.Dev.SubstrateHeight,
                HiddenLayers = sg.Dev.SubstrateLayers
            };
            var graph = _developer.CompileGraph(sg, genomeBudget, devCtx);
            var brain = new BrainRuntime(graph, sg.Learn, sg.Stable, 1);
            var trader = new PaperTrader(_config);
            agents.Add((new MarketAgent(sg.GenomeId, brain, trader), trader));
        }

        var ensembleTrader = new PaperTrader(_config);
        var ensemblePortfolio = ensembleTrader.CreatePortfolio();

        for (int t = 0; t < history.Length; t++)
        {
            float rawP = rawPrices[t];
            if (float.IsNaN(rawP) || float.IsInfinity(rawP) || rawP <= 0f) continue;
            decimal price = (decimal)rawP;

            float rawV = rawVolumes[t];
            decimal vol = (float.IsNaN(rawV) || float.IsInfinity(rawV)) ? 0m : (decimal)rawV;
            var ctx = new TickContext(price, vol, rawFundingRates[t], t, (float)t);

            float directionSum = 0f;
            float sizeSum = 0f;
            float urgencySum = 0f;
            int exitVotes = 0;

            foreach (var (agent, _) in agents)
            {
                agent.ProcessTick(history[t], ctx);
                var sig = agent.LastGeneratedSignal;
                directionSum += (int)sig.Direction;
                sizeSum += sig.SizePct;
                urgencySum += sig.Urgency;
                if (sig.ExitCurrent) exitVotes++;
            }

            int n = agents.Count;
            float avgDir = directionSum / n;
            var direction = avgDir > ActionInterpreter.DirectionDeadzone ? TradeDirection.Long
                : avgDir < -ActionInterpreter.DirectionDeadzone ? TradeDirection.Short : TradeDirection.Flat;
            bool exit = exitVotes > n / 2;

            var ensembleSignal = new TradingSignal(direction, sizeSum / n, urgencySum / n, exit);
            ensembleTrader.ProcessSignal(ensembleSignal, ensemblePortfolio, ctx);
            ensemblePortfolio.RecordEquity(price);
        }

        float lastEP = rawPrices[^1];
        decimal finalPrice = (float.IsNaN(lastEP) || float.IsInfinity(lastEP) || lastEP <= 0f)
            ? ensemblePortfolio.InitialBalance : (decimal)lastEP;
        ensembleTrader.CloseAllPositions(ensemblePortfolio, finalPrice, history.Length);
        return _fitnessFunction.ComputeDetailed(ensemblePortfolio, finalPrice);
    }
}

public readonly record struct MarketEvalResult(
    Guid GenomeId,
    FitnessBreakdown Fitness
);
