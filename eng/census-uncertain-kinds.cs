#:project ../src/ILInspector.Metadata/ILInspector.Metadata.csproj

// SPIKE (exp/unified-diff-exists, #10227): a per-kind census of Fast Diff's API
// fact kinds, to settle each uncertain kind at design time by Promote (Exists
// learns the normalization) or Present (Full Diff shows the raw difference).
//
// For every fact category of eng/measure-census-classes.cs's Census, and on one
// image pair, it reports:
//   carriers   Types and Members carrying the kind on each side;
//   raw        Types and Members whose census differs in the kind;
//   silent     of those, the ones the complete Public API comparison
//              (MetadataFindings.CompareApi) reports as no change: Exists would
//              say Changed where Full Diff shows nothing today;
//   sole       Types whose only census differences are this kind, so promoting
//              it alone removes that Type's surface build;
//   flood      silent Types / Types public on either side;
// and per sub-cause of each difference (attribute blob identity, nullable
// context movement, hidden subject, flag bits, ...), the same counts with an
// example. Member grain matches census Members to the complete comparison's
// changed Members by name (accessor prefixes stripped), so it is approximate;
// Type grain is exact.
//
// Usage: census-uncertain-kinds <before.dll> <after.dll> [label]
//   EXAMPLES=n   examples per sub-cause (default 2)

using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ILInspector.Metadata;
using Inspector.Findings;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: census-uncertain-kinds <before.dll> <after.dll> [label]");
    return 2;
}

string label = args.Length > 2 ? args[2] : Path.GetFileName(args[0]);
int exampleCount = int.TryParse(Environment.GetEnvironmentVariable("EXAMPLES"), out int ex) ? ex : 2;
byte[] beforeBytes = File.ReadAllBytes(args[0]);
byte[] afterBytes = File.ReadAllBytes(args[1]);
var options = new ApiDiffOptions(ApiDiffScope.All);
using var before = new PEReader(new MemoryStream(beforeBytes, writable: false));
using var after = new PEReader(new MemoryStream(afterBytes, writable: false));

// ---- The complete comparison: Type-grain oracle and per-Type changed Member names.
long t0 = Stopwatch.GetTimestamp();
var surfaceA = ApiSurfaceExtractor.Extract(before, ApiSurfaceExtractionScope.Public);
var surfaceB = ApiSurfaceExtractor.Extract(after, ApiSurfaceExtractionScope.Public);
var api = MetadataFindings.CompareApi(surfaceA, surfaceB, new FindingSubject("api", "API"), options);
var oracle = new HashSet<string>(StringComparer.Ordinal);
foreach (TypeDiff type in api.ApiDiff.TypeDiffs)
    oracle.Add(type.TypeFullName);
if (api.Types.Value is FindingComparison<ApiTypeHandle>.Complete ot)
    foreach (var pair in ot.Pairs)
        if (pair.Kind != PairKind.Present)
            oracle.Add(Oracle.Payload(pair).TypeFullName);
if (api.Members.Value is FindingComparison<ApiMemberHandle>.Complete om)
    foreach (var pair in om.Pairs)
        if (pair.Kind != PairKind.Present)
            oracle.Add(Oracle.Payload(pair).TypeFullName);
var byTokenA = surfaceA.Types.Where(x => x.MetadataToken is not null).GroupBy(x => x.MetadataToken!.Value).ToDictionary(g => g.Key, g => g.First());
var byTokenB = surfaceB.Types.Where(x => x.MetadataToken is not null).GroupBy(x => x.MetadataToken!.Value).ToDictionary(g => g.Key, g => g.First());
double oracleMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;

// ---- Census walk.
var a = new FastDiff.Side(before);
var b = new FastDiff.Side(after);
var unitsA = a.Units();
var unitsB = b.Units();
var joined = new List<(FastDiff.Unit? A, FastDiff.Unit? B)>();
foreach (var (k, u) in unitsA)
    joined.Add((u, unitsB.TryGetValue(k, out var v) ? v : null));
foreach (var (k, u) in unitsB)
    if (!unitsA.ContainsKey(k))
        joined.Add((null, u));

var stats = new Dictionary<string, KindStats>(StringComparer.Ordinal);
KindStats S(string cat) => stats.TryGetValue(cat, out var s) ? s : stats[cat] = new KindStats();
int visibleTypes = 0, pairedVisible = 0, malformed = 0, differing = 0, modelTypes = 0, oracleChangedPaired = 0;
var typeLines = new List<string>();
var attrCarriers = new SortedDictionary<string, int[]>(StringComparer.Ordinal);
var qualifiedBlob = new int[2];
var qualifiedBlobMembers = new[] { new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal) };
var unmatchedOracle = new HashSet<string>(oracle, StringComparer.Ordinal);

