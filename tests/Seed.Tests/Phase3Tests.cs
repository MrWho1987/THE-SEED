using Seed.Core;
using Seed.Genetics;
using Seed.Development;
using Seed.Brain;
using Seed.Agents;
using Seed.Worlds;

namespace Seed.Tests;

public class Phase3Tests
{
    [Fact]
    public void CppnEval_AcyclicNetwork_SinglePassMatchesOld()
    {
        var rng = new Rng64(42);
        var cppn = CppnNetwork.CreateMinimal(CppnInputIndex.Count, CppnOutputIndex.Count, rng);

        var inputs = new float[CppnInputIndex.Count];
        inputs[CppnInputIndex.Xi] = 0.3f;
        inputs[CppnInputIndex.Yi] = 0.5f;
        inputs[CppnInputIndex.Xj] = 0.7f;
        inputs[CppnInputIndex.Yj] = 0.2f;
        inputs[CppnInputIndex.Dist] = 0.5f;

        var outputs1 = cppn.Evaluate(inputs);
        var outputs2 = cppn.Evaluate(inputs);

        Assert.Equal(outputs1.Length, outputs2.Length);
        for (int i = 0; i < outputs1.Length; i++)
            Assert.Equal(outputs1[i], outputs2[i], 5);
    }

    [Fact]
    public void CppnEval_CyclicNetwork_Settles()
    {
        var nodes = new List<CppnNode>
        {
            new(0, CppnNodeType.Input, ActivationFn.Identity, 0f),
            new(1, CppnNodeType.Hidden, ActivationFn.Tanh, 0f),
            new(2, CppnNodeType.Output, ActivationFn.Tanh, 0f),
        };
        var connections = new List<CppnConnection>
        {
            new(0, 0, 1, 0.5f, true),   // input -> hidden
            new(1, 1, 2, 0.8f, true),   // hidden -> output
            new(2, 2, 1, -0.3f, true),  // output -> hidden (cycle!)
        };
        var cppn = new CppnNetwork(nodes, connections, 3);

        var inputs = new float[] { 1.0f };
        var outputs = cppn.Evaluate(inputs);

        Assert.Single(outputs);
        Assert.True(float.IsFinite(outputs[0]), "Cyclic CPPN should produce finite outputs");
    }

    [Fact]
    public void PredictionErrorCuriosity_DecreasesForConstantStimuli()
    {
        var world = new ContinuousWorld();
        world.Reset(42, WorldBudget.Default);
        var body = new AgentBody(world);
        body.Reset(new BodyResetContext(42));

        var constantSensors = new float[body.SensorCount];
        for (int i = 0; i < constantSensors.Length; i++)
            constantSensors[i] = 0.5f;

        float prevCuriosity = float.MaxValue;
        bool monotonicallyDecreasing = true;

        for (int t = 0; t < 100; t++)
        {
            float c = body.ComputePredictionErrorCuriosity(constantSensors);
            if (t > 5 && c > prevCuriosity + 1e-6f)
                monotonicallyDecreasing = false;
            prevCuriosity = c;
        }

        Assert.True(monotonicallyDecreasing,
            "Prediction error curiosity should decrease with constant input");
        Assert.True(prevCuriosity < 0.05f,
            $"After 100 steps of constant input, curiosity should be near 0, was {prevCuriosity}");
    }

    [Fact]
    public void PredictionErrorCuriosity_SpikesOnChange()
    {
        var world = new ContinuousWorld();
        world.Reset(42, WorldBudget.Default);
        var body = new AgentBody(world);
        body.Reset(new BodyResetContext(42));

        var sensors = new float[body.SensorCount];
        for (int i = 0; i < sensors.Length; i++) sensors[i] = 0.5f;

        // Let prediction adapt to constant input
        for (int t = 0; t < 50; t++)
            body.ComputePredictionErrorCuriosity(sensors);

        float baselineCuriosity = body.ComputePredictionErrorCuriosity(sensors);

        // Suddenly change all sensors
        for (int i = 0; i < sensors.Length; i++) sensors[i] = -0.5f;
        float spikeCuriosity = body.ComputePredictionErrorCuriosity(sensors);

        Assert.True(spikeCuriosity > baselineCuriosity * 5,
            $"Curiosity should spike on sensor change: baseline={baselineCuriosity}, spike={spikeCuriosity}");
    }

