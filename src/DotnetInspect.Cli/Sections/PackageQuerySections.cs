using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspect.Cli.Views;
using InertText;
using Markout;

namespace DotnetInspect.Cli.Sections;

public static class PackageQuerySections
{
    public const string LiteralStringsName = "Literal Strings";
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
            .Add<LiteralStrings>()
            .Add<QuerySummary>()
            .AddBaseCategory(
                SectionCategoryNames.Query,
                PackageProfileSections.Packages,
                LiteralStringsName,
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
            LiteralStrings = [],
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
        IReadOnlyList<PackageQueryMatch> selectedResults =
            RowWindow.Apply(rows, results);
        return new()
        {
            TitleText = new(TextPolicy.Field, $"Package Query: {prefix}"),
            Summary = summary,
            Results =
            [
                .. selectedResults
                    .Select(match => new PackageQuerySemanticRow(match)),
            ],
            LiteralStrings = CreateLiteralStringRows(selectedResults),
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

    private static List<PackageQueryLiteralStringRow> CreateLiteralStringRows(
        IReadOnlyList<PackageQueryMatch> results)
    {
        var seen = new HashSet<InertString>();
        var rows = new List<PackageQueryLiteralStringRow>();
        foreach (PackageQueryMatch match in results)
        {
            PackageQueryLibraryLiteralResult literal =
                match.LibraryLiteral
                ?? throw new ArgumentException(
                    "A semantic Package Query row requires library-literal evidence.",
                    nameof(results));
            foreach (var occurrence in literal.Occurrences)
            {
                if (seen.Add(occurrence.LiteralText))
                    rows.Add(new(occurrence.LiteralText));
            }
        }
        return rows;
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

    public sealed class LiteralStrings : ISectionDescriptor<PackageQueryView>
    {
        public static string Name => LiteralStringsName;
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
