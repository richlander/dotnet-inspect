#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj

// SPIKE (exp/unified-diff-exists, #10227 Q2; Annotated Type Map adoption step 2).
// One-version shape facts for one Library image pair, retained per side and
// compared by a keyed join, against Fast Diff and the complete diffs.
//
//   Q2a Body facts. Per side: Fast Diff's units (Types with their generated
//       code), every method keyed by Fast Diff's census key (FastDiff.Side.Key),
//       and per method with a body an IlBodyFactProducer fact: the canonical
//       instruction stream with side-local symbolic token ids, plus an XxHash128
//       fingerprint over the same stream with each id replaced by its key's hash.
//       Compare: join the two symbol tables once (old id -> new id), join units by
//       Type key and methods by census key, and compare facts: "hash" decides by
//       fingerprint alone; "exact" confirms equal fingerprints by the translated
//       streams. Grain is per Type (Fast Diff's Type-level Member partition is
//       not implemented yet), with method-grain agreement against
//       IlBodyDiff.Compare.
//   Q2b API facts. Per side, per Fast Diff unit, the exact-Type extraction path of
//       exp/type-diff-cost 6b178603b (g2exact, #10221): resolve the TypeDef by
//       structured name, ask the same-image extension Exists, extract that
//       TypeDef's declarations (with the extension-declaring Types and local
//       extension projection when Exists is true) over one warm
//       ApiSurfaceImageCache per image. Compare: per Type, the complete comparison
//       (MetadataFindings.CompareApi) over the two retained projections.
//
// Usage: measure-map-facts <before.dll> <after.dll> [iterations] [--no-correctness] [--no-api] [--no-body] [--detail]

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
    Console.Error.WriteLine("Usage: measure-map-facts <before.dll> <after.dll> [iterations] [--no-correctness] [--no-api] [--no-body] [--detail]");
    return 2;
}
string beforePath = positional[0];
string afterPath = positional[1];
int iterations = positional.Length > 2 ? int.Parse(positional[2]) : 5;
bool correctness = !args.Contains("--no-correctness");
bool runApi = !args.Contains("--no-api");
bool runBody = !args.Contains("--no-body");
bool detail = args.Contains("--detail");
byte[] beforeBytes = File.ReadAllBytes(beforePath);
byte[] afterBytes = File.ReadAllBytes(afterPath);
const IlBodyDiffNormalization Policy =
    IlBodyDiffNormalization.NormalizeCurrentAssemblyScope | IlBodyDiffNormalization.NormalizePlatformAssemblyScope;
var apiOptions = new ApiDiffOptions(ApiDiffScope.All);

PEReader Open(byte[] bytes) => new(new MemoryStream(bytes, writable: false));
static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
static double MedianOf(IEnumerable<double> v) { var l = v.Order().ToList(); return l.Count == 0 ? 0 : l[l.Count / 2]; }
static string S(FastDiffState s) => s switch
{
    FastDiffState.Changed => "C",
    FastDiffState.Unchanged => "U",
    FastDiffState.Indeterminate => "I",
    _ => s.ToString(),
};

Console.WriteLine($"pair\t{beforePath}\t{afterPath}\titerations={iterations}");

// Fast Diff baseline (and its states for agreement).
FastDiffResult fast = default!, fastApi = default!;
{
    var both = new List<double>(); var api = new List<double>();
    for (int i = 0; i <= iterations; i++)
    {
        using var b = Open(beforeBytes); using var a = Open(afterBytes);
        long t = Stopwatch.GetTimestamp();
        fast = FastDiff.Compare(b, a);
        double x = Ms(t);
        t = Stopwatch.GetTimestamp();
        fastApi = FastDiff.Compare(b, a, FastDiffAxes.Api);
        double y = Ms(t);
        if (i == 0) continue;
        both.Add(x); api.Add(y);
    }
    Console.WriteLine($"ms\tfast(ApiAndBody)\t{MedianOf(both):F2}\tfast(Api)={MedianOf(api):F2}\tfastBodyPart={MedianOf(both) - MedianOf(api):F2}\ttypes={fast.Types.Length}");
}
var fastById = fast.Types.GroupBy(t => t.Identifier).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
var fastByName = fast.Types.GroupBy(t => t.FullName).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

