#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj

// Measures FindingDiffAny over one Library image pair and checks it against the
// complete API comparison and canonical IL comparison.
// Usage: measure-finding-diff-any <before.dll> <after.dll> [iterations]
// Also reports the API axis alone, bounded steps over retained readers, and
// every paired Type's inventory per Type through the walk and through lockstep.

using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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

// Allocation per whole comparison, and the memory a comparison retains while
// its sides are alive (the memoized keys and unit tables), for a producer
// change that moves key storage (docs/design/symbol-key.md#builder).
long allocatedMedian;
{
    var allocations = new List<long>(iterations);
    for (int i = 0; i < iterations; i++)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        Run();
        allocations.Add(GC.GetAllocatedBytesForCurrentThread() - before);
    }
    allocations.Sort();
    allocatedMedian = allocations[allocations.Count / 2];
}
long retainedBytes;
{
    using var before = new PEReader(new MemoryStream(beforeBytes, writable: false));
    using var after = new PEReader(new MemoryStream(afterBytes, writable: false));
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    long idle = GC.GetTotalMemory(forceFullCollection: true);
    var retained = new FindingDiffAnyComparison();
    while (!retained.Step(before, after, Timeout.InfiniteTimeSpan))
    {
    }
    long held = GC.GetTotalMemory(forceFullCollection: true);
    retainedBytes = held - idle;
    GC.KeepAlive(retained);
}
Console.WriteLine($"alloc\tmedianBytes={allocatedMedian}\tretainedBytes={retainedBytes}");

// The whole comparison with lockstep over two IL cursors in place of the walk
// for every paired method, through the Body provision seam
// (docs/design/finding-census.md#provisions, step 2b): the same pairing,
// censuses, and folds, so the two legs differ in the body reader alone. The
// cursor sides are created per run, as the walk's sides are.
FindingDiffAnyResult RunLockstep()
{
    using var before = new PEReader(new MemoryStream(beforeBytes, writable: false));
    using var after = new PEReader(new MemoryStream(afterBytes, writable: false));
    IlCensusSide beforeSide = IlCensusSide.Create(before.GetMetadataReader(), IlBodyDiffPolicy.Body);
    IlCensusSide afterSide = IlCensusSide.Create(after.GetMetadataReader(), IlBodyDiffPolicy.Body);
    return FindingDiffAny.Compare(
        before,
        after,
        (beforePe, beforeMethod, afterPe, afterMethod) =>
            IlFindings.Exists(beforeSide, beforePe, beforeMethod, afterSide, afterPe, afterMethod).Decision);
}
{
    FindingDiffAnyResult lockstepResult = RunLockstep();
    bool agree = lockstepResult.Types.SequenceEqual(result.Types);
    for (int i = 0; i < 3; i++)
        RunLockstep();
    var lockstepSamples = new List<double>(iterations);
    var lockstepAllocations = new List<long>(iterations);
    for (int i = 0; i < iterations; i++)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        RunLockstep();
        lockstepSamples.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        lockstepAllocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocated);
    }
    lockstepSamples.Sort();
    lockstepAllocations.Sort();
    int p95Index = Math.Max(0, (int)Math.Ceiling(lockstepSamples.Count * 0.95) - 1);
    Console.WriteLine(
        $"lockstep\tmedian={lockstepSamples[lockstepSamples.Count / 2]:F2}\tp95={lockstepSamples[p95Index]:F2}"
            + $"\tmin={lockstepSamples[0]:F2}\tmedianBytes={lockstepAllocations[lockstepAllocations.Count / 2]}\tagree={agree}");
    Console.WriteLine($"  samples-ms\tlockstep\t{string.Join(",", lockstepSamples.Select(t => t.ToString("F2")))}");
}

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

