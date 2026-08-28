using System.Collections.Immutable;
using System.Diagnostics;

namespace DotnetInspector.Queries;

/// <summary>Non-generic structural view of an immutable query catalog.</summary>
public interface IInspectionQueryCatalog
{
    /// <summary>Every registered query, in stable registration order.</summary>
    ImmutableArray<InspectionQueryDefinition> RegisteredQueries { get; }

    /// <summary>Whether this catalog contains <paramref name="query"/>.</summary>
    bool Contains(InspectionQueryDefinition query);
}

/// <summary>
/// An immutable fixed-domain catalog of typed inspection queries.
/// </summary>
/// <remarks>
/// Catalog construction validates the complete required-dependency graph and precomputes each
/// query's closure, cost, and single-query execution plan. Hosts with a fixed query domain can
/// retain one catalog for the process lifetime; per-run state remains in the supplied context
/// and returned <see cref="InspectionQueryResults"/>.
/// </remarks>
public sealed class InspectionQueryCatalog<TContext> : IInspectionQueryCatalog
{
    private readonly ImmutableArray<InspectionQueryRegistry<TContext>.Registration>
        _registrations;
    private readonly Dictionary<InspectionQueryDefinition, int> _indexes;
    private readonly ImmutableArray<QueryMetadata> _metadata;
    private readonly ImmutableArray<InspectionQueryPlan<TContext>> _singleQueryPlans;
    private readonly InspectionQueryPlan<TContext> _emptyPlan;
    private readonly Func<
        TContext,
        InspectionQueryDefinition,
        InspectionCost,
        IDisposable>? _enterExecutionScope;

    internal InspectionQueryCatalog(
        IEnumerable<InspectionQueryRegistry<TContext>.Registration> registrations,
        Func<
            TContext,
            InspectionQueryDefinition,
            InspectionCost,
            IDisposable>? enterExecutionScope)
    {
        _registrations = [.. registrations];
        _enterExecutionScope = enterExecutionScope;
        _indexes = new Dictionary<InspectionQueryDefinition, int>(_registrations.Length);

        var registeredQueries =
            ImmutableArray.CreateBuilder<InspectionQueryDefinition>(_registrations.Length);
        for (int index = 0; index < _registrations.Length; index++)
        {
            var registration = _registrations[index];
            if (!_indexes.TryAdd(registration.Query, index))
            {
                throw new InspectionQueryException(
                    $"Query '{registration.Query.Name}' is already registered.");
            }
            registeredQueries.Add(registration.Query);
        }
        RegisteredQueries = registeredQueries.MoveToImmutable();

        var metadata =
            ImmutableArray.CreateBuilder<QueryMetadata>(_registrations.Length);
        for (int index = 0; index < _registrations.Length; index++)
        {
            bool[] closure = new bool[_registrations.Length];
            byte[] state = new byte[_registrations.Length];
            AddRequiredClosure(index, closure, state);

            InspectionCost cost = InspectionCost.NetworkFree;
            for (int member = 0; member < closure.Length; member++)
            {
                if (closure[member] && _registrations[member].Query.Cost > cost)
                    cost = _registrations[member].Query.Cost;
            }

            metadata.Add(new QueryMetadata(closure, cost));
        }
        _metadata = metadata.MoveToImmutable();

        _emptyPlan = new InspectionQueryPlan<TContext>(this, []);
        var plans =
            ImmutableArray.CreateBuilder<InspectionQueryPlan<TContext>>(
                _registrations.Length);
        for (int index = 0; index < _registrations.Length; index++)
            plans.Add(CompilePlan((bool[])_metadata[index].RequiredClosure.Clone()));
        _singleQueryPlans = plans.MoveToImmutable();
    }

    /// <summary>Every registered query, in stable registration order.</summary>
    public ImmutableArray<InspectionQueryDefinition> RegisteredQueries { get; }

    /// <summary>The queries directly required by <paramref name="query"/>.</summary>
    public ImmutableArray<InspectionQueryDefinition> RequirementsOf(
        InspectionQueryDefinition query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return RegistrationOf(query).Requires;
    }

    /// <summary>
    /// Queries whose results this query may consume when independently requested.
    /// </summary>
    public ImmutableArray<InspectionQueryDefinition> OptionalDependenciesOf(
        InspectionQueryDefinition query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return RegistrationOf(query).Optional;
    }

    /// <summary>
    /// Returns the maximum cost over <paramref name="query"/>'s required closure.
    /// </summary>
    public InspectionCost CostOf(InspectionQueryDefinition query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _metadata[IndexOf(query)].Cost;
    }

    /// <summary>
    /// Creates a mutable builder initialized from this catalog. The returned builder and catalogs
    /// subsequently compiled from it do not mutate this instance.
    /// </summary>
    public InspectionQueryRegistry<TContext> ToBuilder()
        => new(_registrations, _enterExecutionScope);

    /// <summary>
    /// Returns the precomputed execution plan for one query and its required closure.
    /// Repeated calls return the same plan instance.
    /// </summary>
    public InspectionQueryPlan<TContext> Plan(InspectionQueryDefinition query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _singleQueryPlans[IndexOf(query)];
    }

