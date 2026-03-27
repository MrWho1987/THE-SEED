# SEED — Seed Core v1 + V2-Ready Contract (Implementation Specification)

> **Document status (March 2026):**
> This spec was written for the original 2D terrarium world. The core engine it describes — genome (CPPN-NEAT), development compiler, brain runtime, learning engine, evolution, speciation, and budget scaling — **remains active and unchanged** in the current market evolution system (`Seed.Market`).
>
> **Sections that are legacy (2D-world only):**
> - Section 5 (World Specification) — replaced by `Seed.Market.Data` feeds
> - Section 6 (Body) — replaced by `Seed.Market.Agents.MarketAgent`
> - Section 12 (Evaluation Protocol) — replaced by `Seed.Market.Evolution.MarketEvaluator`
> - Section 13 (Observatory) — partially applicable; market-specific observability TBD
> - Section 19 (Project Layout) — projects `Seed.Worlds`, `Seed.Agents`, `Seed.Evolution`, `Seed.App`, `Seed.Dashboard` have been removed from main (preserved on the `Legacy` branch)
>
> **Sections that remain authoritative:**
> - Sections 0-4 (Definition, Aim, Design Principles, Kernel & Determinism)
> - Sections 7-11 (Genome, Development, Brain Runtime, Learning, Evolution Engine)
> - Part B (V2-Ready Contract, Budget Model, Reserved Channels)
>
> For the market-specific architecture, see `Docs/MarketEvolution_DraftPaper.md`.

* **Seed Core v1** = minimal, working Evo-Devo loop that reliably produces insect-level adaptive agents.
* **V2-Ready Contract** = stable interfaces + reserved channels so V2 modules can be added **without rewrites**.

The goal is one Seed that scales by **budgets** (compute/world/time), not by changing architecture.

---

## 0) One-line Definition

**Seed Core v1 is a deterministic Evo-Devo artificial life system where a compact CPPN genome compiles into a sparse recurrent neural controller that learns via neuromodulated local plasticity and improves via speciated evolution across procedural world bundles — with reserved extension points for V2 open-ended scaling.**

---

## 1) Aim

### 1.1 What we are building

A **seed**: a compact generative mechanism that can:

1. **Grow** a brain from an indirect encoding (genome → development → brain),
2. **Learn** within a lifetime (online plasticity),
3. **Evolve** across generations (speciated evolutionary search),
4. **Generalize** across randomized worlds,
5. Scale from “small brains on a local PC” to “large brains on big hardware” **by changing budgets only**.

### 1.2 Core v1 success criteria

Within practical local compute:

* Agents evolve to forage + avoid hazards better than baselines.
* Agents improve within a single episode with learning enabled.
* Performance generalizes across a world bundle (not a single map).
* Runs are deterministic and replayable.
* Brain graphs and metrics are inspectable.

### 1.3 Explicit non-goals of Core v1

Core v1 does not attempt:

* Language emergence
* Tool use
* Long-horizon planning
* Persistent culture
  These are V2 modules enabled later via the contract.

---

## 2) Design Principles (Non-negotiable)

1. **Indirect encoding** (recipe, not wiring diagram).
2. **Determinism** end-to-end.
3. **Sparsity everywhere** (no dense all-pairs graphs).
4. **Embodied closed loops** (world↔body↔brain).
5. **Learning + evolution** synergy (Baldwin effect).
6. **Observability as a feature**, not an afterthought.
7. **Budget-scaling** is the only scaling method (no rewrites).

---

## 3) System Overview

### 3.1 Core Loop (conceptual)

1. Choose genome from population
2. Develop phenotype (compile to brain graph)
3. Run episodes in world bundle (learning occurs online)
4. Compute fitness across bundle
5. Speciate, select, mutate → next generation
6. Record metrics + replays + brain exports

### 3.2 Layer Map

* **Kernel**: deterministic scheduler, PRNG, parallel runner (deterministic reduction)
* **Worlds**: procedural environments
* **Body**: sensors/actuators + homeostasis
* **Genome**: CPPN + params
* **Development**: CPPN → sparse recurrent graph
* **Brain runtime**: recurrent micro-steps
* **Learning**: modulated eligibility plasticity + stabilization
* **Evolution**: speciation + mutation + selection
* **Evaluation**: world bundles, robustness scoring
* **Observatory**: replay + exports + ablations

