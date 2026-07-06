using Markout;
using Markout.Formatting;

using ILInspector.Analysis;

namespace ILInspector.AnalysisHarness;

/// <summary>One example row: a bucket and the method it fired on.</summary>
public sealed record LifecycleExample(string Bucket, string Method);

/// <summary>Per-assembly census result: whether it opened, acquires observed, fact count, the
/// bucket histogram, and a few example methods per bucket.</summary>
public sealed record LifecycleAssembly(
    string Name,
    bool Opened,
    bool TimedOut,
    int AcquiresObserved,
    int Facts,
    IReadOnlyDictionary<string, int> Buckets,
    IReadOnlyList<LifecycleExample> Examples);

/// <summary>A resource-lifecycle census report: per-assembly rows plus aggregate bucket totals.</summary>
public sealed record LifecycleReport(
    IReadOnlyList<LifecycleAssembly> Assemblies,
    IReadOnlyDictionary<string, int> TotalsByBucket,
    int TotalAcquires,
    int TotalFacts);

public enum LifecycleFormat { Markdown, Tsv, Jsonl }

/// <summary>
/// The resource-lifecycle corpus sensor (#2439 Slice 1): sweeps the measurement-only
/// <see cref="ResourceLifecycleCensus"/> over a fixed corpus and reports the census - acquires
/// observed, the candidate/suppression bucket histogram, and example methods per bucket. This is
/// pure measurement: there is no product surface and it changes no <see cref="LeakTriageAnalyzer"/>
/// finding. It exists so the size and shape of each bucket (normal-path vs exception-path leaks,
/// ownership/alias/cross-method suppressions) can be measured on ArrayPool-heavy packages before
/// any bucket graduates to a user-facing finding (Slice 4).
/// </summary>
public static class ResourceLifecycleSensor
{
    static readonly IReadOnlySet<string> CandidateBuckets = new HashSet<string>(StringComparer.Ordinal)
    {
        ResourceLifecycleBuckets.NormalPathLeakCandidate,
        ResourceLifecycleBuckets.ExceptionPathLeakCandidate,
        ResourceLifecycleBuckets.UseAfterReturnCandidate,
        ResourceLifecycleBuckets.DoubleReturnCandidate,
    };

    public static LifecycleReport Measure(IReadOnlyList<string> assemblyPaths, int perAssemblyTimeoutSeconds = 120, int examplesPerAssembly = 5)
    {
        var assemblies = new List<LifecycleAssembly>(assemblyPaths.Count);
        foreach (var path in assemblyPaths)
            assemblies.Add(MeasureWithTimeout(path, perAssemblyTimeoutSeconds, examplesPerAssembly));
        assemblies.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
            foreach (var (bucket, count) in assembly.Buckets)
                totals[bucket] = totals.GetValueOrDefault(bucket) + count;

        return new LifecycleReport(
            assemblies,
            totals,
            assemblies.Sum(a => a.AcquiresObserved),
            assemblies.Sum(a => a.Facts));
    }

    // Bound each assembly so one pathological input cannot hang the sweep; the census is already
    // fail-closed per method, so this is belt-and-braces.
    static LifecycleAssembly MeasureWithTimeout(string path, int timeoutSeconds, int examplesPerAssembly)
    {
        string name = Path.GetFileName(path);
        LifecycleAssembly? result = null;
        var worker = new Thread(() => result = MeasureOne(path, examplesPerAssembly)) { IsBackground = true };
        worker.Start();
        if (worker.Join(TimeSpan.FromSeconds(timeoutSeconds)) && result is not null)
            return result;
        return new LifecycleAssembly(name, Opened: false, TimedOut: true, 0, 0, new Dictionary<string, int>(), []);
    }

