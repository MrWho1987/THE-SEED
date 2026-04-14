# Project Map

## Dependency Graph

```
Seed.Core ──────────────────────────────────────────────┐
    │                                                    │
    ├── Seed.Genetics ──────────────────────┐            │
    │                                       │            │
    ├── Seed.Brain ─────────────────────────┤            │
    │                                       │            │
    ├── Seed.Development ───────────────────┤            │
    │   (depends on Core, Genetics, Brain)  │            │
    │                                       │            │
    ├── Seed.Observatory ───────────────────┤            │
    │                                       │            │
    └── Seed.Market ────────────────────────┘            │
        (depends on all above)                           │
            │                                            │
            ├── Seed.Market.App (console exe, net8.0)    │
            │                                            │
            └── Seed.Dashboard (WPF exe, net8.0-windows) ┘
                (depends on Core, Genetics, Brain,
                 Development, Market, Observatory)

tools/Seed.Backtest (standalone exe, net9.0, no project refs)
tests/Seed.Market.Tests (xUnit, refs Seed.Market)
```

## Key Files by Subsystem

### Core (src/Seed.Core/)
| File | Purpose |
|------|---------|
| `Interfaces.cs` | IGenome, IBrain, IBrainGraph, IInnovationTracker, IObservatory |
| `Rng64.cs` | xoshiro256** PRNG — all randomness flows through this |
| `SeedDerivation.cs` | Domain-separated seed generation for deterministic evolution |
| `Budgets.cs` | DevelopmentBudget, RuntimeBudget, PopulationBudget constraints |
| `Parameters.cs` | Evolvable parameter records with bounds |
| `Contexts.cs` | DevelopmentContext, BrainStepContext, BrainLearnContext |

### Genetics (src/Seed.Genetics/)
| File | Purpose |
|------|---------|
| `SeedGenome.cs` (~548 lines) | Genome record, mutation operators, crossover, serialization |
| `CppnTypes.cs` (~277 lines) | CppnNetwork, CppnNode, CppnConnection, activation functions |
| `InnovationTracker.cs` | Global innovation ID assignment for structural mutations |
| `Speciation.cs` (~215 lines) | Species assignment, compatibility distance, stagnation tracking |

### Brain (src/Seed.Brain/)
| File | Purpose |
|------|---------|
| `BrainRuntime.cs` (~423 lines) | Step function, learning, homeostasis, delay buffers |
| `BrainGraphTypes.cs` (~237 lines) | BrainNode, BrainEdge, BrainGraph with JSON serialization |

### Development (src/Seed.Development/)
| File | Purpose |
|------|---------|
| `BrainDeveloper.cs` (~267 lines) | CPPN → BrainGraph compiler, substrate layout, TopK selection |

### Market — Evolution (src/Seed.Market/Evolution/)
| File | Purpose |
|------|---------|
| `MarketEvolution.cs` (~613 lines) | Full NEAT loop: evaluate, speciate, reproduce, checkpoint |
| `MarketEvaluator.cs` (~228 lines) | Parallel genome evaluation, brain compilation |
| `MarketFitness.cs` (~246 lines) | Multi-objective fitness: Sharpe, Sortino, Return, DD, CVaR |
| `EliteArchive.cs` (~77 lines) | Per-species hall of fame |

### Market — Trading (src/Seed.Market/Trading/)
| File | Purpose |
|------|---------|
| `TradingTypes.cs` | TradeDirection, Position, TradingSignal, PortfolioState |
| `PaperTrader.cs` | Realistic simulation (slippage, funding, fees, kill switch) |
| `ActionInterpreter.cs` | 5 brain outputs → TradingSignal |
| `RiskManager.cs` | VaR, position limits, daily loss limit |
| `RollingMetrics.cs` | Real-time Sharpe, Sortino, drawdown |
| `TradeLogger.cs` | JSONL trade log |
| `EnsembleTrader.cs` | Multi-genome voting |
| `KellyPositionSizer.cs` | Kelly criterion sizing |

