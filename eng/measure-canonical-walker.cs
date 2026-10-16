#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj

// SPIKE (exp/staged-diff-spike), round 3. Fast Diff's allocation-free raw
// lockstep IL walk, taught the canonical IL comparison's rules
// (docs/design/il-diff-canonicalization.md), as THE Body predicate:
//
//   * opcode macro folding: ldc.i4.m1..8 / ldc.i4.s / ldc.i4 fold to one
//     family carrying the value; short branches fold to their long family;
//     every other opcode (ldarg.0 vs ldarg.s, ldloc.s vs ldloc) stays distinct;
//   * branch and switch targets compare by instruction index (identity
//     alignment, which is what Exact means), falling back to raw offsets when a
//     target is not an instruction start; switch target counts must match;
//   * metadata token operands compare symbolically (Fast Diff's memoised keys);
//     user strings by value; stand-alone signatures (calli) by signature bytes,
//     as canonical IL does (#9801);
//   * body size, encoding, locals, maxstack, init locals and exception
//     regions are not compared (canonical IL ignores them); raw slots compare
//     raw, as canonical IL does by default.
//
// The shipped FastDiff is untouched; this walker is a spike-only copy.
// Usage: measure-canonical-walker <before.dll> <after.dll> [iterations] [topTypes] [extraType...]
//        measure-canonical-walker --synthetic

using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using ILInspector.ILDiff;
using ILInspector.Metadata;
using Inspector.Findings;

if (args.Length == 1 && args[0] == "--synthetic")
    return Synthetic.Run();
if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: measure-canonical-walker <before.dll> <after.dll> [iterations] [topTypes] [extraType...] | --synthetic");
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

