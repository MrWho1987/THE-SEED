# SEED — Seed Core v1 Technical Spec (Implementation-Grade)

> **Document status (March 2026):**
> This is the implementation-grade reference for the shared core engine. The data structures, algorithms, and formulas defined here are **the active implementation** used by both the original 2D terrarium (now on the `Legacy` branch) and the current market evolution system (`Seed.Market`).
>
> **Sections that are legacy (2D-world only):**
> - Section 2.2 interfaces `IAgentBody`, `IWorld`, `IEvaluator` — the market system uses `MarketAgent`, `DataAggregator`, and `MarketEvaluator` instead
> - Section 2.2.1 context records `BodyResetContext`, `BodyState`, `WorldSignals`, `WorldStepResult`, `WorldStepInfo`, `EpisodeMetrics`, `EvaluationContext` — replaced by market-specific types in `Seed.Market.Trading` and `Seed.Market.Evolution`
> - Section 6 (Deterministic parallel evaluation pseudocode) — the market evaluator uses its own replay-based evaluation loop
> - Section 12 (Fitness aggregation) — market fitness is net-profit-based (see `MarketFitness.cs`)
>
> **Sections that remain authoritative (active code):**
> - Section 1 (Versioning, determinism, numeric types)
> - Section 2.1 (Budgets) — `DevelopmentBudget` used directly by `MarketEvaluator`
> - Section 2.2 interfaces `IGenome`, `IDeveloper`, `IBrain`, `IObservatory` — still the core contracts
> - Section 2.3 (SeedGenome model) — unchanged
> - Section 2.4-2.5 (BrainGraph, BrainState) — unchanged
> - Sections 3-4 (JSON formats) — unchanged
> - Section 5 (Deterministic RNG) — unchanged
> - Section 7 (Development compiler) — unchanged
> - Section 8 (Learning engine) — unchanged
> - Section 9 (Speciation and selection) — reused by `MarketEvolution`
> - Section 10 (Mutation operators) — unchanged
> - Section 11 (Config defaults) — market system uses `MarketConfig` for market-specific params, but core defaults still apply

This document is the **implementation-grade** technical specification for **Seed Core v1 + V2-ready contract** described in `Docs/TheSeed.md`.

Core constraints:
- **Deterministic**: same seeds + same budgets + same genome → same phenotype + behavior (within the same runtime/ISA).
- **Sparse**: no \(O(N^2)\) operations in development/runtime.
- **V2-ready**: reserved fields exist end-to-end (serialization, mutation, runtime storage).

---

## 1) Versioning, determinism, numeric types

### 1.1 Schema versioning
All JSON objects defined here include:
- `schema`: string identifier
- `schemaVersion`: integer

### 1.2 Floating point determinism
Core v1 uses `float` for neural math and weights. Determinism guarantee is:
- **Strong** on the same OS + .NET runtime version + CPU ISA (replayable runs).
- **Best-effort** across different ISAs (minor float rounding drift can occur).

If cross-ISA determinism is required later, replace runtime math with fixed-point; **interfaces and JSON remain stable**.

### 1.3 ID strategy
- `GenomeId`: `Guid` (serialized as canonical string)
- `NodeId`: `int` (stable within a graph)
- `InnovationId`: `int` (stable across a run; increments deterministically)

---

## 2) Core C# data structures (records/classes)

These are the **exact** data models expected in Core v1. They are designed to be:
- serializable to/from the JSON formats in §3 and §4,
- stable across V2 upgrades.

### 2.1 Budgets (the only scaling dial)

```csharp
public readonly record struct DevelopmentBudget(
    int HiddenWidth,
    int HiddenHeight,
    int HiddenLayers,
    int TopKIn,
    int MaxOut,
    int LocalNeighborhoodRadius,
    int GlobalCandidateSamplesPerNeuron
);

public readonly record struct RuntimeBudget(
    int MaxTicksPerEpisode,
    int MicroStepsPerTick
);

public readonly record struct PopulationBudget(
    int PopulationSize,
    int WorldsPerGenome,
    int ElitesPerSpecies,
    int MinSpeciesSizeForElitism
);

public readonly record struct WorldBudget(
    int WorldWidth,
    int WorldHeight,
    float ObstacleDensity,
    float HazardDensity,
    int FoodCount
);

public readonly record struct ComputeBudget(
    int MaxWorkerThreads
);
```

### 2.2 Stable interfaces (contract)

