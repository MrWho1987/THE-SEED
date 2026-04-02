# Market configuration reference

`MarketConfig` is a sealed record in `Seed.Market` (`src/Seed.Market/MarketConfig.cs`). JSON files are loaded with `MarketConfig.Load(path)` using camelCase property names and `System.Text.Json` (indented output on save).

Serialization uses `JsonIgnoreCondition.WhenWritingDefault`, so properties equal to their default type values may be omitted when saving.

---

## Capital and fees

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `initialCapital` | `decimal` | `10000` | Starting account balance used by trading and fitness evaluation. |
| `makerFee` | `decimal` | `0.0004` | Maker fee rate applied per the execution model (fraction of notional, e.g. `0.0004` = 4 bps). |
| `takerFee` | `decimal` | `0.0006` | Taker fee rate (fraction of notional). |
| `slippageBps` | `decimal` | `5` | Slippage expressed in basis points (bps). |

---

## Symbols

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `symbols` | `string[]` | `["BTCUSDT", "ETHUSDT"]` | Tradable symbols. The backtest and several modes use `config.Symbols[0]` as the primary series for data loading. |

---

## Risk management

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `maxPositionPct` | `decimal` | `0.25` | Maximum position notional as a fraction of equity for a single position (see `RiskManager.CheckTrade` / `ComputePositionSize`). |
| `maxDailyLossPct` | `decimal` | `0.05` | Blocks new trades when daily P&amp;L falls below `-(initialBalance * maxDailyLossPct)`. |
| `killSwitchDrawdownPct` | `decimal` | `0.15` | Drawdown from peak equity (`(MaxEquity - equity) / MaxEquity`) that triggers the kill switch; further trading is blocked (`RiskManager`, `PaperTrader`). |
| `maxConcurrentPositions` | `int` | `3` | Maximum number of simultaneous open positions. |
| `maxDailyVaRPct` | `decimal` | `0.05` | Parametric VaR(95%) threshold (fraction). Used by `RiskManager.ComputeVaRScale` to scale position size down when estimated VaR exceeds this level. |
| `maxEquityMultiplier` | `decimal` | `100` | Caps effective equity used for position sizing at `initialBalance * maxEquityMultiplier` so runaway paper equity does not explode size (`ComputePositionSize`). |

---

## Evolution

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `populationSize` | `int` | `50` | Number of genomes per generation. |
| `generations` | `int` | `100` | Maximum generation count in backtest (may stop earlier due to walk-forward or data bounds). |
| `trainingWindowHours` | `int` | `720` | Hours of history loaded; training segment length is derived with validation (`BacktestRunner` / `RunBacktest`). |
| `validationWindowHours` | `int` | `168` | Length of the held-out validation slice after training within the loaded window. |
| `evalWindowHours` | `int` | `500` | Cap on evaluation window length (hours) for each training evaluation (`evalWindow = Min(evalWindowHours, trainLen)`). |
| `evalWindowCount` | `int` | `3` | Number of diverse evaluation windows when `k > 1` (`RegimeDetector.SelectDiverseWindows`). If `1`, a single rolling/walk-forward window is used. |
| `rollingStepHours` | `int` | `24` | Hours to advance walk-forward offset when validation passes or after stall handling. |
| `runSeed` | `ulong` | `42` | Base seed for reproducibility (brain development, regime/window selection, etc., per call sites). |

---

## Fitness (core metric weights)

These five weights are the coefficients in `MarketFitness.ComputeDetailed` for the primary risk/return terms:

`fullFitness = adjustedSharpe*wSharpe + adjustedSortino*wSortino + logReturn*wReturn - maxDdDuration*wDrawdownDuration - cvarPenalty*wCVaR`

The runtime does **not** validate that these five sum to `1.0`. The stock defaults **do** sum to `1.0` (`0.45 + 0.15 + 0.20 + 0.10 + 0.10`). Choosing them to sum to `1.0` keeps the combination interpretable as a weighted mix of similarly scaled terms; asymmetric experiments (for example zero Sharpe/Sortino) are valid and used in repo configs.

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `shrinkageK` | `float` | `10` | Shrinkage multiplier: `confidence = 1 - shrinkageK / (shrinkageK + tradeCount)` (equivalently `tradeCount / (shrinkageK + tradeCount)`) scales Sharpe/Sortino before clamping. |
| `fitnessSharpeWeight` | `float` | `0.45` | Weight on shrinkage-adjusted Sharpe. |
| `fitnessSortinoWeight` | `float` | `0.15` | Weight on shrinkage-adjusted Sortino. |
| `fitnessReturnWeight` | `float` | `0.20` | Weight on `log(1+|return|) * sign(return)` of total return. |
| `fitnessDrawdownDurationWeight` | `float` | `0.10` | Weight on **penalty** term: subtracts `maxDdDuration * weight`. |
| `fitnessCVaRWeight` | `float` | `0.10` | Weight on **penalty** term: subtracts `cvarPenalty * weight` where `cvarPenalty = -CVaR5` when CVaR5 &lt; 0. |

