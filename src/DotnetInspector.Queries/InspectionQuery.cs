using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace DotnetInspector.Queries;

/// <summary>
/// A typed query could not be planned or executed. This is a query contract failure, not an
/// inspected-artifact failure.
/// </summary>
public sealed class InspectionQueryException : InvalidOperationException
{
    public InspectionQueryException(string message)
        : base(message)
    {
    }

    public InspectionQueryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

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
/// Results produced by an <see cref="InspectionQueryRegistry{TContext}"/>. The completed result
/// set exposes every produced query; the view passed to an executor exposes only that query's
/// declared transitive prerequisites.
/// </summary>
public sealed class InspectionQueryResults
{
    private readonly Dictionary<InspectionQueryDefinition, object?> _values;
    private readonly IReadOnlySet<InspectionQueryDefinition>? _accessible;

    internal InspectionQueryResults()
    {
        _values = [];
    }

    private InspectionQueryResults(
        Dictionary<InspectionQueryDefinition, object?> values,
        IReadOnlySet<InspectionQueryDefinition> accessible)
    {
        _values = values;
        _accessible = accessible;
    }

    internal void Set<TResult>(InspectionQuery<TResult> query, TResult result)
        => _values.Add(query, result);

    internal InspectionQueryResults RestrictTo(
        IReadOnlySet<InspectionQueryDefinition> accessible)
        => new(_values, accessible);

    /// <summary>Gets the result produced for <paramref name="query"/>.</summary>
    public TResult Get<TResult>(InspectionQuery<TResult> query)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureAccessible(query);
        if (!_values.TryGetValue(query, out object? value))
            throw new InspectionQueryException($"Query '{query.Name}' did not produce a result.");

        return (TResult)value!;
    }

    /// <summary>Attempts to get the result produced for <paramref name="query"/>.</summary>
    public bool TryGet<TResult>(
        InspectionQuery<TResult> query,
        [MaybeNullWhen(false)] out TResult result)
    {
        ArgumentNullException.ThrowIfNull(query);
        EnsureAccessible(query);
        if (_values.TryGetValue(query, out object? value))
        {
            result = (TResult)value!;
            return true;
        }

        result = default;
        return false;
    }

    private void EnsureAccessible(InspectionQueryDefinition query)
    {
        if (_accessible is not null && !_accessible.Contains(query))
        {
            throw new InspectionQueryException(
                $"Query '{query.Name}' is not a declared prerequisite of the query being executed.");
        }
    }
}

/// <summary>
/// Executes heterogeneous typed queries without reducing their identity to string keys.
/// </summary>
public sealed class InspectionQueryRegistry<TContext>
{
    private readonly Dictionary<InspectionQueryDefinition, Registration> _registrations = [];
    private InspectionQueryCatalog<TContext>? _compiled;
    private readonly Func<
        TContext,
        InspectionQueryDefinition,
        InspectionCost,
        IDisposable>? _enterExecutionScope;

    /// <summary>
    /// Creates a query registry.
    /// </summary>
    /// <param name="enterExecutionScope">
    /// Optional host callback invoked immediately around each query executor with that query's
    /// maximum transitive cost. Hosts can use it to enforce resource acquisition policy without
    /// coupling this query layer to host-specific resource types.
    /// </param>
    public InspectionQueryRegistry(
        Func<TContext, InspectionQueryDefinition, InspectionCost, IDisposable>?
            enterExecutionScope = null)
    {
        _enterExecutionScope = enterExecutionScope;
    }

    internal InspectionQueryRegistry(
        IEnumerable<Registration> registrations,
        Func<TContext, InspectionQueryDefinition, InspectionCost, IDisposable>?
            enterExecutionScope)
        : this(enterExecutionScope)
    {
        foreach (Registration registration in registrations)
            _registrations.Add(registration.Query, registration);
    }

    /// <summary>Every registered query, in registration order.</summary>
    public IReadOnlyCollection<InspectionQueryDefinition> RegisteredQueries => _registrations.Keys;

    /// <summary>Registers a typed query and its context adapter.</summary>
    public InspectionQueryRegistry<TContext> Add<TResult>(
        InspectionQuery<TResult> query,
        Func<TContext, InspectionQueryResults, TResult> execute,
        params InspectionQueryDefinition[] requires)
        => AddCore(query, execute, requires, []);

