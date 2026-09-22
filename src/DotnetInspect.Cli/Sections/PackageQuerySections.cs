using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspect.Cli.Views;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Sections;

public static class PackageQuerySections
{
    public const string QuerySummaryName = "Query Summary";

    public static string[] BareSelectSectionNames { get; } =
    [
        PackageProfileSections.Packages,
    ];

    public static SectionCatalog<PackageQueryView> Catalog { get; } =
        new SectionPipeline<PackageQueryView>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<PackageRows>()
            .Add<QuerySummary>()
            .AddBaseCategory(
                SectionCategoryNames.Query,
                PackageProfileSections.Packages,
                QuerySummaryName)
            .Compile();

    public static DocumentSchema CreateSchema() =>
        SearchViewContext.Default
            .GetSchemaInfo<PackageQuerySemanticView>()!
            .ToDocumentSchema();

    public static PackageQueryView CreateDocument(
        string prefix,
        IReadOnlyList<PackageQueryMatch> results,
        PackageQuerySummary summary,
        RowWindow? rows = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(summary);
        return new()
        {
            TitleText = new(TextPolicy.Field, $"Package Query: {prefix}"),
            Summary = summary,
            Results =
            [
                .. RowWindow.Apply(rows, results)
                    .Select(match => new PackageQueryRow(match)),
            ],
            QuerySummary =
            [
                new(
                    summary.Candidates,
                    summary.Matches,
                    summary.Failures,
                    summary.Completion),
            ],
        };
    }

    public static PackageQuerySemanticView CreateSemanticDocument(
        string prefix,
        IReadOnlyList<PackageQueryMatch> results,
        PackageQuerySummary summary,
        RowWindow? rows = null)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(summary);
        return new()
        {
            TitleText = new(TextPolicy.Field, $"Package Query: {prefix}"),
            Summary = summary,
            Results =
            [
                .. RowWindow.Apply(rows, results)
                    .Select(match => new PackageQuerySemanticRow(match)),
            ],
            QuerySummary =
            [
                new(
                    summary.Candidates,
                    summary.Matches,
                    summary.Failures,
                    summary.Completion),
            ],
        };
    }

    public sealed class PackageRows : ISectionDescriptor<PackageQueryView>
    {
        public static string Name => PackageProfileSections.Packages;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(PackageQueryView model) => true;
    }

    public sealed class QuerySummary : ISectionDescriptor<PackageQueryView>
    {
        public static string Name => QuerySummaryName;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Fixed;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(PackageQueryView model) => true;
    }
}
