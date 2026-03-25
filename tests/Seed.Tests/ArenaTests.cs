using Seed.Core;
using Seed.Worlds;
using Seed.Agents;
using Seed.Evolution;
using Seed.Genetics;

namespace Seed.Tests;

public class ArenaTests
{
    private static readonly WorldBudget TestBudget = new(32, 32, 0f, 0f, 10);

    [Fact]
    public void AgentCollision_MutualPushOut()
    {
        var arena = new SharedArena();
        arena.Reset(42, TestBudget, 2);

        var agents = arena.Agents;
        float initialDist = Distance(agents[0], agents[1]);

        var actions = new float[][] { new float[ContinuousWorld.ActuatorCount], new float[ContinuousWorld.ActuatorCount] };
        arena.StepAll(actions);

        var after = arena.Agents;
        float afterDist = Distance(after[0], after[1]);

        if (initialDist < ContinuousWorld.AgentRadius * 2f)
            Assert.True(afterDist >= initialDist, "Overlapping agents should be pushed apart");
    }

    [Fact]
    public void FoodCompetition_CloserAgentWins()
    {
        var arena = new SharedArena();
        arena.Reset(1234, new WorldBudget(64, 64, 0f, 0f, 50), 2);

        int totalSteps = 100;
        var actions = new float[][] { new float[] { 1, 0, 0, 0 }, new float[] { 1, 0, 0, 0 } };
        int agent0Food = 0, agent1Food = 0;

        for (int t = 0; t < totalSteps; t++)
        {
            var results = arena.StepAll(actions);
            agent0Food += results[0].Signals.FoodCollectedThisStep;
            agent1Food += results[1].Signals.FoodCollectedThisStep;
        }

        Assert.True(agent0Food + agent1Food > 0, "At least one agent should collect food");
    }

    [Fact]
    public void FoodScaling_RespectsBudget()
    {
        var arena = new SharedArena();
        var budget = new WorldBudget(64, 64, 0f, 0f, 5);
        arena.Reset(42, budget, 10);

        int foodCount = arena.GetFoodItems().Count;
        Assert.True(foodCount <= 5, $"Expected <= 5 food items (budget FoodCount), got {foodCount}");
    }

    [Fact]
    public void AgentRaycast_SeesOtherAgent()
    {
        var arena = new SharedArena();
        arena.Reset(99, new WorldBudget(64, 64, 0f, 0f, 0), 2);

        float dx = arena.AgentX(1) - arena.AgentX(0);
        float dy = arena.AgentY(1) - arena.AgentY(0);
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6f) return;

        float ndx = dx / len;
        float ndy = dy / len;

        int hitType = arena.RaycastType(0, arena.AgentX(0), arena.AgentY(0), ndx, ndy, 100f);
        float hitDist = arena.RaycastDistance(0, arena.AgentX(0), arena.AgentY(0), ndx, ndy, 100f);

