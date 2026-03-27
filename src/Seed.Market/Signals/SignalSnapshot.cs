namespace Seed.Market.Signals;

/// <summary>
/// A single point-in-time snapshot of all 88 market signals, normalized to [-1,1].
/// This is the input vector fed to every CPPN brain on each tick.
/// </summary>
public sealed class SignalSnapshot
{
    public float[] Signals { get; }
    public DateTimeOffset Timestamp { get; init; }
    public long TickNumber { get; init; }
    public SignalHealth Health { get; init; }

    public SignalSnapshot()
    {
        Signals = new float[SignalIndex.Count];
        Timestamp = DateTimeOffset.UtcNow;
        Health = SignalHealth.Full;
    }

    public SignalSnapshot(float[] signals, DateTimeOffset timestamp, long tick, SignalHealth health = SignalHealth.Full)
    {
        if (signals.Length != SignalIndex.Count)
            throw new ArgumentException($"Expected {SignalIndex.Count} signals, got {signals.Length}");
        Signals = signals;
        Timestamp = timestamp;
        TickNumber = tick;
        Health = health;
    }

    public float this[int index]
    {
        get => Signals[index];
        set => Signals[index] = value;
    }

    public ReadOnlySpan<float> AsSpan() => Signals;

    public SignalSnapshot Clone()
    {
        var copy = new float[SignalIndex.Count];
        Array.Copy(Signals, copy, SignalIndex.Count);
        return new SignalSnapshot(copy, Timestamp, TickNumber, Health);
    }

    public bool HasNaN()
    {
        for (int i = 0; i < Signals.Length; i++)
            if (float.IsNaN(Signals[i]) || float.IsInfinity(Signals[i]))
                return true;
        return false;
    }
}

public enum SignalHealth
{
    Full,
    Partial,
    Stale
}
