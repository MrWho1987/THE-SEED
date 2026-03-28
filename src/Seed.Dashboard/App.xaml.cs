using System.Windows;

namespace Seed.Dashboard;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Unexpected error: {args.Exception.Message}", "The Seed", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
    }
}