// Type scope: every paired Type's inventory, through the walk and through
// lockstep over two IL cursors, timed per Type with each pairing built once,
// as a host that opened the pair and asks Type by Type pays it
// (docs/design/finding-census.md, Exists, cost at Type scope).
using (var before = new PEReader(new MemoryStream(beforeBytes, writable: false)))
using (var after = new PEReader(new MemoryStream(afterBytes, writable: false)))
{
    var libraryStates = new Dictionary<string, FindingDiffAnyTypeState>(StringComparer.Ordinal);
    foreach (FindingDiffAnyTypeState state in result.Types)
        libraryStates.TryAdd(state.FullName, state);

    FindingDiffAnyTypePairing walkPairing = null!;
    double walkPairingMs = Timed(() => walkPairing = new FindingDiffAnyTypePairing(before, after));
    IlCensusSide cursorSideA = IlCensusSide.Create(before.GetMetadataReader(), IlBodyDiffPolicy.Body);
    IlCensusSide cursorSideB = IlCensusSide.Create(after.GetMetadataReader(), IlBodyDiffPolicy.Body);
    FindingDiffAnyBodyReader cursorReader = (beforePe, beforeMethod, afterPe, afterMethod) =>
        IlFindings.Exists(cursorSideA, beforePe, beforeMethod, cursorSideB, afterPe, afterMethod).Decision;
    FindingDiffAnyTypePairing lockstepPairing = null!;
    double lockstepPairingMs = Timed(() => lockstepPairing = new FindingDiffAnyTypePairing(before, after, cursorReader));

    IReadOnlyList<string> names = walkPairing.Types;
    int count = names.Count;
    int foldMismatch = 0;
    int rowMismatch = 0;
    var bodiesPerType = new int[count];
    var changedType = new bool[count];
    for (int i = 0; i < count; i++)
    {
        FindingDiffAnyTypeInventory w = walkPairing.Inventory(names[i]);
        FindingDiffAnyTypeInventory l = lockstepPairing.Inventory(names[i]);
        if (!libraryStates.TryGetValue(names[i], out FindingDiffAnyTypeState? state) || state.Api != w.Api || state.Body != w.Body)
            foldMismatch++;
        if (w.Api != l.Api || w.Body != l.Body || !w.Facts.SequenceEqual(l.Facts) || !w.Bodies.SequenceEqual(l.Bodies))
        {
            rowMismatch++;
            if (rowMismatch <= 12)
                ReportRowMismatch(before.GetMetadataReader(), names[i], w, l);
        }
        bodiesPerType[i] = w.Bodies.Length;
        changedType[i] = w.Api == FindingDiffAnyState.Changed || w.Body == FindingDiffAnyState.Changed;
    }
    Console.WriteLine(
        $"type-pairing\twalk-ms={walkPairingMs:F2}\tlockstep-ms={lockstepPairingMs:F2}\ttypes={count}"
            + $"\tfoldMismatch={foldMismatch}\trowMismatch={rowMismatch}");

    // Per-Type microseconds: the median over the passes of each Type's own time,
    // the two readers alternating per Type so neither owns a warmer cache.
    var walkUs = new double[count, iterations];
    var lockstepUs = new double[count, iterations];
    for (int pass = 0; pass < iterations; pass++)
    {
        for (int i = 0; i < count; i++)
        {
            string name = names[i];
            if ((pass + i) % 2 == 0)
            {
                walkUs[i, pass] = Timed(() => walkPairing.Inventory(name)) * 1000;
                lockstepUs[i, pass] = Timed(() => lockstepPairing.Inventory(name)) * 1000;
            }
            else
            {
                lockstepUs[i, pass] = Timed(() => lockstepPairing.Inventory(name)) * 1000;
                walkUs[i, pass] = Timed(() => walkPairing.Inventory(name)) * 1000;
            }
        }
    }
    var walkBytes = new long[count];
    var lockstepBytes = new long[count];
    for (int i = 0; i < count; i++)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        walkPairing.Inventory(names[i]);
        walkBytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
        allocated = GC.GetAllocatedBytesForCurrentThread();
        lockstepPairing.Inventory(names[i]);
        lockstepBytes[i] = GC.GetAllocatedBytesForCurrentThread() - allocated;
    }
    double[] walkMedian = PerTypeMedian(walkUs, count, iterations);
    double[] lockstepMedian = PerTypeMedian(lockstepUs, count, iterations);
    ReportTypeScope("walk", walkMedian, walkBytes);
    ReportTypeScope("lockstep", lockstepMedian, lockstepBytes);
    foreach (int i in Enumerable.Range(0, count).OrderByDescending(i => bodiesPerType[i]).Take(5))
        ReportType("type-largest", names[i], bodiesPerType[i], walkMedian[i], lockstepMedian[i], walkBytes[i], lockstepBytes[i]);
    foreach (int i in Enumerable.Range(0, count).Where(i => changedType[i]).OrderByDescending(i => bodiesPerType[i]).Take(5))
        ReportType("type-changed", names[i], bodiesPerType[i], walkMedian[i], lockstepMedian[i], walkBytes[i], lockstepBytes[i]);
    walkPairing.Release();
    lockstepPairing.Release();
}

