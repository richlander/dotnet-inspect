#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj

// Measures FastDiff over one Library image pair and checks it against the
// complete API comparison and canonical IL comparison.
// Usage: measure-fast-diff <before.dll> <after.dll> [iterations] [public|all]

using System.Diagnostics;
using System.Reflection.PortableExecutable;
using ILInspector.ILDiff;
using ILInspector.Metadata;
using Inspector.Findings;

if (args.Length is < 2 or > 4)
{
    Console.Error.WriteLine("Usage: measure-fast-diff <before.dll> <after.dll> [iterations] [public|all]");
    return 2;
}

string beforePath = args[0];
string afterPath = args[1];
int iterations = args.Length > 2 ? int.Parse(args[2]) : 20;
FastDiffScope scope = args.Length > 3 && args[3] == "all" ? FastDiffScope.IncludeAll : FastDiffScope.Public;
byte[] beforeBytes = File.ReadAllBytes(beforePath);
byte[] afterBytes = File.ReadAllBytes(afterPath);

FastDiffResult Run()
{
    using var before = new PEReader(new MemoryStream(beforeBytes, writable: false));
    using var after = new PEReader(new MemoryStream(afterBytes, writable: false));
    return FastDiff.Compare(before, after, scope);
}

FastDiffResult result = Run();
for (int i = 0; i < 3; i++)
    Run();
var samples = new List<double>(iterations);
for (int i = 0; i < iterations; i++)
{
    long start = Stopwatch.GetTimestamp();
    Run();
    samples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
}
samples.Sort();

// Complete API comparison over the same scope.
ApiSurfaceExtractionScope surfaceScope = scope == FastDiffScope.Public
    ? ApiSurfaceExtractionScope.Public
    : ApiSurfaceExtractionScope.IncludeAll;
ApiSurface Surface(byte[] bytes)
{
    using var reader = new PEReader(new MemoryStream(bytes, writable: false));
    return ApiSurfaceExtractor.Extract(reader, surfaceScope);
}
ApiFindingComparison complete = MetadataFindings.CompareApi(
    Surface(beforeBytes), Surface(afterBytes), new FindingSubject("api", "API"),
    new ApiDiffOptions(ApiDiffScope.All));
var apiChanged = new HashSet<string>(StringComparer.Ordinal);
foreach (TypeDiff type in complete.ApiDiff.TypeDiffs)
    apiChanged.Add(type.TypeFullName);
if (complete.Types.Value is FindingComparison<ApiTypeHandle>.Complete types)
{
    foreach (PairFinding<ApiTypeHandle> pair in types.Pairs)
    {
        if (pair.Kind != PairKind.Present)
            apiChanged.Add(Payload<ApiTypeHandle>(pair).TypeFullName);
    }
}
if (complete.Members.Value is FindingComparison<ApiMemberHandle>.Complete members)
{
    foreach (PairFinding<ApiMemberHandle> pair in members.Pairs)
    {
        if (pair.Kind != PairKind.Present)
            apiChanged.Add(Payload<ApiMemberHandle>(pair).TypeFullName);
    }
}

// Canonical IL comparison: the declared owners of changed bodies.
IlAssemblyDiffResult il = IlAssemblyDiff.CompareStreams(
    new MemoryStream(beforeBytes, writable: false), beforePath,
    new MemoryStream(afterBytes, writable: false), afterPath,
    maxExamples: int.MaxValue,
    normalization: IlBodyDiffNormalization.NormalizeCurrentAssemblyScope
        | IlBodyDiffNormalization.NormalizePlatformAssemblyScope).Diff;
var bodyChanged = new HashSet<string>(StringComparer.Ordinal);
foreach (IlAssemblyDiffExample example in il.Examples)
    bodyChanged.Add(DeclaredOwner(example.Method));

Dictionary<string, FastDiffTypeState> byName = [];
foreach (FastDiffTypeState state in result.Types)
    byName[state.FullName] = state;

int apiMissed = apiChanged.Count(name => byName.TryGetValue(name, out var s) && s.Api == FastDiffState.Unchanged);
int apiUnmatched = apiChanged.Count(name => !byName.ContainsKey(name));
int apiOverReported = result.Types.Count(s => s.Api == FastDiffState.Changed && !apiChanged.Contains(s.FullName));
int bodyMissed = bodyChanged.Count(name => byName.TryGetValue(name, out var s) && s.Body == FastDiffState.Unchanged);
int bodyUnmatched = bodyChanged.Count(name => !byName.ContainsKey(name));