```csharp
public interface IGenome
{
    Guid GenomeId { get; }
    string GenomeType { get; } // e.g. "SeedGenome.CPPN.NEAT"
    string ToJson();
    static abstract IGenome FromJson(string json);

    IGenome Clone(Guid? newId = null);
    IGenome Mutate(in MutationContext ctx);
    float DistanceTo(IGenome other, in SpeciationConfig cfg);
}

public interface IDeveloper
{
    IBrain Develop(IGenome genome, in DevelopmentBudget budget, in DevelopmentContext ctx);
}

public interface IBrain
{
    // Inputs are sensor channels; outputs are actuator channels.
    ReadOnlySpan<float> Step(ReadOnlySpan<float> inputs, in BrainStepContext ctx);
    void Learn(ReadOnlySpan<float> modulators, in BrainLearnContext ctx);

    BrainGraph ExportGraph(); // observability
}

public interface IAgentBody
{
    int SensorCount { get; }
    int ActuatorCount { get; }
    void Reset(in BodyResetContext ctx);
    void ReadSensors(Span<float> sensorBuffer);
    void ApplyActions(ReadOnlySpan<float> actionBuffer);
    void ApplyWorldSignals(in WorldSignals signals);
    BodyState GetState(); // includes Energy/Alive etc.
}

public interface IWorld
{
    void Reset(ulong worldSeed, in WorldBudget budget);
    WorldStepResult Step(ReadOnlySpan<float> actions);
}

public interface IEvaluator
{
    GenomeEvaluationResult Evaluate(IGenome genome, in EvaluationContext ctx);
}

public interface IObservatory
{
    void OnEvent(in ObsEvent e); // stable, append-only event types
}
```

#### 2.2.1 Context + result records (exact shapes)
These records make the interfaces above **fully implementable** without ambiguity.

```csharp
public readonly record struct DevelopmentContext(
    ulong RunSeed,
    int GenerationIndex
);

public readonly record struct BrainStepContext(
    int Tick
);

public readonly record struct BrainLearnContext(
    int Tick
);

public readonly record struct BodyResetContext(
    ulong AgentSeed
);

public readonly record struct BodyState(
    float Energy,
    bool Alive
);

public readonly record struct WorldSignals(
    float EnergyDelta, // can be negative for movement cost / hazards
    int FoodCollectedThisStep,
    float HazardPenalty // >= 0, optional
);

public readonly record struct WorldStepResult(
    bool Done,
    WorldSignals Signals,
    // Contract: modulators are a fixed-length vector defined by config (Reward/Pain/Curiosity in v1).
    float[] Modulators,
    WorldStepInfo Info // reserved for observability/debug
);

public readonly record struct WorldStepInfo(
    int Reserved0 = 0,
    int Reserved1 = 0
);

public readonly record struct EpisodeMetrics(
    int SurvivalTicks,
    float NetEnergyDelta,
    int FoodCollected,
    float EnergySpent,
    float InstabilityPenalty,
    float Fitness
);

public readonly record struct FitnessAggregate(
    float MeanFitness,
    float VarianceFitness,
    float WorstFitness,
    float Score
);

public sealed record GenomeEvaluationResult(
    Guid GenomeId,
    EpisodeMetrics[] PerWorld,
    FitnessAggregate Aggregate
);

public readonly record struct EvaluationContext(
    ulong RunSeed,
    int GenerationIndex,
    int WorldBundleKey,
    DevelopmentBudget DevelopmentBudget,
    RuntimeBudget RuntimeBudget,
    WorldBudget WorldBudget,
    int ModulatorCount
);

public readonly record struct MutationContext(
    ulong RunSeed,
    int GenerationIndex,
    MutationConfig Config,
    InnovationTracker Innovations,
    Rng64 Rng // deterministic PRNG (see §5)
);

public enum ObsEventType
{
    GenerationStart = 0,
    GenerationEnd = 1,
    GenomeEvaluated = 2,
    SpeciesAssigned = 3,
    BrainExported = 4
}

public readonly record struct ObsEvent(
    ObsEventType Type,
    int GenerationIndex,
    Guid GenomeId,
    string PayloadJson // stable, append-only payload formats
);
```

### 2.3 Seed genome model (CPPN + params + reserved fields)

#### Enums
```csharp
public enum CppnNodeType { Input = 0, Hidden = 1, Output = 2 }
public enum ActivationFn
{
    Identity = 0,
    Tanh = 1,
    Sigmoid = 2,
    Sin = 3,
    Gauss = 4
}
```

#### CPPN (NEAT-like)
```csharp
public sealed record CppnNode(
    int NodeId,
    CppnNodeType Type,
    ActivationFn Activation,
    float Bias
);

public sealed record CppnConnection(
    int InnovationId,
    int SrcNodeId,
    int DstNodeId,
    float Weight,
    bool Enabled
);

public sealed record CppnNetwork(
    IReadOnlyList<CppnNode> Nodes,
    IReadOnlyList<CppnConnection> Connections,
    int NextInnovationId
);
```

#### Genome params (mutable scalars within bounded ranges)
```csharp
public sealed record DevelopmentParams(
    int TopKInMin,
    int TopKInMax,
    int MaxOutMin,
    int MaxOutMax,
    float ConnectionThreshold,      // score threshold on CPPN output c
    float InitialWeightScale,       // scale applied to CPPN output w
    float GlobalSampleRate          // in [0..1], used to derive global sampling size
);

public sealed record LearningParams(
    float Eta,                      // η
    float EligibilityDecay,         // λ
    float AlphaReward,              // αR
    float AlphaPain,                // αP
    float AlphaCuriosity,           // αC
    float BetaConsolidate,          // β
    float GammaRecall               // γ
);

public sealed record StabilityParams(
    float WeightMaxAbs,             // Wmax
    float HomeostasisStrength,      // scales per-neuron gain adjustment
    float ActivationTarget,         // desired mean abs activation (homeostasis)
    float IncomingNormEps,          // eps for optional normalization
    bool EnableIncomingNormalization
);
```

