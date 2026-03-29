using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seed.Core;
using Seed.Dashboard.Services;
using Seed.Development;
using Seed.Genetics;

namespace Seed.Dashboard.ViewModels;

/// <summary>
/// Groups genome files under a root output directory for hierarchical display.
/// </summary>
public sealed class GenomeDirectoryNode
{
    public GenomeDirectoryNode(string directoryPath, IEnumerable<GenomeFileInfo> files)
    {
        DirectoryPath = directoryPath;
        FolderLabel = Path.GetFileName(directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(FolderLabel))
            FolderLabel = directoryPath;
        foreach (var f in files)
            Files.Add(f);
    }

    public string DirectoryPath { get; }
    public string FolderLabel { get; }
    public ObservableCollection<GenomeFileInfo> Files { get; } = [];
}

public partial class GenomeLabViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly GenomeService _genomeService = new();

    /// <summary>All genome JSON files from the last scan (flat list).</summary>
    public ObservableCollection<GenomeFileInfo> GenomeFiles { get; } = [];

    public GenomeLabViewModel(MainViewModel main)
    {
        _main = main;
        RefreshList();
    }

    public ObservableCollection<GenomeDirectoryNode> DirectoryTree { get; } = [];

    [ObservableProperty] private GenomeFileInfo? _selectedGenome;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _selectedSort = "Date (newest)";

    [ObservableProperty] private string _genomeIdText = "—";
    [ObservableProperty] private int _cppnNodes;
    [ObservableProperty] private int _cppnConnections;
    [ObservableProperty] private int _brainNodes;
    [ObservableProperty] private int _brainEdges;
    [ObservableProperty] private int _substrateWidth;
    [ObservableProperty] private int _substrateHeight;
    [ObservableProperty] private int _substrateLayers;

    [ObservableProperty] private int _devTopKInMin;
    [ObservableProperty] private int _devTopKInMax;
    [ObservableProperty] private int _devMaxOutMin;
    [ObservableProperty] private int _devMaxOutMax;
    [ObservableProperty] private float _devConnectionThreshold;
    [ObservableProperty] private float _devInitialWeightScale;
    [ObservableProperty] private float _devGlobalSampleRate;
    [ObservableProperty] private int _devSubstrateWidth;
    [ObservableProperty] private int _devSubstrateHeight;
    [ObservableProperty] private int _devSubstrateLayers;

    [ObservableProperty] private float _learnEta;
    [ObservableProperty] private float _learnEligibilityDecay;
    [ObservableProperty] private float _learnAlphaReward;
    [ObservableProperty] private float _learnAlphaPain;
    [ObservableProperty] private float _learnAlphaCuriosity;
    [ObservableProperty] private float _learnBetaConsolidate;
    [ObservableProperty] private float _learnGammaRecall;
    [ObservableProperty] private int _learnCriticalPeriodTicks;

    [ObservableProperty] private float _stableWeightMaxAbs;
    [ObservableProperty] private float _stableHomeostasisStrength;
    [ObservableProperty] private float _stableActivationTarget;
    [ObservableProperty] private float _stableIncomingNormEps;
    [ObservableProperty] private bool _stableEnableIncomingNormalization;

    [ObservableProperty] private string _inspectorSourcePath = "—";
    [ObservableProperty] private string _inspectorCategory = "—";
    [ObservableProperty] private string _inspectorModifiedText = "—";

    public string[] SortOptions { get; } =
    [
        "Date (newest)",
        "Date (oldest)",
        "Name (A-Z)",
        "Name (Z-A)",
        "Category (A-Z)"
    ];

    partial void OnSearchTextChanged(string value) => RebuildTree();

    partial void OnSelectedSortChanged(string value) => RebuildTree();

    public bool HasSelection => SelectedGenome is not null;

    partial void OnSelectedGenomeChanged(GenomeFileInfo? value)
    {
        LoadGenome(value);
        DeployToPaperCommand.NotifyCanExecuteChanged();
        RunAnalysisCommand.NotifyCanExecuteChanged();
        DeleteGenomeCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasSelection));
    }

    public void LoadGenome(GenomeFileInfo? file)
    {
        if (file is null)
        {
            ClearInspector();
            return;
        }

        InspectorSourcePath = file.FullPath;
        InspectorCategory = file.Category;
        InspectorModifiedText = file.Modified.ToString("g");

        try
        {
            var json = File.ReadAllText(file.FullPath);
            SeedGenome? genome = null;

            if (file.Category == "Checkpoint" && json.Contains("\"genomeJsons\""))
            {
                var cp = Seed.Market.Backtest.CheckpointState.Load(file.FullPath);
                var restored = cp.RestorePopulation();
                if (restored.Count > 0 && restored[0] is SeedGenome sg)
                    genome = sg;
                InspectorCategory = $"Checkpoint (Gen {cp.Generation}, {restored.Count} genomes)";
            }
            else
            {
                genome = SeedGenome.FromJson(json);
            }

            if (genome is null) { ClearInspector(preservePath: true); return; }

            GenomeIdText = genome.GenomeId.ToString();
            CppnNodes = genome.Cppn.Nodes.Count;
            CppnConnections = genome.Cppn.Connections.Count;

            SubstrateWidth = genome.Dev.SubstrateWidth;
            SubstrateHeight = genome.Dev.SubstrateHeight;
            SubstrateLayers = genome.Dev.SubstrateLayers;

            ApplyDevLearnStable(genome.Dev, genome.Learn, genome.Stable);

            try
            {
                var budget = DevelopmentBudget.Default with
                {
                    HiddenWidth = genome.Dev.SubstrateWidth,
                    HiddenHeight = genome.Dev.SubstrateHeight,
                    HiddenLayers = genome.Dev.SubstrateLayers
                };
                var developer = new BrainDeveloper(88, 5);
                var graph = developer.CompileGraph(genome, budget, new DevelopmentContext(42, 0));
                BrainNodes = graph.NodeCount;
                BrainEdges = graph.EdgeCount;
            }
            catch
            {
                BrainNodes = 0;
                BrainEdges = 0;
            }
        }
        catch (Exception ex)
        {
            _main.Notifications.Show("Genome load failed", ex.Message, NotificationType.Error);
            ClearInspector(preservePath: true);
        }
    }

    private void ClearInspector(bool preservePath = false)
    {
        GenomeIdText = "—";
        CppnNodes = 0;
        CppnConnections = 0;
        BrainNodes = 0;
        BrainEdges = 0;
        SubstrateWidth = SubstrateHeight = SubstrateLayers = 0;
        DevTopKInMin = DevTopKInMax = DevMaxOutMin = DevMaxOutMax = 0;
        DevSubstrateWidth = DevSubstrateHeight = DevSubstrateLayers = 0;
        DevConnectionThreshold = DevInitialWeightScale = DevGlobalSampleRate = 0;
        LearnEta = LearnEligibilityDecay = LearnAlphaReward = LearnAlphaPain =
            LearnAlphaCuriosity = LearnBetaConsolidate = LearnGammaRecall = 0;
        LearnCriticalPeriodTicks = 0;
        StableWeightMaxAbs = StableHomeostasisStrength = StableActivationTarget = StableIncomingNormEps = 0;
        StableEnableIncomingNormalization = false;
        if (!preservePath)
        {
            InspectorSourcePath = "—";
            InspectorCategory = "—";
            InspectorModifiedText = "—";
        }
    }

    private void ApplyDevLearnStable(DevelopmentParams d, LearningParams l, StabilityParams s)
    {
        DevTopKInMin = d.TopKInMin;
        DevTopKInMax = d.TopKInMax;
        DevMaxOutMin = d.MaxOutMin;
        DevMaxOutMax = d.MaxOutMax;
        DevConnectionThreshold = d.ConnectionThreshold;
        DevInitialWeightScale = d.InitialWeightScale;
        DevGlobalSampleRate = d.GlobalSampleRate;
        DevSubstrateWidth = d.SubstrateWidth;
        DevSubstrateHeight = d.SubstrateHeight;
        DevSubstrateLayers = d.SubstrateLayers;

        LearnEta = l.Eta;
        LearnEligibilityDecay = l.EligibilityDecay;
        LearnAlphaReward = l.AlphaReward;
        LearnAlphaPain = l.AlphaPain;
        LearnAlphaCuriosity = l.AlphaCuriosity;
        LearnBetaConsolidate = l.BetaConsolidate;
        LearnGammaRecall = l.GammaRecall;
        LearnCriticalPeriodTicks = l.CriticalPeriodTicks;

        StableWeightMaxAbs = s.WeightMaxAbs;
        StableHomeostasisStrength = s.HomeostasisStrength;
        StableActivationTarget = s.ActivationTarget;
        StableIncomingNormEps = s.IncomingNormEps;
        StableEnableIncomingNormalization = s.EnableIncomingNormalization;
    }

    [RelayCommand]
    private void RefreshList()
    {
        GenomeFiles.Clear();
        foreach (var g in _genomeService.ScanGenomes(OutputDirs))
            GenomeFiles.Add(g);
        RebuildTree();
    }

    private static string[] OutputDirs => PathResolver.DiscoverOutputDirs();

    private void RebuildTree()
    {
        var q = GenomeFiles.AsEnumerable();
        var term = SearchText.Trim();
        if (term.Length > 0)
        {
            q = q.Where(g =>
                g.FileName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                g.Category.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                g.Directory.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        q = SelectedSort switch
        {
            "Date (newest)" => q.OrderByDescending(g => g.Modified).ThenBy(g => g.FileName),
            "Date (oldest)" => q.OrderBy(g => g.Modified).ThenBy(g => g.FileName),
            "Name (A-Z)" => q.OrderBy(g => g.FileName, StringComparer.OrdinalIgnoreCase),
            "Name (Z-A)" => q.OrderByDescending(g => g.FileName, StringComparer.OrdinalIgnoreCase),
            "Category (A-Z)" => q.OrderBy(g => g.Category, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(g => g.Modified),
            _ => q.OrderByDescending(g => g.Modified)
        };

        var list = q.ToList();
        DirectoryTree.Clear();
        foreach (var group in list.GroupBy(g => g.Directory))
            DirectoryTree.Add(new GenomeDirectoryNode(group.Key, group));
    }

    [RelayCommand(CanExecute = nameof(CanDeploy))]
    private void DeployToPaper(GenomeFileInfo? file)
    {
        var target = file ?? SelectedGenome;
        if (target is null || !File.Exists(target.FullPath)) return;
        SelectedGenome = target;
        _main.PaperTrading.SelectedGenome = target.FullPath;
        _main.PaperTrading.RefreshGenomes();
        _main.SelectedNavIndex = 2;
        _main.Notifications.Show("Genome selected for paper trading",
            Path.GetFileName(target.FullPath), NotificationType.Success);
    }

    private bool CanDeploy(GenomeFileInfo? file)
    {
        var target = file ?? SelectedGenome;
        return target is not null && File.Exists(target.FullPath);
    }

    [RelayCommand(CanExecute = nameof(CanAnalyze))]
    private void RunAnalysis(GenomeFileInfo? file)
    {
        var target = file ?? SelectedGenome;
        if (target is null) return;
        SelectedGenome = target;
        _main.SelectedNavIndex = 4;
        _main.Sessions.RecordEvent("AnalysisOpen",
            $"Opened Analysis with genome {target.FileName}");
        _main.Notifications.Show("Analysis",
            $"Selected genome: {target.FileName}. Analysis view is not fully wired yet.",
            NotificationType.Info);
    }

    private bool CanAnalyze(GenomeFileInfo? file) => (file ?? SelectedGenome) is not null;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private void DeleteGenome(GenomeFileInfo? target)
    {
        var file = target ?? SelectedGenome;
        if (file is null || !File.Exists(file.FullPath)) return;

        var result = System.Windows.MessageBox.Show(
            $"Delete genome file?\n\n{file.FileName}",
            "Confirm delete",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            File.Delete(file.FullPath);
            if (SelectedGenome?.FullPath == file.FullPath)
                SelectedGenome = null;
            RefreshList();
            _main.Notifications.Show("Deleted", file.FileName, NotificationType.Warning);
        }
        catch (Exception ex)
        {
            _main.Notifications.Show("Delete failed", ex.Message, NotificationType.Error);
        }
    }

    private bool CanDelete(GenomeFileInfo? target) =>
        (target ?? SelectedGenome) is not null;
}
