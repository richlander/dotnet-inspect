using System.CommandLine;
using System.CommandLine.Parsing;
using DotnetInspector.Options;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using DotnetInspector.Planning;
using CSharpText;
using DotnetInspector.Views;
using ILInspector.Metadata;

namespace DotnetInspector.CommandLine;

/// <summary>
/// Parser for the member command options.
/// Extracts options and builds ApiOptions for member inspection.
/// </summary>
public static class MemberOptionsParser
{
    internal static bool HasAcquisitionFreeMemberGesture(
        ParseResult parseResult,
        MemberCommandArgs args)
    {
        SharedParsers.SourceSelectionInputs sourceInputs =
            SharedParsers.ReadSourceSelectionInputs(
                parseResult,
                args.ArgsArg,
                args.PackageOption,
                args.AssemblyOption,
                args.PlatformOption);
        bool hasProjectSource =
            (parseResult.GetValue(args.ProjectOption) ?? []).Length > 0
            && !sourceInputs.HasExplicitSource;
        int typeIndex =
            SharedParsers.GetStructuralTypeArgumentIndex(
                sourceInputs,
                hasProjectSource);
        string? typeName =
            typeIndex >= 0
            && typeIndex < sourceInputs.Args.Length
                ? sourceInputs.Args[typeIndex]
                : null;
        List<string> positionalMembers =
            typeIndex >= 0
            && sourceInputs.Args.Length > typeIndex + 1
                ? [.. sourceInputs.Args[(typeIndex + 1)..]]
                : [];
        string[] optionMembers =
            parseResult.GetValue(args.MemberOption) ?? [];
        ApplySyntacticMemberSplit(
            sourceInputs,
            routerDeferredTypeOrMember: false,
            mergeExplicitMemberSelectors: true,
            ref typeName,
            positionalMembers,
            optionMembers);
        string[] members =
        [
            .. positionalMembers,
            .. optionMembers,
        ];
        var (memberFilter, _) =
            BuildMemberFilter(
                members,
                parseResult.GetValue(args.CtorOption),
                out _);
        return memberFilter.Count > 0
            || (!string.IsNullOrWhiteSpace(typeName)
                && StructuralViewRegistry
                    .HasUnambiguousMemberTail(typeName));
    }

