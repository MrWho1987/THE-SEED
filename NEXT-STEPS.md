# THE SEED -- Next Steps & Roadmap

> The world's first fully autonomous neuroevolutionary crypto trading agent.
> This document captures planned enhancements, architectural improvements, and
> strategic recommendations. Nothing here changes current behavior -- it is a
> living reference so ideas don't get lost between sessions.

**Last updated:** 2026-04-15
**Current baseline:** V11 (still v1 development, no v2 yet)
**Brain architecture:** 110 inputs, 11 outputs, 1200 neurons (20x20x3 hidden grid),
TopKIn 32, MaxOut 40, MaxSynapticDelay 16, ModuleCount 12

## V11 Status — What's Done vs Pending

Most pre-V11 roadmap items below have been folded into the V11 comprehensive foundation
fix. Use this status map alongside the original section text:

- **Section 1 Leverage:** DONE in V12/V13 — log-scale leverage via `output[5]`, MaxLeverage=125
- **Section 2 Brain Size Expansion:** DONE in V11 Tier 4 — 20×20×3 1200-neuron substrate
- **Section 4 Stop-Loss Evolution:** DONE in V11 Tier 3 — trailing stop, brain-controlled
  SL override, take-profit targets all added
- **Section 5 Additional Signals:** PARTIAL in V11 Tier 2 — Deribit options, futures
  premium historical, expanded regime; still pending: order book WebSocket (live-only),
  whale alerts, cross-exchange arb
- **Section 8.3 Fitness Enhancements:** DONE in V11 Tier 1 — Sortino, Calmar,
  Information Ratio (vs HODL), Diversification bonus, FeeDrag penalty
- **Section 3 Code Cleanup:** NOT DONE — `Program.cs`, `HistoricalSignalEnricher.cs`,
  `MarketEvolution.cs` still monolithic. Lower priority than retrain.
- **Section 7 Production Deployment:** NOT STARTED — paper trading flow exists but
  live execution layer not built.

## Immediate V11 Priorities

1. **Launch V11 retrain** when user approves (~25 day estimate, 3-phase pipeline).
2. **Monitor training** every 12h, watch for signs that the 11-output action space
   is being exercised (non-zero std on partial close / trail / TP / SL slots).
3. **Paper trading dry run** (J1-J3 protocol) after retrain: 24h at 1x leverage,
   then 24h at 125x, compare metrics vs training validation.

---

## Table of Contents

