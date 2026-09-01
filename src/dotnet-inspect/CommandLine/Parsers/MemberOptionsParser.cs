using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Views;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parser for the member command options.
/// Extracts options and builds ApiOptions for member inspection.
/// </summary>
public static class MemberOptionsParser
{
    /// <summary>
    /// Arguments container for member command options.
    /// </summary>
    public record MemberCommandArgs(
        Argument<string[]> ArgsArg,
        Option<string?> PackageOption,
        Option<string?> AssemblyOption,
        Option<string?> PlatformOption,
        Option<string?> FrameworkOption,
        Option<string?> TfmOption,
        Option<bool> AllOption,
        Option<string[]> MemberOption,
        Option<bool> CtorOption,
        Option<bool> CompactOption,
        Option<bool> NoHeaderOption,
        Option<bool> UnsafeOption,
        Option<int?> IndexOption,
        Option<string[]> KindOption,
        Option<string[]> BinOption,
        Option<string[]> ProjectOption,
        Option<string[]> CallerPackageOption,
        Option<string[]> RepoOption,
        Option<string?> AtOption,
        Option<bool> ShapeOption,
        Option<string?> RouterDeferredTargetOption);

    /// <summary>
    /// Result of parsing member command options.
    /// </summary>
    public abstract record MemberParseResult;

    /// <summary>
    /// Indicates sections should be listed.
    /// </summary>
    public record ListSections : MemberParseResult;

    /// <summary>
    /// Indicates schema discovery (-D/--discover).
    /// </summary>
    public record Discovery(string[]? Discover, bool Tree) : MemberParseResult;

    /// <summary>
    /// Indicates help should be shown.
    /// </summary>
    public record ShowHelp : MemberParseResult;

    /// <summary>
    /// Indicates a version error occurred.
    /// </summary>
    /// <remarks>
    /// Carries an <see cref="DotnetInspector.Options.OptionError"/> rather than a
    /// bare string so a validation failure keeps its detail lines all the way to
    /// the writer; the implicit conversion leaves the message-only sites
    /// unchanged.
    /// </remarks>
    public record VersionError(DotnetInspector.Options.OptionError Error) : MemberParseResult;

    /// <summary>
    /// Indicates an unrecognized option was found.
    /// </summary>
    public record UnrecognizedOption(string Option) : MemberParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(MemberOptions Options) : MemberParseResult;