    public static bool TryCreateStructuralPlan(
        ParseResult parseResult,
        SharedOptions options,
        MemberCommandArgs args,
        out StructuralDiscoveryPlan? plan,
        out OptionError? error,
        out bool targetFree,
        string? interpretedTypeTarget = null)
    {
        plan = null;
        error = null;
        targetFree = false;
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
        string[] optionMembers =
            parseResult.GetValue(args.MemberOption) ?? [];
        bool ctor = parseResult.GetValue(args.CtorOption);
        int? index = parseResult.GetValue(args.IndexOption);
        bool hasProjectSource =
            (parseResult.GetValue(args.ProjectOption) ?? []).Length > 0
            && !sourceInputs.HasExplicitSource;
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
        string? typeName = interpretedTypeTarget ?? (typeIndex >= 0
            && typeIndex < sourceInputs.Args.Length
            ? sourceInputs.Args[typeIndex]
            : null);
        List<string> positionalMembers =
            typeIndex >= 0
            && sourceInputs.Args.Length > typeIndex + 1
                ? [.. sourceInputs.Args[(typeIndex + 1)..]]
                : [];
        ApplySyntacticMemberSplit(
            sourceInputs,
            routerDeferredTypeOrMember: false,
            mergeExplicitMemberSelectors: true,
            ref typeName,
            positionalMembers,
            optionMembers);
        string[] constructorMembers =
            ctor ? [".ctor"] : [];
        string[] members =
        [
            .. positionalMembers,
            .. optionMembers,
            .. constructorMembers,
        ];
        targetFree =
            string.IsNullOrWhiteSpace(typeName)
            && sourceInputs.Args.Length == 0
            && !sourceInputs.HasExplicitSource
            && !hasProjectSource
            && members.Length == 0
            && index is null;
        error = GetMemberSelectorConflictError(members);
        if (error is not null)
            return true;
        if (parseResult.GetResult(args.ShapeOption)
            is { Implicit: false })
        {
            error = new OptionError(
                "--shape is only valid for type targets.");
            return true;
        }

        error = SharedParsers.ParseAnalysisQueryOptions(
            parseResult,
            options,
            typeScoped: false,
            typeName: null,
            out BodyKindQueryOptions bodyKindQuery,
            out _);
        if (error is not null)
            return true;

        error = GetMermaidOptionError(
            parseResult,
            options);
        if (error is not null)
            return true;

        string[] discoverSelectors =
            options.ParseDiscover(parseResult) ?? [];
        string[] selectSelectors =
            options.ParseSelect(parseResult) ?? [];
        bool selectDefault =
            options.ParseSelectDefault(parseResult);
        var sectionIntent = new InspectionSectionIntent(
            [.. selectSelectors],
            selectDefault,
            [.. discoverSelectors],
            InspectionDiscoveryMode.Structural);
        SectionDemandClassification demand =
            ApiSectionDemandIndex.Classify(
                InspectionSurface.Member,
                sectionIntent.DemandSelectors,
                selectDefault,
                InspectionTargetRequirement.MemberSet);
        error = ValidateStructuralMemberSelection(
            members,
            typeName,
            ctor,
            index,
            bodyKindQuery.HasFilter,
            demand.RequiredTarget == InspectionTargetRequirement.ExactMember,
            out HashSet<string> memberFilter,
            out bool exactMember);
        if (error is not null)
            return true;
        InspectionCatalogIdentity memberCatalog =
            memberFilter.Count == 0
                ? InspectionCatalogIdentity.ApiMember
                : exactMember
                    ? InspectionCatalogIdentity.ApiMemberDetail
                    : InspectionCatalogIdentity.ApiMemberOverload;

        bool unambiguousMemberTail =
            !string.IsNullOrWhiteSpace(typeName)
            && StructuralViewRegistry
                .HasUnambiguousMemberTail(typeName);
        bool dottedTailAmbiguity =
            memberFilter.Count == 0
            && !string.IsNullOrWhiteSpace(typeName)
            && !TypeMatcher.IsTypeGlobPattern(typeName)
            && (unambiguousMemberTail
                || FqnParser.LastTopLevelDot(typeName) > 0);

        if (!dottedTailAmbiguity)
        {
            StructuralViewIdentity view =
                memberCatalog == InspectionCatalogIdentity.ApiMember
                    ? StructuralViewIdentity.MemberType
                    : StructuralViewIdentity.MemberTarget;
            plan = new StructuralDiscoveryPlan.Resolved(
                StructuralViewRegistry.Route(view, memberCatalog));
            return true;
        }

        string dottedTypeName = typeName!;
        var (_, impliedMemberName) =
            SharedParsers.SplitTrailingMember(
                dottedTypeName);
        string impliedMember =
            impliedMemberName
            ?? dottedTypeName[
                (FqnParser.LastTopLevelDot(
                    dottedTypeName) + 1)..];
        MemberTargetSelector impliedSelector =
            MemberTargetSelector.Parse(impliedMember);
        InspectionCatalogIdentity peeledCatalog =
            index is not null
            || impliedSelector.OverloadIndex is not null
            || !string.IsNullOrWhiteSpace(impliedSelector.DigestPrefix)
            || bodyKindQuery.HasFilter
            || demand.RequiredTarget
                == InspectionTargetRequirement.ExactMember
                ? InspectionCatalogIdentity.ApiMemberDetail
                : InspectionCatalogIdentity.ApiMemberOverload;
        if (unambiguousMemberTail
            || index is not null)
        {
            plan = new StructuralDiscoveryPlan.Resolved(
                StructuralViewRegistry.Route(
                    StructuralViewIdentity.MemberTarget,
                    peeledCatalog));
            return true;
        }

        plan = new StructuralDiscoveryPlan.Alternatives(
            StructuralViewRegistry.CreateAlternatives(
                [
                    StructuralViewRegistry.Route(
                        StructuralViewIdentity.MemberType,
                        InspectionCatalogIdentity.ApiMember),
                    StructuralViewRegistry.Route(
                        StructuralViewIdentity.MemberTarget,
                        peeledCatalog),
                ],
                StructuralDiscoveryRequest.From(
                    parseResult,
                    options)));
        return true;
    }

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
    public record Success(
        MemberOptions Options,
        ResolvedMemberInspectionPlan Plan) : MemberParseResult;

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
        ApplySyntacticMemberSplit(
            sourceInputs,
            routerDeferredTypeOrMember,
            mergeExplicitMemberSelectors: false,
            ref typeName,
            positionalMembers,
            optionMembers);

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
        int? explicitIndex =
            parseResult.GetValue(args.IndexOption);
        OptionError? selectorConflictError =
            GetMemberSelectorConflictError(allMembers);
        if (selectorConflictError is not null)
            return new VersionError(selectorConflictError.Value);
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
        OptionError? memberSelectionError =
            GetMemberSelectionError(
                memberGenericArity,
                memberGenericArityConflict,
                memberFilter.Count);
        if (memberSelectionError is not null)
            return new VersionError(memberSelectionError.Value);

