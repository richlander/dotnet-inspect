using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Options;
using DotnetInspector.Packages;

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
        "package", "assembly", "api", "type", "diff", "find", "search", "samples", "platform", "llmstxt", "extensions", "implements", "cache", "help", "--help", "-h", "-?", "--version"
    };

    /// <summary>
    /// Pre-processes args to handle implicit package command.
    /// </summary>
    public static string[] PreprocessArgs(string[] args)
    {
        if (args.Length > 0 && !KnownCommands.Contains(args[0]))
        {
            // Prepend "package" to treat as package command
            return ["package", .. args];
        }
        return args;
    }

    /// <summary>
    /// Creates the root command with all subcommands configured.
    /// </summary>
    public static RootCommand CreateRootCommand()
    {
        var rootCommand = new RootCommand($"""
            dotnet-inspect {VersionInfo.Version} - A CLI tool for inspecting .NET assemblies and NuGet packages
            
            Tip: Use -v:d for detailed output, --docs for XML documentation, --terse for compact find results.
            Run 'dotnet-inspect llmstxt' for complete usage examples.
            """);

        // Shared options (defined once, reused across commands)
        var jsonOption = new Option<bool>("--json") { Description = "Output as JSON" };
        var markoutOption = new Option<bool>("--markout") { Description = "Output as Markout (default)" };
        var verboseOption = new Option<bool>("--verbose") { Description = "Show progress messages on stderr" };
        var verbosityOption = new Option<string?>("-v") { Description = "Verbosity level: q(uiet), m(inimal), n(ormal), d(etailed)" };
        var includeSectionsOption = new Option<string?>("-s") { Description = "Include only these sections (comma-separated, e.g., -s:1,3)" };
        var excludeSectionsOption = new Option<string?>("-x") { Description = "Exclude these sections (comma-separated, e.g., -x:4)" };
        var limitOption = new Option<int?>("-n") { Description = "Limit number of results" };

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
        
        // API command
        var apiCommand = CreateApiCommand(jsonOption, markoutOption, verboseOption, verbosityOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(apiCommand);

        // Assembly command
        var assemblyCommand = CreateAssemblyCommand(jsonOption, markoutOption, verboseOption, verbosityOption, includeSectionsOption, excludeSectionsOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(assemblyCommand);

        // Cache command
        var cacheCommand = CreateCacheCommand(verboseOption, verbosityOption);
        rootCommand.Subcommands.Add(cacheCommand);

        // Diff command
        var diffCommand = CreateDiffCommand(verboseOption, verbosityOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(diffCommand);

        // Extensions command
        var extensionsCommand = CreateExtensionsCommand(jsonOption, verboseOption, verbosityOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(extensionsCommand);

        // Find command
        var findCommand = CreateFindCommand(jsonOption, verboseOption, verbosityOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(findCommand);

        // Implements command
        var implementsCommand = CreateImplementsCommand(jsonOption, verboseOption, verbosityOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(implementsCommand);

        // Package command
        var packageCommand = CreatePackageCommand(jsonOption, markoutOption, verboseOption, verbosityOption, includeSectionsOption, excludeSectionsOption, limitOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(packageCommand);

        // Platform command
        var platformCommand = CreatePlatformCommand(jsonOption, verboseOption, verbosityOption, limitOption);
        rootCommand.Subcommands.Add(platformCommand);

        // Samples command
        var samplesCommand = CreateSamplesCommand(verboseOption, verbosityOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(samplesCommand);

        // Type command
        var typeCommand = CreateTypeCommand(jsonOption, verboseOption, verbosityOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(typeCommand);

        // LLMs.txt command (meta command, listed last)
        var llmsTxtCommand = new Command("llmstxt", "Show usage examples (run this first)");
        llmsTxtCommand.SetAction((parseResult) => LlmsTxtCommand.Execute());
        rootCommand.Subcommands.Add(llmsTxtCommand);

        return rootCommand;
    }

    private static Command CreateTypeCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var typeCommand = new Command("type", "Show type shape with hierarchy and members (tree view)");

        var typeNameArg = new Argument<string>("type")
        {
            Description = "Type name to inspect"
        };

        var typePackageOption = new Option<string?>("--package") { Description = "Extract from package (name or name@version)" };
        var typeAssemblyOption = new Option<string?>("--assembly") { Description = "Assembly path" };
        var typePlatformOption = new Option<string?>("--platform") { Description = "Extract from platform assembly (e.g., System.Text.Json)" };
        var typeFrameworkOption = new Option<string?>("--framework") { Description = "Platform framework (runtime, aspnetcore, netstandard). Use @version for specific version" };
        var typeTfmOption = new Option<string?>("--tfm") { Description = "Select assembly by TFM" };
        var typeAllOption = new Option<bool>("--all") { Description = "Include hidden/obsolete members" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var memberOption = new Option<string?>("-m") { Description = "Filter to members matching name (keeps constructors)" };

        typeCommand.Arguments.Add(typeNameArg);
        typeCommand.Options.Add(typePackageOption);
        typeCommand.Options.Add(typeAssemblyOption);
        typeCommand.Options.Add(typePlatformOption);
        typeCommand.Options.Add(typeFrameworkOption);
        typeCommand.Options.Add(typeTfmOption);
        typeCommand.Options.Add(typeAllOption);
        typeCommand.Options.Add(memberOption);
        typeCommand.Options.Add(jsonOption);
        typeCommand.Options.Add(compactOption);
        typeCommand.Options.Add(verboseOption);
        typeCommand.Options.Add(verbosityOption);
        typeCommand.Options.Add(sourceOption);
        typeCommand.Options.Add(addSourceOption);
        typeCommand.Options.Add(nugetConfigOption);

        typeCommand.SetAction(async (parseResult, ct) =>
        {
            var typeName = parseResult.GetValue(typeNameArg);
            var options = new TypeOptions
            {
                PackagePath = parseResult.GetValue(typePackageOption),
                AssemblyPath = parseResult.GetValue(typeAssemblyOption),
                PlatformAssembly = parseResult.GetValue(typePlatformOption),
                PlatformFramework = parseResult.GetValue(typeFrameworkOption),
                Tfm = parseResult.GetValue(typeTfmOption),
                IncludeAll = parseResult.GetValue(typeAllOption),
                MemberFilter = parseResult.GetValue(memberOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(verboseOption),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await TypeCommand.ExecuteAsync(typeName!, options);
        });

        return typeCommand;
    }

    private static Command CreateCacheCommand(Option<bool> verboseOption, Option<string?> verbosityOption)
    {
        var cacheCommand = new Command("cache", "Manage the dotnet-inspect cache");

        var cleanOption = new Option<bool>("--clean") { Description = "Clear the cache" };

        cacheCommand.Options.Add(cleanOption);
        cacheCommand.Options.Add(verboseOption);
        cacheCommand.Options.Add(verbosityOption);

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
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var diffCommand = new Command("diff", "Compare API surfaces between package or platform versions");

        var typeNameArg = new Argument<string?>("type")
        {
            Description = "Type name to compare",
            Arity = ArgumentArity.ZeroOrOne
        };
        typeNameArg.DefaultValueFactory = _ => null;

        var packageOption = new Option<string?>("--package")
        {
            Description = "Package with version range (e.g., System.Text.Json@9.0.0..10.0.2)"
        };
        var platformOption = new Option<string?>("--platform")
        {
            Description = "Platform assembly with version range (e.g., System.Text.Json@8.0.23..10.0.2)"
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

        diffCommand.Arguments.Add(typeNameArg);
        diffCommand.Options.Add(packageOption);
        diffCommand.Options.Add(platformOption);
        diffCommand.Options.Add(frameworkOption);
        diffCommand.Options.Add(tfmOption);
        diffCommand.Options.Add(allOption);
        diffCommand.Options.Add(typeFilterOption);
        diffCommand.Options.Add(statOption);
        diffCommand.Options.Add(nameOnlyOption);
        diffCommand.Options.Add(verboseOption);
        diffCommand.Options.Add(verbosityOption);
        diffCommand.Options.Add(sourceOption);
        diffCommand.Options.Add(addSourceOption);
        diffCommand.Options.Add(nugetConfigOption);

        diffCommand.SetAction(async (parseResult, ct) =>
        {
            var typeName = parseResult.GetValue(typeNameArg);
            var typeFilterValues = parseResult.GetValue(typeFilterOption);

            // Merge positional type name with -t filter for backward compatibility
            HashSet<string>? typeFilter = null;
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
                PackageVersionRange = parseResult.GetValue(packageOption),
                PlatformVersionRange = parseResult.GetValue(platformOption),
                Framework = parseResult.GetValue(frameworkOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                Verbose = parseResult.GetValue(verboseOption),
                TypeFilter = typeFilter,
                Stat = parseResult.GetValue(statOption),
                NameOnly = parseResult.GetValue(nameOnlyOption),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await DiffCommand.ExecuteAsync(options);
        });

        return diffCommand;
    }

    private static Command CreateExtensionsCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
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
        var assemblyOption = new Option<string[]>("--assembly")
        {
            Description = "Search in assembly file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var platformOption = new Option<string[]>("--platform")
        {
            Description = "Search in platform assembly(s) (e.g., System.Text.Json). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var frameworkOption = new Option<string[]>("--framework")
        {
            Description = "Search all assemblies in framework(s) (runtime, aspnetcore, netstandard). Can repeat.",
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
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await ExtensionsCommand.ExecuteAsync(targetType!, options);
        });

        return extCommand;
    }

    private static Command CreateFindCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var findCommand = new Command("find", "Search for types across packages and assemblies");
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
        var assemblyOption = new Option<string[]>("--assembly")
        {
            Description = "Search in assembly file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var platformOption = new Option<string[]>("--platform")
        {
            Description = "Search in platform assembly(s) (e.g., System.Text.Json). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var frameworkOption = new Option<string[]>("--framework")
        {
            Description = "Search all assemblies in framework(s) (runtime, aspnetcore, netstandard). Can repeat.",
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
        var tfmOption = new Option<string?>("--tfm") { Description = "Select assembly or target framework by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete types" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var oneLineOption = new Option<bool>("--oneline") { Description = "Space-separated type names on one line" };
        var groupedOption = new Option<bool>("--grouped") { Description = "Group results by pattern (use with --oneline)" };
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
        findCommand.Options.Add(nameOnlyOption);
        findCommand.Options.Add(verboseOption);
        findCommand.Options.Add(verbosityOption);
        findCommand.Options.Add(sourceOption);
        findCommand.Options.Add(addSourceOption);
        findCommand.Options.Add(nugetConfigOption);

        findCommand.SetAction(async (parseResult, ct) =>
        {
            var pattern = parseResult.GetValue(patternArg);
            var options = new FindOptions
            {
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
                OneLine = parseResult.GetValue(oneLineOption),
                Grouped = parseResult.GetValue(groupedOption),
                NameOnly = parseResult.GetValue(nameOnlyOption),
                Verbose = parseResult.GetValue(verboseOption),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await FindCommand.ExecuteAsync(pattern!, options);
        });

        return findCommand;
    }

    private static Command CreateSamplesCommand(
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var samplesCommand = new Command("samples", "Show sample code references for a type or assembly");

        var typeNameArg = new Argument<string?>("type")
        {
            Description = "Type name to get samples for (omit for assembly-wide samples)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string?>("--package") { Description = "Extract from package (name or name@version)" };
        var assemblyOption = new Option<string?>("--assembly") { Description = "Assembly path" };
        var platformOption = new Option<string?>("--platform") { Description = "Extract from platform assembly (e.g., System.Text.Json)" };
        var frameworkOption = new Option<string?>("--framework") { Description = "Platform framework (runtime, aspnetcore, netstandard). Use @version for specific version" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select assembly by TFM" };
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

        samplesCommand.SetAction(async (parseResult, ct) =>
        {
            var typeName = parseResult.GetValue(typeNameArg);
            
            var options = new SamplesOptions
            {
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

            return await SamplesCommand.ExecuteAsync(typeName, options);
        });

        return samplesCommand;
    }

    private static Command CreatePlatformCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<int?> limitOption)
    {
        var platformCommand = new Command("platform", "List installed frameworks and assemblies");

        var frameworkOption = new Option<string?>("--framework")
        {
            Description = "List assemblies for framework (runtime, aspnetcore, netstandard). Use @version for specific version (e.g., runtime@8.0.23)"
        };
        var listVersionsOption = new Option<bool>("--list-versions")
        {
            Description = "List all installed versions for each framework"
        };
        var includeTypesOption = new Option<bool>("--types")
        {
            Description = "Include public type count for each assembly (use with --framework)"
        };
        var compactOption = new Option<bool>("--compact")
        {
            Description = "Minified JSON (use with --json)"
        };

        platformCommand.Options.Add(frameworkOption);
        platformCommand.Options.Add(listVersionsOption);
        platformCommand.Options.Add(includeTypesOption);
        platformCommand.Options.Add(limitOption);
        platformCommand.Options.Add(jsonOption);
        platformCommand.Options.Add(compactOption);
        platformCommand.Options.Add(verboseOption);
        platformCommand.Options.Add(verbosityOption);

        platformCommand.SetAction(async (parseResult, ct) =>
        {
            var options = new PlatformOptions
            {
                Framework = parseResult.GetValue(frameworkOption),
                ListVersions = parseResult.GetValue(listVersionsOption),
                IncludeTypes = parseResult.GetValue(includeTypesOption),
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(verboseOption)
            };

            return await PlatformCommand.ExecuteAsync(options);
        });

        return platformCommand;
    }

    private static Command CreateImplementsCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
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
        var assemblyOption = new Option<string[]>("--assembly")
        {
            Description = "Search in assembly file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var platformOption = new Option<string[]>("--platform")
        {
            Description = "Search in platform assembly(s) (e.g., System.Text.Json). Can repeat.",
            AllowMultipleArgumentsPerToken = true
        };
        var frameworkOption = new Option<string[]>("--framework")
        {
            Description = "Search all assemblies in framework(s) (runtime, aspnetcore, netstandard). Can repeat.",
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

            return await ImplementsCommand.ExecuteAsync(targetType!, options);
        });

        return implCommand;
    }

    private static Command CreatePackageCommand(
        Option<bool> jsonOption,
        Option<bool> markoutOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var packageCommand = new Command("package", "Inspect a NuGet package");

        var packageNameArg = new Argument<string[]>("package")
        {
            Description = "NuGet package name or path to .nupkg file, optionally with version (e.g., System.Text.Json@9.0.0)",
            Arity = ArgumentArity.ZeroOrMore
        };

        var depsOption = new Option<bool>("--deps") { Description = "Include dependency analysis" };
        var filesOption = new Option<bool>("--files") { Description = "List DLLs in the package" };
        var allFilesOption = new Option<bool>("--all") { Description = "With --files: list all files in entire package" };
        var treeOption = new Option<bool>("--tree") { Description = "With --files: display as tree view" };
        var versionsOption = new Option<bool>("--versions") { Description = "List available versions from nuget.org" };
        var prereleaseOption = new Option<bool>("--preview") { Description = "With --versions: include prerelease versions" };
        prereleaseOption.Aliases.Add("--prerelease");
        var readmeOption = new Option<bool>("--readme") { Description = "Show the README.md content from the package" };
        var outOption = new Option<string?>("--out") { Description = "Write output to file instead of stdout" };
        var discoverOption = new Option<bool>("--discover") { Description = "List available sections and exit" };

        packageCommand.Arguments.Add(packageNameArg);
        packageCommand.Options.Add(depsOption);
        packageCommand.Options.Add(filesOption);
        packageCommand.Options.Add(allFilesOption);
        packageCommand.Options.Add(treeOption);
        packageCommand.Options.Add(versionsOption);
        packageCommand.Options.Add(prereleaseOption);
        packageCommand.Options.Add(readmeOption);
        packageCommand.Options.Add(outOption);
        packageCommand.Options.Add(discoverOption);
        packageCommand.Options.Add(limitOption);
        packageCommand.Options.Add(jsonOption);
        packageCommand.Options.Add(markoutOption);
        packageCommand.Options.Add(verboseOption);
        packageCommand.Options.Add(verbosityOption);
        packageCommand.Options.Add(includeSectionsOption);
        packageCommand.Options.Add(excludeSectionsOption);
        packageCommand.Options.Add(sourceOption);
        packageCommand.Options.Add(addSourceOption);
        packageCommand.Options.Add(nugetConfigOption);

        packageCommand.SetAction(async (parseResult, ct) =>
        {
            var packageArgs = parseResult.GetValue(packageNameArg) ?? [];
            var options = new InspectionOptions
            {
                IncludeDeps = parseResult.GetValue(depsOption),
                ListFiles = parseResult.GetValue(filesOption),
                ListAllFiles = parseResult.GetValue(allFilesOption),
                TreeView = parseResult.GetValue(treeOption),
                ListVersions = parseResult.GetValue(versionsOption),
                IncludePrerelease = parseResult.GetValue(prereleaseOption),
                ShowReadme = parseResult.GetValue(readmeOption),
                OutputPath = parseResult.GetValue(outOption),
                Discover = parseResult.GetValue(discoverOption),
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                IncludeSections = ParseSectionList(parseResult.GetValue(includeSectionsOption)),
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await PackageCommand.ExecuteAsync(packageArgs, options);
        });

        return packageCommand;
    }

    private static Command CreateAssemblyCommand(
        Option<bool> jsonOption,
        Option<bool> markoutOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var assemblyCommand = new Command("assembly", "Inspect a .NET assembly file");

        var assemblyPathArg = new Argument<string?>("path")
        {
            Description = "Path to assembly file",
            Arity = ArgumentArity.ZeroOrOne
        };
        assemblyPathArg.DefaultValueFactory = _ => null;

        var auditOption = new Option<bool>("--audit") { Description = "Include SourceLink and determinism audit" };
        var asmPackageOption = new Option<string?>("--package") { Description = "Extract assembly from package (file, name, or name@version)" };
        var asmPlatformOption = new Option<string?>("--platform") { Description = "Inspect platform assembly (e.g., System.Text.Json)" };
        var asmFrameworkOption = new Option<string?>("--framework") { Description = "Platform framework (runtime, aspnetcore). Use @version for specific version" };
        var asmTfmOption = new Option<string?>("--tfm") { Description = "Select assembly by TFM (e.g., net8.0)" };

        assemblyCommand.Arguments.Add(assemblyPathArg);
        assemblyCommand.Options.Add(auditOption);
        assemblyCommand.Options.Add(asmPackageOption);
        assemblyCommand.Options.Add(asmPlatformOption);
        assemblyCommand.Options.Add(asmFrameworkOption);
        assemblyCommand.Options.Add(asmTfmOption);
        assemblyCommand.Options.Add(jsonOption);
        assemblyCommand.Options.Add(markoutOption);
        assemblyCommand.Options.Add(verboseOption);
        assemblyCommand.Options.Add(verbosityOption);
        assemblyCommand.Options.Add(includeSectionsOption);
        assemblyCommand.Options.Add(excludeSectionsOption);
        assemblyCommand.Options.Add(sourceOption);
        assemblyCommand.Options.Add(addSourceOption);
        assemblyCommand.Options.Add(nugetConfigOption);

        assemblyCommand.SetAction(async (parseResult, ct) =>
        {
            var assemblyPath = parseResult.GetValue(assemblyPathArg);
            var options = new AssemblyOptions
            {
                IncludeAudit = parseResult.GetValue(auditOption),
                PackagePath = parseResult.GetValue(asmPackageOption),
                PlatformAssembly = parseResult.GetValue(asmPlatformOption),
                PlatformFramework = parseResult.GetValue(asmFrameworkOption),
                Tfm = parseResult.GetValue(asmTfmOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                IncludeSections = ParseSectionList(parseResult.GetValue(includeSectionsOption)),
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await AssemblyCommand.ExecuteAsync(assemblyPath, options);
        });

        return assemblyCommand;
    }

    private static Command CreateApiCommand(
        Option<bool> jsonOption,
        Option<bool> markoutOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<int?> limitOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var apiCommand = new Command("api", "Extract public API surface");

        var typeNameArg = new Argument<string?>("type")
        {
            Description = "Type name (full or simple). If omitted, lists all types.",
            Arity = ArgumentArity.ZeroOrOne
        };
        typeNameArg.DefaultValueFactory = _ => null;

        var apiPackageOption = new Option<string?>("--package") { Description = "Extract from package (file, name, or name@version)" };
        var apiAssemblyOption = new Option<string?>("--assembly") { Description = "Assembly path (local file, or relative path within package)" };
        var apiPlatformOption = new Option<string?>("--platform") { Description = "Extract from platform assembly (e.g., System.Text.Json)" };
        var apiFrameworkOption = new Option<string?>("--framework") { Description = "Platform framework (runtime, aspnetcore, netstandard). Use @version for specific version" };
        var apiTfmOption = new Option<string?>("--tfm") { Description = "Select assembly by TFM (e.g., net8.0)" };
        var interfacesOption = new Option<bool>("--interfaces") { Description = "Show implemented interfaces" };
        var hierarchyOption = new Option<bool>("--hierarchy") { Description = "Show type hierarchy (base, interfaces, derived types)" };
        var allOption = new Option<bool>("--all") { Description = "Include hidden (EditorBrowsable.Never) and obsolete members" };
        var filterOption = new Option<string?>("--filter") { Description = "Filter type names by glob pattern (e.g., *Json*, Progress*)" };
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
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter to methods with unsafe signatures (pointers)" };
        var ctorOption = new Option<bool>("--ctor") { Description = "Show constructors only (shorthand for -m .ctor)" };
        var fieldsOnlyOption = new Option<bool>("--fields-only") { Description = "Show only type info (source URL, docs) without member tables" };

        apiCommand.Arguments.Add(typeNameArg);
        apiCommand.Options.Add(apiPackageOption);
        apiCommand.Options.Add(apiAssemblyOption);
        apiCommand.Options.Add(apiPlatformOption);
        apiCommand.Options.Add(apiFrameworkOption);
        apiCommand.Options.Add(apiTfmOption);
        apiCommand.Options.Add(interfacesOption);
        apiCommand.Options.Add(hierarchyOption);
        apiCommand.Options.Add(allOption);
        apiCommand.Options.Add(filterOption);
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
        apiCommand.Options.Add(unsafeOption);
        apiCommand.Options.Add(fieldsOnlyOption);
        apiCommand.Options.Add(markoutOption);
        apiCommand.Options.Add(verboseOption);
        apiCommand.Options.Add(verbosityOption);
        apiCommand.Options.Add(sourceOption);
        apiCommand.Options.Add(addSourceOption);
        apiCommand.Options.Add(nugetConfigOption);

        apiCommand.SetAction(async (parseResult, ct) =>
        {
            var typeName = parseResult.GetValue(typeNameArg);
            var members = parseResult.GetValue(memberOption);
            var ctorOnly = parseResult.GetValue(ctorOption);

            // If --ctor is specified, add .ctor to member filter
            HashSet<string>? memberFilter = null;
            if (ctorOnly)
            {
                memberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ctor" };
            }
            else if (members?.Length > 0)
            {
                memberFilter = new HashSet<string>(members, StringComparer.OrdinalIgnoreCase);
            }

            var options = new ApiOptions
            {
                PackagePath = parseResult.GetValue(apiPackageOption),
                AssemblyPath = parseResult.GetValue(apiAssemblyOption),
                PlatformAssembly = parseResult.GetValue(apiPlatformOption),
                PlatformFramework = parseResult.GetValue(apiFrameworkOption),
                Tfm = parseResult.GetValue(apiTfmOption),
                ShowInterfaces = parseResult.GetValue(interfacesOption),
                ShowHierarchy = parseResult.GetValue(hierarchyOption),
                IncludeAll = parseResult.GetValue(allOption),
                TypeFilter = parseResult.GetValue(filterOption),
                MemberFilter = memberFilter,
                Limit = parseResult.GetValue(limitOption),
                ShowDocs = parseResult.GetValue(docsOption) || parseResult.GetValue(useLocalDocsOption),
                UseLocalDocs = parseResult.GetValue(useLocalDocsOption),
                ShowSamples = parseResult.GetValue(samplesOption),
                SourceLinkOnly = parseResult.GetValue(sourcelinkOnlyOption),
                BrowsableUrls = parseResult.GetValue(browsableUrlsOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                SignaturesOnly = parseResult.GetValue(signaturesOnlyOption),
                UnsafeOnly = parseResult.GetValue(unsafeOption),
                CtorOnly = ctorOnly,
                FieldsOnly = parseResult.GetValue(fieldsOnlyOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            return await ApiCommand.ExecuteAsync(typeName, options);
        });

        return apiCommand;
    }

    public static Verbosity ParseVerbosity(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Verbosity.Minimal;

        var v = value.TrimStart(':').ToLowerInvariant();
        return v switch
        {
            "q" or "quiet" => Verbosity.Quiet,
            "m" or "minimal" => Verbosity.Minimal,
            "n" or "normal" => Verbosity.Normal,
            "d" or "detailed" => Verbosity.Detailed,
            _ => Verbosity.Minimal
        };
    }

    public static HashSet<int>? ParseSectionList(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        var v = value.TrimStart(':');
        var sections = new HashSet<int>();
        foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out int section) && section > 0)
            {
                sections.Add(section);
            }
        }
        return sections.Count > 0 ? sections : null;
    }

    /// <summary>
    /// Creates NuGetSourceOptions from parsed command line values.
    /// </summary>
    public static NuGetSourceOptions ParseNuGetSourceOptions(
        ParseResult parseResult,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var sources = parseResult.GetValue(sourceOption) ?? [];
        var addSources = parseResult.GetValue(addSourceOption) ?? [];
        var configFile = parseResult.GetValue(nugetConfigOption);

        if (sources.Length == 0 && addSources.Length == 0 && configFile == null)
        {
            return NuGetSourceOptions.Default;
        }

        return new NuGetSourceOptions
        {
            Sources = sources,
            AdditionalSources = addSources,
            ConfigFile = configFile
        };
    }
}
