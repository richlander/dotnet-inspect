using System.Collections.Immutable;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Planning;

public enum InspectionSurface
{
    Type,
    Member,
    Commandless,
}

public enum InspectionSourceKind
{
    Inferred,
    Package,
    Library,
    Platform,
    Project,
}

public enum InspectionCatalogIdentity
{
    Package,
    Library,
    LibraryAggregate,
    ApiType,
    ApiMember,
    ApiMemberOverload,
    ApiMemberDetail,
}

public enum InspectionTargetRequirement
{
    Type,
    MemberSet,
    ExactMember,
}

public enum InspectionDiscoveryMode
{
    None,
    Structural,
    Effective,
}

public sealed record InspectionSourceIntent(
    InspectionSourceKind Kind,
    string? Value,
    string? Framework,
    string? TargetFramework);

public sealed record InspectionAddressIntent(
    InspectionSourceIntent Source,
    string? TypeOrMember,
    string? PackageRangeAddress);

public sealed record InspectionSectionIntent(
    ImmutableArray<string> Selectors,
    bool SelectDefault,
    ImmutableArray<string> DiscoverySelectors,
    InspectionDiscoveryMode DiscoveryMode)
{
    public ImmutableArray<string> DemandSelectors =>
        !Selectors.IsEmpty || SelectDefault
            ? Selectors
            : DiscoverySelectors;
}

public sealed record InspectionProjectionIntent(
    bool Count,
    bool Print,
    bool Value,
    bool Urls,
    bool Paths,
    ImmutableArray<string> Columns,
    ImmutableArray<string> Fields);

public sealed record InspectionPresentationIntent(
    OutputFormat Format,
    Verbosity Verbosity,
    bool Tree,
    bool Bare);

public sealed record CapabilityRequestProvenance(
    Verbosity UserVerbosity,
    ImmutableArray<string> ExplicitSectionSelectors,
    InspectionDiscoveryMode DiscoveryMode);

public sealed record MemberGestureIntent(
    ImmutableArray<string> Selectors,
    int? OverloadIndex,
    string? DigestPrefix,
    int? GenericArity);

public sealed record ParsedInspectionIntent(
    InspectionSurface Surface,
    InspectionAddressIntent Address,
    MemberGestureIntent Members,
    InspectionSectionIntent Sections,
    InspectionProjectionIntent Projection,
    InspectionPresentationIntent Presentation,
    CapabilityRequestProvenance CapabilityRequest)
{
    public static ParsedInspectionIntent FromOptions(ApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var surface = options is MemberOptions
            ? InspectionSurface.Member
            : InspectionSurface.Type;
        var source = GetSourceIntent(options);
        var discoveryMode = options.Discover is null
            ? InspectionDiscoveryMode.None
            : options.Schema
                ? InspectionDiscoveryMode.Structural
                : InspectionDiscoveryMode.Effective;
        var memberOptions = options as MemberOptions;

        return new ParsedInspectionIntent(
            surface,
            new InspectionAddressIntent(
                source,
                options.TypeName,
                options.PackageRangeAddress),
            new MemberGestureIntent(
                [.. options.MemberFilter],
                memberOptions?.OverloadIndex,
                memberOptions?.MemberDigest,
                memberOptions?.MemberGenericArity),
            new InspectionSectionIntent(
                [.. options.Select ?? []],
                options.SelectDefault,
                [.. options.Discover ?? []],
                discoveryMode),
            new InspectionProjectionIntent(
                options.Count,
                options.Print,
                options.Value,
                options.Urls,
                options.Paths,
                [.. options.Columns ?? []],
                [.. options.Fields ?? []]),
            new InspectionPresentationIntent(
                options.Format,
                options.UserVerbosity,
                options.Tree,
                options.Bare),
            new CapabilityRequestProvenance(
                options.UserVerbosity,
                [.. options.Select ?? []],
                discoveryMode));
    }

    private static InspectionSourceIntent GetSourceIntent(ApiOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.PackagePath))
        {
            return new InspectionSourceIntent(
                InspectionSourceKind.Package,
                options.PackagePath,
                options.PlatformFramework,
                options.Tfm);
        }

        if (!string.IsNullOrWhiteSpace(options.AssemblyPath))
        {
            return new InspectionSourceIntent(
                InspectionSourceKind.Library,
                options.AssemblyPath,
                options.PlatformFramework,
                options.Tfm);
        }

        if (!string.IsNullOrWhiteSpace(options.PlatformAssembly))
        {
            return new InspectionSourceIntent(
                InspectionSourceKind.Platform,
                options.PlatformAssembly,
                options.PlatformFramework,
                options.Tfm);
        }

        if (!string.IsNullOrWhiteSpace(options.ProjectPath))
        {
            return new InspectionSourceIntent(
                InspectionSourceKind.Project,
                options.ProjectPath,
                options.PlatformFramework,
                options.Tfm);
        }

        return new InspectionSourceIntent(
            InspectionSourceKind.Inferred,
            null,
            options.PlatformFramework,
            options.Tfm);
    }
}

