#:project ../../../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:property OwnsItsOwnStderr=true

using ILInspector.Metadata;
using System.Text.Json;

if (args.Length < 3)
{
    Console.Error.WriteLine(
        "Usage: verify-async-lowering.cs "
        + "<assembly> [<assembly> ...] <compiler|runtime> <census.json>");
    return 1;
}

string[] assemblyPaths = args[..^2]
    .Select(Path.GetFullPath)
    .ToArray();
string expectedLowering = args[^2];
string censusPath = args[^1];
if (assemblyPaths.Distinct(StringComparer.Ordinal).Count() != assemblyPaths.Length)
{
    Console.Error.WriteLine("Async census assembly paths must be unique.");
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

var assemblyMethods = new List<(string Path, ClassifiedMethodInfo[] Methods)>();
foreach (string assemblyPath in assemblyPaths)
{
    using FileStream stream = File.OpenRead(assemblyPath);
    ClassifiedMethodInfo[] methods = MethodClassificationScanner.Scan(stream)
        .Where(method =>
            method.Classification is MethodClassification.StateMachineAsync
                or MethodClassification.RuntimeAsync)
        .ToArray();
    if (methods.Length == 0)
    {
        Console.Error.WriteLine(
            $"Expected public async methods in {assemblyPath}; found none.");
        return 1;
    }

    assemblyMethods.Add((assemblyPath, methods));
}

ClassifiedMethodInfo[] asyncMethods =
[
    .. assemblyMethods.SelectMany(assembly => assembly.Methods),
];
int compilerAsyncCount = asyncMethods.Count(method =>
    method.Classification == MethodClassification.StateMachineAsync);
int runtimeAsyncCount = asyncMethods.Count(method =>
    method.Classification == MethodClassification.RuntimeAsync);

int expectedAsyncCount =
    expected == MethodClassification.StateMachineAsync
        ? compilerAsyncCount
        : runtimeAsyncCount;
if (expectedAsyncCount != asyncMethods.Length)
{
    Console.Error.WriteLine(
        $"Expected all {asyncMethods.Length} public async methods across "
        + $"{assemblyPaths.Length} assemblies to use {expectedLowering} lowering; found "
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
        + $"the assembly set; found {matches.Length}.");
    return 1;
}

if (canary.Classification != expected)
{
    Console.Error.WriteLine(
        $"InspectionEngine.AsyncLoweringCanary lowers as "
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
        writer.WriteNumber("assembly_count", assemblyMethods.Count);
        writer.WriteNumber("async_method_count", asyncMethods.Length);
        writer.WriteNumber(
            "compiler_async_method_count",
            compilerAsyncCount);
        writer.WriteNumber(
            "runtime_async_method_count",
            runtimeAsyncCount);
        writer.WriteStartArray("assemblies");
        foreach ((string path, ClassifiedMethodInfo[] methods) in assemblyMethods)
        {
            writer.WriteStartObject();
            writer.WriteString("file", Path.GetFileName(path));
            writer.WriteNumber("async_method_count", methods.Length);
            writer.WriteNumber(
                "compiler_async_method_count",
                methods.Count(method =>
                    method.Classification == MethodClassification.StateMachineAsync));
            writer.WriteNumber(
                "runtime_async_method_count",
                methods.Count(method =>
                    method.Classification == MethodClassification.RuntimeAsync));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    censusStream.WriteByte((byte)'\n');
}

Console.WriteLine(
    $"InspectWeb async census found {asyncMethods.Length} public async methods "
    + $"across {assemblyMethods.Count} assemblies: compiler={compilerAsyncCount}, "
    + $"runtime={runtimeAsyncCount}; canary={expectedLowering}.");
foreach ((string path, ClassifiedMethodInfo[] methods) in assemblyMethods)
{
    Console.WriteLine(
        $"  {Path.GetFileName(path)}: {methods.Length} "
        + $"(compiler={methods.Count(method => method.Classification == MethodClassification.StateMachineAsync)}, "
        + $"runtime={methods.Count(method => method.Classification == MethodClassification.RuntimeAsync)})");
}
return 0;
