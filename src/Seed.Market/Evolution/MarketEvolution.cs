using Seed.Core;
using Seed.Genetics;
using Seed.Market.Backtest;
using Seed.Market.Signals;

namespace Seed.Market.Evolution;

/// <summary>
/// Full evolutionary loop for market trading agents.
/// Reuses NEAT speciation and CPPN genomes from Seed.Genetics.
/// Adds rolling evaluation windows and per-species capital allocation.
/// </summary>
public sealed class MarketEvolution
{
    private readonly MarketConfig _config;
    private readonly IObservatory _observatory;
    private readonly MarketEvaluator _evaluator;
    private readonly SpeciationManager _speciation;
    private readonly InnovationTracker _innovations;

    private List<IGenome> _population;
    private Dictionary<Guid, MarketEvalResult> _evaluations;
    private readonly Dictionary<int, float> _speciesCumulativePnl = [];

    public int Generation { get; private set; }
    public IReadOnlyList<IGenome> Population => _population;
    public IReadOnlyDictionary<Guid, MarketEvalResult> Evaluations => _evaluations;
    public int SpeciesCount => _speciation.Species.Count;

    public MarketEvolution(MarketConfig config, IObservatory observatory)
    {
        _config = config;
        _observatory = observatory;
        _evaluator = new MarketEvaluator(config);
        _speciation = new SpeciationManager();
        _innovations = InnovationTracker.CreateDefault();
        _population = [];
        _evaluations = [];
    }

    public void Initialize()
    {
        var rng = new Rng64(_config.RunSeed);
        _population = new List<IGenome>();
        for (int i = 0; i < _config.PopulationSize; i++)
            _population.Add(SeedGenome.CreateRandom(rng));
        Generation = 0;
    }

    /// <summary>
    /// Run one generation: evaluate on the given data window, speciate, select, reproduce.
    /// </summary>
    public GenerationReport RunGeneration(SignalSnapshot[] history, float[] prices)
    {
        _observatory.OnEvent(new ObsEvent(
            ObsEventType.GenerationStart, Generation, Guid.Empty,
            $"{{\"pop\":{_population.Count}}}"));

        // 1. Evaluate
        _evaluations = _evaluator.Evaluate(_population, history, prices, Generation);

        // 2. Speciate
        var specCfg = new SpeciationConfig(
            C1: 1f, C2: 1f, C3: 0.5f,
            CompatibilityThreshold: 3.5f,
            TournamentSize: 3);
        _speciation.Speciate(_population, specCfg);

        // 3. Update species cumulative P&L for capital allocation
        UpdateSpeciesPnl();

        // 4. Log generation stats
        var report = BuildReport(prices[^1]);

        _observatory.OnEvent(new ObsEvent(
            ObsEventType.GenerationEnd, Generation, report.BestGenomeId,
            $"{{\"best\":{report.BestFitness:F4},\"mean\":{report.MeanFitness:F4}," +
            $"\"species\":{report.SpeciesCount},\"trades\":{report.TotalTrades}}}"));

        // 5. Reproduce
        _population = Reproduce();
        Generation++;

        _observatory.Flush();
        return report;
    }

    private void UpdateSpeciesPnl()
    {
        foreach (var species in _speciation.Species)
        {
            float totalPnl = 0;
            int count = 0;
            foreach (var member in species.Members)
            {
                if (_evaluations.TryGetValue(member.GenomeId, out var eval))
                {
                    totalPnl += eval.Fitness.NetPnl;
                    count++;
                }
            }
            float avgPnl = count > 0 ? totalPnl / count : 0f;

            if (!_speciesCumulativePnl.ContainsKey(species.SpeciesId))
                _speciesCumulativePnl[species.SpeciesId] = 0f;
            _speciesCumulativePnl[species.SpeciesId] += avgPnl;
        }
    }

    private List<IGenome> Reproduce()
    {
        var nextGen = new List<IGenome>();
        int totalOffspring = _config.PopulationSize;

        var fitnesses = _evaluations.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Fitness.Fitness);

        var popBudget = new PopulationBudget(
            PopulationSize: _config.PopulationSize,
            ArenaRounds: 1,
            ElitesPerSpecies: 1,
            MinSpeciesSizeForElitism: 3);

        var specCfg = new SpeciationConfig(
            C1: 1f, C2: 1f, C3: 0.5f,
            CompatibilityThreshold: 3.5f,
            TournamentSize: 3);

