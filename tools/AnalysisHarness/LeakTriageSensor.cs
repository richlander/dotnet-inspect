using Markout;
using Markout.Formatting;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

/// <summary>One example row: its shape, method, and optional drilldown evidence.</summary>
public sealed record LeakTriageExample(
    string Shape,
    string Method,
    string Evidence = "",
    string RentOffset = "",
    string ILOffset = "");

/// <summary>Per-assembly leak-triage result: whether it opened, its finding count, and the shapes hit with a few example methods.</summary>
public sealed record LeakTriageAssembly(
    string Name,
    bool Opened,
    bool TimedOut,
    int Findings,
    IReadOnlyDictionary<string, int> Shapes,
    IReadOnlyList<LeakTriageExample> Examples,
    int Candidates = 0,
    IReadOnlyDictionary<string, int>? CandidateShapes = null,
    IReadOnlyList<LeakTriageExample>? CandidateExamples = null);

/// <summary>A leak-triage corpus report: per-assembly rows plus aggregate shape totals.</summary>
public sealed record LeakTriageReport(
    IReadOnlyList<LeakTriageAssembly> Assemblies,
    IReadOnlyDictionary<string, int> TotalsByShape,
    int Total,
    IReadOnlyDictionary<string, int>? CandidateTotalsByShape = null,
    int CandidateTotal = 0);

public enum LeakTriageFormat { Markdown, Tsv, Jsonl }

