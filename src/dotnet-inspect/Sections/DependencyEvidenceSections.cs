using DotnetInspector.Queries;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Sections;

/// <summary>Host-supplied input for one dependency-evidence query execution.</summary>
/// <remarks>
/// Acquisition already happened: the request carries admitted roots and typed upstream failures,
/// so every section demands the same network-free composition query over one immutable snapshot.
/// </remarks>
public sealed record DependencyEvidenceQueryContext(
    PackageDependencyEvidenceRequest Request);

/// <summary>The complete reusable dependency-evidence section and query catalog.</summary>
public sealed record DependencyEvidenceSectionCatalog(
    CompiledInspectionLens<
        DependencyEvidenceQueryContext,
        DependencyEvidenceProjection> Lens)
{
    public SectionCatalog<DependencyEvidenceProjection> Sections => Lens.Sections;

    public InspectionQueryCatalog<DependencyEvidenceQueryContext> QueryCatalog =>
        Lens.QueryCatalog;

    public SectionPipeline<DependencyEvidenceProjection> Pipeline =>
        Sections.Pipeline;
}

/// <summary>
/// Sections and section-selection policy for the <c>dependency-evidence</c> command.
/// </summary>
/// <remarks>
/// The declared ladder is exactly the one owned by
/// <c>docs/design/dependency-evidence-cli.md</c>: <c>Dependencies</c> is the single high-value
/// section and therefore the only table in the default <c>-v:m</c> view; <c>Roots</c>,
/// <c>Restored Edges</c>, and <c>Failures</c> join at <c>-v:n</c>; <c>Dependency Groups</c> and
/// <c>Restored Packages</c> join at <c>-v:d</c>.
/// </remarks>
public static class DependencyEvidenceSections
{
    public const string Dependencies = "Dependencies";
    public const string Roots = "Roots";
    public const string RestoredEdges = "Restored Edges";
    public const string Failures = "Failures";
    public const string DependencyGroups = "Dependency Groups";
    public const string RestoredPackages = "Restored Packages";

    /// <summary>The reusable fixed-domain catalog for dependency-evidence queries.</summary>
    public static InspectionQueryCatalog<DependencyEvidenceQueryContext>
        QueryCatalog { get; } = BuildQueryCatalog();

    /// <summary>The fixed dependency-evidence producer domain.</summary>
    public static CompiledInspectionDomain<DependencyEvidenceQueryContext> Domain
        { get; } = new(QueryCatalog);

    /// <summary>The reusable lens over the fixed producer domain.</summary>
    public static CompiledInspectionLens<
        DependencyEvidenceQueryContext,
        DependencyEvidenceProjection> Lens { get; } =
        Domain.CompileLens<DependencyEvidenceProjection>(ConfigurePipeline);

    /// <summary>The complete reusable section and query catalog.</summary>
    public static DependencyEvidenceSectionCatalog Catalog { get; } = new(Lens);

    public static DependencyEvidenceSectionCatalog CreateCatalog() => Catalog;

    /// <summary>Every declared section, in rendered order.</summary>
    public static string[] SectionOrder { get; } =
    [
        Dependencies,
        Roots,
        RestoredEdges,
        Failures,
        DependencyGroups,
        RestoredPackages,
    ];

    public static DocumentSchema CreateSchema() =>
        DependencyEvidenceViewContext.Default
            .GetSchemaInfo<DependencyEvidenceView>()!
            .ToDocumentSchema();

    /// <summary>
    /// The table-only schema: the same section row shapes without the document summary fields.
    /// </summary>
    public static DocumentSchema CreateTableSchema() =>
        DependencyEvidenceViewContext.Default
            .GetSchemaInfo<DependencyEvidenceTableView>()!
            .ToDocumentSchema();

    /// <summary>Counts the rows one selected section declares in a projection.</summary>
    public static int CountRows(
        DependencyEvidenceProjection projection,
        string section)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return section switch
        {
            Dependencies => projection.Dependencies.Length,
            Roots => projection.Roots.Length,
            RestoredEdges => projection.RestoredEdges.Length,
            Failures => projection.Failures.Length,
            DependencyGroups => projection.DependencyGroups.Length,
            RestoredPackages => projection.RestoredPackages.Length,
            _ => 0,
        };
    }

    private static InspectionQueryCatalog<DependencyEvidenceQueryContext>
        BuildQueryCatalog() =>
        new InspectionQueryRegistry<DependencyEvidenceQueryContext>()
            .Add(
                PackageDependencyEvidenceQuery.Definition,
                static (context, _) =>
                    PackageDependencyEvidenceQuery.Execute(context.Request))
            .Compile();

    private static void ConfigurePipeline(
        SectionPipeline<DependencyEvidenceProjection> pipeline)
    {
        pipeline
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<DependencyRows>(PackageDependencyEvidenceQuery.Definition)
            .Add<RootRows>(PackageDependencyEvidenceQuery.Definition)
            .Add<RestoredEdgeRows>(PackageDependencyEvidenceQuery.Definition)
            .Add<FailureRows>(PackageDependencyEvidenceQuery.Definition)
            .Add<DependencyGroupRows>(PackageDependencyEvidenceQuery.Definition)
            .Add<RestoredPackageRows>(PackageDependencyEvidenceQuery.Definition);
    }

    /// <summary>The single high-value section: normalized direct declarations.</summary>
    public sealed class DependencyRows
        : ISectionDescriptor<DependencyEvidenceProjection>
    {
        public static string Name => Dependencies;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependencyEvidenceProjection model) =>
            !model.Dependencies.IsEmpty;
    }

    public sealed class RootRows
        : ISectionDescriptor<DependencyEvidenceProjection>
    {
        public static string Name => Roots;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependencyEvidenceProjection model) =>
            !model.Roots.IsEmpty;
    }

    public sealed class RestoredEdgeRows
        : ISectionDescriptor<DependencyEvidenceProjection>
    {
        public static string Name => RestoredEdges;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependencyEvidenceProjection model) =>
            !model.RestoredEdges.IsEmpty;
    }

    public sealed class FailureRows
        : ISectionDescriptor<DependencyEvidenceProjection>
    {
        public static string Name => Failures;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Informative;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependencyEvidenceProjection model) =>
            !model.Failures.IsEmpty;
    }

    public sealed class DependencyGroupRows
        : ISectionDescriptor<DependencyEvidenceProjection>
    {
        public static string Name => DependencyGroups;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependencyEvidenceProjection model) =>
            !model.DependencyGroups.IsEmpty;
    }

    public sealed class RestoredPackageRows
        : ISectionDescriptor<DependencyEvidenceProjection>
    {
        public static string Name => RestoredPackages;
        public static bool IsExpensive => false;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(DependencyEvidenceProjection model) =>
            !model.RestoredPackages.IsEmpty;
    }
}
