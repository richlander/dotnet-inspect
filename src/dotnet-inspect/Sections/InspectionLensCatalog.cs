using System.Collections.Immutable;
using DotnetInspector.Options;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Binds one compiled section lens to one immutable query domain.
/// </summary>
/// <remarks>
/// Multiple lenses may share the same query catalog. A section lens whose producers execute
/// against different context types may instead create one explicit partition binding per query
/// catalog. Every query's required dependency closure must remain inside that query's partition.
/// Request state and resource lifetime remain in <typeparamref name="TContext"/>.
/// </remarks>
public sealed class InspectionLensCatalog<TContext, TModel>
{
    private readonly bool _isPartition;

    /// <summary>
    /// Creates a complete binding and verifies that every section-declared query belongs to the
    /// supplied query catalog.
    /// </summary>
    public InspectionLensCatalog(
        SectionCatalog<TModel> sections,
        InspectionQueryCatalog<TContext> queryCatalog)
        : this(sections, queryCatalog, isPartition: false)
    {
    }

    private InspectionLensCatalog(
        SectionCatalog<TModel> sections,
        InspectionQueryCatalog<TContext> queryCatalog,
        bool isPartition)
    {
        Sections = sections ?? throw new ArgumentNullException(nameof(sections));
        QueryCatalog = queryCatalog ?? throw new ArgumentNullException(nameof(queryCatalog));
        _isPartition = isPartition;

        if (!isPartition)
        {
            foreach (InspectionQueryDefinition query in sections.DeclaredQueries)
            {
                if (!queryCatalog.Contains(query))
                {
                    throw new InspectionQueryException(
                        $"Section query '{query.Name}' is not registered in the bound query catalog.");
                }
            }
        }
    }

    /// <summary>
    /// Creates one explicit query-domain partition for a section lens whose declared producers
    /// execute against more than one context type.
    /// </summary>
    /// <remarks>
    /// A partition plans only the section queries registered in its query catalog. The composing
    /// host remains responsible for proving that its partitions cover every declared section
    /// query exactly as intended.
    /// </remarks>
    public static InspectionLensCatalog<TContext, TModel> CreatePartition(
        SectionCatalog<TModel> sections,
        InspectionQueryCatalog<TContext> queryCatalog) =>
        new(sections, queryCatalog, isPartition: true);

    /// <summary>The immutable section lens.</summary>
    public SectionCatalog<TModel> Sections { get; }

    /// <summary>The immutable query domain bound to this lens.</summary>
    public InspectionQueryCatalog<TContext> QueryCatalog { get; }

    /// <summary>The frozen pipeline used for selection, effectiveness, and rendering.</summary>
    public SectionPipeline<TModel> Pipeline => Sections.Pipeline;

    /// <summary>
    /// Selects section demand and compiles it into an executable query plan for this domain.
    /// </summary>
    public InspectionQueryPlan<TContext> Plan(
        Verbosity verbosity,
        HashSet<string>? include = null,
        bool fixedOverview = false,
        bool excludeUnbounded = false) =>
        Plan(Sections.PlanQueries(
            verbosity,
            include,
            fixedOverview,
            excludeUnbounded));

    /// <summary>
    /// Compiles an existing section-demand plan into an executable query plan for this domain.
    /// </summary>
    public InspectionQueryPlan<TContext> Plan(SectionQueryPlan sectionPlan)
    {
        ArgumentNullException.ThrowIfNull(sectionPlan);

        ImmutableArray<InspectionQueryDefinition> requested = sectionPlan.Queries;
        if (!_isPartition)
            return QueryCatalog.Plan(requested);

        int ownedCount = 0;
        foreach (InspectionQueryDefinition query in requested)
        {
            if (QueryCatalog.Contains(query))
                ownedCount++;
        }

        if (ownedCount == requested.Length)
            return QueryCatalog.Plan(requested);
        if (ownedCount == 0)
            return QueryCatalog.Plan(ImmutableArray<InspectionQueryDefinition>.Empty);

        var owned =
            ImmutableArray.CreateBuilder<InspectionQueryDefinition>(ownedCount);
        foreach (InspectionQueryDefinition query in requested)
        {
            if (QueryCatalog.Contains(query))
                owned.Add(query);
        }

        return QueryCatalog.Plan(owned.MoveToImmutable());
    }
}

/// <summary>Composition checks for section lenses bound to multiple query domains.</summary>
public static class InspectionLensCatalog
{
    /// <summary>
    /// Verifies that the supplied query-domain partitions claim every section-declared query
    /// exactly once.
    /// </summary>
    public static void ValidatePartitions<TModel>(
        SectionCatalog<TModel> sections,
        params IInspectionQueryCatalog[] partitions)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(partitions);

        foreach (IInspectionQueryCatalog? partition in partitions)
            ArgumentNullException.ThrowIfNull(partition);

        foreach (InspectionQueryDefinition query in sections.DeclaredQueries)
        {
            int owners = 0;
            foreach (IInspectionQueryCatalog partition in partitions)
            {
                if (partition.Contains(query))
                    owners++;
            }

            if (owners == 0)
            {
                throw new InspectionQueryException(
                    $"Section query '{query.Name}' is not registered in any query-domain partition.");
            }
            if (owners > 1)
            {
                throw new InspectionQueryException(
                    $"Section query '{query.Name}' is registered in more than one query-domain partition.");
            }
        }
    }
}
