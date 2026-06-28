using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

/// <summary>Per-assembly analysis metrics: the mechanically-checkable corpus signals.</summary>
public sealed record AssemblyMetrics(
    string Name,
    bool Opened,
    bool TimedOut,
    int Methods,
    int Diagnostics,
    int Allocations,
    int ExceptionConstructions,
    IReadOnlyDictionary<string, int> Shapes);

/// <summary>A corpus snapshot: per-assembly metrics plus the run's tool version marker.</summary>
public sealed record CorpusSnapshot(IReadOnlyList<AssemblyMetrics> Assemblies);

public sealed record CorpusDiff(
    IReadOnlyList<string> Regressions,
    IReadOnlyList<string> Drift,
    IReadOnlyList<string> AddedOrRemoved)
{
    public bool HasRegression => Regressions.Count > 0;
}

/// <summary>
/// The analysis corpus stability sensor (#1818, Layer 2). It sweeps <see cref="LibraryBodyIndex"/>
/// over a fixed corpus and reports the mechanically-checkable signals — did every assembly open,
/// did the analyzer choke (recoverable diagnostics), and how did the aggregate signal counts move
/// against a committed baseline. The point is to separate a REGRESSION (an assembly that stops
/// opening, or a jump in analyzer diagnostics) from DRIFT (signal-count movement, expected as the
/// analyzer improves and rebaselined deliberately). There is no precision/recall judgement here —
/// that is Layer 3.
/// </summary>
public static class AnalysisCorpusSensor
{
    static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static CorpusSnapshot Measure(IReadOnlyList<string> assemblyPaths, int perAssemblyTimeoutSeconds = 60)
    {
        var assemblies = new List<AssemblyMetrics>(assemblyPaths.Count);
        foreach (var path in assemblyPaths)
            assemblies.Add(MeasureWithTimeout(path, perAssemblyTimeoutSeconds));
        assemblies.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        return new CorpusSnapshot(assemblies);
    }

    // Bound each assembly so one pathological input cannot hang the whole sweep. A timeout is
    // recorded as a stable signal (TimedOut) rather than a crash; the orphaned worker exits with
    // the process. Going from measured to timed-out (or vice versa) is a regression in the diff.
    static AssemblyMetrics MeasureWithTimeout(string path, int timeoutSeconds)
    {
        string name = Path.GetFileName(path);
        AssemblyMetrics? result = null;
        var worker = new Thread(() => result = MeasureOne(path)) { IsBackground = true };
        worker.Start();
        if (worker.Join(TimeSpan.FromSeconds(timeoutSeconds)) && result is not null)
            return result;
        return new AssemblyMetrics(name, Opened: false, TimedOut: true, 0, 0, 0, 0, new Dictionary<string, int>());
    }

    static AssemblyMetrics MeasureOne(string path)
    {
        string name = Path.GetFileName(path);
        LibraryBodyIndex index;
        try
        {
            index = LibraryBodyIndex.Open(path);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException or ArgumentException)
        {
            return new AssemblyMetrics(name, Opened: false, TimedOut: false, 0, 0, 0, 0, new Dictionary<string, int>());
        }

        var signals = index.GetMethodSignals();
        int allocations = 0;
        int exceptionConstructions = 0;
        foreach (var method in index.Methods)
        {
            var signal = signals.GetValueOrDefault(method.MetadataToken, MethodSignals.None);
            allocations += signal.Allocations;
            exceptionConstructions += signal.ExceptionTypes.Length;
        }

        var shapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var opportunity in index.OptimizationOpportunities)
            shapes[opportunity.Shape] = shapes.GetValueOrDefault(opportunity.Shape) + 1;

