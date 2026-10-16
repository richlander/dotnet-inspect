#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj

// SPIKE (exp/staged-diff-spike), follow-up. One equality predicate per axis,
// shared by an Exists mode and a Complete mode:
//
//   API predicate   Fast Diff's metadata census of the Type (signatures,
//                   flags, names, defaults, constants, attributes, nullable
//                   context, layout, interfaces, generics) decides Unchanged
//                   when equal. Where the census differs, the API surface
//                   model is built for those Types only and the complete API
//                   comparison's own checks decide (classifier checks and the
//                   Finding-lane facet details).
//   Body predicate  Canonical IL equality, lockstep with early exit, over an
//                   instructions-only decode with memoised operand identities.
//                   Methods pair by Fast Diff's memoised symbolic key; the
//                   tool proves that pairing equal to the canonical assembly
//                   diff's identity pairing. BODY_PLUS adds non-public
//                   metadata facts (Fast Diff's Body census) and body headers.
//
//   Exists    runs the predicates and stops each Type at its first false.
//   Complete  runs them over everything and explains only the false items:
//             MetadataFindings.CompareApi over partial surfaces holding just
//             the census-different Types, and IlBodyDiff.Compare (block graph,
//             LCS, hunks) on just the bodies whose predicate is false.
//
// Usage: measure-shared-predicate <before.dll> <after.dll> [iterations] [topTypes] [extraType...]

using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.ILDiff;
using ILInspector.Metadata;
using Inspector.Findings;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: measure-shared-predicate <before.dll> <after.dll> [iterations] [topTypes] [extraType...]");
    return 2;
}

string beforePath = args[0];
string afterPath = args[1];
int iterations = args.Length > 2 ? int.Parse(args[2]) : 9;
int topTypes = args.Length > 3 ? int.Parse(args[3]) : 5;
string[] extraTypes = args.Length > 4 ? args[4..] : [];
byte[] beforeBytes = File.ReadAllBytes(beforePath);
byte[] afterBytes = File.ReadAllBytes(afterPath);
var options = new ApiDiffOptions(ApiDiffScope.All);
int few = Math.Max(3, iterations / 3);

PEReader Open(byte[] bytes) => new(new MemoryStream(bytes, writable: false));
static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
double Median(int n, Func<double> sample)
{
    sample();
    var values = Enumerable.Range(0, n).Select(_ => sample()).Order().ToList();
    return values[values.Count / 2];
}
static double MedianOf(IEnumerable<double> v) { var l = v.Order().ToList(); return l[l.Count / 2]; }

Console.WriteLine($"pair\t{Path.GetFileName(beforePath)}\t{beforePath}\n\t{afterPath}");

// ---------------------------------------------------------------- Fast Diff
FastDiffResult fast = default!;
double fastMs = Median(iterations, () =>
{
    using var b = Open(beforeBytes);
    using var a = Open(afterBytes);
    long t = Stopwatch.GetTimestamp();
    fast = FastDiff.Compare(b, a);
    return Ms(t);
});
var fastByName = new Dictionary<string, FastDiffTypeState>(StringComparer.Ordinal);
foreach (var s in fast.Types)
    fastByName.TryAdd(s.FullName, s);

// ---------------------------------------------------------------- Exists (Library)
Run exists = default!, existsPlus = default!, complete = default!;
var existsTimes = new List<Times>();
double existsMs = Median(iterations, () =>
{
    using var b = Open(beforeBytes);
    using var a = Open(afterBytes);
    long t = Stopwatch.GetTimestamp();
    exists = Shared.Library(b, a, options, Mode.Exists, bodyPlus: false);
    double ms = Ms(t);
    existsTimes.Add(exists.Times);
    return ms;
});
var existsPlusTimes = new List<Times>();
double existsPlusMs = Median(iterations, () =>
{
    using var b = Open(beforeBytes);
    using var a = Open(afterBytes);
    long t = Stopwatch.GetTimestamp();
    existsPlus = Shared.Library(b, a, options, Mode.Exists, bodyPlus: true);
    double ms = Ms(t);
    existsPlusTimes.Add(existsPlus.Times);
    return ms;
});

// ---------------------------------------------------------------- Complete via predicate + explain
var completeTimes = new List<Times>();
double completeMs = Median(few, () =>
{
    using var b = Open(beforeBytes);
    using var a = Open(afterBytes);
    long t = Stopwatch.GetTimestamp();
    complete = Shared.Library(b, a, options, Mode.Complete, bodyPlus: false);
    double ms = Ms(t);
    completeTimes.Add(complete.Times);
    return ms;
});

// ---------------------------------------------------------------- Today's complete diffs
ApiFindingComparison todayApi = default!;
double todayApiMs = Median(few, () =>
{
    long t = Stopwatch.GetTimestamp();
    ApiSurface Surface(byte[] bytes)
    {
        using var r = Open(bytes);
        return ApiSurfaceExtractor.Extract(r, ApiSurfaceExtractionScope.Public);
    }
    todayApi = MetadataFindings.CompareApi(Surface(beforeBytes), Surface(afterBytes), new FindingSubject("api", "API"), options);
    return Ms(t);
});
IlAssemblyDiffResult todayIl = default!;
double todayIlMs = Median(few, () =>
{
    long t = Stopwatch.GetTimestamp();
    todayIl = IlAssemblyDiff.CompareStreams(
        new MemoryStream(beforeBytes, writable: false), beforePath,
        new MemoryStream(afterBytes, writable: false), afterPath,
        maxExamples: int.MaxValue, normalization: Shared.Normalization).Diff;
    return Ms(t);
});

