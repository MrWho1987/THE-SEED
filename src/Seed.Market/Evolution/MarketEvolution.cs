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

    // V11d Fix 7: in-process stuck detector — rolling 15-gen window of key signals.
    private const int StuckWindowSize = 15;
    private readonly Queue<float> _bestFitnessHistory = new();
    private readonly Queue<int> _populationPosCountHistory = new();
    private readonly Queue<float> _activeBestFitnessHistory = new();
    private bool _stuckWarningEmittedThisRun;

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
    /// Reset all species stagnation state and clear the archive.
    /// Used at pipeline phase transitions so species get a fresh baseline
    /// under the new phase's evaluation criteria.
    /// </summary>
    public void ResetSpeciesStagnation()
    {
        foreach (var species in _speciation.Species)
        {
            species.StagnationCounter = 0;
            species.BestFitness = float.MinValue;
        }
        _archive.Clear();
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
            // V11e: track the last window's full result to preserve OutputObs/CloseReasonCounts
            var lastWindowResults = new Dictionary<Guid, MarketEvalResult>();
            foreach (var (snaps, prices, rawVols, rawFunding) in windows)
            {
                var results = _evaluator.Evaluate(_population, snaps, prices, rawVols, rawFunding, Generation);
                foreach (var (id, result) in results)
                {
                    if (!accumulated.ContainsKey(id))
                        accumulated[id] = [];
                    accumulated[id].Add(result.Fitness);
                    lastWindowResults[id] = result;
                }
            }

            _evaluations = new Dictionary<Guid, MarketEvalResult>();
            foreach (var (id, breakdowns) in accumulated)
            {
                var avg = AverageBreakdowns(breakdowns, _config.WindowConsistencyWeight);
                var last = lastWindowResults[id];
                _evaluations[id] = new MarketEvalResult(id, avg, last.OutputObs, last.CloseReasonCounts);
            }
        }

        // 1b. Apply KNN diversity bonus
        ApplyDiversityBonus();

        // 1c. T3 — Apply behavioral niching bonus (anti-dominance via tanh of distance from
        // population output-mean centroid). Gated by WeightSchedule.BehavioralDiversity at
        // current Generation; no-op when weight is 0.
        ApplyBehavioralNiching();

        // 2. Speciate with dynamic threshold
        var specCfg = new SpeciationConfig(
            C1: 1f, C2: 1f, C3: 0.5f,
            CompatibilityThreshold: _compatibilityThreshold,
            TournamentSize: 3);
        _speciation.Speciate(_population, specCfg);

        // 2b. S4 — Adjust compatibility threshold toward target species count.
        //
        // Old upper bound was hard-coded at 10.0; with TargetSpeciesMax=30 and a fast-mutating
        // population, the threshold pinned at 10 immediately and lost its ability to compress
        // species when needed. New behavior:
        //   - Upper bound is config-driven (CompatibilityThresholdMax, default 30.0).
        //   - Adjust rate halves above 70% of the max (a soft brake) so the threshold settles
        //     instead of overshooting the target.
        //   - Lower bound stays at 1.0 (very tight clusters are pathological).
        int specCount = _speciation.Species.Count;
        float maxThreshold = _config.CompatibilityThresholdMax;
        float adjustRate = _config.CompatibilityAdjustRate;
        if (_compatibilityThreshold > maxThreshold * 0.7f)
            adjustRate *= 0.5f;
        if (specCount < _config.TargetSpeciesMin)
            _compatibilityThreshold = Math.Max(1.0f, _compatibilityThreshold - adjustRate);
        else if (specCount > _config.TargetSpeciesMax)
            _compatibilityThreshold = Math.Min(maxThreshold, _compatibilityThreshold + adjustRate);

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

            // Require minimum improvement to reset stagnation — prevents floating-point
            // drift in overfit champions from indefinitely resetting the counter.
            if (bestInSpecies > species.BestFitness + _config.MinStagnationImprovement)
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

        // V11d Fix 7: in-process stuck detector. Tracks 15-gen rolling window and emits
        // [STUCK-WARN] when ≥2 of 4 stuck signals fire. The external monitor greps for
        // this line to trigger autonomous re-intervention.
        DetectAndReportStuck(report);

        // 5. Reproduce
        _population = Reproduce();
        Generation++;

        _observatory.Flush();
        return report;
    }

    /// <summary>
    /// V11d Fix 7: rolling-window stuck detector. Updates 3 rolling histories and emits
    /// a [STUCK-WARN gen N] line when at least 2 of 4 stuck signals fire over 15 gens.
    /// One warning per "stuck episode" — clears when fitness recovers.
    /// </summary>
    private void DetectAndReportStuck(GenerationReport report)
    {
        // Update rolling histories
        _bestFitnessHistory.Enqueue(report.BestFitness);
        if (_bestFitnessHistory.Count > StuckWindowSize) _bestFitnessHistory.Dequeue();

        var (popPos, _, _) = GetPopulationReturnDistribution();
        _populationPosCountHistory.Enqueue(popPos);
        if (_populationPosCountHistory.Count > StuckWindowSize) _populationPosCountHistory.Dequeue();

        var (activeBest, _, _, _, _) = GetActiveStats();
        float activeBestFitness = activeBest?.Fitness ?? float.NaN;
        _activeBestFitnessHistory.Enqueue(activeBestFitness);
        if (_activeBestFitnessHistory.Count > StuckWindowSize) _activeBestFitnessHistory.Dequeue();

        // Need a full window before evaluating signals
        if (_bestFitnessHistory.Count < StuckWindowSize) return;

        int signalsFired = 0;
        var firedSignalNames = new List<string>();

        // Signal 1: best fitness <= inactivityPenalty for 15 consecutive gens
        if (_bestFitnessHistory.All(f => f <= _config.InactivityPenalty + 0.001f))
        {
            signalsFired++;
            firedSignalNames.Add("best_fitness_at_passive");
        }

        // Signal 2: pos_count == 0 for 15 consecutive gens (no profitable genome anywhere)
        if (_populationPosCountHistory.All(c => c == 0))
        {
            signalsFired++;
            firedSignalNames.Add("zero_profitable_genomes");
        }

        // Signal 3: active_best_fitness < 0 for 15 consecutive gens
        if (_activeBestFitnessHistory.All(f => float.IsNaN(f) || f < 0f))
        {
            signalsFired++;
            firedSignalNames.Add("active_best_negative");
        }

        // Signal 4: best fitness has zero variance (no movement) over 15 gens
        float bestMin = _bestFitnessHistory.Min();
        float bestMax = _bestFitnessHistory.Max();
        if (bestMax - bestMin < 0.001f)
        {
            signalsFired++;
            firedSignalNames.Add("best_fitness_flat");
        }

        bool isCurrentlyStuck = signalsFired >= 2;
        if (isCurrentlyStuck && !_stuckWarningEmittedThisRun)
        {
            Console.Error.WriteLine(
                $"  [STUCK-WARN gen {Generation}] {signalsFired}/4 signals: " +
                $"{string.Join(",", firedSignalNames)} | " +
                $"bestRange=[{bestMin:F4}..{bestMax:F4}] " +
                $"popPosLast={_populationPosCountHistory.Last()}");
            _stuckWarningEmittedThisRun = true;
        }
        else if (!isCurrentlyStuck)
        {
            _stuckWarningEmittedThisRun = false;  // allow re-warning if we get stuck again later
        }
    }

    /// <summary>
    /// Builds the K diverse sub-windows used by multi-window training evaluation. Replicates
    /// the windowing in <c>Program.RunBacktest</c> (~line 237-247) so the analyzer's
    /// MatchTraining mode can produce identical inputs to the in-training fitness pipeline.
    /// Sub-window size = max(50, totalLen / k); offsets selected via RegimeDetector.
    /// </summary>
    public static (SignalSnapshot[] Snaps, float[] Prices, float[] RawVolumes, float[] RawFundingRates)[] BuildEvalWindows(
        SignalSnapshot[] snapshots, float[] prices, float[] rawVolumes, float[] rawFundingRates,
        int k, int generation, ulong runSeed)
    {
        int n = snapshots.Length;
        if (n != prices.Length || n != rawVolumes.Length || n != rawFundingRates.Length)
            throw new ArgumentException("BuildEvalWindows: input arrays must have matching length");
        if (k <= 1)
            return [(snapshots, prices, rawVolumes, rawFundingRates)];

        int subWindowSize = Math.Max(50, n / k);
        var diverse = Backtest.RegimeDetector.SelectDiverseWindows(prices, n, subWindowSize, k, generation, runSeed);
        var windows = new (SignalSnapshot[], float[], float[], float[])[diverse.Length];
        for (int w = 0; w < diverse.Length; w++)
        {
            var (off, len, _) = diverse[w];
            int end = Math.Min(off + len, n);
            if (end - off < 50) { off = 0; end = Math.Min(subWindowSize, n); }
            windows[w] = (snapshots[off..end], prices[off..end], rawVolumes[off..end], rawFundingRates[off..end]);
        }
        return windows;
    }

    /// <summary>
    /// Averages a list of per-window fitness breakdowns and applies the consistency penalty
    /// (subtracts <c>consistencyWeight × stdev(fitness)</c>). Public so the analyzer
    /// (CheckpointEval, MatchTraining mode) can replicate the in-training multi-window
    /// fitness exactly. Mutating the math here changes both training and analyzer in lockstep.
    /// </summary>
    public static FitnessBreakdown AverageBreakdowns(List<FitnessBreakdown> breakdowns, float consistencyWeight)
    {
        int n = breakdowns.Count;
        if (n == 0) return default;
        if (n == 1) return breakdowns[0];

        float meanFitness = breakdowns.Average(b => b.Fitness);

        float adjustedFitness = meanFitness;
        if (consistencyWeight > 0f && n > 1)
        {
            float sumSqDiff = 0f;
            foreach (var b in breakdowns)
            {
                float d = b.Fitness - meanFitness;
                sumSqDiff += d * d;
            }
            float stdFitness = MathF.Sqrt(sumSqDiff / n);
            adjustedFitness = meanFitness - consistencyWeight * stdFitness;
        }

        return new FitnessBreakdown(
            Fitness: adjustedFitness,
            ReturnPct: breakdowns.Average(b => b.ReturnPct),
            MaxDrawdown: breakdowns.Max(b => b.MaxDrawdown),
            TotalTrades: (int)breakdowns.Average(b => b.TotalTrades),
            WinRate: breakdowns.Average(b => b.WinRate),
            NetPnl: breakdowns.Average(b => b.NetPnl),
            IsActive: breakdowns.Any(b => b.IsActive),
            RawSharpe: breakdowns.Average(b => b.RawSharpe),
            AdjustedSharpe: breakdowns.Average(b => b.AdjustedSharpe),
            Sortino: breakdowns.Average(b => b.Sortino),
            AdjustedSortino: breakdowns.Average(b => b.AdjustedSortino),
            CVaR5: breakdowns.Average(b => b.CVaR5),
            MaxDrawdownDuration: breakdowns.Max(b => b.MaxDrawdownDuration),
            ShrinkageConfidence: breakdowns.Average(b => b.ShrinkageConfidence)
        );
    }

    /// <summary>
    /// T3 — Behavioral niching. Penalizes genomes whose output behavior vector
    /// (OutputObs.Means) is too similar to the population centroid. Bonus added per genome:
    /// <c>wBehavDiv × tanh(Euclidean(genome.OutputObs.Means, centroid))</c>. The tanh saturates
    /// the bonus so a single far-out genome doesn't get unbounded credit.
    ///
    /// Only active when the current-generation BehavioralDiversity weight (from
    /// <see cref="MarketConfig.GetWeightsAt"/>) is positive AND population ≥ 2 AND every
    /// genome has a valid OutputObs.
    /// </summary>
    public static (float[] Centroid, int OutputDim) ComputePopulationOutputCentroid(
        IReadOnlyDictionary<Guid, MarketEvalResult> evaluations)
    {
        // Find the first genome with a valid OutputObs to size the centroid array.
        int outputDim = -1;
        foreach (var kv in evaluations)
        {
            var obs = kv.Value.OutputObs;
            if (obs.HasValue && obs.Value.Means != null && obs.Value.Means.Length > 0)
            {
                outputDim = obs.Value.Means.Length;
                break;
            }
        }
        if (outputDim < 0) return (Array.Empty<float>(), 0);

        var sum = new float[outputDim];
        int n = 0;
        foreach (var kv in evaluations)
        {
            var obs = kv.Value.OutputObs;
            if (!obs.HasValue || obs.Value.Means == null || obs.Value.Means.Length != outputDim) continue;
            for (int i = 0; i < outputDim; i++) sum[i] += obs.Value.Means[i];
            n++;
        }
        if (n == 0) return (Array.Empty<float>(), 0);
        for (int i = 0; i < outputDim; i++) sum[i] /= n;
        return (sum, outputDim);
    }

    private void ApplyBehavioralNiching()
    {
        if (_evaluations.Count < 2) return;

        var w = _config.GetWeightsAt(Generation);
        if (w.BehavioralDiversity <= 0f) return;

        var (centroid, outputDim) = ComputePopulationOutputCentroid(_evaluations);
        if (outputDim == 0) return;

        // Compute and apply the bonus per genome.
        var bonuses = new Dictionary<Guid, float>(_evaluations.Count);
        foreach (var (id, eval) in _evaluations)
        {
            var obs = eval.OutputObs;
            if (!obs.HasValue || obs.Value.Means == null || obs.Value.Means.Length != outputDim)
                continue;
            float sumSq = 0f;
            for (int i = 0; i < outputDim; i++)
            {
                float d = obs.Value.Means[i] - centroid[i];
                sumSq += d * d;
            }
            float distance = MathF.Sqrt(sumSq);
            bonuses[id] = w.BehavioralDiversity * MathF.Tanh(distance);
        }

        foreach (var (id, bonus) in bonuses)
        {
            if (bonus == 0f) continue;
            if (_evaluations.TryGetValue(id, out var eval))
            {
                var f = eval.Fitness;
                var boosted = f with { Fitness = f.Fitness + bonus };
                _evaluations[id] = new MarketEvalResult(id, boosted, eval.OutputObs, eval.CloseReasonCounts);
            }
        }
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
            if (_evaluations.TryGetValue(id, out var eval) && eval.Fitness.IsActive)
            {
                var f = eval.Fitness;
                var boosted = f with { Fitness = f.Fitness + bonus };
                // V11e: preserve OutputObs and CloseReasonCounts through diversity bonus
                _evaluations[id] = new MarketEvalResult(id, boosted, eval.OutputObs, eval.CloseReasonCounts);
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

        if (_evaluations.Count == 0)
        {
            // All genomes failed evaluation — return current population unchanged
            return _population.Select(g => g.CloneGenome()).ToList();
        }

        var globalBestEval = _evaluations.Values
            .OrderByDescending(e => e.Fitness.Fitness)
            .First();
        nextGen.Add(_population.First(g => g.GenomeId == globalBestEval.GenomeId).CloneGenome());

        var popBudget = new PopulationBudget(
            PopulationSize: _config.PopulationSize,
            ArenaRounds: 1,
            ElitesPerSpecies: 1,
            MinSpeciesSizeForElitism: _config.MinSpeciesSizeForElitism);

        var specCfg = new SpeciationConfig(
            C1: 1f, C2: 1f, C3: 0.5f,
            CompatibilityThreshold: _compatibilityThreshold,
            TournamentSize: 3);

        var allocation = _speciation.AllocateOffspring(fitnesses, totalOffspring - 1, popBudget, specCfg, _config.MinOffspringPerSpecies);

        var mutCfg = MutationConfig.Default;
        int childOrdinal = 0;
        var archiveElites = _archive.GetDiverseElites(10);

        foreach (var species in _speciation.Species.OrderBy(s => s.SpeciesId))
        {
            int numOffspring = allocation.GetValueOrDefault(species.SpeciesId, 0);
            if (numOffspring == 0) continue;

            // Stagnation reseeding: replace half of offspring with fresh genetic material
            if (species.StagnationCounter >= _config.StagnationLimit)
            {
                int replaceCount = numOffspring / 2;
                bool archiveDegenerate = archiveElites.Count == 0 ||
                    _archive.Champions.Count == 0 ||
                    _archive.Champions.Values.Max(c => c.Fitness) <= _config.InactivityPenalty + 0.001f;

                for (int r = 0; r < replaceCount && nextGen.Count < totalOffspring; r++)
                {
                    ulong rseed = SeedDerivation.MutationSeed(
                        _config.RunSeed, Generation, species.SpeciesId, childOrdinal++);
                    var rrng = new Rng64(rseed);

                    if (archiveDegenerate)
                    {
                        nextGen.Add(SeedGenome.CreateRandom(rrng));
                    }
                    else
                    {
                        var elite = archiveElites[rrng.NextInt(archiveElites.Count)].CloneGenome();
                        var rctx = new MutationContext(_config.RunSeed, Generation, mutCfg, _innovations, rrng);
                        nextGen.Add(elite.Mutate(rctx));
                    }
                }
                numOffspring -= replaceCount;

                // Reset stagnation so reseeded species gets a fresh baseline
                // under current evaluation criteria
                species.StagnationCounter = 0;
                species.BestFitness = float.MinValue;
            }

            var sortedMembers = species.Members
                .OrderByDescending(g => fitnesses.GetValueOrDefault(g.GenomeId, 0f))
                .ToList();

            // Elites
            if (sortedMembers.Count >= _config.MinSpeciesSizeForElitism)
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
        if (candidates.Count == 0) throw new ArgumentException("TournamentSelect requires at least 1 candidate");
        if (candidates.Count == 1) return candidates[0];
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
        if (sorted.Length == 0)
        {
            return new GenerationReport(
                Generation: Generation, BestGenomeId: Guid.Empty,
                BestFitness: 0f, BestReturn: 0f, BestTrades: 0, BestWinRate: 0f, BestSharpe: 0f,
                MeanFitness: 0f, SpeciesCount: _speciation.Species.Count, TotalTrades: 0,
                PopulationSize: _population.Count, InactiveCount: _population.Count);
        }
        var best = sorted[0];
        float mean = sorted.Average(e => e.Fitness.Fitness);
        int totalTrades = sorted.Sum(e => e.Fitness.TotalTrades);

        var fitnessesSorted = sorted.Select(e => e.Fitness.Fitness).OrderBy(f => f).ToArray();
        float median = fitnessesSorted.Length % 2 == 0
            ? (fitnessesSorted[fitnessesSorted.Length / 2 - 1] + fitnessesSorted[fitnessesSorted.Length / 2]) / 2f
            : fitnessesSorted[fitnessesSorted.Length / 2];

        int inactiveCount = sorted.Count(e => !e.Fitness.IsActive);

        var tradeCounts = sorted.Select(e => e.Fitness.TotalTrades).OrderBy(t => t).ToArray();
        float medianTrades = tradeCounts.Length % 2 == 0
            ? (tradeCounts[tradeCounts.Length / 2 - 1] + tradeCounts[tradeCounts.Length / 2]) / 2f
            : tradeCounts[tradeCounts.Length / 2];
        int tradingAgentCount = tradeCounts.Count(t => t > 0);
        int maxTradesPerAgent = tradeCounts.Length > 0 ? tradeCounts[^1] : 0;

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
                var diagGraph = diagDev.CompileGraph(bestGenome, diagBudget, new DevelopmentContext(_config.RunSeed, Generation),
                    MarketEvaluator.SignalCategoryMap, MarketEvaluator.RegimeStart, MarketEvaluator.RegimeEnd);
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
            InnovationId: _innovations.NextInnovationId,
            MedianTradesPerAgent: medianTrades,
            TradingAgentCount: tradingAgentCount,
            MaxTradesPerAgent: maxTradesPerAgent,
            WorstFitness: fitnessesSorted[0],
            NaNFitnessCount: sorted.Count(e => float.IsNaN(e.Fitness.RawSharpe) || float.IsNaN(e.Fitness.Fitness)));
    }

    public List<(int SpeciesId, string RepresentativeJson, int StagnationCounter, float BestFitness)> GetSpeciesState()
    {
        var result = new List<(int, string, int, float)>();
        foreach (var sp in _speciation.Species)
            result.Add((sp.SpeciesId, sp.Representative.ToJson(), sp.StagnationCounter, sp.BestFitness));
        return result;
    }

    public (FitnessBreakdown? Best, int PosCount, int NegCount, float MinRet, float MaxRet) GetActiveStats()
    {
        var active = _evaluations.Values
            .Where(e => e.Fitness.TotalTrades > 0)
            .ToList();
        if (active.Count == 0) return (null, 0, 0, 0, 0);
        var best = active.OrderByDescending(e => e.Fitness.Fitness).First();
        int pos = active.Count(e => e.Fitness.ReturnPct > 0);
        int neg = active.Count(e => e.Fitness.ReturnPct <= 0);
        float minRet = active.Min(e => e.Fitness.ReturnPct);
        float maxRet = active.Max(e => e.Fitness.ReturnPct);
        return (best.Fitness, pos, neg, minRet, maxRet);
    }

    /// <summary>
    /// V11d Fix 7: Population-wide return distribution (counts profitable / breakeven /
    /// losing genomes across the entire population, not just active). The earliest signal
    /// of discovery is when `pos > 0` even before the best fitness column moves.
    /// </summary>
    public (int Pos, int Zero, int Neg) GetPopulationReturnDistribution()
    {
        int pos = 0, zero = 0, neg = 0;
        foreach (var eval in _evaluations.Values)
        {
            float r = eval.Fitness.ReturnPct;
            if (r > 0.0001f) pos++;
            else if (r < -0.0001f) neg++;
            else zero++;
        }
        return (pos, zero, neg);
    }

    /// <summary>
    /// V11d Fix 7: Get the best genome's evaluation result (with OutputObservation and
    /// CloseReasonCounts attached). Used to print observability lines for the dominant
    /// strategy each generation.
    /// </summary>
    public MarketEvalResult? GetBestEvalResult()
    {
        if (_evaluations.Count == 0) return null;
        return _evaluations.Values.OrderByDescending(e => e.Fitness.Fitness).First();
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

    /// <summary>
    /// B5 — Returns up to N genomes from the current population ordered by training fitness
    /// (highest first). Used by walk-forward testing to evaluate multiple candidates against
    /// the validation window, not just the single top-training-fit genome.
    /// Matches genomes that exist in BOTH _population and _evaluations — this is the set
    /// of live genomes whose fitness was measured in the latest evaluation.
    /// </summary>
    public List<IGenome> GetTopNByTrainingFitness(int n)
    {
        if (n <= 0 || _evaluations.Count == 0 || _population.Count == 0)
            return new List<IGenome>();

        // Build (genome, fitness) pairs only for population members that have an evaluation.
        // Genomes newly created by Reproduce() may not yet appear in _evaluations — exclude them.
        // Deduplicate by GenomeId since elitism can copy the same genome into multiple slots.
        var ranked = new List<(IGenome Genome, float Fitness)>();
        var seen = new HashSet<Guid>();
        foreach (var g in _population)
        {
            if (!seen.Add(g.GenomeId)) continue;
            if (_evaluations.TryGetValue(g.GenomeId, out var eval))
                ranked.Add((g, eval.Fitness.Fitness));
        }

        return ranked
            .OrderByDescending(x => x.Fitness)
            .Take(n)
            .Select(x => x.Genome)
            .ToList();
    }

    /// <summary>
    /// B4 — Replace the lowest-fitness member of the population with the provided genome.
    /// Used to protect validation-best genomes from evolutionary loss between WF checks.
    /// Returns true if injection succeeded. The next speciation step will reassign the
    /// injected genome to the appropriate species (first-inserted representative policy).
    /// Strategy: find the worst POPULATION MEMBER whose fitness is known (has an entry
    /// in _evaluations). If no overlap (e.g., called right after Reproduce when all
    /// offspring are fresh), fall back to replacing the last position. Either way, the
    /// injected genome will be evaluated on the next RunGeneration and speciated normally.
    ///
    /// CALLER RESPONSIBILITY: the provided genome's GenomeId must NOT already be present
    /// in the population. Duplicate IDs would silently corrupt _evaluations (Dictionary
    /// keyed by GenomeId — last-write-wins) and Reproduce's elitism step (would dedupe
    /// implicitly). For protection-clone usage, derive a fresh GenomeId via SeedDerivation
    /// before calling this method. Throws InvalidOperationException on duplicate.
    /// </summary>
    public bool InjectGenomeIntoPopulation(IGenome genome)
    {
        if (genome == null || _population.Count == 0) return false;

        // Defensive guard: surface duplicate-ID bugs loudly instead of silently corrupting state.
        for (int i = 0; i < _population.Count; i++)
        {
            if (_population[i].GenomeId == genome.GenomeId)
                throw new InvalidOperationException(
                    $"InjectGenomeIntoPopulation: refusing to inject genome with duplicate GenomeId {genome.GenomeId}. " +
                    "Caller must derive a fresh GenomeId (e.g., via SeedDerivation) before injection.");
        }

        // Try 1: find worst-by-training-fit member that's still in _population
        int targetIdx = -1;
        float worstFit = float.MaxValue;
        for (int i = 0; i < _population.Count; i++)
        {
            if (_evaluations.TryGetValue(_population[i].GenomeId, out var eval))
            {
                if (eval.Fitness.Fitness < worstFit)
                {
                    worstFit = eval.Fitness.Fitness;
                    targetIdx = i;
                }
            }
        }

        // Try 2 (fallback): no population members have evaluations (fresh post-Reproduce) — use last slot
        if (targetIdx < 0)
            targetIdx = _population.Count - 1;

        _population[targetIdx] = genome;
        return true;
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
    int InnovationId = 0,
    float MedianTradesPerAgent = 0f,
    int TradingAgentCount = 0,
    int MaxTradesPerAgent = 0,
    float WorstFitness = 0f,
    int NaNFitnessCount = 0
);
