#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj

// SPIKE (exp/unified-diff-exists, #10227). Can the complete diffs answer
// Library-scope Exists per Type, with their own code, at Fast Diff's cost and
// with identical verdicts?
//
//   B1  Body: IlLibraryExistsSession (ILInspector.ILDiff) built once per image
//       pair: IlAssemblyDiff's method map and (identity, occurrence) pairing,
//       a Type partition over that identity, the ordinal correspondence when
//       the policy asks for it, and one IlBodyExistsComparer with memoised
//       operand resolvers. Per Type, early exit at the first non-Exact body.
//   A1  API, no census: both whole Public API surfaces extracted once by the
//       complete extractor; per Type pair a bool predicate made of the complete
//       comparison's own checks (ApiFindingClassifier's ApiDiffAnalyzer checks
//       and MetadataFindings facet details), early exit.
//   A2  API, census as the comparison's first stage: Fast Diff's census and the
//       staged spike's presented/uncertain classes (copied from
//       measure-census-classes.cs), the model built only for uncertain Types.
//
// Baselines: shipped FastDiff.Compare (both axes, API only), today's complete
// API comparison (Extract + CompareApi) and IL diff (IlAssemblyDiff.Compare).
//
//   B2  (#10227 Q1) B1opt with the complete IL diff's text method and operand
//       identities replaced by SymbolKey ids (IlSymbolicExistsSession).
//
// Usage: measure-unified-diff <before.dll> <after.dll> [iterations] [--no-correctness] [--detail] [--q1]
//   --q1  only Fast Diff, B1opt, and B2 (timing, attribution, Body gates).

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

var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
if (positional.Length < 2)
{
    Console.Error.WriteLine("Usage: measure-unified-diff <before.dll> <after.dll> [iterations] [--no-correctness] [--detail]");
    return 2;
}
string beforePath = positional[0];
string afterPath = positional[1];
int iterations = positional.Length > 2 ? int.Parse(positional[2]) : 7;
bool correctness = !args.Contains("--no-correctness");
bool detail = args.Contains("--detail");
bool q1 = args.Contains("--q1");
byte[] beforeBytes = File.ReadAllBytes(beforePath);
byte[] afterBytes = File.ReadAllBytes(afterPath);
var options = new ApiDiffOptions(ApiDiffScope.All);
const IlBodyDiffNormalization Policy =
    IlBodyDiffNormalization.NormalizeCurrentAssemblyScope | IlBodyDiffNormalization.NormalizePlatformAssemblyScope;
int few = Math.Max(3, iterations / 2);

PEReader Open(byte[] bytes) => new(new MemoryStream(bytes, writable: false));
static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
static double MedianOf(IEnumerable<double> v) { var l = v.Order().ToList(); return l.Count == 0 ? 0 : l[l.Count / 2]; }
double Median(int n, Func<double> sample)
{
    sample(); // warm
    return MedianOf(Enumerable.Range(0, n).Select(_ => sample()).ToList());
}
void Row(string label, double ms, string extra = "") => Console.WriteLine($"ms\t{label}\t{ms:F2}{(extra.Length > 0 ? "\t" + extra : "")}");

Console.WriteLine($"pair\t{beforePath}\t{afterPath}\titerations={iterations}");

// ============================================================ Baselines
FastDiffResult fast = default!;
double fastBoth = Median(iterations, () =>
{
    using var b = Open(beforeBytes); using var a = Open(afterBytes);
    long t = Stopwatch.GetTimestamp();
    fast = FastDiff.Compare(b, a);
    return Ms(t);
});
FastDiffResult fastApi = default!;
double fastApiMs = Median(iterations, () =>
{
    using var b = Open(beforeBytes); using var a = Open(afterBytes);
    long t = Stopwatch.GetTimestamp();
    fastApi = FastDiff.Compare(b, a, FastDiffAxes.Api);
    return Ms(t);
});
Row("fast(ApiAndBody)", fastBoth, $"types={fast.Types.Length}\tbodies={fast.Receipt.BodiesCompared}");
Row("fast(Api)", fastApiMs);

// ============================================================ B1: Body via the complete IL diff
(double Total, double Session, double Walk, BodyRun Run) MeasureB1(int n, IlBodyDiffNormalization normalization, bool lite,
    IlSessionSpikeOptions spike = IlSessionSpikeOptions.None, bool familyCache = false)
{
    IlBodyDiff.SpikeFamilyCache = familyCache;
    var totals = new List<double>(); var sessions = new List<double>(); var walks = new List<double>();
    BodyRun last = default!;
    for (int i = 0; i <= n; i++)
    {
        using var b = Open(beforeBytes); using var a = Open(afterBytes);
        long t = Stopwatch.GetTimestamp();
        var session = new IlLibraryExistsSession(b, a, normalization, lite, spike);
        double s = Ms(t);
        long w = Stopwatch.GetTimestamp();
        var run = BodyRun.Walk(session);
        double walk = Ms(w);
        double total = Ms(t);
        if (i == 0) continue; // warm
        totals.Add(total); sessions.Add(s); walks.Add(walk);
        run.SessionTimes = session.Times;
        last = run;
    }
    IlBodyDiff.SpikeFamilyCache = false;
    return (MedianOf(totals), MedianOf(sessions), MedianOf(walks), last);
}

var b1Opt = MeasureB1(iterations, Policy, false, IlSessionSpikeOptions.All, familyCache: true);
var b1 = q1 ? b1Opt : MeasureB1(iterations, Policy, lite: false);
var b1Lite = q1 ? b1Opt : MeasureB1(iterations, Policy, lite: true);
var b1Ord = q1 ? b1Opt : MeasureB1(few, Policy | IlBodyDiffNormalization.NormalizeCompilerGeneratedOrdinals, lite: false);
// Optimizations inside the complete diffs, added one at a time (cumulative).
var b1Map = q1 ? b1Opt : MeasureB1(iterations, Policy, false, IlSessionSpikeOptions.MapMemo);
var b1Res = q1 ? b1Opt : MeasureB1(iterations, Policy, false, IlSessionSpikeOptions.MapMemo | IlSessionSpikeOptions.ResolverMemo);
var b1Fam = q1 ? b1Opt : MeasureB1(iterations, Policy, false, IlSessionSpikeOptions.MapMemo | IlSessionSpikeOptions.ResolverMemo, familyCache: true);
var b1Key = q1 ? b1Opt : MeasureB1(iterations, Policy, false, IlSessionSpikeOptions.MapMemo | IlSessionSpikeOptions.ResolverMemo | IlSessionSpikeOptions.KeyCompare, familyCache: true);
var b1Chk = q1 ? b1Opt : MeasureB1(iterations, Policy, false, IlSessionSpikeOptions.All & ~(IlSessionSpikeOptions.SharedDecode | IlSessionSpikeOptions.BlockGraphTopology), familyCache: true);
var b1Shr = q1 ? b1Opt : MeasureB1(iterations, Policy, false, IlSessionSpikeOptions.All & ~IlSessionSpikeOptions.BlockGraphTopology, familyCache: true);

// B2: B1opt with symbolic method and operand identity.
(double Total, double Session, double Walk, BodyRun Run) MeasureB2(int n)
{
    IlBodyDiff.SpikeFamilyCache = true;
    var totals = new List<double>(); var sessions = new List<double>(); var walks = new List<double>();
    BodyRun last = default!;
    for (int i = 0; i <= n; i++)
    {
        using var b = Open(beforeBytes); using var a = Open(afterBytes);
        long t = Stopwatch.GetTimestamp();
        var session = new IlSymbolicExistsSession(b, a, Policy);
        double s = Ms(t);
        long w = Stopwatch.GetTimestamp();
        var run = BodyRun.Walk(session);
        double walk = Ms(w);
        double total = Ms(t);
        if (i == 0) continue;
        totals.Add(total); sessions.Add(s); walks.Add(walk);
        run.SessionTimes = session.Times;
        last = run;
    }
    IlBodyDiff.SpikeFamilyCache = false;
    return (MedianOf(totals), MedianOf(sessions), MedianOf(walks), last);
}
var b2 = MeasureB2(iterations);
void PrintB1(string label, (double Total, double Session, double Walk, BodyRun Run) m)
    => Row(label, m.Total,
        $"session={m.Session:F2}\tmethodMap={m.Run.SessionTimes.MethodMap:F2}\tpartition={m.Run.SessionTimes.Partition:F2}"
        + $"\tordinals={m.Run.SessionTimes.Ordinals:F2}\tcomparer={m.Run.SessionTimes.Comparer:F2}\twalk={m.Walk:F2}"
        + $"\tunits={m.Run.States.Count}\tbodies={m.Run.Bodies}");
