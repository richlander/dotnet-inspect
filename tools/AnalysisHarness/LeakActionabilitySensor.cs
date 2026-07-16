using Markout;
using Markout.Formatting;

using ILInspector.Analysis;
using ILInspector.Findings;

namespace ILInspector.AnalysisHarness;

/// <summary>One classified exception-path candidate: its actionability class, method, and the
/// distinct boundary members the rented array flows into between Rent and return.</summary>
public sealed record LeakActionabilityExample(string Class, string Method, string BoundarySet);

/// <summary>Per-assembly actionability result: whether it opened, how many exception-path
/// candidates it produced, the per-class histogram, and a few example rows.</summary>
public sealed record LeakActionabilityAssembly(
    string Name,
    bool Opened,
    bool TimedOut,
    int Candidates,
    IReadOnlyDictionary<string, int> ClassCounts,
    IReadOnlyList<LeakActionabilityExample> Examples);

/// <summary>An actionability census: per-assembly rows plus aggregate per-class totals.</summary>
public sealed record LeakActionabilityReport(
    IReadOnlyList<LeakActionabilityAssembly> Assemblies,
    IReadOnlyDictionary<string, int> TotalsByClass,
    int Total);

public enum LeakActionabilityFormat { Markdown, Tsv, Jsonl }

/// <summary>
/// Corpus orchestration and reporting over the Analysis-owned resource lifecycle inspection. The
/// sensor deliberately performs no token resolution, boundary attribution, or actionability
/// classification; those are product capabilities shared with the user-facing Resource Triage
/// section.
/// </summary>
public static class LeakActionabilitySensor
{
    public const string Untrusted = "untrusted-actionable";
    public const string Trusted = "trusted-low-actionability";
    public const string Unknown = "unknown";

    static readonly string[] ClassOrder = [Untrusted, Trusted, Unknown];

    public static LeakActionabilityReport Measure(IReadOnlyList<string> assemblyPaths, int perAssemblyTimeoutSeconds = 120, int examplesPerAssembly = 5)
    {
        var assemblies = new List<LeakActionabilityAssembly>(assemblyPaths.Count);
        foreach (var path in assemblyPaths)
            assemblies.Add(MeasureWithTimeout(path, perAssemblyTimeoutSeconds, examplesPerAssembly));
        assemblies.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
            foreach (var (cls, count) in assembly.ClassCounts)
                totals[cls] = totals.GetValueOrDefault(cls) + count;

        return new LeakActionabilityReport(assemblies, totals, assemblies.Sum(a => a.Candidates));
    }

    static LeakActionabilityAssembly MeasureWithTimeout(string path, int timeoutSeconds, int examplesPerAssembly)
    {
        string name = Path.GetFileName(path);
        LeakActionabilityAssembly? result = null;
        var worker = new Thread(() => result = MeasureOne(path, examplesPerAssembly)) { IsBackground = true };
        worker.Start();
        if (worker.Join(TimeSpan.FromSeconds(timeoutSeconds)) && result is not null)
            return result;
        return new LeakActionabilityAssembly(name, Opened: false, TimedOut: true, 0, new Dictionary<string, int>(), []);
    }

