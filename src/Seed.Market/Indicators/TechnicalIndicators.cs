using Seed.Market.Signals;

namespace Seed.Market.Indicators;

/// <summary>
/// Computes technical indicators from a rolling window of OHLCV candle data.
/// All methods are stateless -- call with full history each time.
/// </summary>
public static class TechnicalIndicators
{
    public readonly record struct Candle(
        float Open, float High, float Low, float Close, float Volume, DateTimeOffset Time);

    /// <summary>
    /// Compute all technical indicator signals from candle history.
    /// Requires at least 26 candles for EMA-26.
    /// </summary>
    public static (int Index, float Value)[] Compute(ReadOnlySpan<Candle> candles)
    {
        if (candles.Length < 26) return [];

        var closes = new float[candles.Length];
        var highs = new float[candles.Length];
        var lows = new float[candles.Length];
        var volumes = new float[candles.Length];

        for (int i = 0; i < candles.Length; i++)
        {
            closes[i] = candles[i].Close;
            highs[i] = candles[i].High;
            lows[i] = candles[i].Low;
            volumes[i] = candles[i].Volume;
        }

        float rsi = ComputeRsi(closes, 14);
        float ema12 = ComputeEma(closes, 12);
        float ema26 = ComputeEma(closes, 26);
        float macdLine = ema12 - ema26;

        var macdHist = new float[candles.Length];
        var ema12s = ComputeEmaArray(closes, 12);
        var ema26s = ComputeEmaArray(closes, 26);
        var macdArr = new float[candles.Length];
        for (int i = 0; i < candles.Length; i++)
            macdArr[i] = ema12s[i] - ema26s[i];
        float macdSignal = ComputeEma(macdArr, 9);

        var (bbUpper, bbLower) = ComputeBollinger(closes, 20, 2f);
        float bbWidth = closes[^1] > 0 ? (bbUpper - bbLower) / closes[^1] : 0f;

        float atr = ComputeAtr(highs, lows, closes, 14);
        float vwap = ComputeVwap(candles);
        float vwapDev = vwap > 0 ? (closes[^1] - vwap) / vwap : 0f;
        float obvSlope = ComputeObvSlope(closes, volumes, 14);

        return
        [
            (SignalIndex.Rsi14, rsi),
            (SignalIndex.Ema12, ema12),
            (SignalIndex.Ema26, ema26),
            (SignalIndex.MacdLine, macdLine),
            (SignalIndex.MacdSignal, macdSignal),
            (SignalIndex.BollingerUpper, bbUpper),
            (SignalIndex.BollingerLower, bbLower),
            (SignalIndex.BollingerWidth, bbWidth),
            (SignalIndex.Atr14, atr),
            (SignalIndex.Vwap, vwap),
            (SignalIndex.VwapDeviation, vwapDev),
            (SignalIndex.ObvSlope, obvSlope)
        ];
    }

    public static float ComputeRsi(float[] closes, int period)
    {
        if (closes.Length < period + 1) return 50f;

        float gainSum = 0, lossSum = 0;
        for (int i = closes.Length - period; i < closes.Length; i++)
        {
            float change = closes[i] - closes[i - 1];
            if (change > 0) gainSum += change;
            else lossSum -= change;
        }

        if (lossSum == 0) return 100f;
        if (gainSum == 0) return 0f;

        float rs = gainSum / lossSum;
        return 100f - 100f / (1f + rs);
    }

    public static float ComputeEma(float[] data, int period)
    {
        if (data.Length == 0) return 0f;
        float mult = 2f / (period + 1f);
        float ema = data[0];
        for (int i = 1; i < data.Length; i++)
            ema = (data[i] - ema) * mult + ema;
        return ema;
    }

    public static float[] ComputeEmaArray(float[] data, int period)
    {
        var result = new float[data.Length];
        if (data.Length == 0) return result;
        float mult = 2f / (period + 1f);
        result[0] = data[0];
        for (int i = 1; i < data.Length; i++)
            result[i] = (data[i] - result[i - 1]) * mult + result[i - 1];
        return result;
    }

    public static (float upper, float lower) ComputeBollinger(float[] closes, int period, float stdDevMult)
    {
        if (closes.Length < period) return (closes[^1], closes[^1]);

        float sum = 0, sumSq = 0;
        for (int i = closes.Length - period; i < closes.Length; i++)
        {
            sum += closes[i];
            sumSq += closes[i] * closes[i];
        }

        float mean = sum / period;
        float variance = sumSq / period - mean * mean;
        float std = MathF.Sqrt(MathF.Max(0, variance));

        return (mean + stdDevMult * std, mean - stdDevMult * std);
    }

    public static float ComputeAtr(float[] highs, float[] lows, float[] closes, int period)
    {
        if (closes.Length < period + 1) return 0f;

        float atr = 0;
        for (int i = closes.Length - period; i < closes.Length; i++)
        {
            float tr = MathF.Max(
                highs[i] - lows[i],
                MathF.Max(
                    MathF.Abs(highs[i] - closes[i - 1]),
                    MathF.Abs(lows[i] - closes[i - 1])));
            atr += tr;
        }
        return atr / period;
    }

