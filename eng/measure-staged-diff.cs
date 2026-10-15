#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj

// SPIKE (exp/staged-diff-spike). Measures one staged, early-exit "did this
// Type change" sequence built from the complete diffs' own comparisons,
// against Fast Diff and the complete oracles, over one Library image pair.
//
//   Stage 1  member count: raw method/field/property/event rows of the Type
//            and its compiler-generated nested Types (Body), and declared
//            public-surface Members (API). Different => Changed.
//   Stage 2  API: Type facets, then Member by Member, paired by the API
//            surface model's member identity and compared with the complete
//            API diff's own primitives (ApiFindingClassifier's checks).
//   Stage 3  Body: canonical IL per method pair (paired by the canonical
//            assembly diff's method identity), stopping at the first
//            difference. Modes: per-method IlBodyDiff.Compare (public API,
//            method granularity), in-body early exit, in-body early exit with
//            the raw-bytes fast path.
//
// Usage: measure-staged-diff <before.dll> <after.dll> [iterations] [topTypes]
// Requires the spike's InternalsVisibleTo on ILInspector.Metadata/ILDiff.

using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.ILDiff;
using ILInspector.Instructions;
using ILInspector.Metadata;
using Inspector.Findings;

if (args.Length is < 2 or > 4)
{
    Console.Error.WriteLine("Usage: measure-staged-diff <before.dll> <after.dll> [iterations] [topTypes]");
    return 2;
}

string beforePath = args[0];
string afterPath = args[1];
int iterations = args.Length > 2 ? int.Parse(args[2]) : 9;
int topTypes = args.Length > 3 ? int.Parse(args[3]) : 5;
byte[] beforeBytes = File.ReadAllBytes(beforePath);
byte[] afterBytes = File.ReadAllBytes(afterPath);
const IlBodyDiffNormalization Normalization =
    IlBodyDiffNormalization.NormalizeCurrentAssemblyScope | IlBodyDiffNormalization.NormalizePlatformAssemblyScope;
var apiOptions = new ApiDiffOptions(ApiDiffScope.All);

PEReader Open(byte[] bytes) => new(new MemoryStream(bytes, writable: false));

double Median(int n, Func<double> sample)
{
    sample();
    var values = Enumerable.Range(0, n).Select(_ => sample()).Order().ToList();
    return values[values.Count / 2];
}

static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

Console.WriteLine($"pair\t{beforePath}\n\t{afterPath}");

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
double fastApiMs = Median(iterations, () =>
{
    using var b = Open(beforeBytes);
    using var a = Open(afterBytes);
    long t = Stopwatch.GetTimestamp();
    FastDiff.Compare(b, a, FastDiffAxes.Api);
    return Ms(t);
});
var fastByName = new Dictionary<string, FastDiffTypeState>(StringComparer.Ordinal);
foreach (var s in fast.Types)
    fastByName.TryAdd(s.FullName, s);

// ---------------------------------------------------------------- Staged
StagedResult staged = default!;
var modeMs = new Dictionary<Stage3Mode, double>();
var modeBreakdown = new Dictionary<Stage3Mode, StageTimes>();
foreach (Stage3Mode mode in Enum.GetValues<Stage3Mode>())
{
    var samples = new List<StageTimes>();
    modeMs[mode] = Median(mode == Stage3Mode.Full ? Math.Max(3, iterations / 3) : iterations, () =>
    {
        using var b = Open(beforeBytes);
        using var a = Open(afterBytes);
        long t = Stopwatch.GetTimestamp();
        var r = Staged.RunLibrary(b, a, mode, apiOptions, Normalization);
        double ms = Ms(t);
        samples.Add(r.Times);
        if (mode == Stage3Mode.EarlyMemo)
            staged = r;
        return ms;
    });
    double Med(Func<StageTimes, double> f) { var v = samples.Select(f).Order().ToList(); return v[v.Count / 2]; }
    modeBreakdown[mode] = new StageTimes
    {
        Setup = Med(x => x.Setup), Extract = Med(x => x.Extract), Stage1 = Med(x => x.Stage1),
        Stage2 = Med(x => x.Stage2), Stage3 = Med(x => x.Stage3),
        BodiesCompared = samples[^1].BodiesCompared, TokensResolved = samples[^1].TokensResolved,
    };
}

// ---------------------------------------------------------------- Complete
const ApiSurfaceExtractionScope SurfaceScope = ApiSurfaceExtractionScope.Public;
ApiFindingComparison completeApi = default!;
double completeApiMs = Median(Math.Max(3, iterations / 3), () =>
{
    long t = Stopwatch.GetTimestamp();
    ApiSurface Surface(byte[] bytes)
    {
        using var r = Open(bytes);
        return ApiSurfaceExtractor.Extract(r, SurfaceScope);
    }
    completeApi = MetadataFindings.CompareApi(
        Surface(beforeBytes), Surface(afterBytes), new FindingSubject("api", "API"), apiOptions);
    return Ms(t);
});
IlAssemblyDiffResult completeIl = default!;
double completeIlMs = Median(Math.Max(3, iterations / 3), () =>
{
    long t = Stopwatch.GetTimestamp();
    completeIl = IlAssemblyDiff.CompareStreams(
        new MemoryStream(beforeBytes, writable: false), beforePath,
        new MemoryStream(afterBytes, writable: false), afterPath,
        maxExamples: int.MaxValue, normalization: Normalization).Diff;
    return Ms(t);
});

var apiOracle = new HashSet<string>(StringComparer.Ordinal);
foreach (TypeDiff type in completeApi.ApiDiff.TypeDiffs)
    apiOracle.Add(type.TypeFullName);
