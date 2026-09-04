using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.CommandLine;
using DotnetInspector.Options;
using DotnetInspector.Services;

namespace DotnetInspector.CommandLine;

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
        Option<bool> LatestVersionOption,
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
        var badOption = packageArgs.FirstOrDefault(a => a.StartsWith('-'));
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        var explicitVersion = parseResult.GetValue(args.VersionOption);
        bool showLatestVersion = parseResult.GetValue(args.LatestVersionOption);
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
        bool showPluralVersions =
            showVersionsWithFeed
            || showVersionList;
        if ((showVersionList && showVersionsWithFeed)
            || (showPluralVersions
                && (hasExplicitVersionSelector
                    || showLatestVersion)))
        {
            return new InvalidArguments(
                "--versions and --versions-with-feed cannot be combined "
                + "with each other, --version, or --latest-version.");
        }

        bool showVersions =
            bareVersion
            || showLatestVersion
            || showPluralVersions;
        CliRowSelectionCommandRegistry.TryGetLowering(
            parseResult,
            out CliRowSelectionLowering<string>? rowSelection);
        bool hasExplicitRowSelection =
            parseResult.GetResult(opts.Limit) is { Implicit: false }
            || parseResult.GetResult(opts.Rows) is { Implicit: false }
            || parseResult.GetValue(opts.Head)
            || parseResult.GetValue(opts.Tail)
            || parseResult.GetValue(args.LinesOption)
            || parseResult.GetValue(args.TailLinesOption);
        if (showPluralVersions
            && hasExplicitRowSelection
            && rowSelection is null)
        {
            return new InvalidArguments(
                "Package version row selection was not lowered before execution.");
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
        bool hasLineSelection =
            parseResult.GetValue(args.LinesOption)
            || parseResult.GetValue(args.TailLinesOption);
        if (showPluralVersions
            && hasLineSelection
            && outputFormat == OutputFormat.Json)
        {
            return new InvalidArguments(
                "--lines and --tail-lines cannot be combined with JSON output; "
                + "use semantic -n to select complete JSON rows.");
        }

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
            JsonArray = parseResult.GetValue(opts.JsonArray),
            ShowContent = parseResult.GetValue(args.ContentOption),
            ContentScope = contentScope,
            FrontmatterRequested = frontmatterRequested,
            BodyRequested = bodyRequested,
            OutputPath = parseResult.GetValue(args.OutOption),
            Limit = (bareVersion || showLatestVersion) ? 1 : null,
            VersionRowSelection =
                showPluralVersions
                    ? rowSelection?.SemanticIntent
                    : null,
            ForceLatest = showLatestVersion,
            Format = outputFormat,
            JsonOutput = outputFormat == OutputFormat.Json,
            Bare = bareOutput,
            Tabular = suppressImplicitRowFormat ? false : opts.ResolveTabular(parseResult),
            Tsv = suppressImplicitRowFormat ? false : opts.ResolveTsv(parseResult),
            Jsonl = suppressImplicitRowFormat ? false : opts.ResolveJsonl(parseResult),
            BrowsableUrls = parseResult.GetValue(opts.BrowsableUrls)
                && !parseResult.GetValue(opts.RawUrls),
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
            Schema = opts.ParseSchema(parseResult),
            Count = parseResult.GetValue(opts.Count),
            Rows = showPluralVersions
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

    private static IEnumerable<string> SplitPathSelectors(string value)
        => value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