#### Reserved “V2 channels” (must exist and serialize in v1)
```csharp
public sealed record ReservedGenomeFields(
    // Reserved CPPN outputs are fixed-length and fixed semantic positions.
    // v1 uses only indices 0 (c) and 1 (w). Others must still exist.
    string[] CppnOutputNames, // length == 6: ["c","w","delay","tau","module_tag","gate"]
    float[]  ReservedMutationScales // length == 4+ (append-only), v1 can keep defaults
);
```

#### The SeedGenome
```csharp
public sealed record SeedGenome(
    Guid GenomeId,
    CppnNetwork Cppn,
    DevelopmentParams Dev,
    LearningParams Learn,
    StabilityParams Stable,
    ReservedGenomeFields Reserved
) : IGenome
{
    public string GenomeType => "SeedGenome.CPPN.NEAT";
    // IGenome methods implemented in code, not shown here.
}
```

### 2.4 Brain graph runtime model (sparse recurrent + reserved metadata)

#### Enums + metadata
```csharp
public enum BrainNodeType { Input = 0, Hidden = 1, Output = 2 }
public enum EdgeType { Normal = 0, Modulatory = 1, Memory = 2 } // V2 may append values

public sealed record NodeMetadata(
    int RegionId = 0,               // reserved
    int ModuleId = 0,               // reserved
    float TimeConstant = 0f,        // reserved (tau)
    int PlasticityProfileId = 0     // reserved
);

public sealed record EdgeMetadata(
    EdgeType EdgeType = EdgeType.Normal,
    int Delay = 0,                  // reserved
    int PlasticityProfileId = 0     // reserved
);
```

#### Nodes + edges
```csharp
public sealed record BrainNode(
    int NodeId,
    BrainNodeType Type,
    float X,
    float Y,
    int Layer,
    NodeMetadata Meta
);

public sealed record BrainEdge(
    int SrcNodeId,
    int DstNodeId,
    float WSlow,
    float WFast,
    float PlasticityGain, // reserved (v1 default = 1.0)
    EdgeMetadata Meta
);
```

#### Brain graph container
Edges are stored as **incoming adjacency lists** to guarantee sparse compute per node.
```csharp
public sealed record BrainGraph(
    IReadOnlyList<BrainNode> Nodes,
    IReadOnlyDictionary<int, IReadOnlyList<BrainEdge>> IncomingByDst, // key = DstNodeId
    int InputCount,
    int OutputCount,
    int ModulatorCount,
    BrainGraphReserved Reserved
);

public sealed record BrainGraphReserved(
    // reserved for V2: modules, tags, etc. Keep as append-only.
    string[] ReservedKeys,
    string[] ReservedValues
);
```

### 2.5 Learning state (eligibility traces + two-speed weights)

Eligibility traces are runtime state, not part of `BrainGraph` export by default (but may be exported for debugging).

```csharp
public sealed class BrainState
{
    // Activations
    public float[] A; // length == Nodes.Count

    // Per-edge eligibility trace e_ij, stored aligned with incoming edge lists.
    // For deterministic layout: for each dst, edges are sorted by src, and E has same ordering.
    public Dictionary<int, float[]> EligibilityByDst;
}
```

---

## 3) JSON format — SeedGenome

### 3.1 Genome JSON (canonical)
Notes:
- All arrays must be in a **deterministic order** (see §6.1).
- `cppn.nodes` sorted by `(type, nodeId)`; `cppn.connections` sorted by `innovationId`.
- `cppn.nextInnovationId` is **informational** for debugging/inspection; mutation assigns innovations via `InnovationTracker` (§10.6). When serializing, set it to `max(innovationId)+1` (or `0` if no connections).

```json
{
  "schema": "Seed.Genome",
  "schemaVersion": 1,
  "genomeType": "SeedGenome.CPPN.NEAT",
  "genomeId": "00000000-0000-0000-0000-000000000000",

  "cppn": {
    "nextInnovationId": 123,
    "nodes": [
      { "nodeId": 0, "type": "Input",  "activation": "Identity", "bias": 0.0 },
      { "nodeId": 1, "type": "Hidden", "activation": "Tanh",     "bias": 0.1 },
      { "nodeId": 2, "type": "Output", "activation": "Tanh",     "bias": 0.0 }
    ],
    "connections": [
      { "innovationId": 10, "srcNodeId": 0, "dstNodeId": 1, "weight": 0.5, "enabled": true },
      { "innovationId": 11, "srcNodeId": 1, "dstNodeId": 2, "weight": -0.2, "enabled": true }
    ]
  },

  "params": {
    "development": {
      "topKInMin": 6,
      "topKInMax": 16,
      "maxOutMin": 8,
      "maxOutMax": 24,
      "connectionThreshold": 0.2,
      "initialWeightScale": 1.0,
      "globalSampleRate": 0.02
    },
    "learning": {
      "eta": 0.01,
      "eligibilityDecay": 0.95,
      "alphaReward": 1.0,
      "alphaPain": -1.0,
      "alphaCuriosity": 0.25,
      "betaConsolidate": 0.01,
      "gammaRecall": 0.01
    },
    "stability": {
      "weightMaxAbs": 3.0,
      "homeostasisStrength": 0.01,
      "activationTarget": 0.15,
      "incomingNormEps": 1e-5,
      "enableIncomingNormalization": true
    }
  },

  "reserved": {
    "cppnOutputNames": ["c","w","delay","tau","module_tag","gate"],
    "reservedMutationScales": [1.0, 1.0, 1.0, 1.0]
  }
}
```

