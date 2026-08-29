using System.Collections.Immutable;
using DotnetInspector.Options;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public readonly record struct SectionQueryDemand(
    string Section,
    InspectionQueryDefinition Query);

public sealed record CompiledSectionCategory(
    string Name,
    SectionCategoryRole Role,
    ImmutableArray<string> Sections);

public sealed class SectionQueryPlan
{
    internal SectionQueryPlan(
        ImmutableArray<InspectionQueryDefinition> queries,
        ImmutableArray<SectionQueryDemand> demands)
    {
        Queries = queries;
        Demands = demands;
    }

    public ImmutableArray<InspectionQueryDefinition> Queries { get; }

    public ImmutableArray<SectionQueryDemand> Demands { get; }

    public HashSet<InspectionQueryDefinition> Activate(
        InspectionTrace? trace = null,
        IReadOnlyList<HostQueryDemand>? commandDemand = null)
    {
        HashSet<InspectionQueryDefinition> queries = [.. Queries];

        if (trace is not null)
        {
            foreach (SectionQueryDemand demand in Demands)
                trace.RecordQueryDemand(demand.Section, demand.Query);
        }

        if (commandDemand is not null)
        {
            foreach (HostQueryDemand demand in commandDemand)
            {
                queries.Add(demand.Query);
                trace?.RecordCommandQueryDemand(demand.Reason, demand.Query);
            }
        }

        trace?.RecordRequestedQueries(queries);
        return queries;
    }
}

public sealed class SectionCatalog<TModel>
{
    private const int PlanVariantCount = 4;

    private readonly SectionQueryPlan[] _automaticPlans;
    private readonly Dictionary<string, PrecomputedSelection> _singleSectionPlans;
    private readonly ImmutableArray<PrecomputedSelection> _selectionPlans;

    internal SectionCatalog(SectionPipeline<TModel> pipeline)
    {
        Pipeline = pipeline;
        AllSectionNames = [.. pipeline.AllSectionNames];
        AlphabeticalSectionOrder = [.. pipeline.AlphabeticalSectionOrder];
        DeclaredQueries = [.. pipeline.DeclaredQueries];
        SelectableSectionNames = [.. pipeline.SelectableSectionNames];
        InfoSectionNames = [.. pipeline.InfoSectionNames];
        BaseSectionNames = [.. pipeline.BaseSectionNames];
        FixedOverviewSectionNames = [.. pipeline.FixedOverviewSectionNames];
        BareSelectSectionNames = [.. pipeline.BareSelectSectionNames];
        AuthoredCategories =
            [.. pipeline.RegisteredCategories.Select(static category =>
                new CompiledSectionCategory(
                    category.Name,
                    category.Role,
                    [.. category.Sections]))];

        ImmutableDictionary<string, ImmutableArray<string>>.Builder categories =
            ImmutableDictionary.CreateBuilder<string, ImmutableArray<string>>(
                StringComparer.OrdinalIgnoreCase);
        IReadOnlyDictionary<string, string[]> categoryMap = pipeline.GetCategoryMap();
        SelectionCategoryMap = categoryMap;
        CategoryNames = [.. categoryMap.Keys];
        foreach ((string name, string[] sections) in categoryMap)
            categories.Add(name, [.. sections]);

        CategoryMap = categories.ToImmutable();

        Verbosity[] verbosityValues = Enum.GetValues<Verbosity>();
        _automaticPlans = new SectionQueryPlan[verbosityValues.Length * PlanVariantCount];
        foreach (Verbosity verbosity in verbosityValues)
        {
            for (int fixedOverview = 0; fixedOverview <= 1; fixedOverview++)
            {
                for (int excludeUnbounded = 0; excludeUnbounded <= 1; excludeUnbounded++)
                {
                    _automaticPlans[GetAutomaticIndex(
                        verbosity,
                        fixedOverview != 0,
                        excludeUnbounded != 0)] = pipeline.CreateQueryPlan(
                            verbosity,
                            include: null,
                            fixedOverview != 0,
                            excludeUnbounded != 0);
                }
            }
        }

        _singleSectionPlans = new Dictionary<string, PrecomputedSelection>(
            StringComparer.OrdinalIgnoreCase);
        foreach (string section in SelectableSectionNames)
        {
            HashSet<string> include = new(StringComparer.OrdinalIgnoreCase) { section };
            _singleSectionPlans.Add(
                section,
                new PrecomputedSelection(
                    [section],
                    CreateExplicitSelectionPlans(pipeline, include)));
        }

        ImmutableArray<PrecomputedSelection>.Builder selections =
            ImmutableArray.CreateBuilder<PrecomputedSelection>();
        foreach (ImmutableArray<string> categorySections in CategoryMap.Values)
            AddSelection(pipeline, selections, categorySections);

        AddSelection(pipeline, selections, BaseSectionNames);
        _selectionPlans = selections.ToImmutable();
    }

