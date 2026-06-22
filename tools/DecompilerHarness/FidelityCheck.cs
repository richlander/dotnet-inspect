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
/// The semantic-fidelity check (validity is <see cref="ValidityCheck"/>;
/// completeness is the <c>--gaps</c> floor).
/// It closes the loop named in docs/decompiler.md: decompile → recompile →
/// compare IL. A decompiled body that compiles and reads plausibly but recompiles
/// to a different opcode stream changed the program — the worst failure class
/// (docs/decompiler-taste.md), invisible to the validity check.
///
/// Unlike <see cref="ValidityCheck"/>'s per-method <c>__Shell</c> — which cannot
/// see the declaring type's fields, so any <c>this.field</c> reference fails to
/// bind as noise — this recompiles each member inside a reconstructed shape of
/// its REAL declaring type: the type declaration, every field, every sibling and
/// nested member as a throwing stub, and the one target member's real decompiled
/// body. The C# analog of the IL round-trip suite's full-skeleton scaffold
/// (IlasmScaffold.BuildCompilationUnit). Fields in scope mean a dropped or
/// mis-bound field access surfaces as a true opcode diff, not a compile error.
/// </summary>
static class FidelityCheck
{
    // The render path for one source: the lowered view, or the shipped raised
    // view with the cross-method import seam bound (so lambda raising can reach
    // a synthesized body in the same module). The lowered view carries no seam
    // yet, so a lambda there stays a delegate creation.
    static Func<IrFunction, DecompilerResult> Renderer(MetadataSource source, bool lowered)
        => lowered
            ? CSharpPrinter.PrintLowered
            : function => CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples, bool lowered = false)
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

        using var metadata = CorpusMetadata.Create(assemblies);
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
                try { source = MetadataSource.Open(path, context: metadata); }
                catch { continue; }
                var references = RuntimeReferences(path);
                using (source)
                {
                    var render = Renderer(source, lowered);
                    foreach (var typeHandle in reader.TypeDefinitions)
                    {
                        if (total >= cap)
                            break;
                        RunType(reader, pe, source, typeHandle, references, parseOptions, compileOptions,
                            cap, maxExamples, render, ref total, ref full, ref exact, ref contextFail,
                            ref recompileFail, ref diffCount, diffExamples, recompileFailCodes);
                    }
                }
            }
        }

        Report(total, full, exact, contextFail, recompileFail, diffCount,
            recompileFailCodes, diffExamples);
        return 0;
    }

    /// <summary>The fidelity check outcome for one method.</summary>
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

    /// <summary>One method's fidelity check result, with both opcode streams for diagnostics.</summary>
    public sealed record CompileBackResult(
        string Type, string Method, int Overload, CompileBackStatus Status,
        string OriginalOpcodes, string RecompiledOpcodes, string? Detail);

    /// <summary>
    /// Runs the fidelity check loop over one assembly and returns a structured result
    /// per rendered method, without printing. This is the testable entry point the
    /// xunit gate uses to assert the green set stays opcode-exact; <see cref="Run"/>
    /// is the console-reporting entry point. Shares all of the skeleton-emission and
    /// opcode-comparison machinery so the two paths can never drift.
    /// </summary>
    public static IReadOnlyList<CompileBackResult> Evaluate(string assemblyPath)
        => Evaluate(assemblyPath, lowered: false);

    /// <summary>
    /// Runs the fidelity check roundtrip for a chosen view — the shipped raised
    /// view (<paramref name="lowered"/> false) or the lowered view (true), so
    /// each official C# view earns its own compiler→decompiler→compiler
    /// validation. The renderer is built here so the raised path can bind the
    /// cross-method import seam from the open source (lambda raising).
    /// </summary>
    public static IReadOnlyList<CompileBackResult> Evaluate(string assemblyPath, bool lowered)
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
        var render = Renderer(source, lowered);
        var references = RuntimeReferences(assemblyPath);

        foreach (var typeHandle in reader.TypeDefinitions)
            EvaluateType(reader, pe, source, typeHandle, references, parseOptions, compileOptions, render, results);

        return results;
    }

    public static IReadOnlyList<CompileBackResult> Evaluate(IReadOnlyList<string> assemblies, int cap, bool lowered)
    {
        if (cap <= 0)
            return [];

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable);

        var results = new List<CompileBackResult>();
        foreach (var assemblyPath in assemblies)
        {
            if (results.Count >= cap)
                break;
            PEReader pe;
            try { pe = new PEReader(File.OpenRead(assemblyPath)); }
            catch { continue; }
            using (pe)
            {
                if (!pe.HasMetadata)
                    continue;
                var reader = pe.GetMetadataReader();
                MetadataSource source;
                try { source = MetadataSource.Open(assemblyPath); }
                catch { continue; }
                using (source)
                {
                    var render = Renderer(source, lowered);
                    var references = RuntimeReferences(assemblyPath);
                    foreach (var typeHandle in reader.TypeDefinitions)
                    {
                        if (results.Count >= cap)
                            break;
                        EvaluateType(reader, pe, source, typeHandle, references, parseOptions, compileOptions, render, results);
                    }
                }
            }
        }
        return results.Count <= cap ? results : results.Take(cap).ToArray();
    }

    /// <summary>One method ready to compile back: its decompiled body and the original opcode stream to match.</summary>
    sealed record Entry(
        MethodDefinitionHandle Handle, string Name, int Overload, TargetBody Target,
        IReadOnlyList<(string Field, string Value)> FieldInits,
        string OrigText, IReadOnlyList<string> OrigOps, bool IsFull);

    /// <summary>
    /// Imports, renders, and disassembles every recompilable method of one type.
    /// Null when the type is not a class/struct we recompile. The render/IL work
    /// is independent of how the methods are later compiled (grouped or per-method).
    /// </summary>
    static (string FullType, List<Entry> Entries)? CollectType(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        Func<IrFunction, DecompilerResult> render)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        if (!typeDef.GetDeclaringType().IsNil)
            return null; // nested types are emitted by their enclosing type
        if (ShapeOf(reader, typeDef) is not (TypeKind.Class or TypeKind.Struct))
            return null;

        string ns = reader.GetString(typeDef.Namespace);
        string tn = reader.GetString(typeDef.Name);
        string fullType = ns.Length == 0 ? tn : $"{ns}.{tn}";
        if (fullType.Contains('<'))
            return null;

        var entries = new List<Entry>();
        var overloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var mh in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(mh);
            string name = reader.GetString(method.Name);
            string key = $"{fullType}::{name}";
            int overload = overloads.GetValueOrDefault(key);
            overloads[key] = overload + 1;
            if (method.RelativeVirtualAddress == 0 || name.Contains('<'))
                continue;

            var function = IrImporter.Import(source, fullType, name, overload);
            if (function is null)
                continue;
            string? body;
            string? chain;
            IReadOnlyList<(string Field, string Value)> fieldInits;
            try { var printed = render(function); body = printed.Output; chain = printed.ConstructorChain; fieldInits = printed.FieldInitializers; }
            catch { continue; }
            if (body is null)
                continue;
            var original = ILDisassembler.Disassemble(pe, reader, method);
            if (original is null)
                continue;
            var origOps = original.Select(i => CanonicalOpcode(i.OpCodeName)).ToList();
            entries.Add(new Entry(mh, name, overload, new TargetBody(body, chain, function.RequiresAsyncBodyModifier), fieldInits,
                string.Join(" ", origOps), origOps, function.Fidelity == DecompilationFidelity.Full));
        }
        return (fullType, entries);
    }

    static void EvaluateType(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        ImmutableArray<MetadataReference> references, CSharpParseOptions parseOptions,
        CSharpCompilationOptions compileOptions, Func<IrFunction, DecompilerResult> render, List<CompileBackResult> results)
    {
        if (CollectType(reader, pe, source, typeHandle, render) is not var (fullType, entries) || entries.Count == 0)
            return;
        results.AddRange(EvaluateGrouped(reader, references, parseOptions, compileOptions, fullType, typeHandle, entries));
    }

    /// <summary>
    /// Compiles a type's decompiled bodies together and compares each method's
    /// recompiled opcodes against its original — the speed win, since a sibling
    /// body never changes a method's emitted IL, so a clean type of N methods
    /// costs one compilation instead of N. A non-recompilable body poisons the
    /// whole compilation, so on failure each method falls back to its own
    /// single-method build — correct either way, but the grouping only pays off
    /// for a type whose every body recompiles (a curated fixture holder).
    /// </summary>
    static List<CompileBackResult> EvaluateGrouped(
        MetadataReader reader, ImmutableArray<MetadataReference> references,
        CSharpParseOptions parseOptions, CSharpCompilationOptions compileOptions,
        string fullType, TypeDefinitionHandle typeHandle, IReadOnlyList<Entry> entries)
    {
        var results = new List<CompileBackResult>();
        // CB_NOGROUP forces the per-method path — the A/B baseline for the speedup.
        bool grouped = entries.Count > 1 && Environment.GetEnvironmentVariable("CB_NOGROUP") is null;
        if (grouped && TryCompileGroup(reader, references, parseOptions, compileOptions, fullType, typeHandle, entries, results))
            return results;
        foreach (var e in entries)
            results.Add(CompileOne(reader, references, parseOptions, compileOptions, fullType, e));
        return results;
    }

    /// <summary>Builds and compiles one grouped unit; on success appends a classified result per method and returns true.</summary>
    static bool TryCompileGroup(
        MetadataReader reader, ImmutableArray<MetadataReference> references,
        CSharpParseOptions parseOptions, CSharpCompilationOptions compileOptions,
        string fullType, TypeDefinitionHandle typeHandle, IReadOnlyList<Entry> entries, List<CompileBackResult> results)
    {
        var targets = new Dictionary<MethodDefinitionHandle, TargetBody>();
        foreach (var e in entries)
            targets[e.Handle] = e.Target;
        // Field initializers are identical across a type's constructors (C# declares
        // them once at the field), so any ctor entry's lifted inits serve the group.
        var fieldInits = entries.FirstOrDefault(e => e.Name is ".ctor" or ".cctor")?.FieldInits ?? [];

        string unit;
        try { unit = BuildUnit(reader, targets, fieldInits, typeHandle); }
        catch { return false; }

        var tree = CSharpSyntaxTree.ParseText(unit, parseOptions);
        var comp = CSharpCompilation.Create("cb", [tree], references, compileOptions);
        using var ms = new MemoryStream();
        if (!comp.Emit(ms).Success)
            return false;

        ms.Position = 0;
        using var rpe = new PEReader(ms);
        var disassembled = new List<CompileBackResult>(entries.Count);
        foreach (var e in entries)
        {
            var rOps = FindAndDisassemble(rpe, fullType, e.Name, e.Overload)
                ?.Select(i => CanonicalOpcode(i.OpCodeName)).ToList();
            if (rOps is null)
                return false;   // a method that compiled but cannot be found — fall to isolation
            disassembled.Add(Classify(fullType, e, rOps));
        }
        results.AddRange(disassembled);
        return true;
    }

    /// <summary>The per-method fallback: build a single-target unit and classify it. Authoritative when the grouped build fails.</summary>
    static CompileBackResult CompileOne(
        MetadataReader reader, ImmutableArray<MetadataReference> references,
        CSharpParseOptions parseOptions, CSharpCompilationOptions compileOptions, string fullType, Entry e)
    {
        string unit;
        try { unit = BuildUnit(reader, e.Handle, e.Target.Body, e.Target.Chain, e.Target.RequiresAsync, e.FieldInits); }
        catch { return new(fullType, e.Name, e.Overload, CompileBackStatus.ContextFail, e.OrigText, "", "skeleton-emit"); }

        var tree = CSharpSyntaxTree.ParseText(unit, parseOptions);
        var comp = CSharpCompilation.Create("cb", [tree], references, compileOptions);
        using var ms = new MemoryStream();
        var emit = comp.Emit(ms);
        if (!emit.Success)
        {
            var err = emit.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            return new(fullType, e.Name, e.Overload, CompileBackStatus.RecompileFail, e.OrigText, "", err?.Id);
        }
        ms.Position = 0;
        using var rpe = new PEReader(ms);
        var rOps = FindAndDisassemble(rpe, fullType, e.Name, e.Overload)?.Select(i => CanonicalOpcode(i.OpCodeName)).ToList();
        return rOps is null
            ? new(fullType, e.Name, e.Overload, CompileBackStatus.ContextFail, e.OrigText, "", "method-not-found")
            : Classify(fullType, e, rOps);
    }

    static CompileBackResult Classify(string fullType, Entry e, IReadOnlyList<string> rOps) =>
        new(fullType, e.Name, e.Overload,
            e.OrigOps.SequenceEqual(rOps) ? CompileBackStatus.Exact
                : e.IsFull ? CompileBackStatus.OpcodeDiff : CompileBackStatus.NotFull,
            e.OrigText, string.Join(" ", rOps), null);

    static void RunType(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        ImmutableArray<MetadataReference> references, CSharpParseOptions parseOptions,
        CSharpCompilationOptions compileOptions, int cap, int maxExamples,
        Func<IrFunction, DecompilerResult> render,
        ref int total, ref int full, ref int exact, ref int contextFail,
        ref int recompileFail, ref int diffCount,
        List<string> diffExamples, SortedDictionary<string, int> recompileFailCodes)
    {
        if (CollectType(reader, pe, source, typeHandle, render) is not var (fullType, entries) || entries.Count == 0)
            return;
        if (Environment.GetEnvironmentVariable("CB_TYPE") is { } filter && !fullType.Contains(filter, StringComparison.Ordinal))
            return;

        var results = EvaluateGrouped(reader, references, parseOptions, compileOptions, fullType, typeHandle, entries);
        for (int i = 0; i < results.Count; i++)
        {
            if (total >= cap)
                return;
            total++;
            var e = entries[i];
            var r = results[i];
            if (e.IsFull)
                full++;
            switch (r.Status)
            {
                case CompileBackStatus.Exact:
                    exact++;
                    break;
                case CompileBackStatus.OpcodeDiff:
                    diffCount++;
                    if (diffExamples.Count < maxExamples)
                        diffExamples.Add($"{r.Type}::{r.Method}\n    orig : {r.OriginalOpcodes}\n    recmp: {r.RecompiledOpcodes}");
                    break;
                case CompileBackStatus.RecompileFail:
                    recompileFail++;
                    recompileFailCodes[r.Detail ?? "<unknown>"] = recompileFailCodes.GetValueOrDefault(r.Detail ?? "<unknown>") + 1;
                    break;
                case CompileBackStatus.ContextFail:
                    contextFail++;
                    break;
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
    /// <summary>The real decompiled body (and optional ctor chain) for one target method.</summary>
    public readonly record struct TargetBody(string Body, string? Chain, bool RequiresAsync);

    /// <summary>Single-method unit — the per-method fallback path when a grouped build fails.</summary>
    static string BuildUnit(MetadataReader reader, MethodDefinitionHandle target, string targetBody, string? targetChain,
        bool targetRequiresAsync,
        IReadOnlyList<(string Field, string Value)> targetFieldInits)
    {
        var targets = new Dictionary<MethodDefinitionHandle, TargetBody> { [target] = new(targetBody, targetChain, targetRequiresAsync) };
        var fieldInitType = reader.GetMethodDefinition(target).GetDeclaringType();
        return BuildUnit(reader, targets, targetFieldInits, fieldInitType);
    }

    /// <summary>
    /// Grouped unit — every <paramref name="targets"/> method carries its real
    /// decompiled body in one compilation, so a whole type's methods recompile
    /// together (a sibling method's body never affects another's emitted IL — only
    /// signatures and fields do — so grouping is opcode-equivalent to the
    /// per-method build, for far fewer compiler invocations). Field initializers
    /// apply to <paramref name="fieldInitType"/> (the type whose constructor lifted
    /// them); they only change constructor IL, so non-constructor targets are
    /// indifferent to them.
    /// </summary>
    static string BuildUnit(MetadataReader reader, IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets,
        IReadOnlyList<(string Field, string Value)> fieldInits, TypeDefinitionHandle fieldInitType)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#pragma warning disable");
        // The decompiled bodies spell common framework types by their short name
        // (e.g. `Span<int>`), matching the product view's assumed `using System;`;
        // the skeleton imports the same namespace so those names bind.
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Threading.Tasks;");
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
                EmitType(reader, typeHandle, targets, fieldInits, fieldInitType, sb, 1);
                sb.AppendLine("}");
            }
            else
            {
                EmitType(reader, typeHandle, targets, fieldInits, fieldInitType, sb, 0);
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
        IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets,
        IReadOnlyList<(string Field, string Value)> fieldInits, TypeDefinitionHandle fieldInitType,
        StringBuilder sb, int indent)
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

        // Byref-like stubs can legally contain Span<T> fields only when emitted
        // as ref structs; otherwise the whole compile-back unit becomes invalid.
        string keyword = kind == TypeKind.Struct
            ? (IsByRefLike(reader, typeDef) ? "ref struct" : "struct")
            : "class";
        string baseClause = BaseClause(reader, typeDef, kind);
        // An [InlineArray(N)] struct must carry the attribute for its span
        // conversions (e.g. `(Span<T>)place`) to bind; the bare reconstructed
        // struct otherwise has no such conversion and the body fails to recompile.
        if (kind == TypeKind.Struct && InlineArrayAttributeText(reader, typeDef) is { } inlineArrayAttr)
            sb.AppendLine($"{pad}{inlineArrayAttr}");
        string unsafeModifier = TypeHasAwaitTarget(reader, typeHandle, targets) ? "" : "unsafe ";
        sb.AppendLine($"{pad}public {unsafeModifier}{keyword} {Identifier(name)}{genParams}{baseClause}");
        sb.AppendLine($"{pad}{{");

        // Field initializers lifted from a target ctor apply to this type's
        // fields only when this is the type that lifted them.
        var thisFieldInits = typeHandle == fieldInitType ? fieldInits : [];

        foreach (var fh in typeDef.GetFields())
            EmitField(reader, fh, typeContext, thisFieldInits, sb, pad + "    ");

        foreach (var mh in typeDef.GetMethods())
        {
            var hasTarget = targets.TryGetValue(mh, out var target);
            EmitMethod(reader, typeHandle, mh,
                hasTarget ? target.Body : null,
                hasTarget ? target.Chain : null,
                hasTarget && target.RequiresAsync,
                sb, pad + "    ");
        }

        foreach (var nested in typeDef.GetNestedTypes())
        {
            if (reader.GetString(reader.GetTypeDefinition(nested).Name).Contains('<'))
                continue; // compiler-generated (display class, iterator) — not valid C#
            EmitType(reader, nested, targets, fieldInits, fieldInitType, sb, indent + 1);
        }

        static bool TypeHasAwaitTarget(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets)
        {
            foreach (var methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods())
                if (targets.TryGetValue(methodHandle, out var target)
                    && target.RequiresAsync)
                {
                    return true;
                }
            return false;
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
            EmitType(reader, nested, NoTargets, [], default, sb, indent + 1);
        }

        sb.AppendLine($"{pad}}}");
    }

    static readonly IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> NoTargets =
        new Dictionary<MethodDefinitionHandle, TargetBody>();

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
        string fieldType = Clean(type);
        string unsafeModifier = RequiresUnsafeSignature(fieldType) ? "unsafe " : "";
        sb.AppendLine($"{pad}public {unsafeModifier}{(isStatic ? "static " : "")}{(isVolatile ? "volatile " : "")}{fieldType} {Identifier(name)}{suffix};");
    }

    static void EmitMethod(MetadataReader reader, TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle mh, string? realBody, string? realChain, bool realRequiresAsync, StringBuilder sb, string pad)
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
            string ctorUnsafeModifier = RequiresUnsafeSignature(parameters) ? "unsafe " : "";
            sb.AppendLine($"{pad}public {ctorUnsafeModifier}{Identifier(StripArity(reader.GetString(typeDef.Name)))}({parameters}){initializer} {{{body}}}");
            return;
        }
        // A finalizer (void Finalize() override) recovered as a destructor: emit
        // ~T() so the recompiled IL re-emits the try/finally + base.Finalize()
        // scaffold the decompiler stripped, making the round trip opcode-exact.
        if (name is "Finalize" && !isStatic && sig.ParameterTypes.Length == 0 && Clean(sig.ReturnType) == "void")
        {
            sb.AppendLine($"{pad}~{Identifier(StripArity(reader.GetString(typeDef.Name)))}() {{{body}}}");
            return;
        }
        if (name is ".cctor")
        {
            sb.AppendLine($"{pad}static {Identifier(StripArity(reader.GetString(typeDef.Name)))}() {{{body}}}");
            return;
        }

        string genParams = GenericParamList(reader, method.GetGenericParameters());
        string returnType = Clean(sig.ReturnType);
        string asyncModifier = realBody is not null && realRequiresAsync && CanBeAsync(returnType)
            ? "async "
            : "";
        string unsafeModifier = asyncModifier.Length == 0 ? "unsafe " : "";
        sb.AppendLine($"{pad}public {unsafeModifier}{(isStatic ? "static " : "")}{asyncModifier}{returnType} {Identifier(name)}{genParams}({parameters}) {{{body}}}");
    }

    static bool CanBeAsync(string returnType)
        => returnType is "void" or "Task"
            || returnType.StartsWith("Task<", StringComparison.Ordinal)
            || returnType.EndsWith(".Task", StringComparison.Ordinal)
            || returnType.Contains(".Task<", StringComparison.Ordinal)
            || returnType is "ValueTask"
            || returnType.StartsWith("ValueTask<", StringComparison.Ordinal)
            || returnType.EndsWith(".ValueTask", StringComparison.Ordinal)
            || returnType.Contains(".ValueTask<", StringComparison.Ordinal);

    static bool RequiresUnsafeSignature(string typeText)
        => typeText.Contains('*', StringComparison.Ordinal);

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
    /// The <c>[InlineArray(N)]</c> attribute text for an inline-array struct, or
    /// null when the type does not carry it. The attribute has a single int32
    /// constructor argument (the buffer length); the blob is a positional-only
    /// custom attribute (prolog <c>0x0001</c>, the int32, no named arguments),
    /// read directly so the harness needs no attribute type provider.
    /// </summary>
    static string? InlineArrayAttributeText(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var ah in typeDef.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(ah);
            if (AttributeTypeName(reader, attribute) != "InlineArrayAttribute")
                continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.Length < 6 || blob.ReadUInt16() != 1)
                return null; // not the expected positional-int prolog
            return $"[System.Runtime.CompilerServices.InlineArray({blob.ReadInt32()})]";
        }
        return null;
    }

    static bool IsByRefLike(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var ah in typeDef.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(ah);
            if (AttributeTypeFullName(reader, attribute) == "System.Runtime.CompilerServices.IsByRefLikeAttribute")
                return true;
        }
        return false;
    }

    /// <summary>The unqualified name of a custom attribute's type (its constructor's declaring type).</summary>
    static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                return member.Parent.Kind switch
                {
                    HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)member.Parent).Name),
                    HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent).Name),
                    _ => "",
                };
            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                return reader.GetString(reader.GetTypeDefinition(method.GetDeclaringType()).Name);
            default:
                return "";
        }
    }

    static string AttributeTypeFullName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                return member.Parent.Kind switch
                {
                    HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)member.Parent)),
                    HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent)),
                    _ => "",
                };
            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                return FullName(reader, reader.GetTypeDefinition(method.GetDeclaringType()));
            default:
                return "";
        }
    }

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
