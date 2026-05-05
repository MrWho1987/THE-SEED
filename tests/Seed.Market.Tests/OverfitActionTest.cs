using System.Text.Json;

namespace Seed.Market.Tests;

/// <summary>
/// S3 — OverfitAction enum routes the in-training OVERFIT detection block. Default None
/// preserves legacy log-only behavior; Halt halts; AdvanceWindow advances the walk-forward
/// offset and resets stall + decline counters. The action logic itself lives inside
/// Program.cs's training loop and is exercised by smoke runs; this file pins the enum
/// surface area: default, JSON roundtrip, recognized values.
/// </summary>
public class OverfitActionTest
{
    [Fact]
    public void Default_IsNone()
    {
        Assert.Equal(OverfitAction.None, MarketConfig.Default.OverfitAction);
    }

    [Fact]
    public void Json_RoundTrip_PreservesEnumByName()
    {
        var cfg = MarketConfig.Default with { OverfitAction = OverfitAction.AdvanceWindow };
        string json = JsonSerializer.Serialize(cfg, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });
        Assert.Contains("AdvanceWindow", json);

        var cfg2 = JsonSerializer.Deserialize<MarketConfig>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        });
        Assert.NotNull(cfg2);
        Assert.Equal(OverfitAction.AdvanceWindow, cfg2!.OverfitAction);
    }

    [Fact]
    public void EnumHasThreeRecognizedValues()
    {
        var values = Enum.GetValues<OverfitAction>();
        Assert.Equal(3, values.Length);
        Assert.Contains(OverfitAction.None, values);
        Assert.Contains(OverfitAction.Halt, values);
        Assert.Contains(OverfitAction.AdvanceWindow, values);
    }
}
