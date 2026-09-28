using System.Reflection.PortableExecutable;
using DotnetInspector.PerformanceOracles;

// queryspace-scorecard check <assembly>...
//   Checks that every column answers every closing as the NLinq oracle does.
// queryspace-scorecard time [--rounds N] [--budget-ms N] [--tsv <path>] <assembly>...
//   Times every column with rotated rounds and prints the ratios to NLinq and
//   the absolute medians.
if (!ScorecardCommandLine.TryParse(args, out ScorecardOptions? options, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape();
ScorecardColumn<PEReader, MethodTextRow> oracle = PublicMethods.NLinqColumn(shape);
ScorecardColumn<PEReader, MethodTextRow>[] columns = [PublicMethods.LinqColumn(shape), oracle];

// Each asset's identity is its position; display names are made distinct.
IReadOnlyList<ScorecardAsset<PEReader>> assets = PublicMethods.LoadAssets(options!.Assets);

try
{
    ScorecardCheck check = Scorecard.Check(assets, oracle, columns, PublicMethods.RowText);
    foreach (ScorecardMismatch mismatch in check.Mismatches)
        Console.WriteLine($"mismatch\t{mismatch.Asset}\t{shape.Label(mismatch.Closing)}\t{mismatch.Column}\t{mismatch.Answer}\toracle={mismatch.OracleAnswer}");
    Console.WriteLine($"# answers: {check.Compared} compared, {check.Mismatches.Count} mismatches, {check.WindowFailures.Count} strict-window failures");
    if (!check.Agrees)
        return 1;

    // Every column agreed, so each listed window failed in every column; it is
    // an outcome, never timed or scored.
    foreach (string asset in check.WindowFailures)
        Console.WriteLine($"window-failed\t{asset}\t{shape.Label(ScorecardClosing.Window)}\tevery column fails: the window does not exist");
    if (options.Command == ScorecardCommand.Check)
        return 0;

    IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(assets, columns, options.Timing, progress => Console.Error.WriteLine(progress));
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
    foreach (ScorecardAsset<PEReader> asset in assets)
        asset.Asset.Dispose();
}