public sealed record SectionSelectorDiagnostic(
    string Selector,
    ImmutableArray<string> Suggestions,
    bool IsGlob = false,
    bool ListsAllSections = false);

public sealed record StructuralSelection(
    InspectionCatalogIdentity Catalog,
    int CatalogVersion,
    ImmutableArray<string> ResolvedSections,
    ImmutableArray<string> ExactSections,
    ImmutableArray<SectionSelectorDiagnostic> UnresolvedSelectors,
    bool CompleteCatalog,
    InspectionTargetRequirement RequiredTarget)
{
    public SelectResult ToSelectResult() =>
        new(
            ResolvedSections.IsEmpty
                ? null
                : new HashSet<string>(
                    ResolvedSections,
                    StringComparer.OrdinalIgnoreCase),
            [.. UnresolvedSelectors.Select(diagnostic =>
                new SelectMiss(
                    diagnostic.Selector,
                    diagnostic.Suggestions,
                    diagnostic.IsGlob,
                    diagnostic.ListsAllSections))])
        {
            ExactSections = new HashSet<string>(
                ExactSections,
                StringComparer.OrdinalIgnoreCase),
        };
}

public sealed record ResolvedMemberInspectionPlan(
    ParsedInspectionIntent Intent,
    StructuralSelection Selection)
{
    public static ResolvedMemberInspectionPlan FromCompatibilityOptions(
        ApiOptions options,
        bool selectCatalogFromDemand = false)
    {
        ParsedInspectionIntent intent = ParsedInspectionIntent.FromOptions(options);
        ImmutableArray<string> demandSelectors =
            intent.Sections.DemandSelectors;
        InspectionTargetRequirement baseRequirement =
            intent.Surface == InspectionSurface.Member
            && intent.Members.Selectors.Length > 0
                ? InspectionTargetRequirement.MemberSet
                : InspectionTargetRequirement.Type;
        SectionDemandClassification demand = ApiSectionDemandIndex.Classify(
            intent.Surface,
            demandSelectors,
            intent.Sections.SelectDefault,
            baseRequirement);
        InspectionCatalogIdentity catalog = SelectCatalog(
            intent,
            selectCatalogFromDemand
                ? demand.RequiredTarget
                : baseRequirement);
        ApiInspectionCatalog descriptor = ApiInspectionCatalogRegistry.Get(catalog);
        IReadOnlyList<string> defaultSections =
            intent.Surface == InspectionSurface.Type
            && catalog == InspectionCatalogIdentity.ApiMember
                ? ApiMemberSectionDescriptors
                    .CreatePipeline()
                    .FixedOverviewSectionNames
                : descriptor.DefaultSectionNames;
        SelectResult resolved = SelectResolver.ResolveSelectAsSections(
            [.. demandSelectors],
            descriptor.SectionNames,
            defaultSections,
            descriptor.Categories,
            intent.Sections.SelectDefault);

        return new ResolvedMemberInspectionPlan(
            intent,
            new StructuralSelection(
                catalog,
                descriptor.Version,
                [.. resolved.Sections ?? []],
                [.. resolved.ExactSections],
                [.. resolved.Unresolved.Select(miss =>
                    new SectionSelectorDiagnostic(
                        miss.Value,
                        [.. miss.Suggestions],
                        miss.IsGlob,
                        miss.ListsAllSections))],
                SelectResolver.IsAllSelector([.. demandSelectors]),
                demand.RequiredTarget));
    }

    private static InspectionCatalogIdentity SelectCatalog(
        ParsedInspectionIntent intent,
        InspectionTargetRequirement targetRequirement)
    {
        if (intent.Surface == InspectionSurface.Type)
        {
            return string.IsNullOrWhiteSpace(intent.Address.TypeOrMember)
                || TypeMatcher.IsTypeGlobPattern(intent.Address.TypeOrMember)
                    ? InspectionCatalogIdentity.ApiType
                    : InspectionCatalogIdentity.ApiMember;
        }

        if (intent.Members.OverloadIndex is not null
            || !string.IsNullOrWhiteSpace(intent.Members.DigestPrefix)
            || targetRequirement == InspectionTargetRequirement.ExactMember)
        {
            return InspectionCatalogIdentity.ApiMemberDetail;
        }

        return intent.Members.Selectors.Length > 0
            ? InspectionCatalogIdentity.ApiMemberOverload
            : InspectionCatalogIdentity.ApiMember;
    }
}