---

# PART A — SEED CORE v1 (Buildable MVP)

## 4) Kernel & Determinism (Core v1)

### 4.1 Deterministic requirements

* Fixed timestep Δt for world and brain
* Deterministic PRNG streams derived from a single run seed
* Stable ordering in parallel execution and aggregation
* No time-based randomness, no thread-race randomness

### 4.2 Required PRNG streams (derived deterministically)

* Run seed
* Generation seed
* World instance seed
* Agent instance seed
* Tie-break seed (for ranking equal scores)

---

## 5) World Specification (Core v1)

### 5.1 World type

**2D world (grid or simple continuous)** with:

* obstacles
* food resources
* energy costs
* optional hazard zones

### 5.2 World invariants

* Food is reachable but non-trivial
* Movement consumes energy
* Episode ends on death or max ticks
* Randomization bounded (to prevent trivial or impossible worlds)

### 5.3 Minimal world API (contract-friendly)

* `Reset(seed)`
* `Step(action)` → observation + reward signals + done + info
* Deterministic physics / collision

---

## 6) Body (Core v1)

### 6.1 Homeostasis

* `Energy` (primary)
* Optional `Health` (secondary, can be constant in v1)
* `Alive` derived from Energy/Health

### 6.2 Sensors (minimal but sufficient)

* Proximity rays (distance + object type)
* Food gradient hint (optional but recommended for bootstrap)
* Energy level (internal)
* Heading/velocity (proprioception)

### 6.3 Actuators

* forward/back thrust (or move)
* turn left/right
* consume (optional; can be automatic if near food)

Body is strictly behind `ISensors` and `IActuators` so world/physics can change without rewriting brain.

---

## 7) Genome — “The Seed” (Core v1)

### 7.1 Genome contents

**SeedGenome = CPPN + params**, where params include:

* Development: hidden resolution cap, sparsity controls
* Learning: plasticity rates, eligibility decay, modulators scaling
* Stability: homeostasis target, normalization, clipping bounds
* (V2 reserved fields exist but are unused in v1)

### 7.2 CPPN details

* Small neural network with evolvable topology (NEAT-style innovations)
* Inputs are geometry; outputs define connectivity pattern

---

## 8) Development Compiler (Genome → BrainGraph) (Core v1)

### 8.1 Neuron placement (the canvas)

* Fixed input nodes = sensor channels
* Hidden nodes on a 2D grid/rings with coordinates `(x,y,layer)`
* Fixed output nodes = actuators

**Scaling rule:** increase hidden resolution by budget; the genome is unchanged.

### 8.2 CPPN query

For candidate edge i→j:
Inputs include:

* `xi, yi, li`
* `xj, yj, lj`
* `dx, dy, dist`

Outputs (Core v1 used):

* `c` connection score
* `w` initial weight

Outputs (reserved for V2; ignored in v1):

* `delay`
* `tau` time constant
* `module_tag`
* `gate` or `attention_score`

### 8.3 Sparsification (hard rule)

To prevent O(N²):

* For each neuron j, evaluate a **bounded candidate set**:

  * local neighborhood in coordinate space
  * plus a small deterministic global sample
* Keep **TopKIn** highest-score incoming edges
* Enforce **MaxOut** to prevent hubs

### 8.4 BrainGraph output

* Neurons: `(id, type, coords, metadata)`
* Edges: adjacency lists (incoming per neuron)

  * `(srcId, w_init, plasticity_gain_reserved, delay_reserved)`

---

## 9) Brain Runtime (Core v1)

### 9.1 Neuron model

Rate-based bounded activation:

* `a = tanh(z)`
* recurrent updates over `MicroSteps` per tick (e.g., 2–5)

### 9.2 Tick cycle

1. Set input neuron activations from sensors
2. MicroSteps:

   * compute hidden/output activations from sparse incoming edges
3. Read output activations → actuators

### 9.3 Stability safeguards (mandatory)

