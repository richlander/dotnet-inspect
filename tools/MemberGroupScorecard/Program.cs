using DotnetInspector.PerformanceOracles;

if (args.Length != 2
    || args[0] is not ("check"
        or "time"
        or "exact-check"
        or "exact-time"))
{
    Console.Error.WriteLine(
        "Usage: membergroup-scorecard "
            + "<check|time|exact-check|exact-time> <assembly>");
    return 2;
}

string path = Path.GetFullPath(args[1]);
if (!File.Exists(path))
{
    Console.Error.WriteLine(
        $"Assembly does not exist: {path}");
    return 2;
}

if (args[0] == "check")
{
    MemberGroupScorecardCheck check =
        MemberGroupPopulation.Check(path);
    Console.WriteLine(
        $"{check.Compared} answers compared; "
            + $"{check.Mismatches.Count} mismatches.");
    foreach (MemberGroupScorecardAnswerHash answer
        in check.AnswerHashes)
    {
        Console.WriteLine(
            $"{answer.Scenario}\t{answer.Terminal}\t"
                + $"{answer.Hash}");
    }
    foreach (MemberGroupScorecardMismatch mismatch
        in check.Mismatches)
    {
        Console.WriteLine(
            $"{mismatch.Scenario}: {mismatch.Terminal} "
                + $"{mismatch.Column}");
    }
    return check.Agrees ? 0 : 1;
}

if (args[0] == "exact-check")
{
    MemberExactScorecardCheck check =
        MemberGroupPopulation.CheckExact(path);
    Console.WriteLine(
        $"{check.Compared} answers compared; "
            + $"{check.Mismatches.Count} mismatches.");
    foreach (MemberGroupScorecardAnswerHash answer
        in check.AnswerHashes)
    {
        Console.WriteLine(
            $"{answer.Scenario}\t{answer.Terminal}\t"
                + $"{answer.Hash}");
    }
    foreach (MemberGroupScorecardMismatch mismatch
        in check.Mismatches)
    {
        Console.WriteLine(
            $"{mismatch.Scenario}: {mismatch.Terminal} "
                + $"{mismatch.Column}");
    }
    return check.Agrees ? 0 : 1;
}

if (args[0] == "exact-time")
{
    MemberExactScorecardResult exactResult =
        MemberGroupPopulation.MeasureExact(path);
    Console.Write(
        MemberGroupPopulation.ReportExact(exactResult));
    return exactResult.Check.Agrees ? 0 : 1;
}

MemberGroupScorecardResult result =
    MemberGroupPopulation.Measure(path);
Console.Write(
    MemberGroupPopulation.Report(result));
return result.Check.Agrees ? 0 : 1;
