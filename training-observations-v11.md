# V11 Training Observations

> Live log of the V11 retrain. Structured per-phase; append observations as generations progress.
> Goal: track fitness trajectory, action-output usage, signal health, anomalies, and
> breakthroughs so future sessions can pick up without re-deriving context.

**Baseline:** V11 (commit `da4f6fe`) on `main`
**Started:** _pending_
**Pipeline:** `dotnet run --pipeline market-config.phase1.json,market-config.phase2.json,market-config.phase3.json`

## V11 Configuration Summary

| Dimension | Value |
|---|---|
| Brain | 20×20×3 (1200 neurons), TopKIn 32, MaxOut 40, MaxSynapticDelay 16, Modules 12 |
| Inputs | 110 signals (regime 4→8, + 6 portfolio context, Deribit replaces News/Reddit) |
| Outputs | 11 (direction, size, urgency, exit, predict, leverage, partialClose, trailEnable, trailDist, tpOffset, slOverride) |
| MaxLeverage | 125 (Binance BTCUSDT retail max) |
| Stop-Loss | 2% protective (config default) |
| Candle interval | 15m |
| Fitness components | 9 (Sharpe, Sortino, Return, DDDuration, CVaR, Calmar, InfoRatio, FeeDrag, Diversification) |
| peakExitBonus | 0.1 |
| explicitExitBonus | 0.02 (applied to all brain-driven exits: output 3, 6-10) |

## Phase Plan

| Phase | Pop | Gens | TrainWin | EvalWin × N | wReturn | wSharpe | minTrades | Notes |
|---|---|---|---|---|---|---|---|---|
| 1 Discovery | 150 | 600 | 26000h | 2000h × 1 | 0.40 | 0.10 | 3 | activityBonus 0.05, single window, fast iteration |
| 2 Refinement | 150 | 1200 | 26000h | 5000h × 3 | 0.20 | 0.22 | 6 | windowConsistency 0.05, 3-window |
| 3 Production | 200 | 1800 | 26000h | 6000h × 3 | 0.15 | 0.30 | 8 | windowConsistency 0.10, strict |

Total gens: 3600. Estimated time at ~2 min/gen (pop 150 on V11 brain): ~120h = **~5 days**.
Plan budgeted 25 days — actual may be faster.

## Pre-launch verification

- [x] Full test suite: 327/327 green
- [x] Build clean (0 warnings, 0 errors)
- [x] Micro smoke (20 pop × 3 gens): pipeline runs end-to-end, 10/10 enrichment sources success
- [x] Mini smoke (30 pop × 10 gens): gen 2 found +0.062 positive genome (Sharpe 0.37, 1886 trades, WR 54%)
- [x] Phase configs validated: fitness weights sum to 1.0, peakExitBonus set, all required fields present
- [x] V11 committed + pushed to origin/main
- [x] All 10 historical data sources populated including new FuturesPremium + Deribit DVOL + Coinglass Exchange Balance (V11b fix)

## What to watch for

### Fitness trajectory
- **Gen 0-2**: expect Best ≈ -0.10 (inactivity penalty dominates random pop)
- **Gen 2-10**: expect first breakthrough (>0 fitness) if Phase 1 discovery works
- **Gen 10-50**: steady climb if evolution is tracking — expect Best to reach +0.5+
- **Gen 50-200**: refinement, fitness plateaus in the +0.8 to +1.5 range typically
- **Gen 200-600**: diminishing returns, species diversification matters more

### Action output diversity
The V11 action space adds 5 new outputs. Watch for their **std > 0** in paper mode dump:
- output[6] partialCloseFrac
- output[7] enableTrailingStop
- output[8] trailingStopDistance
- output[9] takeProfitOffset
- output[10] stopLossOverride

If any of these stay ≈ 0.5 (sigmoid dormant) across all generations, brain isn't
exploring that output. Bootstrap via `explicitExitBonus` should fix this.

### Close reason distribution
Watch the fraction of closes by reason — ideally brain-driven reasons dominate over
reactive ones in later gens:
- Brain-driven: ExitSignal, TakeProfit, TrailingStop, BrainStopLoss, PartialClose
- Reactive: DirectionFlip, StopLoss (config), KillSwitch, EndOfSession

### Signal health
- CoinglassFeed exchange balance: should be 0 warnings (V11b fix)
- Deribit DVOL: 1 slot populated per bar
- FuturesPremium: 1 slot populated per bar
- Total enrichment: 52+ slots

