using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using Markout;

namespace DotnetInspect.Cli.Sections;

public static class LibraryQuerySections
{
    public const string LibrariesName = "Libraries";
    public const string QuerySummaryName = "Query Summary";

    public static SectionCatalog<LibraryQueryView> Catalog { get; } =
        new SectionPipeline<LibraryQueryView>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<Libraries>()
            .Add<QuerySummary>()
            .AddBaseCategory(
                SectionCategoryNames.Query,
                LibrariesName,
                QuerySummaryName)
            .Compile();

    public static DocumentSchema CreateSchema() =>
        SearchViewContext.Default
            .GetSchemaInfo<LibraryQueryView>()!
            .ToDocumentSchema();

    public static LibraryQueryView CreateDocument(
        IReadOnlyList<LibraryQueryMatch> results,
        LibraryQuerySummary summary)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(summary);
        return new()
        {
            Results =
            [
                .. results.Select(match => new LibraryQueryRow(match)),
            ],
            QuerySummary =
            [
                new(
                    summary.Population,
                    summary.Evaluated,
                    summary.Matches,
                    summary.Failures,
                    summary.CandidateLimit,
                    summary.Completion),
            ],
        };
    }

    public sealed class Libraries : ISectionDescriptor<LibraryQueryView>
    {
        public static string Name => LibrariesName;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(LibraryQueryView model) => true;
    }

    public sealed class QuerySummary : ISectionDescriptor<LibraryQueryView>
    {
        public static string Name => QuerySummaryName;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(LibraryQueryView model) => true;
    }
}
