using System.Text;
using System.Text.Json;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

/// <summary>Per-assembly leak-triage result: whether it opened, its finding count, and the shapes hit with a few example methods.</summary>
public sealed record LeakTriageAssembly(
    string Name,
    bool Opened,
    bool TimedOut,
    int Findings,
    IReadOnlyDictionary<string, int> Shapes,
    IReadOnlyList<string> Examples);

/// <summary>A leak-triage corpus report: per-assembly rows plus aggregate shape totals.</summary>
public sealed record LeakTriageReport(
    IReadOnlyList<LeakTriageAssembly> Assemblies,
    IReadOnlyDictionary<string, int> TotalsByShape,
    int Total);

/// <summary>
/// The leak-triage corpus sensor: sweeps <see cref="LeakTriageAnalyzer"/> over a fixed corpus
/// and reports where the fail-closed ArrayPool analysis fires — total findings, the shape
/// histogram, and example methods per shape. This is the evidence engine that must show a
/// non-zero, high-precision signal before any user-facing `Leak Triage` section is wired
/// (#1992): the analyzer is precision-first, so an empty corpus card means recall, not the
/// section, is the next lever. There is no product surface here — the harness measures.
/// </summary>
public static class LeakTriageSensor
{
    static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };

    public static LeakTriageReport Measure(IReadOnlyList<string> assemblyPaths, int perAssemblyTimeoutSeconds = 120, int examplesPerAssembly = 5)
    {
        var assemblies = new List<LeakTriageAssembly>(assemblyPaths.Count);
        foreach (var path in assemblyPaths)
            assemblies.Add(MeasureWithTimeout(path, perAssemblyTimeoutSeconds, examplesPerAssembly));
        assemblies.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
            foreach (var (shape, count) in assembly.Shapes)
                totals[shape] = totals.GetValueOrDefault(shape) + count;

        return new LeakTriageReport(assemblies, totals, assemblies.Sum(a => a.Findings));
    }

    // Bound each assembly so one pathological input cannot hang the sweep; a timeout is a stable
    // signal, not a crash. The analyzer is already fail-closed per method, so this is belt-and-braces.
    static LeakTriageAssembly MeasureWithTimeout(string path, int timeoutSeconds, int examplesPerAssembly)
    {
        string name = Path.GetFileName(path);
        LeakTriageAssembly? result = null;
        var worker = new Thread(() => result = MeasureOne(path, examplesPerAssembly)) { IsBackground = true };
        worker.Start();
        if (worker.Join(TimeSpan.FromSeconds(timeoutSeconds)) && result is not null)
            return result;
        return new LeakTriageAssembly(name, Opened: false, TimedOut: true, 0, new Dictionary<string, int>(), []);
    }

    static LeakTriageAssembly MeasureOne(string path, int examplesPerAssembly)
    {
        string name = Path.GetFileName(path);
        try
        {
            var findings = LeakTriageAnalyzer.AnalyzeAssembly(path);
            var shapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var examples = new List<string>();
            foreach (var finding in findings)
            {
                shapes[finding.Shape] = shapes.GetValueOrDefault(finding.Shape) + 1;
                if (examples.Count < examplesPerAssembly)
                    examples.Add($"{finding.Shape}  {finding.Method.DeclaringType.Name}::{finding.Method.Name}");
            }
            return new LeakTriageAssembly(name, Opened: true, TimedOut: false, findings.Length, shapes, examples);
        }
        catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException or ArgumentException)
        {
            return new LeakTriageAssembly(name, Opened: false, TimedOut: false, 0, new Dictionary<string, int>(), []);
        }
    }

    public static string FormatCard(LeakTriageReport report, int maxExamples)
    {
        var sb = new StringBuilder();
        int opened = report.Assemblies.Count(a => a.Opened);
        int failed = report.Assemblies.Count(a => !a.Opened && !a.TimedOut);
        int timedOut = report.Assemblies.Count(a => a.TimedOut);

        sb.AppendLine($"LEAK TRIAGE CORPUS SENSOR over {report.Assemblies.Count} assemblies ({opened} opened, {failed} failed, {timedOut} timed out)");
        sb.AppendLine($"  total findings: {report.Total}");
        if (report.TotalsByShape.Count == 0)
        {
            sb.AppendLine("  by shape: (none — the fail-closed gates suppressed every candidate; broaden recall, not the section)");
        }
        else
        {
            sb.AppendLine("  by shape:");
            foreach (var (shape, count) in report.TotalsByShape.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
                sb.AppendLine($"    {count,6}  {shape}");
        }

        var withFindings = report.Assemblies.Where(a => a.Findings > 0).ToList();
        if (withFindings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Assemblies with findings:");
            foreach (var assembly in withFindings.OrderByDescending(a => a.Findings))
            {
                sb.AppendLine($"  {assembly.Name}: {assembly.Findings}");
                foreach (var example in assembly.Examples.Take(maxExamples))
                    sb.AppendLine($"      {example}");
            }
        }

        return sb.ToString();
    }

    public static string ToJson(LeakTriageReport report) => JsonSerializer.Serialize(report, s_json);
}
