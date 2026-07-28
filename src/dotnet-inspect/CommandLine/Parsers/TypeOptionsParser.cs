using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parser for the type command options.
/// Extracts options and builds ApiOptions for type discovery.
/// </summary>
public static class TypeOptionsParser
{
    /// <summary>
    /// Arguments container for type command options.
    /// </summary>
    public record TypeCommandArgs(
        Argument<string[]> ArgsArg,
        Option<string?> PackageOption,
        Option<string?> AssemblyOption,
        Option<string?> PlatformOption,
        Option<string?> ProjectOption,
        Option<string?> FrameworkOption,
        Option<string?> TfmOption,
        Option<bool> AllOption,
        Option<string?> TypeFilterOption,
        Option<bool> CompactOption,
        Option<bool> NoHeaderOption,
        Option<bool> ShapeOption,
        Option<bool> UnsafeOption,
        Option<string[]> MemberOption,
        Option<string[]> KindOption,
        Option<string?> AtOption);

    /// <summary>
    /// Result of parsing type command options.
    /// </summary>
    public abstract record TypeParseResult;

    /// <summary>
    /// Indicates sections should be listed.
    /// </summary>
    public record ListSections : TypeParseResult;

    /// <summary>
    /// Indicates schema discovery (-D/--discover).
    /// </summary>
    public record Discovery(string[]? Discover, bool Tree) : TypeParseResult;

    /// <summary>
    /// Indicates help should be shown.
    /// </summary>
    public record ShowHelp : TypeParseResult;

    /// <summary>
    /// Indicates a version error occurred.
    /// </summary>
    public record VersionError(string Message) : TypeParseResult;

    /// <summary>
    /// Indicates an unrecognized option was found.
    /// </summary>
    public record UnrecognizedOption(string Option) : TypeParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(TypeOptions Options) : TypeParseResult;

