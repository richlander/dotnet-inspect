using System.Collections.Immutable;
using System.Runtime.InteropServices;

using ILInspector.Decompiler;
using Markout;
using Markout.Formatting;

const string Usage =
    """
    csharp-diff-harness <old-assembly> <new-assembly> [--max-examples N]
    csharp-diff-harness --pair <old-assembly> <new-assembly> [--pair <old-assembly> <new-assembly>...] [--max-examples N]
    csharp-diff-harness --pairs <manifest.tsv> [--max-examples N]
    csharp-diff-harness ... [--format markdown|tsv|jsonl]

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
            if (i + 1 >= args.Length || !TryParseOutputFormat(args[++i], out outputFormat))
            {
                Console.Error.WriteLine("--format requires one of: markdown, tsv, jsonl.");
                return 2;
            }

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
    var cards = pairs.Select(BuildPairCard).ToImmutableArray();
    Console.Write(FormatCard(cards, maxExamples, outputFormat));
    return 0;
}
catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or BadImageFormatException)
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

static string FormatCard(ImmutableArray<CSharpDiffPairCard> pairs, int maxExamples, OutputFormat format)
{
    var output = new StringWriter();
    if (format == OutputFormat.Markdown)
    {
        MarkoutSerializer.Serialize(
            BuildMarkdownView(pairs, maxExamples),
            output,
            new MarkdownFormatter(),
            CSharpDiffCardViewContext.Default,
            new MarkoutWriterOptions());
    }
    else
    {
        MarkoutSerializer.Serialize(
            BuildTableView(pairs, maxExamples),
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

static CSharpDiffCardMarkdownView BuildMarkdownView(ImmutableArray<CSharpDiffPairCard> pairs, int maxExamples)
{
    var card = Aggregate(pairs, maxExamples);
    return new CSharpDiffCardMarkdownView
    {
        Summary = SummaryRows(pairs.Length, card),
        FailureBuckets = MarkdownBucketRows(card.FailureBuckets) ?? [],
        TopChangeIds = MarkdownBucketRows(card.TopChangeIds) ?? [],
        TopOperationKinds = MarkdownBucketRows(card.TopOperationKinds) ?? [],
        PairSummaries = [.. pairs.Select(pair => PairSummaryRow(pair))],
        Examples = card.Examples.IsDefaultOrEmpty ? null : [.. card.Examples.Select(ExampleMarkdownRow)],
    };
}

static CSharpDiffCardTableView BuildTableView(ImmutableArray<CSharpDiffPairCard> pairs, int maxExamples)
{
    var card = Aggregate(pairs, maxExamples);
    return new CSharpDiffCardTableView
    {
        Summary = SectionedSummaryRows(pairs.Length, card),
        FailureBuckets = SectionedBucketRows("Failure buckets", card.FailureBuckets),
        TopChangeIds = SectionedBucketRows("Top change IDs", card.TopChangeIds),
        TopOperationKinds = SectionedBucketRows("Top operation kinds", card.TopOperationKinds),
        PairSummaries = [.. pairs.Select(pair => SectionedPairSummaryRow(pair))],
        Examples = card.Examples.IsDefaultOrEmpty ? null : [.. card.Examples.Select(ExampleTableRow)],
    };
}

static MarkoutWriterOptions WriterOptions(OutputFormat format)
    => format switch
    {
        OutputFormat.Markdown => new MarkoutWriterOptions(),
        OutputFormat.Tsv => new MarkoutWriterOptions { TableMode = MarkoutTableMode.Tsv },
        OutputFormat.Jsonl => new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl },
        _ => throw new InvalidOperationException($"Unsupported output format '{format}'."),
    };

static CSharpDiffCard Aggregate(ImmutableArray<CSharpDiffPairCard> pairs, int maxExamples)
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
                $"{DisplayPath(pair.OldPath)} to {DisplayPath(pair.NewPath)} :: {example.Member}",
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

static CSharpDiffPairSummaryRow PairSummaryRow(CSharpDiffPairCard pair)
    => new(
        DisplayPath(pair.OldPath),
        DisplayPath(pair.NewPath),
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

static string DisplayPath(string path)
{
    string fullPath = Path.GetFullPath(path);
    string relative = Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath);
    return relative.StartsWith("..", StringComparison.Ordinal)
        ? fullPath
        : relative;
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

sealed record AssemblyPair(string OldPath, string NewPath);

sealed record CSharpDiffPairCard(string OldPath, string NewPath, CSharpDiffCard Card);

enum OutputFormat
{
    Markdown,
    Tsv,
    Jsonl,
}

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

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
sealed class CSharpDiffCardMarkdownView
{
    [MarkoutIgnore]
    public string Title => "C# Diff Card";

    [MarkoutSection(Name = "Summary")]
    public List<CSharpDiffMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "Failure buckets", EmptyText = "None")]
    public List<CSharpDiffBucketRow>? FailureBuckets { get; init; }

    [MarkoutSection(Name = "Top change IDs", EmptyText = "None")]
    public List<CSharpDiffBucketRow>? TopChangeIds { get; init; }

    [MarkoutSection(Name = "Top operation kinds", EmptyText = "None")]
    public List<CSharpDiffBucketRow>? TopOperationKinds { get; init; }

    [MarkoutSection(Name = "Pair summaries")]
    public List<CSharpDiffPairSummaryRow>? PairSummaries { get; init; }

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

    [MarkoutSection(Name = "Failure buckets", EmptyText = "None")]
    public List<CSharpDiffSectionBucketRow>? FailureBuckets { get; init; }

    [MarkoutSection(Name = "Top change IDs", EmptyText = "None")]
    public List<CSharpDiffSectionBucketRow>? TopChangeIds { get; init; }

    [MarkoutSection(Name = "Top operation kinds", EmptyText = "None")]
    public List<CSharpDiffSectionBucketRow>? TopOperationKinds { get; init; }

    [MarkoutSection(Name = "Pair summaries")]
    public List<CSharpDiffSectionPairSummaryRow>? PairSummaries { get; init; }

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
[MarkoutContext(typeof(CSharpDiffExampleMarkdownView))]
[MarkoutContext(typeof(CSharpDiffExampleTableRow))]
partial class CSharpDiffCardViewContext : MarkoutSerializerContext
{
}