/// <summary>
/// The leak-triage corpus sensor: sweeps <see cref="LeakTriageAnalyzer"/> over a fixed corpus
/// and reports where the fail-closed ArrayPool analysis fires - total findings, finding shape
/// histogram, candidate/suppression buckets, and example methods. This is the evidence engine
/// that must show a non-zero, high-precision signal before any user-facing `Leak Triage` section
/// is wired (#1992): the analyzer is precision-first, so an empty findings card plus non-empty
/// candidate buckets means recall (not the section) is the next lever. There is no product
/// surface here - the harness measures. The card is a single-run census with no baseline, so it
/// renders as plain sectioned rows (no composite/delta cells); a `--diff-baseline` mode is the
/// natural home for those.
/// </summary>
public static class LeakTriageSensor
{
    public static LeakTriageReport Measure(IReadOnlyList<string> assemblyPaths, int perAssemblyTimeoutSeconds = 120, int examplesPerAssembly = 5)
    {
        var assemblies = new List<LeakTriageAssembly>(assemblyPaths.Count);
        foreach (var path in assemblyPaths)
            assemblies.Add(MeasureWithTimeout(path, perAssemblyTimeoutSeconds, examplesPerAssembly));
        assemblies.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var candidateTotals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
        {
            foreach (var (shape, count) in assembly.Shapes)
                totals[shape] = totals.GetValueOrDefault(shape) + count;
            foreach (var (shape, count) in assembly.CandidateShapes ?? new Dictionary<string, int>())
                candidateTotals[shape] = candidateTotals.GetValueOrDefault(shape) + count;
        }

        return new LeakTriageReport(
            assemblies,
            totals,
            assemblies.Sum(a => a.Findings),
            candidateTotals,
            assemblies.Sum(a => a.Candidates));
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
            var result = LeakTriageAnalyzer.AnalyzeAssemblyDetailed(path);
            var shapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var examples = new List<LeakTriageExample>();
            foreach (var finding in result.Findings)
            {
                shapes[finding.Shape] = shapes.GetValueOrDefault(finding.Shape) + 1;
                if (examples.Count < examplesPerAssembly)
                    examples.Add(new LeakTriageExample(finding.Shape, $"{finding.Method.DeclaringType.Name}::{finding.Method.Name}"));
            }
            var candidateShapes = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var candidateExamples = new List<LeakTriageExample>();
            foreach (var candidate in result.Candidates)
            {
                candidateShapes[candidate.Shape] = candidateShapes.GetValueOrDefault(candidate.Shape) + 1;
                if (candidateExamples.Count < examplesPerAssembly)
                    candidateExamples.Add(new LeakTriageExample(
                        candidate.Shape,
                        $"{candidate.Method.DeclaringType.Name}::{candidate.Method.Name}",
                        candidate.Evidence,
                        FormatOffset(candidate.RentOffset),
                        FormatOffset(candidate.ILOffset)));
            }

            return new LeakTriageAssembly(
                name,
                Opened: true,
                TimedOut: false,
                result.Findings.Length,
                shapes,
                examples,
                result.Candidates.Length,
                candidateShapes,
                candidateExamples);
        }
        // This is the per-assembly boundary running on a background thread, so ANY unhandled
        // failure would terminate the whole sweep (a corpus path that is a directory throws
        // UnauthorizedAccessException, a truncated PE throws BadImageFormatException, etc.). A
        // sensor over arbitrary user-supplied paths must convert every input failure into a
        // failed row and keep sweeping.
        catch (Exception)
        {
            return new LeakTriageAssembly(name, Opened: false, TimedOut: false, 0, new Dictionary<string, int>(), []);
        }
    }

    public static string Format(LeakTriageReport report, int maxExamples, LeakTriageFormat format)
    {
        var output = new StringWriter();
        if (format == LeakTriageFormat.Markdown)
        {
            MarkoutSerializer.Serialize(
                BuildMarkdownView(report, maxExamples),
                output,
                new MarkdownFormatter(),
                LeakTriageViewContext.Default,
                new MarkoutWriterOptions());
        }
        else
        {
            MarkoutSerializer.Serialize(
                BuildTableView(report, maxExamples),
                output,
                new TableFormatter(showHeader: true),
                LeakTriageViewContext.Default,
                TableWriterOptions(format));
        }

        string rendered = output.ToString();
        return format == LeakTriageFormat.Jsonl
            ? string.Join(Environment.NewLine, rendered.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)) + Environment.NewLine
            : rendered;
    }

    static MarkoutWriterOptions TableWriterOptions(LeakTriageFormat format)
        => format switch
        {
            LeakTriageFormat.Tsv => new MarkoutWriterOptions
            {
                TableMode = MarkoutTableMode.Tsv,
                JsonTypedValues = true,
                OmitEmptyJsonFields = true,
            },
            LeakTriageFormat.Jsonl => new MarkoutWriterOptions
            {
                TableMode = MarkoutTableMode.Jsonl,
                JsonTypedValues = true,
                OmitEmptyJsonFields = true,
            },
            _ => throw new InvalidOperationException($"Unsupported table format '{format}'."),
        };

    static IReadOnlyList<(string Metric, string Value)> SummaryRows(LeakTriageReport report)
        => new (string, string)[]
        {
            ("assemblies", report.Assemblies.Count.ToString()),
            ("opened", report.Assemblies.Count(a => a.Opened).ToString()),
            ("failed", report.Assemblies.Count(a => !a.Opened && !a.TimedOut).ToString()),
            ("timed out", report.Assemblies.Count(a => a.TimedOut).ToString()),
            ("total findings", report.Total.ToString()),
            ("total candidates", report.CandidateTotal.ToString()),
        };

    static IReadOnlyList<(string Shape, string Count)> ShapeRows(LeakTriageReport report)
        => [.. report.TotalsByShape
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value.ToString()))];

    static IReadOnlyList<(string Shape, string Count)> CandidateShapeRows(LeakTriageReport report)
        => [.. (report.CandidateTotalsByShape ?? new Dictionary<string, int>())
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => (kv.Key, kv.Value.ToString()))];

    static IReadOnlyList<(string Assembly, string Shape, string Method)> FindingRows(LeakTriageReport report, int maxExamples)
        => [.. report.Assemblies
            .Where(a => a.Findings > 0)
            .OrderByDescending(a => a.Findings)
            .SelectMany(a => a.Examples.Take(maxExamples).Select(e => (a.Name, e.Shape, e.Method)))];

    static IReadOnlyList<(string Assembly, string Shape, string Method)> CandidateRows(LeakTriageReport report, int maxExamples)
        => [.. report.Assemblies
            .Where(a => a.Candidates > 0)
            .OrderByDescending(a => a.Candidates)
            .SelectMany(a => (a.CandidateExamples ?? []).Take(maxExamples).Select(e => (a.Name, e.Shape, e.Method)))];

    static IReadOnlyList<(string Assembly, string Shape, string Method, string Evidence, string RentOffset, string ILOffset)> CandidateDetailRows(
        LeakTriageReport report,
        int maxExamples)
        => [.. report.Assemblies
            .Where(a => a.Candidates > 0)
            .OrderByDescending(a => a.Candidates)
            .SelectMany(a => (a.CandidateExamples ?? []).Take(maxExamples)
                .Select(e => (a.Name, e.Shape, e.Method, e.Evidence, e.RentOffset, e.ILOffset)))];

    static string FormatOffset(int? offset)
        => offset is { } value ? $"IL_{value:X4}" : "";

    static LeakTriageCardMarkdownView BuildMarkdownView(LeakTriageReport report, int maxExamples)
        => new()
        {
            Summary = [.. SummaryRows(report).Select(r => new LeakTriageMetricRow(r.Metric, r.Value))],
            ByShape = [.. ShapeRows(report).Select(r => new LeakTriageShapeRow(r.Shape, r.Count))],
            Findings = [.. FindingRows(report, maxExamples).Select(r => new LeakTriageFindingRow(r.Assembly, r.Shape, r.Method))],
            CandidateBuckets = [.. CandidateShapeRows(report).Select(r => new LeakTriageShapeRow(r.Shape, r.Count))],
            Candidates = [.. CandidateRows(report, maxExamples).Select(r => new LeakTriageFindingRow(r.Assembly, r.Shape, r.Method))],
        };

    static LeakTriageCardTableView BuildTableView(LeakTriageReport report, int maxExamples)
        => new()
        {
            Summary = [.. SummaryRows(report).Select(r => new LeakTriageSectionMetricRow("Summary", r.Metric, r.Value))],
            ByShape = [.. ShapeRows(report).Select(r => new LeakTriageSectionShapeRow("By shape", r.Shape, r.Count))],
            Findings = [.. FindingRows(report, maxExamples).Select(r => new LeakTriageSectionFindingRow("Findings", r.Assembly, r.Shape, r.Method))],
            CandidateBuckets = [.. CandidateShapeRows(report).Select(r => new LeakTriageSectionShapeRow("Candidate buckets", r.Shape, r.Count))],
            Candidates = [.. CandidateDetailRows(report, maxExamples)
                .Select(r => new LeakTriageSectionCandidateRow(
                    "Candidates",
                    r.Assembly,
                    r.Shape,
                    r.Method,
                    r.Evidence,
                    r.RentOffset,
                    r.ILOffset))],
        };
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
sealed class LeakTriageCardMarkdownView
{
    [MarkoutIgnore]
    public string Title => "Leak Triage Corpus Sensor";