### 3.2 Deterministic serialization rules
- Enums are serialized as strings (stable names).
- Arrays are serialized in deterministic order (see §6.1).
- No dictionary key ordering assumptions; if dictionaries are present, serialize keys sorted ascending.

---

## 4) JSON format — BrainGraph export

### 4.1 BrainGraph JSON (canonical)
Notes:
- `nodes` sorted by `nodeId`.
- `incomingEdges` is a list of entries sorted by `dstNodeId`, and each entry’s `edges` sorted by `srcNodeId`.
- `wSlow` and `wFast` are exported; they are runtime state after learning (or equal at init).

```json
{
  "schema": "Seed.BrainGraph",
  "schemaVersion": 1,
  "inputCount": 32,
  "outputCount": 4,
  "modulatorCount": 3,

  "nodes": [
    { "nodeId": 0, "type": "Input", "x": 0.0, "y": 0.0, "layer": 0,
      "meta": { "regionId": 0, "moduleId": 0, "timeConstant": 0.0, "plasticityProfileId": 0 } }
  ],

  "incomingEdges": [
    {
      "dstNodeId": 100,
      "edges": [
        {
          "srcNodeId": 12,
          "wSlow": 0.10,
          "wFast": 0.12,
          "plasticityGain": 1.0,
          "meta": { "edgeType": "Normal", "delay": 0, "plasticityProfileId": 0 }
        }
      ]
    }
  ],

  "reserved": {
    "reservedKeys": ["v2_placeholder_0"],
    "reservedValues": ["0"]
  }
}
```

---

## 5) Deterministic RNG seeding strategy (streams)

Seed Core v1 uses **multiple deterministic PRNG streams** derived from one `runSeed`.

### 5.1 PRNG algorithm requirements
- Stable algorithm implementation in code (do not use `System.Random`).
- Provide `NextU64()`, `NextFloat01()`, `NextInt(max)` with fully specified behavior.

Recommended: **xoshiro256**\*\* for stream generation + **SplitMix64** for seeding.

#### 5.1.1 Reference `Rng64` (xoshiro256**; exact conversion rules)
This is the canonical PRNG shape referenced by `MutationContext` and any sampling logic. (You can rename it in code; behavior must match.)

```csharp
public struct Rng64
{
    private ulong _s0, _s1, _s2, _s3;

    public Rng64(ulong seed)
    {
        ulong x = seed;
        _s0 = SplitMix64(ref x);
        _s1 = SplitMix64(ref x);
        _s2 = SplitMix64(ref x);
        _s3 = SplitMix64(ref x);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong RotL(ulong x, int k) => (x << k) | (x >> (64 - k));

    // xoshiro256** (David Blackman, Sebastiano Vigna)
    public ulong NextU64()
    {
        ulong result = RotL(_s1 * 5UL, 7) * 9UL;
        ulong t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;

        _s2 ^= t;
        _s3 = RotL(_s3, 45);

        return result;
    }

    // Exact float conversion: take top 24 bits → float in [0,1).
    public float NextFloat01()
    {
        uint u = (uint)(NextU64() >> 40); // top 24 bits
        return u * (1.0f / 16777216.0f);  // 2^24
    }

    // Exact unbiased int sampling via rejection (deterministic).
    public int NextInt(int exclusiveMax)
    {
        if (exclusiveMax <= 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMax));
        uint bound = (uint)exclusiveMax;
        uint threshold = (uint)(0x100000000UL % bound);
        while (true)
        {
            uint r = (uint)NextU64();
            if (r >= threshold) return (int)(r % bound);
        }
    }

    // SplitMix64 reference is specified in §5.2.
    private static ulong SplitMix64(ref ulong x) => throw new NotImplementedException();
}
```

### 5.2 SplitMix64 (seed derivation)
Derivation function \(D\) maps an input `ulong` to a well-mixed `ulong`:

```
ulong SplitMix64(ref ulong x)
{
    x += 0x9E3779B97F4A7C15;
    ulong z = x;
    z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9;
    z = (z ^ (z >> 27)) * 0x94D049BB133111EB;
    return z ^ (z >> 31);
}
```

### 5.3 Stream derivation
Define a stable 64-bit mixing function for `(runSeed, domain, a, b, c)`:

```
ulong DeriveSeed(ulong runSeed, ulong domain, ulong a = 0, ulong b = 0, ulong c = 0)
{
    ulong x = runSeed;
    x ^= domain * 0xD6E8FEB86659FD93;
    x ^= a      * 0xA5A3564E27F2D3A5;
    x ^= b      * 0x3C79AC492BA7B653;
    x ^= c      * 0x1C69B3F74AC4AE35;
    // run through SplitMix64 once for finalization:
    return SplitMix64(ref x);
}
```

