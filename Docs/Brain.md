# Seed.Development and Seed.Brain

This document describes the **development pipeline** (`Seed.Development`) that compiles a CPPN genome into a sparse `BrainGraph`, and the **runtime** (`Seed.Brain`) that executes and trains that graph. Types and default parameter records live primarily in `Seed.Core` (`DevelopmentBudget`, `LearningParams`, `StabilityParams`, `AblationConfig`, modulator indices); the genome type is `Seed.Genetics.SeedGenome`.

---

## 1. Development pipeline

**Entry point:** `BrainDeveloper(sensorCount, actuatorCount).CompileGraph(SeedGenome genome, in DevelopmentBudget budget, in DevelopmentContext ctx)` in `src/Seed.Development/BrainDeveloper.cs`.

### Node layout

- **Input nodes** (`BrainNodeType.Input`): count = `sensorCount`. `Layer = 0`. `X = 0f`. `Y = i / max(1, sensorCount - 1)`.
- **Hidden nodes** (`BrainNodeType.Hidden`): a 3D grid of size **`HiddenWidth × HiddenHeight × HiddenLayers`**. For each `layer` in `0 .. HiddenLayers-1`, each `(x, y)` in the grid, a node is created with `Layer = layer + 1`, `X = x / max(1, HiddenWidth - 1)`, `Y = y / max(1, HiddenHeight - 1)`.
- **Output nodes** (`BrainNodeType.Output`): count = `actuatorCount`. `Layer = HiddenLayers + 1`. `X = 1f`. `Y = i / max(1, actuatorCount - 1)`.

So inputs occupy **layer 0**, hidden units occupy **layers `1 .. HiddenLayers`**, and outputs occupy **layer `HiddenLayers + 1`**. `NodeId` is assigned sequentially in that order (inputs, then hidden grid iteration order, then outputs).

### Edges and graph structure

For every **non-input** destination node, the compiler queries the CPPN for candidate sources, filters and ranks connections, applies weight and metadata rules, and appends edges to `incoming[dstNodeId]`. The result is a `BrainGraph` with `IncomingByDst` adjacency lists. Outgoing counts enforce `MaxOut` per source.

`DevelopmentContext` supplies **`RunSeed`** (used for deterministic global sampling; **`GenerationIndex` is not read** by `CompileGraph`).

---

## 2. Edge candidate selection

Implemented in `GetCandidateSources` (`BrainDeveloper.cs`).

### Local neighborhood

For each candidate source node (excluding a special case below):

- **Outputs are never sources:** nodes with `BrainNodeType.Output` are skipped in the outer loop (`continue`).

Scaled planar distance and layer nearness:

- `dx = |dst.X - node.X| * HiddenWidth`
- `dy = |dst.Y - node.Y| * HiddenHeight`
- `layerDiff = |dst.Layer - node.Layer|`

A node is in the **local** set if:

- `dx <= LocalNeighborhoodRadius`
- `dy <= LocalNeighborhoodRadius`
- `layerDiff <= 1`

### Global random supplement

- RNG: `Rng64(SeedDerivation.DevelopmentSeed(ctx.RunSeed, dst.NodeId))`.
- **Eligible** nodes for global sampling: all nodes that are **not** `Output`, and **not** already in the local candidate set.
- From that array, **`GlobalCandidateSamplesPerNeuron`** items are drawn **without replacement** via `DeterministicSample` (partial Fisher–Yates shuffle; if `count >= source.Length`, the full shuffled copy is returned truncated in the `count < length` branch—see `DeterministicHelpers.DeterministicSample` in `Seed.Core`).

The final candidate list is the union of local and global IDs, then resolved to `BrainNode` instances via `allNodes.Where(n => candidates.Contains(n.NodeId))` (enumeration order follows `allNodes`).

### Self-edges

The local set can include the destination itself; when iterating candidates, **`srcNode.NodeId == dstNode.NodeId` is skipped** before querying the CPPN.

---

## 3. CPPN query to edge

For each candidate source `src` and destination `dst`:

**Inputs to the CPPN** (`CppnInputIndex`, 9 values):

