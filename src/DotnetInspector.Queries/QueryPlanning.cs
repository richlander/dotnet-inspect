using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>Declared execution budget for an inspection query.</summary>
public enum QueryCost
{
    NetworkFree,
    Moderated,
    Unbounded,
}

/// <summary>Host authorization required by an inspection query.</summary>
[Flags]
public enum QueryCapabilities
{
    None = 0,
    Network = 1 << 0,
    SourceContent = 1 << 1,
}

/// <summary>Maximum cost and capabilities authorized for one query plan execution.</summary>
public readonly record struct QueryExecutionPolicy(
    QueryCost MaximumCost,
    QueryCapabilities AllowedCapabilities)
{
    public static QueryExecutionPolicy NetworkFree { get; } =
        new(QueryCost.NetworkFree, QueryCapabilities.None);
}

/// <summary>A query-owned failure that remains distinct from a successful empty result.</summary>
public sealed record QueryFailure
{
    public QueryFailure(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        Code = code;
        Message = message;
    }

    public string Code { get; }

    public string Message { get; }
}

/// <summary>The typed outcome of one query.</summary>
public abstract record QueryResult<TResult> where TResult : notnull
{
    private QueryResult()
    {
    }

    public sealed record Success(TResult Value) : QueryResult<TResult>;

    public sealed record Failure(QueryFailure Error) : QueryResult<TResult>;

    public static QueryResult<TResult> Succeeded(TResult value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Success(value);
    }

    public static QueryResult<TResult> Failed(QueryFailure error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Failure(error);
    }
}

/// <summary>
/// Non-generic query identity used by section bindings and heterogeneous plans.
/// The generic derived type retains the context and result contract.
/// </summary>
public abstract class QueryDefinition
{
    protected QueryDefinition(
        string name,
        QueryCost cost,
        QueryCapabilities capabilities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Cost = cost;
        Capabilities = capabilities;
    }

    public string Name { get; }

    public QueryCost Cost { get; }

    public QueryCapabilities Capabilities { get; }
}

/// <summary>Typed query identity shared by heterogeneous results in one context.</summary>
public abstract class QueryDefinition<TContext> : QueryDefinition
{
    protected QueryDefinition(
        string name,
        QueryCost cost,
        QueryCapabilities capabilities)
        : base(name, cost, capabilities)
    {
    }
}

/// <summary>A typed query definition with a presentation-free executor.</summary>
public sealed class QueryDefinition<TContext, TResult> : QueryDefinition<TContext>
    where TResult : notnull
{
    private readonly Func<
        TContext,
        QueryResultSet<TContext>,
        CancellationToken,
        ValueTask<QueryResult<TResult>>> _execute;

    public QueryDefinition(
        string name,
        QueryCost cost,
        QueryCapabilities capabilities,
        Func<
            TContext,
            QueryResultSet<TContext>,
            CancellationToken,
            ValueTask<QueryResult<TResult>>> execute)
        : base(name, cost, capabilities)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
    }

    internal async ValueTask<object> ExecuteUntypedAsync(
        TContext context,
        QueryResultSet<TContext> results,
        CancellationToken cancellationToken)
        => await _execute(context, results, cancellationToken).ConfigureAwait(false);
}

/// <summary>Builds an immutable query catalog and its typed dependency graph.</summary>
public sealed class QueryCatalogBuilder<TContext>
{
    private readonly List<QueryRegistration<TContext>> _registrations = [];
    private readonly Dictionary<QueryDefinition<TContext>, QueryRegistration<TContext>> _byDefinition =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, QueryDefinition<TContext>> _byName =
        new(StringComparer.Ordinal);
    private bool _built;

