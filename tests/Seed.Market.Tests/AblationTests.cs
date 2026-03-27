using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Evolution;

namespace Seed.Market.Tests;

public class AblationTests
{
    [Fact]
    public void Ablation_LearningDisabled_NoWeightChange()
    {
        var ablation = AblationConfig.Default with { LearningEnabled = false };
        var (brain, _) = CompileBrain(ablation);

        var diag0 = brain.GetDiagnostics();
        float initialWeightSum = diag0.MeanAbsWeightFast;

        var inputs = new float[MarketAgent.InputCount];
        Span<float> modulators = stackalloc float[3];
        modulators[0] = 1f; modulators[1] = 0.5f; modulators[2] = 0.3f;

        for (int t = 0; t < 100; t++)
        {
            var ctx = new BrainStepContext(t);
            brain.Step(inputs, ctx);
            brain.Learn(modulators, new BrainLearnContext(t));
        }

        var diag1 = brain.GetDiagnostics();
        Assert.Equal(initialWeightSum, diag1.MeanAbsWeightFast, 6);
    }

    [Fact]
    public void Ablation_ModulatoryDisabled_StillRuns()
    {
        var ablation = AblationConfig.Default with { ModulatoryEdgesEnabled = false };
        var (brain, _) = CompileBrain(ablation);

        var inputs = new float[MarketAgent.InputCount];
        for (int i = 0; i < inputs.Length; i++) inputs[i] = 0.5f;

        for (int t = 0; t < 50; t++)
        {
            var ctx = new BrainStepContext(t);
            var outputs = brain.Step(inputs, ctx);
            Assert.Equal(MarketAgent.OutputCount, outputs.Length);
            for (int o = 0; o < outputs.Length; o++)
                Assert.False(float.IsNaN(outputs[o]), $"Output {o} is NaN at tick {t}");
        }
    }

    [Fact]
    public void Ablation_DelaysDisabled_StillRuns()
    {
        var ablation = AblationConfig.Default with { SynapticDelaysEnabled = false };
        var (brain, _) = CompileBrain(ablation);

        var inputs = new float[MarketAgent.InputCount];
        for (int t = 0; t < 50; t++)
        {
            var ctx = new BrainStepContext(t);
            var outputs = brain.Step(inputs, ctx);
            for (int o = 0; o < outputs.Length; o++)
                Assert.False(float.IsNaN(outputs[o]));
        }
    }

    [Fact]
    public void CriticalPeriod_EtaDecays()
    {
        int criticalTicks = 1000;
        float etaAt100 = MathF.Max(0.1f, 1f - 100f / criticalTicks);
        float etaAt900 = MathF.Max(0.1f, 1f - 900f / criticalTicks);

        Assert.True(etaAt900 < etaAt100,
            $"Eta scale at tick 900 ({etaAt900:F3}) should be less than at tick 100 ({etaAt100:F3})");
        Assert.True(etaAt100 > 0.85f, $"Eta scale at tick 100 should be > 0.85, got {etaAt100}");
        Assert.True(etaAt900 > 0.09f, $"Eta scale at tick 900 should be > 0.09 (floor), got {etaAt900}");
    }

    [Fact]
    public void SynapticPruning_ZerosWeakEdges()
    {
        var (brain, _) = CompileBrain();

        var inputs = new float[MarketAgent.InputCount];
        for (int t = 0; t < 50; t++)
        {
            var ctx = new BrainStepContext(t);
            brain.Step(inputs, ctx);
        }

        int pruned = brain.PruneWeakEdges(0.01f);
        Assert.True(pruned > 0, "Should have pruned at least 1 weak edge");

        var diag = brain.GetDiagnostics();
        Assert.True(diag.TotalEdges > 0);

        for (int t = 0; t < 10; t++)
        {
            var ctx = new BrainStepContext(50 + t);
            var outputs = brain.Step(inputs, ctx);
            for (int o = 0; o < outputs.Length; o++)
                Assert.False(float.IsNaN(outputs[o]), $"Output {o} is NaN after pruning at tick {50 + t}");
        }
    }

    private static (BrainRuntime Brain, SeedGenome Genome) CompileBrain(AblationConfig? ablation = null)
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var graph = dev.CompileGraph(genome, MarketEvaluator.MarketBrainBudget, new DevelopmentContext(42, 0));
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1, ablation);
        return (brain, genome);
    }
}
