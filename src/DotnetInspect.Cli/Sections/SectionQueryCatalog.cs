using System.Collections.Immutable;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Planning;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

public sealed record SectionQueryDescriptor(
    string Section,
    string Summary,
    ImmutableArray<SectionQueryKey> Keys)
{
    public string QuerySection => $"Query: {Section}";
}

/// <summary>
/// Static CLI bindings over the existing structural section catalogs. A producer's
/// descriptor alone does not advertise a capability its CLI has not adopted.
/// </summary>
public sealed record SectionQueryCatalog(
    ImmutableArray<string> KnownSections,
    IReadOnlyDictionary<string, string[]> Categories,
    ImmutableArray<SectionQueryDescriptor> Queries)
{
    public static SectionQueryCatalog Create(string command)
    {
        StructuralSchemaProjection[] projections = command switch
        {
            "library" => [Project(StructuralViewIdentity.DirectLibrary, InspectionCatalogIdentity.Library)],
            "library query" => [],
            "type" =>
            [
                Project(StructuralViewIdentity.Type, InspectionCatalogIdentity.ApiType),
                Project(StructuralViewIdentity.Type, InspectionCatalogIdentity.ApiMember),
            ],
            "member" =>
            [
                Project(StructuralViewIdentity.MemberType, InspectionCatalogIdentity.ApiMember),
                Project(StructuralViewIdentity.MemberTarget, InspectionCatalogIdentity.ApiMemberOverload),
                Project(StructuralViewIdentity.MemberTarget, InspectionCatalogIdentity.ApiMemberDetail),
            ],
            "package" => [Project(StructuralViewIdentity.Package, InspectionCatalogIdentity.Package)],
            "package query" => [],
            "find" => [],
            "depends" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        List<SectionQueryDescriptor> queries = [];
        if (command == "package query")
        {
            queries.Add(new(
                PackageProfileSections.Packages,
                PackageQueryOptions.DiscoverySummary,
                PackageQueryOptions.QueryKeys));
        }
        if (command == "library query")
        {
            queries.Add(new(
                LibraryQuerySections.LibrariesName,
                LibraryQueryOptions.DiscoverySummary,
                LibraryQueryOptions.QueryKeys));
        }
        if (command == "package")
        {
            queries.Add(new(
                PackageSections.DependencyHierarchy,
                DependencyQueryOptions.HierarchySummary,
                DependencyQueryOptions.QueryKeys(
                    DependencyQueryRouteKind.PackageHierarchy)));
        }
        if (command == "depends")
        {
            queries.Add(new(
                DependsTypeSections.DependencyGraph,
                DependencyQueryOptions.TypeSummary,
                DependencyQueryOptions.QueryKeys(
                    DependencyQueryRouteKind.TypeRelationships)));
            queries.Add(new(
                DependsAssetSections.DependencyHierarchy,
                DependencyQueryOptions.HierarchySummary,
                DependencyQueryOptions.QueryKeys(
                    DependencyQueryRouteKind.AssetHierarchy)));
        }
        if (command is "library" or "type" or "member")
        {
            queries.Add(new(
                SectionNames.CloneCandidates,
                "Choose the Workspace participant breadth and candidate-method admission independently. "
                + "The default is Breadth=Everything and Discovery=SimilarNames.",
                [.. CloneCandidateQueryOptions.QueryKeys]));
            string[] performanceSections = command == "library"
                ? PerformanceKinds.Sections
                : [SectionNames.PerformanceTriage];
            foreach (string section in performanceSections)
            {
                queries.Add(new(
                    section,
                    "Filter with --where; order with --order-by; keep ranked rows with --top N. "
                    + "The default is Triage desc (allocation-fanout has its own default). "
                    + "Triage is a composite order and must be used alone. "
                    + (command == "library"
                        ? "Ranking and --top apply before rows are divided into performance kinds."
                        : "Execution requires a selected type or member."),
                    PerformanceTriageRowQuery.QueryKeys));
            }
            foreach (string section in BodyKindQueryOptions.Sections)
            {
                queries.Add(new(
                    section,
                    "Exactly one Kind=... predicate is required. Values are C# Body Kinds vocabulary IDs. "
                    + (section == SectionNames.BodyShapeSummary
                        ? "Rows group exact rendered matches; Count measures occurrences before row windows, and --count counts groups. "
                        : "")
                    + (command == "library"
                        ? "Other predicates narrow candidate methods before body-shape matching; ordering and --top are not supported."
                        : "Other predicates, ordering, and --top cannot be combined with Kind."),
                    command == "library"
                        ? [BodyKindQueryOptions.QueryKey,
                            .. PerformanceTriageRowQuery.QueryKeys
                                .Where(key => key.Operators.Contains("--where"))
                                .Select(key => key with { Operators = ["--where"] })]
                        : [BodyKindQueryOptions.QueryKey]));
            }
        }
        if (command == "library")
        {
            foreach (string section in new[]
            {
                IntegrationSectionNames.Integrations,
                IntegrationSectionNames.Opportunities,
            })
            {
                queries.Add(new(
                    section,
                    "All integrations are enabled by default. Integration and ecosystem equality predicates narrow "
                    + "ordinary Integration evidence and opportunities; they do not replace full-library presence or Census. "
                    + "This query cannot be combined with Body Shapes or Performance Triage predicates/ranking.",
                    IntegrationQueryOptions.QueryKeys));
            }
        }

        ImmutableArray<string> sections = command switch
        {
            "find" =>
            [
                FindQueryOptions.Section(
                    FindQueryRouteKind.TypeResults),
                FindQueryOptions.Section(
                    FindQueryRouteKind.MemberResults),
            ],
            "package query" => [PackageProfileSections.Packages],
            "library query" => [LibraryQuerySections.LibrariesName],
            "depends" =>
            [
                DependsTypeSections.DependencyGraph,
                DependsAssetSections.DependencyHierarchy,
            ],
            _ => [.. projections.SelectMany(projection => projection.Schema.SectionNames)
                .Concat(queries.Select(query => query.Section))
                .Distinct(StringComparer.OrdinalIgnoreCase)],
        };
        var categories = projections
            .SelectMany(projection => projection.SectionCategories)
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.SelectMany(pair => pair.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        return new(sections, categories, [.. queries]);
    }

    private static StructuralSchemaProjection Project(
        StructuralViewIdentity view,
        InspectionCatalogIdentity catalog)
        => StructuralViewRegistry.Project(StructuralViewRegistry.Route(view, catalog));
}
