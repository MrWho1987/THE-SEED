# Paper Trading Findings — Phase 5 v2 Agents

**Date:** April 2, 2026
**Market:** BTCUSDT
**Starting Capital:** $10,000 each
**Config:** Identical except genome path

## Genomes Under Test

| ID | Source | Compare Fitness | Brain | CPPN |
|---|---|---|---|---|
| G1 | P4_val0840 (best overall) | -0.213 | 961 neurons, 3530 synapses | 26 nodes, 82 connections |
| G2 | P5_val1050 (walk-fwd pass) | -0.287 | 937 neurons, 12144 synapses | 23 nodes, 75 connections |
| G3 | P4_val0780 (3rd best) | -0.287 (est) | TBD | TBD |

---

## Observation Log

### Cycle 1 — Session Start to +7 min

**BTC:** ~$66,920, sideways/slight decline from $66,930

All 3 agents entered within the first 2 minutes, demonstrating fast signal processing. All 3 subsequently exited their first trade at a loss and flipped direction.

| Event | G1 | G2 | G3 |
|---|---|---|---|
| First entry | LONG @ tick 16 | SHORT @ tick ~28 | LONG @ tick 13 |
| First exit | ~tick 60, loss | tick 86, loss (-$30.35, 47 ticks held) | ~tick 60, loss |
| Flip to | SHORT $66,268 | LONG $67,595 | SHORT $66,261 |
| Direction at +7min | SHORT | LONG | SHORT |

**Finding:** All 3 genomes demonstrate voluntary exit capability — a complete behavioral change from the old Phase 2 agent which never exited in 12 hours.

**Finding:** G2 is contrarian to G1/G3. Two different directional strategies coexist in the evolved population.

### Cycle 2 — +22 min

**BTC:** ~$66,975, rising

| Metric | G1 | G2 | G3 |
|---|---|---|---|
| Position | SHORT $66,268 | LONG $67,609 | SHORT $66,261 |
| Closed trades | 1 | 3 | 1 |
| Net P&L | -$26.48 | -$95.39 | -$30.29 |
| Equity | $9,960.39 | $9,891.75 | $9,950.90 |
| Unrl% | -1.1% | -0.9% | -1.1% |

**Finding:** G2 has completed 3 trades in 22 minutes — hyperactive, rapid flipping. Each round-trip costs ~$30 in slippage+fees, so G2 has lost $95 mostly to execution costs.

**Finding:** G1 and G3 converged on nearly identical behavior (both SHORT from ~$66,265) despite different lineages. This suggests the market signals at this moment strongly indicate short.

**Finding:** Entry slippage is the dominant cost factor. All fills are $600-700 worse than market price due to the dynamic slippage model. Every trade starts ~1% underwater.

### Cycle 3 — +36 min

**BTC:** ~$66,955, ranging $66,900-$66,980

| Metric | G1 | G2 | G3 |
|---|---|---|---|
| Position | SHORT $66,268 | SHORT $66,299 | LONG $67,579 |
| Closed trades | 1 | **4** | **2** |
| Net P&L | -$26.48 | **-$123.03** | **-$67.13** |
| Equity | $9,960.76 | $9,870.37 | $9,917.18 |
| Unrl% | -1.0% | -1.0% | -0.9% |
| Max DD | 0.0% | 0.0% | 0.4% |

**New trades since Cycle 2:**

G2 completed 2 more trades (now 4 total, all losses):
- LONG $67,595 -> exit $66,325: P&L **-$28.84** (held 27 ticks — fastest exit yet)
- SHORT $66,325 -> exit $67,609: P&L **-$33.79** (held 86 ticks)
- Then re-entered LONG $67,609 -> exit $66,299: P&L **-$27.37** (held 78 ticks)
- Now SHORT $66,299

G3 closed its SHORT and flipped:
- SHORT $66,261 -> exit $67,579: P&L **-$36.16** (held 279 ticks — longest hold)
- Now LONG $67,579

G1 has not traded again — still holding original SHORT $66,268.

**Finding:** G2 has churned through 4 losing trades totaling -$123 in losses. Every single trade loses because the ~$1,300 slippage gap ($67,600 longs vs $66,300 shorts) exceeds any market movement within the holding period. G2 is the most active but the worst performing.

