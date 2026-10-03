using DotnetInspector.PerformanceOracles;
using ILInspector.Metadata;
using MemberGroupScorecard;

bool declaredCommand =
    args.Length > 0
    && args[0] is "declared-check" or "declared-time";
if ((!declaredCommand && args.Length != 2)
    || (declaredCommand && args.Length is not (2 or 3))
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
            + "declared-check|declared-time> <assembly> "
            + "[metadata-type-name]");
    return 2;
}

string path = Path.GetFullPath(args[1]);
if (!File.Exists(path))
{
    Console.Error.WriteLine(
        $"Assembly does not exist: {path}");
    return 2;
}

MetadataTypeDefinitionName? declaredType =
    declaredCommand
        ? ParseTypeName(
            args.Length == 3
                ? args[2]
                : "System.Text.Json.JsonSerializer")
        : null;

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
        await DeclaredMethodOperationCheck.CheckAsync(
            path,
            declaredType!);
    DeclaredMethodScorecardCheck check =
        DeclaredMethodPopulation.Check(
            path,
            declaredType!);
    if (operationCount != check.Count)
    {
        Console.Error.WriteLine(
            "QuerySpace operation and source-native scorecard disagree.");
        return 1;
    }
    Console.WriteLine(
        $"{DisplayName(declaredType!)}: "
            + $"{check.Count} declared MethodDefs; {check.AnswerHash}; "
            + "QuerySpace operation agrees.");
    return 0;
}

if (args[0] == "declared-time")
{
    DeclaredMethodOperationScorecardResult operationResult =
        await DeclaredMethodOperationCheck.MeasureAsync(
            path,
            declaredType!);
    DeclaredMethodScorecardResult declaredResult =
        DeclaredMethodPopulation.Measure(
            path,
            declaredType!);
    PreparedQuerySpaceScorecardResult preparedQuerySpace =
        DeclaredMethodPopulation.MeasurePreparedQuerySpace(
            path,
            declaredType!);
    PreparedProducerScorecardResult preparedProducer =
        DeclaredMethodPopulation.MeasurePreparedProducer(
            path,
            declaredType!);
    if (operationResult.Count != declaredResult.Check.Count
        || operationResult.Count != preparedQuerySpace.Count
        || operationResult.Count != preparedProducer.Count)
    {
        Console.Error.WriteLine(
            "QuerySpace operation and source-native scorecards disagree.");
        return 1;
    }
    Console.Write(
        DeclaredMethodOperationCheck.Report(operationResult));
    Console.Write(
        DeclaredMethodPopulation.ReportPreparedQuerySpace(
            preparedQuerySpace));
    Console.Write(
        DeclaredMethodPopulation.ReportPreparedProducer(
            preparedProducer));
    Console.Write(
        DeclaredMethodPopulation.Report(declaredResult));
    return 0;
}

MemberGroupScorecardResult result =
    MemberGroupPopulation.Measure(path);
Console.Write(
    MemberGroupPopulation.Report(result));
return result.Check.Agrees ? 0 : 1;

static MetadataTypeDefinitionName ParseTypeName(string value)
{
    ArgumentException.ThrowIfNullOrWhiteSpace(value);
    int namespaceEnd = value.LastIndexOf('.');
    string @namespace =
        namespaceEnd < 0
            ? ""
            : value[..namespaceEnd];
    string metadataName = value[(namespaceEnd + 1)..];
    string[] segments =
        metadataName.Split(
            '+',
            StringSplitOptions.RemoveEmptyEntries);
    return MetadataTypeDefinitionName.Create(
            @namespace,
            [.. segments])
        is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new ArgumentException(
                $"Invalid metadata Type name: {value}",
                nameof(value));
}

static string DisplayName(MetadataTypeDefinitionName type) =>
    string.IsNullOrEmpty(type.Namespace)
        ? string.Join('+', type.Segments)
        : $"{type.Namespace}.{string.Join('+', type.Segments)}";
