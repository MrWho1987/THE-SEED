# Running The Seed

This document describes how to run the market evolution engine from the CLI, what each execution mode does (exactly as implemented), how the Windows dashboard relates to the CLI, where artifacts are written, and how to use the standalone API verification tool.

---

## Prerequisites

- **CLI (`Seed.Market.App`)**: .NET 8.0 (`net8.0`), any OS the SDK supports.
- **Dashboard (`Seed.Dashboard`)**: .NET 8.0 **Windows** (`net8.0-windows`), WPF — not supported on Linux/macOS as shipped.
- **API verifier (`Seed.Backtest`)**: .NET 9.0 (`net9.0`).

From the repository root (where `Seed.sln` lives), paths below are relative to that root.

---

## CLI: `Seed.Market.App`

### Command line

The app reads a single JSON config file. Resolution order in `Program.cs`:

1. If `args.Length >= 2` and `args[0] == "--config"`, the config path is `args[1]`.
2. Else if `args.Length >= 1` and the first argument does **not** start with `-`, the config path is `args[0]`.
3. Otherwise the default file is `market-config.default.json` (current working directory).

Examples:

```bash
dotnet run --project src/Seed.Market.App -- --config market-config.phase1-bootstrap.json
```

```bash
dotnet run --project src/Seed.Market.App -- market-config.paper.json
```

If the config file is missing, the app prints an error and exits without loading settings.

After load, the app ensures `config.OutputDirectory` exists, prints a banner (config file name, `mode`, capital, population), and dispatches on `config.Mode`.

### Config fields that control execution

`MarketConfig` is deserialized with camelCase JSON. Important members:

| Area | Properties |
|------|------------|
| Mode | `mode` — one of `Backtest`, `Paper`, `Live`, `Compare`, `Ablation`, `StressTest`, `MonteCarlo`, `Ensemble`, `NeuroAblation` |
| Paths | `outputDirectory` (default `output_market`), optional `genomePath`, optional `tradeLogPath` |
| Resolved paths | `genomePath` → `ResolvedGenomePath`; if omitted, `{outputDirectory}/best_market_genome.json`. `tradeLogPath` → `ResolvedTradeLogPath`; if omitted, `{outputDirectory}/trades.jsonl` |
| Live safety | `confirmLive` (default `false`) — required for live mode to pass the gate |

---

## Execution modes (exact flow)

### Backtest

1. **Data load**  
   - `end = UtcNow - 1 hour`, `start = end - (TrainingWindowHours + ValidationWindowHours)`.  
   - `BacktestRunner.LoadData(config.Symbols[0], start, end, enrich: true)` fetches candles via `HistoricalDataStore` (cache under `{outputDirectory}/data_cache`) and, when `enrich: true`, runs `HistoricalSignalEnricher` for supplemental series (macro, on-chain, sentiment, etc.; CoinGecko key from `coinGeckoApiKey` when needed).  
   - Returns parallel arrays: `SignalSnapshot[]`, prices, raw volumes, raw funding rates.

2. **Train / validation split**  
   - `trainLen = min(TrainingWindowHours, snapshots.Length - ValidationWindowHours)`.  
   - Training slice: `[0 .. trainLen)`; validation slice: `[trainLen .. snapshots.Length)`.

3. **Observatory and evolution**  
   - `FileObservatory` is constructed with `Path.Combine(config.OutputDirectory, "events.jsonl")` as its base path; it writes `events.jsonl` **inside** that path (so the on-disk layout is `{outputDirectory}/events.jsonl/events.jsonl`).  
   - `MarketEvolution` runs with that observatory.

4. **Checkpoint resume**  
   - Directory: `{outputDirectory}/checkpoints`.  
   - `CheckpointState.FindLatest` picks the lexicographically greatest `checkpoint_*.json`.  
   - If found: population, generation, innovation IDs, compatibility threshold, species/archive state, `WalkForwardOffset`, and `StallCount` are restored; `startGen` and counters come from the checkpoint.  
   - Else: `evolution.Initialize()`.