if (!q1)
{
PrintB1("B1(full decode)", b1);
PrintB1("B1(instructions-only decode)", b1Lite);
PrintB1("B1+ordinals(full decode)", b1Ord);
PrintB1("B1 +map memo", b1Map);
PrintB1("B1 +map,resolver memo", b1Res);
PrintB1("B1 +map,resolver memo,family cache", b1Fam);
PrintB1("B1 +map,resolver memo,family cache,key compare", b1Key);
PrintB1("B1 +...,checked decode", b1Chk);
PrintB1("B1 +...,shared decode for byte-identical bodies", b1Shr);
}
PrintB1("B1opt (+block-graph-only region topology)", b1Opt);
PrintB1("B2 (B1opt + symbolic method and operand identity)", b2);

// Attribution: one instrumented walk (not the timed one).
{
    using var b = Open(beforeBytes); using var a = Open(afterBytes);
    var session = new IlLibraryExistsSession(b, a, Policy, liteDecode: false);
    session.Comparer.Instrument = true;
    long w = Stopwatch.GetTimestamp();
    BodyRun.Walk(session);
    double walk = Ms(w);
    double decode = session.Comparer.DecodeTicks * 1000.0 / Stopwatch.Frequency;
    double resolve = session.Comparer.ResolveTicks * 1000.0 / Stopwatch.Frequency;
    Console.WriteLine($"attr-body\tinstrumentedWalk={walk:F2}\tdecode(full)={decode:F2}\tresolve={resolve:F2}\tresolveMisses={session.Comparer.ResolveMisses}"
        + $"\tcanonicalise+compare={walk - decode - resolve:F2}\tinstructions={session.Comparer.InstructionsCompared}");
    IlBodyDiff.SpikeFamilyCache = true;
    var opt = new IlLibraryExistsSession(b, a, Policy, liteDecode: false, IlSessionSpikeOptions.All);
    opt.Comparer.Instrument = true;
    w = Stopwatch.GetTimestamp();
    BodyRun.Walk(opt);
    walk = Ms(w);
    decode = opt.Comparer.DecodeTicks * 1000.0 / Stopwatch.Frequency;
    resolve = opt.Comparer.ResolveTicks * 1000.0 / Stopwatch.Frequency;
    Console.WriteLine($"attr-body-opt\tinstrumentedWalk={walk:F2}\tdecode(checked)={decode:F2}\tresolve={resolve:F2}\tresolveMisses={opt.Comparer.ResolveMisses}"
        + $"\tcompare={walk - decode - resolve:F2}\tcheckedFallbacks={opt.Comparer.CheckedFallbacks}"
        + $"\tof decode: regionTopology={opt.Comparer.TopologyTicks * 1000.0 / Stopwatch.Frequency:F2} over {opt.Comparer.RegionBodies} bodies\tsharedDecodes={opt.Comparer.SharedDecodes}");
    IlBodyDiff.SpikeFamilyCache = false;
    var lite = new IlLibraryExistsSession(b, a, Policy, liteDecode: true);
    lite.Comparer.Instrument = true;
    w = Stopwatch.GetTimestamp();
    BodyRun.Walk(lite);
    walk = Ms(w);
    decode = lite.Comparer.DecodeTicks * 1000.0 / Stopwatch.Frequency;
    resolve = lite.Comparer.ResolveTicks * 1000.0 / Stopwatch.Frequency;
    Console.WriteLine($"attr-body-lite\tinstrumentedWalk={walk:F2}\tdecode(lite)={decode:F2}\tresolve={resolve:F2}\tcanonicalise+compare={walk - decode - resolve:F2}");

    // B2 attribution: session (symbolic method map, partition), then decode, operand
    // identity (memo misses only, as for B1opt), and comparison.
    IlBodyDiff.SpikeFamilyCache = true;
    long st = Stopwatch.GetTimestamp();
    var sym = new IlSymbolicExistsSession(b, a, Policy);
    double symSession = Ms(st);
    sym.Comparer.Instrument = true;
    w = Stopwatch.GetTimestamp();
    BodyRun.Walk(sym);
    walk = Ms(w);
    decode = sym.Comparer.DecodeTicks * 1000.0 / Stopwatch.Frequency;
    resolve = sym.Comparer.ResolveTicks * 1000.0 / Stopwatch.Frequency;
    Console.WriteLine($"attr-body-b2\tsession={symSession:F2}\tmethodMap={sym.Times.MethodMap:F2}\tpartition={sym.Times.Partition:F2}"
        + $"\tinstrumentedWalk={walk:F2}\tdecode(checked)={decode:F2}\tresolve={resolve:F2}\tresolveMisses={sym.Comparer.ResolveMisses}"
        + $"\tcompare={walk - decode - resolve:F2}\tsharedDecodes={sym.Comparer.SharedDecodes}\tkeys={sym.Table.Count}\tkeyBytes={sym.Table.Bytes}"
        + $"\tmethods={b.GetMetadataReader().MethodDefinitions.Count}+{a.GetMetadataReader().MethodDefinitions.Count}");
    IlBodyDiff.SpikeFamilyCache = false;
}

