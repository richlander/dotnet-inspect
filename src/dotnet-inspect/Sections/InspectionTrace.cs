using System.Diagnostics;
using System.Globalization;
using DotnetInspector.Queries;
using InertText;

namespace DotnetInspector.Sections;

/// <summary>One typed query that the query registry invoked.</summary>
public readonly record struct QueryExecution(InspectionQueryDefinition Query, TimeSpan Elapsed);

/// <summary>
/// A record of what a single inspection actually did: which sections were selected, which typed
/// queries they demanded, what prerequisite closure added, and which expensive resources were
/// acquired.
///
/// This exists to make "the system did the correct minimum work" checkable rather than asserted.
/// The query pipeline is demand-driven, but nothing about a correct run looks different from a run
/// that inspected the whole assembly and threw the result away. A trace makes the difference
/// visible and gives tests a typed record to assert on instead of rendered text.
///
/// Tracing is off unless a trace object is threaded through, so an untraced run allocates nothing
/// and takes no branch beyond a null check.
/// </summary>
public sealed class InspectionTrace
{
    private readonly List<(string Section, InspectionQueryDefinition Query)> _queryDemand = [];
    private readonly List<HostQueryDemand> _commandQueryDemand = [];
    private readonly List<InspectionQueryDefinition> _requestedQueries = [];
    private readonly List<InspectionQueryDefinition> _queryClosure = [];
    private readonly List<QueryExecution> _queryExecutions = [];
    private readonly List<(string Resource, InertString Detail)> _resources = [];

    /// <summary>The command that produced this trace (for the report header).</summary>
    public InertString? Command { get; set; }

    /// <summary>The inspection target (for the report header).</summary>
    public InertString? Target { get; set; }

    /// <summary>The effective verbosity the selection ran at (for the report header).</summary>
    public InertString? Verbosity { get; set; }

    /// <summary>Sections that were selected and the typed query each one demanded.</summary>
    public IReadOnlyList<(string Section, InspectionQueryDefinition Query)> QueryDemand
        => _queryDemand;

    /// <summary>Typed queries the command requested independently of a selected section.</summary>
    public IReadOnlyList<HostQueryDemand> CommandQueryDemand
        => _commandQueryDemand;

    /// <summary>Typed queries demanded directly by sections or the command.</summary>
    public IReadOnlyList<InspectionQueryDefinition> RequestedQueries => _requestedQueries;

    /// <summary>Typed queries after prerequisite expansion.</summary>
    public IReadOnlyList<InspectionQueryDefinition> QueryClosure => _queryClosure;

    /// <summary>Typed queries that ran, in execution order.</summary>
    public IReadOnlyList<QueryExecution> QueryExecutions => _queryExecutions;

    /// <summary>
    /// Expensive resources the run acquired, in acquisition order — the shared metadata session,
    /// the whole-assembly body index, the drill map. A resource that never appears was never built,
    /// which is the property most worth checking: a section that should not have cost anything
    /// leaves no resource line.
    /// </summary>
    public IReadOnlyList<(string Resource, InertString Detail)> Resources => _resources;

    /// <summary>Total time attributed to typed queries.</summary>
    public TimeSpan TotalQueryTime
    {
        get
        {
            var total = TimeSpan.Zero;
            foreach (QueryExecution execution in _queryExecutions)
                total += execution.Elapsed;
            return total;
        }
    }

    /// <summary>Records that <paramref name="section"/> demanded <paramref name="query"/>.</summary>
    public void RecordQueryDemand(string section, InspectionQueryDefinition query)
        => _queryDemand.Add((section, query));

    /// <summary>Records a typed query requested directly by the command.</summary>
    public void RecordCommandQueryDemand(string reason, InspectionQueryDefinition query)
    {
        _commandQueryDemand.Add(new HostQueryDemand(reason, query));
        if (!_requestedQueries.Contains(query))
        {
            _requestedQueries.Add(query);
            _requestedQueries.Sort(QueryNameComparer.Instance);
        }
    }

    /// <summary>Records typed queries demanded directly by sections and the command.</summary>
    public void RecordRequestedQueries(IEnumerable<InspectionQueryDefinition> queries)
    {
        _requestedQueries.Clear();
        _requestedQueries.AddRange(queries);
        _requestedQueries.Sort(QueryNameComparer.Instance);
    }

    /// <summary>Adds typed queries after prerequisite expansion.</summary>
    public void RecordQueryClosure(IEnumerable<InspectionQueryDefinition> queries)
    {
        foreach (InspectionQueryDefinition query in queries)
        {
            if (!_queryClosure.Contains(query))
                _queryClosure.Add(query);
        }
        _queryClosure.Sort(QueryNameComparer.Instance);
    }

