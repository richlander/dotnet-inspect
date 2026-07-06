using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;

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
      - pair exact-empty and changed-body counts;
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
            if (i + 1 >= args.Length || !TryParseOutputFormat(args[++i], out outputFormat))
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
        pairs.AddRange(ReadManifest(pairsManifest));
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

static IEnumerable<AssemblyPair> ReadManifest(string manifestPath)
{
    string manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath)) ?? Directory.GetCurrentDirectory();
    int lineNumber = 0;
    foreach (var rawLine in File.ReadLines(manifestPath))
    {
        lineNumber++;
        string line = rawLine.Trim();
        if (line.Length == 0 || line.StartsWith('#'))
            continue;

        var parts = line.Split('\t', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            throw new InvalidOperationException($"Invalid pair manifest line {lineNumber}: expected old<TAB>new.");

        yield return new AssemblyPair(ResolveManifestPath(manifestDirectory, parts[0]), ResolveManifestPath(manifestDirectory, parts[1]));
    }
}

static string ResolveManifestPath(string manifestDirectory, string path)
    => Path.IsPathFullyQualified(path) ? path : Path.GetFullPath(Path.Combine(manifestDirectory, path));

static bool TryParseOutputFormat(string value, out OutputFormat format)
{
    format = value.ToLowerInvariant() switch
    {
        "markdown" or "md" => OutputFormat.Markdown,
        "tsv" => OutputFormat.Tsv,
        "jsonl" => OutputFormat.Jsonl,
        _ => (OutputFormat)(-1),
    };
    return format is OutputFormat.Markdown or OutputFormat.Tsv or OutputFormat.Jsonl;
}

static IlDiffPairCard BuildPairCard(AssemblyPair pair, int maxExamples)
{
    using var oldFile = File.OpenRead(pair.OldPath);
    using var newFile = File.OpenRead(pair.NewPath);
    using var oldPe = new PEReader(oldFile);
    using var newPe = new PEReader(newFile);
    var oldReader = oldPe.GetMetadataReader();
    var newReader = newPe.GetMetadataReader();
    var card = BuildCard(oldPe, oldReader, newPe, newReader, maxExamples);
    return new IlDiffPairCard(pair.OldPath, pair.NewPath, card);
}