        return new AssemblyMetrics(
            name,
            Opened: true,
            TimedOut: false,
            index.Methods.Length,
            index.Diagnostics.Length,
            allocations,
            exceptionConstructions,
            shapes);
    }

    // Regression = a robustness loss that is always a bug: an assembly that stops opening, or a
    // jump in analyzer diagnostics (recoverable method failures). Drift = expected signal-count
    // movement, surfaced for review/rebaseline but not a failure.
    public static CorpusDiff Diff(CorpusSnapshot baseline, CorpusSnapshot current)
    {
        var regressions = new List<string>();
        var drift = new List<string>();
        var addedOrRemoved = new List<string>();

        var currentByName = current.Assemblies.ToDictionary(a => a.Name, StringComparer.Ordinal);
        var baselineByName = baseline.Assemblies.ToDictionary(a => a.Name, StringComparer.Ordinal);

        foreach (var name in baselineByName.Keys.Union(currentByName.Keys).OrderBy(n => n, StringComparer.Ordinal))
        {
            bool inBase = baselineByName.TryGetValue(name, out var b);
            bool inCur = currentByName.TryGetValue(name, out var c);
            if (!inBase || !inCur)
            {
                addedOrRemoved.Add($"{name}: {(inCur ? "added" : "removed")}");
                continue;
            }

            if (b!.Opened && !c!.Opened)
                regressions.Add($"{name}: {(c.TimedOut ? "timed out" : "stopped opening")} (was {b.Methods} methods)");
            if (c!.Opened && c.Diagnostics > b.Diagnostics)
                regressions.Add($"{name}: analyzer diagnostics {b.Diagnostics} -> {c.Diagnostics}");

            if (c.Opened && b.Opened)
            {
                AddDrift(drift, name, "methods", b.Methods, c.Methods);
                AddDrift(drift, name, "allocations", b.Allocations, c.Allocations);
                AddDrift(drift, name, "exception-constructions", b.ExceptionConstructions, c.ExceptionConstructions);
                foreach (var shape in b.Shapes.Keys.Union(c.Shapes.Keys).OrderBy(s => s, StringComparer.Ordinal))
                    AddDrift(drift, name, shape, b.Shapes.GetValueOrDefault(shape), c.Shapes.GetValueOrDefault(shape));
            }
        }

        return new CorpusDiff(regressions, drift, addedOrRemoved);
    }

    static void AddDrift(List<string> drift, string name, string metric, int oldValue, int newValue)
    {
        if (oldValue != newValue)
            drift.Add($"{name}: {metric} {oldValue} -> {newValue}");
    }

    public static string FormatCard(CorpusSnapshot snapshot)
    {
        var sb = new StringBuilder();
        int failed = snapshot.Assemblies.Count(a => !a.Opened);
        int timedOut = snapshot.Assemblies.Count(a => a.TimedOut);
        int diagnostics = snapshot.Assemblies.Sum(a => a.Diagnostics);
        int methods = snapshot.Assemblies.Sum(a => a.Methods);
        int allocations = snapshot.Assemblies.Sum(a => a.Allocations);
        var shapeTotals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var assembly in snapshot.Assemblies)
            foreach (var (shape, count) in assembly.Shapes)
                shapeTotals[shape] = shapeTotals.GetValueOrDefault(shape) + count;

        sb.AppendLine($"ANALYSIS CORPUS CARD: {snapshot.Assemblies.Count} assemblies, {failed} not measured ({timedOut} timed out)");
        sb.AppendLine($"  methods={methods} diagnostics={diagnostics} allocations={allocations}");
        sb.AppendLine($"  opportunity shapes: {string.Join(", ", shapeTotals.Select(kv => $"{kv.Key}={kv.Value}"))}");
        foreach (var assembly in snapshot.Assemblies)
        {
            string status = assembly.Opened ? "" : assembly.TimedOut ? " TIMED-OUT" : " FAILED";
            sb.AppendLine($"  {assembly.Name}{status}  methods={assembly.Methods} diag={assembly.Diagnostics} alloc={assembly.Allocations} shapes={assembly.Shapes.Values.Sum()}");
        }
        return sb.ToString();
    }

    public static string FormatDiffCard(CorpusDiff diff)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"ANALYSIS CORPUS DIFF: {diff.Regressions.Count} regression(s), {diff.Drift.Count} drift, {diff.AddedOrRemoved.Count} added/removed");
        if (diff.Regressions.Count > 0)
        {
            sb.AppendLine("  REGRESSIONS:");
            foreach (var line in diff.Regressions) sb.AppendLine($"    {line}");
        }
        if (diff.AddedOrRemoved.Count > 0)
        {
            sb.AppendLine("  added/removed:");
            foreach (var line in diff.AddedOrRemoved) sb.AppendLine($"    {line}");
        }
        if (diff.Drift.Count > 0)
        {
            sb.AppendLine("  drift:");
            foreach (var line in diff.Drift) sb.AppendLine($"    {line}");
        }
        return sb.ToString();
    }

    public static string ToJson(CorpusSnapshot snapshot) => JsonSerializer.Serialize(snapshot, s_json);

    public static CorpusSnapshot FromJson(string json)
        => JsonSerializer.Deserialize<CorpusSnapshot>(json, s_json)
           ?? throw new InvalidOperationException("Empty corpus snapshot.");
}