| Index | Meaning |
|------|---------|
| `Xi`, `Yi` | Source `X`, `Y` |
| `Li` | `(float)src.Layer / (HiddenLayers + 1)` |
| `Xj`, `Yj` | Destination `X`, `Y` |
| `Lj` | `(float)dst.Layer / (HiddenLayers + 1)` |
| `Dx`, `Dy` | `dst.X - src.X`, `dst.Y - src.Y` |
| `Dist` | `sqrt(Dx² + Dy²)` |

**Outputs** (`CppnOutputIndex`): `C`, `W`, `Delay`, `Tau`, `ModuleTag`, `Gate` at indices `0..5`. If the evaluated array is shorter, `Tau`, `Gate`, and `Delay` default to `0f` when missing.

**Connection test:**

- `connScore = outputs[C]`
- Edge is a candidate iff `connScore >= genome.Dev.ConnectionThreshold` (`DevelopmentParams.ConnectionThreshold`, default **0.20f**).

**Initial fast/slow weight:**

- `w0 = Clamp(weight * genome.Dev.InitialWeightScale, -genome.Stable.WeightMaxAbs, +genome.Stable.WeightMaxAbs)`
- Default `InitialWeightScale = 1.0f`; default `WeightMaxAbs = 3.0f` (`StabilityParams`).

**Ranking and caps:**

- Candidates are sorted by **`score` descending**, then **`srcId` ascending**.
- Edges are taken in that order until **`taken >= TopKIn`**.
- Each source skips if **`outgoingCount[srcId] >= MaxOut`** before accepting another outgoing edge.

**Incoming list order after selection:** edges for that destination are finally sorted by **`SrcNodeId` ascending** (not by score).

---

## 4. Edge metadata

Constants in `CompileGraph`:

- `ModulatoryGateThreshold = 0.7f`
- `DelayActivationThreshold = 0.3f`

**Edge type and plasticity gain** (from CPPN `Gate` output, `gate` in code):

- `EdgeType = gate > 0.7f ? Modulatory : Normal`
- `PlasticityGain = 2 / (1 + exp(-gate))`

**Delay** (from CPPN `Delay` output, `delay` in code):

- If `delay > 0.3f` **and** `MaxSynapticDelay > 0`:
  - `fraction = (delay - 0.3f) / (1f - 0.3f)`  // i.e. linear map from `(0.3, 1]` to `(0, 1]` for `delay` in `(0.3, 1]`
  - `delayTicks = Round(fraction * MaxSynapticDelay)` then clamped to **`[1, MaxSynapticDelay]`** via `Math.Clamp`
- Otherwise `delayTicks = 0`

`EdgeMetadata` stores `EdgeType` and `Delay` (ticks).

---

## 5. Time constants (tau → leaky integration)

For each **accepted** incoming edge, the raw CPPN **`Tau`** output is accumulated: `tauAccum += tau`, `tauCount++` per edge added.

For each **non-input** node:

- If `tauCount > 0`: `tauAvg = tauAccum / tauCount`
- `sig = 1 / (1 + exp(-tauAvg))`  // logistic sigmoid
- **`tc = 1.0f + sig * 9.0f`** → range **`[1, 10]`**

If there are **no** incoming edges with accumulated tau, **`tc` defaults to `1.0f`** (comment in code: “instant / same as current behavior”).

Stored as `NodeMetadata.TimeConstant` on the node.

---

## 6. `BrainGraph` types (`BrainGraphTypes.cs`)

### `BrainNodeType` (enum)

- `Input = 0`, `Hidden = 1`, `Output = 2`

### `EdgeType` (enum)

- `Normal = 0`, `Modulatory = 1`, `Memory = 2`  
  (`Memory` is reserved; the developer only sets `Normal` or `Modulatory`.)

### `NodeMetadata` (record)

- `RegionId` (default 0), `ModuleId` (0), `TimeConstant` (0f in record default; **overwritten in development** for non-input nodes), `PlasticityProfileId` (0)

### `EdgeMetadata` (record)

- `EdgeType` (default `Normal`), `Delay` (integer ticks, 0 = no delay), `PlasticityProfileId` (0)

### `BrainNode` (record)

- `NodeId`, `Type`, `X`, `Y`, `Layer`, `Meta`

### `BrainEdge` (record)

