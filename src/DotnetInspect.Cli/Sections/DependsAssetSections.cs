using System.Diagnostics;
using DotnetInspect.Cli.Views;
using DotnetInspector.Sections;
using Markout;

namespace DotnetInspect.Cli.Sections;

/// <summary>The section catalog for asset-mode <c>depends</c>.</summary>
internal static class DependsAssetSections
{
    public const string DependencyHierarchy = "Dependency Hierarchy";
    public const string Roots = "Roots";
    public const string Dependencies = "Dependencies";
    public const string Licenses = "Licenses";
    public const string Pruning = "Pruning";
    public const string RestoredEdges = "Restored Edges";
    public const string Failures = "Failures";
    public const string DependencyGroups = "Dependency Groups";
    public const string RestoredPackages = "Restored Packages";

    private static string[] ConsumerSectionOrder { get; } =
    [
        DependencyHierarchy,
        Dependencies,
        Failures,
    ];

    public static SectionCatalog<DependsAssetProjection> Catalog { get; } =
        CreatePipeline().Compile();

    public static string[] SectionOrder { get; } =
        Catalog.Pipeline.AllSectionNames;

    public static DocumentSchema CreateSchema() =>
        CreateTableSchema();

    public static DocumentSchema CreateTableSchema() =>
        FilterSchema(
            DependsAssetViewContext.Default
                .GetSchemaInfo<DependsAssetTableView>()!
                .ToDocumentSchema(),
            Catalog.SelectableSectionNames);

    public static int CountRows(
        DependsAssetProjection projection,
        string section) =>
        section switch
        {
            DependencyHierarchy => projection.HierarchyRows.Length,
            Roots => projection.Roots.Length,
            Dependencies => projection.Dependencies.Length,
            Licenses => projection.Licenses.Length,
            Pruning => projection.Pruning.Length,
            RestoredEdges => projection.RestoredEdges.Length,
            Failures => projection.Failures.Length,
            DependencyGroups => projection.DependencyGroups.Length,
            RestoredPackages => projection.RestoredPackages.Length,
            _ => 0,
        };

    public static bool AppliesRowWindow(
        IReadOnlySet<string> selectedSections,
        string section) =>
        !section.Equals(Failures, StringComparison.OrdinalIgnoreCase)
        || !selectedSections.Contains(Pruning);

    private static SectionPipeline<DependsAssetProjection> CreatePipeline()
    {
        var pipeline = new SectionPipeline<DependsAssetProjection>()
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<HierarchySection>()
            .Add<DependencySection>()
            .Add<LicenseSection>()
            .Add<PruningSection>()
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

    public sealed class HierarchySection :
        ISectionDescriptor<DependsAssetProjection>
    {
        public static string Name => DependencyHierarchy;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependsAssetProjection model) =>
            !model.Hierarchy.Roots.IsEmpty;
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

    public sealed class PruningSection :
        ISectionDescriptor<DependsAssetProjection>
    {
        public static string Name => Pruning;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(DependsAssetProjection model) =>
            model.Summary.Pruning.Completion
                != DependencyInspectionPruningCompletion.NotRequested;
    }

    public sealed class LicenseSection :
        ISectionDescriptor<DependsAssetProjection>
    {
        public static string Name => Licenses;
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass =>
            SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.Unbounded;
        public static bool CanRender(DependsAssetProjection model) =>
            model.Summary.Licenses.Completion
                != DependencyInspectionLicenseCompletion.NotRequested;
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