### 5.4 Required streams (minimum)
Domain constants are fixed forever:

```text
DOMAIN_RUN        = 0x52554E0000000001  // "RUN\0...\1"
DOMAIN_GENERATION = 0x47454E0000000001  // "GEN\0...\1"
DOMAIN_WORLD      = 0x574F524C44000001  // "WORLD\0\1"
DOMAIN_AGENT      = 0x4147454E54000001  // "AGENT\0\1"
DOMAIN_DEVELOP    = 0x444556454C4F5001  // "DEVELOP\1"
DOMAIN_MUTATION   = 0x4D55544154450001  // "MUTATE\0\1"
DOMAIN_TIEBREAK   = 0x5449454252454B01  // "TIEBREK\1"
```

Stream seeds:
- **Generation seed**: `genSeed = DeriveSeed(runSeed, DOMAIN_GENERATION, generationIndex)`
- **World seed**: `worldSeed = DeriveSeed(runSeed, DOMAIN_WORLD, generationIndex, worldIndex, worldBundleKey)`
- **Agent seed**: `agentSeed = DeriveSeed(runSeed, DOMAIN_AGENT, generationIndex, genomeOrdinal, evaluationReplica)`
- **Mutation seed**: `mutSeed = DeriveSeed(runSeed, DOMAIN_MUTATION, generationIndex, parentOrdinal, childOrdinal)`
- **Tie-break seed**: `tieSeed = DeriveSeed(runSeed, DOMAIN_TIEBREAK, generationIndex)`

Notes:
- `worldBundleKey` is a stable integer identifying the world-bundle definition (so adding bundles doesn’t shift old bundles).
- `genomeOrdinal` is the genome index in **stable sorted order** at evaluation time (see §6.2).

---

## 6) Deterministic parallel evaluation (pseudocode)

Goals:
- Evaluate many genomes and many worlds in parallel.
- Ensure exact determinism independent of scheduling.

### 6.1 Ordering rules (non-negotiable)
- Population genomes have a stable `GenomeOrdinal` per generation: sort by `(speciesId, genomeId)` or `(genomeId)` if species not assigned yet.
- World instances in a bundle have stable indices `0..K-1`.
- Per-genome per-world episode returns a fixed-size struct of scalars.
- Reduction is done **in ordinal order**, never by “first completed”.

### 6.2 Pseudocode (deterministic parallel)

```text
Input: runSeed, generationIndex, genomes[], budgets, worldBundle(K worlds), maxWorkers

// 1) Stable order for this generation
sortedGenomes = genomes.OrderBy(g => g.GenomeId) // or (speciesId, GenomeId) if already speciated

// 2) Allocate result slots with fixed indices
results = GenomeEvaluationResult[sortedGenomes.Count]

// 3) Parallel evaluation with deterministic per-slot write
Parallel.For(0, sortedGenomes.Count, MaxDegreeOfParallelism = maxWorkers, i =>
{
    genome = sortedGenomes[i]
    // Derive a deterministic agent seed for this genome evaluation
    // Note: do NOT use thread-local randomness.
    agentBaseSeed = DeriveSeed(runSeed, DOMAIN_AGENT, generationIndex, (ulong)i, 0)

    // Deterministic development (genome -> graph). IMPORTANT: do not leak learning across worlds.
    // Recommended: develop once, then spawn fresh runtime state per world episode.
    brainGraph = developer.Develop(genome, budgets.Development, ctx(runSeed, generationIndex)).ExportGraph()

    worldMetrics = EpisodeMetrics[K]

    for worldIndex in 0..K-1:
        worldSeed = DeriveSeed(runSeed, DOMAIN_WORLD, generationIndex, (ulong)worldIndex, worldBundleKey)
        agentSeed = DeriveSeed(runSeed, DOMAIN_AGENT, generationIndex, (ulong)i, (ulong)worldIndex)

        // Fresh brain per episode (eligibility=0, activations=0, wFast=wSlow=w0 at start):
        brain = BrainRuntime.FromGraph(brainGraph) // internal helper, not part of the stable contract

        metrics = RunEpisode(worldSeed, agentSeed, brain, worldBudget, runtimeBudget)
        worldMetrics[worldIndex] = metrics

    // Deterministic aggregation in worldIndex order
    results[i] = Aggregate(genome.GenomeId, worldMetrics)
})

// 4) Deterministic reduction across genomes (ordinal order)
//    (e.g., for global stats, species allocations, etc.)
global = ReduceInOrder(results)

return results, global
```

### 6.3 Episode execution (deterministic)

```text
RunEpisode(worldSeed, agentSeed, brain, worldBudget, runtimeBudget):
    world.Reset(worldSeed, worldBudget)
    body.Reset(BodyResetContext(agentSeed))

    metrics = init
    for tick in 0..runtimeBudget.MaxTicksPerEpisode-1:
        body.ReadSensors(sensorBuffer) // deterministic
        outputs = brain.Step(sensorBuffer, BrainStepContext(tick))
        body.ApplyActions(outputs)
        worldResult = world.Step(outputs)
        body.ApplyWorldSignals(worldResult.Signals) // deterministic
        brain.Learn(worldResult.Modulators, BrainLearnContext(tick)) // deterministic
        metrics.Accumulate(worldResult, body.GetState())
        if worldResult.Done: break
    return metrics
```

