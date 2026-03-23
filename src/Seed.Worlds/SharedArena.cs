using Seed.Core;

namespace Seed.Worlds;

public struct ArenaAgent
{
    public float X, Y, Heading, Speed, Energy;
    public bool Alive;
    public float Signal0, Signal1;
    public float ShareReceived, AttackReceived;
}

public sealed class SharedArena
{
    private WorldBudget _budget;
    private ArenaAgent[] _agents = Array.Empty<ArenaAgent>();
    private List<AABB> _obstacles = new();
    private List<AABB> _hazards = new();
    private List<FoodItem> _food = new();
    private Rng64 _worldRng;
    private int _tick;
    private SpatialGrid _grid = null!;

    private float[] _clusterX = Array.Empty<float>();
    private float[] _clusterY = Array.Empty<float>();
    private float _clusterSpread;

    private WorldStepResult[] _results = Array.Empty<WorldStepResult>();
    private float[] _hazardPenalties = Array.Empty<float>();
    private int[] _foodCollected = Array.Empty<int>();
    private float[] _foodEnergy = Array.Empty<float>();
    private float[][] _modulators = Array.Empty<float[]>();
    private float[] _interactionGain = Array.Empty<float>();
    private float[] _interactionLoss = Array.Empty<float>();

    public int AgentCount => _agents.Length;

    public float AgentX(int i) => _agents[i].X;
    public float AgentY(int i) => _agents[i].Y;
    public float AgentHeading(int i) => _agents[i].Heading;
    public float AgentSpeed(int i) => _agents[i].Speed;
    public float AgentEnergy(int i) => _agents[i].Energy;
    public bool AgentAlive(int i) => _agents[i].Alive;

    public ReadOnlySpan<ArenaAgent> Agents => _agents;
    public IReadOnlyList<FoodItem> GetFoodItems() => _food;
    public IReadOnlyList<AABB> GetObstacles() => _obstacles;
    public IReadOnlyList<AABB> GetHazards() => _hazards;

    public void Reset(ulong seed, in WorldBudget budget, int agentCount)
    {
        _budget = budget;
        _tick = 0;

        var rng = new Rng64(seed);
        GenerateWorld(ref rng, agentCount);
        PlaceAgents(ref rng, agentCount);
        _worldRng = rng;

        _grid = new SpatialGrid(_budget.WorldWidth, _budget.WorldHeight, 10f);
        _grid.Rebuild(_agents);

        if (_results.Length != agentCount)
        {
            _results = new WorldStepResult[agentCount];
            _hazardPenalties = new float[agentCount];
            _foodCollected = new int[agentCount];
            _foodEnergy = new float[agentCount];
            _interactionGain = new float[agentCount];
            _interactionLoss = new float[agentCount];
            _modulators = new float[agentCount][];
            for (int i = 0; i < agentCount; i++)
                _modulators[i] = new float[ModulatorIndex.Count];
        }
    }