Console.WriteLine($"pair\t{Path.GetFileName(beforePath)}\tscope={scope}");
Console.WriteLine(
    $"types\t{result.Types.Length}\tapiChanged={result.Types.Count(s => s.Api == FastDiffState.Changed)}"
        + $"\tbodyChanged={result.Types.Count(s => s.Body == FastDiffState.Changed)}"
        + $"\tanyChanged={result.Types.Count(s => s.Api == FastDiffState.Changed || s.Body == FastDiffState.Changed)}"
        + $"\tindeterminate={result.Types.Count(s => s.Api == FastDiffState.Indeterminate || s.Body == FastDiffState.Indeterminate)}");
Console.WriteLine($"work\tbodies={result.Receipt.BodiesCompared}\tilBytes={result.Receipt.IlBytesCompared}");
Console.WriteLine(
    $"ms\tmedian={samples[samples.Count / 2]:F2}\tp95={samples[(int)(samples.Count * 0.95)]:F2}"
        + $"\tmin={samples[0]:F2}\tsamples={samples.Count}");
Console.WriteLine(
    $"api-oracle\tchanged={apiChanged.Count}\tmissed={apiMissed}\tunmatched={apiUnmatched}\tover-reported={apiOverReported}");
Console.WriteLine(
    $"body-oracle\tchangedOwners={bodyChanged.Count}\tmissed={bodyMissed}\tunmatched={bodyUnmatched}"
        + $"\tilFailures={il.FailureCount}");
foreach (string name in apiChanged.Where(n => byName.TryGetValue(n, out var s) && s.Api == FastDiffState.Unchanged).Take(20))
    Console.WriteLine($"  api-missed\t{name}");
foreach (string name in bodyChanged.Where(n => byName.TryGetValue(n, out var s) && s.Body == FastDiffState.Unchanged).Take(20))
{
    Console.WriteLine($"  body-missed\t{name}");
    IlAssemblyDiffExample example = il.Examples.First(e => DeclaredOwner(e.Method) == name);
    Console.WriteLine($"    {example.Method}");
    foreach (IlDiffRow row in example.Diff.Rows.Take(3))
        Console.WriteLine($"    {row.Kind} {row.Message}");
}
if (Environment.GetEnvironmentVariable("EXPLAIN") == "1")
{
    using var explainBefore = new PEReader(new MemoryStream(beforeBytes, writable: false));
    using var explainAfter = new PEReader(new MemoryStream(afterBytes, writable: false));
    foreach (FastDiffTypeState state in result.Types.Where(s => s.Api == FastDiffState.Changed && !apiChanged.Contains(s.FullName)).Take(40))
    {
        var (api, _) = FastDiff.Explain(explainBefore, explainAfter, scope, state.FullName);
        Console.WriteLine($"  api-over\t{state.FullName}\n{api}");
    }
    foreach (FastDiffTypeState state in result.Types.Where(s => s.Body == FastDiffState.Changed && !bodyChanged.Contains(s.FullName)).Take(40))
    {
        var (_, body) = FastDiff.Explain(explainBefore, explainAfter, scope, state.FullName);
        Console.WriteLine($"  body-over\t{state.FullName}\n{body}");
    }
}
foreach (string name in apiChanged.Where(n => !byName.ContainsKey(n)).Take(10))
    Console.WriteLine($"  api-unmatched\t{name}");
foreach (string name in bodyChanged.Where(n => !byName.ContainsKey(n)).Take(10))
    Console.WriteLine($"  body-unmatched\t{name}");
return apiMissed == 0 && bodyMissed == 0 ? 0 : 1;

static T Payload<T>(PairFinding<T> pair) where T : notnull
    => (((IPairFinding)pair).New as Finding<T> ?? (Finding<T>)((IPairFinding)pair).Old!).Payload;

static string DeclaredOwner(string method)
{
    string type = method[..method.IndexOf("::", StringComparison.Ordinal)];
    string[] segments = type.Split('/', '+');
    int declared = segments.Length;
    while (declared > 1 && segments[declared - 1].StartsWith('<'))
        declared--;
    return string.Join('.', segments[..declared]);
}
