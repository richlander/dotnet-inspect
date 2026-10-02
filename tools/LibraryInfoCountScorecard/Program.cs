using System.Globalization;
using DotnetInspector.PerformanceOracles;
using DotnetInspector.Queries;
using ILInspector.Analysis.Classification;

// library-info-count-scorecard check <assembly>...
// library-info-count-scorecard time [--rounds N] [--budget-ms N] [--tsv <path>] <assembly>...
//
// The Library Info multi-question Count scorecard (#9153). One answer is the
// Async Methods, Extension Methods, and Union Types counts together, so a
// cell times a strategy for answering all three questions over one asset.
//
// Old is the three production calls as Library Info runs them today: the
// Async Count terminal, and rows built then counted for Extension Methods
// and Union Types. LINQ ×3, NLinq ×3 (the oracle), and NLinq fused apply the
// identical gate and predicates through product-owned tests; only the read
// machinery differs. No column decodes IL.
if (!ScorecardCommandLine.TryParse(args, out ScorecardOptions? options, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape();
ScorecardClosing[] closings = [ScorecardClosing.Count];
ScorecardColumn<LibraryInfoAsset, LaneCount> oracle = LibraryInfoCounts.NLinqColumn();
ScorecardColumn<LibraryInfoAsset, LaneCount>[] columns =
[
    new("Old", (closing, asset) => LibraryInfoCounts.Answer(closing, Old(asset))),
    LibraryInfoCounts.LinqColumn(),
    oracle,
    LibraryInfoCounts.FusedColumn(),
];

IReadOnlyList<ScorecardAsset<LibraryInfoAsset>> assets = LibraryInfoCounts.LoadAssets(options!.Assets);
try
{
    ScorecardCheck check = Scorecard.Check(assets, oracle, columns, LibraryInfoCounts.RowText, closings: closings);
    foreach (ScorecardAsset<LibraryInfoAsset> asset in assets)
    {
        ScorecardAnswer<LaneCount> answer = oracle.Answer(ScorecardClosing.Count, asset.Asset);
        Console.WriteLine($"answer\t{asset.Name}\t{string.Join('\t', answer.Rows!)}");
    }

    foreach (ScorecardMismatch mismatch in check.Mismatches)
        Console.WriteLine($"mismatch\t{mismatch.Asset}\t{shape.Label(mismatch.Closing)}\t{mismatch.Column}\t{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    Console.WriteLine($"# library-info counts: {check.Compared} compared, {check.Mismatches.Count} mismatches");
    if (!check.Agrees)
        return 1;
    if (options.Command == ScorecardCommand.Check)
        return 0;

    IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(assets, columns, options.Timing, progress => Console.Error.WriteLine(progress), closings);
    if (options.TsvPath is { } tsvPath)
    {
        using StreamWriter tsv = File.CreateText(tsvPath);
        Scorecard.WriteTsv(cells, tsv);
    }

    Console.Write(Scorecard.Report(cells, oracle.Name, shape));
    Console.Write(AllocationReport(assets, columns));
    return 0;
}
finally
{
    foreach (ScorecardAsset<LibraryInfoAsset> asset in assets)
        asset.Asset.Dispose();
}

/// <summary>The production calls as Library Info makes them, one per row.</summary>
static LaneCounts Old(LibraryInfoAsset asset)
{
    var question = new ClassificationQuestion(MethodClassificationAnalyzer.Async, ClassificationClosing.Count);
    int async = MethodClassificationQuery.Execute(asset.Session, [question]).AnswerTo(question) switch
    {
        ClassificationAnswer.Count count => count.Value,
        ClassificationAnswer answer => throw new InvalidOperationException($"The Async Count terminal did not answer: {answer}"),
    };
    int extension = ExtensionMethodsQuery.Execute(asset.Session) switch
    {
        ExtensionMethodsResult.Available available => available.Methods.Count(static member => member.Kind == "method"),
        ExtensionMethodsResult.Failed failed => throw new InvalidOperationException("Extension Methods failed.", failed.Error),
        var other => throw new InvalidOperationException($"Unknown extension result: {other}"),
    };
    int union = UnionTypesQuery.Execute(asset.Session) switch
    {
        UnionTypesResult.Available available => available.Unions.Length,
        UnionTypesResult.Failed failed => throw new InvalidOperationException("Union Types failed.", failed.Error),
        var other => throw new InvalidOperationException($"Unknown union result: {other}"),
    };
    return new(async, extension, union);
}

/// <summary>
/// Bytes allocated on the current thread by one answer per column and asset,
/// after warmup. Allocation is reported beside time because a column that
/// trades bytes for microseconds is a different result from one that wins both.
/// </summary>
static string AllocationReport(IReadOnlyList<ScorecardAsset<LibraryInfoAsset>> assets, IReadOnlyList<ScorecardColumn<LibraryInfoAsset, LaneCount>> columns)
{
    var text = new System.Text.StringBuilder();
    text.AppendLine().AppendLine("Allocated bytes per answer (current thread, after warmup).").AppendLine();
    text.Append("| Asset |");
    foreach (ScorecardColumn<LibraryInfoAsset, LaneCount> column in columns)
        text.Append(' ').Append(column.Name).Append(" |");
    text.AppendLine().Append("| --- |");
    foreach (ScorecardColumn<LibraryInfoAsset, LaneCount> _ in columns)
        text.Append(" ---: |");
    text.AppendLine();
    foreach (ScorecardAsset<LibraryInfoAsset> asset in assets)
    {
        text.Append("| ").Append(asset.Name).Append(" |");
        foreach (ScorecardColumn<LibraryInfoAsset, LaneCount> column in columns)
        {
            for (int warmup = 0; warmup < 3; warmup++)
                column.Answer(ScorecardClosing.Count, asset.Asset);
            long before = GC.GetAllocatedBytesForCurrentThread();
            column.Answer(ScorecardClosing.Count, asset.Asset);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            text.Append(' ').Append(allocated.ToString("N0", CultureInfo.InvariantCulture)).Append(" |");
        }

        text.AppendLine();
    }

    return text.ToString();
}