foreach (var (ua, ub) in joined)
{
    var u = (ua ?? ub)!;
    unmatchedOracle.Remove(u.FullName);
    if (ua?.Malformed == true || ub?.Malformed == true)
    {
        malformed++;
        continue;
    }
    bool visA = ua is not null && a.IsVisible(ua.Owner);
    bool visB = ub is not null && b.IsVisible(ub.Owner);
    if (!visA && !visB)
        continue;
    visibleTypes++;
    bool inOracle = oracle.Contains(u.FullName);
    if (ua is null || ub is null)
    {
        var adm = ua is not null ? Admission.Of(a, ua) : Admission.Of(b, ub!);
        var s = S("type-presence");
        s.CarrierTypes[ua is null ? 1 : 0]++;
        s.RawTypes++;
        if (!inOracle) s.SilentTypes++;
        s.SoleTypes++;
        s.Sub(adm.TypeAdmitted ? "type-presence" : "hidden Type").Add(u.FullName, !inOracle, $"{u.FullName} one-sided");
        continue;
    }
    pairedVisible++;
    if (inOracle) oracleChangedPaired++;
    var admA = Admission.Of(a, ua);
    var admB = Admission.Of(b, ub);
    var ca = a.ApiCensus(ua);
    var cb = b.ApiCensus(ub);
    foreach (var (side, census, adm) in new[] { (0, ca, admA), (1, cb, admB) })
    {
        var typesSeen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in census)
            foreach (var (cat, member) in Facts.Carry(Facts.ToFact(key.Bytes)))
            {
                var s = S(cat);
                if (typesSeen.Add(cat)) s.CarrierTypes[side]++;
                if (member is not null) s.CarrierMembers[side].Add(member);
            }
        // Per attribute Type, and attribute blobs naming an assembly-qualified Type (", Version=").
        var attrSeen = new HashSet<string>(StringComparer.Ordinal);
        bool qualified = false;
        foreach (var key in census)
        {
            if (key.Kind != SymbolPart.Attribute) continue;
            var f = Facts.ToFact(key.Bytes);
            string an = Facts.AttributeTypeName(f);
            if (attrSeen.Add(an))
            {
                if (!attrCarriers.TryGetValue(an, out var c)) attrCarriers[an] = c = new int[2];
                c[side]++;
            }
            if (f.Parts.Count > 3 && f.Parts[3].Payload is { } blob && blob.AsSpan().IndexOf(", Version="u8) >= 0)
            {
                qualified = true;
                var (m, _) = Facts.MemberOf(f.Subject);
                qualifiedBlobMembers[side].Add(m ?? "type");
            }
        }
        if (qualified) qualifiedBlob[side]++;
        S("type-presence").CarrierTypes[side]++;
        S("visibility").CarrierTypes[side]++;
        if (!adm.TypeAdmitted) S("hidden-type").CarrierTypes[side]++;
        if (adm.Hidden.Count > 0) { S("hidden-member").CarrierTypes[side]++; foreach (var h in adm.Hidden) S("hidden-member").CarrierMembers[side].Add(h); }
    }
    if (ca.SequenceEqual(cb))
        continue;
    differing++;

    var diffs = Facts.Differences(ca, cb, admA, admB);
    ApiType? ta = byTokenA.TryGetValue(MetadataTokens.GetToken(ua.Owner), out var x1) ? x1 : null;
    ApiType? tb = byTokenB.TryGetValue(MetadataTokens.GetToken(ub.Owner), out var x2) ? x2 : null;
    var changedNames = ta is not null && tb is not null ? Oracle.ChangedMemberNames(ta, tb, options) : [];
    var ctx = new SubCauseContext(a, b, ua, ub, admA, admB, diffs);

    bool anyPresented = diffs.Any(d => d.Admitted && Census.IsPresented(d.Category));
    if (!anyPresented) modelTypes++;
    var catsHere = diffs.Select(d => d.Category).ToHashSet();
    var uncertainHere = diffs.Where(d => !(d.Admitted && Census.IsPresented(d.Category))).Select(d => d.Category).ToHashSet();
    foreach (var cat in catsHere)
    {
        var s = S(cat);
        s.RawTypes++;
        if (!inOracle) s.SilentTypes++;
        if (catsHere.Count == 1) s.SoleTypes++;
        if (!anyPresented && uncertainHere.Count == 1) s.SoleUncertainTypes++;
    }
    // Hidden subjects as their own rows, as measure-census-classes names them.
    foreach (var (hcat, any) in new[] { ("hidden-type", !admA.TypeAdmitted || !admB.TypeAdmitted), ("hidden-member", admA.TypeAdmitted && admB.TypeAdmitted && diffs.Any(d => !d.Admitted)) })
        if (any)
        {
            var s = S(hcat);
            s.RawTypes++;
            if (!inOracle) s.SilentTypes++;
            if (!anyPresented && diffs.All(d => !d.Admitted)) s.SoleUncertainTypes++;
        }
    var uncertainSubs = new SortedSet<string>(StringComparer.Ordinal);
    var memberSeen = new HashSet<(string, string)>();
    var subSeen = new HashSet<(string, string)>();
    foreach (var d in diffs)
    {
        bool memberSilent = !inOracle || (d.MemberName is { } mn && !Oracle.NameChanged(changedNames, mn));
        if (d.Member is not null && memberSeen.Add((d.Category, d.Member)))
        {
            S(d.Category).RawMembers++;
            if (memberSilent) S(d.Category).SilentMembers++;
        }
        string sub = SubCause.Of(d, ctx);
        if (!(d.Admitted && Census.IsPresented(d.Category)))
            uncertainSubs.Add(d.Category + " | " + sub);
        var ss = S(d.Category).Sub(sub);
        if (subSeen.Add((d.Category, sub)))
            ss.Add(u.FullName, !inOracle, d.Describe(u.FullName));
        if (d.Member is not null)
        {
            ss.RawMembers++;
            if (memberSilent) ss.SilentMembers++;
        }
    }
    if (!anyPresented)
        typeLines.Add($"ut\t{u.FullName}\t{(inOracle ? "changed" : "silent")}\t{string.Join(" ; ", uncertainSubs)}\tanyAnnotated={Facts.AnyAnnotated(ca) || Facts.AnyAnnotated(cb)}");
}

