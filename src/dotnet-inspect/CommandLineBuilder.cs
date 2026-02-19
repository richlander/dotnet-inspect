using System.CommandLine;
using System.CommandLine.Help;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector;

/// <summary>
/// Builds the System.CommandLine command structure.
/// </summary>
public static class CommandLineBuilder
{
    /// <summary>
    /// When the -NN shorthand is used (e.g. -30), stores the line limit.
    /// Regular -n N does not set this — only the shorthand triggers line limiting.
    /// </summary>
    public static int? HeadLines { get; private set; }

    /// <summary>
    /// Known commands for implicit package command detection.
    /// </summary>
    public static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "package", "library", "api", "type", "member", "diff", "find", "search", "samples", "list", "ls", "llmstxt", "skill", "extensions", "implements", "depends", "cache", "cli", "demo", "perf", "perf-test", "help", "--help", "-h", "-?", "--version"
    };

    /// <summary>
    /// Platform framework names for --platform scope.
    /// </summary>
    internal static readonly string[] PlatformFrameworkNames = ["runtime", "aspnetcore", "netstandard"];

    /// <summary>
    /// Curated Microsoft.Extensions.* packages for --extensions scope.
    /// </summary>
    internal static readonly string[] ExtensionsScopePackages =
    [
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Hosting.Abstractions",
        "Microsoft.Extensions.FileProviders.Abstractions",
        "Microsoft.Extensions.Http",
        "Microsoft.Extensions.Caching.Memory",
        "Microsoft.Extensions.Caching.Abstractions",
        "Microsoft.Extensions.Telemetry.Abstractions",
        "Microsoft.Extensions.AI",
        "Microsoft.Extensions.AI.Abstractions",
    ];

    /// <summary>
    /// Curated Microsoft.AspNetCore.* packages for --aspnetcore scope.
    /// </summary>
    internal static readonly string[] AspNetCoreScopePackages =
    [
        "Microsoft.AspNetCore.Authentication",
        "Microsoft.AspNetCore.Authorization",
        "Microsoft.AspNetCore.Components",
        "Microsoft.AspNetCore.Mvc.Core",
        "Microsoft.AspNetCore.SignalR",
    ];

    /// <summary>
    /// Small default package set for --curated scope (the implicit default).
    /// </summary>
    internal static readonly string[] CuratedScopePackages =
    [
        "Microsoft.Extensions.AI",
        "Microsoft.Extensions.AI.Abstractions",
    ];

    /// <summary>
    /// Pre-processes args to handle implicit package command and platform framework shorthands.
    /// </summary>
    public static string[] PreprocessArgs(string[] args)
    {
        // Expand -NN shorthand (e.g., -30) into -n 30, like head -30
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Length >= 2 && args[i][0] == '-' && char.IsDigit(args[i][1])
                && int.TryParse(args[i].AsSpan(1), out var headN))
            {
                HeadLines = headN;
                args = [.. args[..i], "-n", args[i][1..], .. args[(i + 1)..]];
                break;
            }
        }

        // Find the first positional argument, skipping any leading options
        int firstPositional = -1;
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith('-'))
            {
                // Skip the value token that follows -n (it's a number, not a command)
                if (i > 0 && args[i - 1] == "-n") continue;
                firstPositional = i;
                break;
            }
        }

        if (firstPositional >= 0 && !KnownCommands.Contains(args[firstPositional]))
        {
            if (TryClassifyAsFilePath(args[firstPositional], out var dllPath, out var nupkgPath))
            {
                if (dllPath != null) return ["library", .. args];
                if (nupkgPath != null) return ["package", .. args];
            }

            // Route bare names through the router command (platform-preferred, NuGet fallback)
            return ["router", .. args];
        }

        return args;
    }

    /// <summary>
    /// Creates the root command with all subcommands configured.
    /// </summary>
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand(
            $"{VersionInfo.ToolName} {VersionInfo.Version} - A CLI tool for inspecting .NET libraries and NuGet packages");

        // Shared options (defined once, reused across commands)
        var jsonOption = new Option<bool>("--json") { Description = "Output as JSON" };
        var markoutOption = new Option<bool>("--markout") { Description = "Output as Markout (default)" };
        var verboseOption = new Option<bool>("--verbose") { Description = "Show progress messages on stderr" };
        var verbosityOption = new Option<string?>("-v") { Description = "Verbosity: q(uiet), m(inimal), n(ormal), d(etailed)" };
        var includeSectionsOption = new Option<string?>("-s") { Description = "Include sections by name (comma-separated, supports wildcards). Use -s alone to list.", Arity = ArgumentArity.ZeroOrOne };
        var excludeSectionsOption = new Option<string?>("-x") { Description = "Exclude sections by name (comma-separated, e.g., -x:Methods)" };
        var limitOption = new Option<int?>("-n") { Description = "Limit number of results" };
        var tipsOption = new Option<string?>("--tips") { Description = "Tip verbosity: q(uiet), m(inimal), d(etailed)", Arity = ArgumentArity.ZeroOrOne };
        tipsOption.Aliases.Add("-T");

        // NuGet source options (shared across package-consuming commands)
        var sourceOption = new Option<string[]>("--source")
        {
            Description = "NuGet source URL (replaces defaults, can repeat)",
            AllowMultipleArgumentsPerToken = true
        };
        var addSourceOption = new Option<string[]>("--add-source")
        {
            Description = "NuGet source URL to add (can repeat)",
            AllowMultipleArgumentsPerToken = true
        };
        var nugetConfigOption = new Option<string?>("--nugetconfig")
        {
            Description = "Path to nuget.config file"
        };

        // Commands in alphabetical order (llmstxt last as meta command)

        // Root-level display option (distinct instance so it appears in root help)
        var rootVerbosityOption = new Option<string?>("-v") { Description = "Verbosity: q(uiet), m(inimal), n(ormal), d(etailed)" };
        rootCommand.Options.Add(rootVerbosityOption);
        var rootTipsOption = new Option<string?>("--tips") { Description = "Tip verbosity: q(uiet), m(inimal), d(etailed)", Arity = ArgumentArity.ZeroOrOne };
        rootTipsOption.Aliases.Add("-T");
        rootCommand.Options.Add(rootTipsOption);
        var offlineOption = new Option<bool>("--offline") { Description = "Disable all network access (use cached data only)" };
        rootCommand.Options.Add(offlineOption);

        // API command (deprecated, hidden)
        var apiCommand = CreateDeprecatedApiCommand();
        rootCommand.Subcommands.Add(apiCommand);

        // Type command (type discovery, terse)
        var typeCommand = CreateTypeCommand(jsonOption, markoutOption, verboseOption, verbosityOption, tipsOption, limitOption, includeSectionsOption, excludeSectionsOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(typeCommand);

        // Member command (member inspection, docs by default)
        var memberCommand = CreateMemberCommand(jsonOption, markoutOption, verboseOption, verbosityOption, tipsOption, limitOption, includeSectionsOption, excludeSectionsOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(memberCommand);

        // Assembly command
        var assemblyCommand = CreateAssemblyCommand(jsonOption, markoutOption, verboseOption, verbosityOption, tipsOption, includeSectionsOption, excludeSectionsOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(assemblyCommand);

        // Cache command
        var cacheCommand = CreateCacheCommand(verboseOption, verbosityOption, tipsOption, limitOption);
        rootCommand.Subcommands.Add(cacheCommand);

        // Demo command
        var demoCommand = CreateDemoCommand(rootCommand, limitOption);
        rootCommand.Subcommands.Add(demoCommand);

        // Diff command
        var diffCommand = CreateDiffCommand(verboseOption, verbosityOption, tipsOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(diffCommand);

        // Depends command
        var dependsCommand = CreateDependsCommand(jsonOption, verboseOption, verbosityOption, tipsOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(dependsCommand);

        // Extensions command
        var extensionsCommand = CreateExtensionsCommand(jsonOption, verboseOption, verbosityOption, tipsOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(extensionsCommand);

        // Find command
        var findCommand = CreateFindCommand(jsonOption, verboseOption, verbosityOption, tipsOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(findCommand);

        // Implements command
        var implementsCommand = CreateImplementsCommand(jsonOption, verboseOption, verbosityOption, tipsOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(implementsCommand);

        // Package command
        var packageCommand = CreatePackageCommand(jsonOption, markoutOption, verboseOption, verbosityOption, tipsOption, includeSectionsOption, excludeSectionsOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(packageCommand);

        // Router command (hidden, implicit default for bare names)
        var routerCommand = CreateRouterCommand(jsonOption, markoutOption, verboseOption, verbosityOption, tipsOption, includeSectionsOption, excludeSectionsOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(routerCommand);

        // Samples command
        var samplesCommand = CreateSamplesCommand(verboseOption, verbosityOption, tipsOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(samplesCommand);

        // CLI command (meta command)
        var schemaCommand = new Command("cli", "Show CLI command structure as API listing");
        var schemaCommandArg = new Argument<string?>("command") { Description = "Command name to show (omit for all)", Arity = ArgumentArity.ZeroOrOne };
        schemaCommand.Arguments.Add(schemaCommandArg);
        schemaCommand.Options.Add(verbosityOption);
        schemaCommand.Options.Add(limitOption);
        schemaCommand.SetAction((parseResult) =>
        {
            var commandFilter = parseResult.GetValue(schemaCommandArg);
            var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
            return CliSchemaCommand.Execute(rootCommand, commandFilter, verbosity);
        });
        rootCommand.Subcommands.Add(schemaCommand);

        // LLMs.txt command (meta command, listed last)
        var llmsTxtCommand = new Command("llmstxt", "Show usage examples (run this first)");
        llmsTxtCommand.Options.Add(limitOption);
        llmsTxtCommand.SetAction((parseResult) => LlmsTxtCommand.Execute());
        rootCommand.Subcommands.Add(llmsTxtCommand);

        var skillCommand = new Command("skill", "Show skill definition");
        skillCommand.Options.Add(limitOption);
        skillCommand.SetAction((parseResult) => SkillCommand.Execute());
        rootCommand.Subcommands.Add(skillCommand);

        // Perf command (hidden, for profiling package inspection path)
        var perfCommand = new Command(PerfCommand.Name, "Run package inspection loop for profiling") { Hidden = true };
        var perfPackageArg = new Argument<string>("package") { Description = "Package name (e.g., System.CommandLine)" };
        var perfIterationsOption = new Option<int>("--iterations") { Description = "Number of iterations (default: 100)" };
        perfIterationsOption.Aliases.Add("-n");
        perfCommand.Arguments.Add(perfPackageArg);
        perfCommand.Options.Add(perfIterationsOption);
        perfCommand.SetAction(async (parseResult) =>
        {
            var package = parseResult.GetValue(perfPackageArg)!;
            var iterations = parseResult.GetValue(perfIterationsOption);
            return await PerfCommand.ExecuteAsync(package, iterations > 0 ? iterations : 100);
        });
        rootCommand.Subcommands.Add(perfCommand);

        // Perf-test command (hidden, for profiling)
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
        rootCommand.Subcommands.Add(perfTestCommand);

        // No-args: show help + tips
        rootCommand.SetAction((parseResult) =>
        {
            var sw = new System.IO.StringWriter();
            var original = Console.Out;
            Console.SetOut(sw);
            new HelpAction().Invoke(parseResult);
            Console.SetOut(original);
            Console.WriteLine(sw.ToString().TrimEnd());

            var verbosity = ParseVerbosity(parseResult.GetValue(rootVerbosityOption));
            var tipLevel = verbosity == Verbosity.Quiet
                ? TipLevel.Quiet : ParseTipLevel(parseResult.GetValue(rootTipsOption), parseResult.GetResult(rootTipsOption) != null);
            Hints.WriteTips(tipLevel,
                new Tip(PackageCommand.Name, "<package>", "inspect a NuGet package"),
                new Tip(LlmsTxtCommand.Name, "", "complete usage examples"),
                new Tip("-T:d", "", "show more tips per command"),
                new Tip(TypeCommand.Name, "--package <package>", "discover types in package"),
                new Tip(MemberCommand.Name, "JsonSerializer --package System.Text.Json", "inspect type members"),
                new Tip(FindCommand.Name, "<pattern> --package <package>", "search package types"),
                new Tip(FindCommand.Name, "<pattern> --platform", "search platform libraries"));
        });

        return rootCommand;
    }

    private static Command CreateCacheCommand(Option<bool> verboseOption, Option<string?> verbosityOption, Option<string?> tipsOption, Option<int?> limitOption)
    {
        var cacheCommand = new Command("cache", "Manage the dotnet-inspect cache");

        var cleanOption = new Option<bool>("--clean") { Description = "Clear the cache" };

        cacheCommand.Options.Add(cleanOption);
        cacheCommand.Options.Add(verboseOption);
        cacheCommand.Options.Add(verbosityOption);
        cacheCommand.Options.Add(tipsOption);
        cacheCommand.Options.Add(limitOption);

        cacheCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
            var options = new CacheOptions(
                Clean: parseResult.GetValue(cleanOption),
                Verbose: parseResult.GetValue(verboseOption) || verbosity >= Verbosity.Detailed);

            return await CacheCommand.ExecuteAsync(options);
        });

        return cacheCommand;
    }

    private static Command CreateDemoCommand(RootCommand rootCommand, Option<int?> limitOption)
    {
        var demoCommand = new Command("demo", "Run curated demo queries that showcase the tool");

        var feelingLuckyOption = new Option<bool>("--feeling-lucky") { Description = "Pick a random demo and run it" };
        demoCommand.Options.Add(feelingLuckyOption);
        demoCommand.Options.Add(limitOption);

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
            new HelpAction().Invoke(parseResult);
            var tips = DemoCommand.Demos.Select((d, i) =>
                new Tip("demo", $"{i + 1}", d.Title)).ToArray();
            Hints.WriteTips(TipLevel.Minimal, tips, randomize: true);
            return 0;
        });

        return demoCommand;
    }

    private static Command CreateDiffCommand(
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var diffCommand = new Command(DiffCommand.Name, "Compare API surfaces between package or platform versions");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Version range and type filter. When no --package/--platform is given, first arg is the package version range.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package")
        {
            Description = "Package with version range (e.g., System.Text.Json@9.0.0..10.0.2)"
        };
        var platformOption = new Option<string?>("--platform")
        {
            Description = "Platform library with version range (e.g., System.Text.Json@8.0.23..10.0.2)"
        };
        var frameworkOption = new Option<string?>("--framework")
        {
            Description = "Framework for platform diff (runtime, aspnetcore). Default: runtime"
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden/obsolete members" };
        var typeFilterOption = new Option<string[]>("-t")
        {
            Description = "Filter to specific type(s)",
            AllowMultipleArgumentsPerToken = true
        };
        typeFilterOption.Aliases.Add("--type");
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        var nameOnlyOption = new Option<bool>("--name-only") { Description = "Show only type names that changed" };
        var breakingOption = new Option<bool>("--breaking") { Description = "Show only breaking changes" };
        var additiveOption = new Option<bool>("--additive") { Description = "Show only additive changes" };

        diffCommand.Arguments.Add(argsArg);
        diffCommand.Options.Add(packageOption);
        diffCommand.Options.Add(platformOption);
        diffCommand.Options.Add(frameworkOption);
        diffCommand.Options.Add(tfmOption);
        diffCommand.Options.Add(allOption);
        diffCommand.Options.Add(typeFilterOption);
        diffCommand.Options.Add(oneLineOption);
        diffCommand.Options.Add(noHeaderOption);
        diffCommand.Options.Add(nameOnlyOption);
        diffCommand.Options.Add(breakingOption);
        diffCommand.Options.Add(additiveOption);
        diffCommand.Options.Add(verboseOption);
        diffCommand.Options.Add(verbosityOption);
        diffCommand.Options.Add(tipsOption);
        diffCommand.Options.Add(limitOption);
        diffCommand.Options.Add(sourceOption);
        diffCommand.Options.Add(addSourceOption);
        diffCommand.Options.Add(nugetConfigOption);

        diffCommand.SetAction(async (parseResult, ct) =>
        {
            var args = parseResult.GetValue(argsArg) ?? [];
            var explicitPackage = parseResult.GetValue(packageOption);
            var explicitPlatform = parseResult.GetValue(platformOption);
            bool hasExplicitSource = explicitPackage != null || explicitPlatform != null;

            string? packageVersionRange = explicitPackage;
            string? platformVersionRange = explicitPlatform;
            string? typeName = null;

            if (hasExplicitSource)
            {
                // All positionals are type filters
                if (args.Length >= 1) typeName = args[0];
            }
            else
            {
                // First positional is the package version range
                if (args.Length >= 1) packageVersionRange = args[0];
                if (args.Length >= 2) typeName = args[1];

                if (LooksLikeVersionNumber(typeName))
                {
                    Console.Error.WriteLine($"Error: '{typeName}' looks like a version number. Use '{packageVersionRange}@{typeName}' to specify a version.");
                    return 1;
                }
            }

            var typeFilterValues = parseResult.GetValue(typeFilterOption);

            // Merge positional type name with -t filter
            HashSet<string> typeFilter = [];
            if (typeFilterValues?.Length > 0 || !string.IsNullOrEmpty(typeName))
            {
                typeFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrEmpty(typeName))
                    typeFilter.Add(typeName);
                if (typeFilterValues != null)
                {
                    foreach (var t in typeFilterValues)
                        typeFilter.Add(t);
                }
            }

            var options = new DiffOptions
            {
                PackageVersionRange = packageVersionRange,
                PlatformVersionRange = platformVersionRange,
                Framework = parseResult.GetValue(frameworkOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Verbose = parseResult.GetValue(verboseOption),
                TypeFilter = typeFilter,
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                NameOnly = parseResult.GetValue(nameOnlyOption),
                Breaking = parseResult.GetValue(breakingOption),
                Additive = parseResult.GetValue(additiveOption),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            var exitCode = await DiffCommand.ExecuteAsync(options);

            var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
            var tipLevel = options.IsRawOutput || verbosity == Verbosity.Quiet
                ? TipLevel.Quiet : ParseTipLevel(parseResult.GetValue(tipsOption), parseResult.GetResult(tipsOption) != null);

            if (exitCode == 0)
            {
                List<Tip> tips = [];
                var versionRange = options.PackageVersionRange ?? options.PlatformVersionRange;
                var sourceFlag = options.PackageVersionRange != null ? "--package" : "--platform";

                if (typeFilter != null)
                    tips.Add(new(DiffCommand.Name, $"{sourceFlag} {versionRange}", "diff all types"));

                if (versionRange != null)
                {
                    var atIdx = versionRange.IndexOf('@');
                    var dotDotIdx = versionRange.IndexOf("..", StringComparison.Ordinal);
                    if (atIdx > 0 && dotDotIdx > atIdx)
                    {
                        var pkgName = versionRange[..atIdx];
                        var toVersion = versionRange[(dotDotIdx + 2)..];
                        if (!options.OneLine && !options.NameOnly)
                            tips.Add(new(TypeCommand.Name, $"<TypeName> {sourceFlag} {pkgName}@{toVersion} --shape", "view current type shape"));
                        if (!options.OneLine)
                            tips.Add(new(DiffCommand.Name, $"{sourceFlag} {versionRange} --oneline", "summary statistics"));
                    }
                }

                tips.Add(new(LlmsTxtCommand.Name, "", "complete usage examples"));
                Hints.WriteTips(tipLevel, [.. tips]);
            }

            return exitCode;
        });

        return diffCommand;
    }

    private static Command CreateDependsCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var dependsCommand = new Command("depends", "Walk dependency graphs upward (type hierarchy, library references, or package dependencies)");

        var targetTypeArg = new Argument<string?>("type")
        {
            Description = "Type name to walk dependencies for (e.g., IFloatingPointIeee754, Int128)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Search in package(s) (name or name@version). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var platformOption = new Option<bool>("--platform") { Description = "Search all platform frameworks (runtime, aspnetcore, netstandard)" };
        var extensionsOption = new Option<bool>("--extensions") { Description = "Search curated Microsoft.Extensions.* packages" };
        var aspnetcoreOption = new Option<bool>("--aspnetcore") { Description = "Search curated Microsoft.AspNetCore.* packages" };
        var curatedOption = new Option<bool>("--curated") { Description = "Use default curated scope explicitly", Hidden = true };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };

        dependsCommand.Arguments.Add(targetTypeArg);
        dependsCommand.Options.Add(packageOption);
        dependsCommand.Options.Add(assemblyOption);
        dependsCommand.Options.Add(platformOption);
        dependsCommand.Options.Add(extensionsOption);
        dependsCommand.Options.Add(aspnetcoreOption);
        dependsCommand.Options.Add(curatedOption);
        dependsCommand.Options.Add(tfmOption);
        dependsCommand.Options.Add(jsonOption);
        dependsCommand.Options.Add(compactOption);
        dependsCommand.Options.Add(verboseOption);
        dependsCommand.Options.Add(verbosityOption);
        dependsCommand.Options.Add(sourceOption);
        dependsCommand.Options.Add(addSourceOption);
        dependsCommand.Options.Add(nugetConfigOption);
        dependsCommand.Options.Add(tipsOption);
        dependsCommand.Options.Add(limitOption);

        dependsCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);
            var packages = parseResult.GetValue(packageOption) ?? [];
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];

            // Mode detection: no type arg → library or package dependency mode
            if (string.IsNullOrEmpty(targetType))
            {
                var commonOptions = new DependsOptions
                {
                    Tfm = parseResult.GetValue(tfmOption),
                    JsonOutput = parseResult.GetValue(jsonOption),
                    CompactJson = parseResult.GetValue(compactOption),
                    Verbose = parseResult.GetValue(verboseOption),
                    SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
                };

                if (assemblies.Length == 1 && packages.Length == 0)
                    return await DependsCommand.ExecuteLibraryDependsAsync(commonOptions with { LibraryName = assemblies[0] });

                if (packages.Length == 1 && assemblies.Length == 0)
                    return await DependsCommand.ExecutePackageDependsAsync(commonOptions with { PackageName = packages[0] });

                new HelpAction().Invoke(parseResult);
                Console.Error.WriteLine();
                Console.Error.WriteLine("Tips:");
                Console.Error.WriteLine("  depends IFloatingPointIeee754 --platform   # type hierarchy");
                Console.Error.WriteLine("  depends --library Microsoft.Extensions.AI   # assembly references");
                Console.Error.WriteLine("  depends --package System.Text.Json          # NuGet dependencies");
                return 0;
            }

            bool wantPlatform = parseResult.GetValue(platformOption);
            bool wantExtensions = parseResult.GetValue(extensionsOption);
            bool wantAspnetcore = parseResult.GetValue(aspnetcoreOption);
            bool wantCurated = parseResult.GetValue(curatedOption);
            bool hasExplicitScope = wantPlatform || wantExtensions || wantAspnetcore || wantCurated
                || packages.Length > 0 || assemblies.Length > 0;

            // Resolve scope
            string[] frameworks = [];
            if (!hasExplicitScope)
            {
                // Default scope: all platform frameworks + curated packages
                frameworks = PlatformFrameworkNames;
                packages = [.. packages, .. CuratedScopePackages];
            }
            else
            {
                if (wantPlatform) frameworks = PlatformFrameworkNames;
                if (wantExtensions) packages = [.. packages, .. ExtensionsScopePackages];
                if (wantAspnetcore) packages = [.. packages, .. AspNetCoreScopePackages];
                if (wantCurated)
                {
                    frameworks = [.. frameworks, .. PlatformFrameworkNames];
                    packages = [.. packages, .. CuratedScopePackages];
                }
            }

            var options = new DependsOptions
            {
                TargetType = targetType,
                Packages = packages,
                Assemblies = assemblies,
                PlatformAssemblies = [],
                PlatformFrameworks = frameworks,
                Tfm = parseResult.GetValue(tfmOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(verboseOption),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await DependsCommand.ExecuteTypeDependsAsync(options);
        });

        return dependsCommand;
    }

    private static Command CreateExtensionsCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var extCommand = new Command("extensions", "Find extension methods for a type");

        var targetTypeArg = new Argument<string?>("type")
        {
            Description = "Target type to find extensions for (e.g., HttpClient, IEnumerable<T>)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Search in package(s) (name or name@version). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var platformOption = new Option<bool>("--platform") { Description = "Search all platform frameworks (runtime, aspnetcore, netstandard)" };
        var extensionsOption = new Option<bool>("--extensions") { Description = "Search curated Microsoft.Extensions.* packages" };
        var aspnetcoreOption = new Option<bool>("--aspnetcore") { Description = "Search curated Microsoft.AspNetCore.* packages" };
        var curatedOption = new Option<bool>("--curated") { Description = "Use default curated scope explicitly", Hidden = true };
        var reachableOption = new Option<bool>("--reachable")
        {
            Description = "Include extensions on types reachable via properties/methods"
        };
        var depthOption = new Option<int>("--depth")
        {
            Description = "Max depth for reachable type traversal (default: 2)",
            DefaultValueFactory = _ => 2
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden/obsolete members" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var packagePrefixOption = new Option<string?>("--package-prefix") { Description = "Search all packages matching a NuGet ID prefix (e.g., Azure.AI, AWSSDK)" };
        extCommand.Arguments.Add(targetTypeArg);
        extCommand.Options.Add(packageOption);
        extCommand.Options.Add(assemblyOption);
        extCommand.Options.Add(platformOption);
        extCommand.Options.Add(extensionsOption);
        extCommand.Options.Add(aspnetcoreOption);
        extCommand.Options.Add(curatedOption);
        extCommand.Options.Add(reachableOption);
        extCommand.Options.Add(depthOption);
        extCommand.Options.Add(tfmOption);
        extCommand.Options.Add(allOption);
        extCommand.Options.Add(limitOption);
        extCommand.Options.Add(jsonOption);
        extCommand.Options.Add(compactOption);
        extCommand.Options.Add(packagePrefixOption);
        extCommand.Options.Add(verboseOption);
        extCommand.Options.Add(verbosityOption);
        extCommand.Options.Add(sourceOption);
        extCommand.Options.Add(addSourceOption);
        extCommand.Options.Add(nugetConfigOption);
        extCommand.Options.Add(tipsOption);

        extCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);

            if (string.IsNullOrEmpty(targetType))
            {
                new HelpAction().Invoke(parseResult);
                Console.Error.WriteLine();
                Console.Error.WriteLine("Tips:");
                Console.Error.WriteLine("  extensions HttpClient                     # search default scope");
                Console.Error.WriteLine("  extensions HttpClient --platform          # platform libraries only");
                Console.Error.WriteLine("  extensions HttpClient --extensions         # Microsoft.Extensions packages");
                Console.Error.WriteLine("  extensions HttpClient --aspnetcore         # ASP.NET Core packages");
                Console.Error.WriteLine("  extensions HttpClient --package Foo        # specific package");
                Console.Error.WriteLine("  extensions HttpClient --platform --extensions  # combine scopes");
                return 0;
            }

            var packages = parseResult.GetValue(packageOption) ?? [];
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];
            var packagePrefix = parseResult.GetValue(packagePrefixOption);

            bool wantPlatform = parseResult.GetValue(platformOption);
            bool wantExtensions = parseResult.GetValue(extensionsOption);
            bool wantAspnetcore = parseResult.GetValue(aspnetcoreOption);
            bool wantCurated = parseResult.GetValue(curatedOption);
            bool hasExplicitScope = wantPlatform || wantExtensions || wantAspnetcore || wantCurated
                || packages.Length > 0 || assemblies.Length > 0 || packagePrefix != null;

            // Resolve --package-prefix to package list
            if (packagePrefix != null)
            {
                var prefixPackages = await ResolvePrefixPackagesAsync(packagePrefix, parseResult.GetValue(verboseOption));
                packages = [..packages, ..prefixPackages];
            }

            // Resolve scope
            string[] frameworks = [];
            if (!hasExplicitScope)
            {
                // Default scope: all platform frameworks + curated packages
                frameworks = PlatformFrameworkNames;
                packages = [..packages, ..CuratedScopePackages];
            }
            else
            {
                if (wantPlatform) frameworks = PlatformFrameworkNames;
                if (wantExtensions) packages = [..packages, ..ExtensionsScopePackages];
                if (wantAspnetcore) packages = [..packages, ..AspNetCoreScopePackages];
                if (wantCurated)
                {
                    frameworks = [..frameworks, ..PlatformFrameworkNames];
                    packages = [..packages, ..CuratedScopePackages];
                }
            }

            var options = new ExtensionsOptions
            {
                TargetType = targetType,
                Packages = packages,
                Assemblies = assemblies,
                PlatformAssemblies = [],
                PlatformFrameworks = frameworks,
                Reachable = parseResult.GetValue(reachableOption),
                Depth = parseResult.GetValue(depthOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                PackagePrefix = packagePrefix,
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await ExtensionsCommand.ExecuteAsync(options);
        });

        return extCommand;
    }

    private static Command CreateFindCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var findCommand = new Command(FindCommand.Name, "Search for types across packages and libraries");
        findCommand.Aliases.Add("search");

        var patternArg = new Argument<string?>("pattern")
        {
            Description = "Type name or glob pattern. Comma-separated for multiple (e.g., \"Option*,Argument*,Command*\")",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Search in package(s) (name or name@version). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var platformOption = new Option<bool>("--platform") { Description = "Search all platform frameworks (runtime, aspnetcore, netstandard)" };
        var extensionsOption = new Option<bool>("--extensions") { Description = "Search curated Microsoft.Extensions.* packages" };
        var aspnetcoreOption = new Option<bool>("--aspnetcore") { Description = "Search curated Microsoft.AspNetCore.* packages" };
        var curatedOption = new Option<bool>("--curated") { Description = "Use default curated scope explicitly", Hidden = true };
        var projectOption = new Option<string[]>("--project")
        {
            Description = "Search project dependencies via project.assets.json. Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var binOption = new Option<string[]>("--bin")
        {
            Description = "Search all DLLs in output directory(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library or target framework by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete types" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        var packagePrefixOption = new Option<string?>("--package-prefix") { Description = "Search all packages matching a NuGet ID prefix (e.g., Azure.AI, AWSSDK)" };
        findCommand.Arguments.Add(patternArg);
        findCommand.Options.Add(packageOption);
        findCommand.Options.Add(assemblyOption);
        findCommand.Options.Add(platformOption);
        findCommand.Options.Add(extensionsOption);
        findCommand.Options.Add(aspnetcoreOption);
        findCommand.Options.Add(curatedOption);
        findCommand.Options.Add(projectOption);
        findCommand.Options.Add(binOption);
        findCommand.Options.Add(tfmOption);
        findCommand.Options.Add(allOption);
        findCommand.Options.Add(limitOption);
        findCommand.Options.Add(jsonOption);
        findCommand.Options.Add(compactOption);
        findCommand.Options.Add(oneLineOption);
        findCommand.Options.Add(noHeaderOption);
        findCommand.Options.Add(packagePrefixOption);
        findCommand.Options.Add(verboseOption);
        findCommand.Options.Add(verbosityOption);
        findCommand.Options.Add(tipsOption);
        findCommand.Options.Add(sourceOption);
        findCommand.Options.Add(addSourceOption);
        findCommand.Options.Add(nugetConfigOption);

        findCommand.SetAction(async (parseResult, ct) =>
        {
            var pattern = parseResult.GetValue(patternArg);

            if (string.IsNullOrEmpty(pattern))
            {
                new HelpAction().Invoke(parseResult);
                Console.Error.WriteLine();
                Console.Error.WriteLine("Tips:");
                Console.Error.WriteLine("  find Chat*                                # search default scope");
                Console.Error.WriteLine("  find Chat* --platform                     # platform libraries only");
                Console.Error.WriteLine("  find Chat* --extensions                   # Microsoft.Extensions packages");
                Console.Error.WriteLine("  find Chat* --aspnetcore                   # ASP.NET Core packages");
                Console.Error.WriteLine("  find Chat* --package Newtonsoft.Json       # specific package");
                Console.Error.WriteLine("  find Chat* --platform --extensions         # combine scopes");
                return 0;
            }

            var packages = parseResult.GetValue(packageOption) ?? [];
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];
            var projects = parseResult.GetValue(projectOption) ?? [];
            var binPaths = parseResult.GetValue(binOption) ?? [];
            var packagePrefix = parseResult.GetValue(packagePrefixOption);

            bool wantPlatform = parseResult.GetValue(platformOption);
            bool wantExtensions = parseResult.GetValue(extensionsOption);
            bool wantAspnetcore = parseResult.GetValue(aspnetcoreOption);
            bool wantCurated = parseResult.GetValue(curatedOption);
            bool hasExplicitScope = wantPlatform || wantExtensions || wantAspnetcore || wantCurated
                || packages.Length > 0 || assemblies.Length > 0 || projects.Length > 0 || binPaths.Length > 0
                || packagePrefix != null;

            // Resolve --package-prefix to package list
            if (packagePrefix != null)
            {
                var prefixPackages = await ResolvePrefixPackagesAsync(packagePrefix, parseResult.GetValue(verboseOption));
                packages = [..packages, ..prefixPackages];
            }

            // Resolve scope
            string[] frameworks = [];
            if (!hasExplicitScope)
            {
                // Default scope: all platform frameworks + curated packages
                frameworks = PlatformFrameworkNames;
                packages = [..packages, ..CuratedScopePackages];
            }
            else
            {
                if (wantPlatform) frameworks = PlatformFrameworkNames;
                if (wantExtensions) packages = [..packages, ..ExtensionsScopePackages];
                if (wantAspnetcore) packages = [..packages, ..AspNetCoreScopePackages];
                if (wantCurated)
                {
                    frameworks = [..frameworks, ..PlatformFrameworkNames];
                    packages = [..packages, ..CuratedScopePackages];
                }
            }

            var options = new FindOptions
            {
                Pattern = pattern!,
                Packages = packages,
                Assemblies = assemblies,
                PlatformAssemblies = [],
                PlatformFrameworks = frameworks,
                Projects = projects,
                BinPaths = binPaths,
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                Verbose = parseResult.GetValue(verboseOption),
                PackagePrefix = packagePrefix,
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            var exitCode = await FindCommand.ExecuteAsync(options);

            var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
            var tipLevel = options.IsRawOutput || verbosity == Verbosity.Quiet
                ? TipLevel.Quiet : ParseTipLevel(parseResult.GetValue(tipsOption), parseResult.GetResult(tipsOption) != null);

            if (exitCode == 0 && !options.IsRawOutput)
            {
                var pkg = options.Packages.Length > 0 ? options.Packages[0] : null;
                var sourceFlag = pkg != null ? $"--package {pkg}" : "--platform";

                Hints.WriteTips(tipLevel,
                    new(MemberCommand.Name, $"<TypeName> {sourceFlag}", "inspect type members"),
                    new(FindCommand.Name, $"{pattern} {sourceFlag} --oneline", "compact output"),
                    new(FindCommand.Name, $"{pattern} {sourceFlag} -v:d", "detailed results"),
                    new(LlmsTxtCommand.Name, "", "complete usage examples"));
            }

            return exitCode;
        });

        return findCommand;
    }

    private static Command CreateSamplesCommand(
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
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
        samplesCommand.Options.Add(verboseOption);
        samplesCommand.Options.Add(verbosityOption);
        samplesCommand.Options.Add(sourceOption);
        samplesCommand.Options.Add(addSourceOption);
        samplesCommand.Options.Add(nugetConfigOption);
        samplesCommand.Options.Add(tipsOption);
        samplesCommand.Options.Add(limitOption);

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

                if (LooksLikeVersionNumber(typeName))
                {
                    Console.Error.WriteLine($"Error: '{typeName}' looks like a version number. Use '{packagePath}@{typeName}' to specify a version.");
                    return 1;
                }

                // Route file paths (.dll → --library, .nupkg stays as package path)
                if (TryClassifyAsFilePath(packagePath, out var dllPath, out var nupkgPath))
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
                Verbose = parseResult.GetValue(verboseOption),
                ListOnly = parseResult.GetValue(listOption),
                PrintSample = parseResult.GetValue(printOption),
                FilePath = parseResult.GetValue(fileOption),
                Region = parseResult.GetValue(regionOption),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await SamplesCommand.ExecuteAsync(options);
        });

        return samplesCommand;
    }

    private static Command CreateImplementsCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var implCommand = new Command("implements", "Find types implementing an interface or extending a base class");

        var targetTypeArg = new Argument<string?>("type")
        {
            Description = "Target interface or base type (e.g., IDisposable, Stream, IList<T>)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Search in package(s) (name or name@version). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var platformOption = new Option<bool>("--platform") { Description = "Search all platform frameworks (runtime, aspnetcore, netstandard)" };
        var extensionsOption = new Option<bool>("--extensions") { Description = "Search curated Microsoft.Extensions.* packages" };
        var aspnetcoreOption = new Option<bool>("--aspnetcore") { Description = "Search curated Microsoft.AspNetCore.* packages" };
        var curatedOption = new Option<bool>("--curated") { Description = "Use default curated scope explicitly", Hidden = true };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden/obsolete types" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var packagePrefixOption = new Option<string?>("--package-prefix") { Description = "Search all packages matching a NuGet ID prefix (e.g., Azure.AI, AWSSDK)" };
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        implCommand.Arguments.Add(targetTypeArg);
        implCommand.Options.Add(packageOption);
        implCommand.Options.Add(assemblyOption);
        implCommand.Options.Add(platformOption);
        implCommand.Options.Add(extensionsOption);
        implCommand.Options.Add(aspnetcoreOption);
        implCommand.Options.Add(curatedOption);
        implCommand.Options.Add(tfmOption);
        implCommand.Options.Add(allOption);
        implCommand.Options.Add(limitOption);
        implCommand.Options.Add(jsonOption);
        implCommand.Options.Add(compactOption);
        implCommand.Options.Add(oneLineOption);
        implCommand.Options.Add(noHeaderOption);
        implCommand.Options.Add(packagePrefixOption);
        implCommand.Options.Add(verboseOption);
        implCommand.Options.Add(verbosityOption);
        implCommand.Options.Add(sourceOption);
        implCommand.Options.Add(addSourceOption);
        implCommand.Options.Add(nugetConfigOption);
        implCommand.Options.Add(tipsOption);

        implCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);

            if (string.IsNullOrEmpty(targetType))
            {
                new HelpAction().Invoke(parseResult);
                Console.Error.WriteLine();
                Console.Error.WriteLine("Tips:");
                Console.Error.WriteLine("  implements Stream                         # search default scope");
                Console.Error.WriteLine("  implements Stream --platform              # platform libraries only");
                Console.Error.WriteLine("  implements Stream --extensions             # Microsoft.Extensions packages");
                Console.Error.WriteLine("  implements Stream --aspnetcore             # ASP.NET Core packages");
                Console.Error.WriteLine("  implements Stream --package Foo            # specific package");
                Console.Error.WriteLine("  implements Stream --platform --extensions  # combine scopes");
                return 0;
            }

            var packages = parseResult.GetValue(packageOption) ?? [];
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];
            var packagePrefix = parseResult.GetValue(packagePrefixOption);

            bool wantPlatform = parseResult.GetValue(platformOption);
            bool wantExtensions = parseResult.GetValue(extensionsOption);
            bool wantAspnetcore = parseResult.GetValue(aspnetcoreOption);
            bool wantCurated = parseResult.GetValue(curatedOption);
            bool hasExplicitScope = wantPlatform || wantExtensions || wantAspnetcore || wantCurated
                || packages.Length > 0 || assemblies.Length > 0 || packagePrefix != null;

            // Resolve --package-prefix to package list
            if (packagePrefix != null)
            {
                var prefixPackages = await ResolvePrefixPackagesAsync(packagePrefix, parseResult.GetValue(verboseOption));
                packages = [..packages, ..prefixPackages];
            }

            // Resolve scope
            string[] frameworks = [];
            if (!hasExplicitScope)
            {
                // Default scope: all platform frameworks + curated packages
                frameworks = PlatformFrameworkNames;
                packages = [..packages, ..CuratedScopePackages];
            }
            else
            {
                if (wantPlatform) frameworks = PlatformFrameworkNames;
                if (wantExtensions) packages = [..packages, ..ExtensionsScopePackages];
                if (wantAspnetcore) packages = [..packages, ..AspNetCoreScopePackages];
                if (wantCurated)
                {
                    frameworks = [..frameworks, ..PlatformFrameworkNames];
                    packages = [..packages, ..CuratedScopePackages];
                }
            }

            var options = new ImplementsOptions
            {
                TargetType = targetType,
                Packages = packages,
                Assemblies = assemblies,
                PlatformAssemblies = [],
                PlatformFrameworks = frameworks,
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                Verbose = parseResult.GetValue(verboseOption),
                PackagePrefix = packagePrefix,
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await ImplementsCommand.ExecuteAsync(options);
        });

        return implCommand;
    }

    private static Command CreatePackageCommand(
        Option<bool> jsonOption,
        Option<bool> markoutOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var packageCommand = new Command(PackageCommand.Name, "Inspect a NuGet package");

        var packageNameArg = new Argument<string[]>("package")
        {
            Description = "NuGet package name or path to .nupkg file, optionally with version (e.g., System.Text.Json@9.0.0)",
            Arity = ArgumentArity.ZeroOrMore
        };

        var dependenciesOption = new Option<bool>("--dependencies") { Description = "Show transitive package dependency tree" };
        var layoutOption = new Option<bool>("--layout") { Description = "Show package file tree" };
        var filesOption = new Option<bool>("--files") { Description = "List files in the package (flat list, filterable with --tfm)" };
        var tfmsOption = new Option<bool>("--tfms") { Description = "List target frameworks in the package" };
        var libOption = new Option<bool>("--lib") { Description = "Scope to lib/ folder (use with --files or --layout)" };
        var toolsOption = new Option<bool>("--tools") { Description = "Scope to tools/ folder (use with --files or --layout)" };
        var versionsOption = new Option<bool>("--versions") { Description = "List available versions from nuget.org" };
        var prereleaseOption = new Option<bool>("--preview") { Description = "With --versions: include prerelease versions" };
        prereleaseOption.Aliases.Add("--prerelease");
        var readmeOption = new Option<bool>("--readme") { Description = "Show the README.md content from the package" };
        var outOption = new Option<string?>("--out") { Description = "Write output to file instead of stdout" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM (e.g., net8.0)" };
        var versionOption = new Option<string?>("--version") { Description = "Package version (or use alone to show latest)", Arity = ArgumentArity.ZeroOrOne };

        packageCommand.Arguments.Add(packageNameArg);
        packageCommand.Options.Add(dependenciesOption);
        packageCommand.Options.Add(layoutOption);
        packageCommand.Options.Add(filesOption);
        packageCommand.Options.Add(tfmsOption);
        packageCommand.Options.Add(libOption);
        packageCommand.Options.Add(toolsOption);
        packageCommand.Options.Add(versionsOption);
        packageCommand.Options.Add(prereleaseOption);
        packageCommand.Options.Add(readmeOption);
        packageCommand.Options.Add(tfmOption);
        packageCommand.Options.Add(versionOption);
        packageCommand.Options.Add(outOption);
        packageCommand.Options.Add(limitOption);
        packageCommand.Options.Add(jsonOption);
        packageCommand.Options.Add(markoutOption);
        packageCommand.Options.Add(verboseOption);
        packageCommand.Options.Add(verbosityOption);
        packageCommand.Options.Add(tipsOption);
        packageCommand.Options.Add(includeSectionsOption);
        packageCommand.Options.Add(excludeSectionsOption);
        packageCommand.Options.Add(sourceOption);
        packageCommand.Options.Add(addSourceOption);
        packageCommand.Options.Add(nugetConfigOption);

        // Search subcommand
        var searchCommand = CreatePackageSearchCommand(jsonOption, verboseOption, limitOption);
        packageCommand.Subcommands.Add(searchCommand);

        packageCommand.SetAction(async (parseResult, ct) =>
        {
            var packageArgs = parseResult.GetValue(packageNameArg) ?? [];
            var explicitVersion = parseResult.GetValue(versionOption);

            // Bare --version (no value): treat as --versions -n 1
            bool bareVersion = explicitVersion == null && parseResult.GetResult(versionOption) is { Implicit: false };

            var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));

            var options = new InspectionOptions
            {
                PackageArgs = packageArgs,
                ExplicitVersion = explicitVersion,
                ShowDependencies = parseResult.GetValue(dependenciesOption),
                Tfm = parseResult.GetValue(tfmOption),
                ListLayout = parseResult.GetValue(layoutOption),
                ListFiles = parseResult.GetValue(filesOption),
                ListTfms = parseResult.GetValue(tfmsOption),
                ScopeLib = parseResult.GetValue(libOption),
                ScopeTools = parseResult.GetValue(toolsOption),
                ListVersions = bareVersion || parseResult.GetValue(versionsOption),
                IncludePrerelease = parseResult.GetValue(prereleaseOption),
                ShowReadme = parseResult.GetValue(readmeOption),
                OutputPath = parseResult.GetValue(outOption),
                Limit = bareVersion ? 1 : parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = verbosity,
                IncludeSections = ParseIncludeSections(parseResult, includeSectionsOption),
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            var tipLevel = options.IsRawOutput || verbosity != Verbosity.Minimal || options.IncludeSections != null
                ? TipLevel.Quiet : ParseTipLevel(parseResult.GetValue(tipsOption), parseResult.GetResult(tipsOption) != null);
            options = options with { TipLevel = tipLevel };

            var exitCode = await PackageCommand.ExecuteAsync(options);

            if (exitCode == 0 && packageArgs.Length > 0 && !options.IsRawOutput)
            {
                var pkg = packageArgs[0];
                if (pkg.Contains('@')) pkg = pkg[..pkg.IndexOf('@')];

                List<Tip> tips = [];

                if (options.Verbosity < Verbosity.Detailed)
                    tips.Add(new(PackageCommand.Name, $"{pkg} -v:d", "detailed metadata"));

                tips.Add(new("library", pkg, "inspect library"));
                tips.Add(new(TypeCommand.Name, $"--package {pkg}", "discover types in package"));
                tips.Add(new(FindCommand.Name, $"<pattern> --package {pkg}", "search for types"));
                tips.Add(new(DiffCommand.Name, $"--package {pkg}@<prev>..<cur>", "diff versions"));
                tips.Add(new(PackageCommand.Name, $"{pkg} --readme", "view README"));
                tips.Add(new(PackageCommand.Name, $"{pkg} --files", "list package files"));
                tips.Add(new(PackageCommand.Name, $"{pkg} --layout", "show file tree"));
                tips.Add(new(LlmsTxtCommand.Name, "", "complete usage examples"));

                Hints.WriteTips(tipLevel, [.. tips]);
            }

            return exitCode;
        });

        return packageCommand;
    }

    private static Command CreatePackageSearchCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<int?> limitOption)
    {
        var searchCommand = new Command(PackageSearchCommand.Name, "Search NuGet for packages by keyword");

        var queryArg = new Argument<string?>("query")
        {
            Description = "Search query (keyword or package name prefix)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var takeOption = new Option<int>("--take")
        {
            Description = "Maximum number of results (default: 20)",
            DefaultValueFactory = _ => 20
        };
        var prereleaseOption = new Option<bool>("--preview") { Description = "Include prerelease versions" };
        prereleaseOption.Aliases.Add("--prerelease");
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };

        searchCommand.Arguments.Add(queryArg);
        searchCommand.Options.Add(takeOption);
        searchCommand.Options.Add(prereleaseOption);
        searchCommand.Options.Add(jsonOption);
        searchCommand.Options.Add(compactOption);
        searchCommand.Options.Add(verboseOption);
        searchCommand.Options.Add(limitOption);

        searchCommand.SetAction(async (parseResult, ct) =>
        {
            var query = parseResult.GetValue(queryArg);

            if (string.IsNullOrEmpty(query))
            {
                Console.Error.WriteLine("Usage: package search <query>");
                Console.Error.WriteLine();
                Console.Error.WriteLine("Examples:");
                Console.Error.WriteLine("  package search Azure.AI");
                Console.Error.WriteLine("  package search AWSSDK --take 50");
                Console.Error.WriteLine("  package search \"json serializer\" --json");
                return 0;
            }

            var options = new PackageSearchOptions
            {
                Query = query,
                Take = parseResult.GetValue(takeOption),
                Prerelease = parseResult.GetValue(prereleaseOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(verboseOption)
            };

            return await PackageSearchCommand.ExecuteAsync(options);
        });

        return searchCommand;
    }

    /// <summary>
    /// Hidden command that routes bare names: platform-preferred for System.*/Microsoft.*, NuGet fallback.
    /// </summary>
    private static Command CreateRouterCommand(
        Option<bool> jsonOption,
        Option<bool> markoutOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var routerCommand = new Command("router", "Auto-resolve package or platform library") { Hidden = true };

        var packageNameArg = new Argument<string[]>("package")
        {
            Description = "Package or platform library name",
            Arity = ArgumentArity.ZeroOrMore
        };

        routerCommand.Arguments.Add(packageNameArg);
        routerCommand.Options.Add(jsonOption);
        routerCommand.Options.Add(markoutOption);
        routerCommand.Options.Add(verboseOption);
        routerCommand.Options.Add(verbosityOption);
        routerCommand.Options.Add(tipsOption);
        routerCommand.Options.Add(limitOption);
        routerCommand.Options.Add(includeSectionsOption);
        routerCommand.Options.Add(excludeSectionsOption);
        routerCommand.Options.Add(sourceOption);
        routerCommand.Options.Add(addSourceOption);
        routerCommand.Options.Add(nugetConfigOption);

        // Version query options for the router
        var routerVersionOption = new Option<bool>("--version") { Description = "Show resolved version" };
        routerCommand.Options.Add(routerVersionOption);
        var routerLatestVersionOption = new Option<bool>("--latest-version") { Description = "Show latest version from nuget.org" };
        routerCommand.Options.Add(routerLatestVersionOption);
        var routerVersionsOption = new Option<bool>("--versions") { Description = "List available versions" };
        routerCommand.Options.Add(routerVersionsOption);

        routerCommand.SetAction(async (parseResult, ct) =>
        {
            var packageArgs = parseResult.GetValue(packageNameArg) ?? [];

            if (packageArgs.Length < 1)
            {
                new HelpAction().Invoke(parseResult);
                return 0;
            }

            var name = packageArgs[0];

            // Detect version number passed as a separate positional argument
            if (packageArgs.Length >= 2 && LooksLikeVersionNumber(packageArgs[1]))
            {
                Console.Error.WriteLine($"Error: '{packageArgs[1]}' looks like a version number. Use '{name}@{packageArgs[1]}' to specify a version.");
                return 1;
            }

            // Route file paths to the appropriate command
            if (TryClassifyAsFilePath(name, out var dllPath, out var nupkgPath))
            {
                if (dllPath != null)
                {
                    var assemblyOptions = new AssemblyOptions
                    {
                        AssemblyName = dllPath,
                        IncludeMetadata = true,
                        JsonOutput = parseResult.GetValue(jsonOption),
                        Verbose = parseResult.GetValue(verboseOption),
                        Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                        IncludeSections = ParseIncludeSections(parseResult, includeSectionsOption),
                        ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption))
                    };
                    return await AssemblyCommand.ExecuteAsync(assemblyOptions);
                }
                // .nupkg falls through to package command below
            }

            bool hasExplicitVersion = name.Contains('@');
            var bareName = hasExplicitVersion ? name[..name.IndexOf('@')] : name;
            var explicitVersion = hasExplicitVersion ? name[(name.IndexOf('@') + 1)..] : null;

            // @latest forces network resolution, bypassing cache-first
            bool forceLatest = string.Equals(explicitVersion, "latest", StringComparison.OrdinalIgnoreCase);
            if (forceLatest)
            {
                hasExplicitVersion = false;
                explicitVersion = null;
            }

            // Platform candidate: download ref packs, then resolve
            // Skip platform probing for version queries (NuGet package operations)
            bool showVersion = parseResult.GetValue(routerVersionOption);
            bool showLatestVersion = parseResult.GetValue(routerLatestVersionOption);
            bool showVersions = parseResult.GetValue(routerVersionsOption);
            bool isVersionQuery = showVersion || showLatestVersion || showVersions;
            if (!isVersionQuery && PlatformResolver.IsPlatformCandidate(bareName))
            {
                bool verbose = parseResult.GetValue(verboseOption);
                Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;
                var client = HttpClientFactory.Shared;

                // Probe at latest to check if the assembly is in a platform pack
                var requests = PlatformPackService.BuildPackRequests(bareName, explicitVersion: null);

                // Download with overlapped I/O; check each result as it lands
                await foreach (var pack in PlatformPackService.EnsurePacksAsync(requests, client, log, forceLatest: forceLatest))
                {
                    if (PlatformPackService.ContainsAssembly(pack.PackDir, bareName))
                    {
                        // Found it — remaining downloads continue for cache warming
                        string? frameworkSpec = null;
                        var (assemblyPath, framework, version, error) =
                            PlatformResolver.ResolveAssembly(bareName);

                        if (assemblyPath != null && hasExplicitVersion && framework != null)
                        {
                            // Now download the specific version of this pack
                            frameworkSpec = $"{framework}@{explicitVersion}";
                            if (PlatformResolver.FrameworkMappings.TryGetValue(framework, out var packName))
                                await PlatformPackService.EnsurePackAsync(packName, explicitVersion!, client, log);
                            (assemblyPath, framework, version, error) =
                                PlatformResolver.ResolveAssembly(bareName, frameworkSpec);
                        }

                        if (assemblyPath != null)
                        {
                            var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
                            var includeSections = ParseIncludeSections(parseResult, includeSectionsOption);
                            var assemblyOptions = new AssemblyOptions
                            {
                                PlatformAssembly = bareName,
                                PlatformFramework = frameworkSpec,
                                JsonOutput = parseResult.GetValue(jsonOption),
                                Verbose = parseResult.GetValue(verboseOption),
                                Verbosity = verbosity,
                                IncludeSections = includeSections,
                                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption))
                            };

                            var assemblyExitCode = await AssemblyCommand.ExecuteAsync(assemblyOptions);

                            if (assemblyExitCode == 0 && !assemblyOptions.JsonOutput)
                            {
                                var platformTipLevel = verbosity != Verbosity.Minimal || includeSections != null
                                    ? TipLevel.Quiet : ParseTipLevel(parseResult.GetValue(tipsOption), parseResult.GetResult(tipsOption) != null);

                                List<Tip> tips = [];

                                if (verbosity < Verbosity.Detailed)
                                    tips.Add(new($"{bareName}", "-v:d", "detailed metadata"));

                                tips.Add(new(PackageCommand.Name, bareName, "inspect as NuGet package"));
                                tips.Add(new(TypeCommand.Name, $"--platform {bareName}", "discover types"));
                                tips.Add(new(FindCommand.Name, $"<pattern> --platform {bareName}", "search for types"));
                                tips.Add(new(LlmsTxtCommand.Name, "", "complete usage examples"));

                                Hints.WriteTips(platformTipLevel, [.. tips]);
                            }

                            return assemblyExitCode;
                        }
                    }
                }
            }

            // Qualified type name: e.g., System.Text.Json.JsonSerializer → type JsonSerializer --platform System.Text.Json
            if (!isVersionQuery && PlatformResolver.IsPlatformCandidate(bareName)
                && PlatformResolver.TryParseQualifiedTypeName(bareName, out var qtAssembly, out var qtType))
            {
                var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
                var typeOptions = new ApiOptions
                {
                    TypeName = qtType,
                    PlatformAssembly = qtAssembly,
                    JsonOutput = parseResult.GetValue(jsonOption),
                    Verbose = parseResult.GetValue(verboseOption),
                    Verbosity = verbosity,
                    IncludeSections = ParseIncludeSections(parseResult, includeSectionsOption),
                    ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
                    TipLevel = ParseTipLevel(parseResult.GetValue(tipsOption), parseResult.GetResult(tipsOption) != null)
                };

                return await ApiCommand.ExecuteAsync(typeOptions);
            }

            // --version: print the resolved version and exit (no package inspection needed)
            if (showVersion)
            {
                if (!forceLatest)
                {
                    if (explicitVersion != null)
                    {
                        // 1. Check app cache and NuGet cache
                        if (NuGetCache.TryGetCachedPackage(bareName, explicitVersion) != null)
                        {
                            Console.WriteLine(explicitVersion);
                            return 0;
                        }

                        // 2. Check NuGet version API
                        var allVersions = await PackageExtractor.GetVersionsAsync(
                            HttpClientFactory.Shared, bareName, includePrerelease: true, limit: null,
                            log: null, sourceOptions: ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption));

                        if (allVersions != null && allVersions.Any(v => string.Equals(v, explicitVersion, StringComparison.OrdinalIgnoreCase)))
                        {
                            Console.WriteLine(explicitVersion);
                            return 0;
                        }

                        // 3. Differentiate bad package from bad version
                        if (allVersions == null || allVersions.Count == 0)
                            Console.Error.WriteLine($"Error: Package '{bareName}' not found.");
                        else
                            Console.Error.WriteLine($"Error: Version '{explicitVersion}' of package '{bareName}' not found. Use --versions to see available versions.");
                        return 1;
                    }
                    else
                    {
                        // Bare name: use newest cached version
                        var cachedVersion = NuGetCache.TryGetLatestCachedVersion(bareName);
                        if (cachedVersion != null)
                        {
                            Console.WriteLine(cachedVersion);
                            return 0;
                        }
                    }
                }
                // No cache hit, or @latest: fall through to --latest-version (version API query)
                showLatestVersion = true;
            }

            // Fall through to package command (NuGet resolution)
            bool useBareName = forceLatest || showLatestVersion;
            var options = new InspectionOptions
            {
                PackageArgs = useBareName ? [bareName] : packageArgs,
                ListVersions = showLatestVersion || showVersions,
                Limit = showLatestVersion ? 1 : parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                IncludeSections = ParseIncludeSections(parseResult, includeSectionsOption),
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption),
                ForceLatest = forceLatest || showLatestVersion
            };

            var tipLevel = options.IsRawOutput || options.Verbosity != Verbosity.Minimal || options.IncludeSections != null
                ? TipLevel.Quiet : ParseTipLevel(parseResult.GetValue(tipsOption), parseResult.GetResult(tipsOption) != null);
            options = options with { TipLevel = tipLevel };

            var exitCode = await PackageCommand.ExecuteAsync(options);

            if (exitCode == 0 && !options.IsRawOutput)
            {
                var pkg = bareName;

                List<Tip> tips = [];

                if (options.Verbosity < Verbosity.Detailed)
                    tips.Add(new(PackageCommand.Name, $"{pkg} -v:d", "detailed metadata"));

                tips.Add(new("library", pkg, "inspect library"));
                tips.Add(new(TypeCommand.Name, $"--package {pkg}", "discover types in package"));
                tips.Add(new(FindCommand.Name, $"<pattern> --package {pkg}", "search for types"));
                tips.Add(new(DiffCommand.Name, $"--package {pkg}@<prev>..<cur>", "diff versions"));
                tips.Add(new(PackageCommand.Name, $"{pkg} --readme", "view README"));
                tips.Add(new(PackageCommand.Name, $"{pkg} --files", "list package files"));
                tips.Add(new(PackageCommand.Name, $"{pkg} --layout", "show file tree"));
                tips.Add(new(LlmsTxtCommand.Name, "", "complete usage examples"));

                Hints.WriteTips(tipLevel, [.. tips]);
            }

            return exitCode;
        });

        return routerCommand;
    }

    private static Command CreateAssemblyCommand(
        Option<bool> jsonOption,
        Option<bool> markoutOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var assemblyCommand = new Command("library", "Inspect a .NET library file");

        var assemblyPathArg = new Argument<string?>("source")
        {
            Description = "Library file path, NuGet package name (e.g., System.Text.Json), or package@version",
            Arity = ArgumentArity.ZeroOrOne
        };
        assemblyPathArg.DefaultValueFactory = _ => null;

        var sourcelinkAuditOption = new Option<bool>("--source-link-audit") { Description = "Full provenance verification (parallel HTTP HEAD on all source files)" };
        var referencesOption = new Option<bool>("--references") { Description = "Show library references" };
        var dependenciesOption = new Option<bool>("--dependencies") { Description = "Show library dependencies as a tree" };
        var asmPlatformOption = new Option<string?>("--platform") { Description = "Inspect platform library (e.g., System.Text.Json)" };
        var asmPackageOption = new Option<string?>("--package") { Description = "Inspect library from NuGet package (e.g., System.Text.Json or System.Text.Json@9.0.4)" };
        var asmFrameworkOption = new Option<string?>("--framework") { Description = "Platform framework (runtime, aspnetcore). Use @version for specific version" };
        var asmTfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM (e.g., net8.0, or 'all' for every TFM)" };
        var extractResourcesOption = new Option<string?>("--extract-resources") { Description = "Extract embedded resources to a directory" };

        assemblyCommand.Arguments.Add(assemblyPathArg);
        assemblyCommand.Options.Add(sourcelinkAuditOption);
        assemblyCommand.Options.Add(referencesOption);
        assemblyCommand.Options.Add(dependenciesOption);
        assemblyCommand.Options.Add(asmPlatformOption);
        assemblyCommand.Options.Add(asmPackageOption);
        assemblyCommand.Options.Add(asmFrameworkOption);
        assemblyCommand.Options.Add(asmTfmOption);
        assemblyCommand.Options.Add(extractResourcesOption);
        assemblyCommand.Options.Add(jsonOption);
        assemblyCommand.Options.Add(markoutOption);
        assemblyCommand.Options.Add(verboseOption);
        assemblyCommand.Options.Add(verbosityOption);
        assemblyCommand.Options.Add(includeSectionsOption);
        assemblyCommand.Options.Add(excludeSectionsOption);
        assemblyCommand.Options.Add(sourceOption);
        assemblyCommand.Options.Add(addSourceOption);
        assemblyCommand.Options.Add(nugetConfigOption);
        assemblyCommand.Options.Add(tipsOption);
        assemblyCommand.Options.Add(limitOption);

        assemblyCommand.SetAction(async (parseResult, ct) =>
        {
            var source = parseResult.GetValue(assemblyPathArg);
            var explicitPackage = parseResult.GetValue(asmPackageOption);
            var explicitPlatform = parseResult.GetValue(asmPlatformOption);

            // Disambiguate positional arg: local file vs package name
            string? assemblyPath = null;
            string? packagePath = explicitPackage;
            string? platformAssembly = explicitPlatform;

            if (!string.IsNullOrEmpty(source) && string.IsNullOrEmpty(explicitPlatform) && string.IsNullOrEmpty(explicitPackage))
            {
                if (File.Exists(source))
                    assemblyPath = source;
                else if (!source.Contains('@') && PlatformResolver.IsPlatformCandidate(source))
                {
                    // Platform-preferred routing for System.*/Microsoft.* bare names
                    bool verbose = parseResult.GetValue(verboseOption);
                    Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;
                    var (asmPath, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(source, HttpClientFactory.Shared, log);
                    if (error == null && asmPath != null)
                        platformAssembly = source;
                    else
                        packagePath = source;
                }
                else
                    packagePath = source;
            }

            bool runSourcelinkAudit = parseResult.GetValue(sourcelinkAuditOption);

            bool showReferences = parseResult.GetValue(referencesOption);
            bool showDependencies = parseResult.GetValue(dependenciesOption);

            var options = new AssemblyOptions
            {
                AssemblyName = assemblyPath,
                IncludeMetadata = true,
                IncludeSourcelinkAudit = runSourcelinkAudit,
                IncludeReferences = showReferences,
                IncludeDependencies = showDependencies,
                PackagePath = packagePath,
                PlatformAssembly = platformAssembly,
                PlatformFramework = parseResult.GetValue(asmFrameworkOption),
                Tfm = parseResult.GetValue(asmTfmOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                IncludeSections = ParseIncludeSections(parseResult, includeSectionsOption),
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption),
                ExtractResources = parseResult.GetValue(extractResourcesOption)
            };

            return await AssemblyCommand.ExecuteAsync(options);
        });

        return assemblyCommand;
    }

    /// <summary>
    /// Creates a deprecated hidden api command that shows a deprecation message.
    /// </summary>
    private static Command CreateDeprecatedApiCommand()
    {
        var deprecatedApiCommand = new Command("api", "Deprecated: Use 'type' or 'member' instead") { Hidden = true };
        deprecatedApiCommand.TreatUnmatchedTokensAsErrors = false;
        deprecatedApiCommand.SetAction(_ =>
        {
            Console.Error.WriteLine("The 'api' command is deprecated. Please use:");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  type   - Discover types in a package/library (terse, no docs by default)");
            Console.Error.WriteLine("  member - Inspect type members (docs by default)");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  dotnet-inspect type --package System.Text.Json");
            Console.Error.WriteLine("  dotnet-inspect member JsonSerializer --package System.Text.Json");
            Console.Error.WriteLine("  dotnet-inspect member -m JsonSerializer.Deserialize --package System.Text.Json");
            return 1;
        });
        return deprecatedApiCommand;
    }

    /// <summary>
    /// Creates the type command for fast type discovery (terse, no docs by default).
    /// </summary>
    private static Command CreateTypeCommand(
        Option<bool> jsonOption,
        Option<bool> markoutOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<int?> limitOption,
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var typeCommand = new Command(TypeCommand.Name, "Discover types in a package or library (terse output)");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Package and type pattern. When no --package/--library/--platform is given, first arg is the package.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package") { Description = "Source: package (file, name, or name@version)" };
        var assemblyOption = new Option<string?>("--library") { Description = "Source: library path (local file, or relative within package)" };
        var platformOption = new Option<string?>("--platform") { Description = "Source: platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Source: platform framework (runtime, aspnetcore, netstandard). @version for specific" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Source: select by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete members" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter types by glob pattern (e.g., *Json*, Progress*)" };
        typeFilterOption.Aliases.Add("--type");
        var sourcelinkOnlyOption = new Option<bool>("--sourcelink-only") { Description = "Filter types to those with SourceLink resolution" };
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        var shapeOption = new Option<bool>("--shape") { Description = "Output type shape (inheritance, interfaces, members)" };
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter types with unsafe signatures (pointers)" };

        typeCommand.Arguments.Add(argsArg);
        typeCommand.Options.Add(packageOption);
        typeCommand.Options.Add(assemblyOption);
        typeCommand.Options.Add(platformOption);
        typeCommand.Options.Add(frameworkOption);
        typeCommand.Options.Add(tfmOption);
        typeCommand.Options.Add(allOption);
        typeCommand.Options.Add(typeFilterOption);
        typeCommand.Options.Add(limitOption);
        typeCommand.Options.Add(sourcelinkOnlyOption);
        typeCommand.Options.Add(jsonOption);
        typeCommand.Options.Add(compactOption);
        typeCommand.Options.Add(oneLineOption);
        typeCommand.Options.Add(noHeaderOption);
        typeCommand.Options.Add(shapeOption);
        typeCommand.Options.Add(unsafeOption);
        typeCommand.Options.Add(includeSectionsOption);
        typeCommand.Options.Add(excludeSectionsOption);
        typeCommand.Options.Add(markoutOption);
        typeCommand.Options.Add(verboseOption);
        typeCommand.Options.Add(verbosityOption);
        typeCommand.Options.Add(sourceOption);
        typeCommand.Options.Add(addSourceOption);
        typeCommand.Options.Add(nugetConfigOption);
        typeCommand.Options.Add(tipsOption);

        typeCommand.SetAction(async (parseResult, ct) =>
        {
            var args = parseResult.GetValue(argsArg) ?? [];
            var explicitPackage = parseResult.GetValue(packageOption);
            var explicitAssembly = parseResult.GetValue(assemblyOption);
            var explicitPlatform = parseResult.GetValue(platformOption);
            bool isLibrarySelector = explicitAssembly != null && explicitPackage == null
                && !explicitAssembly.Contains('/') && !explicitAssembly.Contains('\\')
                && explicitAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            bool hasExplicitSource = explicitPackage != null || (explicitAssembly != null && !isLibrarySelector) || explicitPlatform != null;

            if (args.Length == 0 && !hasExplicitSource)
            {
                if (parseResult.GetResult(includeSectionsOption) != null && parseResult.GetValue(includeSectionsOption) == null)
                {
                    var allTypeSections = SectionRegistry.ApiTypeSections;
                    SectionRegistry.ListSections(allTypeSections);
                    return 0;
                }

                new HelpAction().Invoke(parseResult);
                return 0;
            }

            string? packagePath = explicitPackage;
            string? typeName = null;
            string? apiFrameworkOverride = null;

            if (hasExplicitSource)
            {
                if (args.Length >= 1) typeName = args[0];
            }
            else
            {
                if (args.Length >= 1) packagePath = args[0];
                if (args.Length >= 2) typeName = args[1];

                if (LooksLikeVersionNumber(typeName))
                {
                    Console.Error.WriteLine($"Error: '{typeName}' looks like a version number. Use '{packagePath}@{typeName}' to specify a version.");
                    return 1;
                }

                if (TryClassifyAsFilePath(packagePath, out var dllPath, out var nupkgPath))
                {
                    if (dllPath != null) { explicitAssembly = dllPath; packagePath = null; }
                    else if (nupkgPath != null) { packagePath = nupkgPath; }
                }
                else if (packagePath != null && PlatformResolver.IsPlatformCandidate(
                    packagePath.Contains('@') ? packagePath[..packagePath.IndexOf('@')] : packagePath))
                {
                    var bareName = packagePath.Contains('@') ? packagePath[..packagePath.IndexOf('@')] : packagePath;
                    var explicitVersion = packagePath.Contains('@') ? packagePath[(packagePath.IndexOf('@') + 1)..] : null;

                    var client = HttpClientFactory.Shared;
                    bool verbose = parseResult.GetValue(verboseOption);
                    Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;
                    var requests = PlatformPackService.BuildPackRequests(bareName, explicitVersion: null);
                    await foreach (var pack in PlatformPackService.EnsurePacksAsync(requests, client, log))
                    {
                        if (PlatformPackService.ContainsAssembly(pack.PackDir, bareName))
                        {
                            string? frameworkSpec = null;
                            var (asmPath, framework, _, error) = PlatformResolver.ResolveAssembly(bareName);

                            if (asmPath != null && explicitVersion != null && framework != null)
                            {
                                frameworkSpec = $"{framework}@{explicitVersion}";
                                if (PlatformResolver.FrameworkMappings.TryGetValue(framework, out var packName))
                                    await PlatformPackService.EnsurePackAsync(packName, explicitVersion, client, log);
                                (asmPath, _, _, error) = PlatformResolver.ResolveAssembly(bareName, frameworkSpec);
                            }

                            if (error == null && asmPath != null)
                            {
                                explicitPlatform = bareName;
                                packagePath = null;
                                apiFrameworkOverride = frameworkSpec;
                                break;
                            }
                        }
                    }

                    // Assembly not found — try qualified type name (e.g., System.Text.Json.JsonSerializer)
                    if (explicitPlatform == null && typeName == null
                        && PlatformResolver.TryParseQualifiedTypeName(bareName, out var qtAsm, out var qtTyp))
                    {
                        explicitPlatform = qtAsm;
                        typeName = qtTyp;
                        packagePath = null;
                    }
                }
            }

            var options = new ApiOptions
            {
                TypeName = typeName,
                PackagePath = packagePath,
                AssemblyPath = explicitAssembly,
                PlatformAssembly = explicitPlatform,
                PlatformFramework = apiFrameworkOverride ?? parseResult.GetValue(frameworkOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                TypeFilter = parseResult.GetValue(typeFilterOption),
                MemberFilter = [],
                Limit = parseResult.GetValue(limitOption),
                ShowDocs = false,  // Type command: docs off by default
                DocsExplicitlySet = false,
                SourceLinkOnly = parseResult.GetValue(sourcelinkOnlyOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                ShapeOutput = parseResult.GetValue(shapeOption),
                UnsafeOnly = parseResult.GetValue(unsafeOption),
                IncludeSections = ParseIncludeSections(parseResult, includeSectionsOption),
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            options = options with
            {
                TipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet
                    ? TipLevel.Quiet : ParseTipLevel(parseResult.GetValue(tipsOption), parseResult.GetResult(tipsOption) != null)
            };

            return await TypeCommand.ExecuteAsync(options);
        });

        return typeCommand;
    }

    /// <summary>
    /// Creates the member command for deep member inspection (docs on by default).
    /// </summary>
    private static Command CreateMemberCommand(
        Option<bool> jsonOption,
        Option<bool> markoutOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<int?> limitOption,
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var memberCommand = new Command(MemberCommand.Name, "Inspect type members (docs on by default)");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Package, type name, and member filter. When no --package/--library/--platform is given, first arg is the package.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var packageOption = new Option<string?>("--package") { Description = "Source: package (file, name, or name@version)" };
        var assemblyOption = new Option<string?>("--library") { Description = "Source: library path (local file, or relative within package)" };
        var platformOption = new Option<string?>("--platform") { Description = "Source: platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Source: platform framework (runtime, aspnetcore, netstandard). @version for specific" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Source: select by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete members" };
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter members by name (supports globs, Type.Member dotted syntax)",
            AllowMultipleArgumentsPerToken = true
        };
        memberOption.Aliases.Add("--member");
        var ctorOption = new Option<bool>("--ctor") { Description = "Filter members to constructors (shorthand for -m .ctor)" };
        var docsOption = new Option<bool>("--docs") { Description = "Include XML doc comments (on by default, use --no-docs to suppress)" };
        var noDocsOption = new Option<bool>("--no-docs") { Description = "Suppress XML doc comments" };
        var useLocalDocsOption = new Option<bool>("--use-local-docs") { Description = "Include XML docs from local packs directory (offline)" };
        var samplesOption = new Option<bool>("--samples") { Description = "Include code samples from source" };
        var browsableUrlsOption = new Option<bool>("--browsable-urls") { Description = "Use /blob/ URLs for browser viewing (default: /raw/ for LLM consumption)" };
        var compactOption = new Option<bool>("--compact") { Description = "Output as minified JSON (use with --json)" };
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter members to unsafe signatures (pointers)" };
        var indexOption = new Option<int?>("--index") { Description = "Select member overload by index (or use Name:N shorthand)" };
        var paramsOption = new Option<string>("--params") { Description = "Select member overload by parameter types (comma-separated)" };
        var ofOption = new Option<string>("-of") { Description = "Select member overload by first parameter type" };
        var selectOption = new Option<bool>("--select") { Description = "Show member overload index (Name:N) column" };

        memberCommand.Arguments.Add(argsArg);
        memberCommand.Options.Add(packageOption);
        memberCommand.Options.Add(assemblyOption);
        memberCommand.Options.Add(platformOption);
        memberCommand.Options.Add(frameworkOption);
        memberCommand.Options.Add(tfmOption);
        memberCommand.Options.Add(allOption);
        memberCommand.Options.Add(memberOption);
        memberCommand.Options.Add(ctorOption);
        memberCommand.Options.Add(limitOption);
        memberCommand.Options.Add(docsOption);
        memberCommand.Options.Add(noDocsOption);
        memberCommand.Options.Add(useLocalDocsOption);
        memberCommand.Options.Add(samplesOption);
        memberCommand.Options.Add(browsableUrlsOption);
        memberCommand.Options.Add(jsonOption);
        memberCommand.Options.Add(compactOption);
        memberCommand.Options.Add(oneLineOption);
        memberCommand.Options.Add(noHeaderOption);
        memberCommand.Options.Add(unsafeOption);
        memberCommand.Options.Add(indexOption);
        memberCommand.Options.Add(paramsOption);
        memberCommand.Options.Add(ofOption);
        memberCommand.Options.Add(selectOption);
        memberCommand.Options.Add(includeSectionsOption);
        memberCommand.Options.Add(excludeSectionsOption);
        memberCommand.Options.Add(markoutOption);
        memberCommand.Options.Add(verboseOption);
        memberCommand.Options.Add(verbosityOption);
        memberCommand.Options.Add(sourceOption);
        memberCommand.Options.Add(addSourceOption);
        memberCommand.Options.Add(nugetConfigOption);
        memberCommand.Options.Add(tipsOption);

        memberCommand.SetAction(async (parseResult, ct) =>
        {
            var args = parseResult.GetValue(argsArg) ?? [];
            var explicitPackage = parseResult.GetValue(packageOption);
            var explicitAssembly = parseResult.GetValue(assemblyOption);
            var explicitPlatform = parseResult.GetValue(platformOption);
            bool isLibrarySelector = explicitAssembly != null && explicitPackage == null
                && !explicitAssembly.Contains('/') && !explicitAssembly.Contains('\\')
                && explicitAssembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
            bool hasExplicitSource = explicitPackage != null || (explicitAssembly != null && !isLibrarySelector) || explicitPlatform != null;

            if (args.Length == 0 && !hasExplicitSource)
            {
                if (parseResult.GetResult(includeSectionsOption) != null && parseResult.GetValue(includeSectionsOption) == null)
                {
                    var allMemberSections = SectionRegistry.ApiMemberSections;
                    SectionRegistry.ListSections(allMemberSections);
                    return 0;
                }

                new HelpAction().Invoke(parseResult);
                return 0;
            }

            string? packagePath = explicitPackage;
            string? typeName = null;
            List<string> positionalMembers = [];
            string? apiFrameworkOverride = null;

            if (hasExplicitSource)
            {
                if (args.Length >= 1) typeName = args[0];
                if (args.Length >= 2) positionalMembers.AddRange(args[1..]);
            }
            else
            {
                if (args.Length >= 1) packagePath = args[0];
                if (args.Length >= 2) typeName = args[1];
                if (args.Length >= 3) positionalMembers.AddRange(args[2..]);

                if (LooksLikeVersionNumber(typeName))
                {
                    Console.Error.WriteLine($"Error: '{typeName}' looks like a version number. Use '{packagePath}@{typeName}' to specify a version.");
                    return 1;
                }

                if (TryClassifyAsFilePath(packagePath, out var dllPath, out var nupkgPath))
                {
                    if (dllPath != null) { explicitAssembly = dllPath; packagePath = null; }
                    else if (nupkgPath != null) { packagePath = nupkgPath; }
                }
                else if (packagePath != null && PlatformResolver.IsPlatformCandidate(
                    packagePath.Contains('@') ? packagePath[..packagePath.IndexOf('@')] : packagePath))
                {
                    var bareName = packagePath.Contains('@') ? packagePath[..packagePath.IndexOf('@')] : packagePath;
                    var explicitVersion = packagePath.Contains('@') ? packagePath[(packagePath.IndexOf('@') + 1)..] : null;

                    var client = HttpClientFactory.Shared;
                    bool verbose = parseResult.GetValue(verboseOption);
                    Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;
                    var requests = PlatformPackService.BuildPackRequests(bareName, explicitVersion: null);
                    await foreach (var pack in PlatformPackService.EnsurePacksAsync(requests, client, log))
                    {
                        if (PlatformPackService.ContainsAssembly(pack.PackDir, bareName))
                        {
                            string? frameworkSpec = null;
                            var (asmPath, framework, _, error) = PlatformResolver.ResolveAssembly(bareName);

                            if (asmPath != null && explicitVersion != null && framework != null)
                            {
                                frameworkSpec = $"{framework}@{explicitVersion}";
                                if (PlatformResolver.FrameworkMappings.TryGetValue(framework, out var packName))
                                    await PlatformPackService.EnsurePackAsync(packName, explicitVersion, client, log);
                                (asmPath, _, _, error) = PlatformResolver.ResolveAssembly(bareName, frameworkSpec);
                            }

                            if (error == null && asmPath != null)
                            {
                                explicitPlatform = bareName;
                                packagePath = null;
                                apiFrameworkOverride = frameworkSpec;
                                break;
                            }
                        }
                    }
                }
            }

            var badOption = positionalMembers.FirstOrDefault(m => m.StartsWith("--"));
            if (badOption != null)
            {
                Console.Error.WriteLine($"Error: Unrecognized option '{badOption}'.");
                return 1;
            }

            var members = parseResult.GetValue(memberOption) ?? [];
            var allMembers = members.Concat(positionalMembers).ToArray();
            var ctorOnly = parseResult.GetValue(ctorOption);

            // Parse dotted syntax (Type.Member) from -m option
            string? dottedTypeFilter = null;
            for (int i = 0; i < allMembers.Length; i++)
            {
                var memberArg = allMembers[i];
                var dotIdx = memberArg.LastIndexOf('.');
                // Only split if: has dot, not a glob pattern, and first segment isn't empty
                if (dotIdx > 0 && !memberArg.Contains('*') && !memberArg.Contains('?'))
                {
                    dottedTypeFilter = memberArg[..dotIdx];
                    allMembers[i] = memberArg[(dotIdx + 1)..];
                    // Use the extracted type name if no explicit type was provided
                    if (string.IsNullOrEmpty(typeName))
                        typeName = dottedTypeFilter;
                    break;
                }
            }

            // Parse Name:N shorthand
            int? shorthandIndex = null;
            bool hasExplicitIndex = false;
            for (int i = 0; i < allMembers.Length; i++)
            {
                var colonIdx = allMembers[i].LastIndexOf(':');
                if (colonIdx > 0 && int.TryParse(allMembers[i][(colonIdx + 1)..], out var idx))
                {
                    allMembers[i] = allMembers[i][..colonIdx];
                    shorthandIndex = idx;
                    hasExplicitIndex = true;
                }
            }

            if (!hasExplicitIndex && allMembers.Length == 1)
                shorthandIndex = 1;

            HashSet<string> memberFilter = [];
            if (ctorOnly)
            {
                memberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ctor" };
            }
            else if (allMembers.Length > 0)
            {
                memberFilter = new HashSet<string>(allMembers, StringComparer.OrdinalIgnoreCase);
            }

            // Determine docs behavior: --no-docs suppresses, --docs enables, default is on
            bool showDocs = !parseResult.GetValue(noDocsOption);
            bool docsExplicitlySet = parseResult.GetResult(docsOption) is { Implicit: false }
                || parseResult.GetResult(noDocsOption) is { Implicit: false }
                || parseResult.GetResult(useLocalDocsOption) is { Implicit: false };

            // If --docs is explicitly set, honor it (overrides --no-docs precedence)
            if (parseResult.GetResult(docsOption) is { Implicit: false })
                showDocs = true;

            var options = new ApiOptions
            {
                TypeName = typeName,
                PackagePath = packagePath,
                AssemblyPath = explicitAssembly,
                PlatformAssembly = explicitPlatform,
                PlatformFramework = apiFrameworkOverride ?? parseResult.GetValue(frameworkOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                MemberFilter = memberFilter,
                Limit = parseResult.GetValue(limitOption),
                ShowDocs = showDocs || parseResult.GetValue(useLocalDocsOption),
                DocsExplicitlySet = docsExplicitlySet,
                UseLocalDocs = parseResult.GetValue(useLocalDocsOption),
                ShowSamples = parseResult.GetValue(samplesOption),
                BrowsableUrls = parseResult.GetValue(browsableUrlsOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                UnsafeOnly = parseResult.GetValue(unsafeOption),
                CtorOnly = ctorOnly,
                OverloadIndex = parseResult.GetValue(indexOption) ?? shorthandIndex,
                ParamTypes = parseResult.GetValue(paramsOption)?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                FirstParamType = parseResult.GetValue(ofOption),
                ShowSelect = parseResult.GetValue(selectOption),
                IncludeSections = ParseIncludeSections(parseResult, includeSectionsOption),
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            options = options with
            {
                TipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet
                    ? TipLevel.Quiet : ParseTipLevel(parseResult.GetValue(tipsOption), parseResult.GetResult(tipsOption) != null)
            };

            return await MemberCommand.ExecuteAsync(options);
        });

        return memberCommand;
    }

    // Parse helpers delegated to OptionParsers
    public static Verbosity ParseVerbosity(string? value) => OptionParsers.ParseVerbosity(value);
    public static TipLevel ParseTipLevel(string? value, bool optionPresent) => OptionParsers.ParseTipLevel(value, optionPresent);
    public static HashSet<string>? ParseSectionList(string? value) => OptionParsers.ParseSectionList(value);
    public static HashSet<string>? ParseIncludeSections(ParseResult parseResult, Option<string?> option)
        => OptionParsers.ParseIncludeSections(parseResult, option);
    public static NuGetSourceOptions ParseNuGetSourceOptions(
        ParseResult parseResult, Option<string[]> sourceOption,
        Option<string[]> addSourceOption, Option<string?> nugetConfigOption)
        => OptionParsers.ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption);

    /// <summary>
    /// Classifies a positional argument by file extension.
    /// Returns true if the positional was a file path (.dll or .nupkg) and sets the appropriate out parameter.
    /// </summary>
    internal static bool TryClassifyAsFilePath(string? positional, out string? libraryPath, out string? packagePath)
    {
        libraryPath = null;
        packagePath = null;

        if (string.IsNullOrEmpty(positional))
            return false;

        if (positional.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            libraryPath = positional;
            return true;
        }

        if (positional.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase))
        {
            packagePath = positional;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the value looks like a version number (e.g. "2.0.0", "8.0.0-preview.1").
    /// Used to detect when a user passes a version as a positional argument instead of using the @ syntax.
    /// </summary>
    internal static bool LooksLikeVersionNumber(string? value)
    {
        if (string.IsNullOrEmpty(value) || !char.IsAsciiDigit(value[0]))
            return false;

        // Must contain at least one dot followed by a digit (e.g. "2.0")
        for (int i = 1; i < value.Length - 1; i++)
        {
            if (value[i] == '.' && char.IsAsciiDigit(value[i + 1]))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Resolves a package ID prefix to a list of matching package names via NuGet search.
    /// </summary>
    private static async Task<string[]> ResolvePrefixPackagesAsync(string prefix, bool verbose)
    {
        Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;
        var client = HttpClientFactory.Shared;

        log?.Invoke($"Resolving packages with prefix: {prefix}");
        var results = await NuGetSearchService.SearchByPrefixAsync(client, prefix, log: log);

        if (results.Count == 0)
        {
            Console.Error.WriteLine($"Warning: No packages found matching prefix \"{prefix}\"");
            return [];
        }

        log?.Invoke($"Found {results.Count} package(s) matching prefix \"{prefix}\"");
        var packageNames = results.Select(r => r.PackageId).ToArray();

        foreach (var pkg in packageNames)
            log?.Invoke($"  {pkg}");

        return packageNames;
    }
}
