using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
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
    private InnovationTracker _innovations;

    private List<IGenome> _population;
    private Dictionary<Guid, MarketEvalResult> _evaluations;
    private readonly Dictionary<int, float> _speciesCumulativePnl = [];
    private readonly EliteArchive _archive = new(100);
    private float _compatibilityThreshold = 3.5f;

    public int Generation { get; private set; }
    public IReadOnlyList<IGenome> Population => _population;
    public IReadOnlyDictionary<Guid, MarketEvalResult> Evaluations => _evaluations;
    public int SpeciesCount => _speciation.Species.Count;
    public EliteArchive Archive => _archive;
    public InnovationTracker Innovations => _innovations;
    public float CompatibilityThreshold => _compatibilityThreshold;
    public int NextSpeciesId => _speciation.NextSpeciesId;

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
    /// Resume from a checkpoint: restore a previously saved population at a given generation.
    /// </summary>
    public void InitializeFrom(List<IGenome> restoredPopulation, int generation,
        int nextInnovationId = 0, int nextCppnNodeId = 0, float compatibilityThreshold = 0f,
        IReadOnlyList<(int SpeciesId, IGenome Representative, int StagnationCounter, float BestFitness)>? speciesState = null,
        int nextSpeciesId = 0,
        IReadOnlyList<(int SpeciesId, IGenome Genome, float Fitness)>? archiveState = null)
    {
        _population = restoredPopulation;
        Generation = generation;
        if (nextInnovationId > 0 && nextCppnNodeId > 0)
            _innovations = new InnovationTracker(nextInnovationId, nextCppnNodeId);
        if (compatibilityThreshold > 0f)
            _compatibilityThreshold = compatibilityThreshold;
        if (speciesState is { Count: > 0 })
            _speciation.RestoreFrom(speciesState, nextSpeciesId);
        if (archiveState is { Count: > 0 })
            _archive.RestoreFrom(archiveState);
    }

    /// <summary>
    /// Run one generation: evaluate on the given data window, speciate, select, reproduce.
    /// </summary>
    public GenerationReport RunGeneration(SignalSnapshot[] history, float[] prices,
        float[] rawVolumes, float[] rawFundingRates)
    {
        return RunGeneration([(history, prices, rawVolumes, rawFundingRates)]);
    }

    /// <summary>
    /// Run one generation with multi-window evaluation. Fitness = mean across windows.
    /// </summary>
    public GenerationReport RunGeneration(
        (SignalSnapshot[] Snaps, float[] Prices, float[] RawVolumes, float[] RawFundingRates)[] windows)
    {
        _observatory.OnEvent(new ObsEvent(
            ObsEventType.GenerationStart, Generation, Guid.Empty,
            $"{{\"pop\":{_population.Count},\"windows\":{windows.Length}}}"));

        if (windows.Length == 1)
        {
            _evaluations = _evaluator.Evaluate(_population,
                windows[0].Snaps, windows[0].Prices,
                windows[0].RawVolumes, windows[0].RawFundingRates, Generation);
        }
        else
        {
            var accumulated = new Dictionary<Guid, List<FitnessBreakdown>>();
            foreach (var (snaps, prices, rawVols, rawFunding) in windows)
            {
                var results = _evaluator.Evaluate(_population, snaps, prices, rawVols, rawFunding, Generation);
                foreach (var (id, result) in results)
                {
                    if (!accumulated.ContainsKey(id))
                        accumulated[id] = [];
                    accumulated[id].Add(result.Fitness);
                }
            }

            _evaluations = new Dictionary<Guid, MarketEvalResult>();
            foreach (var (id, breakdowns) in accumulated)
            {
                var avg = AverageBreakdowns(breakdowns);
                _evaluations[id] = new MarketEvalResult(id, avg);
            }
        }

        // 1b. Apply KNN diversity bonus
        ApplyDiversityBonus();

        // 2. Speciate with dynamic threshold
        var specCfg = new SpeciationConfig(
            C1: 1f, C2: 1f, C3: 0.5f,
            CompatibilityThreshold: _compatibilityThreshold,
            TournamentSize: 3);
        _speciation.Speciate(_population, specCfg);

        // 2b. Adjust compatibility threshold toward target species count
        int specCount = _speciation.Species.Count;
        if (specCount < _config.TargetSpeciesMin)
            _compatibilityThreshold = Math.Max(1.0f, _compatibilityThreshold - _config.CompatibilityAdjustRate);
        else if (specCount > _config.TargetSpeciesMax)
            _compatibilityThreshold = Math.Min(10.0f, _compatibilityThreshold + _config.CompatibilityAdjustRate);

        // 2c. Update elite archive with species champions + stagnation tracking
        foreach (var species in _speciation.Species)
        {
            float bestInSpecies = float.MinValue;
            foreach (var member in species.Members)
            {
                if (_evaluations.TryGetValue(member.GenomeId, out var eval))
                {
                    _archive.Update(species.SpeciesId, member, eval.Fitness.Fitness);
                    if (eval.Fitness.Fitness > bestInSpecies)
                        bestInSpecies = eval.Fitness.Fitness;
                }
            }

            if (bestInSpecies > species.BestFitness)
            {
                species.BestFitness = bestInSpecies;
                species.StagnationCounter = 0;
            }
            else
            {
                species.StagnationCounter++;
            }
        }

        // 3. Update species cumulative P&L for capital allocation
        UpdateSpeciesPnl();

        // 4. Log generation stats
        float lastPrice = windows[^1].Prices[^1];
        var report = BuildReport(lastPrice);

        var specDetails = string.Join(",", _speciation.Species.Select(s =>
            $"{{\"id\":{s.SpeciesId},\"n\":{s.Members.Count},\"best\":{s.BestFitness:F4},\"stag\":{s.StagnationCounter}}}"));

        string brainDiagJson = report.BestBrainTotalEdges > 0
            ? $",\"brainDiag\":{{\"sat\":{report.BestBrainSaturation:F3},\"active\":{report.BestBrainActiveEdges},\"total\":{report.BestBrainTotalEdges}}}"
            : "";

        _observatory.OnEvent(new ObsEvent(
            ObsEventType.GenerationEnd, Generation, report.BestGenomeId,
            $"{{\"best\":{report.BestFitness:F4},\"mean\":{report.MeanFitness:F4},\"median\":{report.MedianFitness:F4}," +
            $"\"species\":{report.SpeciesCount},\"trades\":{report.TotalTrades}," +
            $"\"sharpe\":{report.BestSharpe:F2},\"sortino\":{report.BestSortino:F2}," +
            $"\"cvar5\":{report.BestCVaR5:F4},\"maxDD\":{report.BestMaxDrawdown:F4},\"ddDur\":{report.BestMaxDrawdownDuration:F4}," +
            $"\"inactive\":{report.InactiveCount},\"maxStag\":{report.MaxSpeciesStagnation}," +
            $"\"substrate\":\"{report.BestSubstrate}\"," +
            $"\"archiveSize\":{report.ArchiveSize},\"threshold\":{report.CompatibilityThreshold:F2}," +
            $"\"innovId\":{report.InnovationId},\"shrinkage\":{report.BestShrinkageConfidence:F3}," +
            $"\"speciesDetails\":[{specDetails}]{brainDiagJson}}}"));

        // 5. Reproduce
        _population = Reproduce();
        Generation++;

        _observatory.Flush();
        return report;
    }

    private static FitnessBreakdown AverageBreakdowns(List<FitnessBreakdown> breakdowns)
    {
        int n = breakdowns.Count;
        if (n == 0) return default;
        if (n == 1) return breakdowns[0];

        return new FitnessBreakdown(
            Fitness: breakdowns.Average(b => b.Fitness),
            ReturnPct: breakdowns.Average(b => b.ReturnPct),
            MaxDrawdown: breakdowns.Max(b => b.MaxDrawdown),
            TotalTrades: (int)breakdowns.Average(b => b.TotalTrades),
            WinRate: breakdowns.Average(b => b.WinRate),
            NetPnl: breakdowns.Average(b => b.NetPnl),
            IsActive: breakdowns.Any(b => b.IsActive),
            RawSharpe: breakdowns.Average(b => b.RawSharpe),
            AdjustedSharpe: breakdowns.Average(b => b.AdjustedSharpe),
            Sortino: breakdowns.Average(b => b.Sortino),
            CVaR5: breakdowns.Average(b => b.CVaR5),
            MaxDrawdownDuration: breakdowns.Max(b => b.MaxDrawdownDuration),
            ShrinkageConfidence: breakdowns.Average(b => b.ShrinkageConfidence)
        );
    }

    private void ApplyDiversityBonus()
    {
        if (_config.DiversityBonusScale <= 0f || _population.Count < 2)
            return;

        int k = Math.Min(_config.DiversityKNeighbors, _population.Count - 1);
        if (k <= 0) return;

        var specCfg = new SpeciationConfig(C1: 1f, C2: 1f, C3: 0.5f, CompatibilityThreshold: _compatibilityThreshold, TournamentSize: 3);
        var bonuses = new Dictionary<Guid, float>();

        foreach (var genome in _population)
        {
            var distances = new List<float>();
            foreach (var other in _population)
            {
                if (other.GenomeId == genome.GenomeId) continue;
                distances.Add(genome.DistanceTo(other, specCfg));
            }
            distances.Sort();
            float avgKnn = distances.Take(k).Average();
            bonuses[genome.GenomeId] = avgKnn * _config.DiversityBonusScale;
        }

        foreach (var (id, bonus) in bonuses)
        {
            if (_evaluations.TryGetValue(id, out var eval))
            {
                var f = eval.Fitness;
                var boosted = f with { Fitness = f.Fitness + bonus };
                _evaluations[id] = new MarketEvalResult(id, boosted);
            }
        }
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
            CompatibilityThreshold: _compatibilityThreshold,
            TournamentSize: 3);

        var allocation = _speciation.AllocateOffspring(fitnesses, totalOffspring, popBudget, specCfg, _config.MinOffspringPerSpecies);

        var mutCfg = MutationConfig.Default;
        int childOrdinal = 0;
        var archiveElites = _archive.GetDiverseElites(10);

        foreach (var species in _speciation.Species.OrderBy(s => s.SpeciesId))
        {
            int numOffspring = allocation.GetValueOrDefault(species.SpeciesId, 0);
            if (numOffspring == 0) continue;

            // Stagnation reseeding: replace half of offspring with mutated archive elites
            if (species.StagnationCounter >= _config.StagnationLimit && archiveElites.Count > 0)
            {
                int replaceCount = numOffspring / 2;
                for (int r = 0; r < replaceCount && nextGen.Count < totalOffspring; r++)
                {
                    ulong rseed = SeedDerivation.MutationSeed(
                        _config.RunSeed, Generation, species.SpeciesId, childOrdinal++);
                    var rrng = new Rng64(rseed);
                    var elite = archiveElites[rrng.NextInt(archiveElites.Count)].CloneGenome();
                    var rctx = new MutationContext(_config.RunSeed, Generation, mutCfg, _innovations, rrng);
                    nextGen.Add(elite.Mutate(rctx));
                }
                numOffspring -= replaceCount;
            }

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

        // Fill remaining slots: prefer mutated archive elites over random genomes
        while (nextGen.Count < totalOffspring)
        {
            var rng = new Rng64(SeedDerivation.MutationSeed(
                _config.RunSeed, Generation, 9999, childOrdinal++));

            IGenome fill;
            if (archiveElites.Count > 0)
            {
                fill = archiveElites[rng.NextInt(archiveElites.Count)].CloneGenome();
                var mutCtx = new MutationContext(_config.RunSeed, Generation, mutCfg, _innovations, rng);
                fill = fill.Mutate(mutCtx);
            }
            else
            {
                fill = SeedGenome.CreateRandom(rng);
            }
            nextGen.Add(fill);
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

        var fitnessesSorted = sorted.Select(e => e.Fitness.Fitness).OrderBy(f => f).ToArray();
        float median = fitnessesSorted.Length % 2 == 0
            ? (fitnessesSorted[fitnessesSorted.Length / 2 - 1] + fitnessesSorted[fitnessesSorted.Length / 2]) / 2f
            : fitnessesSorted[fitnessesSorted.Length / 2];

        int inactiveCount = sorted.Count(e => !e.Fitness.IsActive);

        int maxStag = _speciation.Species.Count > 0
            ? _speciation.Species.Max(s => s.StagnationCounter)
            : 0;

        string bestSubstrate = "16x16x3";
        var bestGenome = _population.FirstOrDefault(g => g.GenomeId == best.GenomeId) as SeedGenome;
        if (bestGenome != null)
            bestSubstrate = $"{bestGenome.Dev.SubstrateWidth}x{bestGenome.Dev.SubstrateHeight}x{bestGenome.Dev.SubstrateLayers}";

        int brainActive = 0, brainTotal = 0;
        float brainSat = 0f;
        if (bestGenome != null)
        {
            try
            {
                var diagBudget = MarketEvaluator.MarketBrainBudget with
                {
                    HiddenWidth = bestGenome.Dev.SubstrateWidth,
                    HiddenHeight = bestGenome.Dev.SubstrateHeight,
                    HiddenLayers = bestGenome.Dev.SubstrateLayers
                };
                var diagDev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
                var diagGraph = diagDev.CompileGraph(bestGenome, diagBudget, new DevelopmentContext(_config.RunSeed, Generation));
                var diagBrain = new BrainRuntime(diagGraph, bestGenome.Learn, bestGenome.Stable, 1);
                diagBrain.Step(new float[MarketAgent.InputCount], new BrainStepContext(0));
                var diag = diagBrain.GetDiagnostics();
                brainActive = diag.ActiveEdgeCount;
                brainTotal = diag.TotalEdges;
                brainSat = diag.SaturationRate;
            }
            catch { }
        }

        return new GenerationReport(
            Generation: Generation,
            BestGenomeId: best.GenomeId,
            BestFitness: best.Fitness.Fitness,
            BestReturn: best.Fitness.ReturnPct,
            BestTrades: best.Fitness.TotalTrades,
            BestWinRate: best.Fitness.WinRate,
            BestSharpe: best.Fitness.AdjustedSharpe,
            MeanFitness: mean,
            SpeciesCount: _speciation.Species.Count,
            TotalTrades: totalTrades,
            PopulationSize: _population.Count,
            BestSubstrate: bestSubstrate,
            MedianFitness: median,
            BestSortino: best.Fitness.Sortino,
            BestMaxDrawdown: best.Fitness.MaxDrawdown,
            BestCVaR5: best.Fitness.CVaR5,
            BestMaxDrawdownDuration: best.Fitness.MaxDrawdownDuration,
            BestShrinkageConfidence: best.Fitness.ShrinkageConfidence,
            InactiveCount: inactiveCount,
            MaxSpeciesStagnation: maxStag,
            CompatibilityThreshold: _compatibilityThreshold,
            ArchiveSize: _archive.Count,
            BestBrainActiveEdges: brainActive,
            BestBrainTotalEdges: brainTotal,
            BestBrainSaturation: brainSat,
            InnovationId: _innovations.NextInnovationId);
    }

    public List<(int SpeciesId, string RepresentativeJson, int StagnationCounter, float BestFitness)> GetSpeciesState()
    {
        var result = new List<(int, string, int, float)>();
        foreach (var sp in _speciation.Species)
            result.Add((sp.SpeciesId, sp.Representative.ToJson(), sp.StagnationCounter, sp.BestFitness));
        return result;
    }

    public IGenome? GetBestGenome()
    {
        if (_evaluations.Count == 0) return null;
        var bestId = _evaluations.Values.OrderByDescending(e => e.Fitness.Fitness).First().GenomeId;
        return _population.FirstOrDefault(g => g.GenomeId == bestId);
    }

    public List<int> GetSpeciesIds()
    {
        return _population.Select(g => _speciation.GetSpeciesId(g)).ToList();
    }

    public IReadOnlyList<IGenome> GetSpeciesChampions()
    {
        var champions = new List<IGenome>();
        foreach (var species in _speciation.Species)
        {
            IGenome? best = null;
            float bestFit = float.MinValue;
            foreach (var member in species.Members)
            {
                if (_evaluations.TryGetValue(member.GenomeId, out var eval) && eval.Fitness.Fitness > bestFit)
                {
                    bestFit = eval.Fitness.Fitness;
                    best = member;
                }
            }
            if (best != null) champions.Add(best);
        }
        return champions;
    }
}

public readonly record struct GenerationReport(
    int Generation,
    Guid BestGenomeId,
    float BestFitness,
    float BestReturn,
    int BestTrades,
    float BestWinRate,
    float BestSharpe,
    float MeanFitness,
    int SpeciesCount,
    int TotalTrades,
    int PopulationSize,
    string BestSubstrate = "16x16x3",
    float MedianFitness = 0f,
    float BestSortino = 0f,
    float BestMaxDrawdown = 0f,
    float BestCVaR5 = 0f,
    float BestMaxDrawdownDuration = 0f,
    float BestShrinkageConfidence = 0f,
    int InactiveCount = 0,
    int MaxSpeciesStagnation = 0,
    float CompatibilityThreshold = 3.5f,
    int ArchiveSize = 0,
    int BestBrainActiveEdges = 0,
    int BestBrainTotalEdges = 0,
    float BestBrainSaturation = 0f,
    int InnovationId = 0
);