// ---- Report.
Console.WriteLine($"pair\t{label}\t{args[0]}\t{args[1]}");
Console.WriteLine($"types\tvisible={visibleTypes}\tpairedVisible={pairedVisible}\tcensusDiffering={differing}\tsurfaceBuilds(staged r4)={modelTypes}\toracleChanged={oracle.Count}\toracleChangedPaired={oracleChangedPaired}\tmalformed={malformed}\toracleUnmatched={unmatchedOracle.Count}\toracleMs={oracleMs:F0}");
Console.WriteLine("kind\tclass\tcarrierTypesA\tcarrierTypesB\tcarrierMembersA\tcarrierMembersB\trawTypes\trawMembers\tsilentTypes\tsilentMembers\tsoleTypes\tsoleUncertainTypes\tfloodPct");
foreach (var cat in Census.Categories.Concat(stats.Keys.Except(Census.Categories)).Distinct())
{
    var s = S(cat);
    string cls = cat is "hidden-type" or "hidden-member" || !Census.IsPresented(cat) ? "uncertain" : "presented";
    Console.WriteLine($"kind\t{cat}\t{cls}\t{s.CarrierTypes[0]}\t{s.CarrierTypes[1]}\t{s.CarrierMembers[0].Count}\t{s.CarrierMembers[1].Count}"
        + $"\t{s.RawTypes}\t{s.RawMembers}\t{s.SilentTypes}\t{s.SilentMembers}\t{s.SoleTypes}\t{s.SoleUncertainTypes}\t{100.0 * s.SilentTypes / Math.Max(1, visibleTypes):F2}");
}
Console.WriteLine("sub\tkind\tsubCause\trawTypes\tsilentTypes\trawMembers\tsilentMembers\tfloodPct\texamples(silent first)");
foreach (var cat in stats.Keys.Order(StringComparer.Ordinal))
    foreach (var (sub, ss) in stats[cat].Subs.OrderByDescending(kv => kv.Value.SilentTypes).ThenBy(kv => kv.Key, StringComparer.Ordinal))
        Console.WriteLine($"sub\t{cat}\t{sub}\t{ss.RawTypes}\t{ss.SilentTypes}\t{ss.RawMembers}\t{ss.SilentMembers}\t{100.0 * ss.SilentTypes / Math.Max(1, visibleTypes):F2}"
            + $"\t{string.Join(" | ", ss.SilentExamples.Concat(ss.Examples).Take(exampleCount))}");
Console.WriteLine($"qualified-blob\ttypesA={qualifiedBlob[0]}\ttypesB={qualifiedBlob[1]}\tsubjectsA={qualifiedBlobMembers[0].Count}\tsubjectsB={qualifiedBlobMembers[1].Count}");
foreach (var (an, c) in attrCarriers.OrderByDescending(kv => kv.Value[0] + kv.Value[1]))
    Console.WriteLine($"attr-carriers\t{an}\t{c[0]}\t{c[1]}");
foreach (var line in typeLines)
    Console.WriteLine(line);
return 0;

sealed class KindStats
{
    public int[] CarrierTypes = new int[2];
    public HashSet<string>[] CarrierMembers = [new(StringComparer.Ordinal), new(StringComparer.Ordinal)];
    public int RawTypes, RawMembers, SilentTypes, SilentMembers, SoleTypes, SoleUncertainTypes;
    public Dictionary<string, SubStats> Subs { get; } = new(StringComparer.Ordinal);
    public SubStats Sub(string name) => Subs.TryGetValue(name, out var s) ? s : Subs[name] = new SubStats();
}

sealed class SubStats
{
    public int RawTypes, SilentTypes, RawMembers, SilentMembers;
    public List<string> Examples { get; } = [];
    public List<string> SilentExamples { get; } = [];
    public void Add(string type, bool silent, string example)
    {
        RawTypes++;
        if (silent)
        {
            SilentTypes++;
            if (SilentExamples.Count < 4) SilentExamples.Add(example);
        }
        else if (Examples.Count < 4)
            Examples.Add(example);
    }
}

static class Oracle
{
    public static T Payload<T>(PairFinding<T> pair) where T : notnull
        => (((IPairFinding)pair).New as Finding<T> ?? (Finding<T>)((IPairFinding)pair).Old!).Payload;

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
            string k = h.CanonicalSignature ?? h.Identity;
            int n = occurrences.TryGetValue(k, out int c) ? c + 1 : 1;
            occurrences[k] = n;
            map[n == 1 ? k : $"{k}#{n}"] = h;
        }
        return map;
    }

    /// <summary>The staged spike's per-Type surface check (Shared.ModelCompare), collecting changed Member names.</summary>
    public static HashSet<string> ChangedMemberNames(ApiType bt, ApiType at, ApiDiffOptions options)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        void Add(ApiMember m)
        {
            names.Add(m.Name);
            names.Add(Last(m.Name));
            if (m.Kind == "constructor") names.Add("#ctor");
            if (m.Kind == "operator") names.Add("#op");
        }
        var old = Keyed(bt);
        var @new = Keyed(at);
        foreach (var (k, nh) in @new)
        {
            if (!old.TryGetValue(k, out var oh)) { Add(nh.Member); continue; }
            bool changed = !string.Equals(oh.Member.Signature, nh.Member.Signature, StringComparison.Ordinal);
            if (!changed)
            {
                var changes = new List<ApiChange>();
                ApiDiffAnalyzer.CompareMemberModifiers(bt, at, oh.Member, nh.Member, changes);
                ApiDiffAnalyzer.CompareMemberTypeParameterConstraints(bt, at, oh.Member, nh.Member, changes);
                ApiDiffAnalyzer.CompareAttributes(oh.Member.Attributes, nh.Member.Attributes, changes,
                    ChangeKind.MemberAttributeAdded, ChangeKind.MemberAttributeRemoved,
                    $"Member '{oh.MemberName}'", ApiChangeSubject.Member(bt, oh.Member, at, nh.Member));
                var details = new List<string>();
                MetadataFindings.AddMemberFacetDetails(oh.Member, nh.Member, options.Scope, details);
                changed = changes.Count > 0 || details.Count > 0;
            }
            if (changed) { Add(oh.Member); Add(nh.Member); }
        }
        foreach (var (k, oh) in old)
            if (!@new.ContainsKey(k))
                Add(oh.Member);
        return names;
    }

    static string Last(string name)
    {
        int i = name.LastIndexOf('.');
        return i > 0 && i < name.Length - 1 ? name[(i + 1)..] : name;
    }

    static string StripAccessor(string n)
    {
        foreach (var p in new[] { "get_", "set_", "add_", "remove_", "raise_" })
            if (n.StartsWith(p, StringComparison.Ordinal))
                return n[p.Length..];
        return n;
    }

    public static bool NameChanged(HashSet<string> changed, string censusName)
    {
        if (censusName is ".ctor" or ".cctor")
            return changed.Contains("#ctor");
        if (censusName.StartsWith("op_", StringComparison.Ordinal) && changed.Contains("#op"))
            return true;
        string last = Last(censusName);
        return changed.Contains(censusName) || changed.Contains(last)
            || changed.Contains(StripAccessor(censusName)) || changed.Contains(StripAccessor(last));
    }
}