// ===================================================================== Q2a: Body facts
if (runBody)
{
    // Production per side, retained.
    var prodB = new List<double>(); var prodA = new List<double>();
    var keysMs = new List<double>(); var factsMs = new List<double>(); var unitsMs = new List<double>();
    BodySide sideB = default!, sideA = default!;
    PEReader keepB = default!, keepA = default!;
    for (int i = 0; i <= iterations; i++)
    {
        var b = Open(beforeBytes); var a = Open(afterBytes);
        long t = Stopwatch.GetTimestamp();
        var x = BodySide.Build(b, Policy);
        double tb = Ms(t);
        t = Stopwatch.GetTimestamp();
        var y = BodySide.Build(a, Policy);
        double ta = Ms(t);
        if (i > 0)
        {
            prodB.Add(tb); prodA.Add(ta);
            keysMs.Add(x.KeysMs + y.KeysMs); factsMs.Add(x.FactsMs + y.FactsMs); unitsMs.Add(x.UnitsMs + y.UnitsMs);
        }
        if (i < iterations) { b.Dispose(); a.Dispose(); continue; }
        sideB = x; sideA = y; keepB = b; keepA = a;
    }
    Console.WriteLine($"ms\tQ2a-produce\tbefore={MedianOf(prodB):F2}\tafter={MedianOf(prodA):F2}\tboth={MedianOf(prodB) + MedianOf(prodA):F2}"
        + $"\tof both: units={MedianOf(unitsMs):F2}\tmemberKeys={MedianOf(keysMs):F2}\tbodyFacts={MedianOf(factsMs):F2}");

    // Join and compare, two retained tables.
    BodyCompare hash = default!, exact = default!;
    var hashMs = new List<double>(); var exactMs = new List<double>(); var translateMs = new List<double>();
    for (int i = 0; i <= iterations; i++)
    {
        long t = Stopwatch.GetTimestamp();
        var h = BodyCompare.Run(sideB, sideA, confirm: false);
        double th = Ms(t);
        t = Stopwatch.GetTimestamp();
        var e = BodyCompare.Run(sideB, sideA, confirm: true);
        double te = Ms(t);
        if (i == 0) continue;
        hashMs.Add(th); exactMs.Add(te); translateMs.Add(e.TranslateMs);
        hash = h; exact = e;
    }
    Console.WriteLine($"ms\tQ2a-join+compare\thash={MedianOf(hashMs):F2}\texact={MedianOf(exactMs):F2}\tof exact: symbolJoin={MedianOf(translateMs):F2}"
        + $"\ttypes={exact.States.Count}\tmethodPairs={exact.MethodPairs}\tbodyPairs={exact.BodyPairs}\tconfirmed={exact.Confirmed}");

    // Memory: what each retained table holds, and the managed heap it keeps alive.
    foreach (var (label, side) in new[] { ("before", sideB), ("after", sideA) })
    {
        Console.WriteLine($"mem\tQ2a-{label}\tmethods={side.Methods}\tbodies={side.Producer.Bodies}\tstreamBytes={side.Producer.StreamBytes}"
            + $"\tfingerprintBytes={16L * side.Producer.Bodies}\tsymbols={side.Producer.Table.Count}\tsymbolBytes={side.Producer.Table.Bytes}"
            + $"\tmemberKeyBytes={side.MemberKeyBytes}\tunits={side.Types.Count}");
    }
    {
        // Heap kept alive by one side, each form built in its own call so nothing else
        // stays reachable: the whole build (Fast Diff side, producer, and their caches),
        // the retained map (Type key -> census key -> fact, plus the side's symbol table),
        // and a fingerprint-only map (no streams, no table).
        long full = Retained(() => BodySide.Build(Open(beforeBytes), Policy));
        long map = Retained(() => BodySide.Build(Open(beforeBytes), Policy).Lean());
        long fingerprints = Retained(() => BodySide.Build(Open(beforeBytes), Policy).FingerprintsOnly());
        Console.WriteLine($"mem\tQ2a-heap(before side)\tbuildRetained={full}\tmapRetained={map}\tfingerprintMapRetained={fingerprints}\timage={beforeBytes.Length}");
    }

    Console.WriteLine($"verdicts\tQ2a-hash-vs-exact\tdisagree={hash.States.Count(kv => exact.States[kv.Key].State != kv.Value.State)}");

    if (correctness)
    {
        // B2 and the complete IL diff per Type, over the same image pair.
        IlBodyDiff.SpikeFamilyCache = true;
        var sym = new IlSymbolicExistsSession(keepB, keepA, Policy);
        var b2 = new Dictionary<string, (FastDiffState State, string? Reason)>(StringComparer.Ordinal);
        foreach (var unit in sym.Units)
        {
            var st = sym.CompareType(unit, out string? reason);
            b2[Identifier(sym, unit)] = (st switch { IlExistsState.Changed => FastDiffState.Changed, IlExistsState.Unchanged => FastDiffState.Unchanged, _ => FastDiffState.Indeterminate }, reason);
        }
        var oracle = Oracle(sym, Policy, out var expected);
        IlBodyDiff.SpikeFamilyCache = false;

        // Method grain: every joined method pair with a body on both sides against IlBodyDiff.Compare.
        int pairs = 0, disagreeExact = 0, disagreeHash = 0, notInText = 0;
        var mdB = keepB.GetMetadataReader(); var mdA = keepA.GetMetadataReader();
        foreach (var (key, pair) in exact.PairOutcomes)
        {
            pairs++;
            var ob = keepB.GetMethodBody(mdB.GetMethodDefinition(pair.Old).RelativeVirtualAddress);
            var nb = keepA.GetMethodBody(mdA.GetMethodDefinition(pair.New).RelativeVirtualAddress);
            var rows = (MetadataTokens.GetRowNumber(pair.Old), MetadataTokens.GetRowNumber(pair.New));
            IlExistsOutcome want;
            if (expected.TryGetValue(rows, out var w)) want = w;
            else
            {
                notInText++;
                var d = IlBodyDiff.Compare(mdB, ob, mdA, nb, Policy);
                want = !d.IsAvailable ? IlExistsOutcome.Unavailable : d.IsExact ? IlExistsOutcome.Exact : IlExistsOutcome.Changed;
            }
            if (pair.Outcome != want)
            {
                disagreeExact++;
                if (disagreeExact <= 10) Console.WriteLine($"  q2a-pair-disagree\t{key}\tq2a={pair.Outcome}\tcompare={want}");
            }
            if (hash.PairOutcomes.TryGetValue(key, out var hp) && hp.Outcome != want) disagreeHash++;
        }
        Console.WriteLine($"gate-q2a-method\tbodyPairs={pairs}\texactVsCompare-disagree={disagreeExact}\thashVsCompare-disagree={disagreeHash}\tpairsNotInTextMap={notInText}");

        // Type grain: Q2a against B2, the complete IL diff, and Fast Diff.
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var classes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var lines = new List<string>();
        int vsB2 = 0, vsOracle = 0, vsFast = 0, missingB2 = 0;
        foreach (var (id, v) in exact.States)
        {
            string fb = fastById.TryGetValue(id, out var fs) ? S(fs.Body) : "absent";
            string bb = b2.TryGetValue(id, out var bv) ? S(bv.State) : "absent";
            string ob = oracle.TryGetValue(id, out var ov) ? S(ov.State) : "absent";
            if (bb == "absent") missingB2++;
            string k = $"q2a={S(v.State)} b2={bb} oracle={ob} fast={fb}";
            counts[k] = counts.TryGetValue(k, out int c) ? c + 1 : 1;
            if (bb != "absent" && bb != S(v.State)) vsB2++;
            if (ob != "absent" && ob != S(v.State)) vsOracle++;
            if (fb != "absent" && fb != S(v.State)) vsFast++;
            if (bb != S(v.State) || ob != S(v.State) || fb != S(v.State))
            {
                string? fastReason = fs is null ? null : FastDiff.Explain(keepB, keepA, fs.FullName).Body;
                string cls = Classify(v.State, ov.State, ov.Reason, bb, fs?.Body, fastReason, v.Reason);
                classes[cls] = classes.TryGetValue(cls, out int n) ? n + 1 : 1;
                lines.Add($"  q2a-diff\t{cls}\t{id}\t{k}\tq2a: {v.Reason}\toracle: {ov.Reason}\tfast: {fastReason?.Replace('\n', ' ')}");
            }
        }
        foreach (var (k, c) in classes) Console.WriteLine($"class-q2a\t{k}\t{c}");
        foreach (var (k, c) in counts) Console.WriteLine($"agree-q2a\t{k}\t{c}");
        Console.WriteLine($"gate-q2a-type\tunits={exact.States.Count}\tdisagreeB2={vsB2}\tdisagreeOracle={vsOracle}\tdisagreeFast={vsFast}\tmissingInB2={missingB2}");
        foreach (var l in lines.Take(detail ? int.MaxValue : 40)) Console.WriteLine(l);
    }
    keepB.Dispose(); keepA.Dispose();
}

