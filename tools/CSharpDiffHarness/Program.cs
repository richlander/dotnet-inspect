using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;

using ILInspector.DiffHarnessCommon;
using ILInspector.Decompiler;
using Markout;
using Markout.Formatting;

const string Usage =
    """
    csharp-diff-harness <old-assembly> <new-assembly> [--max-examples N]
    csharp-diff-harness --pair <old-assembly> <new-assembly> [--pair <old-assembly> <new-assembly>...] [--max-examples N]
    csharp-diff-harness --pairs <manifest.tsv> [--max-examples N]
    csharp-diff-harness ... [--format markdown|tsv|jsonl]
    csharp-diff-harness ... [--emit-snapshot <file>] [--diff-baseline <file>]

      Emits a small C# Diff card over paired assemblies:
      - exact and changed pair counts;
      - C# diff row and failure counts;
      - failure buckets;
      - top change IDs and operation kinds;
      - capped examples rendered through CSharpDiffPrinter.

      Pair manifests use one old/new assembly pair per line, separated by a tab.
      Empty lines and lines beginning with # are ignored. Relative paths are
      resolved from the manifest directory.
    """;

if (args.Contains("--help") || args.Contains("-h"))
{
    Console.Error.WriteLine(Usage);
    return 0;
}

var pairs = new List<AssemblyPair>();
var positional = new List<string>();
string? pairsManifest = null;
string? emitSnapshotPath = null;
string? diffBaselinePath = null;
int maxExamples = 5;
OutputFormat outputFormat = OutputFormat.Markdown;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--pair":
            if (i + 2 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal) || args[i + 2].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("--pair requires old and new assembly paths.");
                return 2;
            }

            pairs.Add(new AssemblyPair(args[++i], args[++i]));
            break;
        case "--pairs":
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("--pairs requires a manifest path.");
                return 2;
            }

            pairsManifest = args[++i];
            break;
        case "--max-examples":
            if (i + 1 >= args.Length || !int.TryParse(args[++i], out maxExamples) || maxExamples < 0)
            {
                Console.Error.WriteLine("--max-examples requires a non-negative integer.");
                return 2;
            }

            break;
        case "--format":
            if (i + 1 >= args.Length || !DiffHarnessCommon.TryParseOutputFormat(args[++i], out outputFormat))
            {
                Console.Error.WriteLine("--format requires one of: markdown, tsv, jsonl.");
                return 2;
            }

            break;
        case "--emit-snapshot":
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("--emit-snapshot requires a file path.");
                return 2;
            }

            emitSnapshotPath = args[++i];
            break;
        case "--diff-baseline":
            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine("--diff-baseline requires a file path.");
                return 2;
            }

            diffBaselinePath = args[++i];
            break;
        default:
            if (!args[i].StartsWith("-", StringComparison.Ordinal))
            {
                positional.Add(args[i]);
                break;
            }

            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            Console.Error.WriteLine(Usage);
            return 2;
    }
}

if (positional.Count != 0)
{
    if (positional.Count != 2)
    {
        Console.Error.WriteLine("Positional usage requires exactly old and new assembly paths.");
        return 2;
    }

    pairs.Insert(0, new AssemblyPair(positional[0], positional[1]));
}

