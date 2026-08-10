using DotnetInspector.Queries;
using ILInspector.Metadata;
using Markout;

namespace DotnetInspector.Sections;

public sealed record DiffDiscoveryModel;

public sealed class DiffQueryContext
{
    readonly Func<BodySignalComparisonInput>? _createBodySignalComparisonInput;
    BodySignalComparisonInput? _bodySignalComparisonInput;

    public DiffQueryContext(
        ApiSurface fromSurface,
        ApiSurface toSurface,
        Func<BodySignalComparisonInput>? createBodySignalComparisonInput = null)
    {
        FromSurface = fromSurface ?? throw new ArgumentNullException(nameof(fromSurface));
        ToSurface = toSurface ?? throw new ArgumentNullException(nameof(toSurface));
        _createBodySignalComparisonInput = createBodySignalComparisonInput;
    }

    public ApiSurface FromSurface { get; }
    public ApiSurface ToSurface { get; }

    public BodySignalComparisonInput GetBodySignalComparisonInput()
        => _bodySignalComparisonInput ??=
            (_createBodySignalComparisonInput
                ?? throw new InspectionQueryException(
                    "Body signal comparison input was not provided."))();
}

public sealed record DiffSectionCatalog(
    SectionPipeline<DiffDiscoveryModel> Pipeline,
    InspectionQueryRegistry<DiffQueryContext> QueryRegistry);

public static class DiffSections
{
    public static DiffSectionCatalog CreateCatalog()
    {
        var queryRegistry = CreateQueryRegistry();
        return new DiffSectionCatalog(
            CreatePipeline(queryRegistry.CostOf),
            queryRegistry);
    }

    public static SectionPipeline<DiffDiscoveryModel> CreatePipeline()
    {
        var queryRegistry = CreateQueryRegistry();
        return CreatePipeline(queryRegistry.CostOf);
    }

    private static SectionPipeline<DiffDiscoveryModel> CreatePipeline(
        Func<InspectionQueryDefinition, InspectionCost> queryCost)
    {
        return new SectionPipeline<DiffDiscoveryModel>()
            .UseQueryCosts(queryCost)
            .Add<Changes>(ApiComparisonQuery.Definition)
            .Add<AnalysisDiff>(BodySignalComparisonQuery.Definition)
            .Add<ImplementationDiff>()
            .Add<FindingTransitions>();
    }

    public static InspectionQueryRegistry<DiffQueryContext> CreateQueryRegistry()
        => new InspectionQueryRegistry<DiffQueryContext>()
            .Add(
                ApiComparisonQuery.Definition,
                static context => ApiComparisonQuery.Execute(
                    context.FromSurface,
                    context.ToSurface))
            .Add(
                BodySignalComparisonQuery.Definition,
                static context => BodySignalComparisonQuery.Execute(
                    context.GetBodySignalComparisonInput()));

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
        public static string? ScannerKey => null;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }

    public sealed class FindingTransitions : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Finding Transitions";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }

    public sealed class AnalysisDiff : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Analysis Diff";
        public static bool IsExpensive => false;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }

    public sealed class ImplementationDiff : ISectionDescriptor<DiffDiscoveryModel>
    {
        public static string Name => "Implementation Diff";
        public static bool IsExpensive => true;
        public static bool ExplicitOnly => true;
        public static string? ScannerKey => null;
        public static bool CanRender(DiffDiscoveryModel model) => true;
    }
}
