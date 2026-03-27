namespace Seed.Market.Trading;

/// <summary>
/// Maps brain float[4] output to a TradingSignal.
/// Output semantics:
///   [0] tanh  → direction (-1 short, 0 flat, +1 long)
///   [1] sigmoid → position size (0% to 100% of max)
///   [2] sigmoid → urgency (0 = patient limit, 1 = immediate market)
///   [3] sigmoid → exit signal (> threshold closes position)
/// </summary>
public static class ActionInterpreter
{
    public const int OutputCount = 4;
    public const float ExitThreshold = 0.6f;
    public const float DirectionDeadzone = 0.15f;

    public static TradingSignal Interpret(ReadOnlySpan<float> outputs)
    {
        float rawDir = outputs.Length > 0 ? Tanh(outputs[0]) : 0f;
        float rawSize = outputs.Length > 1 ? Sigmoid(outputs[1]) : 0f;
        float rawUrgency = outputs.Length > 2 ? Sigmoid(outputs[2]) : 0f;
        float rawExit = outputs.Length > 3 ? Sigmoid(outputs[3]) : 0f;

        var direction = TradeDirection.Flat;
        if (rawDir > DirectionDeadzone) direction = TradeDirection.Long;
        else if (rawDir < -DirectionDeadzone) direction = TradeDirection.Short;

        float sizePct = MathF.Max(0f, MathF.Min(1f, rawSize));
        float urgency = MathF.Max(0f, MathF.Min(1f, rawUrgency));
        bool exit = rawExit > ExitThreshold;

        return new TradingSignal(direction, sizePct, urgency, exit);
    }

    private static float Sigmoid(float x) => 1f / (1f + MathF.Exp(-x));
    private static float Tanh(float x) => MathF.Tanh(x);
}
