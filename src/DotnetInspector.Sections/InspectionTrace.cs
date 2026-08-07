using System.Diagnostics;
using System.Globalization;
using DotnetInspector.Queries;
using InertText;

namespace DotnetInspector.Sections;

/// <summary>
/// One scanner that an inspection pipeline actually invoked.
/// </summary>
/// <param name="Key">The scanner key.</param>
/// <param name="IsBundle">
/// True when the key does no work of its own and exists only to pull in prerequisites
/// through a bundle declaration. A bundle's elapsed time is its own dispatch only —
/// the work is attributed to the prerequisites it named.
/// </param>
/// <param name="Elapsed">Wall time spent inside the scanner body.</param>
public readonly record struct ScannerExecution(string Key, bool IsBundle, TimeSpan Elapsed);

/// <summary>One typed query that the query registry invoked.</summary>
public readonly record struct QueryExecution(InspectionQueryDefinition Query, TimeSpan Elapsed);

/// <summary>
/// A record of what a single inspection actually did: which sections were selected, which scanner
/// keys those sections demanded, what the prerequisite closure added, which scanners ran and for
/// how long, and which expensive resources were acquired.
///
/// This exists to make "the system did the correct minimum work" checkable rather than asserted.
/// The section/scanner pipeline is demand-driven — a scanner runs only when a selected section
/// declares its key — but nothing about a correct run looks different from a run that scanned the
/// whole assembly and threw the result away. Both print the same sections. A trace is the only
/// place the difference is visible, so it is also what a test asserts on: the typed record, not
/// rendered text.
///
/// Tracing is off unless a trace object is threaded through, so an untraced run allocates nothing
/// and takes no branch beyond a null check.
/// </summary>
public sealed class InspectionTrace
{
    private readonly List<(string Section, string Scanner)> _demand = [];
    private readonly List<(string Reason, string Scanner)> _commandDemand = [];
    private readonly List<string> _requested = [];
    private readonly List<string> _closure = [];
    private readonly List<ScannerExecution> _executions = [];
    private readonly List<(string Section, InspectionQueryDefinition Query)> _queryDemand = [];
    private readonly List<(string Reason, InspectionQueryDefinition Query)> _commandQueryDemand = [];
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

    /// <summary>Sections that were selected and the scanner key each one demanded.</summary>
    public IReadOnlyList<(string Section, string Scanner)> Demand => _demand;

    /// <summary>
    /// Scanners the command asked for directly, with the reason. Kept separate from
    /// <see cref="Demand"/> because no selected section named them, and separate from the
    /// prerequisite closure because no prerequisite edge pulled them in.
    /// </summary>
    public IReadOnlyList<(string Reason, string Scanner)> CommandDemand => _commandDemand;

    /// <summary>Scanner keys demanded directly by a selected section.</summary>
    public IReadOnlyList<string> Requested => _requested;

    /// <summary>
    /// Scanner keys after prerequisite expansion. Anything here but not in
    /// <see cref="Requested"/> was pulled in by a declared prerequisite.
    /// </summary>
    public IReadOnlyList<string> Closure => _closure;

    /// <summary>Scanners that ran, in execution order.</summary>
    public IReadOnlyList<ScannerExecution> Executions => _executions;

    /// <summary>Sections that were selected and the typed query each one demanded.</summary>
    public IReadOnlyList<(string Section, InspectionQueryDefinition Query)> QueryDemand
        => _queryDemand;

    /// <summary>Typed queries the command requested independently of a selected section.</summary>
    public IReadOnlyList<(string Reason, InspectionQueryDefinition Query)> CommandQueryDemand
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

    /// <summary>Total time attributed to scanner bodies.</summary>
    public TimeSpan TotalScannerTime
    {
        get
        {
            var total = TimeSpan.Zero;
            foreach (var execution in _executions)
                total += execution.Elapsed;
            return total;
        }
    }

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

    /// <summary>Records that <paramref name="section"/> demanded <paramref name="scanner"/>.</summary>
    public void RecordDemand(string section, string scanner) => _demand.Add((section, scanner));