    /// <summary>
    /// Parses type command options asynchronously (due to source resolution).
    /// </summary>
    public static async Task<TypeParseResult> ParseAsync(
        ParseResult parseResult,
        SharedOptions opts,
        TypeCommandArgs args)
    {
        var sourceInputs = SharedParsers.ReadSourceSelectionInputs(
            parseResult, args.ArgsArg, args.PackageOption, args.AssemblyOption, args.PlatformOption);
        var projectPath = parseResult.GetValue(args.ProjectOption);
        bool hasProjectSource = !string.IsNullOrWhiteSpace(projectPath);
        bool hasNonProjectSource = sourceInputs.HasExplicitSource;

        // Handle projection discovery or help
        if (sourceInputs.Args.Length == 0 && !sourceInputs.HasExplicitSource && !hasProjectSource)
        {
            if (opts.IsDiscoveryMode(parseResult))
                return new Discovery(opts.ParseDiscover(parseResult), opts.ParseTree(parseResult));
            return new ShowHelp();
        }

        if (hasProjectSource && hasNonProjectSource)
            return new VersionError("Error: --project cannot be combined with --package, --library, or --platform.");

        // Check for unrecognized options in positional args
        var badOption = sourceInputs.Args.FirstOrDefault(a => a.StartsWith('-'));
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        // Resolve source
        SharedParsers.SourceSelection sourceSelection;
        SourceResolver.ResolvedSource source;
        if (hasProjectSource)
        {
            source = new SourceResolver.ResolvedSource(
                PackagePath: null,
                AssemblyPath: null,
                PlatformAssembly: null,
                FrameworkOverride: null,
                TypeName: sourceInputs.Args.FirstOrDefault());
            sourceSelection = new SharedParsers.SourceSelection(
                sourceInputs.Args,
                sourceInputs.ExplicitPackage,
                sourceInputs.ExplicitAssembly,
                sourceInputs.ExplicitPlatform,
                sourceInputs.IsLibrarySelector,
                HasExplicitSource: true,
                source);
        }
        else
        {
            sourceSelection = await SharedParsers.ResolveSourceSelectionAsync(
                sourceInputs, parseResult.GetValue(opts.Verbose), tryQualifiedTypeName: true);
            source = sourceSelection.Source;
        }

        if (source.VersionError)
            return new VersionError(source.VersionErrorMessage!);

        // Parse type filter (number = limit, string = glob)
        var (typeFilter, typeLimit) = SharedParsers.ParseTypeFilter(parseResult.GetValue(args.TypeFilterOption));

        // Parse member filter
        var memberValues = parseResult.GetValue(args.MemberOption) ?? [];
        var (memberFilter, memberLimit) = SharedParsers.ParseMemberFilter(memberValues);

        var kindValues = parseResult.GetValue(args.KindOption) ?? [];
        var kindFilter = SharedParsers.ParseKindFilter(kindValues);
        var routePolicy = TypeRoutePolicy.Resolve(sourceSelection.Args, sourceSelection.HasExplicitSource, source);
        var performanceTriage = opts.ParsePerformanceTriageOptions(parseResult);
        if (!PerformanceTriageOptions.TryValidate(performanceTriage, out var triageShapeError))
            return new VersionError(triageShapeError);
        var select = opts.ParseSelect(parseResult);
        bool hasExplicitSelect = select is { Length: > 0 };
        // Performance Triage row filters (--top/--loop/--min-confidence/--triage-shape/--where/
        // --order-by) surface the Performance Triage section only when the user did not already
        // pick sections with -S. Otherwise an explicit selection like -S "Top Leverage" would
        // silently gain a second section and break single-section formats (--table/--tsv/--jsonl).
        if (performanceTriage.HasFilters && !opts.IsDiscoveryMode(parseResult) && !hasExplicitSelect)
            select = [.. select ?? [], SectionNames.PerformanceTriage];

        var options = routePolicy.ApplyTo(new TypeOptions
        {
            TypeName = source.TypeName,
            PackagePath = source.PackagePath,
            PackageRangeAddress = parseResult.GetValue(args.AtOption),
            AssemblyPath = source.AssemblyPath,
            PlatformAssembly = source.PlatformAssembly,
            ProjectPath = projectPath,
            PlatformFramework = source.FrameworkOverride ?? parseResult.GetValue(args.FrameworkOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            TypeFilter = typeFilter,
            MemberFilter = memberFilter,
            KindFilter = kindFilter,
            Limit = memberLimit ?? typeLimit,
            ShowDocs = false,  // Type command: docs off by default
            DocsExplicitlySet = false,
            BrowsableUrls = parseResult.GetValue(opts.BrowsableUrls)
                && !parseResult.GetValue(opts.RawUrls),
            JsonOutput = opts.ResolveFormat(parseResult) == OutputFormat.Json,
            CompactJson = parseResult.GetValue(args.CompactOption),
            Tabular = opts.ResolveTabular(parseResult),
            Tsv = opts.ResolveTsv(parseResult),
            Jsonl = opts.ResolveJsonl(parseResult),
            TabularExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            MarkdownExplicitlySet = parseResult.GetResult(opts.Markdown) is { Implicit: false },
            PlainText = parseResult.GetValue(opts.PlainText),
            Bare = parseResult.GetValue(opts.Bare),
            RequestAllTaste = parseResult.GetValue(opts.Taste),
            Print = parseResult.GetValue(opts.Print),
            PrintRow = opts.ParsePrintRow(parseResult),
            Value = parseResult.GetValue(opts.Value),
            Urls = parseResult.GetValue(opts.Urls),
            Paths = parseResult.GetValue(opts.Paths),
            JsonArray = parseResult.GetValue(opts.JsonArray),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            ShapeOutput = parseResult.GetValue(args.ShapeOption),
            ShapeExplicitlySet = parseResult.GetResult(args.ShapeOption) is { Implicit: false },
            UnsafeOnly = parseResult.GetValue(args.UnsafeOption),
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            Select = select,
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Count = parseResult.GetValue(opts.Count),
            Rows = opts.ParseRows(parseResult),
            PerformanceTriage = performanceTriage,
            Schema = opts.ParseSchema(parseResult),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            SourceOptions = opts.ParseNuGetSourceOptions(parseResult)
        });

        options = options with
        {
            TipLevel = options.FormatExplicitlySet || options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || typeLimit != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        };

        return new Success(options);
    }
}