    /// <summary>Records one typed query invocation.</summary>
    public void RecordQueryExecution(InspectionQueryDefinition query, TimeSpan elapsed)
        => _queryExecutions.Add(new QueryExecution(query, elapsed));

    /// <summary>
    /// Records an expensive resource acquisition. Called at the point of acquisition rather than
    /// at the point of request, so the trace shows what was built, not what might have been.
    /// </summary>
    public void RecordResource(string resource, InertString detail)
        => _resources.Add((resource, detail));

    /// <summary>
    /// Renders the plain-text diagnostic report written to stderr under <c>--trace</c>. This is a
    /// diagnostic surface, deliberately not a Markout section: it must not alter the stdout
    /// document a caller is parsing.
    /// </summary>
    /// <remarks>
    /// Yields lines rather than one terminated string because the report interpolates untrusted
    /// text -- <see cref="Target"/> is argv, and resource details name paths and package entries.
    /// Returning the report as text would force its writer to recover line boundaries by splitting
    /// on terminators, and a splitter cannot tell the composer's newline from one the attacker put
    /// inside a field: a target named <c>"ev\nError: FORGED"</c> then prints a forged unindented
    /// error line of its own. Measured, on exactly that input, before this returned lines.
    /// Emitting the boundaries the composer intended makes each line a unit the sink can contain
    /// (issue #3319).
    /// </remarks>
    public IEnumerable<InertString> RenderLines()
    {
        InertString command = Command ?? new InertString(TextPolicy.Field, "inspect");
        InertString head = InertString.Format(TextPolicy.Field, $"trace: {command}");
        if (Target is { IsEmpty: false } target)
            head = InertString.Format(TextPolicy.Field, $"{head} {target}");
        if (Verbosity is { IsEmpty: false } verbosity)
            head = InertString.Format(TextPolicy.Field, $"{head} [{verbosity}]");
        yield return head;

        if (_queryDemand.Count > 0 || _commandQueryDemand.Count > 0
            || _requestedQueries.Count > 0 || _queryExecutions.Count > 0)
        {
            yield return Line("  sections demanding a query");
            if (_queryDemand.Count == 0)
            {
                yield return Line("    (none)");
            }
            else
            {
                foreach ((string section, InspectionQueryDefinition query) in _queryDemand)
                {
                    yield return InertString.Format(
                        TextPolicy.Field,
                        $"    {section} -> {query.Name}");
                }
            }

            if (_commandQueryDemand.Count > 0)
            {
                yield return Line("  queries demanded by the command");
                foreach ((string reason, InspectionQueryDefinition query) in _commandQueryDemand)
                {
                    yield return InertString.Format(
                        TextPolicy.Field,
                        $"    {reason} -> {query.Name}");
                }
            }

            yield return InertString.Format(
                TextPolicy.Field,
                $"  queries requested    {JoinQueries(_requestedQueries)}");

            List<InspectionQueryDefinition> queryAdded = _queryClosure
                .Where(query => !_requestedQueries.Contains(query))
                .ToList();
            yield return InertString.Format(
                TextPolicy.Field,
                $"  query prerequisites  {JoinQueries(queryAdded)}");

            yield return Line("  queries executed");
            if (_queryExecutions.Count == 0)
            {
                yield return Line("    (none)");
            }
            else
            {
                foreach (QueryExecution execution in _queryExecutions)
                {
                    yield return InertString.Format(
                        TextPolicy.Field,
                        $"    {execution.Query.Name.PadRight(28)}{Format(execution.Elapsed)}");
                }
            }
        }

        yield return Line("  resources acquired");
        if (_resources.Count == 0)
        {
            yield return Line("    (none)");
        }
        else
        {
            foreach (var (resource, detail) in _resources)
            {
                yield return InertString.Format(
                    TextPolicy.Field,
                    $"    {resource.PadRight(28)}{detail}");
            }
        }

        if (_queryExecutions.Count > 0)
        {
            yield return InertString.Format(
                TextPolicy.Field,
                $"  total query time     {Format(TotalQueryTime)}");
        }
    }

    private static InertString Join(IReadOnlyList<string> values)
        => new(
            TextPolicy.Field,
            values.Count == 0 ? "(none)" : string.Join(", ", values));

    private static InertString JoinQueries(IReadOnlyList<InspectionQueryDefinition> values)
        => new(
            TextPolicy.Field,
            values.Count == 0 ? "(none)" : string.Join(", ", values.Select(q => q.Name)));

    private static InertString Line(string text) => new(TextPolicy.Field, text);

    private static string Format(TimeSpan elapsed)
        => elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms";

    private sealed class QueryNameComparer : IComparer<InspectionQueryDefinition>
    {
        public static QueryNameComparer Instance { get; } = new();

        public int Compare(InspectionQueryDefinition? x, InspectionQueryDefinition? y)
            => StringComparer.Ordinal.Compare(x?.Name, y?.Name);
    }
}
