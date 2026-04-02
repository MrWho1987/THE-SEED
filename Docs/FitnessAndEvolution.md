# Fitness computation and evolution loop

This document matches the implementation in `Seed.Market` as of the current codebase. File references point to the canonical sources.

---

## 1. Fitness computation (`MarketFitness.ComputeDetailed`)

Implementation: `src/Seed.Market/Evolution/MarketFitness.cs`  
Default weights and penalties from config: `src/Seed.Market/Evolution/IFitnessFunction.cs` (`DefaultFitnessFunction` → `MarketConfig` in `src/Seed.Market/MarketConfig.cs`).

### Inputs

- `portfolio`: `PortfolioState` with equity curve, trades, initial balance, etc.
- `finalPrice`: last price for marking equity and PnL.
- Optional parameters (defaults shown): `shrinkageK = 10`, Sharpe/Sortino/return/DD-duration/CVaR weights `0.45 / 0.15 / 0.20 / 0.10 / 0.10`, `inactivityPenalty = -0.1`, `minTradesForActive = 3`, `activityBonusScale = 0`, `ratioClampMax = 10`, `returnFloor = -0.5`.

Derived values:

- `returnPct = (equity - InitialBalance) / InitialBalance` (as `float`), with `equity = portfolio.Equity(finalPrice)`.
- `tradeCount = portfolio.TotalTrades`.

### 1.1 Zero trades

If `tradeCount == 0`, the function returns immediately with:

- `Fitness = inactivityPenalty`
- Other metrics zeroed or defaulted as in the `FitnessBreakdown` constructor for that branch.

### 1.2 Per-step returns (equity curve)

All ratio metrics use the **equity curve** (`List<float>`): for each step `i = 1 .. n` with `n = equityCurve.Count - 1`:

- `r_i = (equity[i] - equity[i-1]) / |equity[i-1]|`
- If `equity[i-1] == 0`, that step is skipped (no contribution to sums); `mean` still divides by `n` (full step count).

### 1.3 Raw Sharpe

- `mean = sumR / n` (over the loop as implemented).
- `variance = sumR2 / n - mean * mean`. If `variance <= 0`, raw Sharpe is `0`.
- `std = sqrt(variance)`.
- **Raw Sharpe** = `(mean / std) * AnnualizationFactor`, where `AnnualizationFactor = 93.54f` (comment in code: `sqrt(8760)` for hourly steps per year).

### 1.4 Raw Sortino (downside deviation uses full `n`)

- `mean = sumR / n` (same `n` as Sharpe).
- Accumulate `sumNegSq` for negative `r` only; also count negatives in `negCount`.
- If `negCount == 0`: return `20` if `mean > 0`, else `0` (capped to prevent extreme values when all hourly returns are positive).
- Otherwise: `downsideDeviation = sqrt(sumNegSq / n)` — **divisor is `n` (all steps), not `negCount`**.
- If `downsideDeviation <= 0`, return `0`.
- **Sortino** = `(mean / downsideDeviation) * AnnualizationFactor`.

### 1.5 Bayesian shrinkage and trade-count-scaled clamp

- `confidence = 1 - shrinkageK / (shrinkageK + tradeCount)`.
- `clampScale = min(1, tradeCount / (minTradesForActive * 3))` — scales the ratio clamp proportionally to trade count. Agents need `3 * minTradesForActive` trades to access the full clamp range. This prevents low-trade agents from exploiting extreme Sharpe/Sortino values.
- `effectiveClamp = ratioClampMax * clampScale`.
- `adjustedSharpe = clamp(rawSharpe * confidence, -effectiveClamp, effectiveClamp)`.
- Sortino is sanitized: if NaN or infinity, treated as `0` before adjustment.
- `adjustedSortino = clamp(sortinoClean * confidence, -effectiveClamp, effectiveClamp)`.

### 1.6 CVaR (5%)

- Build an array of `n` per-step returns (same definition as above).
- Sort ascending.
- `tailCount = max(1, (int)(n * percentile))` with `percentile = 0.05`.
- **CVaR5** = mean of the `tailCount` smallest returns (the left tail).
- `cvarPenalty = -CVaR5` if `CVaR5 < 0`, else `0`.

