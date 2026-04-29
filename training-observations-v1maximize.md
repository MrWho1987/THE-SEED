# V1 Maximize-Value Observations

Living log of findings, actions, and decisions during the v1-maximize-value work (2026-04-22 to ongoing).

## Context

Phase 3 of V11e training running on PID 28832 since 2026-04-21. Goal: extract maximum deployable value from v1 training while drafting v2 vision.

## Autonomous Action Triggers

| Signal | Severity | Autonomous action | Requires approval |
|---|---|---|---|
| Training process dies | CRITICAL | Alert user | — |
| STUCK-WARN fires | HIGH | Investigate + alert | — |
| Phase transition occurs | HIGH | Alert + prep next-phase assets | — |
| WF Pass ValFit > +1.0 | HIGH | Run analyzer on that checkpoint | — |
| WF Pass ValFit > +2.0 | HIGH | Run analyzer + flag as potential new deploy target | Deploy decision |
| WF Fail ValFit < -1.0 | MEDIUM | Log + continue | — |
| V11-heavy elite emerges (>15% V11 closes) AND WF passes | HIGH | Run analyzer + document | — |
| V11-heavy elite emerges AND WF fails | MEDIUM | Log + document pattern | — |
| New best-val genome saved (best_val_gen_NNNN.json) | HIGH | Run analyzer on that checkpoint | — |
| 50+ gens since last analyzer run AND training active | LOW | Log only, no action | — |
| Phase 3 naturally completes | HIGH | Run final analyzer A4 | Phase 4 launch |
| Paper trading deploy | — | — | Always requires approval |
| Real-money deploy | — | — | Always requires explicit approval |
| Real git commits / pushes | — | — | Always requires approval |

---

## Deployable Candidate Leaderboard (all phases, updated over time)

Last updated: gen 1700 post-analyzer (2026-04-28)

| Rank | Source | ValFit | Return | Sharpe | WR | DD | Trades | Saved as |
|---|---|---|---|---|---|---|---|---|
| 1 | **Phase 2 pop[149]** | **+2.01** | **35.0%** | **3.20** | 48% | 8.4% | 2442 | `output_phase2/analysis/best_market_genome.json` |
| 2 | **Phase 3 pop[193] (gen 1700)** | **+1.97** | **15.2%** | **3.17** | 51% | 4.0% | 412 | `output_phase3/analysis_1700/best_market_genome.json` |
| 3 | Phase 2 pop[53] | +1.90 | 14.1% | 3.00 | 49% | 3.8% | 387 | `output_phase2/analysis/top10_val_genomes.json#rank2` |
| 4 | Phase 2 pop[15] | +1.43 | 13.4% | 2.07 | 47% | 5.8% | 1648 | `output_phase2/analysis/top10_val_genomes.json#rank3` |
| 5 | Phase 3 pop[116] (gen 1375) | +1.33 | 10.0% | 2.11 | 42% | 4.3% | 384 | `output_phase3/analysis_1375/best_market_genome.json` |
| 6 | Phase 3 archive sp.25 (gen 1375) | +1.19 | 3.9% | 1.94 | 57% | 2.0% | 975 | `output_phase3/analysis_1375/top10_val_genomes.json#rank2` |
| 7 | **Phase 3 pop[46] (gen 1780)** | **+1.36** | **10.0%** | **2.17** | 52% | 3.6% | 421 | `output_phase3/analysis_1780/best_market_genome.json` |
| 8 | Phase 3 pop[31] (gen 1780) | +1.35 | 10.4% | 2.20 | 67% | 3.2% | 144 | `output_phase3/analysis_1780/top10_val_genomes.json#rank2` |
| 9 | Phase 3 pop[130] (gen 1700) | +1.14 | 9.1% | 1.76 | 50% | 3.6% | 1014 | `output_phase3/analysis_1700/top10_val_genomes.json#rank2` |
| 10 | Phase 3 archive sp.39 (gen 1375) | +1.11 | 8.5% | 1.83 | 43% | 3.9% | 65 | `output_phase3/analysis_1375/top10_val_genomes.json#rank3` |
| 11 | Phase 3 pop[22] (gen 1800) | +1.20 | 18.9% | 1.73 | 36% | 15.4% | 129 | `output_phase3/analysis_1800/best_market_genome.json` |
| 12 | Gen 1230 (in-training best-val) | +0.88 | 0.9% | — | — | — | 110 | `output_phase3/checkpoints/best_val_gen_1230.json` |

**Current deploy target: Phase 2 pop[149]** (+2.01) — Phase 3's final analyzer (gen 1800) produced only pop[22] +1.20 with 15.4% DD (riskier), confirming Phase 3 peaked at gen 1700 and degraded thereafter.

---

## Phase 3 Walk-Forward History

Updated live as WF events fire.