5. **Per-generation evaluation (training window)**  
   - `evalWindow = min(EvalWindowHours, trainLen)`.  
   - Loop: `gen` from `startGen` to `Generations - 1`. If `walkForwardOffset >= trainLen`, the loop **breaks** (walk-forward consumed the train region).  
   - Remaining train segment starts at `walkForwardOffset`.  
   - **If `EvalWindowCount <= 1`**: single rolling window. `maxOff = max(1, remainingLen - wfEvalWindow)`, `offset = WalkForwardEnabled ? 0 : (gen * RollingStepHours) % maxOff`, window slices from that offset. If the window has fewer than 50 bars, the code falls back to the full remaining walk-forward segment.  
   - **If `EvalWindowCount > 1`**: `RegimeDetector.SelectDiverseWindows` builds `k = max(1, EvalWindowCount)` windows; if `WindowStabilityGens > 1`, window selection uses `gen / WindowStabilityGens` as an epoch index. `evolution.RunGeneration` is called with a **list** of windows (multi-window evaluation).  
   - Validation on the **holdout** slice runs on generations where `gen > 0`, `gen % ValidationIntervalGens == 0`, and `valSnapshots.Length >= 50`: best genome is evaluated with `MarketEvaluator.EvaluateSingle` on the full validation arrays.

6. **Early stop (optional)**  
   - If validation fitness fails to beat the best validation fitness, a decline counter increments. If it reaches `EarlyStopPatience` and training fitness is still improving vs `EarlyStopPatience` validations ago, an overfitting message is printed; if `EarlyStopEnabled`, the generation loop **breaks**.

7. **Walk-forward (training region only)**  
   - When `WalkForwardEnabled` and a validation generation runs: if validation fitness `>= WalkForwardMinValFitness`, `walkForwardOffset` increases by `RollingStepHours` (capped); else `stallCount` increments. If `stallCount >= WalkForwardMaxStallGens`, offset is force-advanced and stall reset.

8. **Checkpointing**  
   - When `(gen + 1) % CheckpointIntervalGens == 0`, saves `checkpoint_{gen+1:0000}.json`.  
   - When training best fitness improves all-time, writes `checkpoints/best_gen_{gen:0000}.json`.  
   - On validation improvements, writes `checkpoints/best_val_gen_{gen:0000}.json`.

9. **End of run**  
   - Full-population validation: `BacktestRunner.Evaluate` on validation data.  
   - If at least two species champions exist, `MarketEvaluator.EvaluateEnsemble` on validation.  
   - **Deployment genome**: copies `best_val_gen_*.json` from the checkpoint with the best validation run to `ResolvedGenomePath` if that file exists; otherwise writes the current training-best genome to `ResolvedGenomePath`.  
   - Always writes `best_training_genome.json` in `outputDirectory` when a best genome exists.  
   - `observatory.Flush()`.

---

### Paper

1. Requires `File.Exists(ResolvedGenomePath)`; otherwise prints instructions and returns.  
2. Loads `SeedGenome` JSON, builds `BrainDeveloper` / `BrainRuntime` with substrate dimensions from the genome and `MarketEvaluator.MarketBrainBudget`.  
3. `PaperTrader`, `MarketAgent`, `DataAggregator` (live feeds), `TradeLogger(ResolvedTradeLogPath)`.  
4. `Console.CancelKeyPress` sets cancel on the token (does not terminate the process abruptly).  
5. Loop until cancel: `aggregator.TickAsync`, build `TickContext`, `agent.ProcessTick`, equity and rolling metrics; new trades appended via `TradeLogger`.  
6. Console table every `DisplayIntervalMs`; heartbeat every **60 seconds** — one JSON object per line appended to `{outputDirectory}/heartbeat.jsonl` (`ts`, `equity`, `trades`, `positions`).  
7. Stale feed warning if `UtcNow - aggregator.LastTickTime > 5` minutes.  
8. On exit: `PaperTrader.CloseAllPositions`, session summary printed.