### Fitness (activity, floors, and clamps)

Not part of the five-term sum; applied after the core `fullFitness` path.

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `inactivityPenalty` | `float` | `-0.1` | Fitness when there are no trades; also used in blending for low trade counts and as a floor when `returnPct &lt; returnFloor`. |
| `minTradesForActive` | `int` | `3` | Below this trade count, fitness blends toward `inactivityPenalty`. Also used to scale the ratio clamp (`effectiveClamp = ratioClampMax * min(1, tradeCount / (minTradesForActive * 3))`) and cap the activity bonus. |
| `ratioClampMax` | `float` | `10` | Maximum clamp bound for adjusted Sharpe and Sortino. Effective clamp scales with trade count: agents need `3 * minTradesForActive` trades to access full range. Prevents low-trade agents from exploiting extreme ratio values. |
| `returnFloor` | `float` | `-0.50` | If total return is below this fraction, fitness is forced to at most `inactivityPenalty`. |
| `activityBonusScale` | `float` | `0` | If &gt; 0, adds `min(log(1 + tradeCount), log(1 + minTradesForActive * 3)) * activityBonusScale` to fitness. Capped at the bonus a `3 * minTradesForActive` trade agent would receive to prevent churn exploitation. |

### Multi-window consistency (evolution)

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `windowConsistencyWeight` | `float` | `0` | When `evalWindowCount &gt; 1`, per-genome fitness is averaged across windows, then **penalized** by this weight times the standard deviation of fitness across windows (`MarketEvolution.AverageBreakdowns`). `0` disables the penalty. |

---

## Species diversity

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `targetSpeciesMin` | `int` | `10` | If species count is below this, compatibility threshold decreases (`CompatibilityAdjustRate`). |
| `targetSpeciesMax` | `int` | `50` | If species count is above this, compatibility threshold increases. |
| `compatibilityAdjustRate` | `float` | `0.1` | Step size for threshold adjustment toward target species band. |
| `minOffspringPerSpecies` | `int` | `1` | Minimum offspring allocated per species (reproduction pipeline). |
| `minSpeciesSizeForElitism` | `int` | `2` | Minimum species size required for elitism behavior in speciation. |
| `stagnationLimit` | `int` | `25` | Generations without improvement before a species is considered stagnant. |
| `diversityBonusScale` | `float` | `0.02` | Scales KNN-based diversity bonus applied after evaluation (`MarketEvolution.ApplyDiversityBonus`). |
| `diversityKNeighbors` | `int` | `5` | `k` for KNN diversity. |

---

## Brain development

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `maxBrainNodes` | `int` | `200` | Upper bound on developed brain nodes (genome/brain pipeline). |
| `maxBrainEdges` | `int` | `2000` | Upper bound on developed brain edges. |

---

## Data feeds

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `spotPollMs` | `int` | `5000` | Declared poll interval (ms) for spot-style data. |
| `futuresPollMs` | `int` | `15000` | Declared poll interval (ms) for futures. |
| `sentimentPollMs` | `int` | `300000` | Declared poll interval (ms) for sentiment. |
| `onChainPollMs` | `int` | `3600000` | Declared poll interval (ms) for on-chain. |
| `macroPollMs` | `int` | `3600000` | Declared poll interval (ms) for macro. |

These values are **declared on `MarketConfig` but are not consumed by `DataAggregator`**; individual feed implementations use their own timing. `RunPaper` uses `spotPollMs` for `Task.Delay` between ticks and when price is invalid, not as the aggregator’s internal schedule.

---

## API keys

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `coinGeckoApiKey` | `string` | `null` | Optional CoinGecko API key for feeds that require it. |

---

## Execution mode

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `mode` | `ExecutionMode` (string enum) | `Backtest` | Selects the `Program` switch branch (see below). |
| `confirmLive` | `bool` | `false` | Must be `true` to pass the live-trading safety gate (live mode still exits early if not implemented). |

