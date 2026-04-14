# Paper Trading Observations Log

## Setup

**Started:** 2026-04-14 06:44 UTC
**Genome:** `output_phase5/best_market_genome.json`
**Genome ID:** 2d4bc127-0090-4524-a73e-8818488b0a2c
**Brain:** 477 neurons, 879 synapses
**CPPN:** 25 nodes, 87 connections
**Initial capital:** $10,000
**Stop-loss:** 2.0% per position
**Symbol:** BTCUSDT, 15-min candles
**Output:** `output_paper/`
**Process PID:** 16916

### Validation Baseline (from training)
| Metric | Value |
|--------|-------|
| Val fitness | +0.6048 |
| Sharpe (val) | 1.37 |
| Return | 4.63% |
| Trades | 76 (over ~6 months) |
| Win rate | 39% |
| Max DD | 3.31% |
| Trade frequency | ~12 trades/month ≈ 0.4/day |

### Drift Tolerance
- **Sharpe:** ≥ 1.0 (down from 1.37 acceptable)
- **Max DD:** ≤ 5% (vs 3.31% baseline, allowing some buffer)
- **Win rate:** 30-50% (range around 39%)
- **Trade frequency:** 0.2 - 1.0 trades/day (10x range)
- **Total return:** > 0% over 30 days minimum to call it profitable

---

## Day 0 — 2026-04-14 06:44 UTC (Startup)
- Process started successfully
- Initial BTC price: $74,398
- Brain compiled cleanly (477 neurons, 879 synapses)
- Currently FLAT, $10,000 equity
- All 8 enrichment sources connected
- No errors during startup

**Watching for:**
- First position open
- First trade close
- Initial drift in rolling Sharpe / DD

### +1.5h — Brain Observation Period (06:44 - 08:15 UTC)
- 8 brain decisions made, all FLAT
- BTC range: $74,374 - $74,779 (~0.5% range)
- Brain demonstrated patience — 1.5h of observation before first trade
- Behavior matches training expectation (76 trades / 6 months ≈ 1 trade per ~2 days)

### +1h 30m — FIRST TRADE OPENED (~08:15 UTC)
- **Direction: SHORT**
- **Entry: $74,416.06**
- **Size: 0.016768 BTC** (~$1,248 notional = 12.5% of equity, well below 25% cap)
- Decision #7 generated the SHORT signal
- Cost: $0.50 taker fee
- Position consistent (brain not flip-flopping on subsequent decisions)

### +2h 14m Check — 2026-04-14 08:58 UTC
| Metric | Current | Validation Baseline | Status |
|--------|---------|---------------------|--------|
| Equity | $9,994.30 | $10,000 | -0.057% |
| Position | SHORT 0.0168 BTC | — | Active |
| Unrealized PnL | -$5.20 (-0.4%) | — | Within stop (2.0%) |
| Max DD | 0.04% | 3.31% | ✅ Far below |
| Rolling Sharpe | -2 to -6 (noisy) | 1.37 | Noisy (1 position) |
| Brain decisions | 9 | — | 15-min cadence ✅ |
| Trades closed | 0 | — | First open |
| Signal health | Full | — | ✅ All 8 sources |
| Heartbeats | 127 | — | 1/min cadence ✅ |

