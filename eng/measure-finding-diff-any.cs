#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj

// Measures FindingDiffAny over one Library image pair and checks it against the
// complete API comparison and canonical IL comparison.
// Usage: measure-finding-diff-any <before.dll> <after.dll> [iterations]
// Also reports the API axis alone and bounded steps over retained readers.

using System.Diagnostics;
using System.Reflection.PortableExecutable;
using ILInspector.ILDiff;
using ILInspector.Metadata;
using Inspector.Findings;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: measure-finding-diff-any <before.dll> <after.dll> [iterations]");
    return 2;
}

string beforePath = args[0];
string afterPath = args[1];
int iterations = args.Length > 2 ? int.Parse(args[2]) : 20;
byte[] beforeBytes = File.ReadAllBytes(beforePath);
byte[] afterBytes = File.ReadAllBytes(afterPath);

FindingDiffAnyResult Run()
{
    using var before = new PEReader(new MemoryStream(beforeBytes, writable: false));
    using var after = new PEReader(new MemoryStream(afterBytes, writable: false));
    return FindingDiffAny.Compare(before, after);
}

FindingDiffAnyResult result = Run();
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

// The API axis alone, and the whole comparison advanced in bounded steps over
// retained readers, as a single-threaded host runs it.
double Median(Func<double> sample)
{
    sample();
    var values = Enumerable.Range(0, iterations).Select(_ => sample()).Order().ToList();
    return values[values.Count / 2];
}
double Timed(Action action)
{
    long start = Stopwatch.GetTimestamp();
    action();
    return Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
using (var before = new PEReader(new MemoryStream(beforeBytes, writable: false)))
using (var after = new PEReader(new MemoryStream(afterBytes, writable: false)))
{
    double apiOnly = Median(() => Timed(() => FindingDiffAny.Compare(before, after, FindingDiffAnyAxes.Api)));
    Console.WriteLine($"api-only\tmedian={apiOnly:F2}");
    foreach (double budgetMs in new[] { 0.5, 1.0 })
    {
        int steps = 0;
        double maxStep = 0;
        double stepped = Median(() =>
        {
            var comparison = new FindingDiffAnyComparison();
            steps = 0;
            maxStep = 0;
            double total = 0;
            bool complete = false;
            while (!complete)
            {
                double elapsed = Timed(() => complete = comparison.Step(
                    before, after, TimeSpan.FromMilliseconds(budgetMs)));
                total += elapsed;
                maxStep = Math.Max(maxStep, elapsed);
                steps++;
            }
            return total;
        });
        Console.WriteLine(
            $"stepped\tbudget={budgetMs:F1}ms\tsteps={steps}\tmedian={stepped:F2}\tmaxStep={maxStep:F2}");
    }
}

// Complete Public API comparison, signatures and attributes.
const ApiSurfaceExtractionScope surfaceScope = ApiSurfaceExtractionScope.Public;
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
    normalization: IlBodyDiffPolicy.Body).Diff;
var bodyChanged = new HashSet<string>(StringComparer.Ordinal);
foreach (IlAssemblyDiffExample example in il.Examples)
    bodyChanged.Add(DeclaredOwner(example.Method));

Dictionary<string, FindingDiffAnyTypeState> byName = [];
foreach (FindingDiffAnyTypeState state in result.Types)
    byName[state.FullName] = state;

int apiMissed = apiChanged.Count(name => byName.TryGetValue(name, out var s) && s.Api == FindingDiffAnyState.Unchanged);
int apiUnmatched = apiChanged.Count(name => !byName.ContainsKey(name));
int apiOverReported = result.Types.Count(s => s.Api == FindingDiffAnyState.Changed && !apiChanged.Contains(s.FullName));
int bodyMissed = bodyChanged.Count(name => byName.TryGetValue(name, out var s) && s.Body == FindingDiffAnyState.Unchanged);
int bodyUnmatched = bodyChanged.Count(name => !byName.ContainsKey(name));
// A Body Changed with no changed IL owner is decided by a declaration fact,
// which canonical IL comparison does not show; EXPLAIN=1 names the fact.
int bodyOverReported = result.Types.Count(s => s.Body == FindingDiffAnyState.Changed && !bodyChanged.Contains(s.FullName));