        Assert.Equal((int)EntityType.Agent, hitType);
        float expectedDist = len - ContinuousWorld.AgentRadius;
        Assert.True(MathF.Abs(hitDist - expectedDist) < 1.0f,
            $"Expected hit distance ~{expectedDist:F2}, got {hitDist:F2}");
    }

    [Fact]
    public void AgentRaycast_DistanceIncludesAgents()
    {
        var arena = new SharedArena();
        arena.Reset(77, new WorldBudget(64, 64, 0f, 0f, 0), 2);

        float dx = arena.AgentX(1) - arena.AgentX(0);
        float dy = arena.AgentY(1) - arena.AgentY(0);
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6f) return;

        float ndx = dx / len;
        float ndy = dy / len;

        float hitDist = arena.RaycastDistance(0, arena.AgentX(0), arena.AgentY(0), ndx, ndy, 100f);
        Assert.True(hitDist < 100f, "RaycastDistance should detect other agents as physical bodies");
    }

    [Fact]
    public void DeadAgent_InvisibleToRaycast()
    {
        var arena = new SharedArena();
        arena.Reset(55, new WorldBudget(64, 64, 0f, 0f, 0), 2);

        float dx = arena.AgentX(1) - arena.AgentX(0);
        float dy = arena.AgentY(1) - arena.AgentY(0);
        float len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6f) return;

        float ndx = dx / len;
        float ndy = dy / len;

        float distBefore = arena.RaycastDistance(0, arena.AgentX(0), arena.AgentY(0), ndx, ndy, 100f);

        var actions = new float[2][];
        for (int i = 0; i < 2; i++) actions[i] = new float[ContinuousWorld.ActuatorCount];

        for (int t = 0; t < 10000; t++)
        {
            arena.StepAll(actions);
            if (!arena.AgentAlive(1)) break;
        }

        if (!arena.AgentAlive(1))
        {
            dx = arena.AgentX(1) - arena.AgentX(0);
            dy = arena.AgentY(1) - arena.AgentY(0);
            len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 1e-6f)
            {
                ndx = dx / len;
                ndy = dy / len;
                float distAfter = arena.RaycastDistance(0, arena.AgentX(0), arena.AgentY(0), ndx, ndy, 100f);
                Assert.True(distAfter > distBefore || distAfter >= 100f,
                    "Dead agent should not be detected by raycasts");
            }
        }
    }

    [Fact]
    public void ArenaDeterminism_SameSeed_SameResult()
    {
        var budget = new WorldBudget(32, 32, 0.05f, 0.02f, 10);

        var final1 = RunArena(42, budget, 4, 50);
        var final2 = RunArena(42, budget, 4, 50);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(final1[i].X, final2[i].X);
            Assert.Equal(final1[i].Y, final2[i].Y);
            Assert.Equal(final1[i].Energy, final2[i].Energy);
            Assert.Equal(final1[i].Alive, final2[i].Alive);
        }
    }

    [Fact]
    public void SensorCount_Is62()
    {
        Assert.Equal(62, AgentConfig.Default.TotalSensorCount);
    }

    [Fact]
    public void AgentView_StepThrows()
    {
        var arena = new SharedArena();
        arena.Reset(1, new WorldBudget(), 1);
        var view = new AgentView(arena, 0);

        Assert.Throws<NotSupportedException>(() => view.Step(ReadOnlySpan<float>.Empty));
    }

    [Fact]
    public void AgentView_ResetThrows()
    {
        var arena = new SharedArena();
        arena.Reset(1, new WorldBudget(), 1);
        var view = new AgentView(arena, 0);

        Assert.Throws<NotSupportedException>(() => view.Reset(0, new WorldBudget()));
    }

    [Fact]
    public void ArenaEvaluation_AllAgentsGetScores()
    {
        var rng = new Rng64(42);
        var population = new List<IGenome>();
        for (int i = 0; i < 4; i++)
            population.Add(SeedGenome.CreateRandom(rng));

        var evaluator = new Evaluator(AgentConfig.Default.TotalSensorCount, ContinuousWorld.ActuatorCount);
        var ctx = new EvaluationContext(
            RunSeed: 42,
            GenerationIndex: 0,
            WorldBundleKey: 0,
            DevelopmentBudget: new DevelopmentBudget(),
            RuntimeBudget: new RuntimeBudget(MaxTicksPerEpisode: 50),
            WorldBudget: new WorldBudget(),
            ArenaRounds: 2
        );

        var results = evaluator.EvaluateArena(population, ctx);

        Assert.Equal(4, results.Count);
        foreach (var r in results.Values)
        {
            Assert.Equal(2, r.PerWorld.Length);
            Assert.True(float.IsFinite(r.Aggregate.Score), "Score should be finite");
        }
    }

    [Fact]
    public void SpatialGrid_Rebuild_FindsAgentsInCorrectCells()
    {
        var arena = new SharedArena();
        var budget = new WorldBudget(64, 64, 0f, 0f, 0);
        arena.Reset(123, budget, 8);

        for (int i = 0; i < arena.AgentCount; i++)
        {
            float ax = arena.AgentX(i);
            float ay = arena.AgentY(i);

            bool found = false;
            int hitType = arena.RaycastType(i, ax, ay, 1f, 0f, 0.01f);
            float hitDist = arena.RaycastDistance(i, ax, ay, 1f, 0f, 0.01f);
            Assert.True(ax >= 0 && ax <= 64 && ay >= 0 && ay <= 64,
                $"Agent {i} position ({ax},{ay}) should be within world bounds");
            found = true;
            Assert.True(found);
        }
    }

    [Fact]
    public void SpatialGrid_GetRayBounds_CoversRayPath()
    {
        var arena = new SharedArena();
        var budget = new WorldBudget(64, 64, 0f, 0f, 0);
        arena.Reset(77, budget, 4);

        for (int i = 0; i < arena.AgentCount; i++)
        {
            for (int j = 0; j < arena.AgentCount; j++)
            {
                if (i == j) continue;
                float dx = arena.AgentX(j) - arena.AgentX(i);
                float dy = arena.AgentY(j) - arena.AgentY(i);
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6f) continue;

                var (dist, hitType) = arena.Raycast(i, arena.AgentX(i), arena.AgentY(i),
                    dx / len, dy / len, 100f);
                Assert.True(dist < 100f,
                    $"Agent {i} should detect agent {j} at distance {len:F2} via grid-accelerated Raycast");
            }
        }
    }

    [Fact]
    public void MergedRaycast_MatchesSeparateCalls()
    {
        var arena = new SharedArena();
        var budget = new WorldBudget(64, 64, 0.1f, 0.05f, 20);
        arena.Reset(42, budget, 8);

        var actions = new float[8][];
        for (int i = 0; i < 8; i++) actions[i] = new float[] { 0.5f, 0.2f, 0, 0 };
        for (int t = 0; t < 20; t++) arena.StepAll(actions);

        float[] angles = { 0f, MathF.PI / 4f, MathF.PI / 2f, MathF.PI, -MathF.PI / 4f };

        for (int agent = 0; agent < arena.AgentCount; agent++)
        {
            if (!arena.AgentAlive(agent)) continue;

            foreach (float angle in angles)
            {
                float dirX = MathF.Cos(angle);
                float dirY = MathF.Sin(angle);

                float separateDist = arena.RaycastDistance(agent,
                    arena.AgentX(agent), arena.AgentY(agent), dirX, dirY, 10f);
                int separateType = arena.RaycastType(agent,
                    arena.AgentX(agent), arena.AgentY(agent), dirX, dirY, 10f);

                var (mergedDist, mergedType) = arena.Raycast(agent,
                    arena.AgentX(agent), arena.AgentY(agent), dirX, dirY, 10f);

                Assert.Equal(separateDist, mergedDist);
                Assert.Equal(separateType, mergedType);
            }
        }
    }

    [Fact]
    public void MergedRaycast_FoodCloserThanPhysical()
    {
        var arena = new SharedArena();
        var budget = new WorldBudget(64, 64, 0f, 0f, 50);
        arena.Reset(999, budget, 1);

        float ax = arena.AgentX(0);
        float ay = arena.AgentY(0);

        float[] angles = new float[32];
        for (int i = 0; i < 32; i++)
            angles[i] = i * MathF.PI * 2f / 32f;

        foreach (float angle in angles)
        {
            float dirX = MathF.Cos(angle);
            float dirY = MathF.Sin(angle);

            var (dist, hitType) = arena.Raycast(0, ax, ay, dirX, dirY, 10f);

            if (hitType == (int)EntityType.Food)
                Assert.True(dist > 0, "Physical distance should be positive when food detected");

            Assert.True(dist > 0 || hitType == (int)EntityType.None,
                "Distance should be positive when something is hit");
        }
    }

    private static ArenaAgent[] RunArena(ulong seed, WorldBudget budget, int agents, int ticks)
    {
        var arena = new SharedArena();
        arena.Reset(seed, budget, agents);

        var actions = new float[agents][];
        for (int i = 0; i < agents; i++)
            actions[i] = new float[] { 0.5f, 0.1f, 0, 0 };

        for (int t = 0; t < ticks; t++)
            arena.StepAll(actions);

        return arena.Agents.ToArray();
    }

    [Fact]
    public void SignalEmission_StoredOnAgent()
    {
        var arena = new SharedArena();
        arena.Reset(42, new WorldBudget(64, 64, 0f, 0f, 0), 2);

        var actions = new float[][] {
            new float[] { 0, 0, 0.5f, -0.3f },
            new float[] { 0, 0, -0.8f, 0.9f }
        };
        arena.StepAll(actions);

        Assert.Equal(0.5f, arena.Agents[0].Signal0, 5);
        Assert.Equal(-0.3f, arena.Agents[0].Signal1, 5);
        Assert.Equal(-0.8f, arena.Agents[1].Signal0, 5);
        Assert.Equal(0.9f, arena.Agents[1].Signal1, 5);
    }

    [Fact]
    public void SignalSensing_DetectsNearbySignals()
    {
        var arena = new SharedArena();
        arena.Reset(42, new WorldBudget(64, 64, 0f, 0f, 0), 2);

        var actions = new float[][] {
            new float[] { 0, 0, 0.7f, -0.5f },
            new float[] { 0, 0, 0, 0 }
        };
        arena.StepAll(actions);

        float dist = Distance(arena.Agents[0], arena.Agents[1]);
        if (dist < 15f)
        {
            var (s0, s1) = arena.NearbySignals(1, arena.AgentX(1), arena.AgentY(1), 15f);
            Assert.True(MathF.Abs(s0) > 0.01f, $"Agent 1 should sense signal0 from agent 0, got {s0}");
            Assert.True(MathF.Abs(s1) > 0.01f, $"Agent 1 should sense signal1 from agent 0, got {s1}");
        }
    }

    [Fact]
    public void DeadAgent_SignalsSilenced()
    {
        var arena = new SharedArena();
        arena.Reset(55, new WorldBudget(64, 64, 0f, 0f, 0), 2);

        var actions = new float[2][];
        for (int i = 0; i < 2; i++) actions[i] = new float[] { 0, 0, 0.9f, 0.9f };

        for (int t = 0; t < 10000; t++)
        {
            arena.StepAll(actions);
            if (!arena.AgentAlive(1)) break;
        }

        if (!arena.AgentAlive(1))
        {
            Assert.Equal(0f, arena.Agents[1].Signal0);
            Assert.Equal(0f, arena.Agents[1].Signal1);

            var (s0, s1) = arena.NearbySignals(0, arena.AgentX(0), arena.AgentY(0), 100f);
            Assert.Equal(0f, s0);
            Assert.Equal(0f, s1);
        }
    }

    [Fact]
    public void SignalGradient_PointsTowardSignaler()
    {
        var arena = new SharedArena();
        arena.Reset(42, new WorldBudget(64, 64, 0f, 0f, 0), 2);

        var actions = new float[][] {
            new float[] { 0, 0, 0.8f, 0.8f },
            new float[] { 0, 0, 0, 0 }
        };
        arena.StepAll(actions);

        float trueDx = arena.AgentX(0) - arena.AgentX(1);
        float trueDy = arena.AgentY(0) - arena.AgentY(1);
        float trueLen = MathF.Sqrt(trueDx * trueDx + trueDy * trueDy);
        if (trueLen < 1e-6f || trueLen > 15f) return;

        var (gx, gy) = arena.SignalGradient(1, arena.AgentX(1), arena.AgentY(1), 15f);
        float dot = (gx * trueDx + gy * trueDy) / trueLen;

        Assert.True(dot > 0.5f,
            $"Signal gradient should point toward signaler, dot product = {dot}");
    }

    [Fact]
    public void Signal_DeterministicAcrossRuns()
    {
        var budget = new WorldBudget(32, 32, 0.05f, 0.02f, 10);
        var final1 = RunArenaWithSignals(42, budget, 4, 50);
        var final2 = RunArenaWithSignals(42, budget, 4, 50);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(final1[i].Signal0, final2[i].Signal0);
            Assert.Equal(final1[i].Signal1, final2[i].Signal1);
        }
    }

    private static ArenaAgent[] RunArenaWithSignals(ulong seed, WorldBudget budget, int agents, int ticks)
    {
        var arena = new SharedArena();
        arena.Reset(seed, budget, agents);

        var actions = new float[agents][];
        for (int i = 0; i < agents; i++)
            actions[i] = new float[] { 0.5f, 0.1f, 0.3f * (i + 1) / agents, -0.2f * i / agents };

        for (int t = 0; t < ticks; t++)
            arena.StepAll(actions);

        return arena.Agents.ToArray();
    }

    [Fact]
    public void FoodClustering_FoodNearClusterCenters()
    {
        var clusteredBudget = new WorldBudget(64, 64, 0f, 0f, 30, FoodClusters: 3);
        var uniformBudget = new WorldBudget(64, 64, 0f, 0f, 30, FoodClusters: 0);

        var clusteredArena = new SharedArena();
        clusteredArena.Reset(42, clusteredBudget, 2);
        var clusteredFood = clusteredArena.GetFoodItems().Select(f => (f.X, f.Y)).ToArray();

        var uniformArena = new SharedArena();
        uniformArena.Reset(42, uniformBudget, 2);
        var uniformFood = uniformArena.GetFoodItems().Select(f => (f.X, f.Y)).ToArray();

        float clusteredAvgNN = AverageNearestNeighbor(clusteredFood);
        float uniformAvgNN = AverageNearestNeighbor(uniformFood);

        Assert.True(clusteredAvgNN < uniformAvgNN * 0.75f,
            $"Clustered avg NN distance ({clusteredAvgNN:F2}) should be significantly less than uniform ({uniformAvgNN:F2})");
    }

    [Fact]
    public void SeasonalEnergyMultiplier_VariesOverTime()
    {
        var seasonalBudget = new WorldBudget(64, 64, 0f, 0f, 50,
            SeasonPeriod: 200);
        var flatBudget = new WorldBudget(64, 64, 0f, 0f, 50);

        float seasonalEnergy = CollectFoodEnergy(42, seasonalBudget, 4, 200);
        float flatEnergy = CollectFoodEnergy(42, flatBudget, 4, 200);

        Assert.True(MathF.Abs(seasonalEnergy - flatEnergy) > 0.001f,
            $"Seasonal energy ({seasonalEnergy:F4}) should differ from flat energy ({flatEnergy:F4})");
    }

    [Fact]
    public void BudgetJitter_ProducesDifferentWorlds()
    {
        var budget = new WorldBudget(64, 64, 0.12f, 0.04f, 25, RoundJitter: 0.3f);

        var rng1 = new Rng64(111);
        var jittered1 = budget.Jitter(ref rng1);

        var rng2 = new Rng64(222);
        var jittered2 = budget.Jitter(ref rng2);

        bool anyDiff = jittered1.WorldWidth != jittered2.WorldWidth
            || jittered1.WorldHeight != jittered2.WorldHeight
            || jittered1.ObstacleDensity != jittered2.ObstacleDensity
            || jittered1.HazardDensity != jittered2.HazardDensity
            || jittered1.FoodCount != jittered2.FoodCount;

        Assert.True(anyDiff, "Jittering with different seeds should produce different budgets");
        Assert.Equal(0f, jittered1.RoundJitter);
        Assert.Equal(0f, jittered2.RoundJitter);
    }

    [Fact]
    public void BudgetJitter_ClampsToSafeBounds()
    {
        var budget = new WorldBudget(64, 64, 0.12f, 0.04f, 25, RoundJitter: 1.0f);

        for (ulong seed = 0; seed < 100; seed++)
        {
            var rng = new Rng64(seed);
            var jittered = budget.Jitter(ref rng);

            Assert.True(jittered.WorldWidth >= 16, $"Seed {seed}: width {jittered.WorldWidth} < 16");
            Assert.True(jittered.WorldHeight >= 16, $"Seed {seed}: height {jittered.WorldHeight} < 16");
            Assert.True(jittered.FoodCount >= 5, $"Seed {seed}: food {jittered.FoodCount} < 5");
            Assert.True(jittered.ObstacleDensity >= 0f, $"Seed {seed}: obstacles {jittered.ObstacleDensity} < 0");
            Assert.True(jittered.HazardDensity >= 0f, $"Seed {seed}: hazards {jittered.HazardDensity} < 0");
        }
    }

    [Fact]
    public void ClusteredRespawn_MaintainsStructure()
    {
        var budget = new WorldBudget(64, 64, 0f, 0f, 20, FoodClusters: 3);
        var arena = new SharedArena();
        arena.Reset(42, budget, 4);

        var actions = new float[4][];
        for (int i = 0; i < 4; i++) actions[i] = new float[] { 1f, 0.2f, 0, 0 };

        for (int t = 0; t < 500; t++)
            arena.StepAll(actions);

        var foodAfterRespawn = arena.GetFoodItems()
            .Where(f => !f.Consumed)
            .Select(f => (f.X, f.Y))
            .ToArray();

        if (foodAfterRespawn.Length >= 5)
        {
            float respawnAvgNN = AverageNearestNeighbor(foodAfterRespawn);

            var uniformArena = new SharedArena();
            uniformArena.Reset(99, new WorldBudget(64, 64, 0f, 0f, 20, FoodClusters: 0), 4);
            var uniformFood = uniformArena.GetFoodItems().Select(f => (f.X, f.Y)).ToArray();
            float uniformAvgNN = AverageNearestNeighbor(uniformFood);

            Assert.True(respawnAvgNN < uniformAvgNN * 0.85f,
                $"Respawned clustered food NN ({respawnAvgNN:F2}) should be less than uniform ({uniformAvgNN:F2})");
        }
    }

    [Fact]
    public void EnvironmentalDynamics_DeterministicAcrossRuns()
    {
        var budget = new WorldBudget(32, 32, 0.05f, 0.02f, 10,
            FoodClusters: 2, FoodEnergyAmplitude: 0.3f, FoodEnergyPeriod: 200, RoundJitter: 0f);

        var final1 = RunArena(42, budget, 4, 100);
        var final2 = RunArena(42, budget, 4, 100);

        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(final1[i].X, final2[i].X);
            Assert.Equal(final1[i].Y, final2[i].Y);
            Assert.Equal(final1[i].Energy, final2[i].Energy);
            Assert.Equal(final1[i].Alive, final2[i].Alive);
        }
    }

    private static float AverageNearestNeighbor((float X, float Y)[] points)
    {
        if (points.Length < 2) return float.MaxValue;
        float totalNN = 0;
        for (int i = 0; i < points.Length; i++)
        {
            float minDist = float.MaxValue;
            for (int j = 0; j < points.Length; j++)
            {
                if (i == j) continue;
                float dx = points[i].X - points[j].X;
                float dy = points[i].Y - points[j].Y;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d < minDist) minDist = d;
            }
            totalNN += minDist;
        }
        return totalNN / points.Length;
    }

    private static float CollectFoodEnergy(ulong seed, WorldBudget budget, int agents, int ticks)
    {
        var arena = new SharedArena();
        arena.Reset(seed, budget, agents);

        var actions = new float[agents][];
        for (int i = 0; i < agents; i++)
            actions[i] = new float[] { 1f, 0.1f, 0, 0 };

        float totalFoodEnergy = 0;
        for (int t = 0; t < ticks; t++)
        {
            var results = arena.StepAll(actions);
            for (int i = 0; i < agents; i++)
            {
                if (results[i].Signals.FoodCollectedThisStep > 0)
                    totalFoodEnergy += results[i].Signals.EnergyDelta + 
                        (ContinuousWorld.BaseEnergyCost + MathF.Abs(arena.Agents[i].Speed) * ContinuousWorld.MovementEnergyCost);
            }
        }
        return totalFoodEnergy;
    }

    // --- Emergent Dynamics Tests ---

    [Fact]
    public void Decomposition_DeadAgentSpawnsCorpseFood()
    {
        var budget = new WorldBudget(16, 16, 0f, 2.0f, 0, CorpseEnergyBase: 0.3f);
        var arena = new SharedArena();
        arena.Reset(42, budget, 2);

        int initialFood = arena.GetFoodItems().Count;

        var actions = new float[2][];
        for (int i = 0; i < 2; i++) actions[i] = new float[ContinuousWorld.ActuatorCount];

        for (int t = 0; t < 10000; t++)
        {
            arena.StepAll(actions);
            if (!arena.AgentAlive(0) || !arena.AgentAlive(1)) break;
        }

        bool anyDead = !arena.AgentAlive(0) || !arena.AgentAlive(1);
        if (anyDead)
        {
            int foodAfter = arena.GetFoodItems().Count(f => !f.Consumed);
            Assert.True(foodAfter > initialFood,
                $"Dead agent should spawn corpse food. Before: {initialFood}, After: {foodAfter}");
        }
    }

    [Fact]
    public void LightLevel_CyclesWithDayNight()
    {
        var budget = new WorldBudget(32, 32, 0f, 0f, 10, DayNightPeriod: 100);
        var arena = new SharedArena();
        arena.Reset(42, budget, 2);

        float initialLight = arena.LightLevel;
        Assert.Equal(1f, initialLight, 3);

        var actions = new float[2][];
        for (int i = 0; i < 2; i++) actions[i] = new float[ContinuousWorld.ActuatorCount];

        float minLight = 1f, maxLight = 0f;
        for (int t = 0; t < 100; t++)
        {
            arena.StepAll(actions);
            float light = arena.LightLevel;
            if (light < minLight) minLight = light;
            if (light > maxLight) maxLight = light;
        }

        Assert.True(maxLight - minLight > 0.5f,
            $"Light should cycle significantly. Min: {minLight:F3}, Max: {maxLight:F3}");
    }

    [Fact]
    public void AmbientEnergy_SlowsEnergyDecay()
    {
        var budgetNoAmbient = new WorldBudget(32, 32, 0f, 0f, 0);
        var budgetAmbient = new WorldBudget(32, 32, 0f, 0f, 0, AmbientEnergyRate: 0.01f);

        var arenaNo = new SharedArena();
        arenaNo.Reset(42, budgetNoAmbient, 2);
        var arenaYes = new SharedArena();
        arenaYes.Reset(42, budgetAmbient, 2);

        var actions = new float[2][];
        for (int i = 0; i < 2; i++) actions[i] = new float[ContinuousWorld.ActuatorCount];

        for (int t = 0; t < 100; t++)
        {
            arenaNo.StepAll(actions);
            arenaYes.StepAll(actions);
        }

        int aliveNo = Enumerable.Range(0, 2).Count(i => arenaNo.AgentAlive(i));
        int aliveYes = Enumerable.Range(0, 2).Count(i => arenaYes.AgentAlive(i));

        float energyNo = Enumerable.Range(0, 2).Sum(i => arenaNo.AgentEnergy(i));
        float energyYes = Enumerable.Range(0, 2).Sum(i => arenaYes.AgentEnergy(i));

        Assert.True(energyYes >= energyNo,
            $"Ambient energy agents should have >= energy. With: {energyYes:F4}, Without: {energyNo:F4}");
    }

    [Fact]
    public void FoodQualityVariation_ProducesVariedEnergy()
    {
        var budget = new WorldBudget(64, 64, 0f, 0f, 50, FoodQualityVariation: 0.5f);
        var arena = new SharedArena();
        arena.Reset(42, budget, 2);

        var energies = arena.GetFoodItems().Select(f => f.EnergyValue).Distinct().ToList();
        Assert.True(energies.Count > 1,
            $"Food quality variation should produce different energy values, got {energies.Count} distinct");
    }

    [Fact]
    public void EnergyGain_RewardedRegardlessOfSource()
    {
        int maxTicks = 500;

        float fitnessLowDelta = DeterministicHelpers.ComputeEpisodeFitness(
            maxTicks, 0.1f, 5, 0.5f, 0f, 200f, maxTicks);
        float fitnessHighDelta = DeterministicHelpers.ComputeEpisodeFitness(
            maxTicks, 0.5f, 0, 0f, 0f, 0f, maxTicks);

        Assert.True(fitnessHighDelta > fitnessLowDelta,
            $"Higher energy delta ({fitnessHighDelta:F2}) should outscore lower delta ({fitnessLowDelta:F2}) regardless of food/distance");
    }

    private static float Distance(ArenaAgent a, ArenaAgent b)
    {
        float dx = a.X - b.X;
        float dy = a.Y - b.Y;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    // --- Agent Energy Interaction Tests ---

    [Fact]
    public void ShareEnergy_TransfersToNearbyAgent()
    {
        var budget = new WorldBudget(7, 7, 0f, 0f, 0);

        var arena = new SharedArena();
        arena.Reset(42, budget, 2);
        var baseline = new SharedArena();
        baseline.Reset(42, budget, 2);

        var shareActions = new float[2][];
        shareActions[0] = new float[] { 0, 0, 0, 0, 1f, 0 };
        shareActions[1] = new float[6];

        var noopActions = new float[2][];
        noopActions[0] = new float[6];
        noopActions[1] = new float[6];

        arena.StepAll(shareActions);
        baseline.StepAll(noopActions);

        Assert.True(arena.AgentEnergy(0) < baseline.AgentEnergy(0),
            "Sharer should lose energy relative to no-share baseline");
        Assert.True(arena.AgentEnergy(1) > baseline.AgentEnergy(1),
            "Receiver should gain energy relative to no-share baseline");
        Assert.True(arena.Agents[1].ShareReceived > 0, "ShareReceived should be set");
    }

    [Fact]
    public void AttackEnergy_DrainsFromTarget()
    {
        var budget = new WorldBudget(7, 7, 0f, 0f, 0);

        var arena = new SharedArena();
        arena.Reset(42, budget, 2);
        var baseline = new SharedArena();
        baseline.Reset(42, budget, 2);

        var attackActions = new float[2][];
        attackActions[0] = new float[] { 0, 0, 0, 0, 0, 1f };
        attackActions[1] = new float[6];

        var noopActions = new float[2][];
        noopActions[0] = new float[6];
        noopActions[1] = new float[6];

        arena.StepAll(attackActions);
        baseline.StepAll(noopActions);

        Assert.True(arena.AgentEnergy(1) < baseline.AgentEnergy(1),
            "Target should lose more energy from attack drain vs baseline");
        Assert.True(arena.Agents[1].AttackReceived > 0, "AttackReceived should be set");
    }

    [Fact]
    public void Interaction_RequiresProximity()
    {
        var budget = new WorldBudget(64, 64, 0f, 0f, 0);
        var arena = new SharedArena();
        arena.Reset(12345, budget, 2);

        float dist = Distance(arena.Agents[0], arena.Agents[1]);
        Assert.True(dist > ContinuousWorld.InteractionRadius,
            "Agents should be far apart in 64x64 world");

        var actions = new float[2][];
        actions[0] = new float[] { 0, 0, 0, 0, 1f, 1f };
        actions[1] = new float[6];

        arena.StepAll(actions);

        Assert.Equal(0f, arena.Agents[1].ShareReceived);
        Assert.Equal(0f, arena.Agents[1].AttackReceived);
    }

    [Fact]
    public void DeadAgent_CannotInteractOrBeTargeted()
    {
        var budget = new WorldBudget(16, 16, 0f, 2.0f, 0);
        var arena = new SharedArena();
        arena.Reset(42, budget, 2);

        var idleActions = new float[2][];
        idleActions[0] = new float[6];
        idleActions[1] = new float[] { 1f, 0, 0, 0, 0, 0 };

        for (int t = 0; t < 5000; t++)
        {
            arena.StepAll(idleActions);
            if (!arena.AgentAlive(0) || !arena.AgentAlive(1)) break;
        }

        bool anyDead = !arena.AgentAlive(0) || !arena.AgentAlive(1);
        Assert.True(anyDead, "At least one agent should die from hazards");

        int dead = arena.AgentAlive(0) ? 1 : 0;
        int alive = 1 - dead;

        float eBefore = arena.AgentEnergy(alive);
        var shareActions = new float[2][];
        shareActions[0] = new float[6];
        shareActions[1] = new float[6];
        shareActions[alive] = new float[] { 0, 0, 0, 0, 1f, 0 };
        arena.StepAll(shareActions);

        Assert.False(arena.AgentAlive(dead), "Dead agent should stay dead");
        Assert.Equal(0f, arena.Agents[dead].ShareReceived);
    }

    [Fact]
    public void InteractionFeedback_StoredOnAgent()
    {
        var budget = new WorldBudget(7, 7, 0f, 0f, 0);
        var arena = new SharedArena();
        arena.Reset(42, budget, 2);

        var actions = new float[2][];
        actions[0] = new float[] { 0, 0, 0, 0, 1f, 0 };
        actions[1] = new float[] { 0, 0, 0, 0, 0, 1f };

        arena.StepAll(actions);

        Assert.True(arena.Agents[1].ShareReceived > 0,
            "Agent 1 should have ShareReceived from agent 0");
        Assert.True(arena.Agents[0].AttackReceived > 0,
            "Agent 0 should have AttackReceived from agent 1");
    }

    [Fact]
    public void NegativeActuator_DoesNotTriggerInteraction()
    {
        var budget = new WorldBudget(7, 7, 0f, 0f, 0);
        var arena = new SharedArena();
        arena.Reset(42, budget, 2);

        var actions = new float[2][];
        actions[0] = new float[] { 0, 0, 0, 0, -1f, -1f };
        actions[1] = new float[6];

        arena.StepAll(actions);

        Assert.Equal(0f, arena.Agents[0].ShareReceived);
        Assert.Equal(0f, arena.Agents[0].AttackReceived);
        Assert.Equal(0f, arena.Agents[1].ShareReceived);
        Assert.Equal(0f, arena.Agents[1].AttackReceived);
    }

    [Fact]
    public void Interactions_DeterministicAcrossRuns()
    {
        var budget = new WorldBudget(7, 7, 0f, 0f, 0);

        var arena1 = new SharedArena();
        arena1.Reset(77, budget, 4);
        var arena2 = new SharedArena();
        arena2.Reset(77, budget, 4);

        var actions1 = new float[4][];
        var actions2 = new float[4][];
        for (int i = 0; i < 4; i++)
        {
            actions1[i] = new float[] { 0.5f, 0.1f, 0.3f, -0.2f, 0.8f, 0.6f };
            actions2[i] = new float[] { 0.5f, 0.1f, 0.3f, -0.2f, 0.8f, 0.6f };
        }

        for (int t = 0; t < 50; t++)
        {
            arena1.StepAll(actions1);
            arena2.StepAll(actions2);
        }

        var final1 = arena1.Agents.ToArray();
        var final2 = arena2.Agents.ToArray();
        for (int i = 0; i < 4; i++)
        {
            Assert.Equal(final1[i].Energy, final2[i].Energy);
            Assert.Equal(final1[i].X, final2[i].X);
            Assert.Equal(final1[i].Y, final2[i].Y);
            Assert.Equal(final1[i].ShareReceived, final2[i].ShareReceived);
            Assert.Equal(final1[i].AttackReceived, final2[i].AttackReceived);
            Assert.Equal(final1[i].Alive, final2[i].Alive);
        }
    }
}