static IlDiffCard BuildCard(
    PEReader oldPe,
    MetadataReader oldReader,
    PEReader newPe,
    MetadataReader newReader,
    int maxExamples)
{
    var oldMethods = MethodMap(oldReader);
    var newMethods = MethodMap(newReader);
    var keys = oldMethods.Keys.Union(newMethods.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    var failures = new Dictionary<string, int>(StringComparer.Ordinal);
    var hunkKinds = new Dictionary<string, int>(StringComparer.Ordinal);
    var opcodeFamilies = new Dictionary<string, int>(StringComparer.Ordinal);
    var examples = ImmutableArray.CreateBuilder<IlDiffExample>();

    int compared = 0;
    int pairExact = 0;
    int changed = 0;
    int selfDiffExact = 0;

    foreach (string key in keys)
    {
        if (!oldMethods.TryGetValue(key, out var oldHandle))
        {
            IncrementFailure(failures, IlBodyDiffResult.OldBodyMissing());
            continue;
        }

        if (!newMethods.TryGetValue(key, out var newHandle))
        {
            IncrementFailure(failures, IlBodyDiffResult.NewBodyMissing());
            continue;
        }

        var oldMethod = oldReader.GetMethodDefinition(oldHandle);
        var newMethod = newReader.GetMethodDefinition(newHandle);
        if (oldMethod.RelativeVirtualAddress == 0 && newMethod.RelativeVirtualAddress == 0)
            continue;
        if (oldMethod.RelativeVirtualAddress == 0)
        {
            IncrementFailure(failures, IlBodyDiffResult.OldBodyMissing("method has no body"));
            continue;
        }
        if (newMethod.RelativeVirtualAddress == 0)
        {
            IncrementFailure(failures, IlBodyDiffResult.NewBodyMissing("method has no body"));
            continue;
        }

        compared++;
        MethodBodyBlock oldBody;
        MethodBodyBlock newBody;
        try
        {
            oldBody = oldPe.GetMethodBody(oldMethod.RelativeVirtualAddress);
            newBody = newPe.GetMethodBody(newMethod.RelativeVirtualAddress);
        }
        catch (BadImageFormatException)
        {
            Increment(failures, "body read failed");
            continue;
        }

        var self = IlBodyDiff.Compare(oldReader, oldBody, oldReader, oldBody);
        if (self.IsExact)
            selfDiffExact++;
        else
            IncrementFailure(failures, self.FailureRows.IsDefaultOrEmpty
                ? IlBodyDiffResult.UnsupportedBoundary(self.Failure ?? "self-diff not exact")
                : self);

        var diff = IlBodyDiff.Compare(oldReader, oldBody, newReader, newBody);
        if (!diff.FailureRows.IsDefaultOrEmpty || diff.Failure is { Length: > 0 })
        {
            IncrementFailure(failures, diff);
            continue;
        }

        if (diff.IsExact)
        {
            pairExact++;
            continue;
        }

        changed++;
        foreach (var row in diff.Rows)
        {
            Increment(hunkKinds, row.Kind.ToString());
            Increment(opcodeFamilies, row.Operation.OpcodeFamily);
        }

        if (examples.Count < maxExamples)
            examples.Add(new IlDiffExample(key, IlDiffPrinter.RenderUnified(diff)));
    }

    return new IlDiffCard(
        compared,
        selfDiffExact,
        pairExact,
        changed,
        failures.Values.Sum(),
        Buckets(failures),
        Buckets(hunkKinds),
        Buckets(opcodeFamilies),
        examples.ToImmutable());
}

static Dictionary<string, MethodDefinitionHandle> MethodMap(MetadataReader reader)
{
    var methods = new Dictionary<string, MethodDefinitionHandle>(StringComparer.Ordinal);
    foreach (var handle in reader.MethodDefinitions)
    {
        var method = reader.GetMethodDefinition(handle);
        string key = MethodKey(reader, method);
        methods.TryAdd(key, handle);
    }

    return methods;
}

static string MethodKey(MetadataReader reader, MethodDefinition method)
{
    string type = TypeName(reader, method.GetDeclaringType());
    string name = reader.GetString(method.Name);
    var signature = method.DecodeSignature(SignatureIdentityProvider.Instance, genericContext: null);
    string instance = signature.Header.IsInstance ? "instance" : "static";
    string genericArity = signature.GenericParameterCount > 0 ? $"<{signature.GenericParameterCount}>" : "";
    string signatureText = $"{instance} {signature.ReturnType}({string.Join(", ", signature.ParameterTypes)})";
    return $"{type}::{name}{genericArity}#{signatureText}";
}

static string TypeName(MetadataReader reader, TypeDefinitionHandle handle)
{
    var type = reader.GetTypeDefinition(handle);
    string name = reader.GetString(type.Name);
    var declaring = type.GetDeclaringType();
    if (!declaring.IsNil)
        return $"{TypeName(reader, declaring)}+{name}";
    string ns = reader.GetString(type.Namespace);
    return ns.Length == 0 ? name : $"{ns}.{name}";
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

static void IncrementFailure(Dictionary<string, int> counts, IlBodyDiffResult result)
{
    if (!result.FailureRows.IsDefaultOrEmpty)
    {
        foreach (var failure in result.FailureRows)
            Increment(counts, failure.Message);
        return;
    }

    Increment(counts, result.Failure ?? "unknown failure");
}

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
        SchemaVersion: 1,
        Summary: new IlDiffSnapshotSummary(
            PairCount: pairs.Length,
            ComparedBodyCount: card.ComparedBodyCount,
            SelfDiffEmptyCount: card.SelfDiffExactCount,
            PairExactEmptyCount: card.PairExactCount,
            ChangedBodyCount: card.ChangedBodyCount,
            FailureCount: card.FailureCount),
        Pairs: pairs.Select(pair => new IlDiffSnapshotPair(
            Old: SnapshotPath(pair.OldPath),
            New: SnapshotPath(pair.NewPath),
            ComparedBodyCount: pair.Card.ComparedBodyCount,
            SelfDiffEmptyCount: pair.Card.SelfDiffExactCount,
            PairExactEmptyCount: pair.Card.PairExactCount,
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
    if (snapshot.SchemaVersion != 1)
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
    MetricContext("Changed bodies", comparison.Baseline.Summary.ChangedBodyCount, comparison.Current.Summary.ChangedBodyCount),
    MetricGoal("Failures", comparison.Baseline.Summary.FailureCount, comparison.Current.Summary.FailureCount, "max failures", Goal.Lower),
];

static MetricChange<int> MetricContext(string name, int baseline, int current)
    => new(name, baseline, current);

static MetricChange<int> MetricGoal(string name, int baseline, int current, string targetLabel, Goal goal)
    => new(name, baseline, current, baseline, targetLabel) { Goal = goal };

static IlDiffCard Aggregate(ImmutableArray<IlDiffPairCard> pairs, int maxExamples, bool snapshotPaths = false)
{
    var failures = new Dictionary<string, int>(StringComparer.Ordinal);
    var hunkKinds = new Dictionary<string, int>(StringComparer.Ordinal);
    var opcodeFamilies = new Dictionary<string, int>(StringComparer.Ordinal);
    var examples = ImmutableArray.CreateBuilder<IlDiffExample>();
    int compared = 0;
    int selfDiffExact = 0;
    int pairExact = 0;
    int changed = 0;
    int failureCount = 0;

    foreach (var pair in pairs)
    {
        compared += pair.Card.ComparedBodyCount;
        selfDiffExact += pair.Card.SelfDiffExactCount;
        pairExact += pair.Card.PairExactCount;
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
                Method = $"{PathLabel(pair.OldPath, snapshotPaths)} to {PathLabel(pair.NewPath, snapshotPaths)} :: {example.Method}"
            });
        }
    }

    return new IlDiffCard(
        compared,
        selfDiffExact,
        pairExact,
        changed,
        failureCount,
        Buckets(failures),
        Buckets(hunkKinds),
        Buckets(opcodeFamilies),
        examples.ToImmutable());
}

