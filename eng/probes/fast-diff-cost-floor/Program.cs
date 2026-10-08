using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

// Fast Diff cost-floor probe: per-Type Exists over symbolic metadata + IL, early exit.
// Usage: bench <old.dll> <new.dll> [iterations]
var oldPath = args[0];
var newPath = args[1];
int iters = args.Length > 2 ? int.Parse(args[2]) : 10;
var oldBytes = File.ReadAllBytes(oldPath);
var newBytes = File.ReadAllBytes(newPath);

Result last = default;
var times = new List<double>();
var apiTimes = new List<double>();
for (int i = 0; i < iters + 3; i++)
{
    var sw = Stopwatch.StartNew();
    var a = new Side(oldBytes);
    var b = new Side(newBytes);
    var r = Differ.Run(a, b, bodies: false);
    double apiMs = sw.Elapsed.TotalMilliseconds;
    sw.Restart();
    var a2 = new Side(oldBytes);
    var b2 = new Side(newBytes);
    last = Differ.Run(a2, b2, bodies: true);
    double ms = sw.Elapsed.TotalMilliseconds;
    if (i >= 3) { times.Add(ms); apiTimes.Add(apiMs); }
    if (i == 0) Console.WriteLine($"API-only pass: units={r.Units} changed={r.Changed} oneSided={r.OneSided}");
}
times.Sort(); apiTimes.Sort();
Console.WriteLine($"units={last.Units} changed={last.Changed} (api/census={last.ApiChanged}, body={last.BodyChanged}, oneSided={last.OneSided})");
Console.WriteLine($"bodies compared={last.BodiesCompared} bytesScanned={last.IlBytes:N0} tokenKeys={last.TokenKeys}");
Console.WriteLine($"API+census pass median {apiTimes[apiTimes.Count / 2]:F1} ms; with bodies median {times[times.Count / 2]:F1} ms (min {times[0]:F1})");
foreach (var n in last.ChangedNames.Take(40)) Console.WriteLine("  changed: " + n);

record struct Result(int Units, int Changed, int ApiChanged, int BodyChanged, int OneSided, int BodiesCompared, long IlBytes, int TokenKeys, List<string> ChangedNames);

static class Differ
{
    public static bool Positional = Environment.GetEnvironmentVariable("POSITIONAL") == "1";
    public static Result Run(Side a, Side b, bool bodies)
    {
        var ua = a.Units();
        var ub = b.Units();
        int changed = 0, api = 0, body = 0, oneSided = 0, compared = 0;
        long bytes = 0;
        var names = new List<string>();
        foreach (var (name, typesA) in ua)
        {
            if (!ub.TryGetValue(name, out var typesB)) { changed++; oneSided++; names.Add(name + " (removed)"); continue; }
            // Pass 1: symbolic metadata census (API + one-sided members), no IL.
            Dictionary<string, MethodDefinitionHandle> methodsA, methodsB;
            if (Positional)
            {
                if (!Side.PositionalEqual(a, typesA, b, typesB, out methodsA, out methodsB)) { changed++; api++; names.Add(name + " (metadata)"); continue; }
            }
            else
            {
                var ca = a.Census(typesA, out methodsA);
                var cb = b.Census(typesB, out methodsB);
                if (!ca.SequenceEqual(cb)) { changed++; api++; names.Add(name + " (metadata)"); continue; }
            }
            if (!bodies) continue;
            // Pass 2: lockstep IL, stop at first difference.
            bool diff = false;
            foreach (var (key, ma) in methodsA)
            {
                var mb = methodsB[key];
                compared++;
                if (!BodyEqual(a, ma, b, mb, ref bytes)) { diff = true; break; }
            }
            if (diff) { changed++; body++; names.Add(name + " (body)"); }
        }
        foreach (var name in ub.Keys) if (!ua.ContainsKey(name)) { changed++; oneSided++; names.Add(name + " (added)"); }
        return new Result(ua.Count, changed, api, body, oneSided, compared, bytes, a.KeyCount + b.KeyCount, names);
    }