### 1.7 Max drawdown duration

- Walk the equity series: track running peak; when `equity[i] < peak`, increment a streak counter; reset streak when a new peak is reached.
- `maxDuration` = length of the longest streak (in **bars**) below the running peak.
- **MaxDrawdownDuration** (metric used in fitness) = `maxDuration / equityCurve.Count` (fraction of bars in drawdown from peak, longest such span).

### 1.8 Log return term

- `logReturn = sign(returnPct) * log(1 + |returnPct|)` using `MathF.Log`, `MathF.Sign`, `MathF.Abs`.

### 1.9 Composite (before low-trade blend)

- `fullFitness = adjustedSharpe * wSharpe + adjustedSortino * wSortino + logReturn * wReturn - maxDdDuration * wDrawdownDuration - cvarPenalty * wCVaR`
- If `fullFitness` is NaN or infinity, it is replaced with `inactivityPenalty`.

### 1.10 Low trade count blending

- If `tradeCount >= minTradesForActive`: `fitness = fullFitness`, `isActive = true`.
- Else: `alpha = tradeCount / minTradesForActive`, `fitness = alpha * fullFitness + (1 - alpha) * inactivityPenalty`, `isActive = false`.

### 1.11 Activity bonus (capped)

- If `tradeCount > 0` and `activityBonusScale > 0`:  
  `rawBonus = log(1 + tradeCount) * activityBonusScale`.  
  `maxBonus = log(1 + minTradesForActive * 3) * activityBonusScale`.  
  `fitness += min(rawBonus, maxBonus)`.  
  The cap prevents agents from exploiting high trade counts (churn) to inflate fitness. Beyond `3 * minTradesForActive` trades, the activity bonus no longer increases.

### 1.12 Return floor

- If `returnPct < returnFloor`: `fitness = min(fitness, inactivityPenalty)`.

### 1.13 Open-position penalty (evaluation only)

- After the agent processes all ticks and before fitness is computed, the evaluator counts how many positions remain open. These are then force-closed for scoring, but a penalty is applied:  
  `fitness -= openAtEnd * 0.05`.  
  This creates evolutionary pressure for agents to learn voluntary exits rather than relying on the system to close positions at evaluation end. Applied in `MarketEvaluator.RunAgent`, not in the fitness function itself.

### 1.13 Defaults (`MarketConfig`)

| Setting | Default |
|--------|---------|
| `ShrinkageK` | `10` |
| `FitnessSharpeWeight` | `0.45` |
| `FitnessSortinoWeight` | `0.15` |
| `FitnessReturnWeight` | `0.20` |
| `FitnessDrawdownDurationWeight` | `0.10` |
| `FitnessCVaRWeight` | `0.10` |
| `InactivityPenalty` | `-0.1` |
| `MinTradesForActive` | `3` |
| `ActivityBonusScale` | `0` |
| `RatioClampMax` | `10` |
| `ReturnFloor` | `-0.5` |

`DefaultFitnessFunction` passes these through to `ComputeDetailed` (`IFitnessFunction.cs`).

---

## 2. Multi-window evaluation

Implementation: `MarketEvolution.RunGeneration` with multiple windows (`src/Seed.Market/Evolution/MarketEvolution.cs`), window construction in `src/Seed.Market.App/Program.cs` (backtest loop).

### 2.1 Building windows

When `EvalWindowCount` > 1 (`k = max(1, config.EvalWindowCount)`):

- Training slice after walk-forward offset: length `remainingLen = trainLen - walkForwardOffset`.
- `wfEvalWindow = min(EvalWindowHours, remainingLen)`.
- **Sub-window length**: `perWindow = wfEvalWindow / k`. If `perWindow < 20`, then `perWindow = min(wfEvalWindow, remainingLen)` (code in `RegimeDetector.SelectDiverseWindows`).
- Windows are produced by `RegimeDetector.SelectDiverseWindows(wfPrices, remainingLen, wfEvalWindow, k, windowEpoch, RunSeed)` — see section 4.

Each window’s arrays are sliced from the walk-forward training slice: `[off .. end2)` with a fallback if the slice would be shorter than 50 bars.

