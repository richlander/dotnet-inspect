using System.Collections.Immutable;
using System.Text.Json;

using ILInspector.DiffHarnessCommon;
using ILInspector.Instructions;
using Markout;
using Markout.Formatting;

const string Usage =
    """
    il-diff-harness <old-assembly> <new-assembly> [--max-examples N]
    il-diff-harness --pair <old-assembly> <new-assembly> [--pair <old-assembly> <new-assembly>...] [--max-examples N]
    il-diff-harness --pairs <manifest.tsv> [--max-examples N]
    il-diff-harness ... [--format markdown|tsv|jsonl]
    il-diff-harness ... [--emit-snapshot <file>] [--diff-baseline <file>]

      Emits a small IL Diff card over paired assemblies:
      - compared body count and self-diff empty count;
      - pair exact, operand-diff, opcode-diff, unavailable, and changed-body counts;
      - failure count and buckets;
      - top hunk kinds and opcode families;
      - capped examples rendered through IlDiffPrinter.

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
string? pairsManifest = null;
string? emitSnapshotPath = null;
string? diffBaselinePath = null;
int maxExamples = 5;
OutputFormat outputFormat = OutputFormat.Markdown;

if (args.Length >= 2 && !args[0].StartsWith("-", StringComparison.Ordinal) && !args[1].StartsWith("-", StringComparison.Ordinal))
{
    pairs.Add(new AssemblyPair(args[0], args[1]));
}

for (int i = pairs.Count == 0 ? 0 : 2; i < args.Length; i++)
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
            Console.Error.WriteLine($"Unknown argument: {args[i]}");
            Console.Error.WriteLine(Usage);
            return 2;
    }
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
    var cards = pairs.Select(pair => BuildPairCard(pair, maxExamples)).ToImmutableArray();
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
catch (Exception ex) when (ex is BadImageFormatException or IOException or InvalidOperationException or JsonException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

static IlDiffPairCard BuildPairCard(AssemblyPair pair, int maxExamples)
{
    var pairResult = IlAssemblyDiff.CompareFiles(pair.OldPath, pair.NewPath, maxExamples);
    return new IlDiffPairCard(pairResult.Old, pairResult.New, BuildCard(pairResult.Diff));
}

static IlDiffCard BuildCard(IlAssemblyDiffResult result)
{
    return new IlDiffCard(
        result.ComparedBodyCount,
        result.SelfDiffExactCount,
        result.PairExactCount,
        result.PairOperandDiffCount,
        result.PairOpcodeDiffCount,
        result.PairUnavailableCount,
        result.ChangedBodyCount,
        result.FailureCount,
        ToCardBuckets(result.FailureBuckets),
        ToCardBuckets(result.TopHunkKinds),
        ToCardBuckets(result.TopOpcodeFamilies),
        [.. result.Examples.Select(example => new IlDiffExample(
            example.Method,
            example.Diff.Outcome,
            IlDiffPrinter.RenderUnified(example.Diff)))]);
}

static ImmutableArray<CardBucket> Buckets(Dictionary<string, int> counts)
    => [.. counts
        .OrderByDescending(pair => pair.Value)
        .ThenBy(pair => pair.Key, StringComparer.Ordinal)
        .Select(pair => new CardBucket(pair.Key, pair.Value))];

static ImmutableArray<CardBucket> ToCardBuckets(ImmutableArray<IlDiffBucket> buckets)
    => [.. buckets.Select(bucket => new CardBucket(bucket.Name, bucket.Count))];

static void IncrementBy(Dictionary<string, int> counts, string key, int amount)
    => counts[key] = counts.TryGetValue(key, out int count) ? count + amount : amount;

static string FormatCard(ImmutableArray<IlDiffPairCard> pairs, int maxExamples, OutputFormat format, BaselineComparison? comparison = null)
{
    var output = new StringWriter();
    if (format == OutputFormat.Markdown)
    {
        MarkoutSerializer.Serialize(
            BuildMarkdownView(pairs, maxExamples, comparison),
            output,
            new MarkdownFormatter(),
            IlDiffCardViewContext.Default,
            new MarkoutWriterOptions());
    }
    else
    {
        MarkoutSerializer.Serialize(
            BuildTableView(pairs, maxExamples, comparison),
            output,
            new TableFormatter(showHeader: true),
            IlDiffCardViewContext.Default,
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

static IlDiffSnapshot BuildSnapshot(ImmutableArray<IlDiffPairCard> pairs, int maxExamples)
{
    var card = Aggregate(pairs, maxExamples, snapshotPaths: true);
    return new IlDiffSnapshot(
        SchemaVersion: 2,
        Summary: new IlDiffSnapshotSummary(
            PairCount: pairs.Length,
            ComparedBodyCount: card.ComparedBodyCount,
            SelfDiffEmptyCount: card.SelfDiffExactCount,
            PairExactEmptyCount: card.PairExactCount,
            PairOperandDiffCount: card.PairOperandDiffCount,
            PairOpcodeDiffCount: card.PairOpcodeDiffCount,
            PairUnavailableCount: card.PairUnavailableCount,
            ChangedBodyCount: card.ChangedBodyCount,
            FailureCount: card.FailureCount),
        Pairs: pairs.Select(pair => new IlDiffSnapshotPair(
            Old: DiffHarnessCommon.AbsoluteSnapshotPath(pair.OldPath),
            New: DiffHarnessCommon.AbsoluteSnapshotPath(pair.NewPath),
            ComparedBodyCount: pair.Card.ComparedBodyCount,
            SelfDiffEmptyCount: pair.Card.SelfDiffExactCount,
            PairExactEmptyCount: pair.Card.PairExactCount,
            PairOperandDiffCount: pair.Card.PairOperandDiffCount,
            PairOpcodeDiffCount: pair.Card.PairOpcodeDiffCount,
            PairUnavailableCount: pair.Card.PairUnavailableCount,
            ChangedBodyCount: pair.Card.ChangedBodyCount,
            FailureCount: pair.Card.FailureCount)).ToArray(),
        FailureBuckets: card.FailureBuckets.ToArray(),
        HunkKindBuckets: card.TopHunkKinds.ToArray(),
        OpcodeFamilyBuckets: card.TopOpcodeFamilies.ToArray(),
        Examples: card.Examples.ToArray());
}

static IlDiffSnapshot ReadSnapshot(string path)
{
    var snapshot = JsonSerializer.Deserialize<IlDiffSnapshot>(File.ReadAllText(path), SnapshotJson.Options)
        ?? throw new InvalidOperationException($"Could not read IL diff snapshot: {path}");
    if (snapshot.SchemaVersion != 2)
        throw new InvalidOperationException($"Unsupported IL diff snapshot schema version {snapshot.SchemaVersion}.");
    if (snapshot.Summary is null)
        throw new InvalidOperationException($"IL diff snapshot is missing a summary: {path}");
    return snapshot;
}

static BaselineComparison CompareSnapshots(IlDiffSnapshot baseline, IlDiffSnapshot current)
{
    var regressions = ImmutableArray.CreateBuilder<BaselineFinding>();
    var drift = ImmutableArray.CreateBuilder<BaselineFinding>();

    AddCountRegression(regressions, "Failures", baseline.Summary.FailureCount, current.Summary.FailureCount);
    AddCountDropRegression(regressions, "Self-diff empty", baseline.Summary.SelfDiffEmptyCount, current.Summary.SelfDiffEmptyCount);
    var baselineFailureBuckets = baseline.FailureBuckets ?? [];
    var currentFailureBuckets = current.FailureBuckets ?? [];
    AddNewFailureBuckets(regressions, baselineFailureBuckets, currentFailureBuckets);

    AddDrift(drift, "Pairs", baseline.Summary.PairCount, current.Summary.PairCount);
    AddDrift(drift, "Compared bodies", baseline.Summary.ComparedBodyCount, current.Summary.ComparedBodyCount);
    AddDriftIfImproved(drift, "Failures", baseline.Summary.FailureCount, current.Summary.FailureCount, improvementWhenCurrentIsLower: true);
    AddDriftIfImproved(drift, "Self-diff empty", baseline.Summary.SelfDiffEmptyCount, current.Summary.SelfDiffEmptyCount, improvementWhenCurrentIsLower: false);
    AddDrift(drift, "Pair exact empty", baseline.Summary.PairExactEmptyCount, current.Summary.PairExactEmptyCount);
    AddDrift(drift, "Pair operand diffs", baseline.Summary.PairOperandDiffCount, current.Summary.PairOperandDiffCount);
    AddDrift(drift, "Pair opcode diffs", baseline.Summary.PairOpcodeDiffCount, current.Summary.PairOpcodeDiffCount);
    AddDrift(drift, "Pair unavailable", baseline.Summary.PairUnavailableCount, current.Summary.PairUnavailableCount);
    AddDrift(drift, "Changed bodies", baseline.Summary.ChangedBodyCount, current.Summary.ChangedBodyCount);
    AddExistingFailureBucketDrift(drift, baselineFailureBuckets, currentFailureBuckets);
    AddBucketDrift(drift, "Hunk kind", baseline.HunkKindBuckets ?? [], current.HunkKindBuckets ?? []);
    AddBucketDrift(drift, "Opcode family", baseline.OpcodeFamilyBuckets ?? [], current.OpcodeFamilyBuckets ?? []);

    return new BaselineComparison(baseline, current, regressions.ToImmutable(), drift.ToImmutable());
}

static void AddCountRegression(ImmutableArray<BaselineFinding>.Builder findings, string metric, int baseline, int current)
{
    if (current > baseline)
        findings.Add(new BaselineFinding("Regression", metric, baseline.ToString(System.Globalization.CultureInfo.InvariantCulture), current.ToString(System.Globalization.CultureInfo.InvariantCulture), "count increased"));
}

static void AddCountDropRegression(ImmutableArray<BaselineFinding>.Builder findings, string metric, int baseline, int current)
{
    if (current < baseline)
        findings.Add(new BaselineFinding("Regression", metric, baseline.ToString(System.Globalization.CultureInfo.InvariantCulture), current.ToString(System.Globalization.CultureInfo.InvariantCulture), "count dropped"));
}

static void AddNewFailureBuckets(ImmutableArray<BaselineFinding>.Builder findings, IReadOnlyList<CardBucket> baseline, IReadOnlyList<CardBucket> current)
{
    var baselineNames = baseline.Select(bucket => bucket.Name).ToHashSet(StringComparer.Ordinal);
    foreach (var bucket in current.Where(bucket => bucket.Count > 0 && !baselineNames.Contains(bucket.Name)))
        findings.Add(new BaselineFinding("Regression", $"Failure bucket `{bucket.Name}`", "0", bucket.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), "new failure bucket"));
}

static void AddDrift(ImmutableArray<BaselineFinding>.Builder findings, string metric, int baseline, int current)
{
    if (current != baseline)
        findings.Add(new BaselineFinding("Drift", metric, baseline.ToString(System.Globalization.CultureInfo.InvariantCulture), current.ToString(System.Globalization.CultureInfo.InvariantCulture), "count changed"));
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
            baseline.ToString(System.Globalization.CultureInfo.InvariantCulture),
            current.ToString(System.Globalization.CultureInfo.InvariantCulture),
            improvementWhenCurrentIsLower ? "count dropped" : "count increased"));
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

static void AddBucketDrift(ImmutableArray<BaselineFinding>.Builder findings, string metric, IReadOnlyList<CardBucket> baseline, IReadOnlyList<CardBucket> current)
{
    var baselineCounts = baseline.ToDictionary(bucket => bucket.Name, bucket => bucket.Count, StringComparer.Ordinal);
    var currentCounts = current.ToDictionary(bucket => bucket.Name, bucket => bucket.Count, StringComparer.Ordinal);
    foreach (string name in baselineCounts.Keys.Union(currentCounts.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
    {
        int oldCount = baselineCounts.GetValueOrDefault(name);
        int newCount = currentCounts.GetValueOrDefault(name);
        if (oldCount != newCount)
            findings.Add(new BaselineFinding("Drift", $"{metric} `{name}`", oldCount.ToString(System.Globalization.CultureInfo.InvariantCulture), newCount.ToString(System.Globalization.CultureInfo.InvariantCulture), "bucket count changed"));
    }
}

static IlDiffCardMarkdownView BuildMarkdownView(ImmutableArray<IlDiffPairCard> pairs, int maxExamples, BaselineComparison? comparison)
{
    var card = Aggregate(pairs, maxExamples);
    return new IlDiffCardMarkdownView
    {
        Summary = SummaryRows(pairs.Length, card),
        BaselineMetrics = comparison is null ? null : BaselineMetricRows(comparison),
        BaselineBuckets = comparison is null ? null : BaselineBucketRows(comparison),
        FailureBuckets = MarkdownBucketRows(card.FailureBuckets) ?? [],
        TopHunkKinds = MarkdownBucketRows(card.TopHunkKinds) ?? [],
        TopOpcodeFamilies = MarkdownBucketRows(card.TopOpcodeFamilies) ?? [],
        PairSummaries = [.. pairs.Select(pair => PairSummaryRow(pair))],
        BaselineFindings = comparison is null ? null : BaselineRows(comparison) ?? [],
        Examples = card.Examples.IsDefaultOrEmpty ? null : [.. card.Examples.Select(ExampleMarkdownRow)],
    };
}

static IlDiffCardTableView BuildTableView(ImmutableArray<IlDiffPairCard> pairs, int maxExamples, BaselineComparison? comparison)
{
    var card = Aggregate(pairs, maxExamples);
    return new IlDiffCardTableView
    {
        Summary = SectionedSummaryRows(pairs.Length, card),
        BaselineMetrics = comparison is null ? null : BaselineMetricRows(comparison),
        BaselineBuckets = comparison is null ? null : BaselineBucketRows(comparison),
        FailureBuckets = SectionedBucketRows("Failure buckets", card.FailureBuckets),
        TopHunkKinds = SectionedBucketRows("Top hunk kinds", card.TopHunkKinds),
        TopOpcodeFamilies = SectionedBucketRows("Top opcode families", card.TopOpcodeFamilies),
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

static List<IlDiffMetricRow> SummaryRows(int pairCount, IlDiffCard card) =>
[
    new("Pairs", Count(pairCount)),
    new("Compared bodies", Count(card.ComparedBodyCount)),
    new("Self-diff empty", Count(card.SelfDiffExactCount)),
    new("Pair exact empty", Count(card.PairExactCount)),
    new("Pair operand diffs", Count(card.PairOperandDiffCount)),
    new("Pair opcode diffs", Count(card.PairOpcodeDiffCount)),
    new("Pair unavailable", Count(card.PairUnavailableCount)),
    new("Changed bodies", Count(card.ChangedBodyCount)),
    new("Failures", Count(card.FailureCount)),
];

static List<IlDiffSectionMetricRow> SectionedSummaryRows(int pairCount, IlDiffCard card)
    => [.. SummaryRows(pairCount, card).Select(row => new IlDiffSectionMetricRow("Summary", row.Metric, row.Count))];

static string Count(int count) => count.ToString(System.Globalization.CultureInfo.InvariantCulture);

static List<MetricChange<int>> BaselineMetricRows(BaselineComparison comparison) =>
[
    MetricContext("Pairs", comparison.Baseline.Summary.PairCount, comparison.Current.Summary.PairCount),
    MetricContext("Compared bodies", comparison.Baseline.Summary.ComparedBodyCount, comparison.Current.Summary.ComparedBodyCount),
    MetricGoal("Self-diff empty", comparison.Baseline.Summary.SelfDiffEmptyCount, comparison.Current.Summary.SelfDiffEmptyCount, "minimum self-diff empty", Goal.Higher),
    MetricContext("Pair exact empty", comparison.Baseline.Summary.PairExactEmptyCount, comparison.Current.Summary.PairExactEmptyCount),
    MetricContext("Pair operand diffs", comparison.Baseline.Summary.PairOperandDiffCount, comparison.Current.Summary.PairOperandDiffCount),
    MetricContext("Pair opcode diffs", comparison.Baseline.Summary.PairOpcodeDiffCount, comparison.Current.Summary.PairOpcodeDiffCount),
    MetricContext("Pair unavailable", comparison.Baseline.Summary.PairUnavailableCount, comparison.Current.Summary.PairUnavailableCount),
    MetricContext("Changed bodies", comparison.Baseline.Summary.ChangedBodyCount, comparison.Current.Summary.ChangedBodyCount),
    MetricGoal("Failures", comparison.Baseline.Summary.FailureCount, comparison.Current.Summary.FailureCount, "max failures", Goal.Lower),
];

static MetricChange<int> MetricContext(string name, int baseline, int current)
    => new(name, baseline, current);

static MetricChange<int> MetricGoal(string name, int baseline, int current, string targetLabel, Goal goal)
    => new(name, baseline, current, baseline, targetLabel) { Goal = goal };

static List<MultiSourceRow> BaselineBucketRows(BaselineComparison comparison) =>
[
    BucketChangeRow("Failure buckets (-)", comparison.Baseline.FailureBuckets ?? [], comparison.Current.FailureBuckets ?? [], Goal.Lower),
    BucketChangeRow("Hunk kinds", comparison.Baseline.HunkKindBuckets ?? [], comparison.Current.HunkKindBuckets ?? [], Goal.Context),
    BucketChangeRow("Opcode families", comparison.Baseline.OpcodeFamilyBuckets ?? [], comparison.Current.OpcodeFamilyBuckets ?? [], Goal.Context),
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

static IlDiffCard Aggregate(ImmutableArray<IlDiffPairCard> pairs, int maxExamples, bool snapshotPaths = false)
{
    var failures = new Dictionary<string, int>(StringComparer.Ordinal);
    var hunkKinds = new Dictionary<string, int>(StringComparer.Ordinal);
    var opcodeFamilies = new Dictionary<string, int>(StringComparer.Ordinal);
    var examples = ImmutableArray.CreateBuilder<IlDiffExample>();
    int compared = 0;
    int selfDiffExact = 0;
    int pairExact = 0;
    int pairOperandDiff = 0;
    int pairOpcodeDiff = 0;
    int pairUnavailable = 0;
    int changed = 0;
    int failureCount = 0;

    foreach (var pair in pairs)
    {
        compared += pair.Card.ComparedBodyCount;
        selfDiffExact += pair.Card.SelfDiffExactCount;
        pairExact += pair.Card.PairExactCount;
        pairOperandDiff += pair.Card.PairOperandDiffCount;
        pairOpcodeDiff += pair.Card.PairOpcodeDiffCount;
        pairUnavailable += pair.Card.PairUnavailableCount;
        changed += pair.Card.ChangedBodyCount;
        failureCount += pair.Card.FailureCount;
        foreach (var bucket in pair.Card.FailureBuckets)
            IncrementBy(failures, bucket.Name, bucket.Count);
        foreach (var bucket in pair.Card.TopHunkKinds)
            IncrementBy(hunkKinds, bucket.Name, bucket.Count);
        foreach (var bucket in pair.Card.TopOpcodeFamilies)
            IncrementBy(opcodeFamilies, bucket.Name, bucket.Count);
        foreach (var example in pair.Card.Examples)
        {
            if (examples.Count >= maxExamples)
                break;
            examples.Add(example with
            {
                Method = $"{DiffHarnessCommon.PathLabel(pair.OldPath, snapshotPaths, DiffHarnessCommon.AbsoluteSnapshotPath)} to {DiffHarnessCommon.PathLabel(pair.NewPath, snapshotPaths, DiffHarnessCommon.AbsoluteSnapshotPath)} :: {example.Method}"
            });
        }
    }

    return new IlDiffCard(
        compared,
        selfDiffExact,
        pairExact,
        pairOperandDiff,
        pairOpcodeDiff,
        pairUnavailable,
        changed,
        failureCount,
        Buckets(failures),
        Buckets(hunkKinds),
        Buckets(opcodeFamilies),
        examples.ToImmutable());
}

static IlDiffPairSummaryRow PairSummaryRow(IlDiffPairCard pair)
    => new(
        DiffHarnessCommon.DisplayPath(pair.OldPath),
        DiffHarnessCommon.DisplayPath(pair.NewPath),
        pair.Card.ComparedBodyCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pair.Card.SelfDiffExactCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pair.Card.PairExactCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pair.Card.PairOperandDiffCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pair.Card.PairOpcodeDiffCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pair.Card.PairUnavailableCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pair.Card.ChangedBodyCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pair.Card.FailureCount.ToString(System.Globalization.CultureInfo.InvariantCulture));

static IlDiffSectionPairSummaryRow SectionedPairSummaryRow(IlDiffPairCard pair)
{
    var row = PairSummaryRow(pair);
    return new IlDiffSectionPairSummaryRow(
        "Pair summaries",
        row.Old,
        row.New,
        row.Compared,
        row.SelfDiffEmpty,
        row.PairExactEmpty,
        row.PairOperandDiffs,
        row.PairOpcodeDiffs,
        row.PairUnavailable,
        row.Changed,
        row.Failures);
}

static List<BaselineFindingView>? BaselineRows(BaselineComparison comparison)
    => comparison.Rows.IsDefaultOrEmpty
        ? null
        : [.. comparison.Rows.Select(row => new BaselineFindingView(row.Kind, row.Metric, row.Baseline, row.Current, row.Detail))];

static List<BaselineSectionFindingView>? SectionedBaselineRows(BaselineComparison comparison)
    => comparison.Rows.IsDefaultOrEmpty
        ? null
        : [.. comparison.Rows.Select(row => new BaselineSectionFindingView("Baseline findings", row.Kind, row.Metric, row.Baseline, row.Current, row.Detail))];

static List<IlDiffBucketRow>? MarkdownBucketRows(ImmutableArray<CardBucket> buckets)
    => buckets.IsDefaultOrEmpty
        ? null
        : [.. buckets.Take(10).Select(bucket => new IlDiffBucketRow(bucket.Name, Count(bucket.Count)))];

static List<IlDiffSectionBucketRow>? SectionedBucketRows(string section, ImmutableArray<CardBucket> buckets)
    => buckets.IsDefaultOrEmpty
        ? null
        : [.. buckets.Take(10).Select(bucket => new IlDiffSectionBucketRow(section, bucket.Name, Count(bucket.Count)))];

static IlDiffExampleMarkdownView ExampleMarkdownRow(IlDiffExample example)
    => new($"{example.Method} ({example.Outcome})", new CodeSection("diff", example.UnifiedDiff));

static IlDiffExampleTableRow ExampleTableRow(IlDiffExample example)
    => new("Examples", example.Method, example.Outcome.ToString(), example.UnifiedDiff);

sealed record IlDiffPairCard(string OldPath, string NewPath, IlDiffCard Card);

sealed record IlDiffCard(
    int ComparedBodyCount,
    int SelfDiffExactCount,
    int PairExactCount,
    int PairOperandDiffCount,
    int PairOpcodeDiffCount,
    int PairUnavailableCount,
    int ChangedBodyCount,
    int FailureCount,
    ImmutableArray<CardBucket> FailureBuckets,
    ImmutableArray<CardBucket> TopHunkKinds,
    ImmutableArray<CardBucket> TopOpcodeFamilies,
    ImmutableArray<IlDiffExample> Examples);

sealed record CardBucket(string Name, int Count);

sealed record IlDiffExample(
    string Method,
    IlBodyDiffOutcome Outcome,
    string UnifiedDiff);

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
sealed class IlDiffCardMarkdownView
{
    [MarkoutIgnore]
    public string Title => "IL Diff Card";

    [MarkoutSection(Name = "Summary")]
    public List<IlDiffMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "Baseline metric changes", IncludeSectionInStructuredRows = true)]
    public List<MetricChange<int>>? BaselineMetrics { get; init; }

    [MarkoutSection(Name = "Baseline bucket changes", IncludeSectionInStructuredRows = true)]
    [MarkoutLabelHeader("Bucket set")]
    public List<MultiSourceRow>? BaselineBuckets { get; init; }

    [MarkoutSection(Name = "Failure buckets", EmptyText = "None")]
    public List<IlDiffBucketRow>? FailureBuckets { get; init; }

    [MarkoutSection(Name = "Top hunk kinds", EmptyText = "None")]
    public List<IlDiffBucketRow>? TopHunkKinds { get; init; }

    [MarkoutSection(Name = "Top opcode families", EmptyText = "None")]
    public List<IlDiffBucketRow>? TopOpcodeFamilies { get; init; }

    [MarkoutSection(Name = "Pair summaries")]
    public List<IlDiffPairSummaryRow>? PairSummaries { get; init; }

    [MarkoutSection(Name = "Baseline findings", EmptyText = "No baseline regressions or drift.")]
    public List<BaselineFindingView>? BaselineFindings { get; init; }

    [MarkoutSection(Name = "Examples")]
    public List<IlDiffExampleMarkdownView>? Examples { get; init; }
}

[MarkoutSerializable]
sealed class IlDiffCardTableView
{
    [MarkoutIgnore]
    public string Title => "IL Diff Card";

    [MarkoutSection(Name = "Summary")]
    public List<IlDiffSectionMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "Baseline metric changes", IncludeSectionInStructuredRows = true)]
    public List<MetricChange<int>>? BaselineMetrics { get; init; }

    [MarkoutSection(Name = "Baseline bucket changes", IncludeSectionInStructuredRows = true)]
    [MarkoutLabelHeader("Bucket set")]
    public List<MultiSourceRow>? BaselineBuckets { get; init; }

    [MarkoutSection(Name = "Failure buckets", EmptyText = "None")]
    public List<IlDiffSectionBucketRow>? FailureBuckets { get; init; }

    [MarkoutSection(Name = "Top hunk kinds", EmptyText = "None")]
    public List<IlDiffSectionBucketRow>? TopHunkKinds { get; init; }

    [MarkoutSection(Name = "Top opcode families", EmptyText = "None")]
    public List<IlDiffSectionBucketRow>? TopOpcodeFamilies { get; init; }

    [MarkoutSection(Name = "Pair summaries")]
    public List<IlDiffSectionPairSummaryRow>? PairSummaries { get; init; }

    [MarkoutSection(Name = "Baseline findings", EmptyText = "No baseline regressions or drift.")]
    public List<BaselineSectionFindingView>? BaselineFindings { get; init; }

    [MarkoutSection(Name = "Examples")]
    public List<IlDiffExampleTableRow>? Examples { get; init; }
}

[MarkoutSerializable]
sealed record IlDiffMetricRow(string Metric, string Count);

[MarkoutSerializable]
sealed record IlDiffSectionMetricRow(string Section, string Metric, string Count);

[MarkoutSerializable]
sealed record IlDiffBucketRow(string Bucket, string Count);

[MarkoutSerializable]
sealed record IlDiffSectionBucketRow(string Section, string Bucket, string Count);

[MarkoutSerializable]
sealed record IlDiffPairSummaryRow(
    string Old,
    string New,
    string Compared,
    [property: MarkoutPropertyName("Self-diff empty")] string SelfDiffEmpty,
    [property: MarkoutPropertyName("Pair exact empty")] string PairExactEmpty,
    [property: MarkoutPropertyName("Pair operand diffs")] string PairOperandDiffs,
    [property: MarkoutPropertyName("Pair opcode diffs")] string PairOpcodeDiffs,
    [property: MarkoutPropertyName("Pair unavailable")] string PairUnavailable,
    string Changed,
    string Failures);

[MarkoutSerializable]
sealed record IlDiffSectionPairSummaryRow(
    string Section,
    string Old,
    string New,
    string Compared,
    [property: MarkoutPropertyName("Self-diff empty")] string SelfDiffEmpty,
    [property: MarkoutPropertyName("Pair exact empty")] string PairExactEmpty,
    [property: MarkoutPropertyName("Pair operand diffs")] string PairOperandDiffs,
    [property: MarkoutPropertyName("Pair opcode diffs")] string PairOpcodeDiffs,
    [property: MarkoutPropertyName("Pair unavailable")] string PairUnavailable,
    string Changed,
    string Failures);

[MarkoutSerializable]
sealed record BaselineFindingView(string Kind, string Metric, string Baseline, string Current, string Detail);

[MarkoutSerializable]
sealed record BaselineSectionFindingView(string Section, string Kind, string Metric, string Baseline, string Current, string Detail);

[MarkoutSerializable(TitleProperty = nameof(Example), AutoFields = false)]
sealed record IlDiffExampleMarkdownView(
    [property: MarkoutIgnore] string Example,
    [property: MarkoutSection(Headless = true)] CodeSection Diff);

[MarkoutSerializable]
sealed record IlDiffExampleTableRow(
    string Section,
    string Example,
    string Outcome,
    [property: MarkoutPropertyName("Unified diff")] string UnifiedDiff);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(IlDiffCardMarkdownView))]
[MarkoutContext(typeof(IlDiffCardTableView))]
[MarkoutContext(typeof(IlDiffMetricRow))]
[MarkoutContext(typeof(IlDiffSectionMetricRow))]
[MarkoutContext(typeof(IlDiffBucketRow))]
[MarkoutContext(typeof(IlDiffSectionBucketRow))]
[MarkoutContext(typeof(IlDiffPairSummaryRow))]
[MarkoutContext(typeof(IlDiffSectionPairSummaryRow))]
[MarkoutContext(typeof(BaselineFindingView))]
[MarkoutContext(typeof(BaselineSectionFindingView))]
[MarkoutContext(typeof(IlDiffExampleMarkdownView))]
[MarkoutContext(typeof(IlDiffExampleTableRow))]
partial class IlDiffCardViewContext : MarkoutSerializerContext
{
}

sealed record IlDiffSnapshot(
    int SchemaVersion,
    IlDiffSnapshotSummary Summary,
    IlDiffSnapshotPair[] Pairs,
    CardBucket[] FailureBuckets,
    CardBucket[] HunkKindBuckets,
    CardBucket[] OpcodeFamilyBuckets,
    IlDiffExample[] Examples);

sealed record IlDiffSnapshotSummary(
    int PairCount,
    int ComparedBodyCount,
    int SelfDiffEmptyCount,
    int PairExactEmptyCount,
    int PairOperandDiffCount,
    int PairOpcodeDiffCount,
    int PairUnavailableCount,
    int ChangedBodyCount,
    int FailureCount);

sealed record IlDiffSnapshotPair(
    string Old,
    string New,
    int ComparedBodyCount,
    int SelfDiffEmptyCount,
    int PairExactEmptyCount,
    int PairOperandDiffCount,
    int PairOpcodeDiffCount,
    int PairUnavailableCount,
    int ChangedBodyCount,
    int FailureCount);

sealed record BaselineComparison(
    IlDiffSnapshot Baseline,
    IlDiffSnapshot Current,
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