// ============================================================ A1: API via the complete comparison, no census
ApiSurface sa = default!, sb = default!;
ApiFindingComparison wholeApi = default!;
CensusApiRun a2 = default!;
ApiRun a1 = default!;
if (!q1)
{
var extractTimes = new List<double>();
var compareTimes = new List<double>();
for (int i = 0; i <= iterations; i++)
{
    using var b = Open(beforeBytes); using var a = Open(afterBytes);
    long t = Stopwatch.GetTimestamp();
    sa = ApiSurfaceExtractor.Extract(b, ApiSurfaceExtractionScope.Public);
    sb = ApiSurfaceExtractor.Extract(a, ApiSurfaceExtractionScope.Public);
    double ex = Ms(t);
    t = Stopwatch.GetTimestamp();
    var run = ApiRun.Compare(sa, sb, options);
    double cmp = Ms(t);
    if (i == 0) continue;
    extractTimes.Add(ex); compareTimes.Add(cmp); a1 = run;
}
Row("A1", MedianOf(extractTimes) + MedianOf(compareTimes),
    $"extract={MedianOf(extractTimes):F2}\tcompare={MedianOf(compareTimes):F2}\tsurfaceTypes={sa.Types.Count}+{sb.Types.Count}\tchanged={a1.States.Count(kv => kv.Value.State == FastDiffState.Changed)}");

// A1 on declaration-only surfaces (no receiver-contextual extension projection).
{
    var ex = new List<double>(); var cmp = new List<double>(); ApiRun r = default!;
    for (int i = 0; i <= few; i++)
    {
        using var b = Open(beforeBytes); using var a = Open(afterBytes);
        long t = Stopwatch.GetTimestamp();
        var x = ApiSurfaceExtractor.ExtractDeclarations(b, ApiSurfaceExtractionScope.Public);
        var y = ApiSurfaceExtractor.ExtractDeclarations(a, ApiSurfaceExtractionScope.Public);
        double e = Ms(t);
        t = Stopwatch.GetTimestamp();
        var run = ApiRun.Compare(x, y, options);
        double c = Ms(t);
        if (i == 0) continue;
        ex.Add(e); cmp.Add(c); r = run;
    }
    Row("A1(declarations)", MedianOf(ex) + MedianOf(cmp), $"extract={MedianOf(ex):F2}\tcompare={MedianOf(cmp):F2}\tchanged={r.States.Count(kv => kv.Value.State == FastDiffState.Changed)}\tsameVerdicts={r.States.All(kv => a1.States.TryGetValue(kv.Key, out var o) && o.State == kv.Value.State) && a1.States.Count == r.States.Count}");
}

// Whole-Library CompareApi over the A1 surfaces (reference).
double compareApiMs = Median(few, () =>
{
    long t = Stopwatch.GetTimestamp();
    wholeApi = MetadataFindings.CompareApi(sa, sb, new FindingSubject("api", "API"), options);
    return Ms(t);
});
Row("CompareApi(whole Library, surfaces given)", compareApiMs);

// Extraction attribution.
{
    double Ext(Func<PEReader, object?> f) => Median(few, () =>
    {
        using var b = Open(beforeBytes); using var a = Open(afterBytes);
        long t = Stopwatch.GetTimestamp();
        f(b); f(a);
        return Ms(t);
    });
    double typesOnly = Ext(r => ApiSurfaceExtractor.Extract(r, ApiSurfaceExtractionScope.Public, typesOnly: true));
    double declNone = Ext(r => ApiSurfaceExtractor.ExtractDeclarations(r, ApiSurfaceExtractionScope.Public, _ => false));
    double decl = Ext(r => ApiSurfaceExtractor.ExtractDeclarations(r, ApiSurfaceExtractionScope.Public));
    double full = Ext(r => ApiSurfaceExtractor.Extract(r, ApiSurfaceExtractionScope.Public));
    double memSafety = Ext(r => MemorySafetyMetadataIndex.Create(r.GetMetadataReader()));
    double keyed = Median(few, () =>
    {
        long t = Stopwatch.GetTimestamp();
        foreach (var s in new[] { sa, sb })
            foreach (var type in s.Types)
                ApiModel.Keyed(type);
        return Ms(t);
    });
    Console.WriteLine($"attr-api\textract(full)={full:F2}\textract(declarations)={decl:F2}\textensionProjection={full - decl:F2}"
        + $"\tdeclarations(no Type admitted)={declNone:F2}\ttypesOnly={typesOnly:F2}\tmemorySafetyIndex={memSafety:F2}"
        + $"\tcanonicalMemberIdentities(Keyed, both)={keyed:F2}");
}

// ============================================================ A2: census as the API comparison's first stage
var a2Times = new List<(double Total, double Units, double Census, double Model, double Classify)>();
for (int i = 0; i <= iterations; i++)
{
    using var b = Open(beforeBytes); using var a = Open(afterBytes);
    long t = Stopwatch.GetTimestamp();
    var run = CensusApiRun.Run(b, a, options);
    double total = Ms(t);
    if (i == 0) continue;
    a2Times.Add((total, run.UnitsMs, run.CensusMs, run.ModelMs, run.ClassifyMs)); a2 = run;
}
Row("A2", MedianOf(a2Times.Select(x => x.Total)),
    $"units={MedianOf(a2Times.Select(x => x.Units)):F2}\tcensus+classify={MedianOf(a2Times.Select(x => x.Census)):F2}\tof which classify={MedianOf(a2Times.Select(x => x.Classify)):F2}\tmodel={MedianOf(a2Times.Select(x => x.Model)):F2}"
    + $"\tmodelTypes={a2.ModelTypes}\tsurfaceTypesBuilt={a2.Extracted}\tpresentedDecided={a2.Presented}");

// ============================================================ Today's complete diffs
double todayApiMs = Median(few, () =>
{
    long t = Stopwatch.GetTimestamp();
    using var b = Open(beforeBytes); using var a = Open(afterBytes);
    MetadataFindings.CompareApi(
        ApiSurfaceExtractor.Extract(b, ApiSurfaceExtractionScope.Public),
        ApiSurfaceExtractor.Extract(a, ApiSurfaceExtractionScope.Public),
        new FindingSubject("api", "API"), options);
    return Ms(t);
});
IlAssemblyDiffResult todayIl = default!;
double todayIlMs = Median(few, () =>
{
    long t = Stopwatch.GetTimestamp();
    todayIl = IlAssemblyDiff.CompareStreams(
        new MemoryStream(beforeBytes, writable: false), beforePath,
        new MemoryStream(afterBytes, writable: false), afterPath,
        maxExamples: 0, normalization: Policy).Diff;
    return Ms(t);
});
Row("today-complete", todayApiMs + todayIlMs, $"api={todayApiMs:F2}\til={todayIlMs:F2}\tcomparedBodies={todayIl.ComparedBodyCount}");
}

if (!correctness)
    return 0;

// ============================================================ Correctness
using var pb = Open(beforeBytes);
using var pa = Open(afterBytes);
var oracleSession = new IlLibraryExistsSession(pb, pa, Policy, liteDecode: false);
var bodyOracle = BodyRun.Oracle(oracleSession, out var pairStats);
Console.WriteLine($"gate-method-pairs\tpairs={pairStats.Pairs}\texistsVsCompare-agree={pairStats.Agree}\tdisagree={pairStats.Disagree}"
    + $"\tliteVsCompare-disagree={pairStats.LiteDisagree}\tcalliChanged={pairStats.CalliOnly}");
foreach (var e in pairStats.Examples.Take(10))
    Console.WriteLine($"  pair-disagree\t{e}");

var fastById = fast.Types.GroupBy(t => t.Identifier).ToDictionary(g => g.Key, g => g.First());
var fastApiByName = fast.Types.GroupBy(t => t.FullName).ToDictionary(g => g.Key, g => g.First());

// ---- Partition
{
    var mine = b1.Run.States.Keys.ToHashSet(StringComparer.Ordinal);
    var theirs = fast.Types.Select(t => t.Identifier).ToHashSet(StringComparer.Ordinal);
    Console.WriteLine($"partition\tilUnits={mine.Count}\tfastTypes={theirs.Count}\tonlyIl={mine.Count(x => !theirs.Contains(x))}\tonlyFast={theirs.Count(x => !mine.Contains(x))}"
        + $"\tunreportedMethods(old/new)={oracleSession.UnreportedOldMethods}/{oracleSession.UnreportedNewMethods}\tidentityFailures={oracleSession.IdentityFailures}"
        + $"\tduplicateMethodUnits={oracleSession.Units.Count(u => u.DuplicateMethod)}\tduplicateRowUnits={oracleSession.Units.Count(u => u.DuplicateRow)}");
    foreach (var x in mine.Where(x => !theirs.Contains(x)).Take(5)) Console.WriteLine($"  only-il\t{x}");
    foreach (var x in theirs.Where(x => !mine.Contains(x)).Take(5)) Console.WriteLine($"  only-fast\t{x}");
}

// ---- Body verdicts
void BodyAgreement(string label, BodyRun run)
{
    var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var classes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var calliTypes = new List<string>();
    var lines = new List<string>();
    foreach (var (id, v) in run.States)
    {
        var o = bodyOracle.States[id];
        string f = fastById.TryGetValue(id, out var fs) ? S(fs.Body) : "absent";
        string key = $"b1={S(v.State)} oracle={S(o.State)} fast={f}";
        counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
        if (v.State != o.State || f != S(v.State))
        {
            string? fastReason = fs is null ? null : FastDiff.Explain(pb, pa, fs.FullName).Body;
            string cls = Classify(v.State, o, fs?.Body, fastReason);
            classes[cls] = classes.TryGetValue(cls, out int k) ? k + 1 : 1;
            lines.Add($"  {label}-diff\t{cls}\t{id}\t{key}\tb1: {v.Reason}\toracle: {o.Reason}\tfast: {fastReason?.Replace('\n', ' ')}");
        }
        if (o.Reason?.StartsWith("IL(calli only)", StringComparison.Ordinal) == true)
            calliTypes.Add(id + " b1=" + S(v.State) + " fast=" + f);
    }
    foreach (var (k, c) in classes) Console.WriteLine($"class-{label}\t{k}\t{c}");
    foreach (var c in calliTypes) Console.WriteLine($"calli-{label}\t{c}");
    foreach (var (k, c) in counts) Console.WriteLine($"agree-{label}\t{k}\t{c}");
    int vsOracle = run.States.Count(kv => kv.Value.State != bodyOracle.States[kv.Key].State);
    int vsFast = run.States.Count(kv => fastById.TryGetValue(kv.Key, out var fs) && fs.Body != kv.Value.State);
    Console.WriteLine($"gate-{label}\tunits={run.States.Count}\tdisagreeOracle={vsOracle}\tdisagreeFast={vsFast}");
    foreach (var l in lines.Take(detail ? int.MaxValue : 40)) Console.WriteLine(l);
}
static string Classify(FastDiffState mine, BodyVerdict oracle, FastDiffState? fast, string? fastReason)
{
    if (mine != oracle.State)
        return "B1-vs-oracle";
    if (fast is null)
        return "partition";
    if (mine == FastDiffState.Changed && fast == FastDiffState.Unchanged)
        return oracle.Reason is { } r && (r.StartsWith("method with body", StringComparison.Ordinal) || r.StartsWith("body on one side", StringComparison.Ordinal))
            ? "#10223 one-sided method body not counted by Fast Diff"
            : oracle.Reason?.StartsWith("IL(calli only)", StringComparison.Ordinal) == true
                ? "#9801 calli stand-alone signature (complete IL diff over-reports; B1 inherits it)"
                : "fast-miss(other)";
    if (mine == FastDiffState.Unchanged && fast == FastDiffState.Changed)
    {
        string fr = fastReason ?? "";
        string head = Bucket(fr);
        if (head.StartsWith("IL", StringComparison.Ordinal) || head.Contains("body", StringComparison.OrdinalIgnoreCase))
            return "#10196 Fast Diff raw walker: encoding-level body fact (" + Bucket(fr) + ")";
        return "#10196 Fast Diff Body census: non-IL fact (" + Bucket(fr) + ")";
    }
    return "other";
}
static string Bucket(string reason)
{
    // Fast Diff explains a census fact as "- Kind (...)" / "+ Kind (...)", and IL by method.
    string r = reason.Replace('\n', ' ').TrimStart('-', '+', ' ');
    int cut = r.IndexOfAny([' ', ':', '(']);
    return cut > 0 ? r[..cut] : r;
}