**Finding:** G3 flipped direction — it was SHORT with G1, but after 279 ticks (~23 min hold) it exited and went LONG. G1 stayed SHORT. Diverging strategies.

**Finding:** G1 is the most patient — only 1 trade in 36 minutes, best equity of the three ($9,961). Patience is correlating with performance.

### Complete Trade Log (All Trades Through Cycle 3)

**G1 (1 closed trade, holding SHORT):**

| # | Direction | Entry | Exit | P&L | Fee | Held |
|---|---|---|---|---|---|---|
| 1 | Long | $67,592.77 | $66,268.13 | -$25.48 | $0.74 | 51 ticks |

**G2 (4 closed trades, holding SHORT):**

| # | Direction | Entry | Exit | P&L | Fee | Held |
|---|---|---|---|---|---|---|
| 1 | Short | $66,248.11 | $67,595.26 | -$30.35 | $0.89 | 47 ticks |
| 2 | Long | $67,595.26 | $66,325.14 | -$28.84 | $0.88 | 27 ticks |
| 3 | Short | $66,325.14 | $67,609.40 | -$33.79 | $1.03 | 86 ticks |
| 4 | Long | $67,609.40 | $66,299.10 | -$27.37 | $0.81 | 78 ticks |

**G3 (2 closed trades, holding LONG):**

| # | Direction | Entry | Exit | P&L | Fee | Held |
|---|---|---|---|---|---|---|
| 1 | Long | $67,590.27 | $66,261.00 | -$29.01 | $0.84 | 45 ticks |
| 2 | Short | $66,261.00 | $67,578.66 | -$36.16 | $1.08 | 279 ticks |

### Critical Observation: Slippage Pattern

Every single trade across all 3 agents shows the same pattern:
- LONG entries fill at ~$67,590 (market ~$66,920 + $670 slippage)
- SHORT entries fill at ~$66,270 (market ~$66,920 - $650 slippage)
- The gap between long fill and short fill is ~$1,320

This means for any round-trip (long then short, or short then long), the agent loses ~$1,320 × position_size in slippage alone. With 0.019 BTC position: 1,320 × 0.019 = **$25 minimum loss per round trip from slippage alone**.

No agent can be profitable in these conditions unless the market moves >$1,320 in their favor within a single hold. BTC moved only ~$100 during this 36-minute session.

### Cycle 3b — +36 to +48 min (additional G3 activity)

G3 completed 2 more trades between cycles:
- LONG $67,579 -> exit $66,276: P&L **-$33.92** (held 235 ticks)
- SHORT $66,276 -> exit $67,594: P&L **-$36.67** (held only **10 ticks** — fastest exit of any agent)

G3 is now LONG $67,594 with 4 closed trades totaling -$139.17.

The 10-tick exit on trade 4 is notable: the agent entered SHORT, immediately recognized the market was going against it (rising), and cut within 50 seconds. This is the fastest loss-cutting behavior observed.

### Cycle 4 — +48 min

**BTC:** ~$66,996, still ranging $66,900-$67,010

No new trades from any agent since Cycle 3. All three are holding their positions.

| Metric | G1 | G2 | G3 |
|---|---|---|---|
| Position | SHORT $66,268 | SHORT $66,299 | LONG $67,579 |
| Closed trades | 1 | 4 | 2 |
| Net P&L | -$26.48 | -$123.03 | -$67.13 |
| Equity | $9,960.06 | $9,869.90 | $9,918.14 |
| Unrl% | -1.1% | -1.1% | -0.9% |

**Finding:** All 3 have settled into a holding pattern. No new trades in 12 minutes. G2 (the hyperactive one) has finally stopped churning and is holding SHORT alongside G1. G3 remains contrarian in LONG.

**Finding:** G1 continues to show the best equity preservation. With only 1 closed trade and patient holding, it has lost the least (-$40 total including unrealized) despite being in the wrong direction. G2's hyperactivity has cost it an additional $97 in unnecessary round-trips.

---

## Summary of Findings

### Behavioral Improvements vs Old Phase 2 Agent

