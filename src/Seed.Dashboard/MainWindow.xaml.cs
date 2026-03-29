using System.Windows;

namespace Seed.Dashboard;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void BtnClose_Click(object sender, RoutedEventArgs e) =>
        Close();

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
