using System.Collections.Immutable;
using DotnetInspector.Options;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

/// <summary>A typed query requested by the host for an attributed reason.</summary>
public readonly record struct HostQueryDemand(
    string Reason,
    InspectionQueryDefinition Query);

/// <summary>
/// Binds immutable section lenses to one immutable typed-query domain.
/// </summary>
public sealed class CompiledInspectionDomain<TContext>
{
    private readonly HashSet<InspectionQueryDefinition> _registeredQueries;

    public CompiledInspectionDomain(
        InspectionQueryCatalog<TContext> queryCatalog)
    {
        QueryCatalog = queryCatalog
            ?? throw new ArgumentNullException(nameof(queryCatalog));
        _registeredQueries = [.. QueryCatalog.RegisteredQueries];
    }

    public InspectionQueryCatalog<TContext> QueryCatalog { get; }

    public CompiledInspectionLens<TContext, TModel> CompileLens<TModel>(
        Action<SectionPipeline<TModel>> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var pipeline = new SectionPipeline<TModel>()
            .UseCompiledQueryCosts(CostOf);
        configure(pipeline);
        return new CompiledInspectionLens<TContext, TModel>(
            this,
            pipeline.Compile());
    }

    private InspectionCost CostOf(InspectionQueryDefinition query)
    {
        if (!_registeredQueries.Contains(query))
        {
            throw new InspectionQueryException(
                $"Query '{query.Name}' is outside the compiled inspection domain.");
        }

        return QueryCatalog.CostOf(query);
    }
}

/// <summary>
/// One immutable section-selection lens over a compiled inspection domain.
/// </summary>
public sealed class CompiledInspectionLens<TContext, TModel>
{
    internal CompiledInspectionLens(
        CompiledInspectionDomain<TContext> domain,
        SectionCatalog<TModel> sections)
    {
        Domain = domain;
        Sections = sections;
    }

    public CompiledInspectionDomain<TContext> Domain { get; }

    public InspectionQueryCatalog<TContext> QueryCatalog =>
        Domain.QueryCatalog;

    public SectionCatalog<TModel> Sections { get; }

    public CompiledInspectionPlan<TContext> Plan(
        Verbosity verbosity,
        HashSet<string>? include = null,
        bool fixedOverview = false,
        bool excludeUnbounded = false,
        IReadOnlyList<HostQueryDemand>? hostDemand = null)
    {
        SectionQueryPlan sectionPlan = Sections.PlanQueries(
            verbosity,
            include,
            fixedOverview,
            excludeUnbounded);
        return CompilePlan(sectionPlan, hostDemand);
    }

    private CompiledInspectionPlan<TContext> CompilePlan(
        SectionQueryPlan sectionPlan,
        IReadOnlyList<HostQueryDemand>? hostDemand)
    {
        if (hostDemand is null || hostDemand.Count == 0)
        {
            return new CompiledInspectionPlan<TContext>(
                sectionPlan,
                [],
                sectionPlan.Queries,
                Lower(sectionPlan.Queries));
        }

        var demands = ImmutableArray.CreateBuilder<HostQueryDemand>(
            hostDemand.Count);
        var requested = ImmutableArray.CreateBuilder<InspectionQueryDefinition>(
            sectionPlan.Queries.Length + hostDemand.Count);
        HashSet<InspectionQueryDefinition> seen = [.. sectionPlan.Queries];
        requested.AddRange(sectionPlan.Queries);

        foreach (HostQueryDemand demand in hostDemand)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                demand.Reason,
                nameof(hostDemand));
            ArgumentNullException.ThrowIfNull(demand.Query);
            demands.Add(demand);
            if (seen.Add(demand.Query))
                requested.Add(demand.Query);
        }

        ImmutableArray<InspectionQueryDefinition> requestedQueries =
            requested.DrainToImmutable();
        return new CompiledInspectionPlan<TContext>(
            sectionPlan,
            demands.DrainToImmutable(),
            requestedQueries,
            Lower(requestedQueries));
    }

    private InspectionQueryPlan<TContext> Lower(
        ImmutableArray<InspectionQueryDefinition> requested) =>
        requested.Length switch
        {
            0 => QueryCatalog.Plan(
                Array.Empty<InspectionQueryDefinition>()),
            1 => QueryCatalog.Plan(requested[0]),
            _ => QueryCatalog.Plan(requested),
        };
}

/// <summary>
/// A reusable context-free plan combining section demand with an owner-issued
/// typed-query execution plan.
/// </summary>
public readonly struct CompiledInspectionPlan<TContext>
{
    private readonly SectionQueryPlan? _sectionPlan;
    private readonly ImmutableArray<HostQueryDemand> _hostDemand;
    private readonly ImmutableArray<InspectionQueryDefinition> _requestedQueries;
    private readonly InspectionQueryPlan<TContext>? _queryPlan;

    internal CompiledInspectionPlan(
        SectionQueryPlan sectionPlan,
        ImmutableArray<HostQueryDemand> hostDemand,
        ImmutableArray<InspectionQueryDefinition> requestedQueries,
        InspectionQueryPlan<TContext> queryPlan)
    {
        _sectionPlan = sectionPlan;
        _hostDemand = hostDemand;
        _requestedQueries = requestedQueries;
        _queryPlan = queryPlan;
    }

    public bool IsDefault => _queryPlan is null;

    public SectionQueryPlan SectionPlan =>
        _sectionPlan
        ?? throw new InvalidOperationException(
            "An uninitialized compiled inspection plan has no section plan.");

    public ImmutableArray<SectionQueryDemand> SectionDemand =>
        SectionPlan.Demands;

    public ImmutableArray<HostQueryDemand> HostDemand =>
        _hostDemand.IsDefault ? [] : _hostDemand;

    public ImmutableArray<InspectionQueryDefinition> RequestedQueries =>
        _requestedQueries.IsDefault ? [] : _requestedQueries;

    public InspectionQueryPlan<TContext> QueryPlan =>
        _queryPlan
        ?? throw new InvalidOperationException(
            "An uninitialized compiled inspection plan has no query plan.");

    public InspectionQueryResults Run(
        TContext context,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution = null)
        => QueryPlan.Run(context, recordExecution);

    public Task<InspectionQueryResults> RunAsync(
        TContext context,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution = null,
        CancellationToken cancellationToken = default)
        => QueryPlan.RunAsync(
            context,
            recordExecution,
            cancellationToken);
}
