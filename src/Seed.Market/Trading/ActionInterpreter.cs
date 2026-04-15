namespace Seed.Market.Trading;

/// <summary>
/// Maps brain float[11] output to a TradingSignal.
///
/// V14 output layout (11 total):
///   [0]  tanh    → direction (-1 short, 0 flat, +1 long)
///   [1]  sigmoid → position size (0% to 100% of max)
///   [2]  sigmoid → urgency (0 = patient limit, 1 = immediate market)
///   [3]  sigmoid → exit signal (> threshold closes position)
///   [4]  tanh    → price prediction (curiosity modulator; not consumed by trading)
///   [5]  tanh    → leverage confidence (negative/zero = 1x; positive log-scales to MaxLeverage)
///   [6]  sigmoid → partial close fraction (only takes effect if > 0.2, reduces open position)
///   [7]  sigmoid → enable trailing stop (> 0.5 enables on open)
///   [8]  sigmoid → trailing stop distance [0.5%, 10%] log-scaled
///   [9]  sigmoid → take-profit offset [0.5%, 15%] log-scaled (dead zone below 0.1)
///   [10] sigmoid → stop-loss override [0.5%, 5%] log-scaled (dead zone below 0.1)
/// </summary>
public static class ActionInterpreter
{
    public const int OutputCount = 11;
    public const float ExitThreshold = 0.6f;
    public const float DirectionDeadzone = 0.15f;

    // V11d: Dead zones at 0.8 — force the brain to produce a STRONG explicit signal before
    // outputs 6-10 activate. Random CPPN-initialized brains have sigmoid outputs centered
    // at 0.5, so a 0.8 deadzone requires the brain to bias raw output > ~1.4 (verified
    // <5% random activation in CorrectDeadzone_0point8_KeepsRandomBrainsDormant test).
    //
    // The previous deadzones (0.1-0.5) caused random brains to activate outputs 6-10 at
    // 50-85% rates, creating constant position churn (partial close every other tick,
    // brain-set tight SL/TP on most trades). This anti-learning churn was a key contributor
    // to the V11 passive-trap. With deadzones at 0.8, random brains stay dormant on outputs
    // 6-10 and only learn to use them after base entry/exit (outputs 0-5) is established.
    // The brain-driven-exit bonus (V11c) rewards the eventual discovery.
    public const float PartialCloseDeadzone = 0.8f;
    public const float TpDeadzone = 0.8f;
    public const float SlDeadzone = 0.8f;
    public const float TrailEnableThreshold = 0.8f;

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

        // V14 new outputs (6-10), each sigmoid-scaled to [0, 1]
        float rawPartialClose = outputs.Length > 6 ? Sigmoid(Safe(outputs[6])) : 0f;
        float rawTrailEnable = outputs.Length > 7 ? Sigmoid(Safe(outputs[7])) : 0f;
        float rawTrailDist = outputs.Length > 8 ? Sigmoid(Safe(outputs[8])) : 0f;
        float rawTpOffset = outputs.Length > 9 ? Sigmoid(Safe(outputs[9])) : 0f;
        float rawSlOverride = outputs.Length > 10 ? Sigmoid(Safe(outputs[10])) : 0f;

        var direction = TradeDirection.Flat;
        if (rawDir > DirectionDeadzone) direction = TradeDirection.Long;
        else if (rawDir < -DirectionDeadzone) direction = TradeDirection.Short;

        float sizePct = MathF.Max(0f, MathF.Min(1f, rawSizeSigmoid));
        float urgency = MathF.Max(0f, MathF.Min(1f, rawUrgency));
        bool exit = rawExit > ExitThreshold;

        // Leverage: log-scaled via exponential mapping. See V13 rationale.
        float leverageCeiling = MathF.Max(1.0f, maxLeverage);
        float positiveSignal = MathF.Max(0f, rawLeverageTanh);
        float leverage = MathF.Pow(leverageCeiling, positiveSignal);

        // V14: partial close — only activates if the brain explicitly pushes above dead zone
        float partialCloseFrac = rawPartialClose > PartialCloseDeadzone ? rawPartialClose : 0f;

        // V14: trailing stop
        bool enableTrailingStop = rawTrailEnable > TrailEnableThreshold;
        float trailDist = 0f;
        if (enableTrailingStop)
        {
            // Log-scale map [0, 1] → [0.5%, 10%]: logLo=ln(0.005)=-5.30, range=ln(0.10/0.005)=3.00
            float logDist = -5.30f + rawTrailDist * 3.00f;
            trailDist = MathF.Exp(logDist);
        }

        // V14: take-profit offset
        float tpOffset = 0f;
        if (rawTpOffset > TpDeadzone)
        {
            // Log-scale [0, 1] → [0.5%, 15%]: logLo=ln(0.005)=-5.30, range=ln(0.15/0.005)=3.40
            float logTp = -5.30f + rawTpOffset * 3.40f;
            tpOffset = MathF.Exp(logTp);
        }

        // V14: stop-loss override
        float slOverride = 0f;
        if (rawSlOverride > SlDeadzone)
        {
            // Log-scale [0, 1] → [0.5%, 5%]: logLo=ln(0.005)=-5.30, range=ln(0.05/0.005)=2.30
            float logSl = -5.30f + rawSlOverride * 2.30f;
            slOverride = MathF.Exp(logSl);
        }

        return new TradingSignal(
            direction,
            sizePct,
            urgency,
            exit,
            rawExit,
            leverage,
            rawSizeSigmoid,
            positiveSignal,
            partialCloseFrac,
            enableTrailingStop,
            trailDist,
            tpOffset,
            slOverride);
    }

    private static float Safe(float x) =>
        float.IsNaN(x) || float.IsInfinity(x) ? 0f : x;

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));
}
