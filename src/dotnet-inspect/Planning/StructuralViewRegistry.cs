using System.Collections.Immutable;
using DotnetInspector.CommandLine;
using DotnetInspector.Commands;
using DotnetInspector.Core;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Markout;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace DotnetInspector.Planning;

public enum StructuralViewIdentity
{
    Package,
    PackageSingleLibrary,
    PackageAllLibraries,
    DirectLibrary,
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
    ImmutableArray<SectionSelectorDiagnostic> UnresolvedSelectors);

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
    ImmutableDictionary<string, StructuralSectionInput> SectionInputs);

public sealed record StructuralDiscoveryRequest(
    string[]? Discover,
    string[]? Select,
    bool SelectDefault,
    bool Tree,
    bool Json,
    bool Tsv,
    bool Jsonl,
    bool Markdown,
    bool PlainText,
    Verbosity Verbosity,
    IReadOnlySet<string>? IncludeSections,
    bool Schema,
    IProjectionOptions Projection)
{
    public static StructuralDiscoveryRequest From(
        InspectionOptions options)
        => new(
            options.Discover,
            options.Select,
            options.SelectDefault,
            options.Tree,
            options.JsonOutput,
            options.Tsv,
            options.Jsonl,
            !options.Tabular && !options.JsonOutput,
            options.Format == OutputFormat.PlainText,
            options.Verbosity,
            options.IncludeSections,
            options.Schema,
            options);

    public static StructuralDiscoveryRequest From(ApiOptions options)
        => new(
            options.Discover,
            options.Select,
            options.SelectDefault,
            options.Tree,
            options.JsonOutput,
            options.Tsv,
            options.Jsonl,
            !options.Tabular && !options.JsonOutput,
            options.PlainText,
            options.Verbosity,
            null,
            options.Schema,
            options);

    public static StructuralDiscoveryRequest From(
        LibraryOptions options)
        => new(
            options.Discover,
            options.Select,
            options.SelectDefault,
            options.Tree,
            options.JsonOutput,
            options.Tsv,
            options.Jsonl,
            options.Markdown
            || (!options.Tabular
                && !options.JsonOutput
                && !options.PlainText),
            options.PlainText,
            options.Verbosity,
            options.IncludeSections,
            options.Schema,
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
            format == OutputFormat.Json,
            format == OutputFormat.Tsv,
            format == OutputFormat.Jsonl,
            format == OutputFormat.Markdown,
            format == OutputFormat.PlainText,
            options.ParseVerbosity(parseResult),
            null,
            options.ParseSchema(parseResult),
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
                | StructuralParserCapabilities.Coordinates
                | StructuralParserCapabilities.BodyKindFilter),
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
        out CommandlessStructuralRoute? classification)
    {
        classification = null;
        if (tokens.Length == 0)
            return false;

        if (CommandLineHelpers.IsBooleanOptionEnabled(
                tokens,
                "--all-libraries"))
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
                StructuralViewIdentity view =
                    ContainsOption(tokens, "--library")
                        ? StructuralViewIdentity.PackageSingleLibrary
                        : StructuralViewIdentity.Package;
                InspectionCatalogIdentity catalog =
                    view == StructuralViewIdentity.Package
                        ? InspectionCatalogIdentity.Package
                        : InspectionCatalogIdentity.Library;
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
        bool hasExplicitApiSource =
            ContainsOption(tokens, "--package")
            || ContainsOption(tokens, "--platform")
            || ContainsOption(tokens, "--project")
            || ContainsOption(tokens, "--library");
        string? typeOptionValue =
            GetOptionValues(tokens, "-t", "--type")
                .LastOrDefault();
        var (typeOptionFilter, _) =
            SharedParsers.ParseTypeFilter(typeOptionValue);
        if (hasTypeOption
            && (hasExplicitApiSource
                || !string.IsNullOrWhiteSpace(
                    typeOptionFilter)))
        {
            classification = new CommandlessStructuralRoute(
                Route(
                    StructuralViewIdentity.Type,
                    InspectionCatalogIdentity.ApiType),
                [TypeCommand.Name, .. tokens]);
            return true;
        }

        if (hasMemberOption)
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
            classification = new CommandlessStructuralRoute(
                Route(
                    StructuralViewIdentity.MemberTarget,
                    catalog),
                [MemberCommand.Name, .. tokens]);
            return true;
        }

        if (HasExplicitGenericTypeTail(target)
            && !HasGenericTypeAndGenericTailAmbiguity(target))
        {
            classification = new CommandlessStructuralRoute(
                Route(
                    StructuralViewIdentity.Type,
                    InspectionCatalogIdentity.ApiMember),
                [TypeCommand.Name, .. tokens]);
            return true;
        }

        if (ContainsOption(tokens, "--version")
            || ContainsOption(tokens, "--latest-version")
            || ContainsOption(tokens, "--versions")
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
            StructuralDiscoveryRequest request)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        if (tokens.Length == 0)
            throw new ArgumentException(
                "A commandless structural query requires a target token.",
                nameof(tokens));

        string target = tokens[0];
        string[] memberSelectors =
            GetOptionValues(tokens, "-m", "--member");
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
        string? libraryValue =
            GetOptionValues(tokens, "--library")
                .FirstOrDefault();
        bool hasExplicitLibraryPath =
            libraryValue is not null
            && CommandLineHelpers
                .IsExplicitLibraryPath(libraryValue);
        hasExplicitApiSource |= hasExplicitLibraryPath;
        bool hasLibraryGesture =
            ContainsOption(tokens, "--library")
            && !hasExplicitLibraryPath;
        bool hasTypeMarker =
            ContainsOption(tokens, "-t")
            || ContainsOption(tokens, "--type");
        string? typeMarkerValue =
            GetOptionValues(tokens, "-t", "--type")
                .LastOrDefault();
        var (typeFilter, _) =
            SharedParsers.ParseTypeFilter(typeMarkerValue);
        bool hasTypeFilter =
            !string.IsNullOrWhiteSpace(typeFilter);
        var routes = new List<StructuralRoute>();
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

        if (memberSelectors.Length == 0)
        {
            routes.Add(
                Route(
                    StructuralViewIdentity.Type,
                    InspectionCatalogIdentity.ApiType));
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
            && HasGenericTypeAndGenericTailAmbiguity(target))
        {
            impliedMemberName =
                target[
                    (CSharpText.FqnParser.LastTopLevelDot(
                        target) + 1)..];
        }
        bool tailCanBeMember =
            impliedMemberName is not null
            && !TypeMatcher.IsTypeGlobPattern(target);
        if (memberSelectors.Length > 0
            || (!hasTypeMarker && tailCanBeMember))
        {
            string impliedMember = memberSelectors.FirstOrDefault()
                ?? impliedMemberName!;
            MemberTargetSelector selector =
                MemberTargetSelector.Parse(impliedMember);
            bool hasSelection =
                request.Select is { Length: > 0 }
                || request.SelectDefault;
            string[] sectionSelectors =
                hasSelection
                    ? request.Select ?? []
                    : request.Discover ?? [];
            SectionDemandClassification demand =
                ApiSectionDemandIndex.Classify(
                    InspectionSurface.Commandless,
                    [.. sectionSelectors],
                    hasSelection
                    && request.SelectDefault,
                    InspectionTargetRequirement.MemberSet);
            InspectionCatalogIdentity memberCatalog =
                selector.OverloadIndex is not null
                || !string.IsNullOrWhiteSpace(
                    selector.DigestPrefix)
                || hasBodyKindFilter
                || demand.RequiredTarget
                    == InspectionTargetRequirement.ExactMember
                    ? InspectionCatalogIdentity.ApiMemberDetail
                    : InspectionCatalogIdentity.ApiMemberOverload;
            routes.Add(
                Route(
                    StructuralViewIdentity.MemberTarget,
                    memberCatalog));
        }

        return CreateAlternatives(routes, request);
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
            inputs);
    }

    public static int Execute(
        StructuralRoute route,
        StructuralDiscoveryRequest request,
        StructuralOutputShape outputShape =
            StructuralOutputShape.Document)
    {
        StructuralSchemaProjection projection = Project(route, outputShape);
        DocumentSchema schema = projection.Schema;
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
                && !hasExplicitSelection);
            if (SelectOutput.WriteUnresolved(result))
                return 1;
            if (result.Sections is { Count: > 0 })
                selectedSections.UnionWith(result.Sections);
        }
        if (selectedSections.Count > 0)
            schema = FilterSchema(schema, selectedSections);

        return DiscoverOutput.Execute(
            request.Discover,
            schema,
            tree: request.Tree,
            json: request.Json,
            tsv: request.Tsv,
            jsonl: request.Jsonl,
            markdown: request.Markdown,
            plainText: request.PlainText,
            verbosity: (int)request.Verbosity,
            sectionCostAnnotations:
                projection.SectionCostAnnotations,
            sectionCategories: projection.SectionCategories,
            catalogHiddenSections: request.Schema
                ? null
                : projection.CatalogHiddenSections,
            listedCategoryDoors:
                projection.ListedCategoryDoors,
            projection: request.Projection);
    }

    public static int Execute(
        StructuralCatalogAlternatives alternatives,
        StructuralDiscoveryRequest request)
    {
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

        var schema = new DocumentSchema();
        var annotations =
            new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
        var categories =
            new Dictionary<string, string[]>(
                StringComparer.OrdinalIgnoreCase);
        foreach (StructuralAlternativeSelection alternative in
                 alternatives.Alternatives)
        {
            StructuralSchemaProjection projection =
                Project(alternative.Route);
            IReadOnlyCollection<string> sections =
                alternative.CompleteCatalog
                    ? projection.Schema.SectionNames
                    : alternative.ResolvedSections;
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
            tree: request.Tree,
            json: request.Json,
            tsv: request.Tsv,
            jsonl: request.Jsonl,
            markdown: request.Markdown,
            plainText: request.PlainText,
            verbosity: (int)request.Verbosity,
            sectionCostAnnotations: annotations,
            sectionCategories: categories,
            projection: request.Projection);
    }

    public static StructuralCatalogAlternatives CreateAlternatives(
        IEnumerable<StructuralRoute> routes,
        StructuralDiscoveryRequest request)
    {
        var alternatives =
            ImmutableArray.CreateBuilder<StructuralAlternativeSelection>();
        foreach (StructuralRoute route in routes)
        {
            StructuralSchemaProjection projection = Project(route);
            bool hasSelection =
                request.Select is { Length: > 0 }
                || request.SelectDefault;
            SelectResult selection =
                SelectResolver.ResolveSelectAsSections(
                    request.Select,
                    projection.SelectableSectionNames,
                    projection.DefaultSectionNames,
                    projection.SectionCategories,
                    request.SelectDefault);
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
                request.Discover is { Length: > 0 }
                    ? SelectResolver.ResolveSelectAsSections(
                        request.Discover,
                        discoverySections,
                        infoSections: [],
                        discoveryCategories,
                        selectDefault: false)
                    : selection;
            bool completeCatalog =
                !hasSelection
                && request.Discover is not { Length: > 0 };
            ImmutableArray<SectionSelectorDiagnostic> unresolved =
            [
                .. selection.Unresolved.Select(ToDiagnostic),
                .. request.Discover is { Length: > 0 }
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
        [
            .. GetOptionValues(
                tokens,
                "-D",
                "--discover",
                "-S",
                "--select"),
        ];
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
            || memberName is ".ctor" or ".cctor"
            || selector.Name.StartsWith(
                "op_",
                StringComparison.Ordinal)
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

    private static InspectionCatalogIdentity
        GetCommandlessMemberCatalog(
            IReadOnlyList<string> tokens)
    {
        string[] members =
            GetOptionValues(tokens, "-m", "--member");
        var (memberFilter, _) =
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

        string[] sectionSelectors =
        [
            .. GetOptionValues(
                tokens,
                "-D",
                "--discover",
                "-S",
                "--select"),
        ];
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
                && !tokens[i + 1].StartsWith(
                    "-",
                    StringComparison.Ordinal))
            {
                values.Add(tokens[++i]);
            }
        }

        return [.. values];
    }
}
