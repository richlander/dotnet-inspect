using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The semantic-fidelity anchor (the validity anchor is <see cref="CompileChecker"/>;
/// the agreement anchor is the diff; the quality anchor is <see cref="SourceGrader"/>).
/// It closes the loop named in docs/decompiler-pipeline.md: decompile → recompile →
/// compare IL. A decompiled body that compiles and reads plausibly but recompiles
/// to a different opcode stream changed the program — the worst failure class
/// (docs/decompiler-taste.md), invisible to compile-check and source-grade.
///
/// Unlike <see cref="CompileChecker"/>'s per-method <c>__Shell</c> — which cannot
/// see the declaring type's fields, so any <c>this.field</c> reference fails to
/// bind as noise — this recompiles each member inside a reconstructed shape of
/// its REAL declaring type: the type declaration, every field, every sibling and
/// nested member as a throwing stub, and the one target member's real decompiled
/// body. The C# analog of the IL round-trip suite's full-skeleton scaffold
/// (IlasmScaffold.BuildCompilationUnit). Fields in scope mean a dropped or
/// mis-bound field access surfaces as a true opcode diff, not a compile error.
/// </summary>
static class CompileBack
{
    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        var references = RuntimeReferences();
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        // Release codegen so the recompiled stream is compared against the
        // optimization shape the BCL ships; the fixture assembly is built the
        // same way under -c Release.
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable);

        int total = 0, full = 0, exact = 0, contextFail = 0, recompileFail = 0, diffCount = 0;
        var diffExamples = new List<string>();
        var recompileFailCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var path in assemblies)
        {
            if (total >= cap)
                break;
            PEReader pe;
            try { pe = new PEReader(File.OpenRead(path)); }
            catch { continue; }
            using (pe)
            {
                if (!pe.HasMetadata)
                    continue;
                var reader = pe.GetMetadataReader();
                MetadataSource source;
                try { source = MetadataSource.Open(path); }
                catch { continue; }
                using (source)
                {
                    foreach (var typeHandle in reader.TypeDefinitions)
                    {
                        if (total >= cap)
                            break;
                        RunType(reader, pe, source, typeHandle, references, parseOptions, compileOptions,
                            cap, maxExamples, ref total, ref full, ref exact, ref contextFail,
                            ref recompileFail, ref diffCount, diffExamples, recompileFailCodes);
                    }
                }
            }
        }

        Report(total, full, exact, contextFail, recompileFail, diffCount,
            recompileFailCodes, diffExamples);
        return 0;
    }

    static void RunType(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        ImmutableArray<MetadataReference> references, CSharpParseOptions parseOptions,
        CSharpCompilationOptions compileOptions, int cap, int maxExamples,
        ref int total, ref int full, ref int exact, ref int contextFail,
        ref int recompileFail, ref int diffCount,
        List<string> diffExamples, SortedDictionary<string, int> recompileFailCodes)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        if (!typeDef.GetDeclaringType().IsNil)
            return; // nested types are emitted by their enclosing type
        var kind = ShapeOf(reader, typeDef);
        if (kind is not (TypeKind.Class or TypeKind.Struct))
            return; // only class/struct hold the instance bodies we recompile

        string ns = reader.GetString(typeDef.Namespace);
        string tn = reader.GetString(typeDef.Name);
        string fullType = ns.Length == 0 ? tn : $"{ns}.{tn}";
        if (fullType.Contains('<'))
            return;
        if (Environment.GetEnvironmentVariable("CB_TYPE") is { } filter && !fullType.Contains(filter, StringComparison.Ordinal))
            return;

        var overloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var mh in typeDef.GetMethods())
        {
            if (total >= cap)
                return;
            var method = reader.GetMethodDefinition(mh);
            string name = reader.GetString(method.Name);
            string key = $"{fullType}::{name}";
            int overload = overloads.GetValueOrDefault(key);
            overloads[key] = overload + 1;
            if (method.RelativeVirtualAddress == 0)
                continue;
            if (name.Contains('<'))
                continue; // compiler-generated: name is not a valid identifier

            var function = IrImporter.Import(source, fullType, name, overload);
            if (function is null)
                continue;
            string? body;
            try { body = CSharpPrinter.PrintRaised(function).Output; }
            catch { continue; }
            if (body is null)
                continue;
            total++;
            bool isFull = function.Fidelity == DecompilationFidelity.Full;
            if (isFull) full++;

            var original = ILDisassembler.Disassemble(pe, reader, method);
            if (original is null)
                continue;
            var origOps = original.Select(i => CanonicalOpcode(i.OpCodeName)).ToList();

            string unit;
            try { unit = BuildUnit(reader, typeHandle, mh, body); }
            catch { contextFail++; continue; }

            var tree = CSharpSyntaxTree.ParseText(unit, parseOptions);
            var comp = CSharpCompilation.Create("cb", [tree], references, compileOptions);
            using var ms = new MemoryStream();
            var emit = comp.Emit(ms);
            if (!emit.Success)
            {
                recompileFail++;
                var err = emit.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
                if (err is not null)
                    recompileFailCodes[err.Id] = recompileFailCodes.GetValueOrDefault(err.Id) + 1;
                if (Environment.GetEnvironmentVariable("CB_DUMP") is not null && recompileFail <= 2)
                    Console.Error.WriteLine($"==== {fullType}::{name} ====\n{err?.Id}: {err?.GetMessage()} @ {err?.Location.GetLineSpan().StartLinePosition}\n{unit}\n");
                continue;
            }

            ms.Position = 0;
            using var rpe = new PEReader(ms);
            var rOps = FindAndDisassemble(rpe, fullType, name, overload)
                ?.Select(i => CanonicalOpcode(i.OpCodeName)).ToList();
            if (rOps is null)
            {
                recompileFail++;
                recompileFailCodes["<not-found>"] = recompileFailCodes.GetValueOrDefault("<not-found>") + 1;
                continue;
            }

            if (origOps.SequenceEqual(rOps))
            {
                exact++;
            }
            else if (isFull)
            {
                diffCount++;
                if (diffExamples.Count < maxExamples)
                    diffExamples.Add(
                        $"{fullType}::{name}\n    orig : {string.Join(" ", origOps)}\n    recmp: {string.Join(" ", rOps)}");
            }
        }
    }

    static void Report(
        int total, int full, int exact, int contextFail, int recompileFail, int diffCount,
        SortedDictionary<string, int> recompileFailCodes, List<string> diffExamples)
    {
        string Pct(int n, int d) => d == 0 ? "0" : $"{100.0 * n / d:F2}%";
        Console.WriteLine($"COMPILE-BACK over {total} rendered methods ({full} Full)");
        Console.WriteLine();
        Console.WriteLine($"  exact opcode match : {exact} ({Pct(exact, total)})");
        Console.WriteLine($"  opcode diff (Full) : {diffCount} — recompiled to a different stream (the docket)");
        Console.WriteLine($"  context-build fail : {contextFail} — could not emit the type skeleton");
        Console.WriteLine($"  recompile fail     : {recompileFail} — skeleton + body did not compile");
        if (recompileFailCodes.Count > 0)
        {
            Console.WriteLine("  recompile-fail by code:");
            foreach (var (code, n) in recompileFailCodes.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"    {code}: {n}");
        }
        if (diffExamples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Opcode-diff examples (Full):");
            foreach (var e in diffExamples)
                Console.WriteLine($"  {e}");
        }
    }

    // ---- Type-skeleton emission (the C# analog of IlasmScaffold) ----

    /// <summary>
    /// Emits a compilation unit: the declaring type reconstructed with all fields
    /// and all members as throwing stubs, except <paramref name="target"/>, which
    /// carries its real decompiled <paramref name="targetBody"/>. Nested types are
    /// emitted recursively (enums with their members) so the body's references to
    /// sibling/nested types and fields all bind.
    /// </summary>
    static string BuildUnit(MetadataReader reader, TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle target, string targetBody)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#pragma warning disable");
        var typeDef = reader.GetTypeDefinition(typeHandle);
        string ns = reader.GetString(typeDef.Namespace);
        if (ns.Length > 0)
        {
            sb.AppendLine($"namespace {ns}");
            sb.AppendLine("{");
        }
        EmitType(reader, typeHandle, target, targetBody, sb, ns.Length > 0 ? 1 : 0);
        if (ns.Length > 0)
            sb.AppendLine("}");
        return sb.ToString();
    }

    static void EmitType(MetadataReader reader, TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle target, string targetBody, StringBuilder sb, int indent)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var kind = ShapeOf(reader, typeDef);
        string pad = new(' ', indent * 4);
        string name = reader.GetString(typeDef.Name);
        var typeContext = GenericContext.ForType(reader, typeDef);

        if (kind == TypeKind.Enum)
        {
            EmitEnum(reader, typeDef, name, sb, pad);
            return;
        }

        string genParams = GenericParamList(reader, typeDef.GetGenericParameters());
        string keyword = kind == TypeKind.Struct ? "struct" : "class";
        sb.AppendLine($"{pad}public unsafe {keyword} {Identifier(name)}{genParams}");
        sb.AppendLine($"{pad}{{");

        foreach (var fh in typeDef.GetFields())
            EmitField(reader, fh, typeContext, sb, pad + "    ");

        foreach (var mh in typeDef.GetMethods())
            EmitMethod(reader, typeHandle, mh, mh == target ? targetBody : null, sb, pad + "    ");

        foreach (var nested in typeDef.GetNestedTypes())
        {
            if (reader.GetString(reader.GetTypeDefinition(nested).Name).Contains('<'))
                continue; // compiler-generated (display class, iterator) — not valid C#
            EmitType(reader, nested, target, targetBody, sb, indent + 1);
        }

        sb.AppendLine($"{pad}}}");
    }

    static void EmitEnum(MetadataReader reader, TypeDefinition typeDef, string name, StringBuilder sb, string pad)
    {
        string underlying = "int";
        var members = new List<string>();
        foreach (var fh in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fh);
            string fname = reader.GetString(field.Name);
            if (fname == "value__")
            {
                underlying = field.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForType(reader, typeDef));
                continue;
            }
            if (fname.Contains('<'))
                continue;
            string? value = ConstantText(reader, field.GetDefaultValue());
            members.Add(value is null ? Identifier(fname) : $"{Identifier(fname)} = {value}");
        }
        sb.AppendLine($"{pad}public enum {Identifier(name)} : {underlying}");
        sb.AppendLine($"{pad}{{");
        foreach (var m in members)
            sb.AppendLine($"{pad}    {m},");
        sb.AppendLine($"{pad}}}");
    }

    static void EmitField(MetadataReader reader, FieldDefinitionHandle fh, GenericContext context,
        StringBuilder sb, string pad)
    {
        var field = reader.GetFieldDefinition(fh);
        string name = reader.GetString(field.Name);
        if (name.Contains('<'))
            return; // compiler-generated backing field
        string type;
        try { type = field.DecodeSignature(SignatureDecoder.Instance, context); }
        catch { return; }

        bool isConst = field.Attributes.HasFlag(FieldAttributes.Literal);
        bool isStatic = field.Attributes.HasFlag(FieldAttributes.Static);
        if (isConst)
        {
            string? value = ConstantText(reader, field.GetDefaultValue());
            if (value is null)
                return; // can't synthesize an initializer — drop it
            sb.AppendLine($"{pad}public const {Clean(type)} {Identifier(name)} = {value};");
            return;
        }
        sb.AppendLine($"{pad}public {(isStatic ? "static " : "")}{Clean(type)} {Identifier(name)};");
    }

    static void EmitMethod(MetadataReader reader, TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle mh, string? realBody, StringBuilder sb, string pad)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var method = reader.GetMethodDefinition(mh);
        string name = reader.GetString(method.Name);
        if (name.Contains('<') && name is not ".ctor" and not ".cctor")
            return; // compiler-generated
        if (method.RelativeVirtualAddress == 0 && realBody is null)
            return; // abstract/extern sibling — no body, and we strip abstractness

        var context = GenericContext.ForMethod(reader, typeDef, method);
        MethodSignature<string> sig;
        try { sig = method.DecodeSignature(SignatureDecoder.Instance, context); }
        catch { return; }

        bool isStatic = method.Attributes.HasFlag(MethodAttributes.Static);
        string parameters = Parameters(reader, method, sig);
        string body = realBody is null ? " throw null;" : "\n" + realBody + "\n" + pad;

        if (name is ".ctor")
        {
            // Instance constructor: emit as the type's ctor so signature codegen
            // (field inits, base call) matches; strip the IL name.
            sb.AppendLine($"{pad}public {Identifier(reader.GetString(typeDef.Name))}({parameters}) {{{body}}}");
            return;
        }
        if (name is ".cctor")
        {
            sb.AppendLine($"{pad}static {Identifier(reader.GetString(typeDef.Name))}() {{{body}}}");
            return;
        }

        string genParams = GenericParamList(reader, method.GetGenericParameters());
        sb.AppendLine($"{pad}public {(isStatic ? "static " : "")}{Clean(sig.ReturnType)} {Identifier(name)}{genParams}({parameters}) {{{body}}}");
    }

    static string Parameters(MetadataReader reader, MethodDefinition method, MethodSignature<string> sig)
    {
        var names = new Dictionary<int, string>();
        foreach (var ph in method.GetParameters())
        {
            var p = reader.GetParameter(ph);
            if (p.SequenceNumber >= 1)
                names[p.SequenceNumber - 1] = reader.GetString(p.Name);
        }
        var parts = new List<string>();
        for (int i = 0; i < sig.ParameterTypes.Length; i++)
        {
            string name = names.TryGetValue(i, out var n) && n.Length > 0 ? n : $"arg{i}";
            parts.Add($"{Clean(sig.ParameterTypes[i])} {Identifier(name)}");
        }
        return string.Join(", ", parts);
    }

    // ---- Small helpers ----

    enum TypeKind { Class, Struct, Enum, Interface, Delegate }

    static TypeKind ShapeOf(MetadataReader reader, TypeDefinition typeDef)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return TypeKind.Interface;
        if (typeDef.BaseType.IsNil)
            return TypeKind.Class;
        string baseName = BaseTypeName(reader, typeDef.BaseType);
        return baseName switch
        {
            "System.Enum" => TypeKind.Enum,
            "System.ValueType" => TypeKind.Struct,
            "System.MulticastDelegate" or "System.Delegate" => TypeKind.Delegate,
            _ => TypeKind.Class,
        };
    }

    static string BaseTypeName(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)handle)),
        HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
        _ => "",
    };

    static string FullName(MetadataReader reader, TypeReference t)
    {
        string ns = reader.GetString(t.Namespace);
        string n = reader.GetString(t.Name);
        return ns.Length == 0 ? n : $"{ns}.{n}";
    }

    static string FullName(MetadataReader reader, TypeDefinition t)
    {
        string ns = reader.GetString(t.Namespace);
        string n = reader.GetString(t.Name);
        return ns.Length == 0 ? n : $"{ns}.{n}";
    }

    static string GenericParamList(MetadataReader reader, GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return "";
        var names = handles.Select(h => Identifier(reader.GetString(reader.GetGenericParameter(h).Name)));
        return "<" + string.Join(", ", names) + ">";
    }

    /// <summary>C# keywords that can appear as IL identifiers get an @ escape.</summary>
    static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
        || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
        ? "@" + name : name;

    /// <summary>
    /// The lightweight <see cref="SignatureDecoder"/> renders some shapes as
    /// invalid C#: function pointers as a bare <c>delegate*</c> (no signature),
    /// and unresolved generic parameters as <c>!n</c>/<c>!!n</c>. A single such
    /// member would sink the whole single-unit skeleton, so spellings are
    /// repaired to the nearest compiling form. The target body rarely touches
    /// these; when it does the diff is honestly attributable to the gap.
    /// </summary>
    static string Clean(string type)
    {
        if (type.Contains('!'))
            return "object";
        if (type.Contains("delegate*"))
            return "void*"; // a pointer-sized stand-in; calls through it are rare
        return type;
    }

    static string? ConstantText(MetadataReader reader, ConstantHandle handle)
    {
        if (handle.IsNil)
            return null;
        var constant = reader.GetConstant(handle);
        var blob = reader.GetBlobReader(constant.Value);
        try
        {
            return constant.TypeCode switch
            {
                ConstantTypeCode.Boolean => blob.ReadBoolean() ? "true" : "false",
                ConstantTypeCode.Char => $"(char){(int)blob.ReadChar()}",
                ConstantTypeCode.SByte => blob.ReadSByte().ToString(),
                ConstantTypeCode.Byte => blob.ReadByte().ToString(),
                ConstantTypeCode.Int16 => blob.ReadInt16().ToString(),
                ConstantTypeCode.UInt16 => blob.ReadUInt16().ToString(),
                ConstantTypeCode.Int32 => blob.ReadInt32().ToString(),
                ConstantTypeCode.UInt32 => blob.ReadUInt32() + "U",
                ConstantTypeCode.Int64 => blob.ReadInt64() + "L",
                ConstantTypeCode.UInt64 => blob.ReadUInt64() + "UL",
                ConstantTypeCode.Single => Invariant(blob.ReadSingle()) + "f",
                ConstantTypeCode.Double => Invariant(blob.ReadDouble()),
                _ => null,
            };
        }
        catch { return null; }
    }

    static string Invariant(double d) => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    static List<ILInstruction>? FindAndDisassemble(PEReader pe, string fullType, string name, int overload)
    {
        var reader = pe.GetMetadataReader();
        int seen = 0;
        foreach (var tdh in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(tdh);
            string ns = reader.GetString(td.Namespace);
            string tn = reader.GetString(td.Name);
            string ft = ns.Length == 0 ? tn : $"{ns}.{tn}";
            if (ft != fullType)
                continue;
            // The IL names .ctor/.cctor become the type name / static-ctor in C#;
            // map back so the target re-resolves by its metadata name.
            string match = name is ".ctor" or ".cctor" ? name : name;
            foreach (var mh in td.GetMethods())
            {
                var m = reader.GetMethodDefinition(mh);
                string mn = reader.GetString(m.Name);
                if (mn != match)
                    continue;
                if (seen++ == overload)
                    return ILDisassembler.Disassemble(pe, reader, m);
            }
        }
        return null;
    }

    static string CanonicalOpcode(string op)
    {
        string trimmed = op.EndsWith(".s", StringComparison.Ordinal) ? op[..^2] : op;
        if (trimmed.StartsWith("ldarg", StringComparison.Ordinal)) return "ldarg";
        if (trimmed.StartsWith("ldloc", StringComparison.Ordinal)) return "ldloc";
        if (trimmed.StartsWith("stloc", StringComparison.Ordinal)) return "stloc";
        if (trimmed.StartsWith("ldc.i4", StringComparison.Ordinal)) return "ldc.i4";
        return trimmed;
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
    {
        var tpa = (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var path in tpa)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            try { builder.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }
        return builder.ToImmutable();
    }
}
