using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Seed.Dashboard.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private string _workflowStep = "Configure";
    [ObservableProperty] private bool _hasGenome;
    [ObservableProperty] private string _bestGenomeSource = "";
    [ObservableProperty] private string _bestGenomePath = "";
    [ObservableProperty] private string _bestFitness = "—";
    [ObservableProperty] private string _bestSharpe = "—";
    [ObservableProperty] private string _bestReturn = "—";
    [ObservableProperty] private string _bestTrades = "—";
    [ObservableProperty] private string _bestWinRate = "—";
    [ObservableProperty] private bool _showWelcome;

    public DashboardViewModel(MainViewModel main)
    {
        _main = main;
        _showWelcome = !System.IO.File.Exists(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".seed_welcomed"));
        RefreshGenomeInfo();
    }

    public void RefreshGenomeInfo()
    {
        string[] genomeFiles = ["best_market_genome.json", "best_training_genome.json"];

        foreach (var dir in Services.PathResolver.DiscoverOutputDirs())
        {
            foreach (var gf in genomeFiles)
            {
                var path = System.IO.Path.Combine(dir, gf);
                if (!System.IO.File.Exists(path)) continue;

                HasGenome = true;
                var dirName = System.IO.Path.GetFileName(dir);
                BestGenomeSource = gf.Contains("training") ? $"{dirName} (training best)" : dirName;
                BestGenomePath = path;

                var scores = TryReadScores(System.IO.Path.Combine(dir, "genome_scores.json"));

                try
                {
                    var json = System.IO.File.ReadAllText(path);
                    var genome = Seed.Genetics.SeedGenome.FromJson(json);

                    if (scores != null)
                    {
                        BestFitness = $"{scores.Value.Fitness:F2}";
                        BestSharpe = $"{genome.Dev.SubstrateWidth}x{genome.Dev.SubstrateHeight}x{genome.Dev.SubstrateLayers}";
                        BestReturn = $"Gen {scores.Value.Gens}";
                        BestTrades = $"{genome.Cppn.Nodes.Count}n / {genome.Cppn.Connections.Count}c";
                        BestWinRate = scores.Value.ValFitness > float.MinValue
                            ? $"{scores.Value.ValFitness:F4}" : "—";
                    }
                    else
                    {
                        BestFitness = $"{genome.Cppn.Nodes.Count}n / {genome.Cppn.Connections.Count}c";
                        BestSharpe = $"{genome.Dev.SubstrateWidth}x{genome.Dev.SubstrateHeight}x{genome.Dev.SubstrateLayers}";
                        BestReturn = $"Eta: {genome.Learn.Eta:F3}";
                        BestTrades = "—";
                        BestWinRate = "—";
                    }
                }
                catch { }
                UpdateWorkflowStep();
                return;
            }
        }

        HasGenome = false;
        BestGenomeSource = "";
        UpdateWorkflowStep();
    }

    private static (float Fitness, float ValFitness, int Gens)? TryReadScores(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        try
        {
            var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(path));
            float fit = doc.RootElement.TryGetProperty("bestFitness", out var f) ? f.GetSingle() : 0;
            float val = doc.RootElement.TryGetProperty("bestValFitness", out var v) ? v.GetSingle() : float.MinValue;
            int gens = doc.RootElement.TryGetProperty("generationsCompleted", out var g) ? g.GetInt32() : 0;
            return (fit, val, gens);
        }
        catch { return null; }
    }

    [RelayCommand]
    private void DismissWelcome()
    {
        ShowWelcome = false;
        try
        {
            System.IO.File.WriteAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".seed_welcomed"), "1");
        }
        catch { }
    }

    [RelayCommand]
    private void NavigateToTraining() => _main.NavigateToCommand.Execute("Train");

    [RelayCommand]
    private void NavigateToSettings() => _main.NavigateToCommand.Execute("Settings");

    [RelayCommand]
    private void NavigateToAnalysis() => _main.NavigateToCommand.Execute("Analyze");

    [RelayCommand]
    private void DeployBestToPaper()
    {
        if (!string.IsNullOrEmpty(BestGenomePath) && System.IO.File.Exists(BestGenomePath))
        {
            _main.PaperTrading.SelectedGenome = BestGenomePath;
            _main.PaperTrading.RefreshGenomes();
        }
        _main.NavigateToCommand.Execute("Trade");
    }

    public void UpdateWorkflowStep()
    {
        if (_main.IsPaperRunning) WorkflowStep = "Deploy";
        else if (_main.IsTrainingRunning) WorkflowStep = "Train";
        else if (HasGenome) WorkflowStep = "Evaluate";
        else WorkflowStep = "Configure";
    }
}