    public QueryCatalogBuilder<TContext> Add<TResult>(
        QueryDefinition<TContext, TResult> query,
        params QueryDefinition<TContext>[] dependencies)
        where TResult : notnull
    {
        if (_built)
            throw new InvalidOperationException("The query catalog has already been built.");

        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(dependencies);

        if (_byDefinition.ContainsKey(query))
            throw new QueryPlanException(
                $"Query '{query.Name}' is already registered.");
        if (_byName.TryGetValue(query.Name, out var existing))
        {
            throw new QueryPlanException(
                $"Query name '{query.Name}' is already used by '{existing.Name}'.");
        }
        if (dependencies.Any(static dependency => dependency is null))
            throw new ArgumentException("Query dependencies cannot contain null.", nameof(dependencies));

        var registration = new QueryRegistration<TContext>(
            query,
            dependencies.ToImmutableArray(),
            query.ExecuteUntypedAsync);
        _registrations.Add(registration);
        _byDefinition.Add(query, registration);
        _byName.Add(query.Name, query);
        return this;
    }

    public QueryCatalog<TContext> Build()
    {
        if (_built)
            throw new InvalidOperationException("The query catalog has already been built.");

        _built = true;
        return new QueryCatalog<TContext>(_registrations, _byDefinition);
    }
}

/// <summary>An immutable set of query definitions and their dependencies.</summary>
public sealed class QueryCatalog<TContext>
{
    private readonly ImmutableArray<QueryRegistration<TContext>> _registrations;
    private readonly Dictionary<QueryDefinition<TContext>, QueryRegistration<TContext>> _byDefinition;

    internal QueryCatalog(
        IEnumerable<QueryRegistration<TContext>> registrations,
        IReadOnlyDictionary<QueryDefinition<TContext>, QueryRegistration<TContext>> byDefinition)
    {
        _registrations = registrations.ToImmutableArray();
        _byDefinition = new Dictionary<QueryDefinition<TContext>, QueryRegistration<TContext>>(
            byDefinition,
            ReferenceEqualityComparer.Instance);
    }

    public QueryPlan<TContext> Plan(params QueryDefinition<TContext>[] requested)
        => Plan((IEnumerable<QueryDefinition<TContext>>)requested);

    public QueryPlan<TContext> Plan(IEnumerable<QueryDefinition<TContext>> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);

        HashSet<QueryDefinition<TContext>> required = new(ReferenceEqualityComparer.Instance);
        foreach (var query in requested)
        {
            ArgumentNullException.ThrowIfNull(query);
            AddClosure(query, required);
        }

        Dictionary<QueryDefinition<TContext>, VisitState> states =
            new(ReferenceEqualityComparer.Instance);
        List<QueryRegistration<TContext>> ordered = [];
        foreach (var registration in _registrations)
        {
            if (required.Contains(registration.Definition))
                Visit(registration, required, states, ordered);
        }

        var maximumCost = QueryCost.NetworkFree;
        var capabilities = QueryCapabilities.None;
        foreach (var registration in ordered)
        {
            if (registration.Definition.Cost > maximumCost)
                maximumCost = registration.Definition.Cost;
            capabilities |= registration.Definition.Capabilities;
        }

        return new QueryPlan<TContext>(ordered, maximumCost, capabilities);
    }

    private void AddClosure(
        QueryDefinition<TContext> query,
        HashSet<QueryDefinition<TContext>> required)
    {
        var registration = RequireRegistration(query);
        if (!required.Add(query))
            return;

        foreach (var dependency in registration.Dependencies)
            AddClosure(dependency, required);
    }

    private void Visit(
        QueryRegistration<TContext> registration,
        HashSet<QueryDefinition<TContext>> required,
        Dictionary<QueryDefinition<TContext>, VisitState> states,
        List<QueryRegistration<TContext>> ordered)
    {
        if (states.TryGetValue(registration.Definition, out var state))
        {
            if (state == VisitState.Visiting)
            {
                throw new QueryPlanException(
                    $"Query dependency cycle detected at '{registration.Definition.Name}'.");
            }

            return;
        }

        states.Add(registration.Definition, VisitState.Visiting);
        foreach (var dependency in registration.Dependencies)
        {
            if (required.Contains(dependency))
                Visit(RequireRegistration(dependency), required, states, ordered);
        }

        states[registration.Definition] = VisitState.Complete;
        ordered.Add(registration);
    }

    private QueryRegistration<TContext> RequireRegistration(
        QueryDefinition<TContext> query)
    {
        if (_byDefinition.TryGetValue(query, out var registration))
            return registration;

        throw new QueryPlanException(
            $"Query '{query.Name}' is not registered in this catalog.");
    }

    private enum VisitState
    {
        Visiting,
        Complete,
    }
}