if (completeApi.Types.Value is FindingComparison<ApiTypeHandle>.Complete oracleTypes)
    foreach (var pair in oracleTypes.Pairs)
        if (pair.Kind != PairKind.Present)
            apiOracle.Add(Payload<ApiTypeHandle>(pair).TypeFullName);
if (completeApi.Members.Value is FindingComparison<ApiMemberHandle>.Complete oracleMembers)
    foreach (var pair in oracleMembers.Pairs)
        if (pair.Kind != PairKind.Present)
            apiOracle.Add(Payload<ApiMemberHandle>(pair).TypeFullName);
var bodyOracle = new HashSet<string>(StringComparer.Ordinal);
foreach (var example in completeIl.Examples)
    bodyOracle.Add(DeclaredOwner(example.Method));

// ---------------------------------------------------------------- Report
Console.WriteLine($"types\tstaged={staged.Types.Count}\tfast={fast.Types.Length}");
Console.WriteLine($"ms\tfast(ApiAndBody)={fastMs:F2}\tfast(Api)={fastApiMs:F2}");
foreach (var (mode, ms) in modeMs)
{
    var bt = modeBreakdown[mode];
    Console.WriteLine(
        $"ms\tstaged[{mode}]={ms:F2}\tsetup={bt.Setup:F2}\textract={bt.Extract:F2}\ts1={bt.Stage1:F2}"
            + $"\ts2={bt.Stage2:F2}\ts3={bt.Stage3:F2}\tbodies={bt.BodiesCompared}\ttokens={bt.TokensResolved}");
}
Console.WriteLine($"ms\tcomplete-api={completeApiMs:F2}\tcomplete-il={completeIlMs:F2}\tcomplete-both={completeApiMs + completeIlMs:F2}");

