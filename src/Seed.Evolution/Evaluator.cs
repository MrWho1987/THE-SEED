using Seed.Core;
using Seed.Worlds;
using Seed.Agents;
using Seed.Brain;
using Seed.Development;
using Seed.Genetics;

namespace Seed.Evolution;

public sealed class Evaluator
{
    private readonly BrainDeveloper _developer;

    public Evaluator(int sensorCount, int actuatorCount)
    {
        _developer = new BrainDeveloper(sensorCount, actuatorCount);
    }

    public Dictionary<Guid, GenomeEvaluationResult> EvaluateArena(
        List<IGenome> population, in EvaluationContext ctx)
    {
        var devCtx = new DevelopmentContext(ctx.RunSeed, ctx.GenerationIndex);

        var entries = new (IGenome Genome, BrainGraph Graph)[population.Count];
        var devBudget = ctx.DevelopmentBudget;
        Parallel.For(0, population.Count,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            i =>
            {
                var sg = (SeedGenome)population[i];
                entries[i] = (sg, _developer.CompileGraph(sg, devBudget, devCtx));
            });

        var allMetrics = new EpisodeMetrics[population.Count][];
        for (int i = 0; i < population.Count; i++)
            allMetrics[i] = new EpisodeMetrics[ctx.ArenaRounds];

        for (int round = 0; round < ctx.ArenaRounds; round++)
        {
            ulong arenaSeed = SeedDerivation.WorldSeed(
                ctx.RunSeed, ctx.GenerationIndex, round, ctx.WorldBundleKey);

            var worldBudget = ctx.WorldBudget;
            if (worldBudget.RoundJitter > 0f)
            {
                var jitterRng = new Rng64(arenaSeed ^ 0xB00B1E5);
                worldBudget = worldBudget.Jitter(ref jitterRng);
            }

            var arena = new SharedArena();
            arena.Reset(arenaSeed, worldBudget, population.Count);

            var views = new AgentView[population.Count];
            var bodies = new AgentBody[population.Count];
            var brains = new BrainRuntime[population.Count];

            for (int i = 0; i < population.Count; i++)
            {
                var sg = (SeedGenome)entries[i].Genome;
                views[i] = new AgentView(arena, i);
                bodies[i] = new AgentBody(views[i]);
                brains[i] = new BrainRuntime(
                    entries[i].Graph, sg.Learn, sg.Stable,
                    ctx.RuntimeBudget.MicroStepsPerTick);
                bodies[i].Reset(new BodyResetContext(arenaSeed ^ (ulong)i));
                brains[i].Reset();
            }

            var roundMetrics = RunArenaEpisode(
                arena, bodies, brains, ctx.RuntimeBudget, population.Count,
                ctx.EffectiveAblations);

            for (int i = 0; i < population.Count; i++)
                allMetrics[i][round] = roundMetrics[i];
        }

        var fitCfg = ctx.EffectiveFitnessConfig;
        var results = new Dictionary<Guid, GenomeEvaluationResult>();
        for (int i = 0; i < population.Count; i++)
        {
            var agg = DeterministicHelpers.AggregateFitness(
                allMetrics[i], fitCfg.LambdaVar, fitCfg.LambdaWorst);
            results[entries[i].Genome.GenomeId] = new GenomeEvaluationResult(
                entries[i].Genome.GenomeId, entries[i].Genome, allMetrics[i], agg);
        }
        return results;
    }

    private static EpisodeMetrics[] RunArenaEpisode(
        SharedArena arena, AgentBody[] bodies, BrainRuntime[] brains,
        in RuntimeBudget runtimeBudget, int agentCount,
        in AblationConfig ablations)
    {
        var sensorBuffers = new float[agentCount][];
        var allActions = new float[agentCount][];
        var curiosities = new float[agentCount];

        var survival = new int[agentCount];
        var netEnergy = new float[agentCount];
        var food = new int[agentCount];
        var spent = new float[agentCount];
        var instab = new float[agentCount];

        int actCount = ContinuousWorld.ActuatorCount;
        for (int i = 0; i < agentCount; i++)
        {
            sensorBuffers[i] = new float[bodies[i].SensorCount];
            allActions[i] = new float[actCount];
        }

        bool usePredictionErrorCuriosity = ablations.PredictionErrorCuriosity;

        var parallelOpts = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        for (int tick = 0; tick < runtimeBudget.MaxTicksPerEpisode; tick++)
        {
            int aliveFlag = 0;
            int currentTick = tick;

            Parallel.For(0, agentCount, parallelOpts, i =>
            {
                if (!arena.AgentAlive(i))
                {
                    Array.Clear(allActions[i]);
                    return;
                }
                Interlocked.Exchange(ref aliveFlag, 1);

                bodies[i].ReadSensors(sensorBuffers[i]);

                curiosities[i] = 0f;
                if (currentTick > 0)
                {
                    curiosities[i] = usePredictionErrorCuriosity
                        ? bodies[i].ComputePredictionErrorCuriosity(sensorBuffers[i])
                        : bodies[i].ComputeCuriosity(sensorBuffers[i]);
                }

                var outputs = brains[i].Step(sensorBuffers[i], new BrainStepContext(currentTick));
                for (int a = 0; a < actCount && a < outputs.Length; a++)
                    allActions[i][a] = outputs[a];
            });

            if (aliveFlag == 0) break;

            var results = arena.StepAll(allActions);

            for (int i = 0; i < agentCount; i++)
            {
                results[i].Modulators[ModulatorIndex.Curiosity] = curiosities[i];
                brains[i].Learn(results[i].Modulators, new BrainLearnContext(tick));
                bodies[i].ApplyWorldSignals(results[i].Signals);

                if (arena.AgentAlive(i)) survival[i]++;
                netEnergy[i] += results[i].Signals.EnergyDelta;
                food[i] += results[i].Signals.FoodCollectedThisStep;
                if (results[i].Signals.EnergyDelta < 0)
                    spent[i] += -results[i].Signals.EnergyDelta;
                instab[i] += brains[i].GetInstabilityPenalty();
            }
        }

        var episodeMetrics = new EpisodeMetrics[agentCount];
        for (int i = 0; i < agentCount; i++)
        {
            float instPen = survival[i] > 0 ? instab[i] / survival[i] : 0f;
            float fitness = DeterministicHelpers.ComputeEpisodeFitness(
                survival[i], netEnergy[i], food[i], spent[i], instPen,
                runtimeBudget.MaxTicksPerEpisode);
            episodeMetrics[i] = new EpisodeMetrics(
                survival[i], netEnergy[i], food[i], spent[i], instPen, fitness);
        }
        return episodeMetrics;
    }
}
