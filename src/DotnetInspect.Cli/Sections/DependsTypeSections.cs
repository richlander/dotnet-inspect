using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.Sections;

/// <summary>The legacy section catalog for positional-Type <c>depends</c>.</summary>
internal static class DependsTypeSections
{
    public const string DependencyGraph = "Dependency Graph";

    public static SectionCatalog<DependsAssetProjection> Catalog
    { get; } = new SectionPipeline<DependsAssetProjection>()
        .UseCuratedCatalog()
        .WithoutComputedPoles()
        .Add<GraphSection>()
        .AddBaseCategory(
            SectionCategoryNames.Dependencies,
            DependencyGraph)
        .Compile();

    public static DocumentSchema CreateSchema() =>
        DependsAssetViewContext.Default
            .GetSchemaInfo<DependsGraphTableView>()!
            .ToDocumentSchema();

    public sealed class GraphSection :
        ISectionDescriptor<DependsAssetProjection>
    {
        public static string Name => DependencyGraph;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependsAssetProjection model) =>
            !model.Graph.Nodes.IsEmpty;
    }
}