    /// <summary>
    /// Parses member command options asynchronously (due to source resolution).
    /// </summary>
    public static async Task<MemberParseResult> ParseAsync(
        ParseResult parseResult,
        SharedOptions opts,
        MemberCommandArgs args)
    {
        var sourceInputs = SharedParsers.ReadSourceSelectionInputs(
            parseResult, args.ArgsArg, args.PackageOption, args.AssemblyOption, args.PlatformOption);
        var projectValues = parseResult.GetValue(args.ProjectOption) ?? [];
        var projectSourcePath = !sourceInputs.HasExplicitSource && projectValues.Length > 0
            ? projectValues[0]
            : null;
        var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
        var deferredRouteValue =
            parseResult.GetValue(args.RouterDeferredTargetOption);
        if (deferredRouteValue is not null
            && !RouterCommandDefinition.IsDeferredTypeOrMemberCapability(
                deferredRouteValue))
        {
            return new VersionError("Invalid internal router state.");
        }

        bool routerDeferredTypeOrMember = deferredRouteValue is not null;
        bool shapeExplicitlySet =
            parseResult.GetResult(args.ShapeOption) is { Implicit: false };
        if (shapeExplicitlySet && !routerDeferredTypeOrMember)
            return new VersionError("--shape is only valid for type targets.");

        // Handle projection discovery or help
        if (sourceInputs.Args.Length == 0 && !sourceInputs.HasExplicitSource && projectSourcePath is null)
        {
            if (opts.IsDiscoveryMode(parseResult))
                return new Discovery(opts.ParseDiscover(parseResult), opts.ParseTree(parseResult));
            return new ShowHelp();
        }

        // Extract positional members
        List<string> positionalMembers = [];
        if (projectSourcePath is not null && sourceInputs.Args.Length >= 2)
            positionalMembers.AddRange(sourceInputs.Args[1..]);
        else if (sourceInputs.HasExplicitSource && sourceInputs.Args.Length >= 2)
            positionalMembers.AddRange(sourceInputs.Args[1..]);
        else if (!sourceInputs.HasExplicitSource && sourceInputs.Args.Length >= 3)
            positionalMembers.AddRange(sourceInputs.Args[2..]);

        // Resolve source
        SharedParsers.SourceSelection sourceSelection;
        SourceResolver.ResolvedSource source;
        if (projectSourcePath is not null)
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
                sourceInputs,
                sourceOptions,
                parseResult.GetValue(opts.Verbose),
                tryQualifiedTypeName: false);
            source = sourceSelection.Source;
        }

        if (source.VersionError)
            return new VersionError(source.VersionErrorMessage!);

        var optionMembers = parseResult.GetValue(args.MemberOption) ?? [];

        // If the only positional value is a fully-qualified Type.Member, split the
        // member suffix first, then probe the type portion as the source/type name.
        // Handles: member System.Text.Json.JsonSerializer.SerializeToNode
        //   → source=System.Text.Json, type=JsonSerializer, member=SerializeToNode
        bool resolvedQualifiedTypeMember = false;
        if (sourceSelection.ExplicitPackage == null
            && source.TypeName == null
            && source.PackagePath != null
            && source.PlatformAssembly == null
            && source.AssemblyPath == null
            && positionalMembers.Count == 0
            && optionMembers.Length == 0)
        {
            string? platformLookupFailure = null;
            var split = SharedParsers.TrySplitQualifiedTypeMember(
                source.PackagePath,
                sourceOptions,
                allowPlatformPrefixFallback: true,
                message => platformLookupFailure = message);
            if (platformLookupFailure is not null)
                return new VersionError(platformLookupFailure);
            if (split != null)
            {
                positionalMembers.Add(split.Value.MemberName);
                var probe = split.Value.Probe;
                source = probe.Kind == SourceResolver.LocalSourceKind.Platform
                    ? source with { PackagePath = null, PlatformAssembly = probe.SourceName, TypeName = probe.Remainder }
                    : source with { PackagePath = probe.SourceName, TypeName = probe.Remainder };
                resolvedQualifiedTypeMember = true;
            }
        }

        // If source resolution left us with an unresolved package that is actually
        // a qualified type name (e.g., "System.Text.Json.JsonDocument"), split it
        // so the user can write: member System.Text.Json.JsonDocument Parse
        if (!resolvedQualifiedTypeMember
            && sourceSelection.ExplicitPackage == null
            && source.PackagePath != null
            && source.PlatformAssembly == null
            && source.AssemblyPath == null)
        {
            string? platformLookupFailure = null;
            var probe = SourceResolver.TryResolveQualifiedTypeName(
                source.PackagePath,
                sourceOptions,
                allowPlatformPrefixFallback: true,
                message => platformLookupFailure = message);
            if (platformLookupFailure is not null)
                return new VersionError(platformLookupFailure);
            if (probe != null)
            {
                if (source.TypeName != null)
                    positionalMembers.Insert(0, source.TypeName);
                if (probe.Kind == SourceResolver.LocalSourceKind.Platform)
                    source = source with { PackagePath = null, PlatformAssembly = probe.SourceName, TypeName = probe.Remainder };
                else
                    source = source with { PackagePath = probe.SourceName, TypeName = probe.Remainder };
            }
        }

        var typeName = source.TypeName;

        // Split a dotted name into Type + member at the last top-level dot when:
        //  - non-explicit multi-arg form (e.g. member System.Text.Json JsonSerializer.Serialize),
        //    where the source was already resolved from the first argument; or
        //  - explicit-source form whose trailing segment is unambiguously a member because it
        //    carries an overload (":N") or digest ("~hash") selector, which a type name can
        //    never contain (e.g. member JsonSerializer.Serialize:2 --platform System.Text.Json).
        // Plain explicit-source dotted names are left whole so the type/member boundary is
        // resolved against real metadata in ApiTypeLookupService, because "System.String" is a
        // type while "System" is only a namespace.
        // Router-deferred targets also stay whole so overload, digest, and special-member syntax
        // crosses that same metadata boundary instead of forcing a syntactic type/member guess.
        // Skip if the right part contains '<' — that's a generic type name (e.g., Generic.List<T>),
        // not a type.member pair.
        bool explicitSourceSelectorSplit = !routerDeferredTypeOrMember
            && sourceInputs.HasExplicitSource
            && sourceInputs.Args.Length == 1
            && typeName != null
            && HasMemberSelectorSuffix(typeName);
        if (typeName != null
            && (((!sourceInputs.HasExplicitSource && sourceInputs.Args.Length > 1)
                    || explicitSourceSelectorSplit))
            && typeName.Contains('.')
            && positionalMembers.Count == 0
            && optionMembers.Length == 0)
        {
            var (splitTypeName, splitMemberName) = SharedParsers.SplitTrailingMember(typeName);
            if (splitMemberName != null)
            {
                positionalMembers.Add(splitMemberName);
                typeName = splitTypeName;
            }
        }

        // Check for unrecognized options in positional args
        var badOption = positionalMembers.FirstOrDefault(m => m.StartsWith('-'));
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        // Combine -m option with positional members
        var allMembers = optionMembers.Concat(positionalMembers).ToArray();
        string[] routerDeferredTypeMemberValues = routerDeferredTypeOrMember
            ? [.. allMembers]
            : [];
        var ctorOnly = parseResult.GetValue(args.CtorOption);

        // Process dotted syntax and overload shorthand
        var (dottedTypeFilter, shorthandIndex, memberDigest, memberGenericArity, memberGenericArityConflict, memberKindFilter) =
            SharedParsers.ProcessMemberArguments(
                allMembers,
                inferDottedTypeFilter: string.IsNullOrEmpty(typeName),
                suppliedTypeName: typeName);

        // Use extracted type name if no explicit type was provided
        if (dottedTypeFilter != null && string.IsNullOrEmpty(typeName))
            typeName = dottedTypeFilter;

        // Build member filter
        var (memberFilter, memberLimit) = BuildMemberFilter(allMembers, ctorOnly, out var clearShorthand);
        if (clearShorthand)
            shorthandIndex = null;
        if (memberGenericArityConflict)
            return new VersionError("A member selection cannot combine different generic arities.");
        if (memberGenericArity.HasValue && memberFilter.Count != 1)
            return new VersionError("A generic arity selector requires exactly one member name.");

        var kindValues = parseResult.GetValue(args.KindOption) ?? [];
        var kindFilter = SharedParsers.ParseKindFilter(kindValues);
        kindFilter.UnionWith(memberKindFilter);

        var select = opts.ParseSelect(parseResult);
        var selectDefault = opts.ParseSelectDefault(parseResult);
        bool hasExplicitSelect = select is { Length: > 0 } || selectDefault;
        var whereExpressions = parseResult.GetValue(opts.RowWhere) ?? [];
        if (!BodyKindQueryOptions.TryExtract(
                whereExpressions,
                out var bodyKindQuery,
                out var performanceWhere,
                out var bodyKindError))
        {
            return new VersionError(bodyKindError);
        }
        var performanceTriage = opts.ParsePerformanceTriageOptions(
            parseResult,
            performanceWhere);
        if (!PerformanceTriageOptions.TryValidate(performanceTriage, out var triageShapeError))
            return new VersionError(triageShapeError);
        if (bodyKindQuery.HasFilter && performanceTriage.HasFilters)
        {
            return new VersionError(
                "A Body Shapes predicate cannot yet be combined with Performance Triage "
                + "filters or --order-by in one query.");
        }
        // Only surface Performance Triage from row filters when the user did not select sections
        // with -S; an explicit selection must not silently gain a second section.
        if (performanceTriage.HasFilters && !opts.IsDiscoveryMode(parseResult) && !hasExplicitSelect)
            select = [.. select ?? [], SectionNames.PerformanceTriage];
        var sectionPipeline = ApiMemberSectionPipelines.Create(new MemberOptions());
        if (!opts.TryValidateTopRanking(
                parseResult,
                select,
                autoSelectsRankingSection: performanceTriage.HasFilters && !opts.IsDiscoveryMode(parseResult) && !hasExplicitSelect,
                sectionPipeline.SelectableSectionNames,
                sectionPipeline.InfoSectionNames,
                sectionPipeline.GetCategoryMap(),
                selectDefault,
                out var topRankingError))
        {
            return new VersionError(topRankingError!);
        }
        performanceTriage = opts.BindPerformanceTriageToSelectedKindSections(
            performanceTriage,
            select,
            sectionPipeline.SelectableSectionNames,
            sectionPipeline.InfoSectionNames,
            sectionPipeline.GetCategoryMap(),
            selectDefault);

        var embeddedMermaid = opts.IsEmbeddedMermaid(parseResult);
        if (parseResult.GetValue(opts.Mermaid)
            && (parseResult.GetValue(opts.Json)
                || parseResult.GetValue(opts.PlainText)
                || parseResult.GetValue(opts.Bare)
                || parseResult.GetValue(opts.Table)
                || parseResult.GetValue(opts.Tsv)
                || parseResult.GetValue(opts.Jsonl)
                || (!embeddedMermaid && parseResult.GetResult(opts.Verbosity) is { Implicit: false })))
        {
            return new VersionError(
                "--mermaid is standalone unless paired with --markdown; it cannot combine with another output format.");
        }

        var outputFormat = opts.ResolveFormat(parseResult);
        var options = new MemberOptions
        {
            TypeName = typeName,
            PackagePath = source.PackagePath,
            PackageRangeAddress = parseResult.GetValue(args.AtOption),
            AssemblyPath = source.AssemblyPath,
            PlatformAssembly = source.PlatformAssembly,
            ProjectPath = projectSourcePath,
            PlatformFramework = source.FrameworkOverride ?? parseResult.GetValue(args.FrameworkOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            MemberFilter = memberFilter,
            KindFilter = kindFilter,
            Limit = memberLimit,
            ShowDocs = true,  // Docs always on (local XML); use source command for SourceLink
            DocsExplicitlySet = false,
            BrowsableUrls = parseResult.GetValue(opts.BrowsableUrls)
                && !parseResult.GetValue(opts.RawUrls),
            JsonOutput = outputFormat == OutputFormat.Json,
            CompactJson = parseResult.GetValue(args.CompactOption),
            Tabular = outputFormat is OutputFormat.Table or OutputFormat.Tsv or OutputFormat.Jsonl,
            Tsv = outputFormat == OutputFormat.Tsv,
            Jsonl = outputFormat == OutputFormat.Jsonl,
            TabularExplicitlySet = opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet = opts.IsFormatExplicitlySet(parseResult),
            FormatFlagExplicitlySet = opts.IsFormatFlagExplicitlySet(parseResult),
            Format = outputFormat,
            MarkdownExplicitlySet =
                parseResult.GetResult(opts.Markdown) is { Implicit: false },
            PlainText = parseResult.GetValue(opts.PlainText),
            MermaidOutput = outputFormat == OutputFormat.Mermaid,
            EmbeddedMermaid = embeddedMermaid,
            Bare = parseResult.GetValue(opts.Bare),
            RequestAllTaste = parseResult.GetValue(opts.Taste),
            RequestReadableLocalNames = parseResult.GetValue(opts.ReadableNames),
            Focus = parseResult.GetValue(opts.Focus),
            Print = parseResult.GetValue(opts.Print),
            PrintRow = opts.ParsePrintRow(parseResult),
            Value = parseResult.GetValue(opts.Value),
            Urls = parseResult.GetValue(opts.Urls),
            Paths = parseResult.GetValue(opts.Paths),
            JsonArray = parseResult.GetValue(opts.JsonArray),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            UnsafeOnly = parseResult.GetValue(args.UnsafeOption),
            CtorOnly = ctorOnly,
            OverloadIndex = parseResult.GetValue(args.IndexOption) ?? shorthandIndex,
            OverloadIndexExplicitlySet =
                parseResult.GetResult(args.IndexOption) is { Implicit: false },
            MemberDigest = memberDigest,
            MemberGenericArity = memberGenericArity,
            CallerScopeDirectories = parseResult.GetValue(args.BinOption) ?? [],
            CallerScopeProjects = projectSourcePath is null
                ? projectValues
                : projectValues.Length > 1 ? projectValues[1..] : [],
            CallerScopePackages = parseResult.GetValue(args.CallerPackageOption) ?? [],
            SourceRepositories = parseResult.GetValue(args.RepoOption) ?? [],
            Discover = opts.ParseDiscover(parseResult),
            Tree = parseResult.GetValue(opts.Tree),
            ShapeOutput = parseResult.GetValue(args.ShapeOption),
            ShapeExplicitlySet = shapeExplicitlySet,
            Select = select,
            SelectDefault = selectDefault,
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            Count = parseResult.GetValue(opts.Count),
            Rows = opts.ParseRows(parseResult),
            RankedTopRequested =
                parseResult.GetResult(opts.PerformanceTriageTop) is { Implicit: false },
            PerformanceTriage = performanceTriage,
            BodyKindQuery = bodyKindQuery,
            Schema = opts.ParseSchema(parseResult),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            SourceOptions = sourceOptions,
            RouterDeferredTypeOrMember = routerDeferredTypeOrMember,
            RouterDeferredTypeMemberValues = routerDeferredTypeMemberValues
        };

        options = options with
        {
            TipLevel = options.FormatExplicitlySet || options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null || memberLimit != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        };

        return new Success(options);
    }

    /// <summary>
    /// True when the trailing segment of a dotted name is unambiguously a member:
    /// it carries an overload (":N") / digest ("~hash") selector, or it is a
    /// metadata constructor token ("..ctor"/"..cctor").
    /// </summary>
    private static bool HasMemberSelectorSuffix(string typeName)
    {
        var (splitTypeName, splitMemberName) = SharedParsers.SplitTrailingMember(typeName);
        return splitTypeName != null
            && splitMemberName != null
            && (splitMemberName.Contains(':')
                || splitMemberName.Contains('~')
                || splitMemberName is ".ctor" or ".cctor");
    }

    private static (HashSet<string> Filter, int? Limit) BuildMemberFilter(string[] allMembers, bool ctorOnly, out bool clearShorthand)
    {
        clearShorthand = false;

        if (ctorOnly)
            return (new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ctor" }, null);

        if (allMembers.Length == 1 && int.TryParse(allMembers[0], out var mNum))
        {
            clearShorthand = true;
            return ([], mNum);
        }

        if (allMembers.Length > 0)
            return (new HashSet<string>(allMembers, StringComparer.OrdinalIgnoreCase), null);

        return ([], null);
    }

}