if (pairsManifest is not null)
{
    if (pairs.Count != 0)
    {
        Console.Error.WriteLine("--pairs cannot be combined with positional pairs or --pair.");
        return 2;
    }

    if (!File.Exists(pairsManifest))
    {
        Console.Error.WriteLine($"Pair manifest not found: {pairsManifest}");
        return 2;
    }

    try
    {
        pairs.AddRange(DiffHarnessCommon.ReadManifest(pairsManifest));
    }
    catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

if (pairs.Count == 0)
{
    Console.Error.WriteLine(Usage);
    return 2;
}

foreach (var pair in pairs)
{
    if (!File.Exists(pair.OldPath))
    {
        Console.Error.WriteLine($"Old assembly not found: {pair.OldPath}");
        return 2;
    }

    if (!File.Exists(pair.NewPath))
    {
        Console.Error.WriteLine($"New assembly not found: {pair.NewPath}");
        return 2;
    }
}

try
{
    var cards = pairs.Select(BuildPairCard).ToImmutableArray();
    var snapshot = BuildSnapshot(cards, maxExamples);
    BaselineComparison? comparison = null;

    if (diffBaselinePath is not null)
    {
        if (!File.Exists(diffBaselinePath))
        {
            Console.Error.WriteLine($"Baseline snapshot not found: {diffBaselinePath}");
            return 2;
        }

        comparison = CompareSnapshots(ReadSnapshot(diffBaselinePath), snapshot);
    }

    if (emitSnapshotPath is not null)
        File.WriteAllText(emitSnapshotPath, JsonSerializer.Serialize(snapshot, SnapshotJson.Options) + Environment.NewLine);

    Console.Write(FormatCard(cards, maxExamples, outputFormat, comparison));
    return comparison?.HasRegressions == true ? 1 : 0;
}
catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or BadImageFormatException or JsonException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static CSharpDiffPairCard BuildPairCard(AssemblyPair pair)
{
    var result = CSharpBodyDiff.CompareAssemblies(pair.OldPath, pair.NewPath);
    var failures = new Dictionary<string, int>(StringComparer.Ordinal);
    var changeIds = new Dictionary<string, int>(StringComparer.Ordinal);
    var operationKinds = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var failure in result.FailureRows.IsDefault ? [] : result.FailureRows)
        Increment(failures, failure.Message);
    foreach (var row in result.Rows.IsDefault ? [] : result.Rows)
    {
        Increment(changeIds, row.ChangeId);
        var kind = row.NewOperation?.Kind ?? row.OldOperation?.Kind;
        if (kind is not null)
            Increment(operationKinds, kind.ToString()!);
    }

    var memberKeys = new HashSet<string>(StringComparer.Ordinal);
    foreach (var row in result.Rows.IsDefault ? [] : result.Rows)
        memberKeys.Add(row.StableMemberKey);
    foreach (var failure in result.FailureRows.IsDefault ? [] : result.FailureRows)
        memberKeys.Add(failure.StableMemberKey);

    return new CSharpDiffPairCard(
        pair.OldPath,
        pair.NewPath,
        new CSharpDiffCard(
            result.IsExact,
            memberKeys.Count,
            result.Rows.IsDefault ? 0 : result.Rows.Length,
            result.FailureRows.IsDefault ? 0 : result.FailureRows.Length,
            Buckets(failures),
            Buckets(changeIds),
            Buckets(operationKinds),
            ExampleGroups(result)));
}

static ImmutableArray<CSharpDiffExampleGroup> ExampleGroups(CSharpBodyDiffResult result)
{
    if (result.Rows.IsDefaultOrEmpty && result.FailureRows.IsDefaultOrEmpty)
        return [];

    var groups = new Dictionary<string, ExampleGroup>(StringComparer.Ordinal);
    foreach (var row in result.Rows.IsDefault ? [] : result.Rows)
    {
        ref var group = ref CollectionsMarshal.GetValueRefOrAddDefault(groups, row.StableMemberKey, out bool exists);
        group ??= new ExampleGroup(row.Member);
        group.Rows.Add(row);
    }

    foreach (var failure in result.FailureRows.IsDefault ? [] : result.FailureRows)
    {
        ref var group = ref CollectionsMarshal.GetValueRefOrAddDefault(groups, failure.StableMemberKey, out bool exists);
        group ??= new ExampleGroup(failure.Member);
        group.Failures.Add(failure);
    }

    return [.. groups
        .OrderBy(pair => pair.Value.Member, StringComparer.Ordinal)
        .Select(pair => new CSharpDiffExampleGroup(
            pair.Value.Member,
            pair.Value.Rows.ToImmutableArray(),
            pair.Value.Failures.ToImmutableArray()))];
}

static ImmutableArray<CardBucket> Buckets(Dictionary<string, int> counts)
    => [.. counts
        .OrderByDescending(pair => pair.Value)
        .ThenBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => new CardBucket(pair.Key, pair.Value))];

static void Increment(Dictionary<string, int> counts, string key)
    => counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;

static void IncrementBy(Dictionary<string, int> counts, string key, int amount)
    => counts[key] = counts.TryGetValue(key, out int count) ? count + amount : amount;

