# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

THE-SEED is a neuroevolutionary crypto trading system. It evolves neural network agents using NEAT/CPPN genome encoding, compiles them into sparse recurrent brains via HyperNEAT-style substrate development, and trains them against historical market data with walk-forward validation. Agents process 110 normalized market signals and emit 11 trading decision outputs.

Current baseline: **V11** — still v1 development (no v2 yet). 15-minute candles, Binance futures
(MaxLeverage 125), 20×20×3 brain (1200 neurons, TopKIn 32, MaxOut 40, MaxSynapticDelay 16),
11-output action space (direction/size/urgency/exit/predict/leverage/partialClose/trailEnable/
trailDist/tpOffset/slOverride), 9-component fitness (Sharpe/Sortino/Return/DDDuration/CVaR/
Calmar/InfoRatio/FeeDrag/Diversification).

## Build, Test, Run

```bash
# Build everything
dotnet build

# Run all tests (xUnit, ~358 cases)
dotnet test

# Run a single test file
dotnet test tests/Seed.Market.Tests --filter "FullyQualifiedName~ClassName"

# Run training (backtest mode)
dotnet run --project src/Seed.Market.App -- --config market-config.phase1.json

# Run paper trading
dotnet run --project src/Seed.Market.App -- --config market-config.paper.json

# Run multi-phase pipeline
dotnet run --project src/Seed.Market.App -- --pipeline market-config.phase1.json market-config.phase2.json

# Launch WPF Dashboard (Windows only)
dotnet run --project src/Seed.Dashboard
```

**Frameworks:** .NET 8.0 for all projects; .NET 9.0 only for `tools/Seed.Backtest`. Dashboard is WPF (Windows-only).

## Architecture At a Glance

```
Seed.Core            Zero-dependency foundation: RNG (xoshiro256**), budgets, params, interfaces
  ↓
Seed.Genetics        CPPN genomes, NEAT mutation/crossover, innovation tracking, speciation
  ↓
Seed.Brain           Sparse recurrent neural network runtime with dual-weight plasticity
  ↓
Seed.Development     CPPN → BrainGraph compiler (substrate-based, HyperNEAT-style)
  ↓
Seed.Observatory     File-based event logging (JSONL)
  ↓
Seed.Market          Core trading system (agents, backtesting, data feeds, evolution, fitness, trading)
  ↓                  ↓
Seed.Market.App      Seed.Dashboard
(CLI: 10+ modes)     (WPF control room)
```

Dependencies flow strictly downward. Core libraries have **zero NuGet dependencies** — only Dashboard (MaterialDesign, OxyPlot, CommunityToolkit.Mvvm) and tests (xUnit) use external packages.

## Detailed Guidance

Architecture details, domain concepts, code conventions, and configuration reference are split into focused files under `.claude/`:

| File | Contents |
|------|----------|
| [`.claude/architecture.md`](.claude/architecture.md) | Data flow, layer responsibilities, key integration points |
| [`.claude/domain-concepts.md`](.claude/domain-concepts.md) | NEAT, CPPN, brain development, signals, fitness formula |
| [`.claude/project-map.md`](.claude/project-map.md) | Project dependency graph, key files by subsystem, file size hotspots |
| [`.claude/conventions.md`](.claude/conventions.md) | Code patterns, naming, determinism, testing, refactoring rules |
| [`.claude/configuration.md`](.claude/configuration.md) | MarketConfig reference, execution modes, config file conventions |
| [`.claude/build-and-test.md`](.claude/build-and-test.md) | Detailed build/test/run commands and troubleshooting |

## Critical Rules

1. **Determinism is sacred.** All randomness flows through `Rng64` (xoshiro256**) with domain-separated seeds via `SeedDerivation`. Never use `System.Random` or `Guid.NewGuid()` in evolution/training paths.
2. **All tests must pass.** Run `dotnet test` after any code change. The test suite covers fitness computation, signal normalization, trading logic, risk management, and genome operations.
3. **Fitness weights must sum to 1.0.** V11: nine fitness weights (Sharpe, Sortino, Return, DrawdownDuration, CVaR, Calmar, InfoRatio, FeeDrag, Diversification) validated at runtime in `MarketConfig.Validate()`.
4. **Safety mechanisms live outside the brain.** Stop-loss, kill-switch, and daily loss limits are execution-layer concerns — the brain cannot learn to circumvent them.
5. **Fine-tune, don't restart.** When expanding capabilities, seed populations from existing trained genomes rather than starting from scratch.
6. **No logic changes during refactoring.** Extract-only moves. Verify tests pass after each structural change.
7. **API keys belong in local config.** `market-config.json` is gitignored. Never commit real API keys — use `market-config.default.json` with placeholders.

## Existing Documentation

The `Docs/` directory contains 11 detailed technical references (Architecture, CoreEngine, Genome, Brain, Signals, FitnessAndEvolution, Trading, Configuration, RunningTheSystem, PaperTradingFindings). Consult these for deep dives into specific subsystems.

`NEXT-STEPS.md` contains the current roadmap: leverage support, brain expansion, code cleanup, stop-loss evolution, additional signals, multi-asset trading, and production deployment.
