using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using Seed.Dashboard.Services;
using Seed.Market;

namespace Seed.Dashboard.ViewModels;

public record AnalysisExperimentRecord(string Mode, DateTime Date, string Summary);

public record AnalysisModeRow(string Key, string Title, string Blurb);

public partial class AnalysisViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly GenomeService _genomeService = new();
    private readonly AnalysisService _analysisService = new();
    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _selectedGenome = "";
    [ObservableProperty] private string _selectedMode = "Compare";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private string _interpretationText = "";
    [ObservableProperty] private PlotModel _resultsPlotModel;

    public ObservableCollection<string> AvailableGenomes { get; } = [];
    public ObservableCollection<AnalysisExperimentRecord> PastExperiments { get; } = [];

    public List<AnalysisModeRow> ModeOptions { get; } =
    [
        new("Compare", "Compare",
            "Evolved agent vs Buy&Hold, SMA, Random, Mean-Reversion with p-values."),
        new("Ablation", "Ablation",
            "Disable brain components (learning, curiosity, homeostasis, etc.) one at a time."),
        new("StressTest", "Stress Test",
            "Evaluate robustness under 1x\u20135x fee/slippage multipliers."),
        new("MonteCarlo", "Monte Carlo",
            "Bootstrap 10K trade resamples for 95% confidence intervals."),
        new("NeuroAblation", "Neuro Ablation",
            "Test each neuromodulator channel (reward, pain, curiosity) independently.")
    ];

    public AnalysisViewModel(MainViewModel main)
    {
        _main = main;
        _resultsPlotModel = CreateEmptyPlot("Results");
        RefreshGenomes();
    }

    [RelayCommand]
    private void SelectMode(string mode) => SelectedMode = mode;

    [RelayCommand]
    public void RefreshGenomes()
    {
        AvailableGenomes.Clear();
        var dirs = PathResolver.DiscoverOutputDirs();

        foreach (var dir in dirs)
        {
            foreach (var p in _genomeService.GetGenomeDropdownItems(dir))
            {
                if (!AvailableGenomes.Contains(p))
                    AvailableGenomes.Add(p);
            }
        }

        if (AvailableGenomes.Count > 0 && string.IsNullOrEmpty(SelectedGenome))
            SelectedGenome = AvailableGenomes[0];
    }

    [RelayCommand(CanExecute = nameof(CanRunAnalysis))]
    private async Task RunAnalysis()
    {
        if (string.IsNullOrWhiteSpace(SelectedGenome)) return;

        IsRunning = true;
        HasResults = false;
        ResultText = "";
        InterpretationText = "";
        ResultsPlotModel = CreateEmptyPlot("Running\u2026");

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        var mode = SelectedMode;
        var genomePath = SelectedGenome;
        var config = _main.Config.CurrentConfig;

        var execMode = mode switch
        {
            "Compare" => ExecutionMode.Compare,
            "Ablation" => ExecutionMode.Ablation,
            "StressTest" => ExecutionMode.StressTest,
            "MonteCarlo" => ExecutionMode.MonteCarlo,
            "NeuroAblation" => ExecutionMode.NeuroAblation,
            _ => ExecutionMode.Compare
        };

        try
        {
            var result = await _analysisService.RunAnalysisAsync(config, genomePath, execMode, ct);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ResultText = result.Stdout;

                if (result.ExitCode != 0)
                {
                    InterpretationText = $"Process exited with code {result.ExitCode}.\n{result.Stderr}";
                    ResultsPlotModel = CreateEmptyPlot("Error");
                    HasResults = true;
                    _main.Notifications.Show("Analysis failed",
                        $"Process exited with code {result.ExitCode}.", NotificationType.Warning);
                    return;
                }

                if (result.Metrics != null)
                {
                    ResultsPlotModel = BuildChartFromMetrics(mode, result.Metrics);
                    InterpretationText = GenerateInterpretation(mode, result.Metrics);
                }
                else
                {
                    ResultsPlotModel = CreateEmptyPlot("No experiment data");
                    InterpretationText = "Analysis completed but no experiment JSON was found. " +
                                         "Check that the output directory is correct.";
                }

                HasResults = true;
                PastExperiments.Insert(0,
                    new AnalysisExperimentRecord(mode, DateTime.Now,
                        $"Genome: {Path.GetFileName(genomePath)}"));
                _main.Notifications.Show("Analysis complete",
                    $"{mode} finished successfully.", NotificationType.Success);
            });
        }
        catch (OperationCanceledException)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ResultText = "Analysis cancelled by user.";
                InterpretationText = "";
                HasResults = false;
                _main.Notifications.Show("Analysis cancelled",
                    "The analysis run was stopped.", NotificationType.Info);
            });
        }
        catch (Exception ex)
        {
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ResultText = ex.ToString();
                InterpretationText = "Run failed. Check the error details above.";
                HasResults = true;
                _main.Notifications.Show("Analysis failed", ex.Message, NotificationType.Warning);
            });
        }
        finally
        {
            IsRunning = false;
            RunAnalysisCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void StopAnalysis() => _cts?.Cancel();

    private bool CanRunAnalysis() => !IsRunning && !string.IsNullOrWhiteSpace(SelectedGenome);

    partial void OnIsRunningChanged(bool value) => RunAnalysisCommand.NotifyCanExecuteChanged();

    partial void OnSelectedGenomeChanged(string value) => RunAnalysisCommand.NotifyCanExecuteChanged();

    // ── Chart builders ──────────────────────────────────────────────────────

    private PlotModel BuildChartFromMetrics(string mode, Dictionary<string, JsonElement> metrics) =>
        mode switch
        {
            "Compare" => BuildCompareChart(metrics),
            "Ablation" => BuildPrefixBarChart("Component Ablation \u2014 Fitness",
                "Fitness", metrics, "ablation_", OxyColor.FromRgb(0xF5, 0x9E, 0x0B), "baseline"),
            "StressTest" => BuildPrefixBarChart("Stress Test \u2014 Fitness by Cost Multiplier",
                "Fitness", metrics, "stress_", OxyColor.FromRgb(0xFF, 0x38, 0x64)),
            "MonteCarlo" => BuildMonteCarloChart(metrics),
            "NeuroAblation" => BuildPrefixBarChart("Neuro Ablation \u2014 Fitness by Channel",
                "Fitness", metrics, "neuro_", OxyColor.FromRgb(0xA7, 0x8B, 0xFA)),
            _ => CreateEmptyPlot("Unknown mode")
        };

    private PlotModel BuildCompareChart(Dictionary<string, JsonElement> metrics)
    {
        var model = CreateEmptyPlot("Evolved Agent Mean Fitness");
        double evolved = Val(metrics, "evolvedMeanFitness");
        int windows = (int)Val(metrics, "windows");

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left, Title = "Mean Fitness", Minimum = 0,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        var cat = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        };
        cat.Labels.Add($"Evolved ({windows}w)");
        model.Axes.Add(cat);

        var bar = new BarSeries
        {
            FillColor = OxyColor.FromRgb(0x00, 0xF6, 0xA1),
            StrokeColor = OxyColor.FromRgb(0x00, 0xC4, 0x82),
            StrokeThickness = 1
        };
        bar.Items.Add(new BarItem(evolved));
        model.Series.Add(bar);
        return model;
    }

    private PlotModel BuildPrefixBarChart(string title, string yLabel,
        Dictionary<string, JsonElement> metrics, string prefix, OxyColor barColor,
        string? baselineKey = null)
    {
        var model = CreateEmptyPlot(title);
        var items = metrics
            .Where(kv => kv.Key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(kv => (Label: kv.Key[prefix.Length..], Value: ElVal(kv.Value)))
            .ToList();

        if (items.Count == 0) return model;

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left, Title = yLabel,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        var cat = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8),
            Angle = -30
        };

        var accentColor = OxyColor.FromRgb(0x00, 0xF6, 0xA1);
        var bar = new BarSeries
        {
            FillColor = barColor,
            StrokeColor = OxyColor.FromArgb(200, barColor.R, barColor.G, barColor.B),
            StrokeThickness = 1
        };

        if (baselineKey != null && metrics.TryGetValue(baselineKey, out var bl))
        {
            cat.Labels.Add("Baseline");
            bar.Items.Add(new BarItem(ElVal(bl)) { Color = accentColor });
        }

        foreach (var item in items)
        {
            cat.Labels.Add(item.Label);
            bar.Items.Add(new BarItem(item.Value));
        }

        model.Axes.Add(cat);
        model.Series.Add(bar);
        return model;
    }

    private PlotModel BuildMonteCarloChart(Dictionary<string, JsonElement> metrics)
    {
        var model = CreateEmptyPlot("Monte Carlo \u2014 Return Confidence Interval");
        double p5 = Val(metrics, "ci_p5");
        double median = Val(metrics, "ci_median");
        double p95 = Val(metrics, "ci_p95");

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left, Title = "Return",
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        });
        var cat = new CategoryAxis
        {
            Position = AxisPosition.Bottom,
            TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8)
        };
        cat.Labels.Add("5th pct");
        cat.Labels.Add("Median");
        cat.Labels.Add("95th pct");
        model.Axes.Add(cat);

        var bar = new BarSeries { StrokeThickness = 1 };
        bar.Items.Add(new BarItem(p5)
        {
            Color = p5 < 0 ? OxyColor.FromRgb(0xFF, 0x38, 0x64) : OxyColor.FromRgb(0x00, 0xF6, 0xA1)
        });
        bar.Items.Add(new BarItem(median) { Color = OxyColor.FromRgb(0x3B, 0x82, 0xF6) });
        bar.Items.Add(new BarItem(p95) { Color = OxyColor.FromRgb(0x00, 0xF6, 0xA1) });
        model.Series.Add(bar);
        return model;
    }

    // ── Interpretation ──────────────────────────────────────────────────────

    private static string GenerateInterpretation(string mode, Dictionary<string, JsonElement> m)
    {
        switch (mode)
        {
            case "Compare":
            {
                double evolved = Val(m, "evolvedMeanFitness");
                int windows = (int)Val(m, "windows");
                return $"Evolved agent achieved {evolved:F4} mean fitness across {windows} " +
                       "evaluation windows. See text output above for per-strategy comparison " +
                       "with p-values and Cohen\u2019s d effect sizes.";
            }
            case "Ablation":
            {
                double baseline = Val(m, "baseline");
                var parts = m
                    .Where(kv => kv.Key.StartsWith("ablation_", StringComparison.Ordinal))
                    .Select(kv => (Name: kv.Key["ablation_".Length..],
                                   Delta: ElVal(kv.Value) - baseline))
                    .OrderBy(x => x.Delta)
                    .ToList();
                if (parts.Count == 0) return "No ablation data found.";
                var best = parts.First();
                var least = parts.Last();
                return $"Baseline fitness: {baseline:F4}. Most impactful component: " +
                       $"{best.Name} (\u0394 = {best.Delta:+0.0000;-0.0000}). " +
                       $"Least impactful: {least.Name} ({least.Delta:+0.0000;-0.0000}). " +
                       "Large negative deltas mean the component is helping; positive deltas " +
                       "suggest the component may be adding noise.";
            }
            case "StressTest":
            {
                var pts = m
                    .Where(kv => kv.Key.StartsWith("stress_", StringComparison.Ordinal))
                    .Select(kv => (Label: kv.Key["stress_".Length..], Fitness: ElVal(kv.Value)))
                    .OrderBy(x => x.Label)
                    .ToList();
                if (pts.Count < 2) return "Insufficient stress data.";
                double first = pts[0].Fitness;
                double worst = pts[^1].Fitness;
                double deg = first > 0 ? (1 - worst / first) * 100 : 0;
                return $"At baseline costs fitness = {first:F4}. At max stress " +
                       $"({pts[^1].Label}) fitness = {worst:F4} ({deg:F1}% degradation). " +
                       (worst > 0 ? "Strategy remains profitable under extreme costs."
                                  : "Strategy becomes unprofitable \u2014 edge is thin.");
            }
            case "MonteCarlo":
            {
                double p5 = Val(m, "ci_p5");
                double median = Val(m, "ci_median");
                double p95 = Val(m, "ci_p95");
                int trades = (int)Val(m, "trades");
                return $"Based on {trades} trades bootstrapped 10,000 times: 95% CI " +
                       $"[{p5:F2}, {p95:F2}], median {median:F2}. " +
                       (p5 > 0 ? "Even at the 5th percentile P&L is positive \u2014 " +
                                 "statistically robust."
                               : $"At the 5th percentile P&L is negative ({p5:F2}) \u2014 " +
                                 "meaningful downside risk exists.");
            }
            case "NeuroAblation":
            {
                var channels = m
                    .Where(kv => kv.Key.StartsWith("neuro_", StringComparison.Ordinal))
                    .Select(kv => (Name: kv.Key["neuro_".Length..], Fitness: ElVal(kv.Value)))
                    .ToList();
                if (channels.Count < 2) return "Insufficient neuro-ablation data.";
                double baseline = channels[0].Fitness;
                var ranked = channels.Skip(1)
                    .Select(c => (c.Name, Delta: c.Fitness - baseline))
                    .OrderBy(x => x.Delta)
                    .ToList();
                var top = ranked.First();
                return $"Baseline (all channels): {baseline:F4}. Most critical: " +
                       $"{top.Name} (\u0394 = {top.Delta:+0.0000;-0.0000}). " +
                       "Channels with large negative deltas are essential; positive deltas " +
                       "indicate the channel may be adding noise.";
            }
            default:
                return "Unknown analysis mode.";
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private PlotModel CreateEmptyPlot(string title) => new()
    {
        Title = title,
        PlotAreaBorderColor = OxyColor.FromRgb(0x25, 0x28, 0x30),
        TextColor = OxyColor.FromRgb(0x94, 0xA3, 0xB8),
        TitleColor = OxyColor.FromRgb(0xE2, 0xE8, 0xF0),
        Background = OxyColors.Transparent,
        PlotAreaBackground = OxyColors.Transparent
    };

    private static double Val(Dictionary<string, JsonElement> m, string key) =>
        m.TryGetValue(key, out var el) && el.TryGetDouble(out var d) ? d : 0;

    private static double ElVal(JsonElement el) =>
        el.TryGetDouble(out var d) ? d : 0;
}