static string FormatCard(ImmutableArray<CSharpDiffPairCard> pairs, int maxExamples, OutputFormat format, BaselineComparison? comparison = null)
{
    var output = new StringWriter { NewLine = "\n" };
    if (format == OutputFormat.Markdown)
    {
        MarkoutSerializer.Serialize(
            BuildMarkdownView(pairs, maxExamples, comparison),
            output,
            new MarkdownFormatter(),
            CSharpDiffCardViewContext.Default,
            new MarkoutWriterOptions());
    }
    else
    {
        MarkoutSerializer.Serialize(
            BuildTableView(pairs, maxExamples, comparison),
            output,
            new TableFormatter(showHeader: true),
            CSharpDiffCardViewContext.Default,
            WriterOptions(format));
    }

    string rendered = output.ToString();
    return format == OutputFormat.Jsonl
        ? string.Join(
            Environment.NewLine,
            rendered.ReplaceLineEndings("\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)) + Environment.NewLine
        : rendered;
}

static CSharpDiffSnapshot BuildSnapshot(ImmutableArray<CSharpDiffPairCard> pairs, int maxExamples)
{
    var card = Aggregate(pairs, maxExamples, snapshotPaths: true);
    return new CSharpDiffSnapshot(
        SchemaVersion: 1,
        Summary: new CSharpDiffSnapshotSummary(
            PairCount: pairs.Length,
            ExactPairCount: card.ExactPairCount,
            ChangedPairCount: card.ChangedPairCount,
            ChangedMemberCount: card.ChangedMemberCount,
            RowCount: card.RowCount,
            FailureCount: card.FailureCount),
        Pairs: pairs.Select(pair => new CSharpDiffSnapshotPair(
            Old: DiffHarnessCommon.RelativeSnapshotPath(pair.OldPath),
            New: DiffHarnessCommon.RelativeSnapshotPath(pair.NewPath),
            IsExact: pair.Card.IsExact,
            ChangedMemberCount: pair.Card.ChangedMemberCount,
            RowCount: pair.Card.RowCount,
            FailureCount: pair.Card.FailureCount)).ToArray(),
        FailureBuckets: card.FailureBuckets.ToArray(),
        ChangeIdBuckets: card.TopChangeIds.ToArray(),
        OperationKindBuckets: card.TopOperationKinds.ToArray(),
        Examples: card.Examples.ToArray());
}

static CSharpDiffSnapshot ReadSnapshot(string path)
{
    var snapshot = JsonSerializer.Deserialize<CSharpDiffSnapshot>(File.ReadAllText(path), SnapshotJson.Options)
        ?? throw new InvalidOperationException($"Could not read C# diff snapshot: {path}");
    if (snapshot.SchemaVersion != 1)
        throw new InvalidOperationException($"Unsupported C# diff snapshot schema version {snapshot.SchemaVersion}.");
    if (snapshot.Summary is null)
        throw new InvalidOperationException($"C# diff snapshot is missing a summary: {path}");
    return snapshot;
}

static BaselineComparison CompareSnapshots(CSharpDiffSnapshot baseline, CSharpDiffSnapshot current)
{
    var regressions = ImmutableArray.CreateBuilder<BaselineFinding>();
    var drift = ImmutableArray.CreateBuilder<BaselineFinding>();

    AddCountRegression(regressions, "Failures", baseline.Summary.FailureCount, current.Summary.FailureCount);
    AddCountRegression(regressions, "Changed pairs", baseline.Summary.ChangedPairCount, current.Summary.ChangedPairCount);
    AddCountRegression(regressions, "Rows", baseline.Summary.RowCount, current.Summary.RowCount);
    AddCountDropRegression(regressions, "Exact pairs", baseline.Summary.ExactPairCount, current.Summary.ExactPairCount);
    AddNewFailureBuckets(regressions, baseline.FailureBuckets ?? [], current.FailureBuckets ?? []);

    AddDrift(drift, "Pairs", baseline.Summary.PairCount, current.Summary.PairCount);
    AddDriftIfImproved(drift, "Exact pairs", baseline.Summary.ExactPairCount, current.Summary.ExactPairCount, improvementWhenCurrentIsLower: false);
    AddDriftIfImproved(drift, "Changed pairs", baseline.Summary.ChangedPairCount, current.Summary.ChangedPairCount, improvementWhenCurrentIsLower: true);
    AddDrift(drift, "Changed members", baseline.Summary.ChangedMemberCount, current.Summary.ChangedMemberCount);
    AddDriftIfImproved(drift, "Rows", baseline.Summary.RowCount, current.Summary.RowCount, improvementWhenCurrentIsLower: true);
    AddDriftIfImproved(drift, "Failures", baseline.Summary.FailureCount, current.Summary.FailureCount, improvementWhenCurrentIsLower: true);
    AddExistingFailureBucketDrift(drift, baseline.FailureBuckets ?? [], current.FailureBuckets ?? []);
    AddBucketDrift(drift, "Change ID", baseline.ChangeIdBuckets ?? [], current.ChangeIdBuckets ?? []);
    AddBucketDrift(drift, "Operation kind", baseline.OperationKindBuckets ?? [], current.OperationKindBuckets ?? []);

    return new BaselineComparison(baseline, current, regressions.ToImmutable(), drift.ToImmutable());
}

static void AddCountRegression(ImmutableArray<BaselineFinding>.Builder findings, string metric, int baseline, int current)
{
    if (current > baseline)
        findings.Add(new BaselineFinding("Regression", metric, Count(baseline), Count(current), "count increased"));
}

static void AddCountDropRegression(ImmutableArray<BaselineFinding>.Builder findings, string metric, int baseline, int current)
{
    if (current < baseline)
        findings.Add(new BaselineFinding("Regression", metric, Count(baseline), Count(current), "count dropped"));
}

static void AddNewFailureBuckets(ImmutableArray<BaselineFinding>.Builder findings, IReadOnlyList<CardBucket> baseline, IReadOnlyList<CardBucket> current)
{
    var baselineNames = baseline.Select(bucket => bucket.Name).ToHashSet(StringComparer.Ordinal);
    foreach (var bucket in current.Where(bucket => bucket.Count > 0 && !baselineNames.Contains(bucket.Name)))
        findings.Add(new BaselineFinding("Regression", $"Failure bucket `{bucket.Name}`", "0", Count(bucket.Count), "new failure bucket"));
}

static void AddExistingFailureBucketDrift(ImmutableArray<BaselineFinding>.Builder findings, IReadOnlyList<CardBucket> baseline, IReadOnlyList<CardBucket> current)
{
    var baselineNames = baseline.Select(bucket => bucket.Name).ToHashSet(StringComparer.Ordinal);
    AddBucketDrift(
        findings,
        "Failure bucket",
        baseline,
        current.Where(bucket => baselineNames.Contains(bucket.Name)).ToArray());
}

static void AddDrift(ImmutableArray<BaselineFinding>.Builder findings, string metric, int baseline, int current)
{
    if (current != baseline)
        findings.Add(new BaselineFinding("Drift", metric, Count(baseline), Count(current), "count changed"));
}

static void AddDriftIfImproved(ImmutableArray<BaselineFinding>.Builder findings, string metric, int baseline, int current, bool improvementWhenCurrentIsLower)
{
    bool improved = improvementWhenCurrentIsLower
        ? current < baseline
        : current > baseline;
    if (improved)
        findings.Add(new BaselineFinding(
            "Drift",
            metric,
            Count(baseline),
            Count(current),
            improvementWhenCurrentIsLower ? "count dropped" : "count increased"));
}

static void AddBucketDrift(ImmutableArray<BaselineFinding>.Builder findings, string metric, IReadOnlyList<CardBucket> baseline, IReadOnlyList<CardBucket> current)
{
    var baselineCounts = baseline.ToDictionary(bucket => bucket.Name, bucket => bucket.Count, StringComparer.Ordinal);
    var currentCounts = current.ToDictionary(bucket => bucket.Name, bucket => bucket.Count, StringComparer.Ordinal);
    foreach (string name in baselineCounts.Keys.Union(currentCounts.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
    {
        int oldCount = baselineCounts.GetValueOrDefault(name);
        int newCount = currentCounts.GetValueOrDefault(name);
        if (oldCount != newCount)
            findings.Add(new BaselineFinding("Drift", $"{metric} `{name}`", Count(oldCount), Count(newCount), "bucket count changed"));
    }
}

static CSharpDiffCardMarkdownView BuildMarkdownView(ImmutableArray<CSharpDiffPairCard> pairs, int maxExamples, BaselineComparison? comparison)
{
    var card = Aggregate(pairs, maxExamples);
    return new CSharpDiffCardMarkdownView
    {
        Summary = SummaryRows(pairs.Length, card),
        BaselineMetrics = comparison is null ? null : BaselineMetricRows(comparison),
        BaselineBuckets = comparison is null ? null : BaselineBucketRows(comparison),
        FailureBuckets = MarkdownBucketRows(card.FailureBuckets) ?? [],
        TopChangeIds = MarkdownBucketRows(card.TopChangeIds) ?? [],
        TopOperationKinds = MarkdownBucketRows(card.TopOperationKinds) ?? [],
        PairSummaries = [.. pairs.Select(pair => PairSummaryRow(pair))],
        BaselineFindings = comparison is null ? null : BaselineRows(comparison) ?? [],
        Examples = card.Examples.IsDefaultOrEmpty ? null : [.. card.Examples.Select(ExampleMarkdownRow)],
    };
}

static CSharpDiffCardTableView BuildTableView(ImmutableArray<CSharpDiffPairCard> pairs, int maxExamples, BaselineComparison? comparison)
{
    var card = Aggregate(pairs, maxExamples);
    return new CSharpDiffCardTableView
    {
        Summary = SectionedSummaryRows(pairs.Length, card),
        BaselineMetrics = comparison is null ? null : BaselineMetricRows(comparison),
        BaselineBuckets = comparison is null ? null : BaselineBucketRows(comparison),
        FailureBuckets = SectionedBucketRows("Failure buckets", card.FailureBuckets),
        TopChangeIds = SectionedBucketRows("Top change IDs", card.TopChangeIds),
        TopOperationKinds = SectionedBucketRows("Top operation kinds", card.TopOperationKinds),
        PairSummaries = [.. pairs.Select(pair => SectionedPairSummaryRow(pair))],
        BaselineFindings = comparison is null ? null : SectionedBaselineRows(comparison),
        Examples = card.Examples.IsDefaultOrEmpty ? null : [.. card.Examples.Select(ExampleTableRow)],
    };
}

static MarkoutWriterOptions WriterOptions(OutputFormat format)
    => format switch
    {
        OutputFormat.Markdown => new MarkoutWriterOptions(),
        OutputFormat.Tsv => new MarkoutWriterOptions { TableMode = MarkoutTableMode.Tsv, JsonTypedValues = true, OmitEmptyJsonFields = true },
        OutputFormat.Jsonl => new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl, JsonTypedValues = true, OmitEmptyJsonFields = true },
        _ => throw new InvalidOperationException($"Unsupported output format '{format}'."),
    };

static CSharpDiffCard Aggregate(ImmutableArray<CSharpDiffPairCard> pairs, int maxExamples, bool snapshotPaths = false)
{
    var failures = new Dictionary<string, int>(StringComparer.Ordinal);
    var changeIds = new Dictionary<string, int>(StringComparer.Ordinal);
    var operationKinds = new Dictionary<string, int>(StringComparer.Ordinal);
    var examples = ImmutableArray.CreateBuilder<CSharpDiffExample>();
    int exact = 0;
    int changed = 0;
    int changedMembers = 0;
    int rowCount = 0;
    int failureCount = 0;

    foreach (var pair in pairs)
    {
        if (pair.Card.IsExact)
            exact++;
        else
            changed++;
        changedMembers += pair.Card.ChangedMemberCount;
        rowCount += pair.Card.RowCount;
        failureCount += pair.Card.FailureCount;
        foreach (var bucket in pair.Card.FailureBuckets)
            IncrementBy(failures, bucket.Name, bucket.Count);
        foreach (var bucket in pair.Card.TopChangeIds)
            IncrementBy(changeIds, bucket.Name, bucket.Count);
        foreach (var bucket in pair.Card.TopOperationKinds)
            IncrementBy(operationKinds, bucket.Name, bucket.Count);
        foreach (var example in pair.Card.ExampleGroups)
        {
            if (examples.Count >= maxExamples)
                break;
            examples.Add(RenderExample(
                $"{DiffHarnessCommon.PathLabel(pair.OldPath, snapshotPaths, DiffHarnessCommon.RelativeSnapshotPath)} to {DiffHarnessCommon.PathLabel(pair.NewPath, snapshotPaths, DiffHarnessCommon.RelativeSnapshotPath)} :: {example.Member}",
                example));
        }
    }

    return new CSharpDiffCard(
        IsExact: changed == 0,
        ChangedMemberCount: changedMembers,
        RowCount: rowCount,
        FailureCount: failureCount,
        FailureBuckets: Buckets(failures),
        TopChangeIds: Buckets(changeIds),
        TopOperationKinds: Buckets(operationKinds),
        ExampleGroups: [])
    {
        ExactPairCount = exact,
        ChangedPairCount = changed,
        Examples = examples.ToImmutable(),
    };
}

static List<CSharpDiffMetricRow> SummaryRows(int pairCount, CSharpDiffCard card) =>
[
    new("Pairs", Count(pairCount)),
    new("Exact pairs", Count(card.ExactPairCount)),
    new("Changed pairs", Count(card.ChangedPairCount)),
    new("Changed members", Count(card.ChangedMemberCount)),
    new("Rows", Count(card.RowCount)),
    new("Failures", Count(card.FailureCount)),
];

static List<CSharpDiffSectionMetricRow> SectionedSummaryRows(int pairCount, CSharpDiffCard card)
    => [.. SummaryRows(pairCount, card).Select(row => new CSharpDiffSectionMetricRow("Summary", row.Metric, row.Count))];

static string Count(int count) => count.ToString(System.Globalization.CultureInfo.InvariantCulture);

static List<MetricChange<int>> BaselineMetricRows(BaselineComparison comparison) =>
[
    MetricContext("Pairs", comparison.Baseline.Summary.PairCount, comparison.Current.Summary.PairCount),
    MetricGoal("Exact pairs", comparison.Baseline.Summary.ExactPairCount, comparison.Current.Summary.ExactPairCount, "minimum exact pairs", Goal.Higher),
    MetricGoal("Changed pairs", comparison.Baseline.Summary.ChangedPairCount, comparison.Current.Summary.ChangedPairCount, "max changed pairs", Goal.Lower),
    MetricContext("Changed members", comparison.Baseline.Summary.ChangedMemberCount, comparison.Current.Summary.ChangedMemberCount),
    MetricGoal("Rows", comparison.Baseline.Summary.RowCount, comparison.Current.Summary.RowCount, "max rows", Goal.Lower),
    MetricGoal("Failures", comparison.Baseline.Summary.FailureCount, comparison.Current.Summary.FailureCount, "max failures", Goal.Lower),
];

static MetricChange<int> MetricContext(string name, int baseline, int current)
    => new(name, baseline, current);

static MetricChange<int> MetricGoal(string name, int baseline, int current, string targetLabel, Goal goal)
    => new(name, baseline, current, baseline, targetLabel) { Goal = goal };

static List<MultiSourceRow> BaselineBucketRows(BaselineComparison comparison) =>
[
    BucketChangeRow("Failure buckets (-)", comparison.Baseline.FailureBuckets ?? [], comparison.Current.FailureBuckets ?? [], Goal.Lower),
    BucketChangeRow("Change IDs", comparison.Baseline.ChangeIdBuckets ?? [], comparison.Current.ChangeIdBuckets ?? [], Goal.Context),
    BucketChangeRow("Operation kinds", comparison.Baseline.OperationKindBuckets ?? [], comparison.Current.OperationKindBuckets ?? [], Goal.Context),
];

static MultiSourceRow BucketChangeRow(string label, IReadOnlyList<CardBucket> baseline, IReadOnlyList<CardBucket> current, Goal goal)
{
    var (baselineSegments, currentSegments) = PairedSegments(baseline, current);
    return new(label, new Source("Change", new Change<Segments>(baselineSegments, currentSegments), new MarkoutCellFormat { Goal = goal }));
}

static (Segments Baseline, Segments Current) PairedSegments(IReadOnlyList<CardBucket> baseline, IReadOnlyList<CardBucket> current)
{
    var baselineCounts = baseline.ToDictionary(bucket => bucket.Name, bucket => bucket.Count, StringComparer.Ordinal);
    var currentCounts = current.ToDictionary(bucket => bucket.Name, bucket => bucket.Count, StringComparer.Ordinal);
    var labels = baselineCounts.Keys
        .Union(currentCounts.Keys, StringComparer.Ordinal)
        .Select(label => new
        {
            Label = label,
            Count = Math.Max(baselineCounts.GetValueOrDefault(label), currentCounts.GetValueOrDefault(label))
        })
        .Where(row => row.Count != 0)
        .OrderByDescending(row => row.Count)
        .ThenBy(row => row.Label, StringComparer.Ordinal)
        .Take(10)
        .Select(row => row.Label)
        .ToArray();

    return (
        new Segments([.. labels.Select(label => new Segment(label, baselineCounts.GetValueOrDefault(label)))]),
        new Segments([.. labels.Select(label => new Segment(label, currentCounts.GetValueOrDefault(label)))])
    );
}

static List<BaselineFindingView>? BaselineRows(BaselineComparison comparison)
    => comparison.Rows.IsDefaultOrEmpty
        ? null
        : [.. comparison.Rows.Select(row => new BaselineFindingView(row.Kind, row.Metric, row.Baseline, row.Current, row.Detail))];

static List<BaselineSectionFindingView>? SectionedBaselineRows(BaselineComparison comparison)
    => comparison.Rows.IsDefaultOrEmpty
        ? null
        : [.. comparison.Rows.Select(row => new BaselineSectionFindingView("Baseline findings", row.Kind, row.Metric, row.Baseline, row.Current, row.Detail))];

static CSharpDiffPairSummaryRow PairSummaryRow(CSharpDiffPairCard pair)
    => new(
        DiffHarnessCommon.DisplayPath(pair.OldPath),
        DiffHarnessCommon.DisplayPath(pair.NewPath),
        pair.Card.IsExact ? "yes" : "no",
        Count(pair.Card.ChangedMemberCount),
        Count(pair.Card.RowCount),
        Count(pair.Card.FailureCount));

static CSharpDiffSectionPairSummaryRow SectionedPairSummaryRow(CSharpDiffPairCard pair)
{
    var row = PairSummaryRow(pair);
    return new CSharpDiffSectionPairSummaryRow(
        "Pair summaries",
        row.Old,
        row.New,
        row.Exact,
        row.ChangedMembers,
        row.Rows,
        row.Failures);
}

static List<CSharpDiffBucketRow>? MarkdownBucketRows(ImmutableArray<CardBucket> buckets)
    => buckets.IsDefaultOrEmpty
        ? null
        : [.. buckets.Take(10).Select(bucket => new CSharpDiffBucketRow(bucket.Name, Count(bucket.Count)))];

static List<CSharpDiffSectionBucketRow>? SectionedBucketRows(string section, ImmutableArray<CardBucket> buckets)
    => buckets.IsDefaultOrEmpty
        ? null
        : [.. buckets.Take(10).Select(bucket => new CSharpDiffSectionBucketRow(section, bucket.Name, Count(bucket.Count)))];

static CSharpDiffExampleMarkdownView ExampleMarkdownRow(CSharpDiffExample example)
    => new(example.Member, new CodeSection("diff", example.UnifiedDiff));

static CSharpDiffExampleTableRow ExampleTableRow(CSharpDiffExample example)
    => new("Examples", example.Member, example.UnifiedDiff);

static CSharpDiffExample RenderExample(string member, CSharpDiffExampleGroup group)
    => new(member, CSharpDiffPrinter.RenderUnified(new CSharpBodyDiffResult(group.Rows, group.Failures)));

sealed class ExampleGroup(string member)
{
    public string Member { get; } = member;
    public List<CSharpDiffRow> Rows { get; } = [];
    public List<CSharpDiffFailureRow> Failures { get; } = [];
}

sealed record CSharpDiffPairCard(string OldPath, string NewPath, CSharpDiffCard Card);

sealed record CSharpDiffCard(
    bool IsExact,
    int ChangedMemberCount,
    int RowCount,
    int FailureCount,
    ImmutableArray<CardBucket> FailureBuckets,
    ImmutableArray<CardBucket> TopChangeIds,
    ImmutableArray<CardBucket> TopOperationKinds,
    ImmutableArray<CSharpDiffExampleGroup> ExampleGroups)
{
    public int ExactPairCount { get; init; }
    public int ChangedPairCount { get; init; }
    public ImmutableArray<CSharpDiffExample> Examples { get; init; } = [];
}

sealed record CardBucket(string Name, int Count);

sealed record CSharpDiffExampleGroup(
    string Member,
    ImmutableArray<CSharpDiffRow> Rows,
    ImmutableArray<CSharpDiffFailureRow> Failures);

sealed record CSharpDiffExample(string Member, string UnifiedDiff);

sealed record CSharpDiffSnapshot(
    int SchemaVersion,
    CSharpDiffSnapshotSummary Summary,
    CSharpDiffSnapshotPair[] Pairs,
    CardBucket[] FailureBuckets,
    CardBucket[] ChangeIdBuckets,
    CardBucket[] OperationKindBuckets,
    CSharpDiffExample[] Examples);

sealed record CSharpDiffSnapshotSummary(
    int PairCount,
    int ExactPairCount,
    int ChangedPairCount,
    int ChangedMemberCount,
    int RowCount,
    int FailureCount);

sealed record CSharpDiffSnapshotPair(
    string Old,
    string New,
    bool IsExact,
    int ChangedMemberCount,
    int RowCount,
    int FailureCount);

sealed record BaselineComparison(
    CSharpDiffSnapshot Baseline,
    CSharpDiffSnapshot Current,
    ImmutableArray<BaselineFinding> Regressions,
    ImmutableArray<BaselineFinding> Drift)
{
    public bool HasRegressions => !Regressions.IsDefaultOrEmpty;
    public ImmutableArray<BaselineFinding> Rows => [.. Regressions, .. Drift];
}

sealed record BaselineFinding(string Kind, string Metric, string Baseline, string Current, string Detail);

static class SnapshotJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
sealed class CSharpDiffCardMarkdownView
{
    [MarkoutIgnore]
    public string Title => "C# Diff Card";

    [MarkoutSection(Name = "Summary")]
    public List<CSharpDiffMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "Baseline metric changes", IncludeSectionInStructuredRows = true)]
    public List<MetricChange<int>>? BaselineMetrics { get; init; }

    [MarkoutSection(Name = "Baseline bucket changes", IncludeSectionInStructuredRows = true)]
    [MarkoutLabelHeader("Bucket set")]
    public List<MultiSourceRow>? BaselineBuckets { get; init; }

    [MarkoutSection(Name = "Failure buckets", EmptyText = "None")]
    public List<CSharpDiffBucketRow>? FailureBuckets { get; init; }

    [MarkoutSection(Name = "Top change IDs", EmptyText = "None")]
    public List<CSharpDiffBucketRow>? TopChangeIds { get; init; }

    [MarkoutSection(Name = "Top operation kinds", EmptyText = "None")]
    public List<CSharpDiffBucketRow>? TopOperationKinds { get; init; }

    [MarkoutSection(Name = "Pair summaries")]
    public List<CSharpDiffPairSummaryRow>? PairSummaries { get; init; }

    [MarkoutSection(Name = "Baseline findings", EmptyText = "No baseline regressions or drift.")]
    public List<BaselineFindingView>? BaselineFindings { get; init; }

    [MarkoutSection(Name = "Examples")]
    public List<CSharpDiffExampleMarkdownView>? Examples { get; init; }
}

[MarkoutSerializable]
sealed class CSharpDiffCardTableView
{
    [MarkoutIgnore]
    public string Title => "C# Diff Card";

    [MarkoutSection(Name = "Summary")]
    public List<CSharpDiffSectionMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "Baseline metric changes", IncludeSectionInStructuredRows = true)]
    public List<MetricChange<int>>? BaselineMetrics { get; init; }

    [MarkoutSection(Name = "Baseline bucket changes", IncludeSectionInStructuredRows = true)]
    [MarkoutLabelHeader("Bucket set")]
    public List<MultiSourceRow>? BaselineBuckets { get; init; }

    [MarkoutSection(Name = "Failure buckets", EmptyText = "None")]
    public List<CSharpDiffSectionBucketRow>? FailureBuckets { get; init; }

    [MarkoutSection(Name = "Top change IDs", EmptyText = "None")]
    public List<CSharpDiffSectionBucketRow>? TopChangeIds { get; init; }

    [MarkoutSection(Name = "Top operation kinds", EmptyText = "None")]
    public List<CSharpDiffSectionBucketRow>? TopOperationKinds { get; init; }

    [MarkoutSection(Name = "Pair summaries")]
    public List<CSharpDiffSectionPairSummaryRow>? PairSummaries { get; init; }

    [MarkoutSection(Name = "Baseline findings", EmptyText = "No baseline regressions or drift.")]
    public List<BaselineSectionFindingView>? BaselineFindings { get; init; }

    [MarkoutSection(Name = "Examples")]
    public List<CSharpDiffExampleTableRow>? Examples { get; init; }
}