// ===================================================================== Q2b: API facts
if (runApi)
{
    var bounds = new ApiSurfaceExtractionBounds(1_000_000, 10_000_000, 1_000_000, 1_000_000, int.MaxValue);
    var prodB = new List<double>(); var prodA = new List<double>();
    var resolveMs = new List<double>(); var existsMs = new List<double>(); var extractMs = new List<double>(); var nameMs = new List<double>();
    ApiSide apiB = default!, apiA = default!;
    for (int i = 0; i <= Math.Max(1, iterations); i++)
    {
        using var b = Open(beforeBytes); using var a = Open(afterBytes);
        long t = Stopwatch.GetTimestamp();
        var x = ApiSide.Build(b, bounds);
        double tb = Ms(t);
        t = Stopwatch.GetTimestamp();
        var y = ApiSide.Build(a, bounds);
        double ta = Ms(t);
        if (i == 0) continue;
        prodB.Add(tb); prodA.Add(ta);
        resolveMs.Add(x.ResolveMs + y.ResolveMs); existsMs.Add(x.ExistsMs + y.ExistsMs); extractMs.Add(x.ExtractMs + y.ExtractMs); nameMs.Add(x.NameMs + y.NameMs);
        apiB = x; apiA = y;
    }
    Console.WriteLine($"ms\tQ2b-produce\tbefore={MedianOf(prodB):F2}\tafter={MedianOf(prodA):F2}\tboth={MedianOf(prodB) + MedianOf(prodA):F2}"
        + $"\tof both: units+names={MedianOf(nameMs):F2}\tresolve={MedianOf(resolveMs):F2}\textensionExists={MedianOf(existsMs):F2}\textract={MedianOf(extractMs):F2}"
        + $"\tunits={apiB.Types.Count}+{apiA.Types.Count}\textMode={apiB.ExtensionTypes}+{apiA.ExtensionTypes}\tfallbackFull={apiB.FullFallbacks}+{apiA.FullFallbacks}"
        + $"\tsurfaceTypes={apiB.SurfaceTypes}+{apiA.SurfaceTypes}\tsurfaceMembers={apiB.SurfaceMembers}+{apiA.SurfaceMembers}");

    // Attribution: fixed per-image costs and the extraction's own components, both sides.
    {
        double Ext(Func<PEReader, object?> f) => MedianOf(Enumerable.Range(0, Math.Max(3, iterations)).Select(_ =>
        {
            using var b = Open(beforeBytes); using var a = Open(afterBytes);
            long t = Stopwatch.GetTimestamp();
            f(b); f(a);
            return Ms(t);
        }).ToList());
        double memorySafety = Ext(r => MemorySafetyMetadataIndex.Create(r.GetMetadataReader()));
        double wholeDecl = Ext(r => ApiSurfaceExtractor.ExtractDeclarations(r, ApiSurfaceExtractionScope.Public));
        double wholeFull = Ext(r => ApiSurfaceExtractor.Extract(r, ApiSurfaceExtractionScope.Public));
        double typesOnly = Ext(r => ApiSurfaceExtractor.Extract(r, ApiSurfaceExtractionScope.Public, typesOnly: true));
        double none = Ext(r => ApiSurfaceExtractor.ExtractDeclarations(r, ApiSurfaceExtractionScope.Public, _ => false));
        double keyed = MedianOf(Enumerable.Range(0, Math.Max(3, iterations)).Select(_ =>
        {
            long t = Stopwatch.GetTimestamp();
            foreach (var side in new[] { apiB, apiA })
                foreach (var entry in side.Types.Values)
                    if (entry.Surface is { } surface)
                        foreach (var type in surface.Types)
                            foreach (var member in type.Members)
                                ApiMemberIdentity.CreateHandle(type, member);
            return Ms(t);
        }).ToList());
        // Per-request costs that scale with the Library rather than the Type: one warm
        // extraction call that admits no Type, and one extension Exists, each times the
        // number of requests the whole-Library production makes.
        double warmNone, existsOne;
        {
            using var b = Open(beforeBytes);
            var md = b.GetMetadataReader();
            var cache = new ApiSurfaceImageCache(b);
            ApiSurfaceExtractor.ExtractDeclarations(b, ApiSurfaceExtractionScope.Public, _ => false, cache);
            warmNone = MedianOf(Enumerable.Range(0, 9).Select(_ =>
            {
                long t = Stopwatch.GetTimestamp();
                ApiSurfaceExtractor.ExtractDeclarations(b, ApiSurfaceExtractionScope.Public, _ => false, cache);
                return Ms(t);
            }).ToList());
            var identity = AssemblyReferenceIdentity.FromAssemblyDefinition(md);
            var first = new FastDiff.Side(b).Units().Values.First(u => !u.Malformed);
            var name = ((MetadataTypeDefinitionNameReadResult.Read)MetadataTypeDefinitionName.Read(md, first.Owner)).Name;
            existsOne = MedianOf(Enumerable.Range(0, 9).Select(_ =>
            {
                long t = Stopwatch.GetTimestamp();
                ExactTypeApi.ExtensionExists(b, md, identity, name, first.Owner);
                return Ms(t);
            }).ToList());
        }
        int requests = apiB.Types.Count + apiA.Types.Count;
        Console.WriteLine($"attr-q2b-per-request\textractNoTypeWarm={warmNone:F4}\tx{requests}={warmNone * requests:F2}\textensionExistsOne={existsOne:F4}\tx{requests}={existsOne * requests:F2}");
        Console.WriteLine($"attr-q2b\tmemorySafetyIndex(both)={memorySafety:F2}\twholeExtractDeclarations(both)={wholeDecl:F2}\twholeExtract(both)={wholeFull:F2}"
            + $"\ttypesOnly(both)={typesOnly:F2}\tnoTypeAdmitted(both)={none:F2}\tcanonicalMemberIdentities(Keyed, retained, both)={keyed:F2}");
        foreach (var (label, side) in new[] { ("before", apiB), ("after", apiA) })
        {
            foreach (var (name, ms) in side.SlowestExtractions(5))
                Console.WriteLine($"  q2b-slowest-{label}\t{name}\t{ms:F2}");
        }
    }

    // Compare per Type over retained projections.
    ApiCompare cmp = default!;
    var cmpMs = new List<double>();
    for (int i = 0; i <= Math.Max(1, iterations); i++)
    {
        long t = Stopwatch.GetTimestamp();
        var r = ApiCompare.Run(apiB, apiA, apiOptions);
        double x = Ms(t);
        if (i == 0) continue;
        cmpMs.Add(x); cmp = r;
    }
    Console.WriteLine($"ms\tQ2b-compare\t{MedianOf(cmpMs):F2}\ttypes={cmp.States.Count}\tcompared={cmp.Compared}\tchangedTypes={cmp.States.Count(kv => kv.Value.State == FastDiffState.Changed)}\tchangedMembers={cmp.ChangedMembers}");
    {
        long full = Retained(() => ApiSide.Build(Open(beforeBytes), bounds));
        long only = Retained(() => ApiSide.Build(Open(beforeBytes), bounds).Types.ToDictionary(kv => kv.Key, kv => kv.Value.Surface));
        Console.WriteLine($"mem\tQ2b-heap(before side)\tbuildRetained={full}\tprojectionsOnlyRetained={only}");
    }

    if (correctness)
    {
        // The complete Public API comparison over whole surfaces, as the oracle.
        using var b = Open(beforeBytes); using var a = Open(afterBytes);
        var whole = MetadataFindings.CompareApi(
            ApiSurfaceExtractor.Extract(b, ApiSurfaceExtractionScope.Public),
            ApiSurfaceExtractor.Extract(a, ApiSurfaceExtractionScope.Public),
            new FindingSubject("api", "API"), apiOptions);
        var oracle = new HashSet<string>(StringComparer.Ordinal);
        foreach (TypeDiff type in whole.ApiDiff.TypeDiffs) oracle.Add(type.TypeFullName);
        if (whole.Types.Value is FindingComparison<ApiTypeHandle>.Complete ot)
            foreach (var pair in ot.Pairs) if (pair.Kind != PairKind.Present) oracle.Add(Payload(pair).TypeFullName);
        if (whole.Members.Value is FindingComparison<ApiMemberHandle>.Complete om)
            foreach (var pair in om.Pairs) if (pair.Kind != PairKind.Present) oracle.Add(Payload(pair).TypeFullName);
        var names = cmp.States.Keys.Concat(oracle).Concat(fastByName.Keys).ToHashSet(StringComparer.Ordinal);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var lines = new List<string>();
        int vsOracle = 0, vsFast = 0;
        foreach (var name in names)
        {
            var mine = cmp.States.TryGetValue(name, out var v) ? v.State : FastDiffState.Unchanged;
            bool o = oracle.Contains(name);
            string f = fastByName.TryGetValue(name, out var fs) ? S(fs.Api) : "absent";
            string k = $"q2b={S(mine)} oracle={(o ? "C" : "U")} fast={f}";
            counts[k] = counts.TryGetValue(k, out int c) ? c + 1 : 1;
            bool dO = (mine == FastDiffState.Changed) != o;
            bool dF = f != "absent" && f != S(mine);
            if (dO) vsOracle++;
            if (dF) vsFast++;
            if (dO || dF)
            {
                string cls = dO ? "q2b-vs-oracle" : mine == FastDiffState.Unchanged && f == "C" ? "#10197 Fast Diff API over-report" : "other";
                lines.Add($"  q2b-diff\t{cls}\t{name}\t{k}\t{v.Reason}\tfast: {(fs is null ? "" : FastDiff.Explain(b, a, fs.FullName).Api?.Replace('\n', ' '))}");
            }
        }
        foreach (var (k, c) in counts) Console.WriteLine($"agree-q2b\t{k}\t{c}");
        Console.WriteLine($"gate-q2b-type\ttypes={names.Count}\tdisagreeOracle={vsOracle}\tdisagreeFast={vsFast}");
        foreach (var l in lines.Take(detail ? int.MaxValue : 40)) Console.WriteLine(l);
    }
}
return 0;

