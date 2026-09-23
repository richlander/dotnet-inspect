using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>
/// Parser for the package command options.
/// Extracts options and builds InspectionOptions for package inspection.
/// </summary>
public static class PackageOptionsParser
{
    /// <summary>
    /// Arguments container for package command options.
    /// </summary>
    public record PackageCommandArgs(
        Argument<string[]> PackageNameArg,
        Option<bool> DependenciesOption,
        Option<bool> LayoutOption,
        Option<string[]> PathOption,
        Option<bool> TfmsOption,
        Option<bool> LibOption,
        Option<bool> ToolsOption,
        Option<string?> LibraryOption,
        Option<bool> NamesakeLibraryOption,
        Option<bool> AllLibrariesOption,
        Option<bool> VersionsOption,
        Option<bool> VersionsWithFeedOption,
        Option<bool> PrereleaseOption,
        Option<bool> IncludeUnlistedOption,
        Option<bool> ContentOption,
        Option<bool> FrontmatterOption,
        Option<bool> BodyOption,
        Option<string?> TfmOption,
        Option<string?> DepthOption,
        Option<string?> TypeFilterOption,
        Option<bool> DetailsOption,
        Option<string?> VersionOption,
        Option<bool> LinesOption,
        Option<bool> TailLinesOption,
        Option<string?> OutOption,
        Option<string?> PathMatchOption,
        Option<bool> SkipEmptyOption,
        Option<bool> RootsOption,
        Option<bool> NoHeaderOption,
        Option<string?> WorkspaceOption,
        Option<string?> ShareOption,
        Option<string?>? EvidenceEnvelopeOption);

    /// <summary>
    /// Result of parsing package command options.
    /// </summary>
    public abstract record PackageParseResult;

    /// <summary>
    /// Indicates an unrecognized option was found in positional args.
    /// </summary>
    public record UnrecognizedOption(string Option) : PackageParseResult;

    public record InvalidArguments(string Message) : PackageParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(InspectionOptions Options, Verbosity Verbosity) : PackageParseResult;

    internal static string? GetUnrecognizedOption(
        ParseResult parseResult,
        PackageCommandArgs args) =>
        (parseResult.GetValue(args.PackageNameArg) ?? [])
            .FirstOrDefault(argument => argument.StartsWith('-'));

    internal static int GetPositionalCapacity(
        ParseResult result,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        // Only mode facts are needed here; full option parsing owns format, projection,
        // and row-selection validation and must not run during argv ownership checks.
        var mode = new InspectionOptions
        {
            ExplicitVersion = GetExplicitVersion(result, args),
            ListVersions = result.GetValue(args.VersionsOption)
                || result.GetValue(args.VersionsWithFeedOption),
            ListLayout = result.GetValue(args.LayoutOption) && !opts.IsDiscoveryMode(result),
            ListTfms = result.GetValue(args.TfmsOption),
            Print = result.GetValue(opts.Print),
            Value = result.GetValue(opts.Value),
            Urls = result.GetValue(opts.Urls),
            Paths = result.GetValue(opts.Paths),
            Roots = result.GetValue(args.RootsOption),
            ShowDependencies = result.GetValue(args.DependenciesOption),
            Tree = result.GetValue(opts.Tree),
            Discover = opts.ParseDiscover(result),
            Count = result.GetValue(opts.Count),
            PackageLibrary = result.GetValue(args.NamesakeLibraryOption)
                ? ""
                : GetExactLibrary(result.CommandResult, args),
            AllLibraries = IsAggregateLibraryTarget(
                result.CommandResult,
                args),
            NamesakeLibrary =
                result.GetValue(args.NamesakeLibraryOption),
        };
        return PackageCommand.GetMultiPackageConflicts(mode).Count > 0
            ? 1
            : args.PackageNameArg.Arity.MaximumNumberOfValues;
    }

    private static string? GetExplicitVersion(
        ParseResult result,
        PackageCommandArgs args)
    {
        OptionResult? optionResult =
            result.GetResult(args.VersionOption);
        return optionResult is { Implicit: false }
            && !optionResult.Errors.Any()
                ? result.GetValue(args.VersionOption)
                : null;
    }