[MarkoutSerializable]
sealed record CSharpDiffMetricRow(string Metric, string Count);

[MarkoutSerializable]
sealed record CSharpDiffSectionMetricRow(string Section, string Metric, string Count);

[MarkoutSerializable]
sealed record CSharpDiffBucketRow(string Bucket, string Count);

[MarkoutSerializable]
sealed record CSharpDiffSectionBucketRow(string Section, string Bucket, string Count);

[MarkoutSerializable]
sealed record CSharpDiffPairSummaryRow(
    string Old,
    string New,
    string Exact,
    [property: MarkoutPropertyName("Changed members")] string ChangedMembers,
    string Rows,
    string Failures);

[MarkoutSerializable]
sealed record CSharpDiffSectionPairSummaryRow(
    string Section,
    string Old,
    string New,
    string Exact,
    [property: MarkoutPropertyName("Changed members")] string ChangedMembers,
    string Rows,
    string Failures);

[MarkoutSerializable]
sealed record BaselineFindingView(string Kind, string Metric, string Baseline, string Current, string Detail);

[MarkoutSerializable]
sealed record BaselineSectionFindingView(string Section, string Kind, string Metric, string Baseline, string Current, string Detail);

[MarkoutSerializable(TitleProperty = nameof(Example), AutoFields = false)]
sealed record CSharpDiffExampleMarkdownView(
    [property: MarkoutIgnore] string Example,
    [property: MarkoutSection(Headless = true)] CodeSection Diff);

[MarkoutSerializable]
sealed record CSharpDiffExampleTableRow(
    string Section,
    string Example,
    [property: MarkoutPropertyName("Unified diff")] string UnifiedDiff);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(CSharpDiffCardMarkdownView))]
[MarkoutContext(typeof(CSharpDiffCardTableView))]
[MarkoutContext(typeof(CSharpDiffMetricRow))]
[MarkoutContext(typeof(CSharpDiffSectionMetricRow))]
[MarkoutContext(typeof(CSharpDiffBucketRow))]
[MarkoutContext(typeof(CSharpDiffSectionBucketRow))]
[MarkoutContext(typeof(CSharpDiffPairSummaryRow))]
[MarkoutContext(typeof(CSharpDiffSectionPairSummaryRow))]
[MarkoutContext(typeof(BaselineFindingView))]
[MarkoutContext(typeof(BaselineSectionFindingView))]
[MarkoutContext(typeof(CSharpDiffExampleMarkdownView))]
[MarkoutContext(typeof(CSharpDiffExampleTableRow))]
partial class CSharpDiffCardViewContext : MarkoutSerializerContext
{
}
