using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using QuerySpace.Rows;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>
/// Defines the diff and library commands.
/// </summary>
public static class InspectionCommandDefinitions
{
    public static Command CreateDiffCommand(SharedOptions opts)
    {
        var diffCommand = new Command(
            DiffCommand.Name,
            "Compare API surfaces, analysis signals, or implementation evidence between versions");

        var argsArg = new Argument<string[]>("args")
        {
            Description = "Version range and type filter. When no --package/--platform/--library is given, first arg is the package version range.",
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
        var libraryOption = new Option<string?>("--library")
        {
            Description = "Local library path range (e.g., old/Foo.dll..new/Foo.dll)"
        };
        var frameworkOption = new Option<string?>("--framework")
        {
            Description = "Framework for platform diff (runtime, aspnetcore). Default: runtime"
        };
        var tfmOption = new Option<string?>("--tfm") { Description = "Target framework (e.g., net8.0)" };
        var allOption = new Option<bool>("--all") { Description = "Include non-public, hidden, and obsolete members" };
        var implementationOption = new Option<bool>("--implementation")
        {
            Description = "Select body-level C# and IL comparison (equivalent to -S \"Implementation Diff\")",
        };
        var historyOption = new Option<bool>("--history")
        {
            Description = "Inspect one Finding across a package version population; requires one full Type name",
        };
        var atOption = new Option<string[]>("--at")
        {
            Description = "History checkpoint: exact version, #N, first, last, endpoints, midpoint, or all; repeat for sparse evaluation",
            AllowMultipleArgumentsPerToken = false,
        };
        var maxProbesOption = new Option<int?>("--max-probes")
        {
            Description = "History probe cap (minimum 2); selects adaptive bisection unless --sample-percent selects a survey",
        };
        var samplePercentOption = new Option<int?>("--sample-percent")
        {
            Description = "History representative positional sample (1-100 percent), optionally capped by --max-probes",
        };
        var majorVersionsOption = new Option<bool>("--major-versions")
        {
            Description = "History: evaluate one representative per package major; API uses first stable, Analysis uses latest",
        };
        var prereleaseOption = new Option<bool>("--preview")
        {
            Description = "History: include prerelease versions inside the package range",
        };
        prereleaseOption.Aliases.Add("--prerelease");
        var configDirectoryOption = NuGetConfigDirectoryOption.Create();
        var typeFilterOption = new Option<string[]>("-t")
        {
            Description = "Filter to specific type(s)",
            AllowMultipleArgumentsPerToken = false
        };
        typeFilterOption.Aliases.Add("--type");
        var memberFilterOption = new Option<string[]>("-m")
        {
            Description = "Filter to specific member selector(s); use with --type or pass Type.Member",
            AllowMultipleArgumentsPerToken = false
        };
        memberFilterOption.Aliases.Add("--member");
        var nameOnlyOption = new Option<bool>("--name-only") { Description = "Show only type names that changed" };
        var breakingOption = new Option<bool>("--breaking") { Description = "Show only breaking changes" };
        var additiveOption = new Option<bool>("--additive") { Description = "Show only additive changes" };
        var changedOption = new Option<bool>("--changed") { Description = "Analysis Diff only: show only in-place changes to members present in both versions (drop added/removed members)" };
        var allocRegressionsOption = new Option<bool>("--alloc-regressions") { Description = "Analysis Diff focus: show only allocation increases on members present in both versions (the file-able set), in-loop (hot) ones first" };
        var pdbSourceOption = new Option<bool>("--pdb-source") { Description = "Implementation Diff only: acquire checksum-verified PDB source evidence" };
        var legacyAuthoredSourceOption = new Option<bool>("--authored-source") { Hidden = true };
        var repoOption = new Option<string[]>("--repo")
        {
            Description = "Implementation Diff: read PDB-mapped source from local git clone(s) by SourceLink commit + PDB checksum, before the network. Can repeat.",
            AllowMultipleArgumentsPerToken = false
        };
        var findingOption = new Option<string?>("--finding") { Description = "Finding producer: api.type, api.member, api.attribute, analysis.allocation, analysis.call-site, or analysis.unsafety" };
        var legendOption = new Option<bool>("--legend") { Description = "Show legend explaining change symbols" };
        var compactOption = new Option<bool>("--compact") { Description = "Minified complete Diff JSON (use with unprojected --json or --envelope)" };

#if DEBUG
        var evidenceEnvelopeOption =
            new Option<string?>("--evidence-envelope")
            {
                Description =
                    "Write the complete enriched Diff History envelope to a JSON sidecar",
                Arity = ArgumentArity.ExactlyOne,
            };
#endif

        diffCommand.Arguments.Add(argsArg);
        diffCommand.Options.Add(packageOption);
        diffCommand.Options.Add(platformOption);
        diffCommand.Options.Add(libraryOption);
        diffCommand.Options.Add(frameworkOption);
        diffCommand.Options.Add(tfmOption);
        diffCommand.Options.Add(allOption);
        diffCommand.Options.Add(implementationOption);
        diffCommand.Options.Add(historyOption);
        diffCommand.Options.Add(atOption);
        diffCommand.Options.Add(maxProbesOption);
        diffCommand.Options.Add(samplePercentOption);
        diffCommand.Options.Add(majorVersionsOption);
        diffCommand.Options.Add(prereleaseOption);
        diffCommand.Options.Add(configDirectoryOption);
        diffCommand.Options.Add(typeFilterOption);
        diffCommand.Options.Add(memberFilterOption);
        opts.AddTableOptionsTo(diffCommand);
        diffCommand.Options.Add(opts.Json);
        diffCommand.Options.Add(opts.Markdown);
        diffCommand.Options.Add(nameOnlyOption);
        diffCommand.Options.Add(breakingOption);
        diffCommand.Options.Add(additiveOption);
        diffCommand.Options.Add(changedOption);
        diffCommand.Options.Add(allocRegressionsOption);
        diffCommand.Options.Add(pdbSourceOption);
        diffCommand.Options.Add(legacyAuthoredSourceOption);
        diffCommand.Options.Add(repoOption);
        diffCommand.Options.Add(findingOption);
        diffCommand.Options.Add(legendOption);
        diffCommand.Options.Add(compactOption);
#if DEBUG
        diffCommand.Options.Add(evidenceEnvelopeOption);
        diffCommand.Validators.Add(result =>
        {
            if (result.GetResult(evidenceEnvelopeOption)
                is not { Implicit: false })
            {
                return;
            }
            if (!result.GetValue(historyOption))
            {
                result.AddError(
                    "--evidence-envelope is supported only by diff --history.");
            }
            if (result.GetResult(opts.Discover) is { Implicit: false }
                || result.GetValue(opts.Schema))
            {
                result.AddError(
                    "--evidence-envelope requires a Diff History inspection, not discovery or schema output.");
            }
        });
#endif
        opts.AddCountOptionTo(diffCommand);
        opts.AddOutputOptionsTo(diffCommand);
        opts.AddNuGetOptionsTo(diffCommand);
        diffCommand.Options.Add(opts.Discover);
        diffCommand.Options.Add(opts.Schema);
        diffCommand.Options.Add(opts.Tree);
        diffCommand.Options.Add(opts.Select);
        opts.AddEnvelopeOptionTo(
            diffCommand,
            opts.Discover, opts.Verbosity,
            nameOnlyOption,
            breakingOption, additiveOption, changedOption,
            allocRegressionsOption, pdbSourceOption, legacyAuthoredSourceOption,
            repoOption, legendOption);
        Option[] envelopeRowOptions =
        [
            opts.Rows,
            opts.Limit,
            opts.Head,
            opts.Tail,
        ];
        diffCommand.Validators.Add(result =>
        {
            if (!result.GetValue(opts.Envelope)
                || result.GetValue(historyOption)
                    && result.GetValue(opts.Count))
            {
                return;
            }

            foreach (Option option in envelopeRowOptions)
            {
                if (result.GetResult(option) is { Implicit: false })
                {
                    result.AddError(
                        $"--envelope cannot be combined with {option.Name}.");
                }
            }
        });
        diffCommand.Validators.Add(result =>
        {
            if (!result.GetValue(opts.Envelope)
                || result.GetValue(historyOption))
                return;

            string? selector = opts.SelectText(result);
            bool implementationTransport =
                string.Equals(
                    selector,
                    DiffSections.ImplementationDiff.Name,
                    StringComparison.OrdinalIgnoreCase)
                || result.GetValue(implementationOption)
                    && result.GetResult(opts.Select)
                        is not { Implicit: false };
            if (!implementationTransport)
            {
                if (result.GetResult(opts.Select) is { Implicit: false })
                {
                    result.AddError(
                        "--envelope cannot be combined with --select unless "
                            + "Implementation Diff is selected by itself.");
                }
                if (result.GetResult(typeFilterOption) is { Implicit: false })
                    result.AddError("--envelope cannot be combined with --type.");
                if (result.GetResult(memberFilterOption) is { Implicit: false })
                    result.AddError("--envelope cannot be combined with --member.");
            }

            bool explicitSource =
                result.GetValue(packageOption) is not null
                || result.GetValue(platformOption) is not null
                || result.GetValue(libraryOption) is not null;
            int positionalCount = result.GetValue(argsArg)?.Length ?? 0;
            if (!implementationTransport
                && positionalCount > (explicitSource ? 0 : 1))
            {
                result.AddError(
                    "--envelope cannot be combined with positional type filters.");
            }
        });

        var commandArgs = new DiffOptionsParser.DiffCommandArgs(
            argsArg, packageOption, platformOption, libraryOption, frameworkOption, tfmOption, allOption,
            implementationOption,
            historyOption, atOption, maxProbesOption, samplePercentOption, majorVersionsOption, prereleaseOption, opts.Count,
            typeFilterOption, memberFilterOption, opts.NoHeaders, nameOnlyOption, breakingOption, additiveOption, changedOption, allocRegressionsOption, pdbSourceOption, legacyAuthoredSourceOption, findingOption, legendOption, repoOption, compactOption);

        diffCommand.SetAction(async (parseResult, ct) =>
        {
            bool history = parseResult.GetValue(historyOption);
            if (!history
                && parseResult.GetResult(opts.Count) is { Implicit: false })
            {
                CommandError.Write(
                    "--count is not supported by the 'diff' command because "
                    + "its current modes do not declare countable row semantics.");
                return 1;
            }

            RowSelectionIntent<string>? semanticRowSelection = null;
            if (history
                && !CliRowSelectionCommandRegistry
                    .TryGetPreparedSemanticIntent(
                        parseResult,
                        "Diff History",
                        out semanticRowSelection,
                        out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }

            var result = DiffOptionsParser.Parse(
                parseResult,
                opts,
                commandArgs,
                semanticRowSelection);

            switch (result)
            {
                case DiffOptionsParser.VersionNumberError error:
                    CommandError.Write($"'{error.Value}' looks like a version number. Use '{error.VersionRange}@{error.Value}' to specify a version.");
                    return 1;

                case DiffOptionsParser.Invalid invalid:
                    CommandError.Write(invalid.Message);
                    return 1;

                case DiffOptionsParser.Success success:
                    if (!NuGetConfigDirectoryOption.TryApply(
                            parseResult.GetValue(configDirectoryOption),
                            success.Options.SourceOptions
                                ?? NuGetSourceOptions.Default,
                            out NuGetSourceOptions? sourceOptions,
                            out string? sourceError))
                    {
                        CommandError.Write(sourceError!);
                        return 1;
                    }
                    DiffOptions options = success.Options with
                    {
                        SourceOptions = sourceOptions,
                    };
#if DEBUG
                    if (parseResult.GetResult(evidenceEnvelopeOption)
                        is { Implicit: false })
                    {
                        if (!EvidenceEnvelopeOutput.TryResolvePath(
                                parseResult.GetValue(evidenceEnvelopeOption)!,
                                out string? evidencePath,
                                out string? evidencePathError))
                        {
                            CommandError.Write(evidencePathError!);
                            return 1;
                        }
                        options = options with
                        {
                            EvidenceEnvelopePath = evidencePath,
                        };
                    }
#endif
                    var exitCode = await DiffCommand.ExecuteAsync(options, ct);

                    if (exitCode == 0)
                    {
                        if (options.Legend)
                            Hints.WriteDiffLegend();

                        if (!options.FormatExplicitlySet)
                        {
                            var tips = DiffOptionsParser.BuildTips(options, options.TypeFilter);
                            Hints.WriteTips(success.TipLevel, [.. tips]);
                        }
                    }

                    return exitCode;

                default:
                    return 1;
            }
        });

        CliRowSelectionCommandRegistry.Register(
            diffCommand,
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
            isActive: result => result.GetValue(historyOption),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return diffCommand;
    }

    public static Command CreateLibraryCommand(SharedOptions opts)
    {
        var assemblyCommand = new Command("library", "Inspect .NET libraries");

        var assemblyPathArg = new Argument<string?>("source")
        {
            Description = "Library file path, NuGet package name (e.g., System.Text.Json), or package@version",
            Arity = ArgumentArity.ZeroOrOne
        };
        assemblyPathArg.DefaultValueFactory = _ => null;

        var referencesOption = new Option<bool>("--references") { Description = "Legacy alias for -S References" };
        var dependenciesOption = new Option<bool>("--dependencies") { Description = "Removed; use -S \"Reference Hierarchy\"" };
        var referenceDepthOption = new Option<int?>("--depth") { Description = "With -S \"Reference Hierarchy\": maximum depth (1 = direct references only)" };
        var asmPlatformOption = new Option<string?>("--platform") { Description = "Inspect platform library (e.g., System.Text.Json)" };
        var asmPackageOption = new Option<string?>("--package") { Description = "Inspect library from NuGet package (e.g., System.Text.Json or System.Text.Json@9.0.4)" };
        var workspaceOption = new Option<string?>("--workspace")
        {
            Description =
                "Source: canonical Base64URL Workspace packet string",
        };
        var namesakeLibraryOption = new Option<bool>("--namesake-library")
        {
            Description =
                "Narrow package inspection to its uniquely named Library",
        };
        var asmPrereleaseOption = new Option<bool>("--preview") { Description = "When resolving an unversioned package, include prerelease versions" };
        asmPrereleaseOption.Aliases.Add("--prerelease");
        var asmFrameworkOption = new Option<string?>("--framework") { Description = "Optional platform framework family (runtime, aspnetcore)" };
        var asmVersionOption = new Option<string?>("--version") { Description = "Platform runtime version (searches framework families in priority order)" };
        var asmTfmOption = new Option<string?>("--tfm") { Description = "Select a package library by TFM (e.g., net8.0; 'all' supports Markdown, JSON, and aggregate --count)" };
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter Source Files rows by type glob/name (e.g., *Json*)" };
        typeFilterOption.Aliases.Add("--type");
        var namespaceOption = new Option<string?>("--namespace")
        {
            Description =
                "List public Types in an exact namespace, or in namespaces ending with a leading-dot suffix",
        };
        var namespaceChildrenOption = new Option<bool>("--children")
        {
            Description =
                "With --namespace: include the named namespace and its descendants",
        };
        var metadataRootOption = new Option<string?>("--metadata-root")
        {
            Description = "Metadata root for @Metadata sections: cli or r2r-manifest"
        };
        var detailsOption = new Option<bool>("--details")
        {
            Description =
                "With -D: add structurally supported output formats (offline)"
        };
        var extractResourcesOption = new Option<string?>("--extract-resources")
        {
            Description = "Extract embedded resources beneath a directory without overwriting files"
        };
        var compactOption = new Option<bool>("--compact")
        {
            Description =
                "Minified JSON (use with --envelope)",
        };
        var outOption = SharedOptions.CreateOutputPathOption();
        assemblyCommand.Arguments.Add(assemblyPathArg);
        assemblyCommand.Options.Add(referencesOption);
        assemblyCommand.Options.Add(dependenciesOption);
        assemblyCommand.Options.Add(referenceDepthOption);
        assemblyCommand.Options.Add(asmPlatformOption);
        assemblyCommand.Options.Add(asmPackageOption);
        assemblyCommand.Options.Add(workspaceOption);
        assemblyCommand.Options.Add(namesakeLibraryOption);
        assemblyCommand.Options.Add(asmPrereleaseOption);
        assemblyCommand.Options.Add(asmFrameworkOption);
        assemblyCommand.Options.Add(asmVersionOption);
        assemblyCommand.Options.Add(asmTfmOption);
        assemblyCommand.Options.Add(typeFilterOption);
        assemblyCommand.Options.Add(namespaceOption);
        assemblyCommand.Options.Add(namespaceChildrenOption);
        assemblyCommand.Options.Add(metadataRootOption);
        assemblyCommand.Options.Add(detailsOption);
        assemblyCommand.Options.Add(opts.PreferRenderedUrls);
        assemblyCommand.Options.Add(extractResourcesOption);
        assemblyCommand.Options.Add(compactOption);
        assemblyCommand.Options.Add(outOption);
        SharedOptions.AddOutputPathValidator(assemblyCommand, outOption);
        // Registered per-command rather than in AddOutputOptionsTo: only the commands that build a
        // trace should advertise the flag. A flag every command accepts and only one honours is
        // worse than an unrecognized argument, which at least fails loudly.
        assemblyCommand.Options.Add(opts.Trace);
        assemblyCommand.Options.Add(opts.Effective);
        opts.AddAllOptionsTo(assemblyCommand);
        opts.AddCountOptionTo(assemblyCommand);
        opts.AddPrintOptionTo(assemblyCommand);
        opts.AddShapeProjectionOptionsTo(assemblyCommand);
        opts.AddPerformanceTriageOptionsTo(assemblyCommand);
        opts.AddEnvelopeOptionTo(
            assemblyCommand,
            opts.Discover,
            opts.Schema,
            opts.Verbosity,
            opts.Rows,
            opts.Row,
            opts.RowWhere,
            opts.Limit,
            opts.Head,
            opts.Tail,
            opts.Lines,
            opts.TailLines,
            opts.Trace,
            opts.Count,
            opts.Effective,
            opts.Source,
            opts.AddSource,
            opts.NuGetConfig,
            referencesOption,
            dependenciesOption,
            referenceDepthOption,
            asmPlatformOption,
            asmPackageOption,
            workspaceOption,
            namesakeLibraryOption,
            asmPrereleaseOption,
            asmFrameworkOption,
            asmVersionOption,
            asmTfmOption,
            typeFilterOption,
            metadataRootOption,
            detailsOption,
            extractResourcesOption);
        assemblyCommand.Validators.Add(result =>
        {
            if (result.GetResult(compactOption)
                    is { Implicit: false }
                && !result.GetValue(opts.Envelope))
            {
                result.AddError(
                    "--compact requires library --envelope.");
            }
            if (!result.GetValue(opts.Envelope))
            {
                return;
            }

            if (result.GetResult(opts.Select)
                is { Implicit: false })
            {
                result.AddError(
                    "library --envelope does not accept section "
                        + "selection.");
            }
        });
        assemblyCommand.Subcommands.Add(
            LibraryCoordinateCommandDefinitions.Create(
                opts,
                assemblyCommand,
                assemblyPathArg,
                metadataRootOption));
        assemblyCommand.Subcommands.Add(
            LibraryQueryCommandDefinitions.Create(
                opts,
                assemblyCommand,
                assemblyPathArg));

        assemblyCommand.SetAction(async (parseResult, ct) =>
        {
            bool discoverDetails =
                parseResult.GetValue(detailsOption);
            string[]? discover =
                opts.ParseDiscover(parseResult);
            if (discoverDetails && discover is null)
            {
                CommandError.Write(
                    "--details requires -D/--discover.");
                return 1;
            }

            var source = parseResult.GetValue(assemblyPathArg);
            if (!IntegrationQueryOptions.TryExtract(
                    parseResult.GetValue(opts.RowWhere) ?? [],
                    out var integrationQuery,
                    out var nonIntegrationWhere,
                    out var integrationError))
            {
                CommandError.Write(integrationError);
                return 1;
            }
            if (!CloneCandidateQueryOptions.TryExtract(
                    nonIntegrationWhere,
                    out var cloneCandidateQuery,
                    out var nonCloneWhere,
                    out var cloneCandidateError))
            {
                CommandError.Write(cloneCandidateError);
                return 1;
            }
            var independentTriage = opts.ParsePerformanceTriageOptions(parseResult, []);
            if (integrationQuery.HasFilter
                && (cloneCandidateQuery.HasPredicates
                    || nonCloneWhere.Length > 0
                    || independentTriage.HasFilters
                    || independentTriage.HasRanking
                    // Count mode suppresses Top, but the explicit option is still incompatible.
                    || parseResult.GetValue(opts.PerformanceTriageTop) is not null))
            {
                CommandError.Write(
                    "Integration ecosystem queries cannot be combined with Clone Candidates, "
                    + "Body Shapes, or Performance Triage predicates/ranking.");
                return 1;
            }
            var explicitPackage = parseResult.GetValue(asmPackageOption);
            var explicitPlatform = parseResult.GetValue(asmPlatformOption);
            string? workspacePacket =
                parseResult.GetValue(workspaceOption);
            bool namesakeLibrary =
                parseResult.GetValue(namesakeLibraryOption);

            // Disambiguate positional arg: local file vs package name
            string? assemblyPath = null;
            string? packagePath = explicitPackage;
            string? platformAssembly = explicitPlatform;
            var requestedFramework = parseResult.GetValue(asmFrameworkOption);
            var requestedPlatformVersion = parseResult.GetValue(asmVersionOption);
            NuGetSourceOptions? sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
            bool structuralDiscovery =
                opts.IsDiscoveryMode(parseResult)
                && (opts.ParseSchema(parseResult)
                    || discoverDetails);

            if (namesakeLibrary
                && (string.IsNullOrWhiteSpace(explicitPackage)
                    || !string.IsNullOrWhiteSpace(source)))
            {
                CommandError.Write(
                    "--namesake-library requires --package and cannot be "
                        + "combined with an exact Library source.");
                return 1;
            }
            if (parseResult.GetValue(opts.Envelope))
            {
                assemblyPath = source;
            }
            else if (workspacePacket is not null)
            {
                if (string.IsNullOrWhiteSpace(explicitPackage))
                {
                    CommandError.Write(
                        "--workspace on library requires --package to identify "
                            + "one Package in the selected Workspace context.");
                    return 1;
                }
                if (explicitPlatform is not null
                    || requestedFramework is not null
                    || requestedPlatformVersion is not null
                    || parseResult.GetValue(asmTfmOption) is not null
                    || parseResult.GetValue(asmPrereleaseOption))
                {
                    CommandError.Write(
                        "--workspace supplies the Package location and target; "
                            + "it cannot be combined with --platform, "
                            + "--framework, --version, --tfm, or --preview.");
                    return 1;
                }

                assemblyPath = source;
            }
            else if (structuralDiscovery)
            {
                assemblyPath = source;
            }
            else if (!string.IsNullOrEmpty(source) && string.IsNullOrEmpty(explicitPlatform) && string.IsNullOrEmpty(explicitPackage))
            {
                if (File.Exists(source))
                    assemblyPath = source;
                else if (source.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                    || source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || source.Contains('/')
                    || source.Contains('\\'))
                {
                    // A non-existent value that looks like a local library path (has a file
                    // extension or directory separator) is reported as a missing file rather
                    // than misclassified as a NuGet package. See #1690.
                    assemblyPath = source;
                }
                else if (!source.Contains('@') && PlatformResolver.IsPlatformCandidate(source))
                {
                    // Platform-preferred routing for System.*/Microsoft.* bare names
                    bool verbose = parseResult.GetValue(opts.Verbose);
                    Action<string>? log = CommandLineHelpers.CreateVerboseLogger(verbose);
                    var (asmPath, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(
                        source, HttpClientFactory.Shared, log,
                        requestedFramework,
                        platformVersion: requestedPlatformVersion,
                        useRuntimeAssemblies: true,
                        sourceOptions: sourceOptions);
                    if (error == null && asmPath != null)
                        platformAssembly = source;
                    else if (!string.IsNullOrEmpty(requestedFramework) || !string.IsNullOrEmpty(requestedPlatformVersion))
                        platformAssembly = source;
                    else
                        packagePath = source;
                }
                else
                    packagePath = source;
            }
            else if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(explicitPackage))
            {
                assemblyPath = source;
            }

            bool showReferences = parseResult.GetValue(referencesOption);
            bool showDependencies = parseResult.GetValue(dependenciesOption);

            var typeFilter = parseResult.GetValue(typeFilterOption);
            var select = opts.ParseSelect(parseResult);
            var selectDefault = opts.ParseSelectDefault(parseResult);
            bool hasExplicitSelect = select is { Length: > 0 } || selectDefault;
            if (showReferences
                && select?.Contains(
                    SectionNames.References,
                    StringComparer.OrdinalIgnoreCase) != true)
            {
                select = [.. select ?? [], SectionNames.References];
            }
            bool sectionSelectionControlsInference =
                hasExplicitSelect || showReferences;
            if (!BodyKindQueryOptions.TryExtract(
                    nonCloneWhere,
                    out var bodyKindQuery,
                    out var performanceWhere,
                    out var bodyKindError))
            {
                CommandError.Write(bodyKindError);
                return 1;
            }
            var performanceTriage = opts.ParsePerformanceTriageOptions(
                parseResult,
                performanceWhere);
            if (!PerformanceTriageOptions.TryValidate(performanceTriage, out var triageShapeError))
            {
                CommandError.Write(triageShapeError);
                return 1;
            }
            if (bodyKindQuery.HasFilter && performanceTriage.HasRanking)
            {
                CommandError.Write(
                    "Body Shapes composition accepts Performance Triage filters, "
                    + "but not --top or --order-by. Use --rows to limit rendered matches.");
                return 1;
            }
            if ((cloneCandidateQuery.HasPredicates
                    || CloneCandidatesCommand.IsSelected(select))
                && (bodyKindQuery.HasFilter
                    || performanceTriage.HasFilters
                    || performanceTriage.HasRanking))
            {
                CommandError.Write(
                    "Clone Candidates predicates cannot be combined with Body Shapes or Performance Triage predicates/ranking.");
                return 1;
            }
            if (!string.IsNullOrWhiteSpace(typeFilter))
                select = [.. select ?? [], "Source Files"];
            if (bodyKindQuery.HasFilter
                && !opts.IsDiscoveryMode(parseResult)
                && !sectionSelectionControlsInference)
            {
                select = [.. select ?? [], SectionNames.BodyShapes];
            }
            if (cloneCandidateQuery.HasPredicates
                && !opts.IsDiscoveryMode(parseResult)
                && !sectionSelectionControlsInference)
            {
                select = [.. select ?? [], SectionNames.CloneCandidates];
            }
            RowSelectionIntent<string>? cloneCandidateRowSelection = null;
            if (CloneCandidateRowSelectionAdoption.IsActive(
                    parseResult,
                    opts)
                && !CliRowSelectionCommandRegistry
                    .TryGetPreparedSemanticIntent(
                        parseResult,
                        "Clone Candidates",
                        out cloneCandidateRowSelection,
                        out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }
            // Only surface performance sections from row filters when the user did not select
            // sections with -S; an explicit selection like -S "Top Leverage" must not silently gain
            // a second section and break single-section formats (--table/--tsv/--jsonl). When the
            // filter is a single --triage-shape that maps to one kind section, target that section
            // directly. Otherwise select the homogeneous performance kind family, not the broader
            // @Performance category (which also contains Top Leverage and Resource Triage and
            // therefore cannot render as one tabular stream).
            if (performanceTriage.HasFilters
                && !bodyKindQuery.HasFilter
                && !opts.IsDiscoveryMode(parseResult)
                && !sectionSelectionControlsInference)
            {
                string[] targets = PerformanceKinds.Sections;
                if (performanceTriage.Shapes is { Length: > 0 })
                {
                    var kinds = performanceTriage.Shapes
                        .Select(PerformanceKinds.SectionForShape)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();
                    if (kinds.Length == 1)
                        targets = kinds;
                }
                select = [.. select ?? [], .. targets];
            }
            RowSelectionIntent<string>? referenceRowSelection = null;
            if (LibraryReferenceRowSelectionAdoption.IsActive(
                    parseResult,
                    opts,
                    referencesOption,
                    asmTfmOption,
                    typeFilterOption,
                    select)
                && !CliRowSelectionCommandRegistry
                    .TryGetPreparedSemanticIntent(
                        parseResult,
                        "Library reference",
                        out referenceRowSelection,
                        out string? referenceRowSelectionError))
            {
                CommandError.Write(referenceRowSelectionError!);
                return 1;
            }
            RowSelectionIntent<string>? ecosystemDependencyRowSelection = null;
            if (LibraryEcosystemDependencyRowSelectionAdoption.IsActive(
                    parseResult,
                    opts,
                    asmTfmOption,
                    select)
                && !CliRowSelectionCommandRegistry
                    .TryGetPreparedSemanticIntent(
                        parseResult,
                        "Library ecosystem dependency",
                        out ecosystemDependencyRowSelection,
                        out string? ecosystemRowSelectionError))
            {
                CommandError.Write(ecosystemRowSelectionError!);
                return 1;
            }
            if (opts.IsJsonDocumentOutput(parseResult)
                && LibraryEcosystemDependencyRowSelectionAdoption
                    .IsMultiSectionSelection(
                        parseResult,
                        opts,
                        asmTfmOption,
                        select)
                && LibraryEcosystemDependencyRowSelectionAdoption
                    .HasExplicitSelection(parseResult, opts))
            {
                CommandError.Write(
                    "Rendered-line selection cannot be combined with JSON output.");
                return 1;
            }

            if (!TryParseMetadataRoot(
                    parseResult.GetValue(metadataRootOption),
                    out MetadataRootKind metadataRoot,
                    out string? metadataRootError))
            {
                CommandError.Write(metadataRootError!);
                return 1;
            }

            if (!LibrarySourceAdapter.TryDeclare(
                    assemblyPath,
                    packagePath,
                    platformAssembly,
                    "source",
                    out var sourceIntent,
                    out string? sourceIntentError))
            {
                CommandError.Write(sourceIntentError!);
                return 1;
            }

            var options = new LibraryOptions
            {
                SourceIntent = sourceIntent,
                AssemblyName = assemblyPath,
                WorkspacePacket = workspacePacket,
                NamesakeLibrary = namesakeLibrary,
                IncludeMetadata = true,
                IncludeReferences = showReferences,
                IncludeDependencies = showDependencies,
                ReferenceHierarchyDepth = parseResult.GetValue(referenceDepthOption),
                IncludePrerelease = parseResult.GetValue(asmPrereleaseOption),
                PlatformFramework = requestedFramework,
                PlatformVersion = requestedPlatformVersion,
                Tfm = parseResult.GetValue(asmTfmOption),
                IntegrationQuery = integrationQuery,
                TypeFilter = typeFilter,
                TypeNamespace =
                    parseResult.GetValue(namespaceOption),
                IncludeNamespaceChildren =
                    parseResult.GetValue(namespaceChildrenOption),
                MetadataRoot = metadataRoot,
                PreferRenderedUrls = parseResult.GetValue(opts.PreferRenderedUrls),
                JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
                Markdown = parseResult.GetValue(opts.Markdown),
                PlainText = parseResult.GetValue(opts.PlainText),
                Tabular = opts.ResolveTabular(parseResult),
                Tsv = opts.ResolveTsv(parseResult),
                Jsonl = opts.ResolveJsonl(parseResult),
                TabularExplicitlySet = opts.IsTableExplicitlySet(parseResult),
                FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
                Format = opts.ResolveFormat(parseResult),
                Verbose = parseResult.GetValue(opts.Verbose),
                Trace = parseResult.GetValue(opts.Trace),
                Verbosity = opts.ParseVerbosity(parseResult),
                Discover = discover,
                DiscoverDetails = discoverDetails,
                Effective = parseResult.GetValue(opts.Effective),
                Tree = parseResult.GetValue(opts.Tree),
                Select = select,
                SelectDefault = selectDefault,
                SelectExplicitlySet = hasExplicitSelect,
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                FieldsExplicitlySet =
                    parseResult.GetResult(opts.Fields) is { Implicit: false },
                Count = parseResult.GetValue(opts.Count),
                EnvelopeOutput =
                    parseResult.GetValue(opts.Envelope),
                CompactJson =
                    parseResult.GetValue(compactOption),
                Print = parseResult.GetValue(opts.Print),
                Value = parseResult.GetValue(opts.Value),
                Urls = parseResult.GetValue(opts.Urls),
                Paths = parseResult.GetValue(opts.Paths),
                JsonArray = parseResult.GetValue(opts.JsonArray),
                PrintRow = opts.ParsePrintRow(parseResult),
                ProjectionRow = opts.ParsePrintRow(parseResult),
                Rows = cloneCandidateRowSelection is null
                    && referenceRowSelection is null
                    && ecosystemDependencyRowSelection is null
                    ? opts.ParseRows(parseResult)
                    : null,
                CloneCandidateRowSelection =
                    cloneCandidateRowSelection,
                ReferenceRowSelection =
                    referenceRowSelection,
                EcosystemDependencyRowSelection =
                    ecosystemDependencyRowSelection,
                PerformanceTriage = performanceTriage,
                BodyKindQuery = bodyKindQuery,
                CloneCandidateQuery = cloneCandidateQuery,
                Schema = opts.ParseSchema(parseResult)
                    || discoverDetails,
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                OutputPath = parseResult.GetValue(outOption),
                SourceOptions = opts.ParseNuGetSourceOptions(parseResult),
                ExtractResources = parseResult.GetValue(extractResourcesOption)
            };

            return await LibraryCommand.ExecuteAsync(options, ct);
        });

        CliRowSelectionCommandRegistry.Register(
            assemblyCommand,
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
            result => CloneCandidateRowSelectionAdoption.IsActive(
                result,
                opts),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            assemblyCommand,
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
            result => LibraryReferenceRowSelectionAdoption.IsActive(
                result,
                opts,
                referencesOption,
                asmTfmOption,
                typeFilterOption),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            assemblyCommand,
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
            result => LibraryEcosystemDependencyRowSelectionAdoption.IsActive(
                result,
                opts,
                asmTfmOption,
                opts.ParseSelect(result)),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        return assemblyCommand;
    }

    internal static bool TryParseMetadataRoot(
        string? value,
        out MetadataRootKind metadataRoot,
        out string? error)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Equals("cli", StringComparison.OrdinalIgnoreCase))
        {
            metadataRoot = MetadataRootKind.Cli;
            error = null;
            return true;
        }

        if (value.Equals("r2r-manifest", StringComparison.OrdinalIgnoreCase))
        {
            metadataRoot = MetadataRootKind.ReadyToRunManifest;
            error = null;
            return true;
        }

        metadataRoot = default;
        error =
            $"invalid --metadata-root value '{value}': "
            + "expected cli or r2r-manifest.";
        return false;
    }
}
