# Seed.Core — technical reference

`Seed.Core` is a .NET 8 class library (`net8.0`) defining shared contracts, budgets, configuration records, RNG/seed derivation, and deterministic helpers for the Seed codebase.

---

## 1. Interfaces

### `IGenome`

```csharp
public interface IGenome
{
    Guid GenomeId { get; }
    string GenomeType { get; }

    string ToJson();
    IGenome CloneGenome(Guid? newId = null);
    IGenome Mutate(in MutationContext ctx);
    float DistanceTo(IGenome other, in SpeciationConfig cfg);
}
```

### `MutationContext`

```csharp
public readonly record struct MutationContext(
    ulong RunSeed,
    int GenerationIndex,
    MutationConfig Config,
    IInnovationTracker Innovations,
    Rng64 Rng
);
```

### `IInnovationTracker`

```csharp
public interface IInnovationTracker
{
    int NextInnovationId { get; }
    int NextCppnNodeId { get; }

    int GetOrCreateConnectionInnovation(int srcNodeId, int dstNodeId);
    (int NewNodeId, int InnovSrcToNew, int InnovNewToDst) GetOrCreateSplitInnovation(int oldConnInnovationId);
}
```

### `IBrain`

```csharp
public interface IBrain
{
    ReadOnlySpan<float> Step(ReadOnlySpan<float> inputs, in BrainStepContext ctx);
    void Learn(ReadOnlySpan<float> modulators, in BrainLearnContext ctx);

    IBrainGraph ExportGraph();
    void Reset();

    float GetInstabilityPenalty();
}
```

### `IBrainGraph`

```csharp
public interface IBrainGraph
{
    int InputCount { get; }
    int OutputCount { get; }
    int ModulatorCount { get; }
    int NodeCount { get; }
    int EdgeCount { get; }

    string ToJson();
}
```

### `IObservatory`

Used by the market evolution path (`Seed.Market`) for generation events and JSON payloads; implementations live in `Seed.Observatory` (for example `FileObservatory`, `NullObservatory`).

```csharp
public interface IObservatory
{
    void OnEvent(in ObsEvent e);
    void Flush();
}
```

Supporting types:

| Type | Definition |
|------|------------|
| `ObsEventType` | `GenerationStart = 0`, `GenerationEnd = 1`, `GenomeEvaluated = 2`, `SpeciesAssigned = 3`, `BrainExported = 4`, `EpisodeComplete = 5` |
| `ObsEvent` | `readonly record struct ObsEvent(ObsEventType Type, int GenerationIndex, Guid GenomeId, string PayloadJson)` |

### `IAgentBody` and `IWorld`

These interfaces remain in Core for compatibility with the removed terrarium-style simulation. There are no `IAgentBody` or `IWorld` implementations in this repository; they are retained as contracts for possible future use.

**`IAgentBody`**

```csharp
public interface IAgentBody
{
    int SensorCount { get; }
    int ActuatorCount { get; }

    void Reset(in BodyResetContext ctx);
    void ReadSensors(Span<float> sensorBuffer);
    void ApplyActions(ReadOnlySpan<float> actionBuffer);
    void ApplyWorldSignals(in WorldSignals signals);
    BodyState GetState();
}
```

**`IWorld`**

```csharp
public interface IWorld
{
    void Reset(ulong worldSeed, in WorldBudget budget);
    WorldStepResult Step(ReadOnlySpan<float> actions);

    (float distance, int hitType) Raycast(float originX, float originY, float dirX, float dirY, float maxDistance);
    float RaycastDistance(float originX, float originY, float dirX, float dirY, float maxDistance);
    int RaycastType(float originX, float originY, float dirX, float dirY, float maxDistance);
    (float dx, float dy) FoodGradient(float x, float y);
    (float s0, float s1) NearbySignals(float x, float y);
    (float dx, float dy) SignalGradient(float x, float y);
    float NearestAgentEnergy(float x, float y);
    float NearbyAgentDensity(float x, float y);
    (float shareReceived, float attackReceived) InteractionFeedback();

    float AgentX { get; }
    float AgentY { get; }
    float AgentHeading { get; }
    float AgentSpeed { get; }
    float LightLevel { get; }
}
```

