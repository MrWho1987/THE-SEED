# Build, Test & Run

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (required for all projects)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (only for `tools/Seed.Backtest`)
- Windows 10/11 for Dashboard (WPF); all other components are cross-platform
- CoinGecko API key (free tier) for backtest enrichment and live feeds

## Build

```bash
# Restore and build entire solution
dotnet build

# Build a specific project
dotnet build src/Seed.Market

# Build in Release mode
dotnet build -c Release

# Clean build
dotnet clean && dotnet build
```

The solution file is `Seed.sln` at the repo root. All projects target net8.0 except `tools/Seed.Backtest` (net9.0).

## Test

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test -v normal

# Run a specific test class
dotnet test tests/Seed.Market.Tests --filter "FullyQualifiedName~FitnessTests"

# Run a specific test method
dotnet test tests/Seed.Market.Tests --filter "DisplayName~SharpeRatio_ZeroTrades_Returns_Penalty"

# Run tests matching a pattern
dotnet test tests/Seed.Market.Tests --filter "FullyQualifiedName~Trading"
```

Test project: `tests/Seed.Market.Tests` (xUnit 2.5.3, ~358 test cases).

## Run Training

```bash
# Phase 1 bootstrap training
dotnet run --project src/Seed.Market.App -- --config market-config.phase1.json

# Multi-phase pipeline
dotnet run --project src/Seed.Market.App -- --pipeline market-config.phase1.json market-config.phase2.json market-config.phase3.json

# Resume from checkpoint (automatic — just re-run the same command)
dotnet run --project src/Seed.Market.App -- --config market-config.phase1.json
```

Training creates an output directory (e.g., `output_phase1_v4/`) with checkpoints, genomes, and logs. Interrupted runs resume automatically from the latest checkpoint.

## Run Paper Trading

```bash
dotnet run --project src/Seed.Market.App -- --config market-config.paper.json
```

Requires `genomePath` set in config pointing to a trained genome, and a valid CoinGecko API key.

## Run Analysis Modes

```bash
# Compare against baselines
dotnet run --project src/Seed.Market.App -- --config market-config.compare.json

# Ablation study
dotnet run --project src/Seed.Market.App -- --config market-config.ablation.json
```

Set `"mode"` in the config JSON to the desired analysis mode.

## Run Dashboard

```bash
# Windows only (WPF)
dotnet run --project src/Seed.Dashboard
```

The Dashboard provides a GUI for training control, paper trading monitoring, genome inspection, and analysis. It runs Market.App as a subprocess for analysis modes and in-process for training/paper trading.

## Run Standalone Backtest Tool

```bash
# Requires .NET 9 SDK
dotnet run --project tools/Seed.Backtest
```

## Config Setup for Development

1. Copy any `market-config.*.json` to `market-config.json` (gitignored)
2. Replace `coinGeckoApiKey` with your real API key
3. Adjust `outputDirectory` to avoid overwriting existing training runs
4. For quick iteration: reduce `populationSize` to 20, `generations` to 10

## Common Issues

- **Missing API key**: Enrichment fails silently with stale/partial signal data. Check console output for HTTP errors.
- **Checkpoint corruption**: Delete the specific checkpoint file; training resumes from the previous valid one or restarts.
- **Dashboard won't build on Linux/macOS**: Expected — Dashboard is WPF (Windows-only). All other projects build cross-platform.
- **net9.0 error**: `tools/Seed.Backtest` requires .NET 9 SDK. Install it separately or exclude from build: `dotnet build --no-dependencies src/Seed.Market.App`
