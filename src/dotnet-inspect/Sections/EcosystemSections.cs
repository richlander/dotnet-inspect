using DotnetInspector.Queries;
using Markout;

namespace DotnetInspector.Sections;

/// <summary>The complete reusable ecosystem section and query catalog.</summary>
public sealed record EcosystemSectionCatalog(
    CompiledInspectionLens<EcosystemQueryContext, EcosystemProjection> Lens)
{
    public SectionCatalog<EcosystemProjection> Sections => Lens.Sections;

    public InspectionQueryCatalog<EcosystemQueryContext> QueryCatalog => Lens.QueryCatalog;

    public SectionPipeline<EcosystemProjection> Pipeline => Sections.Pipeline;
}

/// <summary>
/// Sections and section-selection policy for the <c>ecosystem</c> command.
/// </summary>
/// <remarks>
/// The ladder is the one owned by <c>docs/design/ecosystem-cli.md</c>: <c>Ecosystems</c> is the
/// single high-value section and therefore the only table in the default <c>-v:m</c> view, and
/// <c>Pruning</c> is explicit-only because it is verbose and answers for one ecosystem rather
/// than the registry.
/// </remarks>
public static class EcosystemSections
{
    public const string Ecosystems = "Ecosystems";
    public const string Pruning = "Pruning";

    /// <summary>
    /// The platform ecosystem's category door.
    /// </summary>
    /// <remarks>
    /// Named here rather than in <see cref="SectionCategoryNames"/> because a category is an
    /// ecosystem's own contribution, and shared infrastructure must not accumulate a name per
    /// pack. Deriving door names from the registry needs a second pack contributing a
    /// pack-specific section before the scheme has more than one data point; until then this one
    /// constant is honest and the generalization is deferred.
    /// </remarks>
    public const string PlatformCategory = "@Platform";

    /// <summary>The reusable fixed-domain catalog for ecosystem queries.</summary>
    public static InspectionQueryCatalog<EcosystemQueryContext> QueryCatalog { get; } =
        BuildQueryCatalog();

    /// <summary>The fixed ecosystem producer domain.</summary>
    public static CompiledInspectionDomain<EcosystemQueryContext> Domain { get; } =
        new(QueryCatalog);

    /// <summary>The reusable lens over the fixed producer domain.</summary>
    public static CompiledInspectionLens<EcosystemQueryContext, EcosystemProjection> Lens { get; } =
        Domain.CompileLens<EcosystemProjection>(ConfigurePipeline);

    /// <summary>The complete reusable section and query catalog.</summary>
    public static EcosystemSectionCatalog Catalog { get; } = new(Lens);

    public static EcosystemSectionCatalog CreateCatalog() => Catalog;

    /// <summary>Every declared section, in rendered order.</summary>
    public static string[] SectionOrder { get; } = [Ecosystems, Pruning];

    /// <summary>Counts the rows one selected section declares in a projection.</summary>
    public static int CountRows(EcosystemProjection projection, string section)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return section switch
        {
            Ecosystems => projection.Ecosystems.Length,
            Pruning => projection.Pruning.Length,
            _ => 0,
        };
    }

    private static InspectionQueryCatalog<EcosystemQueryContext> BuildQueryCatalog() =>
        new InspectionQueryRegistry<EcosystemQueryContext>()
            .Add(
                EcosystemQuery.Definition,
                static (context, _) => EcosystemQuery.Execute(context))
            .Compile();

    private static void ConfigurePipeline(SectionPipeline<EcosystemProjection> pipeline)
    {
        pipeline
            .UseCuratedCatalog()
            .WithoutComputedPoles()
            .Add<EcosystemRows>(EcosystemQuery.Definition)
            .Add<PruningRows>(EcosystemQuery.Definition)
            .AddCategory(PlatformCategory, [Pruning]);
    }

    /// <summary>The single high-value section: every registered ecosystem.</summary>
    public sealed class EcosystemRows : ISectionDescriptor<EcosystemProjection>
    {
        public static string Name => Ecosystems;
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Terse;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(EcosystemProjection model) => !model.Ecosystems.IsEmpty;
    }

    /// <summary>
    /// What the selected platform target subsumes.
    /// </summary>
    /// <remarks>
    /// Explicit-only for two reasons that both hold independently: it is verbose — 440 entries at
    /// <c>net11.0</c> — and it answers for one ecosystem rather than the registry, so it is not
    /// the command's high-value section. Its cost is network-free because the data ships inside
    /// reference packs already installed on this machine.
    /// </remarks>
    public sealed class PruningRows : ISectionDescriptor<EcosystemProjection>
    {
        public static string Name => Pruning;
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static SectionSizeClass SizeClass => SectionSizeClass.Verbose;
        public static SectionCost Cost => SectionCost.NetworkFree;
        public static bool CanRender(EcosystemProjection model) => !model.Pruning.IsEmpty;
    }
}