- `SrcNodeId`, `DstNodeId`, `WSlow`, `WFast`, `PlasticityGain`, `Meta`

### `BrainGraph` (class)

- `Nodes`, `IncomingByDst` (`Dictionary<int, List<BrainEdge>>`), `InputCount`, `OutputCount`, `ModulatorCount` (set from `ModulatorIndex.Count` = **3** at compile time), optional `Reserved` JSON placeholder
- Implements `IBrainGraph` (`Seed.Core`)

---

## 7. `BrainRuntime` mechanics (`BrainRuntime.cs`)

Constructor parameters: `BrainGraph graph`, `LearningParams learn`, `StabilityParams stable`, `microStepsPerTick` default **3**, `AblationConfig` default `AblationConfig.Default`.

### Micro-steps per tick

In `Step`:

- `steps = ablation.RecurrenceEnabled ? microStepsPerTick : 1`  
  So with **recurrence disabled**, exactly **one** activation update per tick regardless of the `microStepsPerTick` argument.

### Activation update (one micro-step)

For each node that is **not** `Input`:

1. **Homeostatic scale** (if enabled, see §9): multiplies incoming weights before summation.
2. **Incoming normalization** (if `StabilityParams.EnableIncomingNormalization` and `edges.Count > 0`):
   - `sumSq = Σ w_fast[i]²`
   - `rms = sqrt(sumSq / edges.Count + IncomingNormEps)` (default `IncomingNormEps = 1e-5f`)
   - `normFactor = 1 / rms`
   - Each effective weight is `w_fast[offset+i] * scale * normFactor`.

3. **Delayed input:** if ablation delays are on and `Meta.Delay > 0` and `maxDelay > 0`, source activity is read from a **ring buffer** of past activations; index  
   `histIdx = ((historyHead - delay) % length + length) % length`  
   on `_activationHistory[histIdx][srcId]`. Otherwise `srcAct = _activations[srcId]`.

4. **Modulatory vs normal:** if `ModulatoryEdgesEnabled` and `Meta.EdgeType == Modulatory`, contribution goes to **`modSum`**; else to **`sum`**.

5. **`localModulation[dst] = ModulatoryEdgesEnabled ? Tanh(modSum) : 0`**

6. **`raw = Tanh(sum)`**

7. **Leaky integration:** `t = TimeConstant` for the node (`_tau[nodeId]` from graph metadata).  
   - If `t > 1f`: `_activations[dst] = (1 - 1/t) * _prevActivations[dst] + (1/t) * raw`  
   - Else: `_activations[dst] = raw`

Input activations are copied from the `inputs` span at the start of `Step` (length clamped by input count). After all micro-steps, delay history is updated by copying the current `_activations` into `_activationHistory[_historyHead]` and advancing `historyHead`.

### Outputs

Returns a span over output neurons: starting index  
`outputStart = InputCount + (NodeCount - InputCount - OutputCount)`  
(i.e. first output node id), length `OutputCount`.

---

## 8. Learning (`Learn`)

Skipped entirely if `!ablation.LearningEnabled`.

### Global modulator `M`

From `modulators` span and `LearningParams`:

- `M = AlphaReward * modulators[Reward] + AlphaPain * modulators[Pain] + AlphaCuriosity * modulators[Curiosity]`  
  when each index exists (`Length >` index).  
  Indices: `ModulatorIndex.Reward = 0`, `Pain = 1`, `Curiosity = 2`.

Defaults: `AlphaReward = 1`, `AlphaPain = -1`, `AlphaCuriosity = 0.25`.

### Learning rate scale (critical period)

- If `CriticalPeriodHours > 0`:  
  `etaScale = Max(0.1f, 1f - BrainLearnContext.ElapsedHours / CriticalPeriodHours)`  
- Else: `etaScale = 1f`

### Eligibility and weight update

For each non-input node and each incoming edge `i`:

- `product = Clamp(ai * aj, -1, 1)` with `ai` = source activation, `aj` = destination activation
- `eligibility[i] = EligibilityDecay * eligibility[i] + product` (default `EligibilityDecay = 0.95`)

**Effective modulator** when `ModulatoryEdgesEnabled`:

- If edge is **Modulatory:** `effectiveM = M`
- If edge is **Normal:** `effectiveM = M * (1 + localMod)` where `localMod = localModulation[dst]`

When modulatory edges are **disabled**, `effectiveM = M` for all edges.

**Delta fast weight:**

- `dw = Eta * etaScale * effectiveM * eligibility[i] * PlasticityGain`  
- NaN/Infinity → `0`; then clamp fast weight to `[-WeightMaxAbs, +WeightMaxAbs]`.

### Slow / fast consolidation

After all edges are updated, for each edge index `i`:

- `wSlow = (1 - BetaConsolidate) * wSlow + BetaConsolidate * wFast` (default `BetaConsolidate = 0.01`)
- `wFast = (1 - GammaRecall) * wFast + GammaRecall * wSlow` (default `GammaRecall = 0.01`)

NaNs on slow/fast are replaced with `0f`.

---

## 9. Homeostasis

When **`HomeostasisEnabled`**, `HomeostasisStrength > 0`, and `_meanAbsActivation[nodeId] > 0`:

- `diff = meanAbsActivation[nodeId] - ActivationTarget` (default target **0.15**)
- `scale = exp(-HomeostasisStrength * diff)` (default strength **0.01**)

That `scale` multiplies **fast** weights for incoming sums (same as normalization path). If homeostasis is disabled or strength is 0, `scale = 1f`.

---

## 10. Stability tracking

In `TrackStability` (called from `Step` after activation updates):

- `saturationThreshold = 0.95f`
- `emaDecay = 0.99f`
- For each non-input node:  
  `meanAbsActivation[nodeId] = emaDecay * meanAbsActivation[nodeId] + (1 - emaDecay) * |activation|`
- Counts `totalActivations++` and if `|activation| > 0.95f`, `saturatedCount++`.

`GetInstabilityPenalty()` returns `(float)saturatedCount / totalActivations`, or `0` if `totalActivations == 0`.

Diagnostics also use saturation with threshold **0.95f** when reporting `SaturationRate`.

---

## 11. `AblationConfig` flags

Defined in `Seed.Core/Parameters.cs`. Effects **in `BrainRuntime`**:

| Flag | When false / effect |
|------|---------------------|
| `LearningEnabled` | `Learn` returns immediately; no plasticity. |
| `HomeostasisEnabled` | Homeostatic `scale` is not applied (treated as 1). |
| `ModulatoryEdgesEnabled` | Modulatory contributions are folded into the main sum (`modSum` unused for gating); `localModulation` is 0; `effectiveM` is always `M`. |
| `SynapticDelaysEnabled` | Delays ignored; always use current `_activations[src]`. |
| `RecurrenceEnabled` | Only **one** micro-step per `Step` (no multi-step settling). |

Flags **not referenced by `BrainRuntime` or `BrainDeveloper`** in this repository:

| Flag | Notes |
|------|--------|
| `CuriosityEnabled` | Used in `Seed.Market.Agents.MarketAgent`: when false, curiosity modulator is forced to **0** before `Learn`. |
| `EvolutionEnabled` | No references outside `Parameters.cs` (unused in current code). |
| `RandomActionsEnabled` | No references outside `Parameters.cs`. |
| `PredictionErrorCuriosity` | No references outside `Parameters.cs`. |

---

## 12. Key distinction: rate-based dynamics, not spiking

`BrainRuntime` implements **continuous-valued activations** with **tanh** nonlinearities, **optional leaky integration** controlled by `TimeConstant` in `[1, 10]`, and **synaptic delays** implemented as discrete-time taps on past activations. There are **no** spike times, refractory periods, or integrate-and-fire thresholds. This is a **continuous rate-based** recurrent network with modulated three-factor–style learning (global `M` × eligibility × `PlasticityGain`), not a spiking neural network simulator.

---

## Default budget snapshot (`DevelopmentBudget`)

From `Seed.Core/Budgets.cs`, `DevelopmentBudget.Default`:

- `HiddenWidth = 12`, `HiddenHeight = 12`, `HiddenLayers = 2`
- `TopKIn = 12`, `MaxOut = 16`
- `LocalNeighborhoodRadius = 2`
- `GlobalCandidateSamplesPerNeuron = 16`
- `MaxSynapticDelay = 5`