    /// <summary>
    /// Registers a typed query that may consume results independently demanded by the caller.
    /// Optional dependencies run before the consumer when present, but do not enter its
    /// prerequisite closure or cost.
    /// </summary>
    public InspectionQueryRegistry<TContext> AddWithOptional<TResult>(
        InspectionQuery<TResult> query,
        Func<TContext, InspectionQueryResults, TResult> execute,
        IReadOnlyList<InspectionQueryDefinition> optional,
        params InspectionQueryDefinition[] requires)
        => AddCore(query, execute, requires, optional);

    private InspectionQueryRegistry<TContext> AddCore<TResult>(
        InspectionQuery<TResult> query,
        Func<TContext, InspectionQueryResults, TResult> execute,
        IReadOnlyList<InspectionQueryDefinition> requires,
        IReadOnlyList<InspectionQueryDefinition> optional)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(requires);
        ArgumentNullException.ThrowIfNull(optional);

        if (_registrations.ContainsKey(query))
            throw new InspectionQueryException($"Query '{query.Name}' is already registered.");

        ImmutableArray<InspectionQueryDefinition> required = [.. requires];
        ImmutableArray<InspectionQueryDefinition> optionalDependencies = [.. optional];
        ValidateDependencies(query, required, optionalDependencies);
        _registrations.Add(
            query,
            new Registration<TResult>(
                query,
                execute,
                required,
                optionalDependencies));
        _compiled = null;
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

    /// <summary>Registers an asynchronous typed query and its context adapter.</summary>
    public InspectionQueryRegistry<TContext> AddAsync<TResult>(
        InspectionQuery<TResult> query,
        Func<
            TContext,
            InspectionQueryResults,
            CancellationToken,
            ValueTask<TResult>> execute,
        params InspectionQueryDefinition[] requires)
        => AddAsyncCore(query, execute, requires, []);

    /// <summary>
    /// Registers an asynchronous typed query that may consume results independently demanded by
    /// the caller.
    /// </summary>
    public InspectionQueryRegistry<TContext> AddAsyncWithOptional<TResult>(
        InspectionQuery<TResult> query,
        Func<
            TContext,
            InspectionQueryResults,
            CancellationToken,
            ValueTask<TResult>> execute,
        IReadOnlyList<InspectionQueryDefinition> optional,
        params InspectionQueryDefinition[] requires)
        => AddAsyncCore(query, execute, requires, optional);

    private InspectionQueryRegistry<TContext> AddAsyncCore<TResult>(
        InspectionQuery<TResult> query,
        Func<
            TContext,
            InspectionQueryResults,
            CancellationToken,
            ValueTask<TResult>> execute,
        IReadOnlyList<InspectionQueryDefinition> requires,
        IReadOnlyList<InspectionQueryDefinition> optional)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(requires);
        ArgumentNullException.ThrowIfNull(optional);

        if (_registrations.ContainsKey(query))
            throw new InspectionQueryException($"Query '{query.Name}' is already registered.");