    public WorldStepResult[] StepAll(float[][] actionsPerAgent)
    {
        int n = _agents.Length;

        // 1. Apply thrust/turn + signal extraction per agent
        for (int i = 0; i < n; i++)
        {
            if (!_agents[i].Alive)
            {
                _agents[i].Signal0 = 0;
                _agents[i].Signal1 = 0;
                continue;
            }

            float thrust = actionsPerAgent[i].Length > 0
                ? DeterministicHelpers.Clamp(actionsPerAgent[i][0], -1f, 1f) : 0f;
            float turn = actionsPerAgent[i].Length > 1
                ? DeterministicHelpers.Clamp(actionsPerAgent[i][1], -1f, 1f) : 0f;

            float sig0 = actionsPerAgent[i].Length > 2
                ? DeterministicHelpers.Clamp(actionsPerAgent[i][2], -1f, 1f) : 0f;
            float sig1 = actionsPerAgent[i].Length > 3
                ? DeterministicHelpers.Clamp(actionsPerAgent[i][3], -1f, 1f) : 0f;
            _agents[i].Signal0 = sig0;
            _agents[i].Signal1 = sig1;

            _agents[i].Heading += turn * ContinuousWorld.TurnRate;
            while (_agents[i].Heading < 0) _agents[i].Heading += MathF.PI * 2f;
            while (_agents[i].Heading >= MathF.PI * 2f) _agents[i].Heading -= MathF.PI * 2f;

            _agents[i].Speed += thrust * ContinuousWorld.Acceleration;
            _agents[i].Speed *= (1f - ContinuousWorld.Friction);
            _agents[i].Speed = DeterministicHelpers.Clamp(
                _agents[i].Speed, -ContinuousWorld.MaxSpeed, ContinuousWorld.MaxSpeed);

            float dx = MathF.Cos(_agents[i].Heading) * _agents[i].Speed;
            float dy = MathF.Sin(_agents[i].Heading) * _agents[i].Speed;
            _agents[i].X += dx;
            _agents[i].Y += dy;

            // Wall clamp
            _agents[i].X = DeterministicHelpers.Clamp(
                _agents[i].X, ContinuousWorld.AgentRadius, _budget.WorldWidth - ContinuousWorld.AgentRadius);
            _agents[i].Y = DeterministicHelpers.Clamp(
                _agents[i].Y, ContinuousWorld.AgentRadius, _budget.WorldHeight - ContinuousWorld.AgentRadius);
        }

        // 2. Resolve agent-obstacle collisions
        for (int i = 0; i < n; i++)
        {
            if (!_agents[i].Alive) continue;
            foreach (var obs in _obstacles)
            {
                var (depth, pushX, pushY) = obs.CirclePenetration(
                    _agents[i].X, _agents[i].Y, ContinuousWorld.AgentRadius);
                if (depth > 0)
                {
                    _agents[i].X += pushX * depth * 1.01f;
                    _agents[i].Y += pushY * depth * 1.01f;
                    _agents[i].Speed *= 0.5f;
                }
            }
        }

        // Rebuild spatial grid after movement + obstacle push (before agent-agent collision)
        _grid.Rebuild(_agents);

        // 3. Resolve agent-agent collisions (2 passes, grid-accelerated)
        float minDist = ContinuousWorld.AgentRadius * 2f;
        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < n; i++)
            {
                if (!_agents[i].Alive) continue;
                var (col, row) = _grid.CellOf(_agents[i].X, _agents[i].Y);
                for (int dc = -1; dc <= 1; dc++)
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        int nc = col + dc, nr = row + dr;
                        if (nc < 0 || nc >= _grid.Cols || nr < 0 || nr >= _grid.Rows) continue;
                        foreach (int j in _grid.GetCellContents(nc, nr))
                        {
                            if (j <= i || !_agents[j].Alive) continue;
                            float adx = _agents[j].X - _agents[i].X;
                            float ady = _agents[j].Y - _agents[i].Y;
                            float distSq = adx * adx + ady * ady;

                            if (distSq < minDist * minDist && distSq > 1e-10f)
                            {
                                float dist = MathF.Sqrt(distSq);
                                float penetration = minDist - dist;
                                float nx = adx / dist;
                                float ny = ady / dist;
                                float halfPen = penetration * 0.5f * 1.01f;

                                _agents[i].X -= nx * halfPen;
                                _agents[i].Y -= ny * halfPen;
                                _agents[j].X += nx * halfPen;
                                _agents[j].Y += ny * halfPen;

                                _agents[i].Speed *= 0.5f;
                                _agents[j].Speed *= 0.5f;
                            }
                            else if (distSq <= 1e-10f)
                            {
                                _agents[j].X += ContinuousWorld.AgentRadius;
                                _agents[j].Speed *= 0.5f;
                                _agents[i].Speed *= 0.5f;
                            }
                        }
                    }
            }
        }

        // 4. Hazard damage per agent
        Array.Clear(_hazardPenalties, 0, n);
        for (int i = 0; i < n; i++)
        {
            if (!_agents[i].Alive) continue;
            foreach (var haz in _hazards)
            {
                if (haz.OverlapsCircle(_agents[i].X, _agents[i].Y, ContinuousWorld.AgentRadius))
                    _hazardPenalties[i] += ContinuousWorld.HazardDamage;
            }
        }

        // 5. Food competition -- grid-accelerated closest alive agent within collection radius
        float energyMultiplier = 1f;
        if (_budget.FoodEnergyAmplitude > 0f && _budget.FoodEnergyPeriod > 0)
        {
            float phase = 2f * MathF.PI * _tick / _budget.FoodEnergyPeriod;
            energyMultiplier = 1f + _budget.FoodEnergyAmplitude * MathF.Sin(phase);
        }

        Array.Clear(_foodCollected, 0, n);
        Array.Clear(_foodEnergy, 0, n);
        for (int fi = 0; fi < _food.Count; fi++)
        {
            var f = _food[fi];
            if (f.Consumed) continue;

            float bestDistSq = float.MaxValue;
            int bestAgent = -1;
            float collectDist = ContinuousWorld.AgentRadius + f.Radius + ContinuousWorld.FoodCollectionRadius;
            float collectDistSq = collectDist * collectDist;

            var (fcol, frow) = _grid.CellOf(f.X, f.Y);
            int cellRadius = (int)MathF.Ceiling(collectDist / _grid.CellSize);
            for (int dc = -cellRadius; dc <= cellRadius; dc++)
                for (int dr = -cellRadius; dr <= cellRadius; dr++)
                {
                    int nc = fcol + dc, nr = frow + dr;
                    if (nc < 0 || nc >= _grid.Cols || nr < 0 || nr >= _grid.Rows) continue;
                    foreach (int i in _grid.GetCellContents(nc, nr))
                    {
                        if (!_agents[i].Alive) continue;
                        float fdx = _agents[i].X - f.X;
                        float fdy = _agents[i].Y - f.Y;
                        float dSq = fdx * fdx + fdy * fdy;
                        if (dSq <= collectDistSq && dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            bestAgent = i;
                        }
                    }
                }

            if (bestAgent >= 0)
            {
                _food[fi] = f with { Consumed = true, RespawnTick = _tick + ContinuousWorld.FoodRespawnDelay };
                _foodEnergy[bestAgent] += f.EnergyValue * energyMultiplier;
                _foodCollected[bestAgent]++;
            }
        }

        // 6. Food respawn
        RespawnFood();

        // 6.5 Agent interactions (share/attack)
        Array.Clear(_interactionGain, 0, n);
        Array.Clear(_interactionLoss, 0, n);
        for (int i = 0; i < n; i++)
        {
            _agents[i].ShareReceived = 0;
            _agents[i].AttackReceived = 0;
        }

        for (int i = 0; i < n; i++)
        {
            if (!_agents[i].Alive) continue;

            float shareVal = actionsPerAgent[i].Length > ContinuousWorld.ActuatorShare
                ? Math.Max(0f, DeterministicHelpers.Clamp(actionsPerAgent[i][ContinuousWorld.ActuatorShare], -1f, 1f))
                : 0f;
            float attackVal = actionsPerAgent[i].Length > ContinuousWorld.ActuatorAttack
                ? Math.Max(0f, DeterministicHelpers.Clamp(actionsPerAgent[i][ContinuousWorld.ActuatorAttack], -1f, 1f))
                : 0f;

            if (shareVal <= 0f && attackVal <= 0f) continue;

            int target = FindNearestAliveAgent(i, _agents[i].X, _agents[i].Y, ContinuousWorld.InteractionRadius);
            if (target < 0) continue;

            if (shareVal > 0f)
            {
                float amount = shareVal * ContinuousWorld.ShareRate * _agents[i].Energy;
                _interactionLoss[i] += amount;
                float received = amount * ContinuousWorld.ShareEfficiency;
                _interactionGain[target] += received;
                _agents[target].ShareReceived += received;
            }

            if (attackVal > 0f)
            {
                float cost = attackVal * ContinuousWorld.AttackCost;
                _interactionLoss[i] += cost;
                float drain = attackVal * ContinuousWorld.AttackDrainRate * _agents[target].Energy;
                _interactionLoss[target] += drain;
                _agents[target].AttackReceived += drain;
                _interactionGain[i] += drain * ContinuousWorld.AttackEfficiency;
            }
        }

        // 7. Energy update + death check, build results
        for (int i = 0; i < n; i++)
        {
            if (!_agents[i].Alive)
            {
                Array.Clear(_modulators[i]);
                _results[i] = new WorldStepResult(
                    Done: true,
                    Signals: new WorldSignals(0, 0, 0),
                    Modulators: _modulators[i],
                    Info: new WorldStepInfo());
                continue;
            }

            float movementCost = MathF.Abs(_agents[i].Speed) * ContinuousWorld.MovementEnergyCost;
            float totalCost = ContinuousWorld.BaseEnergyCost + movementCost + _hazardPenalties[i];
            float energyDelta = _foodEnergy[i] + _interactionGain[i] - _interactionLoss[i] - totalCost;
            _agents[i].Energy += energyDelta;

            if (_agents[i].Energy <= 0)
            {
                _agents[i].Energy = 0;
                _agents[i].Alive = false;
                _agents[i].Signal0 = 0;
                _agents[i].Signal1 = 0;
                _agents[i].ShareReceived = 0;
                _agents[i].AttackReceived = 0;
            }

            _modulators[i][ModulatorIndex.Reward] = Math.Max(0, _foodEnergy[i] + _interactionGain[i]);
            _modulators[i][ModulatorIndex.Pain] = Math.Max(0, totalCost) + _hazardPenalties[i] + _interactionLoss[i];
            if (ModulatorIndex.Count > 2)
                _modulators[i][ModulatorIndex.Curiosity] = 0f;

            _results[i] = new WorldStepResult(
                Done: !_agents[i].Alive,
                Signals: new WorldSignals(energyDelta, _foodCollected[i], _hazardPenalties[i]),
                Modulators: _modulators[i],
                Info: new WorldStepInfo());
        }

        _tick++;
        return _results;
    }

    public (float distance, int hitType) Raycast(int agentIndex, float originX, float originY,
        float dirX, float dirY, float maxDistance)
    {
        float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
        if (len < 1e-6f) return (maxDistance, (int)EntityType.None);
        dirX /= len;
        dirY /= len;

        float closestPhysical = maxDistance;
        float closestAny = maxDistance;
        EntityType hitType = EntityType.None;

        float wallDist = WorldHelpers.RayWallDistance(
            originX, originY, dirX, dirY,
            _budget.WorldWidth, _budget.WorldHeight, maxDistance);
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

        var bounds = _grid.GetRayBounds(originX, originY, dirX, dirY, closestPhysical);
        for (int c = bounds.minCol; c <= bounds.maxCol; c++)
            for (int r = bounds.minRow; r <= bounds.maxRow; r++)
                foreach (int i in _grid.GetCellContents(c, r))
                {
                    if (i == agentIndex || !_agents[i].Alive) continue;
                    float t = WorldHelpers.RayCircleIntersection(
                        originX, originY, dirX, dirY,
                        _agents[i].X, _agents[i].Y, ContinuousWorld.AgentRadius,
                        closestPhysical);
                    if (t < closestPhysical) closestPhysical = t;
                    if (t < closestAny) { closestAny = t; hitType = EntityType.Agent; }
                }

        return (closestPhysical, (int)hitType);
    }

    public float RaycastDistance(int agentIndex, float originX, float originY,
        float dirX, float dirY, float maxDistance)
        => Raycast(agentIndex, originX, originY, dirX, dirY, maxDistance).distance;

    public int RaycastType(int agentIndex, float originX, float originY,
        float dirX, float dirY, float maxDistance)
        => Raycast(agentIndex, originX, originY, dirX, dirY, maxDistance).hitType;

    public (float dx, float dy) FoodGradient(float x, float y)
    {
        float nearestDistSq = float.MaxValue;
        float nearestDx = 0f;
        float nearestDy = 0f;

        foreach (var f in _food)
        {
            if (f.Consumed) continue;
            float fdx = f.X - x;
            float fdy = f.Y - y;
            float distSq = fdx * fdx + fdy * fdy;
            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearestDx = fdx;
                nearestDy = fdy;
            }
        }

        if (nearestDistSq < 1e-6f || nearestDistSq == float.MaxValue)
            return (0, 0);

        float dist = MathF.Sqrt(nearestDistSq);
        return (nearestDx / dist, nearestDy / dist);
    }

    public (float s0, float s1) NearbySignals(int agentIndex, float x, float y, float radius)
    {
        float s0Sum = 0, s1Sum = 0;
        int count = 0;
        float radiusSq = radius * radius;
        var (col, row) = _grid.CellOf(x, y);
        int cellRadius = (int)MathF.Ceiling(radius / _grid.CellSize);
        for (int dc = -cellRadius; dc <= cellRadius; dc++)
            for (int dr = -cellRadius; dr <= cellRadius; dr++)
            {
                int nc = col + dc, nr = row + dr;
                if (nc < 0 || nc >= _grid.Cols || nr < 0 || nr >= _grid.Rows) continue;
                foreach (int i in _grid.GetCellContents(nc, nr))
                {
                    if (i == agentIndex || !_agents[i].Alive) continue;
                    float dx = _agents[i].X - x, dy = _agents[i].Y - y;
                    float distSq = dx * dx + dy * dy;
                    if (distSq > radiusSq) continue;
                    float dist = MathF.Sqrt(distSq);
                    float w = 1f / MathF.Max(dist, 0.5f);
                    s0Sum += _agents[i].Signal0 * w;
                    s1Sum += _agents[i].Signal1 * w;
                    count++;
                }
            }
        if (count > 0) { s0Sum /= count; s1Sum /= count; }
        return (DeterministicHelpers.Clamp(s0Sum, -1f, 1f),
                DeterministicHelpers.Clamp(s1Sum, -1f, 1f));
    }

    public (float dx, float dy) SignalGradient(int agentIndex, float x, float y, float radius)
    {
        float nearestDistSq = float.MaxValue;
        float nearestDx = 0f, nearestDy = 0f;
        float signalThreshold = 0.01f;
        float radiusSq = radius * radius;

        var (col, row) = _grid.CellOf(x, y);
        int cellRadius = (int)MathF.Ceiling(radius / _grid.CellSize);
        for (int dc = -cellRadius; dc <= cellRadius; dc++)
            for (int dr = -cellRadius; dr <= cellRadius; dr++)
            {
                int nc = col + dc, nr = row + dr;
                if (nc < 0 || nc >= _grid.Cols || nr < 0 || nr >= _grid.Rows) continue;
                foreach (int i in _grid.GetCellContents(nc, nr))
                {
                    if (i == agentIndex || !_agents[i].Alive) continue;
                    float totalSig = MathF.Abs(_agents[i].Signal0) + MathF.Abs(_agents[i].Signal1);
                    if (totalSig < signalThreshold) continue;
                    float ddx = _agents[i].X - x, ddy = _agents[i].Y - y;
                    float distSq = ddx * ddx + ddy * ddy;
                    if (distSq > radiusSq || distSq < 1e-6f) continue;
                    if (distSq < nearestDistSq)
                    {
                        nearestDistSq = distSq;
                        nearestDx = ddx;
                        nearestDy = ddy;
                    }
                }
            }

        if (nearestDistSq == float.MaxValue) return (0, 0);
        float dist = MathF.Sqrt(nearestDistSq);
        return (nearestDx / dist, nearestDy / dist);
    }

    private int FindNearestAliveAgent(int agentIndex, float x, float y, float radius)
    {
        float bestDistSq = radius * radius;
        int bestAgent = -1;
        var (col, row) = _grid.CellOf(x, y);
        int cellRadius = (int)MathF.Ceiling(radius / _grid.CellSize);
        for (int dc = -cellRadius; dc <= cellRadius; dc++)
            for (int dr = -cellRadius; dr <= cellRadius; dr++)
            {
                int nc = col + dc, nr = row + dr;
                if (nc < 0 || nc >= _grid.Cols || nr < 0 || nr >= _grid.Rows) continue;
                foreach (int i in _grid.GetCellContents(nc, nr))
                {
                    if (i == agentIndex || !_agents[i].Alive) continue;
                    float dx = _agents[i].X - x, dy = _agents[i].Y - y;
                    float distSq = dx * dx + dy * dy;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestAgent = i;
                    }
                }
            }
        return bestAgent;
    }

    public float NearestAgentEnergy(int agentIndex, float x, float y, float radius)
    {
        int nearest = FindNearestAliveAgent(agentIndex, x, y, radius);
        return nearest >= 0 ? _agents[nearest].Energy : 0f;
    }

    public float NearbyAgentDensity(int agentIndex, float x, float y, float radius)
    {
        int count = 0;
        float radiusSq = radius * radius;
        var (col, row) = _grid.CellOf(x, y);
        int cellRadius = (int)MathF.Ceiling(radius / _grid.CellSize);
        for (int dc = -cellRadius; dc <= cellRadius; dc++)
            for (int dr = -cellRadius; dr <= cellRadius; dr++)
            {
                int nc = col + dc, nr = row + dr;
                if (nc < 0 || nc >= _grid.Cols || nr < 0 || nr >= _grid.Rows) continue;
                foreach (int i in _grid.GetCellContents(nc, nr))
                {
                    if (i == agentIndex || !_agents[i].Alive) continue;
                    float dx = _agents[i].X - x, dy = _agents[i].Y - y;
                    if (dx * dx + dy * dy <= radiusSq) count++;
                }
            }
        return DeterministicHelpers.Clamp(count / 4f, 0f, 1f);
    }

    private void GenerateWorld(ref Rng64 rng, int agentCount)
    {
        _obstacles.Clear();
        _hazards.Clear();
        _food.Clear();

        float margin = 2f;
        int obstacleCount = (int)(_budget.WorldWidth * _budget.WorldHeight * _budget.ObstacleDensity / 16f);
        int hazardCount = (int)(_budget.WorldWidth * _budget.WorldHeight * _budget.HazardDensity / 16f);
        int effectiveFoodCount = Math.Max(_budget.FoodCount, agentCount * 2);

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
                bool valid = true;
                foreach (var obs in _obstacles)
                {
                    if (WorldHelpers.AABBsOverlap(aabb, obs, 0.5f)) { valid = false; break; }
                }
                if (valid) { _obstacles.Add(aabb); id++; break; }
            }
        }

        for (int i = 0; i < hazardCount; i++)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                float w = rng.NextFloat(1f, 3f);
                float h = rng.NextFloat(1f, 3f);
                float x = rng.NextFloat(margin, _budget.WorldWidth - margin - w);
                float y = rng.NextFloat(margin, _budget.WorldHeight - margin - h);

                var aabb = new AABB(id, x, y, x + w, y + h);
                bool valid = true;
                foreach (var obs in _obstacles)
                {
                    if (WorldHelpers.AABBsOverlap(aabb, obs, 0.5f)) { valid = false; break; }
                }
                if (valid) { _hazards.Add(aabb); id++; break; }
            }
        }

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

            for (int i = 0; i < effectiveFoodCount; i++)
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
                        if (obs.Contains(x, y, ContinuousWorld.FoodCollectionRadius))
                        { valid = false; break; }
                    if (valid) { _food.Add(new FoodItem(i, x, y, 0.3f, 0.2f, false)); break; }
                }
            }
        }
        else
        {
            _clusterX = Array.Empty<float>();
            _clusterY = Array.Empty<float>();
            for (int i = 0; i < effectiveFoodCount; i++)
            {
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    float x = rng.NextFloat(margin, _budget.WorldWidth - margin);
                    float y = rng.NextFloat(margin, _budget.WorldHeight - margin);

                    bool valid = true;
                    foreach (var obs in _obstacles)
                    {
                        if (obs.Contains(x, y, ContinuousWorld.FoodCollectionRadius)) { valid = false; break; }
                    }
                    if (valid) { _food.Add(new FoodItem(i, x, y, 0.3f, 0.2f, false)); break; }
                }
            }
        }

        _obstacles.Sort((a, b) => a.Id.CompareTo(b.Id));
        _hazards.Sort((a, b) => a.Id.CompareTo(b.Id));
        _food.Sort((a, b) => a.Id.CompareTo(b.Id));
    }

    private void PlaceAgents(ref Rng64 rng, int agentCount)
    {
        _agents = new ArenaAgent[agentCount];
        float margin = 3f;
        float fullSep = ContinuousWorld.AgentRadius * 3f;
        float relaxedSep = ContinuousWorld.AgentRadius * 2f;

        for (int a = 0; a < agentCount; a++)
        {
            bool placed = false;

            // Pass 1: full separation
            for (int attempt = 0; attempt < 200 && !placed; attempt++)
            {
                float x = rng.NextFloat(margin, _budget.WorldWidth - margin);
                float y = rng.NextFloat(margin, _budget.WorldHeight - margin);
                if (IsValidAgentPosition(x, y, a, fullSep))
                {
                    _agents[a] = new ArenaAgent
                    {
                        X = x, Y = y,
                        Heading = rng.NextFloat(0f, MathF.PI * 2f),
                        Speed = 0f, Energy = ContinuousWorld.InitialEnergy, Alive = true
                    };
                    placed = true;
                }
            }

            // Pass 2: relaxed separation
            for (int attempt = 0; attempt < 200 && !placed; attempt++)
            {
                float x = rng.NextFloat(margin, _budget.WorldWidth - margin);
                float y = rng.NextFloat(margin, _budget.WorldHeight - margin);
                if (IsValidAgentPosition(x, y, a, relaxedSep))
                {
                    _agents[a] = new ArenaAgent
                    {
                        X = x, Y = y,
                        Heading = rng.NextFloat(0f, MathF.PI * 2f),
                        Speed = 0f, Energy = ContinuousWorld.InitialEnergy, Alive = true
                    };
                    placed = true;
                }
            }

            // Pass 3: fallback -- obstacle-free, no separation guarantee
            if (!placed)
            {
                float x = rng.NextFloat(margin, _budget.WorldWidth - margin);
                float y = rng.NextFloat(margin, _budget.WorldHeight - margin);
                _agents[a] = new ArenaAgent
                {
                    X = x, Y = y,
                    Heading = rng.NextFloat(0f, MathF.PI * 2f),
                    Speed = 0f, Energy = ContinuousWorld.InitialEnergy, Alive = true
                };
            }
        }
    }

    private bool IsValidAgentPosition(float x, float y, int placedSoFar, float minSeparation)
    {
        foreach (var obs in _obstacles)
        {
            if (obs.OverlapsCircle(x, y, ContinuousWorld.AgentRadius + 0.5f))
                return false;
        }

        foreach (var haz in _hazards)
        {
            if (haz.OverlapsCircle(x, y, ContinuousWorld.AgentRadius + 0.5f))
                return false;
        }

        for (int i = 0; i < placedSoFar; i++)
        {
            float adx = _agents[i].X - x;
            float ady = _agents[i].Y - y;
            if (adx * adx + ady * ady < minSeparation * minSeparation)
                return false;
        }

        return true;
    }

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
                        if (obs.Contains(x, y, ContinuousWorld.FoodCollectionRadius))
                        { valid = false; break; }
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
}
