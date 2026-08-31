#:project ../../../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:property OwnsItsOwnStderr=true

using ILInspector.Metadata;

if (args is not [string assemblyPath, string expectedLowering])
{
    Console.Error.WriteLine(
        "Usage: verify-async-lowering.cs <assembly> <compiler|runtime>");
    return 1;
}

MethodClassification expected = expectedLowering switch
{
    "compiler" => MethodClassification.StateMachineAsync,
    "runtime" => MethodClassification.RuntimeAsync,
    _ => throw new ArgumentException(
        "Expected lowering must be 'compiler' or 'runtime'.",
        nameof(expectedLowering)),
};

using FileStream stream = File.OpenRead(assemblyPath);
ClassifiedMethodInfo[] matches = MethodClassificationScanner.Scan(stream)
    .Where(method =>
        method.DeclaringType == "InspectionEngine"
        && method.MethodName == "AsyncLoweringCanary")
    .ToArray();
if (matches is not [ClassifiedMethodInfo canary])
{
    Console.Error.WriteLine(
        $"Expected exactly one InspectionEngine.AsyncLoweringCanary in "
        + $"{assemblyPath}; found {matches.Length}.");
    return 1;
}

if (canary.Classification != expected)
{
    Console.Error.WriteLine(
        $"{assemblyPath} lowers InspectionEngine.AsyncLoweringCanary as "
        + $"{canary.Classification}; expected {expected}.");
    return 1;
}

Console.WriteLine(
    $"InspectWeb async canary has {expectedLowering} lowering in "
    + $"{assemblyPath}.");
return 0;
