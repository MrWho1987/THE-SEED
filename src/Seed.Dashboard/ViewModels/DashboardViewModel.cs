using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Seed.Dashboard.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    [ObservableProperty] private string _workflowStep = "Configure";
    [ObservableProperty] private bool _hasGenome;
    [ObservableProperty] private string _bestGenomeSource = "";
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
        var dirs = new[] { "output_deep", "output_deep_v2", "output_market" };
        foreach (var dir in dirs)
        {
            var path = System.IO.Path.Combine(Environment.CurrentDirectory, dir, "best_market_genome.json");
            if (!System.IO.File.Exists(path))
                path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir, "best_market_genome.json");
            if (System.IO.File.Exists(path))
            {
                HasGenome = true;
                BestGenomeSource = dir;
                try
                {
                    var json = System.IO.File.ReadAllText(path);
                    var genome = Seed.Genetics.SeedGenome.FromJson(json);
                    BestFitness = $"{genome.Cppn.Nodes.Count}n / {genome.Cppn.Connections.Count}c";
                    BestSharpe = $"{genome.Dev.SubstrateWidth}x{genome.Dev.SubstrateHeight}x{genome.Dev.SubstrateLayers}";
                    BestReturn = $"Eta: {genome.Learn.Eta:F3}";
                    BestTrades = $"CPPN: {genome.Cppn.Nodes.Count}";
                    BestWinRate = $"Critical: {genome.Learn.CriticalPeriodTicks}";
                }
                catch { }
                return;
            }
        }

        foreach (var dir in dirs)
        {
            var path = System.IO.Path.Combine(Environment.CurrentDirectory, dir, "best_training_genome.json");
            if (!System.IO.File.Exists(path))
                path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, dir, "best_training_genome.json");
            if (System.IO.File.Exists(path))
            {
                HasGenome = true;
                BestGenomeSource = $"{dir} (training best)";
                try
                {
                    var json = System.IO.File.ReadAllText(path);
                    var genome = Seed.Genetics.SeedGenome.FromJson(json);
                    BestFitness = $"{genome.Cppn.Nodes.Count}n / {genome.Cppn.Connections.Count}c";
                    BestSharpe = $"{genome.Dev.SubstrateWidth}x{genome.Dev.SubstrateHeight}x{genome.Dev.SubstrateLayers}";
                    BestReturn = $"Eta: {genome.Learn.Eta:F3}";
                    BestTrades = $"CPPN: {genome.Cppn.Nodes.Count}";
                    BestWinRate = $"Critical: {genome.Learn.CriticalPeriodTicks}";
                }
                catch { }
                return;
            }
        }

        HasGenome = false;
        BestGenomeSource = "";
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