        var allocation = _speciation.AllocateOffspring(fitnesses, totalOffspring, popBudget, specCfg);

        var mutCfg = MutationConfig.Default;
        int childOrdinal = 0;

        foreach (var species in _speciation.Species.OrderBy(s => s.SpeciesId))
        {
            int numOffspring = allocation.GetValueOrDefault(species.SpeciesId, 0);
            if (numOffspring == 0) continue;

            var sortedMembers = species.Members
                .OrderByDescending(g => fitnesses.GetValueOrDefault(g.GenomeId, 0f))
                .ToList();

            // Elites
            if (sortedMembers.Count >= 3)
            {
                nextGen.Add(sortedMembers[0].CloneGenome());
                numOffspring--;
            }

            for (int i = 0; i < numOffspring && nextGen.Count < totalOffspring; i++)
            {
                ulong mutSeed = SeedDerivation.MutationSeed(
                    _config.RunSeed, Generation, species.SpeciesId, childOrdinal++);
                var rng = new Rng64(mutSeed);

                IGenome child;
                if (rng.NextFloat01() < mutCfg.PCrossover && sortedMembers.Count >= 2)
                {
                    var p1 = TournamentSelect(sortedMembers, fitnesses, 3, ref rng);
                    var p2 = TournamentSelect(sortedMembers, fitnesses, 3, ref rng);
                    if (fitnesses.GetValueOrDefault(p2.GenomeId, 0f) >
                        fitnesses.GetValueOrDefault(p1.GenomeId, 0f))
                        (p1, p2) = (p2, p1);
                    child = SeedGenome.Crossover((SeedGenome)p1, (SeedGenome)p2, ref rng);
                }
                else
                {
                    child = TournamentSelect(sortedMembers, fitnesses, 3, ref rng);
                }

                var mutCtx = new MutationContext(
                    _config.RunSeed, Generation, mutCfg, _innovations, rng);
                child = child.Mutate(mutCtx);
                nextGen.Add(child);
            }
        }

        while (nextGen.Count < totalOffspring)
        {
            var rng = new Rng64(SeedDerivation.MutationSeed(
                _config.RunSeed, Generation, 9999, childOrdinal++));
            nextGen.Add(SeedGenome.CreateRandom(rng));
        }

        return nextGen;
    }

    private static IGenome TournamentSelect(
        List<IGenome> candidates, Dictionary<Guid, float> fitnesses,
        int tournamentSize, ref Rng64 rng)
    {
        if (candidates.Count <= 1) return candidates[0];
        IGenome best = candidates[rng.NextInt(candidates.Count)];
        float bestF = fitnesses.GetValueOrDefault(best.GenomeId, 0f);
        for (int i = 1; i < tournamentSize; i++)
        {
            var c = candidates[rng.NextInt(candidates.Count)];
            float cF = fitnesses.GetValueOrDefault(c.GenomeId, 0f);
            if (cF > bestF) { best = c; bestF = cF; }
        }
        return best;
    }

    private GenerationReport BuildReport(float finalPrice)
    {
        var sorted = _evaluations.Values
            .OrderByDescending(e => e.Fitness.Fitness).ToArray();
        var best = sorted[0];
        float mean = sorted.Average(e => e.Fitness.Fitness);
        int totalTrades = sorted.Sum(e => e.Fitness.TotalTrades);

        return new GenerationReport(
            Generation: Generation,
            BestGenomeId: best.GenomeId,
            BestFitness: best.Fitness.Fitness,
            BestReturn: best.Fitness.ReturnPct,
            BestTrades: best.Fitness.TotalTrades,
            BestWinRate: best.Fitness.WinRate,
            MeanFitness: mean,
            SpeciesCount: _speciation.Species.Count,
            TotalTrades: totalTrades,
            PopulationSize: _population.Count);
    }

    public IGenome? GetBestGenome()
    {
        if (_evaluations.Count == 0) return null;
        var bestId = _evaluations.Values.OrderByDescending(e => e.Fitness.Fitness).First().GenomeId;
        return _population.FirstOrDefault(g => g.GenomeId == bestId);
    }
}

public readonly record struct GenerationReport(
    int Generation,
    Guid BestGenomeId,
    float BestFitness,
    float BestReturn,
    int BestTrades,
    float BestWinRate,
    float MeanFitness,
    int SpeciesCount,
    int TotalTrades,
    int PopulationSize
);
