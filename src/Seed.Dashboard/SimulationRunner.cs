using Microsoft.AspNetCore.SignalR;
using Seed.Brain;
using Seed.Core;
using Seed.Dashboard.Hubs;
using Seed.Dashboard.Models;
using Seed.Development;
using Seed.Genetics;
using Seed.Worlds;
using Seed.Agents;

namespace Seed.Dashboard;

internal sealed class AgentEvalState
{
    public SeedGenome Genome { get; set; } = null!;
    public BrainGraph Brain { get; set; } = null!;
    public BrainRuntime Runtime { get; set; } = null!;
    public AgentBody Body { get; set; } = null!;
    public int CurrentTick { get; set; }

    // Per-episode tracking
    public int SurvivalTicks { get; set; }
    public float NetEnergyDelta { get; set; }
    public int FoodCollected { get; set; }
    public float EnergySpent { get; set; }
    public float InstabilitySum { get; set; }
    public float DistanceTraveled { get; set; }
    public float PrevX { get; set; }
    public float PrevY { get; set; }
    public List<EpisodeMetrics> RoundMetrics { get; set; } = new();
    public FitnessAggregate AggregatedFitness { get; set; }
}

public sealed class SimulationRunner : BackgroundService
{
    private readonly IHubContext<SimulationHub> _hub;
    private readonly ILogger<SimulationRunner> _logger;

    private RunConfig _config = RunConfig.Default;
    private readonly List<AgentEvalState> _agentStates = new();
    private BrainDeveloper _developer = null!;
    private InnovationTracker _innovations = null!;
    private SpeciationManager _speciation = new();

    private SharedArena _arena = null!;
    private AgentView[] _views = Array.Empty<AgentView>();
    private int _currentRound;
    private WorldBudget _currentWorldBudget;

    private int _selectedAgentIndex = 0;
    private int _generation;
    private float[] _selectedAgentModulators = new float[ModulatorIndex.Count];

    private volatile bool _isRunning;
    private volatile bool _isPaused = true;
    private volatile float _speed = 1.0f;
    private volatile bool _stepRequested;
    private readonly object _lock = new();

    private readonly List<GenerationStatsDto> _generationHistory = new();

    private readonly List<WorldFrameDto> _replayBuffer = new();
    private bool _isRecording;
    private const int MaxReplayFrames = 5000;

    private int _ticksSinceFullBrainUpdate = 0;
    private int _ticksSinceFrameUpdate = 0;
    private int _lastBrainAgentIndex = -1;
    private const int FullBrainUpdateInterval = 300;
    private const int FrameUpdateInterval = 3;

    private volatile bool _historyUpdated = false;

    private WorldOverrideDto? _worldOverrides;
    private bool _overridesActive;

    private static readonly AgentConfig AgentCfg = AgentConfig.Default;

    private static readonly AllBudgets DashboardBudgets = new(
        Development: new DevelopmentBudget(12, 12, 2, 12, 16, 2, 16, 5),
        Runtime: new RuntimeBudget(1500, 3),
        Population: new PopulationBudget(32, 4, 1, 5),
        World: new WorldBudget(
            WorldWidth: 64,
            WorldHeight: 64,
            ObstacleDensity: 0.08f,
            HazardDensity: 0.03f,
            FoodCount: 20,
            FoodClusters: 3,
            FoodEnergyAmplitude: 0.4f,
            FoodEnergyPeriod: 500,
            RoundJitter: 0.15f,
            DayNightPeriod: 150,
            SeasonPeriod: 1500,
            AmbientEnergyRate: 0.00015f,
            CorpseEnergyBase: 0.3f,
            FoodQualityVariation: 0.1f
        ),
        Compute: new ComputeBudget(0)
    );