if (!q1)
{
    BodyAgreement("body-B1", b1.Run);
    BodyAgreement("body-B1lite", b1Lite.Run);
}
BodyAgreement("body-B1opt", b1Opt.Run);
BodyAgreement("body-B2", b2.Run);
{
    // B2 pairing against the complete IL diff's text pairing, handle for handle, and the
    // symbolic comparer against IlBodyDiff.Compare on every symbolic pair.
    IlBodyDiff.SpikeFamilyCache = true;
    var sym = new IlSymbolicExistsSession(pb, pa, Policy);
    static int RowOf(MethodDefinitionHandle h) => MetadataTokens.GetRowNumber(h);
    var textPairs = new HashSet<(int, int)>(); var textOld = new HashSet<int>(); var textNew = new HashSet<int>();
    foreach (var unit in oracleSession.Units)
    {
        foreach (var key in unit.OldMethods)
        {
            if (oracleSession.TryNewHandle(key, out var nh)) textPairs.Add((RowOf(oracleSession.OldHandle(key)), RowOf(nh)));
            else textOld.Add(RowOf(oracleSession.OldHandle(key)));
        }
        foreach (var key in unit.NewMethods)
            if (!oracleSession.TryOldHandle(key, out _)) textNew.Add(RowOf(oracleSession.NewHandle(key)));
    }
    var symPairs = new HashSet<(int, int)>(); var symOld = new HashSet<int>(); var symNew = new HashSet<int>();
    foreach (var unit in sym.Units)
    {
        foreach (var key in unit.OldMethods)
        {
            if (sym.TryNewHandle(key, out var nh)) symPairs.Add((RowOf(sym.OldHandle(key)), RowOf(nh)));
            else symOld.Add(RowOf(sym.OldHandle(key)));
        }
        foreach (var key in unit.NewMethods)
            if (!sym.TryOldHandle(key, out _)) symNew.Add(RowOf(sym.NewHandle(key)));
    }
    Console.WriteLine($"gate-b2-map\ttextPairs={textPairs.Count}\tsymPairs={symPairs.Count}\tpairsOnlyText={textPairs.Count(p => !symPairs.Contains(p))}"
        + $"\tpairsOnlySym={symPairs.Count(p => !textPairs.Contains(p))}\toldOneSided(text/sym)={textOld.Count}/{symOld.Count}\tnewOneSided(text/sym)={textNew.Count}/{symNew.Count}"
        + $"\toneSidedEqual={textOld.SetEquals(symOld) && textNew.SetEquals(symNew)}\tidentityFailures(text/sym)={oracleSession.IdentityFailures}/{sym.IdentityFailures}"
        + $"\tunits(text/sym)={oracleSession.Units.Count}/{sym.Units.Count}");
    foreach (var p in textPairs.Where(p => !symPairs.Contains(p)).Take(5)) Console.WriteLine($"  b2-map-only-text\t{p}");
    foreach (var p in symPairs.Where(p => !textPairs.Contains(p)).Take(5)) Console.WriteLine($"  b2-map-only-sym\t{p}");
    int pairs = 0, disagree = 0;
    foreach (var unit in sym.Units)
        foreach (var key in unit.OldMethods)
        {
            if (!sym.TryNewHandle(key, out var nh)) continue;
            var om = pb.GetMetadataReader().GetMethodDefinition(sym.OldHandle(key));
            var nm = pa.GetMetadataReader().GetMethodDefinition(nh);
            if (om.RelativeVirtualAddress == 0 || nm.RelativeVirtualAddress == 0) continue;
            var ob = pb.GetMethodBody(om.RelativeVirtualAddress);
            var nb = pa.GetMethodBody(nm.RelativeVirtualAddress);
            var exists = sym.Comparer.Compare(ob, nb, rawFastPath: false, memoTokens: true, liteDecode: false, out _, out _);
            var expected = pairStats.Expected.TryGetValue((RowOf(sym.OldHandle(key)), RowOf(nh)), out var e)
                ? e
                : IlBodyDiff.Compare(sym.OldReader, ob, sym.NewReader, nb, Policy) is var d && !d.IsAvailable ? IlExistsOutcome.Unavailable
                    : d.IsExact ? IlExistsOutcome.Exact : IlExistsOutcome.Changed;
            pairs++;
            if (exists != expected)
            {
                disagree++;
                if (disagree <= 10) Console.WriteLine($"  b2-pair-disagree\t{sym.Table[key.Item1]}\tb2={exists}\tcompare={expected}");
            }
        }
    Console.WriteLine($"gate-b2-pairs\tpairs={pairs}\tb2VsCompare-disagree={disagree}");
    IlBodyDiff.SpikeFamilyCache = false;
}
{
    // Optimized comparer per method pair, checked completeness on every body, and map memo equality.
    IlBodyDiff.SpikeFamilyCache = true;
    var opt = new IlLibraryExistsSession(pb, pa, Policy, liteDecode: false, IlSessionSpikeOptions.All);
    int pairs = 0, disagree = 0, completeness = 0, bodies = 0;
    foreach (var unit in opt.Units)
        foreach (var key in unit.OldMethods)
        {
            var om = opt.OldReader.GetMethodDefinition(opt.OldHandle(key));
            if (om.RelativeVirtualAddress != 0)
            {
                bodies++;
                if (!opt.Comparer.CheckedCompletenessAgrees(opt.OldPe.GetMethodBody(om.RelativeVirtualAddress))) completeness++;
            }
            if (!opt.TryNewHandle(key, out var nh)) continue;
            var nm = opt.NewReader.GetMethodDefinition(nh);
            if (nm.RelativeVirtualAddress != 0)
            {
                bodies++;
                if (!opt.Comparer.CheckedCompletenessAgrees(opt.NewPe.GetMethodBody(nm.RelativeVirtualAddress))) completeness++;
            }
            if (om.RelativeVirtualAddress == 0 || nm.RelativeVirtualAddress == 0) continue;
            var ob = opt.OldPe.GetMethodBody(om.RelativeVirtualAddress);
            var nb = opt.NewPe.GetMethodBody(nm.RelativeVirtualAddress);
            var exists = opt.Comparer.Compare(ob, nb, rawFastPath: false, memoTokens: true, liteDecode: false, out _, out _);
            var diff = IlBodyDiff.Compare(opt.OldReader, ob, opt.NewReader, nb, Policy);
            var expected = !diff.IsAvailable ? IlExistsOutcome.Unavailable : diff.IsExact ? IlExistsOutcome.Exact : IlExistsOutcome.Changed;
            pairs++;
            if (exists != expected) disagree++;
        }
    IlBodyDiff.SpikeFamilyCache = false;
    var plain = new IlLibraryExistsSession(pb, pa, Policy, liteDecode: false);
    bool mapsEqual = plain.Units.Count == opt.Units.Count
        && plain.Units.Zip(opt.Units).All(p => p.First.Identity == p.Second.Identity
            && p.First.OldMethods.SequenceEqual(p.Second.OldMethods) && p.First.NewMethods.SequenceEqual(p.Second.NewMethods));
    Console.WriteLine($"gate-opt\tpairs={pairs}\toptVsCompare-disagree={disagree}\tbodies={bodies}\tcheckedCompleteness-disagree={completeness}\tmapMemoEqual={mapsEqual}");
}
if (!q1)
{
    int changed = b1Ord.Run.States.Count(kv => kv.Value.State != b1.Run.States[kv.Key].State);
    Console.WriteLine($"ordinals\tunitsWhoseVerdictMoves={changed}");
    foreach (var kv in b1Ord.Run.States.Where(kv => kv.Value.State != b1.Run.States[kv.Key].State).Take(10))
        Console.WriteLine($"  ordinal-move\t{kv.Key}\t{S(b1.Run.States[kv.Key].State)}->{S(kv.Value.State)}");
}

