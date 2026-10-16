#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj
#:project ../src/ILInspector.ILDiff/ILInspector.ILDiff.csproj

// SPIKE (exp/staged-diff-spike), round 4. The Exists API predicate decides
// from Fast Diff's census alone when a difference is in a PRESENTED fact
// class (one the complete API diff always reports), and builds the API
// surface model only for Types whose census differences are all UNCERTAIN.
// The Body predicate is round 3's canonical walker. Classes are named in
// Census.Categories; DEMOTE=cat1,cat2 moves classes to UNCERTAIN.
//
// Usage: measure-census-classes <before.dll> <after.dll> [iterations] [topTypes] [extraType...]

using System.Collections.Immutable;
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
    Console.Error.WriteLine("Usage: measure-census-classes <before.dll> <after.dll> [iterations] [topTypes] [extraType...]");
    return 2;
}

string beforePath = args[0];
string afterPath = args[1];
int iterations = args.Length > 2 ? int.Parse(args[2]) : 9;
int topTypes = args.Length > 3 ? int.Parse(args[3]) : 0;
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
Console.WriteLine($"presented\t{string.Join(",", Census.Categories.Where(Census.IsPresented))}");
Console.WriteLine($"uncertain\t{string.Join(",", Census.Categories.Where(c => !Census.IsPresented(c)))}");

if (Environment.GetEnvironmentVariable("SPIKE_TYPE") is { Length: > 0 } debugType)
{
    using var db = Open(beforeBytes);
    using var da = Open(afterBytes);
    var sa = new FastDiff.Side(db);
    var sb = new FastDiff.Side(da);
    var ua = sa.Units().Values.First(u => u.FullName == debugType);
    var ub = sb.Units().Values.First(u => u.FullName == debugType);
    var ca = sa.ApiCensus(ua);
    var cb = sb.ApiCensus(ub);
    foreach (var f in ca.Except(cb)) Console.WriteLine("- " + f);
    foreach (var f in cb.Except(ca)) Console.WriteLine("+ " + f);
    Console.WriteLine(string.Join(",", Census.Classify(ca, cb).Presented));
    var hb = ub.Owner;
    var surf = ApiSurfaceExtractor.ExtractDeclarations(da, ApiSurfaceExtractionScope.Public, h => h == hb);
    foreach (var t in surf.Types) foreach (var m in t.Members) Console.WriteLine($"  member {m.Kind} {m.Name} hidden={m.IsHidden} {m.Signature}");
    foreach (var f in surf.InspectionFailures) Console.WriteLine($"  failure-member {f.Operation} {f.Mechanism} {f.Kind} {f.Detail}");
    long tt = Stopwatch.GetTimestamp();
    var typesOnly = ApiSurfaceExtractor.Extract(da, ApiSurfaceExtractionScope.Public, typesOnly: true);
    Console.WriteLine($"  typesOnly-member ms={Ms(tt):F1} types={typesOnly.Types.Count} hasType={typesOnly.Types.Any(t => t.FullName == debugType)}");
    var full = ApiSurfaceExtractor.Extract(da, ApiSurfaceExtractionScope.Public);
    Console.WriteLine($"  full-member types={full.Types.Count}");
    foreach (var n in new[] { "System.Runtime.CompilerServices.IsClosedTypeAttribute", "System.Runtime.CompilerServices.MemorySafetyRulesAttribute" })
        Console.WriteLine($"  check-member {n} typesOnly={typesOnly.Types.Any(t => t.FullName == n)} full={full.Types.Any(t => t.FullName == n)}");
    foreach (var t in full.Types.Where(t => t.FullName == debugType)) foreach (var m in t.Members.Where(m => m.Name.Contains("Entry"))) Console.WriteLine($"  full-member {m.Kind} {m.Name} {m.Signature}");
    return 0;
}

double fastMs = Median(iterations, () =>
{
    using var b = Open(beforeBytes);
    using var a = Open(afterBytes);
    long t = Stopwatch.GetTimestamp();
    FastDiff.Compare(b, a);
    return Ms(t);
});

(double Ms, Times Times, Run Run) Measure(int n, Mode mode, bool shortcut)
{
    var times = new List<Times>();
    Run last = default!;
    double ms = Median(n, () =>
    {
        using var b = Open(beforeBytes);
        using var a = Open(afterBytes);
        long t = Stopwatch.GetTimestamp();
        last = Shared.Library(b, a, options, mode, bodyPlus: false, BodyEngine.Walker, apiAxis: true, shortcut: shortcut);
        double total = Ms(t);
        times.Add(last.Times);
        return total;
    });
    return (ms, new Times
    {
        Units = MedianOf(times.Select(x => x.Units)),
        ApiCensus = MedianOf(times.Select(x => x.ApiCensus)),
        ApiModel = MedianOf(times.Select(x => x.ApiModel)),
        BodyKeys = MedianOf(times.Select(x => x.BodyKeys)),
        BodyIl = MedianOf(times.Select(x => x.BodyIl)),
        ExplainApi = MedianOf(times.Select(x => x.ExplainApi)),
        ExplainIl = MedianOf(times.Select(x => x.ExplainIl)),
    }, last);
}