public sealed record ApiInspectionCatalog(
    InspectionCatalogIdentity Identity,
    int Version,
    IReadOnlyList<string> SectionNames,
    IReadOnlyList<string> DefaultSectionNames,
    IReadOnlyDictionary<string, string[]> Categories);

public static class ApiInspectionCatalogRegistry
{
    private const int CatalogVersion = 1;

    private static readonly ImmutableDictionary<
        InspectionCatalogIdentity,
        ApiInspectionCatalog> Catalogs = CreateCatalogs();

    public static IReadOnlyCollection<ApiInspectionCatalog> All =>
        Catalogs.Values.ToArray();

    public static ApiInspectionCatalog Get(InspectionCatalogIdentity identity)
        => Catalogs.TryGetValue(identity, out ApiInspectionCatalog? catalog)
            ? catalog
            : throw new ArgumentOutOfRangeException(
                nameof(identity),
                identity,
                "The requested identity is not an API inspection catalog.");

    public static SectionPipeline<ApiType> CreateMemberPipeline(
        InspectionCatalogIdentity identity)
        => identity switch
        {
            InspectionCatalogIdentity.ApiMember =>
                ApiMemberSectionDescriptors.CreatePipeline(),
            InspectionCatalogIdentity.ApiMemberOverload =>
                ApiMemberOverloadSectionDescriptors.CreatePipeline(),
            InspectionCatalogIdentity.ApiMemberDetail =>
                ApiMemberDetailSectionDescriptors.CreatePipeline(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(identity),
                identity,
                "The requested identity is not a member inspection catalog."),
        };

    private static ImmutableDictionary<
        InspectionCatalogIdentity,
        ApiInspectionCatalog> CreateCatalogs()
    {
        var type = ApiTypeSectionDescriptors.CreatePipeline();
        var member = ApiMemberSectionDescriptors.CreatePipeline();
        var overload = ApiMemberOverloadSectionDescriptors.CreatePipeline();
        var detail = ApiMemberDetailSectionDescriptors.CreatePipeline();

        return new[]
            {
                Create(
                    InspectionCatalogIdentity.ApiType,
                    type.SelectableSectionNames,
                    type.FixedOverviewSectionNames,
                    type.GetCategoryMap()),
                Create(
                    InspectionCatalogIdentity.ApiMember,
                    member.SelectableSectionNames,
                    member.InfoSectionNames,
                    member.GetCategoryMap()),
                Create(
                    InspectionCatalogIdentity.ApiMemberOverload,
                    overload.SelectableSectionNames,
                    overload.InfoSectionNames,
                    overload.GetCategoryMap()),
                Create(
                    InspectionCatalogIdentity.ApiMemberDetail,
                    detail.SelectableSectionNames,
                    detail.FixedOverviewSectionNames,
                    detail.GetCategoryMap()),
            }
            .ToImmutableDictionary(catalog => catalog.Identity);
    }