    public SectionPipeline<TModel> Pipeline { get; }

    public ImmutableArray<string> AllSectionNames { get; }

    public ImmutableArray<string> AlphabeticalSectionOrder { get; }

    public ImmutableArray<InspectionQueryDefinition> DeclaredQueries { get; }

    public ImmutableArray<string> SelectableSectionNames { get; }

    public ImmutableArray<string> InfoSectionNames { get; }

    public ImmutableArray<string> BaseSectionNames { get; }

    public ImmutableArray<string> FixedOverviewSectionNames { get; }

    public ImmutableArray<string> BareSelectSectionNames { get; }

    public ImmutableArray<CompiledSectionCategory> AuthoredCategories { get; }

    public ImmutableArray<string> CategoryNames { get; }

    public ImmutableDictionary<string, ImmutableArray<string>> CategoryMap { get; }

    internal IReadOnlyDictionary<string, string[]> SelectionCategoryMap { get; }

    public SectionQueryPlan PlanQueries(
        Verbosity verbosity,
        HashSet<string>? include = null,
        bool fixedOverview = false,
        bool excludeUnbounded = false)
    {
        if (include is null || include.Count == 0)
        {
            return _automaticPlans[GetAutomaticIndex(
                verbosity,
                fixedOverview,
                excludeUnbounded)];
        }

        if (include.Count == 1)
        {
            foreach (string section in include)
            {
                if (_singleSectionPlans.TryGetValue(
                        section,
                        out PrecomputedSelection? selection)
                    && SelectionEquals(include, selection.Sections))
                {
                    return selection.Plans[excludeUnbounded ? 1 : 0];
                }
            }
        }

        foreach (PrecomputedSelection selection in _selectionPlans)
        {
            if (SelectionEquals(include, selection.Sections))
                return selection.Plans[excludeUnbounded ? 1 : 0];
        }

        return Pipeline.CreateQueryPlan(
            verbosity,
            include,
            fixedOverview,
            excludeUnbounded);
    }

    private static SectionQueryPlan[] CreateExplicitSelectionPlans(
        SectionPipeline<TModel> pipeline,
        HashSet<string> include) =>
    [
        pipeline.CreateQueryPlan(
            Verbosity.Normal,
            include,
            fixedOverview: false,
            excludeUnbounded: false),
        pipeline.CreateQueryPlan(
            Verbosity.Normal,
            include,
            fixedOverview: false,
            excludeUnbounded: true)
    ];

    private static void AddSelection(
        SectionPipeline<TModel> pipeline,
        ImmutableArray<PrecomputedSelection>.Builder selections,
        ImmutableArray<string> sections)
    {
        if (sections.IsEmpty ||
            selections.Any(selection => selection.Sections.SequenceEqual(
                sections,
                StringComparer.OrdinalIgnoreCase)))
        {
            return;
        }

        HashSet<string> include = new(sections, StringComparer.OrdinalIgnoreCase);
        selections.Add(new PrecomputedSelection(
            sections,
            CreateExplicitSelectionPlans(pipeline, include)));
    }

    private static bool SelectionEquals(
        HashSet<string> include,
        ImmutableArray<string> sections)
    {
        if (include.Count != sections.Length)
            return false;

        foreach (string section in sections)
        {
            if (!include.Contains(section))
                return false;
        }

        return true;
    }

    private static int GetAutomaticIndex(
        Verbosity verbosity,
        bool fixedOverview,
        bool excludeUnbounded) =>
        ((int)verbosity * PlanVariantCount) |
        (fixedOverview ? 2 : 0) |
        (excludeUnbounded ? 1 : 0);

    private sealed record PrecomputedSelection(
        ImmutableArray<string> Sections,
        SectionQueryPlan[] Plans);
}