        var kindValues = parseResult.GetValue(args.KindOption) ?? [];
        var kindFilter = SharedParsers.ParseKindFilter(kindValues);
        kindFilter.UnionWith(memberKindFilter);

        var select = opts.ParseSelect(parseResult);
        var selectDefault = opts.ParseSelectDefault(parseResult);
        bool hasExplicitSelect = select is { Length: > 0 } || selectDefault;
        OptionError? analysisError =
            SharedParsers.ParseAnalysisQueryOptions(
                parseResult,
                opts,
                typeScoped: false,
                typeName: null,
                out BodyKindQueryOptions bodyKindQuery,
                out PerformanceTriageOptions performanceTriage);
        if (analysisError is not null)
            return new VersionError(analysisError.Value);
        // Only surface Performance Triage from row filters when the user did not select sections
        // with -S; an explicit selection must not silently gain a second section.
        if (performanceTriage.HasFilters && !opts.IsDiscoveryMode(parseResult) && !hasExplicitSelect)
            select = [.. select ?? [], SectionNames.PerformanceTriage];

        OptionError? mermaidError =
            GetMermaidOptionError(parseResult, opts);
        if (mermaidError is not null)
            return new VersionError(mermaidError.Value);
        var embeddedMermaid = opts.IsEmbeddedMermaid(parseResult);

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
            OverloadIndex = explicitIndex ?? shorthandIndex,
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

        ResolvedMemberInspectionPlan plan =
            ResolvedMemberInspectionPlan
                .FromCompatibilityOptions(options);
        string? potentialImpliedMemberTarget =
            typeName
            ?? (sourceInputs.Args.Length == 1
                ? sourceInputs.Args[0]
                : null);
        if (plan.Selection.RequiredTarget
                == InspectionTargetRequirement.ExactMember
            && options.MemberFilter.Count != 1
            && !(options.MemberFilter.Count == 0
                && potentialImpliedMemberTarget is not null
                && HasPotentialImpliedMember(
                    potentialImpliedMemberTarget)))
        {
            return new VersionError(
                options.BodyKindQuery.HasFilter
                    ? "--where Kind=... requires one exact member name or selector."
                    : "Exact-member section selection requires exactly one member name.");
        }

