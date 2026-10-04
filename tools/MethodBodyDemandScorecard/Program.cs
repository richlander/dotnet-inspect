using DotnetInspector.PerformanceOracles;

// method-body-demand-scorecard check <assembly>...
// method-body-demand-scorecard time [--rounds N] [--budget-ms N]
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
ScorecardColumn<
    PreparedMethodBodies,
    MethodThrowPresenceSummary>[] columns =
        MethodBodyDemandPrototype.Columns();
ScorecardColumn<PreparedMethodBodies, MethodThrowPresenceSummary> oracle =
    columns[0];
IReadOnlyList<ScorecardAsset<PreparedMethodBodies>> assets =
    PreparedMethodBodies.LoadAssets(options!.Assets);

try
{
    bool exactAgreement = true;
    foreach (ScorecardAsset<PreparedMethodBodies> asset in assets)
    {
        foreach (MethodThrowPresenceMismatch mismatch
            in asset.Asset.CheckThrowPresenceAgreement())
        {
            exactAgreement = false;
            Console.WriteLine(
                $"body-mismatch\t{asset.Name}\t{mismatch.BodyIndex}\t"
                + $"streaming={mismatch.Streaming}\t"
                + $"lazy-shallow={mismatch.LazyShallow}\t"
                + $"eager-decoded={mismatch.EagerDecoded}");
        }
    }

    ScorecardCheck check = Scorecard.Check(
        assets,
        oracle,
        columns,
        MethodBodyDemandPrototype.RowText,
        closings: MethodBodyDemandPrototype.Closings);
    foreach (ScorecardMismatch mismatch in check.Mismatches)
    {
        Console.WriteLine(
            $"mismatch\t{mismatch.Asset}\t{mismatch.Column}\t"
            + $"{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    }

    foreach (ScorecardAsset<PreparedMethodBodies> asset in assets)
    {
        MethodThrowPresenceSummary summary = AssertSummary(
            oracle.Answer(ScorecardClosing.Rows, asset.Asset));
        Console.WriteLine($"answer\t{asset.Name}\t{summary}");
    }

    Console.WriteLine(
        $"# method-body demand: {check.Compared} compared, "
        + $"{check.Mismatches.Count} mismatches");
    if (!exactAgreement || !check.Agrees)
        return 1;
    if (options.Command == ScorecardCommand.Check)
        return 0;

    IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(
        assets,
        columns,
        options.Timing,
        progress => Console.Error.WriteLine(progress),
        MethodBodyDemandPrototype.Closings);
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

static MethodThrowPresenceSummary AssertSummary(
    ScorecardAnswer<MethodThrowPresenceSummary> answer)
{
    if (answer.Rows is not { Count: 1 } rows)
    {
        throw new InvalidOperationException(
            "A method-body demand answer must contain one summary.");
    }

    return rows[0];
}
