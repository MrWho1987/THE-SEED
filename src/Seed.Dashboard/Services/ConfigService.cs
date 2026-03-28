using System.IO;
using Seed.Market;

namespace Seed.Dashboard.Services;

public class ConfigService
{
    public MarketConfig CurrentConfig { get; private set; } = MarketConfig.Default;
    public string? CurrentConfigPath { get; private set; }

    public string[] DiscoverConfigFiles(string rootDir)
    {
        if (!Directory.Exists(rootDir)) return [];
        return Directory.GetFiles(rootDir, "market-config*.json")
            .OrderBy(f => f)
            .ToArray();
    }

    public MarketConfig Load(string path)
    {
        CurrentConfig = MarketConfig.Load(path);
        CurrentConfigPath = path;
        return CurrentConfig;
    }

    public void Save(string path)
    {
        CurrentConfig.Save(path);
        CurrentConfigPath = path;
    }

    public void UpdateConfig(MarketConfig config)
    {
        CurrentConfig = config;
    }
}