    /// <summary>
    /// Records a scanner the command itself asked for after section selection had finished — today
    /// only discovery mode, which needs metadata table row counts to decide whether the
    /// <c>@Metadata</c> category has anything worth listing.
    ///
    /// This has its own recorder rather than folding into the closure because the report's entire
    /// value is that a reader can trust which mechanism pulled a scanner in. A command-level demand
    /// rendered as "added by prerequisite" asserts a prerequisite edge that does not exist, and
    /// sends anyone chasing an unexpected scan to the wrong declaration.
    /// </summary>
    public void RecordCommandDemand(string reason, string scanner)
    {
        _commandDemand.Add((reason, scanner));
        if (!_requested.Contains(scanner, StringComparer.Ordinal))
        {
            _requested.Add(scanner);
            _requested.Sort(StringComparer.Ordinal);
        }
    }

    /// <summary>Records the scanner keys demanded directly by the selected sections.</summary>
    public void RecordRequested(IEnumerable<string> keys)
    {
        _requested.Clear();
        _requested.AddRange(keys);
        _requested.Sort(StringComparer.Ordinal);
    }

    /// <summary>Records the scanner keys after prerequisite expansion.</summary>
    public void RecordClosure(IEnumerable<string> keys)
    {
        _closure.Clear();
        _closure.AddRange(keys);
        _closure.Sort(StringComparer.Ordinal);
    }

    /// <summary>Records that <paramref name="section"/> demanded <paramref name="query"/>.</summary>
    public void RecordQueryDemand(string section, InspectionQueryDefinition query)
        => _queryDemand.Add((section, query));

    /// <summary>Records a typed query requested directly by the command.</summary>
    public void RecordCommandQueryDemand(string reason, InspectionQueryDefinition query)
    {
        _commandQueryDemand.Add((reason, query));
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

    /// <summary>Records one scanner invocation.</summary>
    public void RecordExecution(string key, bool isBundle, TimeSpan elapsed)
        => _executions.Add(new ScannerExecution(key, isBundle, elapsed));

    /// <summary>
    /// Records an expensive resource acquisition. Called at the point of acquisition rather than
    /// at the point of request, so the trace shows what was built, not what might have been.
    /// </summary>
    public void RecordResource(string resource, InertString detail)
        => _resources.Add((resource, detail));

    /// <summary>
    /// Runs <paramref name="body"/> and records it as a scanner execution. Timing uses
    /// <see cref="Stopwatch.GetTimestamp"/> deltas rather than a Stopwatch instance so a traced run
    /// allocates nothing per scanner.
    /// </summary>
    public void Time(string key, bool isBundle, Action body)
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            body();
        }
        finally
        {
            RecordExecution(key, isBundle, Stopwatch.GetElapsedTime(start));
        }
    }

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

        yield return Line("  sections demanding a scanner");
        if (_demand.Count == 0)
        {
            yield return Line("    (none)");
        }
        else
        {
            foreach (var (section, scanner) in _demand)
                yield return InertString.Format(TextPolicy.Field, $"    {section} -> {scanner}");
        }

        if (_commandDemand.Count > 0)
        {
            yield return Line("  scanners demanded by the command");
            foreach (var (reason, scanner) in _commandDemand)
                yield return InertString.Format(TextPolicy.Field, $"    {reason} -> {scanner}");
        }

        yield return InertString.Format(
            TextPolicy.Field,
            $"  scanners requested   {Join(_requested)}");

        var added = _closure.Where(k => !_requested.Contains(k, StringComparer.Ordinal)).ToList();
        yield return InertString.Format(
            TextPolicy.Field,
            $"  added by prerequisite {Join(added)}");

        yield return Line("  scanners executed");
        if (_executions.Count == 0)
        {
            yield return Line("    (none)");
        }
        else
        {
            foreach (var execution in _executions)
            {
                InertString line = InertString.Format(
                    TextPolicy.Field,
                    $"    {execution.Key.PadRight(28)}{Format(execution.Elapsed)}");
                if (execution.IsBundle)
                {
                    line = InertString.Format(
                        TextPolicy.Field,
                        $"{line}  (bundle, no work of its own)");
                }
                yield return line;
            }
        }

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

        yield return InertString.Format(
            TextPolicy.Field,
            $"  total scanner time   {Format(TotalScannerTime)}");
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