/// <summary>Admission, as eng/measure-census-classes.cs's Census.Admission.</summary>
sealed class Admission
{
    public bool TypeAdmitted = true;
    public HashSet<string> Hidden { get; } = new(StringComparer.Ordinal);

    public static Admission Of(FastDiff.Side side, FastDiff.Unit unit)
    {
        var md = side.Md;
        var adm = new Admission();
        var type = md.GetTypeDefinition(unit.Owner);
        adm.TypeAdmitted = !AttributeReader.HasHiddenAttribute(md, type.GetCustomAttributes());
        void Add(string kind, StringHandle name, CustomAttributeHandleCollection attrs)
        {
            if (md.StringComparer.StartsWith(name, "<") || AttributeReader.HasEditorBrowsableNeverAttribute(md, attrs))
                adm.Hidden.Add(kind + ":" + Convert.ToHexString(Encoding.UTF8.GetBytes(md.GetString(name))));
        }
        foreach (var h in type.GetMethods()) { var m = md.GetMethodDefinition(h); Add("Method", m.Name, m.GetCustomAttributes()); }
        foreach (var h in type.GetFields()) { var f = md.GetFieldDefinition(h); Add("Field", f.Name, f.GetCustomAttributes()); }
        foreach (var h in type.GetProperties()) { var x = md.GetPropertyDefinition(h); Add("Property", x.Name, x.GetCustomAttributes()); }
        foreach (var h in type.GetEvents()) { var x = md.GetEventDefinition(h); Add("Event", x.Name, x.GetCustomAttributes()); }
        return adm;
    }

    public bool Admits(byte[]? subject)
    {
        if (subject is null || subject.Length == 0)
            return true;
        var parts = Facts.Parse(subject);
        if (parts[0].Tag is SymbolPart.Parameter or SymbolPart.GenericParameter or SymbolPart.Constraint && parts.Count > 1 && parts[1].Payload is { } owner)
            return Admits(owner);
        if (parts[0].Tag is SymbolPart.Method or SymbolPart.Field or SymbolPart.Property or SymbolPart.Event
            && parts.Count > 2 && parts[2].Tag == SymbolPart.Name)
            return !Hidden.Contains(parts[0].Tag + ":" + Convert.ToHexString(parts[2].Payload!));
        return true;
    }
}

readonly record struct Part(SymbolPart Tag, long Int, byte[]? Payload)
{
    public bool Same(Part o) => Tag == o.Tag && Int == o.Int
        && (Payload is null ? o.Payload is null : o.Payload is not null && Payload.AsSpan().SequenceEqual(o.Payload));
}

sealed record Fact(SymbolPart Kind, byte[] Bytes, byte[]? Subject, List<Part> Parts)
{
    public List<Part> Payload => Parts.Skip(2).ToList();
}

/// <summary>One census difference: a category on one subject.</summary>
sealed record Diff(string Category, byte[]? Subject, string? Member, string? MemberName, bool Admitted, Fact? A, Fact? B)
{
    public string Describe(string type)
        => $"{type}{(MemberName is null ? "" : "::" + MemberName)}";
}

static class Facts
{
    public static List<Part> Parse(ReadOnlySpan<byte> bytes)
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

    public static Fact ToFact(ReadOnlySpan<byte> bytes)
    {
        var parts = Parse(bytes);
        byte[]? subject = parts.Count > 1 && parts[1].Tag == SymbolPart.Key ? parts[1].Payload : null;
        return new(parts[0].Tag, bytes.ToArray(), subject, parts);
    }

    /// <summary>The Member a subject key belongs to (hex key), or null for a Type-level subject.</summary>
    public static (string? Member, string? Name) MemberOf(byte[]? subject)
    {
        if (subject is null || subject.Length == 0)
            return (null, null);
        var parts = Parse(subject);
        switch (parts[0].Tag)
        {
            case SymbolPart.Method or SymbolPart.Field or SymbolPart.Property or SymbolPart.Event:
                return (Convert.ToHexString(subject), parts.Count > 2 && parts[2].Tag == SymbolPart.Name ? Encoding.UTF8.GetString(parts[2].Payload!) : null);
            case SymbolPart.Parameter or SymbolPart.GenericParameter or SymbolPart.Constraint when parts.Count > 1 && parts[1].Payload is { } owner:
                return MemberOf(owner);
            default:
                return (null, null);
        }
    }

    /// <summary>The attribute Type's name from an attribute fact's constructor key ([Method][Key type]... or a MethodSpec-free MemberRef key).</summary>
    public static string AttributeTypeName(Fact f)
    {
        if (f.Parts.Count < 3 || f.Parts[2].Payload is not { } ctor) return "?";
        var c = Parse(ctor);
        if (c.Count < 2 || c[1].Payload is not { } type) return "?";
        var t = Parse(type);
        var names = new List<string>();
        foreach (var p in t)
            if (p.Tag == SymbolPart.Name) names.Add(Encoding.UTF8.GetString(p.Payload!));
        return names.Count == 0 ? "?" : string.Join(".", names);
    }

    /// <summary>Whether any nullable fact of a census (Type context, or a Nullable/NullableContext row anywhere) holds annotated (2).</summary>
    public static bool AnyAnnotated(List<SymbolKey> census)
    {
        foreach (var key in census)
        {
            if (key.Kind == SymbolPart.NullableContext)
            {
                if (ToFact(key.Bytes).Payload[0].Int == 2) return true;
            }
            else if (key.Kind == SymbolPart.Attribute)
            {
                var f = ToFact(key.Bytes);
                string n = AttributeTypeName(f);
                if (n is not ("System.Runtime.CompilerServices.NullableAttribute" or "System.Runtime.CompilerServices.NullableContextAttribute")) continue;
                var blob = f.Parts[3].Payload ?? [];
                if (blob.Length == 5 ? blob[2] == 2 : blob.Length >= 8 && blob.AsSpan(6, Math.Min(BitConverter.ToInt32(blob, 2), blob.Length - 6)).IndexOf((byte)2) >= 0) return true;
            }
        }
        return false;
    }

