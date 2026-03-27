using Seed.Market.Signals;

namespace Seed.Market.Tests;

public class SignalNormalizerTests
{
    [Fact]
    public void OutputIsClippedToRange()
    {
        var norm = new SignalNormalizer(lookbackTicks: 10);
        var raw = new float[SignalIndex.Count];

        // Seed with baseline
        for (int i = 0; i < 20; i++)
        {
            raw[0] = 50000f;
            norm.Normalize(raw, DateTimeOffset.UtcNow, i);
        }

        // Spike
        raw[0] = 100000f;
        var snap = norm.Normalize(raw, DateTimeOffset.UtcNow, 20);

        Assert.InRange(snap[0], -1f, 1f);
    }

    [Fact]
    public void NoNaNInOutput()
    {
        var norm = new SignalNormalizer();
        var raw = new float[SignalIndex.Count];
        raw[5] = float.NaN;
        raw[10] = float.PositiveInfinity;

        var snap = norm.Normalize(raw, DateTimeOffset.UtcNow, 0);

        Assert.False(snap.HasNaN());
    }

    [Fact]
    public void FirstCallSeedsStats()
    {
        var norm = new SignalNormalizer();
        var raw = new float[SignalIndex.Count];
        raw[0] = 50000f;

        var snap = norm.Normalize(raw, DateTimeOffset.UtcNow, 0);

        // First call: delta from mean is 0, so normalized should be 0
        Assert.Equal(0f, snap[0]);
    }

    [Fact]
    public void ResetClearsState()
    {
        var norm = new SignalNormalizer();
        var raw = new float[SignalIndex.Count];
        raw[0] = 50000f;
        norm.Normalize(raw, DateTimeOffset.UtcNow, 0);
        norm.Normalize(raw, DateTimeOffset.UtcNow, 1);

        norm.Reset();

        raw[0] = 100000f;
        var snap = norm.Normalize(raw, DateTimeOffset.UtcNow, 2);
        // After reset, first call seeds again so delta = 0
        Assert.Equal(0f, snap[0]);
    }
}