    public static float ComputeVwap(ReadOnlySpan<Candle> candles)
    {
        float cumVol = 0, cumTpVol = 0;
        int start = Math.Max(0, candles.Length - 24);
        for (int i = start; i < candles.Length; i++)
        {
            float tp = (candles[i].High + candles[i].Low + candles[i].Close) / 3f;
            cumTpVol += tp * candles[i].Volume;
            cumVol += candles[i].Volume;
        }
        return cumVol > 0 ? cumTpVol / cumVol : 0f;
    }

    public static float ComputeObvSlope(float[] closes, float[] volumes, int period)
    {
        if (closes.Length < period + 1) return 0f;

        float obv = 0;
        float obvStart = 0;
        int start = closes.Length - period - 1;

        for (int i = start + 1; i < closes.Length; i++)
        {
            float dir = closes[i] > closes[i - 1] ? 1f : closes[i] < closes[i - 1] ? -1f : 0f;
            obv += dir * volumes[i];
            if (i == start + 1) obvStart = obv;
        }

        return period > 0 ? (obv - obvStart) / period : 0f;
    }

    // ── O(n) array-producing variants for batch pre-processing ──

    public static float[] ComputeRsiArray(float[] closes, int period)
    {
        int n = closes.Length;
        var result = new float[n];
        if (n < period + 1)
        {
            Array.Fill(result, 50f);
            return result;
        }

        float gainSum = 0, lossSum = 0;
        for (int i = 1; i <= period; i++)
        {
            float ch = closes[i] - closes[i - 1];
            if (ch > 0) gainSum += ch; else lossSum -= ch;
        }

        float avgGain = gainSum / period;
        float avgLoss = lossSum / period;

        for (int i = 0; i < period + 1; i++)
            result[i] = 50f;

        result[period] = avgLoss == 0 ? 100f : avgGain == 0 ? 0f : 100f - 100f / (1f + avgGain / avgLoss);

        for (int i = period + 1; i < n; i++)
        {
            float ch = closes[i] - closes[i - 1];
            float gain = ch > 0 ? ch : 0f;
            float loss = ch < 0 ? -ch : 0f;
            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
            result[i] = avgLoss == 0 ? 100f : avgGain == 0 ? 0f : 100f - 100f / (1f + avgGain / avgLoss);
        }

        return result;
    }

    public static float[] ComputeAtrArray(float[] highs, float[] lows, float[] closes, int period)
    {
        int n = closes.Length;
        var result = new float[n];
        if (n < period + 1) return result;

        float atr = 0;
        for (int i = 1; i <= period; i++)
        {
            float tr = MathF.Max(highs[i] - lows[i],
                MathF.Max(MathF.Abs(highs[i] - closes[i - 1]),
                          MathF.Abs(lows[i] - closes[i - 1])));
            atr += tr;
        }
        atr /= period;
        result[period] = atr;

        for (int i = period + 1; i < n; i++)
        {
            float tr = MathF.Max(highs[i] - lows[i],
                MathF.Max(MathF.Abs(highs[i] - closes[i - 1]),
                          MathF.Abs(lows[i] - closes[i - 1])));
            atr = (atr * (period - 1) + tr) / period;
            result[i] = atr;
        }

        return result;
    }

    public readonly record struct BollingerBand(float Upper, float Lower);

    public static BollingerBand[] ComputeBollingerArray(float[] closes, int period, float stdDevMult)
    {
        int n = closes.Length;
        var result = new BollingerBand[n];

        float sum = 0, sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            sum += closes[i];
            sumSq += closes[i] * closes[i];

            if (i >= period)
            {
                sum -= closes[i - period];
                sumSq -= closes[i - period] * closes[i - period];
            }

            int w = Math.Min(i + 1, period);
            float mean = sum / w;
            float variance = sumSq / w - mean * mean;
            float std = MathF.Sqrt(MathF.Max(0, variance));
            result[i] = new BollingerBand(mean + stdDevMult * std, mean - stdDevMult * std);
        }

        return result;
    }

    public static float[] ComputeObvSlopeArray(float[] closes, float[] volumes, int period)
    {
        int n = closes.Length;
        var result = new float[n];
        if (n < 2) return result;

        var obv = new float[n];
        obv[0] = 0;
        for (int i = 1; i < n; i++)
        {
            float dir = closes[i] > closes[i - 1] ? 1f : closes[i] < closes[i - 1] ? -1f : 0f;
            obv[i] = obv[i - 1] + dir * volumes[i];
        }

        for (int i = period + 1; i < n; i++)
            result[i] = (obv[i] - obv[i - period]) / period;

        return result;
    }

    public static float[] ComputeVwapArray(Candle[] candles, int window)
    {
        int n = candles.Length;
        var result = new float[n];
        float cumTpVol = 0, cumVol = 0;

        for (int i = 0; i < n; i++)
        {
            float tp = (candles[i].High + candles[i].Low + candles[i].Close) / 3f;
            cumTpVol += tp * candles[i].Volume;
            cumVol += candles[i].Volume;

            if (i >= window)
            {
                float oldTp = (candles[i - window].High + candles[i - window].Low + candles[i - window].Close) / 3f;
                cumTpVol -= oldTp * candles[i - window].Volume;
                cumVol -= candles[i - window].Volume;
            }

            result[i] = cumVol > 0 ? cumTpVol / cumVol : 0f;
        }

        return result;
    }
}