    static LeakActionabilityAssembly MeasureOne(string path, int examplesPerAssembly)
    {
        string name = Path.GetFileName(path);
        try
        {
            var inspection = ResourceLifecycleAnalysis.InspectAssembly(
                path,
                new FindingSubject(Path.GetFullPath(path), name));
            if (inspection.Value
                is not FindingInspection<ResourceLifecycleOccurrence>.Complete complete)
            {
                return inspection.Value
                    is FindingInspection<ResourceLifecycleOccurrence>.Failed
                        ? new LeakActionabilityAssembly(
                            name,
                            Opened: false,
                            TimedOut: false,
                            0,
                            new Dictionary<string, int>(),
                            [])
                        : new LeakActionabilityAssembly(
                            name,
                            Opened: true,
                            TimedOut: false,
                            0,
                            new Dictionary<string, int>(),
                            []);
            }

            var assessments = ResourceTriageAnalysis.Assess(complete);
            if (assessments.Length == 0)
                return new LeakActionabilityAssembly(name, Opened: true, TimedOut: false, 0, new Dictionary<string, int>(), []);

            var classCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var examples = new List<LeakActionabilityExample>();
            foreach (var assessment in assessments)
            {
                string cls = FormatActionability(
                    assessment.Actionability);
                classCounts[cls] = classCounts.GetValueOrDefault(cls) + 1;
                if (examples.Count < examplesPerAssembly)
                    examples.Add(new LeakActionabilityExample(
                        cls,
                        $"{assessment.Source.Payload.Method.DeclaringType.Name}::{assessment.Source.Payload.Method.Name}",
                        FormatBoundarySet(assessment.Boundaries)));
            }

            return new LeakActionabilityAssembly(name, Opened: true, TimedOut: false, assessments.Length, classCounts, examples);
        }
        // Per-assembly boundary on a background thread: convert any input failure (a directory, a
        // truncated PE, ...) into a failed row and keep sweeping the corpus.
        catch (Exception)
        {
            return new LeakActionabilityAssembly(name, Opened: false, TimedOut: false, 0, new Dictionary<string, int>(), []);
        }
    }

    static string FormatActionability(ResourceTriageActionability actionability)
        => actionability switch
        {
            ResourceTriageActionability.UntrustedActionable => Untrusted,
            ResourceTriageActionability.TrustedLowActionability => Trusted,
            _ => Unknown,
        };

    static string FormatBoundarySet(
        IReadOnlyList<ResourceTriageBoundaryAssessment> boundaries)
        => boundaries.Count == 0
            ? "(no-boundary)"
            : string.Join(
                " + ",
                boundaries
                    .Select(boundary =>
                        $"{Short(boundary.Evidence.Operation.DeclaringType.ToQualifiedDisplayString())}::{boundary.Evidence.Operation.Name}")
                    .Distinct(StringComparer.Ordinal));

    static string Short(string type)
    {
        if (type.Contains('.'))
            type = type[(type.LastIndexOf('.') + 1)..];
        int tick = type.IndexOf('`');
        return tick >= 0 ? type[..tick] : type;
    }

    public static string Format(LeakActionabilityReport report, int maxExamples, LeakActionabilityFormat format)
    {
        var output = new StringWriter();
        if (format == LeakActionabilityFormat.Markdown)
        {
            MarkoutSerializer.Serialize(
                BuildMarkdownView(report, maxExamples),
                output,
                new MarkdownFormatter(),
                LeakActionabilityViewContext.Default,
                new MarkoutWriterOptions());
        }
        else
        {
            MarkoutSerializer.Serialize(
                BuildTableView(report, maxExamples),
                output,
                new TableFormatter(showHeader: true),
                LeakActionabilityViewContext.Default,
                format == LeakActionabilityFormat.Tsv
                    ? new MarkoutWriterOptions { TableMode = MarkoutTableMode.Tsv, OmitEmptyJsonFields = true }
                    : new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl, OmitEmptyJsonFields = true });
        }