---

## 2. RNG

### `Rng64`

Deterministic PRNG: **xoshiro256\*\*** state (`_s0`–`_s3`), initialized from a 64-bit seed via **SplitMix64** (four calls) in the constructor `Rng64(ulong seed)`.

| Member | Description |
|--------|-------------|
| `ulong NextU64()` | Next 64-bit value (xoshiro256\*\*). |
| `float NextFloat01()` | `[0, 1)` using top 24 bits: `(uint)(NextU64() >> 40) * (1.0f / 16777216.0f)`. |
| `double NextDouble01()` | `[0, 1)` using top 53 bits: `NextU64() >> 11` scaled by `1.0 / 9007199254740992.0`. |
| `int NextInt(int exclusiveMax)` | Unbiased integer in `[0, exclusiveMax)`; throws if `exclusiveMax <= 0`. |
| `float NextGaussian(float mean = 0f, float stdDev = 1f)` | Box–Muller; resamples `u1` while `u1 <= float.Epsilon`. |
| `float NextFloat(float min, float max)` | Uniform in `[min, max)`. |
| `static ulong SplitMix64(ref ulong x)` | Public static; same mixing as internal seed expansion. |

**Reproducibility:** Any logic that branches on `Rng64` output is fixed once the seed is fixed. `SeedDerivation` and `SplitMix64` are the supported way to derive independent streams from a run seed.

### `SeedDerivation`

Static class; domain tags are **fixed constants** (comments in source: “fixed forever, do not change”).

| Constant | Value |
|----------|--------|
| `DOMAIN_RUN` | `0x52554E0000000001UL` |
| `DOMAIN_GENERATION` | `0x47454E0000000001UL` |
| `DOMAIN_WORLD` | `0x574F524C44000001UL` |
| `DOMAIN_AGENT` | `0x4147454E54000001UL` |
| `DOMAIN_DEVELOP` | `0x444556454C4F5001UL` |
| `DOMAIN_MUTATION` | `0x4D55544154450001UL` |
| `DOMAIN_TIEBREAK` | `0x5449454252454B01UL` |

| Method | Signature |
|--------|-----------|
| `DeriveSeed` | `static ulong DeriveSeed(ulong runSeed, ulong domain, ulong a = 0, ulong b = 0, ulong c = 0)` — mixes with fixed multipliers and returns `Rng64.SplitMix64(ref x)`. |
| `GenerationSeed` | `static ulong GenerationSeed(ulong runSeed, int generationIndex)` |
| `WorldSeed` | `static ulong WorldSeed(ulong runSeed, int generationIndex, int worldIndex, int worldBundleKey = 0)` |
| `AgentSeed` | `static ulong AgentSeed(ulong runSeed, int generationIndex, int genomeOrdinal, int evaluationReplica = 0)` |
| `MutationSeed` | `static ulong MutationSeed(ulong runSeed, int generationIndex, int parentOrdinal, int childOrdinal)` |
| `TieBreakSeed` | `static ulong TieBreakSeed(ulong runSeed, int generationIndex)` |
| `DevelopmentSeed` | `static ulong DevelopmentSeed(ulong runSeed, int nodeId)` |
| `TieBreakHash` | `static ulong TieBreakHash(Guid genomeId, ulong tieSeed)` — writes GUID bytes, splits into two `ulong`s, passes to `DeriveSeed(tieSeed, DOMAIN_TIEBREAK, guidLo, guidHi, 0)`. |

---

## 3. Budgets

Each budget is a `readonly record struct` with optional constructor parameters (defaults below). Several types also expose a `static Default` that **may differ** from the parameter defaults alone.

### `DevelopmentBudget`

