# Trading pipeline

This document describes the market trading path implemented in `Seed.Market`: `MarketAgent` (brain bridge), `ActionInterpreter`, `PaperTrader`, `RiskManager`, `KellyPositionSizer`, `RollingMetrics`, and `LiveTrader`. Formulas and control flow match the current C# sources.

---

## 1. MarketAgent bridge

**Location:** `src/Seed.Market/Agents/MarketAgent.cs`

### Inputs

- **Vector length:** `MarketAgent.InputCount` equals `SignalIndex.Count` (**92**). A `float[92]` is filled from `SignalSnapshot.Signals` via `Array.Copy` for that length.
- **Agent-state overwrite (`InjectAgentState`):** indices **76–79** (`CurrentPnl`, `PositionDirection`, `HoldingDuration`, `CurrentDrawdown`) are always set from portfolio/tick state (see `SignalIndex` in `src/Seed.Market/Signals/SignalIndex.cs`). The snapshot may populate those slots first; they are replaced each tick.

**`InjectAgentState` rules:**

- If there is at least one open position (uses `OpenPositions[0]`):
  - **CurrentPnl (76):** `pnlPct = UnrealizedPnlPct(currentPrice) / 100` (float), then `Clamp(pnlPct, -1, 1)`.
  - **PositionDirection (77):** `+1` if Long, `-1` if Short, `0` if Flat.
  - **HoldingDuration (78):** `holdingHours = ElapsedHours - _elapsedHoursAtEntry`, then `min(holdingHours / 100, 1)`.
- If flat (no open positions): those three are set to `0`.
- **CurrentDrawdown (79):** if `MaxEquity > 0`, `drawdown = (MaxEquity - Equity(currentPrice)) / MaxEquity`, else `0`; then `Clamp(drawdown, 0, 1)`.

### Brain step and outputs

- `BrainRuntime.Step(signals, BrainStepContext)` returns the brain outputs for this tick.
- **Output count:** `ActionInterpreter.OutputCount` = **5** (`MarketAgent.OutputCount`).

### One-tick delay (signal execution)

Execution uses **`_pendingSignal`**, not the freshly interpreted signal:

1. `currentSignal = ActionInterpreter.Interpret(outputs)`; stored in `_lastGeneratedSignal`.
2. `signalToExecute = _pendingSignal ?? new TradingSignal(Flat, 0, 0, false)`.
3. `_pendingSignal = currentSignal`.
4. `_trader.ProcessSignal(signalToExecute, ...)` runs.

So the signal applied to the trader is **always one tick behind** the latest brain output. On the **first** tick, `_pendingSignal` is `null`, so execution is **Flat** with zero size/urgency and no exit flag.

### Reward, pain, curiosity (learning modulators)

After the trade step, `BrainRuntime.Learn` receives a length-3 span: `[reward, pain, curiosity]` (`ModulatorIndex`: Reward = 0, Pain = 1, Curiosity = 2).

**Reward (`ComputeReward`):** starts at `0`, then:

1. **Closed trade:** if `TradeHistory.Count > _lastTradeCount`, take the last closed trade `last`, add  
   `Clamp((float)(last.Pnl / InitialBalance) * 50, -1, 1)`, and set `_lastTradeCount` to the new count.
2. **Open position:** if any open position, let `currentPnlPct = UnrealizedPnlPct(currentPrice) / 100` (float), `delta = currentPnlPct - _lastUnrealizedPnl`, add `Clamp(delta * 30, -0.5, 0.5)`, set `_lastUnrealizedPnl = currentPnlPct`.
3. **No open position:** `equityDelta = Clamp((float)((Equity(currentPrice) - _prevEquity) / InitialBalance) * 5, -0.1, 0.1)`, add to reward, set `_lastUnrealizedPnl = 0`.

Finally `_prevEquity = Equity(currentPrice)` and return `reward`.

**Pain (`ComputePain`):** if no open position, `0`. Else `pnlPct = UnrealizedPnlPct(currentPrice) / 100`; if `pnlPct < 0`, return `Clamp(-pnlPct, 0, 1)`, else `0`.

**Curiosity (`ComputeCuriosity`):** if `_ablation.CuriosityEnabled` is false, the caller passes `0` and learning skips curiosity. Otherwise: if `_lastPrice <= 0`, return `0`. Else `predicted = Tanh(outputs[4])` when `outputs.Length > 4`, else `0`; `actual = Sign((float)(currentPrice - _lastPrice))`; return `Abs(predicted - actual)`.

**How the brain uses modulators:** In `BrainRuntime.Learn`, if learning is enabled,  
`M = AlphaReward * reward + AlphaPain * pain + AlphaCuriosity * curiosity` (see `Seed.Core` learn config alphas). That scalar drives eligibility-based weight updates together with `Eta`, `CriticalPeriodHours` (optional `etaScale`), edge plasticity, etc. Details: `src/Seed.Brain/BrainRuntime.cs`.