    static bool BodyEqual(Side a, MethodDefinitionHandle ha, Side b, MethodDefinitionHandle hb, ref long bytes)
    {
        var da = a.Md.GetMethodDefinition(ha);
        var db = b.Md.GetMethodDefinition(hb);
        if ((da.RelativeVirtualAddress == 0) != (db.RelativeVirtualAddress == 0)) return false;
        if (da.RelativeVirtualAddress == 0) return true;
        var ba = a.Pe.GetMethodBody(da.RelativeVirtualAddress);
        var bb = b.Pe.GetMethodBody(db.RelativeVirtualAddress);
        if (ba.MaxStack != bb.MaxStack || ba.LocalVariablesInitialized != bb.LocalVariablesInitialized) return false;
        if (ba.Size != bb.Size) return false; // conservative Changed: encodings are compiler-stable
        if (ba.LocalSignature.IsNil != bb.LocalSignature.IsNil) return false;
        if (!ba.LocalSignature.IsNil && a.Key(ba.LocalSignature) != b.Key(bb.LocalSignature)) return false;
        var ea = ba.ExceptionRegions; var eb = bb.ExceptionRegions;
        if (ea.Length != eb.Length) return false;
        for (int i = 0; i < ea.Length; i++)
        {
            var x = ea[i]; var y = eb[i];
            if (x.Kind != y.Kind || x.TryOffset != y.TryOffset || x.TryLength != y.TryLength || x.HandlerOffset != y.HandlerOffset
                || x.HandlerLength != y.HandlerLength || x.FilterOffset != y.FilterOffset) return false;
            if (x.CatchType.IsNil != y.CatchType.IsNil) return false;
            if (!x.CatchType.IsNil && a.Key(x.CatchType) != b.Key(y.CatchType)) return false;
        }
        var ra = ba.GetILReader(); var rb = bb.GetILReader();
        bytes += ra.Length;
        while (ra.RemainingBytes > 0)
        {
            int op = ra.ReadByte();
            if (op != rb.ReadByte()) return false;
            if (op == 0xFE) { op = 0xFE00 | ra.ReadByte(); if (op != (0xFE00 | rb.ReadByte())) return false; }
            var kind = Il.Operand(op);
            switch (kind)
            {
                case 0: break;
                case 1: if (ra.ReadByte() != rb.ReadByte()) return false; break;
                case 2: if (ra.ReadInt16() != rb.ReadInt16()) return false; break;
                case 4: if (ra.ReadInt32() != rb.ReadInt32()) return false; break;
                case 8: if (ra.ReadInt64() != rb.ReadInt64()) return false; break;
                case 5: // token
                {
                    int ta = ra.ReadInt32(), tb = rb.ReadInt32();
                    if (!a.TokenEqual(ta, b, tb)) return false;
                    break;
                }
                case 6: // string
                {
                    int ta = ra.ReadInt32(), tb = rb.ReadInt32();
                    if (a.Md.GetUserString(MetadataTokens.UserStringHandle(ta & 0xFFFFFF)) != b.Md.GetUserString(MetadataTokens.UserStringHandle(tb & 0xFFFFFF))) return false;
                    break;
                }
                case 7: // switch
                {
                    int n = ra.ReadInt32(); if (n != rb.ReadInt32()) return false;
                    for (int i = 0; i < n; i++) if (ra.ReadInt32() != rb.ReadInt32()) return false;
                    break;
                }
            }
        }
        return true;
    }
}