---

## 7) Development compiler: CPPN → sparse recurrent BrainGraph

### 7.1 Node placement
Inputs: fixed set of sensor channels \(S\) → nodes `[0..S-1]` with `Type=Input`.
Outputs: fixed actuator channels \(A\) → nodes `[S..S+A-1]` with `Type=Output`.
Hidden nodes: grid/rings determined by `DevelopmentBudget`:
- `HiddenWidth * HiddenHeight * HiddenLayers` hidden nodes with deterministic `(x,y,layer)`.

Node ID assignment (deterministic):
- Inputs first, then hidden in lexicographic order `(layer, y, x)`, then outputs.

### 7.2 Candidate incoming edge set (bounded)
For each destination node `j`:
- **Local candidates**: all nodes `i` within `LocalNeighborhoodRadius` in coordinate space (same layer and adjacent layers allowed by rule).
- **Global candidates**: deterministic sample of size `GlobalCandidateSamplesPerNeuron`.

Deterministic global sample:
- Use `DeriveSeed(runSeed, DOMAIN_DEVELOP, (ulong)j.NodeId, 0, 0)` to seed a sampler.
- Sample without replacement from eligible node IDs using Fisher–Yates shuffle over a precomputed eligible list (bounded).

### 7.3 CPPN query & scoring
For each candidate edge `i→j`, compute input vector:
`[xi, yi, li, xj, yj, lj, dx, dy, dist]`

CPPN outputs fixed positions:
0. `c` (connection score)
1. `w` (initial weight value)
2. `delay` (reserved; ignored in v1)
3. `tau` (reserved; ignored in v1)
4. `module_tag` (reserved; ignored in v1)
5. `gate` (reserved; ignored in v1)

Connection kept if:
- `c >= Dev.ConnectionThreshold`
- and in TopK incoming edges after sorting by descending `c` (then deterministic tie-break by `SrcNodeId`)

Initial weights:
- `w0 = clamp(w * Dev.InitialWeightScale, -Stable.WeightMaxAbs, +Stable.WeightMaxAbs)`
- set `WSlow = w0`, `WFast = w0`
- set `PlasticityGain = 1.0` (reserved in v1; V2 may use profiles/gains)

Out-degree constraint:
- enforce `MaxOut` by tracking outgoing counts and dropping lowest-score edges deterministically.

---

## 8) Learning engine (modulated eligibility + two-speed consolidation)

### 8.1 Modulator vector (fixed indices)
`modulators.Length == ModulatorCount` where indices are fixed in config:
- 0: Reward
- 1: Pain
- 2: Curiosity

### 8.2 Eligibility update
For synapse \(i \rightarrow j\):
\[
e_{ij} \leftarrow \lambda \cdot e_{ij} + \mathrm{clip}(a_i \cdot a_j,\,-1,\,+1)
\]

### 8.3 Combined modulator and weight update
\[
M \equiv \alpha_R R + \alpha_P P + \alpha_C C
\]
\[
\Delta w \leftarrow \eta \cdot M \cdot e_{ij}
\]
\[
w_{fast} \leftarrow \mathrm{clip}(w_{fast} + \Delta w,\,-W_{max},\,+W_{max})
\]

### 8.4 Two-speed consolidation
Applied periodically (default: every tick after learning update):
\[
w_{slow} \leftarrow (1-\beta)\,w_{slow} + \beta\,w_{fast}
\]
\[
w_{fast} \leftarrow (1-\gamma)\,w_{fast} + \gamma\,w_{slow}
\]

### 8.5 Stability safeguards
- Weight clipping to `[-Wmax,+Wmax]` on all updates.
- Homeostasis: per neuron `j`, compute `meanAbsAj` over recent ticks (or exponentially weighted), then scale incoming weights by:
  - `scale_j = exp( -HomeostasisStrength * (meanAbsAj - ActivationTarget) )`
  - Apply to effective weights used during forward pass (not necessarily stored weights).
- Optional incoming normalization: for each dst node `j`, normalize incoming effective weights by RMS.

---

## 9) Speciation (NEAT-style) and selection

### 9.1 Compatibility distance
Given two genomes \(A,B\) with NEAT innovation alignment:
- Let \(E\) = number of excess genes
- Let \(D\) = number of disjoint genes
- Let \(\bar{W}\) = average absolute weight difference of matching genes
- Let \(N = \max(|genes_A|, |genes_B|)\) and use \(N = 1\) if \(N < 20\)

\[
\delta(A,B) = c_1 \frac{E}{N} + c_2 \frac{D}{N} + c_3 \bar{W}
\]

Genome genes include:
- CPPN connections (primary for topology)
- Optional: CPPN node biases can be included, but only if NodeIds are tracked via innovations; v1 may omit to keep NEAT alignment simple.

