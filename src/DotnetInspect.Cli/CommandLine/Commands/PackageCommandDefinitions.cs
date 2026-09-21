using DotnetInspect.Cli.Output;
using System.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>
/// Defines the package command and its package-space query surface.
/// </summary>
public static class PackageCommandDefinitions
{
    /// <summary>
    /// Creates the package command for inspecting NuGet packages.
    /// </summary>
    public static Command CreatePackageCommand(
        SharedOptions opts,
        out PackageOptionsParser.PackageCommandArgs structuralArgs)
    {
        var packageCommand = new Command(PackageCommand.Name, "Inspect a NuGet package");

        var packageNameArg = new Argument<string[]>("package")
        {
            Description = "NuGet package name or path to .nupkg file, optionally with version (e.g., System.Text.Json@9.0.0)",
            Arity = ArgumentArity.ZeroOrMore
        };
        var workspaceOption = new Option<string?>("--workspace")
        {
            Description =
                "Source: canonical Base64URL Workspace packet string",
        };
        var shareOption = WorkspaceShareOption.Create(
            "Emit the resolved Package scenario as a canonical Workspace packet or complete URL");
#if DEBUG
        var evidenceEnvelopeOption =
            new Option<string?>("--evidence-envelope")
            {
                Description =
                    "Write the complete enriched Package envelope to a JSON sidecar",
                Arity = ArgumentArity.ExactlyOne,
            };
#endif

        var dependenciesOption = new Option<bool>("--dependencies")
        {
            Description = "Obsolete Package dependency-tree spelling",
            Hidden = true,
        };
        var layoutOption = new Option<bool>("--layout") { Description = "Show package file tree" };
        var pathOption = new Option<string[]>("--path")
        {
            Description = "List package files with sizes (the Package files section), scoped to a file, directory, glob, @readme (README.md > PACKAGE.md), or @agents. Can repeat. Pass --path with no value for the whole package.",
            Arity = ArgumentArity.ZeroOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var pathMatchOption = new Option<string?>("--match") { Description = "For repeated --path: all (default) or first matching selector per package" };
        var skipEmptyOption = new Option<bool>("--skip-empty") { Description = "With multi-package Files rows, omit packages with no matching files" };
        var rootsOption = new Option<bool>("--roots")
        {
            Description =
                "Project ordered distinct top-level roots represented by selected Package files rows"
        };
        var tfmsOption = new Option<bool>("--tfms")
        {
            Description =
                "List target frameworks in the package; use -n N to select N TFM rows"
        };
        var libOption = new Option<bool>("--lib") { Description = "Scope to lib/ folder (use with --layout)" };
        var toolsOption = new Option<bool>("--tools") { Description = "Scope to tools/ folder (use with --layout)" };
        var libraryOption = new Option<string?>("--library")
        {
            Description = "Inspect this package's compile libraries; provide a DLL name to narrow exactly",
            Arity = ArgumentArity.ZeroOrOne
        };
        var namesakeLibraryOption = new Option<bool>("--namesake-library")
        {
            Description =
                "Narrow to the Library whose assembly name matches the package ID",
        };
        var allLibrariesOption = new Option<bool>("--all-libraries")
        {
            Hidden = true,
        };
        var versionsOption = new Option<bool>("--versions")
        {
            Description = "List available versions; use -n N to select N version rows",
            Arity = ArgumentArity.Zero
        };
        var versionsWithFeedOption = new Option<bool>("--versions-with-feed")
        {
            Description = "List available versions with source feeds; use -n N to select N version/feed rows",
            Arity = ArgumentArity.Zero
        };
        var prereleaseOption = new Option<bool>("--preview") { Description = "Include prerelease versions for --versions and latest resolution" };
        prereleaseOption.Aliases.Add("--prerelease");
        var includeUnlistedOption = new Option<bool>("--include-unlisted") { Description = "Include unlisted versions in --versions output, marked as unlisted" };
        var contentOption = new Option<bool>("--content") { Description = "Print contents of files selected by --path; use --format jsonl for structured rows" };
        var frontmatterOption = new Option<bool>("--frontmatter") { Description = "When printing markdown content, output only the leading YAML frontmatter block" };
        frontmatterOption.Aliases.Add("--yaml-header");
        var bodyOption = new Option<bool>("--body") { Description = "When printing markdown content, output only content after YAML frontmatter" };
        var outOption = SharedOptions.CreateOutputPathOption();
        var tfmOption = new Option<string?>("--tfm") { Description = "Select library by TFM (e.g., net8.0)" };
        var depthOption = new Option<string?>("--depth")
        {
            Description =
                "With -S \"Dependency Hierarchy\": maximum dependency traversal depth"
        };
        depthOption.Validators.Add(result =>
        {
            string? value = result.GetValue(depthOption);
            if (value is not null
                && (!int.TryParse(value, out int depth)
                    || depth <= 0))
            {
                result.AddError("--depth must be a positive integer.");
            }
        });
        var typeFilterOption = new Option<string?>("-t") { Description = "Filter SourceLink: Files rows by type glob/name (e.g., *Json*)" };
        typeFilterOption.Aliases.Add("--type");
        var versionOption = new Option<string?>("--version") { Description = "Package version (or use alone to show resolved version)", Arity = ArgumentArity.ZeroOrOne };
        packageCommand.Arguments.Add(packageNameArg);
        packageCommand.Options.Add(workspaceOption);
        packageCommand.Options.Add(shareOption);
#if DEBUG
        packageCommand.Options.Add(evidenceEnvelopeOption);
#endif
        packageCommand.Options.Add(dependenciesOption);
        packageCommand.Options.Add(layoutOption);
        packageCommand.Options.Add(pathOption);
        packageCommand.Options.Add(pathMatchOption);
        packageCommand.Options.Add(skipEmptyOption);
        packageCommand.Options.Add(rootsOption);
        packageCommand.Options.Add(tfmsOption);
        packageCommand.Options.Add(libOption);
        packageCommand.Options.Add(toolsOption);
        packageCommand.Options.Add(libraryOption);
        packageCommand.Options.Add(namesakeLibraryOption);
        packageCommand.Options.Add(allLibrariesOption);
        packageCommand.Options.Add(versionsOption);
        packageCommand.Options.Add(versionsWithFeedOption);
        packageCommand.Options.Add(prereleaseOption);
        packageCommand.Options.Add(includeUnlistedOption);
        packageCommand.Options.Add(contentOption);
        packageCommand.Options.Add(frontmatterOption);
        packageCommand.Options.Add(bodyOption);
        packageCommand.Options.Add(tfmOption);
        packageCommand.Options.Add(depthOption);
        packageCommand.Options.Add(typeFilterOption);
        packageCommand.Options.Add(versionOption);
        packageCommand.Options.Add(opts.PreferRenderedUrls);
        packageCommand.Options.Add(opts.Bare);
        packageCommand.Options.Add(outOption);
        var commandArgs = new PackageOptionsParser.PackageCommandArgs(
            packageNameArg, dependenciesOption, layoutOption, pathOption, tfmsOption,
            libOption, toolsOption, libraryOption, namesakeLibraryOption, allLibrariesOption, versionsOption, versionsWithFeedOption, prereleaseOption, includeUnlistedOption,
            contentOption, frontmatterOption, bodyOption,
            tfmOption, depthOption, typeFilterOption, versionOption,
            opts.Lines, opts.TailLines, outOption, pathMatchOption,
            skipEmptyOption, rootsOption, opts.NoHeaders,
            workspaceOption, shareOption,
#if DEBUG
            evidenceEnvelopeOption
#else
            null
#endif
            );
        SharedOptions.AddOutputPathValidator(packageCommand, outOption);
        opts.AddTableOptionsTo(packageCommand);
        opts.AddFormatOptionTo(
            packageCommand,
            CliPresentationFormat.Json,
            CliPresentationFormat.Markdown,
            CliPresentationFormat.PlainText);
        opts.AddOutputOptionsTo(
            packageCommand,
            validateLegacyRowWindow: result =>
                !result.GetValue(versionsOption)
                && !result.GetValue(versionsWithFeedOption)
                && !PackageOptionsParser.IsSourceLinkFileRowSelection(
                    result,
                    opts,
                    commandArgs)
                && !PackageOptionsParser.IsPackageFileRowSelection(
                    result,
                    opts,
                    commandArgs)
                && !PackageOptionsParser.IsPackageLayoutRowSelection(
                    result,
                    opts,
                    commandArgs)
                && !PackageOptionsParser.IsPackageTfmRowSelection(
                    result,
                    opts,
                    commandArgs)
                && !PackageOptionsParser
                    .IsEcosystemDependencyRowSelection(
                        result,
                        opts,
                        commandArgs)
                && !PackageOptionsParser.IsCloneCandidateRowSelection(
                    result,
                    opts,
                    commandArgs));
        opts.AddSectionOptionsTo(packageCommand);
        opts.AddCountOptionTo(packageCommand);
        opts.AddPrintOptionTo(packageCommand);
        opts.AddShapeProjectionOptionsTo(packageCommand);
        opts.AddNuGetOptionsTo(packageCommand);
        opts.AddEnvelopeOptionTo(
            packageCommand,
            opts.Discover, opts.Schema, opts.Select, opts.Verbosity,
            opts.Lines, opts.TailLines,
            dependenciesOption, layoutOption, pathOption, pathMatchOption,
            skipEmptyOption, tfmsOption, libOption, toolsOption,
            libraryOption, namesakeLibraryOption, allLibrariesOption,
            contentOption, frontmatterOption, bodyOption, outOption,
            tfmOption, depthOption, typeFilterOption, versionOption, rootsOption);
        packageCommand.Validators.Add(result =>
        {
            bool hasPluralVersionSelector =
                result.GetValue(versionsOption)
                || result.GetValue(versionsWithFeedOption);
            if (result.GetValue(opts.Envelope))
            {
                string[] packageReferences =
                    result.GetValue(packageNameArg) ?? [];
                bool isRange =
                    packageReferences is [var packageReference]
                    && PackageVersionRange.TryParse(
                        packageReference,
                        out _,
                        out string? rangeError)
                    && rangeError is null;
                bool isOrdinaryListing =
                    packageReferences is [var ordinaryReference]
                    && !File.Exists(ordinaryReference)
                    && string.IsNullOrEmpty(
                        PackageExtractor.ParsePackageReference(
                            ordinaryReference).version);
                bool hasPopulationGesture =
                    hasPluralVersionSelector
                    || (isRange && result.GetValue(opts.Count));
                if (!hasPopulationGesture
                    || (!isRange && !isOrdinaryListing))
                {
                    result.AddError(
                        "--envelope on package requires one unversioned package "
                        + "with --versions or --versions-with-feed, or one "
                        + "Package@A..B range with --versions, "
                        + "--versions-with-feed, or --count.");
                }

                if (!result.GetValue(opts.Count))
                {
                    foreach (Option option in new Option[]
                    {
                        opts.Rows, opts.Limit, opts.Head, opts.Tail,
                    })
                    {
                        if (result.GetResult(option) is { Implicit: false })
                        {
                            result.AddError(
                                $"--envelope cannot be combined with {option.Name}.");
                        }
                    }
                }
            }

        });

        // Register this fallback first: the registry prepends, so the
        // established package populations below retain short-limit ownership.
        var legacyPackageSectionRows =
            new Option<string?>(
                "--unavailable-package-section-semantic-rows")
            {
                Hidden = true,
            };
        var legacyPackageSectionHead =
            new Option<bool>(
                "--unavailable-package-section-semantic-head")
            {
                Hidden = true,
            };
        var legacyPackageSectionTail =
            new Option<bool>(
                "--unavailable-package-section-semantic-tail")
            {
                Hidden = true,
            };
        CliRowSelectionCommandRegistry.Register(
            packageCommand,
            new(
                opts.Limit,
                legacyPackageSectionRows,
                top: null,
                orderBy: null,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            isActive: result =>
                result.GetResult(opts.Select)
                    is { Implicit: false }
                && !(result.GetResult(opts.Rows)
                        is { Implicit: false }
                    && result.GetResult(opts.Limit)
                        is not { Implicit: false }),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            packageCommand,
            new(
                opts.Limit,
                legacyPackageSectionRows,
                top: null,
                orderBy: null,
                legacyPackageSectionHead,
                legacyPackageSectionTail,
                opts.Lines,
                opts.TailLines),
            CliRowSelectionCapabilities.HeadTail
                | CliRowSelectionCapabilities.Window
                | CliRowSelectionCapabilities.Lines,
            isActive: result =>
                result.GetResult(opts.Select)
                    is { Implicit: false }
                && result.GetResult(opts.Rows)
                    is { Implicit: false }
                && result.GetResult(opts.Limit)
                    is not { Implicit: false },
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            packageCommand,
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
            result => PackageOptionsParser.IsCloneCandidateRowSelection(
                result,
                opts,
                commandArgs),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliRowSelectionCommandRegistry.Register(
            packageCommand,
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
            result =>
                result.GetValue(versionsOption)
                || result.GetValue(versionsWithFeedOption)
                || (result.GetValue(opts.Count)
                    && (result.GetValue(packageNameArg) ?? [])
                        is [var packageReference]
                    && PackageVersionRange.TryParse(
                        packageReference,
                        out _,
                        out string? rangeError)
                    && rangeError is null)
                || PackageOptionsParser.IsSourceLinkFileRowSelection(
                    result,
                    opts,
                    commandArgs)
                || PackageOptionsParser.IsPackageFileRowSelection(
                    result,
                    opts,
                    commandArgs)
                || PackageOptionsParser.IsPackageLayoutRowSelection(
                    result,
                    opts,
                    commandArgs)
                || PackageOptionsParser.IsPackageTfmRowSelection(
                    result,
                    opts,
                    commandArgs)
                || PackageOptionsParser
                    .IsEcosystemDependencyRowSelection(
                        result,
                        opts,
                        commandArgs),
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));

        var queryCommand = CreatePackageQueryCommand(
            opts,
            packageCommand,
            packageNameArg,
            prereleaseOption,
            tfmOption);
        packageCommand.Subcommands.Add(queryCommand);
        packageCommand.Subcommands.Add(
            PackageChangesCommandDefinitions.CreatePackageChangesCommand(
                opts,
                packageCommand,
                packageNameArg));

        structuralArgs = commandArgs;

        CliOptionValueValidation.RegisterCapacity(
            packageNameArg,
            result => PackageOptionsParser.GetPositionalCapacity(result, opts, commandArgs));

        packageCommand.SetAction(async (parseResult, ct) =>
        {
            if (parseResult.GetValue(packageNameArg) is [var first, ..]
                && first.Equals("search", StringComparison.OrdinalIgnoreCase))
            {
                CommandError.Write(
                    "'package search' has been removed. Use 'package query <ID>' "
                    + "for an exact package or 'package query <PREFIX>*' for "
                    + "package-prefix discovery.");
                return 1;
            }

            var result = PackageOptionsParser.Parse(parseResult, opts, commandArgs);

            switch (result)
            {
                case PackageOptionsParser.UnrecognizedOption error:
                    // A spelling this command removed is answered with its replacement; anything
                    // else the parser did not recognize gets the plain complaint. Both go
                    // through CommandError, which owns the "Error: " prefix and containment.
                    CommandError.Write(
                        ArgumentPreprocessor.GetRemovedPackageOptionError(error.Option) is { } removed
                            ? removed
                            : $"Unrecognized option '{error.Option}'.");
                    return 1;

                case PackageOptionsParser.InvalidArguments error:
                    CommandError.Write(error.Message);
                    return 1;

                case PackageOptionsParser.Success success:
                    {
                        var exitCode = await PackageCommand.ExecuteAsync(success.Options);

                        if (exitCode == 0 && success.Options.PackageArgs.Length > 0 && success.Options.PackageLibrary == null && !success.Options.AllLibraries && !success.Options.FormatExplicitlySet && !success.Options.IsRawOutput)
                        {
                            var target = PackageExtractor.ParsePackageTarget(success.Options.PackageArgs[0]);
                            var pkg = target.IsLocalFile
                                ? target.OriginalArgument
                                : PackageExtractor.ParsePackageReference(target.OriginalArgument).name;
                            TipWriter.WritePackageTips(pkg, success.Options.TipLevel, success.Verbosity);
                        }

                        return exitCode;
                    }

                default:
                    return 1;
            }
        });

        return packageCommand;
    }

    /// <summary>
    /// Creates the host-neutral package query subcommand.
    /// </summary>
    public static Command CreatePackageQueryCommand(
        SharedOptions opts,
        Command packageCommand,
        Argument<string[]> inheritedPackageArgument,
        Option<bool> inheritedPrereleaseOption,
        Option<string?> inheritedTfmOption)
    {
        var queryCommand = new Command(
            "query",
            "Query exact package IDs or package-ID prefixes");

        var inputArg = new Argument<string?>("package")
        {
            Description =
                "Exact package ID or literal package-ID prefix ending in '*'",
            Arity = ArgumentArity.ZeroOrOne
        };
        var takeOption = new Option<string[]>("--take")
        {
            Description =
                "Maximum package candidates to inspect "
                + $"(otherwise default {PackageQuery.DefaultMaximumCandidates}; "
                + $"{PackageQuery.MaximumPackageContentCandidates} for "
                + "package-content queries; 5 for --library-literal; maximum "
                + $"{PackageQueryOptions.MaximumCandidates})",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = false
        };
        var prereleaseOption = new Option<bool>("--preview")
        {
            Description = "Include prerelease versions"
        };
        prereleaseOption.Aliases.Add("--prerelease");
        var nuspecOnlyOption = new Option<bool>("--nuspec-only")
        {
            Description =
                "Reject queries that require package archive content"
        };
        var libraryLiteralOption = new Option<string?>("--library-literal")
        {
            Description =
                "Match packages whose selected primary implementation library "
                + "contains this exact ordinal decoded IL string substring; "
                + "requires --tfm",
            Arity = ArgumentArity.ExactlyOne
        };
        var queryTfmOption = new Option<string?>("--tfm")
        {
            Description =
                "Select the primary implementation library by TFM "
                + "(required with --library-literal)"
        };
        var compactOption = new Option<bool>("--compact")
        {
            Description = "Minified JSON (use with --format json or --envelope)"
        };
        queryCommand.Arguments.Add(inputArg);
        queryCommand.Options.Add(takeOption);
        queryCommand.Options.Add(prereleaseOption);
        queryCommand.Options.Add(nuspecOnlyOption);
        queryCommand.Options.Add(libraryLiteralOption);
        queryCommand.Options.Add(queryTfmOption);
        queryCommand.Options.Add(opts.RowWhere);
        opts.AddJsonOptionTo(queryCommand);
        opts.AddFormatOptionTo(
            queryCommand,
            CliPresentationFormat.Markdown);
        queryCommand.Options.Add(compactOption);
        opts.AddTableOptionsTo(queryCommand);
        queryCommand.Options.Add(opts.Limit);
        queryCommand.Options.Add(opts.Rows);
        queryCommand.Options.Add(opts.Head);
        queryCommand.Options.Add(opts.Tail);
        queryCommand.Options.Add(opts.Lines);
        queryCommand.Options.Add(opts.TailLines);
        queryCommand.Options.Add(opts.Count);
        queryCommand.Options.Add(opts.Fields);
        queryCommand.Options.Add(opts.Columns);
        queryCommand.Options.Add(opts.Discover);
        queryCommand.Options.Add(opts.QueryHelp);
        queryCommand.Options.Add(opts.Select);
        queryCommand.Options.Add(opts.Tree);
        opts.AddNuGetOptionsTo(queryCommand);
        opts.AddEnvelopeOptionTo(
            queryCommand,
            opts.Limit,
            opts.Rows,
            opts.Head,
            opts.Tail,
            opts.Lines,
            opts.TailLines,
            opts.Count,
            opts.Discover,
            opts.QueryHelp,
            opts.Select);
        queryCommand.Validators.Add(result =>
        {
            if (result.GetResult(compactOption) is { Implicit: false }
                && !opts.IsJsonOutput(result)
                && !result.GetValue(opts.Envelope))
            {
                result.AddError(
                    "--compact requires package query --format json or --envelope.");
            }
            if (opts.IsJsonOutput(result)
                && result.GetValue(opts.Tree)
                && result.GetResult(opts.Discover) is not { Implicit: false })
            {
                result.AddError(
                    "--tree with package query --format json requires schema discovery.");
            }
        });

        queryCommand.SetAction(async (parseResult, ct) =>
        {
            var acceptedParentOptions = new HashSet<Option>
            {
                opts.Envelope,
                opts.Format,
                opts.NoHeaders,
                opts.Info,
                opts.Limit,
                opts.Count,
                opts.Source,
                opts.AddSource,
                opts.NuGetConfig,
                opts.Print,
                opts.Value,
                opts.Urls,
                opts.Paths,
                opts.Rows,
                opts.Head,
                opts.Tail,
                opts.Lines,
                opts.TailLines,
                opts.Fields,
                opts.Columns,
                opts.Discover,
                opts.Select,
                opts.Tree,
                opts.QueryHelp,
                inheritedPrereleaseOption,
                inheritedTfmOption,
            };
            var unsupportedParentOption = packageCommand.Options.FirstOrDefault(
                option => !acceptedParentOptions.Contains(option)
                    && parseResult.GetResult(option) is { Implicit: false });
            if (unsupportedParentOption is not null)
            {
                CommandError.Write(
                    $"{unsupportedParentOption.Name} is not available with package query.");
                return 1;
            }

            if (parseResult.GetValue(inheritedPackageArgument) is { Length: > 0 })
            {
                CommandError.Write(
                    "A package inspection target is not available with package query; "
                    + "place 'query' immediately after 'package'.");
                return 1;
            }

            NuGetSourceOptions sourceOptions =
                opts.ParseNuGetSourceOptions(parseResult);
            if (sourceOptions.Sources.Length > 0
                || sourceOptions.AdditionalSources.Length > 0
                || sourceOptions.ConfigFile is not null)
            {
                CommandError.Write(
                    "Package Query currently uses NuGet.org and cannot be combined "
                    + "with source overrides.");
                return 1;
            }

            string[]? discover = opts.ParseDiscover(parseResult);
            bool envelopeOutput = parseResult.GetValue(opts.Envelope);
            OutputFormat format =
                envelopeOutput
                    ? OutputFormat.Json
                    : opts.ResolveFormat(parseResult);
            if (format == OutputFormat.Json
                && parseResult.GetValue(opts.Tree)
                && discover is null)
            {
                CommandError.Write(
                    "--tree with package query --format json requires schema discovery.");
                return 1;
            }
            string? libraryLiteral =
                parseResult.GetValue(libraryLiteralOption);
            string? inheritedTfm =
                parseResult.GetValue(inheritedTfmOption);
            string? queryTfm =
                parseResult.GetValue(queryTfmOption);
            if (inheritedTfm is not null
                && queryTfm is not null
                && !inheritedTfm.Equals(
                    queryTfm,
                    StringComparison.OrdinalIgnoreCase))
            {
                CommandError.Write(
                    "Package Query received conflicting --tfm values.");
                return 1;
            }
            if (!CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                    parseResult,
                    "Package Query",
                    out RowSelectionIntent<string>? rowSelection,
                    out string? rowSelectionError))
            {
                CommandError.Write(rowSelectionError!);
                return 1;
            }
            if (discover is not null)
            {
                if (libraryLiteral is not null)
                {
                    CommandError.Write(
                        "--library-literal is not available with schema discovery.");
                    return 1;
                }

                var discoveryOptions = new PackageQueryOptions
                {
                    Plan = ((PackageQueryPlanResult.Accepted)PackageQuery.PlanInput(
                        "dotnet-inspect",
                        maximumMatches: null,
                        rowSelection: rowSelection)).Plan,
                    Discover = discover,
                    Tree = opts.ParseTree(parseResult),
                    JsonOutput = format == OutputFormat.Json,
                    Tabular = opts.ResolveTabular(parseResult),
                    Tsv = opts.ResolveTsv(parseResult),
                    Jsonl = opts.ResolveJsonl(parseResult),
                    NoHeader = parseResult.GetValue(opts.NoHeaders),
                    Columns = opts.ParseColumns(parseResult),
                    Fields = opts.ParseFields(parseResult),
                    Count = parseResult.GetValue(opts.Count),
                };
                return await PackageQueryCommand.ExecuteAsync(
                    discoveryOptions,
                    new CommandContext(verbose: false),
                    ct);
            }

            string? input = parseResult.GetValue(inputArg);
            if (string.IsNullOrWhiteSpace(input))
            {
                CommandError.Write(
                    "Package Query requires an exact package ID or a literal "
                    + "package-ID prefix ending in '*'.");
                return 1;
            }

            string[]? select = opts.ParseSelect(parseResult);
            HashSet<string>? includeSections = null;
            if (select is not null)
            {
                SelectResult selection = SelectResolver.ResolveSelectAsSections(
                    select,
                    PackageQuerySections.Catalog.SelectableSectionNames,
                    categories:
                        PackageQuerySections.Catalog.SelectionCategoryMap);
                if (SelectOutput.WriteUnresolved(selection))
                    return 1;
                includeSections = selection.Sections;
                if (parseResult.GetValue(opts.Count)
                    && (includeSections is not { Count: 1 }
                        || !includeSections.Contains(PackageProfileSections.Packages)))
                {
                    CommandError.Write(
                        "Package Query --count supports the Packages section only.");
                    return 1;
                }
                if (!parseResult.GetValue(opts.Count)
                    && !OutputFormatResolver.ValidateSingleSectionForTabular(
                        opts.IsTableExplicitlySet(parseResult),
                        includeSections))
                    return 1;
            }

            if (!PackageQueryOptions.TryCreate(
                    input,
                    parseResult.GetValue(opts.RowWhere) ?? [],
                    parseResult.GetValue(nuspecOnlyOption),
                    CliExecutionBoundCommandRegistry.GetPreparedValue(parseResult),
                    rowSelection,
                    parseResult.GetValue(inheritedPrereleaseOption)
                        || parseResult.GetValue(prereleaseOption),
                    libraryLiteral,
                    queryTfm ?? inheritedTfm,
                    out PackageQueryOptions? options,
                    out OptionError error))
            {
                CommandError.Write(error);
                return 1;
            }

            options = options! with
            {
                Count = parseResult.GetValue(opts.Count),
                JsonOutput = format == OutputFormat.Json,
                EnvelopeOutput = envelopeOutput,
                CompactJson = parseResult.GetValue(compactOption),
                Tabular =
                    !envelopeOutput && opts.ResolveTabular(parseResult),
                Tsv = !envelopeOutput && opts.ResolveTsv(parseResult),
                Jsonl = !envelopeOutput && opts.ResolveJsonl(parseResult),
                NoHeader = parseResult.GetValue(opts.NoHeaders),
                Columns = opts.ParseColumns(parseResult),
                Fields = opts.ParseFields(parseResult),
                IncludeSections = includeSections,
                SelectDefault = opts.ParseSelectDefault(parseResult),
            };
            if (options.LibraryLiteralPlan is not null
                && includeSections?.Contains(
                    PackageQuerySections.QuerySummaryName) == true)
            {
                CommandError.Write(
                    "Query Summary is not available with --library-literal.");
                return 1;
            }
            return await PackageQueryCommand.ExecuteAsync(
                options,
                new CommandContext(verbose: false),
                ct);
        });

        CliRowSelectionCommandRegistry.Register(
            queryCommand,
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
            _ => true,
            validateLowering: (result, lowering) =>
                CliRowSelectionValidation.ValidateLineSelectionForOutput(
                    opts.IsJsonDocumentOutput(result),
                    lowering));
        CliExecutionBoundCommandRegistry.Register(
            queryCommand,
            takeOption,
            result => result.GetValue(libraryLiteralOption) is null
                ? PackageQueryOptions.MaximumCandidates
                : PackageAcquisitionPopulation.MaximumCandidates,
            isActive: static _ => true);

        return queryCommand;
    }
}