### `ExecutionMode` values (`Program.cs`)

| Value | Behavior |
|-------|----------|
| `Backtest` | Downloads history, runs `MarketEvolution` with checkpoints, validation, walk-forward logic, saves best genome to `ResolvedGenomePath` (and `best_training_genome.json`). |
| `Paper` | Loads genome from `ResolvedGenomePath`, compiles brain, runs `PaperTrader` with live `DataAggregator` and `TradeLogger` until Ctrl+C. |
| `Live` | If `confirmLive` is false, prints safety message and returns. If true, prints that live trading is not yet implemented. |
| `Compare` | Evaluates evolved genome vs baselines (buy-hold, SMA, random, mean reversion) across rolling windows; statistical comparison. |
| `Ablation` | Turns off brain components one at a time (`AblationConfig`) and reports fitness delta vs full model. |
| `StressTest` | Scales `makerFee`, `takerFee`, and `slippageBps` by 1x, 2x, 3x, 5x and reports fitness/return/trades. |
| `MonteCarlo` | Runs agent on historical window, then bootstrap confidence intervals on per-trade P&amp;L (10,000 resamples). |
| `Ensemble` | Prints that ensemble mode requires checkpoints with species IDs and suggests paper mode. |
| `NeuroAblation` | Varies `LearningParams` (reward/pain/curiosity) and reports fitness deltas vs baseline. |

---

## Output

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `outputDirectory` | `string` | `"output_market"` | Root directory for checkpoints, genomes, logs, and experiment trackers. |

---

## Validation and walk-forward

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `validationIntervalGens` | `int` | `10` | Run validation on held-out data every N generations (when `gen &gt; 0` and `gen % N == 0`). |
| `earlyStopPatience` | `int` | `5` | Consecutive validation declines used with training trend to detect overfit; stops only if `earlyStopEnabled` is true. |
| `earlyStopEnabled` | `bool` | `false` | When true, enables early exit on overfit signal. |
| `walkForwardEnabled` | `bool` | `true` | When true, advances `walkForwardOffset` when validation fitness meets `walkForwardMinValFitness`. |
| `walkForwardMinValFitness` | `float` | `-0.05` | Minimum validation fitness to count as a walk-forward pass. |
| `walkForwardMaxStallGens` | `int` | `50` | After this many failed validations, walk-forward forces an advance. |
| `windowStabilityGens` | `int` | `1` | When `evalWindowCount &gt; 1`, `windowEpoch = windowStabilityGens &gt; 1 ? gen / windowStabilityGens : gen` selects diverse windows (stable window sets for several generations when &gt; 1). |

---

## Paper trading and logging

| Property (JSON) | Type | Default | Description |
|-----------------|------|---------|-------------|
| `genomePath` | `string` | `null` | Optional explicit path to the trained genome JSON. |
| `tradeLogPath` | `string` | `null` | Optional explicit path for `trades.jsonl`. |
| `displayIntervalMs` | `int` | `10000` | Minimum interval between console status lines in paper mode. |
| `checkpointIntervalGens` | `int` | `10` | In backtest, save checkpoint and print detail every N generations (when `(gen + 1) % N == 0`). |

---

## Computed properties (not serialized inputs)

These are C# only; they are not required in JSON for loading (deserialization typically ignores unknown or readonly computed members depending on options). `Save` may emit them if present on the object graph.

| Property | Definition |
|----------|------------|
| `ResolvedGenomePath` | `GenomePath ?? Path.Combine(OutputDirectory, "best_market_genome.json")` |
| `ResolvedTradeLogPath` | `TradeLogPath ?? Path.Combine(OutputDirectory, "trades.jsonl")` |

---

## Pre-built JSON config files