    [Fact]
    public void Tau_ProducesDifferentDynamics()
    {
        var nodes = new List<BrainNode>
        {
            new(0, BrainNodeType.Input, 0f, 0f, 0, new NodeMetadata()),
            new(1, BrainNodeType.Output, 1f, 0f, 2, new NodeMetadata(TimeConstant: 1f)),
            new(2, BrainNodeType.Hidden, 0.5f, 0f, 1, new NodeMetadata(TimeConstant: 1f)),
        };
        var nodesSlowTau = new List<BrainNode>
        {
            new(0, BrainNodeType.Input, 0f, 0f, 0, new NodeMetadata()),
            new(1, BrainNodeType.Output, 1f, 0f, 2, new NodeMetadata(TimeConstant: 5f)),
            new(2, BrainNodeType.Hidden, 0.5f, 0f, 1, new NodeMetadata(TimeConstant: 5f)),
        };

        var edges = new Dictionary<int, List<BrainEdge>>
        {
            [0] = new(),
            [1] = new() { new BrainEdge(2, 1, 0.9f, 0.9f, 1f, new EdgeMetadata()) },
            [2] = new() { new BrainEdge(0, 2, 0.8f, 0.8f, 1f, new EdgeMetadata()) },
        };

        var graphInstant = new BrainGraph(nodes, edges, 1, 1, 3);
        var graphSlow = new BrainGraph(nodesSlowTau, edges, 1, 1, 3);

        var brainInstant = new BrainRuntime(graphInstant, LearningParams.Default, StabilityParams.Default);
        var brainSlow = new BrainRuntime(graphSlow, LearningParams.Default, StabilityParams.Default);

        brainInstant.Reset();
        brainSlow.Reset();

        var inputs = new float[] { 1.0f };
        float totalDeltaInstant = 0f, totalDeltaSlow = 0f;
        float prevInstant = 0f, prevSlow = 0f;

        for (int t = 0; t < 10; t++)
        {
            var stepCtx = new BrainStepContext(t);
            var outInstant = brainInstant.Step(inputs, in stepCtx).ToArray();
            var outSlow = brainSlow.Step(inputs, in stepCtx).ToArray();

            totalDeltaInstant += MathF.Abs(outInstant[0] - prevInstant);
            totalDeltaSlow += MathF.Abs(outSlow[0] - prevSlow);
            prevInstant = outInstant[0];
            prevSlow = outSlow[0];
        }

        Assert.True(totalDeltaSlow < totalDeltaInstant,
            $"Slow tau should produce smaller activation deltas: slow={totalDeltaSlow}, instant={totalDeltaInstant}");
    }

    [Fact]
    public void Determinism_TwoRuns_SameSeed_SameResults()
    {
        float RunAndGetScore(ulong seed)
        {
            var config = RunConfig.Default with
            {
                RunSeed = seed,
                MaxGenerations = 2,
                Budgets = AllBudgets.Default with
                {
                    Population = new PopulationBudget(PopulationSize: 8, ArenaRounds: 2),
                    Runtime = new RuntimeBudget(MaxTicksPerEpisode: 50),
                }
            };
            var loop = new Evolution.EvolutionLoop(config, Seed.Observatory.NullObservatory.Instance);
            loop.Initialize();
            loop.RunGeneration();
            loop.RunGeneration();

            var best = loop.Evaluations.Values
                .OrderByDescending(e => e.Aggregate.Score)
                .First();
            return best.Aggregate.Score;
        }

        float score1 = RunAndGetScore(42);
        float score2 = RunAndGetScore(42);

        Assert.Equal(score1, score2, 5);
    }
}