static T Payload<T>(PairFinding<T> pair) where T : notnull
    => (((IPairFinding)pair).New as Finding<T> ?? (Finding<T>)((IPairFinding)pair).Old!).Payload;

// The managed heap one object graph keeps alive: built in a call of its own, then measured
// with the graph held and again after it is released.
[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
static long Retained(Func<object> build)
{
    static long Heap() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return GC.GetTotalMemory(true); }
    long before = Heap();
    var holder = new object[1];
    Hold(holder, build);
    long with = Heap();
    holder[0] = null!;
    GC.KeepAlive(holder);
    return with - before;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    static void Hold(object[] holder, Func<object> build) => holder[0] = build();
}

static string Identifier(IlSymbolicExistsSession session, IlSymbolicExistsSession.Unit unit)
{
    var (reader, handle) = unit.OldPresent ? (session.OldReader, unit.Old) : (session.NewReader, unit.New);
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
        : "!" + unit.Identity;
}

/// <summary>The complete IL diff per Type (IlBodyDiff.Compare per method pair) over B2's pairing.</summary>
static Dictionary<string, (FastDiffState State, string? Reason)> Oracle(
    IlSymbolicExistsSession session, IlBodyDiffNormalization policy, out Dictionary<(int, int), IlExistsOutcome> expected)
{
    expected = [];
    var states = new Dictionary<string, (FastDiffState State, string? Reason)>(StringComparer.Ordinal);
    foreach (var unit in session.Units)
    {
        FastDiffState state = FastDiffState.Unchanged; string? reason = null;
        void Set(FastDiffState s, string r)
        {
            if (state == FastDiffState.Changed) return;
            if (s == FastDiffState.Indeterminate && state == FastDiffState.Indeterminate) return;
            state = s; reason = r;
        }
        string id = Identifier(session, unit);
        if (unit.DuplicateRow) { states[id] = (FastDiffState.Indeterminate, "duplicate Type rows"); continue; }
        if (!unit.OldPresent || !unit.NewPresent) { states[id] = (FastDiffState.Changed, "presence"); continue; }
        if (unit.IdentityFailure) Set(FastDiffState.Indeterminate, "method identity failure");
        foreach (var key in unit.OldMethods)
            if (!session.TryNewHandle(key, out _) && session.OldReader.GetMethodDefinition(session.OldHandle(key)).RelativeVirtualAddress != 0)
                Set(FastDiffState.Changed, "method with body on old side only: " + session.Table[key.Identity]);
        foreach (var key in unit.NewMethods)
            if (!session.TryOldHandle(key, out _) && session.NewReader.GetMethodDefinition(session.NewHandle(key)).RelativeVirtualAddress != 0)
                Set(FastDiffState.Changed, "method with body on new side only: " + session.Table[key.Identity]);
        foreach (var key in unit.OldMethods)
        {
            if (!session.TryNewHandle(key, out var nh)) continue;
            var om = session.OldReader.GetMethodDefinition(session.OldHandle(key));
            var nm = session.NewReader.GetMethodDefinition(nh);
            if (om.RelativeVirtualAddress == 0 && nm.RelativeVirtualAddress == 0) continue;
            if (om.RelativeVirtualAddress == 0 || nm.RelativeVirtualAddress == 0) { Set(FastDiffState.Changed, "body on one side: " + session.Table[key.Identity]); continue; }
            var ob = session.OldPe.GetMethodBody(om.RelativeVirtualAddress);
            var nb = session.NewPe.GetMethodBody(nm.RelativeVirtualAddress);
            var diff = IlBodyDiff.Compare(session.OldReader, ob, session.NewReader, nb, policy);
            var outcome = !diff.IsAvailable ? IlExistsOutcome.Unavailable : diff.IsExact ? IlExistsOutcome.Exact : IlExistsOutcome.Changed;
            expected[(MetadataTokens.GetRowNumber(session.OldHandle(key)), MetadataTokens.GetRowNumber(nh))] = outcome;
            if (outcome == IlExistsOutcome.Unavailable) Set(FastDiffState.Indeterminate, "IL unavailable: " + diff.Failure);
            else if (outcome == IlExistsOutcome.Changed)
            {
                bool calli = diff.Rows.All(r => r.Operation.OpcodeFamily == "calli");
                Set(FastDiffState.Changed, (calli ? "IL(calli only): " : "IL: ") + session.Table[key.Identity]);
            }
        }
        states[id] = (state, reason);
    }
    return states;
}