1. [Leverage Support](#1-leverage-support)
2. [Brain Size Expansion](#2-brain-size-expansion)
3. [Code Cleanup & Refactoring](#3-code-cleanup--refactoring)
4. [Stop-Loss Evolution](#4-stop-loss-evolution)
5. [Additional Input Signals](#5-additional-input-signals)
6. [Multi-Asset & Portfolio Trading](#6-multi-asset--portfolio-trading)
7. [Production Deployment](#7-production-deployment)
8. [Training Pipeline Improvements](#8-training-pipeline-improvements)
9. [Performance & Scalability](#9-performance--scalability)
10. [Observability & Analytics](#10-observability--analytics)

---

## 1. Leverage Support

**Priority:** High (after v4 validation)
**Effort:** Medium
**Requires retraining:** Partial (fine-tune from v4 genomes)

### Current State
- Agents trade with 1x (no leverage). Position sizing is capped at `MaxPositionPct`
  (25%) of equity per position, max 3 concurrent positions.
- The brain already outputs `SizePct` (0-1) as a confidence signal, which naturally
  maps to leverage scaling.

### Proposed Implementation

**Option A -- Execution-layer leverage (no retraining):**
- Add `MaxLeverage` to `MarketConfig` (e.g., 3.0).
- Multiply position notional by leverage at the execution layer:
  `notional = equity * MaxPositionPct * SizePct * MaxLeverage`.
- Brain remains unaware. Stop-loss and kill-switch provide safety rails.
- Best for conservative leverage (2-3x) as a quick experiment.

**Option B -- Leverage-aware training (recommended for production):**
- Same config change, but run the `MarketEvaluator` with leveraged P&L so the
  fitness function reflects amplified gains and losses.
- Seed the starting population with best v4 genomes (fine-tuning, not fresh start).
- The brain learns that a 1% price move causes a 3% portfolio impact and calibrates
  its risk accordingly.
- Estimated training time: 30-50% of a full run, since directional intelligence
  transfers from v4 genomes.

**Option C -- Leverage as a 6th brain output:**
- Add a new output neuron where the brain explicitly chooses leverage per trade
  (e.g., 1x-5x).
- Requires full retrain since the genome-to-phenotype mapping changes (new output
  node).
- Most powerful but highest cost. Consider only after Option B validates the concept.

### Risk Considerations
- A 2% stop-loss on a 3x leveraged position = 6% equity loss per trade.
- Kill-switch at 15% drawdown becomes much easier to trigger with leverage.
- May need to tighten `StopLossPct` to 1% or adjust `KillSwitchDrawdownPct`
  proportionally to leverage.

---

## 2. Brain Size Expansion

**Priority:** Medium (after v4 baseline established)
**Effort:** Low (config change + retraining)
**Requires retraining:** Yes (but can seed from existing genomes)

### Current Architecture (V11e)
```
Input layer:  110 neurons  (one per signal)
Hidden grid:  20 x 20 x 3 layers = 1200 neurons
Output layer: 11 neurons   (direction, size, urgency, exit, predict, leverage,
                            partialClose, trailEnable, trailDist, tpOffset, slOverride)
Total:        1321 neurons
```

Each hidden neuron receives up to 32 incoming connections (TopKIn) and sends to
up to 40 (MaxOut). MaxSynapticDelay = 16 ticks (4 hours at 15-min bars).
12 gate neurons modulate 12 signal categories via regime context signals.

### Historical Expansion Path (pre-V11 → V11)

| Phase  | Hidden Grid  | Total Neurons | Status |
|--------|-------------|---------------|--------|
| v4     | 16x16x3     | 865           | Archived baseline |
| **V11e** | **20x20x3** | **1321** | **Current (in training)** |
| Future | Evolved     | Variable      | Let evolution decide |

### Why Not Go Big Immediately?
- More neurons = more connections to evolve = more generations needed.
- A well-wired 865-neuron brain beats a poorly-wired 5,000-neuron brain.
- Training time scales roughly with neuron count. 5,125 neurons = ~6x slower per
  generation than 865.
- The genome already carries `SubstrateWidth`, `SubstrateHeight`, `SubstrateLayers`
  which can mutate. Evolution can discover the right size if we widen the mutation
  bounds.

### What Larger Brains Enable
- Deeper cross-signal reasoning (e.g., macro + derivatives + on-chain combined).
- More complex conditional strategies.
- Better regime-switching (different trading styles for different market conditions).
- Longer temporal memory through more hidden layers.

### Implementation
- Change `MarketBrainBudget` in `MarketEvaluator.cs` (single line).
- Optionally increase `TopKIn` and `MaxOut` proportionally.
- Consider increasing `MaxSynapticDelay` from 3 to 5 for deeper temporal patterns.
- Seed initial population from best v4 genomes to transfer learned knowledge.

---

## 3. Code Cleanup & Refactoring

**Priority:** High (improves maintainability for all future work)
**Effort:** Medium-High
**Requires retraining:** No

### Problem
Several files have grown large and contain multiple unrelated responsibilities,
making them harder to navigate, test, and modify.

### Largest Files (current line counts)

| File                            | Lines | Concern                                       |
|---------------------------------|-------|-----------------------------------------------|
| `Program.cs`                    | 894   | 10+ execution modes in one file               |
| `HistoricalSignalEnricher.cs`   | 875   | 7+ data sources, alignment, forward-fill      |
| `MarketEvolution.cs`            | 544   | Evolution loop, speciation, checkpointing     |
| `SeedGenome.cs`                 | 478   | Genome structure, crossover, mutation, I/O     |
| `AnalysisViewModel.cs`          | 372   | Dashboard analysis UI logic                   |
| `BrainRuntime.cs`               | 360   | Brain step, plasticity, diagnostics           |
| `SettingsViewModel.cs`          | 325   | Dashboard settings UI logic                   |
| `DataAggregator.cs`             | 316   | Live feed orchestration, signal computation   |

### Recommended Refactoring

#### 3.1 Program.cs (894 lines -> ~8 files)
Break the monolithic CLI into a command-per-file structure:

```
src/Seed.Market.App/
  Program.cs              (~80 lines)  -- entry point, arg parsing, dispatch
  Commands/
    BacktestCommand.cs    (~300 lines) -- RunBacktest
    PaperCommand.cs       (~250 lines) -- RunPaper
    LiveCommand.cs        (~20 lines)  -- RunLive
    CompareCommand.cs     (~80 lines)  -- RunCompare
    AblationCommand.cs    (~80 lines)  -- RunAblation
    StressTestCommand.cs  (~50 lines)  -- RunStressTest
    MonteCarloCommand.cs  (~70 lines)  -- RunMonteCarlo
    NeuroAblationCommand.cs (~80 lines)-- RunNeuroAblation
    PipelineCommand.cs    (~70 lines)  -- RunPipeline
```

Each command class is a static method or a simple class with a `RunAsync` method.
`Program.cs` becomes a thin dispatcher.

#### 3.2 HistoricalSignalEnricher.cs (875 lines -> ~5 files)
Split by data source:

```
src/Seed.Market/Backtest/Enrichment/
  EnricherBase.cs             -- shared HTTP/caching/alignment helpers
  EthereumEnricher.cs         -- ETH candles, ETH funding
  DerivativesEnricher.cs      -- OI, long/short, taker ratio, liquidations
  OnChainEnricher.cs          -- hash rate, addresses, exchange flow
  SentimentEnricher.cs        -- Fear & Greed, stablecoin flows
  MacroEnricher.cs            -- S&P 500, VIX, DXY, Gold, Treasury
  HistoricalSignalEnricher.cs -- orchestrator that calls all enrichers
```

#### 3.3 MarketEvolution.cs (544 lines -> ~3 files)
Separate concerns:

```
src/Seed.Market/Evolution/
  MarketEvolution.cs        -- main evolution loop (generation management)
  SpeciationManager.cs      -- species creation, compatibility, niching
  CheckpointManager.cs      -- save/load checkpoint state, heartbeat logging
```

#### 3.4 DataAggregator.cs (316 lines -> ~2-3 files)
Split live data orchestration from signal computation:

```
src/Seed.Market/Data/
  DataAggregator.cs          -- feed management, tick orchestration
  LiveSignalComputer.cs      -- candle building, indicator computation
  LiveFeedManager.cs         -- feed health monitoring, reconnection
```

### Behavioral Guardrails
- Every refactoring must be extract-only: move code to new files, update
  references, verify all 358 tests still pass.
- No logic changes during refactoring. Only structural moves.
- Commit each file split independently so any regression is easy to bisect.

---

## 4. Stop-Loss Evolution

**Priority:** Medium
**Effort:** Medium
**Requires retraining:** Yes (for brain-aware stop-loss)

### Current State
- Hard 2% stop-loss at the execution layer, between brain decisions.
- Brain is unaware of the stop-loss. It never sees positions get force-closed.

### Future Enhancements

#### 4.1 Trailing Stop-Loss
Instead of a fixed 2% from entry, implement a trailing stop that follows price:
- Once a position is up 1%, move the stop to breakeven.
- As profit increases, the stop trails at `max_price - ATR * multiplier`.
- Lets winners run while protecting gains.

#### 4.2 ATR-Based Dynamic Stop
Use `Atr14` (signal index 65) to set stop distance relative to current volatility:
- High volatility = wider stop (avoid getting stopped out by noise).
- Low volatility = tighter stop (protect against breakout moves).
- More market-aware than a fixed percentage.

#### 4.3 Brain-Aware Stop-Loss (advanced)
Train the brain WITH the stop-loss in the evaluator so agents learn:
- To avoid entries near support/resistance where stops would trigger.
- To set position sizes that account for stop-loss risk.
- To work WITH the stop-loss rather than getting surprised by it.

Requires adding the stop-loss check inside `MarketEvaluator.RunAgent` and
potentially giving the brain a signal for "distance to stop" as input.

---

## 5. Additional Input Signals

**Priority:** Medium
**Effort:** Medium per signal group
**Requires retraining:** Yes

### Candidates for Signal Expansion (92 -> 110+)

| Signal Group           | Count | Source                    | Why                                            |
|------------------------|-------|---------------------------|------------------------------------------------|
| Order Book Depth       | 4-6   | Binance WebSocket         | Bid/ask walls, absorption, spoofing detection  |
| Liquidation Heatmap    | 3-4   | Binance/Coinglass         | Where leveraged traders will get wiped out     |
| Options Data           | 4-5   | Deribit API               | Put/call ratio, max pain, implied volatility   |
| Whale Alerts           | 2-3   | Whale Alert API           | Large transfers signaling imminent moves       |
| Cross-Exchange Arb     | 2-3   | Multiple exchange APIs    | Price divergence between venues                |
| DeFi Metrics           | 3-4   | DefiLlama API             | TVL changes, yield movements, protocol health  |
| Network Fees           | 2     | Blockchain APIs           | Fee spikes indicate congestion/demand          |

### Implementation Notes
- Extend `SignalIndex.Count` from 92 to new count.
- Add corresponding data fetching in `HistoricalSignalEnricher` and
  `DataAggregator`.
- CPPN handles variable input counts naturally -- existing genome connections to
  indices 0-91 remain valid, new inputs start with random connectivity.
- Can fine-tune from existing genomes; no full retrain needed.

---

## 6. Multi-Asset & Portfolio Trading

**Priority:** Low-Medium (after single-asset performance is strong)
**Effort:** High
**Requires retraining:** Yes

### Current State
- Agents trade a single symbol (BTCUSDT).
- Config supports a `Symbols` array but only the first is used.

### Proposed Architecture

#### 6.1 Multi-Symbol per Agent
- Each agent sees signals for N symbols simultaneously.
- Input vector grows to 92 * N (e.g., 184 for BTC + ETH).
- Outputs become N * 5 (direction/size/urgency/exit/prediction per symbol).
- Brain learns cross-asset correlations and pair trading strategies.

#### 6.2 Specialist Agents with Portfolio Orchestrator
- Train separate specialists per asset (BTC agent, ETH agent, SOL agent).
- A meta-agent or rule-based allocator distributes capital across specialists.
- Simpler to train, easier to add new assets incrementally.

#### 6.3 Sector Rotation
- Train agents that allocate between crypto, cash, and potentially inverse
  positions across multiple assets based on regime detection.

### Recommendation
Start with 6.2 (specialists + allocator). It's the most practical path: train a
strong BTC agent first, then clone the pipeline for ETH with minimal changes.
Portfolio-level risk management (correlation-aware sizing, sector limits) is an
orchestration layer on top.

---

## 7. Production Deployment

**Priority:** High (after paper trading validates performance)
**Effort:** High
**Requires retraining:** No

### Current State
- `RunLive` mode exists in Program.cs but is a placeholder.
- No real exchange connectivity for order execution.

### Required Infrastructure

#### 7.1 Exchange Connectivity
- Binance Futures API (order placement, position management, account queries).
- WebSocket for real-time fills, position updates, and balance changes.
- Order types: market, limit, stop-market (for the stop-loss layer).
- Authentication and API key management (encrypted storage).

#### 7.2 Position Reconciliation
- Sync local state with exchange state on startup and periodically.
- Handle partial fills, order rejections, and network failures.
- Dead-man's switch: if the agent process crashes, existing positions must be
  protected (exchange-side stop-loss orders).

#### 7.3 Fault Tolerance
- Automatic reconnection on WebSocket drops.
- Graceful degradation: if enrichment data sources fail, the brain should still
  receive the last known values (already handled by the enrichment system).
- Heartbeat monitoring: if no decision is made for 2x the expected interval,
  alert the operator.

#### 7.4 Capital Safety
- Start with a small allocation ($500-$1,000) for initial live validation.
- Daily loss limit at the exchange level (not just in-process).
- Human override: kill switch accessible via Dashboard or API.
- Separate API key with position-close-only permissions for emergency shutdown.

#### 7.5 Regulatory & Exchange Compliance
- Rate limiting on API calls (Binance enforces strict limits).
- Audit trail: log every order, fill, and decision with timestamps.
- Tax reporting: track cost basis for all trades.

---

## 8. Training Pipeline Improvements

**Priority:** Medium
**Effort:** Medium
**Requires retraining:** Indirectly (enables better training)

### 8.1 Curriculum Learning
- Phase 1-2 currently use progressively larger data windows.
- Future: add market-regime-specific training phases:
  - Phase A: Bull markets only (learn to ride trends).
  - Phase B: Bear markets only (learn to short or stay flat).
  - Phase C: Sideways/choppy markets (learn patience).
  - Phase D: Black swan events (flash crashes, de-pegs).
  - Phase E: Full data (generalize across all regimes).

### 8.2 Ensemble Evolution
- Currently we pick the single best genome.
- Train an ensemble of 3-5 specialists and combine their votes.
- Already partially supported (`EnsembleTrader` exists).
- Key: evolve diverse agents, not just the single highest fitness.

### 8.3 Fitness Function Enhancements
- Current: profit-weighted with Sharpe ratio, drawdown penalty, and trade quality.
- Add explicit risk-adjusted metrics:
  - Sortino ratio (penalize downside volatility only).
  - Calmar ratio (annualized return / max drawdown).
  - Win rate consistency across sub-windows.
  - Recovery time from drawdowns.
- Penalize excessive correlation with buy-and-hold (the agent should add alpha,
  not just follow the market).

### 8.4 Distributed Training
- Currently single-machine, multi-threaded.
- For larger brains or longer training runs, distribute across multiple machines:
  - Each machine evaluates a subset of the population.
  - Central coordinator manages selection and reproduction.
  - Could use Azure or AWS spot instances for cost-effective scaling.

---

## 9. Performance & Scalability

**Priority:** Low-Medium
**Effort:** Medium
**Requires retraining:** No

### 9.1 Brain Runtime Optimization
- Current `BrainRuntime.Step` uses managed C# arrays.
- SIMD vectorization (System.Numerics.Vector) for weight*activation products.
- GPU acceleration via ONNX Runtime for very large brains.
- Profiling shows brain step is fast at current size (~0.1ms), but matters at
  5,000+ neurons.

### 9.2 Data Pipeline Optimization
- Cache enriched SignalSnapshots (not just raw candles) to avoid recomputing
  indicators every training run.
- Incremental data updates: only download new candles since last run, append to
  cache files.

### 9.3 Memory Optimization
- Large populations (256 agents) each compile a full BrainGraph.
- Share read-only graph structure, only duplicate mutable state (activations,
  weights).
- Reduces memory pressure for larger populations.

---

## 10. Observability & Analytics

**Priority:** Medium
**Effort:** Medium
**Requires retraining:** No

### 10.1 Training Analytics Dashboard
- Real-time visualization of fitness progression across generations.
- Species diversity tracking (are we converging too early?).
- Brain complexity metrics over time (node count, edge count, modularity).
- The Dashboard app already exists with WPF -- extend it with training charts.

### 10.2 Trade Analysis Tools
- Post-hoc analysis of paper trading decisions:
  - What signals were most active when the brain opened a position?
  - Which signals correlate most with profitable vs. losing trades?
  - Feature importance via ablation (already supported by RunAblation mode).
- Confusion matrix: does the brain correctly predict direction?

### 10.3 Live Monitoring
- When deployed to production, a monitoring layer that tracks:
  - Real-time P&L and equity curve.
  - Brain output distribution (is it becoming passive? is it overtrading?).
  - Signal feed health (are all 92 inputs receiving fresh data?).
  - Latency metrics (time from signal to order placement).
  - Alerts for anomalies (sudden behavior changes, data feed failures).

### 10.4 Genome Archaeology
- Track the evolutionary lineage of the best-performing genomes.
- Visualize which mutations and crossovers led to performance breakthroughs.
- Identify "evolutionary dead ends" to improve mutation operators.

---

## Priority Ordering (Recommended Sequence)

```
NOW (v4):  Training in progress with 15m candles + stop-loss
           |
           v
NEXT:      Validate v4 results (paper trading)
           |
           +--> Code Cleanup (Section 3) -- parallel with validation
           |    Makes all future work easier
           |
           v
THEN:      Leverage Support (Section 1, Option B)
           Fine-tune v4 genomes with leveraged P&L
           |
           v
THEN:      Brain Size Expansion (Section 2, v5 = 24x24x4)
           Combined with leverage if timing works
           |
           v
THEN:      Stop-Loss Evolution (Section 4)
           Trailing stop + ATR-based dynamic stop
           |
           v
THEN:      Additional Signals (Section 5)
           Order book depth + options data first
           |
           v
THEN:      Production Deployment (Section 7)
           Only after consistent paper trading profits
           |
           v
FUTURE:    Multi-Asset (Section 6)
           Training improvements (Section 8)
           Performance optimization (Section 9)
           Advanced analytics (Section 10)
```

---

## Design Principles (Carry Forward)

1. **No assumptions** -- verify every change against actual code behavior.
2. **Behavioral guardrails** -- document what changes and what doesn't for
   every modification.
3. **Train, don't hardcode** -- let the brain discover strategies rather than
   programming them.
4. **Execution layer for safety, brain for intelligence** -- keep protective
   mechanisms (stop-loss, kill-switch) outside the brain so it can't learn to
   circumvent them.
5. **Fine-tune, don't restart** -- leverage existing trained genomes as starting
   populations for incremental improvements.
6. **Test at every step** -- all 358 existing tests must pass after any change.
   Add new tests for new features before implementing them.