static Times Med(List<Times> list) => new()
{
    Units = MedianOf(list.Select(x => x.Units)),
    ApiCensus = MedianOf(list.Select(x => x.ApiCensus)),
    ApiModel = MedianOf(list.Select(x => x.ApiModel)),
    BodyKeys = MedianOf(list.Select(x => x.BodyKeys)),
    BodyIl = MedianOf(list.Select(x => x.BodyIl)),
    BodyPlus = MedianOf(list.Select(x => x.BodyPlus)),
    ExplainApi = MedianOf(list.Select(x => x.ExplainApi)),
    ExplainIl = MedianOf(list.Select(x => x.ExplainIl)),
    Residual = MedianOf(list.Select(x => x.Residual)),
};
void PrintTimes(string label, double total, Times t)
    => Console.WriteLine(
        $"ms\t{label}={total:F2}\tunits={t.Units:F2}\tapiCensus={t.ApiCensus:F2}\tapiModel={t.ApiModel:F2}"
            + $"\tbodyKeys={t.BodyKeys:F2}\tbodyIl={t.BodyIl:F2}\tbodyPlus={t.BodyPlus:F2}"
            + $"\texplainApi={t.ExplainApi:F2}\texplainIl={t.ExplainIl:F2}\tresidual={t.Residual:F2}");

Console.WriteLine($"ms\tfast(ApiAndBody)={fastMs:F2}");
PrintTimes("exists", existsMs, Med(existsTimes));
PrintTimes("exists+bodyPlus", existsPlusMs, Med(existsPlusTimes));
PrintTimes("complete(predicate+explain)", completeMs, Med(completeTimes));
Console.WriteLine($"ms\ttoday-complete-api={todayApiMs:F2}\ttoday-complete-il={todayIlMs:F2}\ttoday-both={todayApiMs + todayIlMs:F2}");
Console.WriteLine(
    $"work\ttypes={exists.Types.Count}\tapiCensusDiffer={exists.ApiTierB}\tapiModelTypesExtracted={exists.ApiExtracted}"
        + $"\tbodiesCompared(exists)={exists.Bodies}\tbodiesCompared(complete)={complete.Bodies}\tbodiesExplained={complete.Explained}");

// ---------------------------------------------------------------- API oracle (today's complete)
var apiOracle = new HashSet<string>(StringComparer.Ordinal);
var memberOracle = new HashSet<string>(StringComparer.Ordinal);
var presentTypes = new HashSet<string>(StringComparer.Ordinal);
foreach (TypeDiff type in todayApi.ApiDiff.TypeDiffs)
    apiOracle.Add(type.TypeFullName);
if (todayApi.Types.Value is FindingComparison<ApiTypeHandle>.Complete oracleTypes)
    foreach (var pair in oracleTypes.Pairs)
    {
        if (pair.Kind != PairKind.Present)
            apiOracle.Add(Payload<ApiTypeHandle>(pair).TypeFullName);
        if (pair.Kind is PairKind.Present or PairKind.Changed)
            presentTypes.Add(Payload<ApiTypeHandle>(pair).TypeFullName);
    }
if (todayApi.Members.Value is FindingComparison<ApiMemberHandle>.Complete oracleMembers)
    foreach (var pair in oracleMembers.Pairs)
    {
        if (pair.Kind == PairKind.Present)
            continue;
        apiOracle.Add(Payload<ApiMemberHandle>(pair).TypeFullName);
        foreach (var f in new[] { ((IPairFinding)pair).Old, ((IPairFinding)pair).New })
            if (f is Finding<ApiMemberHandle> m && presentTypes.Contains(m.Payload.TypeFullName))
                memberOracle.Add(m.Payload.TypeFullName + "|" + Shared.MemberKey(m.Payload));
    }
var bodyOracle = new HashSet<string>(StringComparer.Ordinal);
foreach (var example in todayIl.Examples)
    bodyOracle.Add(DeclaredOwner(example.Method));

// ---------------------------------------------------------------- Agreement
void Agreement(string axis, Run run, Func<TypeVerdict, FastDiffState> pick, Func<FastDiffTypeState, FastDiffState> fpick, HashSet<string> oracle)
{
    var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var lines = new List<string>();
    foreach (var (name, v) in run.Types)
    {
        fastByName.TryGetValue(name, out var f);
        string key = $"pred={S(pick(v))} fast={(f is null ? "absent" : S(fpick(f)))} oracle={(oracle.Contains(name) ? "C" : "-")}";
        counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
        bool predChanged = pick(v) == FastDiffState.Changed;
        if (predChanged != oracle.Contains(name) || (f is not null && pick(v) != fpick(f)))
            lines.Add($"  {axis}-diff\t{name}\t{key}\t{(axis.StartsWith("api") ? v.ApiReason : v.BodyReason)}");
    }
    foreach (var (k, c) in counts)
        Console.WriteLine($"agree-{axis}\t{k}\t{c}");
    int misses = run.Types.Count(kv => oracle.Contains(kv.Key) && pick(kv.Value) == FastDiffState.Unchanged);
    int over = run.Types.Count(kv => !oracle.Contains(kv.Key) && pick(kv.Value) == FastDiffState.Changed);
    int unmatched = oracle.Count(n => !run.Types.ContainsKey(n));
    Console.WriteLine($"gate-{axis}\tmisses={misses}\tover={over}\toracleUnmatched={unmatched}");
    int limit = Environment.GetEnvironmentVariable("ALL") == "1" ? int.MaxValue : 12;
    foreach (var l in lines.Where(l => !l.Contains("fast=C oracle=-") || l.Contains("pred=C")).Take(limit))
        Console.WriteLine(l);
    foreach (var n in oracle.Where(n => !run.Types.ContainsKey(n)).Take(5))
        Console.WriteLine($"  {axis}-oracle-unmatched\t{n}");
}
static string S(FastDiffState s) => s switch
{
    FastDiffState.Changed => "C",
    FastDiffState.Unchanged => "U",
    FastDiffState.Indeterminate => "I",
    _ => s.ToString(),
};
Agreement("api", exists, v => v.Api, f => f.Api, apiOracle);
Agreement("body", exists, v => v.Body, f => f.Body, bodyOracle);
Agreement("body+", existsPlus, v => v.Body, f => f.Body, bodyOracle);