| Gen | Result | ValFit | Stall | Window |
|---|---|---|---|---|
| 1200 | Fail | -1.0843 | 1/30 | 26880 |
| 1210 | Fail | -1.0436 | 2/30 | 26880 |
| 1220 | **Pass** | **+0.1059** | 0/30 | → 27552 |
| 1230 | **Pass** | **+0.8761** | 0/30 | → 28224 |
| 1240 | Fail | -0.6966 | 1/30 | 28224 |
| 1250 | Fail | -0.3498 | 2/30 | 28224 |
| 1260 | Fail | -1.7479 | 3/30 | 28224 |
| 1270 | Fail | -1.3059 | 4/30 | 28224 |
| 1280 | Fail | -1.3505 | 5/30 | 28224 |
| 1290 | Fail | -0.7863 | 6/30 | 28224 |
| 1300 | Fail | -0.0759 | 7/30 | 28224 |
| 1310 | Fail | -0.9169 | 8/30 | 28224 |
| 1320 | Fail | +0.0163 | 9/30 | 28224 (miss by 0.004) |
| 1330 | Fail | -0.8474 | 10/30 | 28224 |
| 1340 | Fail | -0.4809 | 11/30 | 28224 |
| 1350 | **Pass** | **+0.2321** | 0/30 | → 28896 |
| 1360 | **Pass** | **+0.5848** | 0/30 | → 29568 |
| 1370 | Fail | -0.8076 | 1/30 | 29568 |
| 1380 | Fail | -0.8542 | 2/30 | 29568 |
| 1390 | Fail | -1.0499 | 3/30 | 29568 |
| 1400 | **Pass** | **+0.3778** | 0/30 | → 30240 |
| 1410 | Fail | -1.3665 | 1/30 | 30240 |
| 1420 | **Pass** | **+0.2670** | 0/30 | → 30912 |
| 1430 | Fail | -1.0392 | 1/30 | 30912 |
| 1440 | Fail | -0.2976 | 2/30 | 30912 |
| 1450 | Fail | -0.6185 | 3/30 | 30912 |
| 1460 | Fail | -0.7939 | 4/30 | 30912 |
| 1470 | Fail | -0.5180 | 5/30 | 30912 |
| 1480 | Fail | -0.5474 | 6/30 | 30912 |
| 1490 | Fail | -0.2435 | 7/30 | 30912 |
| 1500 | Fail | -1.4108 | 8/30 | 30912 |
| 1510 | Fail | -0.2453 | 9/30 | 30912 |
| 1520 | Fail | -0.7079 | 10/30 | 30912 |
| 1530 | Fail | -0.0175 | 11/30 | 30912 (miss by 0.018) |
| 1540 | Fail | -0.1666 | 12/30 | 30912 |
| 1550 | Fail | -0.9200 | 13/30 | 30912 |
| 1560 | Fail | -0.8616 | 14/30 | 30912 |
| 1570 | Fail | -0.9974 | 15/30 | 30912 |
| 1580 | **Pass** | **+0.0263** | 0/30 | → 31584 |
| 1590 | Fail | -0.4977 | 1/30 | 31584 |
| 1600 | Fail | -0.3134 | 2/30 | 31584 |
| 1610 | Fail | -0.4426 | 3/30 | 31584 |
| 1620 | Fail | -1.1285 | 4/30 | 31584 |
| 1630 | Fail | -1.0004 | 5/30 | 31584 |
| 1640 | Fail | -1.0567 | 6/30 | 31584 |
| 1650 | Fail | -0.9234 | 7/30 | 31584 |
| 1660 | **Pass** | **+0.6921** | 0/30 | → 32256 |
| 1670 | Fail | -1.2690 | 1/30 | 32256 |
| 1680 | Fail | -0.9700 | 2/30 | 32256 |
| 1690 | Fail | -1.2498 | 3/30 | 32256 |
| 1700 | **Pass** | **+1.1395** | 0/30 | → 32928 |
| 1710 | Fail | -1.1490 | 1/30 | 32928 |
| 1720 | Fail | -0.4233 | 2/30 | 32928 |
| 1730 | Fail | -1.3324 | 3/30 | 32928 |
| 1740 | **Pass** | **+0.3027** | 0/30 | → 33600 |
| 1750 | Fail | -0.5873 | 1/30 | 33600 |
| 1760 | Fail | -0.4057 | 2/30 | 33600 |
| 1770 | Fail | -0.8250 | 3/30 | 33600 |
| 1780 | **Pass** | **+1.5398** | 0/30 | → 34272 |
| 1790 | Fail | -0.9401 | 1/30 | 34272 |

**Stats**: 11 passes / 56 checks = 19.6%. Window advanced 34272 - 26880 = 7392 bars (+26.4% of training window).

---

## Key Findings Log

### 2026-04-23 — Analyzer discovers hidden best genome in checkpoint_1330
- Ran Checkpoint Analyzer on `output_phase3/checkpoints/checkpoint_1330.json`
- Found: archive species 25 elite at ValFit +1.1987 (previously untested by WF)
- Gen 1230 (in-training tracked best-val) at +0.88 was ranked #11
- **Implication**: WF-only-tests-best-training pathology confirmed; hidden best genomes exist

