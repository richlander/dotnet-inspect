using DotnetInspector.PerformanceOracles;
using ILInspector.AnalysisHarness;

if (!ScorecardCommandLine.TryParse(
        args,
        out ScorecardOptions? options,
        out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

IReadOnlyList<BodyUseScorecardAsset> assets =
    BodyUseScorecard.LoadAssets(options!.Assets);
BodyUseScorecardCheck check = BodyUseScorecard.Check(assets);
foreach (BodyUseScorecardAnswerHash answer in check.AnswerHashes)
{
    Console.WriteLine(
        $"# answer: {answer.Asset} / {answer.Closing} = {answer.Hash}");
}
foreach (BodyUseScorecardMismatch mismatch in check.Mismatches)
{
    Console.WriteLine(
        $"mismatch\t{mismatch.Asset}\t{mismatch.Closing}"
            + $"\t{mismatch.Column}"
            + $"\t{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
}
Console.WriteLine(
    $"# answers: {check.Compared} compared, "
        + $"{check.Mismatches.Count} mismatches");
if (!check.Agrees)
    return 1;
if (options.Command == ScorecardCommand.Check)
    return 0;

IReadOnlyList<BodyUseScorecardCell> cells =
    BodyUseScorecard.Measure(
        assets,
        options.Timing,
        progress => Console.Error.WriteLine(progress));
if (options.TsvPath is { } tsvPath)
{
    using StreamWriter tsv = File.CreateText(tsvPath);
    BodyUseScorecard.WriteTsv(cells, tsv);
}
Console.Write(BodyUseScorecard.Report(cells));
return 0;
