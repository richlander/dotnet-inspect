using DotnetInspector.PerformanceOracles;

if (!ScorecardCommandLine.TryParse(
        args,
        out ScorecardOptions? options,
        out string? error))
{
    Console.Error.WriteLine(error);
    return 2;
}

var shape = new ScorecardShape(
    N: 10,
    WindowFirst: 100,
    WindowLast: 110);
IReadOnlyList<
    ScorecardAsset<LibraryFamilyRoleScorecardAsset>> assets =
        LibraryFamilyRolePopulationScorecard.LoadAssets(
            options!.Assets);
ScorecardColumn<
    LibraryFamilyRoleScorecardAsset,
    ILInspector.Research.LibraryFamilyRoleRow> oracle =
        LibraryFamilyRolePopulationScorecard.NLinqColumn(shape);
ScorecardColumn<
    LibraryFamilyRoleScorecardAsset,
    ILInspector.Research.LibraryFamilyRoleRow>[] columns =
[
    LibraryFamilyRolePopulationScorecard.LinqColumn(shape),
    oracle,
    LibraryFamilyRolePopulationScorecard.QuerySpaceColumn(shape),
];
ScorecardClosing[] closings =
[
    ScorecardClosing.Count,
    ScorecardClosing.Head,
    ScorecardClosing.Tail,
    ScorecardClosing.Rows,
    ScorecardClosing.Window,
];

ScorecardCheck check = Scorecard.Check(
    assets,
    oracle,
    columns,
    LibraryFamilyRolePopulationScorecard.RowText,
    closings: closings);
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
            + "window failures: "
            + (check.WindowFailures.Count == 0
                ? "none"
                : string.Join(", ", check.WindowFailures)));
    return 0;
}

IReadOnlyList<ScorecardCell> cells = Scorecard.Measure(
    assets,
    columns,
    options.Timing,
    progress => Console.Error.WriteLine(progress),
    closings);
if (options.TsvPath is { } tsvPath)
{
    using StreamWriter tsv = File.CreateText(tsvPath);
    Scorecard.WriteTsv(cells, tsv);
}

Console.Write(Scorecard.Report(cells, oracle.Name, shape));
return 0;