if (q1)
    return 0;

// ---- API verdicts
var apiOracle = new HashSet<string>(StringComparer.Ordinal);
var apiOracleWhole = new HashSet<string>(StringComparer.Ordinal);
foreach (TypeDiff type in wholeApi.ApiDiff.TypeDiffs)
    apiOracle.Add(type.TypeFullName);
if (wholeApi.Types.Value is FindingComparison<ApiTypeHandle>.Complete oracleTypes)
    foreach (var pair in oracleTypes.Pairs)
        if (pair.Kind != PairKind.Present)
            apiOracle.Add(Payload(pair).TypeFullName);
if (wholeApi.Members.Value is FindingComparison<ApiMemberHandle>.Complete oracleMembers)
    foreach (var pair in oracleMembers.Pairs)
        if (pair.Kind != PairKind.Present)
            apiOracle.Add(Payload(pair).TypeFullName);

void ApiAgreement(string label, Dictionary<string, ApiVerdict> states)
{
    var names = states.Keys.Concat(apiOracle).Concat(fastApiByName.Keys).ToHashSet(StringComparer.Ordinal);
    var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var lines = new List<string>();
    int vsOracle = 0, vsFast = 0;
    foreach (var name in names)
    {
        var mine = states.TryGetValue(name, out var v) ? v.State : FastDiffState.Unchanged;
        bool oracle = apiOracle.Contains(name);
        string f = fastApiByName.TryGetValue(name, out var fs) ? S(fs.Api) : "absent";
        string key = $"{label}={S(mine)} oracle={(oracle ? "C" : "U")} fast={f}";
        counts[key] = counts.TryGetValue(key, out int c) ? c + 1 : 1;
        bool dOracle = (mine == FastDiffState.Changed) != oracle;
        bool dFast = f != "absent" && f != S(mine);
        if (dOracle) vsOracle++;
        if (dFast) vsFast++;
        if (dOracle || dFast)
            lines.Add($"  {label}-diff\t{name}\t{key}\t{v?.Reason}");
    }
    foreach (var (k, c) in counts) Console.WriteLine($"agree-{label}\t{k}\t{c}");
    Console.WriteLine($"gate-{label}\ttypes={names.Count}\tdisagreeOracle={vsOracle}\tdisagreeFast={vsFast}");
    foreach (var l in lines.Take(detail ? int.MaxValue : 40)) Console.WriteLine(l);
}
ApiAgreement("api-A1", a1.States);
ApiAgreement("api-A2", a2.States);
return 0;

static string S(FastDiffState s) => s switch
{
    FastDiffState.Changed => "C",
    FastDiffState.Unchanged => "U",
    FastDiffState.Indeterminate => "I",
    _ => s.ToString(),
};
static T Payload<T>(PairFinding<T> pair) where T : notnull
    => (((IPairFinding)pair).New as Finding<T> ?? (Finding<T>)((IPairFinding)pair).Old!).Payload;

sealed class BodyVerdict
{
    public FastDiffState State;
    public string? Reason;
}

sealed class PairStats
{
    public int Pairs, Agree, Disagree, LiteDisagree, CalliOnly;
    public List<string> Examples { get; } = [];
    /// <summary>IlBodyDiff.Compare's outcome per (old row, new row) method pair.</summary>
    public Dictionary<(int, int), IlExistsOutcome> Expected { get; } = [];
}

sealed class BodyRun
{
    public Dictionary<string, BodyVerdict> States { get; } = new(StringComparer.Ordinal);
    public long Bodies;
    public IlLibraryExistsSession.SessionTimes SessionTimes = new();

    static FastDiffState Map(IlExistsState s) => s switch
    {
        IlExistsState.Changed => FastDiffState.Changed,
        IlExistsState.Unchanged => FastDiffState.Unchanged,
        _ => FastDiffState.Indeterminate,
    };

    /// <summary>The Fast Diff Identifier spelling (escaped Outer+Inner) of a unit, for joining.</summary>
    public static string Identifier(IlLibraryExistsSession session, IlLibraryExistsSession.Unit unit)
    {
        var (reader, handle) = unit.OldPresent ? (session.OldReader, unit.Old) : (session.NewReader, unit.New);
        return Identifier(reader, handle, unit.Identity);
    }

    public static string Identifier(IlSymbolicExistsSession session, IlSymbolicExistsSession.Unit unit)
    {
        var (reader, handle) = unit.OldPresent ? (session.OldReader, unit.Old) : (session.NewReader, unit.New);
        return Identifier(reader, handle, unit.Identity);
    }

    public static BodyRun Walk(IlSymbolicExistsSession session)
    {
        var run = new BodyRun();
        foreach (var unit in session.Units)
        {
            var state = session.CompareType(unit, out string? reason);
            run.States[Identifier(session, unit)] = new BodyVerdict { State = Map(state), Reason = reason };
        }
        run.Bodies = session.BodiesCompared;
        return run;
    }

    public static string Identifier(MetadataReader reader, TypeDefinitionHandle handle, string identity)
    {
        var segments = ImmutableArray.CreateBuilder<string>();
        var current = handle;
        TypeDefinition root;
        while (true)
        {
            var td = reader.GetTypeDefinition(current);
            segments.Insert(0, reader.GetString(td.Name));
            var parent = td.GetDeclaringType();
            if (parent.IsNil) { root = td; break; }
            current = parent;
        }
        string ns = root.Namespace.IsNil ? "" : reader.GetString(root.Namespace);
        return MetadataTypeDefinitionName.Create(ns, segments.ToImmutable()) is MetadataTypeDefinitionNameResult.Valid { Name: var name }
            ? name.ToEscapedFullName()
            : "!" + identity;
    }

    public static BodyRun Walk(IlLibraryExistsSession session)
    {
        var run = new BodyRun();
        foreach (var unit in session.Units)
        {
            var state = session.CompareType(unit, out string? reason);
            run.States[Identifier(session, unit)] = new BodyVerdict { State = Map(state), Reason = reason };
        }
        run.Bodies = session.BodiesCompared;
        return run;
    }