var r3 = Measure(iterations, Mode.Exists, shortcut: false);
var r4 = Measure(iterations, Mode.Exists, shortcut: true);
var complete = Measure(few, Mode.Complete, shortcut: false);

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

void Print(string label, (double Ms, Times T, Run Run) m)
    => Console.WriteLine(
        $"ms\t{label}={m.Ms:F2}\tunits={m.T.Units:F2}\tapiCensus={m.T.ApiCensus:F2}\tapiModel={m.T.ApiModel:F2}"
            + $"\tbodyKeys={m.T.BodyKeys:F2}\tbodyIl={m.T.BodyIl:F2}\texplainApi={m.T.ExplainApi:F2}\texplainIl={m.T.ExplainIl:F2}"
            + $"\tmodelTypes={m.Run.ApiTierB}");
Console.WriteLine($"ms\tfast(ApiAndBody)={fastMs:F2}");
Print("exists-r3[model for every census difference]", r3);
Print("exists-r4[model only for uncertain]", r4);
Print("complete[predicate+explain]", complete);
Console.WriteLine($"ms\ttoday-complete-api={todayApiMs:F2}\ttoday-complete-il={todayIlMs:F2}\ttoday-both={todayApiMs + todayIlMs:F2}");

// Oracle.
var apiOracle = new HashSet<string>(StringComparer.Ordinal);
foreach (TypeDiff type in todayApi.ApiDiff.TypeDiffs)
    apiOracle.Add(type.TypeFullName);
if (todayApi.Types.Value is FindingComparison<ApiTypeHandle>.Complete oracleTypes)
    foreach (var pair in oracleTypes.Pairs)
        if (pair.Kind != PairKind.Present)
            apiOracle.Add(Payload<ApiTypeHandle>(pair).TypeFullName);
if (todayApi.Members.Value is FindingComparison<ApiMemberHandle>.Complete oracleMembers)
    foreach (var pair in oracleMembers.Pairs)
        if (pair.Kind != PairKind.Present)
            apiOracle.Add(Payload<ApiMemberHandle>(pair).TypeFullName);

// Gate (a): shortcut soundness, by class. Gate (b): Exists verdict equals the complete diff.
{
    var violations = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var used = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var sole = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var examples = new List<string>();
    foreach (var (name, cats) in r4.Run.Presented)
    {
        foreach (var c in cats.Distinct())
            used[c] = used.TryGetValue(c, out int n) ? n + 1 : 1;
        if (apiOracle.Contains(name))
            continue;
        foreach (var c in cats.Distinct())
            violations[c] = violations.TryGetValue(c, out int n) ? n + 1 : 1;
        if (cats.Distinct().Count() == 1)
            sole[cats[0]] = sole.TryGetValue(cats[0], out int n) ? n + 1 : 1;
        examples.Add($"{name}\t{string.Join(",", cats.Distinct())}");
    }
    Console.WriteLine($"gate-a\tpresentedTypes={r4.Run.Presented.Count}\tviolations={examples.Count}");
    foreach (var (c, n) in used)
        Console.WriteLine($"  class-used\t{c}\ttypes={n}\tviolations={(violations.TryGetValue(c, out int v) ? v : 0)}\tsoleCauseViolations={(sole.TryGetValue(c, out int s) ? s : 0)}");
    foreach (var e in examples.Take(12))
        Console.WriteLine($"  violation\t{e}");

    foreach (var (label, run) in new[] { ("r3", r3.Run), ("r4", r4.Run) })
    {
        int misses = run.Types.Count(kv => apiOracle.Contains(kv.Key) && kv.Value.Api != FastDiffState.Changed);
        int over = run.Types.Count(kv => !apiOracle.Contains(kv.Key) && kv.Value.Api == FastDiffState.Changed);
        int unmatched = apiOracle.Count(n => !run.Types.ContainsKey(n));
        Console.WriteLine($"gate-b\t{label}\tmisses={misses}\tover={over}\toracleUnmatched={unmatched}");
    }

    // Buckets.
    int equal = r4.Run.Types.Count - r4.Run.Presented.Count - r4.Run.Uncertain.Count;
    int confirmed = r4.Run.Uncertain.Keys.Count(n => apiOracle.Contains(n));
    int rejected = r4.Run.Uncertain.Count - confirmed;
    Console.WriteLine($"buckets\ttypes={r4.Run.Types.Count}\tequalOrNoApi={equal}\tpresentedChanged={r4.Run.Presented.Count}"
        + $"\tuncertainConfirmed={confirmed}\tuncertainRejected={rejected}");
    var rejectedCats = new SortedDictionary<string, int>(StringComparer.Ordinal);
    var confirmedCats = new SortedDictionary<string, int>(StringComparer.Ordinal);
    foreach (var (name, cats) in r4.Run.Uncertain)
        foreach (var c in cats.Distinct())
        {
            var d = apiOracle.Contains(name) ? confirmedCats : rejectedCats;
            d[c] = d.TryGetValue(c, out int n) ? n + 1 : 1;
        }
    Console.WriteLine($"  uncertain-confirmed-classes\t{string.Join(" ", confirmedCats.Select(kv => $"{kv.Key}={kv.Value}"))}");
    Console.WriteLine($"  uncertain-rejected-classes\t{string.Join(" ", rejectedCats.Select(kv => $"{kv.Key}={kv.Value}"))}");
}

