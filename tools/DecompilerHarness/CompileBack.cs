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
                var references = RuntimeReferences(path);
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

    /// <summary>The compile-back outcome for one method.</summary>
    public enum CompileBackStatus
    {
        /// <summary>Recompiled to the same canonical opcode stream — the goal.</summary>
        Exact,
        /// <summary>Rendered at Full fidelity but recompiled to a different stream (a defect).</summary>
        OpcodeDiff,
        /// <summary>Imported below Full fidelity, so an opcode diff is expected, not a defect.</summary>
        NotFull,
        /// <summary>The decompiled body did not recompile (e.g. an unbindable construct).</summary>
        RecompileFail,
        /// <summary>The type skeleton could not be emitted or the original/recompiled method was not found.</summary>
        ContextFail,
    }

    /// <summary>One method's compile-back result, with both opcode streams for diagnostics.</summary>
    public sealed record CompileBackResult(
        string Type, string Method, int Overload, CompileBackStatus Status,
        string OriginalOpcodes, string RecompiledOpcodes, string? Detail);

    /// <summary>
    /// Runs the compile-back loop over one assembly and returns a structured result
    /// per rendered method, without printing. This is the testable entry point the
    /// xunit gate uses to assert the green set stays opcode-exact; <see cref="Run"/>
    /// is the console-reporting entry point. Shares all of the skeleton-emission and
    /// opcode-comparison machinery so the two paths can never drift.
    /// </summary>
    public static IReadOnlyList<CompileBackResult> Evaluate(string assemblyPath)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable);

        var results = new List<CompileBackResult>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            return results;
        var reader = pe.GetMetadataReader();
        using var source = MetadataSource.Open(assemblyPath);
        var references = RuntimeReferences(assemblyPath);

        foreach (var typeHandle in reader.TypeDefinitions)
            EvaluateType(reader, pe, source, typeHandle, references, parseOptions, compileOptions, results);

        return results;
    }

    static void EvaluateType(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        ImmutableArray<MetadataReference> references, CSharpParseOptions parseOptions,
        CSharpCompilationOptions compileOptions, List<CompileBackResult> results)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        if (!typeDef.GetDeclaringType().IsNil)
            return; // nested types are emitted by their enclosing type
        var kind = ShapeOf(reader, typeDef);
        if (kind is not (TypeKind.Class or TypeKind.Struct))
            return;

        string ns = reader.GetString(typeDef.Namespace);
        string tn = reader.GetString(typeDef.Name);
        string fullType = ns.Length == 0 ? tn : $"{ns}.{tn}";
        if (fullType.Contains('<'))
            return;

        var overloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var mh in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(mh);
            string name = reader.GetString(method.Name);
            string key = $"{fullType}::{name}";
            int overload = overloads.GetValueOrDefault(key);
            overloads[key] = overload + 1;
            if (method.RelativeVirtualAddress == 0)
                continue;
            if (name.Contains('<'))
                continue;

            var function = IrImporter.Import(source, fullType, name, overload);
            if (function is null)
                continue;
            string? body;
            string? chain;
            IReadOnlyList<(string Field, string Value)> fieldInits;
            try { var printed = CSharpPrinter.PrintRaised(function); body = printed.Output; chain = printed.ConstructorChain; fieldInits = printed.FieldInitializers; }
            catch { continue; }
            if (body is null)
                continue;

            bool isFull = function.Fidelity == DecompilationFidelity.Full;
            var original = ILDisassembler.Disassemble(pe, reader, method);
            if (original is null)
                continue;
            var origOps = original.Select(i => CanonicalOpcode(i.OpCodeName)).ToList();
            string origText = string.Join(" ", origOps);

            string unit;
            try { unit = BuildUnit(reader, mh, body, chain, fieldInits); }
            catch
            {
                results.Add(new(fullType, name, overload, CompileBackStatus.ContextFail, origText, "", "skeleton-emit"));
                continue;
            }

            var tree = CSharpSyntaxTree.ParseText(unit, parseOptions);
            var comp = CSharpCompilation.Create("cb", [tree], references, compileOptions);
            using var ms = new MemoryStream();
            var emit = comp.Emit(ms);
            if (!emit.Success)
            {
                var err = emit.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
                results.Add(new(fullType, name, overload, CompileBackStatus.RecompileFail, origText, "", err?.Id));
                continue;
            }

            ms.Position = 0;
            using var rpe = new PEReader(ms);
            var rOps = FindAndDisassemble(rpe, fullType, name, overload)
                ?.Select(i => CanonicalOpcode(i.OpCodeName)).ToList();
            if (rOps is null)
            {
                results.Add(new(fullType, name, overload, CompileBackStatus.ContextFail, origText, "", "method-not-found"));
                continue;
            }

            string recompText = string.Join(" ", rOps);
            var status = origOps.SequenceEqual(rOps) ? CompileBackStatus.Exact
                : isFull ? CompileBackStatus.OpcodeDiff
                : CompileBackStatus.NotFull;
            results.Add(new(fullType, name, overload, status, origText, recompText, null));
        }
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
            string? chain;
            IReadOnlyList<(string Field, string Value)> fieldInits;
            try { var printed = CSharpPrinter.PrintRaised(function); body = printed.Output; chain = printed.ConstructorChain; fieldInits = printed.FieldInitializers; }
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
            try { unit = BuildUnit(reader, mh, body, chain, fieldInits); }
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
    /// Emits a compilation unit for the WHOLE module: every top-level type
    /// reconstructed with all fields and all members as throwing stubs, except
    /// the one <paramref name="target"/> method (in type <paramref name="targetType"/>),
    /// which carries its real decompiled <paramref name="targetBody"/>. Nested
    /// types are emitted recursively. The C# analog of the IL round-trip suite's
    /// full-skeleton scaffold (IlasmScaffold.BuildCompilationUnit): with every
    /// sibling type and every internal member present (and public), the target
    /// body's references to same-assembly types and members all bind — so a
    /// dropped or mis-bound access surfaces as a true opcode diff, not CS0234/CS0122.
    /// </summary>
    static string BuildUnit(MetadataReader reader, MethodDefinitionHandle target, string targetBody, string? targetChain,
        IReadOnlyList<(string Field, string Value)> targetFieldInits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#pragma warning disable");
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!typeDef.GetDeclaringType().IsNil)
                continue; // nested types are emitted by their enclosing type
            string name = reader.GetString(typeDef.Name);
            if (name.Contains('<') || name == "<Module>")
                continue; // compiler-generated / module pseudo-type
            string ns = reader.GetString(typeDef.Namespace);
            if (ns.Length > 0)
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
                EmitType(reader, typeHandle, target, targetBody, targetChain, targetFieldInits, sb, 1);
                sb.AppendLine("}");
            }
            else
            {
                EmitType(reader, typeHandle, target, targetBody, targetChain, targetFieldInits, sb, 0);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// A <c>: Base</c> clause for a class whose base is a non-generic type in
    /// this assembly (so its constructors are visible to a lifted
    /// <c>: base(args)</c> initializer). Object and value-type bases need no
    /// clause; generic bases (TypeSpec) and out-of-assembly bases are skipped —
    /// the skeleton cannot always spell those, and an absent clause only costs a
    /// base-call diff, never a miscompile.
    /// </summary>
    static string BaseClause(MetadataReader reader, TypeDefinition typeDef, TypeKind kind)
    {
        if (kind != TypeKind.Class || typeDef.BaseType.IsNil)
            return "";
        if (typeDef.BaseType.Kind != HandleKind.TypeDefinition)
            return ""; // TypeReference (out of assembly) / TypeSpec (generic) — not reliably spellable
        var baseDef = reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType);
        if (baseDef.GetGenericParameters().Count != 0)
            return "";
        string baseName = BaseTypeName(reader, typeDef.BaseType);
        if (baseName is "System.Object")
            return "";
        return $" : {baseName}";
    }

    static void EmitType(MetadataReader reader, TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle target, string targetBody, string? targetChain,
        IReadOnlyList<(string Field, string Value)> targetFieldInits, StringBuilder sb, int indent)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var kind = ShapeOf(reader, typeDef);
        string pad = new(' ', indent * 4);
        string name = StripArity(reader.GetString(typeDef.Name));
        var typeContext = GenericContext.ForType(reader, typeDef);

        if (kind == TypeKind.Enum)
        {
            EmitEnum(reader, typeDef, name, sb, pad);
            return;
        }

        string genParams = GenericParamList(reader, typeDef.GetGenericParameters());

        if (kind == TypeKind.Delegate)
        {
            EmitDelegate(reader, typeDef, name, genParams, typeContext, sb, pad);
            return;
        }

        if (kind == TypeKind.Interface)
        {
            EmitInterface(reader, typeHandle, name, genParams, sb, pad, indent);
            return;
        }

        string keyword = kind == TypeKind.Struct ? "struct" : "class";
        string baseClause = BaseClause(reader, typeDef, kind);
        sb.AppendLine($"{pad}public unsafe {keyword} {Identifier(name)}{genParams}{baseClause}");
        sb.AppendLine($"{pad}{{");

        // Field initializers lifted from the target ctor apply to this type's
        // fields only when it declares that ctor.
        bool declaresTarget = !target.IsNil && typeDef.GetMethods().Any(mh => mh == target);
        var fieldInits = declaresTarget ? targetFieldInits : [];

        foreach (var fh in typeDef.GetFields())
            EmitField(reader, fh, typeContext, fieldInits, sb, pad + "    ");

        foreach (var mh in typeDef.GetMethods())
            EmitMethod(reader, typeHandle, mh, mh == target ? targetBody : null, mh == target ? targetChain : null, sb, pad + "    ");

        foreach (var nested in typeDef.GetNestedTypes())
        {
            if (reader.GetString(reader.GetTypeDefinition(nested).Name).Contains('<'))
                continue; // compiler-generated (display class, iterator) — not valid C#
            EmitType(reader, nested, target, targetBody, targetChain, targetFieldInits, sb, indent + 1);
        }

        sb.AppendLine($"{pad}}}");
    }

    /// <summary>
    /// A sibling delegate type, reconstructed from its <c>Invoke</c> signature so
    /// references to it (and <c>new D(...)</c> / invocation) bind in a target body.
    /// </summary>
    static void EmitDelegate(MetadataReader reader, TypeDefinition typeDef, string name,
        string genParams, GenericContext typeContext, StringBuilder sb, string pad)
    {
        string ret = "void", parameters = "";
        foreach (var mh in typeDef.GetMethods())
        {
            var m = reader.GetMethodDefinition(mh);
            if (reader.GetString(m.Name) != "Invoke")
                continue;
            try
            {
                var sig = m.DecodeSignature(SignatureDecoder.Instance, typeContext);
                ret = Clean(sig.ReturnType);
                parameters = Parameters(reader, m, sig);
            }
            catch { }
            break;
        }
        sb.AppendLine($"{pad}public unsafe delegate {ret} {Identifier(name)}{genParams}({parameters});");
    }

    /// <summary>
    /// A sibling interface, reconstructed with its method and property signatures
    /// (and nested types) so member access through it binds. Members are emitted
    /// without bodies or accessibility, as the interface form requires.
    /// </summary>
    static void EmitInterface(MetadataReader reader, TypeDefinitionHandle typeHandle,
        string name, string genParams, StringBuilder sb, string pad, int indent)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        sb.AppendLine($"{pad}public unsafe interface {Identifier(name)}{genParams}");
        sb.AppendLine($"{pad}{{");
        string inner = pad + "    ";

        var accessors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ph in typeDef.GetProperties())
        {
            var prop = reader.GetPropertyDefinition(ph);
            var pa = prop.GetAccessors();
            if (!pa.Getter.IsNil) accessors.Add(reader.GetString(reader.GetMethodDefinition(pa.Getter).Name));
            if (!pa.Setter.IsNil) accessors.Add(reader.GetString(reader.GetMethodDefinition(pa.Setter).Name));
            string pname = reader.GetString(prop.Name);
            if (pname.Contains('<') || pname.Contains('.'))
                continue; // indexer / explicit impl — skip
            try
            {
                var sig = prop.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForType(reader, typeDef));
                string body = (!pa.Getter.IsNil ? " get;" : "") + (!pa.Setter.IsNil ? " set;" : "");
                sb.AppendLine($"{inner}{Clean(sig.ReturnType)} {Identifier(pname)} {{{body} }}");
            }
            catch { }
        }

        foreach (var mh in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(mh);
            string mn = reader.GetString(method.Name);
            if (mn.Contains('<') || mn.Contains('.') || accessors.Contains(mn))
                continue; // accessor, static-abstract op, or explicit impl
            var context = GenericContext.ForMethod(reader, typeDef, method);
            MethodSignature<string> sig;
            try { sig = method.DecodeSignature(SignatureDecoder.Instance, context); }
            catch { continue; }
            string mGen = GenericParamList(reader, method.GetGenericParameters());
            sb.AppendLine($"{inner}{Clean(sig.ReturnType)} {Identifier(mn)}{mGen}({Parameters(reader, method, sig)});");
        }

        foreach (var nested in typeDef.GetNestedTypes())
        {
            if (reader.GetString(reader.GetTypeDefinition(nested).Name).Contains('<'))
                continue;
            EmitType(reader, nested, default, "", null, [], sb, indent + 1);
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
        IReadOnlyList<(string Field, string Value)> fieldInits, StringBuilder sb, string pad)
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
        string? initializer = fieldInits.FirstOrDefault(fi => fi.Field == name).Value;
        string suffix = initializer is not null && !isStatic ? $" = {initializer}" : "";
        bool isVolatile = false;
        try { isVolatile = field.DecodeSignature(VolatileFieldDetector.Instance, null); }
        catch { /* signature already decoded above; treat as non-volatile */ }
        sb.AppendLine($"{pad}public {(isStatic ? "static " : "")}{(isVolatile ? "volatile " : "")}{Clean(type)} {Identifier(name)}{suffix};");
    }

    static void EmitMethod(MetadataReader reader, TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle mh, string? realBody, string? realChain, StringBuilder sb, string pad)
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
            // (field inits, base call) matches; strip the IL name. A lifted
            // base(...)/this(...) chain prints as the signature initializer it is.
            string initializer = realChain is { Length: > 0 } ? $" : {realChain}" : "";
            sb.AppendLine($"{pad}public {Identifier(StripArity(reader.GetString(typeDef.Name)))}({parameters}){initializer} {{{body}}}");
            return;
        }
        if (name is ".cctor")
        {
            sb.AppendLine($"{pad}static {Identifier(StripArity(reader.GetString(typeDef.Name)))}() {{{body}}}");
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

    /// <summary>Drops the metadata generic-arity suffix (<c>Foo`1</c> → <c>Foo</c>).</summary>
    static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

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

    /// <summary>
    /// References for recompilation: the running runtime (TPA) plus every sibling
    /// assembly in the target's own directory (project deps, test framework, etc.),
    /// EXCLUDING the target assembly itself. We reconstruct the target's own types
    /// from metadata, so referencing the real DLL would duplicate them (ambiguous-
    /// reference errors); referencing its neighbours resolves cross-assembly types
    /// in the stubbed signatures.
    /// </summary>
    static ImmutableArray<MetadataReference> RuntimeReferences(string targetPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targetName = Path.GetFileNameWithoutExtension(targetPath);
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();

        void Add(string path)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return;
            string simple = Path.GetFileNameWithoutExtension(path);
            if (simple.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                return; // the target is reconstructed, not referenced
            if (!seen.Add(simple))
                return; // first definition wins (prefer TPA over a dir copy)
            try { builder.Add(MetadataReference.CreateFromFile(path)); }
            catch { }
        }

        foreach (var path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            Add(path);

        var dir = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (dir is not null && Directory.Exists(dir))
            foreach (var path in Directory.EnumerateFiles(dir, "*.dll"))
                Add(path);

        return builder.ToImmutable();
    }
}
