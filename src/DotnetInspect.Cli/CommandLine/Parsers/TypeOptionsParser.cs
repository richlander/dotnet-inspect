using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspect.Cli.Options;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Planning;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.CommandLine;

/// <summary>
/// Parser for the type command options.
/// Extracts options and builds ApiOptions for type discovery.
/// </summary>
public static class TypeOptionsParser
{
    public static bool TryCreateStructuralPlan(
        ParseResult parseResult,
        SharedOptions options,
        TypeCommandArgs args,
        out StructuralDiscoveryPlan? plan,
        out OptionError? error,
        out bool targetFree,
        string? interpretedTypeTarget = null,
        InspectionCatalogIdentity? interpretedCatalog = null)
    {
        plan = null;
        error = null;
        targetFree = false;
        if (parseResult.GetValue(args.WorkspaceOption) is not null)
            return false;
        if (!options.IsDiscoveryMode(parseResult)
            || !options.ParseSchema(parseResult))
        {
            return false;
        }

        SharedParsers.SourceSelectionInputs sourceInputs =
            SharedParsers.ReadSourceSelectionInputs(
                parseResult,
                args.ArgsArg,
                args.PackageOption,
                args.AssemblyOption,
                args.PlatformOption);
        error =
            SharedParsers.GetStructuralUnrecognizedOptionError(
                sourceInputs);
        if (error is not null)
            return true;
        bool hasProjectSource =
            parseResult.GetResult(args.ProjectOption)
                is { Implicit: false };
        error =
            SharedParsers.GetStructuralPositionalVersionError(
                sourceInputs,
                hasProjectSource);
        if (error is not null)
            return true;

        int typeIndex =
            SharedParsers.GetStructuralTypeArgumentIndex(
                sourceInputs,
                hasProjectSource);
        string? typeName =
            interpretedTypeTarget ?? (typeIndex >= 0
            && typeIndex < sourceInputs.Args.Length
                ? sourceInputs.Args[typeIndex]
                : null);
        if (hasProjectSource && sourceInputs.HasExplicitSource)
        {
            error = new OptionError(
                "--project cannot be combined with --package, --library, or --platform.");
            return true;
        }

        string[] memberValues =
            parseResult.GetValue(args.MemberOption) ?? [];
        if (memberValues.Any(value =>
                MemberTargetSelector.Parse(value)
                    .GenericArity.HasValue))
        {
            error = new OptionError(
                "The type command's -m filter does not support generic arity selectors; use the member command.");
            return true;
        }

        string? typeFilter =
            SharedParsers.ParseTypeFilter(
                parseResult.GetValue(args.TypeFilterOption));
        var typeGesture = new TypeGestureIntent(typeFilter);
        bool selectsListingCatalog =
            SelectsTypeListingCatalog(typeName, typeFilter);
        InspectionCatalogIdentity catalog =
            interpretedCatalog
            ?? (selectsListingCatalog
                    ? InspectionCatalogIdentity.ApiType
                    : InspectionCatalogIdentity.ApiMember);
        error = SharedParsers.ParseAnalysisQueryOptions(
            parseResult,
            options,
            typeScoped: true,
            typeName: catalog == InspectionCatalogIdentity.ApiType ? null : typeName,
            out _,
            out _,
            out _,
            typeGesture);
        if (error is not null)
            return true;

        targetFree =
            string.IsNullOrWhiteSpace(typeName)
            && sourceInputs.Args.Length == 0
            && !sourceInputs.HasExplicitSource
            && !hasProjectSource
            && parseResult.GetValue(args.TypeFilterOption) is null
            && memberValues.Length == 0;
        plan = new StructuralDiscoveryPlan.Resolved(
            StructuralViewRegistry.Route(
                StructuralViewIdentity.Type,
                catalog));
        return true;
    }

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
        Option<bool> UnsafeOption,
        Option<string[]> RepoOption,
        Option<string[]> MemberOption,
        Option<string[]> KindOption,
        Option<string?> AtOption,
        Option<string?> WorkspaceOption,
        Option<string?> ShareOption);

    internal static bool IsTypeListingRowSelection(
        ParseResult parseResult,
        SharedOptions opts,
        TypeCommandArgs args)
    {
        if (opts.IsDiscoveryMode(parseResult)
            || parseResult.GetResult(opts.QueryHelp) is { Implicit: false }
            || opts.ParseSelect(parseResult) is { Length: > 0 }
            || opts.ParseSelectDefault(parseResult)
            || string.Equals(
                parseResult.GetValue(args.TfmOption),
                "all",
                StringComparison.OrdinalIgnoreCase)
            || parseResult.GetValue(opts.PerformanceTriageLoop)
            || !string.IsNullOrWhiteSpace(
                parseResult.GetValue(opts.PerformanceTriageMinConfidence))
            || parseResult.GetValue(opts.PerformanceTriageShape)
                is { Length: > 0 }
            || parseResult.GetValue(opts.PerformanceTriageTop) is not null
            || parseResult.GetValue(opts.RowWhere) is { Length: > 0 }
            || !string.IsNullOrWhiteSpace(
                parseResult.GetValue(opts.RowOrderBy)))
        {
            return false;
        }

        SharedParsers.SourceSelectionInputs sourceInputs =
            SharedParsers.ReadSourceSelectionInputs(
                parseResult,
                args.ArgsArg,
                args.PackageOption,
                args.AssemblyOption,
                args.PlatformOption);
        bool hasProjectSource =
            parseResult.GetResult(args.ProjectOption)
                is { Implicit: false };
        if (!sourceInputs.HasExplicitSource && !hasProjectSource)
            return false;

        string? typeTarget =
            sourceInputs.Args.FirstOrDefault();
        string? typeFilter =
            SharedParsers.ParseTypeFilter(
                parseResult.GetValue(args.TypeFilterOption));
        return SelectsTypeListingCatalog(
            typeTarget,
            typeFilter);
    }

    private static bool SelectsTypeListingCatalog(
        string? typeTarget,
        string? typeFilter) =>
        string.IsNullOrWhiteSpace(typeTarget)
        || TypeMatcher.IsTypeGlobPattern(typeTarget)
        || new TypeGestureIntent(typeFilter)
            .SelectsListingCatalog(typeTarget);

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
    /// <remarks>
    /// Carries an <see cref="DotnetInspect.Cli.Options.OptionError"/> rather than a
    /// bare string so a validation failure keeps its detail lines all the way to
    /// the writer; the implicit conversion leaves the message-only sites
    /// unchanged.
    /// </remarks>
    public record VersionError(DotnetInspect.Cli.Options.OptionError Error) : TypeParseResult;

    /// <summary>
    /// Indicates an unrecognized option was found.
    /// </summary>
    public record UnrecognizedOption(string Option) : TypeParseResult;

    /// <summary>
    /// Successfully parsed options ready for execution.
    /// </summary>
    public record Success(
        TypeOptions Options,
        ResolvedMemberInspectionPlan Plan) : TypeParseResult;

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
        var sourceOptions = opts.ParseNuGetSourceOptions(parseResult);
        string? workspacePacket =
            parseResult.GetValue(args.WorkspaceOption);
        WorkspaceShareFormat? shareFormat =
            WorkspaceShareOption.Parse(parseResult, args.ShareOption);

        // Handle projection discovery or help
        if (sourceInputs.Args.Length == 0
            && !sourceInputs.HasExplicitSource
            && !hasProjectSource
            && workspacePacket is null)
        {
            if (opts.IsDiscoveryMode(parseResult))
                return new Discovery(opts.ParseDiscover(parseResult), opts.ParseTree(parseResult));
            return new ShowHelp();
        }

        if (hasProjectSource && hasNonProjectSource)
            return new VersionError("--project cannot be combined with --package, --library, or --platform.");
        if (shareFormat is not null && workspacePacket is null)
        {
            return new VersionError(
                "--share on type requires --workspace.");
        }
        if (workspacePacket is not null)
        {
            if (Uri.TryCreate(
                    workspacePacket,
                    UriKind.Absolute,
                    out _))
            {
                return new VersionError(
                    "--workspace accepts a canonical Base64URL Workspace "
                        + "packet string; URLs are not supported.");
            }
            if (hasProjectSource
                || hasNonProjectSource
                || parseResult.GetValue(args.FrameworkOption) is not null
                || parseResult.GetValue(args.TfmOption) is not null
                || parseResult.GetValue(args.AtOption) is not null)
            {
                return new VersionError(
                    "--workspace is the sole location source and cannot be "
                        + "combined with --package, --library, --platform, "
                        + "--project, --framework, --tfm, or --at.");
            }
            if (sourceInputs.Args.Length != 1)
            {
                return new VersionError(
                    "--workspace requires exactly one explicit Type selector.");
            }
            if (parseResult.GetValue(args.TypeFilterOption) is not null
                || TypeMatcher.IsTypeGlobPattern(sourceInputs.Args[0]))
            {
                return new VersionError(
                    "--workspace requires one exact Type selector; Type "
                        + "listing and glob selection are not supported.");
            }
        }

        bool selectsCloneCandidateRows =
            CloneCandidateRowSelectionAdoption.IsActive(
                parseResult,
                opts);
        bool selectsTypeListingRows =
            !selectsCloneCandidateRows
            && IsTypeListingRowSelection(
                parseResult,
                opts,
                args);
        RowSelectionIntent<string>? semanticRowSelection = null;
        if ((selectsCloneCandidateRows || selectsTypeListingRows)
            && !CliRowSelectionCommandRegistry
                .TryGetPreparedSemanticIntent(
                    parseResult,
                    selectsCloneCandidateRows
                        ? "Clone Candidates"
                        : "Type",
                    out semanticRowSelection,
                    out string? rowSelectionError))
        {
            return new VersionError(rowSelectionError!);
        }

        // Check for unrecognized options in positional args
        var badOption = sourceInputs.Args.FirstOrDefault(a => a.StartsWith('-'));
        if (badOption != null)
            return new UnrecognizedOption(badOption);

        // Resolve source
        SharedParsers.SourceSelection sourceSelection;
        SourceResolver.ResolvedSource source;
        if (workspacePacket is not null)
        {
            source = new SourceResolver.ResolvedSource(
                PackagePath: null,
                AssemblyPath: null,
                PlatformAssembly: null,
                FrameworkOverride: null,
                TypeName: sourceInputs.Args[0]);
            sourceSelection = new SharedParsers.SourceSelection(
                sourceInputs.Args,
                ExplicitPackage: null,
                ExplicitAssembly: null,
                ExplicitPlatform: null,
                IsLibrarySelector: false,
                HasExplicitSource: true,
                source);
        }
        else if (hasProjectSource)
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
                tryQualifiedTypeName: true,
                parseResult.GetValue(args.FrameworkOption));
            source = sourceSelection.Source;
        }

        if (source.VersionError)
            return new VersionError(source.VersionErrorMessage!);

        string? typeFilter =
            SharedParsers.ParseTypeFilter(
                parseResult.GetValue(args.TypeFilterOption));

        // Parse member filter
        var memberValues = parseResult.GetValue(args.MemberOption) ?? [];
        if (memberValues.Any(value => MemberTargetSelector.Parse(value).GenericArity.HasValue))
        {
            return new VersionError(
                "The type command's -m filter does not support generic arity selectors; use the member command.");
        }
        var memberFilter = SharedParsers.ParseMemberFilter(memberValues);

        var kindValues = parseResult.GetValue(args.KindOption) ?? [];
        var kindFilter = SharedParsers.ParseKindFilter(kindValues);
        var routePolicy = TypeRoutePolicy.Resolve(sourceSelection.Args, sourceSelection.HasExplicitSource, source);
        OptionError? analysisError =
            SharedParsers.ParseAnalysisQueryOptions(
                parseResult,
                opts,
                typeScoped: true,
                source.TypeName,
                out BodyKindQueryOptions bodyKindQuery,
                out PerformanceTriageOptions performanceTriage,
                out CloneCandidateQueryOptions cloneCandidateQuery,
                new TypeGestureIntent(typeFilter));
        if (analysisError is not null)
            return new VersionError(analysisError.Value);
        var select = opts.ParseSelect(parseResult);
        var selectDefault = opts.ParseSelectDefault(parseResult);
        bool hasExplicitSelect = select is { Length: > 0 } || selectDefault;
        // Performance Triage row filters (--top/--loop/--min-confidence/--triage-shape/--where/
        // --order-by) surface the Performance Triage section only when the user did not already
        // pick sections with -S. Otherwise an explicit selection like -S "Top Leverage" would
        // silently gain a second section and break single-section formats (--table/--tsv/--jsonl).
        if (performanceTriage.HasFilters && !opts.IsDiscoveryMode(parseResult) && !hasExplicitSelect)
            select = [.. select ?? [], SectionNames.PerformanceTriage];
        if (cloneCandidateQuery.HasPredicates
            && !opts.IsDiscoveryMode(parseResult)
            && !hasExplicitSelect)
        {
            select = [.. select ?? [], SectionNames.CloneCandidates];
        }

        bool envelopeOutput = parseResult.GetValue(opts.Envelope);
        bool tree = parseResult.GetValue(opts.Tree);
        bool treeOwnsFormat =
            tree && !opts.IsFormatFlagExplicitlySet(parseResult);
        OutputFormat outputFormat =
            envelopeOutput
                ? OutputFormat.Json
                : treeOwnsFormat
                ? OutputFormat.Markdown
                : opts.ResolveFormat(parseResult);

        var options = routePolicy.ApplyTo(new TypeOptions
        {
            TypeName = source.TypeName,
            WorkspacePacket = workspacePacket,
            ShareFormat = shareFormat,
            PackagePath = source.PackagePath,
            PackageRangeAddress = parseResult.GetValue(args.AtOption),
            AssemblyPath = source.AssemblyPath,
            PlatformAssembly = source.PlatformAssembly,
            ProjectPath = projectPath,
            PlatformFramework = source.FrameworkOverride ?? parseResult.GetValue(args.FrameworkOption),
            Tfm = parseResult.GetValue(args.TfmOption),
            IncludeAll = parseResult.GetValue(args.AllOption),
            TypeFilter = typeFilter,
            TypeListingRowSelection = selectsTypeListingRows
                ? semanticRowSelection
                : null,
            MemberFilter = memberFilter,
            KindFilter = kindFilter,
            ShowDocs = false,  // Type command: docs off by default
            DocsExplicitlySet = false,
            PreferRenderedUrls = parseResult.GetValue(opts.PreferRenderedUrls),
            JsonOutput = !envelopeOutput && outputFormat == OutputFormat.Json,
            EnvelopeOutput = envelopeOutput,
            CompactJson = parseResult.GetValue(args.CompactOption),
            Tabular =
                !envelopeOutput
                && outputFormat is
                    OutputFormat.Table or
                    OutputFormat.Tsv or
                    OutputFormat.Jsonl,
            Tsv = !envelopeOutput && outputFormat == OutputFormat.Tsv,
            Jsonl = !envelopeOutput && outputFormat == OutputFormat.Jsonl,
            TabularExplicitlySet =
                !envelopeOutput
                && !treeOwnsFormat
                && opts.IsTableExplicitlySet(parseResult),
            FormatExplicitlySet =
                !envelopeOutput
                && (tree || opts.IsFormatExplicitlySet(parseResult)),
            FormatFlagExplicitlySet =
                !envelopeOutput
                && (tree || opts.IsFormatFlagExplicitlySet(parseResult)),
            Format = outputFormat,
            MarkdownExplicitlySet = parseResult.GetResult(opts.Markdown) is { Implicit: false },
            PlainText = !envelopeOutput && parseResult.GetValue(opts.PlainText),
            RequestAllTaste = parseResult.GetValue(opts.Taste),
            RequestReadableLocalNames = parseResult.GetValue(opts.ReadableNames),
            Print = parseResult.GetValue(opts.Print),
            PrintRow = opts.ParsePrintRow(parseResult),
            Value = parseResult.GetValue(opts.Value),
            Urls = parseResult.GetValue(opts.Urls),
            Paths = parseResult.GetValue(opts.Paths),
            JsonArray = parseResult.GetValue(opts.JsonArray),
            NoHeader = parseResult.GetValue(opts.NoHeaders),
            UnsafeOnly = parseResult.GetValue(args.UnsafeOption),
            SourceRepositories = parseResult.GetValue(args.RepoOption) ?? [],
            Discover = opts.ParseDiscover(parseResult),
            Tree = tree,
            Select = select,
            SelectDefault = selectDefault,
            Columns = opts.ParseColumns(parseResult),
            Fields = opts.ParseFields(parseResult),
            FieldsExplicitlySet =
                parseResult.GetResult(opts.Fields) is { Implicit: false },
            Count = parseResult.GetValue(opts.Count),
            Rows = selectsTypeListingRows
                || semanticRowSelection is not null
                ? null
                : opts.ParseRows(parseResult),
            CloneCandidateRowSelection =
                selectsCloneCandidateRows
                    ? semanticRowSelection
                    : null,
            PerformanceTriage = performanceTriage,
            BodyKindQuery = bodyKindQuery,
            CloneCandidateQuery = cloneCandidateQuery,
            Schema = opts.ParseSchema(parseResult),
            Verbose = parseResult.GetValue(opts.Verbose),
            Verbosity = opts.ParseVerbosity(parseResult),
            VerbosityExplicitlySet =
                parseResult.GetResult(opts.Verbosity) is { Implicit: false },
            SourceOptions = sourceOptions
        });

        options = options with
        {
            TipLevel = options.EnvelopeOutput
                && parseResult.GetResult(opts.Tips) is { Implicit: false }
                ? opts.ParseTipLevel(parseResult)
                : options.FormatExplicitlySet || options.IsRawOutput || options.Verbosity == Verbosity.Quiet || ArgumentPreprocessor.HeadLines != null || ArgumentPreprocessor.TailLines != null
                ? TipLevel.Quiet : opts.ParseTipLevel(parseResult)
        };

        return new Success(
            options,
            ResolvedMemberInspectionPlan
                .FromCompatibilityOptions(options));
    }
}
