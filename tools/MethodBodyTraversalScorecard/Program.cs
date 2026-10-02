using DotnetInspector.PerformanceOracles;

// method-body-traversal-scorecard check <assembly>...
// method-body-traversal-scorecard time [--rounds N] [--budget-ms N]
//   [--tsv <path>] <assembly>...
if (!ScorecardCommandLine.TryParse(
        args,
        out ScorecardOptions? options,
        out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape();
ScorecardColumn<PreparedMethodBodies, MethodBodyScanSummary>[] columns =
    MethodBodyTraversalPrototype.Columns();
ScorecardColumn<PreparedMethodBodies, MethodBodyScanSummary> oracle =
    columns[0];
IReadOnlyList<ScorecardAsset<PreparedMethodBodies>> assets =
    PreparedMethodBodies.LoadAssets(options!.Assets);

try
{
    ScorecardCheck check = Scorecard.Check(
        assets,
        oracle,
        columns,
        MethodBodyTraversalPrototype.RowText,
        closings: MethodBodyTraversalPrototype.Closings);
    foreach (ScorecardMismatch mismatch in check.Mismatches)
    {
        Console.WriteLine(
            $"mismatch\t{mismatch.Asset}\t{mismatch.Column}\t"
            + $"{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    }

    foreach (ScorecardAsset<PreparedMethodBodies> asset in assets)
    {
        MethodBodyScanSummary summary = AssertSummary(
            oracle.Answer(ScorecardClosing.Rows, asset.Asset));
        Console.WriteLine($"answer\t{asset.Name}\t{summary}");
    }

    Console.WriteLine(
        $"# method-body traversal: {check.Compared} compared, "
        + $"{check.Mismatches.Count} mismatches");
    if (!check.Agrees)
        return 1;
    if (options.Command == ScorecardCommand.Check)
        return 0;

    IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(
        assets,
        columns,
        options.Timing,
        progress => Console.Error.WriteLine(progress),
        MethodBodyTraversalPrototype.Closings);
    if (options.TsvPath is { } tsvPath)
    {
        using StreamWriter tsv = File.CreateText(tsvPath);
        Scorecard.WriteTsv(cells, tsv);
    }

    Console.Write(Scorecard.Report(cells, oracle.Name, shape));
    return 0;
}
finally
{
    foreach (ScorecardAsset<PreparedMethodBodies> asset in assets)
        asset.Asset.Dispose();
}

static MethodBodyScanSummary AssertSummary(
    ScorecardAnswer<MethodBodyScanSummary> answer)
{
    if (answer.Rows is not { Count: 1 } rows)
    {
        throw new InvalidOperationException(
            "A method-body traversal answer must contain one summary.");
    }

    return rows[0];
}
