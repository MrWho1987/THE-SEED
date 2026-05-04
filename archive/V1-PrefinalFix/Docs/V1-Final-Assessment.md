# V1 Final Assessment — Where We Stand

**Date**: 2026-05-05
**Scope**: Comprehensive state of the system after V11d → V11e → Phase 1-4 training and 6.75 days of paper trading.
**Source**: training logs (V11d, V11e, Phase 4 = 11,823 lines combined), `training-observations-v1maximize.md` (770 lines), checkpoint analyzer outputs (10 mid-phase + final), paper-trading heartbeat + trade journals (since 2026-04-28 00:31 UTC).

---

## TL;DR

We invested **~24 days of compute across 4 training phases** and **6.75 days of paper trading**. The empirical v1 ceiling is **+2.0109 ValFit** (Phase 2 pop[149], 35% return on holdout, Sharpe 3.20). Phase 4 minimal (200 gens of relaxed weights) **failed all three target hypotheses**: didn't beat the +1.97 mid-phase peak, lost mid-phase value by end-phase, and confirmed the V11d founder-effect is structurally binding. Paper trading shows neither leaderboard candidate is yet real-money-ready: **pop[149] -$161.89 (-1.62%)**, **pop[193] -$7.35 (-0.07%)** over 6.75 days. The plan's decision matrix said this outcome would commit us to v2; Track F vision doc exists. Safety mechanisms are sound; the brain just isn't generating live alpha at the v1 capacity ceiling.

---

## What We Built (deliverables on disk)

### Code & infrastructure
- **8-project .NET 8 solution** (Seed.Core, Seed.Genetics, Seed.Brain, Seed.Development, Seed.Observatory, Seed.Market, Seed.Market.App, Seed.Dashboard) plus tests + tools.
- **Track A — Checkpoint Analyzer** (`tools/Seed.CheckpointEval/`): standalone tool that evaluates every population + archive genome against a fixed 4380h holdout. Used 10 times during Phase 4 monitoring. Without it, ~5 of our top-10 deployable genomes would not exist.
- **Track B fixes (Fix 1/2/3 + tests + smoke)**, committed at `640a20e`:
  - **Fix 1** (canonical save): `best_market_genome.json` now saves the genome whose stats are printed at end-of-phase. **Verified live in Phase 4** — saved GenomeId matches printed +0.6714 exactly. No save/print mismatch.
  - **Fix 2** (B4 deterministic clone): `InjectGenomeIntoPopulation` now refuses duplicate-GenomeId injection (defensive throw); call site derives a deterministic fresh `Guid` via `SeedDerivation`. Verified live in Phase 4 — `[PROTECT]` fired 3 times with unique IDs.
  - **Fix 3** (`--end-date` CLI): pipeline now accepts a fixed end-date, locking all phases to the same data window. Verified live: Phase 4 ran with `2026-04-20T21:36:00Z` matching all analyzer runs.
  - **Tests**: 368/368 pass (4 new tests, 364 inherited).
  - **Smoke** (`market-config.smoke.json`): all 9 verification checks passed.
- **Track F — V2 architecture vision** (`Docs/V2-Architecture-Vision.md`): drafted and committed.
- **Observations log** (`training-observations-v1maximize.md`, 770 lines): living source of truth, every WF event + analyzer finding + structural issue documented.

### Configurations
- `market-config.phase{1,2,3,4,4-minimal,smoke}.json` + `paper.json` + `paper-pop{149,193}.json`. All gitignored (contain API keys).

### Tests
- **368 tests passing**, including 4 new ones for the B-fixes (`PhaseEndSaveTests`, `InjectionAndTopNTests`).

---

## What We Trained — Compute Summary

| Phase | Gens | Pop | Eval | Wall clock | Outcome |
|---|---|---|---|---|---|
| **V11d era (broken deadzones)** | 985 | 200 | 3-window | ~7 days | **0 V11 firings — wasted gens, locked topology** |
| V11e Phase 1 | 600 | 150 | 1-window | ~1 day | Foundation, return-heavy weights |
| V11e Phase 2 | 1200 | 150 | 3-window | ~3.5 days | **Produced pop[149] +2.0109 — peak v1 deliverable** |
| V11e Phase 3 | 1800 | 200 | 3-window | **158.1h = 6.6 days** | Best mid-phase pop[193] +1.9671; degraded in last 100 gens |
| **V11e Phase 4 minimal** | 200 | 200 | 3-window | **63.0h = 2.6 days** | All H1/H2/H3 hypotheses failed |
| **Total** | **~4785 gens** | — | — | **~21 days** | — |