var st = staged.Types.Values;
Console.WriteLine("settle-api\t"
    + string.Join("\t", st.GroupBy(t => t.ApiBy).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}")));
Console.WriteLine("settle-body\t"
    + string.Join("\t", st.GroupBy(t => t.BodyBy).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}")));
Console.WriteLine(
    $"staged\tapiChanged={st.Count(t => t.Api == FastDiffState.Changed)}\tbodyChanged={st.Count(t => t.Body == FastDiffState.Changed)}"
        + $"\tbodyIndeterminate={st.Count(t => t.Body == FastDiffState.Indeterminate)}"
        + $"\tapiIndeterminate={st.Count(t => t.Api == FastDiffState.Indeterminate)}");
Console.WriteLine(
    $"fast\tapiChanged={fast.Types.Count(s => s.Api == FastDiffState.Changed)}\tbodyChanged={fast.Types.Count(s => s.Body == FastDiffState.Changed)}"
        + $"\tindeterminate={fast.Types.Count(s => s.Api == FastDiffState.Indeterminate || s.Body == FastDiffState.Indeterminate)}");
Console.WriteLine($"oracle\tapiChanged={apiOracle.Count}\tbodyOwners={bodyOracle.Count}\tilFailures={completeIl.FailureCount}\tilCompared={completeIl.ComparedBodyCount}	ilChanged={completeIl.ChangedBodyCount}	ilUnavailable={completeIl.PairUnavailableCount}");

// Agreement, per axis.
using (var eb = Open(beforeBytes))
using (var ea = Open(afterBytes))
{
    foreach (string axis in new[] { "api", "body" })
    {
        var counts = new Dictionary<string, int>();
        var disagreements = new List<string>();
        var names = new SortedSet<string>(st.Select(t => t.Name), StringComparer.Ordinal);
        foreach (var n in fastByName.Keys)
            names.Add(n);
        foreach (string name in names)
        {
            staged.Types.TryGetValue(name, out var s);
            fastByName.TryGetValue(name, out var f);
            FastDiffState? sv = s is null ? null : axis == "api" ? s.Api : s.Body;
            FastDiffState? fv = f is null ? null : axis == "api" ? f.Api : f.Body;
            bool oracle = axis == "api" ? apiOracle.Contains(name) : bodyOracle.Contains(name);
            string key = $"staged={Short(sv)} fast={Short(fv)} oracle={(oracle ? "C" : "-")}";
            counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
            bool stagedChanged = sv == FastDiffState.Changed;
            bool fastChanged = fv == FastDiffState.Changed;
            if (sv != fv || (oracle && sv == FastDiffState.Unchanged))
            {
                string reason = "";
                if (f is not null && fv == FastDiffState.Changed)
                {
                    var (api, body) = FastDiff.Explain(eb, ea, name);
                    reason = "fast: " + FirstLine(axis == "api" ? api : body);
                }
                string stagedReason = s is null ? "" : axis == "api" ? s.ApiDetail ?? "" : s.BodyDetail ?? "";
                if (oracle && axis == "api")
                    reason += "\toracle: " + FirstLine(string.Join("; ", completeApi.ApiDiff.TypeDiffs
                        .Where(d => d.TypeFullName == name).SelectMany(d => d.Changes).Select(c => c.Message).Take(3)));
                disagreements.Add($"  {axis}-disagree\t{name}\t{key}\tstaged: {FirstLine(stagedReason)}\t{reason}");
            }
            _ = stagedChanged || fastChanged;
        }
        foreach (var (key, count) in counts.OrderBy(kv => kv.Key))
            Console.WriteLine($"agree-{axis}\t{key}\t{count}");
        Console.WriteLine($"disagree-{axis}\t{disagreements.Count}");
        int limit = Environment.GetEnvironmentVariable("ALL") == "1" ? int.MaxValue : 25;
        foreach (string line in disagreements.Take(limit))
            Console.WriteLine(line);
    }
}

// Decode gaps.
Console.WriteLine($"gaps\tstaged-unavailable-bodies={staged.UnavailableBodies}\til-failures={completeIl.FailureCount}"
    + $"\tfast-indeterminate-types={fast.Types.Count(s => s.Api == FastDiffState.Indeterminate || s.Body == FastDiffState.Indeterminate)}");
foreach (var bucket in completeIl.FailureBuckets.Take(6))
    Console.WriteLine($"  il-failure\t{bucket.Count}\t{bucket.Name}");
foreach (var (reason, count) in staged.UnavailableReasons.OrderByDescending(kv => kv.Value).Take(6))
    Console.WriteLine($"  staged-unavailable\t{count}\t{reason}");
foreach (var s in fast.Types.Where(s => s.Api == FastDiffState.Indeterminate || s.Body == FastDiffState.Indeterminate).Take(5))
    Console.WriteLine($"  fast-indeterminate\t{s.FullName}\tapi={s.Api}\tbody={s.Body}");

// Stage 3 fast paths over every method pair of Types present on both sides.
FastPaths.Run(beforeBytes, afterBytes, Normalization, Math.Max(3, iterations / 3));

// Per Type: the largest changed Types.
var largest = staged.Types.Values
    .Where(t => t.Present && (t.Api == FastDiffState.Changed || t.Body == FastDiffState.Changed))
    .OrderByDescending(t => t.MethodRows)
    .Take(topTypes)
    .Select(t => t.Name)
    .ToList();
using (var pb = Open(beforeBytes))
using (var pa = Open(afterBytes))
{
    foreach (string name in largest)
    {
        TypeResult r = default!;
        double typeMs = Median(iterations, () =>
        {
            long t = Stopwatch.GetTimestamp();
            r = Staged.RunType(pb, pa, name, Stage3Mode.EarlyMemo, apiOptions, Normalization);
            return Ms(t);
        });
        TypeResult rf = default!;
        double typeFullMs = Median(Math.Max(3, iterations / 3), () =>
        {
            long t = Stopwatch.GetTimestamp();
            rf = Staged.RunType(pb, pa, name, Stage3Mode.Full, apiOptions, Normalization);
            return Ms(t);
        });
        double explainMs = Median(Math.Max(3, iterations / 3), () =>
        {
            long t = Stopwatch.GetTimestamp();
            FastDiff.Explain(pb, pa, name);
            return Ms(t);
        });
        double completeTypeMs = Median(Math.Max(3, iterations / 3), () =>
        {
            long t = Stopwatch.GetTimestamp();
            Staged.CompleteType(pb, pa, name, apiOptions, Normalization);
            return Ms(t);
        });
        Console.WriteLine(
            $"type\t{name}\tmethods={r.MethodRows}\tstaged={typeMs:F2}ms (api={r.Api}/{r.ApiBy} body={r.Body}/{r.BodyBy})"
                + $"\tstagedNoEarlyExitIl={typeFullMs:F2}ms\tfastExplain={explainMs:F2}ms\tcompleteType={completeTypeMs:F2}ms"
                + $"\tbreakdown extract={r.Times.Extract:F2} s1={r.Times.Stage1:F2} s2={r.Times.Stage2:F2} s3={r.Times.Stage3:F2} setup={r.Times.Setup:F2}");
    }
}
return 0;

static string Short(FastDiffState? s) => s switch
{
    null => "absent",
    FastDiffState.Changed => "C",
    FastDiffState.Unchanged => "U",
    FastDiffState.Indeterminate => "I",
    _ => s.ToString()!,
};

static string FirstLine(string? text)
{
    if (text is null)
        return "(null)";
    string l = text.Replace('\n', '|').Trim();
    return l.Length > 200 ? l[..200] + "…" : l;
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

enum Stage3Mode
{
    PerMethod,
    Early,
    EarlyRaw,
    EarlyMemo,
    EarlyMemoLite,
    Full,
}

sealed class StageTimes
{
    public double Setup, Extract, Stage1, Stage2, Stage3;
    public int BodiesCompared;
    public long TokensResolved;
}

sealed class TypeResult
{
    public required string Name;
    public bool Present;
    public int MethodRows;
    public FastDiffState Api = FastDiffState.Unchanged;
    public FastDiffState Body = FastDiffState.Unchanged;
    public string ApiBy = "";
    public string BodyBy = "";
    public string? ApiDetail;
    public string? BodyDetail;
    public StageTimes Times = new();
}

sealed class StagedResult
{
    public Dictionary<string, TypeResult> Types { get; } = new(StringComparer.Ordinal);
    public StageTimes Times { get; } = new();
    public int UnavailableBodies;
    public Dictionary<string, int> UnavailableReasons { get; } = new(StringComparer.Ordinal);
}

/// <summary>A declared Type and its compiler-generated nested Types.</summary>
sealed class Group
{
    public required string Name;
    public TypeDefinitionHandle Declared;
    public List<TypeDefinitionHandle> Rows { get; } = [];
    public bool Duplicate;

    public (int Methods, int Fields, int Properties, int Events) Count(MetadataReader md)
    {
        int m = 0, f = 0, p = 0, e = 0;
        foreach (var h in Rows)
        {
            var t = md.GetTypeDefinition(h);
            m += t.GetMethods().Count;
            f += t.GetFields().Count;
            p += t.GetProperties().Count;
            e += t.GetEvents().Count;
        }
        return (m, f, p, e);
    }
}

static class Staged
{
    public static Dictionary<string, Group> Groups(MetadataReader md)
    {
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        var path = new List<TypeDefinitionHandle>();
        foreach (var handle in md.TypeDefinitions)
        {
            path.Clear();
            var h = handle;
            while (!h.IsNil && path.Count < 64)
            {
                path.Add(h);
                h = md.GetTypeDefinition(h).GetDeclaringType();
            }
            path.Reverse();
            int generated = path.FindIndex(p => md.GetString(md.GetTypeDefinition(p).Name).StartsWith('<'));
            if (generated == 0)
                continue;
            int declaredDepth = generated < 0 ? path.Count : generated;
            var declared = path[declaredDepth - 1];
            string name = Name(md, path, declaredDepth);
            if (!groups.TryGetValue(name, out var group))
            {
                group = new Group { Name = name, Declared = declared };
                groups[name] = group;
            }
            else if (group.Declared != declared)
            {
                group.Duplicate = true;
            }
            group.Rows.Add(handle);
        }
        return groups;
    }

    static string Name(MetadataReader md, List<TypeDefinitionHandle> path, int depth)
    {
        var top = md.GetTypeDefinition(path[0]);
        string ns = md.GetString(top.Namespace);
        var parts = new List<string>(depth + 1);
        if (ns.Length > 0)
            parts.Add(ns);
        for (int i = 0; i < depth; i++)
            parts.Add(md.GetString(md.GetTypeDefinition(path[i]).Name));
        return string.Join('.', parts);
    }

    /// <summary>Finds one declared Type's group without walking the whole Type table's nesting.</summary>
    public static Group? FindGroup(MetadataReader md, string name)
    {
        foreach (var handle in md.TypeDefinitions)
        {
            var t = md.GetTypeDefinition(handle);
            if (t.GetDeclaringType().IsNil == false)
                continue;
            string ns = md.GetString(t.Namespace);
            string top = ns.Length > 0 ? ns + "." + md.GetString(t.Name) : md.GetString(t.Name);
            if (!name.StartsWith(top, StringComparison.Ordinal))
                continue;
            var found = Descend(md, handle, top, name);
            if (found is not null)
            {
                var group = new Group { Name = name, Declared = found.Value };
                AddGenerated(md, found.Value, group, includeSelf: true);
                return group;
            }
        }
        return null;
    }

    static TypeDefinitionHandle? Descend(MetadataReader md, TypeDefinitionHandle handle, string prefix, string name)
    {
        if (prefix == name)
            return handle;
        foreach (var nested in md.GetTypeDefinition(handle).GetNestedTypes())
        {
            string n = md.GetString(md.GetTypeDefinition(nested).Name);
            if (n.StartsWith('<'))
                continue;
            string next = prefix + "." + n;
            if (name.StartsWith(next, StringComparison.Ordinal))
            {
                var found = Descend(md, nested, next, name);
                if (found is not null)
                    return found;
            }
        }
        return null;
    }

    static void AddGenerated(MetadataReader md, TypeDefinitionHandle handle, Group group, bool includeSelf)
    {
        if (includeSelf)
            group.Rows.Add(handle);
        foreach (var nested in md.GetTypeDefinition(handle).GetNestedTypes())
        {
            bool generated = md.GetString(md.GetTypeDefinition(nested).Name).StartsWith('<');
            if (generated || !includeSelf)
            {
                group.Rows.Add(nested);
                AddGenerated(md, nested, group, includeSelf: false);
            }
        }
    }

    public static StagedResult RunLibrary(
        PEReader before,
        PEReader after,
        Stage3Mode mode,
        ApiDiffOptions options,
        IlBodyDiffNormalization normalization)
    {
        var result = new StagedResult();
        var times = result.Times;
        long t = Stopwatch.GetTimestamp();
        var bmd = before.GetMetadataReader();
        var amd = after.GetMetadataReader();
        var bGroups = Groups(bmd);
        var aGroups = Groups(amd);
        var comparer = new IlBodyExistsComparer(bmd, amd, normalization);
        times.Setup += Ms(t);

        t = Stopwatch.GetTimestamp();
        var bSurface = ApiSurfaceExtractor.ExtractDeclarations(before, ApiSurfaceExtractionScope.Public);
        var aSurface = ApiSurfaceExtractor.ExtractDeclarations(after, ApiSurfaceExtractionScope.Public);
        var bApi = ByName(bSurface);
        var aApi = ByName(aSurface);
        times.Extract += Ms(t);

        var names = new SortedSet<string>(bGroups.Keys, StringComparer.Ordinal);
        names.UnionWith(aGroups.Keys);
        foreach (string name in names)
        {
            bGroups.TryGetValue(name, out var bg);
            aGroups.TryGetValue(name, out var ag);
            bApi.TryGetValue(name, out var bt);
            aApi.TryGetValue(name, out var at);
            var r = new TypeResult { Name = name, Present = bg is not null && ag is not null };
            r.MethodRows = Math.Max(bg?.Count(bmd).Methods ?? 0, ag?.Count(amd).Methods ?? 0);
            CompareType(before, after, bg, ag, bt, at, r, times, comparer, mode, options, normalization, result);
            result.Types[name] = r;
        }
        times.TokensResolved = comparer.TokensResolved;
        return result;
    }

    public static TypeResult RunType(
        PEReader before,
        PEReader after,
        string name,
        Stage3Mode mode,
        ApiDiffOptions options,
        IlBodyDiffNormalization normalization)
    {
        var r = new TypeResult { Name = name };
        var times = r.Times;
        long t = Stopwatch.GetTimestamp();
        var bmd = before.GetMetadataReader();
        var amd = after.GetMetadataReader();
        var bg = FindGroup(bmd, name);
        var ag = FindGroup(amd, name);
        var comparer = new IlBodyExistsComparer(bmd, amd, normalization);
        times.Setup += Ms(t);
        t = Stopwatch.GetTimestamp();
        ApiType? Extract(PEReader pe, Group? g)
        {
            if (g is null)
                return null;
            var handle = g.Declared;
            int token = MetadataTokens.GetToken(handle);
            return ApiSurfaceExtractor.ExtractDeclarations(pe, ApiSurfaceExtractionScope.Public, h => h == handle)
                .Types.FirstOrDefault(type => type.MetadataToken == token);
        }
        var bt = Extract(before, bg);
        var at = Extract(after, ag);
        times.Extract += Ms(t);
        r.Present = bg is not null && ag is not null;
        r.MethodRows = Math.Max(bg?.Count(bmd).Methods ?? 0, ag?.Count(amd).Methods ?? 0);
        CompareType(before, after, bg, ag, bt, at, r, times, comparer, mode, options, normalization, new StagedResult());
        return r;
    }

    /// <summary>The complete per-Type reference: complete API comparison of one Type's surfaces and every canonical body pair.</summary>
    public static void CompleteType(
        PEReader before,
        PEReader after,
        string name,
        ApiDiffOptions options,
        IlBodyDiffNormalization normalization)
    {
        var bmd = before.GetMetadataReader();
        var amd = after.GetMetadataReader();
        var bg = FindGroup(bmd, name);
        var ag = FindGroup(amd, name);
        ApiSurface Surface(PEReader pe, Group? g)
        {
            if (g is null)
                return new ApiSurface();
            var handle = g.Declared;
            return ApiSurfaceExtractor.ExtractDeclarations(pe, ApiSurfaceExtractionScope.Public, h => h == handle);
        }
        MetadataFindings.CompareApi(Surface(before, bg), Surface(after, ag), new FindingSubject("api", "API"), options);
        if (bg is null || ag is null)
            return;
        var bm = Methods(bmd, bg);
        var am = Methods(amd, ag);
        foreach (var (key, bh) in bm)
        {
            if (am.TryGetValue(key, out var ah)
                && bmd.GetMethodDefinition(bh).RelativeVirtualAddress != 0
                && amd.GetMethodDefinition(ah).RelativeVirtualAddress != 0)
            {
                IlAssemblyDiff.CompareMembers(before, bmd, bh, after, amd, ah, normalization: normalization);
            }
        }
    }

    static Dictionary<string, ApiType> ByName(ApiSurface surface)
    {
        var map = new Dictionary<string, ApiType>(StringComparer.Ordinal);
        foreach (var type in surface.Types)
            map.TryAdd(type.FullName, type);
        return map;
    }

    static Dictionary<string, MethodDefinitionHandle> Methods(MetadataReader md, Group g)
    {
        var map = new Dictionary<string, MethodDefinitionHandle>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in g.Rows)
        {
            foreach (var m in md.GetTypeDefinition(row).GetMethods())
            {
                string identity = IlAssemblyDiff.SpikeMethodIdentity(md, m) ?? $"!token {MetadataTokens.GetToken(m):X8}";
                int n = occurrences.TryGetValue(identity, out int c) ? c + 1 : 1;
                occurrences[identity] = n;
                map[n == 1 ? identity : $"{identity}#{n}"] = m;
            }
        }
        return map;
    }

    static void CompareType(
        PEReader before,
        PEReader after,
        Group? bg,
        Group? ag,
        ApiType? bt,
        ApiType? at,
        TypeResult r,
        StageTimes times,
        IlBodyExistsComparer comparer,
        Stage3Mode mode,
        ApiDiffOptions options,
        IlBodyDiffNormalization normalization,
        StagedResult library)
    {
        var bmd = before.GetMetadataReader();
        var amd = after.GetMetadataReader();

        // Presence and identity.
        if (bg?.Duplicate == true || ag?.Duplicate == true)
        {
            r.Api = r.Body = FastDiffState.Indeterminate;
            r.ApiBy = r.BodyBy = "duplicate-name";
            return;
        }
        if (bg is null || ag is null)
        {
            r.Body = FastDiffState.Changed;
            r.BodyBy = "presence";
            r.Api = bt is not null || at is not null ? FastDiffState.Changed : FastDiffState.Unchanged;
            r.ApiBy = "presence";
            return;
        }

        // ---- API axis.
        long t = Stopwatch.GetTimestamp();
        if (bt is null && at is null)
        {
            r.ApiBy = "not-public";
            times.Stage1 += Ms(t);
        }
        else if (bt is null || at is null)
        {
            r.Api = FastDiffState.Changed;
            r.ApiBy = "visibility";
            r.ApiDetail = "public on one side only";
            times.Stage1 += Ms(t);
        }
        else
        {
            var bMembers = DeclaredMembers(bt);
            var aMembers = DeclaredMembers(at);
            if (bMembers.Count != aMembers.Count)
            {
                r.Api = FastDiffState.Changed;
                r.ApiBy = "s1-count";
                r.ApiDetail = $"public members {bMembers.Count} -> {aMembers.Count}";
                times.Stage1 += Ms(t);
            }
            else
            {
                times.Stage1 += Ms(t);
                t = Stopwatch.GetTimestamp();
                r.ApiDetail = ApiFirstDifference(bt, at, bMembers, aMembers, options, out string by);
                r.Api = r.ApiDetail is null ? FastDiffState.Unchanged : FastDiffState.Changed;
                r.ApiBy = by;
                times.Stage2 += Ms(t);
            }
        }

        // ---- Body axis.
        t = Stopwatch.GetTimestamp();
        var bc = bg.Count(bmd);
        var ac = ag.Count(amd);
        if (bc != ac)
        {
            r.Body = FastDiffState.Changed;
            r.BodyBy = "s1-count";
            r.BodyDetail = $"rows {bc} -> {ac}";
            times.Stage1 += Ms(t);
            return;
        }
        times.Stage1 += Ms(t);

        t = Stopwatch.GetTimestamp();
        var bm = Methods(bmd, bg);
        var am = Methods(amd, ag);
        bool unavailable = false;
        r.BodyBy = "s3-unchanged";
        foreach (var (key, bh) in bm)
        {
            if (!am.TryGetValue(key, out var ah))
            {
                r.Body = FastDiffState.Changed;
                r.BodyBy = "s3-pairing";
                r.BodyDetail = $"unpaired {key}";
                break;
            }
            var bd = bmd.GetMethodDefinition(bh);
            var ad = amd.GetMethodDefinition(ah);
            if (bd.RelativeVirtualAddress == 0 && ad.RelativeVirtualAddress == 0)
                continue;
            if (bd.RelativeVirtualAddress == 0 || ad.RelativeVirtualAddress == 0)
            {
                r.Body = FastDiffState.Changed;
                r.BodyBy = "s3-body-presence";
                r.BodyDetail = key;
                break;
            }
            MethodBodyBlock bb, ab;
            try
            {
                bb = before.GetMethodBody(bd.RelativeVirtualAddress);
                ab = after.GetMethodBody(ad.RelativeVirtualAddress);
            }
            catch (BadImageFormatException ex)
            {
                unavailable = true;
                Note(library, "body read failed: " + ex.Message);
                continue;
            }
            times.BodiesCompared++;
            IlExistsOutcome outcome;
            string? failure = null;
            switch (mode)
            {
                case Stage3Mode.PerMethod:
                case Stage3Mode.Full:
                    var diff = IlBodyDiff.Compare(bmd, bb, amd, ab, normalization);
                    outcome = !diff.IsAvailable ? IlExistsOutcome.Unavailable
                        : diff.IsExact ? IlExistsOutcome.Exact : IlExistsOutcome.Changed;
                    failure = diff.Failure;
                    break;
                default:
                    outcome = comparer.Compare(
                        bb, ab,
                        rawFastPath: mode == Stage3Mode.EarlyRaw,
                        memoTokens: mode is Stage3Mode.EarlyMemo or Stage3Mode.EarlyMemoLite,
                        liteDecode: mode == Stage3Mode.EarlyMemoLite,
                        out _, out failure);
                    break;
            }
            if (outcome == IlExistsOutcome.Unavailable)
            {
                unavailable = true;
                Note(library, failure ?? "unavailable");
                continue;
            }
            if (outcome == IlExistsOutcome.Changed && r.Body != FastDiffState.Changed)
            {
                r.Body = FastDiffState.Changed;
                r.BodyBy = "s3-il";
                r.BodyDetail = key;
                if (mode != Stage3Mode.Full)
                    break;
            }
        }
        if (unavailable && r.Body != FastDiffState.Changed)
        {
            r.Body = FastDiffState.Indeterminate;
            r.BodyBy = "s3-unavailable";
        }
        times.Stage3 += Ms(t);
    }

    static void Note(StagedResult library, string reason)
    {
        library.UnavailableBodies++;
        library.UnavailableReasons[reason] = library.UnavailableReasons.TryGetValue(reason, out int c) ? c + 1 : 1;
    }

    static bool IsDeclared(ApiMember m)
        => m.DeclaringTypeCanonicalName is null && m.DeclaringTypeDefinitionName is null && m.DeclaringOverloadIndex is null;

    static List<ApiMemberHandle> DeclaredMembers(ApiType type)
    {
        var list = new List<ApiMemberHandle>(type.Members.Count);
        foreach (var member in type.Members)
        {
            if (!IsDeclared(member) || MemberFilters.IsCompilerGenerated(member.Name))
                continue;
            list.Add(ApiMemberIdentity.CreateHandle(type, member));
        }
        return list;
    }

    static string Key(ApiMemberHandle h) => h.CanonicalSignature ?? h.Identity;

    /// <summary>
    /// Stage 2: the complete API diff's own checks (ApiFindingClassifier), Type facets
    /// first, then Member by Member, stopping at the first change.
    /// </summary>
    static string? ApiFirstDifference(
        ApiType bt,
        ApiType at,
        List<ApiMemberHandle> bMembers,
        List<ApiMemberHandle> aMembers,
        ApiDiffOptions options,
        out string by)
    {
        var facets = ApiDiffAnalyzer.CompareTypeFacetsOnly(bt, at, options);
        if (facets.Count > 0)
        {
            by = "s2-type-facets";
            return facets[0].Message;
        }
        // The complete comparison also folds Finding-lane facet details onto each pair
        // (MetadataFindings.Add*FacetDetails), a fact set that overlaps but is not equal to
        // the classifier's; a Type it marks Changed is API-changed in the complete diff.
        var details = new List<string>();
        MetadataFindings.AddTypeFacetDetails(bt, at, options.Scope, details);
        if (details.Count > 0)
        {
            by = "s2-type-facet-details";
            return details[0];
        }
        var old = new Dictionary<string, ApiMemberHandle>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var h in bMembers)
        {
            string k = Key(h);
            int n = occurrences.TryGetValue(k, out int c) ? c + 1 : 1;
            occurrences[k] = n;
            old[n == 1 ? k : $"{k}#{n}"] = h;
        }
        occurrences.Clear();
        var changes = new List<ApiChange>();
        foreach (var nh in aMembers)
        {
            string k = Key(nh);
            int n = occurrences.TryGetValue(k, out int c) ? c + 1 : 1;
            occurrences[k] = n;
            if (!old.TryGetValue(n == 1 ? k : $"{k}#{n}", out var oh))
            {
                by = "s2-member-pairing";
                return $"unpaired member {k}";
            }
            if (!string.Equals(oh.Member.Signature, nh.Member.Signature, StringComparison.Ordinal))
            {
                by = "s2-member-signature";
                return $"signature {oh.Member.Signature} -> {nh.Member.Signature}";
            }
            ApiDiffAnalyzer.CompareMemberModifiers(bt, at, oh.Member, nh.Member, changes);
            ApiDiffAnalyzer.CompareMemberTypeParameterConstraints(bt, at, oh.Member, nh.Member, changes);
            if (changes.Count == 0)
            {
                ApiDiffAnalyzer.CompareAttributes(
                    oh.Member.Attributes, nh.Member.Attributes, changes,
                    ChangeKind.MemberAttributeAdded, ChangeKind.MemberAttributeRemoved,
                    $"Member '{oh.MemberName}'", ApiChangeSubject.Member(bt, oh.Member, at, nh.Member));
            }
            if (changes.Count > 0)
            {
                by = "s2-member-facets";
                return changes[0].Message;
            }
            MetadataFindings.AddMemberFacetDetails(oh.Member, nh.Member, options.Scope, details);
            if (details.Count > 0)
            {
                by = "s2-member-facet-details";
                return details[0];
            }
        }
        by = "s2-unchanged";
        return null;
    }

    static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}

/// <summary>Stage 3 fast-path statistics over every method pair with a body on both sides.</summary>
static class FastPaths
{
    public static void Run(byte[] beforeBytes, byte[] afterBytes, IlBodyDiffNormalization normalization, int repeats)
    {
        using var before = new PEReader(new MemoryStream(beforeBytes, writable: false));
        using var after = new PEReader(new MemoryStream(afterBytes, writable: false));
        var bmd = before.GetMetadataReader();
        var amd = after.GetMetadataReader();
        var bGroups = Staged.Groups(bmd);
        var aGroups = Staged.Groups(amd);
        var pairs = new List<(MethodBodyBlock B, MethodBodyBlock A)>();
        foreach (var (name, bg) in bGroups)
        {
            if (!aGroups.TryGetValue(name, out var ag) || bg.Duplicate || ag.Duplicate)
                continue;
            var bm = MethodMap(bmd, bg);
            var am = MethodMap(amd, ag);
            foreach (var (key, bh) in bm)
            {
                if (!am.TryGetValue(key, out var ah))
                    continue;
                var bd = bmd.GetMethodDefinition(bh);
                var ad = amd.GetMethodDefinition(ah);
                if (bd.RelativeVirtualAddress == 0 || ad.RelativeVirtualAddress == 0)
                    continue;
                try
                {
                    pairs.Add((before.GetMethodBody(bd.RelativeVirtualAddress), after.GetMethodBody(ad.RelativeVirtualAddress)));
                }
                catch (BadImageFormatException)
                {
                }
            }
        }

        // Outcomes and the helper's agreement with IlBodyDiff.Compare.
        var comparer = new IlBodyExistsComparer(bmd, amd, normalization);
        int exact = 0, changed = 0, unavailable = 0, helperMismatch = 0;
        int rawEqual = 0, rawEqualChanged = 0, rawEqualUnavailable = 0;
        int headerEqual = 0, headerDiffExact = 0, headerEqualChanged = 0, codeSizeDiffExact = 0, codeSizeDiff = 0;
        int countEqual = 0, countEqualChanged = 0, countDiffExact = 0;
        var outcomes = new IlExistsOutcome[pairs.Count];
        for (int i = 0; i < pairs.Count; i++)
        {
            var (b, a) = pairs[i];
            var outcome = comparer.Compare(b, a, rawFastPath: false, out bool raw, out _);
            outcomes[i] = outcome;
            var full = IlBodyDiff.Compare(bmd, b, amd, a, normalization);
            var fullOutcome = !full.IsAvailable ? IlExistsOutcome.Unavailable : full.IsExact ? IlExistsOutcome.Exact : IlExistsOutcome.Changed;
            if (fullOutcome != outcome)
                helperMismatch++;
            switch (outcome)
            {
                case IlExistsOutcome.Exact: exact++; break;
                case IlExistsOutcome.Changed: changed++; break;
                default: unavailable++; break;
            }
            if (raw)
            {
                rawEqual++;
                if (outcome == IlExistsOutcome.Changed)
                    rawEqualChanged++;
                if (outcome == IlExistsOutcome.Unavailable)
                    rawEqualUnavailable++;
            }
            bool header = Header(bmd, b) == Header(amd, a);
            if (header)
            {
                headerEqual++;
                if (outcome == IlExistsOutcome.Changed)
                    headerEqualChanged++;
            }
            else if (outcome == IlExistsOutcome.Exact)
            {
                headerDiffExact++;
            }
            if (b.Size != a.Size)
            {
                codeSizeDiff++;
                if (outcome == IlExistsOutcome.Exact)
                    codeSizeDiffExact++;
            }
            var bi = InstructionDecoder.Decode(b.GetILBytes() ?? []);
            var ai = InstructionDecoder.Decode(a.GetILBytes() ?? []);
            if (bi.Length == ai.Length)
            {
                countEqual++;
                if (outcome == IlExistsOutcome.Changed)
                    countEqualChanged++;
            }
            else if (outcome == IlExistsOutcome.Exact)
            {
                countDiffExact++;
            }
        }
        Console.WriteLine(
            $"fastpath\tpairs={pairs.Count}\texact={exact}\tchanged={changed}\tunavailable={unavailable}\thelperVsIlBodyDiffMismatch={helperMismatch}");
        Console.WriteLine(
            $"fastpath-b\trawEqual={rawEqual}\trawEqualButCanonicalChanged={rawEqualChanged}\trawEqualUnavailable={rawEqualUnavailable}");
        Console.WriteLine(
            $"fastpath-a\theaderEqual={headerEqual}\theaderEqualButChanged={headerEqualChanged}\theaderDiffButExact={headerDiffExact}"
                + $"\tcodeSizeDiff={codeSizeDiff}\tcodeSizeDiffButExact={codeSizeDiffExact}");
        Console.WriteLine(
            $"fastpath-c\tcountEqual={countEqual}\tcountEqualButChanged={countEqualChanged}\tcountDiffButExact={countDiffExact}");

        // Loop timings over all pairs, medians of repeats.
        double Time(Action<int> body)
        {
            var values = new List<double>();
            for (int r = 0; r <= repeats; r++)
            {
                long t = Stopwatch.GetTimestamp();
                for (int i = 0; i < pairs.Count; i++)
                    body(i);
                if (r > 0)
                    values.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            }
            values.Sort();
            return values[values.Count / 2];
        }
        double tHeader = Time(i => _ = Header(bmd, pairs[i].B) == Header(amd, pairs[i].A));
        double tRaw = Time(i => _ = (pairs[i].B.GetILBytes() ?? []).AsSpan().SequenceEqual(pairs[i].A.GetILBytes() ?? []));
        double count = Time(i => _ = InstructionDecoder.Decode(pairs[i].B.GetILBytes() ?? []).Length
            == InstructionDecoder.Decode(pairs[i].A.GetILBytes() ?? []).Length);
        double decode = Time(i =>
        {
            _ = MethodInstructions.Decode(pairs[i].B);
            _ = MethodInstructions.Decode(pairs[i].A);
        });
        var c1 = new IlBodyExistsComparer(bmd, amd, normalization);
        double early = Time(i => c1.Compare(pairs[i].B, pairs[i].A, rawFastPath: false, out _, out _));
        var c2 = new IlBodyExistsComparer(bmd, amd, normalization);
        double earlyRaw = Time(i => c2.Compare(pairs[i].B, pairs[i].A, rawFastPath: true, out _, out _));
        double tFull = Time(i => IlBodyDiff.Compare(bmd, pairs[i].B, amd, pairs[i].A, normalization));
        var c3 = new IlBodyExistsComparer(bmd, amd, normalization);
        double earlyMemo = Time(i => c3.Compare(pairs[i].B, pairs[i].A, false, memoTokens: true, liteDecode: false, out _, out _));
        var c4 = new IlBodyExistsComparer(bmd, amd, normalization);
        double earlyMemoLite = Time(i => c4.Compare(pairs[i].B, pairs[i].A, false, memoTokens: true, liteDecode: true, out _, out _));
        var c5 = new IlBodyExistsComparer(bmd, amd, normalization);
        int liteMismatch = 0;
        for (int i = 0; i < pairs.Count; i++)
            if (c5.Compare(pairs[i].B, pairs[i].A, false, memoTokens: true, liteDecode: true, out _, out _) != outcomes[i])
                liteMismatch++;
        Console.WriteLine($"fastpath-lite\tmemoLiteVsCanonicalMismatch={liteMismatch}");
        Console.WriteLine(
            $"fastpath-ms\tall-pairs earlyExit+memo={earlyMemo:F2}\tearlyExit+memo+liteDecode={earlyMemoLite:F2}");
        Console.WriteLine(
            $"fastpath-ms\tall-pairs header={tHeader:F2}\trawBytes={tRaw:F2}\tcountInstructions={count:F2}\tdecode(MethodInstructions)={decode:F2}"
                + $"\tearlyExit={early:F2}\tearlyExit+raw={earlyRaw:F2}\tIlBodyDiff.Compare={tFull:F2}");
        // Same over the Exact (unchanged) pairs only: the common case the fast path targets.
        var exactPairs = Enumerable.Range(0, pairs.Count).Where(i => outcomes[i] == IlExistsOutcome.Exact).ToArray();
        double TimeSubset(Action<int> body)
        {
            var values = new List<double>();
            for (int r = 0; r <= repeats; r++)
            {
                long t = Stopwatch.GetTimestamp();
                foreach (int i in exactPairs)
                    body(i);
                if (r > 0)
                    values.Add(Stopwatch.GetElapsedTime(t).TotalMilliseconds);
            }
            values.Sort();
            return values[values.Count / 2];
        }
        double eEarly = TimeSubset(i => c1.Compare(pairs[i].B, pairs[i].A, rawFastPath: false, out _, out _));
        double eEarlyRaw = TimeSubset(i => c2.Compare(pairs[i].B, pairs[i].A, rawFastPath: true, out _, out _));
        double eDecode = TimeSubset(i =>
        {
            _ = MethodInstructions.Decode(pairs[i].B);
            _ = MethodInstructions.Decode(pairs[i].A);
        });
        Console.WriteLine(
            $"fastpath-ms\texact-pairs={exactPairs.Length} decode={eDecode:F2}\tearlyExit={eEarly:F2}\tearlyExit+raw={eEarlyRaw:F2}");
    }

    static (int Size, int MaxStack, bool InitLocals, int Regions, int LocalSigLength) Header(MetadataReader md, MethodBodyBlock body)
        => (body.Size, body.MaxStack, body.LocalVariablesInitialized, body.ExceptionRegions.Length,
            body.LocalSignature.IsNil ? -1 : md.GetBlobReader(md.GetStandaloneSignature(body.LocalSignature).Signature).Length);

    static Dictionary<string, MethodDefinitionHandle> MethodMap(MetadataReader md, Group g)
    {
        var map = new Dictionary<string, MethodDefinitionHandle>(StringComparer.Ordinal);
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in g.Rows)
        {
            foreach (var m in md.GetTypeDefinition(row).GetMethods())
            {
                string identity = IlAssemblyDiff.SpikeMethodIdentity(md, m) ?? $"!token {MetadataTokens.GetToken(m):X8}";
                int n = occurrences.TryGetValue(identity, out int c) ? c + 1 : 1;
                occurrences[identity] = n;
                map[n == 1 ? identity : $"{identity}#{n}"] = m;
            }
        }
        return map;
    }
}