// Per-Member API gate: predicate (census-equal => every Member Unchanged; else model, every Member) vs today's pairs.
{
    var mine = complete.ChangedMembers;
    int misses = memberOracle.Count(m => !mine.Contains(m));
    int over = mine.Count(m => !memberOracle.Contains(m));
    Console.WriteLine($"gate-member\toracle={memberOracle.Count}\tpredicate={mine.Count}\tmisses={misses}\tover={over}");
    foreach (var m in memberOracle.Where(m => !mine.Contains(m)).Take(8))
        Console.WriteLine($"  member-miss\t{m}");
    foreach (var m in mine.Where(m => !memberOracle.Contains(m)).Take(8))
        Console.WriteLine($"  member-over\t{m}");
}

// Pairing proof: Fast Diff symbolic-key pairing vs the canonical assembly diff's identity pairing.
{
    using var b = Open(beforeBytes);
    using var a = Open(afterBytes);
    var (same, onlyMine, onlyOracle, examples) = Shared.ComparePairing(b, a);
    Console.WriteLine($"pairing\tsame={same}\tonlyKeyPairing={onlyMine}\tonlyOraclePairing={onlyOracle}");
    foreach (var e in examples.Take(6))
        Console.WriteLine($"  pairing-diff\t{e}");
}

// Complete output equality.
{
    var todayLines = Shared.ApiLines(todayApi);
    var mineLines = Shared.ApiLines(complete.Api!);
    var (onlyToday, onlyMine) = MultisetDiff(todayLines, mineLines);
    Console.WriteLine($"complete-api\ttodayLines={todayLines.Count}\tpredicateLines={mineLines.Count}\tonlyToday={onlyToday.Count}\tonlyPredicate={onlyMine.Count}");
    foreach (var l in onlyToday.Take(8))
        Console.WriteLine($"  api-only-today\t{Trunc(l)}");
    foreach (var l in onlyMine.Take(8))
        Console.WriteLine($"  api-only-predicate\t{Trunc(l)}");

    var todayIlLines = todayIl.Examples.Select(e => e.Method + "\n" + string.Join("\n", e.Diff.Rows.Select(r => r.Message))).ToList();
    var mineIlLines = complete.IlExamples.Select(e => e.Method + "\n" + string.Join("\n", e.Rows)).ToList();
    var (ilOnlyToday, ilOnlyMine) = MultisetDiff(todayIlLines, mineIlLines);
    Console.WriteLine(
        $"complete-il\ttodayChanged={todayIl.ChangedBodyCount}\tpredicateChanged={complete.IlExamples.Count}"
            + $"\ttodayUnavailable={todayIl.PairUnavailableCount}\tpredicateUnavailable={complete.Unavailable}"
            + $"\tonlyToday={ilOnlyToday.Count}\tonlyPredicate={ilOnlyMine.Count}\tpredicateFalseButExact={complete.FalseButExact}");
    foreach (var l in ilOnlyToday.Take(5))
        Console.WriteLine($"  il-only-today\t{Trunc(l.Replace('\n', '|'))}");
    foreach (var l in ilOnlyMine.Take(5))
        Console.WriteLine($"  il-only-predicate\t{Trunc(l.Replace('\n', '|'))}");
    foreach (var bucket in todayIl.FailureBuckets)
        Console.WriteLine($"  today-il-failure\t{bucket.Count}\t{bucket.Name}");
}

// ---------------------------------------------------------------- Exists per Type
using (var pb = Open(beforeBytes))
using (var pa = Open(afterBytes))
{
    var names = exists.Types
        .Where(kv => kv.Value.Present && (kv.Value.Api == FastDiffState.Changed || kv.Value.Body == FastDiffState.Changed))
        .OrderByDescending(kv => kv.Value.Methods)
        .Take(topTypes)
        .Select(kv => kv.Key)
        .Concat(extraTypes.Where(exists.Types.ContainsKey))
        .Distinct()
        .ToList();
    foreach (string name in names)
    {
        TypeRun r = default!;
        var unitsMs = new List<double>();
        double ms = Median(iterations, () =>
        {
            long t = Stopwatch.GetTimestamp();
            r = Shared.Type(pb, pa, name, options);
            double total = Ms(t);
            unitsMs.Add(r.UnitsMs);
            return total;
        });
        Console.WriteLine(
            $"type\t{name}\tmethods={r.Methods}\tms={ms:F2}\tof which units(whole-image type table)={MedianOf(unitsMs):F2}"
                + $"\tapi={r.Api}{(r.ModelBuilt ? " (model built)" : "")}\tbody={r.Body}\tbodiesCompared={r.Bodies}");
    }
}
return 0;