Total WF checks across V11e: ~74. Pass rate dropped phase-over-phase: 31.5% (P1+P2) → 19.6% (P3) → ~26% (P4 minimal: 14 of ~20 events).

---

## Deployable Genome Leaderboard (canonical analyzer, fixed holdout)

All entries scored on the **same fixed 4380h holdout** ending 2026-04-20T21:36:00Z under Phase 4 weights (Phase 2/3 entries re-scored under Phase 4 weights produce slight differences but rankings hold).

| # | Source | ValFit | Return | Sharpe | WR | DD | Trades | File |
|---|---|---|---|---|---|---|---|---|
| 1 | **Phase 2 pop[149]** | **+2.0109** | 35.0% | 3.20 | 48% | 8.4% | 2442 | `output_phase2/analysis/best_market_genome.json` |
| 2 | **Phase 3 pop[193] (gen 1700)** | **+1.9671** | 15.2% | 3.17 | 51% | 4.0% | 412 | `output_phase3/analysis_1700/best_market_genome.json` |
| 3 | Phase 2 pop[53] | +1.8968 | 14.1% | 3.00 | 49% | 3.8% | 387 | top10 cache |
| 4 | Phase 4 pop[49] (gen 1820) | +1.5274 | 7.9% | 2.56 | 52% | **2.4%** | 413 | `output_phase4_minimal/analysis_1820/best_market_genome.json` |
| 5 | Phase 4 archive sp.22 (gen 1900) | +1.5080 | 2.1% | 2.87 | 50% | **0.6%** | 187 | `output_phase4_minimal/analysis_1900/best_market_genome.json` |
| 6 | Phase 4 pop[71] (gen 1890) | +1.4938 | 2.1% | 2.84 | 50% | **0.6%** | 187 | `output_phase4_minimal/analysis_1890/best_market_genome.json` |
| 7 | Phase 2 pop[15] | +1.43 | 13.4% | 2.07 | 47% | 5.8% | 1648 | top10 cache |
| 8 | Phase 3 pop[46] (gen 1780) | +1.36 | 10.0% | 2.17 | 52% | 3.6% | 421 | `output_phase3/analysis_1780/best_market_genome.json` |
| 9 | Phase 4 archive sp.6 (gen 2000) | +0.9456 | 14.5% | 1.66 | 53% | 11.8% | 676 | `output_phase4_minimal/analysis_2000/best_market_genome.json` |

**Phase 4's contribution**: 3 entries in top-10 with **dramatically lower DD** (0.6%–2.4% vs Phase 2's 8.4% / Phase 3's 4.0%) but ValFit ceiling -0.5 lower. Risk-adjusted, **Phase 4 conservatives may be more deployable than the magnitude leaders** — this is a real finding even though H1 failed.

---

## Paper Trading — 6.75 days, 186 closed trades

**Live state (2026-05-04 21:37 UTC)**:

| | pop[149] (Phase 2 +2.01) | pop[193] (Phase 3 +1.97) |
|---|---|---|
| Closed trades | 75 | 111 |
| Long / Short | 35 / 39 | 53 / 57 |
| Wins after fees | **17 (23%)** | **31 (28%)** |
| Gross PnL | -$133.28 | +$9.04 |
| Fees | -$28.60 | -$16.39 |
| **Net** | **-$161.89 (-1.62%)** | **-$7.35 (-0.07%)** |
| Equity | $9851.67 | $9999.41 |
| Current direction | Flat | Flat |
| Live leverage | 1.75× | 1.41× |
| Live gateMean | 0.914 (high firing) | 0.699 (medium) |
| rolling Sharpe | +10.04 | +9.23 |

