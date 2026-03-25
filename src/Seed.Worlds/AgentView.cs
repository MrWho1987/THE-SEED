using Seed.Core;

namespace Seed.Worlds;

public sealed class AgentView : IWorld
{
    private readonly SharedArena _arena;
    private readonly int _agentIndex;

    public AgentView(SharedArena arena, int agentIndex)
    {
        _arena = arena;
        _agentIndex = agentIndex;
    }

    public float AgentX => _arena.AgentX(_agentIndex);
    public float AgentY => _arena.AgentY(_agentIndex);
    public float AgentHeading => _arena.AgentHeading(_agentIndex);
    public float AgentSpeed => _arena.AgentSpeed(_agentIndex);
    public float LightLevel => _arena.LightLevel;

    public (float distance, int hitType) Raycast(float originX, float originY,
        float dirX, float dirY, float maxDistance)
        => _arena.Raycast(_agentIndex, originX, originY, dirX, dirY, maxDistance);

    public float RaycastDistance(float originX, float originY, float dirX, float dirY, float maxDistance)
        => Raycast(originX, originY, dirX, dirY, maxDistance).distance;

    public int RaycastType(float originX, float originY, float dirX, float dirY, float maxDistance)
        => Raycast(originX, originY, dirX, dirY, maxDistance).hitType;

    public (float dx, float dy) FoodGradient(float x, float y)
        => _arena.FoodGradient(x, y);

    public (float s0, float s1) NearbySignals(float x, float y)
        => _arena.NearbySignals(_agentIndex, x, y, ContinuousWorld.SignalHearingRadius);

    public (float dx, float dy) SignalGradient(float x, float y)
        => _arena.SignalGradient(_agentIndex, x, y, ContinuousWorld.SignalHearingRadius);

    public float NearestAgentEnergy(float x, float y)
        => _arena.NearestAgentEnergy(_agentIndex, x, y, ContinuousWorld.InteractionRadius);

    public float NearbyAgentDensity(float x, float y)
        => _arena.NearbyAgentDensity(_agentIndex, x, y, ContinuousWorld.InteractionRadius);

    public (float shareReceived, float attackReceived) InteractionFeedback()
        => (_arena.Agents[_agentIndex].ShareReceived, _arena.Agents[_agentIndex].AttackReceived);

    public void Reset(ulong worldSeed, in WorldBudget budget)
        => throw new NotSupportedException("Use SharedArena.Reset instead");

    public WorldStepResult Step(ReadOnlySpan<float> actions)
        => throw new NotSupportedException("Use SharedArena.StepAll instead");
}
