using Seed.Core;
using Seed.Worlds;

namespace Seed.Agents;

/// <summary>
/// Agent body with deterministic sensor assembly and action shaping.
/// </summary>
public sealed class AgentBody : IAgentBody
{
    private readonly IWorld _world;
    private readonly AgentConfig _config;

    // Internal state
    private float _energy;
    private bool _alive;
    private float[] _prevSensors;
    private float[] _predictedSensors;
    private const float PredictionAlpha = 0.9f;

    public int SensorCount => _config.TotalSensorCount;
    public int ActuatorCount => ContinuousWorld.ActuatorCount;

    public AgentBody(IWorld world, AgentConfig? config = null)
    {
        _world = world;
        _config = config ?? AgentConfig.Default;
        _prevSensors = new float[SensorCount];
        _predictedSensors = new float[SensorCount];
    }

    public void Reset(in BodyResetContext ctx)
    {
        _energy = 1.0f;
        _alive = true;
        Array.Clear(_prevSensors);
        Array.Clear(_predictedSensors);
    }

    public void ReadSensors(Span<float> sensorBuffer)
    {
        if (sensorBuffer.Length < SensorCount)
            throw new ArgumentException($"Buffer too small: {sensorBuffer.Length} < {SensorCount}");

        int idx = 0;

        // Proximity rays
        float heading = _world.AgentHeading;
        float x = _world.AgentX;
        float y = _world.AgentY;

        for (int i = 0; i < _config.RayCount; i++)
        {
            // Rays spread across the forward hemisphere
            float rayAngle = heading + (i - _config.RayCount / 2f) * _config.RaySpreadRadians / _config.RayCount;
            float dirX = MathF.Cos(rayAngle);
            float dirY = MathF.Sin(rayAngle);

            var (distance, hitType) = _world.Raycast(x, y, dirX, dirY, _config.RayMaxDistance);

            // Normalize distance to [0, 1] where 0 = max distance, 1 = touching
            sensorBuffer[idx++] = 1f - (distance / _config.RayMaxDistance);

            // One-hot encode hit type (5 types: wall, obstacle, hazard, food, agent)
            sensorBuffer[idx++] = hitType == (int)EntityType.Wall ? 1f : 0f;
            sensorBuffer[idx++] = hitType == (int)EntityType.Obstacle ? 1f : 0f;
            sensorBuffer[idx++] = hitType == (int)EntityType.Hazard ? 1f : 0f;
            sensorBuffer[idx++] = hitType == (int)EntityType.Food ? 1f : 0f;
            sensorBuffer[idx++] = hitType == (int)EntityType.Agent ? 1f : 0f;
        }

        // Food gradient hint
        var (gradX, gradY) = _world.FoodGradient(x, y);
        // Transform to agent-relative coordinates
        float cosH = MathF.Cos(-heading);
        float sinH = MathF.Sin(-heading);
        float relGradX = gradX * cosH - gradY * sinH;
        float relGradY = gradX * sinH + gradY * cosH;
        sensorBuffer[idx++] = relGradX;
        sensorBuffer[idx++] = relGradY;

        // Energy level
        sensorBuffer[idx++] = _energy;

        // Proprioception: speed (normalized)
        sensorBuffer[idx++] = _world.AgentSpeed / ContinuousWorld.MaxSpeed;

        // Bias input (always 1)
        sensorBuffer[idx++] = 1f;

        // Signal sensing (strength per channel + gradient direction)
        var (s0, s1) = _world.NearbySignals(x, y);
        sensorBuffer[idx++] = s0;
        sensorBuffer[idx++] = s1;

        var (sgX, sgY) = _world.SignalGradient(x, y);
        float relSgX = sgX * cosH - sgY * sinH;
        float relSgY = sgX * sinH + sgY * cosH;
        sensorBuffer[idx++] = relSgX;
        sensorBuffer[idx++] = relSgY;

        sensorBuffer[idx++] = _world.NearestAgentEnergy(x, y);
        sensorBuffer[idx++] = _world.NearbyAgentDensity(x, y);
        var (shareRcv, attackRcv) = _world.InteractionFeedback();
        sensorBuffer[idx++] = DeterministicHelpers.Clamp(shareRcv * 10f, 0f, 1f);
        sensorBuffer[idx++] = DeterministicHelpers.Clamp(attackRcv * 10f, 0f, 1f);

        // Store for curiosity calculation
        sensorBuffer[..SensorCount].CopyTo(_prevSensors);
    }

    /// <summary>
    /// Calculate curiosity signal based on sensor change.
    /// </summary>
    public float ComputeCuriosity(ReadOnlySpan<float> currentSensors)
    {
        float sum = 0f;
        int count = Math.Min(currentSensors.Length, _prevSensors.Length);

        for (int i = 0; i < count; i++)
        {
            sum += MathF.Abs(currentSensors[i] - _prevSensors[i]);
        }

        // Normalize and clip
        float avgChange = count > 0 ? sum / count : 0f;
        return DeterministicHelpers.Clamp(avgChange, 0f, 1f);
    }

    /// <summary>
    /// EMA-based prediction-error curiosity: high when actual sensors diverge from prediction.
    /// Prediction adapts over time, so constant stimuli produce decreasing curiosity.
    /// </summary>
    public float ComputePredictionErrorCuriosity(ReadOnlySpan<float> currentSensors)
    {
        int count = Math.Min(currentSensors.Length, _predictedSensors.Length);
        float errorSum = 0f;

        for (int i = 0; i < count; i++)
        {
            float error = MathF.Abs(currentSensors[i] - _predictedSensors[i]);
            errorSum += error;
            _predictedSensors[i] = PredictionAlpha * _predictedSensors[i] + (1f - PredictionAlpha) * currentSensors[i];
        }

        float meanError = count > 0 ? errorSum / count : 0f;
        return DeterministicHelpers.Clamp(meanError, 0f, 1f);
    }

    public void ApplyActions(ReadOnlySpan<float> actionBuffer)
    {
        // Actions are applied directly to world in the step
        // This method exists for potential pre-processing
    }

    public void ApplyWorldSignals(in WorldSignals signals)
    {
        _energy += signals.EnergyDelta;
        if (_energy <= 0)
        {
            _energy = 0;
            _alive = false;
        }
        _energy = DeterministicHelpers.Clamp(_energy, 0f, 2f); // Cap at 2x initial
    }

    public BodyState GetState() => new(_energy, _alive);
}

/// <summary>
/// Agent sensor/actuator configuration.
/// </summary>
public sealed record AgentConfig(
    int RayCount = 8,
    float RaySpreadRadians = MathF.PI * 0.8f, // ~144 degrees
    float RayMaxDistance = 10f,
    int SignalChannels = 2,
    float SignalHearingRadius = 15f
)
{
    // Per ray: distance + 5 type channels = 6
    // Plus: food gradient (2) + energy (1) + speed (1) + bias (1) = 5
    // Plus: signal strength per channel + signal gradient (2) = SignalChannels + 2
    // Plus: nearest agent energy (1) + agent density (1) + share feedback (1) + attack feedback (1) = 4
    public int TotalSensorCount => RayCount * 6 + 5 + SignalChannels + 2 + 4;

    public static AgentConfig Default => new();
}