static string Classify(FastDiffState mine, FastDiffState oracle, string? oracleReason, string b2, FastDiffState? fast, string? fastReason, string? mineReason)
{
    if (mine != oracle)
        return "q2a-vs-oracle(" + (mineReason ?? "") .Split(':')[0] + ")";
    if (b2 != S(mine))
        return "q2a-vs-b2";
    if (fast is null)
        return "partition";
    if (mine == FastDiffState.Changed && fast == FastDiffState.Unchanged)
        return oracleReason is { } r && (r.StartsWith("method with body", StringComparison.Ordinal) || r.StartsWith("body on one side", StringComparison.Ordinal))
            ? "#10223 one-sided method body not counted by Fast Diff"
            : oracleReason?.StartsWith("IL(calli only)", StringComparison.Ordinal) == true
                ? "#9801 calli stand-alone signature (complete IL diff over-reports)"
                : "fast-miss(other)";
    if (mine == FastDiffState.Unchanged && fast == FastDiffState.Changed)
    {
        string fr = (fastReason ?? "").Replace('\n', ' ').TrimStart('-', '+', ' ');
        int cut = fr.IndexOfAny([' ', ':', '(']);
        string head = cut > 0 ? fr[..cut] : fr;
        return head.StartsWith("IL", StringComparison.Ordinal)
            ? "#10196 Fast Diff raw walker: encoding-level body fact (" + head + ")"
            : "#10196 Fast Diff Body census: non-IL fact (" + head + ")";
    }
    return "other";

    static string S(FastDiffState s) => s switch
    {
        FastDiffState.Changed => "C",
        FastDiffState.Unchanged => "U",
        FastDiffState.Indeterminate => "I",
        _ => s.ToString(),
    };
}

