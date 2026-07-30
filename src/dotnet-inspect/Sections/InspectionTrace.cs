using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DotnetInspector.Sections;

/// <summary>
/// One scanner that <see cref="ScannerRegistry.RunScanners"/> actually invoked.
/// </summary>
/// <param name="Key">The scanner key.</param>
/// <param name="IsBundle">
/// True when the key does no work of its own and exists only to pull in prerequisites
/// (<see cref="ScannerRegistry.AddBundle"/>). A bundle's elapsed time is its own dispatch only —
/// the work is attributed to the prerequisites it named.
/// </param>
/// <param name="Elapsed">Wall time spent inside the scanner body.</param>
public readonly record struct ScannerExecution(string Key, bool IsBundle, TimeSpan Elapsed);

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
    private readonly List<string> _requested = [];
    private readonly List<string> _closure = [];
    private readonly List<ScannerExecution> _executions = [];
    private readonly List<(string Resource, string Detail)> _resources = [];

    /// <summary>The command that produced this trace (for the report header).</summary>
    public string? Command { get; set; }

    /// <summary>The inspection target (for the report header).</summary>
    public string? Target { get; set; }

    /// <summary>The effective verbosity the selection ran at (for the report header).</summary>
    public string? Verbosity { get; set; }

    /// <summary>Sections that were selected and the scanner key each one demanded.</summary>
    public IReadOnlyList<(string Section, string Scanner)> Demand => _demand;

    /// <summary>Scanner keys demanded directly by a selected section.</summary>
    public IReadOnlyList<string> Requested => _requested;

    /// <summary>
    /// Scanner keys after prerequisite expansion. Anything here but not in
    /// <see cref="Requested"/> was pulled in by a declared prerequisite.
    /// </summary>
    public IReadOnlyList<string> Closure => _closure;

    /// <summary>Scanners that ran, in execution order.</summary>
    public IReadOnlyList<ScannerExecution> Executions => _executions;

    /// <summary>
    /// Expensive resources the run acquired, in acquisition order — the shared metadata session,
    /// the whole-assembly body index, the drill map. A resource that never appears was never built,
    /// which is the property most worth checking: a section that should not have cost anything
    /// leaves no resource line.
    /// </summary>
    public IReadOnlyList<(string Resource, string Detail)> Resources => _resources;

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

    /// <summary>Records that <paramref name="section"/> demanded <paramref name="scanner"/>.</summary>
    public void RecordDemand(string section, string scanner) => _demand.Add((section, scanner));

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

    /// <summary>Records one scanner invocation.</summary>
    public void RecordExecution(string key, bool isBundle, TimeSpan elapsed)
        => _executions.Add(new ScannerExecution(key, isBundle, elapsed));

    /// <summary>
    /// Records an expensive resource acquisition. Called at the point of acquisition rather than
    /// at the point of request, so the trace shows what was built, not what might have been.
    /// </summary>
    public void RecordResource(string resource, string detail)
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
    public string Render()
    {
        var text = new StringBuilder();
        text.Append("trace: ").Append(Command ?? "inspect");
        if (Target is { Length: > 0 })
            text.Append(' ').Append(Target);
        if (Verbosity is { Length: > 0 })
            text.Append(" [").Append(Verbosity).Append(']');
        text.AppendLine();

        text.AppendLine("  sections demanding a scanner");
        if (_demand.Count == 0)
        {
            text.AppendLine("    (none)");
        }
        else
        {
            foreach (var (section, scanner) in _demand)
                text.Append("    ").Append(section).Append(" -> ").AppendLine(scanner);
        }

        text.Append("  scanners requested   ").AppendLine(Join(_requested));

        var added = _closure.Where(k => !_requested.Contains(k, StringComparer.Ordinal)).ToList();
        text.Append("  added by prerequisite").Append(' ').AppendLine(Join(added));

        text.AppendLine("  scanners executed");
        if (_executions.Count == 0)
        {
            text.AppendLine("    (none)");
        }
        else
        {
            foreach (var execution in _executions)
            {
                text.Append("    ")
                    .Append(execution.Key.PadRight(28))
                    .Append(Format(execution.Elapsed));
                if (execution.IsBundle)
                    text.Append("  (bundle, no work of its own)");
                text.AppendLine();
            }
        }

        text.AppendLine("  resources acquired");
        if (_resources.Count == 0)
        {
            text.AppendLine("    (none)");
        }
        else
        {
            foreach (var (resource, detail) in _resources)
                text.Append("    ").Append(resource.PadRight(28)).AppendLine(detail);
        }

        text.Append("  total scanner time   ").AppendLine(Format(TotalScannerTime));
        return text.ToString();
    }

    private static string Join(IReadOnlyList<string> values)
        => values.Count == 0 ? "(none)" : string.Join(", ", values);

    private static string Format(TimeSpan elapsed)
        => elapsed.TotalMilliseconds.ToString("F1", CultureInfo.InvariantCulture) + " ms";
}
