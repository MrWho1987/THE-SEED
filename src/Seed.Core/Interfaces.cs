namespace Seed.Core;

/// <summary>
/// Genome interface - the evolvable recipe.
/// </summary>
public interface IGenome
{
    Guid GenomeId { get; }
    string GenomeType { get; }
    
    string ToJson();
    IGenome CloneGenome(Guid? newId = null);
    IGenome Mutate(in MutationContext ctx);
    float DistanceTo(IGenome other, in SpeciationConfig cfg);
}

/// <summary>
/// Context for mutation operations.
/// </summary>
public readonly record struct MutationContext(
    ulong RunSeed,
    int GenerationIndex,
    MutationConfig Config,
    IInnovationTracker Innovations,
    Rng64 Rng
);

/// <summary>
/// Tracks structural innovations for NEAT-style alignment.
/// </summary>
public interface IInnovationTracker
{
    int NextInnovationId { get; }
    int NextCppnNodeId { get; }
    
    int GetOrCreateConnectionInnovation(int srcNodeId, int dstNodeId);
    (int NewNodeId, int InnovSrcToNew, int InnovNewToDst) GetOrCreateSplitInnovation(int oldConnInnovationId);
}

/// <summary>
/// Brain interface - runtime neural controller.
/// </summary>
public interface IBrain
{
    ReadOnlySpan<float> Step(ReadOnlySpan<float> inputs, in BrainStepContext ctx);
    void Learn(ReadOnlySpan<float> modulators, in BrainLearnContext ctx);
    
    IBrainGraph ExportGraph();
    void Reset();
    
    float GetInstabilityPenalty();
}

/// <summary>
/// Brain graph export interface.
/// </summary>
public interface IBrainGraph
{
    int InputCount { get; }
    int OutputCount { get; }
    int ModulatorCount { get; }
    int NodeCount { get; }
    int EdgeCount { get; }
    
    string ToJson();
}

/// <summary>
/// Agent body interface - sensors and actuators.
/// </summary>
public interface IAgentBody
{
    int SensorCount { get; }
    int ActuatorCount { get; }
    
    void Reset(in BodyResetContext ctx);
    void ReadSensors(Span<float> sensorBuffer);
    void ApplyActions(ReadOnlySpan<float> actionBuffer);
    void ApplyWorldSignals(in WorldSignals signals);
    BodyState GetState();
}

/// <summary>
/// World interface - simulation environment.
/// </summary>
public interface IWorld
{
    void Reset(ulong worldSeed, in WorldBudget budget);
    WorldStepResult Step(ReadOnlySpan<float> actions);
    
    // Sensor queries for the agent
    (float distance, int hitType) Raycast(float originX, float originY, float dirX, float dirY, float maxDistance);
    float RaycastDistance(float originX, float originY, float dirX, float dirY, float maxDistance);
    int RaycastType(float originX, float originY, float dirX, float dirY, float maxDistance);
    (float dx, float dy) FoodGradient(float x, float y);
    (float s0, float s1) NearbySignals(float x, float y);
    (float dx, float dy) SignalGradient(float x, float y);
    float NearestAgentEnergy(float x, float y);
    float NearbyAgentDensity(float x, float y);
    (float shareReceived, float attackReceived) InteractionFeedback();
    
    // State queries
    float AgentX { get; }
    float AgentY { get; }
    float AgentHeading { get; }
    float AgentSpeed { get; }
}

/// <summary>
/// Observatory interface - metrics and replay logging.
/// </summary>
public interface IObservatory
{
    void OnEvent(in ObsEvent e);
    void Flush();
}