---

## 2. ActionInterpreter: five outputs to `TradingSignal`

**Location:** `src/Seed.Market/Trading/ActionInterpreter.cs`

**Constants:** `OutputCount = 5`, `ExitThreshold = 0.6f`, `DirectionDeadzone = 0.15f`.

The XML comment on the class still refers to “float[4]” output; **`OutputCount` is 5** and index **4** is not consumed here (it is reserved for price-direction curiosity in `MarketAgent`).

For each output, `Safe(x)` replaces NaN/Infinity with `0`, else returns `x`. `Sigmoid(x) = 1 / (1 + Exp(-x))`.

| Index | Use |
|-------|-----|
| **[0] Direction** | `rawDir = Safe(outputs[0])`. If `rawDir > 0.15` → Long; else if `rawDir < -0.15` → Short; else Flat. **No `tanh` in code** (only `Safe`). |
| **[1] Size** | `Sigmoid(Safe(outputs[1]))`, then clamped to `[0, 1]` via `Max(0, Min(1, ...))`. |
| **[2] Urgency** | Same as size: sigmoid then `[0, 1]`. |
| **[3] Exit** | `Sigmoid(Safe(outputs[3]))`; **`ExitCurrent = (rawExit > 0.6)`**. |
| **[4] Price prediction** | Not read by `Interpret`. Used in `MarketAgent.ComputeCuriosity` as `Tanh(outputs[4])` vs. price move sign. |

Result: `new TradingSignal(direction, sizePct, urgency, exit)`.

---

## 3. PaperTrader mechanics

**Location:** `src/Seed.Market/Trading/PaperTrader.cs`

### Daily reset

If `ctx.ElapsedHours - _lastResetHour >= 24`, then `RiskManager.ResetDaily(portfolio)` (clears `DailyPnl`, updates `LastResetDay`) and `_lastResetHour = ctx.ElapsedHours`.

### Watermark and kill switch (execution-time)

- `RiskManager.UpdateWatermark` updates `MaxEquity` and rolling `MaxDrawdown`.
- If kill switch is not already triggered and there are open positions:  
  `drawdown = (MaxEquity - Equity(ctx.Price)) / MaxEquity` (when `MaxEquity > 0`). If `drawdown > MarketConfig.KillSwitchDrawdownPct`, sets `KillSwitchTriggered`, closes **all** positions with `ClosePosition`, returns `"Kill switch: positions closed"`.

### Funding

`ApplyFundingRates` runs if `ctx.FundingRate != 0`:

- `prevFundingSlot = (int)(_lastFundingHour / 8f)`, `currFundingSlot = (int)(ctx.ElapsedHours / 8f)`.
- If `currFundingSlot <= prevFundingSlot`, return (no payment this tick).
- Else set `_lastFundingHour = ctx.ElapsedHours` and for each open position:  
  `fundingCost = Size * EntryPrice * (decimal)ctx.FundingRate`.  
  **Long:** `Balance -= fundingCost`; `DailyPnl -= fundingCost`.  
  **Short:** `Balance += fundingCost`; `DailyPnl -= (-fundingCost)` i.e. `DailyPnl += fundingCost`.

So longs pay when funding is positive; shorts receive (balance increases).

### Signal handling order (exit-first, flip, no same-side add)

1. If `signal.ExitCurrent` and there is at least one open position → `ClosePosition` on `OpenPositions[0]` only, then return.
2. If direction is Flat → no trade (empty result).
3. **Flip:** find first position with `Direction != signal.Direction` and not Flat; if found, `ClosePosition` that position; if close fails, return that result.
4. **No same-side add:** if any open position matches `signal.Direction`, return without opening.
5. `RiskManager.CheckTrade`; if not allowed, return with reason.
6. `OpenPosition`.

### Fees

- **Open:** `feeRate = Urgency > 0.5 ? TakerFee : MakerFee`; `fee = fillPrice * size * feeRate`; subtracted from `Balance` and `DailyPnl`.
- **Close:** `feeRate = TakerFee` always; same fee formula on exit fill.

### Dynamic slippage

`ComputeDynamicSlippage(orderNotional, hourlyVolume)`:

- If `hourlyVolume <= 0`, return `SlippageBps` (config base).
- Else `participation = orderNotional / (hourlyVolume * 0.01)`; if `participation > 100`, set `participation = 100`.
- `multiplier = Min(1 + participation^2, 20)`.
- Return `SlippageBps * multiplier` (basis points; not further divided).

Fill prices: slippage amount = `Price * dynamicSlippageBps / 10000`. Long entry: price + slippage; short entry: price − slippage. Long exit: price − slippage; short exit: price + slippage.

---

## 4. RiskManager

