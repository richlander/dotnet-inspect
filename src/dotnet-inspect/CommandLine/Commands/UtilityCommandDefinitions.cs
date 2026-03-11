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

    public static Command CreateSamplesCommand(SharedOptions opts)
    {
        var samplesCommand = new Command("samples", "Show sample code references for a type or library");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Package and type name. When no --package/--library/--platform is given, first arg is the package.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package") { Description = "Extract from package (name or name@version)" };
        var assemblyOption = new Option<string?>("--library") { Description = "Library path" };
        var platformOption = new Option<string?>("--platform") { Description = "Extract from platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Platform framework (runtime, aspnetcore, netstandard). Use @version for specific version" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM" };
        var browsableUrlsOption = new Option<bool>("--browsable-urls") { Description = "Use /blob/ URLs for browser viewing instead of /raw/ URLs" };
        var listOption = new Option<bool>("--list") { Description = "List samples only (don't fetch content)" };
        var printOption = new Option<int?>("--print") { Description = "Print specific sample by number (raw code, no markdown)", Arity = ArgumentArity.ExactlyOne };
        var fileOption = new Option<string?>("--file") { Description = "Read a local .cs file directly (skips SourceLink/PDB)" };
        var regionOption = new Option<string?>("--region") { Description = "Extract a specific #region from the file (used with --file)" };

        samplesCommand.Arguments.Add(argsArg);
        samplesCommand.Options.Add(packageOption);
        samplesCommand.Options.Add(assemblyOption);
        samplesCommand.Options.Add(platformOption);
        samplesCommand.Options.Add(frameworkOption);
        samplesCommand.Options.Add(tfmOption);
        samplesCommand.Options.Add(browsableUrlsOption);
        samplesCommand.Options.Add(listOption);
        samplesCommand.Options.Add(printOption);
        samplesCommand.Options.Add(fileOption);
        samplesCommand.Options.Add(regionOption);
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        samplesCommand.Options.Add(oneLineOption);
        samplesCommand.Options.Add(noHeaderOption);
        samplesCommand.Options.Add(opts.Columns);
        samplesCommand.Options.Add(opts.Fields);
        opts.AddOutputOptionsTo(samplesCommand);
        opts.AddNuGetOptionsTo(samplesCommand);

        samplesCommand.SetAction(async (parseResult, ct) =>
        {
            var args = parseResult.GetValue(argsArg) ?? [];
            var explicitPackage = parseResult.GetValue(packageOption);
            var explicitAssembly = parseResult.GetValue(assemblyOption);
            var explicitPlatform = parseResult.GetValue(platformOption);
            bool hasExplicitSource = explicitPackage != null || explicitAssembly != null || explicitPlatform != null;

            string? packagePath = explicitPackage;
            string? typeName = null;

            if (hasExplicitSource)
            {
                if (args.Length >= 1) typeName = args[0];
            }
            else
            {
                if (args.Length >= 1) packagePath = args[0];
                if (args.Length >= 2) typeName = args[1];

                if (CommandLineHelpers.LooksLikeVersionNumber(typeName))
                {
                    Console.Error.WriteLine($"Error: '{typeName}' looks like a version number. Use '{packagePath}@{typeName}' to specify a version.");
                    return 1;
                }

                // Route file paths (.dll → --library, .nupkg stays as package path)
                if (CommandLineHelpers.TryClassifyAsFilePath(packagePath, out var dllPath, out var nupkgPath))
                {
                    if (dllPath != null) { explicitAssembly = dllPath; packagePath = null; }
                    else if (nupkgPath != null) { packagePath = nupkgPath; }
                }
            }

            var options = new SamplesOptions
            {
                TypeName = typeName,
                PackagePath = packagePath,
                AssemblyPath = explicitAssembly,
                PlatformAssembly = explicitPlatform,
                PlatformFramework = parseResult.GetValue(frameworkOption),
                Tfm = parseResult.GetValue(tfmOption),
                BrowsableUrls = parseResult.GetValue(browsableUrlsOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                ListOnly = parseResult.GetValue(listOption),
                PrintSample = parseResult.GetValue(printOption),
                FilePath = parseResult.GetValue(fileOption),
                Region = parseResult.GetValue(regionOption),
                OneLine = opts.ResolveOneLine(parseResult, oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            return await SamplesCommand.ExecuteAsync(options);
        });

        return samplesCommand;
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
