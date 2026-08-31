#:project ../../../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:property OwnsItsOwnStderr=true

using ILInspector.Metadata;
using System.Text.Json;

if (args is not
    [string assemblyPath, string expectedLowering, string censusPath])
{
    Console.Error.WriteLine(
        "Usage: verify-async-lowering.cs "
        + "<assembly> <compiler|runtime> <census.json>");
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
ClassifiedMethodInfo[] asyncMethods = MethodClassificationScanner.Scan(stream)
    .Where(method =>
        method.Classification is MethodClassification.StateMachineAsync
            or MethodClassification.RuntimeAsync)
    .ToArray();
int compilerAsyncCount = asyncMethods.Count(method =>
    method.Classification == MethodClassification.StateMachineAsync);
int runtimeAsyncCount = asyncMethods.Count(method =>
    method.Classification == MethodClassification.RuntimeAsync);
if (asyncMethods.Length == 0)
{
    Console.Error.WriteLine(
        $"Expected public async methods in {assemblyPath}; found none.");
    return 1;
}

int expectedAsyncCount =
    expected == MethodClassification.StateMachineAsync
        ? compilerAsyncCount
        : runtimeAsyncCount;
if (expectedAsyncCount != asyncMethods.Length)
{
    Console.Error.WriteLine(
        $"Expected all {asyncMethods.Length} public async methods in "
        + $"{assemblyPath} to use {expectedLowering} lowering; found "
        + $"compiler={compilerAsyncCount}, runtime={runtimeAsyncCount}.");
    return 1;
}

ClassifiedMethodInfo[] matches = asyncMethods
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

Directory.CreateDirectory(
    Path.GetDirectoryName(Path.GetFullPath(censusPath))!);
using (FileStream censusStream = File.Create(censusPath))
{
    using (var writer = new Utf8JsonWriter(censusStream))
    {
        writer.WriteStartObject();
        writer.WriteNumber("async_method_count", asyncMethods.Length);
        writer.WriteNumber(
            "compiler_async_method_count",
            compilerAsyncCount);
        writer.WriteNumber(
            "runtime_async_method_count",
            runtimeAsyncCount);
        writer.WriteEndObject();
    }

    censusStream.WriteByte((byte)'\n');
}

Console.WriteLine(
    $"InspectWeb async census found {asyncMethods.Length} public async methods "
    + $"in {assemblyPath}: compiler={compilerAsyncCount}, "
    + $"runtime={runtimeAsyncCount}; canary={expectedLowering}.");
return 0;
