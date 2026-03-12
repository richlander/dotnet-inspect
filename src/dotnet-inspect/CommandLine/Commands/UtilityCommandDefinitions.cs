using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Defines the cache, demo, samples, llmstxt, skill, perf, and perf-test commands.
/// </summary>
public static class UtilityCommandDefinitions
{
    public static Command CreateCacheCommand(SharedOptions opts)
    {
        var cacheCommand = new Command("cache", "Manage the dotnet-inspect cache");

        var cleanOption = new Option<bool>("--clean", "--clear") { Hidden = true };

        cacheCommand.Options.Add(cleanOption);
        opts.AddOutputOptionsTo(cacheCommand);

        // Subcommand: clear
        var clearCommand = new Command("clear", "Clear the cache");
        var sessionOption = new Option<string?>("--session") { Description = "Clear a named isolated session cache" };
        clearCommand.Options.Add(sessionOption);
        clearCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var session = parseResult.GetValue(sessionOption);
            var options = new CacheOptions(Clean: true, Verbose: false, Session: session);
            return await CacheCommand.ExecuteAsync(options);
        });
        cacheCommand.Subcommands.Add(clearCommand);

        cacheCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var clean = parseResult.GetValue(cleanOption);
            if (clean)
            {
                Console.Error.WriteLine("hint: use 'dotnet-inspect cache clear' instead of --clean/--clear");
            }

            var verbosity = OptionParsers.ParseVerbosity(parseResult.GetValue(opts.Verbosity));
            var options = new CacheOptions(
                Clean: clean,
                Verbose: parseResult.GetValue(opts.Verbose) || verbosity >= Verbosity.Detailed);

            return await CacheCommand.ExecuteAsync(options);
        });

        return cacheCommand;
    }

    public static Command CreateDemoCommand(RootCommand rootCommand, SharedOptions opts)
    {
        var demoCommand = new Command("demo", "Run curated demo queries that showcase the tool");

        var feelingLuckyOption = new Option<bool>("--feeling-lucky") { Description = "Pick a random demo and run it" };
        demoCommand.Options.Add(feelingLuckyOption);
        demoCommand.Options.Add(opts.Limit);

        var indexArg = new Argument<int?>("index")
        {
            Description = "Demo index to run (from 'demo list')",
            Arity = ArgumentArity.ZeroOrOne
        };
        demoCommand.Arguments.Add(indexArg);

        // Subcommand: list
        var listCommand = new Command("list", "List all available demos");
        listCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            return await DemoCommand.ExecuteListAsync();
        });
        demoCommand.Subcommands.Add(listCommand);

        // Default: index, --feeling-lucky, or show help
        demoCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var index = parseResult.GetValue(indexArg);
            if (index.HasValue)
            {
                return await DemoCommand.ExecuteInvokeAsync(index.Value, rootCommand);
            }

            if (parseResult.GetValue(feelingLuckyOption))
            {
                return await DemoCommand.ExecuteFeelingLuckyAsync(rootCommand);
            }

            // No index and no flag: show help + random demo tips
            HelpWriter.WriteHelp(demoCommand);
            var tips = DemoCommand.Demos.Select((d, i) =>
                new Tip("demo", $"{i + 1}", d.Title)).ToArray();
            Hints.WriteTips(TipLevel.Minimal, tips, randomize: true);
            return 0;
        });

        return demoCommand;
    }

    public static Command CreateLlmsTxtCommand(SharedOptions opts)
    {
        var llmsTxtCommand = new Command("llmstxt", "Show usage examples (run this first)");
        llmsTxtCommand.Options.Add(opts.Limit);
        llmsTxtCommand.SetAction((parseResult) => LlmsTxtCommand.Execute());
        return llmsTxtCommand;
    }

    public static Command CreateSkillCommand(SharedOptions opts)
    {
        var skillCommand = new Command("skill", "Show skill definition");
        skillCommand.Options.Add(opts.Limit);
        skillCommand.SetAction((parseResult) => SkillCommand.Execute());
        return skillCommand;
    }

    public static Command CreatePerfCommand()
    {
        var perfCommand = new Command(PerfCommand.Name, "Run operations in a loop for profiling") { Hidden = true };
        var perfTargetArg = new Argument<string>("target") { Description = "Package or library name (e.g., System.CommandLine, System.Text.Json)" };
        var perfIterationsOption = new Option<int>("--iterations") { Description = "Number of iterations (default: 100)" };
        perfIterationsOption.Aliases.Add("-n");
        var perfModeOption = new Option<PerfCommand.Mode>("--mode") { Description = "Test mode: package, version, library, type (default: package)" };
        perfModeOption.Aliases.Add("-m");
        var perfSkipWarmupOption = new Option<bool>("--skip-warmup") { Description = "Skip warmup iteration (test cold start)" };
        perfCommand.Arguments.Add(perfTargetArg);
        perfCommand.Options.Add(perfIterationsOption);
        perfCommand.Options.Add(perfModeOption);
        perfCommand.Options.Add(perfSkipWarmupOption);
        perfCommand.SetAction(async (parseResult) =>
        {
            var target = parseResult.GetValue(perfTargetArg)!;
            var iterations = parseResult.GetValue(perfIterationsOption);
            var mode = parseResult.GetValue(perfModeOption);
            var skipWarmup = parseResult.GetValue(perfSkipWarmupOption);
            return await PerfCommand.ExecuteAsync(target, iterations > 0 ? iterations : 100, mode, skipWarmup);
        });
        return perfCommand;
    }

    public static Command CreatePerfTestCommand()
    {
        var perfTestCommand = new Command(PerfTestCommand.Name, "Run perf test loop for profiling") { Hidden = true };
        var perfTestPathArg = new Argument<string>("path") { Description = "Path to assembly file" };
        var perfTestIterationsOption = new Option<int>("--iterations") { Description = "Number of iterations (default: 1000)" };
        perfTestIterationsOption.Aliases.Add("-n");
        var perfTestTypesOnlyOption = new Option<bool>("--types-only") { Description = "Skip member extraction (types-only mode)" };
        perfTestCommand.Arguments.Add(perfTestPathArg);
        perfTestCommand.Options.Add(perfTestIterationsOption);
        perfTestCommand.Options.Add(perfTestTypesOnlyOption);
        perfTestCommand.SetAction((parseResult) =>
        {
            var path = parseResult.GetValue(perfTestPathArg)!;
            var iterations = parseResult.GetValue(perfTestIterationsOption);
            var typesOnly = parseResult.GetValue(perfTestTypesOnlyOption);
            return PerfTestCommand.Execute(path, iterations > 0 ? iterations : 1000, typesOnly);
        });
        return perfTestCommand;
    }
}
