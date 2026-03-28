using System.IO;
using Seed.Core;
using Seed.Genetics;
using Seed.Market;
using Seed.Market.Backtest;
using Seed.Market.Evolution;
using Seed.Observatory;

namespace Seed.Dashboard.Services;

public record GenerationReportData(
    int Generation, float BestFitness, float MeanFitness, float BestSharpe,
    float BestReturn, int BestTrades, float BestWinRate, int SpeciesCount,
    string Substrate, float? ValidationFitness = null, string? WalkForwardStatus = null);

public class TrainingService
{
    private readonly MarketConfig _config;
    private readonly Action<GenerationReportData> _onGeneration;
    private CancellationTokenSource? _cts;

    public TrainingService(MarketConfig config, Action<GenerationReportData> onGeneration)
    {
        _config = config;
        _onGeneration = onGeneration;
    }

    public async Task RunAsync()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        await Task.Factory.StartNew(() => RunBacktest(token), token,
            TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void Stop() => _cts?.Cancel();

    private void RunBacktest(CancellationToken ct)
    {
        Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;

        Directory.CreateDirectory(_config.OutputDirectory);

        var runner = new BacktestRunner(_config);
        var end = DateTimeOffset.UtcNow.AddHours(-1);
        var start = end.AddHours(-_config.TrainingWindowHours - _config.ValidationWindowHours);
        var (snapshots, prices) = runner.LoadData(_config.Symbols[0], start, end, enrich: true).GetAwaiter().GetResult();

        int trainLen = Math.Min(_config.TrainingWindowHours, snapshots.Length - _config.ValidationWindowHours);
        var trainSnapshots = snapshots[..trainLen];
        var trainPrices = prices[..trainLen];
        var valSnapshots = snapshots[trainLen..];
        var valPrices = prices[trainLen..];

        var observatory = new FileObservatory(Path.Combine(_config.OutputDirectory, "events.jsonl"));
        var evolution = new MarketEvolution(_config, observatory);

        var checkpointDir = Path.Combine(_config.OutputDirectory, "checkpoints");
        Directory.CreateDirectory(checkpointDir);
        var latestCp = CheckpointState.FindLatest(checkpointDir);
        if (latestCp != null)
        {
            var cp = CheckpointState.Load(latestCp);
            var restored = cp.RestorePopulation();
            evolution.InitializeFrom(restored, cp.Generation);
        }
        else
        {
            evolution.Initialize();
        }

        int evalWindow = Math.Min(_config.EvalWindowHours, trainLen);
        float bestEverFitness = float.MinValue;
        float bestValFitness = float.MinValue;
        int walkForwardOffset = 0;
        int stallCount = 0;
        var evaluator = new MarketEvaluator(_config);

        for (int gen = evolution.Generation; gen < _config.Generations; gen++)
        {
            if (ct.IsCancellationRequested) break;

            int remainingLen = Math.Max(50, trainLen - walkForwardOffset);
            if (walkForwardOffset + remainingLen > trainLen)
                remainingLen = trainLen - walkForwardOffset;
            if (remainingLen < 1) remainingLen = 1;
            var wfSnaps = trainSnapshots[walkForwardOffset..(walkForwardOffset + remainingLen)];
            var wfPrices = trainPrices[walkForwardOffset..(walkForwardOffset + remainingLen)];
            int wfEvalWindow = Math.Min(evalWindow, remainingLen);

            int maxOff = Math.Max(1, remainingLen - wfEvalWindow);
            int offset = _config.WalkForwardEnabled ? 0 : (gen * _config.RollingStepHours) % maxOff;
            var evalSnaps = wfSnaps[offset..(offset + wfEvalWindow)];
            var evalPrices = wfPrices[offset..(offset + wfEvalWindow)];

            var report = evolution.RunGeneration(evalSnaps, evalPrices);

            float? valFit = null;
            string? wfStatus = null;
            bool isValGen = _config.ValidationIntervalGens > 0 && (gen + 1) % _config.ValidationIntervalGens == 0;
            if (isValGen)
            {
                var bestGenome = evolution.GetBestGenome();
                if (bestGenome != null)
                {
                    int valWindow = Math.Min(_config.EvalWindowHours, valPrices.Length);
                    var valResult = evaluator.EvaluateSingle(bestGenome, valSnapshots[..valWindow], valPrices[..valWindow], gen);
                    valFit = valResult.Fitness.Fitness;

                    if (_config.WalkForwardEnabled)
                    {
                        int maxWfOffset = Math.Max(0, trainLen - evalWindow);
                        if (valFit >= _config.WalkForwardMinValFitness)
                        {
                            walkForwardOffset = Math.Min(walkForwardOffset + _config.RollingStepHours, maxWfOffset);
                            stallCount = 0;
                            wfStatus = "PASSED";
                        }
                        else
                        {
                            stallCount++;
                            if (stallCount >= _config.WalkForwardMaxStallGens)
                            {
                                walkForwardOffset = Math.Min(walkForwardOffset + _config.RollingStepHours, maxWfOffset);
                                stallCount = 0;
                                wfStatus = "FORCE";
                            }
                            else
                            {
                                wfStatus = "FAILED";
                            }
                        }
                    }

                    if (valFit > bestValFitness)
                    {
                        bestValFitness = valFit.Value;
                        var valGenomePath = Path.Combine(checkpointDir, $"best_val_gen_{gen:D4}.json");
                        File.WriteAllText(valGenomePath, bestGenome.ToJson());
                    }
                }
            }

            if (report.BestFitness > bestEverFitness)
            {
                bestEverFitness = report.BestFitness;
                var bestGenome = evolution.GetBestGenome();
                if (bestGenome != null)
                {
                    var bestPath = Path.Combine(checkpointDir, $"best_gen_{gen:D4}.json");
                    File.WriteAllText(bestPath, bestGenome.ToJson());
                }
            }

            if (_config.CheckpointIntervalGens > 0 && (gen + 1) % _config.CheckpointIntervalGens == 0)
            {
                var cp = CheckpointState.FromPopulation(evolution.Population, gen, report.BestFitness, evolution.GetSpeciesIds());
                cp.Save(Path.Combine(checkpointDir, $"checkpoint_{gen:D4}.json"));
            }

            _onGeneration(new GenerationReportData(
                gen, report.BestFitness, report.MeanFitness, report.BestSharpe,
                report.BestReturn, report.BestTrades, report.BestWinRate,
                report.SpeciesCount, report.BestSubstrate, valFit, wfStatus));
        }

        var finalBest = evolution.GetBestGenome();
        if (finalBest != null)
        {
            File.WriteAllText(Path.Combine(_config.OutputDirectory, "best_training_genome.json"), finalBest.ToJson());
            File.WriteAllText(_config.ResolvedGenomePath, finalBest.ToJson());
        }

        observatory.Flush();
    }
}
