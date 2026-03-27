namespace Seed.Market.Signals;

/// <summary>
/// Rolling z-score normalizer that maps raw signal values to [-1,1].
/// Maintains per-signal running mean and variance with exponential decay.
/// </summary>
public sealed class SignalNormalizer
{
    private readonly float[] _mean;
    private readonly float[] _variance;
    private readonly float _alpha;
    private readonly float _clip;
    private bool _initialized;

    /// <param name="lookbackTicks">
    /// Effective window for exponential moving average.
    /// Alpha = 2/(lookback+1), matching EMA convention.
    /// </param>
    /// <param name="clip">Absolute value to clip normalized output at.</param>
    public SignalNormalizer(int lookbackTicks = 500, float clip = 1f)
    {
        _mean = new float[SignalIndex.Count];
        _variance = new float[SignalIndex.Count];
        _alpha = 2f / (lookbackTicks + 1f);
        _clip = clip;

        for (int i = 0; i < SignalIndex.Count; i++)
            _variance[i] = 1f;
    }

    /// <summary>
    /// Feed raw values and return normalized snapshot.
    /// First call seeds the running stats; subsequent calls update exponentially.
    /// </summary>
    public SignalSnapshot Normalize(float[] raw, DateTimeOffset timestamp, long tick)
    {
        var normalized = new float[SignalIndex.Count];

        if (!_initialized)
        {
            Array.Copy(raw, _mean, SignalIndex.Count);
            for (int i = 0; i < SignalIndex.Count; i++)
                _variance[i] = 1f;
            _initialized = true;
        }

        for (int i = 0; i < SignalIndex.Count; i++)
        {
            float x = raw[i];

            if (float.IsNaN(x) || float.IsInfinity(x))
            {
                normalized[i] = 0f;
                continue;
            }

            float delta = x - _mean[i];
            _mean[i] += _alpha * delta;
            _variance[i] = (1f - _alpha) * _variance[i] + _alpha * delta * delta;

            float std = MathF.Sqrt(_variance[i]);
            float z = std > 1e-8f ? delta / std : 0f;

            normalized[i] = MathF.Max(-_clip, MathF.Min(_clip, z));
        }

        return new SignalSnapshot(normalized, timestamp, tick);
    }

    public void Reset()
    {
        Array.Clear(_mean);
        for (int i = 0; i < SignalIndex.Count; i++)
            _variance[i] = 1f;
        _initialized = false;
    }
}
