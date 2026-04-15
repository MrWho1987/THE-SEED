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

_(populated during training)_

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
