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
        "package", "library", "api", "type", "member", "diff", "find", "search", "samples", "list", "ls", "llmstxt", "skill", "extensions", "implements", "depends", "cache", "cli", "demo", "perf", "perf-test", "help", "--help", "-h", "-?", "--version", "--flavor"
    };

    // Scope constants delegated to ScopeConstants for backward compatibility
    internal static string[] PlatformFrameworkNames => ScopeConstants.PlatformFrameworks;
    internal static string[] ExtensionsScopePackages => ScopeConstants.ExtensionsPackages;
    internal static string[] AspNetCoreScopePackages => ScopeConstants.AspNetCorePackages;
    internal static string[] CuratedScopePackages => ScopeConstants.CuratedPackages;

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

        // Set HeadLines for explicit -n N (so -n 6 behaves like -6)
        if (HeadLines == null)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-n" && int.TryParse(args[i + 1], out var n))
                {
                    HeadLines = n;
                    break;
                }
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

        // Shared options container (defined once, reused across commands)
        var opts = new SharedOptions();

        // Root-level display options (distinct instances so they appear in root help)
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
        var typeCommand = CreateTypeCommand(opts);
        rootCommand.Subcommands.Add(typeCommand);

        // Member command (member inspection, docs by default)
        var memberCommand = CreateMemberCommand(opts);
        rootCommand.Subcommands.Add(memberCommand);

        // Assembly command
        var assemblyCommand = CreateAssemblyCommand(opts);
        rootCommand.Subcommands.Add(assemblyCommand);

        // Cache command
        var cacheCommand = CreateCacheCommand(opts);
        rootCommand.Subcommands.Add(cacheCommand);

        // Demo command
        var demoCommand = CreateDemoCommand(rootCommand, opts);
        rootCommand.Subcommands.Add(demoCommand);

        // Diff command
        var diffCommand = CreateDiffCommand(opts);
        rootCommand.Subcommands.Add(diffCommand);

        // Depends command
        var dependsCommand = CreateDependsCommand(opts);
        rootCommand.Subcommands.Add(dependsCommand);

        // Extensions command
        var extensionsCommand = CreateExtensionsCommand(opts);
        rootCommand.Subcommands.Add(extensionsCommand);

        // Find command
        var findCommand = CreateFindCommand(opts);
        rootCommand.Subcommands.Add(findCommand);

        // Implements command
        var implementsCommand = CreateImplementsCommand(opts);
        rootCommand.Subcommands.Add(implementsCommand);

        // Package command
        var packageCommand = CreatePackageCommand(opts);
        rootCommand.Subcommands.Add(packageCommand);

        // Router command (hidden, implicit default for bare names)
        var routerCommand = CreateRouterCommand(opts);
        rootCommand.Subcommands.Add(routerCommand);

        // Samples command
        var samplesCommand = CreateSamplesCommand(opts);
        rootCommand.Subcommands.Add(samplesCommand);

        // CLI command (meta command)
        var schemaCommand = new Command("cli", "Show CLI command structure as API listing");
        var schemaCommandArg = new Argument<string?>("command") { Description = "Command name to show (omit for all)", Arity = ArgumentArity.ZeroOrOne };
        schemaCommand.Arguments.Add(schemaCommandArg);
        schemaCommand.Options.Add(opts.Verbosity);
        schemaCommand.Options.Add(opts.Limit);
        schemaCommand.SetAction((parseResult) =>
        {
            var commandFilter = parseResult.GetValue(schemaCommandArg);
            var verbosity = ParseVerbosity(parseResult.GetValue(opts.Verbosity));
            return CliSchemaCommand.Execute(rootCommand, commandFilter, verbosity);
        });
        rootCommand.Subcommands.Add(schemaCommand);

        // LLMs.txt command (meta command, listed last)
        var llmsTxtCommand = new Command("llmstxt", "Show usage examples (run this first)");
        llmsTxtCommand.Options.Add(opts.Limit);
        llmsTxtCommand.SetAction((parseResult) => LlmsTxtCommand.Execute());
        rootCommand.Subcommands.Add(llmsTxtCommand);

        var skillCommand = new Command("skill", "Show skill definition");
        skillCommand.Options.Add(opts.Limit);
        skillCommand.SetAction((parseResult) => SkillCommand.Execute());
        rootCommand.Subcommands.Add(skillCommand);

        // Perf command (hidden, for profiling various code paths)
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
            var tipLevel = verbosity == Verbosity.Quiet || HeadLines != null
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

    private static Command CreateCacheCommand(SharedOptions opts)
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

            var verbosity = ParseVerbosity(parseResult.GetValue(opts.Verbosity));
            var options = new CacheOptions(
                Clean: clean,
                Verbose: parseResult.GetValue(opts.Verbose) || verbosity >= Verbosity.Detailed);

            return await CacheCommand.ExecuteAsync(options);
        });

        return cacheCommand;
    }

    private static Command CreateDemoCommand(RootCommand rootCommand, SharedOptions opts)
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
            new HelpAction().Invoke(parseResult);
            var tips = DemoCommand.Demos.Select((d, i) =>
                new Tip("demo", $"{i + 1}", d.Title)).ToArray();
            Hints.WriteTips(TipLevel.Minimal, tips, randomize: true);
            return 0;
        });

        return demoCommand;
    }

    private static Command CreateDiffCommand(SharedOptions opts)
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
        opts.AddOutputOptionsTo(diffCommand);
        opts.AddNuGetOptionsTo(diffCommand);

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
                Verbose = parseResult.GetValue(opts.Verbose),
                TypeFilter = typeFilter,
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                NameOnly = parseResult.GetValue(nameOnlyOption),
                Breaking = parseResult.GetValue(breakingOption),
                Additive = parseResult.GetValue(additiveOption),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            var exitCode = await DiffCommand.ExecuteAsync(options);

            var verbosity = opts.ParseVerbosity(parseResult);
            var tipLevel = options.IsRawOutput || verbosity == Verbosity.Quiet || HeadLines != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);

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

    private static Command CreateDependsCommand(SharedOptions opts)
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
        dependsCommand.Options.Add(opts.Json);
        dependsCommand.Options.Add(compactOption);
        opts.AddOutputOptionsTo(dependsCommand);
        opts.AddNuGetOptionsTo(dependsCommand);

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
                    JsonOutput = parseResult.GetValue(opts.Json),
                    CompactJson = parseResult.GetValue(compactOption),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
                };

                if (assemblies.Length == 1 && packages.Length == 0)
                    return await DependsCommand.ExecuteLibraryDependsAsync(commonOptions with { LibraryName = assemblies[0] });

                if (packages.Length == 1 && assemblies.Length == 0)
                    return await DependsCommand.ExecutePackageDependsAsync(commonOptions with { PackageName = packages[0] });

                return ShowHelpWithTips(parseResult,
                    "depends IFloatingPointIeee754 --platform   # type hierarchy",
                    "depends --library Microsoft.Extensions.AI   # assembly references",
                    "depends --package System.Text.Json          # NuGet dependencies");
            }

            var scopeFlags = new ScopeResolver.ScopeFlags(
                Platform: parseResult.GetValue(platformOption),
                Extensions: parseResult.GetValue(extensionsOption),
                AspNetCore: parseResult.GetValue(aspnetcoreOption),
                Curated: parseResult.GetValue(curatedOption));
            var scope = ScopeResolver.Resolve(scopeFlags, packages, assemblies);

            var options = new DependsOptions
            {
                TargetType = targetType,
                Packages = scope.Packages,
                Assemblies = assemblies,
                PlatformAssemblies = [],
                PlatformFrameworks = scope.Frameworks,
                Tfm = parseResult.GetValue(tfmOption),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            return await DependsCommand.ExecuteTypeDependsAsync(options);
        });

        return dependsCommand;
    }

    private static Command CreateExtensionsCommand(SharedOptions opts)
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
        var typeFilterOption = new Option<string?>("-t") { Description = "Limit type count (-t 5) or filter by glob (-t *Json*)" };
        typeFilterOption.Aliases.Add("--type");

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
        extCommand.Options.Add(typeFilterOption);
        extCommand.Options.Add(opts.Json);
        extCommand.Options.Add(compactOption);
        extCommand.Options.Add(packagePrefixOption);
        opts.AddOutputOptionsTo(extCommand);
        opts.AddNuGetOptionsTo(extCommand);

        extCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);

            if (string.IsNullOrEmpty(targetType))
            {
                return ShowHelpWithTips(parseResult,
                    "extensions HttpClient                     # search default scope",
                    "extensions HttpClient --platform          # platform libraries only",
                    "extensions HttpClient --extensions         # Microsoft.Extensions packages",
                    "extensions HttpClient --aspnetcore         # ASP.NET Core packages",
                    "extensions HttpClient --package Foo        # specific package",
                    "extensions HttpClient --platform --extensions  # combine scopes");
            }

            var packagePrefix = parseResult.GetValue(packagePrefixOption);
            var packages = await MergeWithPrefixPackagesAsync(
                parseResult.GetValue(packageOption) ?? [], packagePrefix, parseResult.GetValue(opts.Verbose));
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];

            var scopeFlags = new ScopeResolver.ScopeFlags(
                Platform: parseResult.GetValue(platformOption),
                Extensions: parseResult.GetValue(extensionsOption),
                AspNetCore: parseResult.GetValue(aspnetcoreOption),
                Curated: parseResult.GetValue(curatedOption));
            var scope = ScopeResolver.Resolve(scopeFlags, packages, assemblies, packagePrefix);

            var options = new ExtensionsOptions
            {
                TargetType = targetType,
                Packages = scope.Packages,
                Assemblies = assemblies,
                PlatformAssemblies = [],
                PlatformFrameworks = scope.Frameworks,
                Reachable = parseResult.GetValue(reachableOption),
                Depth = parseResult.GetValue(depthOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = ParseTypeLimit(parseResult.GetValue(typeFilterOption)),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                PackagePrefix = packagePrefix,
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            return await ExtensionsCommand.ExecuteAsync(options);
        });

        return extCommand;
    }

    private static Command CreateFindCommand(SharedOptions opts)
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
        var typeFilterOption = new Option<string?>("-t") { Description = "Limit type count (-t 5) or filter by glob (-t *Json*)" };
        typeFilterOption.Aliases.Add("--type");

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
        findCommand.Options.Add(typeFilterOption);
        findCommand.Options.Add(opts.Json);
        findCommand.Options.Add(compactOption);
        findCommand.Options.Add(oneLineOption);
        findCommand.Options.Add(noHeaderOption);
        findCommand.Options.Add(packagePrefixOption);
        opts.AddOutputOptionsTo(findCommand);
        opts.AddNuGetOptionsTo(findCommand);

        findCommand.SetAction(async (parseResult, ct) =>
        {
            var pattern = parseResult.GetValue(patternArg);

            if (string.IsNullOrEmpty(pattern))
            {
                return ShowHelpWithTips(parseResult,
                    "find Chat*                                # search default scope",
                    "find Chat* --platform                     # platform libraries only",
                    "find Chat* --extensions                   # Microsoft.Extensions packages",
                    "find Chat* --aspnetcore                   # ASP.NET Core packages",
                    "find Chat* --package Newtonsoft.Json       # specific package",
                    "find Chat* --platform --extensions         # combine scopes");
            }

            var packagePrefix = parseResult.GetValue(packagePrefixOption);
            var packages = await MergeWithPrefixPackagesAsync(
                parseResult.GetValue(packageOption) ?? [], packagePrefix, parseResult.GetValue(opts.Verbose));
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];
            var projects = parseResult.GetValue(projectOption) ?? [];
            var binPaths = parseResult.GetValue(binOption) ?? [];

            var scopeFlags = new ScopeResolver.ScopeFlags(
                Platform: parseResult.GetValue(platformOption),
                Extensions: parseResult.GetValue(extensionsOption),
                AspNetCore: parseResult.GetValue(aspnetcoreOption),
                Curated: parseResult.GetValue(curatedOption));
            var scope = ScopeResolver.Resolve(scopeFlags, packages, assemblies, packagePrefix,
                hasOtherScopeIndicators: projects.Length > 0 || binPaths.Length > 0);

            var options = new FindOptions
            {
                Pattern = pattern!,
                Packages = scope.Packages,
                Assemblies = assemblies,
                PlatformAssemblies = [],
                PlatformFrameworks = scope.Frameworks,
                Projects = projects,
                BinPaths = binPaths,
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = ParseTypeLimit(parseResult.GetValue(typeFilterOption)),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                PackagePrefix = packagePrefix,
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            var exitCode = await FindCommand.ExecuteAsync(options);

            var verbosity = opts.ParseVerbosity(parseResult);
            var tipLevel = options.IsRawOutput || verbosity == Verbosity.Quiet || HeadLines != null || options.Limit != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);

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

    private static Command CreateSamplesCommand(SharedOptions opts)
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
                Verbose = parseResult.GetValue(opts.Verbose),
                ListOnly = parseResult.GetValue(listOption),
                PrintSample = parseResult.GetValue(printOption),
                FilePath = parseResult.GetValue(fileOption),
                Region = parseResult.GetValue(regionOption),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            return await SamplesCommand.ExecuteAsync(options);
        });

        return samplesCommand;
    }

    private static Command CreateImplementsCommand(SharedOptions opts)
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
        var typeFilterOption = new Option<string?>("-t") { Description = "Limit type count (-t 5) or filter by glob (-t *Json*)" };
        typeFilterOption.Aliases.Add("--type");

        implCommand.Arguments.Add(targetTypeArg);
        implCommand.Options.Add(packageOption);
        implCommand.Options.Add(assemblyOption);
        implCommand.Options.Add(platformOption);
        implCommand.Options.Add(extensionsOption);
        implCommand.Options.Add(aspnetcoreOption);
        implCommand.Options.Add(curatedOption);
        implCommand.Options.Add(tfmOption);
        implCommand.Options.Add(allOption);
        implCommand.Options.Add(typeFilterOption);
        implCommand.Options.Add(opts.Json);
        implCommand.Options.Add(compactOption);
        implCommand.Options.Add(oneLineOption);
        implCommand.Options.Add(noHeaderOption);
        implCommand.Options.Add(packagePrefixOption);
        opts.AddOutputOptionsTo(implCommand);
        opts.AddNuGetOptionsTo(implCommand);

        implCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);

            if (string.IsNullOrEmpty(targetType))
            {
                return ShowHelpWithTips(parseResult,
                    "implements Stream                         # search default scope",
                    "implements Stream --platform              # platform libraries only",
                    "implements Stream --extensions             # Microsoft.Extensions packages",
                    "implements Stream --aspnetcore             # ASP.NET Core packages",
                    "implements Stream --package Foo            # specific package",
                    "implements Stream --platform --extensions  # combine scopes");
            }

            var packagePrefix = parseResult.GetValue(packagePrefixOption);
            var packages = await MergeWithPrefixPackagesAsync(
                parseResult.GetValue(packageOption) ?? [], packagePrefix, parseResult.GetValue(opts.Verbose));
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];

            var scopeFlags = new ScopeResolver.ScopeFlags(
                Platform: parseResult.GetValue(platformOption),
                Extensions: parseResult.GetValue(extensionsOption),
                AspNetCore: parseResult.GetValue(aspnetcoreOption),
                Curated: parseResult.GetValue(curatedOption));
            var scope = ScopeResolver.Resolve(scopeFlags, packages, assemblies, packagePrefix);

            var options = new ImplementsOptions
            {
                TargetType = targetType,
                Packages = scope.Packages,
                Assemblies = assemblies,
                PlatformAssemblies = [],
                PlatformFrameworks = scope.Frameworks,
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = ParseTypeLimit(parseResult.GetValue(typeFilterOption)),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                PackagePrefix = packagePrefix,
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            return await ImplementsCommand.ExecuteAsync(options);
        });

        return implCommand;
    }

    private static Command CreatePackageCommand(SharedOptions opts)
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
        var versionsOption = new Option<int?>("--versions") { Description = "List available versions (optionally limit count)", Arity = ArgumentArity.ZeroOrOne };
        versionsOption.DefaultValueFactory = _ => null;
        var prereleaseOption = new Option<bool>("--preview") { Description = "With --versions: include prerelease versions" };
        prereleaseOption.Aliases.Add("--prerelease");
        var readmeOption = new Option<bool>("--readme") { Description = "Show the README.md content from the package" };
        var outOption = new Option<string?>("--out") { Description = "Write output to file instead of stdout" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM (e.g., net8.0)" };
        var versionOption = new Option<string?>("--version") { Description = "Package version (or use alone to show latest)", Arity = ArgumentArity.ZeroOrOne };
        var oneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var noHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };

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
        packageCommand.Options.Add(oneLineOption);
        packageCommand.Options.Add(noHeaderOption);
        packageCommand.Options.Add(opts.Json);
        packageCommand.Options.Add(opts.Markout);
        opts.AddOutputOptionsTo(packageCommand);
        opts.AddSectionOptionsTo(packageCommand);
        opts.AddNuGetOptionsTo(packageCommand);

        // Search subcommand
        var searchCommand = CreatePackageSearchCommand(opts);
        packageCommand.Subcommands.Add(searchCommand);

        packageCommand.SetAction(async (parseResult, ct) =>
        {
            var packageArgs = parseResult.GetValue(packageNameArg) ?? [];
            var explicitVersion = parseResult.GetValue(versionOption);

            // Bare --version (no value): treat as --versions 1
            bool bareVersion = explicitVersion == null && parseResult.GetResult(versionOption) is { Implicit: false };

            var versionsValue = parseResult.GetValue(versionsOption);
            bool showVersions = bareVersion || parseResult.GetResult(versionsOption) is { Implicit: false };

            var verbosity = opts.ParseVerbosity(parseResult);

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
                ListVersions = showVersions,
                IncludePrerelease = parseResult.GetValue(prereleaseOption),
                ShowReadme = parseResult.GetValue(readmeOption),
                OutputPath = parseResult.GetValue(outOption),
                Limit = bareVersion ? 1 : versionsValue,
                JsonOutput = parseResult.GetValue(opts.Json),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = verbosity,
                IncludeSections = opts.ParseIncludeSections(parseResult),
                ExcludeSections = opts.ParseExcludeSections(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            var tipLevel = options.IsRawOutput || verbosity != Verbosity.Minimal || options.IncludeSections != null || HeadLines != null || options.Limit != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
            options = options with { TipLevel = tipLevel };

            var exitCode = await PackageCommand.ExecuteAsync(options);

            if (exitCode == 0 && packageArgs.Length > 0 && !options.IsRawOutput)
            {
                var pkg = packageArgs[0];
                if (pkg.Contains('@')) pkg = pkg[..pkg.IndexOf('@')];
                WritePackageTips(pkg, tipLevel, options.Verbosity);
            }

            return exitCode;
        });

        return packageCommand;
    }

    private static Command CreatePackageSearchCommand(SharedOptions opts)
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
        searchCommand.Options.Add(opts.Json);
        searchCommand.Options.Add(compactOption);
        searchCommand.Options.Add(opts.Verbose);
        searchCommand.Options.Add(opts.Limit);

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
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(opts.Verbose)
            };

            return await PackageSearchCommand.ExecuteAsync(options);
        });

        return searchCommand;
    }

    /// <summary>
    /// Hidden command that routes bare names: platform-preferred for System.*/Microsoft.*, NuGet fallback.
    /// </summary>
    private static Command CreateRouterCommand(SharedOptions opts)
    {
        var routerCommand = new Command("router", "Auto-resolve package or platform library") { Hidden = true };

        var packageNameArg = new Argument<string[]>("package")
        {
            Description = "Package or platform library name",
            Arity = ArgumentArity.ZeroOrMore
        };

        routerCommand.Arguments.Add(packageNameArg);
        opts.AddAllOptionsTo(routerCommand);

        var routerOneLineOption = new Option<bool>("--oneline") { Description = "One result per line, columnar output" };
        var routerNoHeaderOption = new Option<bool>("--no-header") { Description = "Suppress column headers (use with --oneline)" };
        routerCommand.Options.Add(routerOneLineOption);
        routerCommand.Options.Add(routerNoHeaderOption);

        // Version query options for the router
        var routerVersionOption = new Option<bool>("--version") { Description = "Show resolved version" };
        routerCommand.Options.Add(routerVersionOption);
        var routerLatestVersionOption = new Option<bool>("--latest-version") { Description = "Show latest version from nuget.org" };
        routerCommand.Options.Add(routerLatestVersionOption);
        var routerVersionsOption = new Option<int?>("--versions") { Description = "List available versions (optionally limit count)", Arity = ArgumentArity.ZeroOrOne };
        routerVersionsOption.DefaultValueFactory = _ => null;
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
                        JsonOutput = parseResult.GetValue(opts.Json),
                        Verbose = parseResult.GetValue(opts.Verbose),
                        Verbosity = opts.ParseVerbosity(parseResult),
                        IncludeSections = opts.ParseIncludeSections(parseResult),
                        ExcludeSections = opts.ParseExcludeSections(parseResult)
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
            var routerVersionsValue = parseResult.GetValue(routerVersionsOption);
            bool showVersions = parseResult.GetResult(routerVersionsOption) is { Implicit: false };
            bool isVersionQuery = showVersion || showLatestVersion || showVersions;
            if (!isVersionQuery && PlatformResolver.IsPlatformCandidate(bareName))
            {
                bool verbose = parseResult.GetValue(opts.Verbose);
                Action<string>? log = verbose ? msg => Console.Error.WriteLine(msg) : null;
                var client = HttpClientFactory.Shared;

                // Build framework spec if explicit version given (e.g., System.Text.Json@9.0.0 -> runtime@9.0.0)
                string? platformFrameworkSpec = null;
                if (hasExplicitVersion)
                {
                    var (_, discoveredFramework, _, _) = PlatformResolver.ResolveAssembly(bareName);
                    if (discoveredFramework != null)
                        platformFrameworkSpec = $"{discoveredFramework}@{explicitVersion}";
                }

                // Resolve assembly (local-first, then network if needed)
                var (resolvedPath, _, _, resolvedError) = await PlatformResolver.ResolveAssemblyAsync(
                    bareName, client, log, platformFrameworkSpec);

                if (resolvedPath != null && resolvedError == null)
                {
                    var verbosity = opts.ParseVerbosity(parseResult);
                    var includeSections = opts.ParseIncludeSections(parseResult);
                    var assemblyOptions = new AssemblyOptions
                    {
                        PlatformAssembly = bareName,
                        PlatformFramework = platformFrameworkSpec,
                        JsonOutput = parseResult.GetValue(opts.Json),
                        Verbose = parseResult.GetValue(opts.Verbose),
                        Verbosity = verbosity,
                        IncludeSections = includeSections,
                        ExcludeSections = opts.ParseExcludeSections(parseResult)
                    };

                    var assemblyExitCode = await AssemblyCommand.ExecuteAsync(assemblyOptions);

                    if (assemblyExitCode == 0 && !assemblyOptions.JsonOutput)
                    {
                        var platformTipLevel = verbosity != Verbosity.Minimal || includeSections != null || HeadLines != null
                            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
                        WritePlatformTips(bareName, platformTipLevel, verbosity);
                    }

                    return assemblyExitCode;
                }
            }

            // Qualified type name: e.g., System.Text.Json.JsonSerializer -> type JsonSerializer --platform System.Text.Json
            if (!isVersionQuery && PlatformResolver.IsPlatformCandidate(bareName)
                && PlatformResolver.TryParseQualifiedTypeName(bareName, out var qtAssembly, out var qtType))
            {
                var verbosity = opts.ParseVerbosity(parseResult);
                var typeOptions = new ApiOptions
                {
                    TypeName = qtType,
                    PlatformAssembly = qtAssembly,
                    JsonOutput = parseResult.GetValue(opts.Json),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    Verbosity = verbosity,
                    IncludeSections = opts.ParseIncludeSections(parseResult),
                    ExcludeSections = opts.ParseExcludeSections(parseResult),
                    TipLevel = HeadLines != null ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
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
                            log: null, sourceOptions: opts.ParseNuGetSourceOptions(parseResult));

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
                Limit = showLatestVersion ? 1 : routerVersionsValue,
                JsonOutput = parseResult.GetValue(opts.Json),
                OneLine = parseResult.GetValue(routerOneLineOption),
                NoHeader = parseResult.GetValue(routerNoHeaderOption),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                IncludeSections = opts.ParseIncludeSections(parseResult),
                ExcludeSections = opts.ParseExcludeSections(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
                ForceLatest = forceLatest || showLatestVersion
            };

            var tipLevel = options.IsRawOutput || options.Verbosity != Verbosity.Minimal || options.IncludeSections != null || HeadLines != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
            options = options with { TipLevel = tipLevel };

            var exitCode = await PackageCommand.ExecuteAsync(options);

            if (exitCode == 0 && !options.IsRawOutput)
                WritePackageTips(bareName, tipLevel, options.Verbosity);

            return exitCode;
        });

        return routerCommand;
    }

    private static Command CreateAssemblyCommand(SharedOptions opts)
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
        opts.AddAllOptionsTo(assemblyCommand);

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
                    bool verbose = parseResult.GetValue(opts.Verbose);
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
                JsonOutput = parseResult.GetValue(opts.Json),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                IncludeSections = opts.ParseIncludeSections(parseResult),
                ExcludeSections = opts.ParseExcludeSections(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
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
    private static Command CreateTypeCommand(SharedOptions opts)
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
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter members by name or limit count (-m 5)",
            AllowMultipleArgumentsPerToken = true
        };
        memberOption.Aliases.Add("--member");

        typeCommand.Arguments.Add(argsArg);
        typeCommand.Options.Add(packageOption);
        typeCommand.Options.Add(assemblyOption);
        typeCommand.Options.Add(platformOption);
        typeCommand.Options.Add(frameworkOption);
        typeCommand.Options.Add(tfmOption);
        typeCommand.Options.Add(allOption);
        typeCommand.Options.Add(typeFilterOption);
        typeCommand.Options.Add(opts.Limit);
        typeCommand.Options.Add(sourcelinkOnlyOption);
        typeCommand.Options.Add(opts.Json);
        typeCommand.Options.Add(compactOption);
        typeCommand.Options.Add(oneLineOption);
        typeCommand.Options.Add(noHeaderOption);
        typeCommand.Options.Add(shapeOption);
        typeCommand.Options.Add(unsafeOption);
        typeCommand.Options.Add(memberOption);
        opts.AddSectionOptionsTo(typeCommand);
        typeCommand.Options.Add(opts.Markout);
        opts.AddOutputOptionsTo(typeCommand);
        opts.AddNuGetOptionsTo(typeCommand);

        typeCommand.SetAction(async (parseResult, ct) =>
        {
            var args = parseResult.GetValue(argsArg) ?? [];
            var explicitPackage = parseResult.GetValue(packageOption);
            var explicitAssembly = parseResult.GetValue(assemblyOption);
            var explicitPlatform = parseResult.GetValue(platformOption);
            bool isLibrarySelector = SourceResolver.IsLibrarySelector(explicitAssembly, explicitPackage);
            bool hasExplicitSource = SourceResolver.HasExplicitSource(explicitPackage, explicitAssembly, explicitPlatform, isLibrarySelector);

            if (args.Length == 0 && !hasExplicitSource)
            {
                if (parseResult.GetResult(opts.IncludeSections) != null && parseResult.GetValue(opts.IncludeSections) == null)
                {
                    var allTypeSections = SectionRegistry.ApiTypeSections;
                    SectionRegistry.ListSections(allTypeSections);
                    return 0;
                }

                new HelpAction().Invoke(parseResult);
                return 0;
            }

            var source = await SourceResolver.ResolveAsync(
                args, explicitPackage, explicitAssembly, explicitPlatform,
                parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: true);

            if (source.VersionError)
            {
                Console.Error.WriteLine(source.VersionErrorMessage);
                return 1;
            }

            var packagePath = source.PackagePath;
            var typeName = source.TypeName;
            var apiFrameworkOverride = source.FrameworkOverride;

            var typeFilterValue = parseResult.GetValue(typeFilterOption);
            int? typeLimit = null;
            string? typeFilter = typeFilterValue;
            if (typeFilterValue != null && int.TryParse(typeFilterValue, out var tNum))
            {
                typeLimit = tNum;
                typeFilter = null;
            }

            // Parse -m: number = member limit, glob = member filter
            var memberValues = parseResult.GetValue(memberOption) ?? [];
            HashSet<string> memberFilter = [];
            int? memberLimit = null;
            if (memberValues.Length == 1 && int.TryParse(memberValues[0], out var mNum))
            {
                memberLimit = mNum;
            }
            else if (memberValues.Length > 0)
            {
                memberFilter = new HashSet<string>(memberValues, StringComparer.OrdinalIgnoreCase);
            }

            var options = new ApiOptions
            {
                TypeName = typeName,
                PackagePath = packagePath,
                AssemblyPath = source.AssemblyPath,
                PlatformAssembly = source.PlatformAssembly,
                PlatformFramework = apiFrameworkOverride ?? parseResult.GetValue(frameworkOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                TypeFilter = typeFilter,
                MemberFilter = memberFilter,
                Limit = memberLimit ?? typeLimit,
                ShowDocs = false,  // Type command: docs off by default
                DocsExplicitlySet = false,
                SourceLinkOnly = parseResult.GetValue(sourcelinkOnlyOption),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                ShapeOutput = parseResult.GetValue(shapeOption),
                UnsafeOnly = parseResult.GetValue(unsafeOption),
                IncludeSections = opts.ParseIncludeSections(parseResult),
                ExcludeSections = opts.ParseExcludeSections(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            options = options with
            {
                TipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet || HeadLines != null || typeLimit != null
                    ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
            };

            return await TypeCommand.ExecuteAsync(options);
        });

        return typeCommand;
    }

    /// <summary>
    /// Creates the member command for deep member inspection (docs on by default).
    /// </summary>
    private static Command CreateMemberCommand(SharedOptions opts)
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
        memberCommand.Options.Add(opts.Limit);
        memberCommand.Options.Add(docsOption);
        memberCommand.Options.Add(noDocsOption);
        memberCommand.Options.Add(useLocalDocsOption);
        memberCommand.Options.Add(samplesOption);
        memberCommand.Options.Add(browsableUrlsOption);
        memberCommand.Options.Add(opts.Json);
        memberCommand.Options.Add(compactOption);
        memberCommand.Options.Add(oneLineOption);
        memberCommand.Options.Add(noHeaderOption);
        memberCommand.Options.Add(unsafeOption);
        memberCommand.Options.Add(indexOption);
        memberCommand.Options.Add(paramsOption);
        memberCommand.Options.Add(ofOption);
        memberCommand.Options.Add(selectOption);
        opts.AddSectionOptionsTo(memberCommand);
        memberCommand.Options.Add(opts.Markout);
        opts.AddOutputOptionsTo(memberCommand);
        opts.AddNuGetOptionsTo(memberCommand);

        memberCommand.SetAction(async (parseResult, ct) =>
        {
            var args = parseResult.GetValue(argsArg) ?? [];
            var explicitPackage = parseResult.GetValue(packageOption);
            var explicitAssembly = parseResult.GetValue(assemblyOption);
            var explicitPlatform = parseResult.GetValue(platformOption);
            bool isLibrarySelector = SourceResolver.IsLibrarySelector(explicitAssembly, explicitPackage);
            bool hasExplicitSource = SourceResolver.HasExplicitSource(explicitPackage, explicitAssembly, explicitPlatform, isLibrarySelector);

            if (args.Length == 0 && !hasExplicitSource)
            {
                if (parseResult.GetResult(opts.IncludeSections) != null && parseResult.GetValue(opts.IncludeSections) == null)
                {
                    var allMemberSections = SectionRegistry.ApiMemberSections;
                    SectionRegistry.ListSections(allMemberSections);
                    return 0;
                }

                new HelpAction().Invoke(parseResult);
                return 0;
            }

            // Member command needs to extract positional members separately
            List<string> positionalMembers = [];
            if (hasExplicitSource && args.Length >= 2)
                positionalMembers.AddRange(args[1..]);
            else if (!hasExplicitSource && args.Length >= 3)
                positionalMembers.AddRange(args[2..]);

            var source = await SourceResolver.ResolveAsync(
                args, explicitPackage, explicitAssembly, explicitPlatform,
                parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: false);

            if (source.VersionError)
            {
                Console.Error.WriteLine(source.VersionErrorMessage);
                return 1;
            }

            var packagePath = source.PackagePath;
            var typeName = source.TypeName;
            var apiFrameworkOverride = source.FrameworkOverride;

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

            // Parse Name:N shorthand for explicit overload selection
            int? shorthandIndex = null;
            for (int i = 0; i < allMembers.Length; i++)
            {
                var colonIdx = allMembers[i].LastIndexOf(':');
                if (colonIdx > 0 && int.TryParse(allMembers[i][(colonIdx + 1)..], out var idx))
                {
                    allMembers[i] = allMembers[i][..colonIdx];
                    shorthandIndex = idx;
                }
            }
            // Note: We don't auto-select overload 1 when a single member is filtered.
            // This allows seeing all overloads when e.g. `-m GetValue` matches multiple.
            // Use explicit Name:1 syntax to select a specific overload.

            HashSet<string> memberFilter = [];
            int? memberLimit = null;
            if (ctorOnly)
            {
                memberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ctor" };
            }
            else if (allMembers.Length == 1 && int.TryParse(allMembers[0], out var mNum))
            {
                memberLimit = mNum;
                shorthandIndex = null;
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
                AssemblyPath = source.AssemblyPath,
                PlatformAssembly = source.PlatformAssembly,
                PlatformFramework = apiFrameworkOverride ?? parseResult.GetValue(frameworkOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                MemberFilter = memberFilter,
                Limit = memberLimit,
                ShowDocs = showDocs || parseResult.GetValue(useLocalDocsOption),
                DocsExplicitlySet = docsExplicitlySet,
                UseLocalDocs = parseResult.GetValue(useLocalDocsOption),
                ShowSamples = parseResult.GetValue(samplesOption),
                BrowsableUrls = parseResult.GetValue(browsableUrlsOption),
                JsonOutput = parseResult.GetValue(opts.Json),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = parseResult.GetValue(oneLineOption),
                NoHeader = parseResult.GetValue(noHeaderOption),
                UnsafeOnly = parseResult.GetValue(unsafeOption),
                CtorOnly = ctorOnly,
                OverloadIndex = parseResult.GetValue(indexOption) ?? shorthandIndex,
                ParamTypes = parseResult.GetValue(paramsOption)?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                FirstParamType = parseResult.GetValue(ofOption),
                ShowSelect = parseResult.GetValue(selectOption),
                IncludeSections = opts.ParseIncludeSections(parseResult),
                ExcludeSections = opts.ParseExcludeSections(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
            };

            options = options with
            {
                TipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet || HeadLines != null || memberLimit != null
                    ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
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
    /// Parses a -t value as either a numeric limit or null (glob patterns are handled separately).
    /// </summary>
    internal static int? ParseTypeLimit(string? value)
        => value != null && int.TryParse(value, out var n) ? n : null;

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
    /// Shows help action followed by command-specific tips.
    /// </summary>
    private static int ShowHelpWithTips(ParseResult parseResult, params string[] tips)
    {
        new HelpAction().Invoke(parseResult);
        Console.Error.WriteLine();
        Console.Error.WriteLine("Tips:");
        foreach (var tip in tips)
            Console.Error.WriteLine($"  {tip}");
        return 0;
    }

    /// <summary>
    /// Writes package-related tips after successful package inspection.
    /// </summary>
    private static void WritePackageTips(string packageName, TipLevel tipLevel, Verbosity verbosity)
    {
        List<Tip> tips = [];

        if (verbosity < Verbosity.Detailed)
            tips.Add(new(PackageCommand.Name, $"{packageName} -v:d", "detailed metadata"));

        tips.Add(new("library", packageName, "inspect library"));
        tips.Add(new(TypeCommand.Name, $"--package {packageName}", "discover types in package"));
        tips.Add(new(FindCommand.Name, $"<pattern> --package {packageName}", "search for types"));
        tips.Add(new(DiffCommand.Name, $"--package {packageName}@<prev>..<cur>", "diff versions"));
        tips.Add(new(PackageCommand.Name, $"{packageName} --readme", "view README"));
        tips.Add(new(PackageCommand.Name, $"{packageName} --files", "list package files"));
        tips.Add(new(PackageCommand.Name, $"{packageName} --layout", "show file tree"));
        tips.Add(new(LlmsTxtCommand.Name, "", "complete usage examples"));

        Hints.WriteTips(tipLevel, [.. tips]);
    }

    /// <summary>
    /// Writes platform library-related tips after successful assembly inspection.
    /// </summary>
    private static void WritePlatformTips(string assemblyName, TipLevel tipLevel, Verbosity verbosity)
    {
        List<Tip> tips = [];

        if (verbosity < Verbosity.Detailed)
            tips.Add(new(assemblyName, "-v:d", "detailed metadata"));

        tips.Add(new(PackageCommand.Name, assemblyName, "inspect as NuGet package"));
        tips.Add(new(TypeCommand.Name, $"--platform {assemblyName}", "discover types"));
        tips.Add(new(FindCommand.Name, $"<pattern> --platform {assemblyName}", "search for types"));
        tips.Add(new(LlmsTxtCommand.Name, "", "complete usage examples"));

        Hints.WriteTips(tipLevel, [.. tips]);
    }

    /// <summary>
    /// Resolves a package ID prefix and merges with existing packages.
    /// </summary>
    private static async Task<string[]> MergeWithPrefixPackagesAsync(string[] packages, string? prefix, bool verbose)
    {
        if (prefix == null)
            return packages;

        var prefixPackages = await ResolvePrefixPackagesAsync(prefix, verbose);
        return [.. packages, .. prefixPackages];
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