static string Trunc(string s) => s.Length > 260 ? s[..260] + "…" : s;

static (List<string> OnlyA, List<string> OnlyB) MultisetDiff(List<string> a, List<string> b)
{
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var x in a) counts[x] = counts.TryGetValue(x, out int c) ? c + 1 : 1;
    var onlyB = new List<string>();
    foreach (var x in b)
    {
        if (counts.TryGetValue(x, out int c) && c > 0) counts[x] = c - 1;
        else onlyB.Add(x);
    }
    var onlyA = counts.Where(kv => kv.Value > 0).SelectMany(kv => Enumerable.Repeat(kv.Key, kv.Value)).ToList();
    return (onlyA, onlyB);
}

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

enum Mode { Exists, Complete }

sealed class Times
{
    public double Units, ApiCensus, ApiModel, BodyKeys, BodyIl, BodyPlus, ExplainApi, ExplainIl, Residual;
}

sealed class TypeVerdict
{
    public bool Present;
    public int Methods;
    public FastDiffState Api = FastDiffState.Unchanged;
    public FastDiffState Body = FastDiffState.Unchanged;
    public string? ApiReason;
    public string? BodyReason;
}

sealed class Run
{
    public Dictionary<string, TypeVerdict> Types { get; } = new(StringComparer.Ordinal);
    public Times Times { get; } = new();
    public int ApiTierB, ApiExtracted, Bodies, Explained, Unavailable, FalseButExact;
    public HashSet<string> ChangedMembers { get; } = new(StringComparer.Ordinal);
    public ApiFindingComparison? Api;
    public List<(string Method, List<string> Rows)> IlExamples { get; } = [];
}

sealed class TypeRun
{
    public double UnitsMs;
    public int Methods, Bodies;
    public bool ModelBuilt;
    public FastDiffState Api, Body;
}

static class Shared
{
    public const IlBodyDiffNormalization Normalization =
        IlBodyDiffNormalization.NormalizeCurrentAssemblyScope | IlBodyDiffNormalization.NormalizePlatformAssemblyScope;

    static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    public static string MemberKey(ApiMemberHandle h) => h.CanonicalSignature ?? h.Identity;

    /// <summary>The ordered Type units of both sides, joined by Fast Diff's symbolic Type key.</summary>
    static List<(SymbolKey Key, FastDiff.Unit? A, FastDiff.Unit? B)> Join(
        OrderedDictionary<SymbolKey, FastDiff.Unit> a,
        OrderedDictionary<SymbolKey, FastDiff.Unit> b)
    {
        var list = new List<(SymbolKey, FastDiff.Unit?, FastDiff.Unit?)>(a.Count + 8);
        foreach (var (k, u) in a)
            list.Add((k, u, b.TryGetValue(k, out var v) ? v : null));
        foreach (var (k, u) in b)
            if (!a.ContainsKey(k))
                list.Add((k, null, u));
        return list;
    }