| Field | Constructor default | `DevelopmentBudget.Default` |
|-------|---------------------|-------------------------------|
| `HiddenWidth` | `12` | `12` |
| `HiddenHeight` | `12` | `12` |
| `HiddenLayers` | `2` | `2` |
| `TopKIn` | `12` | `12` |
| `MaxOut` | `16` | `16` |
| `LocalNeighborhoodRadius` | `2` | `2` |
| `GlobalCandidateSamplesPerNeuron` | `16` | `16` |
| `MaxSynapticDelay` | `5` | `5` |

- Computed: `int TotalHiddenNeurons => HiddenWidth * HiddenHeight * HiddenLayers`.

**Usage:** The market pipeline compiles brains with a dedicated `DevelopmentBudget` instance (`MarketEvaluator.MarketBrainBudget` in `Seed.Market`), not necessarily `DevelopmentBudget.Default`. Brain development (`Seed.Development`) takes a `DevelopmentBudget` for compilation.

### `RuntimeBudget`

| Field | Constructor default | `RuntimeBudget.Default` |
|-------|---------------------|---------------------------|
| `MaxTicksPerEpisode` | `1500` | `1500` |
| `MicroStepsPerTick` | `3` | `3` |

**Usage:** Intended for episode-limited evaluation (`EvaluationContext`). The current `Seed.Market` project does not reference `RuntimeBudget`; it remains part of Core and `AllBudgets` for the terrarium-style evaluation contract.

### `PopulationBudget`

| Field | Constructor default | `PopulationBudget.Default` |
|-------|---------------------|------------------------------|
| `PopulationSize` | `128` | `256` |
| `ArenaRounds` | `4` | `6` |
| `ElitesPerSpecies` | `1` | `2` |
| `MinSpeciesSizeForElitism` | `5` | `5` |

**Usage:** `Seed.Market` constructs `PopulationBudget` values when allocating offspring (for example `ArenaRounds: 1`, population size from market config). `Seed.Genetics` speciation uses `PopulationBudget` in APIs.

### `WorldBudget`

Constructor defaults (full parameter list):

| Field | Default |
|-------|---------|
| `WorldWidth` | `64` |
| `WorldHeight` | `64` |
| `ObstacleDensity` | `0.12f` |
| `HazardDensity` | `0.04f` |
| `FoodCount` | `25` |
| `FoodClusters` | `0` |
| `FoodEnergyAmplitude` | `0f` |
| `FoodEnergyPeriod` | `500` |
| `RoundJitter` | `0f` |
| `DayNightPeriod` | `0` |
| `SeasonPeriod` | `0` |
| `AmbientEnergyRate` | `0f` |
| `CorpseEnergyBase` | `0f` |
| `FoodQualityVariation` | `0f` |

`WorldBudget.Default` is `new(64, 64, 0.12f, 0.04f, 25)` (only the first five arguments; the rest use struct default `0` / `0f` where applicable).

**`Jitter(ref Rng64 rng)`:** If `RoundJitter <= 0`, returns `this`. Otherwise returns a new budget with randomized width/height/densities/counts (with floors), and `RoundJitter` forced to `0f` in the result.

**Usage:** Passed into `IWorld.Reset` and `EvaluationContext`. Not used by the market trading path in this repository.

### `ComputeBudget`

| Field | Default | `ComputeBudget.Default` |
|-------|---------|-------------------------|
| `MaxWorkerThreads` | `0` | `new(0)` |

- `EffectiveWorkerCount`: if `MaxWorkerThreads > 0`, that value; else `Math.Max(1, Environment.ProcessorCount)`.

**Usage:** Carried on `AllBudgets`; no other projects in this solution reference `ComputeBudget` outside Core.

### `AllBudgets`

```csharp
public readonly record struct AllBudgets(
    DevelopmentBudget Development,
    RuntimeBudget Runtime,
    PopulationBudget Population,
    WorldBudget World,
    ComputeBudget Compute
)
```

`AllBudgets.Default` sets:

- `Development`: `DevelopmentBudget.Default`
- `Runtime`: `RuntimeBudget.Default`
- `Population`: `PopulationBudget.Default`
- `World`: explicit `WorldBudget` with  
  `WorldWidth: 64`, `WorldHeight: 64`, `ObstacleDensity: 0.12f`, `HazardDensity: 0.04f`, `FoodCount: 25`, `FoodClusters: 3`, `FoodEnergyAmplitude: 0.4f`, `FoodEnergyPeriod: 500`, `RoundJitter: 0.15f`, `DayNightPeriod: 150`, `SeasonPeriod: 1500`, `AmbientEnergyRate: 0.00015f`, `CorpseEnergyBase: 0.3f`, `FoodQualityVariation: 0.1f`
- `Compute`: `ComputeBudget.Default`

**Usage:** `RunConfig` holds `AllBudgets`. Nothing outside `Seed.Core` references `RunConfig` in the current codebase; market runs use `MarketConfig` and related types instead.

### Summary: market vs legacy (this repo)

| Budget / aggregate | Used by market-related code | Notes |
|--------------------|-----------------------------|--------|
| `DevelopmentBudget` | Yes | Custom values in `MarketEvaluator.MarketBrainBudget`; genome fields can override substrate dimensions. |
| `PopulationBudget` | Yes | Built with market config during reproduction. |
| `RuntimeBudget` | No | `EvaluationContext` / terrarium-style evaluation. |
| `WorldBudget` | No | `IWorld` / `EvaluationContext`; terrarium-style. |
| `ComputeBudget` | No | Only via `AllBudgets` / `RunConfig`. |
| `AllBudgets` | No | Only `RunConfig.Default` in Core. |

---

## 4. Contexts

| Type | Definition |
|------|------------|
| `DevelopmentContext` | `readonly record struct DevelopmentContext(ulong RunSeed, int GenerationIndex)` |
| `BrainStepContext` | `readonly record struct BrainStepContext(int Tick)` |
| `BrainLearnContext` | `readonly record struct BrainLearnContext(int Tick, float ElapsedHours = 0f)` |
| `BodyResetContext` | `readonly record struct BodyResetContext(ulong AgentSeed)` |

`ElapsedHours` defaults to `0f`. The market agent passes a non-zero elapsed interval when calling `IBrain.Learn` (see `Seed.Market.Agents.MarketAgent`).

**`EvaluationContext`** (genome evaluation bundle — terrarium-oriented):

```csharp
public readonly record struct EvaluationContext(
    ulong RunSeed,
    int GenerationIndex,
    int WorldBundleKey,
    DevelopmentBudget DevelopmentBudget,
    RuntimeBudget RuntimeBudget,
    WorldBudget WorldBudget,
    int ModulatorCount = 3,
    AblationConfig? Ablations = null,
    int ArenaRounds = 4,
    FitnessConfig? FitnessConfig = null
)
```

- `EffectiveAblations` → `Ablations ?? AblationConfig.Default`
- `EffectiveFitnessConfig` → `FitnessConfig ?? FitnessConfig.Default`

---

## 5. Config records — defaults

### `RunConfig` (master run configuration)

```csharp
public sealed record RunConfig(
    ulong RunSeed,
    int MaxGenerations,
    AllBudgets Budgets,
    SpeciationConfig Speciation,
    MutationConfig Mutation,
    FitnessConfig Fitness,
    AblationConfig Ablations
)
```

| `RunConfig.Default` field | Value |
|---------------------------|--------|
| `RunSeed` | `42UL` |
| `MaxGenerations` | `2000` |
| `Budgets` | `AllBudgets.Default` |
| `Speciation` | `SpeciationConfig.Default` |
| `Mutation` | `MutationConfig.Default` |
| `Fitness` | `FitnessConfig.Default` |
| `Ablations` | `AblationConfig.Default` |

Serialization: `ToJson()`, `FromJson(string)`, `LoadFromFile(string)`, `SaveToFile(string)` using camelCase JSON and `WhenWritingNull` ignore.

### `SpeciationConfig`

| Field | Default |
|-------|---------|
| `C1` | `1.0f` |
| `C2` | `1.0f` |
| `C3` | `0.4f` |
| `CompatibilityThreshold` | `3.0f` |
| `ShareSigma` | `3.0f` |
| `ShareAlpha` | `1.0f` |
| `TournamentSize` | `3` |