    static LifecycleAssembly MeasureOne(string path, int examplesPerAssembly)
    {
        string name = Path.GetFileName(path);
        try
        {
            var census = ResourceLifecycleCensus.CensusAssembly(path);
            var buckets = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var examples = new List<LifecycleExample>();
            var seenPerBucket = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var fact in census.Facts)
            {
                buckets[fact.Bucket] = buckets.GetValueOrDefault(fact.Bucket) + 1;
                int seen = seenPerBucket.GetValueOrDefault(fact.Bucket);
                if (seen < examplesPerAssembly)
                {
                    examples.Add(new LifecycleExample(fact.Bucket, $"{fact.Method.DeclaringType.Name}::{fact.Method.Name}"));
                    seenPerBucket[fact.Bucket] = seen + 1;
                }
            }
            return new LifecycleAssembly(name, Opened: true, TimedOut: false, census.AcquiresObserved, census.Facts.Length, buckets, examples);
        }
        // Per-assembly boundary on a background thread: convert any input failure (directory,
        // truncated PE, ...) into a failed row and keep sweeping.
        catch (Exception)
        {
            return new LifecycleAssembly(name, Opened: false, TimedOut: false, 0, 0, new Dictionary<string, int>(), []);
        }
    }

    public static string Format(LifecycleReport report, int maxExamples, LifecycleFormat format)
    {
        var output = new StringWriter();
        if (format == LifecycleFormat.Markdown)
        {
            MarkoutSerializer.Serialize(
                BuildMarkdownView(report, maxExamples),
                output,
                new MarkdownFormatter(),
                LifecycleViewContext.Default,
                new MarkoutWriterOptions());
        }
        else
        {
            MarkoutSerializer.Serialize(
                BuildTableView(report, maxExamples),
                output,
                new TableFormatter(showHeader: true),
                LifecycleViewContext.Default,
                format == LifecycleFormat.Tsv
                    ? new MarkoutWriterOptions { TableMode = MarkoutTableMode.Tsv }
                    : new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl });
        }

        string rendered = output.ToString();
        return format == LifecycleFormat.Jsonl
            ? string.Join(Environment.NewLine, rendered.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)) + Environment.NewLine
            : rendered;
    }

    static IReadOnlyList<(string Metric, string Value)> SummaryRows(LifecycleReport report)
    {
        int candidates = report.TotalsByBucket.Where(kv => CandidateBuckets.Contains(kv.Key)).Sum(kv => kv.Value);
        return new (string, string)[]
        {
            ("assemblies", report.Assemblies.Count.ToString()),
            ("opened", report.Assemblies.Count(a => a.Opened).ToString()),
            ("failed", report.Assemblies.Count(a => !a.Opened && !a.TimedOut).ToString()),
            ("timed out", report.Assemblies.Count(a => a.TimedOut).ToString()),
            ("acquires observed", report.TotalAcquires.ToString()),
            ("candidate facts", candidates.ToString()),
            ("suppressed facts", (report.TotalFacts - candidates).ToString()),
            ("total facts", report.TotalFacts.ToString()),
        };
    }

    // Buckets in canonical order (candidates, then suppressions), skipping any that never fired.
    static IReadOnlyList<(string Bucket, string Count)> BucketRows(LifecycleReport report)
        => [.. ResourceLifecycleBuckets.All
            .Where(bucket => report.TotalsByBucket.ContainsKey(bucket))
            .Select(bucket => (bucket, report.TotalsByBucket[bucket].ToString()))];

    // Up to maxExamples example methods per bucket, gathered across assemblies in bucket order.
    static IReadOnlyList<(string Bucket, string Assembly, string Method)> ExampleRows(LifecycleReport report, int maxExamples)
    {
        var rows = new List<(string Bucket, string Assembly, string Method)>();
        foreach (var bucket in ResourceLifecycleBuckets.All)
        {
            int taken = 0;
            foreach (var assembly in report.Assemblies)
            {
                foreach (var example in assembly.Examples.Where(e => e.Bucket == bucket))
                {
                    if (taken >= maxExamples)
                        break;
                    rows.Add((bucket, assembly.Name, example.Method));
                    taken++;
                }
                if (taken >= maxExamples)
                    break;
            }
        }
        return rows;
    }

    static LifecycleCardMarkdownView BuildMarkdownView(LifecycleReport report, int maxExamples)
        => new()
        {
            Summary = [.. SummaryRows(report).Select(r => new LifecycleMetricRow(r.Metric, r.Value))],
            ByBucket = [.. BucketRows(report).Select(r => new LifecycleBucketRow(r.Bucket, r.Count))],
            Examples = [.. ExampleRows(report, maxExamples).Select(r => new LifecycleExampleRow(r.Bucket, r.Assembly, r.Method))],
        };

    static LifecycleCardTableView BuildTableView(LifecycleReport report, int maxExamples)
        => new()
        {
            Summary = [.. SummaryRows(report).Select(r => new LifecycleSectionMetricRow("Summary", r.Metric, r.Value))],
            ByBucket = [.. BucketRows(report).Select(r => new LifecycleSectionBucketRow("By bucket", r.Bucket, r.Count))],
            Examples = [.. ExampleRows(report, maxExamples).Select(r => new LifecycleSectionExampleRow("Examples", r.Bucket, r.Assembly, r.Method))],
        };
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
sealed class LifecycleCardMarkdownView
{
    [MarkoutIgnore]
    public string Title => "Resource Lifecycle Census";

    [MarkoutSection(Name = "Summary")]
    public List<LifecycleMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "By bucket", EmptyText = "None - no recognized ArrayPool acquire reached a candidate or suppression bucket.")]
    public List<LifecycleBucketRow>? ByBucket { get; init; }

    [MarkoutSection(Name = "Examples", EmptyText = "None")]
    public List<LifecycleExampleRow>? Examples { get; init; }
}

[MarkoutSerializable]
sealed class LifecycleCardTableView
{
    [MarkoutIgnore]
    public string Title => "Resource Lifecycle Census";

    [MarkoutSection(Name = "Summary")]
    public List<LifecycleSectionMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "By bucket")]
    public List<LifecycleSectionBucketRow>? ByBucket { get; init; }

    [MarkoutSection(Name = "Examples")]
    public List<LifecycleSectionExampleRow>? Examples { get; init; }
}

[MarkoutSerializable]
sealed record LifecycleMetricRow(string Metric, string Count);

[MarkoutSerializable]
sealed record LifecycleSectionMetricRow(string Section, string Metric, string Count);

[MarkoutSerializable]
sealed record LifecycleBucketRow(string Bucket, string Count);

[MarkoutSerializable]
sealed record LifecycleSectionBucketRow(string Section, string Bucket, string Count);

[MarkoutSerializable]
sealed record LifecycleExampleRow(string Bucket, string Assembly, string Method);

[MarkoutSerializable]
sealed record LifecycleSectionExampleRow(string Section, string Bucket, string Assembly, string Method);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(LifecycleCardMarkdownView))]
[MarkoutContext(typeof(LifecycleCardTableView))]
[MarkoutContext(typeof(LifecycleMetricRow))]
[MarkoutContext(typeof(LifecycleSectionMetricRow))]
[MarkoutContext(typeof(LifecycleBucketRow))]
[MarkoutContext(typeof(LifecycleSectionBucketRow))]
[MarkoutContext(typeof(LifecycleExampleRow))]
[MarkoutContext(typeof(LifecycleSectionExampleRow))]
partial class LifecycleViewContext : MarkoutSerializerContext
{
}
