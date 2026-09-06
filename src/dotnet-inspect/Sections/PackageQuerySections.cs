using DotnetInspector.Output;
using DotnetInspector.Queries;
using DotnetInspector.Views;
using InertText;
using Markout;

namespace DotnetInspector.Sections;

public static class PackageQuerySections
{
    public static SectionCatalog<PackageQueryView> Catalog { get; } =
        new SectionPipeline<PackageQueryView>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<PackageRows>()
            .Compile();

    public static DocumentSchema CreateSchema() =>
        SearchViewContext.Default.GetSchemaInfo<PackageQueryView>()!.ToDocumentSchema();

    public static PackageQueryView CreateDocument(
        string prefix,
        IReadOnlyList<PackageQueryEvent> events,
        RowWindow? rows = null)
    {
        PackageQuerySummary summary = events
            .OfType<PackageQueryEvent.Completed>().Single().Value;
        PackageQueryMatch[] matches =
            [.. events.OfType<PackageQueryEvent.Match>().Select(match => match.Value)];
        return new()
        {
            TitleText = new(TextPolicy.Field, $"Package Query: {prefix}"),
            Summary = summary,
            Results = [.. RowWindow.Apply(rows, matches).Select(match => new PackageQueryRow(match))],
        };
    }

    public sealed class PackageRows : ISectionDescriptor<PackageQueryView>
    {
        public static string Name => PackageProfileSections.Packages;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(PackageQueryView model) => model.Results.Count > 0;
    }
}
