using System.Diagnostics;
using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.Sections;

/// <summary>The section catalog for asset-mode <c>depends</c>.</summary>
internal static class DependsAssetSections
{
    public const string DependencyGraph = "Dependency Graph";
    public const string Roots = "Roots";
    public const string Dependencies = "Dependencies";
    public const string RestoredEdges = "Restored Edges";
    public const string Failures = "Failures";
    public const string DependencyGroups = "Dependency Groups";
    public const string RestoredPackages = "Restored Packages";

    private static string[] ConsumerSectionOrder { get; } =
    [
        DependencyGraph,
        Dependencies,
        Failures,
    ];

    public static SectionCatalog<DependsAssetProjection> Catalog { get; } =
        CreatePipeline().Compile();

    public static string[] SectionOrder { get; } =
        Catalog.Pipeline.AllSectionNames;

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
        FilterSchema(
            DependsAssetViewContext.Default
                .GetSchemaInfo<DependsAssetTableView>()!
                .ToDocumentSchema(),
            Catalog.SelectableSectionNames);

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

    private static SectionPipeline<DependsAssetProjection> CreatePipeline()
    {
        var pipeline = new SectionPipeline<DependsAssetProjection>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<GraphSection>()
            .Add<DependencySection>()
            .Add<FailureSection>()
            .AddBaseCategory(
                SectionCategoryNames.Dependencies,
                ConsumerSectionOrder);
        RegisterDiagnosticSections(pipeline);
        return pipeline;
    }

    [Conditional("DEBUG")]
    private static void RegisterDiagnosticSections(
        SectionPipeline<DependsAssetProjection> pipeline)
    {
        pipeline
            .Add<RootSection>()
            .Add<RestoredEdgeSection>()
            .Add<DependencyGroupSection>()
            .Add<RestoredPackageSection>();
    }

    private static DocumentSchema FilterSchema(
        DocumentSchema schema,
        IEnumerable<string> registeredSections)
    {
        HashSet<string> registered = registeredSections.ToHashSet(
            StringComparer.OrdinalIgnoreCase);
        var result = new DocumentSchema();
        foreach (string name in schema.SectionNames)
        {
            if (!registered.Contains(name))
                continue;

            var section = schema.GetSection(name);
            if (section is { Items.Length: > 0 })
            {
                result.Add(
                    name,
                    section.ItemKind,
                    section.Items.Select(item => item.Name).ToArray());
            }
            else
            {
                result.AddSection(name);
            }
        }

        return result;
    }

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
        public static bool ExplicitOnly => true;
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
        public static bool ExplicitOnly => true;
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
        public static bool ExplicitOnly => true;
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
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependsAssetProjection model) =>
            !model.RestoredPackages.IsEmpty;
    }
}
