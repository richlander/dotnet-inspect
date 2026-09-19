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
        Option<bool> AllLibrariesOption,
        Option<bool> VersionsOption,
        Option<bool> VersionsWithFeedOption,
        Option<bool> PrereleaseOption,
        Option<bool> IncludeUnlistedOption,
        Option<bool> ContentOption,
        Option<bool> FrontmatterOption,
        Option<bool> BodyOption,
        Option<string?> TfmOption,
        Option<string?> TypeFilterOption,
        Option<string?> VersionOption,
        Option<bool> LinesOption,
        Option<bool> TailLinesOption,
        Option<string?> OutOption,
        Option<string?> PathMatchOption,
        Option<bool> SkipEmptyOption,
        Option<bool> NoHeaderOption);

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
            ExplicitVersion = result.GetValue(args.VersionOption),
            ListVersions = result.GetResult(args.VersionOption) is { Implicit: false }
                || result.GetValue(args.VersionsOption)
                || result.GetValue(args.VersionsWithFeedOption),
            ListLayout = result.GetValue(args.LayoutOption) && !opts.IsDiscoveryMode(result),
            ListTfms = result.GetValue(args.TfmsOption),
            Print = result.GetValue(opts.Print),
            Value = result.GetValue(opts.Value),
            Urls = result.GetValue(opts.Urls),
            Paths = result.GetValue(opts.Paths),
            ShowDependencies = result.GetValue(args.DependenciesOption),
            Tree = result.GetValue(opts.Tree),
            Discover = opts.ParseDiscover(result),
            Count = result.GetValue(opts.Count),
            PackageLibrary = result.GetResult(args.LibraryOption) is { Implicit: false } ? "" : null,
            AllLibraries = result.GetValue(args.AllLibrariesOption)
        };
        return PackageCommand.GetMultiPackageConflicts(mode).Count > 0
            ? 1
            : args.PackageNameArg.Arity.MaximumNumberOfValues;
    }

    /// <summary>
    /// Parses package command options.
    /// </summary>
    public static PackageParseResult Parse(
        ParseResult parseResult,
        SharedOptions opts,
        PackageCommandArgs args)
    {
        var packageArgs = parseResult.GetValue(args.PackageNameArg) ?? [];

        // Check for unrecognized options in positional args
        var badOption = GetUnrecognizedOption(parseResult, args);
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        var explicitVersion = parseResult.GetValue(args.VersionOption);
        var libraryValue = parseResult.GetValue(args.LibraryOption);
        var packageLibrary = parseResult.GetResult(args.LibraryOption) is { Implicit: false }
            ? libraryValue ?? ""
            : null;

        bool hasExplicitVersionSelector =
            parseResult.GetResult(args.VersionOption) is { Implicit: false };
        // Bare --version (no value): treat as a version query.
        bool bareVersion =
            explicitVersion == null
            && hasExplicitVersionSelector;

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
        if (selectsVersionPopulation
            && hasExplicitVersionSelector)
        {
            return new InvalidArguments(
                "--versions, --versions-with-feed, and range --count "
                + "cannot be combined with --version.");
        }

        bool showVersions =
            bareVersion
            || showPluralVersions
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
        bool selectsPackageTfms =
            IsPackageTfmRowSelection(
                parseResult,
                opts,
                args);
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

        var verbosity = opts.ParseVerbosity(parseResult);
        bool frontmatterRequested = parseResult.GetValue(args.FrontmatterOption);
        bool bodyRequested = parseResult.GetValue(args.BodyOption);
        var contentScope = frontmatterRequested
            ? PackageFileContentScope.Frontmatter
            : bodyRequested
                ? PackageFileContentScope.Body
                : PackageFileContentScope.Full;
        bool bareOutput = parseResult.GetValue(opts.Bare);
        bool explicitTabularOutput = opts.IsTableExplicitlySet(parseResult);
        bool suppressImplicitRowFormat = bareOutput && !opts.IsTableFlagExplicitlySet(parseResult);
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
            ShowDependencies = parseResult.GetValue(args.DependenciesOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            TypeFilter = typeFilter,
            PackageLibrary = packageLibrary,
            AllLibraries = parseResult.GetValue(args.AllLibrariesOption),
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
            SingleVersionQuery = bareVersion,
            ListVersionsWithFeed = showVersionsWithFeed,
            IncludePrerelease = parseResult.GetValue(args.PrereleaseOption),
            IncludeUnlisted = parseResult.GetValue(args.IncludeUnlistedOption),
            Print = parseResult.GetValue(opts.Print),
            PrintRow = opts.ParsePrintRow(parseResult),
            Value = parseResult.GetValue(opts.Value),
            Urls = parseResult.GetValue(opts.Urls),
            Paths = parseResult.GetValue(opts.Paths),
            JsonArray = parseResult.GetValue(opts.JsonArray),
            ShowContent = parseResult.GetValue(args.ContentOption),
            ContentScope = contentScope,
            FrontmatterRequested = frontmatterRequested,
            BodyRequested = bodyRequested,
            OutputPath = parseResult.GetValue(args.OutOption),
            Limit = bareVersion ? 1 : null,
            VersionRowSelection = versionRowSelection,
            SourceLinkFileRowSelection = sourceLinkFileRowSelection,
            PackageFileRowSelection = packageFileRowSelection,
            PackageTfmRowSelection = packageTfmRowSelection,
            CloneCandidateRowSelection = cloneCandidateRowSelection,
            Format = outputFormat,
            JsonOutput = outputFormat == OutputFormat.Json,
            Bare = bareOutput,
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
                || selectsPackageTfms
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

        var tipLevel = options.FormatExplicitlySet || options.IsRawOutput || verbosity != Verbosity.Minimal || options.Select != null || options.SelectDefault || options.Discover != null || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || options.Limit != null
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
            || result.GetResult(args.LibraryOption)
                is { Implicit: false }
            || result.GetValue(args.AllLibrariesOption)
            || result.GetValue(args.VersionsOption)
            || result.GetValue(args.VersionsWithFeedOption)
            || result.GetValue(args.ContentOption)
            || (result.GetResult(args.VersionOption)
                is { Implicit: false }
                && result.GetValue(args.VersionOption) is null))
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
            || result.GetResult(args.LibraryOption)
                is { Implicit: false }
            || result.GetValue(args.AllLibrariesOption)
            || result.GetValue(args.VersionsOption)
            || result.GetValue(args.VersionsWithFeedOption)
            || result.GetValue(args.ContentOption)
            || (result.GetResult(args.VersionOption)
                is { Implicit: false }
                && result.GetValue(args.VersionOption) is null))
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
            || result.GetResult(args.LibraryOption)
                is not { Implicit: false }
            || result.GetValue(args.AllLibrariesOption)
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
            || result.GetResult(args.LibraryOption) is { Implicit: false }
            || result.GetValue(args.AllLibrariesOption)
            || result.GetValue(args.VersionsOption)
            || result.GetValue(args.VersionsWithFeedOption)
            || result.GetValue(args.IncludeUnlistedOption)
            || result.GetValue(args.ContentOption)
            || result.GetValue(args.FrontmatterOption)
            || result.GetValue(args.BodyOption)
            || (result.GetResult(args.VersionOption) is { Implicit: false }
                && result.GetValue(args.VersionOption) is null);

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
