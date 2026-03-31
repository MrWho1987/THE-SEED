# The Seed — Neuroevolutionary Market Trading System

A neuroevolutionary system that evolves neural network trading agents using NEAT/CPPN-based genome encoding, substrate-based brain development, and multi-objective fitness evaluation. Agents are evolved against historical market data with walk-forward validation, then deployed via paper trading against live market feeds.

## Architecture

```
Seed.Core           Pure-math foundation: RNG, budgets, parameter types
Seed.Genetics       CPPN genomes, NEAT mutation/crossover, speciation
Seed.Brain          Substrate-developed spiking neural networks
Seed.Development    CPPN → Brain compiler (HyperNEAT-style)
Seed.Observatory    File-based observation and logging
Seed.Market         Market arena: signals, agents, fitness, evolution, trading
Seed.Market.App     CLI entry point (backtest, paper, live modes)
Seed.Dashboard      WPF control room (Windows only)
```

### How It Works

1. **Genome encoding.** Each agent is defined by a CPPN (Compositional Pattern-Producing Network) genome that encodes neural connectivity patterns rather than direct weights.

2. **Brain development.** The CPPN is queried across a substrate grid to produce a spiking neural network with hundreds to thousands of neurons and synapses.

3. **Market signals.** The system ingests 30+ normalized market signals: price action, volume, funding rates, on-chain metrics, sentiment, macroeconomic indicators, and technical indicators.

4. **Neuroevolution.** Populations of agents compete in simulated market environments. Fitness combines Sharpe ratio, Sortino ratio, raw return, drawdown duration, and CVaR (Conditional Value at Risk). NEAT speciation maintains diversity.

5. **Walk-forward validation.** Training windows advance only when validation fitness exceeds a threshold, preventing overfitting to a single market regime.