### 2.2 Aggregating fitness

For each genome, evaluations are run per window; each yields a `FitnessBreakdown`. The list of breakdowns is merged by `AverageBreakdowns` (`MarketEvolution.cs`):

- `meanFitness = average(b.Fitness)` across windows.
- If `WindowConsistencyWeight > 0` and there is more than one window:  
  `stdFitness = sqrt( sum_i (fitness_i - meanFitness)^2 / n )` (population std of window fitnesses, `n` = window count).  
  **Adjusted fitness** = `meanFitness - WindowConsistencyWeight * stdFitness`.
- Other fields in the merged `FitnessBreakdown` are mostly means or maxes (e.g. `MaxDrawdown` and `MaxDrawdownDuration` take **max** across windows).

Default `WindowConsistencyWeight` in `MarketConfig` is `0` (no penalty unless configured).

---

## 3. Regime classification

Implementation: `src/Seed.Market/Backtest/RegimeDetector.cs`.

- Requires at least **20** bars; otherwise returns `Sideways`.
- `totalReturn = (last - first) / first` over the window.
- Loop `i = startIdx+1 .. end-1`: sum **absolute** bar-to-bar returns into `sumAbsReturn`; track min/max price for range.
- `avgAbsReturn = sumAbsReturn / (n - 1)`.
- `range = (maxPrice - minPrice) / first` (normalized by first price).

**High volatility** (checked first):

- If `avgAbsReturn > 0.015` **or** `range > 0.30` → `HighVolatility`.

**Otherwise**:

- If `totalReturn > 0.05` → `Bull`.
- If `totalReturn < -0.05` → `Bear`.
- Else → `Sideways`.

---

## 4. Window selection (`SelectDiverseWindows`)

Implementation: `RegimeDetector.SelectDiverseWindows(prices, trainLen, windowSize, k, generation, seed)`.

- Returns empty if `k <= 0` or `trainLen < windowSize`.
- `perWindow = windowSize / k` (adjusted as in §2.1).
- `maxOffset = max(1, trainLen - perWindow)`.
- RNG: `new Random((int)(seed ^ (uint)generation))`.
- Up to `max(k * 3, 10)` random `(offset, Classify(prices, offset, perWindow))` candidates.
- **First pass**: for each `MarketRegime` enum value in order, pick one candidate with that regime that does **not** overlap already selected windows (same length `perWindow`).
- **Second pass**: add non-overlapping candidates until `k` windows.
- **Fallback**: while still short, deterministic offsets  
  `(selected.Count * maxOffset / k + generation * 17) % maxOffset`, classify each.

Overlap rule: two intervals `[offset, offset+length)` and `[o, o+l)` overlap if `offset < o+l && o < offset+length`.

---

## 5. Window stability (`WindowStabilityGens`)

Config: `MarketConfig.WindowStabilityGens` (default `1`).

In `Program.cs`, when `k > 1`:

- `windowEpoch = (WindowStabilityGens > 1) ? (gen / WindowStabilityGens) : gen`
- This `windowEpoch` is passed to `SelectDiverseWindows` as the **generation** argument (along with `RunSeed`), so changing it only on every `WindowStabilityGens` generations reuses the same epoch index for window selection for longer stretches.

---

## 6. Evolution loop (`MarketEvolution`)

Implementation: `src/Seed.Market/Evolution/MarketEvolution.cs`.

**Per generation, actual order:**

1. **Evaluate** the population on the current window(s); build `_evaluations` (multi-window uses `AverageBreakdowns` as in §2).
2. **KNN diversity bonus** (`ApplyDiversityBonus`): optional; see below.
3. **Speciate** with `SpeciationManager` using `SpeciationConfig` (`C1=1`, `C2=1`, `C3=0.5`, `CompatibilityThreshold`, `TournamentSize=3`).
4. **Compatibility threshold adjustment**: if species count `< TargetSpeciesMin`, decrease threshold by `CompatibilityAdjustRate` (floor `1.0`); if `> TargetSpeciesMax`, increase (ceiling `10.0`).
5. **Elite archive and stagnation**: for each species, update `EliteArchive` with members’ fitness; track per-species `BestFitness` and `StagnationCounter` (increment if no improvement).
6. **Species cumulative PnL** (`UpdateSpeciesPnl`): rolling sum of average member `NetPnl` per species (for allocation context).
7. **Report** (`BuildGeneration` / logging).
8. **Reproduce** → next population; `Generation++` after reproduction.

