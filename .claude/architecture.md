# Architecture

## Layer Responsibilities

### Seed.Core (Foundation)
Pure-math layer with zero dependencies. Provides:
- **Rng64**: xoshiro256** PRNG with SplitMix64 initialization. All stochastic operations use this.
- **SeedDerivation**: Domain-separated seed generation (RUN, GEN, WORLD, AGENT, DEVELOP, MUTATION, TIEBREAK) ensuring independent PRNG streams per evolutionary operation.
- **Budgets**: Computational constraints for brain development (DevelopmentBudget), runtime (RuntimeBudget), population (PopulationBudget).
- **Interfaces**: `IGenome`, `IBrain`, `IBrainGraph`, `IInnovationTracker`, `IObservatory`.
- **Parameters**: Type-safe evolvable parameter records (DevelopmentParams, LearningParams, StabilityParams).

### Seed.Genetics (Genome Layer)
NEAT-style evolutionary genetics:
- **SeedGenome**: Record containing CppnNetwork + DevelopmentParams + LearningParams + StabilityParams. Implements mutation (5 operators), crossover (gene alignment by innovation ID), distance metric, and JSON serialization.
- **CppnTypes**: Node types (Input/Hidden/Output), activation functions (Identity/Tanh/Sigmoid/Sin/Gauss), connection genes with innovation IDs.
- **InnovationTracker**: Ensures structural mutations produce consistent innovation IDs across the population.
- **Speciation**: Dynamic compatibility threshold (±0.1/gen), fitness sharing, stagnation tracking (25-gen limit).

### Seed.Brain (Neural Runtime)
- **BrainRuntime**: Sparse recurrent network execution. Per-tick: set inputs → micro-step recurrence (1-3 steps) with tanh activation → RMS incoming normalization → homeostasis scaling → synaptic delay buffers.
- **Learning**: Eligibility traces (`e' = λe + a_i·a_j`), fast weight updates modulated by reward/pain/curiosity signals, slow weight consolidation, critical period decay.
- **BrainGraphTypes**: Node/edge structures with dual weights (WSlow/WFast), plasticity gain, edge types (Normal/Modulatory), synaptic delays (0-3 ticks), per-node time constants (tau).

### Seed.Development (CPPN-to-Brain Compiler)
- **BrainDeveloper**: Queries CPPN with geometric inputs (Xi,Yi,Li,Xj,Yj,Lj,Dx,Dy,Dist) for every candidate connection. Outputs: connection score, weight, delay, tau, modulatory gate. TopK selection (16 inputs max per neuron, 20 outputs max) with local neighborhood + global sampling.

### Seed.Observatory (Logging)
- **FileObservatory**: Writes structured events to JSONL files. Thread-safe with file locking.

### Seed.Market (Trading System)
The largest project, organized into subsystems:

| Subsystem | Purpose |
|-----------|---------|
| `Agents/` | MarketAgent bridges brain to market — injects 110 signals (incl. 14 risk/portfolio context), interprets 11 brain outputs as trading signals, computes reward/pain/curiosity/risk for learning |
| `Backtest/` | HistoricalDataStore (Binance API + caching), HistoricalSignalEnricher (7+ data sources), BacktestRunner (rolling window evaluation), RegimeDetector (diverse window selection), Checkpoint (resume logic) |
| `Data/` | Live feeds: BinanceSpotFeed, BinanceFuturesFeed, SentimentFeed, OnChainFeed, MacroFeed, StablecoinFeed, DeribitFeed, CoinglassFeed. DataAggregator orchestrates all feeds into SignalSnapshots |
| `Evaluation/` | BaselineStrategies (buy-and-hold, random), MonteCarloSimulator, StatisticalTests, ExperimentTracker |
| `Evolution/` | MarketEvolution (NEAT loop), MarketEvaluator (parallel genome evaluation), MarketFitness (multi-objective), EliteArchive (per-species champions) |
| `Indicators/` | TechnicalIndicators (SMA, EMA, RSI, ATR, MACD, Bollinger, VWAP, OBV), TimeEncoding, VaderSentiment |
| `Signals/` | SignalIndex (110-element constants, 12 categories), SignalSnapshot (float array + health), SignalNormalizer (rolling z-score to [-1,1]) |
| `Trading/` | PaperTrader (realistic sim with dynamic slippage, funding rates, fees), RiskManager (VaR, position limits, kill switch), ActionInterpreter (brain outputs → TradingSignal), TradeLogger (JSONL), RollingMetrics, KellyPositionSizer, EnsembleTrader |

