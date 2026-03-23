using Seed.Core;
using Seed.Genetics;
using Seed.Agents;
using Seed.Worlds;

namespace Seed.Evolution;

/// <summary>
/// The main evolution loop with deterministic parallel evaluation.
/// </summary>
public sealed class EvolutionLoop
{
    private readonly RunConfig _config;
    private readonly IObservatory _observatory;
    private readonly Evaluator _evaluator;
    private readonly SpeciationManager _speciation;
    private readonly InnovationTracker _innovations;

    private List<IGenome> _population;
    private Dictionary<Guid, GenomeEvaluationResult> _evaluations;

    public int Generation { get; private set; }
    public IReadOnlyList<IGenome> Population => _population;
    public IReadOnlyDictionary<Guid, GenomeEvaluationResult> Evaluations => _evaluations;
    public int SpeciesCount => _speciation.Species.Count;

    public EvolutionLoop(RunConfig config, IObservatory observatory)
    {
        _config = config;
        _observatory = observatory;
        _speciation = new SpeciationManager();
        _innovations = InnovationTracker.CreateDefault();

        // Calculate sensor count based on agent config
        var agentConfig = AgentConfig.Default;
        _evaluator = new Evaluator(agentConfig.TotalSensorCount, ContinuousWorld.ActuatorCount);

        _population = new List<IGenome>();
        _evaluations = new Dictionary<Guid, GenomeEvaluationResult>();
    }

    /// <summary>
    /// Initialize the population with random genomes.
    /// </summary>
    public void Initialize()
    {
        var rng = new Rng64(_config.RunSeed);

        _population.Clear();
        for (int i = 0; i < _config.Budgets.Population.PopulationSize; i++)
        {
            var genome = SeedGenome.CreateRandom(rng);
            _population.Add(genome);
        }

        Generation = 0;
    }

    /// <summary>
    /// Run one generation of evolution.
    /// </summary>
    public void RunGeneration()
    {
        _observatory.OnEvent(new ObsEvent(
            ObsEventType.GenerationStart,
            Generation,
            Guid.Empty,
            $"{{\"populationSize\": {_population.Count}}}"
        ));

        // 1) Evaluate all genomes (deterministic parallel)
        EvaluatePopulation();

        // 2) Speciate
        _speciation.Speciate(_population, _config.Speciation);

        // 3) Log species assignment
        foreach (var genome in _population)
        {
            int speciesId = _speciation.GetSpeciesId(genome);
            _observatory.OnEvent(new ObsEvent(
                ObsEventType.SpeciesAssigned,
                Generation,
                genome.GenomeId,
                $"{{\"speciesId\": {speciesId}}}"
            ));
        }

        // 4) Selection and reproduction
        var nextGen = Reproduce();

        // 5) Replace population
        _population = nextGen;
        Generation++;

        // Log generation end
        if (_evaluations.Count > 0)
        {
            var bestEval = _evaluations.Values
                .OrderByDescending(e => e.Aggregate.Score)
                .First();

            _observatory.OnEvent(new ObsEvent(
                ObsEventType.GenerationEnd,
                Generation - 1,
                bestEval.GenomeId,
                $"{{\"bestScore\": {bestEval.Aggregate.Score:F2}, \"meanFitness\": {bestEval.Aggregate.MeanFitness:F2}}}"
            ));
        }

        _observatory.Flush();
    }

    private void EvaluatePopulation()
    {
        var evalCtx = new EvaluationContext(
            RunSeed: _config.RunSeed,
            GenerationIndex: Generation,
            WorldBundleKey: 0,
            DevelopmentBudget: _config.Budgets.Development,
            RuntimeBudget: _config.Budgets.Runtime,
            WorldBudget: _config.Budgets.World,
            ModulatorCount: ModulatorIndex.Count,
            Ablations: _config.Ablations,
            ArenaRounds: _config.Budgets.Population.ArenaRounds,
            FitnessConfig: _config.Fitness
        );

        _evaluations = _evaluator.EvaluateArena(_population, evalCtx);

        foreach (var (id, eval) in _evaluations)
        {
            _observatory.OnEvent(new ObsEvent(
                ObsEventType.GenomeEvaluated,
                Generation,
                id,
                $"{{\"score\": {eval.Aggregate.Score:F2}, \"mean\": {eval.Aggregate.MeanFitness:F2}}}"
            ));
        }
    }

    private List<IGenome> Reproduce()
    {
        var nextGen = new List<IGenome>();

        // Get fitness scores
        var fitnesses = _evaluations.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Aggregate.Score
        );