    public static string OwnerKind(byte[]? subject)
    {
        if (subject is null || subject.Length == 0)
            return "none";
        var parts = Parse(subject);
        return parts[0].Tag switch
        {
            SymbolPart.Parameter => parts[^1].Tag == SymbolPart.Int && parts[^1].Int == 0 ? "return" : "parameter",
            SymbolPart.GenericParameter => "generic-parameter",
            SymbolPart.Constraint => "constraint",
            SymbolPart.InterfaceFact => "interface-impl",
            SymbolPart.Type => "type",
            var t => t.ToString().ToLowerInvariant(),
        };
    }

    const long TypePresented = 0x7 | 0x20 | 0x80 | 0x100;
    const long MethodNamed = 0x7L | 0x10 | 0x20 | 0x40 | 0x100 | 0x400;
    const long FieldNamed = 0x7L | 0x10 | 0x20 | 0x40;

    static bool HasConstant(List<Part> p, int i) => p.Count > i && p[i].Tag == SymbolPart.Constant;

    /// <summary>The categories a fact carries, with the Member it belongs to.</summary>
    public static IEnumerable<(string Category, string? Member)> Carry(Fact f)
    {
        var (member, _) = MemberOf(f.Subject);
        var p = f.Payload;
        switch (f.Kind)
        {
            case SymbolPart.TypeFact:
                yield return ("type-flags:visibility-kind-abstract-sealed", null);
                if ((p[0].Int & ~TypePresented) != 0) yield return ("type-flags:other", null);
                if (p.Count > 1 && p[1].Tag != SymbolPart.Absent) yield return ("type-base", null);
                break;
            case SymbolPart.InterfaceFact: yield return ("interface", null); break;
            case SymbolPart.GenericParameterFact:
                yield return ("genparam-presence", member);
                yield return ("genparam-name", member);
                if (p[1].Int != 0) yield return ("genparam-flags", member);
                break;
            case SymbolPart.Constraint: yield return ("constraint", member); break;
            case SymbolPart.MethodImplementation: yield return ("method-impl", null); break;
            case SymbolPart.Attribute: yield return ("attribute", member); break;
            case SymbolPart.NullableContext: if (p[0].Int != 0) yield return ("nullable-context", null); break;
            case SymbolPart.Layout: yield return ("layout", null); break;
            case SymbolPart.Import: yield return ("import", member); break;
            case SymbolPart.MethodFact:
                yield return ("member-presence:method", member);
                yield return ("method-flags:access", member);
                if ((p[0].Int & 0x10) != 0) yield return ("method-flags:static", member);
                if ((p[0].Int & (0x20 | 0x40 | 0x100 | 0x400)) != 0) yield return ("method-flags:virtual-shape", member);
                if ((p[0].Int & ~MethodNamed) != 0) yield return ("method-flags:other", member);
                if (p[1].Int != 0) yield return ("method-implflags", member);
                break;
            case SymbolPart.FieldFact:
                yield return ("member-presence:field", member);
                yield return ("field-flags:access", member);
                if ((p[0].Int & 0x70) != 0) yield return ("field-flags:static-readonly-const", member);
                if ((p[0].Int & ~FieldNamed) != 0) yield return ("field-flags:other", member);
                if (HasConstant(p, 1)) yield return ("field-constant", member);
                if (p[^1].Int != -1) yield return ("field-offset", member);
                break;
            case SymbolPart.ParameterFact:
            {
                bool ret = OwnerKind(f.Subject) == "return";
                yield return (ret ? "param-presence:return" : "param-presence", member);
                if (!ret) yield return ("param-name", member);
                if ((p[1].Int & 0x2) != 0) yield return ("param-flags:out", member);
                if ((p[1].Int & ~0x2L) != 0) yield return ("param-flags:other", member);
                if (HasConstant(p, 2)) yield return ("param-default", member);
                break;
            }
            case SymbolPart.PropertyFact:
                yield return ("member-presence:property", member);
                if (p[0].Int != 0) yield return ("property-flags", member);
                if (HasConstant(p, 1)) yield return ("property-constant", member);
                yield return ("property-accessors", member);
                break;
            case SymbolPart.EventFact:
                yield return ("member-presence:event", member);
                if (p[0].Int != 0) yield return ("event-flags", member);
                yield return ("event-accessors", member);
                break;
            case SymbolPart.Visible or SymbolPart.Hidden:
                break;
            default:
                yield return ("other", member);
                break;
        }
    }

