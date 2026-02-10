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
    /// Known commands for implicit package command detection.
    /// </summary>
    public static readonly HashSet<string> KnownCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "package", "library", "api", "diff", "find", "search", "samples", "platform", "list", "ls", "llmstxt", "skill", "extensions", "implements", "cache", "cli", "help", "--help", "-h", "-?", "--version"
    };

    /// <summary>
    /// Pre-processes args to handle implicit package command.
    /// </summary>
    public static string[] PreprocessArgs(string[] args)
    {
        if (args.Length > 0 && !args[0].StartsWith('-') && !KnownCommands.Contains(args[0]))
        {
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
        var verbosityOption = new Option<string?>("-v") { Description = "Verbosity level: q(uiet), m(inimal), n(ormal), d(etailed)" };
        var includeSectionsOption = new Option<string?>("-s") { Description = "Include only these sections (comma-separated, supports wildcards e.g. -s:Extension*).\nUse -s alone to list available sections.", Arity = ArgumentArity.ZeroOrOne };
        var excludeSectionsOption = new Option<string?>("-x") { Description = "Exclude these sections by name (comma-separated, e.g., -x:Methods)" };
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
            Description = "Additional NuGet source URL (can repeat)",
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

        // API command
        var apiCommand = CreateApiCommand(jsonOption, markoutOption, verboseOption, verbosityOption, tipsOption, limitOption, includeSectionsOption, excludeSectionsOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(apiCommand);

        // Assembly command
        var assemblyCommand = CreateAssemblyCommand(jsonOption, markoutOption, verboseOption, verbosityOption, tipsOption, includeSectionsOption, excludeSectionsOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(assemblyCommand);

        // Cache command
        var cacheCommand = CreateCacheCommand(verboseOption, verbosityOption, tipsOption);
        rootCommand.Subcommands.Add(cacheCommand);

        // Diff command
        var diffCommand = CreateDiffCommand(verboseOption, verbosityOption, tipsOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(diffCommand);

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

        // Platform command
        var platformCommand = CreatePlatformCommand(jsonOption, verboseOption, verbosityOption, tipsOption, limitOption, includeSectionsOption, excludeSectionsOption);
        rootCommand.Subcommands.Add(platformCommand);

        // Router command (hidden, implicit default for bare names)
        var routerCommand = CreateRouterCommand(jsonOption, markoutOption, verboseOption, verbosityOption, tipsOption, includeSectionsOption, excludeSectionsOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(routerCommand);

        // Samples command
        var samplesCommand = CreateSamplesCommand(verboseOption, verbosityOption, tipsOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(samplesCommand);

        // CLI command (meta command)
        var schemaCommand = new Command("cli", "Show CLI command structure as API listing");
        var schemaCommandArg = new Argument<string?>("command") { Description = "Command name to show (omit for all)", Arity = ArgumentArity.ZeroOrOne };
        schemaCommand.Arguments.Add(schemaCommandArg);
        schemaCommand.Options.Add(verbosityOption);
        schemaCommand.SetAction((parseResult) =>
        {
            var commandFilter = parseResult.GetValue(schemaCommandArg);
            var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
            return CliSchemaCommand.Execute(rootCommand, commandFilter, verbosity);
        });
        rootCommand.Subcommands.Add(schemaCommand);

        // LLMs.txt command (meta command, listed last)
        var llmsTxtCommand = new Command("llmstxt", "Show usage examples (run this first)");
        llmsTxtCommand.SetAction((parseResult) => LlmsTxtCommand.Execute());
        rootCommand.Subcommands.Add(llmsTxtCommand);

        var skillCommand = new Command("skill", "Show skill definition");
        skillCommand.SetAction((parseResult) => SkillCommand.Execute());
        rootCommand.Subcommands.Add(skillCommand);

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
                new Tip(ApiCommand.Name, "--package <package>", "view public API surface"),
                new Tip(FindCommand.Name, "<pattern> --package <package>", "search package types"),
                new Tip(FindCommand.Name, "<pattern> --platform <library>", "search platform types"));
        });

        return rootCommand;
    }

    private static Command CreateCacheCommand(Option<bool> verboseOption, Option<string?> verbosityOption, Option<string?> tipsOption)
    {
        var cacheCommand = new Command("cache", "Manage the dotnet-inspect cache");

        var cleanOption = new Option<bool>("--clean") { Description = "Clear the cache" };

        cacheCommand.Options.Add(cleanOption);
        cacheCommand.Options.Add(verboseOption);
        cacheCommand.Options.Add(verbosityOption);
        cacheCommand.Options.Add(tipsOption);

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

    private static Command CreateDiffCommand(
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
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
        var statOption = new Option<bool>("--stat") { Description = "Show only statistics per type (no member details)" };
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
        diffCommand.Options.Add(statOption);
        diffCommand.Options.Add(nameOnlyOption);
        diffCommand.Options.Add(breakingOption);
        diffCommand.Options.Add(additiveOption);
        diffCommand.Options.Add(verboseOption);
        diffCommand.Options.Add(verbosityOption);
        diffCommand.Options.Add(tipsOption);
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
                Stat = parseResult.GetValue(statOption),
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
                        if (!options.Stat && !options.NameOnly)
                            tips.Add(new(ApiCommand.Name, $"<TypeName> {sourceFlag} {pkgName}@{toVersion} --shape", "view current type shape"));
                        if (!options.Stat)
                            tips.Add(new(DiffCommand.Name, $"{sourceFlag} {versionRange} --stat", "summary statistics"));
                    }
                }

                tips.Add(new(LlmsTxtCommand.Name, "", "complete usage examples"));
                Hints.WriteTips(tipLevel, [.. tips]);
            }

            return exitCode;
        });

        return diffCommand;
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

        var targetTypeArg = new Argument<string>("type")
        {
            Description = "Target type to find extensions for (e.g., HttpClient, IEnumerable<T>)"
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
        var platformOption = new Option<string[]>("--platform")
        {
            Description = "Search in platform library(s) (e.g., System.Text.Json). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var frameworkOption = new Option<string[]>("--framework")
        {
            Description = "Search all libraries in framework(s) (runtime, aspnetcore, netstandard). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
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

        extCommand.Arguments.Add(targetTypeArg);
        extCommand.Options.Add(packageOption);
        extCommand.Options.Add(assemblyOption);
        extCommand.Options.Add(platformOption);
        extCommand.Options.Add(frameworkOption);
        extCommand.Options.Add(reachableOption);
        extCommand.Options.Add(depthOption);
        extCommand.Options.Add(tfmOption);
        extCommand.Options.Add(allOption);
        extCommand.Options.Add(limitOption);
        extCommand.Options.Add(jsonOption);
        extCommand.Options.Add(compactOption);
        extCommand.Options.Add(verboseOption);
        extCommand.Options.Add(verbosityOption);
        extCommand.Options.Add(sourceOption);
        extCommand.Options.Add(addSourceOption);
        extCommand.Options.Add(nugetConfigOption);
        extCommand.Options.Add(tipsOption);

        extCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);
            var options = new ExtensionsOptions
            {
                TargetType = targetType!,
                Packages = parseResult.GetValue(packageOption) ?? [],
                Assemblies = parseResult.GetValue(assemblyOption) ?? [],
                PlatformAssemblies = parseResult.GetValue(platformOption) ?? [],
                PlatformFrameworks = parseResult.GetValue(frameworkOption) ?? [],
                Reachable = parseResult.GetValue(reachableOption),
                Depth = parseResult.GetValue(depthOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
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

        var patternArg = new Argument<string>("pattern")
        {
            Description = "Type name or glob pattern. Comma-separated for multiple (e.g., \"Option*,Argument*,Command*\")"
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
        var platformOption = new Option<string[]>("--platform")
        {
            Description = "Search in platform library(s) (e.g., System.Text.Json). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var frameworkOption = new Option<string[]>("--framework")
        {
            Description = "Search all libraries in framework(s) (runtime, aspnetcore, netstandard). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
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
        var oneLineOption = new Option<bool>("--oneline") { Description = "Space-separated type names on one line" };
        var groupedOption = new Option<bool>("--grouped") { Description = "Group results by pattern (use with --oneline)" };
        var terseOption = new Option<bool>("--terse") { Description = "Compact output (alias for --oneline --grouped)" };
        var nameOnlyOption = new Option<bool>("--name-only") { Description = "Show only type names, one per line" };

        findCommand.Arguments.Add(patternArg);
        findCommand.Options.Add(packageOption);
        findCommand.Options.Add(assemblyOption);
        findCommand.Options.Add(platformOption);
        findCommand.Options.Add(frameworkOption);
        findCommand.Options.Add(projectOption);
        findCommand.Options.Add(binOption);
        findCommand.Options.Add(tfmOption);
        findCommand.Options.Add(allOption);
        findCommand.Options.Add(limitOption);
        findCommand.Options.Add(jsonOption);
        findCommand.Options.Add(compactOption);
        findCommand.Options.Add(oneLineOption);
        findCommand.Options.Add(groupedOption);
        findCommand.Options.Add(terseOption);
        findCommand.Options.Add(nameOnlyOption);
        findCommand.Options.Add(verboseOption);
        findCommand.Options.Add(verbosityOption);
        findCommand.Options.Add(tipsOption);
        findCommand.Options.Add(sourceOption);
        findCommand.Options.Add(addSourceOption);
        findCommand.Options.Add(nugetConfigOption);

        findCommand.SetAction(async (parseResult, ct) =>
        {
            var pattern = parseResult.GetValue(patternArg);
            var terse = parseResult.GetValue(terseOption);
            var options = new FindOptions
            {
                Pattern = pattern!,
                Packages = parseResult.GetValue(packageOption) ?? [],
                Assemblies = parseResult.GetValue(assemblyOption) ?? [],
                PlatformAssemblies = parseResult.GetValue(platformOption) ?? [],
                PlatformFrameworks = parseResult.GetValue(frameworkOption) ?? [],
                Projects = parseResult.GetValue(projectOption) ?? [],
                BinPaths = parseResult.GetValue(binOption) ?? [],
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                OneLine = parseResult.GetValue(oneLineOption) || terse,
                Grouped = parseResult.GetValue(groupedOption) || terse,
                NameOnly = parseResult.GetValue(nameOnlyOption),
                Verbose = parseResult.GetValue(verboseOption),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            var exitCode = await FindCommand.ExecuteAsync(options);

            var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
            var tipLevel = options.IsRawOutput || verbosity == Verbosity.Quiet
                ? TipLevel.Quiet : ParseTipLevel(parseResult.GetValue(tipsOption), parseResult.GetResult(tipsOption) != null);

            if (exitCode == 0 && !options.IsRawOutput)
            {
                var pkg = options.Packages.Length > 0 ? options.Packages[0] : null;
                var sourceFlag = pkg != null ? $"--package {pkg}" : "--platform <library>";

                Hints.WriteTips(tipLevel,
                    new(ApiCommand.Name, $"<TypeName> {sourceFlag} --shape", "view type shape"),
                    new(FindCommand.Name, $"{pattern} {sourceFlag} --terse", "compact output"),
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
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var samplesCommand = new Command("samples", "Show sample code references for a type or library");

        var typeNameArg = new Argument<string?>("type")
        {
            Description = "Type name to get samples for (omit for library-wide samples)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string?>("--package") { Description = "Extract from package (name or name@version)" };
        var assemblyOption = new Option<string?>("--library") { Description = "Library path" };
        var platformOption = new Option<string?>("--platform") { Description = "Extract from platform library (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Platform framework (runtime, aspnetcore, netstandard). Use @version for specific version" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM" };
        var browsableUrlsOption = new Option<bool>("--browsable-urls") { Description = "Use /blob/ URLs for browser viewing instead of /raw/ URLs" };
        var listOption = new Option<bool>("--list") { Description = "List samples only (don't fetch content)" };
        var printOption = new Option<int?>("--print") { Description = "Print specific sample by number (raw code, no markdown)", Arity = ArgumentArity.ExactlyOne };

        samplesCommand.Arguments.Add(typeNameArg);
        samplesCommand.Options.Add(packageOption);
        samplesCommand.Options.Add(assemblyOption);
        samplesCommand.Options.Add(platformOption);
        samplesCommand.Options.Add(frameworkOption);
        samplesCommand.Options.Add(tfmOption);
        samplesCommand.Options.Add(browsableUrlsOption);
        samplesCommand.Options.Add(listOption);
        samplesCommand.Options.Add(printOption);
        samplesCommand.Options.Add(verboseOption);
        samplesCommand.Options.Add(verbosityOption);
        samplesCommand.Options.Add(sourceOption);
        samplesCommand.Options.Add(addSourceOption);
        samplesCommand.Options.Add(nugetConfigOption);
        samplesCommand.Options.Add(tipsOption);

        samplesCommand.SetAction(async (parseResult, ct) =>
        {
            var typeName = parseResult.GetValue(typeNameArg);
            
            var options = new SamplesOptions
            {
                TypeName = typeName,
                PackagePath = parseResult.GetValue(packageOption),
                AssemblyPath = parseResult.GetValue(assemblyOption),
                PlatformAssembly = parseResult.GetValue(platformOption),
                PlatformFramework = parseResult.GetValue(frameworkOption),
                Tfm = parseResult.GetValue(tfmOption),
                BrowsableUrls = parseResult.GetValue(browsableUrlsOption),
                Verbose = parseResult.GetValue(verboseOption),
                ListOnly = parseResult.GetValue(listOption),
                PrintSample = parseResult.GetValue(printOption),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await SamplesCommand.ExecuteAsync(options);
        });

        return samplesCommand;
    }

    private static Command CreatePlatformCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> tipsOption,
        Option<int?> limitOption,
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption)
    {
        var platformCommand = new Command(PlatformCommand.Name, "Inspect platform libraries and frameworks");

        var assemblyNameArg = new Argument<string?>("library")
        {
            Description = "Platform library name to inspect (e.g., System.Text.Json)",
            Arity = ArgumentArity.ZeroOrOne
        };
        assemblyNameArg.DefaultValueFactory = _ => null;

        var frameworkOption = new Option<string?>("--framework")
        {
            Description = "Framework to use (runtime, aspnetcore, netstandard). Use @version for specific version (e.g., runtime@8.0.23)"
        };
        var metadataOption = new Option<bool>("--metadata")
        {
            Description = "Show library info (PE metadata: name, version, TFM, architecture)"
        };
        var sourcelinkAuditOption = new Option<bool>("--source-link-audit")
        {
            Description = "Full provenance verification (parallel HTTP HEAD on all source files)"
        };
        platformCommand.Arguments.Add(assemblyNameArg);
        platformCommand.Options.Add(frameworkOption);
        platformCommand.Options.Add(metadataOption);
        platformCommand.Options.Add(sourcelinkAuditOption);
        platformCommand.Options.Add(jsonOption);
        platformCommand.Options.Add(verboseOption);
        platformCommand.Options.Add(verbosityOption);
        platformCommand.Options.Add(includeSectionsOption);
        platformCommand.Options.Add(excludeSectionsOption);
        platformCommand.Options.Add(tipsOption);

        // list subcommand (alias: ls) - list installed frameworks and assemblies
        var listCommand = new Command("list", "List installed frameworks and libraries");
        listCommand.Aliases.Add("ls");

        var listFrameworkOption = new Option<string?>("--framework")
        {
            Description = "Framework to use (runtime, aspnetcore, netstandard). Use @version for specific version (e.g., runtime@8.0.23)"
        };
        var listVersionsOption = new Option<bool>("--list-versions")
        {
            Description = "List all installed versions for each framework"
        };
        var includeTypesOption = new Option<bool>("--types")
        {
            Description = "Include public type count for each library (use with --framework)"
        };
        var listJsonOption = new Option<bool>("--json")
        {
            Description = "Output as JSON"
        };
        var compactOption = new Option<bool>("--compact")
        {
            Description = "Minified JSON (use with --json)"
        };
        var listLimitOption = new Option<int?>("--limit")
        {
            Description = "Limit number of results"
        };
        var listVerboseOption = new Option<bool>("--verbose")
        {
            Description = "Show verbose output"
        };
        var listVerbosityOption = new Option<string?>("-v")
        {
            Description = "Verbosity: q(uiet), m(inimal), n(ormal), d(etailed)"
        };
        listCommand.Options.Add(listFrameworkOption);
        listCommand.Options.Add(listVersionsOption);
        listCommand.Options.Add(includeTypesOption);
        listCommand.Options.Add(listJsonOption);
        listCommand.Options.Add(compactOption);
        listCommand.Options.Add(listLimitOption);
        listCommand.Options.Add(listVerboseOption);
        listCommand.Options.Add(listVerbosityOption);

        listCommand.SetAction(async (parseResult, ct) =>
        {
            var options = new PlatformOptions
            {
                Framework = parseResult.GetValue(listFrameworkOption),
                ListVersions = parseResult.GetValue(listVersionsOption),
                IncludeTypes = parseResult.GetValue(includeTypesOption),
                Limit = parseResult.GetValue(listLimitOption),
                JsonOutput = parseResult.GetValue(listJsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(listVerboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(listVerbosityOption))
            };

            return await PlatformCommand.ExecuteAsync(options);
        });

        platformCommand.Subcommands.Add(listCommand);

        platformCommand.SetAction(async (parseResult, ct) =>
        {
            var assemblyName = parseResult.GetValue(assemblyNameArg);

            // If an assembly name is specified, delegate to AssemblyCommand
            if (!string.IsNullOrEmpty(assemblyName))
            {
                bool showMetadata = parseResult.GetValue(metadataOption);
                bool runSourcelinkAudit = parseResult.GetValue(sourcelinkAuditOption);

                var assemblyOptions = new AssemblyOptions
                {
                    PlatformAssembly = assemblyName,
                    PlatformFramework = parseResult.GetValue(frameworkOption),
                    IncludeMetadata = showMetadata,
                    IncludeSourcelinkAudit = runSourcelinkAudit,
                    JsonOutput = parseResult.GetValue(jsonOption),
                    Verbose = parseResult.GetValue(verboseOption),
                    Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                    IncludeSections = ParseIncludeSections(parseResult, includeSectionsOption),
                    ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption))
                };

                return await AssemblyCommand.ExecuteAsync(assemblyOptions);
            }

            // No assembly specified: show help
            new HelpAction().Invoke(parseResult);
            return 0;
        });

        return platformCommand;
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

        var targetTypeArg = new Argument<string>("type")
        {
            Description = "Target interface or base type (e.g., IDisposable, Stream, IList<T>)"
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
        var platformOption = new Option<string[]>("--platform")
        {
            Description = "Search in platform library(s) (e.g., System.Text.Json). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var frameworkOption = new Option<string[]>("--framework")
        {
            Description = "Search all libraries in framework(s) (runtime, aspnetcore, netstandard). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden/obsolete types" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };

        implCommand.Arguments.Add(targetTypeArg);
        implCommand.Options.Add(packageOption);
        implCommand.Options.Add(assemblyOption);
        implCommand.Options.Add(platformOption);
        implCommand.Options.Add(frameworkOption);
        implCommand.Options.Add(tfmOption);
        implCommand.Options.Add(allOption);
        implCommand.Options.Add(limitOption);
        implCommand.Options.Add(jsonOption);
        implCommand.Options.Add(compactOption);
        implCommand.Options.Add(verboseOption);
        implCommand.Options.Add(verbosityOption);
        implCommand.Options.Add(sourceOption);
        implCommand.Options.Add(addSourceOption);
        implCommand.Options.Add(nugetConfigOption);
        implCommand.Options.Add(tipsOption);

        implCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);
            var options = new ImplementsOptions
            {
                TargetType = targetType!,
                Packages = parseResult.GetValue(packageOption) ?? [],
                Assemblies = parseResult.GetValue(assemblyOption) ?? [],
                PlatformAssemblies = parseResult.GetValue(platformOption) ?? [],
                PlatformFrameworks = parseResult.GetValue(frameworkOption) ?? [],
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(verboseOption),
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
        var versionOption = new Option<string?>("--version") { Description = "Package version" };

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

        packageCommand.SetAction(async (parseResult, ct) =>
        {
            var packageArgs = parseResult.GetValue(packageNameArg) ?? [];
            var explicitVersion = parseResult.GetValue(versionOption);

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
                ListVersions = parseResult.GetValue(versionsOption),
                IncludePrerelease = parseResult.GetValue(prereleaseOption),
                ShowReadme = parseResult.GetValue(readmeOption),
                OutputPath = parseResult.GetValue(outOption),
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = verbosity,
                IncludeSections = ParseIncludeSections(parseResult, includeSectionsOption),
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            var tipLevel = options.IsRawOutput || verbosity == Verbosity.Quiet
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
                tips.Add(new(ApiCommand.Name, $"--package {pkg}", "view public API surface"));
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

        routerCommand.SetAction(async (parseResult, ct) =>
        {
            var packageArgs = parseResult.GetValue(packageNameArg) ?? [];

            if (packageArgs.Length < 1)
            {
                new HelpAction().Invoke(parseResult);
                return 0;
            }

            var name = packageArgs[0];
            bool hasExplicitVersion = name.Contains('@');
            var bareName = hasExplicitVersion ? name[..name.IndexOf('@')] : name;

            // Platform candidate without explicit version: try platform resolution first
            if (!hasExplicitVersion && PlatformResolver.IsPlatformCandidate(bareName))
            {
                var (assemblyPath, framework, version, error) = PlatformResolver.ResolveAssembly(bareName);

                if (error == null && assemblyPath != null)
                {
                    var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
                    var assemblyOptions = new AssemblyOptions
                    {
                        PlatformAssembly = bareName,
                        JsonOutput = parseResult.GetValue(jsonOption),
                        Verbose = parseResult.GetValue(verboseOption),
                        Verbosity = verbosity,
                        IncludeSections = ParseIncludeSections(parseResult, includeSectionsOption),
                        ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption))
                    };

                    return await AssemblyCommand.ExecuteAsync(assemblyOptions);
                }
            }

            // Fall through to package command (NuGet resolution)
            var options = new InspectionOptions
            {
                PackageArgs = packageArgs,
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                IncludeSections = ParseIncludeSections(parseResult, includeSectionsOption),
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            var tipLevel = options.IsRawOutput || options.Verbosity == Verbosity.Quiet
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
                tips.Add(new(ApiCommand.Name, $"--package {pkg}", "view public API surface"));
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
                    var (asmPath, _, _, error) = PlatformResolver.ResolveAssembly(source);
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

    private static Command CreateApiCommand(
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
        var apiCommand = new Command(ApiCommand.Name, "Extract public API surface");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Package and type name. When no --package/--library/--platform is given, first arg is the package.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var apiPackageOption = new Option<string?>("--package") { Description = "Extract from package (file, name, or name@version)" };
        var apiAssemblyOption = new Option<string?>("--library") { Description = "Library path (local file, or relative path within package)" };
        var apiPlatformOption = new Option<string?>("--platform") { Description = "Extract from platform library (e.g., System.Text.Json)" };
        var apiFrameworkOption = new Option<string?>("--framework") { Description = "Platform framework (runtime, aspnetcore, netstandard). Use @version for specific version" };
        var apiTfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete members" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter to types by glob pattern (e.g., *Json*, Progress*)" };
        typeFilterOption.Aliases.Add("--type");
        var memberOption = new Option<string[]>("-m")
        {
            Description = "Filter to specific member(s)",
            AllowMultipleArgumentsPerToken = true
        };
        memberOption.Aliases.Add("--member");
        var docsOption = new Option<bool>("--docs") { Description = "Fetch and display XML doc comments from source" };
        var useLocalDocsOption = new Option<bool>("--use-local-docs") { Description = "Use XML doc files from packs directory (offline, implies --docs)" };
        var samplesOption = new Option<bool>("--samples") { Description = "Fetch and display code samples from source" };
        var sourcelinkOnlyOption = new Option<bool>("--sourcelink-only") { Description = "Filter to types with sourcelink resolution" };
        var browsableUrlsOption = new Option<bool>("--browsable-urls") { Description = "Use /blob/ URLs for browser viewing instead of /raw/ URLs (default is /raw/ for LLM consumption)" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var signaturesOnlyOption = new Option<bool>("--signatures-only") { Description = "Output only method signatures (no table formatting)" };
        var shapeOption = new Option<bool>("--shape") { Description = "View type shape (inheritance, interfaces, members)" };
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter to methods with unsafe signatures (pointers)" };
        var ctorOption = new Option<bool>("--ctor") { Description = "Show constructors only (shorthand for -m .ctor)" };
        var indexOption = new Option<int?>("--index") { Description = "Select a specific overload by 1-based index (use with -m)" };
        var paramsOption = new Option<string>("--params") { Description = "Select overload by comma-separated simple parameter type names (use with -m)" };
        var ofOption = new Option<string>("-of") { Description = "Select overload by first parameter simple type name (use with -m)" };

        apiCommand.Arguments.Add(argsArg);
        apiCommand.Options.Add(apiPackageOption);
        apiCommand.Options.Add(apiAssemblyOption);
        apiCommand.Options.Add(apiPlatformOption);
        apiCommand.Options.Add(apiFrameworkOption);
        apiCommand.Options.Add(apiTfmOption);
        apiCommand.Options.Add(allOption);
        apiCommand.Options.Add(typeFilterOption);
        apiCommand.Options.Add(memberOption);
        apiCommand.Options.Add(ctorOption);
        apiCommand.Options.Add(limitOption);
        apiCommand.Options.Add(docsOption);
        apiCommand.Options.Add(useLocalDocsOption);
        apiCommand.Options.Add(samplesOption);
        apiCommand.Options.Add(sourcelinkOnlyOption);
        apiCommand.Options.Add(browsableUrlsOption);
        apiCommand.Options.Add(jsonOption);
        apiCommand.Options.Add(compactOption);
        apiCommand.Options.Add(signaturesOnlyOption);
        apiCommand.Options.Add(shapeOption);
        apiCommand.Options.Add(unsafeOption);
        apiCommand.Options.Add(indexOption);
        apiCommand.Options.Add(paramsOption);
        apiCommand.Options.Add(ofOption);
        apiCommand.Options.Add(includeSectionsOption);
        apiCommand.Options.Add(excludeSectionsOption);
        apiCommand.Options.Add(markoutOption);
        apiCommand.Options.Add(verboseOption);
        apiCommand.Options.Add(verbosityOption);
        apiCommand.Options.Add(sourceOption);
        apiCommand.Options.Add(addSourceOption);
        apiCommand.Options.Add(nugetConfigOption);
        apiCommand.Options.Add(tipsOption);

        apiCommand.SetAction(async (parseResult, ct) =>
        {
            var args = parseResult.GetValue(argsArg) ?? [];
            var explicitPackage = parseResult.GetValue(apiPackageOption);
            var explicitAssembly = parseResult.GetValue(apiAssemblyOption);
            var explicitPlatform = parseResult.GetValue(apiPlatformOption);
            bool hasExplicitSource = explicitPackage != null || explicitAssembly != null || explicitPlatform != null;

            // No args and no explicit source: show help (unless bare -s for section discovery)
            if (args.Length == 0 && !hasExplicitSource)
            {
                if (parseResult.GetResult(includeSectionsOption) != null && parseResult.GetValue(includeSectionsOption) == null)
                {
                    var allApiSections = SectionRegistry.ApiTypeSections.Concat(SectionRegistry.ApiMemberSections).Distinct().ToArray();
                    SectionRegistry.ListSections(allApiSections);
                    return 0;
                }

                new HelpAction().Invoke(parseResult);
                return 0;
            }

            string? packagePath = explicitPackage;
            string? typeName = null;
            List<string> positionalMembers = [];

            if (hasExplicitSource)
            {
                // All positionals are type + member filters
                if (args.Length >= 1) typeName = args[0];
                if (args.Length >= 2) positionalMembers.AddRange(args[1..]);
            }
            else
            {
                // First positional is the package (or platform candidate)
                if (args.Length >= 1) packagePath = args[0];
                if (args.Length >= 2) typeName = args[1];
                if (args.Length >= 3) positionalMembers.AddRange(args[2..]);

                // Platform-preferred routing for System.*/Microsoft.* bare names
                if (packagePath != null && !packagePath.Contains('@') &&
                    PlatformResolver.IsPlatformCandidate(packagePath))
                {
                    var (asmPath, _, _, error) = PlatformResolver.ResolveAssembly(packagePath);
                    if (error == null && asmPath != null)
                    {
                        explicitPlatform = packagePath;
                        packagePath = null;
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

            HashSet<string> memberFilter = [];
            if (ctorOnly)
            {
                memberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ctor" };
            }
            else if (allMembers.Length > 0)
            {
                memberFilter = new HashSet<string>(allMembers, StringComparer.OrdinalIgnoreCase);
            }

            var options = new ApiOptions
            {
                TypeName = typeName,
                PackagePath = packagePath,
                AssemblyPath = explicitAssembly,
                PlatformAssembly = explicitPlatform,
                PlatformFramework = parseResult.GetValue(apiFrameworkOption),
                Tfm = parseResult.GetValue(apiTfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                TypeFilter = parseResult.GetValue(typeFilterOption),
                MemberFilter = memberFilter,
                Limit = parseResult.GetValue(limitOption),
                ShowDocs = parseResult.GetValue(docsOption) || parseResult.GetValue(useLocalDocsOption),
                DocsExplicitlySet = parseResult.GetResult(docsOption) is { Implicit: false } || parseResult.GetResult(useLocalDocsOption) is { Implicit: false },
                UseLocalDocs = parseResult.GetValue(useLocalDocsOption),
                ShowSamples = parseResult.GetValue(samplesOption),
                SourceLinkOnly = parseResult.GetValue(sourcelinkOnlyOption),
                BrowsableUrls = parseResult.GetValue(browsableUrlsOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                SignaturesOnly = parseResult.GetValue(signaturesOnlyOption),
                ShapeOutput = parseResult.GetValue(shapeOption),
                UnsafeOnly = parseResult.GetValue(unsafeOption),
                CtorOnly = ctorOnly,
                OverloadIndex = parseResult.GetValue(indexOption),
                ParamTypes = parseResult.GetValue(paramsOption)?.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                FirstParamType = parseResult.GetValue(ofOption),
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

            return await ApiCommand.ExecuteAsync(options);
        });

        return apiCommand;
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
}
