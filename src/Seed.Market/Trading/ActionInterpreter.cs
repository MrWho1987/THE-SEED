namespace Seed.Market.Trading;

/// <summary>
/// Maps brain float[6] output to a TradingSignal.
/// Output semantics:
///   [0] tanh    → direction (-1 short, 0 flat, +1 long)
///   [1] sigmoid → position size (0% to 100% of max)
///   [2] sigmoid → urgency (0 = patient limit, 1 = immediate market)
///   [3] sigmoid → exit signal (> threshold closes position)
///   [4] tanh    → price prediction (curiosity modulator; not consumed by trading)
///   [5] tanh    → leverage confidence (negative/zero = 1x; positive log-scales to MaxLeverage)
/// </summary>
public static class ActionInterpreter
{
    public const int OutputCount = 6;
    public const float ExitThreshold = 0.6f;
    public const float DirectionDeadzone = 0.15f;

    /// <summary>
    /// Interprets raw brain outputs into a TradingSignal.
    /// </summary>
    /// <param name="outputs">Raw brain outputs span (length up to OutputCount).</param>
    /// <param name="maxLeverage">
    /// Ceiling for per-trade leverage. The brain's raw output for output[5] is passed through
    /// tanh → [-1, +1], clamped to [0, +1], then exponentially mapped to [1, maxLeverage] so
    /// a dormant/negative/zero output yields 1× (safe default) and a strong positive output
    /// yields full maxLeverage. Log-scaled because real traders pick leverage in log space
    /// (1, 2, 5, 10, 25, 125). Default maxLeverage=1 disables leverage entirely.
    /// </param>
    public static TradingSignal Interpret(ReadOnlySpan<float> outputs, float maxLeverage = 1.0f)
    {
        float rawDir = outputs.Length > 0 ? Safe(outputs[0]) : 0f;
        float rawSizeSigmoid = outputs.Length > 1 ? Sigmoid(Safe(outputs[1])) : 0f;
        float rawUrgency = outputs.Length > 2 ? Sigmoid(Safe(outputs[2])) : 0f;
        float rawExit = outputs.Length > 3 ? Sigmoid(Safe(outputs[3])) : 0f;
        // outputs[4] is the price-prediction curiosity output, consumed by ComputeCuriosity.
        float rawLeverageTanh = outputs.Length > 5 ? MathF.Tanh(Safe(outputs[5])) : 0f;

        var direction = TradeDirection.Flat;
        if (rawDir > DirectionDeadzone) direction = TradeDirection.Long;
        else if (rawDir < -DirectionDeadzone) direction = TradeDirection.Short;

        float sizePct = MathF.Max(0f, MathF.Min(1f, rawSizeSigmoid));
        float urgency = MathF.Max(0f, MathF.Min(1f, rawUrgency));
        bool exit = rawExit > ExitThreshold;

        // Leverage: log-scaled via exponential mapping so a dormant/negative output yields 1×
        // (safe default) and a strong positive signal reaches maxLeverage. At maxLeverage=1
        // this collapses to leverage=1 identically for all outputs. At maxLeverage=125:
        //   tanh ≤ 0  → 1×     (no leverage)
        //   tanh=0.3  → 4.3×   (moderate)
        //   tanh=0.5  → 11.2×  (medium-high)
        //   tanh=0.7  → 33.6×  (high)
        //   tanh=0.9  → 82.2×  (aggressive)
        //   tanh=1.0  → 125×   (max)
        // Log scale matches how traders actually pick leverage (1, 2, 5, 10, 25, 125).
        float leverageCeiling = MathF.Max(1.0f, maxLeverage);
        float positiveSignal = MathF.Max(0f, rawLeverageTanh);  // [0, 1], dormant/negative = 0
        float leverage = MathF.Pow(leverageCeiling, positiveSignal);

        return new TradingSignal(
            direction,
            sizePct,
            urgency,
            exit,
            rawExit,
            leverage,
            rawSizeSigmoid,
            positiveSignal);  // RawLeverage stores the clamped [0,1] signal before scaling
    }

    private static float Safe(float x) =>
        float.IsNaN(x) || float.IsInfinity(x) ? 0f : x;

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));
}