        return new Success(options, plan);
    }

    internal static OptionError? ValidateStructuralMemberSelection(
        string[] members,
        string? typeName,
        bool ctor,
        int? index,
        bool hasBodyKindFilter,
        bool requiresExactMember,
        out HashSet<string> memberFilter,
        out bool exactMember)
    {
        string[] parsedMembers = [.. members];
        var (_, shorthandIndex, memberDigest, genericArity, genericArityConflict, _) =
            SharedParsers.ProcessMemberArguments(
                parsedMembers,
                inferDottedTypeFilter: string.IsNullOrEmpty(typeName),
                suppliedTypeName: typeName);
        (memberFilter, _) = BuildMemberFilter(parsedMembers, ctor, out _);
        exactMember = index is not null
            || shorthandIndex is not null
            || !string.IsNullOrWhiteSpace(memberDigest)
            || hasBodyKindFilter
            || requiresExactMember;

        OptionError? error = GetMemberSelectorConflictError(members)
            ?? GetMemberSelectionError(genericArity, genericArityConflict, memberFilter.Count);
        if (error is not null)
            return error;

        bool hasDottedImpliedMember = memberFilter.Count == 0
            && !string.IsNullOrWhiteSpace(typeName)
            && HasPotentialImpliedMember(typeName);
        if ((index is not null || shorthandIndex is not null)
            && (memberFilter.Count > 1
                || (memberFilter.Count == 0 && !hasDottedImpliedMember)))
        {
            return new OptionError("--index/Name:N requires exactly one member name.");
        }
        if (!string.IsNullOrWhiteSpace(memberDigest)
            && memberFilter.Count != 1
            && !hasDottedImpliedMember)
        {
            return new OptionError("Name~digest requires exactly one member name.");
        }

        int exactTargetCount = hasDottedImpliedMember ? 1 : memberFilter.Count;
        if (exactMember && exactTargetCount != 1)
        {
            return new OptionError(hasBodyKindFilter
                ? "--where Kind=... requires one exact member name or selector."
                : "Exact-member section selection requires exactly one member name.");
        }
        return null;
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

    private static OptionError? GetMemberSelectionError(
        int? genericArity,
        bool genericArityConflict,
        int memberCount)
    {
        if (genericArityConflict)
        {
            return new OptionError(
                "A member selection cannot combine different generic arities.");
        }

        if (genericArity.HasValue && memberCount != 1)
        {
            return new OptionError(
                "A generic arity selector requires exactly one member name.");
        }

        return null;
    }

    private static void ApplySyntacticMemberSplit(
        SharedParsers.SourceSelectionInputs sourceInputs,
        bool routerDeferredTypeOrMember,
        bool mergeExplicitMemberSelectors,
        ref string? typeName,
        List<string> positionalMembers,
        string[] optionMembers)
    {
        bool explicitSourceSelectorSplit =
            !routerDeferredTypeOrMember
            && sourceInputs.HasExplicitSource
            && sourceInputs.Args.Length == 1
            && typeName is not null
            && StructuralViewRegistry
                .HasUnambiguousMemberTail(typeName);
        bool positionalFileSource =
            !sourceInputs.HasExplicitSource
            && sourceInputs.Args.Length > 1
            && CommandLineHelpers.TryClassifyAsFilePath(
                sourceInputs.Args[0],
                out string? dllPath,
                out string? nupkgPath)
            && (dllPath is not null || nupkgPath is not null);
        bool positionalFileSelectorSplit =
            positionalFileSource
            && typeName is not null
            && StructuralViewRegistry
                .HasUnambiguousMemberTail(typeName);
        bool selectorSplit =
            explicitSourceSelectorSplit
            || positionalFileSelectorSplit;
        bool multiArgumentDottedTarget =
            !sourceInputs.HasExplicitSource
            && sourceInputs.Args.Length > 1
            && !positionalFileSource;
        if (typeName is null
            || (!multiArgumentDottedTarget
                && !selectorSplit)
            || !typeName.Contains('.')
            || positionalMembers.Count != 0
            || (optionMembers.Length != 0
                && (!mergeExplicitMemberSelectors
                    || !selectorSplit)))
        {
            return;
        }

        var (splitTypeName, splitMemberName) =
            SharedParsers.SplitTrailingMember(typeName);
        if (splitMemberName is null)
            return;

        positionalMembers.Add(splitMemberName);
        typeName = splitTypeName;
    }

    private static bool HasPotentialImpliedMember(string typeName)
    {
        var (_, memberName) =
            SharedParsers.SplitTrailingMember(typeName);
        return memberName is not null
            || CSharpText.FqnParser.LastTopLevelDot(
                typeName) > 0;
    }

    private static OptionError? GetMemberSelectorConflictError(
        IEnumerable<string> members)
    {
        int? overloadIndex = null;
        string? digestPrefix = null;
        foreach (string member in members)
        {
            MemberTargetSelector selector =
                MemberTargetSelector.Parse(member);
            if (selector.OverloadIndex is { } candidateIndex)
            {
                if (overloadIndex is { } existingIndex
                    && existingIndex != candidateIndex)
                {
                    return new OptionError(
                        "A member selection cannot combine different overload selectors.");
                }

                overloadIndex = candidateIndex;
            }

            if (selector.DigestPrefix is not { Length: > 0 } candidateDigest)
                continue;
            if (digestPrefix is not null
                && !digestPrefix.StartsWith(
                    candidateDigest,
                    StringComparison.OrdinalIgnoreCase)
                && !candidateDigest.StartsWith(
                    digestPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new OptionError(
                    "A member selection cannot combine different overload selectors.");
            }

            if (digestPrefix is null
                || candidateDigest.Length > digestPrefix.Length)
            {
                digestPrefix = candidateDigest;
            }
        }

        return null;
    }

    internal static OptionError? GetMermaidOptionError(
        ParseResult parseResult,
        SharedOptions options)
    {
        bool embeddedMermaid =
            options.IsEmbeddedMermaid(parseResult);
        if (parseResult.GetValue(options.Mermaid)
            && (parseResult.GetValue(options.Json)
                || parseResult.GetValue(options.PlainText)
                || parseResult.GetValue(options.Bare)
                || parseResult.GetValue(options.Table)
                || parseResult.GetValue(options.Tsv)
                || parseResult.GetValue(options.Jsonl)
                || (!embeddedMermaid
                    && parseResult.GetResult(options.Verbosity)
                        is { Implicit: false })))
        {
            return new OptionError(
                "--mermaid is standalone unless paired with --markdown; it cannot combine with another output format.");
        }

        return null;
    }

}