Console.WriteLine($"pair\t{Path.GetFileName(beforePath)}");
Console.WriteLine(
    $"types\t{result.Types.Length}\tapiChanged={result.Types.Count(s => s.Api == FindingDiffAnyState.Changed)}"
        + $"\tbodyChanged={result.Types.Count(s => s.Body == FindingDiffAnyState.Changed)}"
        + $"\tanyChanged={result.Types.Count(s => s.Api == FindingDiffAnyState.Changed || s.Body == FindingDiffAnyState.Changed)}"
        + $"\tindeterminate={result.Types.Count(s => s.Api == FindingDiffAnyState.Indeterminate || s.Body == FindingDiffAnyState.Indeterminate)}");
Console.WriteLine($"work\tbodies={result.Receipt.BodiesCompared}\tilBytes={result.Receipt.IlBytesCompared}");
Console.WriteLine(
    $"ms\tmedian={samples[samples.Count / 2]:F2}\tp95={samples[(int)(samples.Count * 0.95)]:F2}"
        + $"\tmin={samples[0]:F2}\tsamples={samples.Count}");
Console.WriteLine(
    $"api-oracle\tchanged={apiChanged.Count}\tmissed={apiMissed}\tunmatched={apiUnmatched}\tover-reported={apiOverReported}");
Console.WriteLine(
    $"body-oracle\tchangedOwners={bodyChanged.Count}\tmissed={bodyMissed}\tunmatched={bodyUnmatched}"
        + $"\tdeclarationOnly={bodyOverReported}\tilFailures={il.FailureCount}");
foreach (string name in apiChanged.Where(n => byName.TryGetValue(n, out var s) && s.Api == FindingDiffAnyState.Unchanged).Take(20))
    Console.WriteLine($"  api-missed\t{name}");
foreach (string name in bodyChanged.Where(n => byName.TryGetValue(n, out var s) && s.Body == FindingDiffAnyState.Unchanged).Take(20))
{
    Console.WriteLine($"  body-missed\t{name}");
    IlAssemblyDiffExample example = il.Examples.First(e => DeclaredOwner(e.Method) == name);
    Console.WriteLine($"    {example.Method}");
    foreach (IlDiffRow row in example.Diff.Rows.Take(3))
        Console.WriteLine($"    {row.Kind} {row.Message}");
}
if (Environment.GetEnvironmentVariable("LIST") == "1")
{
    foreach (FindingDiffAnyTypeState state in result.Types.Where(s => s.Api != FindingDiffAnyState.Unchanged || s.Body != FindingDiffAnyState.Unchanged))
        Console.WriteLine($"  changed\t{state.FullName}\tapi={state.Api}\tbody={state.Body}");
}
if (Environment.GetEnvironmentVariable("EXPLAIN") == "1")
{
    using var explainBefore = new PEReader(new MemoryStream(beforeBytes, writable: false));
    using var explainAfter = new PEReader(new MemoryStream(afterBytes, writable: false));
    foreach (FindingDiffAnyTypeState state in result.Types.Where(s => s.Api == FindingDiffAnyState.Changed && !apiChanged.Contains(s.FullName)).Take(40))
    {
        var (api, _) = FindingDiffAny.Explain(explainBefore, explainAfter, state.FullName);
        Console.WriteLine($"  api-over\t{state.FullName}\n{api}");
    }
    foreach (FindingDiffAnyTypeState state in result.Types.Where(s => s.Body == FindingDiffAnyState.Changed && !bodyChanged.Contains(s.FullName)).Take(40))
    {
        var (_, body) = FindingDiffAny.Explain(explainBefore, explainAfter, state.FullName);
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
