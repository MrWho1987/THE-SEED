# Paper Trading Findings — Phase 2 Agent

**Date:** March 31, 2026  
**Genome:** `bff5ecde-2322-4f4b-b737-0760efd2f7c0` (Phase 2 best, gen 600)  
**Config:** `market-config.paper.json`  
**Market:** BTCUSDT  
**Duration:** ~12 hours (09:40–21:25 UTC)

---

## Training Background

### Phase 1 — Bootstrap (Generations 1–300)

Return-dominated fitness (70% return weight, 0% Sharpe/Sortino) with a single 2000h evaluation window. Objective: evolve populations that actively trade and generate returns. Produced agents that trade frequently but with poor risk management.

### Phase 2 — Quality Training (Generations 301–600)

Seeded from Phase 1 checkpoint. Transitioned to risk-adjusted fitness:

| Parameter | Phase 1 | Phase 2 |
|---|---|---|
| Sharpe weight | 0.00 | 0.25 |
| Sortino weight | 0.00 | 0.10 |
| Return weight | 0.70 | 0.35 |
| Drawdown duration weight | 0.02 | 0.10 |
| CVaR weight | 0.15 | 0.20 |
| Eval windows | 1 | 3 |
| Eval window hours | 2000 | 6000 (2000 per window) |
| Consistency penalty | 0 | 0.15 |
| Shrinkage K | 1.0 | 5.0 |
| Min trades for active | 1 | 3 |

Phase 2 produced agents optimized for risk-adjusted returns across diverse market regimes with walk-forward validation.

---

## Paper Trading Session

### Session Summary

| Metric | Value |
|---|---|
| Start time | 2026-03-31 09:40 UTC |
| End time | 2026-03-31 21:25 UTC |
| Runtime | ~11 hours 45 minutes |
| Starting capital | $10,000.00 |
| Final equity | $10,019.14 |
| Session return | **+0.19%** |
| Peak equity | $10,030.84 (+0.31%) |
| Trough equity | $9,973.06 (-0.27%) |
| Peak-to-trough drawdown | 0.58% |
| Closed trades | 0 |
| BTC entry price | ~$66,503 |
| BTC final price | ~$68,017 |
| BTC session move | +2.31% |
| Capture ratio | ~8.3% |

### Brain Architecture

The evolved CPPN has 17 nodes (9 input, 2 hidden, 6 output) and 60 connections. Through substrate development, this produces a brain with 865 neurons and 4,918 synapses.

### Chronological Action Log

**09:40–09:44 (Ticks 0–44): FLAT — Observation**  
The agent observed the market for ~4 minutes without acting. During this period, the `SignalNormalizer` was seeding its rolling statistics (cold start). BTC traded around $66,477.

**09:44 (Tick 46): Opened LONG at BTC ~$66,503**  
The agent's first and only action. Entry costs (slippage + taker fee) caused an immediate mark-to-market of -$15.32. Position size was conservative at roughly 10–15% of capital (~$1,000–$1,500 notional).

**09:44–21:25 (Ticks 46–7880): Held LONG — No further actions**  
The agent held a single LONG position for the entire remaining session (~11 hours 40 minutes). Zero adjustments, zero exits, zero additional entries.

### Equity Curve Phases

**Phase 1 — Underwater (09:44–13:40, ~4 hours)**  
Entry costs plus an initial BTC dip to $66,415 pushed equity to a session low of $9,973.06 (-0.27%). BTC ranged $66,400–$66,700. Slow grind back toward breakeven.

**Phase 2 — Breakeven Cross (13:40–14:00)**  
Equity crossed above $10,000 as BTC pushed past $66,780.

**Phase 3 — First Profit Zone (14:00–14:30)**  
Peak at ~$10,012. Then BTC pulled back, dropping equity to $9,984.81 around 14:56.

**Phase 4 — Rally to Session High (15:00–17:15)**  
BTC rallied strongly from $66,600 to $68,494. Equity peaked at **$10,030.84** at 17:15 UTC. Rolling Sharpe reached +26.04.

**Phase 5 — Retracement and Stabilization (17:15–21:25)**  
BTC retraced from $68,494 to $67,400, then recovered to $68,000 range. Equity oscillated between $10,005 and $10,025. Agent held passively through all volatility.

---

## Analysis

### What Went Right

1. **Directional conviction.** The agent correctly identified the bullish trend early and entered LONG within 4 minutes. BTC moved +2.3% during the session.

2. **Ultra-low drawdown.** Maximum peak-to-trough of 0.58% (from initial $10,000 to $9,973 low). The 15% kill switch was never remotely threatened. This is directly attributable to Phase 2's drawdown duration and CVaR fitness weights.

3. **Zero overtrading.** No churn, no unnecessary round-trips. This patient behavior aligns with Phase 2's consistency penalty and multi-window evaluation, which penalize erratic strategies.

4. **Resilience through volatility.** The agent held through a -$25 drawdown from peak equity without panic-selling, and recovered each time.

### What Went Wrong

1. **Capture efficiency: 8.3%.** BTC moved +2.31%; the agent returned +0.19%. A passive 25% BTC allocation would have returned ~0.58% — three times more. The conservative position sizing is learned from Sharpe optimization (smaller positions = lower volatility = higher Sharpe), but limits absolute returns.

2. **Zero exits.** After 11+ hours of continuous holding, the agent never closed its position. This means:
   - We have zero evidence of exit logic functioning
   - Win rate, realized Sharpe, and actual risk management during exits are all unknown
   - The agent may have learned that "holding" is always safer than "exiting"

3. **Profit retention: 38%.** The agent reached +$30.84 unrealized at peak but ended at +$19.14. It gave back 38% of peak gains without any protective action (no trailing stop, no partial take-profit).

4. **Single-direction only.** The agent only went LONG. We have no evidence it can SHORT, suggesting a potential long-only bias from training data.

### Behavioral Interpretation

The Phase 2 training produced a **pure trend-following holder**:
- Enters directionally after brief observation
- Uses very conservative position sizing (optimized for Sharpe, not return)
- Holds through all volatility without adjusting
- Does not take profits, cut losses, or manage position size dynamically

This behavior is consistent with what the fitness function would optimize for:
- Multi-window consistency penalty rewards stable strategies
- Sharpe/Sortino weights reward low-volatility returns
- CVaR weight penalizes tail risk, encouraging small positions
- The combination learns that "small position + hold + don't trade" minimizes fitness variance across evaluation windows

---

## Recommendations for Next Phase

1. **Longer paper trading run (48–72h minimum).** 12 hours is insufficient to see exits, regime changes, or the full daily cycle (funding rate resets, session opens).

2. **Introduce exit incentive in Phase 3 training.** The current fitness function doesn't explicitly reward profit-taking or loss-cutting. Consider adding a "trade completion bonus" or penalizing unrealized P&L drawdowns.

3. **Increase position sizing floor.** The Sharpe-optimal position is too small for meaningful returns. Consider a minimum position size parameter or a return-floor penalty for under-deployment.

4. **Multi-agent ensemble.** Deploy multiple evolved agents (from different training windows or fitness weight mixes) and combine signals for more robust trading.

5. **Stress test with historical drawdowns.** Backtest the Phase 2 genome against known crash periods (e.g., March 2020, May 2021, November 2022) to evaluate behavior under extreme conditions.
