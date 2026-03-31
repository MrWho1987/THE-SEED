# Seed.Genetics — Genome and CPPN documentation

This document describes the **Seed.Genetics** library: CPPN structure, evaluation, `SeedGenome`, mutation, crossover, NEAT distance, speciation, innovation tracking, and JSON formats. Constants and behavior are taken from the implementation in `src/Seed.Genetics` and the shared parameter types in `src/Seed.Core` (referenced by genetics).

---

## 1. CPPN structure

### 1.1 `CppnNodeType`

| Value | Name |
|-------|------|
| 0 | `Input` |
| 1 | `Hidden` |
| 2 | `Output` |

### 1.2 `ActivationFn`

| Value | Name | `Activate(x)` |
|-------|------|----------------|
| 0 | `Identity` | `x` |
| 1 | `Tanh` | `MathF.Tanh(x)` |
| 2 | `Sigmoid` | `1f / (1f + MathF.Exp(-x))` |
| 3 | `Sin` | `MathF.Sin(x)` |
| 4 | `Gauss` | `MathF.Exp(-x * x)` |
| (other) | — | falls through to `x` (same as identity) |

### 1.3 `CppnNode`

Record: `NodeId`, `Type`, `Activation`, `Bias`. Method `Activate(float x)` dispatches on `Activation` as in the table above.

### 1.4 `CppnConnection`

Record fields:

- `InnovationId` — NEAT alignment key.
- `SrcNodeId`, `DstNodeId` — endpoints.
- `Weight` — scalar weight.
- `Enabled` — if `false`, the connection is omitted from the evaluation graph.

### 1.5 `CppnNetwork`

Record: `Nodes`, `Connections`, `NextNodeId`.

Evaluation caches (built lazily in `EnsureCached()`): activation buffer, per-destination incoming lists `(srcIdx, weight)`, topological order (or `null` if cyclic), indices of non-input and output nodes, and `NodeId` → array index map.

**`CreateMinimal(int inputCount, int outputCount, Rng64 rng)`**

- Creates `inputCount` input nodes with ids `0 .. inputCount-1`, type `Input`, `Identity` activation, bias `0f`.
- Creates `outputCount` output nodes with ids `inputCount .. inputCount+outputCount-1`, type `Output`, `Tanh` activation, bias `0f`.
- Adds one enabled connection from every input to every output with weight `rng.NextFloat(-1f, 1f)` and innovation ids assigned sequentially starting at `0`.
- Returns `NextNodeId = inputCount + outputCount` (the next id that would be used for a new node).

---

## 2. CPPN inputs (`CppnInputIndex`)

Nine geometric inputs for a candidate edge (source *i* → destination *j*). `Count = 9`.

| Constant | Index | Meaning (from code comments) |
|----------|-------|-------------------------------|
| `Xi` | 0 | Source X coordinate |
| `Yi` | 1 | Source Y coordinate |
| `Li` | 2 | Source layer |
| `Xj` | 3 | Destination X coordinate |
| `Yj` | 4 | Destination Y coordinate |
| `Lj` | 5 | Destination layer |
| `Dx` | 6 | Delta X |
| `Dy` | 7 | Delta Y |
| `Dist` | 8 | Euclidean distance |

Naming note: destination coordinates are `Xj`, `Yj`, `Lj` in code (not `Xo`/`Yo`/`Lo`). Deltas are `Dx`/`Dy` (not `DeltaX`/`DeltaY`).

---

## 3. CPPN outputs (`CppnOutputIndex`)

Six outputs. `Count = 6`.

| Constant | Index | Role |
|----------|-------|------|
| `C` | 0 | Connection score |
| `W` | 1 | Initial weight |
| `Delay` | 2 | Reserved for V2 |
| `Tau` | 3 | Reserved for V2 |
| `ModuleTag` | 4 | Reserved for V2 |
| `Gate` | 5 | Reserved for V2 |

---

## 4. Minimal genome (fully connected 9→6)

From `CppnNetwork.CreateMinimal(CppnInputIndex.Count, CppnOutputIndex.Count, rng)`:

- **Nodes:** ids `0–8` (inputs), `9–14` (outputs). No hidden nodes.
- **Connections:** `9 × 6 = 54`, fully connected from each input to each output.
- **Innovation IDs:** `0` through `53` (inclusive), assigned in nested loop order: for each `src` in `0..8`, for each `dst` in `9..14`, one connection with the next innovation id.
- **`NextNodeId`:** `15` (same as `inputCount + outputCount`).

**Innovation tracker default** (`InnovationTracker.CreateDefault()`): `initialNextInnovationId = 54` (first id for *new* connections after the minimal pattern), `initialNextCppnNodeId = 15` (first id for a new hidden node after the minimal pattern).

---

## 5. CPPN evaluation (`CppnNetwork.Evaluate`)

1. **`EnsureCached()`** builds incoming adjacency by **destination** (only **enabled** connections; both endpoints must exist in the node list).

2. **Inputs:** For each node, if type is `Input`, the next value from the `inputs` span is written; if the span is exhausted, `0f` is used. Non-input activations are cleared to `0` before propagation.

3. **Acyclic case** (`TryTopologicalSort` succeeds):  
   - Topological order is computed with **Kahn’s algorithm** over **non-input** nodes only.  
   - An edge from non-input `src` to non-input `dst` contributes to `dst`’s in-degree.  
   - Nodes with in-degree `0` are queued; processing updates in-degrees for edges where the processed node is the source.  
   - **Single forward pass:** for each index in topological order, `sum = Bias + Σ(act[src] * weight)`, then `act[idx] = Activate(sum)`.

4. **Cyclic case** (`TryTopologicalSort` returns `null`):  
   - Up to **10** iterations.  
   - Each iteration updates **all non-input** nodes in array index order: same sum and activation as above.  
   - Tracks `maxDelta = max |newVal - act[idx]|` over those updates.  
   - Stops early if `maxDelta < 1e-4f`.  
   - After the loop, outputs are read from output node indices.

5. **Outputs:** Values at all nodes marked `Output` are copied to the returned `float[]` in the order those output indices appear in the node list.

---

## 6. `SeedGenome`

Record in `Seed.Genetics`:

| Field | Type |
|-------|------|
| `GenomeId` | `Guid` |
| `Cppn` | `CppnNetwork` |
| `Dev` | `DevelopmentParams` |
| `Learn` | `LearningParams` |
| `Stable` | `StabilityParams` |
| `Reserved` | `ReservedGenomeFields` |

**`GenomeType`** (string property on `IGenome`): `"SeedGenome.CPPN.NEAT"`.

**`ReservedGenomeFields`:** `CppnOutputNames` (default `["c", "w", "delay", "tau", "module_tag", "gate"]`), `ReservedMutationScales` (default four floats, all `1.0f`).

**`DevelopmentParams`** (defaults from `Seed.Core`): `TopKInMin` 6, `TopKInMax` 16, `MaxOutMin` 8, `MaxOutMax` 24, `ConnectionThreshold` 0.20, `InitialWeightScale` 1.0, `GlobalSampleRate` 0.02, `SubstrateWidth`/`SubstrateHeight` 16, `SubstrateLayers` 3.

**`LearningParams`:** `Eta` 0.01, `EligibilityDecay` 0.95, `AlphaReward` 1.0, `AlphaPain` -1.0, `AlphaCuriosity` 0.25, `BetaConsolidate` 0.01, `GammaRecall` 0.01, `CriticalPeriodTicks` 1000, `CriticalPeriodHours` 1000f.

**`StabilityParams`:** `WeightMaxAbs` 3.0, `HomeostasisStrength` 0.01, `ActivationTarget` 0.15, `IncomingNormEps` 1e-5, `EnableIncomingNormalization` true.

**`CreateRandom`:** `CreateMinimal(9, 6, rng)` plus default `Dev`/`Learn`/`Stable`/`Reserved`, new `GenomeId`.

---

## 7. Mutation (`SeedGenome.Mutate`)

