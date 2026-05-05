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
    // V14 architecture: expanded to handle 110 inputs / 11 outputs / richer regime gating.
    // - HiddenWidth/Height 20: 1200 neurons total (+56% over v13's 16x16x3)
    // - TopKIn 32: doubles fan-in to address sparsity bottleneck
    // - MaxOut 40: doubles outgoing edges for richer information propagation
    // - MaxSynapticDelay 16: 4h temporal context at 15min bars (was 3h)
    // - ModuleCount 12: matches SignalCategory count for symmetric module layout
    public static readonly DevelopmentBudget MarketBrainBudget = new(
        HiddenWidth: 20,
        HiddenHeight: 20,
        HiddenLayers: 3,
        TopKIn: 32,
        MaxOut: 40,
        LocalNeighborhoodRadius: 3,
        GlobalCandidateSamplesPerNeuron: 24,
        MaxSynapticDelay: 16,
        ModuleCount: 12,
        GateNeuronCount: 12);

    public static readonly int[] SignalCategoryMap = BuildCategoryMap();
    public static int RegimeStart => SignalIndex.Categories.RegimeStart;
    public static int RegimeEnd => SignalIndex.Categories.RegimeEnd;

    private static int[] BuildCategoryMap()
    {
        var map = new int[SignalIndex.Count];
        for (int s = 0; s < SignalIndex.Count; s++)
            map[s] = SignalIndex.GetCategoryIndex(s);
        return map;
    }

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
                entries[i] = (sg, _developer.CompileGraph(sg, genomeBudget, devCtx, SignalCategoryMap, RegimeStart, RegimeEnd));
            });

        var results = new MarketEvalResult[population.Count];
        Parallel.For(0, population.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            i =>
            {
                results[i] = RunAgent(entries[i], history, rawPrices, rawVolumes, rawFundingRates, generationIndex);
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
        var graph = _developer.CompileGraph(sg, genomeBudget, devCtx, SignalCategoryMap, RegimeStart, RegimeEnd);
        return RunAgent((sg, graph), history, rawPrices, rawVolumes, rawFundingRates, generationIndex);
    }

    private MarketEvalResult RunAgent(
        (IGenome Genome, BrainGraph Graph) entry,
        SignalSnapshot[] history,
        float[] rawPrices,
        float[] rawVolumes,
        float[] rawFundingRates,
        int generationIndex)
    {
        var sg = (SeedGenome)entry.Genome;
        var brain = new BrainRuntime(entry.Graph, sg.Learn, sg.Stable, 1);
        var trader = new PaperTrader(_config);
        var agent = new MarketAgent(sg.GenomeId, brain, trader, maxLeverage: _config.MaxLeverage, explicitExitBonus: _config.ExplicitExitBonus, peakExitBonus: _config.PeakExitBonus, staleThresholdTicks: _config.StaleThresholdTicks, stalePenaltyPerTick: _config.StalePenaltyPerTick);

        for (int t = 0; t < history.Length; t++)
        {
            float rawP = rawPrices[t];
            if (float.IsNaN(rawP) || float.IsInfinity(rawP) || rawP <= 0f) continue;
            decimal price = (decimal)rawP;

            float rawV = rawVolumes[t];
            decimal vol = (float.IsNaN(rawV) || float.IsInfinity(rawV)) ? 0m : (decimal)rawV;
            float elapsedHours = (float)t / _config.BarsPerHour;
            var ctx = new TickContext(price, vol, rawFundingRates[t], t, elapsedHours);

            agent.ProcessTick(history[t], ctx);
            agent.Portfolio.RecordEquity(price);
        }

        float lastP = rawPrices[^1];
        decimal finalPrice = (float.IsNaN(lastP) || float.IsInfinity(lastP) || lastP <= 0f)
            ? agent.Portfolio.InitialBalance : (decimal)lastP;

        int openAtEnd = agent.Portfolio.OpenPositions.Count;
        trader.CloseAllPositions(agent.Portfolio, finalPrice, agent.Tick);

        // Compute HODL return baseline for Information Ratio fitness term
        float firstP = rawPrices[0];
        float hodlReturn = (firstP > 0f && !float.IsNaN(firstP) && !float.IsInfinity(firstP))
            ? (lastP - firstP) / firstP : 0f;

        var breakdown = _fitnessFunction.ComputeDetailed(agent.Portfolio, finalPrice, generationIndex, hodlReturn);

        if (openAtEnd > 0)
        {
            float penalty = openAtEnd * _config.OpenPositionPenalty;
            breakdown = breakdown with { Fitness = breakdown.Fitness - penalty };
        }

        // V11d Fix 7 observability: capture brain output stats and close-reason histogram
        var outputObs = agent.GetOutputObservation();
        var closeReasonCounts = new int[Enum.GetValues<CloseReason>().Length];
        foreach (var t in agent.Portfolio.TradeHistory)
            closeReasonCounts[(int)t.Reason]++;

        return new MarketEvalResult(sg.GenomeId, breakdown, outputObs, closeReasonCounts);
    }

    /// <summary>
    /// How many top-fitness champions to include in the ensemble vote.
    /// Old behavior used ALL champions with arithmetic-mean voting, which diluted aggressive
    /// signals (30 champions × 0.8 + 1 × 0.1 averaged 0.76 instead of reflecting the top
    /// performer). Fixed: top-3 with fitness-weighted voting for sharper consensus.
    /// </summary>
    public const int EnsembleTopK = 3;

    public FitnessBreakdown EvaluateEnsemble(
        IReadOnlyList<IGenome> champions, SignalSnapshot[] history, float[] rawPrices,
        float[] rawVolumes, float[] rawFundingRates, int generationIndex)
    {
        if (champions.Count == 0)
            return default;

        var devCtx = new DevelopmentContext(_config.RunSeed, generationIndex);

        // Step 1: Evaluate each champion individually to score their fitness.
        // This is the ONLY way to know which are top-K, since the caller passes raw genomes
        // without fitness scores. Cost: O(n) runs but this runs once at end of training.
        var championScores = new List<(IGenome Genome, float Fitness)>();
        foreach (var genome in champions)
        {
            var sg = (SeedGenome)genome;
            var genomeBudget = MarketBrainBudget with
            {
                HiddenWidth = sg.Dev.SubstrateWidth,
                HiddenHeight = sg.Dev.SubstrateHeight,
                HiddenLayers = sg.Dev.SubstrateLayers
            };
            var graph = _developer.CompileGraph(sg, genomeBudget, devCtx, SignalCategoryMap, RegimeStart, RegimeEnd);
            var result = RunAgent((sg, graph), history, rawPrices, rawVolumes, rawFundingRates, generationIndex);
            championScores.Add((genome, result.Fitness.Fitness));
        }

        // Step 2: Select the top-K champions by fitness. If fewer than K, use all.
        // Stable order: sort by fitness desc, then by GenomeId for determinism on ties.
        var topK = championScores
            .OrderByDescending(c => c.Fitness)
            .ThenBy(c => ((SeedGenome)c.Genome).GenomeId)
            .Take(Math.Min(EnsembleTopK, championScores.Count))
            .ToList();

        // Step 3: Compute fitness-based weights for the top-K.
        // Shift fitness values so the minimum is 0 (handles negative fitnesses gracefully),
        // then normalize. If all weights collapse to 0 (all equal), fall back to equal weights.
        float minFit = topK.Min(t => t.Fitness);
        float[] shifted = topK.Select(t => MathF.Max(0f, t.Fitness - minFit + 0.001f)).ToArray();
        float sumShifted = shifted.Sum();
        float[] weights = sumShifted > 0f
            ? shifted.Select(s => s / sumShifted).ToArray()
            : Enumerable.Repeat(1f / topK.Count, topK.Count).ToArray();

        // Step 4: Build agents for the top-K champions for live ensemble voting.
        var agents = new List<(MarketAgent Agent, PaperTrader Trader, float Weight)>();
        for (int i = 0; i < topK.Count; i++)
        {
            var sg = (SeedGenome)topK[i].Genome;
            var genomeBudget = MarketBrainBudget with
            {
                HiddenWidth = sg.Dev.SubstrateWidth,
                HiddenHeight = sg.Dev.SubstrateHeight,
                HiddenLayers = sg.Dev.SubstrateLayers
            };
            var graph = _developer.CompileGraph(sg, genomeBudget, devCtx, SignalCategoryMap, RegimeStart, RegimeEnd);
            var brain = new BrainRuntime(graph, sg.Learn, sg.Stable, 1);
            var trader = new PaperTrader(_config);
            agents.Add((new MarketAgent(sg.GenomeId, brain, trader, maxLeverage: _config.MaxLeverage, explicitExitBonus: _config.ExplicitExitBonus, peakExitBonus: _config.PeakExitBonus), trader, weights[i]));
        }

        var ensembleTrader = new PaperTrader(_config);
        var ensemblePortfolio = ensembleTrader.CreatePortfolio();

        // Step 5: Tick through history with fitness-weighted voting.
        for (int t = 0; t < history.Length; t++)
        {
            float rawP = rawPrices[t];
            if (float.IsNaN(rawP) || float.IsInfinity(rawP) || rawP <= 0f) continue;
            decimal price = (decimal)rawP;

            float rawV = rawVolumes[t];
            decimal vol = (float.IsNaN(rawV) || float.IsInfinity(rawV)) ? 0m : (decimal)rawV;
            float elapsedHours = (float)t / _config.BarsPerHour;
            var ctx = new TickContext(price, vol, rawFundingRates[t], t, elapsedHours);

            float weightedDirection = 0f;
            float weightedSize = 0f;
            float weightedUrgency = 0f;
            float weightedExitRaw = 0f;
            float weightedLeverage = 0f;

            foreach (var (agent, _, weight) in agents)
            {
                agent.ProcessTick(history[t], ctx);
                var sig = agent.LastGeneratedSignal;
                weightedDirection += (int)sig.Direction * weight;
                weightedSize += sig.SizePct * weight;
                weightedUrgency += sig.Urgency * weight;
                weightedExitRaw += sig.RawExitValue * weight;
                weightedLeverage += sig.Leverage * weight;
            }

            var direction = weightedDirection > ActionInterpreter.DirectionDeadzone ? TradeDirection.Long
                : weightedDirection < -ActionInterpreter.DirectionDeadzone ? TradeDirection.Short : TradeDirection.Flat;
            bool exit = weightedExitRaw > ActionInterpreter.ExitThreshold;

            var ensembleSignal = new TradingSignal(
                direction,
                weightedSize,
                weightedUrgency,
                exit,
                RawExitValue: weightedExitRaw,
                Leverage: weightedLeverage);
            ensembleTrader.ProcessSignal(ensembleSignal, ensemblePortfolio, ctx);
            ensemblePortfolio.RecordEquity(price);
        }

        float lastEP = rawPrices[^1];
        decimal finalPrice = (float.IsNaN(lastEP) || float.IsInfinity(lastEP) || lastEP <= 0f)
            ? ensemblePortfolio.InitialBalance : (decimal)lastEP;
        ensembleTrader.CloseAllPositions(ensemblePortfolio, finalPrice, history.Length);

        // HODL baseline for Information Ratio
        float firstEP = rawPrices[0];
        float hodlReturn = (firstEP > 0f && !float.IsNaN(firstEP) && !float.IsInfinity(firstEP))
            ? (lastEP - firstEP) / firstEP : 0f;

        return _fitnessFunction.ComputeDetailed(ensemblePortfolio, finalPrice, generationIndex, hodlReturn);
    }
}

public readonly record struct MarketEvalResult(
    Guid GenomeId,
    FitnessBreakdown Fitness,
    // V11d Fix 7 observability:
    OutputObservation? OutputObs = null,
    int[]? CloseReasonCounts = null  // indexed by (int)CloseReason
);