    public SimulationRunner(IHubContext<SimulationHub> hub, ILogger<SimulationRunner> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public bool IsRunning => _isRunning;
    public bool IsPaused => _isPaused;
    public float Speed => _speed;
    public int CurrentGeneration => _generation;
    public int CurrentTick => _agentStates.Count > 0 ? _agentStates[0].CurrentTick : 0;
    public int SelectedAgentIndex => _selectedAgentIndex;
    public IReadOnlyList<GenerationStatsDto> GenerationHistory
    {
        get { lock (_lock) { return _generationHistory.ToArray(); } }
    }

    public void Initialize(RunConfig? config = null)
    {
        lock (_lock)
        {
            _config = config ?? RunConfig.Default with { Budgets = DashboardBudgets };

            if (_config.Budgets.World.WorldWidth <= 0 || _config.Budgets.Population.PopulationSize <= 0)
                _config = _config with { Budgets = DashboardBudgets };

            _agentStates.Clear();
            _generationHistory.Clear();
            _speciation = new SpeciationManager();
            _generation = 0;
            _selectedAgentIndex = 0;
            _currentRound = 0;

            int popSize = _config.Budgets.Population.PopulationSize;

            _developer = new BrainDeveloper(AgentCfg.TotalSensorCount, ContinuousWorld.ActuatorCount);
            _innovations = InnovationTracker.CreateDefault();

            for (int i = 0; i < popSize; i++)
            {
                var rng = new Rng64(SeedDerivation.AgentSeed(_config.RunSeed, 0, i));
                var genome = SeedGenome.CreateRandom(rng);
                var devCtx = new DevelopmentContext(_config.RunSeed, 0);
                var brain = _developer.CompileGraph(genome, _config.Budgets.Development, devCtx);
                _agentStates.Add(new AgentEvalState { Genome = genome, Brain = brain });
            }

            InitializeRound(0);
            _isRunning = true;
            _logger.LogInformation("Arena evolution initialized with {Pop} agents", popSize);
        }
    }

    private void InitializeRound(int round)
    {
        _currentRound = round;
        int n = _agentStates.Count;
        ulong arenaSeed = SeedDerivation.WorldSeed(_config.RunSeed, _generation, round, 0);

        var worldBudget = _config.Budgets.World;
        if (worldBudget.RoundJitter > 0f)
        {
            var jitterRng = new Rng64(arenaSeed ^ 0xB00B1E5);
            worldBudget = worldBudget.Jitter(ref jitterRng);
        }
        _currentWorldBudget = worldBudget;

        _arena = new SharedArena();
        _arena.Reset(arenaSeed, worldBudget, n);

        _views = new AgentView[n];
        for (int i = 0; i < n; i++)
        {
            var state = _agentStates[i];
            _views[i] = new AgentView(_arena, i);
            state.Body = new AgentBody(_views[i]);
            state.Runtime = new BrainRuntime(
                state.Brain, state.Genome.Learn, state.Genome.Stable,
                _config.Budgets.Runtime.MicroStepsPerTick);
            state.Body.Reset(new BodyResetContext(arenaSeed ^ (ulong)i));
            state.Runtime.Reset();
            state.CurrentTick = 0;
            state.SurvivalTicks = 0;
            state.NetEnergyDelta = 0f;
            state.FoodCollected = 0;
            state.EnergySpent = 0f;
            state.InstabilitySum = 0f;
            state.DistanceTraveled = 0f;
            state.PrevX = _arena.AgentX(i);
            state.PrevY = _arena.AgentY(i);
            if (round == 0)
                state.RoundMetrics.Clear();
        }

        if (_overridesActive && _worldOverrides != null)
            ApplyOverridesToArena();
    }

    public void Play() { _isPaused = false; }
    public void Pause() { _isPaused = true; }
    public void Step() { _stepRequested = true; }
    public void SetSpeed(float speed) { _speed = Math.Clamp(speed, 0.1f, 100f); }

    public void SelectAgent(int index)
    {
        lock (_lock)
        {
            if (index >= 0 && index < _agentStates.Count)
                _selectedAgentIndex = index;
        }
    }

    public void Reset() { Initialize(_config); }

    public void ApplyWorldOverride(WorldOverrideDto dto)
    {
        lock (_lock)
        {
            _worldOverrides = dto;
            _overridesActive = dto.FoodCount != null || dto.AmbientEnergyRate != null
                || dto.CorpseEnergyBase != null || dto.DayNightPeriod != null
                || dto.SeasonPeriod != null || dto.HazardDamageMultiplier != null
                || dto.FoodQualityVariation != null || dto.LightLevelOverride != null;
            ApplyOverridesToArena();
        }
    }

    public WorldOverrideDto? GetWorldOverrides()
    {
        lock (_lock) { return _worldOverrides; }
    }

    public void ClearWorldOverride()
    {
        lock (_lock)
        {
            _worldOverrides = null;
            _overridesActive = false;
            if (_arena != null)
            {
                _arena.SetBudget(_currentWorldBudget);
                _arena.HazardDamageMultiplier = 1.0f;
                _arena.LightLevelOverride = null;
            }
        }
    }

    private void ApplyOverridesToArena()
    {
        if (_arena == null || _worldOverrides == null) return;

        var dto = _worldOverrides;
        var b = _currentWorldBudget;

        var newBudget = b with
        {
            AmbientEnergyRate = dto.AmbientEnergyRate ?? b.AmbientEnergyRate,
            CorpseEnergyBase = dto.CorpseEnergyBase ?? b.CorpseEnergyBase,
            DayNightPeriod = dto.DayNightPeriod ?? b.DayNightPeriod,
            SeasonPeriod = dto.SeasonPeriod ?? b.SeasonPeriod,
            FoodQualityVariation = dto.FoodQualityVariation ?? b.FoodQualityVariation
        };
        _arena.SetBudget(newBudget);

        _arena.HazardDamageMultiplier = dto.HazardDamageMultiplier ?? 1.0f;
        _arena.LightLevelOverride = dto.LightLevelOverride;

        if (dto.FoodCount.HasValue)
            _arena.AdjustFoodCount(dto.FoodCount.Value);
    }

    public void StartRecording()
    {
        lock (_lock) { _replayBuffer.Clear(); _isRecording = true; }
    }

    public List<WorldFrameDto> StopRecording()
    {
        lock (_lock) { _isRecording = false; return new List<WorldFrameDto>(_replayBuffer); }
    }

    public IReadOnlyList<WorldFrameDto> GetReplayBuffer()
    {
        lock (_lock) { return new List<WorldFrameDto>(_replayBuffer); }
    }
    public bool IsRecording { get { lock (_lock) { return _isRecording; } } }

    public SimulationStatusDto GetStatus()
    {
        lock (_lock)
        {
            int aliveCount = _arena != null
                ? Enumerable.Range(0, _arena.AgentCount).Count(i => _arena.AgentAlive(i))
                : 0;

            return new SimulationStatusDto(
                IsRunning: _isRunning,
                IsPaused: _isPaused,
                CurrentGeneration: _generation,
                CurrentTick: CurrentTick,
                CurrentRound: _currentRound,
                Speed: _speed,
                PopulationSize: _agentStates.Count,
                SpeciesCount: _speciation.Species.Count,
                AliveCount: aliveCount,
                MaxTicksPerRound: _config.Budgets.Runtime.MaxTicksPerEpisode,
                ArenaRounds: _config.Budgets.Population.ArenaRounds,
                OverridesActive: _overridesActive
            );
        }
    }

    public WorldFrameDto? GetCurrentFrame()
    {
        lock (_lock)
        {
            if (_arena == null || _agentStates.Count == 0) return null;
            return BuildWorldFrame();
        }
    }

    public BrainSnapshotDto? GetSelectedBrainSnapshot()
    {
        lock (_lock)
        {
            if (_selectedAgentIndex >= _agentStates.Count) return null;
            var state = _agentStates[_selectedAgentIndex];
            if (state.Runtime == null) return null;
            return BuildBrainSnapshot(_selectedAgentIndex, state.Brain, state.Runtime);
        }
    }

    public float[]? GetBrainActivations()
    {
        lock (_lock)
        {
            if (_selectedAgentIndex >= _agentStates.Count) return null;
            return _agentStates[_selectedAgentIndex].Runtime?.GetActivations().ToArray();
        }
    }

    private SelectedAgentDetailsDto? BuildSelectedAgentDetails()
    {
        lock (_lock)
        {
            if (_selectedAgentIndex < 0 || _selectedAgentIndex >= _agentStates.Count)
                return null;

            var state = _agentStates[_selectedAgentIndex];
            var genome = state.Genome;
            int hiddenNodes = genome.Cppn.Nodes.Count(n => n.Type == CppnNodeType.Hidden);

            float instPen = state.SurvivalTicks > 0
                ? state.InstabilitySum / state.SurvivalTicks : 0f;

            var roundHistory = state.RoundMetrics
                .Select((m, idx) => new RoundMetricsDto(
                    idx, m.SurvivalTicks, m.NetEnergyDelta,
                    m.FoodCollected, m.DistanceTraveled, m.Fitness))
                .ToArray();

            return new SelectedAgentDetailsDto(
                AgentId: _selectedAgentIndex,
                ConnectionCount: genome.Cppn.Connections.Count,
                HiddenNodeCount: hiddenNodes,
                TotalNodeCount: genome.Cppn.Nodes.Count,
                SurvivalTicks: state.SurvivalTicks,
                FoodCollected: state.FoodCollected,
                NetEnergyDelta: state.NetEnergyDelta,
                DistanceTraveled: state.DistanceTraveled,
                InstabilityPenalty: instPen,
                ModReward: _selectedAgentModulators[ModulatorIndex.Reward],
                ModPain: _selectedAgentModulators[ModulatorIndex.Pain],
                ModCuriosity: _selectedAgentModulators[ModulatorIndex.Curiosity],
                RoundHistory: roundHistory,
                AggregatedFitness: state.RoundMetrics.Count >= _config.Budgets.Population.ArenaRounds
                    ? state.AggregatedFitness.Score
                    : (float?)null
            );
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Initialize();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_isRunning)
                {
                    await Task.Delay(100, stoppingToken);
                    continue;
                }

                bool shouldStep = !_isPaused || _stepRequested;
                _stepRequested = false;

                if (shouldStep)
                {
                    lock (_lock)
                    {
                        StepArena();
                    }
                    _ticksSinceFrameUpdate++;
                    _ticksSinceFullBrainUpdate++;
                }

                if (_ticksSinceFrameUpdate >= FrameUpdateInterval)
                {
                    _ticksSinceFrameUpdate = 0;

                    var frame = GetCurrentFrame();
                    if (frame != null)
                        await _hub.Clients.All.SendAsync("WorldFrame", frame, stoppingToken);

                    var activations = GetBrainActivations();
                    if (activations != null)
                        await _hub.Clients.All.SendAsync("BrainActivations", activations, stoppingToken);

                    var agentDetails = BuildSelectedAgentDetails();
                    if (agentDetails != null)
                        await _hub.Clients.All.SendAsync("SelectedAgentDetails", agentDetails, stoppingToken);

                    await _hub.Clients.All.SendAsync("Status", GetStatus(), stoppingToken);
                }

                bool agentChanged = _selectedAgentIndex != _lastBrainAgentIndex;
                if (agentChanged || _ticksSinceFullBrainUpdate >= FullBrainUpdateInterval)
                {
                    _ticksSinceFullBrainUpdate = 0;
                    _lastBrainAgentIndex = _selectedAgentIndex;
                    var brain = GetSelectedBrainSnapshot();
                    if (brain != null)
                        await _hub.Clients.All.SendAsync("BrainSnapshot", brain, stoppingToken);
                }

                if (_historyUpdated)
                {
                    _historyUpdated = false;
                    GenerationStatsDto[] historyCopy;
                    lock (_lock) { historyCopy = _generationHistory.ToArray(); }
                    await _hub.Clients.All.SendAsync("GenerationHistory", historyCopy, stoppingToken);
                }

                int delayMs = (int)(33 / _speed);
                delayMs = Math.Max(1, Math.Min(1000, delayMs));
                await Task.Delay(delayMs, stoppingToken);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in simulation loop");
                await Task.Delay(1000, stoppingToken);
            }
        }
    }

    private void StepArena()
    {
        int n = _agentStates.Count;
        int actCount = ContinuousWorld.ActuatorCount;
        var allActions = new float[n][];
        var curiosities = new float[n];

        for (int i = 0; i < n; i++)
        {
            allActions[i] = new float[actCount];
            if (!_arena.AgentAlive(i)) continue;

            var state = _agentStates[i];
            var sensorBuf = new float[state.Body.SensorCount];
            state.Body.ReadSensors(sensorBuf);

            curiosities[i] = state.CurrentTick > 0
                ? state.Body.ComputeCuriosity(sensorBuf) : 0f;

            var outputs = state.Runtime.Step(sensorBuf, new BrainStepContext(state.CurrentTick));
            for (int a = 0; a < actCount && a < outputs.Length; a++)
                allActions[i][a] = outputs[a];
        }

        var results = _arena.StepAll(allActions);

        for (int i = 0; i < n; i++)
        {
            var state = _agentStates[i];
            results[i].Modulators[ModulatorIndex.Curiosity] = curiosities[i];
            if (i == _selectedAgentIndex)
                Array.Copy(results[i].Modulators, _selectedAgentModulators, Math.Min(results[i].Modulators.Length, ModulatorIndex.Count));
            state.Runtime.Learn(results[i].Modulators, new BrainLearnContext(state.CurrentTick));
            state.Body.ApplyWorldSignals(results[i].Signals);

            state.CurrentTick++;
            if (_arena.AgentAlive(i))
            {
                state.SurvivalTicks++;
                float dx = _arena.AgentX(i) - state.PrevX;
                float dy = _arena.AgentY(i) - state.PrevY;
                state.DistanceTraveled += MathF.Sqrt(dx * dx + dy * dy);
                state.PrevX = _arena.AgentX(i);
                state.PrevY = _arena.AgentY(i);
            }
            state.FoodCollected += results[i].Signals.FoodCollectedThisStep;
            state.NetEnergyDelta += results[i].Signals.EnergyDelta;
            if (results[i].Signals.EnergyDelta < 0)
                state.EnergySpent += -results[i].Signals.EnergyDelta;
            state.InstabilitySum += state.Runtime.GetInstabilityPenalty();
        }

        if (_isRecording && _replayBuffer.Count < MaxReplayFrames)
            _replayBuffer.Add(BuildWorldFrame());

        bool allDead = n == 0 || !Enumerable.Range(0, n).Any(i => _arena.AgentAlive(i));
        int tick = n > 0 ? _agentStates[0].CurrentTick : 0;
        if (allDead || tick >= _config.Budgets.Runtime.MaxTicksPerEpisode)
            CompleteRound();
    }

    private void CompleteRound()
    {
        int n = _agentStates.Count;
        for (int i = 0; i < n; i++)
        {
            var state = _agentStates[i];
            float instPen = state.SurvivalTicks > 0 ? state.InstabilitySum / state.SurvivalTicks : 0f;
            float fitness = DeterministicHelpers.ComputeEpisodeFitness(
                state.SurvivalTicks, state.NetEnergyDelta, state.FoodCollected,
                state.EnergySpent, instPen, state.DistanceTraveled,
                _config.Budgets.Runtime.MaxTicksPerEpisode);
            state.RoundMetrics.Add(new EpisodeMetrics(
                state.SurvivalTicks, state.NetEnergyDelta, state.FoodCollected,
                state.EnergySpent, instPen, state.DistanceTraveled, fitness));
        }

        _currentRound++;
        if (_currentRound < _config.Budgets.Population.ArenaRounds)
        {
            InitializeRound(_currentRound);
        }
        else
        {
            CompleteGeneration();
        }
    }

    private void CompleteGeneration()
    {
        var fitCfg = _config.Fitness;
        for (int i = 0; i < _agentStates.Count; i++)
        {
            var state = _agentStates[i];
            state.AggregatedFitness = DeterministicHelpers.AggregateFitness(
                state.RoundMetrics.ToArray(), fitCfg.LambdaVar, fitCfg.LambdaWorst);
        }

        if (_agentStates.Count == 0)
        {
            _logger.LogWarning("No agents to evolve, skipping generation");
            return;
        }

        var scores = _agentStates.Select(a => a.AggregatedFitness.Score).OrderByDescending(f => f).ToList();
        float bestFitness = scores[0];
        float worstFitness = scores[^1];
        float meanFitness = scores.Average();

        var genomes = _agentStates.Select(a => (IGenome)a.Genome).ToList();
        _speciation.Speciate(genomes, _config.Speciation);

        var fitnessLookup = _agentStates
            .GroupBy(a => a.Genome.GenomeId)
            .ToDictionary(g => g.Key, g => g.First().AggregatedFitness.Score);

        var speciesBreakdown = _speciation.Species
            .Select(sp => new SpeciesInfoDto(
                sp.SpeciesId,
                sp.Members.Count,
                sp.Members.Count > 0
                    ? (float)sp.Members.Average(m => fitnessLookup.GetValueOrDefault(m.GenomeId, 0f))
                    : 0f))
            .ToArray();

        int modulatoryEdgeCount = 0;
        float avgDelay = 0f;
        var bestAgent = _agentStates.OrderByDescending(a => a.AggregatedFitness.Score).First();
        var allEdges = bestAgent.Brain.IncomingByDst.Values.SelectMany(e => e).ToList();
        if (allEdges.Count > 0)
        {
            modulatoryEdgeCount = allEdges.Count(e => e.Meta.EdgeType == Brain.EdgeType.Modulatory);
            avgDelay = (float)allEdges.Average(e => e.Meta.Delay);
        }

        float avgDist = (float)_agentStates.Average(a =>
            a.RoundMetrics.Count > 0 ? a.RoundMetrics.Average(m => m.DistanceTraveled) : 0.0);
        float avgFood = (float)_agentStates.Average(a =>
            a.RoundMetrics.Count > 0 ? a.RoundMetrics.Average(m => (double)m.FoodCollected) : 0.0);
        float avgSurv = (float)_agentStates.Average(a =>
            a.RoundMetrics.Count > 0 ? a.RoundMetrics.Average(m => (double)m.SurvivalTicks) : 0.0);

        var stats = new GenerationStatsDto(
            Generation: _generation,
            BestFitness: bestFitness,
            MeanFitness: meanFitness,
            WorstFitness: worstFitness,
            SpeciesCount: _speciation.Species.Count,
            PopulationSize: _agentStates.Count,
            ModulatoryEdgeCount: modulatoryEdgeCount,
            AvgDelay: avgDelay,
            AvgDistanceTraveled: avgDist,
            AvgFoodCollected: avgFood,
            AvgSurvivalTicks: avgSurv,
            SpeciesBreakdown: speciesBreakdown
        );
        _generationHistory.Add(stats);
        _historyUpdated = true;

        _logger.LogInformation("Generation {Gen} complete. Best: {Best:F1}, Mean: {Mean:F1}, Species: {Species}",
            _generation, bestFitness, meanFitness, _speciation.Species.Count);

        EvolvePopulation(fitnessLookup);
        _generation++;
        InitializeRound(0);
    }

    private void EvolvePopulation(Dictionary<Guid, float> fitnessLookup)
    {
        int popSize = _agentStates.Count;
        int elitesPerSpecies = _config.Budgets.Population.ElitesPerSpecies;
        int minSpeciesSize = _config.Budgets.Population.MinSpeciesSizeForElitism;
        int tournamentSize = _config.Speciation.TournamentSize;

        var offspringAlloc = _speciation.AllocateOffspring(fitnessLookup, popSize, _config.Budgets.Population, _config.Speciation);

        var newGenomes = new List<SeedGenome>();
        var newBrains = new List<BrainGraph>();
        int childOrdinal = 0;

        foreach (var species in _speciation.Species.OrderBy(s => s.SpeciesId))
        {
            var members = species.Members
                .Select(g => (SeedGenome)g)
                .OrderByDescending(g => fitnessLookup[g.GenomeId])
                .ToList();

            int offspring = offspringAlloc.GetValueOrDefault(species.SpeciesId, 0);
            if (offspring == 0) continue;

            int elitesCopied = 0;
            if (members.Count >= minSpeciesSize)
            {
                for (int i = 0; i < elitesPerSpecies && i < members.Count && elitesCopied < offspring; i++)
                {
                    var elite = members[i];
                    newGenomes.Add(elite);
                    var existingState = _agentStates.FirstOrDefault(a => a.Genome.GenomeId == elite.GenomeId);
                    newBrains.Add(existingState?.Brain ?? _developer.CompileGraph(
                        elite, _config.Budgets.Development, new DevelopmentContext(_config.RunSeed, _generation + 1)));
                    elitesCopied++;
                }
            }

            for (int i = elitesCopied; i < offspring; i++)
            {
                ulong mutSeed = SeedDerivation.MutationSeed(
                    _config.RunSeed, _generation + 1, species.SpeciesId, childOrdinal++);
                var childRng = new Rng64(mutSeed);

                SeedGenome parentOrCross;
                if (childRng.NextFloat01() < _config.Mutation.PCrossover && members.Count >= 2)
                {
                    var p1 = TournamentSelect(members, fitnessLookup, tournamentSize, ref childRng);
                    var p2 = TournamentSelect(members, fitnessLookup, tournamentSize, ref childRng);
                    if (fitnessLookup.GetValueOrDefault(p2.GenomeId, 0f) > fitnessLookup.GetValueOrDefault(p1.GenomeId, 0f))
                        (p1, p2) = (p2, p1);
                    parentOrCross = SeedGenome.Crossover(p1, p2, ref childRng);
                }
                else
                {
                    parentOrCross = TournamentSelect(members, fitnessLookup, tournamentSize, ref childRng);
                }

                var mutCtx = new MutationContext(
                    RunSeed: _config.RunSeed,
                    GenerationIndex: _generation + 1,
                    Config: _config.Mutation,
                    Innovations: _innovations,
                    Rng: childRng
                );
                var child = (SeedGenome)parentOrCross.Mutate(mutCtx);
                var devCtx = new DevelopmentContext(_config.RunSeed, _generation + 1);
                var brain = _developer.CompileGraph(child, _config.Budgets.Development, devCtx);
                newGenomes.Add(child);
                newBrains.Add(brain);
            }
        }

        while (newGenomes.Count < popSize)
        {
            var rng = new Rng64(SeedDerivation.MutationSeed(
                _config.RunSeed, _generation + 1, 9999, childOrdinal++));
            var genome = SeedGenome.CreateRandom(rng);
            var devCtx = new DevelopmentContext(_config.RunSeed, _generation + 1);
            var brain = _developer.CompileGraph(genome, _config.Budgets.Development, devCtx);
            newGenomes.Add(genome);
            newBrains.Add(brain);
        }

        _agentStates.Clear();
        for (int i = 0; i < newGenomes.Count; i++)
        {
            _agentStates.Add(new AgentEvalState
            {
                Genome = newGenomes[i],
                Brain = newBrains[i]
            });
        }
    }

    private static SeedGenome TournamentSelect(
        List<SeedGenome> members,
        Dictionary<Guid, float> fitnessLookup,
        int tournamentSize,
        ref Rng64 rng)
    {
        SeedGenome best = members[rng.NextInt(members.Count)];
        float bestFit = fitnessLookup[best.GenomeId];

        for (int t = 1; t < tournamentSize; t++)
        {
            var candidate = members[rng.NextInt(members.Count)];
            float fit = fitnessLookup[candidate.GenomeId];
            if (fit > bestFit) { best = candidate; bestFit = fit; }
        }
        return best;
    }

    private WorldFrameDto BuildWorldFrame()
    {
        int n = _arena.AgentCount;
        var agents = new AgentDto[n];
        for (int i = 0; i < n; i++)
        {
            int speciesId = i < _agentStates.Count
                ? _speciation.GetSpeciesId(_agentStates[i].Genome)
                : -1;

            agents[i] = new AgentDto(
                Id: i,
                X: _arena.AgentX(i),
                Y: _arena.AgentY(i),
                Heading: _arena.AgentHeading(i),
                Energy: _arena.AgentEnergy(i),
                Alive: _arena.AgentAlive(i),
                Speed: _arena.AgentSpeed(i),
                SpeciesId: speciesId,
                Signal0: _arena.Agents[i].Signal0,
                Signal1: _arena.Agents[i].Signal1,
                ShareReceived: _arena.Agents[i].ShareReceived,
                AttackReceived: _arena.Agents[i].AttackReceived
            );
        }

        var food = _arena.GetFoodItems()
            .Where(f => !f.Consumed)
            .Select((f, i) => new FoodDto(i, f.X, f.Y, f.EnergyValue, f.IsCorpse))
            .ToArray();
        var obstacles = _arena.GetObstacles()
            .Select(o => new ObstacleDto(o.MinX, o.MinY, o.Width, o.Height))
            .ToArray();
        var hazards = _arena.GetHazards()
            .Select(h => new HazardDto(h.MinX, h.MinY, h.Width, h.Height, ContinuousWorld.HazardDamage))
            .ToArray();

        return new WorldFrameDto(
            Tick: _agentStates.Count > 0 ? _agentStates[0].CurrentTick : 0,
            Generation: _generation,
            WorldIndex: _currentRound,
            WorldWidth: _currentWorldBudget.WorldWidth,
            WorldHeight: _currentWorldBudget.WorldHeight,
            Agents: agents,
            Food: food,
            Obstacles: obstacles,
            Hazards: hazards,
            FoodEnergyMultiplier: _arena?.FoodEnergyMultiplier ?? 1f,
            LightLevel: _arena?.LightLevel ?? 1f
        );
    }

    private BrainSnapshotDto BuildBrainSnapshot(int agentId, BrainGraph brain, BrainRuntime runtime)
    {
        var nodes = new List<BrainNodeDto>();
        var activations = runtime.GetActivations();

        foreach (var node in brain.Nodes)
        {
            float activation = node.NodeId < activations.Length ? activations[node.NodeId] : 0f;
            nodes.Add(new BrainNodeDto(
                Id: node.NodeId,
                Type: node.Type.ToString(),
                X: node.X,
                Y: node.Y,
                Activation: activation,
                Label: node.Type == BrainNodeType.Input ? $"I{node.NodeId}" :
                       node.Type == BrainNodeType.Output ? $"O{node.NodeId}" : null
            ));
        }

        var edges = new List<BrainEdgeDto>();
        foreach (var (dstId, edgeList) in brain.IncomingByDst)
        {
            foreach (var edge in edgeList)
            {
                edges.Add(new BrainEdgeDto(
                    From: edge.SrcNodeId,
                    To: dstId,
                    Weight: edge.WSlow + edge.WFast,
                    Type: edge.Meta.EdgeType.ToString(),
                    Delay: edge.Meta.Delay,
                    WSlow: edge.WSlow,
                    WFast: edge.WFast,
                    PlasticityGain: edge.PlasticityGain
                ));
            }
        }

        return new BrainSnapshotDto(agentId, nodes.ToArray(), edges.ToArray());
    }
}
