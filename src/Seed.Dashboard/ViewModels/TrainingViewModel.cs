using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Seed.Dashboard.Services;

namespace Seed.Dashboard.ViewModels;

public record ConfigItem(string DisplayName, string FullPath)
{
    public override string ToString() => DisplayName;
}

public record GenerationRow(
    int Gen, float Best, float Mean, float Sharpe, string Return,
    string WinRate, int Trades, int Species, string ValFit, string Status);

public partial class TrainingViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private TrainingService? _trainingService;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isComplete;
    [ObservableProperty] private int _currentGen;
    [ObservableProperty] private int _totalGens;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _elapsedTime = "";
    [ObservableProperty] private string _etaTime = "";
    [ObservableProperty] private string _selectedConfig = "";

    [ObservableProperty] private string _bestFitness = "—";
    [ObservableProperty] private string _bestSharpe = "—";
    [ObservableProperty] private string _bestReturn = "—";
    [ObservableProperty] private string _bestTrades = "—";
    [ObservableProperty] private string _bestWinRate = "—";
    [ObservableProperty] private string _bestSubstrate = "—";

    [ObservableProperty] private int _walkForwardPasses;
    [ObservableProperty] private string _walkForwardOffset = "0h";
    [ObservableProperty] private string _stallCounter = "0/50";
    [ObservableProperty] private string _lastValidation = "—";

    [ObservableProperty] private string _configPopulation = "";
    [ObservableProperty] private string _configGenerations = "";
    [ObservableProperty] private string _configEvalWindow = "";
    [ObservableProperty] private string _configSeed = "";

    [ObservableProperty] private PlotModel _fitnessPlotModel;

    public ObservableCollection<GenerationRow> GenerationLog { get; } = [];
    public ObservableCollection<string> AvailableConfigs { get; } = [];

    private readonly LineSeries _bestSeries;
    private readonly LineSeries _meanSeries;
    private readonly ScatterSeries _valSeries;
    private DateTime _startTime;

    public TrainingViewModel(MainViewModel main)
    {
        _main = main;

        var model = new PlotModel
        {
            PlotAreaBorderColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8),
            TitleColor = OxyColor.FromRgb(0xE2, 0xE8, 0xF0),
            Background = OxyColors.Transparent,
            PlotAreaBackground = OxyColors.Transparent
        };

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom, Title = "Generation",
            Minimum = 0, Maximum = 10,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TicklineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left, Title = "Fitness",
            Minimum = -1, Maximum = 5,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TicklineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });

        _bestSeries = new LineSeries
        {
            Title = "Best Fitness",
            Color = OxyColor.FromRgb(0x00, 0xF6, 0xA1),
            StrokeThickness = 2
        };
        _meanSeries = new LineSeries
        {
            Title = "Mean Fitness",
            Color = OxyColor.FromArgb(128, 0x3B, 0x82, 0xF6),
            StrokeThickness = 1.5,
            LineStyle = LineStyle.Dash
        };
        _valSeries = new ScatterSeries
        {
            Title = "Validation",
            MarkerType = MarkerType.Circle,
            MarkerSize = 5,
            MarkerFill = OxyColor.FromRgb(0xF5, 0x9E, 0x0B),
            MarkerStroke = OxyColor.FromRgb(0xF5, 0x9E, 0x0B),
            MarkerStrokeThickness = 1
        };

        model.Series.Add(_bestSeries);
        model.Series.Add(_meanSeries);
        model.Series.Add(_valSeries);

        _fitnessPlotModel = model;

        RefreshAvailableConfigs();
    }

    public ObservableCollection<ConfigItem> ConfigItems { get; } = [];

    public void RefreshAvailableConfigs()
    {
        AvailableConfigs.Clear();
        ConfigItems.Clear();
        var configs = _main.Config.DiscoverConfigFiles(Services.PathResolver.ProjectRoot);
        foreach (var c in configs)
        {
            AvailableConfigs.Add(c);
            ConfigItems.Add(new ConfigItem(System.IO.Path.GetFileName(c), c));
        }
        if (AvailableConfigs.Count > 0 && string.IsNullOrEmpty(SelectedConfig))
            SelectedConfig = AvailableConfigs[0];
    }

    partial void OnSelectedConfigChanged(string value)
    {
        if (string.IsNullOrEmpty(value) || !System.IO.File.Exists(value)) return;
        var cfg = _main.Config.Load(value);
        ConfigPopulation = cfg.PopulationSize.ToString();
        ConfigGenerations = cfg.Generations.ToString();
        ConfigEvalWindow = $"{cfg.EvalWindowHours}h (x{cfg.EvalWindowCount})";
        ConfigSeed = cfg.RunSeed.ToString();
        TotalGens = cfg.Generations;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartTraining()
    {
        if (string.IsNullOrEmpty(SelectedConfig)) return;

        var cfg = _main.Config.Load(SelectedConfig);
        _trainingService = new TrainingService(cfg, OnGenerationComplete);
        IsRunning = true;
        IsComplete = false;
        _startTime = DateTime.UtcNow;
        CurrentGen = 0;
        TotalGens = cfg.Generations;
        GenerationLog.Clear();
        _bestSeries.Points.Clear();
        _meanSeries.Points.Clear();
        _valSeries.Points.Clear();
        WalkForwardPasses = 0;
        WalkForwardOffset = "0h";
        StallCounter = "0/50";
        LastValidation = "—";
        foreach (var axis in FitnessPlotModel.Axes)
        {
            axis.Minimum = double.NaN;
            axis.Maximum = double.NaN;
        }
        FitnessPlotModel.ResetAllAxes();
        FitnessPlotModel.InvalidatePlot(true);

        _main.UpdateTrainingSession(true, $"Training | Gen 0/{TotalGens}");
        _main.Sessions.RecordEvent("TrainingStarted",
            $"Started with {cfg.PopulationSize} pop, {cfg.Generations} gens", cfg.OutputDirectory);
        _main.Notifications.Show("Training Started",
            $"Population: {cfg.PopulationSize}, Generations: {cfg.Generations}", NotificationType.Info);

        try
        {
            await _trainingService.RunAsync();

            IsComplete = true;
            _main.Sessions.RecordEvent("TrainingComplete",
                $"Completed {CurrentGen} gens. Best fitness: {BestFitness}", cfg.OutputDirectory);
            _main.Notifications.Show("Training Complete",
                $"{CurrentGen} generations. Best Sharpe: {BestSharpe}", NotificationType.Success,
                "View Results", () => _main.NavigateToCommand.Execute("Genomes"));
            _main.Dashboard.RefreshGenomeInfo();
        }
        catch (Exception ex)
        {
            _main.Notifications.Show("Training Error", ex.Message, NotificationType.Error);
        }
        finally
        {
            IsRunning = false;
            _main.UpdateTrainingSession(false);
            StartTrainingCommand.NotifyCanExecuteChanged();
            StopTrainingCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopTraining()
    {
        _trainingService?.Stop();
        _main.Notifications.Show("Training Stopped",
            $"Stopped at Gen {CurrentGen}. Last checkpoint saved.", NotificationType.Warning);
    }

    private bool CanStop() => IsRunning;

    private void OnGenerationComplete(GenerationReportData data)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            CurrentGen = data.Generation;
            Progress = TotalGens > 0 ? (double)CurrentGen / TotalGens * 100 : 0;

            var elapsed = DateTime.UtcNow - _startTime;
            ElapsedTime = FormatTime(elapsed);
            if (CurrentGen > 0)
            {
                var perGen = elapsed / CurrentGen;
                var remaining = perGen * (TotalGens - CurrentGen);
                EtaTime = FormatTime(remaining);
            }

            BestFitness = data.BestFitness.ToString("F2");
            BestSharpe = data.BestSharpe.ToString("F2");
            BestReturn = data.BestReturn.ToString("P1");
            BestTrades = data.BestTrades.ToString();
            BestWinRate = data.BestWinRate.ToString("P0");
            BestSubstrate = data.Substrate;

            _bestSeries.Points.Add(new DataPoint(data.Generation, data.BestFitness));
            _meanSeries.Points.Add(new DataPoint(data.Generation, data.MeanFitness));
            if (data.ValidationFitness.HasValue)
                _valSeries.Points.Add(new ScatterPoint(data.Generation, data.ValidationFitness.Value));
            FitnessPlotModel.InvalidatePlot(true);

            if (data.WalkForwardStatus == "PASSED") WalkForwardPasses++;
            WalkForwardOffset = $"{data.WalkForwardOffsetHours}h";
            StallCounter = $"{data.StallCount}/50";
            if (data.ValidationFitness.HasValue)
                LastValidation = $"{data.ValidationFitness.Value:F4} ({data.WalkForwardStatus ?? "—"})";

            _main.BestFitnessText = $"Best: {data.BestFitness:F2} | Sharpe: {data.BestSharpe:F2}";

            var row = new GenerationRow(
                data.Generation, data.BestFitness, data.MeanFitness, data.BestSharpe,
                data.BestReturn.ToString("P1"), data.BestWinRate.ToString("P0"),
                data.BestTrades, data.SpeciesCount,
                data.ValidationFitness?.ToString("F4") ?? "",
                data.WalkForwardStatus ?? "");
            GenerationLog.Add(row);

            _main.UpdateTrainingSession(true, $"Training | Gen {CurrentGen}/{TotalGens}");

            StartTrainingCommand.NotifyCanExecuteChanged();
            StopTrainingCommand.NotifyCanExecuteChanged();
        });
    }

    private static string FormatTime(TimeSpan ts) =>
        ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m" : $"{ts.Minutes}m {ts.Seconds:D2}s";
}