### 6.1 KNN diversity bonus

If `DiversityBonusScale > 0` and population size ≥ 2:

- For each genome, compute genomic **distance** to every other (`genome.DistanceTo` with the same speciation coefficients).
- Sort distances; take the average of the **k** smallest distances, `k = min(DiversityKNeighbors, populationCount - 1)`.
- Bonus = `avgKnn * DiversityBonusScale`.
- Applied **only** if `eval.Fitness.IsActive` is true: `Fitness += bonus`.

Defaults: `DiversityBonusScale = 0.02`, `DiversityKNeighbors = 5`.

### 6.2 Offspring allocation and reproduction

- **Global best clone**: before allocation, one slot is filled with a **clone of the genome** with highest evaluated fitness (`Reproduce`).
- Remaining count: `totalOffspring - 1` is passed to `AllocateOffspring` with `PopulationBudget` (`ElitesPerSpecies: 1`, `MinSpeciesSizeForElitism` from config, etc.) and `MinOffspringPerSpecies`.

Per species (ordered by `SpeciesId`):

- **Stagnation reseeding**: if `StagnationCounter >= StagnationLimit` (default `25`), half of that species’ offspring count (`numOffspring / 2`) are replaced by:
  - **Random** `SeedGenome.CreateRandom` if the archive is “degenerate”: no elites **or** max archive fitness `<= InactivityPenalty + 0.001`.
  - Otherwise: pick a genome from **top 10** archive elites by fitness (`GetDiverseElites(10)`), clone, **mutate** (`MutationContext` with run seed, generation, default `MutationConfig`).
  - `numOffspring` is then reduced by `replaceCount` for the normal loop below.
- **Elitism**: if `sortedMembers.Count >= MinSpeciesSizeForElitism`, clone the best member by fitness into the next generation and decrement `numOffspring`.
- Remaining slots: with probability `MutationConfig.Default.PCrossover` and at least two members, **crossover** two tournament-selected parents (higher fitness parent ordered first for crossover); else **mutation-only** from tournament selection. Tournament size **3**.
- **Fill**: if still under `PopulationSize`, prefer **mutated archive elites**; if no archive, **random** genomes.

### 6.3 Stagnation vs archive (summary)

Species-level stagnation increments when the species best fitness does not beat its previous best. Crossing `StagnationLimit` triggers partial reseeding from archive or random (see above).

---

## 7. Walk-forward validation

Implementation: `src/Seed.Market.App/Program.cs` (backtest main loop). Config: `WalkForwardEnabled`, `WalkForwardMinValFitness`, `WalkForwardMaxStallGens`, `RollingStepHours`, `ValidationIntervalGens`.

- Training data is sliced from `[walkForwardOffset .. trainLen)` for evolution each generation (`remainingLen`).
- On validation generations (`gen > 0`, `gen % ValidationIntervalGens == 0`, validation slice length ≥ 50), the **current best genome** is evaluated on the **held-out validation** window; `valFit` is that fitness.
- **Advance** (when `WalkForwardEnabled`):  
  If `valFit >= WalkForwardMinValFitness` (default `-0.05`):  
  `walkForwardOffset = min(walkForwardOffset + RollingStepHours, maxWfOffset)` with `maxWfOffset = max(0, trainLen - evalWindow)`, and **`stallCount = 0`**.
- **Stall**: if validation fails the threshold: `stallCount++`.  
  If `stallCount >= WalkForwardMaxStallGens` (default `50`): force advance with the same `walkForwardOffset` update and **`stallCount = 0`**.

Single-window training offset when `EvalWindowCount <= 1`: if `WalkForwardEnabled`, evaluation offset is **0** within the walk-forward slice; if disabled, offset rolls with `(gen * RollingStepHours) % maxOff`.

