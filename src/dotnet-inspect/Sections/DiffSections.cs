using DotnetInspector.Queries;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Sections;

public sealed record DiffDiscoveryModel;

public sealed class DiffQueryContext
{
    readonly Func<BodySignalComparisonInput>? _createBodySignalComparisonInput;
    readonly Func<ImplementationComparisonInput>?
        _createImplementationComparisonInput;
    BodySignalComparisonInput? _bodySignalComparisonInput;
    ImplementationComparisonInput? _implementationComparisonInput;

    public DiffQueryContext(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        Func<BodySignalComparisonInput>? createBodySignalComparisonInput = null,
        Func<ImplementationComparisonInput>?
            createImplementationComparisonInput = null)
    {
        FromSurface = fromSurface ?? throw new ArgumentNullException(nameof(fromSurface));
        ToSurface = toSurface ?? throw new ArgumentNullException(nameof(toSurface));
        _createBodySignalComparisonInput = createBodySignalComparisonInput;
        _createImplementationComparisonInput =
            createImplementationComparisonInput;
    }

    public ApiSurface FromSurface { get; }
    public ApiSurface ToSurface { get; }

    public BodySignalComparisonInput GetBodySignalComparisonInput()
        => _bodySignalComparisonInput ??=
            (_createBodySignalComparisonInput
                ?? throw new InspectionQueryException(
                    "Body signal comparison input was not provided."))();

    public ImplementationComparisonInput GetImplementationComparisonInput()
        => _implementationComparisonInput ??=
            (_createImplementationComparisonInput
                ?? throw new InspectionQueryException(
                    "Implementation comparison input was not provided."))();
}

public sealed record DiffSectionCatalog(
    SectionCatalog<DiffDiscoveryModel> Sections,
    InspectionQueryCatalog<DiffQueryContext> QueryCatalog)
{
    public SectionPipeline<DiffDiscoveryModel> Pipeline => Sections.Pipeline;
}

public static class DiffSections
{
    /// <summary>The reusable fixed-domain catalog for Diff queries.</summary>
    public static InspectionQueryCatalog<DiffQueryContext> QueryCatalog { get; } =
        BuildQueryCatalog();

    /// <summary>The reusable fixed-domain catalog for Diff sections and query-demand plans.</summary>
    public static SectionCatalog<DiffDiscoveryModel> SectionCatalog { get; } =
        CreatePipeline().Compile();

    /// <summary>The complete reusable Diff section and query catalog.</summary>
    public static DiffSectionCatalog Catalog { get; } =
        new(SectionCatalog, QueryCatalog);

    public static DiffSectionCatalog CreateCatalog() => Catalog;

    public static SectionPipeline<DiffDiscoveryModel> CreatePipeline()
        => CreatePipeline(QueryCatalog.CostOf);

    private static SectionPipeline<DiffDiscoveryModel> CreatePipeline(
        Func<InspectionQueryDefinition, InspectionCost> queryCost)
    {
        return new SectionPipeline<DiffDiscoveryModel>()
            .UseQueryCosts(queryCost)
            .Add<Changes>(ApiComparisonQuery.Definition)
            .Add<AnalysisDiff>(BodySignalComparisonQuery.Definition)
            .Add<ImplementationDiff>(ImplementationComparisonQuery.Definition)
            .Add<FindingTransitions>();
    }

    public static InspectionQueryRegistry<DiffQueryContext> CreateQueryRegistry()
        => QueryCatalog.ToBuilder();

    private static InspectionQueryCatalog<DiffQueryContext> BuildQueryCatalog()
        => new InspectionQueryRegistry<DiffQueryContext>()
            .Add(
                ApiComparisonQuery.Definition,
                static context => ApiComparisonQuery.Execute(
                    context.FromSurface,
                    context.ToSurface))
            .Add(
                BodySignalComparisonQuery.Definition,
                static context => BodySignalComparisonQuery.Execute(
                    context.GetBodySignalComparisonInput()))
            .Add(
                ImplementationComparisonQuery.Definition,
                static context => ImplementationComparisonQuery.Execute(
                    context.GetImplementationComparisonInput()))
            .Compile();

    public static DocumentSchema CreateSchema()
    {
        return new DocumentSchema()
            .Add(Changes.Name, "column", "Change", "Classification", "Type", "Member", "Kind", "Detail", "Old", "New")
            .Add(AnalysisDiff.Name, "section", "Member", "Signal", "Old", "New", "Delta", "Shape", "Evidence")
            .Add(ImplementationDiff.Name, "section", "Member", "Mechanism", "Difference", "Change", "Evidence")
            .Add(FindingTransitions.Name, "section", "Transition", "Finding", "Target", "From", "To", "Old", "New", "Detail");
    }

    public sealed class Changes : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Changes";
        public static bool IsExpensive => false;
        public static bool Info => true;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }

    public sealed class FindingTransitions : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Finding Transitions";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }

    public sealed class AnalysisDiff : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Analysis Diff";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }

    public sealed class ImplementationDiff : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Implementation Diff";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }
}