**Location:** `src/Seed.Market/Trading/RiskManager.cs`

### `CheckTrade` (gates before open)

Order of checks:

1. Kill switch already triggered → deny.
2. `DailyPnl < -(InitialBalance * MaxDailyLossPct)` → deny.
3. Drawdown vs peak: if `MaxEquity > 0` and `(MaxEquity - Equity) / MaxEquity > KillSwitchDrawdownPct` → set kill switch, deny.
4. Exit-only / flat signals are allowed through relevant branches.
5. If not exiting and direction not flat: `OpenPositions.Count >= MaxConcurrentPositions` → deny.
6. `maxSize = Equity * MaxPositionPct`, `requestedSize = maxSize * signal.SizePct`; if `requestedSize > maxSize` → deny.

### `ComputePositionSize`

- `equity = Equity(currentPrice)`.
- Cap: `maxEquityForSizing = InitialBalance * MaxEquityMultiplier`; if `equity > maxEquityForSizing`, use `maxEquityForSizing`.
- `maxNotional = equity * MaxPositionPct`.
- `requested = maxNotional * signal.SizePct * ComputeVaRScale(portfolio)`.
- Return `Min(requested, maxNotional)`.

### VaR scaling (`ComputeVaRScale`)

- Requires `EquityCurve.Count >= 24`; uses the **last 24** equity points (indices `Count - 24` .. `Count - 1`).
- For each consecutive pair with `prev > 0`, `r = (curve[i] - prev) / prev`; accumulate `sumR`, `sumR2`, count `n`.
- If `n < 2` or non-positive variance, return `1`.
- `mean = sumR / n`, `variance = sumR2 / n - mean^2`; `std = Sqrt(variance)`.
- `var95 = -(mean - 1.645 * std)` (Gaussian tail; see code).
- If `var95 <= 0` or `var95 <= MaxDailyVaRPct`, return `1`.
- Else return `Clamp(MaxDailyVaRPct / var95, 0.1, 1)`.

### `ResetDaily` / `UpdateWatermark`

- `ResetDaily`: `DailyPnl = 0`, `LastResetDay = UtcNow`.
- `UpdateWatermark`: raises `MaxEquity` if current equity is higher; updates `MaxDrawdown` from peak to current equity.

---

## 5. KellyPositionSizer (not wired into live/paper path)

**Location:** `src/Seed.Market/Trading/KellyPositionSizer.cs`

- `ComputeHalfKelly(recentTrades, minPct = 0.01, maxPct = 0.25)`:
  - If `recentTrades.Count < 5`, return `minPct`.
  - `winRate = (count of Pnl > 0) / Count`.
  - `avgWin` = average of positive `Pnl`; `avgLoss` = average of `Abs(Pnl)` for `Pnl <= 0`, default `1` if empty; if `avgLoss <= 0`, set `avgLoss = 1`.
  - `winLossRatio = avgWin / avgLoss`.
  - `kellyFraction = winRate - (1 - winRate) / winLossRatio`.
  - `halfKelly = Max(0, kellyFraction / 2)`.
  - Return `Clamp(halfKelly, minPct, maxPct)`.

**Usage:** referenced from tests only (`Seed.Market.Tests`); **not** called from `PaperTrader` or `RiskManager`.

---

## 6. RollingMetrics

**Location:** `src/Seed.Market/Trading/RollingMetrics.cs`

- Fixed window (default **100**) of equity values in a queue.
- **RollingSharpe:** from window equities, compute simple returns `r_i = (e_i - e_{i-1}) / e_{i-1}` (skip non-positive prior). With `mean` and `variance` of those returns, if `variance <= 0` return `0`; else `mean / Sqrt(variance) * Sqrt(8760)` (8760 ≈ hours per year).
- **RollingDrawdown:** track running peak over the window; `maxDd = max over points of `(peak - eq) / peak` when `peak > 0`.

---

## 7. LiveTrader

**Location:** `src/Seed.Market/Trading/LiveTrader.cs`

- Constructor requires `MarketConfig.ConfirmLive == true`; otherwise throws `InvalidOperationException` with the safety message in code.
- `CreatePortfolio` and `CloseAllPositions` delegate to an internal `PaperTrader` instance (`_fallback`).
- `ProcessSignal` logs `[LIVE] Would execute: ...` to the console and **delegates execution to `_fallback.ProcessSignal(signal, portfolio, currentPrice, currentTick)`** (no `TickContext` overload; hourly volume and funding default to `0` in that `TickContext` constructor path used inside `PaperTrader` overloads).

---

## Related types

- **`TradingSignal`, `TickContext`, `PortfolioState`, `Position`:** `src/Seed.Market/Trading/TradingTypes.cs`.
- **Signal layout:** `src/Seed.Market/Signals/SignalIndex.cs` (`Count = 92`, agent slots **76–79**).
