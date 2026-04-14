# Configuration Reference

## MarketConfig (sealed record)

All behavior is controlled via JSON config files loaded as `MarketConfig`. Key parameter groups:

### Execution Mode
- `mode`: `Backtest` | `Paper` | `Live` | `Compare` | `Ablation` | `StressTest` | `MonteCarlo` | `Ensemble` | `NeuroAblation`
- `symbols`: Array of trading pairs (currently only first is used: `["BTCUSDT"]`)
- `candleInterval`: Candle period string (e.g., `"15m"`, `"1h"`, `"4h"`)
- `runSeed`: Deterministic RNG seed (default: 42)

### Capital & Fees
- `initialCapital`: Starting balance (default: 10000)
- `makerFeePct`: Maker fee (default: 0.0004)
- `takerFeePct`: Taker fee (default: 0.0006)
- `slippageBps`: Base slippage in basis points (default: 5)

### Risk Management
- `maxPositionPct`: Max position as fraction of equity (default: 0.25)
- `maxConcurrentPositions`: Simultaneous positions (default: 3)
- `killSwitchDrawdownPct`: Hard stop drawdown from watermark (default: 0.15)
- `maxDailyLossPct`: Daily loss limit (default: 0.05)
- `stopLossPct`: Per-trade stop-loss (default: 0.02)
- `varThreshold`: VaR(95%) threshold for position scaling (default: 0.05)

### Evolution
- `populationSize`: Agents per generation (default: 50)
- `generations`: Total generations (default: 100)
- `trainingWindowHours`: Historical data window for training (default: 720)
- `validationWindowHours`: Hold-out validation window (default: 168)
- `evalWindowHours`: Per-evaluation sub-window (default: 500)
- `evalWindowCount`: Diverse regime windows (default: 3)
- `rollingStepHours`: Walk-forward advance step (default: 24)

### Fitness Weights (must sum to 1.0)
- `fitnessSharpeWeight`: Sharpe ratio weight (default: 0.45)
- `fitnessSortinoWeight`: Sortino ratio weight (default: 0.15)
- `fitnessReturnWeight`: Log return weight (default: 0.20)
- `fitnessDrawdownDurationWeight`: Drawdown duration penalty (default: 0.10)
- `fitnessCVaRWeight`: CVaR 5% penalty (default: 0.10)
- `shrinkageK`: Bayesian shrinkage parameter (default: 10)
- `inactivityPenalty`: Fitness for 0-trade genomes (default: -0.10)
- `activityBonus`: Bonus for active genomes (default: 0.0)
- `minimumActiveTrades`: Threshold for "active" status (default: 3)

### Speciation
- `targetSpeciesMin` / `targetSpeciesMax`: Target species range (default: 10-50)
- `compatibilityThreshold`: Starting NEAT distance threshold (default: 3.5)
- `compatibilityStep`: Threshold adjustment per generation (default: 0.1)
- `stagnationLimit`: Generations before reseeding (default: 25)

### Checkpointing & Validation
- `checkpointIntervalGens`: Save every N generations (default: 25)
- `validationIntervalGens`: Validate every N generations (default: 10)
- `earlyStopPatience`: Generations without validation improvement before stopping (default: 5)
- `outputDirectory`: Where to write results (e.g., `"output_phase1_v4"`)

### Paths
- `genomePath`: Path to pre-trained genome for Paper/Live/Analysis modes
- `tradeLogPath`: Override trade log location

### API Keys
- `coinGeckoApiKey`: Required for enrichment data (CoinGecko API)
- `binanceApiKey` / `binanceApiSecret`: For live trading (not needed for backtest/paper)

## Config File Conventions

| Pattern | Purpose |
|---------|---------|
| `market-config.default.json` | Committed baseline with placeholder API keys |
| `market-config.phase{1-5}.json` | Training curriculum (progressively refined weights) |
| `market-config.paper.json` | Paper trading with genome path |
| `market-config.json` | Local override (gitignored) — put real API keys here |

### Phase Curriculum Design

| Phase | Focus | Key Differences |
|-------|-------|-----------------|
| Phase 1 | Bootstrap | Return-focused weights (0.60), low shrinkage (1.0), inactivity penalty |
| Phase 2 | Quality | Balanced risk (Return 0.55, DD 0.15, CVaR 0.15), higher shrinkage (2.0) |
| Phase 3-5 | Refinement | Progressively tighter risk weights, more evaluation windows |

Each phase's output directory contains the best genome, which becomes the seed population for the next phase via `genomePath`.

## Execution Modes

| Mode | What It Does |
|------|-------------|
| `Backtest` | Train population on historical data with walk-forward validation and checkpointing |
| `Paper` | Run best genome against live market feeds with simulated execution (no real orders) |
| `Live` | Execute real orders on exchange (placeholder — requires exchange connectivity) |
| `Compare` | Benchmark evolved agent against baselines (buy-and-hold, random, etc.) |
| `Ablation` | Disable signal groups one at a time to measure feature importance |
| `StressTest` | Run evaluation with multiplied costs (fees, slippage) for robustness |
| `MonteCarlo` | Bootstrap resampling and permutation testing of trade sequences |
| `Ensemble` | Multi-genome voting strategy using diverse evolved agents |
| `NeuroAblation` | Knock out brain neurons/connections to identify critical pathways |
| `Pipeline` | Run multiple configs sequentially (multi-phase training) |

## CLI Usage

```bash
# Single config
dotnet run --project src/Seed.Market.App -- --config market-config.phase1.json

# Multi-phase pipeline
dotnet run --project src/Seed.Market.App -- --pipeline market-config.phase1.json market-config.phase2.json

# Positional argument (config path)
dotnet run --project src/Seed.Market.App -- market-config.paper.json
```