static IlDiffPairSummaryRow PairSummaryRow(IlDiffPairCard pair)
    => new(
        DisplayPath(pair.OldPath),
        DisplayPath(pair.NewPath),
        pair.Card.ComparedBodyCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pair.Card.SelfDiffExactCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        pair.Card.PairExactCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
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

static string DisplayPath(string path)
{
    string fullPath = Path.GetFullPath(path);
    string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath);
    return relative.StartsWith("..", StringComparison.Ordinal)
        ? fullPath
        : relative;
}

static string SnapshotPath(string path) => Path.GetFullPath(path);

static string PathLabel(string path, bool snapshotPath)
    => snapshotPath ? SnapshotPath(path) : DisplayPath(path);

static List<IlDiffBucketRow>? MarkdownBucketRows(ImmutableArray<CardBucket> buckets)
    => buckets.IsDefaultOrEmpty
        ? null
        : [.. buckets.Take(10).Select(bucket => new IlDiffBucketRow(bucket.Name, Count(bucket.Count)))];

static List<IlDiffSectionBucketRow>? SectionedBucketRows(string section, ImmutableArray<CardBucket> buckets)
    => buckets.IsDefaultOrEmpty
        ? null
        : [.. buckets.Take(10).Select(bucket => new IlDiffSectionBucketRow(section, bucket.Name, Count(bucket.Count)))];

static IlDiffExampleMarkdownView ExampleMarkdownRow(IlDiffExample example)
    => new(example.Method, new CodeSection("diff", example.UnifiedDiff));

static IlDiffExampleTableRow ExampleTableRow(IlDiffExample example)
    => new("Examples", example.Method, example.UnifiedDiff);

sealed record AssemblyPair(string OldPath, string NewPath);

sealed record IlDiffPairCard(string OldPath, string NewPath, IlDiffCard Card);

enum OutputFormat
{
    Markdown,
    Tsv,
    Jsonl,
}

sealed record IlDiffCard(
    int ComparedBodyCount,
    int SelfDiffExactCount,
    int PairExactCount,
    int ChangedBodyCount,
    int FailureCount,
    ImmutableArray<CardBucket> FailureBuckets,
    ImmutableArray<CardBucket> TopHunkKinds,
    ImmutableArray<CardBucket> TopOpcodeFamilies,
    ImmutableArray<IlDiffExample> Examples);