    /// <summary>
    /// The complete IL diff run to completion per method pair (IlBodyDiff.Compare,
    /// LCS and all) under the same partition and Body contract. Also gates the
    /// Exists comparer against IlBodyDiff.Compare on every paired body.
    /// </summary>
    public static BodyRun Oracle(IlLibraryExistsSession session, out PairStats stats)
    {
        stats = new PairStats();
        var run = new BodyRun();
        var lite = new IlBodyExistsComparer(session.OldReader, session.NewReader, session.Normalization);
        var full = session.Comparer;
        foreach (var unit in session.Units)
        {
            var v = new BodyVerdict { State = FastDiffState.Unchanged };
            run.States[Identifier(session, unit)] = v;
            void Set(FastDiffState s, string r)
            {
                if (v.State == FastDiffState.Changed) return;
                if (s == FastDiffState.Indeterminate && v.State == FastDiffState.Indeterminate) return;
                v.State = s; v.Reason = r;
            }
            if (unit.DuplicateRow) { Set(FastDiffState.Indeterminate, "duplicate Type rows"); continue; }
            if (!unit.OldPresent || !unit.NewPresent) { Set(FastDiffState.Changed, "presence"); continue; }
            if (unit.IdentityFailure) Set(FastDiffState.Indeterminate, "method identity failure");
            foreach (var key in unit.OldMethods)
                if (!session.TryNewHandle(key, out _) && session.OldReader.GetMethodDefinition(session.OldHandle(key)).RelativeVirtualAddress != 0)
                    Set(FastDiffState.Changed, "method with body on old side only: " + key.Identity);
            foreach (var key in unit.NewMethods)
                if (!session.TryOldHandle(key, out _) && session.NewReader.GetMethodDefinition(session.NewHandle(key)).RelativeVirtualAddress != 0)
                    Set(FastDiffState.Changed, "method with body on new side only: " + key.Identity);
            foreach (var key in unit.OldMethods)
            {
                if (!session.TryNewHandle(key, out var nh)) continue;
                var om = session.OldReader.GetMethodDefinition(session.OldHandle(key));
                var nm = session.NewReader.GetMethodDefinition(nh);
                if (om.RelativeVirtualAddress == 0 && nm.RelativeVirtualAddress == 0) continue;
                if (om.RelativeVirtualAddress == 0 || nm.RelativeVirtualAddress == 0) { Set(FastDiffState.Changed, "body on one side: " + key.Identity); continue; }
                MethodBodyBlock ob, nb;
                try
                {
                    ob = session.OldPe.GetMethodBody(om.RelativeVirtualAddress);
                    nb = session.NewPe.GetMethodBody(nm.RelativeVirtualAddress);
                }
                catch (BadImageFormatException) { Set(FastDiffState.Indeterminate, "body read failed"); continue; }
                var diff = IlBodyDiff.Compare(session.OldReader, ob, session.NewReader, nb, session.Normalization);
                stats.Pairs++;
                var exists = full.Compare(ob, nb, rawFastPath: false, memoTokens: true, liteDecode: false, out _, out _);
                var existsLite = lite.Compare(ob, nb, rawFastPath: false, memoTokens: true, liteDecode: true, out _, out _);
                var expected = !diff.IsAvailable ? IlExistsOutcome.Unavailable : diff.IsExact ? IlExistsOutcome.Exact : IlExistsOutcome.Changed;
                stats.Expected[(MetadataTokens.GetRowNumber(session.OldHandle(key)), MetadataTokens.GetRowNumber(nh))] = expected;
                if (exists == expected) stats.Agree++;
                else { stats.Disagree++; if (stats.Examples.Count < 20) stats.Examples.Add($"{key.Identity} exists={exists} compare={expected}"); }
                if (existsLite != expected) stats.LiteDisagree++;
                if (!diff.IsAvailable) { Set(FastDiffState.Indeterminate, "IL unavailable: " + diff.Failure); continue; }
                if (!diff.IsExact)
                {
                    bool calli = diff.Rows.All(r => r.Operation.OpcodeFamily == "calli");
                    if (calli) stats.CalliOnly++;
                    Set(FastDiffState.Changed, (calli ? "IL(calli only): " : "IL: ") + key.Identity);
                }
            }
        }
        return run;
    }
}

sealed class ApiVerdict
{
    public FastDiffState State;
    public string? Reason;
}

sealed class ApiRun
{
    public Dictionary<string, ApiVerdict> States { get; } = new(StringComparer.Ordinal);

    /// <summary>Per Type pair (by ApiType.FullName), the complete comparison's own checks with early exit.</summary>
    public static ApiRun Compare(ApiSurface before, ApiSurface after, ApiDiffOptions options)
    {
        var run = new ApiRun();
        var a = new Dictionary<string, ApiType>(before.Types.Count, StringComparer.Ordinal);
        foreach (var t in before.Types) a.TryAdd(t.FullName, t);
        var b = new Dictionary<string, ApiType>(after.Types.Count, StringComparer.Ordinal);
        foreach (var t in after.Types) b.TryAdd(t.FullName, t);
        foreach (var (name, ta) in a)
        {
            if (!b.TryGetValue(name, out var tb))
            {
                run.States[name] = new ApiVerdict { State = FastDiffState.Changed, Reason = "type removed" };
                continue;
            }
            string? reason = ApiModel.Compare(ta, tb, options, stopAtFirst: true, out _);
            run.States[name] = new ApiVerdict { State = reason is null ? FastDiffState.Unchanged : FastDiffState.Changed, Reason = reason };
        }
        foreach (var (name, _) in b)
            if (!a.ContainsKey(name))
                run.States[name] = new ApiVerdict { State = FastDiffState.Changed, Reason = "type added" };
        return run;
    }
}

/// <summary>A2: Fast Diff's census as the API comparison's first stage, then the model for uncertain Types.</summary>
sealed class CensusApiRun
{
    public Dictionary<string, ApiVerdict> States { get; } = new(StringComparer.Ordinal);
    public double UnitsMs, CensusMs, ModelMs, ClassifyMs;
    public int ModelTypes, Extracted, Presented;

    static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    public static CensusApiRun Run(PEReader before, PEReader after, ApiDiffOptions options)
    {
        var run = new CensusApiRun();
        long t = Stopwatch.GetTimestamp();
        var a = new FastDiff.Side(before);
        var b = new FastDiff.Side(after);
        var unitsA = a.Units();
        var unitsB = b.Units();
        var joined = new List<(FastDiff.Unit? A, FastDiff.Unit? B)>(unitsA.Count + 8);
        foreach (var (k, u) in unitsA) joined.Add((u, unitsB.TryGetValue(k, out var v) ? v : null));
        foreach (var (k, u) in unitsB) if (!unitsA.ContainsKey(k)) joined.Add((null, u));
        run.UnitsMs = Ms(t);

        t = Stopwatch.GetTimestamp();
        var tierB = new List<(string Name, FastDiff.Unit? A, FastDiff.Unit? B)>();
        foreach (var (ua, ub) in joined)
        {
            var u = (ua ?? ub)!;
            var v = new ApiVerdict { State = FastDiffState.Unchanged };
            run.States[u.FullName] = v;
            if (ua?.Malformed == true || ub?.Malformed == true) { v.State = FastDiffState.Indeterminate; continue; }
            if (ua is null || ub is null)
            {
                bool visible = ua is not null ? a.IsVisible(ua.Owner) : b.IsVisible(ub!.Owner);
                if (!visible) continue;
                if ((ua is not null ? Census.Admission.Of(a, ua) : Census.Admission.Of(b, ub!)).TypeAdmitted)
                {
                    v.State = FastDiffState.Changed; v.Reason = "presented: type-presence"; run.Presented++;
                }
                else tierB.Add((u.FullName, ua, ub));
                continue;
            }
            try
            {
                var ca = a.ApiCensus(ua);
                var cb = b.ApiCensus(ub);
                if (ca.SequenceEqual(cb)) continue;
                long tc = Stopwatch.GetTimestamp();
                var (presented, _) = Census.Classify(ca, cb, Census.Admission.Of(a, ua), Census.Admission.Of(b, ub));
                run.ClassifyMs += Ms(tc);
                if (presented.Count > 0)
                {
                    v.State = FastDiffState.Changed; v.Reason = "presented: " + string.Join(",", presented); run.Presented++;
                    continue;
                }
                tierB.Add((u.FullName, ua, ub));
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                a.Keys.Reset(); b.Keys.Reset();
                v.State = FastDiffState.Indeterminate; v.Reason = "census: " + ex.Message;
            }
        }
        run.ModelTypes = tierB.Count;
        run.CensusMs = Ms(t);

        t = Stopwatch.GetTimestamp();
        var handlesA = tierB.Where(x => x.A is not null).Select(x => x.A!.Owner).ToHashSet();
        var handlesB = tierB.Where(x => x.B is not null).Select(x => x.B!.Owner).ToHashSet();
        ApiSurface surfaceA = handlesA.Count == 0 ? new ApiSurface()
            : ApiSurfaceExtractor.ExtractDeclarations(before, ApiSurfaceExtractionScope.Public, h => handlesA.Contains(h));
        ApiSurface surfaceB = handlesB.Count == 0 ? new ApiSurface()
            : ApiSurfaceExtractor.ExtractDeclarations(after, ApiSurfaceExtractionScope.Public, h => handlesB.Contains(h));
        run.Extracted = handlesA.Count + handlesB.Count;
        var byTokenA = surfaceA.Types.Where(x => x.MetadataToken is not null).GroupBy(x => x.MetadataToken!.Value).ToDictionary(g => g.Key, g => g.First());
        var byTokenB = surfaceB.Types.Where(x => x.MetadataToken is not null).GroupBy(x => x.MetadataToken!.Value).ToDictionary(g => g.Key, g => g.First());
        foreach (var (name, ua, ub) in tierB)
        {
            var v = run.States[name];
            ApiType? ta = ua is not null && byTokenA.TryGetValue(MetadataTokens.GetToken(ua.Owner), out var x1) ? x1 : null;
            ApiType? tb = ub is not null && byTokenB.TryGetValue(MetadataTokens.GetToken(ub.Owner), out var x2) ? x2 : null;
            if (ta is null && tb is null) continue;
            if (ta is null || tb is null) { v.State = FastDiffState.Changed; v.Reason = "public on one side only"; continue; }
            var reason = ApiModel.Compare(ta, tb, options, stopAtFirst: true, out _);
            if (reason is not null) { v.State = FastDiffState.Changed; v.Reason = reason; }
        }
        run.ModelMs = Ms(t);
        return run;
    }
}

