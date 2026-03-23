using Seed.Core;

namespace Seed.Worlds;

/// <summary>
/// Continuous 2D kinematic world with deterministic physics.
/// Implements IWorld interface for agent evaluation.
/// </summary>
public sealed class ContinuousWorld : IWorld
{
    // World state
    private WorldBudget _budget;
    private List<AABB> _obstacles = new();
    private List<AABB> _hazards = new();
    private List<FoodItem> _food = new();
    private Rng64 _worldRng;  // RNG for food respawn positions

    private float[] _clusterX = Array.Empty<float>();
    private float[] _clusterY = Array.Empty<float>();
    private float _clusterSpread;

    // Agent state
    private float _agentX;
    private float _agentY;
    private float _agentHeading; // radians, 0 = right, PI/2 = up
    private float _agentSpeed;
    private float _agentEnergy;
    private bool _agentAlive;
    private int _tick;


    // Physics constants
    public const float AgentRadius = 0.5f;
    public const float MaxSpeed = 2.0f;
    public const float Acceleration = 0.5f;
    public const float TurnRate = 0.3f;      // radians per tick
    public const float Friction = 0.02f;
    public const float MovementEnergyCost = 0.001f;
    public const float BaseEnergyCost = 0.0002f;
    public const float HazardDamage = 0.05f;
    public const float InitialEnergy = 1.0f;
    public const float FoodCollectionRadius = 1.0f;
    public const int FoodRespawnDelay = 100;  // Ticks before consumed food respawns

    // Interaction constants
    public const float InteractionRadius = 2.0f;
    public const float ShareRate = 0.02f;
    public const float ShareEfficiency = 0.8f;
    public const float AttackDrainRate = 0.015f;
    public const float AttackCost = 0.003f;
    public const float AttackEfficiency = 0.5f;

    // Actuator indices
    public const int ActuatorThrust = 0;     // [-1, 1] backward/forward
    public const int ActuatorTurn = 1;       // [-1, 1] left/right
    public const int ActuatorSignal0 = 2;    // [-1, 1] signal channel 0
    public const int ActuatorSignal1 = 3;    // [-1, 1] signal channel 1
    public const int ActuatorShare = 4;      // [0, 1] share energy with nearest agent
    public const int ActuatorAttack = 5;     // [0, 1] attack nearest agent
    public const int ActuatorCount = 6;

    public const float SignalHearingRadius = 15f;

    public float AgentX => _agentX;
    public float AgentY => _agentY;
    public float AgentHeading => _agentHeading;
    public float AgentSpeed => _agentSpeed;
    public float AgentEnergy => _agentEnergy;
    public bool AgentAlive => _agentAlive;
    
    /// <summary>
    /// Get readonly list of food items for visualization.
    /// </summary>
    public IReadOnlyList<FoodItem> GetFoodItems() => _food;
    
    /// <summary>
    /// Get readonly list of obstacles for visualization.
    /// </summary>
    public IReadOnlyList<AABB> GetObstacles() => _obstacles;
    
    /// <summary>
    /// Get readonly list of hazards for visualization.
    /// </summary>
    public IReadOnlyList<AABB> GetHazards() => _hazards;

    public void Reset(ulong worldSeed, in WorldBudget budget)
    {
        _budget = budget;
        _tick = 0;

        var rng = new Rng64(worldSeed);
        GenerateWorld(ref rng);

        // Place agent at a safe starting position
        PlaceAgent(ref rng);

        // Save RNG state for food respawning
        _worldRng = rng;

        _agentSpeed = 0f;
        _agentEnergy = InitialEnergy;
        _agentAlive = true;
    }

    private void GenerateWorld(ref Rng64 rng)
    {
        _obstacles.Clear();
        _hazards.Clear();
        _food.Clear();

        float margin = 2f;
        int obstacleCount = (int)(_budget.WorldWidth * _budget.WorldHeight * _budget.ObstacleDensity / 16f);
        int hazardCount = (int)(_budget.WorldWidth * _budget.WorldHeight * _budget.HazardDensity / 16f);

        // Generate obstacles
        int id = 0;
        for (int i = 0; i < obstacleCount; i++)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float w = rng.NextFloat(1f, 4f);
                float h = rng.NextFloat(1f, 4f);
                float x = rng.NextFloat(margin, _budget.WorldWidth - margin - w);
                float y = rng.NextFloat(margin, _budget.WorldHeight - margin - h);

                var aabb = new AABB(id, x, y, x + w, y + h);

                // Check no overlap with existing obstacles
                bool valid = true;
                foreach (var obs in _obstacles)
                {
                    if (WorldHelpers.AABBsOverlap(aabb, obs, 0.5f))
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                {
                    _obstacles.Add(aabb);
                    id++;
                    break;
                }
            }
        }