sealed record CardBucket(string Name, int Count);

sealed record IlDiffExample(string Method, string UnifiedDiff);

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
sealed class IlDiffCardMarkdownView
{
    [MarkoutIgnore]
    public string Title => "IL Diff Card";

    [MarkoutSection(Name = "Summary")]
    public List<IlDiffMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "Baseline metric changes", IncludeSectionInStructuredRows = true)]
    public List<MetricChange<int>>? BaselineMetrics { get; init; }

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
    int ChangedBodyCount,
    int FailureCount);

sealed record IlDiffSnapshotPair(
    string Old,
    string New,
    int ComparedBodyCount,
    int SelfDiffEmptyCount,
    int PairExactEmptyCount,
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

sealed class SignatureIdentityProvider : ISignatureTypeProvider<string, object?>
{
    public static SignatureIdentityProvider Instance { get; } = new();

    public string GetPrimitiveType(PrimitiveTypeCode typeCode)
        => typeCode switch
        {
            PrimitiveTypeCode.Void => "void",
            PrimitiveTypeCode.Boolean => "bool",
            PrimitiveTypeCode.Char => "char",
            PrimitiveTypeCode.SByte => "int8",
            PrimitiveTypeCode.Byte => "uint8",
            PrimitiveTypeCode.Int16 => "int16",
            PrimitiveTypeCode.UInt16 => "uint16",
            PrimitiveTypeCode.Int32 => "int32",
            PrimitiveTypeCode.UInt32 => "uint32",
            PrimitiveTypeCode.Int64 => "int64",
            PrimitiveTypeCode.UInt64 => "uint64",
            PrimitiveTypeCode.Single => "float32",
            PrimitiveTypeCode.Double => "float64",
            PrimitiveTypeCode.String => "string",
            PrimitiveTypeCode.Object => "object",
            PrimitiveTypeCode.IntPtr => "native int",
            PrimitiveTypeCode.UIntPtr => "native uint",
            PrimitiveTypeCode.TypedReference => "typedref",
            _ => typeCode.ToString(),
        };

    public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        => TypeDefinitionName(reader, handle);

    public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
    {
        var type = reader.GetTypeReference(handle);
        string name = reader.GetString(type.Name);
        string ns = reader.GetString(type.Namespace);
        string fullName = ns.Length == 0 ? name : $"{ns}.{name}";
        return type.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference =>
                $"[{reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)type.ResolutionScope).Name)}]{fullName}",
            HandleKind.TypeReference =>
                $"{GetTypeFromReference(reader, (TypeReferenceHandle)type.ResolutionScope, rawTypeKind)}+{fullName}",
            _ => fullName,
        };
    }

    public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind)
        => reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);

    public string GetSZArrayType(string elementType) => $"{elementType}[]";
    public string GetArrayType(string elementType, ArrayShape shape) => $"{elementType}[{new string(',', Math.Max(shape.Rank - 1, 0))}]";
    public string GetByReferenceType(string elementType) => $"{elementType}&";
    public string GetPointerType(string elementType) => $"{elementType}*";
    public string GetPinnedType(string elementType) => $"{elementType} pinned";
    public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
        => $"{genericType}<{string.Join(", ", typeArguments)}>";
    public string GetGenericTypeParameter(object? genericContext, int index) => $"!{index}";
    public string GetGenericMethodParameter(object? genericContext, int index) => $"!!{index}";
    public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
        => $"{(isRequired ? "modreq" : "modopt")}({modifier}) {unmodifiedType}";
    public string GetFunctionPointerType(MethodSignature<string> signature)
        => $"method {signature.ReturnType} *({string.Join(", ", signature.ParameterTypes)})";

    static string TypeDefinitionName(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var type = reader.GetTypeDefinition(handle);
        string name = reader.GetString(type.Name);
        var declaring = type.GetDeclaringType();
        if (!declaring.IsNil)
            return $"{TypeDefinitionName(reader, declaring)}+{name}";
        string ns = reader.GetString(type.Namespace);
        string assembly = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : "";
        return $"[{assembly}]{(ns.Length == 0 ? name : $"{ns}.{name}")}";
    }
}