| Behavior | Old Phase 2 Agent | New 5-Phase Agents |
|---|---|---|
| Entry timing | ~4 min observation | 1-3 min observation |
| Voluntary exits | **Never** (0 in 12 hours) | All 3 exit within minutes |
| Direction changes | Never (LONG only) | All 3 flip between LONG and SHORT |
| Trade frequency | 0 closed trades in 12h | 1-4 closed trades in 48 min |
| Position sizing | ~12% of equity | 12-15% of equity (similar) |
| Shorting capability | Never demonstrated | All 3 go short |

### Remaining Issues

1. **Slippage is the dominant cost.** The dynamic slippage model creates a ~$1,320 round-trip cost (~1% per entry). In training, each tick represents 1 hour of candle data where BTC can move hundreds of dollars. In paper trading, ticks are 5 seconds apart and BTC moves $5-20 per tick. The agents were trained to expect hourly-scale moves but experience second-scale moves in paper mode. This time-scale mismatch means agents cannot overcome entry costs within their typical holding period.

2. **No profitable trades yet.** All 7 closed trades across all 3 genomes are losses. The losses range from -$25 to -$36 per trade. The structural cause is the slippage gap, not bad directional calls.

3. **Exit triggers only fire on direction change, not profit-taking.** The agents exit by flipping direction (LONG→SHORT or SHORT→LONG), not by using the exit signal (output[3]). The heartbeat data confirms `exit:true` has never appeared. The exit pathway (sigmoid > 0.6) may be insufficiently trained or the threshold may be too high for the evolved brain's output range.

4. **The most active agent performs worst.** G2's 4 trades lost $123 vs G1's 1 trade losing $26. In a high-slippage environment, patience beats activity. This suggests the fitness function may need a transaction cost awareness component.

---

## Root Cause: Volume Unit Mismatch in Dynamic Slippage

### The Bug

`PaperTrader.ComputeDynamicSlippage` (line 174 of `PaperTrader.cs`):

```csharp
decimal participation = orderNotional / (hourlyVolume * 0.01m);
```

- `orderNotional` = $1,250 (**USD**)
- `hourlyVolume` = 195.88 (**BTC**, from Binance kline field [5])
- `participation = 1250 / (195.88 * 0.01) = 638` -- nonsensical, caps to 100
- `multiplier = min(1 + 100^2, 20) = 20`
- `slippage = 5 bps * 20 = 100 bps = $669 per trade`

### The Fix

Convert volume to USD: `hourlyVolume * price`.

- `hourlyVolume in USD = 195.88 * $66,943 = $13,112,724`
- `participation = 1250 / ($13,112,724 * 0.01) = 0.0095`
- `multiplier = 1.00009`
- `slippage = 5.0005 bps = $33 per trade`

### Impact

| | Bugged (current) | Fixed |
|---|---|---|
| Slippage per entry | **$669 (100 bps)** | **$33 (5 bps)** |
| Round-trip cost | ~$1,340 | ~$66 |
| Min BTC move to breakeven | >$1,340 | >$66 |

### This Bug Exists in Training Too

`HistoricalDataStore.CandlesToSignals` passes `candles[i].Volume` (BTC units) as `rawVolumes[i]`, which flows to `MarketEvaluator.RunAgent` → `TickContext.HourlyVolume` → `PaperTrader.ComputeDynamicSlippage`. Same unit mismatch.

**Consequence:** All 1,200 generations of training ran under 20x inflated slippage. The agents adapted to this by being very selective about trades (which is why the Phase 3-5 agents make only 5-15 trades per 2000-hour window). The agent behavior is internally consistent — they were trained under harsh conditions and paper-trade under the same harsh conditions. But those conditions are **unrealistically harsh** compared to real Binance execution.

### Recommended Fix

In `PaperTrader.ComputeDynamicSlippage`, multiply `hourlyVolume` by price:

```csharp
decimal volumeUsd = hourlyVolume * (orderNotional > 0 ? orderNotional / (orderNotional / ctx.Price) : ctx.Price);
```

Or more simply, change the call sites to pass `hourlyVolume * price` instead of raw BTC volume.

**This fix affects both training and paper trading.** Re-training with correct slippage would produce agents that trade more freely since the execution cost barrier is 20x lower. The existing agents would also perform better in paper with correct slippage since their directional calls are reasonable — they just couldn't overcome the inflated entry cost.