        string rendered = output.ToString();
        return format == LeakActionabilityFormat.Jsonl
            ? string.Join(Environment.NewLine, rendered.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)) + Environment.NewLine
            : rendered;
    }

    static IReadOnlyList<(string Metric, string Value)> SummaryRows(LeakActionabilityReport report)
        => new (string, string)[]
        {
            ("assemblies", report.Assemblies.Count.ToString()),
            ("opened", report.Assemblies.Count(a => a.Opened).ToString()),
            ("failed", report.Assemblies.Count(a => !a.Opened && !a.TimedOut).ToString()),
            ("timed out", report.Assemblies.Count(a => a.TimedOut).ToString()),
            ("exception-path candidates", report.Total.ToString()),
            ("untrusted (actionable)", report.TotalsByClass.GetValueOrDefault(Untrusted).ToString()),
        };

    // Classes in canonical order (Untrusted, Trusted, Unknown), skipping any that never fired.
    static IReadOnlyList<(string Class, string Count)> ClassRows(LeakActionabilityReport report)
        => [.. ClassOrder
            .Where(cls => report.TotalsByClass.ContainsKey(cls))
            .Select(cls => (cls, report.TotalsByClass[cls].ToString()))];

    static IReadOnlyList<(string Class, string Assembly, string Method, string BoundarySet)> ExampleRows(LeakActionabilityReport report, int maxExamples)
    {
        var rows = new List<(string, string, string, string)>();
        foreach (var cls in ClassOrder)
        {
            int taken = 0;
            foreach (var assembly in report.Assemblies)
            {
                foreach (var example in assembly.Examples.Where(e => e.Class == cls))
                {
                    if (taken >= maxExamples) break;
                    rows.Add((cls, assembly.Name, example.Method, example.BoundarySet));
                    taken++;
                }
                if (taken >= maxExamples) break;
            }
        }
        return rows;
    }

    static LeakActionabilityCardMarkdownView BuildMarkdownView(LeakActionabilityReport report, int maxExamples)
        => new()
        {
            Summary = [.. SummaryRows(report).Select(r => new LeakActionabilityMetricRow(r.Metric, r.Value))],
            ByClass = [.. ClassRows(report).Select(r => new LeakActionabilityClassRow(r.Class, r.Count))],
            Examples = [.. ExampleRows(report, maxExamples).Select(r => new LeakActionabilityExampleRow(r.Class, r.Assembly, r.Method, r.BoundarySet))],
        };

    static LeakActionabilityCardTableView BuildTableView(LeakActionabilityReport report, int maxExamples)
        => new()
        {
            Summary = [.. SummaryRows(report).Select(r => new LeakActionabilitySectionMetricRow("Summary", r.Metric, r.Value))],
            ByClass = [.. ClassRows(report).Select(r => new LeakActionabilitySectionClassRow("By class", r.Class, r.Count))],
            Examples = [.. ExampleRows(report, maxExamples).Select(r => new LeakActionabilitySectionExampleRow("Examples", r.Class, r.Assembly, r.Method, r.BoundarySet))],
        };
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
sealed class LeakActionabilityCardMarkdownView
{
    [MarkoutIgnore]
    public string Title => "Leak Actionability Corpus Sensor";

    [MarkoutSection(Name = "Summary")]
    public List<LeakActionabilityMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "By class", EmptyText = "None - no exception-path candidates in this corpus.")]
    public List<LeakActionabilityClassRow>? ByClass { get; init; }

    [MarkoutSection(Name = "Examples", EmptyText = "None")]
    public List<LeakActionabilityExampleRow>? Examples { get; init; }
}

[MarkoutSerializable]
sealed class LeakActionabilityCardTableView
{
    [MarkoutIgnore]
    public string Title => "Leak Actionability Corpus Sensor";

    [MarkoutSection(Name = "Summary")]
    public List<LeakActionabilitySectionMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "By class")]
    public List<LeakActionabilitySectionClassRow>? ByClass { get; init; }

    [MarkoutSection(Name = "Examples")]
    public List<LeakActionabilitySectionExampleRow>? Examples { get; init; }
}

[MarkoutSerializable]
sealed record LeakActionabilityMetricRow(string Metric, string Count);

[MarkoutSerializable]
sealed record LeakActionabilitySectionMetricRow(string Section, string Metric, string Count);

[MarkoutSerializable]
sealed record LeakActionabilityClassRow(string Class, string Count);

[MarkoutSerializable]
sealed record LeakActionabilitySectionClassRow(string Section, string Class, string Count);

[MarkoutSerializable]
sealed record LeakActionabilityExampleRow(string Class, string Assembly, string Method, string Boundaries);

[MarkoutSerializable]
sealed record LeakActionabilitySectionExampleRow(string Section, string Class, string Assembly, string Method, string Boundaries);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(LeakActionabilityCardMarkdownView))]
[MarkoutContext(typeof(LeakActionabilityCardTableView))]
[MarkoutContext(typeof(LeakActionabilityMetricRow))]
[MarkoutContext(typeof(LeakActionabilitySectionMetricRow))]
[MarkoutContext(typeof(LeakActionabilityClassRow))]
[MarkoutContext(typeof(LeakActionabilitySectionClassRow))]
[MarkoutContext(typeof(LeakActionabilityExampleRow))]
[MarkoutContext(typeof(LeakActionabilitySectionExampleRow))]
partial class LeakActionabilityViewContext : MarkoutSerializerContext
{
}