**Critical observations**:
1. **Both rawExit = 0.500 (neutral)** — the explicit-exit output never fires in either brain. Confirms S8 (training-time exit-output starvation). Both rely entirely on stop-loss / dirFlip transitions for exits.
2. **pop[193] significantly outperforms pop[149]** in live — opposite of what +0.04 ValFit gap suggested. pop[193] is essentially break-even (-0.07%) while pop[149] has lost 1.62%.
3. **pop[149] direction-discrimination failed** at multiple regime changes (V-bottom 04-28, rally 04-29, bottom catches in early May). Repeated wrong-side entries with elevated leverage.
4. **pop[193] showed adaptive bidirectional capture** on multiple occasions (documented in observations 04-29 V-bottom +$6.33, 04-30 morning down/up sequence).
5. **Trade frequency mismatch grew**: pop[193] now firing 6.8× backtest rate (was 4× at 27h, now sustained higher). Regime-mismatch warning. pop[149] is firing at 0.46×.

**Verdict**: Neither is real-money-ready. pop[193] is the closer candidate — break-even with sound direction-reading on regime changes — but the high-frequency-low-edge profile means fees consume gross gains.

---

## Hypotheses Tested — Final Verdict

The plan stated three pass/fail gates evaluated at gen 2000:

| H | Criterion | Phase 4 result | **Verdict** |
|---|---|---|---|
| **H1** (relaxed weights help) | Top genome > +1.97 | Phase 4 best at gen 2000 = +0.9456; mid-phase peak +1.5274 | **❌ FAIL** |
| **H2** (B-fixes preserve value) | Mid-phase peak ≤ end-phase peak | mid +1.5274 (gen 1820) vs end +0.9456 = **-0.58 regression** | **❌ FAIL** |
| **H3** (S7 founder-effect binding?) | V11 outputs \|mean\| > 0.1 in elite for ≥10% of gens | Gen 1999 elite: lv/prt/tre/trd/tp/sl all = 0.00:0.00 | **❌ FAIL — S7 is binding** |

**Per the plan's decision matrix** (3 fails): "v1 ceiling at +2.01 confirmed; v2 is the path forward."

---

## Structural Issues Catalogued (S1–S11)

The 11 issues documented during V11d→Phase 4 monitoring. Each is a real, evidence-backed finding that would affect any future v1 training and informs v2 design.

| ID | Issue | Status |
|---|---|---|
| **S1** | WF only tests `GetBestGenome()` — hidden generalizers lost | **Partially mitigated** by B5 (top-N WF); B5 still misses out-of-top-N generalizers |
| **S2** | ~60% of population never trades each generation | **Open** — fitness shrinkage + brain dev thresholds need investigation |
| **S3** | OVERFIT diagnostic detects but takes no action | **Open** — needs early-stop or window-advance integration |
| **S4** | Speciation in churn mode (MaxStag 25/25 always; CtAdj pinned 10) | **Open** — stagnation limit and compatibility threshold growth need redesign |
| **S5** | Fitness function gameable in 10+ overfit modes | **Open** — needs train/val divergence penalty during training (v2-scope) |
| **S6** | Hidden assets need salvage workflow | **Mitigated** by manual mid-phase analyzer (proven essential — 5 of top-10 genomes saved this way) |
| **S7** | **Founder-effect — V11d's 985 broken-deadzone gens locked 6-output topology** | **CONFIRMED BINDING** by Phase 4 (H3 fail). Cannot be reversed in 200 gens of relaxed weights; likely cannot in 600 either |
| **S8** | Three-position trap in live deployment (rawExit dead) | **Confirmed in paper** — both candidates at rawExit=0.500 throughout |
| **S9** | Brain-output-activity inspection should be a deploy gate | **Refined**: yesterday's "dead leverage" diagnosis on pop[193] was wrong (selective firing, not dead). Still a real concern for genuine output-deadness; need variance threshold over time |
| **S10** | In-training WF ValFit ≠ analyzer ValFit for same genome | **Open** — 12-40% gap observed between `valEvaluator` (long-lived) and analyzer's fresh `MarketEvaluator`. Likely brain plasticity / accumulator state leak. Untraced source. **In-training values cannot be used as canonical metrics** until fixed. |
| **S11** | End-of-phase eval blind to archive elites | **NEW from Phase 4** — `BacktestRunner.Evaluate(evolution.Population, ...)` skips the 26 archive entries. Phase 4's actual best (archive sp.6 +0.9456) was invisible to canonical save (which chose pop[126] +0.6714). Easy fix: union pop+archive in end-of-phase eval. |