// Complete output equality (unchanged method; regression check).
{
    var (onlyToday, onlyMine) = MultisetDiff(Shared.ApiLines(todayApi), Shared.ApiLines(complete.Run.Api!));
    var todayIlLines = todayIl.Examples.Select(e => e.Method + "\n" + string.Join("\n", e.Diff.Rows.Select(r => r.Message))).ToList();
    var mineIlLines = complete.Run.IlExamples.Select(e => e.Method + "\n" + string.Join("\n", e.Rows)).ToList();
    var (ilOnlyToday, ilOnlyMine) = MultisetDiff(todayIlLines, mineIlLines);
    Console.WriteLine($"complete\tapiOnlyToday={onlyToday.Count}\tapiOnlyPredicate={onlyMine.Count}\tilOnlyToday={ilOnlyToday.Count}\tilOnlyPredicate={ilOnlyMine.Count}"
        + $"\tilChanged={complete.Run.IlExamples.Count}/{todayIl.ChangedBodyCount}\tunavailable={complete.Run.Unavailable}/{todayIl.PairUnavailableCount}");
}

using (var pb = Open(beforeBytes))
using (var pa = Open(afterBytes))
{
    foreach (string name in extraTypes.Where(r4.Run.Types.ContainsKey))
    {
        foreach (bool shortcut in new[] { false, true })
        {
            TypeRun r = default!;
            double ms = Median(iterations, () =>
            {
                long t = Stopwatch.GetTimestamp();
                r = Shared.Type(pb, pa, name, options, shortcut);
                return Ms(t);
            });
            Console.WriteLine($"type\t{name}\t{(shortcut ? "r4" : "r3")}\tms={ms:F2}\tunits={r.UnitsMs:F2}\tapi={r.Api}{(r.ModelBuilt ? " (model built)" : "")}\tbody={r.Body}\tbodies={r.Bodies}");
        }
    }
}
return 0;

static T Payload<T>(PairFinding<T> pair) where T : notnull
    => (((IPairFinding)pair).New as Finding<T> ?? (Finding<T>)((IPairFinding)pair).Old!).Payload;

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
/// Classifies the differences between two API censuses of one Type by fact
/// class. A fact is [kind marker][Key subject][payload parts...]; facts of one
/// subject are compared part by part so a flags difference is told from a
/// constant or accessor difference, and a member whose key exists on one side
/// only is a presence difference.
/// </summary>
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
    public Dictionary<string, List<string>> Presented { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, List<string>> Uncertain { get; } = new(StringComparer.Ordinal);
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

    public static Run Library(PEReader before, PEReader after, ApiDiffOptions options, Mode mode, bool bodyPlus, BodyEngine engine = BodyEngine.Walker, bool apiAxis = true, bool shortcut = false)
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
                {
                    if (shortcut && Census.IsPresented("type-presence") && (ua is not null ? Census.Admission.Of(a, ua) : Census.Admission.Of(b, ub!)).TypeAdmitted)
                    {
                        v.Api = FastDiffState.Changed;
                        v.ApiReason = "presented: type-presence";
                        run.Presented[u.FullName] = ["type-presence"];
                    }
                    else
                    {
                        tierB.Add((u.FullName, ua, ub));
                        run.Uncertain[u.FullName] = ["type-presence"];
                    }
                }
                continue;
            }
            try
            {
                var ca = a.ApiCensus(ua);
                var cb = b.ApiCensus(ub);
                if (!ca.SequenceEqual(cb))
                {
                    if (shortcut)
                    {
                        var (presented, uncertain) = Census.Classify(ca, cb, Census.Admission.Of(a, ua), Census.Admission.Of(b, ub));
                        if (presented.Count > 0)
                        {
                            v.Api = FastDiffState.Changed;
                            v.ApiReason = "presented: " + string.Join(",", presented);
                            run.Presented[u.FullName] = presented;
                            continue;
                        }
                        run.Uncertain[u.FullName] = uncertain;
                    }
                    tierB.Add((u.FullName, ua, ub));
                }
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
    public static TypeRun Type(PEReader before, PEReader after, string name, ApiDiffOptions options, bool shortcut = false)
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
        var ca = a.ApiCensus(ua);
        var cb = b.ApiCensus(ub);
        if (!ca.SequenceEqual(cb) && shortcut && Census.Classify(ca, cb, Census.Admission.Of(a, ua), Census.Admission.Of(b, ub)).Presented.Count > 0)
            r.Api = FastDiffState.Changed;
        else if (!ca.SequenceEqual(cb))
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
