using System.Collections.Immutable;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using Markout;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace DotnetInspect.Cli.Planning;

public enum StructuralViewIdentity
{
    Package,
    PackageSingleLibrary,
    PackageAllLibraries,
    DirectLibrary,
    LibraryCoordinate,
    Type,
    MemberType,
    MemberTarget,
}

[Flags]
public enum StructuralParserCapabilities
{
    None = 0,
    Sections = 1 << 0,
    Fields = 1 << 1,
    Columns = 1 << 2,
    Count = 1 << 3,
    Print = 1 << 4,
    Value = 1 << 5,
    Urls = 1 << 6,
    Paths = 1 << 7,
    Tree = 1 << 8,
    Rows = 1 << 9,
    TypeFilter = 1 << 10,
    MemberFilter = 1 << 11,
    Overload = 1 << 12,
    Digest = 1 << 13,
    Coordinates = 1 << 14,
    BodyKindFilter = 1 << 15,
}

[Flags]
public enum StructuralSectionInput
{
    None = 0,
    TypeFilter = 1 << 0,
    MemberFilter = 1 << 1,
    ExactMember = 1 << 2,
    IlCoordinate = 1 << 3,
    HeapCoordinate = 1 << 4,
    BodyKindFilter = 1 << 5,
}

public enum StructuralOutputShape
{
    Document,
    Rows,
}

public sealed record StructuralViewDescriptor(
    StructuralViewIdentity Identity,
    int Precedence,
    string DestinationCommand,
    string ViewMode,
    ImmutableArray<InspectionCatalogIdentity> Catalogs,
    StructuralParserCapabilities ParserCapabilities);

public sealed record StructuralRoute(
    StructuralViewDescriptor View,
    InspectionCatalogIdentity Catalog)
{
    public string Label =>
        $"{View.DestinationCommand}/{View.ViewMode}/{Catalog}";
}

public sealed record CommandlessStructuralRoute(
    StructuralRoute Route,
    string[] RewrittenTokens);

public sealed record StructuralAlternativeSelection(
    StructuralRoute Route,
    bool CompleteCatalog,
    ImmutableArray<string> ResolvedSections,
    ImmutableArray<SectionSelectorDiagnostic> UnresolvedSelectors,
    OptionError? Error = null);

public sealed record StructuralCatalogAlternatives(
    ImmutableArray<StructuralAlternativeSelection> Alternatives);

public abstract record StructuralDiscoveryPlan
{
    public sealed record Resolved(StructuralRoute Route)
        : StructuralDiscoveryPlan;

    public sealed record Alternatives(
        StructuralCatalogAlternatives Value)
        : StructuralDiscoveryPlan;
}

public sealed record StructuralSchemaProjection(
    StructuralRoute Route,
    DocumentSchema Schema,
    IReadOnlyList<string> SelectableSectionNames,
    IReadOnlyList<string> DefaultSectionNames,
    IReadOnlyDictionary<string, string> SectionCostAnnotations,
    IReadOnlyDictionary<string, string[]> SectionCategories,
    IReadOnlySet<string>? ListedCategoryDoors,
    IReadOnlySet<string> CatalogHiddenSections,
    IReadOnlySet<string> ExactOnlySections,
    IReadOnlyDictionary<
        string,
        SectionCardinalityDeclaration>? SectionCardinalities,
    ImmutableDictionary<string, StructuralSectionInput> SectionInputs,
    OutputCapabilityCatalog? OutputCapabilities);

public sealed record StructuralDiscoveryRequest(
    string[]? Discover,
    string[]? Select,
    bool SelectDefault,
    bool Tree,
    OutputFormat Format,
    bool TableExplicitlySet,
    bool NoHeader,
    Verbosity Verbosity,
    IReadOnlySet<string>? IncludeSections,
    bool Schema,
    bool Details,
    IProjectionOptions Projection)
{
    public InspectionSectionIntent SectionIntent =>
        new(
            [.. Select ?? []],
            SelectDefault,
            [.. Discover ?? []],
            InspectionDiscoveryMode.Structural);

    public static StructuralDiscoveryRequest From(
        InspectionOptions options)
        => new(
            options.Discover,
            options.Select,
            options.SelectDefault,
            options.Tree,
            OutputFormatResolver.ResolveStored(
                options.Format,
                options.JsonOutput,
                plainText: false,
                options.Tabular,
                options.Tsv,
                options.Jsonl),
            options.TabularExplicitlySet,
            options.NoHeader,
            options.Verbosity,
            options.IncludeSections,
            options.Schema,
            options.DiscoverDetails,
            options);

    public static StructuralDiscoveryRequest From(ApiOptions options)
        => new(
            options.Discover,
            options.Select,
            options.SelectDefault,
            options.Tree,
            OutputFormatResolver.ResolveStored(
                options.Format,
                options.JsonOutput,
                options.PlainText,
                options.Tabular,
                options.Tsv,
                options.Jsonl),
            options.TabularExplicitlySet,
            options.NoHeader,
            options.Verbosity,
            null,
            options.Schema,
            false,
            options);

    public static StructuralDiscoveryRequest From(
        LibraryOptions options)
        => new(
            options.Discover,
            options.Select,
            options.SelectDefault,
            options.Tree,
            OutputFormatResolver.ResolveStored(
                options.Format,
                options.JsonOutput,
                options.PlainText,
                options.Tabular,
                options.Tsv,
                options.Jsonl),
            options.TabularExplicitlySet,
            options.NoHeader,
            options.Verbosity,
            options.IncludeSections,
            options.Schema,
            options.DiscoverDetails,
            options);

    public static StructuralDiscoveryRequest From(
        ParseResult parseResult,
        SharedOptions options,
        OutputFormat defaultFormat = OutputFormat.Markdown)
    {
        OutputFormat format =
            options.ResolveFormat(parseResult, defaultFormat);
        return new StructuralDiscoveryRequest(
            options.ParseDiscover(parseResult),
            options.ParseSelect(parseResult),
            options.ParseSelectDefault(parseResult),
            options.ParseTree(parseResult),
            format,
            options.IsTableExplicitlySet(parseResult),
            parseResult.GetValue(options.NoHeaders),
            options.ParseVerbosity(parseResult),
            null,
            options.ParseSchema(parseResult),
            false,
            ProjectionAudit.Requested(parseResult, options));
    }
}

public static class StructuralViewRegistry
{
    private const StructuralParserCapabilities SharedProjectionCapabilities =
        StructuralParserCapabilities.Sections
        | StructuralParserCapabilities.Fields
        | StructuralParserCapabilities.Columns
        | StructuralParserCapabilities.Count
        | StructuralParserCapabilities.Value
        | StructuralParserCapabilities.Urls
        | StructuralParserCapabilities.Paths
        | StructuralParserCapabilities.Tree
        | StructuralParserCapabilities.Rows;

