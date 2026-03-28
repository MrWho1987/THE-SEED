using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Seed.Dashboard.Services;

namespace Seed.Dashboard.ViewModels;

public record AnalysisExperimentRecord(string Mode, DateTime Date, string Summary);

public record AnalysisModeRow(string Key, string Title, string Blurb);

public partial class AnalysisViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly GenomeService _genomeService = new();

    [ObservableProperty] private string _selectedGenome = "";
    [ObservableProperty] private string _selectedMode = "Compare";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private string _interpretationText = "";

    [ObservableProperty] private PlotModel _resultsPlotModel;

    public ObservableCollection<string> AvailableGenomes { get; } = [];
    public ObservableCollection<AnalysisExperimentRecord> PastExperiments { get; } = [];

    public List<AnalysisModeRow> ModeOptions { get; } =
    [
        new("Compare", "Compare",
            "Bar chart of strategy fitnesses (placeholder until engine is wired)."),
        new("Ablation", "Ablation",
            "Measure fitness deltas when removing feeds or components."),
        new("StressTest", "Stress test",
            "Shock scenarios: liquidity, funding, flash moves."),
        new("MonteCarlo", "Monte Carlo",
            "Distribution of outcomes from resampled returns."),
        new("NeuroAblation", "Neuro ablation",
            "Prune neural subgraphs and measure fitness impact.")
    ];

    public AnalysisViewModel(MainViewModel main)
    {
        _main = main;
        _resultsPlotModel = CreateEmptyPlot("Results");
        RefreshGenomes();
    }

    [RelayCommand]
    public void RefreshGenomes()
    {
        AvailableGenomes.Clear();
        var cfg = _main.Config.CurrentConfig;
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Environment.CurrentDirectory,
            cfg.OutputDirectory,
            Path.Combine(Environment.CurrentDirectory, cfg.OutputDirectory),
            "output_market",
            "output_deep",
            Path.Combine(Environment.CurrentDirectory, "output_market"),
            Path.Combine(Environment.CurrentDirectory, "output_deep")
        };
        foreach (var dir in dirs)
        {
            foreach (var p in _genomeService.GetGenomeDropdownItems(dir))
            {
                if (File.Exists(p) && !AvailableGenomes.Contains(p))
                    AvailableGenomes.Add(p);
            }
        }

        foreach (var fi in _genomeService.ScanGenomes(dirs.ToArray()))
        {
            if (File.Exists(fi.FullPath) && fi.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                && !AvailableGenomes.Contains(fi.FullPath))
                AvailableGenomes.Add(fi.FullPath);
        }

        if (AvailableGenomes.Count > 0 && string.IsNullOrEmpty(SelectedGenome))
            SelectedGenome = AvailableGenomes[0];
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunAnalysis()
    {
        if (string.IsNullOrWhiteSpace(SelectedGenome)) return;

        IsRunning = true;
        ResultText = "";
        InterpretationText = "";

        var mode = SelectedMode;
        var genomePath = SelectedGenome;
        var config = _main.Config.CurrentConfig;

        try
        {
            await Task.Run(() =>
            {
                if (!File.Exists(genomePath))
                    throw new FileNotFoundException("Genome file not found.", genomePath);
                _ = File.ReadAllText(genomePath);
                _ = config.OutputDirectory;
                Thread.Sleep(400);
            });

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ApplyPlaceholderResults(mode, genomePath);
                PastExperiments.Insert(0,
                    new AnalysisExperimentRecord(mode, DateTime.Now,
                        $"Genome: {Path.GetFileName(genomePath)} — placeholder run"));
                _main.Notifications.Show("Analysis complete",
                    $"{mode} finished (placeholder).", NotificationType.Success);
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ResultText = ex.ToString();
                InterpretationText = "Run failed. Check the genome path and try again.";
                _main.Notifications.Show("Analysis failed", ex.Message, NotificationType.Warning);
            });
        }
        finally
        {
            IsRunning = false;
            RunAnalysisCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRunAnalysis() => !IsRunning && !string.IsNullOrWhiteSpace(SelectedGenome);

    partial void OnIsRunningChanged(bool value) => RunAnalysisCommand.NotifyCanExecuteChanged();

    partial void OnSelectedGenomeChanged(string value) => RunAnalysisCommand.NotifyCanExecuteChanged();

    private void ApplyPlaceholderResults(string mode, string genomePath)
    {
        var name = Path.GetFileName(genomePath);
        switch (mode)
        {
            case "Compare":
                BuildCompareChart();
                ResultText =
                    $"Compare (placeholder){Environment.NewLine}" +
                    $"Genome: {name}{Environment.NewLine}" +
                    $"Strategy          Fitness{Environment.NewLine}" +
                    $"────────────────────────────{Environment.NewLine}" +
                    $"Baseline              0.812{Environment.NewLine}" +
                    $"Momentum variant      0.847{Environment.NewLine}" +
                    $"Mean-revert variant   0.791{Environment.NewLine}" +
                    $"Hybrid ensemble       0.863{Environment.NewLine}";
                InterpretationText =
                    "Placeholder compare: hybrid leads on fitness; real run will rank strategies from the loaded genome and config.";
                break;

            case "Ablation":
                BuildAblationChart();
                ResultText =
                    $"Ablation deltas vs full (placeholder){Environment.NewLine}" +
                    $"Genome: {name}{Environment.NewLine}" +
                    "Remove sentiment feed     -0.034{Environment.NewLine}" +
                    "Remove on-chain feed      -0.012{Environment.NewLine}" +
                    "Remove macro feed         -0.008{Environment.NewLine}" +
                    "Remove futures overlay    -0.021{Environment.NewLine}";
                InterpretationText =
                    "Placeholder ablation: sentiment removal hurts most; production will measure actual component contributions.";
                break;

            case "StressTest":
                BuildStressChart();
                ResultText =
                    $"Stress scenarios (placeholder){Environment.NewLine}" +
                    $"Genome: {name}{Environment.NewLine}" +
                    "Flash crash sim      portfolio -4.2%{Environment.NewLine}" +
                    "Liquidity dry-up     max DD +1.8pp{Environment.NewLine}" +
                    "Funding spike        Sharpe -0.11{Environment.NewLine}";
                InterpretationText =
                    "Placeholder stress metrics; full engine will replay shocks against the strategy.";
                break;

            case "MonteCarlo":
                BuildMonteCarloChart();
                ResultText =
                    $"Monte Carlo summary (placeholder){Environment.NewLine}" +
                    $"Genome: {name}{Environment.NewLine}" +
                    "Runs: 2,000  |  median return: +12.4%{Environment.NewLine}" +
                    "5th pct return: -6.1%   |  95th pct: +28.9%{Environment.NewLine}" +
                    "Prob. loss > 10%: 14.2%{Environment.NewLine}";
                InterpretationText =
                    "Placeholder distribution; live analysis will bootstrap returns from evaluation windows.";
                break;

            case "NeuroAblation":
                BuildNeuroAblationChart();
                ResultText =
                    $"Neuro ablation (placeholder){Environment.NewLine}" +
                    $"Genome: {name}{Environment.NewLine}" +
                    "Layer prune 10% edges    Δfitness -0.019{Environment.NewLine}" +
                    "Layer prune 25% edges    Δfitness -0.047{Environment.NewLine}" +
                    "Drop attention block     Δfitness -0.063{Environment.NewLine}";
                InterpretationText =
                    "Placeholder neural component study; implementation will slice the brain graph per genome.";
                break;

            default:
                ResultsPlotModel = CreateEmptyPlot("Results");
                ResultText = $"Unknown mode: {mode}";
                InterpretationText = "Select a supported analysis mode.";
                break;
        }

        ResultsPlotModel.InvalidatePlot(true);
    }

    private PlotModel CreateEmptyPlot(string title)
    {
        var model = new PlotModel
        {
            Title = title,
            PlotAreaBorderColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8),
            TitleColor = OxyColor.FromRgb(0xE2, 0xE8, 0xF0),
            Background = OxyColors.Transparent,
            PlotAreaBackground = OxyColors.Transparent
        };
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TicklineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        model.Axes.Add(new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TicklineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        return model;
    }

    private void BuildCompareChart()
    {
        var model = CreateEmptyPlot("Strategy fitness (placeholder)");
        model.Axes.Clear();
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Fitness",
            Minimum = 0,
            Maximum = 1,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        var cat = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        };
        cat.Labels.Add("Baseline");
        cat.Labels.Add("Momentum");
        cat.Labels.Add("MeanRev");
        cat.Labels.Add("Hybrid");
        model.Axes.Add(cat);

        var bar = new BarSeries
        {
            FillColor = OxyColor.FromRgb(0x00, 0xF6, 0xA1),
            StrokeColor = OxyColor.FromRgb(0x00, 0xC4, 0x82),
            StrokeThickness = 1
        };
        bar.Items.Add(new BarItem(0.812));
        bar.Items.Add(new BarItem(0.847));
        bar.Items.Add(new BarItem(0.791));
        bar.Items.Add(new BarItem(0.863));
        model.Series.Add(bar);
        ResultsPlotModel = model;
    }

    private void BuildAblationChart()
    {
        var model = CreateEmptyPlot("Ablation Δ fitness (placeholder)");
        model.Axes.Clear();
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Δ fitness",
            Minimum = -0.05,
            Maximum = 0,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        var cat = new CategoryAxis { Position = AxisPosition.Bottom };
        cat.Labels.Add("Sentiment");
        cat.Labels.Add("On-chain");
        cat.Labels.Add("Macro");
        cat.Labels.Add("Futures");
        model.Axes.Add(cat);

        var bar = new BarSeries
        {
            FillColor = OxyColor.FromRgb(0xF5, 0x9E, 0x0B),
            StrokeColor = OxyColor.FromRgb(0xC2, 0x7E, 0x08),
            StrokeThickness = 1
        };
        bar.Items.Add(new BarItem(-0.034));
        bar.Items.Add(new BarItem(-0.012));
        bar.Items.Add(new BarItem(-0.008));
        bar.Items.Add(new BarItem(-0.021));
        model.Series.Add(bar);
        ResultsPlotModel = model;
    }

    private void BuildStressChart()
    {
        var model = CreateEmptyPlot("Stress impact (placeholder)");
        model.Axes.Clear();
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Severity (arb.)",
            Minimum = 0,
            Maximum = 10,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        var cat = new CategoryAxis { Position = AxisPosition.Bottom };
        cat.Labels.Add("Flash crash");
        cat.Labels.Add("Liquidity");
        cat.Labels.Add("Funding");
        model.Axes.Add(cat);

        var bar = new BarSeries
        {
            FillColor = OxyColor.FromRgb(0xFF, 0x38, 0x64),
            StrokeColor = OxyColor.FromRgb(0xCC, 0x2D, 0x50),
            StrokeThickness = 1
        };
        bar.Items.Add(new BarItem(6.2));
        bar.Items.Add(new BarItem(4.5));
        bar.Items.Add(new BarItem(5.1));
        model.Series.Add(bar);
        ResultsPlotModel = model;
    }

    private void BuildMonteCarloChart()
    {
        var model = CreateEmptyPlot("Return distribution (placeholder)");
        model.Axes.Clear();
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Frequency",
            Minimum = 0,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Return %",
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });

        var line = new LineSeries
        {
            Title = "Density (mock)",
            Color = OxyColor.FromRgb(0x3B, 0x82, 0xF6),
            StrokeThickness = 2
        };
        for (var x = -20; x <= 40; x += 2)
        {
            var y = 100 * Math.Exp(-Math.Pow(x - 12, 2) / 200.0);
            line.Points.Add(new DataPoint(x, y));
        }
        model.Series.Add(line);
        ResultsPlotModel = model;
    }

    private void BuildNeuroAblationChart()
    {
        var model = CreateEmptyPlot("Neuro ablation Δ fitness (placeholder)");
        model.Axes.Clear();
        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Δ fitness",
            Minimum = -0.08,
            Maximum = 0,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        var cat = new CategoryAxis { Position = AxisPosition.Bottom };
        cat.Labels.Add("Prune 10%");
        cat.Labels.Add("Prune 25%");
        cat.Labels.Add("Drop attn.");
        model.Axes.Add(cat);

        var bar = new BarSeries
        {
            FillColor = OxyColor.FromRgb(0xA7, 0x8B, 0xFA),
            StrokeColor = OxyColor.FromRgb(0x7C, 0x6A, 0xD6),
            StrokeThickness = 1
        };
        bar.Items.Add(new BarItem(-0.019));
        bar.Items.Add(new BarItem(-0.047));
        bar.Items.Add(new BarItem(-0.063));
        model.Series.Add(bar);
        ResultsPlotModel = model;
    }
}