### `MutationConfig`

| Field | Default |
|-------|---------|
| `PWeightMutate` | `0.50f` |
| `PWeightReset` | `0.10f` |
| `SigmaWeight` | `0.10f` |
| `WeightResetMax` | `1.00f` |
| `PBiasMutate` | `0.30f` |
| `SigmaBias` | `0.10f` |
| `PAddConn` | `0.05f` |
| `PAddNode` | `0.02f` |
| `WInitMax` | `1.00f` |
| `PParamMutate` | `0.20f` |
| `SigmaParam` | `0.05f` |
| `PCrossover` | `0.35f` |

### `FitnessConfig`

| Field | Default |
|-------|---------|
| `LambdaVar` | `0.10f` |
| `LambdaWorst` | `0.20f` |

### `AblationConfig`

| Field | Default |
|-------|---------|
| `LearningEnabled` | `true` |
| `CuriosityEnabled` | `true` |
| `HomeostasisEnabled` | `true` |
| `EvolutionEnabled` | `true` |
| `RandomActionsEnabled` | `false` |
| `PredictionErrorCuriosity` | `false` |
| `ModulatoryEdgesEnabled` | `true` |
| `SynapticDelaysEnabled` | `true` |
| `RecurrenceEnabled` | `true` |

---

## 6. Parameter records — defaults

### `DevelopmentParams`

| Field | Default |
|-------|---------|
| `TopKInMin` | `6` |
| `TopKInMax` | `16` |
| `MaxOutMin` | `8` |
| `MaxOutMax` | `24` |
| `ConnectionThreshold` | `0.20f` |
| `InitialWeightScale` | `1.0f` |
| `GlobalSampleRate` | `0.02f` |
| `SubstrateWidth` | `16` |
| `SubstrateHeight` | `16` |
| `SubstrateLayers` | `3` |

`DevelopmentParams.Default` is `new()` (same values).

### `LearningParams`

| Field | Default |
|-------|---------|
| `Eta` | `0.01f` |
| `EligibilityDecay` | `0.95f` |
| `AlphaReward` | `1.0f` |
| `AlphaPain` | `-1.0f` |
| `AlphaCuriosity` | `0.25f` |
| `BetaConsolidate` | `0.01f` |
| `GammaRecall` | `0.01f` |
| `CriticalPeriodTicks` | `1000` |
| `CriticalPeriodHours` | `1000f` |

Comment on source: `CriticalPeriodTicks` is marked deprecated in favor of `CriticalPeriodHours`.

### `StabilityParams`

| Field | Default |
|-------|---------|
| `WeightMaxAbs` | `3.0f` |
| `HomeostasisStrength` | `0.01f` |
| `ActivationTarget` | `0.15f` |
| `IncomingNormEps` | `1e-5f` |
| `EnableIncomingNormalization` | `true` |

---

## 7. Helper functions and modulator indices

### `ModulatorIndex` (`RunConfig.cs`)

| Constant | Value |
|----------|--------|
| `Reward` | `0` |
| `Pain` | `1` |
| `Curiosity` | `2` |
| `Count` | `3` |

### `DeterministicHelpers`