Uses `MutationContext`: `Config` (`MutationConfig`), `Innovations` (`IInnovationTracker`), `Rng`, etc. Default `MutationConfig` values:

| Field | Default |
|-------|---------|
| `PWeightMutate` | 0.50 |
| `PWeightReset` | 0.10 |
| `SigmaWeight` | 0.10 |
| `WeightResetMax` | 1.00 |
| `PBiasMutate` | 0.30 |
| `SigmaBias` | 0.10 |
| `PAddConn` | 0.05 |
| `PAddNode` | 0.02 |
| `WInitMax` | 1.00 |
| `PParamMutate` | 0.20 |
| `SigmaParam` | 0.05 |

Algorithm (order matters):

1. **Weight mutation (every connection):** With probability `PWeightMutate`, update weight: with probability `PWeightReset`, set to `Uniform(-WeightResetMax, WeightResetMax)`; else add Gaussian noise `N(0, SigmaWeight)`.

2. **Bias mutation (every node):** For each node with `Type != Input`, with probability `PBiasMutate`, set `Bias += N(0, SigmaBias)`.

3. **Add connection:** With probability `PAddConn`, call `TryAddConnection` (at most **20** attempts):
   - Pick random `src` from **all** nodes, random `dst` from **non-input** nodes; skip if `src == dst`.
   - If a connection `(src, dst)` exists and is disabled, **enable** it and **continue** to the next attempt (does not return early).
   - If it exists and is enabled, skip attempt.
   - Otherwise allocate innovation via `GetOrCreateConnectionInnovation(src, dst)`, weight `Uniform(-WInitMax, WInitMax)`, `Enabled = true`, append and **return**.

4. **Add node:** With probability `PAddNode`, call `TryAddNode`:
   - Uniform random **enabled** connection; if none, return.
   - `GetOrCreateSplitInnovation(conn.InnovationId)` yields `(newNodeId, innovSrcToNew, innovNewToDst)`.
   - If `newNodeId` already exists in `Nodes`, **return** (no duplicate node).
   - Disable the chosen connection; append hidden node `(newNodeId, Hidden, Tanh, bias 0)`; add connection `(innovSrcToNew, src, newNodeId, weight 1.0, enabled)` and `(innovNewToDst, newNodeId, dst, original weight, enabled)`.

5. **Development params:** With probability `PParamMutate`, `MutateDevelopmentParams` — each field updated as below.

6. **Learning params:** With probability `PParamMutate`, `MutateLearningParams`.

7. **Stability params:** With probability `PParamMutate`, `MutateStabilityParams`.

8. Return new `SeedGenome` with **new** `GenomeId`, mutated copies, **`Reserved` unchanged** (same reference as source).

**Development mutation clamping** (`SigmaParam` from config; substrate steps use fixed probabilities):

- `ConnectionThreshold`: add `N(0, SigmaParam)`, clamp to `[0.01, 0.9]`.
- `InitialWeightScale`: add `N(0, SigmaParam)`, clamp to `[0.1, 3]`.
- `GlobalSampleRate`: add `N(0, SigmaParam * 0.1)`, clamp to `[0.001, 0.1]`.
- `SubstrateWidth` / `SubstrateHeight`: with probability `0.1`, add `-2` or `+2` (50/50); then clamp to `[4, 32]`.
- `SubstrateLayers`: with probability `0.05`, add `-1` or `+1` (50/50); then clamp to `[1, 5]`.

**Learning mutation:**

- `Eta`: `+ N(0, SigmaParam * 0.1)`, clamp `[0.0001, 0.1]`.
- `EligibilityDecay`: `+ N(0, SigmaParam * 0.1)`, clamp `[0.8, 0.999]`.
- `AlphaCuriosity`: `+ N(0, SigmaParam)`, clamp `[0, 1]`.

**Stability mutation:**

- `WeightMaxAbs`: `+ N(0, SigmaParam)`, clamp `[1, 10]`.
- `HomeostasisStrength`: `+ N(0, SigmaParam * 0.1)`, clamp `[0, 0.1]`.
- `ActivationTarget`: `+ N(0, SigmaParam)`, clamp `[0.05, 0.5]`.