    /// <summary>
    /// Returns a deterministic plan for the requested queries and their required closures.
    /// Single-query requests use the precomputed plan; arbitrary combinations are compiled
    /// explicitly.
    /// </summary>
    public InspectionQueryPlan<TContext> Plan(
        ImmutableArray<InspectionQueryDefinition> requested)
    {
        if (requested.IsDefaultOrEmpty)
            return _emptyPlan;
        if (requested.Length == 1)
            return Plan(requested[0]);

        return CompileRequested(requested);
    }

    /// <summary>
    /// Returns a deterministic plan for the requested queries and their required closures.
    /// Single-query requests use the precomputed plan; arbitrary combinations are compiled
    /// explicitly.
    /// </summary>
    public InspectionQueryPlan<TContext> Plan(
        IEnumerable<InspectionQueryDefinition> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);

        if (requested is InspectionQueryDefinition[] { Length: 0 })
            return _emptyPlan;
        if (requested is InspectionQueryDefinition[] { Length: 1 } single)
            return Plan(single[0]);
        if (requested is ImmutableArray<InspectionQueryDefinition> immutable)
            return Plan(immutable);

        return CompileRequested(requested);
    }

    private InspectionQueryPlan<TContext> CompileRequested(
        IEnumerable<InspectionQueryDefinition> requested)
    {
        bool[] active = new bool[_registrations.Length];
        int onlyRequested = -1;
        bool hasMultipleRequested = false;
        foreach (InspectionQueryDefinition query in requested)
        {
            ArgumentNullException.ThrowIfNull(query);
            int index = IndexOf(query);
            if (onlyRequested < 0)
                onlyRequested = index;
            else if (onlyRequested != index)
                hasMultipleRequested = true;

            Union(active, _metadata[index].RequiredClosure);
        }

        if (onlyRequested < 0)
            return _emptyPlan;
        if (!hasMultipleRequested)
            return _singleQueryPlans[onlyRequested];
        return CompilePlan(active);
    }

    /// <summary>Whether this catalog contains <paramref name="query"/>.</summary>
    public bool Contains(InspectionQueryDefinition query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _indexes.ContainsKey(query);
    }

    /// <summary>Expands queries to include every transitively required query.</summary>
    public HashSet<InspectionQueryDefinition> ExpandRequired(
        IEnumerable<InspectionQueryDefinition> requested)
        => [.. Plan(requested).Queries];

    /// <summary>Executes a request through its immutable plan.</summary>
    public InspectionQueryResults Run(
        IEnumerable<InspectionQueryDefinition> requested,
        TContext context,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution = null)
        => Plan(requested).Run(context, recordExecution);

    /// <summary>Executes a request through its immutable asynchronous plan.</summary>
    public Task<InspectionQueryResults> RunAsync(
        IEnumerable<InspectionQueryDefinition> requested,
        TContext context,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution = null,
        CancellationToken cancellationToken = default)
        => Plan(requested).RunAsync(
            context,
            recordExecution,
            cancellationToken);

    internal InspectionQueryResults Execute(
        ImmutableArray<InspectionQueryPlan<TContext>.Entry> entries,
        TContext context,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution)
    {
        var results = new InspectionQueryResults();
        foreach (InspectionQueryPlan<TContext>.Entry entry in entries)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                using IDisposable? scope = _enterExecutionScope?.Invoke(
                    context,
                    entry.Registration.Query,
                    entry.Cost);
                entry.Registration.Execute(
                    context,
                    results,
                    results.RestrictTo(entry.AccessibleDependencies));
            }
            finally
            {
                recordExecution?.Invoke(
                    entry.Registration.Query,
                    Stopwatch.GetElapsedTime(start));
            }
        }
        return results;
    }

    internal async Task<InspectionQueryResults> ExecuteAsync(
        ImmutableArray<InspectionQueryPlan<TContext>.Entry> entries,
        TContext context,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var results = new InspectionQueryResults();
        foreach (InspectionQueryPlan<TContext>.Entry entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long start = Stopwatch.GetTimestamp();
            try
            {
                using IDisposable? scope = _enterExecutionScope?.Invoke(
                    context,
                    entry.Registration.Query,
                    entry.Cost);
                await entry.Registration.ExecuteAsync(
                    context,
                    results,
                    results.RestrictTo(entry.AccessibleDependencies),
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                recordExecution?.Invoke(
                    entry.Registration.Query,
                    Stopwatch.GetElapsedTime(start));
            }
        }
        return results;
    }

    private InspectionQueryPlan<TContext> CompilePlan(bool[] active)
    {
        byte[] state = new byte[_registrations.Length];
        var order =
            ImmutableArray.CreateBuilder<int>(_registrations.Length);
        for (int index = 0; index < _registrations.Length; index++)
        {
            if (active[index])
                AddExecutionOrder(index, active, state, order);
        }

        var entries =
            ImmutableArray.CreateBuilder<InspectionQueryPlan<TContext>.Entry>(
                order.Count);
        foreach (int index in order)
        {
            var registration = _registrations[index];
            var accessible =
                ImmutableHashSet.CreateBuilder<InspectionQueryDefinition>();
            foreach (InspectionQueryDefinition required in registration.Requires)
            {
                int requiredIndex = IndexOf(required);
                AddDefinitions(
                    accessible,
                    _metadata[requiredIndex].RequiredClosure);
            }
            foreach (InspectionQueryDefinition optional in registration.Optional)
            {
                int optionalIndex = IndexOf(optional);
                accessible.Add(optional);
                if (active[optionalIndex])
                {
                    AddDefinitions(
                        accessible,
                        _metadata[optionalIndex].RequiredClosure);
                }
            }

            entries.Add(
                new InspectionQueryPlan<TContext>.Entry(
                    registration,
                    accessible.ToImmutable(),
                    _metadata[index].Cost));
        }
        return new InspectionQueryPlan<TContext>(
            this,
            entries.MoveToImmutable());
    }

    private void AddRequiredClosure(
        int index,
        bool[] closure,
        byte[] state)
    {
        if (state[index] == 1)
        {
            throw new InspectionQueryException(
                $"Inspection query prerequisite cycle detected at "
                    + $"'{_registrations[index].Query.Name}'.");
        }
        if (state[index] == 2)
            return;

        state[index] = 1;
        var registration = _registrations[index];
        foreach (InspectionQueryDefinition optional in registration.Optional)
            _ = IndexOf(optional);
        foreach (InspectionQueryDefinition required in registration.Requires)
            AddRequiredClosure(IndexOf(required), closure, state);
        closure[index] = true;
        state[index] = 2;
    }

    private void AddExecutionOrder(
        int index,
        bool[] active,
        byte[] state,
        ImmutableArray<int>.Builder order)
    {
        if (state[index] == 1)
        {
            throw new InspectionQueryException(
                $"Inspection query active dependency cycle detected at "
                    + $"'{_registrations[index].Query.Name}'.");
        }
        if (state[index] == 2)
            return;

        state[index] = 1;
        var registration = _registrations[index];
        foreach (InspectionQueryDefinition required in registration.Requires)
            AddExecutionOrder(IndexOf(required), active, state, order);
        foreach (InspectionQueryDefinition optional in registration.Optional)
        {
            int optionalIndex = IndexOf(optional);
            if (active[optionalIndex])
                AddExecutionOrder(optionalIndex, active, state, order);
        }
        state[index] = 2;
        order.Add(index);
    }

    private void AddDefinitions(
        ImmutableHashSet<InspectionQueryDefinition>.Builder destination,
        bool[] members)
    {
        for (int index = 0; index < members.Length; index++)
        {
            if (members[index])
                destination.Add(_registrations[index].Query);
        }
    }

    private static void Union(bool[] destination, bool[] source)
    {
        for (int index = 0; index < destination.Length; index++)
            destination[index] |= source[index];
    }

    private InspectionQueryRegistry<TContext>.Registration RegistrationOf(
        InspectionQueryDefinition query)
        => _registrations[IndexOf(query)];

    private int IndexOf(InspectionQueryDefinition query)
        => _indexes.TryGetValue(query, out int index)
            ? index
            : throw new InspectionQueryException(
                $"Query '{query.Name}' is not registered.");

    private sealed record QueryMetadata(
        bool[] RequiredClosure,
        InspectionCost Cost);
}