/// <summary>A deterministic, dependency-closed query execution plan.</summary>
public sealed class QueryPlan<TContext>
{
    private readonly ImmutableArray<QueryRegistration<TContext>> _registrations;

    internal QueryPlan(
        IEnumerable<QueryRegistration<TContext>> registrations,
        QueryCost maximumCost,
        QueryCapabilities requiredCapabilities)
    {
        _registrations = registrations.ToImmutableArray();
        Queries = _registrations
            .Select(static registration => registration.Definition)
            .ToImmutableArray();
        MaximumCost = maximumCost;
        RequiredCapabilities = requiredCapabilities;
    }

    public ImmutableArray<QueryDefinition<TContext>> Queries { get; }

    public QueryCost MaximumCost { get; }

    public QueryCapabilities RequiredCapabilities { get; }

    public async ValueTask<QueryResultSet<TContext>> ExecuteAsync(
        TContext context,
        QueryExecutionPolicy policy,
        CancellationToken cancellationToken = default)
    {
        Preflight(policy);

        QueryResultSet<TContext> results = new();
        foreach (var registration in _registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await registration.Execute(
                context,
                results,
                cancellationToken).ConfigureAwait(false);
            results.Add(registration.Definition, result);
        }

        return results;
    }

    private void Preflight(QueryExecutionPolicy policy)
    {
        if (MaximumCost > policy.MaximumCost)
        {
            throw new QueryPolicyException(
                $"The query plan requires cost '{MaximumCost}', but the host authorized " +
                $"only '{policy.MaximumCost}'.");
        }

        var denied = RequiredCapabilities & ~policy.AllowedCapabilities;
        if (denied != QueryCapabilities.None)
        {
            throw new QueryPolicyException(
                $"The query plan requires capabilities '{denied}' that the host did not authorize.");
        }
    }
}

/// <summary>Typed results produced by one query plan execution.</summary>
public sealed class QueryResultSet<TContext>
{
    private readonly Dictionary<QueryDefinition<TContext>, object> _results =
        new(ReferenceEqualityComparer.Instance);

    public bool Contains(QueryDefinition<TContext> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _results.ContainsKey(query);
    }

    public QueryResult<TResult> Get<TResult>(
        QueryDefinition<TContext, TResult> query)
        where TResult : notnull
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!_results.TryGetValue(query, out var result))
        {
            throw new InvalidOperationException(
                $"Query '{query.Name}' was not executed by this plan.");
        }

        return (QueryResult<TResult>)result;
    }

    public TResult RequireValue<TResult>(
        QueryDefinition<TContext, TResult> query)
        where TResult : notnull
        => Get(query) switch
        {
            QueryResult<TResult>.Success success => success.Value,
            QueryResult<TResult>.Failure failure => throw new QueryFailedException(
                query.Name,
                failure.Error),
            _ => throw new InvalidOperationException(
                $"Query '{query.Name}' returned an unknown result kind."),
        };

    internal void Add(QueryDefinition<TContext> query, object result)
    {
        if (!_results.TryAdd(query, result))
        {
            throw new InvalidOperationException(
                $"Query '{query.Name}' produced more than one result.");
        }
    }
}

public sealed class QueryPolicyException(string message) : InvalidOperationException(message);

public sealed class QueryPlanException(string message) : InvalidOperationException(message);

public sealed class QueryFailedException : InvalidOperationException
{
    public QueryFailedException(string queryName, QueryFailure failure)
        : base($"Query '{queryName}' failed ({failure.Code}): {failure.Message}")
    {
        QueryName = queryName;
        Failure = failure;
    }

    public string QueryName { get; }

    public QueryFailure Failure { get; }
}

internal sealed record QueryRegistration<TContext>(
    QueryDefinition<TContext> Definition,
    ImmutableArray<QueryDefinition<TContext>> Dependencies,
    Func<
        TContext,
        QueryResultSet<TContext>,
        CancellationToken,
        ValueTask<object>> Execute);