static class Il
{
    static readonly byte[] One = new byte[256];
    static readonly byte[] Two = new byte[256];
    static Il()
    {
        foreach (var f in typeof(System.Reflection.Emit.OpCodes).GetFields())
        {
            var oc = (System.Reflection.Emit.OpCode)f.GetValue(null);
            byte k = oc.OperandType switch
            {
                System.Reflection.Emit.OperandType.InlineNone => 0,
                System.Reflection.Emit.OperandType.ShortInlineBrTarget or System.Reflection.Emit.OperandType.ShortInlineI or System.Reflection.Emit.OperandType.ShortInlineVar => 1,
                System.Reflection.Emit.OperandType.InlineVar => 2,
                System.Reflection.Emit.OperandType.InlineBrTarget or System.Reflection.Emit.OperandType.InlineI or System.Reflection.Emit.OperandType.ShortInlineR => 4,
                System.Reflection.Emit.OperandType.InlineI8 or System.Reflection.Emit.OperandType.InlineR => 8,
                System.Reflection.Emit.OperandType.InlineString => 6,
                System.Reflection.Emit.OperandType.InlineSwitch => 7,
                _ => 5,
            };
            ushort v = (ushort)oc.Value;
            if (oc.Size == 1) One[v & 0xFF] = k; else Two[v & 0xFF] = k;
        }
    }
    public static int Operand(int op) => op >= 0xFE00 ? Two[op & 0xFF] : One[op];
}

sealed class Side
{
    public readonly PEReader Pe;
    public readonly MetadataReader Md;
    readonly Dictionary<int, string> _keys = new();
    readonly SigProvider _sig;
    public int KeyCount => _keys.Count;

    public Side(byte[] bytes)
    {
        Pe = new PEReader(ImmutableArray.Create(bytes));
        Md = Pe.GetMetadataReader();
        _sig = new SigProvider(this);
    }

    public bool TokenEqual(int ta, Side b, int tb) => Key(MetadataTokens.EntityHandle(ta)) == b.Key(MetadataTokens.EntityHandle(tb));

    // Declaring units: non-generated types, with compiler-generated nested types folded in.
    public Dictionary<string, List<TypeDefinitionHandle>> Units()
    {
        var map = new Dictionary<string, List<TypeDefinitionHandle>>();
        foreach (var h in Md.TypeDefinitions)
        {
            var owner = h;
            while (true)
            {
                var td = Md.GetTypeDefinition(owner);
                var nameStr = Md.GetString(td.Name);
                var decl = td.GetDeclaringType();
                if (nameStr.StartsWith('<') && !decl.IsNil) { owner = decl; continue; }
                break;
            }
            var k = TypeName(owner);
            if (!map.TryGetValue(k, out var list)) map[k] = list = new();
            list.Add(h);
        }
        return map;
    }

    public List<string> Census(List<TypeDefinitionHandle> types, out Dictionary<string, MethodDefinitionHandle> methods)
    {
        var c = new List<string>();
        methods = new();
        foreach (var h in types)
        {
            var td = Md.GetTypeDefinition(h);
            c.Add($"T {TypeName(h)} {(int)td.Attributes} {(td.BaseType.IsNil ? "" : Key(td.BaseType))} {td.GetGenericParameters().Count}");
            foreach (var ih in td.GetInterfaceImplementations()) c.Add("I " + Key(Md.GetInterfaceImplementation(ih).Interface));
            Attrs(c, td.GetCustomAttributes(), "T");
            foreach (var fh in td.GetFields())
            {
                var f = Md.GetFieldDefinition(fh);
                var fk = $"F {TypeName(h)}::{Md.GetString(f.Name)} {(int)f.Attributes} {f.DecodeSignature(_sig, null)}";
                c.Add(fk);
                Attrs(c, f.GetCustomAttributes(), fk);
            }
            foreach (var mh in td.GetMethods())
            {
                var mk = Key(mh);
                var m = Md.GetMethodDefinition(mh);
                c.Add($"M {mk} {(int)m.Attributes} {(int)m.ImplAttributes}");
                Attrs(c, m.GetCustomAttributes(), mk);
                foreach (var ph in m.GetParameters()) Attrs(c, Md.GetParameter(ph).GetCustomAttributes(), mk + "#" + Md.GetParameter(ph).SequenceNumber);
                methods[mk] = mh;
            }
            foreach (var ph in td.GetProperties())
            {
                var p = Md.GetPropertyDefinition(ph);
                var pk = $"P {TypeName(h)}::{Md.GetString(p.Name)} {p.DecodeSignature(_sig, null).ReturnType}";
                c.Add(pk);
                Attrs(c, p.GetCustomAttributes(), pk);
            }
            foreach (var eh in td.GetEvents())
            {
                var e = Md.GetEventDefinition(eh);
                c.Add($"E {TypeName(h)}::{Md.GetString(e.Name)} {Key(e.Type)}");
            }
        }
        c.Sort(StringComparer.Ordinal);
        return c;
    }