double fastMs = Median(iterations, () =>
{
    using var b = Open(beforeBytes);
    using var a = Open(afterBytes);
    long t = Stopwatch.GetTimestamp();
    FastDiff.Compare(b, a);
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

(double Ms, Times Times, long Bytes, Run Run) Measure(int n, Mode mode, BodyEngine engine, bool apiAxis, bool bodyPlus = false)
{
    var times = new List<Times>();
    var bytes = new List<double>();
    Run last = default!;
    double ms = Median(n, () =>
    {
        using var b = Open(beforeBytes);
        using var a = Open(afterBytes);
        long alloc = GC.GetAllocatedBytesForCurrentThread();
        long t = Stopwatch.GetTimestamp();
        last = Shared.Library(b, a, options, mode, bodyPlus, engine, apiAxis);
        double total = Ms(t);
        bytes.Add(GC.GetAllocatedBytesForCurrentThread() - alloc);
        times.Add(last.Times);
        return total;
    });
    return (ms, Med(times), (long)MedianOf(bytes), last);
}

var bodyRaw = Measure(iterations, Mode.Exists, BodyEngine.Raw, apiAxis: false);
var bodyWalker = Measure(iterations, Mode.Exists, BodyEngine.Walker, apiAxis: false);
var bodyLite = Measure(iterations, Mode.Exists, BodyEngine.Lite, apiAxis: false);
var bodyWalkerPlus = Measure(iterations, Mode.Exists, BodyEngine.Walker, apiAxis: false, bodyPlus: true);
var exists = Measure(iterations, Mode.Exists, BodyEngine.Walker, apiAxis: true);
var complete = Measure(few, Mode.Complete, BodyEngine.Walker, apiAxis: true);

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
void Print(string label, (double Ms, Times T, long Bytes, Run Run) m)
    => Console.WriteLine(
        $"ms\t{label}={m.Ms:F2}\talloc={m.Bytes / 1024.0 / 1024.0:F1}MB\tunits={m.T.Units:F2}\tapiCensus={m.T.ApiCensus:F2}\tapiModel={m.T.ApiModel:F2}"
            + $"\tbodyKeys={m.T.BodyKeys:F2}\tbodyIl={m.T.BodyIl:F2}\tbodyPlus={m.T.BodyPlus:F2}"
            + $"\texplainApi={m.T.ExplainApi:F2}\texplainIl={m.T.ExplainIl:F2}\tresidual={m.T.Residual:F2}\tbodies={m.Run.Bodies}");

Console.WriteLine($"ms\tfast(ApiAndBody)={fastMs:F2}\tfast(Api)={fastApiMs:F2}\tfast(Body, by difference)={fastMs - fastApiMs:F2}");
Print("body-only[raw Fast Diff rules]", bodyRaw);
Print("body-only[canonical walker]", bodyWalker);
Print("body-only[round-2 lite canonical]", bodyLite);
Print("body-only[canonical walker + non-public metadata/headers]", bodyWalkerPlus);
Print("exists[census+model API, walker Body]", exists);
Print("complete[predicate+explain, walker first pass]", complete);
Console.WriteLine($"ms\ttoday-complete-api={todayApiMs:F2}\ttoday-complete-il={todayIlMs:F2}\ttoday-both={todayApiMs + todayIlMs:F2}");

// Type verdicts: walker vs lite (same Body definition).
{
    int diff = 0;
    foreach (var (name, v) in bodyWalker.Run.Types)
        if (bodyLite.Run.Types.TryGetValue(name, out var w) && w.Body != v.Body)
        {
            diff++;
            if (diff <= 5)
                Console.WriteLine($"  type-body-diff\t{name}\twalker={v.Body}\tlite={w.Body}");
        }
    Console.WriteLine($"gate-type-body\twalkerVsLiteTypeVerdictDiffs={diff}");
}

// Per-method gate: walker verdict vs IlBodyDiff.Compare over every paired method with bodies.
{
    using var b = Open(beforeBytes);
    using var a = Open(afterBytes);
    var g = Gate.Run(b, a);
    Console.WriteLine($"gate-method\tpairs={g.Pairs}\texact={g.Exact}\tchanged={g.Changed}\tunavailable={g.Unavailable}"
        + $"\tmismatch={g.Mismatches.Count}\twalkerInstructions={g.Instructions}");
    foreach (var m in g.Mismatches.Take(10))
        Console.WriteLine($"  method-mismatch\t{m}");
    Console.WriteLine($"raw-vs-canonical\tpairsWhereFastDiffRulesSayUnequalButCanonicalExact={g.RawStricter}\tcanonicalUnequalButRawEqual={g.RawLooser}");
}

// Complete output equality, walker first pass.
{
    var todayLines = Shared.ApiLines(todayApi);
    var mineLines = Shared.ApiLines(complete.Run.Api!);
    var (onlyToday, onlyMine) = MultisetDiff(todayLines, mineLines);
    Console.WriteLine($"complete-api\ttodayLines={todayLines.Count}\tpredicateLines={mineLines.Count}\tonlyToday={onlyToday.Count}\tonlyPredicate={onlyMine.Count}");
    var todayIlLines = todayIl.Examples.Select(e => e.Method + "\n" + string.Join("\n", e.Diff.Rows.Select(r => r.Message))).ToList();
    var mineIlLines = complete.Run.IlExamples.Select(e => e.Method + "\n" + string.Join("\n", e.Rows)).ToList();
    var (ilOnlyToday, ilOnlyMine) = MultisetDiff(todayIlLines, mineIlLines);
    Console.WriteLine(
        $"complete-il\ttodayChanged={todayIl.ChangedBodyCount}\tpredicateChanged={complete.Run.IlExamples.Count}"
            + $"\ttodayUnavailable={todayIl.PairUnavailableCount}\tpredicateUnavailable={complete.Run.Unavailable}"
            + $"\tonlyToday={ilOnlyToday.Count}\tonlyPredicate={ilOnlyMine.Count}\tpredicateFalseButExact={complete.Run.FalseButExact}");
    foreach (var l in ilOnlyToday.Take(3))
        Console.WriteLine($"  il-only-today\t{l.Replace('\n', '|')}");
}

using (var pb = Open(beforeBytes))
using (var pa = Open(afterBytes))
{
    var names = exists.Run.Types
        .Where(kv => kv.Value.Present && (kv.Value.Api == FastDiffState.Changed || kv.Value.Body == FastDiffState.Changed))
        .OrderByDescending(kv => kv.Value.Methods)
        .Take(topTypes)
        .Select(kv => kv.Key)
        .Concat(extraTypes.Where(exists.Run.Types.ContainsKey))
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

/// <summary>
/// Fast Diff's lockstep walk with canonical rules, allocation-free per
/// instruction. Per-body scratch (instruction start offsets, deferred branch
/// targets) lives in arrays retained by the walker and grown by doubling, so
/// steady state allocates nothing; user strings are memoised per side.
/// </summary>
sealed class CanonicalWalker(FastDiff.Side a, FastDiff.Side b, bool canonical)
{
    int[] _offA = new int[256], _offB = new int[256];
    int[] _tgtA = new int[64], _tgtB = new int[64];
    readonly Dictionary<int, string> _strA = [], _strB = [];
    public long Instructions;

    public IlExistsOutcome Compare(MethodBodyBlock x, MethodBodyBlock y)
    {
        try
        {
            if (!canonical && !RawHeaderEqual(x, y))
                return IlExistsOutcome.Changed;
            return Walk(x.GetILReader(), y.GetILReader());
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            a.Keys.Reset();
            b.Keys.Reset();
            return IlExistsOutcome.Unavailable;
        }
    }

    /// <summary>Fast Diff's header facts, compared only in raw (Fast Diff rules) mode.</summary>
    bool RawHeaderEqual(MethodBodyBlock x, MethodBodyBlock y)
    {
        if (x.MaxStack != y.MaxStack || x.LocalVariablesInitialized != y.LocalVariablesInitialized || x.Size != y.Size
            || x.LocalSignature.IsNil != y.LocalSignature.IsNil
            || (!x.LocalSignature.IsNil && a.Key(x.LocalSignature) != b.Key(y.LocalSignature)))
            return false;
        var rx = x.ExceptionRegions;
        var ry = y.ExceptionRegions;
        if (rx.Length != ry.Length)
            return false;
        for (int i = 0; i < rx.Length; i++)
        {
            var p = rx[i];
            var q = ry[i];
            if (p.Kind != q.Kind || p.TryOffset != q.TryOffset || p.TryLength != q.TryLength
                || p.HandlerOffset != q.HandlerOffset || p.HandlerLength != q.HandlerLength || p.FilterOffset != q.FilterOffset
                || p.CatchType.IsNil != q.CatchType.IsNil || (!p.CatchType.IsNil && a.Key(p.CatchType) != b.Key(q.CatchType)))
                return false;
        }
        return true;
    }

    static int ReadOpCode(ref BlobReader r)
    {
        int op = r.ReadByte();
        if (op == 0xFE)
            op = 0xFE00 | r.ReadByte();
        return op;
    }

    // Short branch -> long family (br.s..blt.un.s are 0x2B..0x37, long forms +13; leave.s 0xDE -> leave 0xDD).
    static int Family(int op) => op switch
    {
        >= 0x2B and <= 0x37 => op + 13,
        0xDE => 0xDD,
        _ => op,
    };

    static bool IsLdcI4(int op) => op is >= 0x15 and <= 0x20;

    static int LdcValue(int op, ref BlobReader r) => op switch
    {
        0x15 => -1,
        >= 0x16 and <= 0x1E => op - 0x16,
        0x1F => r.ReadSByte(),
        _ => r.ReadInt32(),
    };

    void PushTarget(int n, int ta, int tb)
    {
        if (n >= _tgtA.Length)
        {
            Array.Resize(ref _tgtA, _tgtA.Length * 2);
            Array.Resize(ref _tgtB, _tgtB.Length * 2);
        }
        _tgtA[n] = ta;
        _tgtB[n] = tb;
    }

    IlExistsOutcome Walk(BlobReader ra, BlobReader rb)
    {
        int n = 0;
        int targets = 0;
        while (ra.RemainingBytes > 0 && rb.RemainingBytes > 0)
        {
            if (n >= _offA.Length)
            {
                Array.Resize(ref _offA, _offA.Length * 2);
                Array.Resize(ref _offB, _offB.Length * 2);
            }
            _offA[n] = ra.Offset;
            _offB[n] = rb.Offset;
            n++;
            Instructions++;
            int opA = ReadOpCode(ref ra);
            int opB = ReadOpCode(ref rb);
            if (canonical && (IsLdcI4(opA) || IsLdcI4(opB)))
            {
                if (!IsLdcI4(opA) || !IsLdcI4(opB) || LdcValue(opA, ref ra) != LdcValue(opB, ref rb))
                    return IlExistsOutcome.Changed;
                continue;
            }
            if (canonical ? Family(opA) != Family(opB) : opA != opB)
                return IlExistsOutcome.Changed;

            switch (Operand(opA))
            {
                case Kind.None:
                    break;
                case Kind.Int8:
                    if (ra.ReadByte() != rb.ReadByte()) return IlExistsOutcome.Changed;
                    break;
                case Kind.Int16:
                    if (ra.ReadUInt16() != rb.ReadUInt16()) return IlExistsOutcome.Changed;
                    break;
                case Kind.Int32:
                    if (ra.ReadInt32() != rb.ReadInt32()) return IlExistsOutcome.Changed;
                    break;
                case Kind.Int64:
                    if (ra.ReadInt64() != rb.ReadInt64()) return IlExistsOutcome.Changed;
                    break;
                case Kind.Branch:
                {
                    int da = IsShort(opA) ? ra.ReadSByte() : ra.ReadInt32();
                    int db = IsShort(opB) ? rb.ReadSByte() : rb.ReadInt32();
                    if (!canonical)
                    {
                        if (da != db) return IlExistsOutcome.Changed;
                        break;
                    }
                    PushTarget(targets++, ra.Offset + da, rb.Offset + db);
                    break;
                }
                case Kind.Switch:
                {
                    int count = ra.ReadInt32();
                    if (count != rb.ReadInt32()) return IlExistsOutcome.Changed;
                    int baseA = ra.Offset + 4 * count;
                    int baseB = rb.Offset + 4 * count;
                    for (int i = 0; i < count; i++)
                    {
                        int da = ra.ReadInt32();
                        int db = rb.ReadInt32();
                        if (!canonical)
                        {
                            if (da != db) return IlExistsOutcome.Changed;
                            continue;
                        }
                        PushTarget(targets++, baseA + da, baseB + db);
                    }
                    break;
                }
                case Kind.String:
                    if (!string.Equals(UserString(a, _strA, ra.ReadInt32()), UserString(b, _strB, rb.ReadInt32()), StringComparison.Ordinal))
                        return IlExistsOutcome.Changed;
                    break;
                case Kind.Sig:
                {
                    int ta = ra.ReadInt32();
                    int tb = rb.ReadInt32();
                    if (canonical ? !SignatureBytesEqual(ta, tb) : a.Key(MetadataTokens.EntityHandle(ta)) != b.Key(MetadataTokens.EntityHandle(tb)))
                        return IlExistsOutcome.Changed;
                    break;
                }
                case Kind.Token:
                    if (a.Key(MetadataTokens.EntityHandle(ra.ReadInt32())) != b.Key(MetadataTokens.EntityHandle(rb.ReadInt32())))
                        return IlExistsOutcome.Changed;
                    break;
            }
        }
        if (ra.RemainingBytes != 0 || rb.RemainingBytes != 0)
            return IlExistsOutcome.Changed;

        // Exact means identity alignment: each target must be the instruction at the same index,
        // or, where a target is not an instruction start, the same raw offset (canonical fallback).
        for (int i = 0; i < targets; i++)
        {
            int ia = Array.BinarySearch(_offA, 0, n, _tgtA[i]);
            int ib = Array.BinarySearch(_offB, 0, n, _tgtB[i]);
            if (ia < 0 || ib < 0)
            {
                if (_tgtA[i] != _tgtB[i]) return IlExistsOutcome.Changed;
            }
            else if (ia != ib)
            {
                return IlExistsOutcome.Changed;
            }
        }
        return IlExistsOutcome.Exact;
    }

    static string UserString(FastDiff.Side side, Dictionary<int, string> memo, int token)
    {
        if (!memo.TryGetValue(token, out var s))
        {
            s = side.Md.GetUserString(MetadataTokens.UserStringHandle(token & 0xFFFFFF));
            memo[token] = s;
        }
        return s;
    }

    /// <summary>Canonical IL compares a stand-alone signature by its bytes (#9801).</summary>
    bool SignatureBytesEqual(int ta, int tb)
    {
        var ha = MetadataTokens.EntityHandle(ta);
        var hb = MetadataTokens.EntityHandle(tb);
        if (ha.Kind != HandleKind.StandaloneSignature || hb.Kind != HandleKind.StandaloneSignature)
            throw new BadImageFormatException("calli operand is not a stand-alone signature");
        var x = a.Md.GetBlobReader(a.Md.GetStandaloneSignature((StandaloneSignatureHandle)ha).Signature);
        var y = b.Md.GetBlobReader(b.Md.GetStandaloneSignature((StandaloneSignatureHandle)hb).Signature);
        if (x.Length != y.Length)
            return false;
        while (x.RemainingBytes > 0)
            if (x.ReadByte() != y.ReadByte())
                return false;
        return true;
    }

    static bool IsShort(int op) => op is (>= 0x2B and <= 0x37) or 0xDE;

    enum Kind { None, Int8, Int16, Int32, Int64, Branch, Switch, String, Token, Sig }

    static Kind Operand(int op) => (ILOpCode)op switch
    {
        ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s
            or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
            or ILOpCode.Unaligned or (ILOpCode)0xFE19 => Kind.Int8,
        ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg
            or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc => Kind.Int16,
        ILOpCode.Ldc_i4_s => Kind.Int8,
        ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4 => Kind.Int32,
        ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8 => Kind.Int64,
        >= ILOpCode.Br_s and <= ILOpCode.Blt_un_s or ILOpCode.Leave_s
            or >= ILOpCode.Br and <= ILOpCode.Blt_un or ILOpCode.Leave => Kind.Branch,
        ILOpCode.Switch => Kind.Switch,
        ILOpCode.Ldstr => Kind.String,
        ILOpCode.Calli => Kind.Sig,
        ILOpCode.Jmp or ILOpCode.Call or ILOpCode.Callvirt
            or ILOpCode.Cpobj or ILOpCode.Ldobj or ILOpCode.Newobj
            or ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Unbox
            or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld
            or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
            or ILOpCode.Stobj or ILOpCode.Box or ILOpCode.Newarr
            or ILOpCode.Ldelema or ILOpCode.Ldelem or ILOpCode.Stelem
            or ILOpCode.Unbox_any or ILOpCode.Refanyval or ILOpCode.Mkrefany
            or ILOpCode.Ldtoken or ILOpCode.Ldftn or ILOpCode.Ldvirtftn
            or ILOpCode.Initobj or ILOpCode.Constrained or ILOpCode.Sizeof => Kind.Token,
        _ when Known(op) => Kind.None,
        _ => throw new BadImageFormatException($"Unknown IL opcode 0x{op:X}."),
    };

    static readonly HashSet<int> KnownOps = [.. Enum.GetValues<ILOpCode>().Select(o => (int)o)];
    static bool Known(int op) => KnownOps.Contains(op);
}

/// <summary>Per-method gate over every method pair (Fast Diff key pairing, proven equal to the oracle's in round 2).</summary>
static class Gate
{
    public sealed class Result
    {
        public int Pairs, Exact, Changed, Unavailable, RawStricter, RawLooser;
        public long Instructions;
        public List<string> Mismatches { get; } = [];
    }

    public static Result Run(PEReader before, PEReader after)
    {
        var r = new Result();
        var a = new FastDiff.Side(before);
        var b = new FastDiff.Side(after);
        var walker = new CanonicalWalker(a, b, canonical: true);
        var raw = new CanonicalWalker(a, b, canonical: false);
        var mb = new Dictionary<SymbolKey, MethodDefinitionHandle>();
        foreach (var h in b.Md.MethodDefinitions)
            try { mb.TryAdd(b.Key(h), h); } catch (BadImageFormatException) { b.Keys.Reset(); }
        foreach (var ha in a.Md.MethodDefinitions)
        {
            SymbolKey k;
            try { k = a.Key(ha); } catch (BadImageFormatException) { a.Keys.Reset(); continue; }
            if (!mb.TryGetValue(k, out var hb))
                continue;
            var da = a.Md.GetMethodDefinition(ha);
            var db = b.Md.GetMethodDefinition(hb);
            if (da.RelativeVirtualAddress == 0 || db.RelativeVirtualAddress == 0)
                continue;
            var x = a.Pe.GetMethodBody(da.RelativeVirtualAddress);
            var y = b.Pe.GetMethodBody(db.RelativeVirtualAddress);
            r.Pairs++;
            var canonicalResult = IlBodyDiff.Compare(a.Md, x, b.Md, y, Shared.Normalization);
            var expected = !canonicalResult.IsAvailable ? IlExistsOutcome.Unavailable
                : canonicalResult.IsExact ? IlExistsOutcome.Exact : IlExistsOutcome.Changed;
            var got = walker.Compare(x, y);
            var rawGot = raw.Compare(x, y);
            switch (expected)
            {
                case IlExistsOutcome.Exact: r.Exact++; break;
                case IlExistsOutcome.Changed: r.Changed++; break;
                default: r.Unavailable++; break;
            }
            if (expected == IlExistsOutcome.Exact && rawGot != IlExistsOutcome.Exact) r.RawStricter++;
            if (expected == IlExistsOutcome.Changed && rawGot == IlExistsOutcome.Exact) r.RawLooser++;
            if (got != expected)
                r.Mismatches.Add($"{IlAssemblyDiff.SpikeMethodIdentity(a.Md, ha)}\twalker={got}\tcanonical={expected}"
                    + $"\t{string.Join(" | ", canonicalResult.Rows.Take(2).Select(row => row.Message))}{canonicalResult.Failure}");
        }
        r.Instructions = walker.Instructions;
        return r;
    }
}

/// <summary>
/// A generated image pair whose methods differ only in IL encoding, or by a real change,
/// written with System.Reflection.Metadata's InstructionEncoder.
/// </summary>
static class Synthetic
{
    // (name, expected canonical-equal, emit before, emit after)
    static readonly (string Name, bool Equal, Action<InstructionEncoder, Ctx> Before, Action<InstructionEncoder, Ctx> After)[] Cases =
    [
        ("br.s vs br", true, (il, c) => BranchToRet(il, ILOpCode.Br_s), (il, c) => BranchToRet(il, ILOpCode.Br)),
        ("brtrue.s vs brtrue", true, (il, c) => CondBranch(il, ILOpCode.Brtrue_s), (il, c) => CondBranch(il, ILOpCode.Brtrue)),
        ("leave.s vs leave (no EH, shape only)", true, (il, c) => BranchToRet(il, ILOpCode.Leave_s), (il, c) => BranchToRet(il, ILOpCode.Leave)),
        ("ldc.i4.s 5 vs ldc.i4 5", true, (il, c) => Ldc(il, ILOpCode.Ldc_i4_s, 5), (il, c) => Ldc(il, ILOpCode.Ldc_i4, 5)),
        ("ldc.i4.5 vs ldc.i4.s 5", true, (il, c) => Ldc(il, ILOpCode.Ldc_i4_5, 5), (il, c) => Ldc(il, ILOpCode.Ldc_i4_s, 5)),
        ("ldc.i4.m1 vs ldc.i4 -1", true, (il, c) => Ldc(il, ILOpCode.Ldc_i4_m1, -1), (il, c) => Ldc(il, ILOpCode.Ldc_i4, -1)),
        ("shifted offsets: br.s->br before a later branch target", true, (il, c) => Shifted(il, ILOpCode.Br_s), (il, c) => Shifted(il, ILOpCode.Br)),
        ("switch with shifted targets", true, (il, c) => Switch(il, 2, pad: false), (il, c) => Switch(il, 2, pad: true)),
        ("ldarg.1 vs ldarg.s 1 (distinct families by contract)", false, (il, c) => Ldarg(il, ILOpCode.Ldarg_1), (il, c) => Ldarg(il, ILOpCode.Ldarg_s)),
        ("ldarg.s 1 vs ldarg 1 (distinct families by contract)", false, (il, c) => Ldarg(il, ILOpCode.Ldarg_s), (il, c) => Ldarg(il, ILOpCode.Ldarg)),
        ("ldc.i4 5 vs ldc.i4 6", false, (il, c) => Ldc(il, ILOpCode.Ldc_i4, 5), (il, c) => Ldc(il, ILOpCode.Ldc_i4, 6)),
        ("ldc.i4.s 5 vs ldc.i4.6", false, (il, c) => Ldc(il, ILOpCode.Ldc_i4_s, 5), (il, c) => Ldc(il, ILOpCode.Ldc_i4_6, 6)),
        ("branch retargeted", false, (il, c) => Retarget(il, first: true), (il, c) => Retarget(il, first: false)),
        ("switch target count change", false, (il, c) => Switch(il, 2, pad: false), (il, c) => Switch(il, 3, pad: false)),
        ("call target changed", false, (il, c) => Call(il, c.Callee1), (il, c) => Call(il, c.Callee2)),
        ("ldstr changed", false, (il, c) => Str(il, c.S1), (il, c) => Str(il, c.S2)),
        ("instruction inserted", false, (il, c) => { il.OpCode(ILOpCode.Nop); il.OpCode(ILOpCode.Ret); }, (il, c) => { il.OpCode(ILOpCode.Nop); il.OpCode(ILOpCode.Nop); il.OpCode(ILOpCode.Ret); }),
    ];

    sealed class Ctx
    {
        public MethodDefinitionHandle Callee1, Callee2;
        public UserStringHandle S1, S2;
    }

    static void BranchToRet(InstructionEncoder il, ILOpCode op)
    {
        var end = il.DefineLabel();
        il.Branch(op, end);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
    }

    static void CondBranch(InstructionEncoder il, ILOpCode op)
    {
        var end = il.DefineLabel();
        il.LoadArgument(0);
        il.Branch(op, end);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
    }

    static void Shifted(InstructionEncoder il, ILOpCode firstBranch)
    {
        var mid = il.DefineLabel();
        var end = il.DefineLabel();
        il.Branch(firstBranch, mid);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(mid);
        il.LoadArgument(0);
        il.Branch(ILOpCode.Brfalse_s, end);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(end);
        il.OpCode(ILOpCode.Ret);
    }

    static void Retarget(InstructionEncoder il, bool first)
    {
        var a = il.DefineLabel();
        var b = il.DefineLabel();
        il.Branch(ILOpCode.Br_s, first ? a : b);
        il.MarkLabel(a);
        il.OpCode(ILOpCode.Nop);
        il.MarkLabel(b);
        il.OpCode(ILOpCode.Ret);
    }

    static void Switch(InstructionEncoder il, int count, bool pad)
    {
        il.LoadArgument(0);
        var labels = Enumerable.Range(0, count).Select(_ => il.DefineLabel()).ToArray();
        var sw = il.Switch(count);
        foreach (var l in labels)
            sw.Branch(l);
        if (pad)
        {
            // Encoding-only padding: a long branch in place of a short one before the targets.
            var skip = il.DefineLabel();
            il.Branch(ILOpCode.Br, skip);
            il.MarkLabel(skip);
        }
        else
        {
            var skip = il.DefineLabel();
            il.Branch(ILOpCode.Br_s, skip);
            il.MarkLabel(skip);
        }
        foreach (var l in labels)
        {
            il.MarkLabel(l);
            il.OpCode(ILOpCode.Nop);
        }
        il.OpCode(ILOpCode.Ret);
    }

    static void Ldc(InstructionEncoder il, ILOpCode op, int value)
    {
        il.OpCode(op);
        if (op == ILOpCode.Ldc_i4_s) il.CodeBuilder.WriteSByte((sbyte)value);
        else if (op == ILOpCode.Ldc_i4) il.CodeBuilder.WriteInt32(value);
        il.OpCode(ILOpCode.Pop);
        il.OpCode(ILOpCode.Ret);
    }

    static void Ldarg(InstructionEncoder il, ILOpCode op)
    {
        il.OpCode(op);
        if (op == ILOpCode.Ldarg_s) il.CodeBuilder.WriteByte(1);
        else if (op == ILOpCode.Ldarg) il.CodeBuilder.WriteUInt16(1);
        il.OpCode(ILOpCode.Pop);
        il.OpCode(ILOpCode.Ret);
    }

    static void Call(InstructionEncoder il, MethodDefinitionHandle callee)
    {
        il.Call(callee);
        il.OpCode(ILOpCode.Ret);
    }

    static void Str(InstructionEncoder il, UserStringHandle s)
    {
        il.LoadString(s);
        il.OpCode(ILOpCode.Pop);
        il.OpCode(ILOpCode.Ret);
    }

    static byte[] Build(bool after)
    {
        var md = new MetadataBuilder();
        var il = new BlobBuilder();
        var bodies = new MethodBodyStreamEncoder(il);
        md.AddModule(0, md.GetOrAddString("Synthetic.dll"), md.GetOrAddGuid(new Guid(after ? "22222222-2222-2222-2222-222222222222" : "11111111-1111-1111-1111-111111111111")), default, default);
        md.AddAssembly(md.GetOrAddString("Synthetic"), new Version(1, 0, 0, 0), default, default, 0, AssemblyHashAlgorithm.Sha1);
        var corlib = md.AddAssemblyReference(md.GetOrAddString("System.Runtime"), new Version(10, 0, 0, 0), default, default, 0, default);
        var obj = md.AddTypeReference(corlib, md.GetOrAddString("System"), md.GetOrAddString("Object"));
        md.AddTypeDefinition(default, default, md.GetOrAddString("<Module>"), default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var sig = new BlobBuilder();
        new BlobEncoder(sig).MethodSignature().Parameters(2, r => r.Void(), p => { p.AddParameter().Type().Int32(); p.AddParameter().Type().Int32(); });
        var sigHandle = md.GetOrAddBlob(sig);

        // Callees first so their handles are known: rows 1 and 2.
        int row = 1;
        var ctx = new Ctx
        {
            Callee1 = MetadataTokens.MethodDefinitionHandle(row),
            Callee2 = MetadataTokens.MethodDefinitionHandle(row + 1),
            S1 = md.GetOrAddUserString("one"),
            S2 = md.GetOrAddUserString("two"),
        };
        var calleeSig = new BlobBuilder();
        new BlobEncoder(calleeSig).MethodSignature().Parameters(0, r => r.Void(), p => { });
        var calleeSigHandle = md.GetOrAddBlob(calleeSig);
        foreach (var name in new[] { "Callee1", "Callee2" })
        {
            var e = new InstructionEncoder(new BlobBuilder());
            e.OpCode(ILOpCode.Ret);
            int offset = bodies.AddMethodBody(e);
            md.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, MethodImplAttributes.IL,
                md.GetOrAddString(name), calleeSigHandle, offset, default);
            row++;
        }
        foreach (var c in Cases)
        {
            var e = new InstructionEncoder(new BlobBuilder(), new ControlFlowBuilder());
            (after ? c.After : c.Before)(e, ctx);
            int offset = bodies.AddMethodBody(e, maxStack: 8);
            md.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static | MethodAttributes.HideBySig, MethodImplAttributes.IL,
                md.GetOrAddString("M_" + new string(c.Name.Where(char.IsLetterOrDigit).ToArray())), sigHandle, offset, default);
            row++;
        }
        md.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed, md.GetOrAddString("Synthetic"),
            md.GetOrAddString("Cases"), obj, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));

        var pe = new ManagedPEBuilder(PEHeaderBuilder.CreateLibraryHeader(), new MetadataRootBuilder(md), il);
        var blob = new BlobBuilder();
        pe.Serialize(blob);
        return blob.ToArray();
    }

    public static int Run()
    {
        byte[] before = Build(after: false);
        byte[] after = Build(after: true);
        using var pb = new PEReader(new MemoryStream(before));
        using var pa = new PEReader(new MemoryStream(after));
        var a = new FastDiff.Side(pb);
        var b = new FastDiff.Side(pa);
        var walker = new CanonicalWalker(a, b, canonical: true);
        var raw = new CanonicalWalker(a, b, canonical: false);
        int failures = 0;
        var methodsB = b.Md.MethodDefinitions.ToDictionary(h => b.Md.GetString(b.Md.GetMethodDefinition(h).Name));
        int i = 0;
        foreach (var ha in a.Md.MethodDefinitions)
        {
            var da = a.Md.GetMethodDefinition(ha);
            string name = a.Md.GetString(da.Name);
            if (!name.StartsWith("M_", StringComparison.Ordinal))
                continue;
            var c = Cases[i++];
            var db = b.Md.GetMethodDefinition(methodsB[name]);
            var x = pb.GetMethodBody(da.RelativeVirtualAddress);
            var y = pa.GetMethodBody(db.RelativeVirtualAddress);
            var canonicalResult = IlBodyDiff.Compare(a.Md, x, b.Md, y, Shared.Normalization);
            bool canonicalEqual = canonicalResult.IsExact;
            bool walkerEqual = walker.Compare(x, y) == IlExistsOutcome.Exact;
            bool rawEqual = raw.Compare(x, y) == IlExistsOutcome.Exact;
            bool ok = canonicalEqual == c.Equal && walkerEqual == c.Equal;
            if (!ok) failures++;
            Console.WriteLine($"synthetic\t{(ok ? "ok  " : "FAIL")}\t{c.Name}\texpected={(c.Equal ? "equal" : "unequal")}"
                + $"\tcanonical={(canonicalEqual ? "equal" : canonicalResult.IsAvailable ? "unequal" : "unavailable")}"
                + $"\twalker={(walkerEqual ? "equal" : "unequal")}\tfastDiffRules={(rawEqual ? "equal" : "unequal")}"
                + $"\tbytes={x.Size}/{y.Size}");
        }
        Console.WriteLine($"synthetic\tcases={i}\tfailures={failures}");
        return failures == 0 ? 0 : 1;
    }
}

