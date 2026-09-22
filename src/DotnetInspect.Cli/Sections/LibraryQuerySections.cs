using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Sections;

public static class LibraryQuerySections
{
    public const string LibrariesName = "Libraries";
    public const string QuerySummaryName = "Query Summary";

    public static string[] BareSelectSectionNames { get; } =
    [
        LibrariesName,
    ];

    public static SectionCatalog<LibraryQueryView> Catalog { get; } =
        new SectionPipeline<LibraryQueryView>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<LibraryRows>()
            .Add<QuerySummary>()
            .AddBaseCategory(
                SectionCategoryNames.Query,
                LibrariesName,
                QuerySummaryName)
            .Compile();

    public static DocumentSchema CreateSchema() =>
        SearchViewContext.Default.GetSchemaInfo<LibraryQueryView>()!
            .ToDocumentSchema();

    public static LibraryQueryView CreateDocument(
        string population,
        IReadOnlyList<LibraryQueryMatch> results,
        LibraryQuerySummary summary)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(summary);
        return new()
        {
            TitleText = new(
                TextPolicy.Field,
                $"Library Query: {population}"),
            Results =
            [
                .. results.Select(match => new LibraryQueryRow(match)),
            ],
            QuerySummary =
            [
                new(
                    summary.PopulationCandidates,
                    summary.Candidates,
                    summary.Matches,
                    summary.Failures,
                    Completion(summary)),
            ],
        };
    }

    private static string Completion(
        LibraryQuerySummary summary) =>
        summary.IncompleteReasons
            == LibraryQueryIncompleteReason.None
                ? "Complete"
                : summary.IncompleteReasons.ToString();

    public sealed class LibraryRows
        : ISectionDescriptor<LibraryQueryView>
    {
        public static string Name => LibrariesName;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(LibraryQueryView model) => true;
    }

    public sealed class QuerySummary
        : ISectionDescriptor<LibraryQueryView>
    {
        public static string Name => QuerySummaryName;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Fixed;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(LibraryQueryView model) => true;
    }
}