    // Positional census: metadata order is declaration order and stable across rebuilds of
    // unchanged source, so compare in table order and treat any misalignment as Changed.
    public static bool PositionalEqual(Side a, List<TypeDefinitionHandle> ta, Side b, List<TypeDefinitionHandle> tb,
        out Dictionary<string, MethodDefinitionHandle> ma, out Dictionary<string, MethodDefinitionHandle> mb)
    {
        ma = new(); mb = new();
        if (ta.Count != tb.Count) return false;
        var A = a.Md; var B = b.Md;
        for (int i = 0; i < ta.Count; i++)
        {
            var x = A.GetTypeDefinition(ta[i]); var y = B.GetTypeDefinition(tb[i]);
            if (x.Attributes != y.Attributes || a.TypeName(ta[i]) != b.TypeName(tb[i])) return false;
            if (x.BaseType.IsNil != y.BaseType.IsNil || (!x.BaseType.IsNil && a.Key(x.BaseType) != b.Key(y.BaseType))) return false;
            if (!AttrsEqual(a, x.GetCustomAttributes(), b, y.GetCustomAttributes())) return false;
            var ia = x.GetInterfaceImplementations(); var ib = y.GetInterfaceImplementations();
            if (ia.Count != ib.Count) return false;
            using (var ea = ia.GetEnumerator()) using (var eb = ib.GetEnumerator())
                while (ea.MoveNext() && eb.MoveNext())
                    if (a.Key(A.GetInterfaceImplementation(ea.Current).Interface) != b.Key(B.GetInterfaceImplementation(eb.Current).Interface)) return false;
            var fa = x.GetFields(); var fb = y.GetFields();
            if (fa.Count != fb.Count) return false;
            using (var ea = fa.GetEnumerator()) using (var eb = fb.GetEnumerator())
                while (ea.MoveNext() && eb.MoveNext())
                {
                    var f1 = A.GetFieldDefinition(ea.Current); var f2 = B.GetFieldDefinition(eb.Current);
                    if (f1.Attributes != f2.Attributes || a.Key(ea.Current) != b.Key(eb.Current)) return false;
                    if (!AttrsEqual(a, f1.GetCustomAttributes(), b, f2.GetCustomAttributes())) return false;
                }
            var mA = x.GetMethods(); var mB = y.GetMethods();
            if (mA.Count != mB.Count) return false;
            using (var ea = mA.GetEnumerator()) using (var eb = mB.GetEnumerator())
                while (ea.MoveNext() && eb.MoveNext())
                {
                    var m1 = A.GetMethodDefinition(ea.Current); var m2 = B.GetMethodDefinition(eb.Current);
                    var k1 = a.Key(ea.Current); var k2 = b.Key(eb.Current);
                    if (m1.Attributes != m2.Attributes || m1.ImplAttributes != m2.ImplAttributes || k1 != k2) return false;
                    if (!AttrsEqual(a, m1.GetCustomAttributes(), b, m2.GetCustomAttributes())) return false;
                    var p1 = m1.GetParameters(); var p2 = m2.GetParameters();
                    if (p1.Count != p2.Count) return false;
                    using (var qa = p1.GetEnumerator()) using (var qb = p2.GetEnumerator())
                        while (qa.MoveNext() && qb.MoveNext())
                        {
                            var r1 = A.GetParameter(qa.Current); var r2 = B.GetParameter(qb.Current);
                            if (r1.Attributes != r2.Attributes || A.GetString(r1.Name) != B.GetString(r2.Name)) return false;
                            if (!AttrsEqual(a, r1.GetCustomAttributes(), b, r2.GetCustomAttributes())) return false;
                        }
                    ma[k1] = ea.Current; mb[k2] = eb.Current;
                }
            var pa = x.GetProperties(); var pb = y.GetProperties();
            if (pa.Count != pb.Count) return false;
            using (var ea = pa.GetEnumerator()) using (var eb = pb.GetEnumerator())
                while (ea.MoveNext() && eb.MoveNext())
                {
                    var p1 = A.GetPropertyDefinition(ea.Current); var p2 = B.GetPropertyDefinition(eb.Current);
                    if (A.GetString(p1.Name) != B.GetString(p2.Name) || a.PropertySignature(p1) != b.PropertySignature(p2)) return false;
                    if (!AttrsEqual(a, p1.GetCustomAttributes(), b, p2.GetCustomAttributes())) return false;
                }
            var va = x.GetEvents(); var vb = y.GetEvents();
            if (va.Count != vb.Count) return false;
            using (var ea = va.GetEnumerator()) using (var eb = vb.GetEnumerator())
                while (ea.MoveNext() && eb.MoveNext())
                {
                    var e1 = A.GetEventDefinition(ea.Current); var e2 = B.GetEventDefinition(eb.Current);
                    if (A.GetString(e1.Name) != B.GetString(e2.Name) || a.Key(e1.Type) != b.Key(e2.Type)) return false;
                    if (!AttrsEqual(a, e1.GetCustomAttributes(), b, e2.GetCustomAttributes())) return false;
                }
        }
        return true;
    }

