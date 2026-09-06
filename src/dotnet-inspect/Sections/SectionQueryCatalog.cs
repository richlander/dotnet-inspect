using System.Collections.Immutable;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Planning;

namespace DotnetInspector.Sections;

public sealed record SectionQueryDescriptor(
    string Section,
    string Summary,
    ImmutableArray<SectionQueryFacet> Facets)
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
            "find" => [],
            _ => throw new ArgumentOutOfRangeException(nameof(command)),
        };
        List<SectionQueryDescriptor> queries = [];
        if (command == "find")
        {
            queries.Add(new(
                PackageProfileSections.Packages,
                PackageQueryOptions.DiscoverySummary,
                [PackageQueryOptions.QueryFacet]));
        }
        if (command is "library" or "type" or "member")
        {
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
                    PerformanceTriageOptions.QueryFacets));
            }
            queries.Add(new(
                SectionNames.BodyShapes,
                "Exactly one Kind=... predicate is required. Values are C# Body Kinds vocabulary IDs. "
                + (command == "library"
                    ? "Other predicates narrow candidate methods before body-shape matching; ordering and --top are not supported."
                    : "Other predicates, ordering, and --top cannot be combined with Kind."),
                command == "library"
                    ? [BodyKindQueryOptions.QueryFacet,
                        .. PerformanceTriageOptions.QueryFacets
                            .Where(facet => facet.Operators.Contains("--where"))
                            .Select(facet => facet with { Operators = ["--where"] })]
                    : [BodyKindQueryOptions.QueryFacet]));
        }
        if (command == "library")
        {
            string[] integrationSections =
            [
                LibraryIntegrationCatalog.RollupName,
                .. LibraryIntegrationCatalog.CategorySections,
                IntegrationSectionNames.Opportunities,
            ];
            foreach (string section in integrationSections)
            {
                queries.Add(new(
                    section,
                    "All integrations are enabled by default. An ecosystem equality predicate narrows "
                    + "ordinary Integration evidence and opportunities; it does not replace full-library presence or Census. "
                    + "This query cannot be combined with Body Shapes or Performance Triage predicates/ranking.",
                    [IntegrationQueryOptions.QueryFacet]));
            }
        }

        ImmutableArray<string> sections = command == "find"
            ? ["Packages", "Results", "Members"]
            : [.. projections.SelectMany(projection => projection.Schema.SectionNames)
                .Concat(queries.Select(query => query.Section))
                .Distinct(StringComparer.OrdinalIgnoreCase)];
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