| Member | Signature / behavior |
|--------|----------------------|
| `SortedKeys` | `IEnumerable<TKey> SortedKeys<TKey, TValue>(this Dictionary<TKey, TValue> dict) where TKey : notnull, IComparable<TKey>` — copies keys, sorts, yields keys. |
| `SortedEntries` | `IEnumerable<KeyValuePair<TKey, TValue>> SortedEntries<...>(...)` — key-sorted entries. |
| `StableOrderByGuid` | `IOrderedEnumerable<T> StableOrderByGuid<T>(this IEnumerable<T> source, Func<T, Guid> selector)` — `OrderBy(selector(x))`. |
| `StableOrderBy` | `IOrderedEnumerable<T> StableOrderBy<T, TKey1, TKey2>(..., Func<T, TKey1>, Func<T, TKey2>)` — `OrderBy` then `ThenBy`. |
| `DeterministicShuffle` | Overloads for `T[]` and `List<T>`: Fisher–Yates with `rng.NextInt(i + 1)`. |
| `DeterministicSample` | `T[] DeterministicSample<T>(this T[] source, int count, ref Rng64 rng)` — partial Fisher–Yates; if `count >= source.Length`, returns `source.ToArray()`. |
| `Clamp` | `float Clamp(float value, float min, float max)` |
| `Clamp` | `int Clamp(int value, int min, int max)` |
| `ComputeFitnessScore` | `float ComputeFitnessScore(float mean, float variance, float worst, float lambdaVar = 0.10f, float lambdaWorst = 0.20f)` — `mean - lambdaVar * Sqrt(variance) + lambdaWorst * worst`. |
| `ComputeEpisodeFitness` | Uses survival fraction, `netEnergyDelta`, `instabilityPenalty`, `maxTicks`; returns `0f` if result is NaN or infinity. |
| `AggregateFitness` | `FitnessAggregate AggregateFitness(ReadOnlySpan<EpisodeMetrics> episodes, float lambdaVar = 0.10f, float lambdaWorst = 0.20f)` — empty span → `(0,0,0,0)`; else mean/variance/worst and `ComputeFitnessScore`. |

Source comment: do not rely on dictionary iteration order; use `SortedKeys` / `SortedEntries` where order matters.

---

## 8. Legacy / terrarium-oriented types (`Contexts.cs`)

These types support the previous terrarium simulation and evaluation pipeline, which is not part of the main application path documented here; they remain in Core for serialization and helper code.

| Type | Role |
|------|------|
| `BodyState` | `readonly record struct BodyState(float Energy, bool Alive)` |
| `WorldSignals` | `readonly record struct WorldSignals(float EnergyDelta, int FoodCollectedThisStep, float HazardPenalty)` |
| `WorldStepResult` | `readonly record struct WorldStepResult(bool Done, WorldSignals Signals, float[] Modulators, WorldStepInfo Info)` — modulator array described in source as `[Reward, Pain, Curiosity]`. |
| `WorldStepInfo` | `readonly record struct WorldStepInfo(int Reserved0 = 0, int Reserved1 = 0)` |
| `EpisodeMetrics` | `readonly record struct EpisodeMetrics(int SurvivalTicks, float NetEnergyDelta, int FoodCollected, float EnergySpent, float InstabilityPenalty, float DistanceTraveled, float Fitness)` |
| `FitnessAggregate` | `readonly record struct FitnessAggregate(float MeanFitness, float VarianceFitness, float WorstFitness, float Score)` |
| `GenomeEvaluationResult` | `sealed record GenomeEvaluationResult(Guid GenomeId, IGenome Genome, EpisodeMetrics[] PerWorld, FitnessAggregate Aggregate)` |

`DeterministicHelpers.ComputeEpisodeFitness` and `AggregateFitness` operate on these metrics shapes.

---

## File map (`src/Seed.Core`)

| File | Contents |
|------|----------|
| `Interfaces.cs` | `IGenome`, `MutationContext`, `IInnovationTracker`, `IBrain`, `IBrainGraph`, `IAgentBody`, `IWorld`, `IObservatory` |
| `Contexts.cs` | Context structs, `EvaluationContext`, observatory enums/events, legacy world/body metrics |
| `Budgets.cs` | All budget structs and `AllBudgets` |
| `Parameters.cs` | `DevelopmentParams`, `LearningParams`, `StabilityParams`, `SpeciationConfig`, `MutationConfig`, `FitnessConfig`, `AblationConfig` |
| `RunConfig.cs` | `RunConfig`, `ModulatorIndex`, JSON helpers |
| `Rng64.cs` | `Rng64`, `SplitMix64` |
| `SeedDerivation.cs` | Domain constants and seed helpers |
| `DeterministicHelpers.cs` | Deterministic collection and fitness helpers |