    private static string? GetExactLibrary(
        CommandResult result,
        PackageCommandArgs args)
    {
        if (result.GetResult(args.LibraryOption)
            is not { Implicit: false })
        {
            return null;
        }

        string? library = result.GetValue(args.LibraryOption);
        return string.IsNullOrWhiteSpace(library)
            ? null
            : library;
    }

    private static bool IsAggregateLibraryTarget(
        CommandResult result,
        PackageCommandArgs args) =>
        result.GetResult(args.AllLibrariesOption)
            is { Implicit: false }
        || (result.GetResult(args.LibraryOption)
                is { Implicit: false }
            && string.IsNullOrWhiteSpace(
                result.GetValue(args.LibraryOption)));

    private static bool HasLibraryTarget(
        CommandResult result,
        PackageCommandArgs args) =>
        result.GetResult(args.LibraryOption)
            is { Implicit: false }
        || result.GetValue(args.NamesakeLibraryOption)
        || result.GetResult(args.AllLibrariesOption)
            is { Implicit: false };

    private static bool HasExactLibraryTarget(
        CommandResult result,
        PackageCommandArgs args) =>
        result.GetValue(args.NamesakeLibraryOption)
        || GetExactLibrary(result, args) is not null;

    /// <summary>
    /// Parses package command options.
    /// </summary>
    public static PackageParseResult Parse(
        ParseResult parseResult,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        var packageArgs = parseResult.GetValue(args.PackageNameArg) ?? [];

        if (parseResult.GetResult(args.AllLibrariesOption)
            is { Implicit: false })
        {
            return new InvalidArguments(
                "'--all-libraries' is no longer valid. Use '--library' "
                    + "to inspect the selected compile-Library aggregate.");
        }

        // Check for unrecognized options in positional args
        var badOption = GetUnrecognizedOption(parseResult, args);
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        string? explicitVersion =
            parseResult.GetValue(args.VersionOption);
        bool hasExplicitVersionSelector =
            parseResult.GetResult(args.VersionOption)
                is { Implicit: false };
        if (hasExplicitVersionSelector
            && !PackageExtractor.TryNormalizePackageVersion(
                explicitVersion,
                out _))
        {
            return new InvalidArguments(
                "--version requires an exact Package version.");
        }

        PackageReferenceTarget? selectedTarget =
            packageArgs is [var selectedPackageReference]
                ? PackageExtractor.ParsePackageTarget(
                    selectedPackageReference)
                : null;
        if (explicitVersion is not null
            && selectedTarget is not null)
        {
            if (selectedTarget.IsLocalFile)
            {
                return new InvalidArguments(
                    "--version cannot be combined with a local Package file.");
            }

            if (selectedTarget.Version.Length > 0)
            {
                return new InvalidArguments(
                    "--version cannot be combined with a versioned Package coordinate.");
            }
        }
        bool namesakeLibrary =
            parseResult.GetValue(args.NamesakeLibraryOption);
        bool explicitLibrary =
            parseResult.GetResult(args.LibraryOption)
                is { Implicit: false };
        if (namesakeLibrary && explicitLibrary)
        {
            return new InvalidArguments(
                "--namesake-library cannot be combined with --library.");
        }
        bool allLibraries = IsAggregateLibraryTarget(
            parseResult.CommandResult,
            args);
        var packageLibrary = namesakeLibrary
            ? ""
            : GetExactLibrary(parseResult.CommandResult, args);

        bool showVersionsWithFeed =
            parseResult.GetValue(args.VersionsWithFeedOption);
        bool showVersionList =
            parseResult.GetValue(args.VersionsOption);
        bool countRange = false;
        if (parseResult.GetValue(opts.Count)
            && packageArgs is [var packageReference])
        {
            countRange =
                PackageVersionRange.TryParse(
                    packageReference,
                    out _,
                    out string? countRangeError)
                && countRangeError is null;
        }
        bool showPluralVersions =
            showVersionsWithFeed
            || showVersionList;
        bool selectsVersionPopulation =
            showPluralVersions
            || countRange;
        if (showVersionList && showVersionsWithFeed)
        {
            return new InvalidArguments(
                "--versions and --versions-with-feed cannot be combined "
                + "with each other or --version.");
        }
        if (selectsVersionPopulation && hasExplicitVersionSelector)
        {
            return new InvalidArguments(
                "--versions, --versions-with-feed, and range --count "
                + "cannot be combined with --version.");
        }

        bool showVersions =
            showPluralVersions
            || countRange;
        bool selectsSourceLinkFiles =
            IsSourceLinkFileRowSelection(
                parseResult,
                opts,
                args);
        bool selectsPackageFiles =
            IsPackageFileRowSelection(
                parseResult,
                opts,
                args);
        bool selectsPackageLayout =
            IsPackageLayoutRowSelection(
                parseResult,
                opts,
                args);
        bool selectsPackageTfms =
            IsPackageTfmRowSelection(
                parseResult,
                opts,
                args);
        bool selectsEcosystemDependencies =
            IsEcosystemDependencyRowSelection(
                parseResult,
                opts,
                args);
        if (opts.IsJsonDocumentOutput(parseResult)
            && IsMultiSectionEcosystemDependencySelection(
                parseResult.CommandResult,
                opts,
                args)
            && HasExplicitRowSelection(parseResult, opts))
        {
            return new InvalidArguments(
                "Rendered-line selection cannot be combined with JSON output.");
        }

        bool selectsCloneCandidateRows =
            IsCloneCandidateRowSelection(
                parseResult,
                opts,
                args);
        RowSelectionIntent<string>? versionRowSelection = null;
        if (selectsVersionPopulation
            && !CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Package version",
                out versionRowSelection,
                out string? rowSelectionError))
        {
            return new InvalidArguments(
                rowSelectionError!);
        }