### Anomalies to flag
- Generation time > 10 min/gen (suggests compute pressure beyond brain size)
- All-inactive population for >20 consecutive gens (brain stuck, may need seed reset)
- Negative Sharpe across all species for >50 gens (curriculum/fitness weights miscalibrated)
- "Worst fitness" dropping below -5 (extreme outliers; mostly cosmetic unless affecting mean badly)

---

## Phase 1 — Discovery

### Configuration
- Pop: 150, Gens: 600
- Fitness emphasis: Return (0.40), CVaR (0.17), activity bonus (0.05)
- Eval: single 2000h window, walk-forward enabled, minValFitness 0.01

### Observations

**Launched:** 2026-04-15, task `b0fzte0ze` (background bash), log `training-v11.log`

**Pre-evolution:** 54 enrichment slots populated across all 10 sources
(ETH/multi-asset, Macro, On-Chain, Fear&Greed, Stablecoin, Funding, Derivatives,
Futures Klines, Deribit DVOL, Coinglass — all SUCCESS, 0 warnings). Training window
loaded 26000h of 15m candles.

**Gen 0–5 warmup:**
- Gen 0: Best -0.10, 100% inactive, 1 species, 574s
- Gen 1: 21% active (first mutations break deadzone), Mean -0.47, 1 species, 550s
- Gen 2: 21% active, Mean -0.45, 1 species, 520s
- Gen 3: 23% active, Mean -0.46, 1 species, 500s
- Gen 4: 21% active, 3 species (first speciation), 483s
- Gen 5: 31% active, 10 species (hit targetSpeciesMin), Mean -0.59, 483s
- **Observation:** Pace speeds up as JIT warms (574s → 483s).
- **Observation:** Mean worsens as more genomes activate and lose, even though best stays at -0.10 passive baseline. This is expected when asymmetric reward penalizes losses harder.

**Gen 6: FIRST BREAKTHROUGH ★**
- **Best: +0.0855** (first positive fitness, beats inactivity penalty)
- Sharpe 0.71, Sortino 1.50, DD 0.1%, 2236 trades, WR 42%
- **Walk-forward validation PASSED**: ValFit delta +0.1855
- Species: 11, Inactive: 63%, Time 434s
- **Profile:** Very active (1.1 trades/hour avg), below-50% WR but profitable via
  tight risk management (Sortino > Sharpe confirms low downside variance, tiny DD)
- Gap to pre-V11 baseline: pre-V11 broke through at gen 1 with a simpler 5-output
  action space. V11's 11-output space requires more random combinations — 6 gens
  is still fast for this search radius.

**Gen 7: elite preserved + improved**
- **Best: +0.0935** (up from +0.0855, same genome re-evaluated with small gain)
- Same Sharpe/Sortino/Trades signature — confirms elitism is working at pop 150
- Mean improved -0.64 → -0.45 (population quality rising)
- Species: 15 (diversifying), Inactive: 76%, Time 449s
- **Validation of mini-smoke concern:** At pop 150, elite preservation works correctly.
  The mini smoke (pop 30) artifact was due to small-population fragility.

**Current pace projection:**
- ~450s/gen average (7.5 min) × 600 gens = **~75 hours = ~3.1 days for Phase 1**
- Well ahead of plan's estimate