        // Allocate offspring per species
        int totalOffspring = _config.Budgets.Population.PopulationSize;
        var allocation = _speciation.AllocateOffspring(
            fitnesses, totalOffspring, _config.Budgets.Population, _config.Speciation);

        // Generate offspring for each species
        int childOrdinal = 0;
        foreach (var species in _speciation.Species.OrderBy(s => s.SpeciesId))
        {
            int numOffspring = allocation.GetValueOrDefault(species.SpeciesId, 0);
            if (numOffspring == 0) continue;

            // Sort members by fitness
            var sortedMembers = species.Members
                .OrderByDescending(g => fitnesses.GetValueOrDefault(g.GenomeId, 0f))
                .ToList();

            // Elites
            int eliteCount = Math.Min(
                _config.Budgets.Population.ElitesPerSpecies,
                sortedMembers.Count
            );

            if (species.Members.Count >= _config.Budgets.Population.MinSpeciesSizeForElitism)
            {
                for (int i = 0; i < eliteCount && nextGen.Count < totalOffspring; i++)
                {
                    nextGen.Add(sortedMembers[i].CloneGenome());
                    numOffspring--;
                }
            }

            int tournamentSize = _config.Speciation.TournamentSize;
            for (int i = 0; i < numOffspring && nextGen.Count < totalOffspring; i++)
            {
                ulong mutSeed = SeedDerivation.MutationSeed(
                    _config.RunSeed, Generation, species.SpeciesId, childOrdinal++);
                var rng = new Rng64(mutSeed);

                IGenome child;
                if (rng.NextFloat01() < _config.Mutation.PCrossover && sortedMembers.Count >= 2)
                {
                    var p1 = TournamentSelect(sortedMembers, fitnesses, tournamentSize, ref rng);
                    var p2 = TournamentSelect(sortedMembers, fitnesses, tournamentSize, ref rng);
                    if (fitnesses.GetValueOrDefault(p2.GenomeId, 0f) > fitnesses.GetValueOrDefault(p1.GenomeId, 0f))
                        (p1, p2) = (p2, p1);
                    child = SeedGenome.Crossover((SeedGenome)p1, (SeedGenome)p2, ref rng);
                }
                else
                {
                    child = TournamentSelect(sortedMembers, fitnesses, tournamentSize, ref rng);
                }

                var mutCtx = new MutationContext(
                    _config.RunSeed,
                    Generation,
                    _config.Mutation,
                    _innovations,
                    rng
                );

                child = child.Mutate(mutCtx);
                nextGen.Add(child);
            }
        }

        // Fill remaining slots if needed
        while (nextGen.Count < totalOffspring)
        {
            var rng = new Rng64(SeedDerivation.MutationSeed(
                _config.RunSeed, Generation, 9999, childOrdinal++));
            nextGen.Add(SeedGenome.CreateRandom(rng));
        }

        return nextGen;
    }

    /// <summary>
    /// Select a parent using tournament selection.
    /// </summary>
    private IGenome TournamentSelect(
        List<IGenome> candidates,
        Dictionary<Guid, float> fitnesses,
        int tournamentSize,
        ref Rng64 rng)
    {
        if (candidates.Count == 0)
            throw new InvalidOperationException("Cannot select from empty candidate list");

        if (candidates.Count == 1)
            return candidates[0];

        IGenome best = candidates[rng.NextInt(candidates.Count)];
        float bestFitness = fitnesses.GetValueOrDefault(best.GenomeId, 0f);

        for (int i = 1; i < tournamentSize; i++)
        {
            var challenger = candidates[rng.NextInt(candidates.Count)];
            float challengerFitness = fitnesses.GetValueOrDefault(challenger.GenomeId, 0f);
            if (challengerFitness > bestFitness)
            {
                best = challenger;
                bestFitness = challengerFitness;
            }
        }

        return best;
    }

    /// <summary>
    /// Get the best evaluation result from the last generation.
    /// </summary>
    public GenomeEvaluationResult? GetBestEvaluation()
    {
        if (_evaluations.Count == 0)
            return null;

        return _evaluations.Values
            .OrderByDescending(e => e.Aggregate.Score)
            .First();
    }

    /// <summary>
    /// Get the best genome from the current population.
    /// </summary>
    public IGenome? GetBestGenome()
    {
        if (_evaluations.Count == 0)
            return null;

        var best = _evaluations
            .OrderByDescending(kv => kv.Value.Aggregate.Score)
            .First();

        return _population.FirstOrDefault(g => g.GenomeId == best.Key);
    }
}

