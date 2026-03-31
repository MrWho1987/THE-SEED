# System Architecture

## Overview

The Seed is a neuroevolutionary trading system that evolves neural network agents to trade cryptocurrency markets. It uses NEAT (NeuroEvolution of Augmenting Topologies) with CPPN (Compositional Pattern-Producing Network) indirect encoding to evolve brain connectivity patterns, which are compiled into substrate-based neural networks via HyperNEAT-style development.

The system operates in two primary modes:
- **Backtest (training):** Populations of agents compete in simulated market environments using historical data. Multi-objective fitness combines Sharpe ratio, Sortino ratio, return, drawdown duration, and CVaR. Walk-forward validation prevents overfitting.
- **Paper trading (deployment):** The best evolved genome is loaded and processes real-time market data from live exchange feeds to generate trading decisions.

## Solution Structure

The solution targets **.NET 8** across all projects except `tools/Seed.Backtest` which targets .NET 9.

```
Seed.sln
├── src/
│   ├── Seed.Core              Pure-math foundation (no external dependencies)
│   ├── Seed.Genetics           CPPN genomes, NEAT mutation/crossover, speciation
│   ├── Seed.Brain              Substrate-developed neural network runtime
│   ├── Seed.Development        CPPN → Brain compiler (HyperNEAT-style)
│   ├── Seed.Observatory        File-based telemetry and logging
│   ├── Seed.Market             Market arena: signals, agents, fitness, evolution, trading
│   ├── Seed.Market.App         CLI entry point (all execution modes)
│   └── Seed.Dashboard          WPF control room (Windows only, MaterialDesign dark theme)
├── tests/
│   └── Seed.Market.Tests       xUnit test suite (~140+ test cases)
└── tools/
    └── Seed.Backtest           Standalone API verifier (.NET 9)
```

## Project Dependency Graph

```
Seed.Core
├── Seed.Genetics          (depends on Core)
├── Seed.Brain             (depends on Core)
├── Seed.Observatory       (depends on Core)
└── Seed.Development       (depends on Core, Genetics, Brain)
    └── Seed.Market        (depends on Core, Genetics, Brain, Development, Observatory)
        ├── Seed.Market.App    (depends on Market)
        └── Seed.Dashboard     (depends on Core, Genetics, Brain, Development, Market, Observatory)
```

**External NuGet dependencies** exist only in:
- `Seed.Dashboard`: MaterialDesignThemes 5.*, MaterialDesignColors 5.*, OxyPlot.Wpf 2.*, CommunityToolkit.Mvvm 8.*
- `Seed.Market.Tests`: xunit 2.5.3, Microsoft.NET.Test.Sdk 17.8.0, coverlet.collector 6.0.0

All other projects have zero external NuGet dependencies.

## Training Data Flow

```
Historical Data (Binance API)
        │
        ▼
HistoricalDataStore.FetchCandles()
        │
        ├──── HistoricalSignalEnricher (optional: ETH, macro, on-chain, sentiment)
        │
        ▼
CandlesToSignals() ──► float[92] per hourly bar
        │
        ▼
MarketEvolution.RunGeneration()
        │
        ├──── RegimeDetector.SelectDiverseWindows()  (if EvalWindowCount > 1)
        │
        ▼
MarketEvaluator.Evaluate()  (parallel per genome)
        │
        ├──── BrainDeveloper.CompileGraph(genome) ──► BrainGraph ──► BrainRuntime
        │
        ▼
MarketAgent.ProcessTick()  (tick loop over historical signals)
        │
        ├──── brain.Step(signals) ──► outputs
        ├──── ActionInterpreter.Interpret(outputs) ──► TradingSignal
        ├──── PaperTrader.ProcessSignal(signal) ──► TradeResult
        │
        ▼
MarketFitness.ComputeDetailed(portfolio)
        │
        ▼
MarketEvolution ──► speciation, selection, crossover, mutation ──► next generation
```

## Paper Trading Data Flow

```
Live Exchange APIs
        │
        ├── BinanceSpotFeed (5s)
        ├── BinanceFuturesFeed (15s)
        ├── SentimentFeed (5min)
        ├── OnChainFeed (1h)
        ├── MacroFeed (1h)
        └── StablecoinFeed (1h)
                │
                ▼
        DataAggregator.TickAsync()
                │
                ├── Merge raw signals
                ├── TechnicalIndicators.Compute() (needs ≥26 candles)
                ├── TimeEncoding.Compute()
                ├── ComputeDerivedSignals()
                ├── ZeroMaskLiveOnlySignals()
                └── SignalNormalizer.Normalize()
                        │
                        ▼
                SignalSnapshot (92 signals)
                        │
                        ▼
                MarketAgent.ProcessTick(snapshot, tickContext)
                        │
                        ├── brain.Step() + brain.Learn()
                        ├── ActionInterpreter
                        └── PaperTrader
                                │
                                ▼
                        TradeLogger ──► trades.jsonl
                        Heartbeat   ──► heartbeat.jsonl
```

## Brain Pipeline

```
SeedGenome
  ├── CppnNetwork (9 inputs → 6 outputs, variable hidden nodes)
  ├── DevelopmentParams (substrate dimensions, connectivity threshold)
  ├── LearningParams (Hebbian plasticity rates)
  └── StabilityParams (homeostasis, weight clamping)
        │
        ▼
BrainDeveloper.CompileGraph(genome, budget, ctx)
        │
        ├── Build node grid: inputs + hidden(W×H×L) + outputs
        ├── For each destination: find candidate source nodes
        │     ├── Local neighbors (within radius, layer diff ≤ 1)
        │     └── Global random samples (deterministic from seed)
        ├── Query CPPN with geometric coordinates
        │     ├── C > ConnectionThreshold → create edge
        │     ├── W → initial weight (scaled, clamped)
        │     ├── Gate > 0.7 → modulatory edge
        │     └── Delay > 0.3 → synaptic delay
        └── TopKIn per destination, MaxOut per source
                │
                ▼
        BrainGraph (nodes + edges)
                │
                ▼
        BrainRuntime
          ├── Step(): micro-steps, leaky integration, delays, modulation
          └── Learn(): Hebbian + neuromodulatory plasticity
```

## Key Design Decisions

- **Deterministic RNG:** `Rng64` (xoshiro256**) with `SeedDerivation` domain-separated seeds ensures reproducible runs across invocations. All randomness flows from the single `RunSeed` config value.
- **Generational evolution:** The system uses standard NEAT generational replacement, not continuous ecology. Each generation evaluates the full population, selects parents, produces offspring, and advances.
- **One-tick action delay:** `MarketAgent` executes the *previous* tick's trading signal on the current tick. This prevents look-ahead bias by ensuring decisions are based on data available at decision time, not execution time.
- **CPPN indirect encoding:** Rather than evolving weights directly, the system evolves a pattern-producing network (CPPN) that generates connectivity and weights across a geometric substrate. This enables compact genome representation for large brains (e.g., 865 neurons / 4,918 synapses from a 17-node CPPN).
- **Continuous neurons, not spiking:** Despite some legacy naming, `BrainRuntime` uses continuous rate-based neurons with `tanh` activation and leaky time-constant integration, not discrete spike events.