    public static Run Library(PEReader before, PEReader after, ApiDiffOptions options, Mode mode, bool bodyPlus)
    {
        var run = new Run();
        var times = run.Times;
        long t = Stopwatch.GetTimestamp();
        var a = new FastDiff.Side(before);
        var b = new FastDiff.Side(after);
        var unitsA = a.Units();
        var unitsB = b.Units();
        var joined = Join(unitsA, unitsB);
        var il = new IlBodyExistsComparer(a.Md, b.Md, Normalization);
        times.Units += Ms(t);

        // ---- API census: Unchanged where equal; collect the rest for the model.
        t = Stopwatch.GetTimestamp();
        var tierB = new List<(string Name, FastDiff.Unit? A, FastDiff.Unit? B)>();
        foreach (var (_, ua, ub) in joined)
        {
            var u = (ua ?? ub)!;
            var v = new TypeVerdict { Present = ua is not null && ub is not null };
            run.Types[u.FullName] = v;
            if (ua?.Malformed == true || ub?.Malformed == true)
            {
                v.Api = v.Body = FastDiffState.Indeterminate;
                continue;
            }
            if (ua is null || ub is null)
            {
                bool visible = ua is not null ? a.IsVisible(ua.Owner) : b.IsVisible(ub!.Owner);
                if (visible)
                    tierB.Add((u.FullName, ua, ub));
                continue;
            }
            try
            {
                if (!a.ApiCensus(ua).SequenceEqual(b.ApiCensus(ub)))
                    tierB.Add((u.FullName, ua, ub));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                a.Keys.Reset();
                b.Keys.Reset();
                v.Api = FastDiffState.Indeterminate;
                v.ApiReason = "census: " + ex.Message;
            }
        }
        run.ApiTierB = tierB.Count;
        times.ApiCensus += Ms(t);

        // ---- API model for census-different Types only.
        t = Stopwatch.GetTimestamp();
        var handlesA = tierB.Where(x => x.A is not null).Select(x => x.A!.Owner).ToHashSet();
        var handlesB = tierB.Where(x => x.B is not null).Select(x => x.B!.Owner).ToHashSet();
        ApiSurface surfaceA = handlesA.Count == 0 ? new ApiSurface()
            : ApiSurfaceExtractor.ExtractDeclarations(before, ApiSurfaceExtractionScope.Public, h => handlesA.Contains(h));
        ApiSurface surfaceB = handlesB.Count == 0 ? new ApiSurface()
            : ApiSurfaceExtractor.ExtractDeclarations(after, ApiSurfaceExtractionScope.Public, h => handlesB.Contains(h));
        run.ApiExtracted = handlesA.Count + handlesB.Count;
        var byTokenA = surfaceA.Types.Where(x => x.MetadataToken is not null).GroupBy(x => x.MetadataToken!.Value).ToDictionary(g => g.Key, g => g.First());
        var byTokenB = surfaceB.Types.Where(x => x.MetadataToken is not null).GroupBy(x => x.MetadataToken!.Value).ToDictionary(g => g.Key, g => g.First());
        foreach (var (name, ua, ub) in tierB)
        {
            var v = run.Types[name];
            ApiType? ta = ua is not null && byTokenA.TryGetValue(MetadataTokens.GetToken(ua.Owner), out var x1) ? x1 : null;
            ApiType? tb = ub is not null && byTokenB.TryGetValue(MetadataTokens.GetToken(ub.Owner), out var x2) ? x2 : null;
            if (ta is null && tb is null)
                continue;
            if (ta is null || tb is null)
            {
                v.Api = FastDiffState.Changed;
                v.ApiReason = "public on one side only";
                if (ua is not null && ub is not null)
                    foreach (var m in (ta ?? tb)!.Members.Where(IsDeclared))
                        run.ChangedMembers.Add(name + "|" + MemberKey(ApiMemberIdentity.CreateHandle((ta ?? tb)!, m)));
                continue;
            }
            var reason = ModelCompare(ta, tb, options, stopAtFirst: mode == Mode.Exists, out var changedMembers);
            if (reason is not null)
            {
                v.Api = FastDiffState.Changed;
                v.ApiReason = reason;
            }
            foreach (var m in changedMembers)
                run.ChangedMembers.Add(name + "|" + m);
        }
        times.ApiModel += Ms(t);

        if (mode == Mode.Complete)
        {
            t = Stopwatch.GetTimestamp();
            run.Api = MetadataFindings.CompareApi(surfaceA, surfaceB, new FindingSubject("api", "API"), options);
            times.ExplainApi += Ms(t);
        }

        // ---- Body.
        var covered = new HashSet<TypeDefinitionHandle>();
        var coveredB = new HashSet<TypeDefinitionHandle>();
        foreach (var (_, ua, ub) in joined)
        {
            var u = (ua ?? ub)!;
            var v = run.Types[u.FullName];
            if (ua is not null) { covered.Add(ua.Owner); foreach (var g in ua.Generated) covered.Add(g); }
            if (ub is not null) { coveredB.Add(ub.Owner); foreach (var g in ub.Generated) coveredB.Add(g); }
            if (v.Body == FastDiffState.Indeterminate)
                continue;
            if (ua is null || ub is null)
            {
                v.Body = FastDiffState.Changed;
                v.BodyReason = "presence";
                if (mode == Mode.Complete)
                    CountUnpaired(run, a, b, Rows(ua), Rows(ub));
                continue;
            }
            CompareBodies(run, a, b, ua, ub, Rows(ua), Rows(ub), v, il, mode, bodyPlus, times);
        }

        if (mode == Mode.Complete)
        {
            // Methods outside every unit (<Module>, top-level generated Types): compared, never reported as a Type.
            long tr = Stopwatch.GetTimestamp();
            var restA = a.Md.TypeDefinitions.Where(h => !covered.Contains(h)).ToList();
            var restB = b.Md.TypeDefinitions.Where(h => !coveredB.Contains(h)).ToList();
            var dummy = new TypeVerdict();
            CompareBodies(run, a, b, null, null, restA, restB, dummy, il, mode, bodyPlus: false, new Times());
            times.Residual += Ms(tr);
        }
        return run;
    }

    static List<TypeDefinitionHandle> Rows(FastDiff.Unit? u)
        => u is null ? [] : [u.Owner, .. u.Generated];

    static Dictionary<SymbolKey, MethodDefinitionHandle> MethodKeys(FastDiff.Side side, List<TypeDefinitionHandle> rows, out bool duplicate)
    {
        duplicate = false;
        var map = new Dictionary<SymbolKey, MethodDefinitionHandle>();
        foreach (var row in rows)
            foreach (var m in side.Md.GetTypeDefinition(row).GetMethods())
                if (!map.TryAdd(side.Key(m), m))
                    duplicate = true;
        return map;
    }

    static void CountUnpaired(Run run, FastDiff.Side a, FastDiff.Side b, List<TypeDefinitionHandle> rowsA, List<TypeDefinitionHandle> rowsB)
    {
        foreach (var row in rowsA)
            run.Unavailable += a.Md.GetTypeDefinition(row).GetMethods().Count;
        foreach (var row in rowsB)
            run.Unavailable += b.Md.GetTypeDefinition(row).GetMethods().Count;
    }

    static bool IsVisibleMethod(FastDiff.Side side, FastDiff.Unit? unit, MethodDefinitionHandle handle)
    {
        if (unit is null)
            return false;
        var method = side.Md.GetMethodDefinition(handle);
        if (method.GetDeclaringType() != unit.Owner || !side.IsVisible(unit.Owner))
            return false;
        var access = method.Attributes & MethodAttributes.MemberAccessMask;
        return access is MethodAttributes.Public or MethodAttributes.Family or MethodAttributes.FamORAssem;
    }