| File | Purpose (from contents and naming) |
|------|-------------------------------------|
| `market-config.default.json` | Baseline **backtest**: larger population (`100`), long run (`1000` generations), default fitness weights, two symbols, standard windows; writes under `output_market`. Includes serialized resolved paths pointing at `output_market`. |
| `market-config.validation.json` | **Smoke / quick validation**: small population and generations (`20`), short training/validation windows, fast rolling step; output `output_market_validation`. |
| `market-config.paper.json` | **Paper trading**: `mode` `Paper`, single symbol, `maxEquityMultiplier` `50`, genome `genomes/phase2_best.json`, output `output_paper`, placeholder `coinGeckoApiKey`. |
| `market-config.phase1-bootstrap.json` | **Phase 1 backtest**: long horizons (`trainingWindowHours` 65000, `validationWindowHours` 8760), fitness emphasizes return and CVaR with Sharpe/Sortino at `0`, `walkForwardMinValFitness` `0`, `windowStabilityGens` `10`, checkpoint every `25` gens; `output_phase1`. |
| `market-config.phase2-quality.json` | **Phase 2 backtest**: same broad horizons as phase 1 with balanced fitness weights including `windowConsistencyWeight` and `activityBonusScale`; `output_phase2`. |
| `market-config.deep.json` | **Deep backtest** (initial variant): population `150`, `1000` gens, long windows, `evalWindowHours` `2000`, `runSeed` `7`, `output_deep`. |
| `market-config.deep-v2.json` | **Deep v2**: `1500` gens, `evalWindowHours` `4000`, `evalWindowCount` `1`, `rollingStepHours` `336`, `runSeed` `13`, `output_deep_v2`. |
| `market-config.deep-v3.json` | **Deep v3**: `1500` gens, custom fitness split (0.35/0.15/0.15/0.20/0.15), `evalWindowCount` `3`, `walkForwardMinValFitness` `0`, `runSeed` `37`, `output_deep_v3`. |
| `market-config.deep-v4.json` | **Deep v4**: `2000` gens, `maxEquityMultiplier` `50`, same fitness split as v3, walk-forward and species tuning, `runSeed` `41`, `output_deep_v4`. |
| `market-config.deep-v5.json` | **Deep v5**: `5000` gens, same fitness split as v3–v4, adds `windowConsistencyWeight` `0.3`, `activityBonusScale` `0.03`, `walkForwardMaxStallGens` `30`, `runSeed` `42`, `output_deep_v5`. |
| `market-config.exp1-stable-windows.json` | **Experiment**: `200` gens, deep-v5-style fitness and diversity settings, **`windowStabilityGens` `10`** for stable multi-window evaluation, `output_exp1`. |
| `market-config.exp2-stagnation.json` | **Experiment**: Same parameters as `market-config.exp1-stable-windows.json` except **`outputDirectory`** (`output_exp2` vs `output_exp1`); filename suggests stagnation-focused runs. |
| `market-config.exp3-low-dd.json` | **Experiment**: Like exp1/exp2 but **`fitnessDrawdownDurationWeight` `0.02`** (lower drawdown-duration penalty weight). `output_exp3`. |
| `market-config.exp4-short-windows.json` | **Experiment**: Like exp3 but **`evalWindowHours` `500`** (shorter eval windows). `output_exp4`. |
| `market-config.phase1-v2.json` | **Phase 1 v2 — Trade Activation**: return-heavy (60%), no Sharpe/Sortino, `minTradesForActive` 3, `activityBonusScale` 0.06, `shrinkageK` 1.0, single eval window 2000h; `output_phase1_v2`. |
| `market-config.phase2-v2.json` | **Phase 2 v2 — Directional Quality**: return 55%, still no Sharpe/Sortino, `minTradesForActive` 5, `activityBonusScale` 0.05, `shrinkageK` 2.0, single window 3000h; `output_phase2_v2`. |
| `market-config.phase3-v2.json` | **Phase 3 v2 — Exit Mastery**: Sharpe 0.05 introduced, return 50%, `ddDurationWeight` 0.20 (strongest), `minTradesForActive` 6, 2 eval windows 5000h; `output_phase3_v2`. |
| `market-config.phase4-v2.json` | **Phase 4 v2 — Risk-Adjusted Returns**: Sharpe 0.15 + Sortino 0.05, return 40%, `minTradesForActive` 8, 3 eval windows 6000h, `shrinkageK` 4.0; `output_phase4_v2`. |
| `market-config.phase5-v2.json` | **Phase 5 v2 — Regime Adaptation**: Sharpe 0.20 + Sortino 0.10, return 35%, `minTradesForActive` 10, `shrinkageK` 5.0, `walkForwardMinValFitness` -0.03 (strictest); `output_phase5_v2`. |

---

## Static helper

| Member | Description |
|--------|-------------|
| `MarketConfig.Default` | `new MarketConfig()` with all property defaults. |
| `MarketConfig.Load(string path)` | Deserialize from JSON file. |
| `Save(string path)` | Serialize to JSON file (indented, camelCase). |
