using System.Windows;

namespace Seed.Dashboard;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        var vm = DataContext as ViewModels.MainViewModel;
        if (vm?.IsTrainingRunning == true || vm?.IsPaperRunning == true)
        {
            var result = MessageBox.Show(
                "Active sessions are running. Close anyway? Training will stop but the last checkpoint is saved.",
                "The Seed", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnClosing(e);
    }
}