enum Mode { Exists, Complete }

enum BodyEngine { Lite, Walker, Raw }

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

    public static Run Library(PEReader before, PEReader after, ApiDiffOptions options, Mode mode, bool bodyPlus, BodyEngine engine = BodyEngine.Walker, bool apiAxis = true)
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
        var walker = new CanonicalWalker(a, b, canonical: engine != BodyEngine.Raw);
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
            if (!apiAxis)
                continue;
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
            CompareBodies(run, a, b, ua, ub, Rows(ua), Rows(ub), v, il, walker, engine, mode, bodyPlus, times);
        }

        if (mode == Mode.Complete)
        {
            // Methods outside every unit (<Module>, top-level generated Types): compared, never reported as a Type.
            long tr = Stopwatch.GetTimestamp();
            var restA = a.Md.TypeDefinitions.Where(h => !covered.Contains(h)).ToList();
            var restB = b.Md.TypeDefinitions.Where(h => !coveredB.Contains(h)).ToList();
            var dummy = new TypeVerdict();
            CompareBodies(run, a, b, null, null, restA, restB, dummy, il, walker, engine, mode, bodyPlus: false, new Times());
            times.Residual += Ms(tr);
        }
        return run;
    }

    public static List<TypeDefinitionHandle> Rows(FastDiff.Unit? u)
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
        TypeVerdict v, IlBodyExistsComparer il, CanonicalWalker walker, BodyEngine engine, Mode mode, bool bodyPlus, Times times)
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
            string? failure = null;
            var outcome = engine == BodyEngine.Lite
                ? il.Compare(ba, bb, rawFastPath: false, memoTokens: true, liteDecode: true, out _, out failure)
                : walker.Compare(ba, bb);
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
        CompareBodies(run, a, b, ua, ub, Rows(ua), Rows(ub), v, il, new CanonicalWalker(a, b, canonical: true), BodyEngine.Walker, Mode.Exists, bodyPlus: false, new Times());
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
