using DotnetInspector.PerformanceOracles;

if (args.Length is < 2 or > 3
    || (args[0] != "check" && args[0] != "time"))
{
    Console.Error.WriteLine(
        "Usage: membergroup-scorecard <check|time> <System.Text.Json.dll> [System.Private.CoreLib.dll]");
    return 2;
}

var assemblies = new Dictionary<string, string>
{
    [MemberGroupPopulation.SystemTextJson] = Path.GetFullPath(args[1]),
};
if (args.Length == 3)
    assemblies[MemberGroupPopulation.CoreLib] = Path.GetFullPath(args[2]);
foreach (string path in assemblies.Values)
{
    if (!File.Exists(path))
    {
        Console.Error.WriteLine($"Assembly does not exist: {path}");
        return 2;
    }
}

if (args[0] == "check")
{
    MemberGroupScorecardCheck check = MemberGroupPopulation.Check(assemblies);
    Console.WriteLine($"{check.Compared} answers compared; {check.Mismatches.Count} mismatches.");
    foreach (MemberGroupScorecardAnswerHash answer in check.AnswerHashes)
        Console.WriteLine($"{answer.Scenario}\t{answer.Terminal}\t{answer.Hash}\tcount={answer.Count}\trows={answer.Rows}\ttypeMethods={answer.TypeMethods}");
    foreach (MemberGroupScorecardMismatch mismatch in check.Mismatches)
        Console.WriteLine($"MISMATCH {mismatch.Scenario}: {mismatch.Terminal} {mismatch.Column}");
    return check.Agrees ? 0 : 1;
}

MemberGroupScorecardResult result = MemberGroupPopulation.Measure(assemblies);
Console.Write(MemberGroupPopulation.Report(result));
return result.Check.Agrees ? 0 : 1;