/// <summary>The complete API comparison's checks on one Type pair (copied from measure-shared-predicate.cs).</summary>
static class ApiModel
{
    public static string MemberKey(ApiMemberHandle h) => h.CanonicalSignature ?? h.Identity;

    static bool IsDeclared(ApiMember m)
        => m.DeclaringTypeCanonicalName is null && m.DeclaringTypeDefinitionName is null && m.DeclaringOverloadIndex is null;

    public static Dictionary<string, ApiMemberHandle> Keyed(ApiType type)
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

    public static string? Compare(ApiType bt, ApiType at, ApiDiffOptions options, bool stopAtFirst, out List<string> changedMembers)
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
            changedMembers.Add(k);
            first ??= reason;
            if (stopAtFirst)
                return first;
        }
        foreach (var k in old.Keys)
        {
            if (@new.ContainsKey(k))
                continue;
            changedMembers.Add(k);
            first ??= "member removed " + k;
            if (stopAtFirst)
                return first;
        }
        return first;
    }
}

// ---- Census (copied verbatim from eng/measure-census-classes.cs, exp/staged-diff-spike 87f53a7cd).
static class Census
{
    public static readonly string[] Categories =
    [
        "type-presence", "visibility",
        "member-presence:method", "member-presence:field", "member-presence:property", "member-presence:event",
        "method-flags:access", "method-flags:static", "method-flags:virtual-shape", "method-flags:other", "method-implflags",
        "field-flags:access", "field-flags:static-readonly-const", "field-flags:other", "field-constant", "field-offset",
        "param-name", "param-flags:out", "param-flags:other", "param-default", "param-presence", "param-presence:return",
        "property-flags", "property-constant", "property-accessors",
        "event-flags", "event-accessors",
        "type-flags:visibility-kind-abstract-sealed", "type-flags:other", "type-base",
        "interface", "genparam-name", "genparam-flags", "genparam-presence", "constraint",
        "method-impl", "attribute", "nullable-context", "layout", "import", "other", "hidden-type", "hidden-member",
    ];

    static readonly HashSet<string> Uncertain =
    [
        "method-flags:other", "method-implflags", "field-flags:other", "field-offset", "param-flags:other",
        "param-presence:return", "property-flags", "event-flags", "type-flags:other",
        "attribute", "nullable-context", "layout", "import", "other", "hidden-type", "hidden-member",
        .. (Environment.GetEnvironmentVariable("DEMOTE") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries),
    ];

    public static bool IsPresented(string category) => !Uncertain.Contains(category);

    readonly record struct Part(SymbolPart Tag, long Int, byte[]? Payload)
    {
        public bool Same(Part o) => Tag == o.Tag && Int == o.Int
            && (Payload is null ? o.Payload is null : o.Payload is not null && Payload.AsSpan().SequenceEqual(o.Payload));
    }

    static List<Part> Parse(ReadOnlySpan<byte> bytes)
    {
        var parts = new List<Part>();
        int p = 0;
        while (p < bytes.Length)
        {
            var tag = (SymbolPart)bytes[p++];
            switch (tag)
            {
                case SymbolPart.Int:
                    ulong z = Varint(bytes, ref p);
                    parts.Add(new(tag, (long)(z >> 1) ^ -(long)(z & 1), null));
                    break;
                case SymbolPart.Name or SymbolPart.Key or SymbolPart.Blob:
                    int len = (int)Varint(bytes, ref p);
                    parts.Add(new(tag, 0, bytes.Slice(p, len).ToArray()));
                    p += len;
                    break;
                default:
                    parts.Add(new(tag, 0, null));
                    break;
            }
        }
        return parts;
    }

    static ulong Varint(ReadOnlySpan<byte> bytes, ref int p)
    {
        ulong v = 0;
        for (int shift = 0; ; shift += 7)
        {
            byte b = bytes[p++];
            v |= (ulong)(b & 0x7F) << shift;
            if (b < 0x80)
                return v;
        }
    }

    sealed record Fact(SymbolPart Kind, string Subject, List<Part> Parts);

    static Fact ToFact(SymbolKey key)
    {
        var parts = Parse(key.Bytes);
        string subject = parts.Count > 1 && parts[1].Tag == SymbolPart.Key ? Convert.ToHexString(parts[1].Payload!) : "";
        return new(parts[0].Tag, subject, parts);
    }

    static string MemberKind(SymbolPart k) => k switch
    {
        SymbolPart.MethodFact => "method",
        SymbolPart.FieldFact => "field",
        SymbolPart.PropertyFact => "property",
        SymbolPart.EventFact => "event",
        _ => "",
    };

    public static (List<string> Presented, List<string> Uncertain) Classify(List<SymbolKey> a, List<SymbolKey> b)
        => Classify(a, b, null, null);

    /// <summary>
    /// Admission: the API surface omits hidden Types (EditorBrowsable(Never), Obsolete; AttributeReader.HasHiddenAttribute),
    /// hidden members (EditorBrowsable(Never)) and compiler-named members. A presented difference counts only for an
    /// admitted subject; otherwise it is the uncertain class "hidden".
    /// </summary>
    public sealed class Admission
    {
        public bool TypeAdmitted = true;
        public HashSet<string> Hidden { get; } = new(StringComparer.Ordinal); // "<kind>:<name hex>"

        public static Admission Of(FastDiff.Side side, FastDiff.Unit unit)
        {
            var md = side.Md;
            var adm = new Admission();
            var type = md.GetTypeDefinition(unit.Owner);
            adm.TypeAdmitted = !AttributeReader.HasHiddenAttribute(md, type.GetCustomAttributes());
            void Add(string kind, StringHandle name, CustomAttributeHandleCollection attrs)
            {
                if (md.StringComparer.StartsWith(name, "<") || AttributeReader.HasEditorBrowsableNeverAttribute(md, attrs))
                    adm.Hidden.Add(kind + ":" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(md.GetString(name))));
            }
            foreach (var h in type.GetMethods()) { var m = md.GetMethodDefinition(h); Add("Method", m.Name, m.GetCustomAttributes()); }
            foreach (var h in type.GetFields()) { var f = md.GetFieldDefinition(h); Add("Field", f.Name, f.GetCustomAttributes()); }
            foreach (var h in type.GetProperties()) { var x = md.GetPropertyDefinition(h); Add("Property", x.Name, x.GetCustomAttributes()); }
            foreach (var h in type.GetEvents()) { var x = md.GetEventDefinition(h); Add("Event", x.Name, x.GetCustomAttributes()); }
            return adm;
        }

