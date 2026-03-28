using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Seed.Dashboard.Services;

public enum NotificationType { Success, Warning, Error, Info }

public partial class NotificationItem : ObservableObject
{
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private NotificationType _type;
    [ObservableProperty] private string _actionLabel = "";
    [ObservableProperty] private Action? _action;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public class NotificationService
{
    public ObservableCollection<NotificationItem> ActiveToasts { get; } = [];

    private readonly System.Windows.Threading.DispatcherTimer _dismissTimer;

    public NotificationService()
    {
        _dismissTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _dismissTimer.Tick += (_, _) => DismissExpired();
        _dismissTimer.Start();
    }

    public void Show(string title, string message, NotificationType type = NotificationType.Info,
        string actionLabel = "", Action? action = null)
    {
        var item = new NotificationItem
        {
            Title = title,
            Message = message,
            Type = type,
            ActionLabel = actionLabel,
            Action = action,
            Timestamp = DateTimeOffset.UtcNow
        };

        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            ActiveToasts.Insert(0, item);
            if (ActiveToasts.Count > 5)
                ActiveToasts.RemoveAt(ActiveToasts.Count - 1);
        });
    }

    public void Dismiss(NotificationItem item)
    {
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => ActiveToasts.Remove(item));
    }

    private void DismissExpired()
    {
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(-8);
        for (int i = ActiveToasts.Count - 1; i >= 0; i--)
        {
            if (ActiveToasts[i].Timestamp < cutoff)
                ActiveToasts.RemoveAt(i);
        }
    }
}
