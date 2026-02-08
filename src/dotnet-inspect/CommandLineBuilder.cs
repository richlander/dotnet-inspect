using System.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;
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
        "package", "assembly", "audit", "api", "diff", "find", "search", "samples", "platform", "llmstxt", "extensions", "implements", "cache", "cli", "help", "--help", "-h", "-?", "--version"
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
        var includeSectionsOption = new Option<string?>("-s") { Description = "Include only these sections by name (comma-separated, e.g., -s:Methods,Properties).\nUse -s alone for header only.", Arity = ArgumentArity.ZeroOrOne };
        var excludeSectionsOption = new Option<string?>("-x") { Description = "Exclude these sections by name (comma-separated, e.g., -x:Methods)" };
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
        var apiCommand = CreateApiCommand(jsonOption, markoutOption, verboseOption, verbosityOption, limitOption, includeSectionsOption, excludeSectionsOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(apiCommand);

        // Audit command (opinionated, always strict)
        var auditCommand = CreateAuditCommand(jsonOption, verboseOption, verbosityOption, sourceOption, addSourceOption, nugetConfigOption);
        rootCommand.Subcommands.Add(auditCommand);

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
        var platformCommand = CreatePlatformCommand(jsonOption, verboseOption, verbosityOption, limitOption, includeSectionsOption, excludeSectionsOption);
        rootCommand.Subcommands.Add(platformCommand);

        // Samples command
        var samplesCommand = CreateSamplesCommand(verboseOption, verbosityOption, sourceOption, addSourceOption, nugetConfigOption);
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

        return rootCommand;
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

            var exitCode = await DiffCommand.ExecuteAsync(options);

            var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
            if (exitCode == 0 && verbosity != Verbosity.Quiet)
            {
                // If filtered to specific type(s), suggest viewing all changes
                if (typeFilter != null)
                {
                    var versionRange = options.PackageVersionRange ?? options.PlatformVersionRange;
                    var sourceFlag = options.PackageVersionRange != null ? "--package" : "--platform";
                    Hints.WriteHint($"dotnet-inspect diff {sourceFlag} {versionRange}   # diff all types");
                }
                // If showing all types, suggest api --tree for inspection
                else if (!options.Stat && !options.NameOnly)
                {
                    var versionRange = options.PackageVersionRange ?? options.PlatformVersionRange;
                    if (versionRange != null)
                    {
                        var atIdx = versionRange.IndexOf('@');
                        var dotDotIdx = versionRange.IndexOf("..", StringComparison.Ordinal);
                        if (atIdx > 0 && dotDotIdx > atIdx)
                        {
                            var pkgName = versionRange[..atIdx];
                            var toVersion = versionRange[(dotDotIdx + 2)..];
                            var sourceFlag = options.PackageVersionRange != null ? "--package" : "--platform";
                            Hints.WriteHint($"dotnet-inspect api <TypeName> {sourceFlag} {pkgName}@{toVersion} --tree   # view current type shape");
                        }
                    }
                }
            }

            return exitCode;
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
        findCommand.Options.Add(sourceOption);
        findCommand.Options.Add(addSourceOption);
        findCommand.Options.Add(nugetConfigOption);

        findCommand.SetAction(async (parseResult, ct) =>
        {
            var pattern = parseResult.GetValue(patternArg);
            var terse = parseResult.GetValue(terseOption);
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
                OneLine = parseResult.GetValue(oneLineOption) || terse,
                Grouped = parseResult.GetValue(groupedOption) || terse,
                NameOnly = parseResult.GetValue(nameOnlyOption),
                Verbose = parseResult.GetValue(verboseOption),
                SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
            };

            var exitCode = await FindCommand.ExecuteAsync(pattern!, options);

            var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
            if (exitCode == 0 && !options.JsonOutput && verbosity != Verbosity.Quiet
                && !options.OneLine && !options.NameOnly)
            {
                // Suggest api --tree for drilling into results
                var pkg = options.Packages.Length > 0 ? options.Packages[0] : null;
                var sourceFlag = pkg != null ? $"--package {pkg}" : "--platform <assembly>";
                Hints.WriteHint($"dotnet-inspect api <TypeName> {sourceFlag} --tree   # view type shape");
            }

            return exitCode;
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
        Option<int?> limitOption,
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption)
    {
        var platformCommand = new Command("platform", "List installed frameworks and assemblies, or inspect a platform assembly");

        var assemblyNameArg = new Argument<string?>("assembly")
        {
            Description = "Platform assembly name to inspect (e.g., System.Text.Json)",
            Arity = ArgumentArity.ZeroOrOne
        };
        assemblyNameArg.DefaultValueFactory = _ => null;

        var frameworkOption = new Option<string?>("--framework")
        {
            Description = "Framework to use (runtime, aspnetcore, netstandard). Use @version for specific version (e.g., runtime@8.0.23)"
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
        var metadataOption = new Option<bool>("--metadata")
        {
            Description = "Show assembly info (PE metadata: name, version, TFM, architecture)"
        };
        var symbolsOption = new Option<bool>("--symbols")
        {
            Description = "Show Build Audit + PDB info (downloads PDB if needed)"
        };
        var sourcelinkAuditOption = new Option<bool>("--sourcelink-audit")
        {
            Description = "Full provenance verification (parallel HTTP HEAD on all source files)"
        };
        // Deprecated flags
        var auditOption = new Option<bool>("--audit")
        {
            Description = "[Deprecated: use --sourcelink-audit] Full provenance verification"
        };
        var sourcelinkOption = new Option<bool>("--sourcelink")
        {
            Description = "[Deprecated: use --symbols] Show SourceLink presence and URL"
        };

        platformCommand.Arguments.Add(assemblyNameArg);
        platformCommand.Options.Add(frameworkOption);
        platformCommand.Options.Add(listVersionsOption);
        platformCommand.Options.Add(includeTypesOption);
        platformCommand.Options.Add(metadataOption);
        platformCommand.Options.Add(symbolsOption);
        platformCommand.Options.Add(sourcelinkAuditOption);
        platformCommand.Options.Add(auditOption);
        platformCommand.Options.Add(sourcelinkOption);
        platformCommand.Options.Add(limitOption);
        platformCommand.Options.Add(jsonOption);
        platformCommand.Options.Add(compactOption);
        platformCommand.Options.Add(verboseOption);
        platformCommand.Options.Add(verbosityOption);
        platformCommand.Options.Add(includeSectionsOption);
        platformCommand.Options.Add(excludeSectionsOption);

        platformCommand.SetAction(async (parseResult, ct) =>
        {
            var assemblyName = parseResult.GetValue(assemblyNameArg);
            bool showMetadata = parseResult.GetValue(metadataOption);
            bool showSymbols = parseResult.GetValue(symbolsOption);
            bool runSourcelinkAudit = parseResult.GetValue(sourcelinkAuditOption);

            // Deprecated flag mapping
            bool runAudit = parseResult.GetValue(auditOption);
            bool showSourcelink = parseResult.GetValue(sourcelinkOption);
            if (runAudit)
            {
                Console.Error.WriteLine("Warning: --audit is deprecated. Use --sourcelink-audit instead.");
                runSourcelinkAudit = true;
            }
            if (showSourcelink)
            {
                Console.Error.WriteLine("Warning: --sourcelink is deprecated. Use --symbols instead.");
                showSymbols = true;
            }

            // If an assembly name is specified, delegate to AssemblyCommand
            if (!string.IsNullOrEmpty(assemblyName))
            {
                var assemblyOptions = new AssemblyOptions
                {
                    PlatformAssembly = assemblyName,
                    PlatformFramework = parseResult.GetValue(frameworkOption),
                    IncludeMetadata = showMetadata,
                    IncludeSymbols = showSymbols,
                    IncludeSourcelinkAudit = runSourcelinkAudit,
                    JsonOutput = parseResult.GetValue(jsonOption),
                    Verbose = parseResult.GetValue(verboseOption),
                    Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                    IncludeSections = ParseSectionList(parseResult.GetValue(includeSectionsOption)),
                    ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption))
                };

                return await AssemblyCommand.ExecuteAsync(null, assemblyOptions);
            }

            // Otherwise, list frameworks/assemblies
            var options = new PlatformOptions
            {
                Framework = parseResult.GetValue(frameworkOption),
                ListVersions = parseResult.GetValue(listVersionsOption),
                IncludeTypes = parseResult.GetValue(includeTypesOption),
                Limit = parseResult.GetValue(limitOption),
                JsonOutput = parseResult.GetValue(jsonOption),
                CompactJson = parseResult.GetValue(compactOption),
                Verbose = parseResult.GetValue(verboseOption),
                Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption))
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
        var tfmsOption = new Option<bool>("--tfms") { Description = "List target frameworks in the package" };
        var allFilesOption = new Option<bool>("--all") { Description = "With --files: list all files in entire package" };
        var versionsOption = new Option<bool>("--versions") { Description = "List available versions from nuget.org" };
        var prereleaseOption = new Option<bool>("--preview") { Description = "With --versions: include prerelease versions" };
        prereleaseOption.Aliases.Add("--prerelease");
        var readmeOption = new Option<bool>("--readme") { Description = "Show the README.md content from the package" };
        var outOption = new Option<string?>("--out") { Description = "Write output to file instead of stdout" };
        var discoverOption = new Option<bool>("--discover") { Description = "List available sections and exit" };
        // New tiered flags
        var metadataOption = new Option<bool>("--metadata") { Description = "Show assembly info (PE metadata: name, version, TFM, architecture)" };
        var symbolsOption = new Option<bool>("--symbols") { Description = "Show Build Audit + PDB info (downloads PDB if needed)" };
        var sourcelinkAuditOption = new Option<bool>("--sourcelink-audit") { Description = "Full provenance verification (parallel HTTP HEAD on all source files)" };
        // Deprecated flags
        var assemblyOption = new Option<bool>("--assembly") { Description = "[Deprecated: use --metadata] Show assembly info" };
        var auditOption = new Option<bool>("--audit") { Description = "[Deprecated: use --sourcelink-audit] Verify SourceLink, determinism, and signature" };
        var sourcelinkOption = new Option<bool>("--sourcelink") { Description = "[Deprecated: use --symbols] Show SourceLink presence and URL" };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select assembly by TFM (e.g., net8.0)" };
        var versionOption = new Option<string?>("--version") { Description = "Package version" };

        packageCommand.Arguments.Add(packageNameArg);
        packageCommand.Options.Add(depsOption);
        packageCommand.Options.Add(filesOption);
        packageCommand.Options.Add(tfmsOption);
        packageCommand.Options.Add(allFilesOption);
        packageCommand.Options.Add(versionsOption);
        packageCommand.Options.Add(prereleaseOption);
        packageCommand.Options.Add(readmeOption);
        packageCommand.Options.Add(metadataOption);
        packageCommand.Options.Add(symbolsOption);
        packageCommand.Options.Add(sourcelinkAuditOption);
        packageCommand.Options.Add(assemblyOption);
        packageCommand.Options.Add(auditOption);
        packageCommand.Options.Add(sourcelinkOption);
        packageCommand.Options.Add(tfmOption);
        packageCommand.Options.Add(versionOption);
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
            var explicitVersion = parseResult.GetValue(versionOption);

            // New tiered flags
            bool showMetadata = parseResult.GetValue(metadataOption);
            bool showSymbols = parseResult.GetValue(symbolsOption);
            bool runSourcelinkAudit = parseResult.GetValue(sourcelinkAuditOption);

            // Deprecated flag mapping
            bool showAssembly = parseResult.GetValue(assemblyOption);
            bool runAudit = parseResult.GetValue(auditOption);
            bool showSourcelink = parseResult.GetValue(sourcelinkOption);
            if (showAssembly)
            {
                Console.Error.WriteLine("Warning: --assembly is deprecated. Use --metadata instead.");
                showMetadata = true;
            }
            if (runAudit)
            {
                Console.Error.WriteLine("Warning: --audit is deprecated. Use --sourcelink-audit instead.");
                runSourcelinkAudit = true;
            }
            if (showSourcelink)
            {
                Console.Error.WriteLine("Warning: --sourcelink is deprecated. Use --symbols instead.");
                showSymbols = true;
            }

            // Handle --metadata, --symbols, --sourcelink-audit: delegate to AssemblyCommand
            if (showMetadata || showSymbols || runSourcelinkAudit)
            {
                if (packageArgs.Length < 1)
                {
                    Console.Error.WriteLine("Error: Package name required.");
                    return 1;
                }

                var assemblyOptions = new AssemblyOptions
                {
                    PackagePath = explicitVersion != null && !packageArgs[0].Contains('@')
                        ? $"{packageArgs[0]}@{explicitVersion}"
                        : packageArgs[0],
                    Tfm = parseResult.GetValue(tfmOption),
                    IncludeMetadata = showMetadata,
                    IncludeSymbols = showSymbols,
                    IncludeSourcelinkAudit = runSourcelinkAudit,
                    JsonOutput = parseResult.GetValue(jsonOption),
                    Verbose = parseResult.GetValue(verboseOption),
                    Verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption)),
                    IncludeSections = ParseSectionList(parseResult.GetValue(includeSectionsOption)),
                    ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
                    SourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption)
                };

                return await AssemblyCommand.ExecuteAsync(null, assemblyOptions);
            }

            var options = new InspectionOptions
            {
                IncludeDeps = parseResult.GetValue(depsOption),
                ListFiles = parseResult.GetValue(filesOption),
                ListTfms = parseResult.GetValue(tfmsOption),
                ListAllFiles = parseResult.GetValue(allFilesOption),
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

            var exitCode = await PackageCommand.ExecuteAsync(packageArgs, options, explicitVersion);

            if (exitCode == 0 && !options.JsonOutput && options.Verbosity != Verbosity.Quiet
                && packageArgs.Length > 0 && !options.ListVersions && !options.ListFiles && !options.ListTfms && !options.Discover && !options.ShowReadme)
            {
                var pkg = packageArgs[0];
                if (pkg.Contains('@')) pkg = pkg[..pkg.IndexOf('@')];
                Hints.WriteHint($"dotnet-inspect api --package {pkg}   # view public API surface");
            }

            return exitCode;
        });

        return packageCommand;
    }

    private static Command CreateAuditCommand(
        Option<bool> jsonOption,
        Option<bool> verboseOption,
        Option<string?> verbosityOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var auditCommand = new Command("audit", "Verify package/assembly provenance (SourceLink, determinism, signature)");

        var targetArg = new Argument<string[]>("target")
        {
            Description = "Package name, file path, or .nupkg path (can specify multiple)",
            Arity = ArgumentArity.OneOrMore
        };

        var tfmOption = new Option<string?>("--tfm") { Description = "Select assembly by TFM (e.g., net8.0)" };
        var versionOption = new Option<string?>("--version") { Description = "Package version" };

        auditCommand.Arguments.Add(targetArg);
        auditCommand.Options.Add(tfmOption);
        auditCommand.Options.Add(versionOption);
        auditCommand.Options.Add(jsonOption);
        auditCommand.Options.Add(verboseOption);
        auditCommand.Options.Add(verbosityOption);
        auditCommand.Options.Add(sourceOption);
        auditCommand.Options.Add(addSourceOption);
        auditCommand.Options.Add(nugetConfigOption);

        auditCommand.SetAction(async (parseResult, ct) =>
        {
            var targets = parseResult.GetValue(targetArg) ?? [];
            if (targets.Length == 0)
            {
                Console.Error.WriteLine("Error: At least one target required (package name, file path, or .nupkg).");
                return 1;
            }

            var tfm = parseResult.GetValue(tfmOption);
            var explicitVersion = parseResult.GetValue(versionOption);
            var verbose = parseResult.GetValue(verboseOption);
            var verbosity = ParseVerbosity(parseResult.GetValue(verbosityOption));
            var jsonOutput = parseResult.GetValue(jsonOption);
            var sourceOptions = ParseNuGetSourceOptions(parseResult, sourceOption, addSourceOption, nugetConfigOption);

            int failures = 0;

            foreach (var target in targets)
            {
                // Determine input type and create appropriate options
                bool isFilePath = target.Contains('/') || target.Contains('\\') ||
                                  target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                                  target.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

                // Disambiguation for System.*/Microsoft.* bare names
                if (!isFilePath && !target.Contains('@') && explicitVersion == null)
                {
                    string bareName = target;
                    if (bareName.StartsWith("System.", StringComparison.OrdinalIgnoreCase) ||
                        bareName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
                    {
                        var (platformPath, _, _, platformError) =
                            PlatformResolver.ResolveAssembly(bareName);

                        if (platformError == null && platformPath != null)
                        {
                            Console.Error.WriteLine($"'{bareName}' is available as both a NuGet package and a platform assembly.");
                            Console.Error.WriteLine();
                            Console.Error.WriteLine($"  dotnet-inspect audit {bareName}@<version>       # audit NuGet package (specific version)");
                            Console.Error.WriteLine($"  dotnet-inspect package {bareName} --audit        # audit NuGet package (latest)");
                            Console.Error.WriteLine($"  dotnet-inspect platform {bareName} --audit       # audit platform assembly");
                            Console.Error.WriteLine();
                            Console.Error.WriteLine($"Tip: Use 'dotnet-inspect package {bareName} --versions -n 5' to find recent versions.");
                            failures++;
                            continue;
                        }
                    }
                }

                // Apply --version to bare package names
                var effectiveTarget = target;
                if (explicitVersion != null && !isFilePath && !target.Contains('@'))
                {
                    effectiveTarget = $"{target}@{explicitVersion}";
                }

                var options = new AssemblyOptions
                {
                    IncludeSourcelinkAudit = true,
                    PackagePath = isFilePath ? null : effectiveTarget,
                    Tfm = tfm,
                    JsonOutput = jsonOutput,
                    Verbose = verbose,
                    Verbosity = verbosity,
                    SourceOptions = sourceOptions
                };

                // For file paths, pass as assemblyPath; for packages, pass as PackagePath
                string? assemblyPath = isFilePath ? target : null;

                int result = await AssemblyCommand.ExecuteAsync(assemblyPath, options);
                if (result != 0)
                {
                    failures++;
                }
            }

            if (failures == 0 && !jsonOutput && verbosity != Verbosity.Quiet)
            {
                // Hint about --sourcelink for quick checks when auditing packages
                var firstTarget = targets[0];
                bool firstIsFile = firstTarget.Contains('/') || firstTarget.Contains('\\') ||
                                   firstTarget.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                                   firstTarget.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);
                if (!firstIsFile)
                {
                    Hints.WriteHint($"dotnet-inspect package {firstTarget} --sourcelink   # quick SourceLink check (no verification)");
                }
            }

            return failures > 0 ? 1 : 0;
        });

        return auditCommand;
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

        // New tiered flags
        var metadataOption = new Option<bool>("--metadata") { Description = "Show assembly info (PE metadata: name, version, TFM, architecture)" };
        var symbolsOption = new Option<bool>("--symbols") { Description = "Show Build Audit + PDB info (downloads PDB if needed)" };
        var sourcelinkAuditOption = new Option<bool>("--sourcelink-audit") { Description = "Full provenance verification (parallel HTTP HEAD on all source files)" };
        // Deprecated flags
        var auditOption = new Option<bool>("--audit") { Description = "[Deprecated: use --sourcelink-audit] Full provenance verification" };
        var sourcelinkOption = new Option<bool>("--sourcelink") { Description = "[Deprecated: use --symbols] Show SourceLink presence and URL" };
        var referencesOption = new Option<bool>("--references") { Description = "Show assembly references" };
        var transitiveOption = new Option<bool>("--transitive") { Description = "Show transitive assembly references (full dependency tree)" };
        var asmPackageOption = new Option<string?>("--package") { Description = "[Deprecated: use 'package X --metadata'] Extract from package" };
        var asmPlatformOption = new Option<string?>("--platform") { Description = "Inspect platform assembly (e.g., System.Text.Json)" };
        var asmFrameworkOption = new Option<string?>("--framework") { Description = "Platform framework (runtime, aspnetcore). Use @version for specific version" };
        var asmTfmOption = new Option<string?>("--tfm") { Description = "Select assembly by TFM (e.g., net8.0, or 'all' for every TFM)" };

        assemblyCommand.Arguments.Add(assemblyPathArg);
        assemblyCommand.Options.Add(metadataOption);
        assemblyCommand.Options.Add(symbolsOption);
        assemblyCommand.Options.Add(sourcelinkAuditOption);
        assemblyCommand.Options.Add(auditOption);
        assemblyCommand.Options.Add(sourcelinkOption);
        assemblyCommand.Options.Add(referencesOption);
        assemblyCommand.Options.Add(transitiveOption);
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
            var packagePath = parseResult.GetValue(asmPackageOption);

            // Emit deprecation warning for --package
            if (!string.IsNullOrEmpty(packagePath))
            {
                Console.Error.WriteLine("Warning: 'assembly --package X' is deprecated. Use 'package X --metadata' instead.");
            }

            // New tiered flags
            bool showMetadata = parseResult.GetValue(metadataOption);
            bool showSymbols = parseResult.GetValue(symbolsOption);
            bool runSourcelinkAudit = parseResult.GetValue(sourcelinkAuditOption);

            // Deprecated flag mapping
            bool runAudit = parseResult.GetValue(auditOption);
            bool showSourcelink = parseResult.GetValue(sourcelinkOption);
            if (runAudit)
            {
                Console.Error.WriteLine("Warning: --audit is deprecated. Use --sourcelink-audit instead.");
                runSourcelinkAudit = true;
            }
            if (showSourcelink)
            {
                Console.Error.WriteLine("Warning: --sourcelink is deprecated. Use --symbols instead.");
                showSymbols = true;
            }

            bool showReferences = parseResult.GetValue(referencesOption);
            bool showTransitive = parseResult.GetValue(transitiveOption);

            var options = new AssemblyOptions
            {
                IncludeMetadata = showMetadata,
                IncludeSymbols = showSymbols,
                IncludeSourcelinkAudit = runSourcelinkAudit,
                IncludeReferences = showReferences,
                TransitiveReferences = showTransitive,
                PackagePath = packagePath,
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
        Option<string?> includeSectionsOption,
        Option<string?> excludeSectionsOption,
        Option<string[]> sourceOption,
        Option<string[]> addSourceOption,
        Option<string?> nugetConfigOption)
    {
        var apiCommand = new Command("api", "Extract public API surface");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Package and type name. When no --package/--assembly/--platform is given, first arg is the package.",
            Arity = ArgumentArity.ZeroOrMore
        };

        var apiPackageOption = new Option<string?>("--package") { Description = "Extract from package (file, name, or name@version)" };
        var apiAssemblyOption = new Option<string?>("--assembly") { Description = "Assembly path (local file, or relative path within package)" };
        var apiPlatformOption = new Option<string?>("--platform") { Description = "Extract from platform assembly (e.g., System.Text.Json)" };
        var apiFrameworkOption = new Option<string?>("--framework") { Description = "Platform framework (runtime, aspnetcore, netstandard). Use @version for specific version" };
        var apiTfmOption = new Option<string?>("--tfm") { Description = "Select assembly by TFM (e.g., net8.0)" };
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
        var treeOption = new Option<bool>("--tree") { Description = "Tree view of type shape" };
        var unsafeOption = new Option<bool>("--unsafe") { Description = "Filter to methods with unsafe signatures (pointers)" };
        var ctorOption = new Option<bool>("--ctor") { Description = "Show constructors only (shorthand for -m .ctor)" };

        apiCommand.Arguments.Add(argsArg);
        apiCommand.Options.Add(apiPackageOption);
        apiCommand.Options.Add(apiAssemblyOption);
        apiCommand.Options.Add(apiPlatformOption);
        apiCommand.Options.Add(apiFrameworkOption);
        apiCommand.Options.Add(apiTfmOption);
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
        apiCommand.Options.Add(treeOption);
        apiCommand.Options.Add(unsafeOption);
        apiCommand.Options.Add(includeSectionsOption);
        apiCommand.Options.Add(excludeSectionsOption);
        apiCommand.Options.Add(markoutOption);
        apiCommand.Options.Add(verboseOption);
        apiCommand.Options.Add(verbosityOption);
        apiCommand.Options.Add(sourceOption);
        apiCommand.Options.Add(addSourceOption);
        apiCommand.Options.Add(nugetConfigOption);

        apiCommand.SetAction(async (parseResult, ct) =>
        {
            var args = parseResult.GetValue(argsArg) ?? [];
            var explicitPackage = parseResult.GetValue(apiPackageOption);
            var explicitAssembly = parseResult.GetValue(apiAssemblyOption);
            var explicitPlatform = parseResult.GetValue(apiPlatformOption);
            bool hasExplicitSource = explicitPackage != null || explicitAssembly != null || explicitPlatform != null;

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
                // First positional is the package
                if (args.Length >= 1) packagePath = args[0];
                if (args.Length >= 2) typeName = args[1];
                if (args.Length >= 3) positionalMembers.AddRange(args[2..]);
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

            HashSet<string>? memberFilter = null;
            if (ctorOnly)
            {
                memberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ctor" };
            }
            else if (allMembers.Length > 0)
            {
                memberFilter = new HashSet<string>(allMembers, StringComparer.OrdinalIgnoreCase);
            }

            var includeSections = ParseSectionList(parseResult.GetValue(includeSectionsOption));
            // Bare -s with no value means "header only" (empty set excludes all sections)
            if (includeSections == null && parseResult.GetResult(includeSectionsOption) != null)
                includeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var options = new ApiOptions
            {
                PackagePath = packagePath,
                AssemblyPath = explicitAssembly,
                PlatformAssembly = explicitPlatform,
                PlatformFramework = parseResult.GetValue(apiFrameworkOption),
                Tfm = parseResult.GetValue(apiTfmOption),
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
                TreeOutput = parseResult.GetValue(treeOption),
                UnsafeOnly = parseResult.GetValue(unsafeOption),
                CtorOnly = ctorOnly,
                IncludeSections = includeSections,
                ExcludeSections = ParseSectionList(parseResult.GetValue(excludeSectionsOption)),
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

    public static HashSet<string>? ParseSectionList(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        var v = value.TrimStart(':');
        var sections = new HashSet<string>();
        foreach (var part in v.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                sections.Add(trimmed);
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