---

### Live

1. If `confirmLive` is not `true`, prints the safety message and returns.  
2. If `confirmLive` is `true`, prints that live trading is not implemented and suggests paper mode.

---

### Compare

1. `ExperimentTracker` writes `{outputDirectory}/experiments/{experimentId}.json` (12-char hex id) on dispose with mode `"compare"`.  
2. Loads data: `end = UtcNow - 1h`, `start = end - TrainingWindowHours` (no separate validation hours in this load).  
3. Requires genome at `ResolvedGenomePath`.  
4. `windowSize = min(EvalWindowHours, snapshots.Length / 4)`, `numWindows = min(20, snapshots.Length / windowSize)`.  
5. For each window `w`: slices `[w * windowSize .. (w+1) * windowSize)`; evolved agent fitness via `EvaluateSingle`; baselines `BuyAndHold`, `SmaCrossover`, `RandomAgent` (seed `w`), `MeanReversion`.  
6. Prints mean fitness per strategy and `StatisticalTests.PairedTTest(evolvedFitnesses, baselineFitnesses)` — **p-value and Cohen’s d** vs evolved per baseline row.

---

### Ablation

1. Tracker mode `"ablation"`.  
2. Data: `start = end - ValidationWindowHours` (same end as other modes).  
3. Genome required at `ResolvedGenomePath`.  
4. `EvalWithAblation`: compile graph, `BrainRuntime(..., abl)`, simulate full `snapshots` length, close positions, `MarketFitness.ComputeDetailed` fitness.  
5. Baseline: `AblationConfig.Default`. Variants: `No Learning`, `No Curiosity`, `No Homeostasis`, `No Modulatory`, `No Delays`, `No Recurrence` (each property toggled on `AblationConfig.Default`).  
6. Prints fitness, delta vs baseline, and label `HELPS` / `HURTS` / `neutral` (threshold `0.01` on delta).

---

### StressTest

1. Tracker mode `"stress-test"`.  
2. Data: `start = end - TrainingWindowHours`.  
3. For each multiplier in `{1, 2, 3, 5}`: clones config with `MakerFee`, `TakerFee`, `SlippageBps` multiplied; `MarketEvaluator.EvaluateSingle` on full loaded series.  
4. Records metrics `stress_1x`, `stress_2x`, etc.

---

### MonteCarlo

1. Tracker mode `"monte-carlo"`.  
2. Data: `start = end - TrainingWindowHours`.  
3. `EvaluateSingle` for summary; then a second pass builds trade list: full tick replay, `tradePnls` from `ClosedTrade.Pnl`.  
4. `StatisticalTests.BootstrapReturn(tradePnls, 10000, seed: 42)` — reports P5, median, P95, width.

---

### Ensemble

Prints: `Ensemble mode requires checkpoint with species IDs. Use paper mode for now.` No file output from this branch.

---

### NeuroAblation

1. Tracker mode `"neuro-ablation"`.  
2. Data: `start = end - ValidationWindowHours` (same window length choice as Ablation).  
3. For each row, `BrainRuntime` uses varied `LearningParams` derived from `genome.Learn`: all-active baseline, `AlphaReward = 0`, `AlphaPain = 0`, `AlphaCuriosity = 0`, reward and pain zeroed (curiosity only), pain and curiosity zeroed (reward only).  
4. First row’s fitness becomes the numeric baseline; the table shows delta vs that baseline (first row delta displayed as `—`).  
5. Prints interpretation lines from `Program.cs` (largest negative delta = most important channel, etc.).

---

## Dashboard (WPF)

### Launch

```bash
dotnet run --project src/Seed.Dashboard
```

Startup object: `Seed.Dashboard.App` (`App.xaml` → `MainWindow.xaml`). Requires Windows.

### Theme and shell

- **MaterialDesignInXaml**: `BundledTheme` with `BaseTheme="Dark"`, `PrimaryColor="Green"`, `SecondaryColor="Pink"`, merged with `MaterialDesign3.Defaults` and `Themes/SeedDarkTheme.xaml`.