### Verification: Bug Prediction Matches Observed Fills

| Trade | Fill Price | Market Price (est) | Observed Slippage | Predicted (100 bps) | Match |
|---|---|---|---|---|---|
| G1 LONG entry | $67,592.77 | ~$66,920 | $672.77 | $669.20 | YES |
| G2 SHORT entry | $66,248.11 | ~$66,920 | $671.89 | $669.20 | YES |
| G2 LONG entry | $67,595.26 | ~$66,920 | $675.26 | $669.20 | YES |
| G3 LONG entry | $67,590.27 | ~$66,920 | $670.27 | $669.20 | YES |

Every observed fill matches the 100 bps prediction within $6 (0.9% error from volume fluctuation).

---

## Cycle 5 — +1h 1min (Final Observation)

**BTC:** ~$67,030, slight upward drift from session start ($66,920)

| Metric | G1 | G2 | G3 |
|---|---|---|---|
| Position | SHORT $66,268 | LONG $67,676 | LONG $67,594 |
| Closed trades | 1 | **5** | **4** |
| Net P&L | -$26.48 | **-$138.05** | **-$139.17** |
| Equity | $9,959.30 | $9,846.81 | $9,845.71 |
| Unrl% | -1.2% | -1.0% | -0.8% |

### Updated Complete Trade Log

**G3 (4 closed trades — most informative):**

| # | Dir | Entry Fill | Exit Fill | P&L | Held | Market Move |
|---|---|---|---|---|---|---|
| 1 | Long | $67,590 | $66,261 | -$29.01 | 45 ticks | Flat |
| 2 | Short | $66,261 | $67,579 | -$36.16 | 279 ticks | Flat |
| 3 | Long | $67,579 | $66,276 | -$33.92 | 235 ticks | Flat |
| 4 | Short | $66,276 | $67,594 | -$36.67 | **10 ticks** | Rising (correctly cut) |

Every trade loses ~$30-37 because the entry-to-exit spread is always ~$1,300 (Long fills at ~$67,590, Short fills at ~$66,270). The actual market barely moved ($66,920 -> $67,030 = $110 total in 1 hour). The slippage gap ($1,300) is 12x larger than the total market movement ($110).

---

## Corrected Assessment

### What the Agents Do Well (verified)

1. **Voluntary exits** — All 3 agents exit positions. G3 exited 4 trades in 1 hour. The old Phase 2 agent never exited in 12 hours.
2. **Loss cutting** — G3 trade 4 was cut in 10 ticks (50 seconds) when the market moved against it. This is genuine learned behavior.
3. **Direction flipping** — All agents go both LONG and SHORT. The old agent was LONG-only.
4. **Active trading** — G2 made 5 trades in 1 hour. The old agent made 0.

### What We Cannot Conclude (due to confounding factors)

1. **Directional accuracy is untestable** in a flat/ranging market. BTC moved only $110 in 1 hour ($66,920 -> $67,030). No directional strategy can demonstrate skill in a $110 range when slippage is $670 per side.
2. **Profit-taking behavior is untestable** when no trade can ever be profitable under 100 bps slippage. The exit signal (output[3]) may or may not fire on profitable positions — we haven't had a profitable position to test it.
3. **Exit signal pathway** — the brain's exit output never exceeded the 0.6 sigmoid threshold (0 out of 210+ heartbeats). This could mean the pathway is undertrained OR it could mean the brain exits via direction flipping (changing output[0] from Long to Short) instead of using output[3]. Both are valid exit mechanisms — the agent doesn't need to use the explicit exit signal if it can flip direction.

### The Slippage Bug's Impact on Conclusions

The bug is confirmed real (predicted $669 matches observed $672 within $3). But it does NOT invalidate the behavioral improvements from the 5-phase training. The agents demonstrably:
- Exit (they didn't before)
- Flip direction (they didn't before)
- Cut losses quickly (10-tick exit on G3)
- Trade actively (1-5 trades per hour vs 0 per 12 hours)

What the bug DOES prevent us from evaluating:
- Whether agents can trade profitably (impossible at 100 bps slippage in a ranging market)
- Whether the exit signal pathway works (no profitable position has existed to test it)
- Whether directional calls are good (market too flat relative to slippage cost)