    private static readonly ImmutableArray<StructuralViewDescriptor>
        RegisteredViews =
        [
            new(
                StructuralViewIdentity.Package,
                10,
                PackageCommand.Name,
                "package",
                [InspectionCatalogIdentity.Package],
                SharedProjectionCapabilities
                | StructuralParserCapabilities.Print
                | StructuralParserCapabilities.TypeFilter),
            new(
                StructuralViewIdentity.PackageSingleLibrary,
                20,
                PackageCommand.Name,
                "single-library",
                [InspectionCatalogIdentity.Library],
                SharedProjectionCapabilities
                | StructuralParserCapabilities.TypeFilter),
            new(
                StructuralViewIdentity.PackageAllLibraries,
                30,
                PackageCommand.Name,
                "all-libraries",
                [InspectionCatalogIdentity.LibraryAggregate],
                SharedProjectionCapabilities
                & ~StructuralParserCapabilities.Fields
                & ~StructuralParserCapabilities.Columns
                | StructuralParserCapabilities.TypeFilter),
            new(
                StructuralViewIdentity.DirectLibrary,
                40,
                "library",
                "library",
                [InspectionCatalogIdentity.Library],
                SharedProjectionCapabilities
                | StructuralParserCapabilities.Print
                | StructuralParserCapabilities.TypeFilter
                | StructuralParserCapabilities.BodyKindFilter),
            new(
                StructuralViewIdentity.LibraryCoordinate,
                45,
                "library coordinate",
                "coordinate",
                [InspectionCatalogIdentity.Library],
                SharedProjectionCapabilities
                | StructuralParserCapabilities.Print
                | StructuralParserCapabilities.Coordinates),
            new(
                StructuralViewIdentity.Type,
                50,
                TypeCommand.Name,
                "type",
                [
                    InspectionCatalogIdentity.ApiType,
                    InspectionCatalogIdentity.ApiMember,
                ],
                SharedProjectionCapabilities
                | StructuralParserCapabilities.Print
                | StructuralParserCapabilities.TypeFilter
                | StructuralParserCapabilities.MemberFilter),
            new(
                StructuralViewIdentity.MemberType,
                60,
                MemberCommand.Name,
                "type-view",
                [InspectionCatalogIdentity.ApiMember],
                SharedProjectionCapabilities
                | StructuralParserCapabilities.Print
                | StructuralParserCapabilities.MemberFilter),
            new(
                StructuralViewIdentity.MemberTarget,
                70,
                MemberCommand.Name,
                "member-target",
                [
                    InspectionCatalogIdentity.ApiMemberOverload,
                    InspectionCatalogIdentity.ApiMemberDetail,
                ],
                SharedProjectionCapabilities
                | StructuralParserCapabilities.Print
                | StructuralParserCapabilities.MemberFilter
                | StructuralParserCapabilities.Overload
                | StructuralParserCapabilities.Digest),
        ];

    public static IReadOnlyList<StructuralViewDescriptor> All =>
        RegisteredViews;

    public static StructuralViewDescriptor Get(
        StructuralViewIdentity identity)
        => RegisteredViews.First(view => view.Identity == identity);

    public static StructuralRoute Route(
        StructuralViewIdentity view,
        InspectionCatalogIdentity catalog)
    {
        StructuralViewDescriptor descriptor = Get(view);
        if (!descriptor.Catalogs.Contains(catalog))
        {
            throw new ArgumentException(
                $"Catalog '{catalog}' is not registered for structural view '{view}'.",
                nameof(catalog));
        }

        return new StructuralRoute(descriptor, catalog);
    }

    public static bool TryClassifyCommandless(
        string[] tokens,
        bool structuralDiscovery,
        bool hasBareLibraryTarget,
        out CommandlessStructuralRoute? classification)
    {
        classification = null;
        if (tokens.Length == 0)
            return false;

        if (hasBareLibraryTarget)
        {
            classification = new CommandlessStructuralRoute(
                Route(
                    StructuralViewIdentity.PackageAllLibraries,
                    InspectionCatalogIdentity.LibraryAggregate),
                [PackageCommand.Name, .. tokens]);
            return true;
        }

        if (!structuralDiscovery)
            return false;

        string target = tokens[0];
        if (CommandLineHelpers.TryClassifyAsFilePath(
                target,
                out string? dllPath,
                out string? nupkgPath))
        {
            if (dllPath is not null)
            {
                classification = new CommandlessStructuralRoute(
                    Route(
                        StructuralViewIdentity.DirectLibrary,
                        InspectionCatalogIdentity.Library),
                    ["library", .. tokens]);
                return true;
            }

            if (nupkgPath is not null)
            {
                bool aggregateLibrary =
                    hasBareLibraryTarget;
                bool exactLibraryTarget =
                    ContainsOption(tokens, "--library")
                    && !hasBareLibraryTarget
                    || ContainsOption(tokens, "--namesake-library");
                StructuralViewIdentity view = aggregateLibrary
                    ? StructuralViewIdentity.PackageAllLibraries
                    : exactLibraryTarget
                        ? StructuralViewIdentity.PackageSingleLibrary
                        : StructuralViewIdentity.Package;
                InspectionCatalogIdentity catalog = view switch
                {
                    StructuralViewIdentity.PackageAllLibraries =>
                        InspectionCatalogIdentity.LibraryAggregate,
                    StructuralViewIdentity.PackageSingleLibrary =>
                        InspectionCatalogIdentity.Library,
                    _ => InspectionCatalogIdentity.Package,
                };
                classification = new CommandlessStructuralRoute(
                    Route(view, catalog),
                    [PackageCommand.Name, .. tokens]);
                return true;
            }
        }

        bool hasMemberOption =
            ContainsOption(tokens, "--member")
            || ContainsOption(tokens, "-m");
        bool hasTypeOption =
            ContainsOption(tokens, "--type")
            || ContainsOption(tokens, "-t");
        string? libraryValue =
            GetOptionValues(tokens, "--library")
                .LastOrDefault();
        bool hasPackageLibraryValue =
            libraryValue is not null
            && SourceResolver
                .IsPackageLibraryValue(target, libraryValue);
        if (hasTypeOption
            && hasPackageLibraryValue)
        {
            classification = new CommandlessStructuralRoute(
                Route(
                    StructuralViewIdentity.PackageSingleLibrary,
                    InspectionCatalogIdentity.Library),
                [PackageCommand.Name, .. tokens]);
            return true;
        }
        bool hasExplicitApiSource =
            ContainsOption(tokens, "--package")
            || ContainsOption(tokens, "--platform")
            || ContainsOption(tokens, "--project")
            || (ContainsOption(tokens, "--library")
                && !hasPackageLibraryValue);
        string? typeOptionValue =
            GetOptionValues(tokens, "-t", "--type")
                .LastOrDefault();
        string? typeOptionFilter =
            SharedParsers.ParseTypeFilter(typeOptionValue);
        bool typeOptionSelectsListing =
            new TypeGestureIntent(typeOptionFilter)
                .SelectsListingCatalog(target);
        if (hasTypeOption
            && (hasExplicitApiSource
                || typeOptionSelectsListing))
        {
            classification = new CommandlessStructuralRoute(
                Route(
                    StructuralViewIdentity.Type,
                    InspectionCatalogIdentity.ApiType),
                [TypeCommand.Name, .. tokens]);
            return true;
        }

        bool genericDottedMemberAmbiguity =
            hasMemberOption
            && TypeMatcher.HasExplicitGenericNotation(target)
            && !HasExplicitGenericTypeTail(target)
            && !HasUnambiguousMemberTail(target);
        if (hasMemberOption
            && !genericDottedMemberAmbiguity)
        {
            InspectionCatalogIdentity catalog =
                GetCommandlessMemberCatalog(tokens);
            classification = new CommandlessStructuralRoute(
                Route(
                    catalog == InspectionCatalogIdentity.ApiMember
                        ? StructuralViewIdentity.MemberType
                        : StructuralViewIdentity.MemberTarget,
                    catalog),
                [MemberCommand.Name, .. tokens]);
            return true;
        }

        if (tokens.Length >= 2
            && !tokens[1].StartsWith(
                "-",
                StringComparison.Ordinal)
            && !CommandLineHelpers.LooksLikeVersionNumber(
                tokens[1]))
        {
            classification = new CommandlessStructuralRoute(
                Route(
                    StructuralViewIdentity.Type,
                    TypeMatcher.IsTypeGlobPattern(tokens[1])
                        ? InspectionCatalogIdentity.ApiType
                        : InspectionCatalogIdentity.ApiMember),
                [
                    TypeCommand.Name,
                    tokens[1],
                    "--package",
                    target,
                    .. tokens[2..],
                ]);
            return true;
        }

        if (TypeMatcher.IsTypeGlobPattern(target))
        {
            classification = new CommandlessStructuralRoute(
                Route(
                    StructuralViewIdentity.Type,
                    InspectionCatalogIdentity.ApiType),
                [TypeCommand.Name, .. tokens]);
            return true;
        }

        if (ContainsOption(tokens, "--index")
            || HasUnambiguousMemberTail(target))
        {
            InspectionCatalogIdentity catalog =
                GetImpliedMemberCatalog(target, tokens);
            string[] rewrittenTokens =
                ContainsOption(tokens, "--index")
                && SharedParsers.SplitTrailingMember(target)
                    is { MemberName: { } memberName } split
                    ? [
                        MemberCommand.Name,
                        split.TypeName,
                        "-m",
                        memberName,
                        .. tokens[1..],
                    ]
                    : [MemberCommand.Name, .. tokens];
            classification = new CommandlessStructuralRoute(
                Route(
                    StructuralViewIdentity.MemberTarget,
                    catalog),
                rewrittenTokens);
            return true;
        }

        if (HasExplicitGenericTypeTail(target)
            && !HasGenericTypeAndGenericTailAmbiguity(target)
            && !RequiresGenericTailMemberAlternative(
                target,
                tokens))
        {
            classification = new CommandlessStructuralRoute(
                Route(
                    StructuralViewIdentity.Type,
                    InspectionCatalogIdentity.ApiMember),
                [TypeCommand.Name, .. tokens]);
            return true;
        }

        if (ContainsOption(tokens, "--versions")
            || ContainsOption(tokens, "--versions-with-feed")
            || target.Contains('@'))
        {
            classification = new CommandlessStructuralRoute(
                Route(
                    StructuralViewIdentity.Package,
                    InspectionCatalogIdentity.Package),
                [PackageCommand.Name, .. tokens]);
            return true;
        }

        return false;
    }