### Navigation (left rail)

| Order | Page | Shortcut |
|-------|------|----------|
| 0 | Dashboard (Home) | Ctrl+1 |
| 1 | Training | Ctrl+2 |
| 2 | Paper trading | Ctrl+3 |
| 3 | Genome lab | Ctrl+4 |
| 4 | Analysis | Ctrl+5 |
| 5 | Settings | Ctrl+6 |

### Services (high level)

| Service | Role |
|---------|------|
| `ConfigService` | Load/save `MarketConfig`, discover `market-config*.json` |
| `PathResolver` | Repo root (walk up for `Seed.sln`), resolve paths, discover existing `outputDirectory` values from configs |
| `TrainingService` | Background training loop; **not** identical to CLI backtest (see below) |
| `PaperTradingService` | Genome load, brain compile, `DataAggregator`, ticks, trade log, UI callbacks |
| `GenomeService` | Lists genome/checkpoint JSONs for lab and analysis |
| `AnalysisService` | Builds temp config, builds `Seed.Market.App` into `build_analysis/`, runs `dotnet path/to/Seed.Market.App.dll path/to/temp.json`, picks newest experiment JSON from `{outputDirectory}/experiments` |
| `NotificationService` / `SessionManager` | UI notifications and session state |

### TrainingService vs CLI backtest

`TrainingService` **simplifies** evaluation to a **single** window per generation: it always calls `evolution.RunGeneration(evalSnaps, evalPrices, evalRawVols, evalRawFund)` with one slice — no `EvalWindowCount > 1` branch, no `RegimeDetector.SelectDiverseWindows`. Validation uses only the **first** `min(EvalWindowHours, valPrices.Length)` hours of the validation slice, not necessarily the full validation set. It does **not** implement CLI-only behaviors such as early-stop-overfitting, the final `BacktestRunner.Evaluate` over the population, or ensemble validation. On completion it writes `best_training_genome.json` and **`ResolvedGenomePath` from the final training-best genome** (not the CLI’s validation-best copy logic). It may write `genome_scores.json` with best training/validation summary.

Use the **CLI backtest** for full fidelity and for deployment genomes chosen by validation when that path is used.

---

## Output artifacts

| Artifact | Where / when |
|----------|----------------|
| Checkpoints | `{outputDirectory}/checkpoints/checkpoint_{NNNN}.json` — JSON: `generation`, `bestFitness`, `timestamp`, `genomeJsons`, `speciesIds`, innovation IDs, `compatibilityThreshold`, `walkForwardOffset`, `stallCount`, `speciesState`, `nextSpeciesId`, `archiveState` (`CheckpointState`). |
| Best genomes (checkpoints dir) | `best_gen_{NNNN}.json`, `best_val_gen_{NNNN}.json` — `SeedGenome` JSON. |
| Deployed genome | `ResolvedGenomePath` (default `{outputDirectory}/best_market_genome.json`) after backtest. |
| Training best (backtest) | `{outputDirectory}/best_training_genome.json`. |
| Events | `{outputDirectory}/events.jsonl/events.jsonl` with current CLI wiring (see Backtest section). |
| Heartbeat (paper) | `{outputDirectory}/heartbeat.jsonl` — JSON lines ~60s apart. |
| Trades (paper) | `ResolvedTradeLogPath` (default `{outputDirectory}/trades.jsonl`) — JSONL including `session_start` and per-trade lines (`TradeLogger`). |
| Experiments | `{outputDirectory}/experiments/{experimentId}.json` — from `ExperimentTracker` (compare, ablation, stress-test, monte-carlo, neuro-ablation): `experimentId`, `mode`, timestamps, subset of config, recorded metrics. |
| Generation summary (optional API) | `FileObservatory.WriteGenerationSummary` would write `gen_{NNNN}_summary.json` under the observatory output directory; the current evolution path does not call this method — the type exists for future or manual use. |
| Dashboard training | `genome_scores.json` in output directory when training finishes from the dashboard. |