**Exact computation (connections only, deterministic):**
- Let `genesA` and `genesB` be the full CPPN connection lists (enabled + disabled), each sorted by `innovationId` ascending.
- Two-pointer scan:
  - If `innovA == innovB`: this is a matching gene. Accumulate `abs(weightA - weightB)` into `sumDiff`, increment `matchCount`, advance both.
  - If `innovA < innovB`: increment `D` and advance A.
  - If `innovB < innovA`: increment `D` and advance B.
- After one list ends, remaining genes in the other list are **excess** \(E\).
- \(\bar{W} = (matchCount == 0) ? 0 : (sumDiff / matchCount)\).

### 9.2 Species assignment (deterministic)
Maintain list of species representatives in stable order (ascending `speciesId`).
For each genome in stable population order:
- Assign to the **first** species whose representative yields \(\delta < \delta_t\).
- If none match, create a new species with next `speciesId`.

### 9.3 Fitness sharing (optional but recommended)
Adjusted fitness:
\[
f'_i = \frac{f_i}{\sum_{j \in species(i)} \mathrm{sh}(\delta(i,j))}
\]
with:
\[
\mathrm{sh}(\delta)=
\begin{cases}
1 - (\delta/\sigma_{share})^\alpha & \delta < \sigma_{share}\\
0 & \text{otherwise}
\end{cases}
\]