---

## 8. Crossover (`SeedGenome.Crossover`)

Inputs: `fitter`, `other`, `ref Rng64 rng`.

**Connections:** Sort both parents’ connections by `InnovationId`. Merge like NEAT:

- **Matching** (`InnovationId` equal): choose `gF` or `gO` with probability `0.5` each. Start with `enabled = chosen.Enabled`. If **either** parent’s gene is disabled, set `enabled = (rng.NextFloat01() >= 0.75f)` — i.e. **75% probability the child gene is disabled**, **25% probability enabled** when at least one parent had the gene disabled.
- **Disjoint / excess:** If `gF.InnovationId < gO.InnovationId`, take `gF` and advance `iF`; if greater, **discard** `gO` and advance `iO`. After one list ends, append **remaining genes from the fitter only**; the weaker parent’s excess is discarded.

**Nodes:**  
- All **input and output** nodes from **fitter** are copied to the child.  
- **Hidden** nodes from fitter whose `NodeId` appears in any selected connection: if that id exists in `other` and `rng.NextFloat01() < 0.5`, use `other`’s node; else fitter’s.  
- **Hidden** nodes only in `other` but referenced by a selected connection and not in fitter: add from `other`.  
- Duplicate `NodeId` in a parent list is handled by first-wins grouping when building lookup maps.

**Prune:** Remove connections whose `SrcNodeId` or `DstNodeId` is missing from the child’s node set.

**`NextNodeId`:** `max(fitter.Cppn.NextNodeId, other.Cppn.NextNodeId)`.

**Substrate:** `SubstrateWidth`, `SubstrateHeight`, `SubstrateLayers` each independently from fitter or other with probability `0.5`.

**`LearningParams` / `StabilityParams`:** Entire record from fitter or other with probability `0.5` each (not field-by-field).

**`Reserved`:** Taken from **fitter**.

---

## 9. NEAT distance (`SeedGenome.DistanceTo` / `ComputeNeatDistance`)

Connections sorted by `InnovationId`. Two-pointer scan:

- **Matching:** accumulate `sumDiff += |wA - wB|`, increment `matchCount`.
- **Disjoint:** count when innovation ids differ (one pointer advances).
- **Excess:** after one list ends, `excess = (remaining in A) + (remaining in B)`.

Let `avgWeight = matchCount > 0 ? sumDiff / matchCount : 0`.

**Formula:** With `n = 1` (hard-coded):

`distance = C1 * excess / n + C2 * disjoint / n + C3 * avgWeight`

Default `SpeciationConfig`: `C1 = 1.0`, `C2 = 1.0`, `C3 = 0.4`, `CompatibilityThreshold = 3.0`, plus `ShareSigma = 3.0`, `ShareAlpha = 1.0`, `TournamentSize = 3`.

If `other` is not a `SeedGenome`, `DistanceTo` returns `float.MaxValue`.

---

## 10. Speciation (`SpeciationManager`)

**`Speciate`:** Clears member lists; assigns each genome in **input order** to the first existing species (ordered by `SpeciesId`) whose representative satisfies `DistanceTo(representative, config) < CompatibilityThreshold`; otherwise creates a new species. Empty species removed. Representative becomes the member with **minimum `GenomeId`** (lexicographic order of `Guid` sort).

**`ComputeAdjustedFitness(genome, rawFitness, config)`:** If the genome is in species `S` with `|S| > 0`, returns `rawFitness / |S|`; else `rawFitness`.

**`AllocateOffspring`:** Per species, adjusted fitness per member is `rawFit / memberCount`; species sums `AdjustedFitnessSum`. Floor minimum per species (if `Members.Count >= 2` and `minOffspringPerSpecies > 0`). Remaining slots split **proportionally** to `AdjustedFitnessSum`; remainder goes to species with highest `AdjustedFitnessSum` one slot at a time; may trim from lowest-fitness species if over-allocated. Edge cases: if `totalAdjusted <= 0` or `remaining == 0`, remaining budget split evenly across species when `remaining > 0`.