---

## How-to guides

### Training from scratch (Phase 1–style bootstrap)

1. Copy or use `market-config.phase1-bootstrap.json` (large `trainingWindowHours`, `evalWindowCount: 1`, return-heavy fitness, etc.).  
2. Set `coinGeckoApiKey` if enrichment needs it.  
3. Run:

   `dotnet run --project src/Seed.Market.App -- market-config.phase1-bootstrap.json`

4. Artifacts go under `output_phase1` (or your `outputDirectory`): checkpoints, `best_market_genome.json` when complete, `best_training_genome.json`.

### Continuing training (Phase 2–style, seeding from checkpoint)

1. Point `outputDirectory` at a folder where you want the new run (e.g. `output_phase2`).  
2. Copy the latest `checkpoint_*.json` files (and optionally prior `best_*.json`) from the previous run’s `checkpoints` into `{output_phase2}/checkpoints/`. `FindLatest` resumes from the lexicographically latest `checkpoint_*.json`.  
3. Use a config such as `market-config.phase2-quality.json` (multi-window eval, richer fitness, longer `evalWindowHours`, etc.).  
4. Run the CLI with that config. Generations continue from the restored generation until `generations` is reached or walk-forward / early stop ends the loop.

### Paper trading a trained genome

1. Ensure a genome JSON exists (e.g. from backtest `best_market_genome.json` or `genomes/phase2_best.json`).  
2. Set `mode` to `Paper`, `genomePath` to that file, `outputDirectory` for logs/heartbeat. Example: `market-config.paper.json`.  
3. Run:

   `dotnet run --project src/Seed.Market.App -- market-config.paper.json`

4. Stop with **Ctrl+C**; positions are closed in code after the loop exits.

### Running analysis modes

**CLI:** set `mode` to `Compare`, `Ablation`, `StressTest`, `MonteCarlo`, or `NeuroAblation`, ensure `genomePath` / `outputDirectory` / window-related fields match your intent, then run `Seed.Market.App` with that config.

**Dashboard:** Analysis view lets you pick a genome and a mode (`Compare`, `Ablation`, `StressTest`, `MonteCarlo`, `NeuroAblation`); it spawns the compiled `Seed.Market.App` DLL with a temporary JSON config.

### Designing a new experiment config

1. Start from `market-config.default.json` or an existing `market-config.*.json`.  
2. Set `mode`, `outputDirectory`, and `runSeed` for reproducibility.  
3. Tune `trainingWindowHours`, `validationWindowHours`, `evalWindowHours`, `evalWindowCount`, `rollingStepHours`, `walkForwardEnabled`, `walkForwardMinValFitness`, `checkpointIntervalGens`, `validationIntervalGens`, `earlyStopEnabled` / `earlyStopPatience` to match the experiment.  
4. For analysis-only modes, ensure `genomePath` resolves to a trained genome and that the loaded history length is sufficient (compare uses `TrainingWindowHours`; ablation/neuro use `ValidationWindowHours` for the span).  
5. Save as a new `market-config.<name>.json` and pass it as the CLI argument.

---

## `tools/Seed.Backtest` — API verifier

This is a **standalone console program** targeting **.NET 9.0**. Its entry point is:

```csharp
await ApiVerifier.Run();
```

It does **not** load `MarketConfig`, train genomes, or run backtests. It performs HTTP (and RSS) checks against external APIs documented in `ApiVerifier.cs` (Binance, Alternative.me Fear & Greed, CoinGecko, Binance futures, Blockchain.com, RSS, Reddit, Yahoo Finance, etc.), prints pass/fail, and ends with a short architecture note about `DataAggregator`.

Run from repo root:

```bash
dotnet run --project tools/Seed.Backtest
```

Use it to verify connectivity and data sources before relying on live aggregation — not as a substitute for `Seed.Market.App` training or evaluation.