**Gen 8–18 (pre-restart run):**
- Gen 8: +0.0937 — same elite, eval window drift
- Gen 9: +0.0854 (-0.008 drift)
- Gen 10: +0.0856 — walk-forward validation ran, **ValFit = -1.3260** (heavy overfitting on 2000h single window; Phase 2's 3-window eval will filter this)
- Gen 11: +0.0851 stable
- Gen 12: +0.0855, Mean improving -0.52→-0.43 (population quality rising)
- Gen 13: +0.0934 (+0.008 eval window shift)
- **Gen 14: NEW BREAKTHROUGH — Best +0.1510**
  - **Strategy shift:** 2236 trades/42% WR → **125 trades/61% WR**
  - Quality-over-quantity: selective high-conviction trader replaces scalper
  - Sharpe 0.78, Sortino 1.37, DD 0.0%
  - Pre: scalper grinding thin edges; Post: 18x fewer trades with much higher win rate
- Gen 15: +0.1146 (-0.036 drift; new strategy is window-sensitive due to 18x fewer trades)
- Gen 16: +0.1188, Mean jumped -0.46→-0.33 (new strategy propagating through population)
- Gen 17: +0.1146, Mean -0.44 (partial disruption)
- Gen 18: +0.1148, Species 28 (above max), Mean -0.37
- **Pace trend:** 574s → 332s → ~350s (JIT warmup complete, steady state ~350s = 5.8 min/gen)

**Interruption 2026-04-15 ~06:25 UTC:** machine restarted mid-gen 19 (estimated). No full
population checkpoint existed (first scheduled at gen 25). Best-genome snapshots saved
for gens 0/6/7/8/14 + val snapshot at gen 10.

**Restart 2026-04-15:**
- Old `training-v11.log` archived as `training-v11.log.pre-restart` (118 lines)
- Stale `output_phase1/` deleted (no population checkpoint to resume)
- `pipeline_shared_cache/` preserved — 31 cached data files (Binance candles,
  Deribit DVOL, Coinglass, etc.) — enrichment will load from cache, not re-download
- RunSeed=42 deterministic — restart should replay identical gen 0–18 trajectory
- New pipeline task: `bgushi81z`, new monitor: `b2p3cbny4`
- Expected wasted compute: ~2 hours (gen 0–18 re-evaluated from scratch)
- Recommendation for future runs: reduce `checkpointIntervalGens` from 25 → 10 so
  checkpoints save sooner (tradeoff: slightly more I/O overhead)

---

## V11d Diagnostic + Remediation (2026-04-15)

After the resumed run got stuck in the passive local optimum for 60+ generations
(every active genome losing, `pos:0` population-wide), a deep audit revealed three
verified root causes plus one orthogonal reproducibility issue. All claims were
empirically validated via unit tests in `RewardShapeVerificationTests`, and the
fixes were verified by smoke testing before launching the full retrain.

### Root causes (verified)

1. **A1 reward "fix" was based on a misread of pre-V11 code (PRIMARY)**
   The pre-V11 reward had a SYMMETRIC delta-based per-tick reward (zero expected
   value for random trading) plus a losing-only holding penalty (`if pnlPct ≤ 0
   && ticks > 20`). This is the correct shape — random exploration is neutral,
   and losing positions get pushed to close. My A1 "fix" replaced this with an
   ASYMMETRIC absolute-pnl reward (profit slope 2, loss slope 5) plus a symmetric
   after-40-ticks holding penalty. Empirically verified that V11 random trading
   has strongly negative expected reward → aversive conditioning → brain plasticity
   decays trading outputs → passive dominates.

2. **Action-space deadzones too permissive (AMPLIFIES)**
   V11 added 5 new outputs (partialClose, trailEnable, trailDist, tpOffset,
   slOverride) with deadzones at 0.1-0.5. Random CPPN brains produce sigmoid
   outputs centered at 0.5, so they activate outputs 6-10 at 50%-85% rates. Result:
   constant position churn — open, partial close, TP fire, SL fire, re-open. The
   brain never sees a clean trade to its conclusion, so it can't learn coherent
   behavior. Empirically verified.

3. **maxPositionPct collapsed 0.25 → 0.02 (COMPOUNDS)**
   I reduced max position 12.5x when adding 125x leverage, assuming the brain
   would scale up via output[5]. But random brains have output[5] ≈ 0 → leverage
   ≈ 1 → effective notional $200 vs pre-V11's $2500. The fitness Return component
   becomes barely detectable.

4. **Speciation non-determinism (ORTHOGONAL — reproducibility)**
   `Speciation.cs:98` used `OrderBy(g => g.GenomeId).First()` where GenomeId came
   from `Guid.NewGuid()`. Different runs with the same `runSeed=42` selected
   different species representatives → different speciation paths → different
   trajectories. This is why Run 1 got lucky and Run 2 didn't.

### V11d Fixes Applied

| Fix | File | Change |
|------|------|--------|
| 1 | `MarketAgent.ComputeReward` | Reverted to symmetric delta-based reward + losing-only holding penalty |
| 2 | `ActionInterpreter.cs` | Raised PartialClose/TrailEnable/TP/SL deadzones from 0.1-0.5 → 0.8 |
| 3 | `market-config.phase1.json` | maxPositionPct 0.02 → 0.08 (Phase 1 only) |
| 4 | `Speciation.cs` | Replaced Guid-ordering with first-inserted/stable-previous representative |
| 5 | All phase configs | checkpointIntervalGens 25 → 5 |
| 6 | `Tier1RewardTests.cs` | Updated to assert restored reward shape |
| 7 | `MarketEvolution.cs` + `Program.cs` | Observability: per-gen output stats, close-reason histogram, population pos/zero/neg counts, in-process [STUCK-WARN] detector |
| 8 | NEW `V11dEvolutionSmokeTests.cs` | Mini-evolution must find profitable genome within 15 gens |
| 8 | NEW `V11dDeterminismTests.cs` | Same-seed runs produce bit-identical reports |
| 9 | NEW `V11dOutputLearnabilityTests.cs` | Outputs 6-10 reachable under intentional brain signal |

### V11d Verification (smoke tests before full launch)

**Unit + integration tests:** 347/347 green
- `RewardShapeVerificationTests` (7 tests) confirm the math
- `V11dEvolutionSmokeTests.MiniEvolution_FindsProfitableGenome_Within15Gens` PASS
- `V11dDeterminismTests.SameSeed_TwoRuns_ProduceBitIdenticalReports_Gen0to5` PASS
- `V11dOutputLearnabilityTests` (7 tests) confirm outputs 6-10 reachable

**Micro smoke** (20 pop × 3 gens × 1500h):
- Gen 2 found Best **+1.0035** (Sharpe 4.25, Sortino 6.73, 45 trades, 49% WR)
- 2 profitable genomes in population
- All 10 enrichment sources clean
- New `[returns]`, `[outputs]`, `[closes]` lines visible

**Mini smoke** (30 pop × 10 gens × 2000h):
- **Gen 1 BREAKTHROUGH: Best +1.1964** (Sharpe 5.69, Sortino 11.60, 6 trades, 50% WR)
- Gen 3 NEW HIGH: **+1.4328** (Sharpe 5.53, Sortino 8.24, 102 trades)
- Population maintained 1-7 profitable genomes throughout
- **Validation passed**: ValFit +0.1815 (vs pre-fix V11 ValFit -1.3260 — improvement of +1.5)
- No `[STUCK-WARN]` events
- Pace 22-32s/gen at pop 30

**Comparison vs pre-fix V11:**
| Metric | V11 (broken) | V11d (fixed) |
|---|---|---|
| First positive fitness | gen 6 (lucky run 1), never (run 2) | **gen 1** (mini smoke) |
| Validation fitness | -1.3260 (overfit) | **+0.1815** (generalizes) |
| Population pos count | 0 throughout | 1-7 most gens |
| Stuck detector | n/a (didn't exist) | did not fire |

### V11d Configuration

- All 6 fixes applied + 3 new test files
- All test suites green (347/347)
- Phase configs validated locally (checkpointIntervalGens=5 across all phases,
  maxPositionPct=0.08 in phase1 only)

---

## Phase 2 — Refinement

### Configuration
- Pop: 150, Gens: 1200
- Fitness emphasis: balanced 9-component with Sharpe (0.22)
- Eval: 3 windows × 5000h, walk-forward, minValFitness 0.02

### Observations

_(populated during training)_

---

## Phase 3 — Production

### Configuration
- Pop: 200, Gens: 1800
- Fitness emphasis: Sharpe (0.30), tightest criteria
- Eval: 3 windows × 6000h, strict walk-forward

### Observations

_(populated during training)_

---

## Known V11 risks

1. **Asymmetric reward shape** (loss 5x > profit 2x) may make passive locally optimal early.
   Mitigation: Phase 1's `activityBonusScale: 0.05` + high return weight pulls population
   toward activity. Mini smoke gen 2 breakthrough confirms the dynamics work.

2. **11-output action space** expands search radically. Outputs 6-10 have dead zones
   forcing the brain to explicitly commit to using them.
   Mitigation: V11c bootstrap bonus applies to all brain-driven close reasons.

3. **Multi-position pyramiding** allows up to 3 concurrent (MaxConcurrentPositions).
   MarketAgent aggregates PnL/direction across positions via weighted notional.

4. **Brain 2x larger** than pre-V11 means ~2x slower per-step. Monitor gen time.

## Historical context

Pre-V11 training runs (see `memory/project_training_log.md`) achieved:
- Phase 1 best fitness: +1.232 at gen ~150 (Sharpe 6.44, Sortino 11.41, DD 2.1%)
- Phase 2 best: +0.675 under tighter 2-window eval
- Phase 3 best: +0.378 under 3-window eval (early phase)

V11 should match or exceed these with the expanded action space and improved fitness
function — new action outputs unlock strategies that were physically impossible before.