* Weight clipping `[−Wmax, +Wmax]`
* Homeostatic activity control (scale incoming weights per neuron)
* Optional incoming normalization
* Optional recurrent gain scaling

These prevent “epilepsy” or “dead” brains dominating.

---

## 10) Learning Engine (Core v1)

### 10.1 Modulators

Core v1 supports 3 modulators:

* Reward (food/energy gain)
* Pain (energy loss / hazard penalty)
* Curiosity (learning progress proxy)

**Contract note:** modulators are a vector, not a single scalar (V2 will add social/novelty/culture signals).

### 10.2 Eligibility traces

Per synapse i→j:

* Maintain `e_ij`
* Update:

  * `e_ij = λ * e_ij + clip(a_i * a_j)`

### 10.3 Weight updates

Let combined modulator be:

* `M = αR*R + αP*P + αC*C`

Update volatile weight:

* `Δw = η * M * e_ij`
* `w_fast += Δw`

### 10.4 Two-speed consolidation (anti-forgetting, minimal)

Per synapse:

* `w_fast` (volatile)
* `w_slow` (stable)

Periodic consolidation:

* `w_slow = (1−β)*w_slow + β*w_fast`
* `w_fast = (1−γ)*w_fast + γ*w_slow`

This is the minimal “sleep-lite” that makes V1 workable and V2-compatible.

---

## 11) Evolution Engine (Core v1)

### 11.1 Population loop

* Evaluate genome fitness over a **bundle of worlds**
* Speciate genomes
* Selection within species
* Mutation to reproduce
* Elitism per species
* Next generation

### 11.2 Speciation

NEAT-style distance:

* topology (innovation IDs)
* weight deltas

### 11.3 Mutation operators

* perturb CPPN weights
* add CPPN node
* add CPPN edge
* mutate dev params (TopKIn, thresholds within bounds)
* mutate learning params (η, λ, αR/αP/αC)
* mutate stability params (homeostasis strength, Wmax)

Crossover is optional; v1 can ship without it.

---

## 12) Evaluation Protocol (Core v1)

### 12.1 World bundle evaluation

Each genome evaluated on K worlds (e.g., 8) with different seeds.

### 12.2 Fitness components

* survival time
* net energy gained
* food collected
* efficiency (food per energy spent)
* penalty for instability (saturation/oscillation)

### 12.3 Robustness preference

Selection should prefer:

* high mean
* low variance
* decent worst-case

---

## 13) Observatory (Core v1)

Mandatory:

* deterministic episode replay (seed + actions + key state logs)
* generation metrics (fitness distribution, species counts)
* brain export (JSON graph)
* activation traces for sample runs
* ablations toggles:

  * learning off
  * curiosity off
  * homeostasis off
  * evolution off

---

# PART B — V2-READY CONTRACT (No-Rewrite Guarantees)

This section defines **what must be stable forever** so V2 modules plug in without breaking the system.

## 14) Stable Interfaces (must not change)

### 14.1 Pipeline invariants

**Genome → Development → Brain → Agent → World → Fitness**

Every future upgrade must preserve this pipeline shape.

### 14.2 Core contracts (names conceptual)

* `IGenome`
  Serialize/deserialize, mutate, distance, clone
* `IDeveloper`
  `Develop(genome, budget) -> IBrain`
* `IBrain`
  `Step(inputs) -> outputs`, `Learn(signals)`
* `IAgentBody`
  `GetSensors()`, `ApplyActions()`, internal homeostasis
* `IWorld`
  `Reset(seed)`, `Step(actions) -> obs + signals`
* `IEvaluator`
  runs world bundles deterministically and returns metrics
* `IObservatory`
  receives standardized events and exports

The AI agent implementing must keep these boundaries clean.

---

## 15) Budget Model (the scaling mechanism)

Budgets are the only scaling dial. These MUST exist from day 1:

* **DevelopmentBudget**

  * hidden neuron count / grid resolution
  * topK fan-in/out limits
  * candidate sampling size
* **RuntimeBudget**

  * ticks per episode (lifetime length)
  * micro-steps per tick