        // Generate hazards
        for (int i = 0; i < hazardCount; i++)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float w = rng.NextFloat(1f, 3f);
                float h = rng.NextFloat(1f, 3f);
                float x = rng.NextFloat(margin, _budget.WorldWidth - margin - w);
                float y = rng.NextFloat(margin, _budget.WorldHeight - margin - h);

                var aabb = new AABB(id, x, y, x + w, y + h);

                // Check no overlap with obstacles
                bool valid = true;
                foreach (var obs in _obstacles)
                {
                    if (WorldHelpers.AABBsOverlap(aabb, obs, 0.5f))
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                {
                    _hazards.Add(aabb);
                    id++;
                    break;
                }
            }
        }

        // Generate food
        if (_budget.FoodClusters > 0)
        {
            int nc = _budget.FoodClusters;
            _clusterX = new float[nc];
            _clusterY = new float[nc];
            float innerMargin = margin * 2;
            for (int c = 0; c < nc; c++)
            {
                _clusterX[c] = rng.NextFloat(innerMargin, _budget.WorldWidth - innerMargin);
                _clusterY[c] = rng.NextFloat(innerMargin, _budget.WorldHeight - innerMargin);
            }
            _clusterSpread = MathF.Min(_budget.WorldWidth, _budget.WorldHeight) / (nc * 2.5f);

            for (int i = 0; i < _budget.FoodCount; i++)
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    int cluster = rng.NextInt(nc);
                    float x = _clusterX[cluster] + rng.NextGaussian() * _clusterSpread;
                    float y = _clusterY[cluster] + rng.NextGaussian() * _clusterSpread;
                    x = DeterministicHelpers.Clamp(x, margin, _budget.WorldWidth - margin);
                    y = DeterministicHelpers.Clamp(y, margin, _budget.WorldHeight - margin);

                    bool valid = true;
                    foreach (var obs in _obstacles)
                        if (obs.Contains(x, y, FoodCollectionRadius))
                        { valid = false; break; }
                    if (valid) { _food.Add(new FoodItem(i, x, y, 0.3f, 0.2f, false)); break; }
                }
            }
        }
        else
        {
            _clusterX = Array.Empty<float>();
            _clusterY = Array.Empty<float>();
            for (int i = 0; i < _budget.FoodCount; i++)
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    float x = rng.NextFloat(margin, _budget.WorldWidth - margin);
                    float y = rng.NextFloat(margin, _budget.WorldHeight - margin);

                    bool valid = true;
                    foreach (var obs in _obstacles)
                    {
                        if (obs.Contains(x, y, FoodCollectionRadius))
                        {
                            valid = false;
                            break;
                        }
                    }

                    if (valid)
                    {
                        _food.Add(new FoodItem(i, x, y, 0.3f, 0.2f, false));
                        break;
                    }
                }
            }
        }

        // Sort for deterministic iteration
        _obstacles.Sort((a, b) => a.Id.CompareTo(b.Id));
        _hazards.Sort((a, b) => a.Id.CompareTo(b.Id));
        _food.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    private void PlaceAgent(ref Rng64 rng)
    {
        float margin = 3f;
        for (int attempt = 0; attempt < 100; attempt++)
        {
            float x = rng.NextFloat(margin, _budget.WorldWidth - margin);
            float y = rng.NextFloat(margin, _budget.WorldHeight - margin);

            // Check not inside obstacle or hazard
            bool valid = true;
            foreach (var obs in _obstacles)
            {
                if (obs.OverlapsCircle(x, y, AgentRadius + 0.5f))
                {
                    valid = false;
                    break;
                }
            }

            if (valid)
            {
                foreach (var haz in _hazards)
                {
                    if (haz.OverlapsCircle(x, y, AgentRadius + 0.5f))
                    {
                        valid = false;
                        break;
                    }
                }
            }

            if (valid)
            {
                _agentX = x;
                _agentY = y;
                _agentHeading = rng.NextFloat(0f, MathF.PI * 2f);
                return;
            }
        }

        // Fallback to center
        _agentX = _budget.WorldWidth / 2f;
        _agentY = _budget.WorldHeight / 2f;
        _agentHeading = 0f;
    }

    /// <summary>
    /// Check for consumed food that should respawn and respawn it at a new location.
    /// </summary>
    private void RespawnFood()
    {
        float margin = 2f;
        
        for (int i = 0; i < _food.Count; i++)
        {
            var f = _food[i];
            if (f.Consumed && f.RespawnTick > 0 && _tick >= f.RespawnTick)
            {
                bool placed = false;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    float x, y;
                    if (_budget.FoodClusters > 0 && _clusterX.Length > 0)
                    {
                        int cluster = _worldRng.NextInt(_clusterX.Length);
                        x = _clusterX[cluster] + _worldRng.NextGaussian() * _clusterSpread;
                        y = _clusterY[cluster] + _worldRng.NextGaussian() * _clusterSpread;
                        x = DeterministicHelpers.Clamp(x, margin, _budget.WorldWidth - margin);
                        y = DeterministicHelpers.Clamp(y, margin, _budget.WorldHeight - margin);
                    }
                    else
                    {
                        x = _worldRng.NextFloat(margin, _budget.WorldWidth - margin);
                        y = _worldRng.NextFloat(margin, _budget.WorldHeight - margin);
                    }

                    bool valid = true;
                    foreach (var obs in _obstacles)
                    {
                        if (obs.Contains(x, y, FoodCollectionRadius))
                        {
                            valid = false;
                            break;
                        }
                    }

                    if (valid)
                    {
                        float dx = x - _agentX;
                        float dy = y - _agentY;
                        if (dx * dx + dy * dy < 4f)
                            valid = false;
                    }

                    if (valid)
                    {
                        _food[i] = new FoodItem(f.Id, x, y, f.Radius, f.EnergyValue, false, -1);
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                {
                    float x, y;
                    if (_budget.FoodClusters > 0 && _clusterX.Length > 0)
                    {
                        int cluster = _worldRng.NextInt(_clusterX.Length);
                        x = _clusterX[cluster] + _worldRng.NextGaussian() * _clusterSpread;
                        y = _clusterY[cluster] + _worldRng.NextGaussian() * _clusterSpread;
                        x = DeterministicHelpers.Clamp(x, margin, _budget.WorldWidth - margin);
                        y = DeterministicHelpers.Clamp(y, margin, _budget.WorldHeight - margin);
                    }
                    else
                    {
                        x = _worldRng.NextFloat(margin, _budget.WorldWidth - margin);
                        y = _worldRng.NextFloat(margin, _budget.WorldHeight - margin);
                    }
                    _food[i] = new FoodItem(f.Id, x, y, f.Radius, f.EnergyValue, false, -1);
                }
            }
        }
    }

    public WorldStepResult Step(ReadOnlySpan<float> actions)
    {
        if (!_agentAlive)
        {
            return new WorldStepResult(
                Done: true,
                Signals: new WorldSignals(0, 0, 0),
                Modulators: new float[ModulatorIndex.Count],
                Info: new WorldStepInfo()
            );
        }

        float thrust = actions.Length > ActuatorThrust 
            ? DeterministicHelpers.Clamp(actions[ActuatorThrust], -1f, 1f) 
            : 0f;
        float turn = actions.Length > ActuatorTurn 
            ? DeterministicHelpers.Clamp(actions[ActuatorTurn], -1f, 1f) 
            : 0f;

        // Apply turn
        _agentHeading += turn * TurnRate;

        // Normalize heading to [0, 2π)
        while (_agentHeading < 0) _agentHeading += MathF.PI * 2f;
        while (_agentHeading >= MathF.PI * 2f) _agentHeading -= MathF.PI * 2f;

        // Apply thrust
        _agentSpeed += thrust * Acceleration;
        _agentSpeed *= (1f - Friction);
        _agentSpeed = DeterministicHelpers.Clamp(_agentSpeed, -MaxSpeed, MaxSpeed);

        // Calculate new position
        float dx = MathF.Cos(_agentHeading) * _agentSpeed;
        float dy = MathF.Sin(_agentHeading) * _agentSpeed;
        float newX = _agentX + dx;
        float newY = _agentY + dy;

        // Wall collision
        newX = DeterministicHelpers.Clamp(newX, AgentRadius, _budget.WorldWidth - AgentRadius);
        newY = DeterministicHelpers.Clamp(newY, AgentRadius, _budget.WorldHeight - AgentRadius);

        // Obstacle collision (deterministic order by ID)
        foreach (var obs in _obstacles)
        {
            var (depth, pushX, pushY) = obs.CirclePenetration(newX, newY, AgentRadius);
            if (depth > 0)
            {
                newX += pushX * depth * 1.01f;
                newY += pushY * depth * 1.01f;
                _agentSpeed *= 0.5f; // Dampen on collision
            }
        }

        _agentX = newX;
        _agentY = newY;

        // Hazard check
        float hazardPenalty = 0f;
        foreach (var haz in _hazards)
        {
            if (haz.OverlapsCircle(_agentX, _agentY, AgentRadius))
            {
                hazardPenalty += HazardDamage;
            }
        }

        // Food collection
        float energyMultiplier = 1f;
        if (_budget.FoodEnergyAmplitude > 0f && _budget.FoodEnergyPeriod > 0)
        {
            float phase = 2f * MathF.PI * _tick / _budget.FoodEnergyPeriod;
            energyMultiplier = 1f + _budget.FoodEnergyAmplitude * MathF.Sin(phase);
        }

        int foodCollected = 0;
        float foodEnergy = 0f;
        for (int i = 0; i < _food.Count; i++)
        {
            var f = _food[i];
            if (!f.Consumed)
            {
                float fdx = _agentX - f.X;
                float fdy = _agentY - f.Y;
                float distSq = fdx * fdx + fdy * fdy;
                float collectDist = AgentRadius + f.Radius + FoodCollectionRadius;

                if (distSq <= collectDist * collectDist)
                {
                    _food[i] = f with { Consumed = true, RespawnTick = _tick + FoodRespawnDelay };
                    foodEnergy += f.EnergyValue * energyMultiplier;
                    foodCollected++;
                }
            }
        }

        // Food respawning
        RespawnFood();

        // Energy accounting
        float movementCost = MathF.Abs(_agentSpeed) * MovementEnergyCost;
        float totalCost = BaseEnergyCost + movementCost + hazardPenalty;
        float energyDelta = foodEnergy - totalCost;
        _agentEnergy += energyDelta;

        if (_agentEnergy <= 0)
        {
            _agentEnergy = 0;
            _agentAlive = false;
        }

        _tick++;

        // Build modulators
        var modulators = new float[ModulatorIndex.Count];
        modulators[ModulatorIndex.Reward] = Math.Max(0, foodEnergy);
        modulators[ModulatorIndex.Pain] = Math.Max(0, totalCost) + hazardPenalty;
        modulators[ModulatorIndex.Curiosity] = 0f; // Will be computed by agent/evaluator

        return new WorldStepResult(
            Done: !_agentAlive,
            Signals: new WorldSignals(energyDelta, foodCollected, hazardPenalty),
            Modulators: modulators,
            Info: new WorldStepInfo()
        );
    }

    public (float distance, int hitType) Raycast(float originX, float originY,
        float dirX, float dirY, float maxDistance)
    {
        float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 1e-6f) return (maxDistance, (int)EntityType.None);
        dirX /= len;
        dirY /= len;

        float closestPhysical = maxDistance;
        float closestAny = maxDistance;
        EntityType hitType = EntityType.None;

        float wallDist = RayWallDistance(originX, originY, dirX, dirY, maxDistance);
        if (wallDist < closestPhysical) closestPhysical = wallDist;
        if (wallDist < closestAny) { closestAny = wallDist; hitType = EntityType.Wall; }

        foreach (var obs in _obstacles)
        {
            float t = obs.RayIntersection(originX, originY, dirX, dirY, closestAny);
            if (t < closestPhysical) closestPhysical = t;
            if (t < closestAny) { closestAny = t; hitType = EntityType.Obstacle; }
        }

        foreach (var haz in _hazards)
        {
            float t = haz.RayIntersection(originX, originY, dirX, dirY, closestAny);
            if (t < closestPhysical) closestPhysical = t;
            if (t < closestAny) { closestAny = t; hitType = EntityType.Hazard; }
        }

        foreach (var f in _food)
        {
            if (f.Consumed) continue;
            float t = WorldHelpers.RayCircleIntersection(
                originX, originY, dirX, dirY, f.X, f.Y, f.Radius, closestAny);
            if (t < closestAny) { closestAny = t; hitType = EntityType.Food; }
        }

        return (closestPhysical, (int)hitType);
    }

    public float RaycastDistance(float originX, float originY, float dirX, float dirY, float maxDistance)
        => Raycast(originX, originY, dirX, dirY, maxDistance).distance;

    public int RaycastType(float originX, float originY, float dirX, float dirY, float maxDistance)
        => Raycast(originX, originY, dirX, dirY, maxDistance).hitType;

    public (float dx, float dy) FoodGradient(float x, float y)
    {
        float nearestDistSq = float.MaxValue;
        float nearestDx = 0f;
        float nearestDy = 0f;

        foreach (var f in _food)
        {
            if (f.Consumed) continue;
            float dx = f.X - x;
            float dy = f.Y - y;
            float distSq = dx * dx + dy * dy;

            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearestDx = dx;
                nearestDy = dy;
            }
        }

        if (nearestDistSq < 1e-6f || nearestDistSq == float.MaxValue)
            return (0, 0);

        float dist = MathF.Sqrt(nearestDistSq);
        return (nearestDx / dist, nearestDy / dist);
    }

    public (float s0, float s1) NearbySignals(float x, float y) => (0, 0);
    public (float dx, float dy) SignalGradient(float x, float y) => (0, 0);
    public float NearestAgentEnergy(float x, float y) => 0f;
    public float NearbyAgentDensity(float x, float y) => 0f;
    public (float shareReceived, float attackReceived) InteractionFeedback() => (0f, 0f);

    private float RayWallDistance(float ox, float oy, float dx, float dy, float maxDist)
        => WorldHelpers.RayWallDistance(ox, oy, dx, dy, _budget.WorldWidth, _budget.WorldHeight, maxDist);
}