/// <summary>One side's retained Body facts: Fast Diff's units, census-keyed methods, and facts.</summary>
sealed class BodySide
{
    public required FastDiff.Side Side { get; init; }
    public required IlBodyFactProducer Producer { get; init; }
    public Dictionary<SymbolKey, TypeFacts> Types { get; } = [];
    public double UnitsMs, KeysMs, FactsMs;
    public int Methods;
    public long MemberKeyBytes;

    public static BodySide Build(PEReader pe, IlBodyDiffNormalization policy)
    {
        long t = Stopwatch.GetTimestamp();
        var side = new FastDiff.Side(pe);
        var units = side.Units();
        var result = new BodySide { Side = side, Producer = new IlBodyFactProducer(pe, policy) };
        result.UnitsMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        long keyTicks = 0, factTicks = 0;
        var md = side.Md;
        foreach (var (typeKey, unit) in units)
        {
            var facts = new TypeFacts(unit);
            result.Types[typeKey] = facts;
            if (unit.Malformed) { facts.Failure = "malformed Type"; continue; }
            foreach (var type in (TypeDefinitionHandle[])[unit.Owner, .. unit.Generated])
            {
                foreach (var method in md.GetTypeDefinition(type).GetMethods())
                {
                    long k = Stopwatch.GetTimestamp();
                    SymbolKey key;
                    try
                    {
                        key = side.Key(method);
                    }
                    catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                    {
                        side.Keys.Reset();
                        facts.Failure ??= "member key: " + ex.Message;
                        keyTicks += Stopwatch.GetTimestamp() - k;
                        continue;
                    }
                    long f = Stopwatch.GetTimestamp();
                    keyTicks += f - k;
                    var fact = result.Producer.Produce(method);
                    factTicks += Stopwatch.GetTimestamp() - f;
                    result.Methods++;
                    if (!facts.Methods.TryAdd(key, new MethodEntry(method, fact)))
                        facts.Failure ??= "two methods share the census key " + key;
                    else
                        result.MemberKeyBytes += key.Length;
                }
            }
        }
        result.KeysMs = keyTicks * 1000.0 / Stopwatch.Frequency;
        result.FactsMs = factTicks * 1000.0 / Stopwatch.Frequency;
        return result;
    }

    /// <summary>The retained map alone: Type key -> census key -> fact, and the symbol table.</summary>
    public (Dictionary<SymbolKey, Dictionary<SymbolKey, BodyFact>> Types, SymbolKeyTable Table) Lean()
        => (Types.ToDictionary(kv => kv.Key, kv => kv.Value.Methods.ToDictionary(m => m.Key, m => m.Value.Fact)), Producer.Table);

    /// <summary>A fingerprint-only map: Type key -> census key -> (outcome, fingerprint).</summary>
    public Dictionary<SymbolKey, Dictionary<SymbolKey, (BodyFactOutcome, UInt128)>> FingerprintsOnly()
        => Types.ToDictionary(kv => kv.Key, kv => kv.Value.Methods.ToDictionary(m => m.Key, m => (m.Value.Fact.Outcome, m.Value.Fact.Fingerprint)));
}

sealed class TypeFacts(FastDiff.Unit unit)
{
    public FastDiff.Unit Unit { get; } = unit;
    public Dictionary<SymbolKey, MethodEntry> Methods { get; } = [];
    public string? Failure { get; set; }
}

readonly record struct MethodEntry(MethodDefinitionHandle Handle, BodyFact Fact);