    static void CompareBodies(
        Run run, FastDiff.Side a, FastDiff.Side b, FastDiff.Unit? ua, FastDiff.Unit? ub,
        List<TypeDefinitionHandle> rowsA, List<TypeDefinitionHandle> rowsB,
        TypeVerdict v, IlBodyExistsComparer il, Mode mode, bool bodyPlus, Times times)
    {
        long t = Stopwatch.GetTimestamp();
        Dictionary<SymbolKey, MethodDefinitionHandle> ma, mb;
        bool dupA, dupB;
        try
        {
            ma = MethodKeys(a, rowsA, out dupA);
            mb = MethodKeys(b, rowsB, out dupB);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            a.Keys.Reset();
            b.Keys.Reset();
            v.Body = FastDiffState.Indeterminate;
            v.BodyReason = "keys: " + ex.Message;
            times.BodyKeys += Ms(t);
            return;
        }
        v.Methods = Math.Max(ma.Count, mb.Count);
        if (dupA || dupB)
        {
            v.Body = FastDiffState.Indeterminate;
            v.BodyReason = "duplicate method key";
        }
        times.BodyKeys += Ms(t);

        if (bodyPlus && ua is not null && ub is not null && v.Body == FastDiffState.Unchanged)
        {
            t = Stopwatch.GetTimestamp();
            try
            {
                bool visible = a.IsVisible(ua.Owner) || b.IsVisible(ub.Owner);
                if (!a.BodyCensus(ua, visible, out _).SequenceEqual(b.BodyCensus(ub, visible, out _)))
                {
                    v.Body = FastDiffState.Changed;
                    v.BodyReason = "non-public metadata fact";
                }
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                a.Keys.Reset();
                b.Keys.Reset();
                v.Body = FastDiffState.Indeterminate;
            }
            times.BodyPlus += Ms(t);
            if (v.Body != FastDiffState.Unchanged && mode == Mode.Exists)
                return;
        }

        t = Stopwatch.GetTimestamp();
        bool complete = mode == Mode.Complete;
        foreach (var (key, ha) in ma)
        {
            if (!mb.TryGetValue(key, out var hb))
            {
                if (complete)
                    run.Unavailable++;
                if (!IsVisibleMethod(a, ua, ha) && v.Body == FastDiffState.Unchanged)
                {
                    v.Body = FastDiffState.Changed;
                    v.BodyReason = "non-public method on one side";
                    if (!complete)
                        break;
                }
                continue;
            }
            var da = a.Md.GetMethodDefinition(ha);
            var db = b.Md.GetMethodDefinition(hb);
            if (da.RelativeVirtualAddress == 0 && db.RelativeVirtualAddress == 0)
                continue;
            if (da.RelativeVirtualAddress == 0 || db.RelativeVirtualAddress == 0)
            {
                if (complete)
                    run.Unavailable++;
                if (v.Body == FastDiffState.Unchanged)
                {
                    v.Body = FastDiffState.Changed;
                    v.BodyReason = "body on one side";
                    if (!complete)
                        break;
                }
                continue;
            }
            var ba = a.Pe.GetMethodBody(da.RelativeVirtualAddress);
            var bb = b.Pe.GetMethodBody(db.RelativeVirtualAddress);
            run.Bodies++;
            if (bodyPlus && !HeaderEqual(a, ba, b, bb) && v.Body == FastDiffState.Unchanged)
            {
                v.Body = FastDiffState.Changed;
                v.BodyReason = "body header (maxstack/locals/EH)";
                if (!complete)
                    break;
            }
            var outcome = il.Compare(ba, bb, rawFastPath: false, memoTokens: true, liteDecode: true, out _, out string? failure);
            if (outcome == IlExistsOutcome.Exact)
                continue;
            if (complete)
            {
                long te = Stopwatch.GetTimestamp();
                var diff = IlBodyDiff.Compare(a.Md, ba, b.Md, bb, Normalization);
                if (!diff.IsAvailable)
                    run.Unavailable++;
                else if (diff.IsExact)
                    run.FalseButExact++;
                else
                {
                    run.Explained++;
                    string id = IlAssemblyDiff.SpikeMethodIdentity(a.Md, ha) ?? "?";
                    run.IlExamples.Add((id, diff.Rows.Select(r => r.Message).ToList()));
                }
                times.ExplainIl += Ms(te);
            }
            if (v.Body == FastDiffState.Unchanged)
            {
                v.Body = outcome == IlExistsOutcome.Changed ? FastDiffState.Changed : FastDiffState.Indeterminate;
                v.BodyReason = outcome == IlExistsOutcome.Changed ? "IL" : "IL unavailable: " + failure;
                if (!complete && outcome == IlExistsOutcome.Changed)
                    break;
            }
        }
        if (complete || v.Body == FastDiffState.Unchanged)
        {
            foreach (var key in mb.Keys)
            {
                if (ma.ContainsKey(key))
                    continue;
                if (complete)
                    run.Unavailable++;
                if (!IsVisibleMethod(b, ub, mb[key]) && v.Body == FastDiffState.Unchanged)
                {
                    v.Body = FastDiffState.Changed;
                    v.BodyReason = "non-public method on one side";
                    if (!complete)
                        break;
                }
            }
        }
        times.BodyIl += Ms(t);
    }