        /// <summary>Whether the member a subject key names is admitted; non-member subjects are admitted.</summary>
        public bool Admits(byte[]? subject)
        {
            if (subject is null || subject.Length == 0)
                return true;
            var parts = Parse(subject);
            if (parts[0].Tag == SymbolPart.Parameter && parts.Count > 1 && parts[1].Payload is { } method)
                return Admits(method);
            if (parts[0].Tag is SymbolPart.Method or SymbolPart.Field or SymbolPart.Property or SymbolPart.Event
                && parts.Count > 2 && parts[2].Tag == SymbolPart.Name)
                return !Hidden.Contains(parts[0].Tag + ":" + Convert.ToHexString(parts[2].Payload!));
            return true;
        }
    }

    public static (List<string> Presented, List<string> Uncertain) Classify(List<SymbolKey> a, List<SymbolKey> b, Admission? admA, Admission? admB)
    {
        if ((admA is not null && !admA.TypeAdmitted) || (admB is not null && !admB.TypeAdmitted))
            return ([], ["hidden-type"]);
        var setA = a.ToHashSet();
        var setB = b.ToHashSet();
        var cats = new List<string>();
        var factsA = a.Select(ToFact).ToList();
        var factsB = b.Select(ToFact).ToList();
        var bySubjectA = factsA.GroupBy(f => (f.Kind, f.Subject)).ToDictionary(g => g.Key, g => g.ToList());
        var bySubjectB = factsB.GroupBy(f => (f.Kind, f.Subject)).ToDictionary(g => g.Key, g => g.ToList());
        var seen = new HashSet<(SymbolPart, string)>();
        for (int side = 0; side < 2; side++)
        {
            var mine = side == 0 ? a : b;
            var other = side == 0 ? setB : setA;
            foreach (var key in mine)
            {
                if (other.Contains(key))
                    continue;
                var f = ToFact(key);
                if (!seen.Add((f.Kind, f.Subject)) && f.Kind is not (SymbolPart.Attribute or SymbolPart.InterfaceFact or SymbolPart.Constraint or SymbolPart.MethodImplementation))
                    continue;
                bySubjectA.TryGetValue((f.Kind, f.Subject), out var la);
                bySubjectB.TryGetValue((f.Kind, f.Subject), out var lb);
                bool both = la is not null && lb is not null;
                byte[]? subjectBytes = f.Parts.Count > 1 && f.Parts[1].Tag == SymbolPart.Key ? f.Parts[1].Payload : null;
                bool admitted = (la is null || admA is null || admA.Admits(subjectBytes)) && (lb is null || admB is null || admB.Admits(subjectBytes));
                int before = cats.Count;
                switch (f.Kind)
                {
                    case SymbolPart.Visible or SymbolPart.Hidden:
                        cats.Add("visibility");
                        break;
                    case SymbolPart.MethodFact or SymbolPart.FieldFact or SymbolPart.PropertyFact or SymbolPart.EventFact:
                        if (!both)
                            cats.Add("member-presence:" + MemberKind(f.Kind));
                        else
                            Components(f.Kind, la![0].Parts, lb![0].Parts, cats);
                        break;
                    case SymbolPart.ParameterFact:
                        if (!both)
                            cats.Add(IsReturnParameter(f) ? "param-presence:return" : "param-presence");
                        else
                            Components(f.Kind, la![0].Parts, lb![0].Parts, cats);
                        break;
                    case SymbolPart.TypeFact:
                        Components(f.Kind, la![0].Parts, lb![0].Parts, cats);
                        break;
                    case SymbolPart.GenericParameterFact:
                        if (!both)
                            cats.Add("genparam-presence");
                        else
                        {
                            if (!la![0].Parts[2].Same(lb![0].Parts[2])) cats.Add("genparam-name");
                            if (!la[0].Parts[3].Same(lb[0].Parts[3])) cats.Add("genparam-flags");
                        }
                        break;
                    case SymbolPart.InterfaceFact: cats.Add("interface"); break;
                    case SymbolPart.Constraint: cats.Add("constraint"); break;
                    case SymbolPart.MethodImplementation: cats.Add("method-impl"); break;
                    case SymbolPart.Attribute: cats.Add("attribute"); break;
                    case SymbolPart.NullableContext: cats.Add("nullable-context"); break;
                    case SymbolPart.Layout: cats.Add("layout"); break;
                    case SymbolPart.Import: cats.Add("import"); break;
                    default: cats.Add("other"); break;
                }
                if (!admitted)
                    for (int i = before; i < cats.Count; i++)
                        cats[i] = "hidden-member";
            }
        }
        var distinct = cats.Distinct().ToList();
        return (distinct.Where(IsPresented).ToList(), distinct.Where(c => !IsPresented(c)).ToList());
    }

    // Parameter key = Parameter marker, Key(method), Int(sequence); sequence 0 is the return parameter.
    static bool IsReturnParameter(Fact f)
    {
        var parameterKey = Parse(f.Parts[1].Payload!);
        return parameterKey.Count >= 3 && parameterKey[^1].Tag == SymbolPart.Int && parameterKey[^1].Int == 0;
    }

    static void Components(SymbolPart kind, List<Part> x, List<Part> y, List<string> cats)
    {
        // Parts after [marker, subject].
        var px = x.Skip(2).ToList();
        var py = y.Skip(2).ToList();
        switch (kind)
        {
            case SymbolPart.MethodFact:
            {
                long fx = px[0].Int, fy = py[0].Int, diff = fx ^ fy;
                if ((diff & 0x7) != 0) cats.Add("method-flags:access");
                if ((diff & 0x10) != 0) cats.Add("method-flags:static");
                if ((diff & (0x20 | 0x40 | 0x100 | 0x400)) != 0) cats.Add("method-flags:virtual-shape");
                if ((diff & ~(0x7L | 0x10 | 0x20 | 0x40 | 0x100 | 0x400)) != 0) cats.Add("method-flags:other");
                if (px[1].Int != py[1].Int) cats.Add("method-implflags");
                break;
            }
            case SymbolPart.FieldFact:
            {
                long diff = px[0].Int ^ py[0].Int;
                if ((diff & 0x7) != 0) cats.Add("field-flags:access");
                if ((diff & (0x10 | 0x20 | 0x40)) != 0) cats.Add("field-flags:static-readonly-const");
                if ((diff & ~(0x7L | 0x10 | 0x20 | 0x40)) != 0) cats.Add("field-flags:other");
                if (!SameRange(px, py, 1, px.Count - 1, 1, py.Count - 1)) cats.Add("field-constant");
                if (!px[^1].Same(py[^1])) cats.Add("field-offset");
                break;
            }
            case SymbolPart.ParameterFact:
            {
                if (!px[0].Same(py[0])) cats.Add("param-name");
                long diff = px[1].Int ^ py[1].Int;
                if ((diff & 0x2) != 0) cats.Add("param-flags:out");
                if ((diff & ~0x2L) != 0) cats.Add("param-flags:other");
                if (!SameRange(px, py, 2, px.Count, 2, py.Count)) cats.Add("param-default");
                break;
            }
            case SymbolPart.PropertyFact:
            {
                if (px[0].Int != py[0].Int) cats.Add("property-flags");
                // constant (Absent | Constant Int Blob), then getter, setter (Absent | Key).
                int cx = px[1].Tag == SymbolPart.Absent ? 2 : 4;
                int cy = py[1].Tag == SymbolPart.Absent ? 2 : 4;
                if (!SameRange(px, py, 1, cx, 1, cy)) cats.Add("property-constant");
                if (!SameRange(px, py, cx, px.Count, cy, py.Count)) cats.Add("property-accessors");
                break;
            }
            case SymbolPart.EventFact:
            {
                if (px[0].Int != py[0].Int) cats.Add("event-flags");
                if (!SameRange(px, py, 1, px.Count, 1, py.Count)) cats.Add("event-accessors");
                break;
            }
            case SymbolPart.TypeFact:
            {
                long diff = px[0].Int ^ py[0].Int;
                const long presented = 0x7 | 0x20 | 0x80 | 0x100;
                if ((diff & presented) != 0) cats.Add("type-flags:visibility-kind-abstract-sealed");
                if ((diff & ~presented) != 0) cats.Add("type-flags:other");
                if (!SameRange(px, py, 1, px.Count, 1, py.Count)) cats.Add("type-base");
                break;
            }
            default:
                cats.Add("other");
                break;
        }
    }

    static bool SameRange(List<Part> x, List<Part> y, int fx, int tx, int fy, int ty)
    {
        if (tx - fx != ty - fy)
            return false;
        for (int i = 0; i < tx - fx; i++)
            if (!x[fx + i].Same(y[fy + i]))
                return false;
        return true;
    }
}
