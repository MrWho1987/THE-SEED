using System.CommandLine;
using Seed.Core;
using Seed.Evolution;
using Seed.Observatory;
using Seed.Genetics;
using Seed.Development;
using Seed.Agents;
using Seed.Worlds;

namespace Seed.App;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Seed Core v1 - Evo-Devo Artificial Life System");

        // Train command
        var trainCommand = new Command("train", "Run evolution training");
        var configOption = new Option<FileInfo?>(
            name: "--config",
            description: "Path to config JSON file (uses default if not specified)");
        var outputOption = new Option<DirectoryInfo>(
            name: "--output",
            description: "Output directory for logs and exports",
            getDefaultValue: () => new DirectoryInfo("output"));
        var seedOption = new Option<ulong?>(
            name: "--seed",
            description: "Random seed (overrides config)");
        var generationsOption = new Option<int?>(
            name: "--generations",
            description: "Number of generations to run (overrides config)");

        trainCommand.AddOption(configOption);
        trainCommand.AddOption(outputOption);
        trainCommand.AddOption(seedOption);
        trainCommand.AddOption(generationsOption);

        trainCommand.SetHandler(TrainHandler, configOption, outputOption, seedOption, generationsOption);
        rootCommand.AddCommand(trainCommand);

        // Export-brain command
        var exportCommand = new Command("export-brain", "Export brain graph from a genome JSON file");
        var genomeOption = new Option<FileInfo>(
            name: "--genome",
            description: "Path to genome JSON file")
        { IsRequired = true };
        var outputFileOption = new Option<FileInfo>(
            name: "--output",
            description: "Output path for brain graph JSON",
            getDefaultValue: () => new FileInfo("brain.json"));

        exportCommand.AddOption(genomeOption);
        exportCommand.AddOption(outputFileOption);

        exportCommand.SetHandler(ExportBrainHandler, genomeOption, outputFileOption);
        rootCommand.AddCommand(exportCommand);

        // Init-config command
        var initConfigCommand = new Command("init-config", "Create a default config file");
        var initOutputOption = new Option<FileInfo>(
            name: "--output",
            description: "Output path for config file",
            getDefaultValue: () => new FileInfo("config.json"));

        initConfigCommand.AddOption(initOutputOption);
        initConfigCommand.SetHandler(InitConfigHandler, initOutputOption);
        rootCommand.AddCommand(initConfigCommand);

        return await rootCommand.InvokeAsync(args);
    }

    static void TrainHandler(
        FileInfo? configFile,
        DirectoryInfo outputDir,
        ulong? seed,
        int? generations)
    {
        Console.WriteLine("=== Seed Core v1 Training ===");
        Console.WriteLine();

        // Load or create config
        RunConfig config;
        if (configFile?.Exists == true)
        {
            Console.WriteLine($"Loading config from: {configFile.FullName}");
            config = RunConfig.LoadFromFile(configFile.FullName);
        }
        else
        {
            Console.WriteLine("Using default config");
            config = RunConfig.Default;
        }

        // Apply overrides
        if (seed.HasValue)
        {
            config = config with { RunSeed = seed.Value };
            Console.WriteLine($"Seed override: {seed.Value}");
        }
        if (generations.HasValue)
        {
            config = config with { MaxGenerations = generations.Value };
            Console.WriteLine($"Generations override: {generations.Value}");
        }

        Console.WriteLine($"Output directory: {outputDir.FullName}");
        Console.WriteLine($"Population size: {config.Budgets.Population.PopulationSize}");
        Console.WriteLine($"Max generations: {config.MaxGenerations}");
        Console.WriteLine();

        // Create output directory
        outputDir.Create();

        // Save used config
        config.SaveToFile(Path.Combine(outputDir.FullName, "config.json"));

        // Create observatory
        using var observatory = new FileObservatory(outputDir.FullName);

        // Create evolution loop
        var evolution = new EvolutionLoop(config, observatory);
        evolution.Initialize();

        Console.WriteLine("Starting evolution...");
        Console.WriteLine();

        var startTime = DateTime.Now;

        for (int gen = 0; gen < config.MaxGenerations; gen++)
        {
            var genStart = DateTime.Now;
            evolution.RunGeneration();
            var genTime = DateTime.Now - genStart;

            var bestEval = evolution.GetBestEvaluation();
            var bestScore = bestEval?.Aggregate.Score ?? 0f;

            Console.WriteLine($"Gen {gen + 1:D4} | Best: {bestScore,8:F2} | Species: {evolution.SpeciesCount,3} | Time: {genTime.TotalSeconds:F1}s");

            // Export best genome every 10 generations
            if ((gen + 1) % 10 == 0 && bestEval != null)
            {
                observatory.ExportGenome(bestEval.Genome, $"best_gen_{gen + 1:D4}.json");
            }
        }

        var totalTime = DateTime.Now - startTime;
        Console.WriteLine();
        Console.WriteLine($"Training complete! Total time: {totalTime.TotalMinutes:F1} minutes");

        // Export final best genome
        var finalBestEval = evolution.GetBestEvaluation();
        if (finalBestEval != null)
        {
            observatory.ExportGenome(finalBestEval.Genome, "best_final.json");
            Console.WriteLine($"Best genome exported to: {Path.Combine(outputDir.FullName, "best_final.json")}");
        }
    }

    static void ExportBrainHandler(FileInfo genomeFile, FileInfo outputFile)
    {
        Console.WriteLine($"Loading genome from: {genomeFile.FullName}");

        var json = File.ReadAllText(genomeFile.FullName);
        var genome = SeedGenome.FromJson(json);

        Console.WriteLine($"Genome ID: {genome.GenomeId}");
        Console.WriteLine($"CPPN nodes: {genome.Cppn.Nodes.Count}");
        Console.WriteLine($"CPPN connections: {genome.Cppn.Connections.Count}");

        // Compile to brain graph
        var agentConfig = AgentConfig.Default;
        var developer = new BrainDeveloper(agentConfig.TotalSensorCount, ContinuousWorld.ActuatorCount);
        var devBudget = new DevelopmentBudget();
        var devCtx = new DevelopmentContext(0, 0);

        var graph = developer.CompileGraph(genome, devBudget, devCtx);

        Console.WriteLine($"Brain nodes: {graph.NodeCount}");
        Console.WriteLine($"Brain edges: {graph.EdgeCount}");

        File.WriteAllText(outputFile.FullName, graph.ToJson());
        Console.WriteLine($"Brain graph exported to: {outputFile.FullName}");
    }

    static void InitConfigHandler(FileInfo outputFile)
    {
        var config = RunConfig.Default;
        config.SaveToFile(outputFile.FullName);
        Console.WriteLine($"Default config saved to: {outputFile.FullName}");
    }
}