* **PopulationBudget**

  * population size
  * number of evaluated worlds per genome
* **WorldBudget**

  * world size
  * obstacle density
  * number of resource types
* **ComputeBudget**

  * local CPU threads, GPU mode later, cluster mode later

**Invariant:** same genome + same budgets + same seeds → same phenotype and behavior.

---

## 16) Reserved Channels (do not remove)

To avoid future rewrites, v1 must already store and pass these fields, even if unused.

### 16.1 Reserved CPPN outputs

The CPPN output vector is fixed-length from v1:

1. `c` connection score (used)
2. `w` initial weight (used)
3. `delay` (reserved)
4. `tau` neuron time constant (reserved)
5. `module_tag` (reserved)
6. `gate`/`attention_score` (reserved)

In v1, unused outputs are ignored but preserved in serialization and mutation.

### 16.2 Reserved neuron/edge metadata

Every neuron and edge must carry a `Metadata` struct with reserved fields:

* `RegionId` / `ModuleId` (for modular skills later)
* `EdgeType` (normal, modulatory, memory, etc.)
* `Delay` and `TimeConstant` slots
* `PlasticityProfileId` slot

### 16.3 Modulator vector

Learning accepts a vector of modulators:

* indices 0..N fixed in config
* v1 uses: Reward/Pain/Curiosity
* v2 adds: Social, Novelty, Reputation, etc.

---

## 17) V2 Module Plug-In Points (defined now)

These are extension modules that must be injectable without changing Core v1.

### 17.1 Persistent World Module

* Adds persistence across episodes
* Adds multi-agent ecology
* Must not change `IWorld.Step()` signature; it changes world config/budget only.

### 17.2 Consolidation / Sleep Module

* Adds offline replay, memory stabilization
* Must plug into `IBrain` as optional:

  * `OfflineConsolidate(replayBuffer, budget)`

### 17.3 Modular Skill Library Module

* Allows brains to freeze/reuse modules (subgraphs)
* Uses `ModuleId` reserved metadata

### 17.4 World-Model + Planning Module

* Adds predictive model & planner
* Must appear as additional brain subgraph(s) or an auxiliary module called by `IBrain.Step()`
* No interface rewrite; it consumes reserved channels/metadata

### 17.5 Social Learning / Communication Module

* Adds communication channels in sensors/actuators
* Must be added by increasing sensor/actuator sets through the Body config, not changing the brain contract.

---

## 18) Performance & Portability Rules (local PC ↔ big hardware)

To keep the same Seed runnable across hardware:

### 18.1 Sparse compute rule (forever)

* No dense all-pairs operations in development or runtime.
* Any new V2 module must declare complexity class and remain sub-quadratic per step.

### 18.2 Deterministic parallelism rule

* parallel evaluation allowed
* deterministic reductions required
* same run seed must reproduce

### 18.3 Modes

* **Local Mode:** small budgets, fast iteration, smaller populations
* **Scale Mode:** larger budgets, distributed evaluation, same codepaths

---

# PART C — Implementation Map (AI-agent Friendly)

## 19) Minimal C# Project Layout

* `Seed.Core` — determinism, RNG, config, scheduler
* `Seed.Worlds` — IWorld + procedural world
* `Seed.Agents` — body, sensors/actuators
* `Seed.Genetics` — genome, mutation, distance, speciation
* `Seed.Development` — CPPN eval + graph compiler
* `Seed.Brain` — runtime + learning
* `Seed.Evolution` — training loop + evaluation bundle
* `Seed.Observatory` — replay + exports
* `Seed.App` — CLI runner

Implementation-grade specs (exact C# records/JSON/RNG/eval/speciation defaults):
- `Docs/SeedCoreV1_TechnicalSpec.md`

## 20) MVP Build Order (no over-engineering)

1. Deterministic kernel + world + replay
2. Body sensors/actuators + baseline random controller
3. CPPN genome + development compiler (sparse graph)
4. Brain runtime + stability guardrails
5. Learning (eligibility + modulators + two-speed weights)
6. Evolution + speciation + bundle evaluation
7. Observatory exports + ablations

---

# End: Seed Core v1 + V2-Ready Contract