**Unused config fields:** `ShareSigma` and `ShareAlpha` on `SpeciationConfig` are **not** read in `SpeciationManager` (sharing is purely divide-by-species-size as above).

---

## 11. Innovation tracking (`InnovationTracker`)

Implements `IInnovationTracker`:

- **`_nextInnovationId`**, **`_nextCppnNodeId`** — monotonic counters.
- **`_connInnov`:** `Dictionary<ConnectionKey, int>` with `ConnectionKey = (SrcNodeId, DstNodeId)` — first use allocates the next innovation id.
- **`_splitInnov`:** `Dictionary<int, SplitInnovation>` keyed by **old connection innovation id**; value holds `NewNodeId`, `InnovSrcToNew`, `InnovNewToDst`. Reusing the same old innovation id returns the **same** triple (deterministic structural alignment).

**`GetOrCreateSplitInnovation`:** If missing, allocates `NewNodeId = _nextCppnNodeId++`, then `InnovSrcToNew = _nextInnovationId++`, `InnovNewToDst = _nextInnovationId++`, stores and returns.

Construction must be **single-threaded** and in a deterministic reproduction order (as stated in source comments).

---

## 12. JSON schema

Serialization uses **camelCase** JSON property names (`JsonNamingPolicy.CamelCase`), indented.

### 12.1 `Seed.Genome` (v1)

Root object (`GenomeDto`):

| JSON property | Meaning |
|---------------|---------|
| `schema` | `"Seed.Genome"` |
| `schemaVersion` | `1` |
| `genomeType` | e.g. `"SeedGenome.CPPN.NEAT"` |
| `genomeId` | GUID string |
| `cppn` | CPPN object |
| `params` | Nested `development`, `learning`, `stability` (types `DevelopmentParams`, `LearningParams`, `StabilityParams`) |
| `reserved` | `ReservedGenomeFields`: `cppnOutputNames`, `reservedMutationScales` |

**`cppn` object (`CppnDto`):**

- `nextNodeId` — `int`
- `nodes` — array of `{ nodeId, type, activation, bias }` with string enums matching `CppnNodeType` and `ActivationFn` names
- `connections` — array of `{ innovationId, srcNodeId, dstNodeId, weight, enabled }`

Export order: nodes sorted by `Type` then `NodeId`; connections sorted by `InnovationId`.

### 12.2 `Seed.BrainGraph` (v1)

Defined in **`Seed.Brain`** (`BrainGraph.ToJson` / `FromJson`), not in Seed.Genetics, but is the compiled artifact of development from a genome.

| JSON property | Meaning |
|---------------|---------|
| `schema` | `"Seed.BrainGraph"` |
| `schemaVersion` | `1` |
| `inputCount`, `outputCount`, `modulatorCount` | `int` |
| `nodes` | Sorted by `nodeId`: `{ nodeId, type, x, y, layer, meta }` (`NodeMetadata`: `regionId`, `moduleId`, `timeConstant`, `plasticityProfileId`) |
| `incomingEdges` | Array of `{ dstNodeId, edges }`; each edge `{ srcNodeId, wSlow, wFast, plasticityGain, meta }` (`EdgeMetadata`: `edgeType`, `delay`, `plasticityProfileId`) |
| `reserved` | `reservedKeys`, `reservedValues` arrays |

Export order: nodes by `NodeId`; `incomingEdges` by destination id; edges within a destination by `srcNodeId`.

---

## File reference

| Area | Primary files |
|------|----------------|
| CPPN types & evaluation | `src/Seed.Genetics/CppnTypes.cs` |
| Genome, mutation, crossover, distance, JSON | `src/Seed.Genetics/SeedGenome.cs` |
| Speciation | `src/Seed.Genetics/Speciation.cs` |
| Innovation tracker | `src/Seed.Genetics/InnovationTracker.cs` |
| Parameters & interfaces | `src/Seed.Core/Parameters.cs`, `src/Seed.Core/Interfaces.cs` |
| Brain graph JSON | `src/Seed.Brain/BrainGraphTypes.cs` |