        RowSelectionIntent<string>? sourceLinkFileRowSelection = null;
        if (selectsSourceLinkFiles
            && !CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Package SourceLink file",
                out sourceLinkFileRowSelection,
                out string? sourceLinkRowSelectionError))
        {
            return new InvalidArguments(
                sourceLinkRowSelectionError!);
        }

        RowSelectionIntent<string>? packageFileRowSelection = null;
        if (selectsPackageFiles
            && !CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Package file",
                out packageFileRowSelection,
                out string? packageFileRowSelectionError))
        {
            return new InvalidArguments(
                packageFileRowSelectionError!);
        }

        RowSelectionIntent<string>? packageLayoutRowSelection = null;
        if (selectsPackageLayout
            && !CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Package layout file",
                out packageLayoutRowSelection,
                out string? packageLayoutRowSelectionError))
        {
            return new InvalidArguments(
                packageLayoutRowSelectionError!);
        }

        RowSelectionIntent<string>? packageTfmRowSelection = null;
        if (selectsPackageTfms
            && !CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Package TFM",
                out packageTfmRowSelection,
                out string? packageTfmRowSelectionError))
        {
            return new InvalidArguments(
                packageTfmRowSelectionError!);
        }

        RowSelectionIntent<string>? ecosystemDependencyRowSelection = null;
        if (selectsEcosystemDependencies
            && !CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Package ecosystem dependency",
                out ecosystemDependencyRowSelection,
                out string? ecosystemDependencyRowSelectionError))
        {
            return new InvalidArguments(
                ecosystemDependencyRowSelectionError!);
        }

        RowSelectionIntent<string>? cloneCandidateRowSelection = null;
        if (selectsCloneCandidateRows
            && !CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Clone Candidates",
                out cloneCandidateRowSelection,
                out string? cloneCandidateRowSelectionError))
        {
            return new InvalidArguments(
                cloneCandidateRowSelectionError!);
        }

        RowSelectionIntent<string>? packageSectionRowSelection = null;
        if (!selectsVersionPopulation
            && !selectsSourceLinkFiles
            && !selectsPackageFiles
            && !selectsPackageLayout
            && !selectsPackageTfms
            && !selectsCloneCandidateRows
            && !CliRowSelectionCommandRegistry.TryGetPreparedSemanticIntent(
                parseResult,
                "Package section",
                out packageSectionRowSelection,
                out string? packageSectionRowSelectionError))
        {
            return new InvalidArguments(
                packageSectionRowSelectionError!);
        }
        packageSectionRowSelection =
            DependencyQueryOptions.AppendLegacyRows(
                parseResult,
                opts,
                packageSectionRowSelection,
                out int? legacyHierarchyWindowStageIndex);
        int? dependencyDepth =
            int.TryParse(
                parseResult.GetValue(args.DepthOption),
                out int parsedDependencyDepth)
                ? parsedDependencyDepth
                : null;
        if (!DependencyQueryOptions.TryResolve(
                DependencyQueryRouteKind.PackageHierarchy,
                [],
                null,
                packageSectionRowSelection,
                dependencyDepth,
                out DependencyQueryPlan dependencyQueryPlan,
                out OptionError dependencyQueryError))
        {
            return new InvalidArguments(
                dependencyQueryError.Message);
        }

        var verbosity = opts.ParseVerbosity(parseResult);
        bool frontmatterRequested = parseResult.GetValue(args.FrontmatterOption);
        bool bodyRequested = parseResult.GetValue(args.BodyOption);
        var contentScope = frontmatterRequested
            ? PackageFileContentScope.Frontmatter
            : bodyRequested
                ? PackageFileContentScope.Body
                : PackageFileContentScope.Full;
        bool rawOutput = parseResult.GetValue(opts.Raw);
        bool explicitTabularOutput = opts.IsTableExplicitlySet(parseResult);
        bool suppressImplicitRowFormat = rawOutput && !opts.IsTableFlagExplicitlySet(parseResult);
        var outputFormat = opts.ResolveFormat(parseResult);

        // --path scopes the file listing and selects the Files section. A bare
        // --path (present without a value) means the whole package (root and below);
        // an explicit /, directory, file, or glob narrows it.
        string? pathFilter = null;
        string[]? pathFilters = null;
        if (parseResult.GetResult(args.PathOption) is { Implicit: false })
        {
            var values = parseResult.GetValue(args.PathOption) ?? [];
            pathFilters = values.Length == 0
                ? ["**"]
                : [.. values
                    .SelectMany(SplitPathSelectors)
                    .Select(ArgumentPreprocessor.UnescapeAtCategoryValue)
                    .Where(value => !string.IsNullOrWhiteSpace(value))];
            if (pathFilters.Length == 0)
                pathFilters = ["**"];
            pathFilter = pathFilters.Length == 1 ? pathFilters[0] : null;
        }

        var typeFilter = parseResult.GetValue(args.TypeFilterOption);

        var options = new InspectionOptions
        {
            PackageArgs = packageArgs,
            ExplicitVersion = explicitVersion,
            WorkspacePacket = parseResult.GetValue(args.WorkspaceOption),
            ShareFormat = WorkspaceShareOption.Parse(
                parseResult,
                args.ShareOption),
#if DEBUG
            EvidenceEnvelopePath =
                parseResult.GetValue(args.EvidenceEnvelopeOption!),
#endif
            ShowDependencies = parseResult.GetValue(args.DependenciesOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            DependencyQueryPlan = dependencyQueryPlan,
            DependencyHierarchyLegacyWindowStageIndex =
                legacyHierarchyWindowStageIndex,
            TypeFilter = typeFilter,
            PackageLibrary = packageLibrary,
            AllLibraries = allLibraries,
            NamesakeLibrary = namesakeLibrary,
            ListLayout = parseResult.GetValue(args.LayoutOption) && !opts.IsDiscoveryMode(parseResult),
            ListLayoutExplicitlySet =
                parseResult.GetValue(args.LayoutOption),
            PathFilter = pathFilter,
            PathFilters = pathFilters,
            PathMatchMode = parseResult.GetValue(args.PathMatchOption) ?? "all",
            SkipEmpty = parseResult.GetValue(args.SkipEmptyOption),
            ListTfms = parseResult.GetValue(args.TfmsOption),
            ScopeLib = parseResult.GetValue(args.LibOption),
            ScopeTools = parseResult.GetValue(args.ToolsOption),
            ListVersions = showVersions,
            ListVersionsWithFeed = showVersionsWithFeed,
            IncludePrerelease = parseResult.GetValue(args.PrereleaseOption),
            IncludeUnlisted = parseResult.GetValue(args.IncludeUnlistedOption),
            Print = parseResult.GetValue(opts.Print),
            PrintRow = opts.ParsePrintRow(parseResult),
            Value = parseResult.GetValue(opts.Value),
            Urls = parseResult.GetValue(opts.Urls),
            Paths = parseResult.GetValue(opts.Paths),
            Roots = parseResult.GetValue(args.RootsOption),
            JsonArray = parseResult.GetValue(opts.JsonArray),
            ShowContent = parseResult.GetValue(args.ContentOption),
            ContentScope = contentScope,
            FrontmatterRequested = frontmatterRequested,
            BodyRequested = bodyRequested,
            OutputPath = parseResult.GetValue(args.OutOption),
            VersionRowSelection = versionRowSelection,
            SourceLinkFileRowSelection = sourceLinkFileRowSelection,
            PackageFileRowSelection = packageFileRowSelection,
            PackageLayoutRowSelection = packageLayoutRowSelection,
            PackageTfmRowSelection = packageTfmRowSelection,
            EcosystemDependencyRowSelection =
                ecosystemDependencyRowSelection,
            CloneCandidateRowSelection = cloneCandidateRowSelection,
            Format = outputFormat,
            JsonOutput = outputFormat == OutputFormat.Json,
            Raw = rawOutput,
            Tabular = suppressImplicitRowFormat ? false : opts.ResolveTabular(parseResult),
            Tsv = suppressImplicitRowFormat ? false : opts.ResolveTsv(parseResult),
            Jsonl = suppressImplicitRowFormat ? false : opts.ResolveJsonl(parseResult),
            PreferRenderedUrls = parseResult.GetValue(opts.PreferRenderedUrls),
            TabularExplicitlySet = suppressImplicitRowFormat ? false : explicitTabularOutput,
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = verbosity,
            Discover = opts.ParseDiscover(parseResult),
            DiscoverDetails =
                parseResult.GetValue(args.DetailsOption),
            Tree = parseResult.GetValue(opts.Tree),
            Select = opts.ParseSelect(parseResult),
            SelectDefault = opts.ParseSelectDefault(parseResult),
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            FieldsExplicitlySet =
                parseResult.GetResult(opts.Fields) is { Implicit: false },
            Schema = opts.ParseSchema(parseResult),
            Count = parseResult.GetValue(opts.Count),
            EnvelopeOutput = parseResult.GetValue(opts.Envelope),
            Rows = selectsVersionPopulation
                || selectsSourceLinkFiles
                || selectsPackageFiles
                || selectsPackageLayout
                || selectsPackageTfms
                || selectsEcosystemDependencies
                || selectsCloneCandidateRows
                ? null
                : opts.ParseRows(parseResult),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
        };

        // Captured before the sugar below rewrites Select, so it reflects what the caller typed.
        options = options with { SelectExplicitlySet = options.Select is { Length: > 0 } || options.SelectDefault };

        // --path is sugar for selecting the Files section (which carries path + size).
        if (pathFilter != null)
            options = options with { Select = [.. options.Select ?? [], Views.PackageSections.Files] };
        else if (pathFilters != null)
            options = options with { Select = [.. options.Select ?? [], Views.PackageSections.Files] };
        if (!string.IsNullOrWhiteSpace(typeFilter))
            options = options with { Select = [.. options.Select ?? [], Views.PackageSections.SourceLinkFiles] };

        var tipLevel = options.FormatExplicitlySet || options.IsRawOutput || verbosity != Verbosity.Minimal || options.Select != null || options.SelectDefault || options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
            ? TipLevel.Quiet : opts.ParseTipLevel(parseResult);
        options = options with { TipLevel = tipLevel };

        return new Success(options, verbosity);
    }

    internal static bool IsSourceLinkFileRowSelection(
        ParseResult parseResult,
        SharedOptions opts,
        PackageCommandArgs args)
        => IsSourceLinkFileRowSelection(
            parseResult.CommandResult,
            opts,
            args);

    internal static bool IsSourceLinkFileRowSelection(
        CommandResult result,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        string[] packageArgs =
            result.GetValue(args.PackageNameArg) ?? [];
        if (packageArgs.Length != 1
            || result.GetResult(opts.Discover)
                is { Implicit: false }
            || result.GetValue(args.DependenciesOption)
            || result.GetValue(args.LayoutOption)
            || result.GetResult(args.PathOption)
                is { Implicit: false }
            || result.GetValue(args.TfmsOption)
            || HasLibraryTarget(result, args)
            || result.GetValue(args.VersionsOption)
            || result.GetValue(args.VersionsWithFeedOption)
            || result.GetValue(args.ContentOption))
        {
            return false;
        }

        if (result.GetValue(opts.Count)
            && packageArgs is [var packageReference]
            && PackageVersionRange.TryParse(
                packageReference,
                out _,
                out string? countRangeError)
            && countRangeError is null)
        {
            return false;
        }

        string[]? selectors =
            ParseSelectors(result.GetValue(opts.Select));
        string? typeFilter =
            result.GetValue(args.TypeFilterOption);
        if (!string.IsNullOrWhiteSpace(typeFilter))
        {
            selectors =
            [
                .. selectors ?? [],
                Views.PackageSections.SourceLinkFiles,
            ];
        }

        if (selectors is not { Length: > 0 })
            return false;

        var catalog = PackageSectionDescriptors.CreateCatalog();
        var sections = catalog.Sections;
        var resolved = SelectResolver.ResolveSelectAsSections(
            selectors,
            sections.SelectableSectionNames,
            sections.InfoSectionNames,
            sections.SelectionCategoryMap,
            selectDefault: false);
        return !resolved.HasError
            && resolved.Sections is { Count: 1 } selected
            && selected.Contains(
                Views.PackageSections.SourceLinkFiles);
    }

    internal static bool IsPackageFileRowSelection(
        ParseResult parseResult,
        SharedOptions opts,
        PackageCommandArgs args)
        => IsPackageFileRowSelection(
            parseResult.CommandResult,
            opts,
            args);

    internal static bool IsPackageFileRowSelection(
        CommandResult result,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        string[] packageArgs =
            result.GetValue(args.PackageNameArg) ?? [];
        if (packageArgs.Length != 1
            || result.GetResult(opts.Discover)
                is { Implicit: false }
            || result.GetValue(args.DependenciesOption)
            || result.GetValue(args.LayoutOption)
            || result.GetValue(args.TfmsOption)
            || HasLibraryTarget(result, args)
            || result.GetValue(args.VersionsOption)
            || result.GetValue(args.VersionsWithFeedOption)
            || result.GetValue(args.ContentOption))
        {
            return false;
        }

        if (result.GetValue(opts.Count)
            && packageArgs is [var packageReference]
            && PackageVersionRange.TryParse(
                packageReference,
                out _,
                out string? countRangeError)
            && countRangeError is null)
        {
            return false;
        }

        string[]? selectors =
            ParseSelectors(result.GetValue(opts.Select));
        if (result.GetResult(args.PathOption)
            is { Implicit: false })
        {
            selectors =
            [
                .. selectors ?? [],
                Views.PackageSections.Files,
            ];
        }

        if (selectors is not { Length: > 0 })
            return false;

        var catalog = PackageSectionDescriptors.CreateCatalog();
        var sections = catalog.Sections;
        var resolved = SelectResolver.ResolveSelectAsSections(
            selectors,
            sections.SelectableSectionNames,
            sections.InfoSectionNames,
            sections.SelectionCategoryMap,
            selectDefault: false);
        if (resolved.HasError
            || resolved.Sections is not { Count: 1 } selected)
        {
            return false;
        }

        return selected.Contains(
            Views.PackageSections.Files);
    }

    internal static bool IsEcosystemDependencyRowSelection(
        ParseResult parseResult,
        SharedOptions opts,
        PackageCommandArgs args) =>
        IsEcosystemDependencyRowSelection(
            parseResult.CommandResult,
            opts,
            args);

    internal static bool IsEcosystemDependencyRowSelection(
        CommandResult result,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        if (!IsOrdinaryPackageInspection(result, opts, args))
            return false;

        string[]? selectors =
            ParseSelectors(result.GetValue(opts.Select));
        if (selectors is not { Length: > 0 })
            return false;

        var catalog = PackageSectionDescriptors.CreateCatalog();
        var sections = catalog.Sections;
        var resolved =
            SelectResolver.ResolveSelectAsSections(
                selectors,
                sections.SelectableSectionNames,
                sections.InfoSectionNames,
                sections.SelectionCategoryMap,
                selectDefault: false);
        return !resolved.HasError
            && resolved.Sections is { Count: 1 } selected
            && selected.Contains(
                Views.PackageSections.EcosystemDependencies);
    }

    internal static bool IsMultiSectionEcosystemDependencySelection(
        CommandResult result,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        if (!IsOrdinaryPackageInspection(result, opts, args))
            return false;

        string[]? selectors =
            ParseSelectors(result.GetValue(opts.Select));
        if (selectors is not { Length: > 0 })
            return false;

        var catalog = PackageSectionDescriptors.CreateCatalog();
        var sections = catalog.Sections;
        var resolved =
            SelectResolver.ResolveSelectAsSections(
                selectors,
                sections.SelectableSectionNames,
                sections.InfoSectionNames,
                sections.SelectionCategoryMap,
                selectDefault: false);
        return !resolved.HasError
            && resolved.Sections is { Count: > 1 } selected
            && selected.Contains(
                Views.PackageSections.EcosystemDependencies);
    }

    private static bool IsOrdinaryPackageInspection(
        CommandResult result,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        string[] packageArgs =
            result.GetValue(args.PackageNameArg) ?? [];
        return packageArgs.Length == 1
            && result.GetResult(opts.Discover)
                is not { Implicit: false }
            && !result.GetValue(args.DependenciesOption)
            && !result.GetValue(args.LayoutOption)
            && result.GetResult(args.PathOption)
                is not { Implicit: false }
            && !result.GetValue(args.TfmsOption)
            && !HasLibraryTarget(result, args)
            && !result.GetValue(args.VersionsOption)
            && !result.GetValue(args.VersionsWithFeedOption)
            && !result.GetValue(args.ContentOption);
    }

    private static bool HasExplicitRowSelection(
        ParseResult result,
        SharedOptions opts) =>
        result.GetResult(opts.Limit) is { Implicit: false }
        || result.GetResult(opts.Rows) is { Implicit: false }
        || result.GetResult(opts.Head) is { Implicit: false }
        || result.GetResult(opts.Tail) is { Implicit: false }
        || result.GetResult(opts.Lines) is { Implicit: false }
        || result.GetResult(opts.TailLines) is { Implicit: false };

    internal static bool IsPackageLayoutRowSelection(
        ParseResult parseResult,
        SharedOptions opts,
        PackageCommandArgs args)
        => IsPackageLayoutRowSelection(
            parseResult.CommandResult,
            opts,
            args);

    internal static bool IsPackageLayoutRowSelection(
        CommandResult result,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        string[] packageArgs =
            result.GetValue(args.PackageNameArg) ?? [];
        if (packageArgs.Length != 1
            || !result.GetValue(args.LayoutOption)
            || HasCompetingPackageLayoutIntent(result, opts, args))
        {
            return false;
        }

        string packageReference = packageArgs[0];
        if (!File.Exists(packageReference))
        {
            bool isRange = PackageVersionRange.TryParse(
                packageReference,
                out _,
                out string? rangeError);
            if (isRange || rangeError is not null)
                return false;
        }

        return true;
    }

    private static bool HasCompetingPackageLayoutIntent(
        CommandResult result,
        SharedOptions opts,
        PackageCommandArgs args)
        => result.GetResult(opts.Discover) is { Implicit: false }
            || result.GetResult(opts.Select) is { Implicit: false }
            || result.GetValue(opts.Tree)
            || result.GetValue(opts.Schema)
            || result.GetValue(opts.Envelope)
            || result.GetValue(opts.Print)
            || result.GetResult(opts.Row) is { Implicit: false }
            || result.GetValue(opts.Value)
            || result.GetValue(opts.Urls)
            || result.GetValue(opts.Paths)
            || result.GetValue(args.RootsOption)
            || result.GetValue(opts.JsonArray)
            || result.GetValue(opts.PreferRenderedUrls)
            || opts.IsTableOrTsvOutput(result)
            || result.GetValue(args.NoHeaderOption)
            || result.GetResult(opts.Fields) is { Implicit: false }
            || result.GetResult(opts.Columns) is { Implicit: false }
            || result.GetValue(args.DependenciesOption)
            || result.GetValue(args.TfmsOption)
            || result.GetResult(args.PathOption) is { Implicit: false }
            || result.GetResult(args.PathMatchOption) is { Implicit: false }
            || result.GetValue(args.SkipEmptyOption)
            || result.GetResult(args.TypeFilterOption) is { Implicit: false }
            || HasLibraryTarget(result, args)
            || result.GetValue(args.VersionsOption)
            || result.GetValue(args.VersionsWithFeedOption)
            || result.GetValue(args.IncludeUnlistedOption)
            || result.GetValue(args.ContentOption)
            || result.GetValue(args.FrontmatterOption)
            || result.GetValue(args.BodyOption);

    internal static bool IsPackageTfmRowSelection(
        ParseResult parseResult,
        SharedOptions opts,
        PackageCommandArgs args)
        => IsPackageTfmRowSelection(
            parseResult.CommandResult,
            opts,
            args);

    internal static bool IsPackageTfmRowSelection(
        CommandResult result,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        string[] packageArgs =
            result.GetValue(args.PackageNameArg) ?? [];
        if (packageArgs.Length != 1
            || !result.GetValue(args.TfmsOption)
            || HasCompetingPackageTfmIntent(result, opts, args))
        {
            return false;
        }

        string packageReference = packageArgs[0];
        if (!File.Exists(packageReference))
        {
            bool isRange = PackageVersionRange.TryParse(
                packageReference,
                out _,
                out string? rangeError);
            if (isRange || rangeError is not null)
                return false;
        }

        return true;
    }

    internal static bool IsCloneCandidateRowSelection(
        ParseResult parseResult,
        SharedOptions opts,
        PackageCommandArgs args)
        => IsCloneCandidateRowSelection(
            parseResult.CommandResult,
            opts,
            args);

    internal static bool IsCloneCandidateRowSelection(
        CommandResult result,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        string[] packageArgs =
            result.GetValue(args.PackageNameArg) ?? [];
        if (packageArgs.Length != 1
            || !HasExactLibraryTarget(result, args)
            || result.GetResult(opts.Discover)
                is { Implicit: false })
        {
            return false;
        }

        string[]? selectors =
            ParseSelectors(result.GetValue(opts.Select));
        return selectors is [var selector]
            && selector.Equals(
                SectionNames.CloneCandidates,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCompetingPackageTfmIntent(
        CommandResult result,
        SharedOptions opts,
        PackageCommandArgs args)
        => result.GetResult(opts.Discover) is { Implicit: false }
            || result.GetResult(opts.Select) is { Implicit: false }
            || result.GetValue(opts.Tree)
            || result.GetValue(opts.Schema)
            || result.GetValue(opts.Envelope)
            || result.GetValue(opts.Print)
            || result.GetResult(opts.Row) is { Implicit: false }
            || result.GetValue(opts.Value)
            || result.GetValue(opts.Urls)
            || result.GetValue(opts.Paths)
            || result.GetValue(args.RootsOption)
            || result.GetValue(opts.JsonArray)
            || result.GetValue(opts.PreferRenderedUrls)
            || (!result.GetValue(opts.Count)
                && (result.GetResult(opts.Columns) is { Implicit: false }
                    || result.GetResult(opts.Fields) is { Implicit: false }))
            || result.GetValue(args.DependenciesOption)
            || result.GetValue(args.LayoutOption)
            || result.GetValue(args.LibOption)
            || result.GetValue(args.ToolsOption)
            || result.GetResult(args.PathOption) is { Implicit: false }
            || result.GetResult(args.PathMatchOption) is { Implicit: false }
            || result.GetValue(args.SkipEmptyOption)
            || result.GetResult(args.TfmOption) is { Implicit: false }
            || result.GetResult(args.TypeFilterOption) is { Implicit: false }
            || HasLibraryTarget(result, args)
            || result.GetValue(args.VersionsOption)
            || result.GetValue(args.VersionsWithFeedOption)
            || result.GetValue(args.IncludeUnlistedOption)
            || result.GetValue(args.ContentOption)
            || result.GetValue(args.FrontmatterOption)
            || result.GetValue(args.BodyOption);

    private static string[]? ParseSelectors(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value.Split(
                [',', ';'],
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries);

    private static IEnumerable<string> SplitPathSelectors(string value)
        => value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