    private static ApiInspectionCatalog Create(
        InspectionCatalogIdentity identity,
        IReadOnlyList<string> sections,
        IReadOnlyList<string> defaults,
        IReadOnlyDictionary<string, string[]> categories)
        => new(
            identity,
            CatalogVersion,
            sections.ToArray(),
            defaults.ToArray(),
            categories.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToArray(),
                StringComparer.OrdinalIgnoreCase));
}

public sealed record SectionDemandClassification(
    InspectionTargetRequirement RequiredTarget,
    ImmutableArray<string> MatchedSections,
    ImmutableArray<SectionSelectorDiagnostic> UnresolvedSelectors);

public static class ApiSectionDemandIndex
{
    private static readonly ImmutableDictionary<
        string,
        InspectionTargetRequirement> MemberRequirements =
        CreateMemberRequirements();

    private static readonly string[] KnownSectionNames =
        [.. MemberRequirements.Keys.Order(StringComparer.OrdinalIgnoreCase)];

    private static readonly IReadOnlyDictionary<string, string[]> Categories =
        CreateCategories();

    public static IReadOnlyDictionary<
        string,
        InspectionTargetRequirement> Declarations => MemberRequirements;

    public static SectionDemandClassification Classify(
        InspectionSurface surface,
        ImmutableArray<string> selectors,
        bool selectDefault,
        InspectionTargetRequirement baseRequirement)
    {
        if (selectors.Length == 0
            || selectDefault
            || SelectResolver.IsAllSelector([.. selectors]))
        {
            return new SectionDemandClassification(
                baseRequirement,
                [],
                []);
        }

        SelectResult result = SelectResolver.ResolveSelectAsSections(
            [.. selectors],
            KnownSectionNames,
            infoSections: null,
            Categories);
        InspectionTargetRequirement requirement = baseRequirement;
        if (surface != InspectionSurface.Type)
        {
            foreach (string section in result.Sections ?? [])
            {
                if (MemberRequirements.TryGetValue(
                        section,
                        out InspectionTargetRequirement declared)
                    && declared > requirement)
                {
                    requirement = declared;
                }
            }
        }

        return new SectionDemandClassification(
            requirement,
            [.. result.Sections ?? []],
            [.. result.Unresolved.Select(miss =>
                new SectionSelectorDiagnostic(
                    miss.Value,
                    [.. miss.Suggestions],
                    miss.IsGlob,
                    miss.ListsAllSections))]);
    }