    [MarkoutSection(Name = "Summary")]
    public List<LeakTriageMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "By shape", EmptyText = "None - the fail-closed gates suppressed every candidate; broaden recall, not the section.")]
    public List<LeakTriageShapeRow>? ByShape { get; init; }

    [MarkoutSection(Name = "Findings", EmptyText = "None")]
    public List<LeakTriageFindingRow>? Findings { get; init; }

    [MarkoutSection(Name = "Candidate buckets", EmptyText = "None")]
    public List<LeakTriageShapeRow>? CandidateBuckets { get; init; }

    [MarkoutSection(Name = "Candidates", EmptyText = "None")]
    public List<LeakTriageFindingRow>? Candidates { get; init; }
}

[MarkoutSerializable]
sealed class LeakTriageCardTableView
{
    [MarkoutIgnore]
    public string Title => "Leak Triage Corpus Sensor";

    [MarkoutSection(Name = "Summary")]
    public List<LeakTriageSectionMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "By shape")]
    public List<LeakTriageSectionShapeRow>? ByShape { get; init; }

    [MarkoutSection(Name = "Findings")]
    public List<LeakTriageSectionFindingRow>? Findings { get; init; }

    [MarkoutSection(Name = "Candidate buckets")]
    public List<LeakTriageSectionShapeRow>? CandidateBuckets { get; init; }

    [MarkoutSection(Name = "Candidates")]
    public List<LeakTriageSectionCandidateRow>? Candidates { get; init; }
}

[MarkoutSerializable]
sealed record LeakTriageMetricRow(string Metric, string Count);

[MarkoutSerializable]
sealed record LeakTriageSectionMetricRow(string Section, string Metric, string Count);

[MarkoutSerializable]
sealed record LeakTriageShapeRow(string Shape, string Count);

[MarkoutSerializable]
sealed record LeakTriageSectionShapeRow(string Section, string Shape, string Count);

[MarkoutSerializable]
sealed record LeakTriageFindingRow(string Assembly, string Shape, string Method);

[MarkoutSerializable]
sealed record LeakTriageSectionFindingRow(string Section, string Assembly, string Shape, string Method);

[MarkoutSerializable]
sealed record LeakTriageSectionCandidateRow(
    string Section,
    string Assembly,
    string Shape,
    string Method,
    string Evidence,
    string RentOffset,
    string ILOffset);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(LeakTriageCardMarkdownView))]
[MarkoutContext(typeof(LeakTriageCardTableView))]
[MarkoutContext(typeof(LeakTriageMetricRow))]
[MarkoutContext(typeof(LeakTriageSectionMetricRow))]
[MarkoutContext(typeof(LeakTriageShapeRow))]
[MarkoutContext(typeof(LeakTriageSectionShapeRow))]
[MarkoutContext(typeof(LeakTriageFindingRow))]
[MarkoutContext(typeof(LeakTriageSectionFindingRow))]
[MarkoutContext(typeof(LeakTriageSectionCandidateRow))]
partial class LeakTriageViewContext : MarkoutSerializerContext
{
}
