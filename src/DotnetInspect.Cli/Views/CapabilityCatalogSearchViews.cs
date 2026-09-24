using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Views;

[MarkoutSerializable(
    TitleProperty = nameof(Title),
    FieldLayout = FieldLayout.Inline)]
public sealed class CapabilityCatalogSearchView
{
    [MarkoutIgnore]
    public string Title
    {
        get;
        init => field = LibraryViewText.Contain(value);
    } = "";

    public string Query
    {
        get;
        init => field = LibraryViewText.Contain(value);
    } = "";

    public int CandidateResources { get; init; }

    public int Matches { get; init; }

    public string? Status
    {
        get;
        init => field = LibraryViewText.Contain(value);
    }

    [MarkoutSection(Name = "Capabilities")]
    public List<CapabilityCatalogSearchRow> Results { get; init; } = [];

    public static CapabilityCatalogSearchView Create(
        CapabilityCatalogSearchDocument document) =>
        new()
        {
            Title = $"Search capabilities for {document.Query}",
            Query = document.Query,
            CandidateResources = document.CandidateResourceCount,
            Matches = document.MatchCount,
            Status = document.MatchCount == 0
                ? "No installed capabilities matched."
                : document.IsTruncated
                    ? $"Showing {document.ReturnedCount} of "
                        + $"{document.MatchCount} matches."
                    : null,
            Results =
            [
                .. document.Results.Select(
                    CapabilityCatalogSearchRow.Create),
            ],
        };
}

[MarkoutSerializable]
public sealed class CapabilityCatalogSearchTableView
{
    [MarkoutSection(Headless = true)]
    public List<CapabilityCatalogSearchRow> Results { get; init; } = [];

    public static CapabilityCatalogSearchTableView Create(
        CapabilityCatalogSearchDocument document) =>
        new()
        {
            Results =
            [
                .. document.Results.Select(
                    CapabilityCatalogSearchRow.Create),
            ],
        };
}

public sealed record CapabilityCatalogSearchRow(
    int Rank,
    string Score,
    string Kind,
    string Key,
    string Name,
    string Route,
    string Explain,
    string Binding)
{
    public static CapabilityCatalogSearchRow Create(
        CapabilityCatalogSearchResult result) =>
        new(
            result.Rank,
            result.Similarity.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture),
            KindName(result.ResourceKind),
            result.CanonicalKeys.FirstOrDefault() ?? "",
            LibraryViewText.Contain(result.ResourceName),
            LibraryViewText.Contain(
                string.Join(
                    ", ",
                    result.OwningRoutes.Select(static route => route.Name))),
            LibraryViewText.Contain(result.ResourcePath),
            LibraryViewText.Contain(
                string.Join(
                    ", ",
                    result.ProductionBindings.Select(
                        static binding => binding.Gesture))));

    private static string KindName(
        ResourceExplanationResourceKind kind) =>
        kind switch
        {
            ResourceExplanationResourceKind.InspectionDocument =>
                "Document",
            ResourceExplanationResourceKind.HostNeutralRoute =>
                "Route",
            ResourceExplanationResourceKind.QuerySpace =>
                "Query space",
            ResourceExplanationResourceKind.QueryFacet =>
                "Query facet",
            ResourceExplanationResourceKind.ConsumerBinding =>
                "Binding",
            _ => kind.ToString(),
        };
}

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(CapabilityCatalogSearchView))]
[MarkoutContext(typeof(CapabilityCatalogSearchTableView))]
[MarkoutContext(typeof(CapabilityCatalogSearchRow))]
public partial class CapabilityCatalogSearchViewContext :
    MarkoutSerializerContext;