### Seed.Market.App (CLI)
Single `Program.cs` (~894 lines) with 10+ execution modes. Entry point: parse CLI args → load MarketConfig from JSON → dispatch to mode-specific runner. Candidate for command-pattern refactoring (see NEXT-STEPS.md §3).

### Seed.Dashboard (WPF)
MVVM architecture with CommunityToolkit.Mvvm:
- **MainViewModel** coordinates 6 child ViewModels via tab navigation.
- **Services**: ConfigService (config I/O), PathResolver (directory discovery), TrainingService (runs BacktestRunner on thread pool), PaperTradingService (runs DataAggregator loop), AnalysisService (spawns Market.App subprocess), GenomeService (file scanning), SessionManager (event log), NotificationService (toast queue).
- **No formal DI container** — manual instantiation via constructor injection of `MainViewModel` reference.
- All UI updates marshaled via `Application.Current.Dispatcher.InvokeAsync()`.

## Data Flow: Training (Backtest Mode)

```
MarketConfig JSON
    → BacktestRunner.LoadData() → historical candles + enrichment
    → Split into train/validation windows (rolling 24h step)
    → MarketEvolution.Initialize() → random SeedGenome population

Per generation:
    → MarketEvaluator.Evaluate() [parallel per genome]:
        → BrainDeveloper.CompileGraph(genome.Cppn) → BrainGraph
        → MarketAgent(brain, trader).ProcessTick() per bar
        → PaperTrader executes trades (slippage, fees, funding)
        → MarketFitness.Compute(portfolio) → composite score
    → Apply KNN diversity bonus
    → Speciate (dynamic threshold)
    → Update EliteArchive
    → Reproduce (tournament selection → crossover 35% / mutation 65%)
    → Stagnation reseeding (archive elites if stagnant ≥25 gens)
    → Checkpoint save (every N gens)
    → Validation eval (every N gens, walk-forward advance on improvement)
```

## Data Flow: Paper Trading

```
MarketConfig JSON + genome path
    → Load SeedGenome → BrainDeveloper.CompileGraph() → BrainGraph
    → Create MarketAgent + PaperTrader + DataAggregator

Loop:
    → DataAggregator.TickAsync() polls live feeds:
        BinanceSpotFeed (1s) → price, volume, spread
        BinanceFuturesFeed (15s) → funding, OI, liquidations
        SentimentFeed (5m) → Fear & Greed
        OnChainFeed (1h) → hash rate, addresses, exchange flow
        MacroFeed (1h) → S&P500, VIX, DXY, gold
    → Build SignalSnapshot (92 floats)
    → On bar boundary: agent.ProcessTick(snapshot)
    → ActionInterpreter → TradingSignal
    → PaperTrader.Execute() → portfolio updates
    → Brain.Learn(reward, pain, curiosity)
```

## Dashboard ↔ Market.App Integration

The Dashboard launches Market.App as a **subprocess** for analysis modes (Compare, Ablation, StressTest, MonteCarlo, NeuroAblation):
1. AnalysisService writes a temp `market-config.json` with mode + genome path
2. Spawns `dotnet Seed.Market.App.dll config.json`
3. Reads result from `output/experiments/*.json`

Training and paper trading run **in-process** on background threads via TrainingService and PaperTradingService, with callback-based UI updates.
