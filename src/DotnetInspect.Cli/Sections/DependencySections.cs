using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using Markout;

namespace DotnetInspect.Cli.Sections;

internal static class DependencySections
{
    internal const string Graph = "Dependency Graph";
    internal static readonly string[] Order =
    [
        Graph, DependencyEvidenceSections.Roots, DependencyEvidenceSections.Dependencies,
        DependencyEvidenceSections.RestoredEdges, DependencyEvidenceSections.Failures,
        DependencyEvidenceSections.DependencyGroups, DependencyEvidenceSections.RestoredPackages,
    ];

    internal static SectionCatalog<DependencyEvidenceProjection> CreateCatalog(bool networkGraph)
    {
        var pipeline = new SectionPipeline<DependencyEvidenceProjection>()
            .UseCuratedCatalog().WithoutComputedPoles();
        pipeline.Add(new SectionEntry<DependencyEvidenceProjection>
        {
            Name = Graph,
            IsExpensive = false,
            Info = true,
            SizeClass = SectionSizeClass.Informative,
            Cost = networkGraph ? SectionCost.Moderated : SectionCost.NetworkFree,
            IsApplicable = _ => true,
            CanRender = _ => true,
        });
        pipeline.Add<DependencyEvidenceSections.RootRows>();
        pipeline.Add(new SectionEntry<DependencyEvidenceProjection>
        {
            Name = DependencyEvidenceSections.Dependencies,
            IsExpensive = false,
            SizeClass = SectionSizeClass.Informative,
            Cost = SectionCost.NetworkFree,
            IsApplicable = model => !model.Dependencies.IsEmpty,
            CanRender = DependencyEvidenceSections.DependencyRows.CanRender,
        });
        pipeline.Add<DependencyEvidenceSections.RestoredEdgeRows>()
            .Add<DependencyEvidenceSections.FailureRows>()
            .Add<DependencyEvidenceSections.DependencyGroupRows>()
            .Add<DependencyEvidenceSections.RestoredPackageRows>()
            .AddBaseCategory(SectionCategoryNames.Dependencies, Order);
        return pipeline.Compile();
    }

    internal static DocumentSchema CreateSchema()
    {
        DocumentSchema schema = DependencyEvidenceSections.CreateTableSchema();
        schema.Add(Graph, "column",
            "Roots", "Source Kind", "Source Identity", "Source", "Relationship",
            "Target Kind", "Target Identity", "Target", "Minimum Depth", "Resolution",
            "Evidence Kind", "Evidence Identity");
        schema.Add(DependencyEvidenceSections.Roots, "column", DependencyDocumentOutput.RootColumns);
        schema.Add(DependencyEvidenceSections.Failures, "column", DependencyDocumentOutput.FailureColumns);
        return schema;
    }
}
