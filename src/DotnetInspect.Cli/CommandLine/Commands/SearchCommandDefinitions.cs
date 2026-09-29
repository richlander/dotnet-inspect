using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using QuerySpace.Rows;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>
/// Defines the find, implements, extensions, and depends commands.
/// </summary>
public static class SearchCommandDefinitions
{
    /// <summary>
    /// System.CommandLine binds an unrecognized option-like token to an empty positional slot, so
    /// <c>find -S</c> searched for a type named "-S" and exited 0 with no match. No type or member
    /// name starts with '-', so such a pattern is reported the way a token after the pattern already
    /// is -- unless it follows the <c>--</c> terminator, which makes any token a literal pattern.
    /// </summary>
    private static string? OptionLikePattern(
        ParseResult parseResult,
        Argument<string?> patternArg)
    {
        if (parseResult.GetResult(patternArg) is not { Tokens: [.., var pattern] }
            || !pattern.Value.StartsWith('-'))
        {
            return null;
        }

        foreach (Token token in parseResult.Tokens)
        {
            if (token.Type == TokenType.DoubleDash)
                return null;
            if (ReferenceEquals(token, pattern))
                return pattern.Value;
        }

        return pattern.Value;
    }

    public static Command CreateFindCommand(SharedOptions opts)
    {
        var findCommand = new Command(
            FindCommand.Name,
            "Search API type or member symbols across packages and libraries");

        var patternArg = new Argument<string?>("pattern")
        {
            Description = "API type or member name/glob. Does not search package IDs. Comma-separated for multiple.",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Search in package(s) (name or name@version). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var platformOption = CommandLineHelpers.CreatePlatformSearchOption();
        var platformLibraryOption = CommandLineHelpers.CreatePlatformLibrarySearchOption();
        var ecosystemOption = new Option<string[]>("--ecosystem")
        {
            Description =
                "Register exactly the named canonical ecosystem(s) in caller order. Can repeat.",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var extensionsOption = new Option<bool>("--extensions")
        {
            Description =
                "Search current Microsoft.Extensions.* packages outside the shared frameworks"
        };
        var aspnetcoreOption = new Option<bool>("--aspnetcore")
        {
            Description =
                "Search current Microsoft.AspNetCore.* packages outside the shared frameworks"
        };
        var projectOption = new Option<string[]>("--project")
        {
            Description = "Search project dependencies via project.assets.json. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var binOption = new Option<string[]>("--bin")
        {
            Description = "Search all DLLs in output directory(s). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library or target framework by TFM (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete types" };
        var membersOption = new Option<bool>("--members") { Description = "Search member names instead of type names (auto-enabled when the pattern starts with '.', e.g. .Serialize)" };
        var literalOption = new Option<string?>("--literal")
        {
            Hidden = true,
            Arity = ArgumentArity.ExactlyOne,
        };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var packagePrefixOption = new Option<string?>("--package-prefix")
        {
            Description =
                $"With a type or member pattern, search up to "
                + $"{ScopeConstants.PackagePrefixExpansionLimit} matching package IDs"
        };
        var typeFilterOption = new Option<string?>("--type")
        {
            Description = "Filter API types by glob (for example --type *Json*)"
        };
        findCommand.Arguments.Add(patternArg);
        findCommand.Options.Add(packageOption);
        findCommand.Options.Add(assemblyOption);
        findCommand.Options.Add(platformOption);
        findCommand.Options.Add(platformLibraryOption);
        findCommand.Options.Add(ecosystemOption);
        findCommand.Options.Add(extensionsOption);
        findCommand.Options.Add(aspnetcoreOption);
        findCommand.Options.Add(projectOption);
        findCommand.Options.Add(binOption);
        findCommand.Options.Add(tfmOption);
        findCommand.Options.Add(allOption);
        findCommand.Options.Add(membersOption);
        findCommand.Options.Add(literalOption);
        findCommand.Options.Add(typeFilterOption);
        findCommand.Options.Add(opts.Json);
        findCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(findCommand);
        findCommand.Options.Add(packagePrefixOption);
        findCommand.Options.Add(opts.Discover);
        findCommand.Options.Add(opts.Tree);
        findCommand.Options.Add(opts.Columns);
        findCommand.Options.Add(opts.Fields);
        opts.AddCountOptionTo(findCommand);
        opts.AddOutputOptionsTo(
            findCommand,
            validateLegacyRowWindow: static _ => false);
        opts.AddNuGetOptionsTo(findCommand);

        var commandArgs = new FindOptionsParser.FindCommandArgs(
            patternArg, packageOption, assemblyOption, platformOption, platformLibraryOption,
            ecosystemOption, extensionsOption, aspnetcoreOption, projectOption, binOption, tfmOption, allOption,
            typeFilterOption, compactOption, opts.NoHeaders, packagePrefixOption, membersOption);

        findCommand.SetAction(async (parseResult, ct) =>
        {
            if (OptionLikePattern(parseResult, patternArg) is { } optionLike)
            {
                CommandError.Write($"Unrecognized command or argument '{optionLike}'.");
                return 1;
            }

            if (parseResult.GetResult(literalOption)
                is { Implicit: false })
            {
                CommandError.Write(
                    "'find --literal' is no longer valid because Find returns "
                    + "Type results. Use 'package query <ID-or-prefix*> "
                    + "--where \"library-literal=TEXT\" --tfm TFM'. Package Query selects "
                    + "the latest eligible listed version for an exact ID, so "
                    + "this is not an equivalent replacement for an older "
                    + "ID@VERSION query.");
                return 1;
            }

            var result = await FindOptionsParser.ParseAsync(parseResult, opts, commandArgs);

            switch (result)
            {
                case FindOptionsParser.ShowHelpWithTips:
                    return TipWriter.MissingArgumentWithTips(findCommand,
                        "Search pattern required.",
                        "find Chat*                                # implicit platform scope",
                        "find Chat* --platform                     # explicit platform scope",
                        "find Chat* --extensions                   # Microsoft.Extensions packages",
                        "find Chat* --aspnetcore                   # ASP.NET Core packages",
                        "find Chat* --package Newtonsoft.Json       # specific package",
                        "find Chat* --platform --extensions         # combine scopes",
                        "package query 'Newtonsoft.*'               # discover package IDs");

                case FindOptionsParser.Success success:
                    var execution = await FindCommand.ExecuteWithResultAsync(
                        success.Options,
                        ct);

                    if (execution.ExitCode == 0
                        && !success.Options.FormatExplicitlySet
                        && !success.Options.IsRawOutput)
                    {
                        var tips = FindOptionsParser.BuildTips(
                            success.Options,
                            success.Options.Pattern,
                            execution.RowCount);
                        Hints.WriteTips(success.TipLevel, [.. tips]);
                    }

                    return execution.ExitCode;

                default:
                    return 1;
            }
        });

        CliRowSelectionCommandRegistry.Register(
            findCommand,
            new(
                opts.Limit,
                opts.Rows,
                top: null,
                orderBy: null,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            isActive: static _ => true,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        return findCommand;
    }

    public static Command CreateExtensionsCommand(SharedOptions opts)
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
            AllowMultipleArgumentsPerToken = false
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Search in library file(s). Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var platformOption = CommandLineHelpers.CreatePlatformSearchOption();
        var platformLibraryOption = CommandLineHelpers.CreatePlatformLibrarySearchOption();
        var extensionsOption = new Option<bool>("--extensions")
        {
            Description =
                "Search current Microsoft.Extensions.* packages outside the shared frameworks"
        };
        var aspnetcoreOption = new Option<bool>("--aspnetcore")
        {
            Description =
                "Search current Microsoft.AspNetCore.* packages outside the shared frameworks"
        };
        var projectOption = new Option<string[]>("--project")
        {
            Description = "Search project dependencies via project.assets.json. Can repeat.",
            AllowMultipleArgumentsPerToken = false
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
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete members" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json)" };
        var packagePrefixOption = new Option<string?>("--package-prefix")
        {
            Description = $"Search up to {ScopeConstants.PackagePrefixExpansionLimit} packages matching a NuGet ID prefix (e.g., Azure.AI, AWSSDK)"
        };
        var typeFilterOption = new Option<string?>("-t")
        {
            Description = "Filter declaring types by glob or name"
        };
        typeFilterOption.Aliases.Add("--type");

        extCommand.Arguments.Add(targetTypeArg);
        extCommand.Options.Add(packageOption);
        extCommand.Options.Add(assemblyOption);
        extCommand.Options.Add(platformOption);
        extCommand.Options.Add(platformLibraryOption);
        extCommand.Options.Add(extensionsOption);
        extCommand.Options.Add(aspnetcoreOption);
        extCommand.Options.Add(projectOption);
        extCommand.Options.Add(reachableOption);
        extCommand.Options.Add(depthOption);
        extCommand.Options.Add(tfmOption);
        extCommand.Options.Add(allOption);
        extCommand.Options.Add(typeFilterOption);
        extCommand.Options.Add(opts.Json);
        extCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(extCommand);
        extCommand.Options.Add(packagePrefixOption);
        extCommand.Options.Add(opts.Columns);
        extCommand.Options.Add(opts.Fields);
        opts.AddCountOptionTo(extCommand);
        opts.AddOutputOptionsTo(
            extCommand,
            validateLegacyRowWindow: static _ => false);
        opts.AddNuGetOptionsTo(extCommand);

        extCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);

            if (string.IsNullOrEmpty(targetType))
            {
                return TipWriter.MissingArgumentWithTips(extCommand,
                    "Type name required.",
                    "extensions HttpClient                     # implicit platform scope",
                    "extensions HttpClient --platform          # explicit platform scope",
                    "extensions HttpClient --extensions         # Microsoft.Extensions packages",
                    "extensions HttpClient --aspnetcore         # ASP.NET Core packages",
                    "extensions HttpClient --package Foo        # specific package",
                    "extensions HttpClient --platform --extensions  # combine scopes");
            }

            var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
            var packagePrefix = parseResult.GetValue(packagePrefixOption);
            var intent = SearchSourceAdapter.Declare(
                parseResult, packageOption, assemblyOption, projectOption, platformOption,
                platformLibraryOption, extensionsOption, aspnetcoreOption,
                prefixOption: packagePrefixOption);
            var (selection, sources) = await SearchSourceAdapter.BindAsync(
                intent, HttpClientFactory.Shared, parseResult.GetValue(opts.Verbose), sourceOptions);
            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    parseResult,
                    "Extensions",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }

            var options = new ExtensionsOptions
            {
                SourceSelection = selection,
                TargetType = targetType,
                Packages = [.. sources.Packages],
                Assemblies = [.. sources.Assemblies],
                PlatformAssemblies = [.. sources.PlatformAssemblies],
                PlatformFrameworks = [.. sources.PlatformFrameworks],
                Projects = [.. sources.Projects],
                Reachable = parseResult.GetValue(reachableOption),
                Depth = parseResult.GetValue(depthOption),
                Tfm = parseResult.GetValue(tfmOption),
                IncludeAll = parseResult.GetValue(allOption),
                TypeFilter = parseResult.GetValue(typeFilterOption),
                Limit = null,
                Rows = rowSelection is null ? opts.ParseRows(parseResult) : null,
                RowSelection = rowSelection,
                Count = parseResult.GetValue(opts.Count),
                JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
                CompactJson = parseResult.GetValue(compactOption),
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                Verbosity = opts.ParseVerbosity(parseResult),
                PackagePrefix = packagePrefix,
                SourceOptions = sourceOptions
            };

            return await ExtensionsCommand.ExecuteAsync(options, ct);
        });

        CliRowSelectionCommandRegistry.Register(
            extCommand,
            new(
                opts.Limit,
                opts.Rows,
                top: null,
                orderBy: null,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            isActive: static _ => true,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return extCommand;
    }

    public static Command CreateDependsCommand(SharedOptions opts)
    {
        var dependsCommand = new Command(
            "depends",
            "Inspect dependency graphs and evidence for a type scope or explicit package, nuspec, library, project, or package-prefix roots");

        var targetTypeArg = new Argument<string?>("type")
        {
            Description = "Type name to walk dependencies for (e.g., IFloatingPointIeee754, Int128)",
            Arity = ArgumentArity.ZeroOrOne
        };

        var packageOption = new Option<string[]>("--package")
        {
            Description = "Type mode: package-ID scope. Asset mode: package root ID, ID@VERSION, or local .nupkg (repeatable).",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var assemblyOption = new Option<string[]>("--library")
        {
            Description = "Type mode: library search scope. Asset mode: library root path or name (repeatable).",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var nuspecOption = new Option<string[]>("--nuspec")
        {
            Description = "Asset-mode direct nuspec root (repeatable).",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var platformOption = CommandLineHelpers.CreatePlatformSearchOption();
        var platformLibraryOption = CommandLineHelpers.CreatePlatformLibrarySearchOption();
        var extensionsOption = new Option<bool>("--extensions")
        {
            Description =
                "Search current Microsoft.Extensions.* packages outside the shared frameworks"
        };
        var aspnetcoreOption = new Option<bool>("--aspnetcore")
        {
            Description =
                "Search current Microsoft.AspNetCore.* packages outside the shared frameworks"
        };
        var projectOption = new Option<string[]>("--project")
        {
            Description = "Type mode: restored-project search scope. Asset mode: csproj, directory, or project.assets.json root (repeatable).",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var packagePrefixOption = new Option<string?>("--package-prefix")
        {
            Description =
                $"Exclusive bounded NuGet Gallery package root set ({DependencyEvidenceAcquisition.PackageProfileDefaultLimit} packages by default)"
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var pruningPlatformFamilyOption =
            new Option<string?>("--platform-family")
            {
                Description =
                    "Asset-mode Pruning comparison family: runtime or aspnetcore (default: runtime)"
            };
        var previewOption = new Option<bool>("--preview")
        {
            Description =
                "Allow latest remote asset-mode --package resolution to select a prerelease version"
        };
        var maxPackagesOption = new Option<int?>("--max-packages")
        {
            Description =
                $"Bound --package-prefix discovery (1 to {DependencyEvidenceAcquisition.PackageProfileMaximumLimit}, default {DependencyEvidenceAcquisition.PackageProfileDefaultLimit})"
        };
        var depthOption = new Option<int?>("--depth")
        {
            Description =
                "Maximum dependency depth; 1 includes direct relationships only"
        };
        var compactOption = new Option<bool>("--compact") { Description = "Minified JSON (use with --json or --envelope)" };
        var shareOption = WorkspaceShareOption.Create(
            "Emit a resolved NuGet package dependency view as a canonical Workspace packet or complete URL");
#if DEBUG
        var evidenceEnvelopeOption =
            new Option<string?>("--evidence-envelope")
            {
                Description =
                    "Write the complete enriched dependency envelope to a JSON sidecar",
                Arity = ArgumentArity.ExactlyOne,
            };
        var outOption = SharedOptions.CreateOutputPathOption();
#endif

        depthOption.Validators.Add(result =>
        {
            if (result.GetValue(depthOption) is <= 0)
            {
                result.AddError("--depth must be a positive integer.");
            }
        });

        dependsCommand.Arguments.Add(targetTypeArg);
        dependsCommand.Options.Add(packageOption);
        dependsCommand.Options.Add(assemblyOption);
        dependsCommand.Options.Add(nuspecOption);
        dependsCommand.Options.Add(platformOption);
        dependsCommand.Options.Add(platformLibraryOption);
        dependsCommand.Options.Add(extensionsOption);
        dependsCommand.Options.Add(aspnetcoreOption);
        dependsCommand.Options.Add(projectOption);
        dependsCommand.Options.Add(packagePrefixOption);
        dependsCommand.Options.Add(tfmOption);
        dependsCommand.Options.Add(pruningPlatformFamilyOption);
        dependsCommand.Options.Add(previewOption);
        dependsCommand.Options.Add(maxPackagesOption);
        dependsCommand.Options.Add(depthOption);
        dependsCommand.Options.Add(shareOption);
#if DEBUG
        dependsCommand.Options.Add(evidenceEnvelopeOption);
        dependsCommand.Options.Add(outOption);
        SharedOptions.AddOutputPathValidator(
            dependsCommand,
            outOption);
#endif
        dependsCommand.Options.Add(opts.Json);
        dependsCommand.Options.Add(compactOption);
        dependsCommand.Options.Add(opts.Mermaid);
        dependsCommand.Options.Add(opts.Markdown);
        dependsCommand.Options.Add(opts.PlainText);
        opts.AddTableOptionsTo(dependsCommand);
        opts.AddSectionOptionsTo(dependsCommand);
        dependsCommand.Options.Add(opts.Effective);
        opts.AddCountOptionTo(dependsCommand);
        opts.AddOutputOptionsTo(dependsCommand);
        dependsCommand.Options.Add(opts.RowWhere);
        dependsCommand.Options.Add(opts.RowOrderBy);
        dependsCommand.Options.Add(opts.PerformanceTriageTop);
        opts.AddNuGetOptionsTo(dependsCommand);
        opts.AddEnvelopeOptionTo(
            dependsCommand,
            opts.Discover, opts.Schema, opts.Effective, opts.Select,
            opts.Verbosity, opts.Count);

        dependsCommand.Validators.Add(result =>
        {
            if (result.GetValue(opts.Tree)
                && result.GetValue(opts.Mermaid))
            {
                result.AddError(
                    "--tree and --mermaid are alternate graph renderings; choose one.");
            }
            if (result.GetValue(opts.Tree)
                && (result.GetValue(opts.Json)
                    || result.GetValue(opts.Markdown)
                    || result.GetValue(opts.PlainText)
                    || result.GetValue(opts.Table)
                    || result.GetValue(opts.Tsv)
                    || result.GetValue(opts.Jsonl)
                    || result.GetResult(opts.Verbosity)
                        is { Implicit: false }))
            {
                result.AddError(
                    "--tree is a standalone graph rendering and cannot combine with another output format.");
            }
            bool typeMode =
                !string.IsNullOrEmpty(result.GetValue(targetTypeArg));
#if DEBUG
            bool evidenceEnvelope =
                result.GetResult(evidenceEnvelopeOption)
                    is { Implicit: false };
            if (evidenceEnvelope && typeMode)
            {
                result.AddError(
                    "--evidence-envelope is supported only by asset-mode depends.");
            }
            if (evidenceEnvelope
                && (result.GetResult(opts.Discover)
                        is { Implicit: false }
                    || result.GetValue(opts.Schema)
                    || result.GetValue(opts.Effective)))
            {
                result.AddError(
                    "--evidence-envelope requires an asset dependency inspection, not discovery or schema output.");
            }
            if (result.GetResult(outOption) is { Implicit: false }
                && !evidenceEnvelope)
            {
                result.AddError(
                    "--out is supported by asset-mode depends only with --evidence-envelope.");
            }
#else
            const bool evidenceEnvelope = false;
#endif
            if (result.GetValue(opts.Envelope)
                && !typeMode
                && !evidenceEnvelope)
            {
                result.AddError(
                    "--envelope currently requires a positional type in depends.");
            }
            if (evidenceEnvelope
                && !typeMode
                && result.GetValue(opts.Envelope))
            {
                RejectAssetEnvelopeRowOption(opts.Rows, "--rows");
                RejectAssetEnvelopeRowOption(opts.Limit, "-n");
            }
            bool effective = result.GetValue(opts.Effective);
            bool discovery =
                result.GetResult(opts.Discover)
                    is { Implicit: false };
            if (effective && !discovery)
            {
                result.AddError("--effective requires -D/--discover.");
            }
            if (typeMode)
            {
                if (result.GetResult(opts.Limit)
                    is { Implicit: false }
                    && result.GetValue(opts.Limit) is int count
                    && count <= 0)
                {
                    result.AddError(
                        "-n requires a positive whole number for type dependency rows.");
                }
                if (effective)
                {
                    result.AddError(
                        "--effective discovery is available only without a positional type.");
                }
                RejectTypeModeOption(nuspecOption, "--nuspec");
                RejectTypeModeOption(
                    packagePrefixOption,
                    "--package-prefix");
                RejectTypeModeOption(maxPackagesOption, "--max-packages");
                RejectTypeModeOption(previewOption, "--preview");
            }
            else
            {
                RejectAssetModeOption(platformOption, "--platform");
                RejectAssetModeOption(
                    platformLibraryOption,
                    "--platform-library");
                RejectAssetModeOption(extensionsOption, "--extensions");
                RejectAssetModeOption(aspnetcoreOption, "--aspnetcore");

                bool hasPrefix =
                    result.GetResult(packagePrefixOption)
                        is { Implicit: false };
                bool hasExplicitRoots =
                    (result.GetValue(packageOption)?.Length ?? 0) > 0
                    || (result.GetValue(nuspecOption)?.Length ?? 0) > 0
                    || (result.GetValue(assemblyOption)?.Length ?? 0) > 0
                    || (result.GetValue(projectOption)?.Length ?? 0) > 0;
                if (hasPrefix && hasExplicitRoots)
                {
                    result.AddError(
                        "--package-prefix cannot be combined with explicit --package, --nuspec, --library, or --project roots.");
                }
            }

            return;

            void RejectTypeModeOption(Option option, string name)
            {
                if (result.GetResult(option) is { Implicit: false })
                    result.AddError($"{name} is available only without a positional type.");
            }

            void RejectAssetModeOption(Option option, string name)
            {
                if (result.GetResult(option) is { Implicit: false })
                    result.AddError($"{name} is available only with a positional type.");
            }

            void RejectAssetEnvelopeRowOption(
                Option option,
                string name)
            {
                if (result.GetResult(option) is { Implicit: false })
                {
                    result.AddError(
                        $"--envelope cannot be combined with {name} for asset dependency inspection.");
                }
            }
        });

        dependsCommand.SetAction(async (parseResult, ct) =>
        {
            var targetType = parseResult.GetValue(targetTypeArg);
            var packages = parseResult.GetValue(packageOption) ?? [];
            var assemblies = parseResult.GetValue(assemblyOption) ?? [];
            var projects = parseResult.GetValue(projectOption) ?? [];
            OutputFormat outputFormat = opts.ResolveFormat(parseResult);
            RowWindow? rows = ParseDependsRows(parseResult, opts);
            if (!CliRowSelectionCommandRegistry
                    .TryGetPreparedSemanticIntent(
                        parseResult,
                        "Dependency",
                        out RowSelectionIntent<string>? rowSelection,
                        out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }
            RowSelectionIntent<string>? dependencyRowSelection =
                DependencyQueryOptions.AppendLegacyRows(
                    parseResult,
                    opts,
                    rowSelection,
                    out int? legacyHierarchyWindowStageIndex);
            WorkspaceShareFormat? shareFormat =
                WorkspaceShareOption.Parse(parseResult, shareOption);
#if DEBUG
            string? evidenceEnvelopePath = null;
            string? outputPath = null;
            if (parseResult.GetResult(evidenceEnvelopeOption)
                is { Implicit: false })
            {
                string requestedEvidencePath =
                    parseResult.GetValue(evidenceEnvelopeOption)!;
                if (!EvidenceEnvelopeOutput.TryResolvePath(
                        requestedEvidencePath,
                        out evidenceEnvelopePath,
                        out string? pathError))
                {
                    CommandError.Write(pathError!);
                    return 1;
                }

                string? requestedOutputPath =
                    parseResult.GetValue(outOption);
                if (requestedOutputPath is not null)
                {
                    try
                    {
                        outputPath = Path.GetFullPath(requestedOutputPath);
                    }
                    catch (Exception exception)
                        when (exception is ArgumentException
                            or NotSupportedException
                            or PathTooLongException)
                    {
                        CommandError.Write("--out requires a valid file path.");
                        return 1;
                    }

                    if (EvidenceEnvelopeOutput.PathsMayIdentifySameFile(
                            evidenceEnvelopePath,
                            outputPath))
                    {
                        CommandError.Write(
                            "--out and --evidence-envelope must name distinct files.");
                        return 1;
                    }
                }
            }
#else
            const string? evidenceEnvelopePath = null;
            const string? outputPath = null;
#endif
            bool hasNonPackageShareInput =
                !string.IsNullOrEmpty(targetType)
                || packages.Length != 1
                || (parseResult.GetValue(nuspecOption)?.Length ?? 0) > 0
                || assemblies.Length > 0
                || projects.Length > 0
                || parseResult.GetValue(packagePrefixOption) is not null
                || parseResult.GetValue(platformOption)
                || (parseResult.GetValue(platformLibraryOption)?.Length ?? 0) > 0
                || parseResult.GetValue(extensionsOption)
                || parseResult.GetValue(aspnetcoreOption);
            hasNonPackageShareInput =
                hasNonPackageShareInput
                || parseResult.GetValue(pruningPlatformFamilyOption) is not null;
            bool hasValidTypeShareInput =
                !string.IsNullOrEmpty(targetType)
                && packages.Length == 1
                && (parseResult.GetValue(nuspecOption)?.Length ?? 0) == 0
                && assemblies.Length == 0
                && projects.Length == 0
                && parseResult.GetValue(packagePrefixOption) is null
                && (parseResult.GetValue(platformLibraryOption)?.Length ?? 0) == 0
                && !parseResult.GetValue(platformOption)
                && !parseResult.GetValue(extensionsOption)
                && !parseResult.GetValue(aspnetcoreOption);
            bool invalidShareInput =
                string.IsNullOrEmpty(targetType)
                    ? hasNonPackageShareInput
                    : !hasValidTypeShareInput;
            if (shareFormat is not null && invalidShareInput)
            {
                CommandError.Write(
                    string.IsNullOrEmpty(targetType)
                        ? "--share requires exactly one --package input and "
                            + "cannot be used with type, library, project, or platform dependency modes."
                        : "--share in type mode requires exactly one --package "
                            + "input and cannot be used with another dependency source.");
                return 1;
            }

            // Mode detection: no type arg → library or package dependency mode
            if (string.IsNullOrEmpty(targetType))
            {
                DependsAssetRoot[] assetRoots = ParseDependsAssetRoots(
                    parseResult,
                    packageOption,
                    nuspecOption,
                    assemblyOption,
                    projectOption);
                if ((!opts.IsDiscoveryMode(parseResult)
                        || parseResult.GetValue(opts.Effective))
                    && assetRoots.Length == 0
                    && parseResult.GetValue(packagePrefixOption) is null)
                {
                    return TipWriter.MissingArgumentWithTips(
                        dependsCommand,
                        "Asset-mode depends requires at least one root.",
                        "depends --project ./src/App/App.csproj",
                        "depends --package System.Text.Json@10.0.0",
                        "depends --nuspec ./artifacts/package.nuspec",
                        "depends --library System.Text.Json",
                        "depends --package-prefix Microsoft.Extensions",
                        "depends --help");
                }
                if (!DependencyQueryOptions.TryResolve(
                        DependencyQueryRouteKind.AssetHierarchy,
                        parseResult.GetValue(opts.RowWhere) ?? [],
                        parseResult.GetValue(opts.RowOrderBy),
                        dependencyRowSelection,
                        parseResult.GetValue(depthOption),
                        out DependencyQueryPlan queryPlan,
                        out OptionError queryError))
                {
                    CommandError.Write(queryError);
                    return 1;
                }
                var commonOptions = new DependsOptions
                {
                    AssetRoots = assetRoots,
                    PackagePrefix =
                        parseResult.GetValue(packagePrefixOption),
                    IncludePrerelease =
                        parseResult.GetValue(previewOption),
                    MaxPackages =
                        parseResult.GetValue(maxPackagesOption),
                    Depth = queryPlan.MaximumDepth,
                    QueryPlan = queryPlan,
                    LegacyHierarchyWindowStageIndex =
                        legacyHierarchyWindowStageIndex,
                    Tfm = parseResult.GetValue(tfmOption),
                    PruningPlatformFamily =
                        parseResult.GetValue(pruningPlatformFamilyOption),
                    Verbosity = opts.ParseVerbosity(parseResult),
                    ShareFormat = shareFormat,
                    PackageName = shareFormat is not null
                        ? packages[0]
                        : null,
                    Format = outputFormat,
                    JsonOutput = outputFormat == OutputFormat.Json,
                    EnvelopeOutput =
                        parseResult.GetValue(opts.Envelope),
                    EvidenceEnvelopePath = evidenceEnvelopePath,
                    OutputPath = outputPath,
                    CompactJson = parseResult.GetValue(compactOption),
                    MermaidOutput = outputFormat == OutputFormat.Mermaid,
                    EmbeddedMermaid = opts.IsEmbeddedMermaid(parseResult),
                    Tree = parseResult.GetValue(opts.Tree),
                    Rows = rows,
                    Count = parseResult.GetValue(opts.Count),
                    Tabular = opts.ResolveTabular(parseResult),
                    Tsv = opts.ResolveTsv(parseResult),
                    Jsonl = opts.ResolveJsonl(parseResult),
                    NoHeader = parseResult.GetValue(opts.NoHeaders),
                    Discover = opts.ParseDiscover(parseResult),
                    Effective = parseResult.GetValue(opts.Effective),
                    Schema = opts.ParseSchema(parseResult),
                    Select = opts.ParseSelect(parseResult),
                    SelectDefault = opts.ParseSelectDefault(parseResult),
                    Columns = opts.ParseColumns(parseResult),
                    Fields = opts.ParseFields(parseResult),
                    Verbose = parseResult.GetValue(opts.Verbose),
                    SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
                    LineWindowExplicitlySet =
                        parseResult.GetResult(opts.Limit) is { Implicit: false }
                        || parseResult.GetResult(opts.Head) is { Implicit: false }
                        || parseResult.GetResult(opts.Tail) is { Implicit: false },
                    OutputFormatExplicitlySet =
                        opts.IsFormatFlagExplicitlySet(parseResult),
                };

                return await DependsCommand.ExecuteAssetDependsAsync(
                    commonOptions,
                    ct);
            }

            var typePlanOptions = new DependsOptions
            {
                Depth = parseResult.GetValue(depthOption),
                Verbosity = opts.ParseVerbosity(parseResult),
                Discover = opts.ParseDiscover(parseResult),
                Schema = opts.ParseSchema(parseResult),
                Select = opts.ParseSelect(parseResult),
                SelectDefault = opts.ParseSelectDefault(parseResult),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
            };
            if (parseResult.GetValue(pruningPlatformFamilyOption) is not null)
            {
                CommandError.Write(
                    "--platform-family is supported only by asset-mode depends with the Pruning section.");
                return 1;
            }
            if (!DependsCommand.ValidateTypeDepthSelectionBeforeAcquisition(
                    typePlanOptions))
            {
                return 1;
            }
            if (!DependencyQueryOptions.TryResolve(
                    DependencyQueryRouteKind.TypeRelationships,
                    parseResult.GetValue(opts.RowWhere) ?? [],
                    parseResult.GetValue(opts.RowOrderBy),
                    dependencyRowSelection,
                    parseResult.GetValue(depthOption),
                    out DependencyQueryPlan typeQueryPlan,
                    out OptionError typeQueryError))
            {
                CommandError.Write(typeQueryError);
                return 1;
            }

            var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
            var intent = SearchSourceAdapter.Declare(
                parseResult, packageOption, assemblyOption, projectOption, platformOption,
                platformLibraryOption, extensionsOption, aspnetcoreOption);
            var (selection, sources) = await SearchSourceAdapter.BindAsync(
                intent, HttpClientFactory.Shared, parseResult.GetValue(opts.Verbose), sourceOptions);

            var options = new DependsOptions
            {
                SourceSelection = selection,
                TargetType = targetType,
                Packages = [.. sources.Packages],
                Assemblies = [.. sources.Assemblies],
                PlatformAssemblies = [.. sources.PlatformAssemblies],
                PlatformFrameworks = [.. sources.PlatformFrameworks],
                Projects = [.. sources.Projects],
                Tfm = parseResult.GetValue(tfmOption),
                Depth = typeQueryPlan.MaximumDepth,
                QueryPlan = typeQueryPlan,
                Verbosity = opts.ParseVerbosity(parseResult),
                Format = outputFormat,
                JsonOutput = outputFormat == OutputFormat.Json,
                EnvelopeOutput = parseResult.GetValue(opts.Envelope),
                CompactJson = parseResult.GetValue(compactOption),
                MermaidOutput = outputFormat == OutputFormat.Mermaid,
                EmbeddedMermaid = opts.IsEmbeddedMermaid(parseResult),
                Tree = parseResult.GetValue(opts.Tree),
                Rows = rows,
                TypeDependencyRowQuery =
                    typeQueryPlan.RelationshipRows,
                Count = parseResult.GetValue(opts.Count),
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Discover = opts.ParseDiscover(parseResult),
                Effective = parseResult.GetValue(opts.Effective),
                Schema = opts.ParseSchema(parseResult),
                Select = opts.ParseSelect(parseResult),
                SelectDefault = opts.ParseSelectDefault(parseResult),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                SourceOptions = sourceOptions,
                ShareFormat = shareFormat
            };

            var outcome = await DependsCommand.ExecuteTypeDependsAsync(
                options,
                ct);

            if (outcome.ExitCode == DependsCommand.TypeNotFoundExitCode)
            {
                return outcome.Uncertified
                    ? DependsCommand.UncertifiedScanExitCode
                    : 1;
            }

            return outcome.ExitCode;
        });

        var legacyDependsRows =
            new Option<string?>("--unavailable-depends-semantic-rows")
            {
                Hidden = true,
            };
        var legacyDependsHead =
            new Option<bool>("--unavailable-depends-semantic-head")
            {
                Hidden = true,
            };
        var legacyDependsTail =
            new Option<bool>("--unavailable-depends-semantic-tail")
            {
                Hidden = true,
            };
        CliRowSelectionCommandRegistry.Register(
            dependsCommand,
            new(
                opts.Limit,
                legacyDependsRows,
                opts.PerformanceTriageTop,
                opts.RowOrderBy,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            isActive: result =>
                string.IsNullOrEmpty(
                    result.GetValue(targetTypeArg))
                && !(result.GetResult(opts.Rows)
                        is { Implicit: false }
                    && result.GetResult(opts.Limit)
                        is not { Implicit: false }),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            dependsCommand,
            new(
                opts.Limit,
                legacyDependsRows,
                opts.PerformanceTriageTop,
                opts.RowOrderBy,
                legacyDependsHead,
                legacyDependsTail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            isActive: result =>
                string.IsNullOrEmpty(
                    result.GetValue(targetTypeArg))
                && result.GetResult(opts.Rows)
                    is { Implicit: false }
                && result.GetResult(opts.Limit)
                    is not { Implicit: false },
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            dependsCommand,
            new(
                opts.Limit,
                legacyDependsRows,
                opts.PerformanceTriageTop,
                opts.RowOrderBy,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.All,
            isActive: result =>
                !string.IsNullOrEmpty(
                    result.GetValue(targetTypeArg))
                && !(result.GetResult(opts.Rows)
                        is { Implicit: false }
                    && result.GetResult(opts.Limit)
                        is not { Implicit: false }),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            dependsCommand,
            new(
                opts.Limit,
                legacyDependsRows,
                opts.PerformanceTriageTop,
                opts.RowOrderBy,
                legacyDependsHead,
                legacyDependsTail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.All,
            isActive: result =>
                !string.IsNullOrEmpty(
                    result.GetValue(targetTypeArg))
                && result.GetResult(opts.Rows)
                    is { Implicit: false }
                && result.GetResult(opts.Limit)
                    is not { Implicit: false },
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return dependsCommand;
    }

    private static DependsAssetRoot[] ParseDependsAssetRoots(
        ParseResult parseResult,
        Option<string[]> packageOption,
        Option<string[]> nuspecOption,
        Option<string[]> libraryOption,
        Option<string[]> projectOption)
    {
        var aliases = new Dictionary<string, DependencyInspectionRootKind>(
            StringComparer.Ordinal)
        {
            [packageOption.Name] = DependencyInspectionRootKind.Package,
            [nuspecOption.Name] = DependencyInspectionRootKind.Nuspec,
            [libraryOption.Name] = DependencyInspectionRootKind.Library,
            [projectOption.Name] = DependencyInspectionRootKind.Project,
        };
        var roots = new List<DependsAssetRoot>();
        for (int index = 0; index < parseResult.Tokens.Count; index++)
        {
            Token token = parseResult.Tokens[index];
            if (token.Type != TokenType.Option
                || !aliases.TryGetValue(
                    token.Value,
                    out DependencyInspectionRootKind kind))
            {
                continue;
            }

            if (index + 1 >= parseResult.Tokens.Count
                || parseResult.Tokens[index + 1].Type == TokenType.Option)
            {
                continue;
            }

            roots.Add(
                new DependsAssetRoot(
                    roots.Count + 1,
                    kind,
                    parseResult.Tokens[++index].Value));
        }

        return [.. roots];
    }

    private static RowWindow? ParseDependsRows(
        ParseResult parseResult,
        SharedOptions opts)
    {
        RowWindow? rows = opts.ParseRows(parseResult);
        if (rows is not null)
            return rows;

        if (UsesRenderedLineSelection(parseResult, opts))
            return null;

        if (parseResult.GetResult(opts.Limit) is not { Implicit: false }
            || parseResult.GetValue(opts.Limit) is not int count)
        {
            return null;
        }

        return parseResult.GetValue(opts.Tail)
            ? RowWindow.Tail(count)
            : RowWindow.Head(count);
    }

    private static bool UsesRenderedLineSelection(
        ParseResult parseResult,
        SharedOptions opts) =>
        parseResult.GetResult(opts.Lines) is { Implicit: false }
        || parseResult.GetResult(opts.TailLines) is { Implicit: false };
}