    private static ImmutableDictionary<
        string,
        InspectionTargetRequirement> CreateMemberRequirements()
    {
        var declarations =
            ImmutableDictionary.CreateBuilder<
                string,
                InspectionTargetRequirement>(
                    StringComparer.OrdinalIgnoreCase);
        Declare(
            declarations,
            InspectionTargetRequirement.Type,
            SectionNames.TypeInfo,
            "Values",
            "Type Parameters",
            "Interfaces",
            "Baseclass");
        Declare(
            declarations,
            InspectionTargetRequirement.MemberSet,
            "Constructors",
            SectionNames.Finalizer,
            "Fields",
            "Properties",
            SectionNames.MethodGroups,
            SectionNames.Methods,
            SectionNames.MemberIndex,
            SectionNames.Operators,
            SectionNames.ExplicitInterfaceImplementations,
            SectionNames.ExtensionMethods,
            "Events",
            SectionNames.UnsafeMembers,
            SectionNames.CalledTypes,
            SectionNames.AllocationFacts,
            SectionNames.SafetyFacts,
            SectionNames.CostFacts,
            SectionNames.TopLeverage,
            SectionNames.SourceFiles,
            SectionNames.SourceLocations,
            SectionNames.PerformanceTriage);
        Declare(
            declarations,
            InspectionTargetRequirement.ExactMember,
            SectionNames.Signature,
            SectionNames.CustomAttributes,
            SectionNames.DecompiledSource,
            SectionNames.FidelityCauses,
            SectionNames.AppliedTaste,
            SectionNames.AnnotatedSource,
            SectionNames.AnnotatedSourceDocument,
            SectionNames.CostOverlay,
            SectionNames.SemanticsOverlay,
            SectionNames.PdbSource,
            SectionNames.SourceDiff,
            SectionNames.Calls,
            SectionNames.ExceptionRegions,
            SectionNames.Callers,
            SectionNames.CallGraph,
            SectionNames.UnsafeOperations,
            SectionNames.BodyShapes,
            SectionNames.Facts,
            SectionNames.IL);

        string[] catalogSections =
        [
            .. ApiInspectionCatalogRegistry.All
                .Where(catalog =>
                    catalog.Identity != InspectionCatalogIdentity.ApiType)
                .SelectMany(catalog => catalog.SectionNames)
                .Distinct(StringComparer.OrdinalIgnoreCase),
        ];
        string[] missing =
        [
            .. catalogSections.Where(section =>
                !declarations.ContainsKey(section)),
        ];
        string[] stale =
        [
            .. declarations.Keys.Where(section =>
                !catalogSections.Contains(
                    section,
                    StringComparer.OrdinalIgnoreCase)),
        ];
        if (missing.Length > 0 || stale.Length > 0)
        {
            throw new InvalidOperationException(
                "Member section target-requirement declarations "
                + "do not match the registered catalogs. "
                + $"Missing: {string.Join(", ", missing)}. "
                + $"Stale: {string.Join(", ", stale)}.");
        }

        return declarations.ToImmutable();
    }

    internal static ImmutableDictionary<
        string,
        InspectionTargetRequirement> CreateRequirementsForTest(
        params (string Section, InspectionTargetRequirement Requirement)[]
            declarations)
    {
        var result =
            ImmutableDictionary.CreateBuilder<
                string,
                InspectionTargetRequirement>(
                    StringComparer.OrdinalIgnoreCase);
        foreach (var (section, requirement) in declarations)
            Declare(result, requirement, section);
        return result.ToImmutable();
    }

    private static void Declare(
        ImmutableDictionary<
            string,
            InspectionTargetRequirement>.Builder declarations,
        InspectionTargetRequirement requirement,
        params string[] sections)
    {
        foreach (string section in sections)
        {
            if (declarations.TryGetValue(
                    section,
                    out InspectionTargetRequirement existing)
                && existing != requirement)
            {
                throw new InvalidOperationException(
                    $"Section '{section}' declares both "
                    + $"'{existing}' and '{requirement}' target requirements.");
            }

            declarations[section] = requirement;
        }
    }

    private static IReadOnlyDictionary<string, string[]> CreateCategories()
    {
        Dictionary<string, HashSet<string>> categories =
            new(StringComparer.OrdinalIgnoreCase);
        foreach (ApiInspectionCatalog catalog in
                 ApiInspectionCatalogRegistry.All.Where(catalog =>
                     catalog.Identity != InspectionCatalogIdentity.ApiType))
        {
            foreach (var (category, sections) in catalog.Categories)
            {
                if (!categories.TryGetValue(category, out var merged))
                {
                    merged = new HashSet<string>(
                        StringComparer.OrdinalIgnoreCase);
                    categories.Add(category, merged);
                }

                merged.UnionWith(sections);
            }
        }

        categories[SelectResolver.AllSelector] =
            new HashSet<string>(
                MemberRequirements.Keys,
                StringComparer.OrdinalIgnoreCase);
        return categories.ToDictionary(
            pair => pair.Key,
            pair => pair.Value
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }
}