        ImmutableArray<InspectionQueryDefinition> required = [.. requires];
        ImmutableArray<InspectionQueryDefinition> optionalDependencies = [.. optional];
        ValidateDependencies(query, required, optionalDependencies);
        _registrations.Add(
            query,
            new AsyncRegistration<TResult>(
                query,
                execute,
                required,
                optionalDependencies));
        _compiled = null;
        return this;
    }

    private static void ValidateDependencies(
        InspectionQueryDefinition query,
        ImmutableArray<InspectionQueryDefinition> requires,
        ImmutableArray<InspectionQueryDefinition> optional)
    {
        if (requires.Any(static dependency => dependency is null)
            || optional.Any(static dependency => dependency is null))
        {
            throw new ArgumentException(
                $"Query '{query.Name}' has a null dependency.");
        }
        if (requires.Length != requires.Distinct().Count()
            || optional.Length != optional.Distinct().Count())
        {
            throw new InspectionQueryException(
                $"Query '{query.Name}' declares the same dependency more than once.");
        }
        if (requires.Intersect(optional).Any())
        {
            throw new InspectionQueryException(
                $"Query '{query.Name}' declares one dependency as both required and optional.");
        }
    }

    /// <summary>
    /// Registers an asynchronous query that does not consume prerequisite results.
    /// </summary>
    public InspectionQueryRegistry<TContext> AddAsync<TResult>(
        InspectionQuery<TResult> query,
        Func<TContext, CancellationToken, ValueTask<TResult>> execute,
        params InspectionQueryDefinition[] requires)
    {
        ArgumentNullException.ThrowIfNull(execute);
        return AddAsync(
            query,
            (context, _, cancellationToken) => execute(context, cancellationToken),
            requires);
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
    /// Queries whose results this query may consume when they were independently requested.
    /// Optional dependencies do not enter prerequisite closure or cost.
    /// </summary>
    public ImmutableArray<InspectionQueryDefinition> OptionalDependenciesOf(
        InspectionQueryDefinition query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return _registrations.TryGetValue(query, out Registration? registration)
            ? registration.Optional
            : [];
    }

    /// <summary>
    /// Returns the maximum cost over <paramref name="query"/>'s prerequisite closure.
    /// </summary>
    public InspectionCost CostOf(InspectionQueryDefinition query)
        => Compile().CostOf(query);

    /// <summary>
    /// Compiles the current registrations into an immutable reusable catalog. Repeated calls
    /// return the same catalog until another query is registered.
    /// </summary>
    public InspectionQueryCatalog<TContext> Compile()
        => _compiled ??= new InspectionQueryCatalog<TContext>(
            _registrations.Values,
            _enterExecutionScope);

    /// <summary>Expands queries to include every transitively required query.</summary>
    public HashSet<InspectionQueryDefinition> ExpandRequired(
        IEnumerable<InspectionQueryDefinition> requested)
        => Compile().ExpandRequired(requested);

    /// <summary>
    /// Executes the requested queries and their prerequisites once, in deterministic order.
    /// </summary>
    public InspectionQueryResults Run(
        IEnumerable<InspectionQueryDefinition> requested,
        TContext context,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution = null)
        => Compile().Run(requested, context, recordExecution);

    /// <summary>
    /// Executes synchronous and asynchronous queries and their prerequisites once, in
    /// deterministic order.
    /// </summary>
    public async Task<InspectionQueryResults> RunAsync(
        IEnumerable<InspectionQueryDefinition> requested,
        TContext context,
        Action<InspectionQueryDefinition, TimeSpan>? recordExecution = null,
        CancellationToken cancellationToken = default)
        => await Compile().RunAsync(
            requested,
            context,
            recordExecution,
            cancellationToken).ConfigureAwait(false);

    internal abstract class Registration(
        InspectionQueryDefinition query,
        ImmutableArray<InspectionQueryDefinition> requires,
        ImmutableArray<InspectionQueryDefinition> optional)
    {
        public InspectionQueryDefinition Query { get; } = query;
        public ImmutableArray<InspectionQueryDefinition> Requires { get; } = requires;
        public ImmutableArray<InspectionQueryDefinition> Optional { get; } = optional;
        internal abstract void Execute(
            TContext context,
            InspectionQueryResults results,
            InspectionQueryResults prerequisiteResults);
        internal abstract ValueTask ExecuteAsync(
            TContext context,
            InspectionQueryResults results,
            InspectionQueryResults prerequisiteResults,
            CancellationToken cancellationToken);
    }

    private sealed class Registration<TResult>(
        InspectionQuery<TResult> query,
        Func<TContext, InspectionQueryResults, TResult> execute,
        ImmutableArray<InspectionQueryDefinition> requires,
        ImmutableArray<InspectionQueryDefinition> optional)
        : Registration(query, requires, optional)
    {
        internal override void Execute(
            TContext context,
            InspectionQueryResults results,
            InspectionQueryResults prerequisiteResults)
            => results.Set(query, execute(context, prerequisiteResults));

        internal override ValueTask ExecuteAsync(
            TContext context,
            InspectionQueryResults results,
            InspectionQueryResults prerequisiteResults,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Execute(context, results, prerequisiteResults);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class AsyncRegistration<TResult>(
        InspectionQuery<TResult> query,
        Func<
            TContext,
            InspectionQueryResults,
            CancellationToken,
            ValueTask<TResult>> execute,
        ImmutableArray<InspectionQueryDefinition> requires,
        ImmutableArray<InspectionQueryDefinition> optional)
        : Registration(query, requires, optional)
    {
        internal override void Execute(
            TContext context,
            InspectionQueryResults results,
            InspectionQueryResults prerequisiteResults)
            => throw new InspectionQueryException(
                $"Query '{query.Name}' is asynchronous and must be executed with RunAsync.");

        internal override async ValueTask ExecuteAsync(
            TContext context,
            InspectionQueryResults results,
            InspectionQueryResults prerequisiteResults,
            CancellationToken cancellationToken)
        {
            TResult result = await execute(
                context,
                prerequisiteResults,
                cancellationToken).ConfigureAwait(false);
            results.Set(query, result);
        }
    }
}