/// <summary>
/// A dependency-ordered immutable execution plan over one fixed query catalog.
/// </summary>
public sealed class InspectionQueryPlan<TContext>
{
    private readonly InspectionQueryCatalog<TContext> _catalog;
    private readonly ImmutableArray<Entry> _entries;

    internal InspectionQueryPlan(
        InspectionQueryCatalog<TContext> catalog,
        ImmutableArray<Entry> entries)
    {
        _catalog = catalog;
        _entries = entries;
        Queries = [.. entries.Select(static entry => entry.Registration.Query)];

        InspectionCost cost = InspectionCost.NetworkFree;
        foreach (Entry entry in entries)
        {
            if (entry.Cost > cost)
                cost = entry.Cost;
        }
        Cost = cost;
    }

    /// <summary>Queries in deterministic execution order.</summary>
    public ImmutableArray<InspectionQueryDefinition> Queries { get; }

    /// <summary>The maximum transitive cost of all queries in the plan.</summary>
    public InspectionCost Cost { get; }

    /// <summary>Executes this plan synchronously.</summary>
    public InspectionQueryResults Run(
        TContext context,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution = null)
        => _catalog.Execute(_entries, context, recordExecution);

    /// <summary>Executes this plan asynchronously.</summary>
    public Task<InspectionQueryResults> RunAsync(
        TContext context,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution = null,
        CancellationToken cancellationToken = default)
        => _catalog.ExecuteAsync(
            _entries,
            context,
            recordExecution,
            cancellationToken);

    internal sealed record Entry(
        InspectionQueryRegistry<TContext>.Registration Registration,
        IReadOnlySet<InspectionQueryDefinition> AccessibleDependencies,
        InspectionCost Cost);
}