### 2026-04-24 — Analyzer on Phase 2 checkpoint_1200 reveals true best
- Ran on `output_phase2/checkpoints/checkpoint_1200.json`
- Found: pop[149] at ValFit **+2.0109**, 35% return, Sharpe 3.20, 2442 trades
- Phase 2's end-of-phase eval had printed 1.8597 (different genome) — discrepancy unresolved, possibly parallel non-determinism
- **Implication**: Phase 2 pop[149] is the all-time best deployable genome across all phases

### 2026-04-24 — V11-heavy lineage emerges and fails WF (gen 1378-1380)
- Gen 1378 elite: fit 1.71, WR 55%, 483 trades, DD 4.5%, DDDur 34.8%, **688 partial closes** (97% V11 usage)
- Gen 1380 WF: Failed at -0.8542 (train/val gap 2.57)
- **Interpretation**: partialClose output discovered but produced regime-specific overfit
- **Evidence for Phase 4**: This is exactly the failure mode Phase 4's relaxed consistency weight + higher fee drag is designed to prevent
- Similar pattern in Phase 2 gen 1140 (partial:90) passed WF at +0.12 under Phase 2's looser weights

### 2026-04-24 — Analyzer on checkpoint_1375 shows Phase 3 progress
- New Phase 3 best: pop[116] at ValFit +1.33 (was +1.20 at checkpoint_1330, 45 gens earlier)
- **Improvement rate**: +0.13 ValFit / 45 gens → projecting ~+1.5 by gen 1500 if trend holds
- Still well below Phase 2 pop[149] at +2.01
- 26 species champions preserved in archive

### 2026-04-24 — Second V11-heavy elite re-emerges after WF fail (gen 1390-1391)
- Gen 1390 elite: fit 0.60, ret 7.1%, WR 54%, 117 trades, `partial:22 trail:2` (24/58 = 41% V11 usage); WF **Failed** -1.0499
- Gen 1391 elite (replaced after WF fail): fit 0.84, ret 5.1%, WR 60%, 146 trades, **92 partial + 2 trail / 124 closes = 76% V11 usage**
- Output magnitudes show partialClose (`prt:-0.07:0.43`), sl (`sl:0.03:0.79`), lv (`lv:0.07:0.76`), sz (`sz:0.95:0.13`) all active — genome is heavily exploiting V11 action space
- **Pattern** (with gen 1378-1380 lineage): V11-heavy genomes keep winning on training fitness but failing WF validation. Phase 3's windowConsistencyWeight=0.10 + shrinkageK=5.0 punishes cross-window variance, and partial-close strategies appear regime-dependent.
- **Evidence strengthens Phase 4 rationale**: relaxed weights (consistency 0.02, shrinkage 3.0, feeDrag 0.05) are explicitly designed to let V11-heavy genomes compete fairly while penalizing true over-turnover
- No analyzer trigger fired (WF failed, no new best-val); following the "Log only" protocol for V11-heavy + WF-fail

### 2026-04-28 — Analyzer on checkpoint_1780 (gen 1780 WF Pass +1.54)
- WF Pass at +1.5398 — **strongest Phase 3 in-training pass observed** — triggered analyzer
- WF-tested elite (gen 1779): pure dirFlip+cfgSL, 262 trades, DD 0.7%, train fit 1.25, val fit *exceeded* train (1.54) — indicates WF measures forward-looking validation while analyzer uses fixed final-4380h holdout (different windows, both valid signals)
- **Analyzer #1 on fixed holdout: pop[46]** at ValFit +1.3588, ret 10.0%, Sharpe 2.17, WR 52%, DD 3.61%, **421 trades**, GenomeId `0e4030f5-8a8b-4d6a-b50a-680e01332556`
- Analyzer #2: pop[31] at +1.35, ret 10.4%, Sharpe 2.20, **WR 67%** (high), **144 trades**, DD 3.2% — distinct strategy from #1
- **Doesn't beat current leaders**: pop[193] (+1.97) and Phase 2 pop[149] (+2.01) remain top
- **Implication**: WF's forward-looking +1.54 is encouraging (best of Phase 3 by that metric) but doesn't translate to fixed-holdout dominance. The fixed-holdout (older data) selects different genomes than the moving-window WF.

### 2026-04-28 — Early paper trading P&L (first ~24h, statistically meaningless yet)
- **pop[149]** (Phase 2 candidate, ValFit +2.01): **7 trades, gross +$0.27, fees -$0.76, net -$0.48** on $10k → ~breakeven
- **pop[193]** (Phase 3 candidate, ValFit +1.97): **8 trades, gross -$6.03, fees -$0.66, net -$6.69** on $10k → -0.067%
- pop[149] last 5 trades: -1.28, +0.16, +0.46, +0.54, +0.89 (mostly winning shorts in 76.4-77.1k range)
- pop[193] last 5 trades: -0.36, -0.16, -2.16, -1.69, -1.21 (5-loss streak, mostly long-biased catching falling BTC)
- **Caveat**: 7-8 trades is statistical noise; need ≥30 trades / 2-week observation before any signal
- **Tentative observation**: pop[149] showing more directional discrimination (mix of long+short with shorts winning); pop[193] biased toward longs against a downtrending tape
- Both processes alive (PID 19464, 32388), heartbeat.jsonl writing every ~5s