    string PropertySignature(PropertyDefinition p)
    {
        var sig = p.DecodeSignature(_sig, null);
        return $"{sig.Header.IsInstance}({string.Join(",", sig.ParameterTypes)}){sig.ReturnType}";
    }

    static bool AttrsEqual(Side a, CustomAttributeHandleCollection x, Side b, CustomAttributeHandleCollection y)
    {
        if (x.Count != y.Count) return false;
        using var ea = x.GetEnumerator(); using var eb = y.GetEnumerator();
        while (ea.MoveNext() && eb.MoveNext())
        {
            var c1 = a.Md.GetCustomAttribute(ea.Current); var c2 = b.Md.GetCustomAttribute(eb.Current);
            if (a.Key(c1.Constructor) != b.Key(c2.Constructor)) return false;
            var r1 = a.Md.GetBlobReader(c1.Value); var r2 = b.Md.GetBlobReader(c2.Value);
            if (r1.Length != r2.Length) return false;
            unsafe { if (!new ReadOnlySpan<byte>(r1.StartPointer, r1.Length).SequenceEqual(new ReadOnlySpan<byte>(r2.StartPointer, r2.Length))) return false; }
        }
        return true;
    }

    void Attrs(List<string> c, CustomAttributeHandleCollection attrs, string owner)
    {
        foreach (var ah in attrs)
        {
            var ca = Md.GetCustomAttribute(ah);
            c.Add($"A {owner} {Key(ca.Constructor)} {Convert.ToHexString(Md.GetBlobBytes(ca.Value))}");
        }
    }

    public string TypeName(TypeDefinitionHandle h)
    {
        int tok = MetadataTokens.GetToken(h);
        if (_keys.TryGetValue(tok, out var s)) return s;
        var td = Md.GetTypeDefinition(h);
        var decl = td.GetDeclaringType();
        s = decl.IsNil
            ? (td.Namespace.IsNil ? "" : Md.GetString(td.Namespace) + ".") + Md.GetString(td.Name)
            : TypeName(decl) + "/" + Md.GetString(td.Name);
        _keys[tok] = s;
        return s;
    }