sealed class BodyCompare
{
    public Dictionary<string, (FastDiffState State, string? Reason)> States { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, (MethodDefinitionHandle Old, MethodDefinitionHandle New, IlExistsOutcome Outcome)> PairOutcomes { get; } = new(StringComparer.Ordinal);
    public int MethodPairs, BodyPairs, Confirmed;
    public double TranslateMs;

    /// <summary>
    /// The map diff's shape comparison on the Body axis, per Type, run to completion (no early
    /// exit) so every paired body's outcome is recorded.
    /// </summary>
    public static BodyCompare Run(BodySide before, BodySide after, bool confirm)
    {
        var run = new BodyCompare();
        long t = Stopwatch.GetTimestamp();
        int[] translate = confirm ? IlBodyFactProducer.Translate(before.Producer.Table, after.Producer.Table) : [];
        run.TranslateMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        foreach (var (key, x) in before.Types)
        {
            string id = x.Unit.Identifier;
            if (!after.Types.TryGetValue(key, out var y))
            {
                run.States[id] = x.Unit.Malformed ? (FastDiffState.Indeterminate, "malformed") : (FastDiffState.Changed, "presence");
                continue;
            }
            run.States[id] = Compare(run, x, y, translate, confirm);
        }
        foreach (var (key, y) in after.Types)
        {
            if (!before.Types.ContainsKey(key))
                run.States[y.Unit.Identifier] = y.Unit.Malformed ? (FastDiffState.Indeterminate, "malformed") : (FastDiffState.Changed, "presence");
        }
        return run;
    }

    static (FastDiffState, string?) Compare(BodyCompare run, TypeFacts x, TypeFacts y, int[] translate, bool confirm)
    {
        if (x.Failure is not null || y.Failure is not null)
            return (FastDiffState.Indeterminate, x.Failure ?? y.Failure);
        FastDiffState state = FastDiffState.Unchanged;
        string? reason = null;
        void Set(FastDiffState s, string r)
        {
            if (state == FastDiffState.Changed) return;
            if (s == FastDiffState.Indeterminate && state == FastDiffState.Indeterminate) return;
            state = s; reason = r;
        }
        foreach (var (key, a) in x.Methods)
        {
            if (!y.Methods.TryGetValue(key, out var b))
            {
                if (a.Fact.Outcome != BodyFactOutcome.NoBody)
                    Set(FastDiffState.Changed, "method with body on old side only: " + key);
                continue;
            }
            run.MethodPairs++;
            if (a.Fact.Outcome == BodyFactOutcome.NoBody && b.Fact.Outcome == BodyFactOutcome.NoBody)
                continue;
            if (a.Fact.Outcome == BodyFactOutcome.NoBody || b.Fact.Outcome == BodyFactOutcome.NoBody)
            {
                Set(FastDiffState.Changed, "body on one side: " + key);
                continue;
            }
            run.BodyPairs++;
            IlExistsOutcome outcome;
            if (a.Fact.Outcome == BodyFactOutcome.Unavailable || b.Fact.Outcome == BodyFactOutcome.Unavailable)
                outcome = IlExistsOutcome.Unavailable;
            else if (a.Fact.Fingerprint != b.Fact.Fingerprint)
                outcome = IlExistsOutcome.Changed;
            else if (!confirm)
                outcome = IlExistsOutcome.Exact;
            else
            {
                run.Confirmed++;
                outcome = IlBodyFactProducer.StreamsEqual(a.Fact.Stream!, b.Fact.Stream!, translate) ? IlExistsOutcome.Exact : IlExistsOutcome.Changed;
            }
            run.PairOutcomes[key.ToString()] = (a.Handle, b.Handle, outcome);
            if (outcome == IlExistsOutcome.Changed) Set(FastDiffState.Changed, "IL: " + key);
            else if (outcome == IlExistsOutcome.Unavailable) Set(FastDiffState.Indeterminate, "IL unavailable: " + (a.Fact.Failure ?? b.Fact.Failure));
        }
        foreach (var (key, b) in y.Methods)
        {
            if (!x.Methods.ContainsKey(key) && b.Fact.Outcome != BodyFactOutcome.NoBody)
                Set(FastDiffState.Changed, "method with body on new side only: " + key);
        }
        return (state, reason);
    }
}

/// <summary>One side's retained API projections, one per Fast Diff unit, through the exact-Type path.</summary>
sealed class ApiSide
{
    public Dictionary<string, (TypeDefinitionHandle Handle, ApiSurface? Surface, bool Extension, double Ms)> Types { get; } = new(StringComparer.Ordinal);
    public double NameMs, ResolveMs, ExistsMs, ExtractMs;
    public int ExtensionTypes, FullFallbacks, SurfaceTypes, SurfaceMembers;
    ApiSurfaceImageCache? _cache;

    public IEnumerable<(string Name, double Ms)> SlowestExtractions(int n)
        => Types.OrderByDescending(kv => kv.Value.Ms).Take(n).Select(kv => (kv.Key, kv.Value.Ms));

    public static ApiSide Build(PEReader pe, ApiSurfaceExtractionBounds bounds)
    {
        var side = new ApiSide();
        long t = Stopwatch.GetTimestamp();
        var md = pe.GetMetadataReader();
        var identity = AssemblyReferenceIdentity.FromAssemblyDefinition(md);
        var units = new FastDiff.Side(pe).Units();
        var names = new List<(string FullName, MetadataTypeDefinitionName Name)>(units.Count);
        foreach (var (_, unit) in units)
        {
            if (unit.Malformed) continue;
            if (MetadataTypeDefinitionName.Read(md, unit.Owner) is MetadataTypeDefinitionNameReadResult.Read read)
                names.Add((unit.FullName, read.Name));
        }
        side.NameMs = Stopwatch.GetElapsedTime(t).TotalMilliseconds;
        side._cache = new ApiSurfaceImageCache(pe);
        long resolveTicks = 0, existsTicks = 0, extractTicks = 0;
        foreach (var (fullName, name) in names)
        {
            long a = Stopwatch.GetTimestamp();
            TypeDefinitionHandle handle = ExactTypeApi.Resolve(md, name, bounds);
            long b = Stopwatch.GetTimestamp();
            bool extension = ExactTypeApi.ExtensionExists(pe, md, identity, name, handle) != false;
            long c = Stopwatch.GetTimestamp();
            ApiSurface surface = ExactTypeApi.Extract(pe, md, identity, side._cache, name, handle, extension, out bool full);
            long d = Stopwatch.GetTimestamp();
            resolveTicks += b - a; existsTicks += c - b; extractTicks += d - c;
            if (extension) side.ExtensionTypes++;
            if (full) side.FullFallbacks++;
            side.SurfaceTypes += surface.Types.Count;
            foreach (var type in surface.Types) side.SurfaceMembers += type.Members.Count;
            side.Types[fullName] = (handle, surface, extension, (d - c) * 1000.0 / Stopwatch.Frequency);
        }
        side.ResolveMs = resolveTicks * 1000.0 / Stopwatch.Frequency;
        side.ExistsMs = existsTicks * 1000.0 / Stopwatch.Frequency;
        side.ExtractMs = extractTicks * 1000.0 / Stopwatch.Frequency;
        return side;
    }
}

sealed class ApiCompare
{
    public Dictionary<string, (FastDiffState State, string? Reason)> States { get; } = new(StringComparer.Ordinal);
    public int Compared, ChangedMembers;

    public static ApiCompare Run(ApiSide before, ApiSide after, ApiDiffOptions options)
    {
        var run = new ApiCompare();
        foreach (var (name, x) in before.Types)
        {
            bool xs = x.Surface!.Types.Any(type => type.FullName == name);
            if (!after.Types.TryGetValue(name, out var y))
            {
                if (xs) run.States[name] = (FastDiffState.Changed, "removed");
                continue;
            }
            bool ys = y.Surface!.Types.Any(type => type.FullName == name);
            if (!xs && !ys)
                continue;
            run.Compared++;
            var comparison = MetadataFindings.CompareApi(x.Surface, y.Surface, new FindingSubject("api", "API"), options);
            int members = ChangedMembersOf(comparison, name);
            run.ChangedMembers += members;
            if (TypeApiChanged(comparison, name))
                run.States[name] = (FastDiffState.Changed, $"members={members}");
        }
        foreach (var (name, y) in after.Types)
        {
            if (!before.Types.ContainsKey(name) && y.Surface!.Types.Any(type => type.FullName == name))
                run.States[name] = (FastDiffState.Changed, "added");
        }
        return run;
    }

