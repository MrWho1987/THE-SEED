# V1 Ceiling-Test — Training Observations

Live observations log for the V1 ceiling test. Companion doc to `Docs/V1-Final-Assessment.md` (V1 baseline) and the eventual `Docs/V1-Ceiling-Test-Verdict.md` (final answer at gen 6000).

**Purpose:** the empirical question is *"does V1 architecture, with all known structural defects corrected, have meaningful headroom above +2.0109 ValFit (the archived Phase 2 pop[149] ceiling)?"*

If max-val-fit (median of 3 seeds) stays below ~+2.5, V1 is empirically exhausted and a V2 architectural commitment has full evidence. If it exceeds, V1 has headroom worth pushing further.

---

## Run config

| Field | Value |
|---|---|
| Config | `market-config.ceiling.json` |
| Generations | 6000 |
| Population | 200 |
| Training window | 26000h (~36 months) |
| Validation window | 4380h (~6 months, fixed holdout) |
| Eval sub-windows | 3 (multi-window averaged + 0.05 consistency penalty) |
| End date | 2026-04-28T00:00:00Z (matches archived V1 final analyzer) |
| Run seed | 42 |
| Cache | `archive/V1-PrefinalFix/cache/pipeline_shared_cache/` (103 MB, reused from V1 era) |
| Brain | 20×20×3 substrate, 1200 neurons, TopKIn 32, MaxOut 40 |
| Outputs | 11 (V11: dir, size, urg, exit, predict, lv, prt, tre, trd, tp, sl) |
| Inputs | 110 normalized signals |

## Active fixes (L0–L3)

| Fix | Setting |
|---|---|
| **S1** WalkForwardFullPopGens | 50 |
| **S2** InactivityPenalty / MinTradesForActive | -0.20 / 5 |
| **S3** OverfitAction | AdvanceWindow |
| **S4** CompatibilityThresholdMax / TargetSpeciesMax / MinStagnationImprovement | 30.0 / 20 / 0.02 |
| **S6** AutoAnalyzeOnCheckpoint | true |
| **S8** StaleThresholdTicks / StalePenaltyPerTick | 50 / 0.005 |
| **S9** DeployOutputStdMin | 0.01 |
| **S10** Analyzer MatchTraining mode available via `--match-training` flag |
| **S11** End-phase eval includes archive (always-on) |
| **B4 / B5** ProtectBestValInPop / WalkForwardTopN | true / 5 |
| **T1** WeightSchedule | 5 waypoints (gen 0/1500/3000/4500/6000) |
| **T2** Multi-phase RunPipeline | removed (single-config run) |
| **T3** BehavioralDiversity column | 0.00 → 0.05 across the schedule |
| **T4** DirFlipDominance column | 0.00 → 0.04 across the schedule |
| **T4** EoS auto-close | removed |
| **T5** Behavior-validation hooks | active (deadness + mode-collapse) |

## WeightSchedule curriculum

| Gen | Sharpe | Sortino | Return | DDDur | CVaR | Calmar | InfoR | FeeDrag | Diversif | Stability | BehavDiv | DirFlipPen |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| 0 | 0.10 | 0.05 | **0.40** | 0.15 | 0.17 | 0.05 | 0.03 | 0.03 | 0.02 | 0.00 | 0.00 | 0.00 |
| 1500 | 0.20 | 0.12 | 0.25 | 0.13 | 0.15 | 0.05 | 0.04 | 0.02 | 0.02 | 0.00 | **0.02** | 0.00 |
| 3000 | **0.25** | 0.13 | 0.18 | 0.13 | 0.15 | 0.05 | 0.03 | 0.02 | 0.02 | **0.02** | 0.02 | 0.00 |
| 4500 | 0.25 | 0.13 | 0.13 | 0.12 | 0.13 | 0.05 | 0.03 | 0.05 | 0.02 | **0.04** | **0.04** | **0.01** |
| 6000 | 0.23 | 0.13 | 0.12 | 0.12 | 0.12 | 0.04 | 0.03 | **0.05** | 0.02 | **0.05** | **0.05** | **0.04** |

Curriculum logic: return-tolerant start → gradually emphasize risk-adjusted returns (Sharpe up, Return down) → finally add stability + niching + dirFlip penalties for live realism.

---

## Leaderboard to beat (archived V1-PrefinalFix)

| Source | ValFit | Return | Sharpe | DD% | Notes |
|---|---|---|---|---|---|
| Phase 2 pop[149] | **+2.0109** | 35% | 3.20 | — | the V1 ceiling — what we're trying to beat |
| Phase 3 pop[193] | +1.9671 | 15.2% | 3.17 | — | stable runner-up |
| Phase 4 pop[49] (mid-phase) | +1.5274 | — | — | low | bred away by gen 1850 |

---

## Signals (computed at gen 6000)

| Signal | Threshold | Verdict |
|---|---|---|
| **A** Best validation fitness | > +2.5 → headroom; < +1.8 → regression | TBD |
| **B** Deploy-gate pass rate | 0/N → architecture can't learn V11 outputs | TBD |
| **C** Stability dispersion | > 50% → fitness is overfitting to specific windows | TBD |
| **D** Train/val gap on final pop | > 1.8 → severe overfit | TBD |

**v1-exhausted verdict requires:** Signal A median-of-3 < +2.5 AND (Signal B = 0 OR Signal C > 50%) AND Signal D > 1.8.

---

## Live monitor events

(Cron monitor populates this section as training progresses. Each entry: `gen N | event-type | summary`.)