---

## What's Working

1. **Determinism is intact**: same RunSeed + same checkpoint → same evolution trajectory. The cross-phase same-checkpoint analyzer reads (gen 1820, 1900) reproduce within float-noise bounds.
2. **Pipeline mode** (`--pipeline phase1 phase2 phase3 phase4`) cleanly resumes between phases, copies checkpoints forward, applies stagnation reset. **No data corruption observed** across 4 phases of resumes.
3. **Track A analyzer** is fast (~15 min per checkpoint), deterministic, and has been the actual safety net for value preservation. It found:
   - Phase 2 pop[149] +2.01 (would have been +1.86 by save/print bug)
   - Phase 3 pop[193] +1.97 at gen 1700 (would have been deleted by gen 1800)
   - 5 Phase 4 genomes that the active population bred away
4. **Track B fixes (Fix 1/2/3)** all worked correctly under live Phase 4 production.
5. **Safety mechanisms** (stop-loss, kill-switch, daily-loss limit, max-concurrent-positions) are sound — paper trading has hit max DD only once for either agent and has not blown up.
6. **Paper trading harness** (live Binance feeds, P&L tracking, heartbeat journaling) works reliably for 6+ days of unattended operation.
7. **Test suite** (368/368) is comprehensive enough to catch regressions on the eval path.

---

## What's NOT Working

1. **The v1 ceiling is empirically +2.01 ValFit**. After 21 days of compute, no genome breaks +2.05. Phase 4's relaxed-weight experiment confirmed this is a real ceiling, not a tuning issue.
2. **In-training metrics drift from canonical** (S10). Cannot trust WF reports as absolute. `[PROTECT]` injection fires on inflated values.
3. **End-of-phase canonical save misses archive** (S11). Real best in pop+archive is invisible to the printed/saved genome.
4. **Founder-effect locked the topology** (S7). 985 gens of broken deadzones during V11d cannot be undone with relaxed weights post-hoc.
5. **Exit output never trains** (S8). Every deployable genome has `ex:0.00:0.00` mean:std. In live, this becomes the 3-position trap.
6. **~60% of population is dead weight** every generation (S2). 120/200 genomes pay full eval cost while producing zero trades.
7. **Speciation churn** (S4): MaxStag 25/25 nearly every checkpoint — the system spends most time in extinction-recovery, not productive search.
8. **Paper trading shows neither candidate is profitable** at 6.75 days. pop[149] -1.62%, pop[193] break-even. The +2.01 backtest result does not transfer to live trading at production scale.

---

## What's Missing

Things that would meaningfully change v1 deployability if added, in priority order:

1. **S11 fix**: union pop+archive in end-of-phase eval. ~5 LOC change, would have promoted archive sp.6 +0.9456 to canonical save. Easy quick-win.
2. **S10 root-cause investigation**: trace why `valEvaluator.EvaluateSingle` and `MarketEvaluator.Evaluate` produce different scores for the same genome on the same window. Could be plasticity state, RegimeStart/RegimeEnd, or something else. Determinism guarantees depend on fixing this.
3. **Auto-analyzer per checkpoint** (S6 cheap fix): every 5-gen checkpoint, run analyzer in background. Would eliminate the manual intervention need. Adds ~15 min wall-clock per checkpoint to a non-blocking task.
4. **Train the explicit exit output** (S8): add a fitness term penalizing stale open positions. Would fix the 3-position trap structurally.
5. **OVERFIT-action integration** (S3): when validation has been declining for K gens, force-advance the WF window or halt the phase. Currently it just logs.
6. **Output-activity deploy gate**: refuse to deploy genomes where any of the 11 outputs has variance < threshold across the holdout. Would have blocked both pop[149]'s aggressive constant-leverage and any structurally-dead-output candidate.
7. **Real-money simulation harness** including realistic slippage, partial fills, exchange-side rejections, etc. Paper currently uses simple slippage model.

---