    static bool TypeApiChanged(ApiFindingComparison comparison, string name)
    {
        if (comparison.ApiDiff.TypeDiffs.Any(diff => diff.TypeFullName == name))
            return true;
        if (comparison.Types.Value is FindingComparison<ApiTypeHandle>.Complete types
            && types.Pairs.Any(pair => pair.Kind != PairKind.Present && Payload(pair).TypeFullName == name))
            return true;
        if (comparison.Members.Value is FindingComparison<ApiMemberHandle>.Complete members
            && members.Pairs.Any(pair => pair.Kind != PairKind.Present && Payload(pair).TypeFullName == name))
            return true;
        return comparison.Types.Value is not FindingComparison<ApiTypeHandle>.Complete
            || comparison.Members.Value is not FindingComparison<ApiMemberHandle>.Complete;
    }

    static int ChangedMembersOf(ApiFindingComparison comparison, string name)
        => comparison.Members.Value is FindingComparison<ApiMemberHandle>.Complete members
            ? members.Pairs.Count(pair => pair.Kind != PairKind.Present && Payload(pair).TypeFullName == name)
            : 0;

    static T Payload<T>(PairFinding<T> pair) where T : notnull
        => (((IPairFinding)pair).New as Finding<T> ?? (Finding<T>)((IPairFinding)pair).Old!).Payload;
}

// The exact-Type request path (#9466), as exp/type-diff-cost 6b178603b's g2exact runs it.
static class ExactTypeApi
{
    public static TypeDefinitionHandle Resolve(MetadataReader md, MetadataTypeDefinitionName type, ApiSurfaceExtractionBounds bounds)
        => MetadataExactTypeDefinitionInspection.Resolve(md, type, bounds, CancellationToken.None)
            is MetadataExactTypeDefinitionResolution.Resolved resolved
                ? resolved.Handle
                : default;

    static MetadataExtensionReceiverSelection Receiver(
        MetadataReader md, AssemblyReferenceIdentity identity, MetadataTypeDefinitionName type, TypeDefinitionHandle handle)
        => new(identity, type, handle.IsNil ? null : MetadataTypeDefinitionAddress.FromHandle(md, handle));

    public static bool? ExtensionExists(
        PEReader pe, MetadataReader md, AssemblyReferenceIdentity identity,
        MetadataTypeDefinitionName type, TypeDefinitionHandle handle)
        => MetadataRelationInspection.ExecuteExtensionPresence(
                pe,
                md,
                new MetadataExtensionRelationPresenceRequest(
                    Receiver(md, identity, type, handle),
                    new MetadataOperationPolicy(int.MaxValue),
                    includeNonPublic: false,
                    sourceAssembly: identity),
                CancellationToken.None)
            is MetadataExtensionRelationPresenceOutcome.Available available
                ? available.Exists
                : null;

    static HashSet<TypeDefinitionHandle>? ExtensionDeclaringTypes(
        PEReader pe, MetadataReader md, AssemblyReferenceIdentity identity,
        MetadataTypeDefinitionName type, TypeDefinitionHandle handle)
    {
        MetadataExtensionRelationPopulationOutcome outcome = MetadataRelationInspection.ExecuteExtensionPopulation(
            pe,
            md,
            new MetadataExtensionRelationPopulationRequest(
                Receiver(md, identity, type, handle),
                new MetadataOperationPolicy(int.MaxValue),
                rows: new MetadataExtensionRelationPopulationRowsRequest(0, int.MaxValue)),
            CancellationToken.None);
        if (outcome is not MetadataExtensionRelationPopulationOutcome.Available
            {
                Result.Rows: MetadataExtensionRelationPopulationRowsOutcome.Read { NextOrdinal: null } read,
            })
        {
            return null;
        }
        var declaring = new HashSet<TypeDefinitionHandle>();
        foreach (MetadataExtensionRelationPopulationRow row in read.Items)
        {
            foreach (MetadataExtensionRelationEvidence occurrence in row.Occurrences)
            {
                if (!occurrence.DeclaringType.TryResolve(md, out TypeDefinitionHandle declaringType))
                    return null;
                declaring.Add(declaringType);
            }
        }
        return declaring;
    }

    public static ApiSurface Extract(
        PEReader pe, MetadataReader md, AssemblyReferenceIdentity identity, ApiSurfaceImageCache cache,
        MetadataTypeDefinitionName type, TypeDefinitionHandle handle, bool extensionMode, out bool full)
    {
        full = false;
        if (!extensionMode)
            return ApiSurfaceExtractor.ExtractDeclarations(pe, ApiSurfaceExtractionScope.Public, candidate => candidate == handle, cache);
        HashSet<TypeDefinitionHandle>? declaring = ExtensionDeclaringTypes(pe, md, identity, type, handle);
        if (declaring is null)
        {
            full = true;
            return ApiSurfaceExtractor.Extract(pe, ApiSurfaceExtractionScope.Public);
        }
        return ApiSurfaceExtractor.ExtractDeclarations(
            pe,
            ApiSurfaceExtractionScope.Public,
            candidate =>
            {
                if (candidate == handle)
                    return true;
                for (TypeDefinitionHandle current = candidate; !current.IsNil; current = md.GetTypeDefinition(current).GetDeclaringType())
                {
                    if (declaring.Contains(current))
                        return true;
                }
                return false;
            },
            cache,
            includeLocalExtensionProjections: true);
    }
}
