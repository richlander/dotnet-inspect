using DotnetInspector.Options;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>
/// Typed L2-to-L1 bindings for one section pipeline and one query context/catalog.
/// </summary>
public sealed class SectionQueryBindings<TModel, TQueryContext>
{
    private readonly SectionPipeline<TModel> _pipeline;
    private readonly QueryCatalog<TQueryContext> _catalog;
    private readonly Dictionary<string, QueryDefinition<TQueryContext>> _queriesBySection =
        new(StringComparer.OrdinalIgnoreCase);

    public SectionQueryBindings(
        SectionPipeline<TModel> pipeline,
        QueryCatalog<TQueryContext> catalog)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(catalog);
        _pipeline = pipeline;
        _catalog = catalog;
    }

    public SectionQueryBindings<TModel, TQueryContext> Bind<TDescriptor>()
        where TDescriptor : IQuerySectionDescriptor<TModel, TQueryContext>
    {
        var sectionName = TDescriptor.Name;
        if (!_pipeline.SelectableSectionNames.Contains(sectionName))
        {
            throw new InvalidOperationException(
                $"Section '{sectionName}' must be registered before its query is bound.");
        }
        if (_queriesBySection.ContainsKey(sectionName))
        {
            throw new InvalidOperationException(
                $"Section '{sectionName}' already has a query binding.");
        }

        _catalog.Plan(TDescriptor.Query);
        _pipeline.RaiseSectionCost(sectionName, ToSectionCost(TDescriptor.Query.Cost));
        _queriesBySection.Add(sectionName, TDescriptor.Query);
        return this;
    }

    public IReadOnlySet<QueryDefinition<TQueryContext>> DeclaredQueries =>
        _queriesBySection.Values.ToHashSet(
            (IEqualityComparer<QueryDefinition<TQueryContext>>)
                ReferenceEqualityComparer.Instance);

    public HashSet<QueryDefinition<TQueryContext>> GetRequiredQueries(
        Verbosity verbosity,
        HashSet<string>? include = null,
        bool fixedOverview = false)
    {
        var requestedSections = _pipeline.GetCandidateSections(
            verbosity,
            include,
            fixedOverview);
        HashSet<QueryDefinition<TQueryContext>> queries =
            new(ReferenceEqualityComparer.Instance);
        foreach (var sectionName in requestedSections)
        {
            if (_queriesBySection.TryGetValue(sectionName, out var query))
                queries.Add(query);
        }

        return queries;
    }

    private static SectionCost ToSectionCost(QueryCost cost)
        => cost switch
        {
            QueryCost.NetworkFree => SectionCost.NetworkFree,
            QueryCost.Moderated => SectionCost.Moderated,
            QueryCost.Unbounded => SectionCost.Unbounded,
            _ => throw new InvalidOperationException(
                $"Unknown query cost '{cost}'."),
        };
}