// The first row on which the two readers disagree for one Type, by method
// name and both states, so a disagreement the Library fold hides is visible.
static void ReportRowMismatch(MetadataReader md, string type, FindingDiffAnyTypeInventory walk, FindingDiffAnyTypeInventory lockstep)
{
    if (walk.Api != lockstep.Api || walk.Body != lockstep.Body)
        Console.WriteLine($"  type-row-mismatch\t{type}\tfold\twalk={walk.Api}/{walk.Body}\tlockstep={lockstep.Api}/{lockstep.Body}");
    if (!walk.Facts.SequenceEqual(lockstep.Facts))
    {
        Console.WriteLine($"  type-row-mismatch\t{type}\tfacts\twalk={walk.Facts.Length}\tlockstep={lockstep.Facts.Length}");
        for (int i = 0; i < Math.Min(walk.Facts.Length, lockstep.Facts.Length); i++)
        {
            if (walk.Facts[i] == lockstep.Facts[i])
                continue;
            Console.WriteLine($"    walk    \t{walk.Facts[i].Axis}\t{walk.Facts[i].Side}\t{walk.Facts[i].Key}");
            Console.WriteLine($"    lockstep\t{lockstep.Facts[i].Axis}\t{lockstep.Facts[i].Side}\t{lockstep.Facts[i].Key}");
            break;
        }
    }
    if (walk.Bodies.Length != lockstep.Bodies.Length)
    {
        Console.WriteLine($"  type-row-mismatch\t{type}\tbodies\twalk={walk.Bodies.Length}\tlockstep={lockstep.Bodies.Length}");
        return;
    }
    for (int i = 0; i < walk.Bodies.Length; i++)
    {
        if (walk.Bodies[i] == lockstep.Bodies[i])
            continue;
        MethodDefinition method = md.GetMethodDefinition(walk.Bodies[i].Before);
        Console.WriteLine(
            $"  type-row-mismatch\t{type}\t{md.GetString(method.Name)}\twalk={walk.Bodies[i].Body}\tlockstep={lockstep.Bodies[i].Body}"
                + $"\tbeforeToken=0x{MetadataTokens.GetToken(walk.Bodies[i].Before):X8}\tafterToken=0x{MetadataTokens.GetToken(walk.Bodies[i].After):X8}");
        return;
    }
}

static double[] PerTypeMedian(double[,] samples, int count, int passes)
{
    var medians = new double[count];
    var row = new double[passes];
    for (int i = 0; i < count; i++)
    {
        for (int pass = 0; pass < passes; pass++)
            row[pass] = samples[i, pass];
        Array.Sort(row);
        medians[i] = row[passes / 2];
    }
    return medians;
}

static void ReportTypeScope(string reader, double[] medianUs, long[] bytes)
{
    double[] sorted = medianUs.Order().ToArray();
    long[] sortedBytes = bytes.Order().ToArray();
    int p95 = Math.Max(0, (int)Math.Ceiling(sorted.Length * 0.95) - 1);
    Console.WriteLine(
        $"type\treader={reader}\tsum-ms={sorted.Sum() / 1000:F2}\tmedian-us={sorted[sorted.Length / 2]:F1}"
            + $"\tp95-us={sorted[p95]:F1}\tmax-us={sorted[^1]:F1}\tsumBytes={sortedBytes.Sum()}"
            + $"\tmedianBytes={sortedBytes[sortedBytes.Length / 2]}\tmaxBytes={sortedBytes[^1]}");
}

static void ReportType(string label, string name, int bodies, double walkUs, double lockstepUs, long walkBytes, long lockstepBytes)
    => Console.WriteLine(
        $"  {label}\t{name}\tbodies={bodies}\twalk-us={walkUs:F1}\tlockstep-us={lockstepUs:F1}"
            + $"\twalkBytes={walkBytes}\tlockstepBytes={lockstepBytes}");

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
