using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Seed.Dashboard.Services;

namespace Seed.Dashboard.ViewModels;

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private int _selectedNavIndex;
    [ObservableProperty] private string _trainingSessionLabel = "";
    [ObservableProperty] private string _paperSessionLabel = "";
    [ObservableProperty] private bool _isTrainingRunning;
    [ObservableProperty] private bool _isPaperRunning;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private string _bestFitnessText = "";
    [ObservableProperty] private string _clockText = DateTime.Now.ToString("HH:mm");

    public DashboardViewModel Dashboard { get; }
    public TrainingViewModel Training { get; }
    public PaperTradingViewModel PaperTrading { get; }
    public GenomeLabViewModel GenomeLab { get; }
    public AnalysisViewModel Analysis { get; }
    public SettingsViewModel Settings { get; }

    public NotificationService Notifications { get; }
    public SessionManager Sessions { get; }
    public ConfigService Config { get; }

    private readonly System.Windows.Threading.DispatcherTimer _clockTimer;

    public MainViewModel()
    {
        Config = new ConfigService();
        Sessions = new SessionManager();
        Notifications = new NotificationService();

        Dashboard = new DashboardViewModel(this);
        Training = new TrainingViewModel(this);
        PaperTrading = new PaperTradingViewModel(this);
        GenomeLab = new GenomeLabViewModel(this);
        Analysis = new AnalysisViewModel(this);
        Settings = new SettingsViewModel(this);

        _clockTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => ClockText = DateTime.Now.ToString("HH:mm:ss");
        _clockTimer.Start();
    }

    [RelayCommand]
    private void NavigateTo(string view)
    {
        SelectedNavIndex = view switch
        {
            "Home" => 0,
            "Train" => 1,
            "Trade" => 2,
            "Genomes" => 3,
            "Analyze" => 4,
            "Settings" => 5,
            _ => 0
        };
    }

    public void UpdateTrainingSession(bool running, string label = "")
    {
        IsTrainingRunning = running;
        TrainingSessionLabel = label;
        Dashboard.UpdateWorkflowStep();
    }

    public void UpdatePaperSession(bool running, string label = "")
    {
        IsPaperRunning = running;
        PaperSessionLabel = label;
        Dashboard.UpdateWorkflowStep();
    }
}
