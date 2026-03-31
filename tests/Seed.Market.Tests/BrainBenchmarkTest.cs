using System.Diagnostics;
using Seed.Brain;
using Seed.Core;
using Seed.Development;
using Seed.Genetics;
using Seed.Market.Agents;
using Seed.Market.Evolution;

namespace Seed.Market.Tests;

public class BrainBenchmarkTest
{
    [Fact]
    public void BrainStep_MicrosecondsPerStep_BelowThreshold()
    {
        var (brain, _) = CompileMarketBrain();
        var inputs = new float[MarketAgent.InputCount];

        // Warm up
        for (int i = 0; i < 100; i++)
            brain.Step(inputs, new BrainStepContext(i));

        var sw = Stopwatch.StartNew();
        int steps = 10_000;
        for (int i = 0; i < steps; i++)
            brain.Step(inputs, new BrainStepContext(100 + i));
        sw.Stop();

        double usPerStep = sw.Elapsed.TotalMicroseconds / steps;
        Assert.True(usPerStep < 1000,
            $"Brain step took {usPerStep:F1} us/step -- should be < 1000 us");
    }

    [Fact]
    public void BrainCompile_TimeBelowThreshold()
    {
        var rng = new Rng64(42);
        var developer = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var devCtx = new DevelopmentContext(42, 0);

        var genomes = Enumerable.Range(0, 10)
            .Select(_ => SeedGenome.CreateRandom(rng))
            .ToList();

        // Warm up
        developer.CompileGraph(genomes[0], MarketEvaluator.MarketBrainBudget, devCtx);

        var sw = Stopwatch.StartNew();
        for (int i = 1; i < genomes.Count; i++)
            developer.CompileGraph(genomes[i], MarketEvaluator.MarketBrainBudget, devCtx);
        sw.Stop();

        double msPerCompile = sw.Elapsed.TotalMilliseconds / (genomes.Count - 1);
        Assert.True(msPerCompile < 1000,
            $"Brain compile took {msPerCompile:F1} ms -- should be < 1000 ms");
    }

    [Fact]
    public void BrainStep_ProducesValidOutputs()
    {
        var (brain, _) = CompileMarketBrain();
        var inputs = new float[MarketAgent.InputCount];
        for (int i = 0; i < MarketAgent.InputCount; i++)
            inputs[i] = 0.5f;

        for (int t = 0; t < 100; t++)
        {
            var outputs = brain.Step(inputs, new BrainStepContext(t));
            Assert.Equal(MarketAgent.OutputCount, outputs.Length);
            for (int o = 0; o < outputs.Length; o++)
                Assert.False(float.IsNaN(outputs[o]), $"Output {o} at tick {t} is NaN");
        }
    }

    private static (BrainRuntime Brain, SeedGenome Genome) CompileMarketBrain()
    {
        var rng = new Rng64(42);
        var genome = SeedGenome.CreateRandom(rng);
        var dev = new BrainDeveloper(MarketAgent.InputCount, MarketAgent.OutputCount);
        var graph = dev.CompileGraph(genome, MarketEvaluator.MarketBrainBudget, new DevelopmentContext(42, 0));
        var brain = new BrainRuntime(graph, genome.Learn, genome.Stable, 1);
        return (brain, genome);
    }
}