    /// <summary>Body headers without encoding: maxstack, init locals, local types, EH kinds and catch types.</summary>
    static bool HeaderEqual(FastDiff.Side a, MethodBodyBlock x, FastDiff.Side b, MethodBodyBlock y)
    {
        if (x.MaxStack != y.MaxStack || x.LocalVariablesInitialized != y.LocalVariablesInitialized
            || x.LocalSignature.IsNil != y.LocalSignature.IsNil
            || (!x.LocalSignature.IsNil && a.Key(x.LocalSignature) != b.Key(y.LocalSignature))
            || x.ExceptionRegions.Length != y.ExceptionRegions.Length)
            return false;
        for (int i = 0; i < x.ExceptionRegions.Length; i++)
        {
            var p = x.ExceptionRegions[i];
            var q = y.ExceptionRegions[i];
            if (p.Kind != q.Kind || p.CatchType.IsNil != q.CatchType.IsNil
                || (!p.CatchType.IsNil && a.Key(p.CatchType) != b.Key(q.CatchType)))
                return false;
        }
        return true;
    }

    static bool IsDeclared(ApiMember m)
        => m.DeclaringTypeCanonicalName is null && m.DeclaringTypeDefinitionName is null && m.DeclaringOverloadIndex is null;

    static Dictionary<string, ApiMemberHandle> Keyed(ApiType type)
    {
        var map = new Dictionary<string, ApiMemberHandle>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var member in type.Members)
        {
            if (!IsDeclared(member))
                continue;
            var h = ApiMemberIdentity.CreateHandle(type, member);
            string k = MemberKey(h);
            int n = occurrences.TryGetValue(k, out int c) ? c + 1 : 1;
            occurrences[k] = n;
            map[n == 1 ? k : $"{k}#{n}"] = h;
        }
        return map;
    }

    /// <summary>
    /// The complete API comparison's checks on one Type pair: classifier checks
    /// (ApiFindingClassifier) and Finding-lane facet details (MetadataFindings).
    /// Returns the first difference, or null; lists every changed Member key
    /// unless <paramref name="stopAtFirst"/>.
    /// </summary>
    static string? ModelCompare(ApiType bt, ApiType at, ApiDiffOptions options, bool stopAtFirst, out List<string> changedMembers)
    {
        changedMembers = [];
        string? first = null;
        var facets = ApiDiffAnalyzer.CompareTypeFacetsOnly(bt, at, options);
        if (facets.Count > 0)
            first = facets[0].Message;
        if (first is null)
        {
            var details = new List<string>();
            MetadataFindings.AddTypeFacetDetails(bt, at, options.Scope, details);
            if (details.Count > 0)
                first = details[0];
        }
        if (first is not null && stopAtFirst)
            return first;

        var old = Keyed(bt);
        var @new = Keyed(at);
        foreach (var (k, nh) in @new)
        {
            string? reason = null;
            if (!old.TryGetValue(k, out var oh))
                reason = "member added " + k;
            else
            {
                var changes = new List<ApiChange>();
                if (!string.Equals(oh.Member.Signature, nh.Member.Signature, StringComparison.Ordinal))
                    reason = $"signature {oh.Member.Signature} -> {nh.Member.Signature}";
                else
                {
                    ApiDiffAnalyzer.CompareMemberModifiers(bt, at, oh.Member, nh.Member, changes);
                    ApiDiffAnalyzer.CompareMemberTypeParameterConstraints(bt, at, oh.Member, nh.Member, changes);
                    ApiDiffAnalyzer.CompareAttributes(
                        oh.Member.Attributes, nh.Member.Attributes, changes,
                        ChangeKind.MemberAttributeAdded, ChangeKind.MemberAttributeRemoved,
                        $"Member '{oh.MemberName}'", ApiChangeSubject.Member(bt, oh.Member, at, nh.Member));
                    if (changes.Count > 0)
                        reason = changes[0].Message;
                    else
                    {
                        var details = new List<string>();
                        MetadataFindings.AddMemberFacetDetails(oh.Member, nh.Member, options.Scope, details);
                        if (details.Count > 0)
                            reason = details[0];
                    }
                }
            }
            if (reason is null)
                continue;
            changedMembers.Add(StripOccurrence(k));
            first ??= reason;
            if (stopAtFirst)
                return first;
        }
        foreach (var k in old.Keys)
        {
            if (@new.ContainsKey(k))
                continue;
            changedMembers.Add(StripOccurrence(k));
            first ??= "member removed " + k;
            if (stopAtFirst)
                return first;
        }
        return first;
    }

    static string StripOccurrence(string k)
    {
        int i = k.LastIndexOf('#');
        return i > 0 && int.TryParse(k.AsSpan(i + 1), out _) ? k[..i] : k;
    }

    /// <summary>One Type: the whole-image unit table, that Type's census, its model only when the census differs, and its bodies.</summary>
    public static TypeRun Type(PEReader before, PEReader after, string name, ApiDiffOptions options)
    {
        var r = new TypeRun();
        long t = Stopwatch.GetTimestamp();
        var a = new FastDiff.Side(before);
        var b = new FastDiff.Side(after);
        var ua = a.Units().Values.FirstOrDefault(u => u.FullName == name);
        var ub = b.Units().Values.FirstOrDefault(u => u.FullName == name);
        r.UnitsMs = Ms(t);
        if (ua is null || ub is null)
        {
            r.Api = r.Body = FastDiffState.Changed;
            return r;
        }
        r.Api = FastDiffState.Unchanged;
        if (!a.ApiCensus(ua).SequenceEqual(b.ApiCensus(ub)))
        {
            r.ModelBuilt = true;
            var ha = ua.Owner;
            var hb = ub.Owner;
            var ta = ApiSurfaceExtractor.ExtractDeclarations(before, ApiSurfaceExtractionScope.Public, h => h == ha).Types.FirstOrDefault();
            var tb = ApiSurfaceExtractor.ExtractDeclarations(after, ApiSurfaceExtractionScope.Public, h => h == hb).Types.FirstOrDefault();
            if (ta is null != tb is null)
                r.Api = FastDiffState.Changed;
            else if (ta is not null && ModelCompare(ta, tb!, options, stopAtFirst: true, out _) is not null)
                r.Api = FastDiffState.Changed;
        }
        var run = new Run();
        var v = new TypeVerdict();
        var il = new IlBodyExistsComparer(a.Md, b.Md, Normalization);
        CompareBodies(run, a, b, ua, ub, Rows(ua), Rows(ub), v, il, Mode.Exists, bodyPlus: false, new Times());
        r.Body = v.Body;
        r.Methods = v.Methods;
        r.Bodies = run.Bodies;
        return r;
    }

    /// <summary>Fast Diff symbolic-key pairing vs IlAssemblyDiff's identity pairing (identity + occurrence), over every method.</summary>
    public static (int Same, int OnlyMine, int OnlyOracle, List<string> Examples) ComparePairing(PEReader before, PEReader after)
    {
        var a = new FastDiff.Side(before);
        var b = new FastDiff.Side(after);
        Dictionary<string, MethodDefinitionHandle> OracleMap(MetadataReader md)
        {
            var map = new Dictionary<string, MethodDefinitionHandle>(StringComparer.Ordinal);
            var occ = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var h in md.MethodDefinitions)
            {
                string? id = IlAssemblyDiff.SpikeMethodIdentity(md, h);
                if (id is null)
                    continue;
                int n = occ.TryGetValue(id, out int c) ? c + 1 : 1;
                occ[id] = n;
                map[$"{id}#{n}"] = h;
            }
            return map;
        }
        var oa = OracleMap(a.Md);
        var ob = OracleMap(b.Md);
        var oracle = new HashSet<(int, int)>();
        foreach (var (k, h) in oa)
            if (ob.TryGetValue(k, out var h2))
                oracle.Add((MetadataTokens.GetToken(h), MetadataTokens.GetToken(h2)));

        Dictionary<SymbolKey, MethodDefinitionHandle> KeyMap(FastDiff.Side s)
        {
            var map = new Dictionary<SymbolKey, MethodDefinitionHandle>();
            foreach (var h in s.Md.MethodDefinitions)
            {
                try { map.TryAdd(s.Key(h), h); }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException) { s.Keys.Reset(); }
            }
            return map;
        }
        var ka = KeyMap(a);
        var kb = KeyMap(b);
        var mine = new HashSet<(int, int)>();
        foreach (var (k, h) in ka)
            if (kb.TryGetValue(k, out var h2))
                mine.Add((MetadataTokens.GetToken(h), MetadataTokens.GetToken(h2)));
        var examples = new List<string>();
        foreach (var p in mine.Where(p => !oracle.Contains(p)).Take(3))
            examples.Add("key-only " + IlAssemblyDiff.SpikeMethodIdentity(a.Md, MetadataTokens.MethodDefinitionHandle(p.Item1 & 0xFFFFFF))
                + " <-> " + IlAssemblyDiff.SpikeMethodIdentity(b.Md, MetadataTokens.MethodDefinitionHandle(p.Item2 & 0xFFFFFF)));
        foreach (var p in oracle.Where(p => !mine.Contains(p)).Take(3))
            examples.Add("oracle-only " + IlAssemblyDiff.SpikeMethodIdentity(a.Md, MetadataTokens.MethodDefinitionHandle(p.Item1 & 0xFFFFFF)));
        return (mine.Count(oracle.Contains), mine.Count(p => !oracle.Contains(p)), oracle.Count(p => !mine.Contains(p)), examples);
    }

    /// <summary>The complete API comparison's output as comparable lines.</summary>
    public static List<string> ApiLines(ApiFindingComparison c)
    {
        var lines = new List<string>();
        foreach (var d in c.ApiDiff.TypeDiffs)
            foreach (var ch in d.Changes)
                lines.Add($"change|{d.TypeFullName}|{ch.Kind}|{ch.Classification}|{ch.Message}");
        if (c.Types.Value is FindingComparison<ApiTypeHandle>.Complete types)
            foreach (var p in types.Pairs.Where(p => p.Kind != PairKind.Present))
                lines.Add($"type|{p.Kind}|{Pay(p).TypeFullName}|{p.Detail}");
        if (c.Members.Value is FindingComparison<ApiMemberHandle>.Complete members)
            foreach (var p in members.Pairs.Where(p => p.Kind != PairKind.Present))
                lines.Add($"member|{p.Kind}|{Pay(p).TypeFullName}|{MemberKey(Pay(p))}|{p.Detail}");
        return lines;
    }

    static T Pay<T>(PairFinding<T> pair) where T : notnull
        => (((IPairFinding)pair).New as Finding<T> ?? (Finding<T>)((IPairFinding)pair).Old!).Payload;
}
