using DotnetInspect.Cli.Options;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Cli.Sections;

public static class SectionPipelineCliExtensions
{
    public static List<string> GetEffectiveSections<TModel>(
        this SectionPipeline<TModel> pipeline,
        TModel model,
        Verbosity verbosity,
        HashSet<string>? include = null,
        bool fixedOverview = false,
        bool explicitInclude = false) =>
        pipeline.GetEffectiveSections(
            model,
            ToSectionViewLevel(verbosity),
            include,
            fixedOverview,
            explicitInclude);

    public static HashSet<string> GetCandidateSections<TModel>(
        this SectionPipeline<TModel> pipeline,
        Verbosity verbosity,
        HashSet<string>? include = null,
        bool fixedOverview = false) =>
        pipeline.GetCandidateSections(
            ToSectionViewLevel(verbosity),
            include,
            fixedOverview);

    public static (List<string> Empty, int RequestedCount) GetEmptySections<TModel>(
        this SectionPipeline<TModel> pipeline,
        TModel model,
        Verbosity verbosity,
        HashSet<string>? include = null) =>
        pipeline.GetEmptySections(
            model,
            ToSectionViewLevel(verbosity),
            include);

    public static void ListEffectiveSections<TModel>(
        this SectionPipeline<TModel> pipeline,
        TModel model)
    {
        foreach (string name in pipeline.GetEffectiveSections(
            model,
            SectionViewLevel.Detailed))
        {
            Console.WriteLine(name);
        }
    }

    public static HashSet<string>? ComputeIncludeSections<TModel>(
        this SectionPipeline<TModel> pipeline,
        TModel model,
        Verbosity verbosity,
        HashSet<string>? include = null,
        bool allSelector = false,
        bool fixedOverview = false,
        bool explicitInclude = false) =>
        pipeline.ComputeIncludeSections(
            model,
            ToSectionViewLevel(verbosity),
            include,
            allSelector,
            fixedOverview,
            explicitInclude);

    public static Verbosity GetRequiredVerbosity<TModel>(
        this SectionPipeline<TModel> pipeline,
        HashSet<string>? include) =>
        ToVerbosity(pipeline.GetRequiredViewLevel(include));

    public static HashSet<InspectionQueryDefinition> GetRequiredQueries<TModel>(
        this SectionPipeline<TModel> pipeline,
        Verbosity verbosity,
        HashSet<string>? include = null,
        bool fixedOverview = false,
        InspectionTrace? trace = null,
        IReadOnlyList<HostQueryDemand>? commandDemand = null,
        bool excludeUnbounded = false)
    {
        SectionQueryPlan plan = pipeline.PlanQueries(
            ToSectionViewLevel(verbosity),
            include,
            fixedOverview,
            excludeUnbounded);
        return Activate(plan, trace, commandDemand);
    }

    public static SectionQueryPlan PlanQueries<TModel>(
        this SectionCatalog<TModel> catalog,
        Verbosity verbosity,
        HashSet<string>? include = null,
        bool fixedOverview = false,
        bool excludeUnbounded = false) =>
        catalog.PlanQueries(
            ToSectionViewLevel(verbosity),
            include,
            fixedOverview,
            excludeUnbounded);

    public static CompiledInspectionPlan<TContext> Plan<TContext, TModel>(
        this CompiledInspectionLens<TContext, TModel> lens,
        Verbosity verbosity,
        HashSet<string>? include = null,
        bool fixedOverview = false,
        bool excludeUnbounded = false,
        IReadOnlyList<HostQueryDemand>? hostDemand = null) =>
        lens.Plan(
            ToSectionViewLevel(verbosity),
            include,
            fixedOverview,
            excludeUnbounded,
            hostDemand);

    public static HashSet<InspectionQueryDefinition> Activate(
        this SectionQueryPlan plan,
        InspectionTrace? trace,
        IReadOnlyList<HostQueryDemand>? hostDemand = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (trace is not null)
        {
            foreach (SectionQueryDemand demand in plan.Demands)
                trace.RecordQueryDemand(demand.Section, demand.Query);
            if (hostDemand is not null)
            {
                foreach (HostQueryDemand demand in hostDemand)
                    trace.RecordCommandQueryDemand(demand.Reason, demand.Query);
            }
        }

        HashSet<InspectionQueryDefinition> queries =
            plan.Activate(hostDemand);
        trace?.RecordRequestedQueries(queries);
        return queries;
    }

    public static HashSet<string> GetAuthorizedSections<TModel>(
        this SectionPipeline<TModel> pipeline,
        SectionCapabilities capabilities,
        Verbosity userVerbosity,
        HashSet<string>? include)
    {
        bool explicitInclude = include is { Count: > 0 };
        bool wantsSourceContent =
            (capabilities & SectionCapabilities.MayFetchSources) != 0;
        if (!explicitInclude
            && (wantsSourceContent || userVerbosity < Verbosity.Detailed))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return pipeline.GetRequestedSections(
            capabilities,
            ToSectionViewLevel(userVerbosity),
            include);
    }

    private static SectionViewLevel ToSectionViewLevel(Verbosity verbosity) =>
        verbosity switch
        {
            Verbosity.Quiet => SectionViewLevel.Quiet,
            Verbosity.Minimal => SectionViewLevel.Minimal,
            Verbosity.Normal => SectionViewLevel.Normal,
            Verbosity.Detailed => SectionViewLevel.Detailed,
            _ => throw new ArgumentOutOfRangeException(nameof(verbosity)),
        };

    private static Verbosity ToVerbosity(SectionViewLevel view) =>
        view switch
        {
            SectionViewLevel.Quiet => Verbosity.Quiet,
            SectionViewLevel.Minimal => Verbosity.Minimal,
            SectionViewLevel.Normal => Verbosity.Normal,
            SectionViewLevel.Detailed => Verbosity.Detailed,
            _ => throw new ArgumentOutOfRangeException(nameof(view)),
        };
}