## Where We Stand on the "Ultimate AI Trading Agent" Goal

**Direct answer**: we are very far from it.

- **Goal**: an agent that consistently makes money in live markets.
- **Current evidence**: 6.75 days of paper trading on the two best v1 candidates → both net-negative, the better one (pop[193]) at -0.07% (effectively break-even within fee noise).
- **The +2.01 ValFit holdout result does not translate to live alpha**. This is not a deploy-readiness gap to be closed with one more phase; it is the v1 architecture's natural ceiling.

What we DO have:
- A working evolutionary pipeline (NEAT + CPPN + HyperNEAT brain compilation).
- A diagnostic toolchain (analyzer, observations, structural-issue catalogue).
- A documented list of v1 limitations (S1-S11) that informs v2 requirements.
- Two genomes that pass synthetic validation, currently being live-tested.
- Safety/risk infrastructure validated under live operation.

---

## Recommended Path Forward

### Stop
- **Further v1 training phases**. Diminishing returns confirmed. Phase 5 with 600 more gens at relaxed weights would be ~5 more days of compute against a known ceiling.
- **Real-money deployment** of any current candidate. Paper data is unambiguous: not yet ready.

### Continue (low-cost, high-signal)
- **Paper trading both candidates**. Each additional week of data refines our regime-coverage and confirms whether any genome can sustain through varied conditions.
- **Mid-phase analyzer salvage** as a discipline for any future training run (proven essential).

### Quick fixes worth doing on v1 (each <1 day)
- **S11 fix** (end-phase eval includes archive): ~30 LOC + test. Recovers proper canonical save.
- **S8 mitigation** (fitness penalty for stale open positions): trains the explicit-exit output. Would fix 3-position trap.
- **S3 fix** (OVERFIT triggers action): add early-stop or window-advance at K=15. Prevents wasted gens chasing dead-end optima.
- **S10 investigation**: trace the ValFit divergence source. Without this, no in-training metric is trustworthy.

### Commit to v2 (the structural answer)
The Track F V2-Architecture-Vision.md exists. The empirical evidence for committing:
- **S7 founder-effect** is binding and cannot be cleared in v1's evolutionary timescale.
- **The +2.01 ceiling** is real after 21 days of compute and 4 phases of weight-tuning.
- **Live alpha gap**: holdout success doesn't transfer; the v1 brain capacity (1200 neurons, sparse recurrent) appears insufficient for the regime-discrimination required.

V2 should keep what works (signal pipeline, paper-trading harness, risk layer, backtesting cache, analyzer) and replace the brain runtime + learning algorithm. Per the vision doc: transformer-based predictor + RL (PPO/SAC) + remove evolution + remove CPPN.

### Decision gate for v2
Before committing engineering effort to v2, decide:
- **Compute budget**: GPU cluster or cloud, 6-18 month engineering scope.
- **Live data coverage required**: how many weeks of paper-trading the v1 candidates before declaring v1 done?
- **Which v1 components to keep verbatim** vs reimplement under new constraints.

---

## Files of Record

| Artifact | Path | Purpose |
|---|---|---|
| This file | `Docs/V1-Final-Assessment.md` | The state-of-v1 reference |
| Living observations | `training-observations-v1maximize.md` | Per-event diary, full WF history, all 11 structural issues |
| V2 vision | `Docs/V2-Architecture-Vision.md` | What we'd replace and why |
| Top deploy candidate | `output_phase2/analysis/best_market_genome.json` | pop[149] +2.01 |
| Secondary candidate | `output_phase3/analysis_1700/best_market_genome.json` | pop[193] +1.97 |
| Phase 4 best | `output_phase4_minimal/analysis_2000/best_market_genome.json` | archive sp.6 +0.9456 (lower magnitude, lower DD) |
| Last commit | `640a20e` | All Track A/B/F + fixes |
| Paper logs | `output_paper_pop{149,193}/{trades,heartbeat}.jsonl` | 6.75 days of live data |

---

## Final Statement

V1 is functionally complete and architecturally exhausted. The pipeline runs, the fixes work, the analyzer recovers value, and we know exactly which genomes are deployable. **What we don't have is one that profitably trades live.** That gap is structural, not configurational, and v2 is the answer.
