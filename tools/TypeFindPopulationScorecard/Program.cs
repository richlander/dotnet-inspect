using DotnetInspector.PerformanceOracles;

// type-find-population-scorecard <pattern> check <assembly>...
//   Checks LINQ and the shipping selector against the pinned NLinq oracle.
// type-find-population-scorecard <pattern> time [--rounds N] [--budget-ms N]
//   [--tsv <path>] <assembly>...
//   Times the same settlement contract and reports ratios to NLinq.
if (args.Length == 0)
{
    Console.Error.WriteLine(
        "usage: <pattern> check|time [--rounds N] [--budget-ms N] "
        + "[--tsv <path>] <assembly>...");
    return 2;
}

string pattern = args[0];
if (!ScorecardCommandLine.TryParse(args[1..], out ScorecardOptions? options, out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape(N: 3, WindowFirst: 2, WindowLast: 4);
IReadOnlyList<ScorecardAsset<TypeFindPopulationScorecardAsset>> assets =
    TypeFindPopulationScorecard.LoadAssets(pattern, options!.Assets);
ScorecardColumn<TypeFindPopulationScorecardAsset, TypeFindPopulationScorecardRow> oracle =
    TypeFindPopulationScorecard.NLinqColumn(shape);
ScorecardColumn<TypeFindPopulationScorecardAsset, TypeFindPopulationScorecardRow>[] columns =
[
    TypeFindPopulationScorecard.LinqColumn(shape),
    oracle,
    TypeFindPopulationScorecard.SelectorColumn(shape),
];

ScorecardCheck check = Scorecard.Check(
    assets,
    oracle,
    columns,
    TypeFindPopulationScorecard.RowText);
if (!check.Agrees)
{
    foreach (ScorecardMismatch mismatch in check.Mismatches)
        Console.Error.WriteLine(mismatch);
    return 1;
}

if (options.Command == ScorecardCommand.Check)
{
    Console.WriteLine(
        $"checked {check.Compared} answers; "
        + $"window failures: {(check.WindowFailures.Count == 0 ? "none" : string.Join(", ", check.WindowFailures))}");
    return 0;
}

IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(
    assets,
    columns,
    options.Timing,
    progress => Console.Error.WriteLine(progress));
if (options.TsvPath is { } tsvPath)
{
    using StreamWriter tsv = File.CreateText(tsvPath);
    Scorecard.WriteTsv(cells, tsv);
}

Console.Write(Scorecard.Report(cells, oracle.Name, shape));
return 0;
