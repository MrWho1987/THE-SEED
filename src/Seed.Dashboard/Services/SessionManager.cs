using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;

namespace Seed.Dashboard.Services;

public record SessionEvent(string Type, string Description, DateTimeOffset Timestamp, string? OutputDir = null);

public class SessionManager
{
    public ObservableCollection<SessionEvent> RecentEvents { get; } = [];

    private readonly string _historyPath;

    public SessionManager()
    {
        _historyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sessions.jsonl");
        LoadHistory();
    }

    public void RecordEvent(string type, string description, string? outputDir = null)
    {
        var evt = new SessionEvent(type, description, DateTimeOffset.UtcNow, outputDir);
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            RecentEvents.Insert(0, evt);
            if (RecentEvents.Count > 100)
                RecentEvents.RemoveAt(RecentEvents.Count - 1);
        });

        try
        {
            var json = JsonSerializer.Serialize(evt);
            File.AppendAllText(_historyPath, json + "\n");
        }
        catch { /* non-critical */ }
    }

    private void LoadHistory()
    {
        if (!File.Exists(_historyPath)) return;
        try
        {
            var lines = File.ReadAllLines(_historyPath);
            foreach (var line in lines.Reverse().Take(50))
            {
                var evt = JsonSerializer.Deserialize<SessionEvent>(line);
                if (evt != null) RecentEvents.Add(evt);
            }
        }
        catch { /* corrupt history is non-fatal */ }
    }
}
