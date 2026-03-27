namespace Seed.Market.Trading;

/// <summary>
/// Maintains a sliding window of equity values for real-time performance monitoring.
/// </summary>
public sealed class RollingMetrics
{
    private readonly Queue<float> _equities = new();
    private readonly int _window;

    public RollingMetrics(int window = 100)
    {
        _window = window;
    }

    public void Add(float equity)
    {
        _equities.Enqueue(equity);
        if (_equities.Count > _window)
            _equities.Dequeue();
    }

    public float RollingSharpe
    {
        get
        {
            if (_equities.Count < 2) return 0f;
            var arr = _equities.ToArray();
            float sumR = 0f, sumR2 = 0f;
            int n = 0;
            for (int i = 1; i < arr.Length; i++)
            {
                if (arr[i - 1] <= 0f) continue;
                float r = (arr[i] - arr[i - 1]) / arr[i - 1];
                sumR += r;
                sumR2 += r * r;
                n++;
            }
            if (n < 2) return 0f;
            float mean = sumR / n;
            float variance = sumR2 / n - mean * mean;
            if (variance <= 0f) return 0f;
            return mean / MathF.Sqrt(variance) * MathF.Sqrt(8760f);
        }
    }

    public float RollingDrawdown
    {
        get
        {
            if (_equities.Count < 2) return 0f;
            float peak = float.MinValue;
            float maxDd = 0f;
            foreach (var eq in _equities)
            {
                if (eq > peak) peak = eq;
                if (peak > 0f)
                {
                    float dd = (peak - eq) / peak;
                    if (dd > maxDd) maxDd = dd;
                }
            }
            return maxDd;
        }
    }

    public int Count => _equities.Count;
}
