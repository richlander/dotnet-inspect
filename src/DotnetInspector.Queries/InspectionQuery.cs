using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Queries;

/// <summary>
/// The identity, result contract, and cost of one typed inspection query. Identity is the
/// definition instance, not <see cref="Name"/>; a consumer supplies execution context separately.
/// </summary>
public abstract class InspectionQueryDefinition
{
    private protected InspectionQueryDefinition(string name, InspectionCost cost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Cost = cost;
    }

    /// <summary>A stable diagnostic name. It is never used for lookup or binding.</summary>
    public string Name { get; }

    /// <summary>The acquisition cost declared by the query.</summary>
    public InspectionCost Cost { get; }
}

/// <summary>
/// A typed inspection query definition that produces <typeparamref name="TResult"/>.
/// </summary>
public sealed class InspectionQuery<TResult> : InspectionQueryDefinition
{
    public InspectionQuery(string name, InspectionCost cost)
        : base(name, cost)
    {
    }
}

/// <summary>
/// Results produced by an <see cref="InspectionQueryRegistry{TContext}"/>.
/// </summary>
public sealed class InspectionQueryResults
{
    private readonly Dictionary<InspectionQueryDefinition, object?> _values = [];

    internal void Set<TResult>(InspectionQuery<TResult> query, TResult result)
        => _values.Add(query, result);

    /// <summary>Gets the result produced for <paramref name="query"/>.</summary>
    public TResult Get<TResult>(InspectionQuery<TResult> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!_values.TryGetValue(query, out object? value))
            throw new InvalidOperationException($"Query '{query.Name}' did not produce a result.");

        return (TResult)value!;
    }

    /// <summary>Attempts to get the result produced for <paramref name="query"/>.</summary>
    public bool TryGet<TResult>(
        InspectionQuery<TResult> query,
        [MaybeNullWhen(false)] out TResult result)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (_values.TryGetValue(query, out object? value))
        {
            result = (TResult)value!;
            return true;
        }

        result = default;
        return false;
    }
}

/// <summary>
/// Executes heterogeneous typed queries without reducing their identity to string keys.
/// </summary>
public sealed class InspectionQueryRegistry<TContext>
{
    private readonly Dictionary<InspectionQueryDefinition, Registration> _registrations = [];

    /// <summary>Every registered query, in registration order.</summary>
    public IReadOnlyCollection<InspectionQueryDefinition> RegisteredQueries => _registrations.Keys;

    /// <summary>Registers a typed query and its context adapter.</summary>
    public InspectionQueryRegistry<TContext> Add<TResult>(
        InspectionQuery<TResult> query,
        Func<TContext, InspectionQueryResults, TResult> execute,
        params InspectionQueryDefinition[] requires)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(requires);

        if (_registrations.ContainsKey(query))
            throw new InvalidOperationException($"Query '{query.Name}' is already registered.");

        _registrations.Add(
            query,
            new Registration<TResult>(query, execute, [.. requires]));
        return this;
    }

    /// <summary>Registers a query that does not consume prerequisite results.</summary>
    public InspectionQueryRegistry<TContext> Add<TResult>(
        InspectionQuery<TResult> query,
        Func<TContext, TResult> execute,
        params InspectionQueryDefinition[] requires)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return Add(query, (context, _) => execute(context), requires);
    }

    /// <summary>The queries directly required by <paramref name="query"/>.</summary>
    public ImmutableArray<InspectionQueryDefinition> RequirementsOf(InspectionQueryDefinition query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _registrations.TryGetValue(query, out Registration? registration)
            ? registration.Requires
            : [];
    }

    /// <summary>
    /// Returns the maximum cost over <paramref name="query"/>'s prerequisite closure.
    /// </summary>
    public InspectionCost CostOf(InspectionQueryDefinition query)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureRegistered(query);

        InspectionCost cost = InspectionCost.NetworkFree;
        foreach (InspectionQueryDefinition member in ExpandRequired([query]))
        {
            if (member.Cost > cost)
                cost = member.Cost;
        }

        return cost;
    }

    /// <summary>Expands queries to include every transitively required query.</summary>
    public HashSet<InspectionQueryDefinition> ExpandRequired(
        IEnumerable<InspectionQueryDefinition> requested)
    {
        ArgumentNullException.ThrowIfNull(requested);

        HashSet<InspectionQueryDefinition> closure = [];
        HashSet<InspectionQueryDefinition> visiting = [];
        foreach (InspectionQueryDefinition query in requested)
            AddWithRequirements(query, closure, visiting);
        return closure;
    }

    /// <summary>
    /// Executes the requested queries and their prerequisites once, in deterministic order.
    /// </summary>
    public InspectionQueryResults Run(
        IEnumerable<InspectionQueryDefinition> requested,
        TContext context,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution = null)
    {
        HashSet<InspectionQueryDefinition> required = ExpandRequired(requested);
        HashSet<InspectionQueryDefinition> ran = [];
        var results = new InspectionQueryResults();

        foreach (InspectionQueryDefinition query in _registrations.Keys)
        {
            if (required.Contains(query))
                RunWithRequirements(query, context, results, ran, recordExecution);
        }

        return results;
    }

    private void AddWithRequirements(
        InspectionQueryDefinition query,
        HashSet<InspectionQueryDefinition> closure,
        HashSet<InspectionQueryDefinition> visiting)
    {
        EnsureRegistered(query);
        if (!visiting.Add(query))
            throw new InvalidOperationException(
                $"Inspection query prerequisite cycle detected at '{query.Name}'.");

        if (closure.Add(query))
        {
            foreach (InspectionQueryDefinition required in _registrations[query].Requires)
                AddWithRequirements(required, closure, visiting);
        }

        visiting.Remove(query);
    }

    private void RunWithRequirements(
        InspectionQueryDefinition query,
        TContext context,
        InspectionQueryResults results,
        HashSet<InspectionQueryDefinition> ran,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution)
    {
        if (ran.Contains(query))
            return;

        Registration registration = _registrations[query];
        foreach (InspectionQueryDefinition required in registration.Requires)
            RunWithRequirements(required, context, results, ran, recordExecution);

        ran.Add(query);
        long start = Stopwatch.GetTimestamp();
        try
        {
            registration.Execute(context, results);
        }
        finally
        {
            recordExecution?.Invoke(query, Stopwatch.GetElapsedTime(start));
        }
    }

    private void EnsureRegistered(InspectionQueryDefinition query)
    {
        if (!_registrations.ContainsKey(query))
            throw new InvalidOperationException($"Query '{query.Name}' is not registered.");
    }

    private abstract class Registration(
        InspectionQueryDefinition query,
        ImmutableArray<InspectionQueryDefinition> requires)
    {
        public InspectionQueryDefinition Query { get; } = query;
        public ImmutableArray<InspectionQueryDefinition> Requires { get; } = requires;
        public abstract void Execute(TContext context, InspectionQueryResults results);
    }

    private sealed class Registration<TResult>(
        InspectionQuery<TResult> query,
        Func<TContext, InspectionQueryResults, TResult> execute,
        ImmutableArray<InspectionQueryDefinition> requires)
        : Registration(query, requires)
    {
        public override void Execute(TContext context, InspectionQueryResults results)
            => results.Set(query, execute(context, results));
    }
}
