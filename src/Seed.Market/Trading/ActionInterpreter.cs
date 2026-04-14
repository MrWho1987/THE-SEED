namespace Seed.Market.Trading;

/// <summary>
/// Maps brain float[6] output to a TradingSignal.
/// Output semantics:
///   [0] tanh    → direction (-1 short, 0 flat, +1 long)
///   [1] sigmoid → position size (0% to 100% of max)
///   [2] sigmoid → urgency (0 = patient limit, 1 = immediate market)
///   [3] sigmoid → exit signal (> threshold closes position)
///   [4] tanh    → price prediction (curiosity modulator; not consumed by trading)
///   [5] sigmoid → leverage confidence (scaled to [1, MaxLeverage] per trade)
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
    /// Ceiling for per-trade leverage. The brain's raw sigmoid output for output[5] is scaled
    /// linearly to [1, maxLeverage]. Default 1.0f means "leverage disabled" — the brain's
    /// leverage output is ignored and every trade runs at 1x.
    /// </param>
    public static TradingSignal Interpret(ReadOnlySpan<float> outputs, float maxLeverage = 1.0f)
    {
        float rawDir = outputs.Length > 0 ? Safe(outputs[0]) : 0f;
        float rawSizeSigmoid = outputs.Length > 1 ? Sigmoid(Safe(outputs[1])) : 0f;
        float rawUrgency = outputs.Length > 2 ? Sigmoid(Safe(outputs[2])) : 0f;
        float rawExit = outputs.Length > 3 ? Sigmoid(Safe(outputs[3])) : 0f;
        // outputs[4] is the price-prediction curiosity output, consumed by ComputeCuriosity.
        float rawLeverageSigmoid = outputs.Length > 5 ? Sigmoid(Safe(outputs[5])) : 0f;

        var direction = TradeDirection.Flat;
        if (rawDir > DirectionDeadzone) direction = TradeDirection.Long;
        else if (rawDir < -DirectionDeadzone) direction = TradeDirection.Short;

        float sizePct = MathF.Max(0f, MathF.Min(1f, rawSizeSigmoid));
        float urgency = MathF.Max(0f, MathF.Min(1f, rawUrgency));
        bool exit = rawExit > ExitThreshold;

        // Scale leverage to [1, maxLeverage]. Dormant output (sigmoid=0.5) → midpoint of range.
        // Under-confident output (sigmoid<0.5) → less than midpoint; over-confident → more.
        float leverageCeiling = MathF.Max(1.0f, maxLeverage);
        float leverage = 1.0f + rawLeverageSigmoid * (leverageCeiling - 1.0f);

        return new TradingSignal(
            direction,
            sizePct,
            urgency,
            exit,
            rawExit,
            leverage,
            rawSizeSigmoid,
            rawLeverageSigmoid);
    }

    private static float Safe(float x) =>
        float.IsNaN(x) || float.IsInfinity(x) ? 0f : x;

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));
}
