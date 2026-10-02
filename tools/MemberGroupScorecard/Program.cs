using DotnetInspector.PerformanceOracles;
using MemberGroupScorecard;

if (args.Length != 2
    || args[0] is not ("check"
        or "time"
        or "exact-check"
        or "exact-time"
        or "declared-check"
        or "declared-time"))
{
    Console.Error.WriteLine(
        "Usage: membergroup-scorecard "
            + "<check|time|exact-check|exact-time|"
            + "declared-check|declared-time> <assembly>");
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

if (args[0] == "declared-check")
{
    int operationCount =
        await DeclaredMethodOperationCheck.CheckAsync(path);
    DeclaredMethodScorecardCheck check =
        DeclaredMethodPopulation.Check(path);
    if (operationCount != check.Count)
    {
        Console.Error.WriteLine(
            "QuerySpace operation and source-native scorecard disagree.");
        return 1;
    }
    Console.WriteLine(
        $"{check.Count} declared MethodDefs; {check.AnswerHash}; "
            + "QuerySpace operation agrees.");
    return 0;
}

if (args[0] == "declared-time")
{
    int operationCount =
        await DeclaredMethodOperationCheck.CheckAsync(path);
    DeclaredMethodScorecardResult declaredResult =
        DeclaredMethodPopulation.Measure(path);
    if (operationCount != declaredResult.Check.Count)
    {
        Console.Error.WriteLine(
            "QuerySpace operation and source-native scorecard disagree.");
        return 1;
    }
    Console.Write(
        DeclaredMethodPopulation.Report(declaredResult));
    return 0;
}

MemberGroupScorecardResult result =
    MemberGroupPopulation.Measure(path);
Console.Write(
    MemberGroupPopulation.Report(result));
return result.Check.Agrees ? 0 : 1;