    public string Key(EntityHandle h)
    {
        int tok = MetadataTokens.GetToken(h);
        if (_keys.TryGetValue(tok, out var s)) return s;
        s = h.Kind switch
        {
            HandleKind.TypeDefinition => TypeName((TypeDefinitionHandle)h),
            HandleKind.TypeReference => TypeRefName((TypeReferenceHandle)h),
            HandleKind.TypeSpecification => Md.GetTypeSpecification((TypeSpecificationHandle)h).DecodeSignature(_sig, null),
            HandleKind.MethodDefinition => MethodDefKey((MethodDefinitionHandle)h),
            HandleKind.FieldDefinition => FieldDefKey((FieldDefinitionHandle)h),
            HandleKind.MemberReference => MemberRefKey((MemberReferenceHandle)h),
            HandleKind.MethodSpecification => MethodSpecKey((MethodSpecificationHandle)h),
            HandleKind.StandaloneSignature => StandaloneKey((StandaloneSignatureHandle)h),
            _ => h.Kind + ":" + tok,
        };
        _keys[tok] = s;
        return s;
    }

    string TypeRefName(TypeReferenceHandle h)
    {
        var tr = Md.GetTypeReference(h);
        var name = (tr.Namespace.IsNil ? "" : Md.GetString(tr.Namespace) + ".") + Md.GetString(tr.Name);
        return tr.ResolutionScope.Kind == HandleKind.TypeReference ? Key(tr.ResolutionScope) + "/" + name : name;
    }

    string MethodDefKey(MethodDefinitionHandle h)
    {
        var m = Md.GetMethodDefinition(h);
        var sig = m.DecodeSignature(_sig, null);
        return $"{TypeName(m.GetDeclaringType())}::{Md.GetString(m.Name)}`{sig.GenericParameterCount}({string.Join(",", sig.ParameterTypes)}){sig.ReturnType}";
    }

    string FieldDefKey(FieldDefinitionHandle h)
    {
        var f = Md.GetFieldDefinition(h);
        return $"{TypeName(f.GetDeclaringType())}::{Md.GetString(f.Name)}:{f.DecodeSignature(_sig, null)}";
    }

    string MemberRefKey(MemberReferenceHandle h)
    {
        var r = Md.GetMemberReference(h);
        var parent = Key(r.Parent);
        if (r.GetKind() == MemberReferenceKind.Field) return $"{parent}::{Md.GetString(r.Name)}:{r.DecodeFieldSignature(_sig, null)}";
        var sig = r.DecodeMethodSignature(_sig, null);
        return $"{parent}::{Md.GetString(r.Name)}`{sig.GenericParameterCount}({string.Join(",", sig.ParameterTypes)}){sig.ReturnType}";
    }

    string MethodSpecKey(MethodSpecificationHandle h)
    {
        var s = Md.GetMethodSpecification(h);
        return Key(s.Method) + "<" + string.Join(",", s.DecodeSignature(_sig, null)) + ">";
    }

    string StandaloneKey(StandaloneSignatureHandle h)
    {
        var s = Md.GetStandaloneSignature(h);
        if (s.GetKind() == StandaloneSignatureKind.LocalVariables) return "L(" + string.Join(",", s.DecodeLocalSignature(_sig, null)) + ")";
        var m = s.DecodeMethodSignature(_sig, null);
        return $"S({string.Join(",", m.ParameterTypes)}){m.ReturnType}";
    }

    sealed class SigProvider(Side side) : ISignatureTypeProvider<string, object>
    {
        public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
        public string GetByReferenceType(string e) => e + "&";
        public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr(" + string.Join(",", s.ParameterTypes) + ")" + s.ReturnType;
        public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
        public string GetGenericMethodParameter(object c, int i) => "!!" + i;
        public string GetGenericTypeParameter(object c, int i) => "!" + i;
        public string GetModifiedType(string m, string u, bool r) => u + (r ? " modreq(" : " modopt(") + m + ")";
        public string GetPinnedType(string e) => e + " pinned";
        public string GetPointerType(string e) => e + "*";
        public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString();
        public string GetSZArrayType(string e) => e + "[]";
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => side.TypeName(h);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => side.Key(h);
        public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => side.Key(h);
    }
}