6. **Deployment.** The best evolved genome is loaded into paper/live trading mode, where it processes real-time market data and generates trading decisions.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (required)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (only for `Seed.Backtest` tool)
- A [CoinGecko API key](https://www.coingecko.com/en/api) (free tier works; required for backtest data enrichment and live feeds)
- Windows 10/11 for `Seed.Dashboard` (WPF); all other components are cross-platform

## Quick Start

### 1. Clone and Build

```bash
git clone https://github.com/MrWho1987/THE-SEED.git
cd THE-SEED
dotnet restore
dotnet build
```

### 2. Configure API Key

Copy any `market-config.*.json` file and replace the placeholder API key:

```json
"coinGeckoApiKey": "YOUR_COINGECKO_API_KEY"
```

Or create a local `market-config.json` (gitignored) with your key.

### 3. Run Training (Backtest Mode)

Train a new population from scratch:

```bash
dotnet run --project src/Seed.Market.App -- --config market-config.phase1-bootstrap.json
```

This will evolve agents for the configured number of generations, saving checkpoints to the output directory. Training resumes automatically from the latest checkpoint if interrupted.

### 4. Run Paper Trading

Paper trade using a pre-trained genome:

```bash
dotnet run --project src/Seed.Market.App -- --config market-config.paper.json
```

The repo includes a pre-trained Phase 2 genome at `genomes/phase2_best.json`. The paper config references it via `genomePath`.

### 5. Run Tests

```bash
dotnet test
```

## Configuration

All behavior is controlled via JSON config files. Key parameters:

### Training Parameters

| Parameter | Description | Default |
|---|---|---|
| `populationSize` | Number of agents per generation | 50 |
| `generations` | Total generations to evolve | 100 |
| `trainingWindowHours` | Hours of historical data for training | 720 |
| `evalWindowHours` | Total evaluation window size | 500 |
| `evalWindowCount` | Number of diverse regime windows | 3 |
| `runSeed` | Deterministic RNG seed | 42 |

### Fitness Weights (must sum to 1.0)

| Parameter | Description | Default |
|---|---|---|
| `fitnessSharpeWeight` | Sharpe ratio weight | 0.45 |
| `fitnessSortinoWeight` | Sortino ratio weight | 0.15 |
| `fitnessReturnWeight` | Log return weight | 0.20 |
| `fitnessDrawdownDurationWeight` | Drawdown duration penalty | 0.10 |
| `fitnessCVaRWeight` | Conditional Value at Risk weight | 0.10 |

### Risk Management

| Parameter | Description | Default |
|---|---|---|
| `initialCapital` | Starting capital | 10,000 |
| `maxPositionPct` | Max position as % of equity | 0.25 |
| `killSwitchDrawdownPct` | Hard stop drawdown threshold | 0.15 |
| `maxDailyLossPct` | Daily loss limit | 0.05 |
| `slippageBps` | Simulated slippage in basis points | 5 |

### Execution Mode

Set `"mode"` to one of: `Backtest`, `Paper`, `Live`, `Compare`, `Ablation`, `StressTest`, `MonteCarlo`, `Ensemble`, `NeuroAblation`.

### Pre-built Configs

| Config | Purpose |
|---|---|
| `market-config.phase1-bootstrap.json` | Phase 1: return-focused bootstrap training |
| `market-config.phase2-quality.json` | Phase 2: risk-adjusted quality training |
| `market-config.paper.json` | Paper trading with Phase 2 genome |
| `market-config.default.json` | Balanced defaults for general training |
| `market-config.exp[1-4]-*.json` | Experimental configs for ablation studies |

## Project Structure

```
The Seed/
├── src/
│   ├── Seed.Core/              Core math, RNG, types
│   ├── Seed.Genetics/          CPPN genomes, NEAT, speciation
│   ├── Seed.Brain/             Spiking neural network runtime
│   ├── Seed.Development/       CPPN → Brain compiler
│   ├── Seed.Observatory/       File-based logging
│   ├── Seed.Market/            Market arena and evolution
│   │   ├── Agents/             Agent ↔ brain bridge
│   │   ├── Backtest/           Historical data, checkpoints, regime detection
│   │   ├── Data/               Live data feeds (Binance, CoinGecko, etc.)
│   │   ├── Evaluation/         Baselines, Monte Carlo, statistical tests
│   │   ├── Evolution/          Fitness, evolution loop, elite archive
│   │   ├── Indicators/         Technical indicators, time encoding
│   │   ├── Signals/            Signal normalization and indexing
│   │   └── Trading/            Paper/live trader, risk management, logging
│   ├── Seed.Market.App/        CLI entry point
│   └── Seed.Dashboard/         WPF control room (Windows only)
├── tests/
│   └── Seed.Market.Tests/      xUnit test suite
├── tools/
│   └── Seed.Backtest/          Standalone backtest analysis tool
├── genomes/                    Pre-trained genome files
├── Docs/                       Technical specs and research papers
├── market-config.*.json        Training and deployment configurations
└── Seed.sln                    Visual Studio solution file
```

## Output Directories

Training and paper trading create output directories (gitignored) containing:
- `checkpoint_XXXX.json` — Population snapshots for resuming training
- `best_market_genome.json` — Best genome from training
- `best_training_genome.json` — Best genome by training fitness
- `trades.jsonl` — Trade log
- `heartbeat.jsonl` — Periodic equity snapshots

## Documentation

- [`Docs/MarketEvolution_DraftPaper.md`](Docs/MarketEvolution_DraftPaper.md) — Research paper on the market evolution architecture
- [`Docs/SeedCoreV1_TechnicalSpec.md`](Docs/SeedCoreV1_TechnicalSpec.md) — Core system technical specification
- [`Docs/TheSeed.md`](Docs/TheSeed.md) — Seed v1/v2 contract specification
- [`Docs/PaperTradingFindings.md`](Docs/PaperTradingFindings.md) — Paper trading session analysis and findings

## License

Private repository. All rights reserved.