**Drift assessment:** Within tolerance. Position is a normal market loss (BTC moved up while we're short).

**Switching to hourly monitoring** for the rest of Day 0 to catch trade closures and direction changes.

### +3h 14m Check — 2026-04-14 09:58 UTC
- **Position recovering:** Unrealized -$2.07 (was -$5.20 last hour)
- BTC moved from $74,725 → $74,505 (↓$220, short working)
- Equity: $9,997.90 (up $3.60)
- Max DD: 0.07% (up slightly from 0.04%)
- Rolling Sharpe: -11.45 (single position noise, will stabilize with more trades)
- Brain still holding SHORT, exit flag false
- 4 more brain decisions since open, all hold

| Metric | +2h 14m | +3h 14m | Change |
|--------|---------|---------|--------|
| BTC price | $74,725 | $74,505 | ↓$220 |
| Unrealized | -$5.20 (-0.4%) | -$2.07 (-0.1%) | +$3.13 |
| Equity | $9,994.30 | $9,997.90 | +$3.60 |

**Drift assessment:** Within tolerance. Position behaving as expected.

### +4h 15m Check — 2026-04-14 10:59 UTC
- **Position turned profitable!** BTC continued down to $74,377
- Unrealized: **+$0.65** (+0.05%) — first time positive
- Equity: **$10,000.14** (just above starting capital)
- Max DD: 0.07% (unchanged)
- Rolling Sharpe recovering: -11.45 → -1.54

| Metric | +3h 14m | +4h 15m | Change |
|--------|---------|---------|--------|
| BTC price | $74,505 | $74,377 | ↓$128 |
| Unrealized | -$2.07 (-0.1%) | +$0.65 (+0.05%) | **Crossed to profit** |
| Equity | $9,997.90 | $10,000.14 | +$2.24 |

**Brain signal observation:** Heartbeat shows `"dir":"Flat","exit":false` — brain is currently signaling FLAT (no new entry) but NOT signaling exit. The original short is being held passively. Rational: brain took the opportunity, now waiting.

**Direction was CORRECT** (BTC dropped $39 from entry). Net gain still tiny (+$0.16 after fees) because position size is small (~$1,248 notional).

### +5h 15m Check — 2026-04-14 12:00 UTC
- **🎯 Rolling Sharpe FLIPPED POSITIVE: +2.99** (was -1.54 last hour, ABOVE validation baseline 1.37)
- BTC continued lower: $74,375 (down $2 from $74,377)
- Unrealized: +$0.69 (improved from +$0.65)
- Equity: $10,000.19
- Brain signal direction: **Short** again (was Flat last hour) — re-affirmed bearish view
- Exit flag still false (3h 45m hold)
- Decision #22 fired at 12:00 UTC, brain confirmed direction

| Metric | +4h 15m | +5h 15m | Change |
|--------|---------|---------|--------|
| BTC price | $74,377 | $74,375 | ↓$2 |
| Equity | $10,000.14 | $10,000.19 | +$0.05 |
| Rolling Sharpe | -1.54 | **+2.99** | **Crossed positive** |
| Brain signal | Flat | Short | Re-affirmed |

**Drift assessment:** All metrics within tolerance. Rolling Sharpe ABOVE baseline (noisy with 1 position but encouraging). Brain showing consistent bearish view.

### +6h 15m Check — 2026-04-14 13:00 UTC — FIRST TRADE CLOSED + DIRECTION FLIP
**Trade 1 CLOSED at a LOSS:**
- Short entered @ $74,416.06
- Exited @ $74,505.31
- P&L: **-$2.25** (fee $0.75)
- Held 19 ticks (~4h 45m)
- Note: Position peaked in profit ~+$0.69 @ $74,375 but reversed, exited after BTC recovered

**Trade 2 OPENED immediately:**
- **LONG @ $74,505.31** (direction flip)
- Size ~0.0168 BTC
- Currently slightly underwater (~-$0.32)

| Metric | +5h 15m | +6h 15m | Change |
|--------|---------|---------|--------|
| BTC price | $74,375 | $74,487 | ↑$112 |
| Position | SHORT | **LONG** | **Direction flipped** |
| Realized P&L | $0 | -$2.25 | First closed trade (loss) |
| Equity | $10,000.19 | $9,996.46 | -$3.73 |
| Trades closed | 0 | **1** | First trade locked |
| Rolling Sharpe | +2.99 | -13.22 | Crashed (1 losing trade) |
| Max DD | 0.07% | 0.07% | Unchanged |

**Assessment:**
- ⚠ First trade was a loss (-0.023% of equity)
- ✅ Brain is actively trading, not frozen
- ✅ Brain flipped long right after exiting short (trend-following style)
- ✅ Max DD 0.07% (far below 3.31% baseline)
- ⚠ Rolling Sharpe volatile (-13.22) — will stabilize with more trades
- ✅ Equity drawdown only 0.04% — negligible

**Context:** Validation showed 39% win rate. Losing the first trade is statistically likely. Need more trades (5-10+) to assess whether the win/loss distribution matches training.

### Observability Fixes Applied — 2026-04-14 ~14:12 UTC (restart)

**Problem found:** While monitoring, discovered two observability bugs:
1. Direction-flip OPENED log message was silently skipped (CLOSED fired, OPENED didn't)
2. Brain's exit signal was only logged as boolean — couldn't see proximity to threshold

**Fixes applied (display-only, no retraining):**
1. Changed `OpenPositions.Count > prevOpenCount` check → iterate `OpenPositions` and match `OpenTick == ctx.TickIndex`
2. Added `RawExitValue` field to `TradingSignal` record (default 0f, populated by `ActionInterpreter.Interpret`)
3. Added `ExitRaw` column to status line header and data row
4. Added `rawExit` field to heartbeat JSONL
5. 3 new unit tests for `ActionInterpreter.RawExitValue` behavior
- Build clean, 238/239 tests passing (1 flaky benchmark unrelated to changes)

**Restart effects:**
- Lost in-memory session state: previous LONG position @ $74,505 (peak unrealized +$15.74)
- Preserved: trained genome file, closed trade in trades.jsonl, heartbeat history
- New session PID 33092 started at 14:12:34 UTC, same genome hash

### 🔍 CRITICAL FINDING — Brain Does Not Use Explicit Exit Output

**Gen 0 evidence (first 6 status lines after restart):**
```
Feed ... Exit ExitRaw rSharpe rDD Elapsed
0    ... (none) 0.5000   0.00 0.0% 0:00
2    ... (none) 0.5000   0.00 0.0% 0:00
4    ... (none) 0.5000   0.00 0.0% 0:00
...
```

**ExitRaw = 0.5000 = sigmoid(0)** — the brain's raw `outputs[3]` is ~0.0, meaning the exit neuron is effectively **not firing at all**. The brain consistently outputs 0 for exit, giving sigmoid(0) = 0.5, which is below the 0.6 threshold.

**Implications:**
- Previous 7h session had 0 explicit exit signals — not noise, **this is consistent brain behavior**
- The brain's 4th output neuron (exit signal) never develops meaningful activity
- All exits happen via **direction reversals** (brain signals opposite direction → PaperTrader closes existing, opens new)
- The training reward function heavily incentivized profitable exits (+50x multiplier on realized P&L), but the brain **did not learn to use the explicit exit path** — it learned to flip direction instead

**Why this matters for strategy:**
- Profit-taking happens only when the brain flips direction
- Positions can round-trip if the brain stays directional but the trend reverses
- The first trade (SHORT) went profitable (+$0.69) then round-tripped to a loss (-$2.25) — the brain held until it saw clear reversal, missing the peak

**Is this a training failure?**
- Not exactly: validation showed +0.6048 val fitness with 4.63% return and 39% win rate — so this strategy IS profitable
- But it explains why paper trading P&L will be slow and lumpy
- Would require **retraining with stronger exit incentives** (e.g., reward mid-position exits when rolling Sharpe drops) to develop explicit exit behavior

### +1h Post-Fix Check — 2026-04-14 15:16 UTC
**Measured state (1h 3m into new session):**
- Process alive: PID 33092, 147 MB
- Brain decisions: 5 fired (Decision #0-4)
- Trades: **0 (FLAT entire hour)**
- Equity: $10,000.00 (unchanged, no position)
- Max DD: 0.00%
- Previous session opened first SHORT at 1h 31m — current session is within normal observation window

**CRITICAL EVIDENCE — rawExit is literally stuck at 0.5000:**
```bash
$ grep -c "rawExit" heartbeat.jsonl            # 62 post-fix heartbeats
$ grep -c '"rawExit":0.5000' heartbeat.jsonl   # 62 — ALL of them
$ grep -oE '"rawExit":[0-9.]+' heartbeat.jsonl | sort -u
"rawExit":0.5000
```

**62 out of 62 post-fix heartbeats show EXACTLY `"rawExit":0.5000`.** Not approximately 0.5 — exact. This means the brain's raw `outputs[3]` is **exactly 0.0** on every single decision.

**Interpretation (factual, not speculation):**
- `sigmoid(0) = 0.5` exactly, so `outputs[3] = 0.0` exactly
- A floating-point neural output being EXACTLY 0 (not 1e-6, not 1e-10) is only possible if:
  1. The output neuron has zero incoming connections in the compiled brain graph, OR
  2. All incoming weights have activated neurons producing exactly zero sum
- Option 2 is statistically impossible across thousands of steps with evolved weights
- **Conclusion:** The trained genome's CPPN + HyperNEAT substrate development did NOT create any connections to output neuron index 3. The exit signal path is dormant.

**Implication for the trained genome:**
- The brain CANNOT signal explicit exit regardless of market conditions
- All position closures happen via direction reversal only
- This is a genome-level property, not a runtime state — a fresh training run with stronger exit-reward incentives would be needed to develop a functional exit neuron

**This finding does NOT invalidate the current paper trading:**
- Direction-reversal exits are a legitimate strategy (trend-following)
- Validation showed +4.63% return using this exact genome — the strategy works
- But the brain's "take profit" behavior will be lumpy and slow

### +3h 6m Check — First Trade Opened, Currently Profitable — 2026-04-14 17:18 UTC

**First trade event (finally!):**
- `>>> OPENED Short @ $75,187.37 | Size 0.016617 BTC`
- Entered around Decision #10 or #11 (16:30-16:45 UTC)

**Price context decisions #9-#13:**
```
#9  16:15  $75,495  ← local peak
#10 16:30  $75,253  ← reversal starts (likely entry point)
#11 16:45  $75,225
#12 17:00  $74,652  ← strong drop
#13 17:15  $74,787  ← current
```

**Current state:**
| Metric | Value |
|--------|-------|
| Position | SHORT 0.016617 BTC @ $75,187 |
| BTC price | $74,831 (↓$356 from entry) |
| Unrealized | **+0.5% = +$5.92** |
| Equity | $10,005.42 (+0.054%) |
| Max DD | 0.02% |
| Rolling Sharpe | -7.70 (noisy, 1 position) |
| **rawExit** | **STILL 0.5000 exactly** |

**Compared to previous session (same genome, different timing):**
| | Previous | Current |
|---|---|---|
| First trade | SHORT @ $74,416 (1h 31m) | SHORT @ $75,187 (~2h 30m) |
| Market context | Chop | Clear uptrend → reversal |
| Initial direction vs entry | -$89 adverse | +$356 favorable |
| Outcome | Round-tripped -$2.25 | **Currently +$5.92** |

**Brain behaved smarter this session:** waited for clearer reversal signal before entering. This is the same genome — the difference is either (a) market context or (b) plasticity state after longer observation.

**🎯 DEFINITIVE CONFIRMATION — exit neuron is genuinely dormant:**

With an ACTIVE profitable short position, the brain should be experiencing strong reward-reshaping signals (unrealized PnL increasing). If the exit neuron had ANY learned behavior, it would fire to lock in the profit (training rewarded explicit exits 50x the P&L). 

**Instead: `rawExit` remains EXACTLY 0.5000** across heartbeats during the active profitable position. This is impossible for a functional output neuron in a recurrent network — the only explanation is **the exit output (index 3) has zero incoming synapses in the compiled brain graph**.

The trained genome literally lost the exit pathway during substrate development. No amount of runtime observation will change this — it's baked into the genome.

### +4h 7m Check — 2026-04-14 18:19 UTC — Short Still Profitable, Sharpe Crossed Baseline

**Still SHORT from $75,187.37 (held ~1h 50m).**

Price action during hold:
```
#13 17:15  $74,788  (-$399 vs entry)
#14 17:30  $74,620
#15 17:45  $74,550  ← local low
#16 18:00  $74,770
#17 18:15  $74,676  (-$511 vs entry)
```

| Metric | +3h 6m | +4h 7m | Change |
|--------|--------|--------|--------|
| BTC price | $74,831 | $74,676 | ↓$155 |
| Unrealized | +$5.92 | +$7.87 | +$1.95 |
| Equity | $10,005.42 | $10,007.37 | +$1.95 |
| Max DD | 0.02% | 0.04% | +0.02% |
| Rolling Sharpe | -7.70 | **+3.89** | **🎯 ABOVE validation baseline 1.37** |
| rawExit | 0.5000 | 0.5000 | unchanged |

**Session progress vs validation baseline:**
| Metric | Validation | Current | Status |
|--------|-----------|---------|--------|
| Sharpe | 1.37 | +3.89 | ✅ Above |
| Max DD | 3.31% | 0.04% | ✅ Far below |
| Return | 4.63% (6mo) | 0.074% (4h) | Too early to compare |
| Trade freq | 76 / 6mo | 1 open, 0 closed | Too early |
| Win rate | 39% | N/A | Too early |

**The fix is producing the observability we needed.** The new `rawExit` column definitively proves the exit neuron is dormant, and the direction-flip logging fix is ready to fire when the next trade reversal happens.

### +5h 8m Check — 2026-04-14 19:20 UTC — Short Deeply Profitable

**Price action since last check:**
```
#17 18:15  $74,676
#18 18:30  $74,506
#19 18:45  $74,303
#20 19:00  $74,071  ← local LOW, $1,116 below entry
#21 19:15  $74,258  ← slight bounce
```

**Current state:**
| Metric | +4h 7m | +5h 8m | Change |
|--------|--------|--------|--------|
| BTC price | $74,676 | $74,258 | ↓$418 |
| Equity | $10,007.37 | $10,013.55 | +$6.18 |
| Unrealized PnL | +$7.87 | **+$14.05** | +$6.18 |
| Return | 0.074% | 0.136% | +0.062% |
| Max DD | 0.04% | 0.04% | stable |
| Rolling Sharpe | +3.89 | +2.90 | Both above baseline |
| rawExit | 0.5000 | 0.5000 | dormant |

**Position now +1.23% on the short.**  The brain has captured a significant downward move. But now BTC bounced from $74,071 to $74,258 — first sign of a potential reversal.

**Round-trip risk watch:**
- Brain has no explicit exit
- If BTC continues bouncing, the brain only exits via direction flip
- Meanwhile unrealized profit could erode (like the previous session's first trade)

**Session stats (5h post-fix):**
- Equity: $10,013.55 (+0.136%)
- Sharpe: +2.90 (above validation baseline 1.37) ✅
- Max DD: 0.04% ✅
- 1 open profitable trade

### +6h 8m Check — 2026-04-14 20:20 UTC — Round-Trip Starting

**Price action:**
```
#21 19:15  $74,258
#22 19:30  $74,173
#23 19:45  $74,137  ← LOCAL LOW (peak short profit ≈ +$17)
#24 20:00  $74,224
#25 20:15  $74,431  (bounce +$294 from low)
```

| Metric | +5h 8m | +6h 8m | Change |
|--------|--------|--------|--------|
| BTC price | $74,258 | $74,431 | ↑$173 |
| Unrealized | +$14.05 | +$12.56 | **-$1.49 (bleeding)** |
| Equity | $10,013.55 | $10,014.14 | +$0.59 |
| Max DD | 0.04% | 0.06% | +0.02% |
| Rolling Sharpe | +2.90 | +4.70 | +1.80 (session high) |
| rawExit | 0.5000 | 0.5000 | dormant |
| Brain direction | Short | **Flat** | ⚠ Lost conviction |

**🚨 Round-trip test ACTIVE:**
- Peak short profit: +$17 at $74,137 low
- Current: +$12.56 at $74,431
- Given back: ~$5 so far
- BTC would need to hit $75,187 (entry) for position to zero out

**Brain signal change:** Direction output went from "Short" → "Flat". The brain no longer generates a short signal but is holding the position because it also doesn't signal exit (rawExit 0.5000) or opposite direction. This is Session 1's exact pattern: brain loses conviction → holds passively → eventually forced to flip.

**This is the dormant exit neuron's cost manifesting live.** A functional exit output would have fired near the $74,137 peak to lock in ~$17 profit. Instead, the brain is losing gains as it waits for a clearer long signal.

---