    public static StructuralCatalogAlternatives
        CreateCommandlessAlternatives(
            string[] tokens,
            StructuralDiscoveryRequest request,
            string? sourceIdentityTypeTarget = null)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Length == 0)
            throw new ArgumentException(
                "A commandless structural query requires a target token.",
                nameof(tokens));

        string target = tokens[0];
        string[] memberSelectors =
            GetOptionValues(tokens, "-m", "--member");
        var memberFilter =
            SharedParsers.ParseMemberFilter(memberSelectors);
        bool hasBodyKindFilter =
            BodyKindQueryOptions.TryExtract(
                GetOptionValues(tokens, "--where"),
                out BodyKindQueryOptions bodyKindQuery,
                out _,
                out _)
            && bodyKindQuery.HasFilter;
        bool hasExplicitApiSource =
            ContainsOption(tokens, "--package")
            || ContainsOption(tokens, "--platform")
            || ContainsOption(tokens, "--project");
        string? packageValue =
            GetOptionValues(tokens, "--package")
                .FirstOrDefault();
        string? platformValue =
            GetOptionValues(tokens, "--platform")
                .FirstOrDefault();
        bool isPackageIdentity =
            packageValue is not null
            && target.Equals(
                packageValue,
                StringComparison.OrdinalIgnoreCase);
        bool isPlatformIdentity =
            platformValue is not null
            && target.Equals(
                platformValue,
                StringComparison.OrdinalIgnoreCase);
        string? libraryValue =
            GetOptionValues(tokens, "--library")
                .FirstOrDefault();
        bool hasPackageLibraryValue =
            libraryValue is not null
            && SourceResolver
                .IsPackageLibraryValue(target, libraryValue);
        bool hasExplicitLibraryPath =
            libraryValue is not null
            && !hasPackageLibraryValue;
        hasExplicitApiSource |= hasExplicitLibraryPath;
        bool hasLibraryGesture =
            ContainsOption(tokens, "--library")
            && hasPackageLibraryValue;
        bool hasTypeMarker =
            ContainsOption(tokens, "-t")
            || ContainsOption(tokens, "--type");
        string? typeMarkerValue =
            GetOptionValues(tokens, "-t", "--type")
                .LastOrDefault();
        string? typeFilter =
            SharedParsers.ParseTypeFilter(typeMarkerValue);
        string? typeCatalogTarget =
            (isPackageIdentity || isPlatformIdentity)
                ? sourceIdentityTypeTarget
                : target;
        bool hasTypeFilter =
            new TypeGestureIntent(typeFilter)
                .SelectsListingCatalog(typeCatalogTarget);
        SectionDemandClassification demand =
            ApiSectionDemandIndex.Classify(
                InspectionSurface.Commandless,
                request.SectionIntent.DemandSelectors,
                request.SelectDefault,
                InspectionTargetRequirement.MemberSet);
        var routes = new List<StructuralRoute>();
        if (isPackageIdentity)
        {
            routes.Add(
                Route(
                    StructuralViewIdentity.Package,
                    InspectionCatalogIdentity.Package));
        }
        if (isPlatformIdentity)
        {
            routes.Add(
                Route(
                    StructuralViewIdentity.DirectLibrary,
                    InspectionCatalogIdentity.Library));
        }
        if (!hasExplicitApiSource)
        {
            routes.Add(
                Route(
                    hasLibraryGesture
                        ? StructuralViewIdentity.PackageSingleLibrary
                        : StructuralViewIdentity.Package,
                    hasLibraryGesture
                        ? InspectionCatalogIdentity.Library
                        : InspectionCatalogIdentity.Package));
            if (!hasLibraryGesture)
            {
                routes.Add(
                    Route(
                        StructuralViewIdentity.DirectLibrary,
                        InspectionCatalogIdentity.Library));
            }
        }

        bool exactGenericType =
            HasExplicitGenericTypeTail(target)
            && !HasGenericTypeAndGenericTailAmbiguity(target)
            && !RequiresGenericTailMemberAlternative(
                target,
                tokens);
        bool memberSelectorsCanFilterType =
            memberFilter.Count > 0;
        if (memberFilter.Count == 0
            || memberSelectorsCanFilterType)
        {
            bool exactTypeGesture =
                !hasTypeFilter
                && ((hasTypeMarker
                        && typeCatalogTarget is not null)
                    || exactGenericType);
            if (!exactTypeGesture)
            {
                routes.Add(
                    Route(
                        StructuralViewIdentity.Type,
                        InspectionCatalogIdentity.ApiType));
            }
            if (!hasTypeFilter)
            {
                routes.Add(
                    Route(
                        StructuralViewIdentity.Type,
                        InspectionCatalogIdentity.ApiMember));
            }
        }

        var (_, impliedMemberName) =
            SharedParsers.SplitTrailingMember(target);
        if (impliedMemberName is null
            && (HasGenericTypeAndGenericTailAmbiguity(target)
                || (demand.RequiredTarget
                        == InspectionTargetRequirement.ExactMember
                    && HasExplicitGenericTypeTail(target)
                    && CSharpText.FqnParser.LastTopLevelDot(
                        target) > 0)))
        {
            impliedMemberName =
                target[
                    (CSharpText.FqnParser.LastTopLevelDot(
                        target) + 1)..];
        }
        bool tailCanBeMember =
            impliedMemberName is not null
            && !TypeMatcher.IsTypeGlobPattern(target);
        OptionError? memberError = null;
        if (memberFilter.Count > 0
            || (!hasTypeMarker && tailCanBeMember))
        {
            string[] members = memberFilter.Count > 0
                ? memberSelectors
                : [impliedMemberName!];
            memberError = MemberOptionsParser.ValidateStructuralMemberSelection(
                members,
                target,
                ctor: false,
                index: null,
                hasBodyKindFilter,
                demand.RequiredTarget == InspectionTargetRequirement.ExactMember,
                out _,
                out bool exactMember);
            InspectionCatalogIdentity memberCatalog =
                exactMember
                    ? InspectionCatalogIdentity.ApiMemberDetail
                    : InspectionCatalogIdentity.ApiMemberOverload;
            routes.Add(
                Route(
                    StructuralViewIdentity.MemberTarget,
                    memberCatalog));
        }
        StructuralCatalogAlternatives alternatives = CreateAlternatives(routes, request);
        return memberError is null
            ? alternatives
            : new StructuralCatalogAlternatives(
                [.. alternatives.Alternatives.Select(alternative =>
                    alternative.Route.View.Identity == StructuralViewIdentity.MemberTarget
                        ? alternative with
                        {
                            CompleteCatalog = false,
                            ResolvedSections = [],
                            Error = memberError,
                        }
                        : alternative)]);
    }

    public static StructuralDiscoveryPlan CreateApiPlan(
        ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ResolvedMemberInspectionPlan plan =
            ResolvedMemberInspectionPlan
                .FromCompatibilityOptions(
                    options,
                    selectCatalogFromDemand: true);
        bool exactMemberGesture =
            options is MemberOptions
            {
                OverloadIndex: not null,
            }
            || options is MemberOptions memberWithDigest
                && !string.IsNullOrWhiteSpace(
                    memberWithDigest.MemberDigest);
        if (options is MemberOptions
            {
                MemberFilter.Count: 0,
                TypeName: { } typeName,
            }
            && CSharpText.FqnParser.LastTopLevelDot(typeName) > 0)
        {
            if (exactMemberGesture)
            {
                return new StructuralDiscoveryPlan.Resolved(
                    Route(
                        StructuralViewIdentity.MemberTarget,
                        InspectionCatalogIdentity.ApiMemberDetail));
            }

            int tailStart =
                CSharpText.FqnParser.LastTopLevelDot(typeName);
            MemberTargetSelector implied =
                MemberTargetSelector.Parse(
                    typeName[(tailStart + 1)..]);
            InspectionCatalogIdentity peeledCatalog =
                implied.OverloadIndex is not null
                || !string.IsNullOrWhiteSpace(
                    implied.DigestPrefix)
                || plan.Selection.RequiredTarget
                    == InspectionTargetRequirement.ExactMember
                    ? InspectionCatalogIdentity.ApiMemberDetail
                    : InspectionCatalogIdentity.ApiMemberOverload;
            string[]? selectors =
                options.Discover is { Length: > 0 }
                    ? options.Discover
                    : options.Select;
            return new StructuralDiscoveryPlan.Alternatives(
                CreateAlternatives(
                    [
                        Route(
                            StructuralViewIdentity.MemberType,
                            InspectionCatalogIdentity.ApiMember),
                        Route(
                            StructuralViewIdentity.MemberTarget,
                            peeledCatalog),
                    ],
                    StructuralDiscoveryRequest.From(options)));
        }

        StructuralViewIdentity view =
            options is TypeOptions
                ? StructuralViewIdentity.Type
                : plan.Selection.Catalog
                    == InspectionCatalogIdentity.ApiMember
                    ? StructuralViewIdentity.MemberType
                    : StructuralViewIdentity.MemberTarget;
        return new StructuralDiscoveryPlan.Resolved(
            Route(view, plan.Selection.Catalog));
    }

    public static StructuralSchemaProjection Project(
        StructuralRoute route,
        StructuralOutputShape outputShape =
            StructuralOutputShape.Document)
    {
        DocumentSchema schema;
        IReadOnlyList<string> selectableSections;
        IReadOnlyList<string> defaultSections;
        IReadOnlyDictionary<string, string> annotations;
        IReadOnlyDictionary<string, string[]> categories;
        IReadOnlySet<string>? listedCategoryDoors = null;
        IReadOnlySet<string> catalogHiddenSections =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlySet<string> exactOnlySections =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<
            string,
            SectionCardinalityDeclaration>? sectionCardinalities = null;
        OutputCapabilityCatalog? outputCapabilities = null;
        switch (route.Catalog)
        {
            case InspectionCatalogIdentity.Package:
            {
                var catalog = PackageSectionDescriptors.CreateCatalog();
                schema = PackageCommand.PackageDiscoverySchema();
                selectableSections =
                    catalog.Sections.SelectableSectionNames;
                defaultSections =
                    catalog.Sections.BareSelectSectionNames;
                annotations = catalog.Pipeline.GetCostAnnotations();
                categories = catalog.Sections.SelectionCategoryMap;
                listedCategoryDoors =
                    catalog.Pipeline.GetListedCategoryDoors();
                catalogHiddenSections =
                    catalog.Pipeline.GetCatalogHiddenSections();
                break;
            }
            case InspectionCatalogIdentity.Library:
            {
                var catalog = LibrarySections.CreateCatalog();
                schema = LibraryCommand.CreateStructuralSchema();
                selectableSections =
                    catalog.Sections.SelectableSectionNames;
                defaultSections = catalog.Sections.InfoSectionNames;
                annotations = catalog.Pipeline.GetCostAnnotations();
                categories = catalog.Sections.SelectionCategoryMap;
                listedCategoryDoors =
                    catalog.Pipeline.GetListedCategoryDoors();
                catalogHiddenSections =
                    catalog.Pipeline.GetCatalogHiddenSections();
                outputCapabilities =
                    LibraryOutputCapabilities.Catalog;
                sectionCardinalities =
                    LibrarySectionCardinality.ExactDeclarations;
                break;
            }
            case InspectionCatalogIdentity.LibraryAggregate:
            {
                var catalog = LibrarySections.CreateCatalog();
                schema = outputShape == StructuralOutputShape.Rows
                    ? PackageCommand
                        .PackageAllLibrariesDiscoverySchema()
                    : LibraryCommand.CreateStructuralSchema();
                selectableSections =
                    catalog.Sections.SelectableSectionNames;
                defaultSections = catalog.Sections.InfoSectionNames;
                annotations = catalog.Pipeline.GetCostAnnotations();
                categories = catalog.Sections.SelectionCategoryMap;
                listedCategoryDoors =
                    catalog.Pipeline.GetListedCategoryDoors();
                catalogHiddenSections =
                    catalog.Pipeline.GetCatalogHiddenSections();
                outputCapabilities =
                    LibraryOutputCapabilities
                        .AggregateCardinalityCatalog;
                sectionCardinalities =
                    LibrarySectionCardinality.AggregateDeclarations;
                break;
            }
            case InspectionCatalogIdentity.ApiType:
            {
                var pipeline = ApiTypeSectionDescriptors.CreatePipeline();
                schema = ApiCommand.GetStructuralSchema(route.Catalog);
                selectableSections =
                    pipeline.SelectableSectionNames;
                defaultSections = pipeline.FixedOverviewSectionNames;
                annotations = pipeline.GetCostAnnotations();
                categories = pipeline.GetCategoryMap();
                listedCategoryDoors =
                    pipeline.GetListedCategoryDoors();
                catalogHiddenSections =
                    pipeline.GetCatalogHiddenSections();
                sectionCardinalities =
                    ApiTypeSectionCardinality.Declarations;
                break;
            }
            case InspectionCatalogIdentity.ApiMember:
            case InspectionCatalogIdentity.ApiMemberOverload:
            case InspectionCatalogIdentity.ApiMemberDetail:
            {
                var pipeline =
                    ApiInspectionCatalogRegistry.CreateMemberPipeline(
                        route.Catalog);
                schema = ApiCommand.GetStructuralSchema(route.Catalog);
                if (route.View.Identity == StructuralViewIdentity.Type
                    && route.Catalog
                        == InspectionCatalogIdentity.ApiMember)
                {
                    DocumentSchema relationSchema =
                        SearchViewContext.Default
                            .GetSchemaInfo<TypeRelationsResultView>()!
                            .ToDocumentSchema();
                    foreach (string sectionName in new[]
                    {
                        SectionNames.Implementers,
                        SectionNames.DerivedTypes,
                    })
                    {
                        var section =
                            relationSchema.GetSection(sectionName)!;
                        schema.Add(
                            sectionName,
                            section.ItemKind,
                            [.. section.Items.Select(item => item.Name)]);
                    }
                }
                selectableSections =
                    ApiInspectionCatalogRegistry
                        .Get(route.Catalog)
                        .SectionNames;
                defaultSections =
                    route.View.Identity
                        == StructuralViewIdentity.Type
                    && route.Catalog
                        == InspectionCatalogIdentity.ApiMember
                        ? pipeline.FixedOverviewSectionNames
                        : ApiInspectionCatalogRegistry
                            .Get(route.Catalog)
                            .DefaultSectionNames;
                annotations = pipeline.GetCostAnnotations();
                categories = pipeline.GetCategoryMap();
                listedCategoryDoors =
                    pipeline.GetListedCategoryDoors();
                catalogHiddenSections =
                    pipeline.GetCatalogHiddenSections();
                exactOnlySections =
                    ApiMemberSectionPipelines.GetExactOnlySections(
                        route.Catalog
                            == InspectionCatalogIdentity.ApiMemberOverload);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(route),
                    route.Catalog,
                    "Unknown structural catalog.");
        }

        if (route.Catalog is
            InspectionCatalogIdentity.ApiType
            or InspectionCatalogIdentity.ApiMember
            or InspectionCatalogIdentity.ApiMemberOverload
            or InspectionCatalogIdentity.ApiMemberDetail)
        {
            schema = DiscoverOutput.WithoutColumn(
                schema,
                "Select");
        }

        ImmutableDictionary<string, StructuralSectionInput> inputs =
            CreateSectionInputs(route, schema);
        StructuralSectionInput availableInputs =
            GetAvailableInputs(route.View.ParserCapabilities);
        if (inputs.Any(pair =>
                (pair.Value & ~availableInputs) != 0))
        {
            schema = FilterSchema(
                schema,
                inputs
                    .Where(pair =>
                        (pair.Value & ~availableInputs) == 0)
                    .Select(pair => pair.Key)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));
            inputs = inputs
                .Where(pair => schema.SectionNames.Contains(
                    pair.Key,
                    StringComparer.OrdinalIgnoreCase))
                .ToImmutableDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
        }
        selectableSections = selectableSections
            .Where(name => schema.SectionNames.Contains(
                name,
                StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var selectableSet = new HashSet<string>(
            selectableSections,
            StringComparer.OrdinalIgnoreCase);
        categories = categories
            .Select(pair => new KeyValuePair<string, string[]>(
                pair.Key,
                [.. pair.Value.Where(selectableSet.Contains)]))
            .Where(pair => pair.Value.Length > 0)
            .ToDictionary(
                pair => pair.Key,
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);

        return new StructuralSchemaProjection(
            route,
            schema,
            selectableSections,
            defaultSections,
            annotations,
            categories,
            listedCategoryDoors,
            catalogHiddenSections,
            exactOnlySections,
            sectionCardinalities,
            inputs,
            outputCapabilities);
    }

    public static int Execute(
        StructuralRoute route,
        StructuralDiscoveryRequest request,
        StructuralOutputShape outputShape =
            StructuralOutputShape.Document)
    {
        var (normalizedRequest, aliasError) = NormalizeLibrarySelectors(request, [route]);
        if (aliasError is not null)
        {
            CommandError.Write(aliasError.Value);
            return 1;
        }
        request = normalizedRequest;
        StructuralSchemaProjection projection = Project(route, outputShape);
        DocumentSchema schema = projection.Schema;
        IReadOnlyDictionary<
            string,
            SectionCardinalityDeclaration>? sectionCardinalities =
                route.Catalog == InspectionCatalogIdentity.Library
                && request.Projection is LibraryOptions libraryOptions
                && LibrarySectionCardinality.IsAllTfmPackageSelection(
                    libraryOptions)
                    ? null
                    : projection.SectionCardinalities;
        if (request.Details)
        {
            if (route.Catalog
                    == InspectionCatalogIdentity.LibraryAggregate
                && (request.Discover is not [var section]
                    || !section.Equals(
                        SectionNames.LibraryInfo,
                        StringComparison.OrdinalIgnoreCase)))
            {
                CommandError.Write(
                    "Detailed aggregate Library discovery currently supports "
                    + $"only '{SectionNames.LibraryInfo}'.");
                return 1;
            }

            if (request.Discover is { Length: > 1 })
            {
                CommandError.Write(
                    "--details supports bare -D or one exact category or "
                    + "section selector in this Library adoption.");
                return 1;
            }

            if (request.Select is not null
                || request.SelectDefault
                || request.IncludeSections is { Count: > 0 })
            {
                CommandError.Write(
                    "--details cannot be combined with -S/--select; name the "
                    + "category or section after -D/--discover.");
                return 1;
            }

            if (projection.OutputCapabilities is null)
            {
                CommandError.Write(
                    "Detailed discovery is not available for this command.");
                return 1;
            }

            DiscoveryOutputRequest detailedRequest =
                DiscoveryOutputRequest.Create(
                    request.Format,
                    request.Tree,
                    request.TableExplicitlySet,
                    request.NoHeader,
                    (int)request.Verbosity,
                    request.Projection);
            if (DetailedDiscoverOutput.Validate(detailedRequest) != 0)
                return 1;

            DiscoveryDocumentFactory.Projection? detailedProjection =
                DiscoveryDocumentFactory.CreateProjection(
                    "library",
                    request.Discover,
                    schema,
                    projection.SectionCategories,
                    projection.CatalogHiddenSections,
                    projection.ListedCategoryDoors,
                    projection.SectionCostAnnotations,
                    projection.ExactOnlySections,
                    projection.OutputCapabilities,
                    requireExactSelection: true,
                    sectionCardinalities:
                        sectionCardinalities);
            if (detailedProjection is null)
                return 1;

            return DetailedDiscoverOutput.Write(
                detailedProjection,
                detailedRequest);
        }

        var selectedSections =
            request.IncludeSections is { Count: > 0 }
                ? new HashSet<string>(
                    request.IncludeSections,
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
        bool hasExplicitSelection =
            request.Select is { Length: > 0 };
        if (hasExplicitSelection || request.SelectDefault)
        {
            SelectResult result = SelectResolver.ResolveSelectAsSections(
                request.Select,
                projection.SelectableSectionNames,
                projection.DefaultSectionNames,
                projection.SectionCategories,
                request.SelectDefault
                && !hasExplicitSelection,
                projection.ExactOnlySections);
            if (SelectOutput.WriteUnresolved(result))
                return 1;
            if (result.Sections is { Count: > 0 })
                selectedSections.UnionWith(result.Sections);
        }
        if (selectedSections.Count > 0
            || hasExplicitSelection
            || request.SelectDefault)
            schema = FilterSchema(schema, selectedSections);

        DiscoveryDocumentFactory.Projection? discoveryProjection = null;
        if (projection.OutputCapabilities is not null)
        {
            discoveryProjection =
                DiscoveryDocumentFactory.CreateProjection(
                "library",
                request.Discover,
                schema,
                projection.SectionCategories,
                request.Schema
                    ? null
                    : projection.CatalogHiddenSections,
                projection.ListedCategoryDoors,
                projection.SectionCostAnnotations,
                projection.ExactOnlySections,
                projection.OutputCapabilities,
                sectionCardinalities:
                    FilterCardinalities(
                        sectionCardinalities,
                        schema.SectionNames));
            if (discoveryProjection is null)
                return 1;
        }

        return DiscoverOutput.Execute(
            request.Discover,
            schema,
            DiscoveryOutputRequest.Create(
                request.Format,
                request.Tree,
                request.TableExplicitlySet,
                request.NoHeader,
                (int)request.Verbosity,
                request.Projection),
            sectionCostAnnotations:
                projection.SectionCostAnnotations,
            sectionCategories: projection.SectionCategories,
            catalogHiddenSections: request.Schema
                ? null
                : projection.CatalogHiddenSections,
            listedCategoryDoors:
                projection.ListedCategoryDoors,
            exactOnlySections: projection.ExactOnlySections,
            document: discoveryProjection?.Document,
            resourcePaths: discoveryProjection?.ResourcePaths.ToDictionary(
                static registration => registration.Identity,
                static registration => registration.Path));
    }

    public static int Execute(
        StructuralCatalogAlternatives alternatives,
        StructuralDiscoveryRequest request)
    {
        var (normalizedRequest, aliasError) = NormalizeLibrarySelectors(
            request,
            alternatives.Alternatives
                .Where(alternative => alternative.Error is null)
                .Select(alternative => alternative.Route));
        if (aliasError is not null)
        {
            CommandError.Write(aliasError.Value);
            return 1;
        }
        request = normalizedRequest;
        if (alternatives.Alternatives.Any(alternative => alternative.Error is not null)
            && !alternatives.Alternatives.Any(alternative =>
                alternative.Error is null
                && (alternative.CompleteCatalog || !alternative.ResolvedSections.IsEmpty)))
        {
            foreach (OptionError error in alternatives.Alternatives
                .Select(alternative => alternative.Error)
                .OfType<OptionError>()
                .Distinct())
            {
                CommandError.Write(error);
            }
            return 1;
        }

        if (request.Select is { Length: > 0 } selectors)
        {
            StructuralSchemaProjection[] projections =
            [
                .. alternatives.Alternatives
                    .Select(alternative =>
                        Project(alternative.Route)),
            ];
            string[] knownSections =
            [
                .. projections
                    .SelectMany(projection =>
                        projection.SelectableSectionNames)
                    .Distinct(StringComparer.OrdinalIgnoreCase),
            ];
            Dictionary<string, string[]> universalCategories =
                new(StringComparer.OrdinalIgnoreCase);
            foreach (StructuralSchemaProjection projection in
                     projections)
            {
                foreach (var (name, sections) in
                         projection.SectionCategories)
                {
                    universalCategories[name] =
                        universalCategories.TryGetValue(
                            name,
                            out string[]? existing)
                            ? [.. existing
                                .Concat(sections)
                                .Distinct(
                                    StringComparer.OrdinalIgnoreCase)]
                            : sections;
                }
            }

            SelectResult universalSelection =
                SelectResolver.ResolveSelectAsSections(
                    selectors,
                    knownSections,
                    infoSections: [],
                    universalCategories,
                    selectDefault: false);
            if (universalSelection.Unresolved.Count > 0
                && universalSelection.Sections
                    is null or { Count: 0 }
                && SelectOutput.WriteUnresolved(
                    universalSelection))
            {
                return 1;
            }
        }
        if (request.Discover is { Length: > 0 }
            && alternatives.Alternatives.All(
                alternative =>
                    alternative.ResolvedSections.IsEmpty))
        {
            HashSet<string> discoverySelectors =
                new(
                    request.Discover
                        .SelectMany(value =>
                            value.Split(
                                [',', ';'],
                                StringSplitOptions.TrimEntries
                                | StringSplitOptions
                                    .RemoveEmptyEntries))
                        .Select(
                            ArgumentPreprocessor
                                .UnescapeAtCategoryValue),
                    StringComparer.OrdinalIgnoreCase);
            SelectMiss[] discoveryMisses =
            [
                .. alternatives.Alternatives
                    .SelectMany(alternative =>
                        alternative.UnresolvedSelectors)
                    .Where(diagnostic =>
                        discoverySelectors.Contains(
                            diagnostic.Selector))
                    .GroupBy(
                        diagnostic => diagnostic.Selector,
                        StringComparer.OrdinalIgnoreCase)
                    .Select(group =>
                    {
                        SectionSelectorDiagnostic diagnostic =
                            group.First();
                        return new SelectMiss(
                            diagnostic.Selector,
                            [
                                .. group
                                    .SelectMany(item =>
                                        item.Suggestions)
                                    .Distinct(
                                        StringComparer
                                            .OrdinalIgnoreCase),
                            ],
                            diagnostic.IsGlob,
                            diagnostic.ListsAllSections);
                    }),
            ];
            if (discoveryMisses.Length > 0
                && SelectOutput.WriteUnresolved(
                    new SelectResult([], discoveryMisses)))
            {
                return 1;
            }
        }

        var schema = new DocumentSchema();
        var annotations =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        var categories =
            new Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase);
        var catalogHiddenSections =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
        var listedCategoryDoors =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
        bool hasCuratedCatalog = false;
        foreach (StructuralAlternativeSelection alternative in
                 alternatives.Alternatives)
        {
            if (alternative.Error is { } error)
            {
                schema.AddSection($"[{alternative.Route.Label}] error: {error.Message}");
                continue;
            }
            StructuralSchemaProjection projection =
                Project(alternative.Route);
            IReadOnlyCollection<string> sections =
                alternative.CompleteCatalog
                    ? projection.Schema.SectionNames
                    : alternative.ResolvedSections;
            if (alternative.CompleteCatalog
                && projection.ListedCategoryDoors is not null)
            {
                hasCuratedCatalog = true;
                foreach (string name in
                         projection.CatalogHiddenSections)
                {
                    catalogHiddenSections.Add(
                        $"[{alternative.Route.Label}] {name}");
                }
                foreach (string name in
                         projection.ListedCategoryDoors)
                {
                    listedCategoryDoors.Add(
                        $"[{alternative.Route.Label}] {name}");
                }
            }
            foreach (string name in sections)
            {
                var section = projection.Schema.GetSection(name);
                if (section is null)
                    continue;

                string labeledName =
                    $"[{alternative.Route.Label}] {name}";
                if (projection.SectionCostAnnotations.TryGetValue(
                        name,
                        out string? annotation))
                {
                    annotations[labeledName] = annotation;
                }
                string[] items =
                    [.. section.Items.Select(item => item.Name)];
                if (items.Length == 0)
                    schema.AddSection(labeledName);
                else
                    schema.Add(labeledName, section.ItemKind, items);
            }

            foreach (var (category, categorySections) in
                     projection.SectionCategories)
            {
                var includedSections =
                    new HashSet<string>(
                        sections,
                        StringComparer.OrdinalIgnoreCase);
                string[] labeledSections =
                [
                    .. categorySections
                        .Where(includedSections.Contains)
                        .Select(name =>
                            $"[{alternative.Route.Label}] {name}"),
                ];
                if (labeledSections.Length > 0)
                {
                    categories[
                        $"[{alternative.Route.Label}] {category}"] =
                        labeledSections;
                }
            }

            foreach (SectionSelectorDiagnostic diagnostic in
                     alternative.UnresolvedSelectors)
            {
                string labeledName =
                    $"[{alternative.Route.Label}] "
                    + $"unresolved '{diagnostic.Selector}'";
                if (diagnostic.Suggestions.Length == 0)
                    schema.AddSection(labeledName);
                else
                    schema.Add(
                        labeledName,
                        "suggestion",
                        [.. diagnostic.Suggestions]);
            }
        }

        return DiscoverOutput.Execute(
            discover: null,
            schema,
            DiscoveryOutputRequest.Create(
                request.Format,
                request.Tree,
                request.TableExplicitlySet,
                request.NoHeader,
                (int)request.Verbosity,
                request.Projection),
            sectionCostAnnotations: annotations,
            sectionCategories: categories,
            catalogHiddenSections: request.Schema
                || !hasCuratedCatalog
                ? null
                : catalogHiddenSections,
            listedCategoryDoors: hasCuratedCatalog
                ? listedCategoryDoors
                : null);
    }

    public static StructuralCatalogAlternatives CreateAlternatives(
        IEnumerable<StructuralRoute> routes,
        StructuralDiscoveryRequest request)
    {
        var alternatives =
            ImmutableArray.CreateBuilder<StructuralAlternativeSelection>();
        foreach (StructuralRoute route in routes)
        {
            var (routeRequest, aliasError) = NormalizeLibrarySelectors(request, [route]);
            if (aliasError is not null)
            {
                alternatives.Add(new StructuralAlternativeSelection(
                    route, CompleteCatalog: false, [], [], aliasError));
                continue;
            }
            StructuralSchemaProjection projection = Project(route);
            bool hasSelection =
                routeRequest.Select is { Length: > 0 }
                || routeRequest.SelectDefault;
            SelectResult selection =
                SelectResolver.ResolveSelectAsSections(
                    routeRequest.Select,
                    projection.SelectableSectionNames,
                    projection.DefaultSectionNames,
                    projection.SectionCategories,
                    routeRequest.SelectDefault,
                    projection.ExactOnlySections);
            IReadOnlyList<string> discoverySections =
                hasSelection
                    ? [.. selection.Sections ?? []]
                    : projection.SelectableSectionNames;
            var discoverySet = new HashSet<string>(
                discoverySections,
                StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string[]> discoveryCategories =
                projection.SectionCategories
                    .Select(pair => new KeyValuePair<string, string[]>(
                        pair.Key,
                        [.. pair.Value.Where(discoverySet.Contains)]))
                    .Where(pair => pair.Value.Length > 0)
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase);
            SelectResult discovery =
                routeRequest.Discover is { Length: > 0 }
                    ? SelectResolver.ResolveSelectAsSections(
                        routeRequest.Discover,
                        discoverySections,
                        infoSections: [],
                        discoveryCategories,
                        selectDefault: false,
                        exactOnlySections:
                            projection.ExactOnlySections)
                    : selection;
            bool completeCatalog =
                !hasSelection
                && routeRequest.Discover is not { Length: > 0 };
            ImmutableArray<SectionSelectorDiagnostic> unresolved =
            [
                .. selection.Unresolved.Select(ToDiagnostic),
                .. routeRequest.Discover is { Length: > 0 }
                    ? discovery.Unresolved.Select(ToDiagnostic)
                    : [],
            ];
            alternatives.Add(
                new StructuralAlternativeSelection(
                    route,
                    completeCatalog,
                    [.. discovery.Sections ?? []],
                    unresolved));
        }

        return new StructuralCatalogAlternatives(
            alternatives.ToImmutable());

        static SectionSelectorDiagnostic ToDiagnostic(
            SelectMiss miss) =>
            new(
                miss.Value,
                [.. miss.Suggestions],
                miss.IsGlob,
                miss.ListsAllSections);
    }

    private static (StructuralDiscoveryRequest Request, OptionError? Error) NormalizeLibrarySelectors(
        StructuralDiscoveryRequest request,
        IEnumerable<StructuralRoute> routes)
    {
        if (!routes.Any(route => route.Catalog is
                InspectionCatalogIdentity.Library or InspectionCatalogIdentity.LibraryAggregate))
        {
            return (request, null);
        }

        var (select, selectError) = LibraryCommand.ResolveTableAliases(request.Select);
        if (selectError is not null)
            return (request, new OptionError(selectError));
        var (discover, discoverError) = LibraryCommand.ResolveTableAliases(request.Discover);
        if (discoverError is not null)
            return (request, new OptionError(discoverError));

        return (request with
        {
            Select = select ?? request.Select,
            Discover = discover ?? request.Discover,
        }, null);
    }

    private static ImmutableDictionary<string, StructuralSectionInput>
        CreateSectionInputs(
            StructuralRoute route,
            DocumentSchema schema)
    {
        var inputs = ImmutableDictionary.CreateBuilder<
            string,
            StructuralSectionInput>(StringComparer.OrdinalIgnoreCase);
        foreach (string section in schema.SectionNames)
        {
            StructuralSectionInput input =
                route.Catalog is
                    InspectionCatalogIdentity.Library
                    or InspectionCatalogIdentity.LibraryAggregate
                    ? LibraryCommand.GetStructuralSectionInput(
                        section)
                    : StructuralSectionInput.None;
            if (route.Catalog
                is InspectionCatalogIdentity.ApiMemberOverload)
            {
                input |= StructuralSectionInput.MemberFilter;
            }
            else if (route.Catalog
                     is InspectionCatalogIdentity.ApiMemberDetail)
            {
                input |= StructuralSectionInput.ExactMember;
            }

            inputs.Add(section, input);
        }

        return inputs.ToImmutable();
    }

    private static StructuralSectionInput GetAvailableInputs(
        StructuralParserCapabilities capabilities)
    {
        StructuralSectionInput inputs =
            StructuralSectionInput.None;
        if (capabilities.HasFlag(
                StructuralParserCapabilities.TypeFilter))
        {
            inputs |= StructuralSectionInput.TypeFilter;
        }
        if (capabilities.HasFlag(
                StructuralParserCapabilities.MemberFilter))
        {
            inputs |= StructuralSectionInput.MemberFilter;
        }
        if (capabilities.HasFlag(
                StructuralParserCapabilities.Overload)
            || capabilities.HasFlag(
                StructuralParserCapabilities.Digest))
        {
            inputs |= StructuralSectionInput.ExactMember;
        }
        if (capabilities.HasFlag(
                StructuralParserCapabilities.Coordinates))
        {
            inputs |= StructuralSectionInput.IlCoordinate
                | StructuralSectionInput.HeapCoordinate;
        }
        if (capabilities.HasFlag(
                StructuralParserCapabilities.BodyKindFilter))
        {
            inputs |= StructuralSectionInput.BodyKindFilter;
        }

        return inputs;
    }

    private static DocumentSchema FilterSchema(
        DocumentSchema schema,
        IReadOnlySet<string> selected)
    {
        var filtered = new DocumentSchema();
        foreach (string name in schema.SectionNames.Where(selected.Contains))
        {
            var section = schema.GetSection(name);
            if (section is null)
            {
                filtered.AddSection(name);
                continue;
            }

            string[] items =
                [.. section.Items.Select(item => item.Name)];
            if (items.Length == 0)
                filtered.AddSection(name);
            else
                filtered.Add(name, section.ItemKind, items);
        }

        return filtered;
    }

    private static IReadOnlyDictionary<
        string,
        SectionCardinalityDeclaration>? FilterCardinalities(
        IReadOnlyDictionary<
            string,
            SectionCardinalityDeclaration>? declarations,
        IReadOnlyList<string> sectionNames)
    {
        if (declarations is null)
            return null;

        var known = new HashSet<string>(
            sectionNames,
            StringComparer.OrdinalIgnoreCase);
        return declarations
            .Where(pair => known.Contains(pair.Key))
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool ContainsOption(
        IEnumerable<string> tokens,
        string option)
        => tokens.Any(token =>
            token.Equals(option, StringComparison.Ordinal)
            || (token.Length > option.Length
                && token.StartsWith(
                    option,
                    StringComparison.Ordinal)
                && token[option.Length] is '=' or ':'));

    private static InspectionCatalogIdentity GetImpliedMemberCatalog(
        string target,
        IReadOnlyList<string> tokens)
    {
        var (_, memberName) =
            SharedParsers.SplitTrailingMember(target);
        MemberTargetSelector selector =
            MemberTargetSelector.Parse(memberName ?? "");
        bool exactMember =
            ContainsOption(tokens, "--index")
            || selector.OverloadIndex is not null
            || !string.IsNullOrWhiteSpace(
                selector.DigestPrefix);
        if (BodyKindQueryOptions.TryExtract(
                GetOptionValues(tokens, "--where"),
                out BodyKindQueryOptions bodyKindQuery,
                out _,
                out _)
            && bodyKindQuery.HasFilter)
        {
            exactMember = true;
        }

        string[] sectionSelectors =
            GetSectionOptionValues(tokens);
        if (ApiSectionDemandIndex.Classify(
                InspectionSurface.Member,
                [.. sectionSelectors],
                selectDefault: false,
                InspectionTargetRequirement.MemberSet)
            .RequiredTarget
            == InspectionTargetRequirement.ExactMember)
        {
            exactMember = true;
        }

        return exactMember
            ? InspectionCatalogIdentity.ApiMemberDetail
            : InspectionCatalogIdentity.ApiMemberOverload;
    }

    internal static bool HasUnambiguousMemberTail(
        string target)
    {
        var (typeName, memberName) =
            SharedParsers.SplitTrailingMember(target);
        if (memberName is null
            || typeName.Length == target.Length)
            return false;

        MemberTargetSelector selector =
            MemberTargetSelector.Parse(memberName);
        return selector.OverloadIndex is not null
            || !string.IsNullOrWhiteSpace(
                selector.DigestPrefix)
            || memberName.Equals(
                ".ctor",
                StringComparison.OrdinalIgnoreCase)
            || memberName.Equals(
                ".cctor",
                StringComparison.OrdinalIgnoreCase)
            || OperatorNames.IsMetadataOperatorName(
                selector.Name)
            || memberName.StartsWith(
                "explicit:",
                StringComparison.OrdinalIgnoreCase)
            || memberName.StartsWith(
                "extension:",
                StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasExplicitGenericTypeTail(
        string target)
    {
        int trailingSegmentStart =
            CSharpText.FqnParser.LastTopLevelDot(target) + 1;
        return TypeMatcher.HasExplicitGenericNotation(
            target[trailingSegmentStart..]);
    }

    internal static bool HasGenericTypeAndGenericTailAmbiguity(
        string target)
    {
        int boundary =
            CSharpText.FqnParser.LastTopLevelDot(target);
        return boundary > 0
            && HasExplicitGenericTypeTail(target)
            && HasExplicitGenericTypeTail(
                target[..boundary]);
    }

    internal static bool RequiresGenericTailMemberAlternative(
        string target,
        IReadOnlyList<string> tokens)
    {
        if (!HasExplicitGenericTypeTail(target)
            || CSharpText.FqnParser.LastTopLevelDot(target) <= 0)
        {
            return false;
        }

        ImmutableArray<string> selectors =
            GetDemandSectionOptionValues(tokens);
        return ApiSectionDemandIndex.Classify(
                InspectionSurface.Commandless,
                selectors,
                selectDefault: false,
                InspectionTargetRequirement.MemberSet)
            .RequiredTarget
            == InspectionTargetRequirement.ExactMember;
    }

    internal static bool RejectUniversallyInvalidCommandlessRequest(
        string[] tokens,
        StructuralDiscoveryRequest request,
        string? sourceIdentityTypeTarget = null)
    {
        StructuralCatalogAlternatives alternatives = CreateCommandlessAlternatives(
            tokens, request, sourceIdentityTypeTarget);
        var (normalizedRequest, aliasError) = NormalizeLibrarySelectors(
            request,
            alternatives.Alternatives
                .Where(alternative => alternative.Error is null)
                .Select(alternative => alternative.Route));
        if (aliasError is not null)
        {
            CommandError.Write(aliasError.Value);
            return true;
        }
        request = normalizedRequest;
        StructuralSchemaProjection[] projections =
        [
            .. alternatives.Alternatives
                .Select(alternative =>
                    Project(alternative.Route)),
        ];
        string[] knownSections =
        [
            .. projections
                .SelectMany(projection =>
                    projection.SelectableSectionNames)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        string[] defaultSections =
        [
            .. projections
                .SelectMany(projection =>
                    projection.DefaultSectionNames)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        Dictionary<string, string[]> categories =
            MergeCategories(projections);
        bool hasSelection =
            request.Select is { Length: > 0 }
            || request.SelectDefault;
        SelectResult selection =
            SelectResolver.ResolveSelectAsSections(
                request.Select,
                knownSections,
                defaultSections,
                categories,
                request.SelectDefault);
        if (hasSelection
            && IsTotalFailure(selection))
        {
            return SelectOutput.WriteUnresolved(selection);
        }

        if (request.Discover is not { Length: > 0 })
            return false;

        IReadOnlyList<string> discoverySections =
            hasSelection
                ? [.. selection.Sections ?? []]
                : knownSections;
        var discoverySet = new HashSet<string>(
            discoverySections,
            StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string[]> discoveryCategories =
            categories
                .Select(pair =>
                    new KeyValuePair<string, string[]>(
                        pair.Key,
                        [.. pair.Value.Where(
                            discoverySet.Contains)]))
                .Where(pair => pair.Value.Length > 0)
                .ToDictionary(
                    pair => pair.Key,
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
        SelectResult discovery =
            SelectResolver.ResolveSelectAsSections(
                request.Discover,
                discoverySections,
                infoSections: [],
                discoveryCategories,
                selectDefault: false);
        return IsTotalFailure(discovery)
            && SelectOutput.WriteUnresolved(discovery);

        static bool IsTotalFailure(SelectResult result) =>
            result.Unresolved.Count > 0
            && result.Sections is null or { Count: 0 };

        static Dictionary<string, string[]> MergeCategories(
            IEnumerable<StructuralSchemaProjection> projections)
        {
            Dictionary<string, string[]> merged =
                new(StringComparer.OrdinalIgnoreCase);
            foreach (StructuralSchemaProjection projection
                     in projections)
            {
                foreach (var (name, sections)
                         in projection.SectionCategories)
                {
                    merged[name] =
                        merged.TryGetValue(
                            name,
                            out string[]? existing)
                            ? [.. existing
                                .Concat(sections)
                                .Distinct(
                                    StringComparer.OrdinalIgnoreCase)]
                            : sections;
                }
            }

            return merged;
        }
    }

    private static InspectionCatalogIdentity
        GetCommandlessMemberCatalog(
            IReadOnlyList<string> tokens)
    {
        string[] members =
            GetOptionValues(tokens, "-m", "--member");
        var memberFilter =
            SharedParsers.ParseMemberFilter(members);
        if (memberFilter.Count == 0)
            return InspectionCatalogIdentity.ApiMember;

        bool exactMember =
            ContainsOption(tokens, "--index")
            || members.Any(member =>
            {
                MemberTargetSelector selector =
                    MemberTargetSelector.Parse(member);
                return selector.OverloadIndex is not null
                    || !string.IsNullOrWhiteSpace(
                        selector.DigestPrefix);
            });
        if (BodyKindQueryOptions.TryExtract(
                GetOptionValues(tokens, "--where"),
                out BodyKindQueryOptions bodyKindQuery,
                out _,
                out _)
            && bodyKindQuery.HasFilter)
        {
            exactMember = true;
        }

        ImmutableArray<string> sectionSelectors =
            GetDemandSectionOptionValues(tokens);
        if (ApiSectionDemandIndex.Classify(
                InspectionSurface.Member,
                sectionSelectors,
                selectDefault: false,
                InspectionTargetRequirement.MemberSet)
            .RequiredTarget
            == InspectionTargetRequirement.ExactMember)
        {
            exactMember = true;
        }

        return exactMember
            ? InspectionCatalogIdentity.ApiMemberDetail
            : InspectionCatalogIdentity.ApiMemberOverload;
    }

    private static string[] GetOptionValues(
        IReadOnlyList<string> tokens,
        params string[] options)
    {
        List<string> values = [];
        for (var i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            string? matched = options.FirstOrDefault(option =>
                token.Equals(option, StringComparison.Ordinal)
                || (token.Length > option.Length
                    && token.StartsWith(
                        option,
                        StringComparison.Ordinal)
                    && token[option.Length] is '=' or ':'));
            if (matched is null)
                continue;

            int separator =
                token.AsSpan().IndexOfAny('=', ':');
            if (separator >= 0)
            {
                values.Add(token[(separator + 1)..]);
                continue;
            }

            if (i + 1 < tokens.Count
                && (!tokens[i + 1].StartsWith(
                        "-",
                        StringComparison.Ordinal)
                    || (matched is "-t" or "--type"
                            or "-m" or "--member"
                        && int.TryParse(
                            tokens[i + 1],
                            out _))))
            {
                values.Add(tokens[++i]);
            }
        }

        return [.. values];
    }

    private static string[] GetSectionOptionValues(
        IReadOnlyList<string> tokens)
        => [
            .. GetOptionValues(
                    tokens,
                    "-D",
                    "--discover",
                    "-S",
                    "-s",
                    "--select",
                    "--section")
                .SelectMany(value =>
                    value.Split(
                        [',', ';'],
                        StringSplitOptions.TrimEntries
                        | StringSplitOptions.RemoveEmptyEntries))
                .Select(
                    ArgumentPreprocessor
                        .UnescapeAtCategoryValue),
        ];

    private static ImmutableArray<string>
        GetDemandSectionOptionValues(
            IReadOnlyList<string> tokens)
    {
        string[] select = GetSectionOptionValues(
            tokens,
            "-S",
            "-s",
            "--select",
            "--section");
        string[] discover = GetSectionOptionValues(
            tokens,
            "-D",
            "--discover");
        bool selectDefault =
            select.Length == 0
            && (ContainsOption(tokens, "-S")
                || ContainsOption(tokens, "-s")
                || ContainsOption(tokens, "--select")
                || ContainsOption(tokens, "--section"));
        return new InspectionSectionIntent(
                [.. select],
                selectDefault,
                [.. discover],
                InspectionDiscoveryMode.Structural)
            .DemandSelectors;
    }

    private static string[] GetSectionOptionValues(
        IReadOnlyList<string> tokens,
        params string[] options)
        => [
            .. GetOptionValues(tokens, options)
                .SelectMany(value =>
                    value.Split(
                        [',', ';'],
                        StringSplitOptions.TrimEntries
                        | StringSplitOptions.RemoveEmptyEntries))
                .Select(
                    ArgumentPreprocessor
                        .UnescapeAtCategoryValue),
        ];
}
