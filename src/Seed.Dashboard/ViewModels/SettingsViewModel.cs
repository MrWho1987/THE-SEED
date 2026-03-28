using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seed.Dashboard.Services;
using Seed.Market;
using WinOpenFileDialog = Microsoft.Win32.OpenFileDialog;
using WinSaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Seed.Dashboard.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private bool _isAdvancedMode;

    // Capital & fees
    [ObservableProperty] private decimal _initialCapital;
    [ObservableProperty] private decimal _makerFee;
    [ObservableProperty] private decimal _takerFee;
    [ObservableProperty] private decimal _slippageBps;

    [ObservableProperty] private string _symbolsText = "";

    // Risk
    [ObservableProperty] private decimal _maxPositionPct;
    [ObservableProperty] private decimal _maxDailyLossPct;
    [ObservableProperty] private decimal _killSwitchDrawdownPct;
    [ObservableProperty] private int _maxConcurrentPositions;
    [ObservableProperty] private decimal _maxDailyVaRPct;

    // Evolution
    [ObservableProperty] private int _populationSize;
    [ObservableProperty] private int _generations;
    [ObservableProperty] private int _trainingWindowHours;
    [ObservableProperty] private int _validationWindowHours;
    [ObservableProperty] private int _evalWindowHours;
    [ObservableProperty] private int _evalWindowCount;
    [ObservableProperty] private int _rollingStepHours;
    [ObservableProperty] private string _runSeedText = "42";

    // Fitness
    [ObservableProperty] private float _shrinkageK;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FitnessWeightsSum))]
    [NotifyPropertyChangedFor(nameof(IsFitnessWeightsValid))]
    [NotifyPropertyChangedFor(nameof(FitnessWeightsStatusMessage))]
    private float _fitnessSharpeWeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FitnessWeightsSum))]
    [NotifyPropertyChangedFor(nameof(IsFitnessWeightsValid))]
    [NotifyPropertyChangedFor(nameof(FitnessWeightsStatusMessage))]
    private float _fitnessSortinoWeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FitnessWeightsSum))]
    [NotifyPropertyChangedFor(nameof(IsFitnessWeightsValid))]
    [NotifyPropertyChangedFor(nameof(FitnessWeightsStatusMessage))]
    private float _fitnessReturnWeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FitnessWeightsSum))]
    [NotifyPropertyChangedFor(nameof(IsFitnessWeightsValid))]
    [NotifyPropertyChangedFor(nameof(FitnessWeightsStatusMessage))]
    private float _fitnessDrawdownDurationWeight;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FitnessWeightsSum))]
    [NotifyPropertyChangedFor(nameof(IsFitnessWeightsValid))]
    [NotifyPropertyChangedFor(nameof(FitnessWeightsStatusMessage))]
    private float _fitnessCVaRWeight;

    // Species
    [ObservableProperty] private int _targetSpeciesMin;
    [ObservableProperty] private int _targetSpeciesMax;
    [ObservableProperty] private float _compatibilityAdjustRate;
    [ObservableProperty] private int _minOffspringPerSpecies;
    [ObservableProperty] private int _stagnationLimit;
    [ObservableProperty] private float _diversityBonusScale;
    [ObservableProperty] private int _diversityKNeighbors;

    // Brain
    [ObservableProperty] private int _maxBrainNodes;
    [ObservableProperty] private int _maxBrainEdges;

    // Feeds
    [ObservableProperty] private int _spotPollMs;
    [ObservableProperty] private int _futuresPollMs;
    [ObservableProperty] private int _sentimentPollMs;
    [ObservableProperty] private int _onChainPollMs;
    [ObservableProperty] private int _macroPollMs;

    [ObservableProperty] private string? _coinGeckoApiKey;

    [ObservableProperty] private ExecutionMode _mode;
    [ObservableProperty] private bool _confirmLive;

    [ObservableProperty] private string _outputDirectory = "";

    // Validation
    [ObservableProperty] private int _validationIntervalGens;
    [ObservableProperty] private int _earlyStopPatience;
    [ObservableProperty] private bool _earlyStopEnabled;
    [ObservableProperty] private bool _walkForwardEnabled;
    [ObservableProperty] private float _walkForwardMinValFitness;
    [ObservableProperty] private int _walkForwardMaxStallGens;

    // Paper / paths
    [ObservableProperty] private string? _genomePath;
    [ObservableProperty] private string? _tradeLogPath;
    [ObservableProperty] private int _displayIntervalMs;
    [ObservableProperty] private int _checkpointIntervalGens;

    public float FitnessWeightsSum =>
        FitnessSharpeWeight + FitnessSortinoWeight + FitnessReturnWeight
        + FitnessDrawdownDurationWeight + FitnessCVaRWeight;

    public bool IsFitnessWeightsValid => Math.Abs(FitnessWeightsSum - 1f) < 0.001f;

    public string FitnessWeightsStatusMessage =>
        IsFitnessWeightsValid
            ? $"Weights sum to {FitnessWeightsSum:F4} (OK)"
            : $"Weights sum to {FitnessWeightsSum:F4} — expected 1.0000";

    public ObservableCollection<ExecutionMode> ExecutionModes { get; } =
        new(Enum.GetValues<ExecutionMode>());

    public SettingsViewModel(MainViewModel main)
    {
        _main = main;
        SyncFromConfig(_main.Config.CurrentConfig);
    }

    public void SyncFromConfig(MarketConfig c)
    {
        InitialCapital = c.InitialCapital;
        MakerFee = c.MakerFee;
        TakerFee = c.TakerFee;
        SlippageBps = c.SlippageBps;

        SymbolsText = string.Join(", ", c.Symbols);

        MaxPositionPct = c.MaxPositionPct;
        MaxDailyLossPct = c.MaxDailyLossPct;
        KillSwitchDrawdownPct = c.KillSwitchDrawdownPct;
        MaxConcurrentPositions = c.MaxConcurrentPositions;
        MaxDailyVaRPct = c.MaxDailyVaRPct;

        PopulationSize = c.PopulationSize;
        Generations = c.Generations;
        TrainingWindowHours = c.TrainingWindowHours;
        ValidationWindowHours = c.ValidationWindowHours;
        EvalWindowHours = c.EvalWindowHours;
        EvalWindowCount = c.EvalWindowCount;
        RollingStepHours = c.RollingStepHours;
        RunSeedText = c.RunSeed.ToString(CultureInfo.InvariantCulture);

        ShrinkageK = c.ShrinkageK;
        FitnessSharpeWeight = c.FitnessSharpeWeight;
        FitnessSortinoWeight = c.FitnessSortinoWeight;
        FitnessReturnWeight = c.FitnessReturnWeight;
        FitnessDrawdownDurationWeight = c.FitnessDrawdownDurationWeight;
        FitnessCVaRWeight = c.FitnessCVaRWeight;

        TargetSpeciesMin = c.TargetSpeciesMin;
        TargetSpeciesMax = c.TargetSpeciesMax;
        CompatibilityAdjustRate = c.CompatibilityAdjustRate;
        MinOffspringPerSpecies = c.MinOffspringPerSpecies;
        StagnationLimit = c.StagnationLimit;
        DiversityBonusScale = c.DiversityBonusScale;
        DiversityKNeighbors = c.DiversityKNeighbors;

        MaxBrainNodes = c.MaxBrainNodes;
        MaxBrainEdges = c.MaxBrainEdges;

        SpotPollMs = c.SpotPollMs;
        FuturesPollMs = c.FuturesPollMs;
        SentimentPollMs = c.SentimentPollMs;
        OnChainPollMs = c.OnChainPollMs;
        MacroPollMs = c.MacroPollMs;

        CoinGeckoApiKey = c.CoinGeckoApiKey;

        Mode = c.Mode;
        ConfirmLive = c.ConfirmLive;

        OutputDirectory = c.OutputDirectory;

        ValidationIntervalGens = c.ValidationIntervalGens;
        EarlyStopPatience = c.EarlyStopPatience;
        EarlyStopEnabled = c.EarlyStopEnabled;
        WalkForwardEnabled = c.WalkForwardEnabled;
        WalkForwardMinValFitness = c.WalkForwardMinValFitness;
        WalkForwardMaxStallGens = c.WalkForwardMaxStallGens;

        GenomePath = c.GenomePath;
        TradeLogPath = c.TradeLogPath;
        DisplayIntervalMs = c.DisplayIntervalMs;
        CheckpointIntervalGens = c.CheckpointIntervalGens;
    }

    private MarketConfig BuildConfig()
    {
        var symbols = string.IsNullOrWhiteSpace(SymbolsText)
            ? new[] { "BTCUSDT", "ETHUSDT" }
            : SymbolsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (symbols.Length == 0)
            symbols = new[] { "BTCUSDT" };

        if (!ulong.TryParse(RunSeedText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
            seed = 42;

        return new MarketConfig
        {
            InitialCapital = InitialCapital,
            MakerFee = MakerFee,
            TakerFee = TakerFee,
            SlippageBps = SlippageBps,
            Symbols = symbols,
            MaxPositionPct = MaxPositionPct,
            MaxDailyLossPct = MaxDailyLossPct,
            KillSwitchDrawdownPct = KillSwitchDrawdownPct,
            MaxConcurrentPositions = MaxConcurrentPositions,
            MaxDailyVaRPct = MaxDailyVaRPct,
            PopulationSize = PopulationSize,
            Generations = Generations,
            TrainingWindowHours = TrainingWindowHours,
            ValidationWindowHours = ValidationWindowHours,
            EvalWindowHours = EvalWindowHours,
            EvalWindowCount = EvalWindowCount,
            RollingStepHours = RollingStepHours,
            RunSeed = seed,
            ShrinkageK = ShrinkageK,
            FitnessSharpeWeight = FitnessSharpeWeight,
            FitnessSortinoWeight = FitnessSortinoWeight,
            FitnessReturnWeight = FitnessReturnWeight,
            FitnessDrawdownDurationWeight = FitnessDrawdownDurationWeight,
            FitnessCVaRWeight = FitnessCVaRWeight,
            TargetSpeciesMin = TargetSpeciesMin,
            TargetSpeciesMax = TargetSpeciesMax,
            CompatibilityAdjustRate = CompatibilityAdjustRate,
            MinOffspringPerSpecies = MinOffspringPerSpecies,
            StagnationLimit = StagnationLimit,
            DiversityBonusScale = DiversityBonusScale,
            DiversityKNeighbors = DiversityKNeighbors,
            MaxBrainNodes = MaxBrainNodes,
            MaxBrainEdges = MaxBrainEdges,
            SpotPollMs = SpotPollMs,
            FuturesPollMs = FuturesPollMs,
            SentimentPollMs = SentimentPollMs,
            OnChainPollMs = OnChainPollMs,
            MacroPollMs = MacroPollMs,
            CoinGeckoApiKey = string.IsNullOrWhiteSpace(CoinGeckoApiKey) ? null : CoinGeckoApiKey.Trim(),
            Mode = Mode,
            ConfirmLive = ConfirmLive,
            OutputDirectory = OutputDirectory.Trim(),
            ValidationIntervalGens = ValidationIntervalGens,
            EarlyStopPatience = EarlyStopPatience,
            EarlyStopEnabled = EarlyStopEnabled,
            WalkForwardEnabled = WalkForwardEnabled,
            WalkForwardMinValFitness = WalkForwardMinValFitness,
            WalkForwardMaxStallGens = WalkForwardMaxStallGens,
            GenomePath = string.IsNullOrWhiteSpace(GenomePath) ? null : GenomePath.Trim(),
            TradeLogPath = string.IsNullOrWhiteSpace(TradeLogPath) ? null : TradeLogPath.Trim(),
            DisplayIntervalMs = DisplayIntervalMs,
            CheckpointIntervalGens = CheckpointIntervalGens
        };
    }

    [RelayCommand]
    private void QuickTest() => TryApplyPreset("market-config.paper.json");

    [RelayCommand]
    private void Standard() => TryApplyPreset("market-config.default.json");

    [RelayCommand]
    private void DeepV2() => TryApplyPreset("market-config.deep-v2.json");

    private void TryApplyPreset(string fileName)
    {
        var path = Path.Combine(Environment.CurrentDirectory, fileName);
        if (!File.Exists(path))
        {
            _main.Notifications.Show("Preset not found",
                $"Looked for {path}", NotificationType.Warning);
            return;
        }

        _main.Config.Load(path);
        SyncFromConfig(_main.Config.CurrentConfig);
        _main.Notifications.Show("Preset loaded", fileName, NotificationType.Info);
    }

    [RelayCommand]
    private void Save()
    {
        if (!IsFitnessWeightsValid)
        {
            _main.Notifications.Show("Validation",
                FitnessWeightsStatusMessage, NotificationType.Warning);
            return;
        }

        var cfg = BuildConfig();
        _main.Config.UpdateConfig(cfg);

        if (string.IsNullOrEmpty(_main.Config.CurrentConfigPath))
        {
            _main.Notifications.Show("Save",
                "No path set — use Save As to write a file.", NotificationType.Warning);
            return;
        }

        _main.Config.Save(_main.Config.CurrentConfigPath!);
        _main.Notifications.Show("Saved", _main.Config.CurrentConfigPath!, NotificationType.Success);
    }

    [RelayCommand]
    private void SaveAs()
    {
        if (!IsFitnessWeightsValid)
        {
            _main.Notifications.Show("Validation",
                FitnessWeightsStatusMessage, NotificationType.Warning);
            return;
        }

        var dlg = new WinSaveFileDialog
        {
            Filter = "Market config JSON|market-config*.json;*.json|All files|*.*",
            FileName = "market-config.json",
            DefaultExt = ".json"
        };

        if (dlg.ShowDialog() != true) return;

        var cfg = BuildConfig();
        _main.Config.UpdateConfig(cfg);
        _main.Config.Save(dlg.FileName);
        _main.Notifications.Show("Saved", dlg.FileName, NotificationType.Success);
    }

    [RelayCommand]
    private void Reset()
    {
        SyncFromConfig(MarketConfig.Default);
        _main.Config.UpdateConfig(MarketConfig.Default);
        _main.Notifications.Show("Reset", "Editor reverted to default template.", NotificationType.Info);
    }

    [RelayCommand]
    private void Load()
    {
        var dlg = new WinOpenFileDialog
        {
            Filter = "Market config JSON|market-config*.json;*.json|All files|*.*"
        };

        if (dlg.ShowDialog() != true) return;

        _main.Config.Load(dlg.FileName);
        SyncFromConfig(_main.Config.CurrentConfig);
        _main.Notifications.Show("Loaded", dlg.FileName, NotificationType.Info);
    }

    [RelayCommand]
    private void SetUiMode(string? mode)
    {
        if (string.Equals(mode, "Advanced", StringComparison.OrdinalIgnoreCase))
            IsAdvancedMode = true;
        else
            IsAdvancedMode = false;
    }
}