---

## 8. Checkpointing

### 8.1 State type

`src/Seed.Market/Backtest/Checkpoint.cs` — `CheckpointState` fields:

- `Generation`, `BestFitness`, `Timestamp`
- `GenomeJsons` (full population JSON list)
- `SpeciesIds` (per-genome species assignment)
- `NextInnovationId`, `NextCppnNodeId`
- `CompatibilityThreshold` (default `3.5` in record)
- `WalkForwardOffset`, `StallCount`
- `SpeciesState`: list of `SpeciesCheckpointEntry` (`SpeciesId`, `RepresentativeGenomeJson`, `StagnationCounter`, `BestFitness`)
- `NextSpeciesId`
- `ArchiveState`: list of `ArchiveCheckpointEntry` (`SpeciesId`, `GenomeJson`, `Fitness`)

`RestorePopulation()` deserializes `GenomeJsons` via `SeedGenome.FromJson`.

### 8.2 When saved (backtest)

When `(gen + 1) % CheckpointIntervalGens == 0` and `CheckpointIntervalGens > 0` (default interval `10`):

- Filename: `checkpoint_{gen + 1:D4}.json` under `{OutputDirectory}/checkpoints/`.
- Population at **end** of generation `gen` is saved with **`generation = gen + 1`** in `FromPopulation` (next generation index).

### 8.3 Resume

- `CheckpointState.FindLatest(checkpointDir)` picks the lexicographically greatest `checkpoint_*.json`.
- Load → restore population, `InitializeFrom` with innovation IDs, compatibility threshold, species state, archive state, **`walkForwardOffset`**, **`stallCount`**.

---

## 9. Baselines

Implementation: `src/Seed.Market/Evaluation/BaselineStrategies.cs` (not `BaselineAgents.cs`). Each builds a `PortfolioState` / `PaperTrader`, runs the strategy, then returns `MarketFitness.ComputeDetailed(..., config.ShrinkageK)` (default fitness weights from `MarketFitness` / config as applicable).

| Baseline | Behavior |
|----------|----------|
| **BuyAndHold** | Initial long: `MaxPositionPct` of capital at `prices[0]`, taker fee on entry; equity recorded each bar; close at last price; one closed trade in history. |
| **SmaCrossover(20, 50)** | Default `shortPeriod = 20`, `longPeriod = 50`. From `t = longPeriod` onward: if `SMA(short) > SMA(long)` and flat, open long (signal size `0.5`, urgency `0.9`); if short SMA ≤ long and long position open, exit. Uses `PaperTrader.ProcessSignal`. |
| **RandomAgent** | Default `seed = 42`. Each bar: `r = NextDouble()`; `r < 0.05` → long `0.3`; `r < 0.10` → short `0.3`; `r < 0.15` and has positions → flat/exit; else flat. |
| **MeanReversion(30, 70, 14)** | Default `buyRsi = 30`, `sellRsi = 70`, `rsiPeriod = 14`. RSI computed from simple average gains/losses over `period`. Buy when `RSI < buyRsi` and flat; sell when `RSI > sellRsi` and long open. |

---

## 10. Evaluation entry points

- **Population backtest**: `MarketEvaluator.Evaluate` compiles brains, runs `MarketAgent` over history, then `IFitnessFunction.ComputeDetailed` (default: `MarketFitness` with `MarketConfig` weights). See `src/Seed.Market/Evolution/MarketEvaluator.cs`.

---

## Related config keys (quick reference)

| Key | Role |
|-----|------|
| `EvalWindowHours`, `EvalWindowCount` | Evaluation window size and multi-window count |
| `RollingStepHours` | Walk-forward step size on successful validation / force advance |
| `WindowConsistencyWeight` | Penalize variance of fitness across windows |
| `CheckpointIntervalGens` | Checkpoint every N generations (after `gen+1`) |
| `WindowStabilityGens` | Changes epoch for diverse window selection when > 1 |
| `StagnationLimit` | Species stagnation before archive/random reseed |
| `TargetSpeciesMin`, `TargetSpeciesMax`, `CompatibilityAdjustRate` | Dynamic compatibility threshold |