### 2026-04-29 — Phase 3 ENDED at gen 1800 + final analyzer reveals **degradation in last 100 gens**
**Phase 3 termination** (per `training-v11e.log`):
- Final gen: 1800 (158.1h wall-clock)
- End-of-phase printed: "Best validation fitness: 1.5676 / Sharpe 2.48 / Return 10.05% / 394 trades / WR 52% / DD 3.14%"
- "Best genome (by validation) saved to: output_phase3\best_market_genome.json" — **but file timestamp is 28-Apr 8:55 PM (the gen 1780 WF Pass save)**, NOT 02:32 AM (Phase 3 end). **Save/print mismatch recurred** (same Phase 2 bug; Track B fix B1 was coded but JIT'd process never reloaded).
- Pipeline: `[PIPELINE] All 3 phases complete.` — Phase 4 was NOT in the pipeline arg list, did NOT auto-launch.

**Final analyzer on `checkpoint_1800.json`** (fixed final-4380h holdout, deterministic):
- #1: pop[22] ValFit **+1.1967**, ret 18.93%, Sharpe 1.73, WR 35.7%, **DD 15.37%** (high), 129 trades — GenomeId `149d6de9-8e8c-4044-9e64-58e8767e8ecb`
- #2: pop[130] +1.0736, only 24 trades (low activity)
- #3: pop[159] +0.9989, 258 trades, DD 0.6%
- #7: pop[193] +0.5215 — **the gen 1700 +1.97 leader is now only +0.52** (different genome at same index, confirmed S1 pathology — pop[193] gen 1700 was lost by gen 1800)
- #8: pop[46] +0.5213 — same: gen 1780's +1.36 leader is now +0.52

**Gen 1800 best (+1.1967) vs Phase 3 historical bests on the FIXED holdout**:
| Source | ValFit | Trades | DD% | Saved? |
|---|---|---|---|---|
| Phase 2 pop[149] (gen 1200) | +2.0109 | 2442 | 8.4% | yes |
| Phase 3 pop[193] (gen 1700) | +1.9671 | 412 | 4.0% | yes |
| Phase 3 pop[116] (gen 1375) | +1.33 | 384 | 4.3% | yes |
| **Phase 3 pop[22] (gen 1800)** | **+1.1967** | **129** | **15.4%** | **yes (just saved)** |

**Decisive fact**: Phase 3's last 500 gens (1300→1800) produced no deployable improvement on the fixed holdout. Peak Phase 3 deployable was at **gen 1700**. The end-of-phase printed +1.5676 references a different validation window (forward-looking after training end) — on the FIXED holdout (consistent across all phases) the gen 1800 best is only +1.1967.

**WF discrepancy mechanism confirmed**: end-of-phase val uses moving validation window (after gen 1800 training end at 34272 bars); analyzer uses fixed final-4380h holdout from `--end-date 2026-04-20`. Different windows reward different genomes. The analyzer is the cross-phase comparable metric.

**Implication for Phase 4 decision**: Phase 3 was a **declining pipeline** in its last 100 gens — peaks mid-phase, erodes by end. Phase 4 inherits this degraded gen 1800 state. Whether Phase 4 reverses this requires the relaxed weights + B4 protection + B5 top-N WF to actually overcome whatever caused the decline.

### 2026-04-29 — Paper trading deep-read at 27h (behavior verdict ≠ P&L verdict)
**P&L state** (insufficient for fitness verdict but signed correctly):
- pop[149]: 6 closed + 3 open, equity $10,000.21 (+$0.21 net), realized -$0.40, unrealized +$0.61
- pop[193]: 7 closed + 3 open, equity $9,992.69 (-$7.31 net), realized -$6.60, unrealized -$0.71
- Win rate after fees: pop[149] **3/6 (50%)**, pop[193] **0/7 (0%)**

**Trade-frequency mismatch — diagnostic insight**:
- pop[149] backtest = 2442 trades / 4380h = 0.557/hr → expected 15 in 27h, actual 9 → **0.6× (more selective live)**
- pop[193] backtest = 412 trades / 4380h = 0.094/hr → expected 2.5 in 27h, actual 10 → **4.0× (firing far more often)**
- pop[193]'s 4× over-firing is a regime-mismatch signature: brain's entry circuit hits some signal threshold much more often in live BTC than in 17,520-bar holdout

**Backtest-scaled expectation vs reality**:
- pop[149]: expected +$22 in 27h (35% / 4380 × 27), actual +$0.21 (underpaced but signed correctly)
- pop[193]: expected +$9 in 27h, actual -$7.31 (~$16 below expectation, **wrong sign**)

**Direction discrimination — closed trade tape**:
- pop[149]: 3 longs at 77.0k → all losing → **switched to 3 shorts at local high** → all winning. Mean-reversion-aware behavior.
- pop[193]: 3 shorts at 76.8k → BTC went up → all losing → 4 longs at 76.7k-76.9k → BTC went down → all losing. Anti-momentum / wrong-sided every regime change.

**Founder-effect (S7) confirmation in live brain outputs**:
| Output | pop[149] (Phase 2) | pop[193] (V11d-descendant) |
|---|---|---|
| rawLeverage | 0.43 (active, scaled 1× → 8× over 24h) | **0.00 (dead, pinned at 1×)** |
| Gate range | 0.45-0.90 (analog modulation) | 0.987-0.989 (rigid binary) |
| Direction pivots | Yes (long → short) | No (stuck on losing dir) |
| Size variation | 4.7× range | 2.7× range |
| rSharpe (rolling) | **+6.30** | **-7.75** |

The +0.04 analyzer ValFit gap (+2.01 vs +1.97) is hiding a structural brain-quality gap that only live behavior surfaces. **Analyzer cannot detect dead V11 output wiring** — single-window backtest doesn't exercise leverage if regime didn't reward it during training.

**Implication for v1 deployment policy**: the analyzer is necessary but not sufficient. **Add brain-output-activity inspection as a deploy gate** — refuse to deploy genomes where any of {rawLeverage, rawSize, rawExit, rawTrailDist, rawTpOffset, rawSlOverride} is structurally dead.

### 2026-04-28 — Gen 1785 multi-output anomaly (transient, low-impact)
- Single dominant genome briefly took population at gen 1785: outputs lv/prt/tre/trd/tp/sl all simultaneously at **0.33:0.31** (mean:std identical across 6 outputs)
- Indicates a CPPN topology where 6 V11 outputs share the same generator — all encoded by one shared sub-network
- Activity: only **6 closes total** (3 dirFlip, 3 eos), DD 14.2%, ret 9.4%
- This is OVERFIT no-action behavior (S3) — high-fitness from low-volatility plus a few lucky closes, not real V11 usage
- Replaced at gen 1786 by another dirFlip-dominated genome (248 closes, 205 dirFlip)
- **Insight**: Phase 3 keeps producing brief peaks where one genome's quirks dominate, then cycles back. Genome diversity is low (consistent with S4 speciation churn).

### 2026-04-28 — Analyzer on checkpoint_1700 finds hidden genome **near-tying Phase 2 leader**
- WF triggered analyzer at gen 1700 (Pass +1.1395, first Phase 3 pass >+1.0)
- Analyzer evaluated 222 unique genomes (200 pop + 26 archive, 4 dedup) on 17,520-bar (4,380h) holdout
- **#1 by ValFit: pop[193]** at **+1.9671**, ret **15.18%**, Sharpe **3.17**, WR **51.0%**, DD **3.98%**, **412 trades**
- The training-tracked WF passer (pop[130], +1.1393) ranked **#2** — confirming again that WF tests only the top-trained genome and misses better generalizers
- pop[193] vs Phase 2 pop[149] (current leader at +2.01): **0.04 ValFit gap, 51% vs 48% WR, 3.98% vs 8.4% DD, 412 vs 2442 trades** — pop[193] is more conservative with similar generalization
- Profile: GenomeId `3618867b-9d2f-4791-a652-c35e0e2e523a`, saved as `output_phase3/analysis_1700/best_market_genome.json`
- **Implication for deploy**: Phase 3 has finally produced a genome that approaches Phase 2's quality. Two strong candidates now in play. The analyzer **discovers** value the training pipeline misses — without it, pop[193] would have been lost forever (Phase 3 ends gen 1800)

### 2026-04-25 — Phase 3 record-high training fit produces massive overfit (gen 1493-1500)
- Gens 1493-1499 elite reached training fit **2.06** (highest in Phase 3, exceeds prior peak 1.68 at gen 1432)
- Strategy was **non-V11**: pure dirFlip + cfgSL, low DD (0.5%), short DDDur (17%), Sharpe 4.00, Sortino 6.15
- Output magnitudes confined to dir + pr (price), with TP/SL outputs at very low magnitudes (0.06-0.13)
- **Gen 1500 WF: Failed -1.4108** (train 2.06 / val -1.41 → gap ~3.5, the largest gap observed)
- **Confirms hypothesis**: Phase 3's training pressure produces overfit regardless of strategy class. Even conservative non-V11 strategies with strong risk metrics fail to generalize when the training fit gets very high.
- **Mechanism inference**: training reward (Sharpe + Sortino dominant) is too easy to game on the specific 30912-bar training window. ANY strategy that gets close to fit 2.0 on this window is exploiting window-specific structure rather than learning general patterns.
- **Implication for deploy decision**: Phase 2 pop[149] (ValFit +2.01, 35% return, Sharpe 3.20 on the holdout) remains the only robust deploy candidate — Phase 3 has produced no genome that beats it on validation
- WF -1.41 is close to but does not exceed -1.5 deep-overfit threshold (just barely)

### 2026-04-25 — First V11 lineage to use trail+TP+SL instead of partialClose (gen 1457)
- Gen 1457 elite: fit 0.97, ret 7.2%, WR 35%, 56 trades, DD 7.7%
- Closes: dirFlip:0 cfgSL:8 brainSL:9 TP:8 trail:12 partial:0 → **20/40 = 50% V11 usage**
- Output magnitudes: `tre:0.08:0.57 tp:0.44:0.47 sl:0.55:0.45 lv:-0.02:0.52 prt:-0.04:0.26 trd:0.04:0.55`
- **This is a structurally different V11 strategy** — exploits trailEnable, tpOffset, and slOverride/brainSL while ignoring partialClose
- Replaces the 3-lineage partial-close pattern (gen 1378, 1391, 1410) with broader V11 output usage
- Has NOT yet been WF-tested (gen 1460 WF imminent next cycle)
- Lower train-fit (0.97) than partial-close lineages (1.19-1.68) but uses risk-management outputs the others ignored
- **Hypothesis to verify at gen 1460 WF**: this strategy may generalize better than partial-close strategies because trail/TP/SL are inherently more regime-robust
- **UPDATE — gen 1460 WF result**: Failed at -0.7939 (train 0.97 / val -0.79 → gap 1.76). Hypothesis disconfirmed — trail+TP+SL also overfits the training window
- **Refined conclusion**: Phase 3's training pressure overfits ANY V11-heavy strategy regardless of which V11 outputs are used. The issue isn't partial-close specifically — it's that V11 outputs collectively give the brain enough flexibility to memorize regime-specific patterns. Consistent with the OVERFIT diagnostic firing

### 2026-04-25 — [OVERFIT] warning fires at gen 1420 despite WF Pass
- System-generated diagnostic: "Validation declining for 19 checks while training improves"
- Context: gens 1401-1419 training fit trended from 0.55 → 1.37 (+148%), but per-check ValFit trended downward
- Gen 1420 WF still Passed (+0.2670) and advanced window to 30912, but the mean-over-time trend shows widening train/val gap
- **Implication**: Phase 3's training pressure is producing genomes that fit training ever-better without corresponding generalization gains
- **Reinforces deploy recommendation**: current training is reaching diminishing returns; Phase 2 pop[149] at +2.01 remains the best deployable candidate, not anything from late Phase 3
- This is not a STUCK warning (different diagnostic) — training is progressing mechanically but quality-wise plateaued

### 2026-04-24 — Third V11-heavy lineage is MOST EXTREME yet (gen 1410)
- Gen 1410 elite: fit 1.19, ret 2.9%, WR 48%, **1017 trades**, `partial:1327 trail:2` out of 1375 total closes = **96.5% V11 usage**
- Near-pure partial-close strategy: zero dir-flip closes, 35 cfgSL, 8 brainSL, everything else partial
- WF **Failed** at -1.3665 (train 1.19 / val -0.18 → gap 1.37) — strong overfit signal
- Pop trades jumped from ~33k to ~22k (fewer distinct traders, one high-activity strategy dominates)
- **Progression observed across three lineages**: gen 1378 (90 partial), gen 1391 (92 partial), gen 1410 (1327 partial) — each lineage pushes partial-close usage higher, each fails WF more severely (-0.85 → -1.05 → -1.37)
- **Interpretation**: Phase 3 weights (consistency 0.10, shrinkage 5.0) create a local training optimum around high-frequency partial-closes that regime-overfits. The mechanism rewards intra-window micro-scalping without generalizing.
- **Phase 4 rationale further strengthened**: This exact failure mode is what feeDrag weight 0.05 (vs 0.02) + relaxed consistency 0.02 is designed to counterbalance.
- No analyzer trigger (WF fail, no new best-val saved); log-only per protocol

---

## Structural Issues Surfaced (need investigation/fix)

Findings that emerged from the Phase 3 monitoring run (gens 1201–1702). These are **patterns**, not events — they affect any future training and won't be solved by Phase 4 weights alone.

### S1 — WF-only-tests-best-training pathology (3 confirmations, **structural**)
- gen 1230 hidden archive elite (+1.20 vs WF +0.88)
- Phase 2 hidden pop[149] (+1.86 printed → +2.01 actual)
- gen 1700 hidden pop[193] (+1.97 vs WF-tracked +1.14)
- **Cause**: WF runs against `evolution.GetBestGenome()` (single best by training fit). Hidden generalizers in pop+archive are invisible.
- **Fix**: B5 (`WalkForwardTopN`) is coded but unused in this run. Without it OR an automatic per-checkpoint analyzer, every phase silently loses its best generalizers.
- **Workaround until B5 ships**: run analyzer on every checkpoint_NNNN.json automatically.

### S2 — ~60% of population never trades, every generation
- inactPct ranged **50–74%** across 502 Phase 3 gens; MedTrd consistently **0**.
- Implication: ~120 of 200 genomes pay full evaluation cost while producing zero trading signal.
- **Hypothesis**: selection pressure may favor inaction (zero variance, no DD, no kill-switch trigger). Combined with shrinkageK=5.0 only mildly penalizing low-trade genomes.
- **Investigation needed**: brain development thresholds (TopKIn=32, MaxOut=40), action interpreter deadzones (0.70), or fitness-shrinkage interaction. May indicate the brain-input pipeline is producing too-quiet signals for most random genomes.

### S3 — OVERFIT detection fires for hundreds of generations with zero action
- "Validation declining for K checks while training improves" reached **K=46** at gen 1690 before resetting at gen 1700 pass.
- The system **detects** divergence but takes **no action** — no early stopping, no window advance, no diversity injection.
- **Cheap fix**: at OVERFIT ≥ N (say 20), force-advance the validation window OR halt the phase. Both prevent the population from spending hundreds of gens chasing a dead-end fitness optimum.

### S4 — Speciation in churn mode
- **MaxStag hits 25/25** (configured StagnationLimit) at almost every checkpoint.
- **CtAdj pinned at 10.00** (compatibility threshold cap) consistently.
- Species are constantly being culled and reset → exploration is dominated by extinction-recovery rather than diversification.
- **Investigation needed**: StagnationLimit may be too aggressive, or compatibility threshold growth needs different bounds (e.g., adaptive based on genomic distance distribution).

### S5 — Fitness function is gameable in many directions
- Catalogued **~10 distinct overfit modes** across Phase 3:
  - Partial-close-heavy (gens 1378, 1391, 1410, 1480, 1520, 1541, 1620 lineages)
  - Trail+TP+SL-heavy (gens 1457, 1605 lineages)
  - DirFlip+cfgSL with fit 2.0+ (gens 1493, 1575)
  - Mixed exit+TP+SL (gen 1697 — the rare WF-passing exception)
- The 9-component fitness reduces but doesn't eliminate any single mode — it just shifts which mode wins.
- **Structural fix needed (v2-scope)**: directly penalize train/val divergence during training, not just at WF time. Could use a held-out mini-window for per-gen validation.

### S7 — Founder-effect hypothesis: Phase 1 genome topology may be locked into 6-output strategies (HIGH-IMPACT)
- **Verified mechanism**: V11d had deadzones at 0.80 that were **mathematically unreachable** (sigmoid(tanh) ceiling = 0.731). V11d log: **0 V11 firings across 985 generations**. V11e fix lowered deadzones to 0.70 and produced 92 firings in 170 gens.
- **Implication**: during Phase 1 + early Phase 2 (~985 gens), V11 outputs (partialClose, trailEnable, trailDist, tpOffset, slOverride) physically could not fire. Selection saw zero P&L impact from V11-output wiring. **Genome topology converged on 6-output circuits**.
- **Evidence in Phase 3**: V11e fix activated ~gen 985, but the first V11-using lineage to PASS walk-forward appeared at gen 1697 — a **~712-gen lag** for V11 strategies to evolve from scratch against established 6-output champions.
- **Evidence in catalogued overfit lineages**: most V11-heavy lineages (gens 1378, 1391, 1410, 1480, 1520, 1541, 1620) are transient and overfit — they emerge without evolutionary depth and crash. The successful gen 1697-1699 (+1.14 WF) was a rare exception that mixed exit + V11.
- **Open question**: a fresh-from-Phase-1 restart with V11e working from gen 0, Phase 4 weights, and B4/B5 enabled COULD plausibly produce significantly better results because:
  - 1800 gens of evolution with all 11 outputs functional from start = no founder handicap
  - Population shaped by relaxed consistency weight from gen 0
  - B5 catches hidden generalizers like pop[193] in real time
- **But it's an empirical question** — could also produce a worse local optimum.
- **Cheaper diagnostic before full rebuild**: run Phase 4 (already configured, ~5-6 day compute) — if it produces structurally V11-rich genomes that beat +1.97, founder effect is real and a fresh restart is likely to win. If it produces another 6-output dominated leader at similar quality, founder effect isn't the binding constraint and a different intervention (e.g., genome-level mutation that randomly rewires V11 outputs) would be needed.
- **Reverses earlier "skip Phase 4" recommendation**: given S7, Phase 4 is now the most informative next experiment.

### S6 — Hidden assets need a salvage workflow
- Without the analyzer, pop[193] (+1.97) would have been deleted by gen 1800.
- No mechanism exists during training to discover/save high-val genomes that aren't top-trained.
- **Cheap fix**: run analyzer **automatically on every checkpoint** during training, save results inline. Adds ~15 min per 5 gens (one-time per checkpoint), gains complete coverage.
- **Better fix**: integrate per-checkpoint analyzer pass into the training loop itself.

### S8 — Three-position trap in live deployment (V1 architectural finding, **affects all deployed agents**)
- Observed in both paper sessions at 27h: each brain opened 3 positions in first 7h, then **stopped closing trades for 12+ hours**.
- Mechanism:
  - Both brains have `rawExit ≈ 0.5000` (neutral default) — explicit exit output never fires
  - Training logs confirm `ex:0.00:0.00` (mean:std) in nearly every Phase 3 gen — the exit output was never selected during evolution
  - Training relied on `dirFlip` (mean-reversion entry triggers self-close) + `cfgSL` (config stop-loss) + `eos` (end-of-sample, non-applicable live) for closes
  - In paper: dirFlip is rarer (slower regime), eos doesn't apply, so closes wait passively on stop-loss only
- **Result**: trained agent gets stuck at maxConcurrentPositions, becomes effectively a "buy-and-hold-with-stop-loss" strategy regardless of training pattern
- **Distribution gap**: training selects on close-frequency that emerges from dirFlip-rich windows; live windows give very different dirFlip rates
- **Cheap mitigations**:
  - Add `minHoldTicks` floor with auto-close on hold-time exceeded
  - Train the explicit exit output by adding fitness penalty for stale open positions
  - Lower `maxConcurrentPositions` from 3 to 1 in paper (force serial trading)
- **Real fix (v2-scope)**: train brain in a true online regime where positions must be explicitly closed via the exit output

### S9 — Brain-output-activity inspection should be a deploy gate
- Live data revealed pop[193]'s `rawLeverage` is structurally dead (always 0.000), while pop[149]'s scales 1×→8× actively
- Backtest analyzer doesn't surface this — single-window evaluation may not exercise an output if the training window didn't reward it
- **Cheap fix**: extend `tools/Seed.CheckpointEval` to log per-genome activity profile across all 11 outputs, flag any that are structurally dead (variance < 0.001 across 4380h)
- **Add to deploy checklist**: "Verify all V11 outputs have non-trivial variance during validation."

---

## Code Changes Applied (awaiting Phase 4 restart)

All Track B code fixes compiled clean but not yet running in the live training process (JIT-loaded at process start).

- B1: Save full-pop best-val at phase end → `best_val_from_pop.json`
- B2: Save species-champion ensemble → `ensemble_champions.json`
- B3: Save top-5 by validation → `top5_val_genome_rank{NN}.json`
- B4: `ProtectBestValInPop` config field + `InjectGenomeIntoPopulation()` method
- B5: `WalkForwardTopN` config field + `GetTopNByTrainingFitness()` method + parallel top-N WF eval in Program.cs
- B6: Signal-count validation in paper mode

Test status: 364/364 tests pass (6 new tests added, no regressions).

---

## Pending Decisions / Actions

- [x] User decision: deploy which genome to paper trading? → **Both pop[149] and pop[193] launched in parallel, $10k each (2026-04-28)**
- [x] Paper trading launch: alt-path build (`build_paper/`), training DLL locks not affected
- [ ] **Paper trading active**: pop193 PID 32388 (output_paper_pop193/), pop149 PID 19464 (output_paper_pop149/)
- [ ] Phase 4 launch: **REACTIVATED** as founder-effect (S7) diagnostic — run after Phase 3 ends. If V11-rich genomes beat +1.97, founder effect is real → consider fresh-restart. If 6-output dominated leader emerges at similar quality, founder effect isn't binding.
- [ ] Real-money deployment: requires explicit user approval after 2+ weeks of paper trading data
- [ ] Future training: address structural issues S1-S9 above

---

## Paper Trading Metrics To Track Going Forward

Per S8/S9 findings, future paper-trading sessions should log these every check-in (cheap to compute, high signal):

| Metric | Computation | Significance threshold |
|---|---|---|
| **Trade-frequency ratio** | (paper trades/hr) / (backtest trades/hr) | <0.5 or >2.0 = regime mismatch |
| **Backtest-scaled P&L delta** | actual − (backtest_return / backtest_hours × paper_hours) | >2 stddev below = paper underperformance |
| **Open-position dwell time** | seconds since last trade close, while positions > 0 | >12h with all 3 slots full = position trap |
| **Output-activity variance** | per-tick stddev of {rawLeverage, rawSize, rawExit, rawTrailDist, rawTpOffset, rawSlOverride} | <0.001 = output structurally dead |
| **Direction-flip rate post-loss** | shares of trades where direction switches after a loss within K trades | <20% = anti-adaptive (overfit) |
| **Gate range** | max gateMean − min gateMean over rolling window | <0.05 = brain in binary mode (rigid) |
| **rSharpe drift** | sign and magnitude of heartbeat rSharpe over last K hours | sustained <-5 for >K hours = paper failure |
