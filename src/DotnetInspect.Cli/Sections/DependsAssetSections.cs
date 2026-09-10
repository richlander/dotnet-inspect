using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.Sections;

/// <summary>The section catalog for asset-mode <c>depends</c>.</summary>
internal static class DependsAssetSections
{
    public const string DependencyGraph = "Dependency Graph";
    public const string Roots = DependencyEvidenceSections.Roots;
    public const string Dependencies = DependencyEvidenceSections.Dependencies;
    public const string RestoredEdges =
        DependencyEvidenceSections.RestoredEdges;
    public const string Failures = DependencyEvidenceSections.Failures;
    public const string DependencyGroups =
        DependencyEvidenceSections.DependencyGroups;
    public const string RestoredPackages =
        DependencyEvidenceSections.RestoredPackages;

    public static string[] SectionOrder { get; } =
    [
        DependencyGraph,
        Roots,
        Dependencies,
        RestoredEdges,
        Failures,
        DependencyGroups,
        RestoredPackages,
    ];

    public static SectionCatalog<DependsAssetProjection> Catalog { get; } =
        CreatePipeline().Compile();

    public static SectionCatalog<DependsAssetProjection> GraphCatalog
        { get; } = new SectionPipeline<DependsAssetProjection>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<GraphSection>()
            .AddBaseCategory(
                SectionCategoryNames.Dependencies,
                DependencyGraph)
            .Compile();

    public static DocumentSchema CreateSchema() =>
        CreateTableSchema();

    public static DocumentSchema CreateTableSchema() =>
        DependsAssetViewContext.Default
            .GetSchemaInfo<DependsAssetTableView>()!
            .ToDocumentSchema();

    public static DocumentSchema CreateGraphSchema() =>
        DependsAssetViewContext.Default
            .GetSchemaInfo<DependsGraphTableView>()!
            .ToDocumentSchema();

    public static int CountRows(
        DependsAssetProjection projection,
        string section) =>
        section switch
        {
            DependencyGraph => projection.GraphRows.Length,
            Roots => projection.Roots.Length,
            Dependencies => projection.Dependencies.Length,
            RestoredEdges => projection.RestoredEdges.Length,
            Failures => projection.Failures.Length,
            DependencyGroups => projection.DependencyGroups.Length,
            RestoredPackages => projection.RestoredPackages.Length,
            _ => 0,
        };

    private static SectionPipeline<DependsAssetProjection> CreatePipeline() =>
        new SectionPipeline<DependsAssetProjection>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<GraphSection>()
            .Add<RootSection>()
            .Add<DependencySection>()
            .Add<RestoredEdgeSection>()
            .Add<FailureSection>()
            .Add<DependencyGroupSection>()
            .Add<RestoredPackageSection>()
            .AddBaseCategory(
                SectionCategoryNames.Dependencies,
                SectionOrder);

    public sealed class GraphSection :
        ISectionDescriptor<DependsAssetProjection>
    {
        public static string Name => DependencyGraph;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependsAssetProjection model) =>
            !model.Graph.Nodes.IsEmpty;
    }

    public sealed class RootSection :
        ISectionDescriptor<DependsAssetProjection>
    {
        public static string Name => Roots;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependsAssetProjection model) =>
            !model.Roots.IsEmpty;
    }

    public sealed class DependencySection :
        ISectionDescriptor<DependsAssetProjection>
    {
        public static string Name => Dependencies;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependsAssetProjection model) =>
            !model.Dependencies.IsEmpty;
    }

    public sealed class RestoredEdgeSection :
        ISectionDescriptor<DependsAssetProjection>
    {
        public static string Name => RestoredEdges;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependsAssetProjection model) =>
            !model.RestoredEdges.IsEmpty;
    }

    public sealed class FailureSection :
        ISectionDescriptor<DependsAssetProjection>
    {
        public static string Name => Failures;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependsAssetProjection model) =>
            !model.Failures.IsEmpty;
    }

    public sealed class DependencyGroupSection :
        ISectionDescriptor<DependsAssetProjection>
    {
        public static string Name => DependencyGroups;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependsAssetProjection model) =>
            !model.DependencyGroups.IsEmpty;
    }

    public sealed class RestoredPackageSection :
        ISectionDescriptor<DependsAssetProjection>
    {
        public static string Name => RestoredPackages;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependsAssetProjection model) =>
            !model.RestoredPackages.IsEmpty;
    }
}