Core v1 can also use simpler sharing: \(f'_i = f_i / |species|\).

### 9.4 Selection and reproduction
Per species:
- Keep `ElitesPerSpecies` best genomes unchanged.
- Fill remaining offspring via tournament selection (size `k`) on adjusted fitness.
- Optional crossover: disabled by default in v1 (can be added later; keep JSON stable).

---

## 10) Mutation operators (formulas) + deterministic application

Mutation uses `mutSeed` derived in §5.4. All random decisions consume PRNG in a fixed order.

### 10.1 Weight perturbation (CPPN connections)
With probability `pWeightMutate` per connection:
- either **perturb**:
  \[
  w \leftarrow w + \mathcal{N}(0,\sigma_w)
  \]
- or **reset** with probability `pWeightReset`:
  \[
  w \leftarrow \mathrm{Uniform}(-w_{resetMax}, +w_{resetMax})
  \]

### 10.2 Bias mutation (CPPN nodes)
With probability `pBiasMutate` per node:
\[
b \leftarrow b + \mathcal{N}(0,\sigma_b)
\]

### 10.3 Add connection
With probability `pAddConn`:
- Choose `(src,dst)` among valid pairs (respect acyclic constraint if enforced; otherwise allow recurrent edges).
- If connection exists and disabled, enable it; else add new with:
  - `innovationId = NextInnovationId++` (deterministic within a genome’s mutation)
  - `weight ~ Uniform(-wInitMax, +wInitMax)`

### 10.4 Add node (split connection)
With probability `pAddNode`:
- Choose an enabled connection uniformly from enabled list.
- Disable it.
- Add a new hidden node `n`.
- Add two connections:
  - `src -> n` with weight 1.0
  - `n -> dst` with old connection’s weight
- Innovation IDs allocated deterministically in order.

### 10.5 Param mutation (Dev/Learn/Stable)
Each scalar param has:
- a min/max clamp range,
- a mutation sigma,
- a per-param mutate probability.

Example (Gaussian):
\[
x \leftarrow \mathrm{clip}(x + \mathcal{N}(0,\sigma_x), x_{min}, x_{max})
\]

Reserved fields:
- `Reserved.ReservedMutationScales` must be preserved; v1 may mutate them but they are unused.

### 10.6 Global innovation tracking (deterministic, NEAT-style)
Structural mutations MUST assign **global** innovation IDs so distance/speciation aligns across the population.

Determinism rule:
- Offspring creation + mutation is performed in a **single-threaded, stable order** (speciesId, then parent rank, then child index).
- Evaluation is parallel; innovation assignment is not.

Canonical data structure:

```csharp
public readonly record struct ConnectionKey(int SrcNodeId, int DstNodeId);

public readonly record struct SplitInnovation(
    int NewNodeId,
    int InnovSrcToNew,
    int InnovNewToDst
);

public sealed class InnovationTracker
{
    public int NextInnovationId { get; private set; }
    public int NextCppnNodeId { get; private set; }

    private readonly Dictionary<ConnectionKey, int> _connInnov = new();
    private readonly Dictionary<int, SplitInnovation> _splitInnov = new(); // key: old connection innovationId

    public InnovationTracker(int initialNextInnovationId, int initialNextCppnNodeId)
    {
        NextInnovationId = initialNextInnovationId;
        NextCppnNodeId = initialNextCppnNodeId;
    }

    public int GetOrCreateConnectionInnovation(int srcNodeId, int dstNodeId)
    {
        var key = new ConnectionKey(srcNodeId, dstNodeId);
        if (_connInnov.TryGetValue(key, out int innov)) return innov;
        innov = NextInnovationId++;
        _connInnov[key] = innov;
        return innov;
    }

    public SplitInnovation GetOrCreateSplitInnovation(int oldConnInnovationId)
    {
        if (_splitInnov.TryGetValue(oldConnInnovationId, out var split)) return split;
        split = new SplitInnovation(
            NewNodeId: NextCppnNodeId++,
            InnovSrcToNew: NextInnovationId++,
            InnovNewToDst: NextInnovationId++
        );
        _splitInnov[oldConnInnovationId] = split;
        return split;
    }
}
```

Initialization defaults:
- CPPN input nodes (fixed): 9 (xi, yi, li, xj, yj, lj, dx, dy, dist) → NodeIds `0..8`
- CPPN output nodes (fixed): 6 (c, w, delay, tau, module_tag, gate) → NodeIds `9..14`
- Therefore: `initialNextCppnNodeId = 15`
- `initialNextInnovationId = 0` (or any fixed start)

Notes:
- If `AddConnection` selects an already-existing disabled gene, it should **enable it** without creating a new innovation.
- `AddNode` always splits an enabled connection; the split innovations are reused across the run via `_splitInnov`.

---

## 11) Core v1 config defaults (starter values)

These defaults are tuned for “local mode” and insect-level tasks.

### 11.1 Budgets (local)
- `DevelopmentBudget`:
  - `HiddenWidth=12`, `HiddenHeight=12`, `HiddenLayers=2` (288 hidden)
  - `TopKIn=12`, `MaxOut=16`
  - `LocalNeighborhoodRadius=2`
  - `GlobalCandidateSamplesPerNeuron=16`
- `RuntimeBudget`:
  - `MaxTicksPerEpisode=1500`
  - `MicroStepsPerTick=3`
- `PopulationBudget`:
  - `PopulationSize=128`
  - `WorldsPerGenome=8`
  - `ElitesPerSpecies=1`
  - `MinSpeciesSizeForElitism=5`
- `WorldBudget`:
  - `WorldWidth=64`, `WorldHeight=64`
  - `ObstacleDensity=0.12`, `HazardDensity=0.04`
  - `FoodCount=25`
- `ComputeBudget`:
  - `MaxWorkerThreads = Environment.ProcessorCount` (clamp to >= 1)

### 11.2 Development params
- `TopKInMin=6`, `TopKInMax=16`
- `MaxOutMin=8`, `MaxOutMax=24`
- `ConnectionThreshold=0.20`
- `InitialWeightScale=1.0`
- `GlobalSampleRate=0.02`

### 11.3 Learning params
- `Eta=0.01`
- `EligibilityDecay=0.95`
- `AlphaReward=+1.0`
- `AlphaPain=-1.0`
- `AlphaCuriosity=+0.25`
- `BetaConsolidate=0.01`
- `GammaRecall=0.01`

### 11.4 Stability params
- `WeightMaxAbs=3.0`
- `HomeostasisStrength=0.01`
- `ActivationTarget=0.15`
- `IncomingNormEps=1e-5`
- `EnableIncomingNormalization=true`

### 11.5 Speciation config defaults
```csharp
public sealed record SpeciationConfig(
    float C1 = 1.0f,
    float C2 = 1.0f,
    float C3 = 0.4f,
    float CompatibilityThreshold = 3.0f,
    float ShareSigma = 3.0f,
    float ShareAlpha = 1.0f
);
```

### 11.6 Mutation config defaults
```csharp
public sealed record MutationConfig(
    float PWeightMutate = 0.80f,
    float PWeightReset  = 0.10f,
    float SigmaWeight   = 0.20f,
    float WeightResetMax= 1.00f,

    float PBiasMutate   = 0.30f,
    float SigmaBias     = 0.10f,

    float PAddConn      = 0.10f,
    float PAddNode      = 0.03f,
    float WInitMax      = 1.00f,

    float PParamMutate  = 0.20f,
    float SigmaParam    = 0.05f
);
```

---

## 12) Fitness aggregation (bundle) and tie-breaks

### 12.1 Per-world episode metrics (fixed scalar set)
At minimum:
- `survivalTicks`
- `netEnergyDelta`
- `foodCollected`
- `energySpent`
- `instabilityPenalty` (saturation/oscillation measure)

### 12.2 Aggregate across K worlds
Compute:
- `meanFitness`
- `varianceFitness`
- `worstFitness`

Selection score (default):
\[
F = \mu - \lambda_{var}\sqrt{\mathrm{var}} + \lambda_{worst}\cdot \min
\]
with defaults:
- `lambdaVar = 0.10`
- `lambdaWorst = 0.20`

### 12.3 Deterministic tie-break
When sorting equal scores, break ties by:
1. `score` descending
2. `worstFitness` descending
3. deterministic hash: `Hash64(genomeId, tieSeed)` ascending

Canonical `Hash64` definition:
- Let `guidLo` = little-endian `UInt64` of the first 8 bytes of `GenomeId`
- Let `guidHi` = little-endian `UInt64` of the last 8 bytes of `GenomeId`
- Then:
  - `Hash64(genomeId, tieSeed) = DeriveSeed(tieSeed, DOMAIN_TIEBREAK, guidLo, guidHi, 0)`

---

## 13) Appendix: required deterministic collections

Implementation rules:
- Never iterate `Dictionary<>` and assume stable order; always materialize sorted keys.
- Always sort adjacency lists (`incomingEdges`) by `srcNodeId`.
- Always store population in stable order when assigning ordinals and deriving seeds.