### Market — Signals (src/Seed.Market/Signals/)
| File | Purpose |
|------|---------|
| `SignalIndex.cs` | 92-element enum (all signal indices) |
| `SignalSnapshot.cs` | Float array + timestamp + health status |
| `SignalNormalizer.cs` | Per-signal min/max normalization to [-1,1] |

### Market — Data (src/Seed.Market/Data/)
| File | Purpose |
|------|---------|
| `DataAggregator.cs` (~316 lines) | Orchestrates all live feeds into SignalSnapshots |
| `BinanceSpotFeed.cs` | Live spot price/volume via REST |
| `BinanceFuturesFeed.cs` | Futures funding, OI, liquidations |
| `SentimentFeed.cs` | Fear & Greed Index |
| `OnChainFeed.cs` | Blockchain metrics |
| `MacroFeed.cs` | S&P500, VIX, DXY, gold, treasury |
| `StablecoinFeed.cs` | USDT/USDC flows, dominance |

### Market — Backtest (src/Seed.Market/Backtest/)
| File | Purpose |
|------|---------|
| `BacktestRunner.cs` | Data loading, train/val split, rolling window evaluation |
| `HistoricalDataStore.cs` | Binance API candle fetching + disk caching |
| `HistoricalSignalEnricher.cs` (~875 lines) | 7+ source enrichment pipeline |
| `RegimeDetector.cs` | Diverse evaluation window selection |
| `Checkpoint.cs` | Generation checkpoint save/load for resume |

### Market — Agents (src/Seed.Market/Agents/)
| File | Purpose |
|------|---------|
| `MarketAgent.cs` (~180 lines) | Brain ↔ market bridge, signal injection, reward computation |

### Dashboard (src/Seed.Dashboard/)
| Directory | Purpose |
|-----------|---------|
| `ViewModels/` | MVVM ViewModels: Main, Dashboard, Training, PaperTrading, GenomeLab, Analysis, Settings |
| `Services/` | ConfigService, TrainingService, PaperTradingService, AnalysisService, GenomeService, PathResolver, SessionManager, NotificationService |
| `Views/` | XAML views matching each ViewModel |
| `Controls/` | BrainTopologyControl, SignalHeatmapControl, WalkForwardTimeline |

### CLI (src/Seed.Market.App/)
| File | Purpose |
|------|---------|
| `Program.cs` (~894 lines) | Entry point with 10+ mode dispatchers (Backtest, Paper, Live, Compare, Ablation, StressTest, MonteCarlo, Ensemble, NeuroAblation, Pipeline) |

## Refactoring Hotspots

Files flagged for splitting (see NEXT-STEPS.md §3):

| File | Lines | Issue | Proposed Split |
|------|-------|-------|----------------|
| `Program.cs` | 894 | 10+ modes in one file | Extract per-mode command classes |
| `HistoricalSignalEnricher.cs` | 875 | 7+ data sources interleaved | Extract per-source enricher classes |
| `MarketEvolution.cs` | 613 | Evolution + speciation + checkpointing | Separate SpeciationManager, CheckpointManager |
| `DataAggregator.cs` | 316 | Feed orchestration + signal computation | Separate LiveSignalComputer, LiveFeedManager |

## Output File Conventions

Training and paper trading produce output directories (gitignored):

| File | Location | Format |
|------|----------|--------|
| `checkpoint_XXXX.json` | `{output}/checkpoints/` | Full population state |
| `best_gen_XXXX.json` | `{output}/checkpoints/` | Best genome per generation |
| `best_val_XXXX.json` | `{output}/checkpoints/` | Best validation genome |
| `best_training_genome.json` | `{output}/` | Final best from training |
| `best_market_genome.json` | `{output}/` | Exported for deployment |
| `genome_scores.json` | `{output}/` | Fitness metadata |
| `events.jsonl` | `{output}/` | Observatory event log |
| `trades.jsonl` | `{output}/` | Trade log |
| `heartbeat.jsonl` | `{output}/` | Periodic equity snapshots |
| `experiments/*.json` | `{output}/experiments/` | Analysis mode results |
