# Code Conventions & Patterns

## Determinism

All randomness in evolution/training flows through `Rng64` (xoshiro256**). Seeds are derived via `SeedDerivation` with domain constants:

```csharp
// Correct: domain-separated deterministic seed
var rng = new Rng64(SeedDerivation.MutationSeed(runSeed, generation, speciesId, childOrdinal));

// NEVER in evolution paths:
var rng = new Random();
var id = Guid.NewGuid();
```

`DeterministicHelpers` provides stable sorting (by Guid, by multiple keys), Fisher-Yates shuffle, and sample-without-replacement — all taking `Rng64`.

## C# Style

- **Target**: .NET 8.0 (net9.0 only for tools/Seed.Backtest)
- **Nullable reference types**: Enabled globally
- **Implicit usings**: Enabled globally
- **Records**: Used extensively for immutable data (SeedGenome, MarketConfig, CppnNode, BrainNode, etc.)
- **Sealed records**: MarketConfig and related config types are sealed
- **No DI container**: Manual constructor injection. Dashboard ViewModels receive `MainViewModel` reference for shared service access
- **No external dependencies in core**: Only Dashboard (MaterialDesign, OxyPlot, CommunityToolkit.Mvvm) and tests (xUnit) use NuGet packages

## Naming

- **Projects**: `Seed.{Domain}` (Core, Genetics, Brain, Development, Observatory, Market)
- **Namespaces**: Match project + folder structure (`Seed.Market.Trading`, `Seed.Market.Evolution`, etc.)
- **Config fields**: camelCase in JSON, PascalCase in C# (System.Text.Json with `PropertyNameCaseInsensitive = true`)
- **Signal indices**: PascalCase enum members matching signal name (`BtcPrice`, `FundingRate`, `Rsi14`)
- **Output files**: snake_case with descriptive prefixes (`best_training_genome.json`, `checkpoint_0100.json`)

## Testing

- **Framework**: xUnit 2.5.3 with Microsoft.NET.Test.Sdk 17.8.0
- **Location**: `tests/Seed.Market.Tests/` (single test project covering market-layer logic)
- **Coverage**: ~140+ test cases covering fitness computation, signal normalization, trading execution, risk management, genome operations, candle interval parsing
- **Run all**: `dotnet test`
- **Run filtered**: `dotnet test tests/Seed.Market.Tests --filter "FullyQualifiedName~ClassName"` or `--filter "DisplayName~test_name"`
- **Expectation**: All tests must pass after any code change. Add tests before implementing new features.

## MVVM Pattern (Dashboard)

```csharp
// ViewModels use CommunityToolkit.Mvvm source generators
public partial class TrainingViewModel : ObservableObject
{
    [ObservableProperty] private string _statusText = "";
    [RelayCommand] private async Task StartTraining() { ... }
}
```

- Properties use `[ObservableProperty]` (source-generated from `_fieldName` → `FieldName`)
- Commands use `[RelayCommand]` (source-generated async command with CanExecute)
- UI thread dispatch: `Application.Current.Dispatcher.InvokeAsync(() => { ... })`
- Background work: `Task.Factory.StartNew(..., TaskCreationOptions.LongRunning)` with CancellationToken

## Config System

- `MarketConfig` is a sealed record with 40+ properties and sensible defaults
- Config files: `market-config.{name}.json` at repo root
- `market-config.json` (no suffix) is gitignored — use for local overrides with real API keys
- `market-config.default.json` is committed with placeholder values
- Phase configs (`phase1` through `phase5`) implement a training curriculum with progressively refined fitness weights

## Serialization

- All serialization uses `System.Text.Json` (no Newtonsoft)
- Genome serialization: custom `ToJson()`/`FromJson()` methods on `SeedGenome` with schema version field
- Config serialization: `JsonSerializer.Deserialize<MarketConfig>()` with case-insensitive property matching
- Event logging: one JSON object per line (JSONL format) via `FileObservatory`

## Error Handling

- Dashboard: Try-catch around genome loading, build operations, and subprocess management. Failures surface via NotificationService toasts.
- Data feeds: Graceful degradation — if a feed fails, last known values persist. SignalSnapshot reports health status (Full/Partial/Stale).
- Checkpointing: Atomic write pattern (write to temp, rename). Resume tolerates missing or corrupt checkpoints by restarting from generation 0.

## Refactoring Rules (from NEXT-STEPS.md)

1. Extract-only: move code to new files, update references
2. No logic changes during structural refactoring
3. Commit each file split independently for easy bisection
4. All tests must pass after every step