    /// <summary>Census.Classify, per subject: every differing category with its subject and admission.</summary>
    public static List<Diff> Differences(List<SymbolKey> a, List<SymbolKey> b, Admission admA, Admission admB)
    {
        bool typeHidden = !admA.TypeAdmitted || !admB.TypeAdmitted;
        var setA = a.ToHashSet();
        var setB = b.ToHashSet();
        var factsA = a.Select(k => ToFact(k.Bytes)).ToList();
        var factsB = b.Select(k => ToFact(k.Bytes)).ToList();
        string SubjectKey(Fact f) => f.Subject is null ? "" : Convert.ToHexString(f.Subject);
        var bySubjectA = factsA.GroupBy(f => (f.Kind, SubjectKey(f))).ToDictionary(g => g.Key, g => g.ToList());
        var bySubjectB = factsB.GroupBy(f => (f.Kind, SubjectKey(f))).ToDictionary(g => g.Key, g => g.ToList());
        var seen = new HashSet<(SymbolPart, string)>();
        var result = new List<Diff>();
        for (int side = 0; side < 2; side++)
        {
            var mine = side == 0 ? a : b;
            var other = side == 0 ? setB : setA;
            foreach (var key in mine)
            {
                if (other.Contains(key))
                    continue;
                var f = ToFact(key.Bytes);
                string sk = SubjectKey(f);
                bool multi = f.Kind is SymbolPart.Attribute or SymbolPart.InterfaceFact or SymbolPart.Constraint or SymbolPart.MethodImplementation;
                if (!seen.Add((f.Kind, sk)) && !multi)
                    continue;
                bySubjectA.TryGetValue((f.Kind, sk), out var la);
                bySubjectB.TryGetValue((f.Kind, sk), out var lb);
                bool both = la is not null && lb is not null;
                bool admitted = !typeHidden && (la is null || admA.Admits(f.Subject)) && (lb is null || admB.Admits(f.Subject));
                var cats = new List<string>();
                switch (f.Kind)
                {
                    case SymbolPart.Visible or SymbolPart.Hidden: cats.Add("visibility"); break;
                    case SymbolPart.MethodFact or SymbolPart.FieldFact or SymbolPart.PropertyFact or SymbolPart.EventFact:
                        if (!both) cats.Add("member-presence:" + f.Kind.ToString().Replace("Fact", "").ToLowerInvariant());
                        else Components(f.Kind, la![0].Parts, lb![0].Parts, cats);
                        break;
                    case SymbolPart.ParameterFact:
                        if (!both) cats.Add(OwnerKind(f.Subject) == "return" ? "param-presence:return" : "param-presence");
                        else Components(f.Kind, la![0].Parts, lb![0].Parts, cats);
                        break;
                    case SymbolPart.TypeFact: Components(f.Kind, la![0].Parts, lb![0].Parts, cats); break;
                    case SymbolPart.GenericParameterFact:
                        if (!both) cats.Add("genparam-presence");
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
                var (member, name) = MemberOf(f.Subject);
                Fact? fa = multi ? (side == 0 ? f : null) : la?[0];
                Fact? fb = multi ? (side == 1 ? f : null) : lb?[0];
                foreach (var c in cats)
                    result.Add(new Diff(c, f.Subject, member, name, admitted, fa, fb));
            }
        }
        return result;
    }

    static void Components(SymbolPart kind, List<Part> x, List<Part> y, List<string> cats)
    {
        var px = x.Skip(2).ToList();
        var py = y.Skip(2).ToList();
        switch (kind)
        {
            case SymbolPart.MethodFact:
            {
                long diff = px[0].Int ^ py[0].Int;
                if ((diff & 0x7) != 0) cats.Add("method-flags:access");
                if ((diff & 0x10) != 0) cats.Add("method-flags:static");
                if ((diff & (0x20 | 0x40 | 0x100 | 0x400)) != 0) cats.Add("method-flags:virtual-shape");
                if ((diff & ~MethodNamed) != 0) cats.Add("method-flags:other");
                if (px[1].Int != py[1].Int) cats.Add("method-implflags");
                break;
            }
            case SymbolPart.FieldFact:
            {
                long diff = px[0].Int ^ py[0].Int;
                if ((diff & 0x7) != 0) cats.Add("field-flags:access");
                if ((diff & 0x70) != 0) cats.Add("field-flags:static-readonly-const");
                if ((diff & ~FieldNamed) != 0) cats.Add("field-flags:other");
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
                if ((diff & TypePresented) != 0) cats.Add("type-flags:visibility-kind-abstract-sealed");
                if ((diff & ~TypePresented) != 0) cats.Add("type-flags:other");
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

/// <summary>Per-Type context for sub-cause classification: the attribute rows behind each attribute fact.</summary>
sealed class SubCauseContext(FastDiff.Side a, FastDiff.Side b, FastDiff.Unit ua, FastDiff.Unit ub, Admission admA, Admission admB, List<Diff> diffs)
{
    public FastDiff.Side A => a;
    public FastDiff.Side B => b;
    public Admission AdmA => admA;
    public Admission AdmB => admB;
    public List<Diff> Diffs => diffs;
    Dictionary<string, CustomAttributeHandle>? _rowsA, _rowsB;

    public Dictionary<string, CustomAttributeHandle> Rows(int side)
        => side == 0 ? _rowsA ??= Index(a, ua) : _rowsB ??= Index(b, ub);

    /// <summary>(constructor key, blob) of every attribute row a Type's API census can name.</summary>
    static Dictionary<string, CustomAttributeHandle> Index(FastDiff.Side side, FastDiff.Unit unit)
    {
        var md = side.Md;
        var map = new Dictionary<string, CustomAttributeHandle>(StringComparer.Ordinal);
        void Add(CustomAttributeHandleCollection attrs)
        {
            foreach (var h in attrs)
            {
                var attr = md.GetCustomAttribute(h);
                string k;
                try { k = Convert.ToHexString(side.Key(attr.Constructor).Bytes) + ":" + Convert.ToHexString(md.GetBlobBytes(attr.Value)); }
                catch (Exception) { continue; }
                map.TryAdd(k, h);
            }
        }
        var type = md.GetTypeDefinition(unit.Owner);
        Add(type.GetCustomAttributes());
        foreach (var i in type.GetInterfaceImplementations()) Add(md.GetInterfaceImplementation(i).GetCustomAttributes());
        void GenericParameters(GenericParameterHandleCollection ps)
        {
            foreach (var g in ps)
            {
                var gp = md.GetGenericParameter(g);
                Add(gp.GetCustomAttributes());
                foreach (var c in gp.GetConstraints()) Add(md.GetGenericParameterConstraint(c).GetCustomAttributes());
            }
        }
        GenericParameters(type.GetGenericParameters());
        foreach (var h in type.GetFields()) Add(md.GetFieldDefinition(h).GetCustomAttributes());
        foreach (var h in type.GetMethods())
        {
            var m = md.GetMethodDefinition(h);
            Add(m.GetCustomAttributes());
            GenericParameters(m.GetGenericParameters());
            foreach (var p in m.GetParameters()) Add(md.GetParameter(p).GetCustomAttributes());
        }
        foreach (var h in type.GetProperties()) Add(md.GetPropertyDefinition(h).GetCustomAttributes());
        foreach (var h in type.GetEvents()) Add(md.GetEventDefinition(h).GetCustomAttributes());
        return map;
    }
}

static class SubCause
{
    // AttributeReader.Rendering.cs IsReEmittedAttribute: rows the Public API surface drops.
    static readonly HashSet<string> ReEmitted =
    [
        "System.Runtime.CompilerServices.ExtensionAttribute", "System.Runtime.CompilerServices.CompilerFeatureRequiredAttribute",
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute", "System.Runtime.CompilerServices.InlineArrayAttribute",
        "System.Runtime.CompilerServices.NullableAttribute", "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute", "System.Runtime.CompilerServices.RequiresLocationAttribute",
        "System.Runtime.CompilerServices.IsByRefLikeAttribute", "System.Runtime.CompilerServices.IsUnmanagedAttribute",
        "System.Runtime.CompilerServices.RefSafetyRulesAttribute", "System.Runtime.CompilerServices.ScopedRefAttribute",
        "System.Runtime.CompilerServices.NativeIntegerAttribute", "System.Runtime.CompilerServices.DynamicAttribute",
        "System.Runtime.CompilerServices.TupleElementNamesAttribute", "System.Runtime.CompilerServices.RequiredMemberAttribute",
        "System.Diagnostics.CodeAnalysis.RequiresUnsafeAttribute", "System.Runtime.CompilerServices.RequiresUnsafeAttribute",
        "System.Runtime.CompilerServices.DecimalConstantAttribute", "System.Runtime.CompilerServices.DateTimeConstantAttribute",
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute", "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
        "System.Runtime.CompilerServices.FixedBufferAttribute", "System.Runtime.CompilerServices.IntrinsicAttribute",
        "System.Runtime.Versioning.NonVersionableAttribute", "System.Reflection.DefaultMemberAttribute",
    ];

    static string Short(string? name)
    {
        if (name is null) return "?";
        int i = name.LastIndexOf('.');
        var s = i >= 0 ? name[(i + 1)..] : name;
        return s.EndsWith("Attribute", StringComparison.Ordinal) ? s[..^9] : s;
    }

    public static string Of(Diff d, SubCauseContext ctx)
    {
        if (!d.Admitted)
            return !ctx.AdmA.TypeAdmitted || !ctx.AdmB.TypeAdmitted ? "hidden Type (EditorBrowsable(Never)/Obsolete)" : "hidden Member (EditorBrowsable(Never) or <-named)";
        switch (d.Category)
        {
            case "attribute": return Attribute(d, ctx);
            case "nullable-context":
            {
                bool memberAttrs = ctx.Diffs.Any(x => x.Category == "attribute" && x.Member is not null
                    && AttributeName(x, ctx) is "System.Runtime.CompilerServices.NullableContextAttribute" or "System.Runtime.CompilerServices.NullableAttribute");
                return (memberAttrs ? "context moved between Type and Member" : "Type context value only") + $" ({d.A!.Payload[0].Int}->{d.B!.Payload[0].Int})";
            }
            case "method-flags:other": return "bits " + (MethodAttributes)((d.A!.Payload[0].Int ^ d.B!.Payload[0].Int) & ~0x577L);
            case "method-implflags": return "bits " + (MethodImplAttributes)(d.A!.Payload[1].Int ^ d.B!.Payload[1].Int);
            case "field-flags:other": return "bits " + (FieldAttributes)((d.A!.Payload[0].Int ^ d.B!.Payload[0].Int) & ~0x77L);
            case "param-flags:other": return "bits " + (ParameterAttributes)((d.A!.Payload[1].Int ^ d.B!.Payload[1].Int) & ~0x2L);
            case "property-flags": return "bits " + (PropertyAttributes)(d.A!.Payload[0].Int ^ d.B!.Payload[0].Int);
            case "event-flags": return "bits " + (EventAttributes)(d.A!.Payload[0].Int ^ d.B!.Payload[0].Int);
            case "type-flags:other": return "bits " + (TypeAttributes)((d.A!.Payload[0].Int ^ d.B!.Payload[0].Int) & ~0x1A7L);
            case "param-presence:return":
            {
                var f = d.A ?? d.B!;
                bool attrs = ctx.Diffs.Any(x => x.Category == "attribute" && x.Subject is not null && f.Subject is not null && x.Subject.AsSpan().SequenceEqual(f.Subject));
                return attrs ? "return row with its attributes" : "return row only";
            }
            default: return d.Category;
        }
    }

    static string? AttributeName(Diff d, SubCauseContext ctx)
    {
        var f = d.A ?? d.B!;
        var side = d.A is not null ? ctx.A : ctx.B;
        return Row(d, ctx, d.A is not null ? 0 : 1) is { } h ? AttributeDecoder.GetAttributeTypeName(side.Md, side.Md.GetCustomAttribute(h).Constructor) : null;
    }

    static CustomAttributeHandle? Row(Diff d, SubCauseContext ctx, int side)
    {
        var f = side == 0 ? d.A : d.B;
        if (f is null) return null;
        // [Attribute][Key owner][Key ctor][Blob]
        string k = Convert.ToHexString(f.Parts[2].Payload!) + ":" + Convert.ToHexString(f.Parts[3].Payload ?? []);
        return ctx.Rows(side).TryGetValue(k, out var h) ? h : null;
    }

    static string Attribute(Diff d, SubCauseContext ctx)
    {
        int side = d.A is not null ? 0 : 1;
        var me = side == 0 ? ctx.A : ctx.B;
        var other = side == 0 ? ctx.B : ctx.A;
        var f = (d.A ?? d.B)!;
        string owner = Facts.OwnerKind(f.Subject);
        if (Row(d, ctx, side) is not { } h)
            return "attribute row not found";
        var attr = me.Md.GetCustomAttribute(h);
        string? name = AttributeDecoder.GetAttributeTypeName(me.Md, attr.Constructor);
        if (name is "System.Runtime.CompilerServices.NullableAttribute" or "System.Runtime.CompilerServices.NullableContextAttribute")
        {
            var mineBlob = me.Md.GetBlobBytes(attr.Value);
            bool hasCounterpart = ctx.Diffs.Any(x => x.Category == "attribute" && !ReferenceEquals(x, d)
                && (side == 0 ? x.B : x.A) is { } g && g.Parts[1].Same(f.Parts[1]) && g.Parts[2].Same(f.Parts[2]));
            var values = NullableValues(mineBlob);
            string what = hasCounterpart ? "value changed" : side == 0 ? "removed" : "added";
            return $"nullable annotation ({Short(name)}) on {owner}, {what}, {(values.Contains((byte)2) ? "has annotated(2)" : values.All(v => v == 1) ? "not-annotated(1) only" : "oblivious(0) only")}";
        }
        if (name is "System.Runtime.CompilerServices.AsyncStateMachineAttribute" or "System.Runtime.CompilerServices.IteratorStateMachineAttribute"
            or "System.Runtime.CompilerServices.AsyncIteratorStateMachineAttribute")
            return $"state-machine typeof ({Short(name)})";
        if (name is not null && ReEmitted.Contains(name))
            return $"compiler-emitted, filtered ({Short(name)})";
        if (name == "System.ObsoleteAttribute" && owner is "method" or "field" or "property" or "event")
            return "Obsolete on Member (surface skips)";
        if (owner is "generic-parameter" or "constraint" or "interface-impl")
            return $"on {owner} (no surface attribute list)";

        // The opposite side's rows of the same owner and constructor.
        var counterpart = ctx.Diffs.Where(x => x.Category == "attribute" && !ReferenceEquals(x, d)
                && (side == 0 ? x.B : x.A) is { } g
                && g.Parts[1].Same(f.Parts[1]) && g.Parts[2].Same(f.Parts[2]))
            .Select(x => side == 0 ? x.B! : x.A!).ToList();
        var mine = AttributeDecoder.TryDecode(me.Md, attr);
        if (mine is null)
            return $"undecodable blob ({Short(name)})";
        if (!Renderable(mine.Value))
            return $"unrenderable argument, surface drops the row ({Short(name)})";
        if (counterpart.Count == 0)
            return $"added or removed, renderable ({Short(name)})";
        foreach (var g in counterpart)
        {
            string k = Convert.ToHexString(g.Parts[2].Payload!) + ":" + Convert.ToHexString(g.Parts[3].Payload ?? []);
            if (!(side == 0 ? ctx.Rows(1) : ctx.Rows(0)).TryGetValue(k, out var oh))
                continue;
            var oattr = other.Md.GetCustomAttribute(oh);
            var theirs = AttributeDecoder.TryDecode(other.Md, oattr);
            if (theirs is null)
                return $"undecodable blob ({Short(name)})";
            if (Same(mine.Value, theirs.Value, ordered: true))
            {
                var pm = AttributeDecoder.TryDecodePreservingSerializedTypeNames(me.Md, attr);
                var po = AttributeDecoder.TryDecodePreservingSerializedTypeNames(other.Md, oattr);
                return pm is not null && po is not null && !Same(pm.Value, po.Value, ordered: true)
                    ? $"referenced-assembly identity in blob ({Short(name)})"
                    : $"encoding only, equal decoded value ({Short(name)})";
            }
            if (Same(mine.Value, theirs.Value, ordered: false))
                return $"named-argument order ({Short(name)})";
            if (!Renderable(theirs.Value))
                return $"unrenderable argument, surface drops the row ({Short(name)})";
        }
        return $"value changed, renderable ({Short(name)})";
    }

    // NullableAttribute(byte) / NullableContextAttribute(byte): 01 00 b 00 00; NullableAttribute(byte[]): 01 00 n32 b... 00 00.
    static List<byte> NullableValues(byte[] blob)
    {
        if (blob.Length == 5) return [blob[2]];
        if (blob.Length >= 8)
        {
            int n = BitConverter.ToInt32(blob, 2);
            if (n >= 0 && 6 + n <= blob.Length) return [.. blob.AsSpan(6, n).ToArray()];
        }
        return [];
    }

    static bool Renderable(CustomAttributeValue<string> v)
        => v.FixedArguments.All(x => Renderable(x.Type, x.Value)) && v.NamedArguments.All(x => Renderable(x.Type, x.Value));

    static bool Renderable(string type, object? value) => value switch
    {
        null => true,
        _ when type == "System.Type" && value is string s => s.IndexOfAny(['`', '[', ',']) < 0,
        string or bool or char => true,
        byte or sbyte or short or ushort or int or uint or long or ulong or double or float => true,
        _ => false,
    };

    static string Text(object? v) => v switch
    {
        null => "null",
        ImmutableArray<CustomAttributeTypedArgument<string>> arr => "[" + string.Join(",", arr.Select(x => x.Type + ":" + Text(x.Value))) + "]",
        _ => v.ToString() ?? "",
    };

    static bool Same(CustomAttributeValue<string> x, CustomAttributeValue<string> y, bool ordered)
    {
        var fx = x.FixedArguments.Select(a => a.Type + "=" + Text(a.Value)).ToList();
        var fy = y.FixedArguments.Select(a => a.Type + "=" + Text(a.Value)).ToList();
        var nx = x.NamedArguments.Select(a => a.Kind + a.Name + ":" + a.Type + "=" + Text(a.Value)).ToList();
        var ny = y.NamedArguments.Select(a => a.Kind + a.Name + ":" + a.Type + "=" + Text(a.Value)).ToList();
        if (!fx.SequenceEqual(fy)) return false;
        return ordered ? nx.SequenceEqual(ny) : nx.Order().SequenceEqual(ny.Order());
    }
}

/// <summary>eng/measure-census-classes.cs's categories and its presented/uncertain split.</summary>
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
    ];

    public static bool IsPresented(string category) => !Uncertain.Contains(category);
}
