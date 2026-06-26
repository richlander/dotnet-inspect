using System.Collections.Immutable;
using System.IO;
using System.Linq;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using LegacyFixtures = ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures;
using NewFixtures = ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures;
using ChainB = ILInspector.Decompiler.Fixtures.UnsafeChainB.LibraryB;
using ChainC = ILInspector.Decompiler.Fixtures.UnsafeChainC.Program;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// The unsafe-context emitter. The two fixture assemblies compile to identical
/// IL; the only difference the decompiler can observe is the new-rules module's
/// <c>MemorySafetyRulesAttribute</c>. The printer uses that signal to wrap
/// unsafe operations in explicit, minimally scoped <c>unsafe { }</c> blocks for
/// a new-rules module, and to emit none for a legacy module (whose member
/// <c>unsafe</c> modifier — rendered at the signature, not by this body printer
/// — still supplies the context).
/// </summary>
public class UnsafeEmitterTests
{
    static string Decompile(string assemblyPath, string typeFullName, string method)
    {
        var source = MetadataSource.Open(assemblyPath);
        var function = IrImporter.Import(source, typeFullName, method);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    /// <summary>Decompile in optimistic ("simulate") mode — force new-rules rendering.</summary>
    static string DecompileSimulate(string assemblyPath, string typeFullName, string method)
    {
        var source = MetadataSource.Open(assemblyPath);
        source.SimulateNewRules = true;
        var function = IrImporter.Import(source, typeFullName, method);
        Assert.NotNull(function);
        var result = CSharpPrinter.PrintRaised(function!);
        Assert.NotNull(result.Output);
        return result.Output!;
    }

    static string DecompileNew(string method) =>
        Decompile(typeof(NewFixtures).Assembly.Location, typeof(NewFixtures).FullName!, method);

    static string DecompileLegacy(string method) =>
        Decompile(typeof(LegacyFixtures).Assembly.Location, typeof(LegacyFixtures).FullName!, method);

    static string DecompileChainB(string method) =>
        Decompile(typeof(ChainB).Assembly.Location, typeof(ChainB).FullName!, method);

    static string DecompileLegacySimulate(string method) =>
        DecompileSimulate(typeof(LegacyFixtures).Assembly.Location, typeof(LegacyFixtures).FullName!, method);

    static (IrFunction Function, IReadOnlyList<Annotation> Annotations) ClassifyNew(string method)
    {
        var source = MetadataSource.Open(typeof(NewFixtures).Assembly.Location);
        var function = IrImporter.Import(source, typeof(NewFixtures).FullName!, method);
        Assert.NotNull(function);
        return (function!, AnnotationEngine.Default.ClassifyImported(function!));
    }

    /// <summary>The body of the first <c>unsafe { }</c> block, by brace matching.</summary>
    static string FirstUnsafeBlockBody(string output)
    {
        int keyword = output.IndexOf("unsafe", StringComparison.Ordinal);
        Assert.True(keyword >= 0, "no unsafe block in output:\n" + output);
        int open = output.IndexOf('{', keyword);
        Assert.True(open >= 0);
        int depth = 0;
        for (int i = open; i < output.Length; i++)
        {
            if (output[i] == '{') depth++;
            else if (output[i] == '}' && --depth == 0)
                return output[(open + 1)..i];
        }
        throw new Xunit.Sdk.XunitException("unbalanced unsafe block:\n" + output);
    }

    [Fact]
    public void NewRulesModule_PointerDeref_WrapsInUnsafeBlock()
    {
        var output = DecompileNew(nameof(NewFixtures.DerefPointer));

        Assert.Contains("unsafe", output);
        Assert.Contains("*", FirstUnsafeBlockBody(output));
    }

    [Fact]
    public void NewRulesModule_FunctionPointerInvoke_WrapsInUnsafeBlock()
    {
        var output = DecompileNew(nameof(NewFixtures.InvokeFunctionPointer));

        Assert.Contains("callback(x)", FirstUnsafeBlockBody(output));
    }

    [Fact]
    public void NewRulesModule_UnsafeCatchFilter_WrapsWholeTryCatch()
    {
        var int32 = TypeRef.CoreLib("System", "Int32");
        var voidType = TypeRef.CoreLib("System", "Void");
        var pointer = TypeRef.Pointer(int32);
        var tryBody = new BlockContainer();
        tryBody.Add(new Block(1));
        var catchBody = new BlockContainer();
        catchBody.Add(new Block(2));
        var filter = new Comparison(
            ComparisonKind.NotEqual,
            isUnsigned: false,
            new LoadIndirect(int32, new LoadArgument(0, "p", pointer)),
            new Constant(0, int32));
        var block = new Block(0);
        block.Add(new TryCatch(
            tryBody,
            [new CatchClause(TypeRef.CoreLib("System", "Exception"), catchBody, filter)]));
        var body = new BlockContainer();
        body.Add(block);
        var function = new IrFunction(
            "M",
            TypeRef.CoreLib("Synthetic", "Holder"),
            new MethodSignature(voidType, [new Parameter("p", pointer)], HasThis: false, GenericParameterCount: 0),
            [],
            body)
        {
            UsesUpdatedMemorySafetyRules = true,
        };

        var result = CSharpPrinter.Print(function);

        Assert.NotNull(result.Output);
        Assert.Contains("unsafe", result.Output);
        Assert.Contains("when", FirstUnsafeBlockBody(result.Output!));
    }

    [Fact]
    public void NewRulesModule_PointerElementAccessInLoop_WrapsMinimally()
    {
        // The pointer element access is one statement inside the loop body, so
        // the unsafe block must wrap only that statement — not the surrounding
        // loop control. A whole-loop wrap would swallow the increment.
        var output = DecompileNew(nameof(NewFixtures.SumPinned));
        var block = FirstUnsafeBlockBody(output);

        Assert.Contains("sum +=", block);
        Assert.DoesNotContain("i++", block);
        Assert.Contains("i++", output);
    }

    [Fact]
    public void LegacyModule_PointerDeref_EmitsNoUnsafeBlock()
    {
        // A legacy module relies on the member `unsafe` modifier for its body
        // context, so the body printer emits no block.
        var output = DecompileLegacy(nameof(LegacyFixtures.DerefPointer));

        Assert.DoesNotContain("unsafe", output);
    }

    [Fact]
    public void LegacyModule_PointerElementAccessInLoop_EmitsNoUnsafeBlock()
    {
        var output = DecompileLegacy(nameof(LegacyFixtures.SumPinned));

        Assert.DoesNotContain("unsafe", output);
    }

    [Fact]
    public void NewRulesModule_RequiresUnsafeCall_WrapsInUnsafeBlock()
    {
        // Risky() has no pointers but is declared `unsafe`, so the compiler
        // stamps it requires-unsafe. Every call site needs an unsafe context
        // even though no pointer crosses the boundary.
        var output = DecompileNew(nameof(NewFixtures.CallRisky));

        Assert.Contains("Risky()", FirstUnsafeBlockBody(output));
    }

    [Fact]
    public void NewRulesModule_CrossAssemblyRequiresUnsafeCall_WrapsInUnsafeBlock()
    {
        // B.M2 calls A.M1 — a pointerless requires-unsafe method in another
        // assembly. The RequiresUnsafeAttribute lives on A.M1's MethodDef, so it
        // is invisible in B's MemberRef and the signature carries no pointer; the
        // wrap is possible only by resolving A cross-assembly (MetadataContext).
        var output = DecompileChainB(nameof(ChainB.M2));

        Assert.Contains("M1()", FirstUnsafeBlockBody(output));
    }

    [Fact]
    public void LegacyModule_RequiresUnsafeCall_EmitsNoUnsafeBlock()
    {
        // A legacy module relies on the member `unsafe` modifier for its body
        // context, so the call to the unsafe member needs no block here.
        var output = DecompileLegacy(nameof(LegacyFixtures.CallRisky));

        Assert.DoesNotContain("unsafe", output);
    }

    [Fact]
    public void NewRulesModule_CompatPointerSignatureCall_WrapsInUnsafeBlock()
    {
        // NativeMemory.Free has a pointer in its signature, so under compat mode
        // it is requires-unsafe even though its attributes are cross-assembly.
        // The call must be wrapped; the pointer parameter itself is safe.
        var output = DecompileNew(nameof(NewFixtures.FreePointer));

        Assert.Contains("Free", FirstUnsafeBlockBody(output));
    }

    [Fact]
    public void LegacyModule_CompatPointerSignatureCall_EmitsNoUnsafeBlock()
    {
        var output = DecompileLegacy(nameof(LegacyFixtures.FreePointer));

        Assert.DoesNotContain("unsafe", output);
    }

    [Fact]
    public void NewRulesModule_StackAllocSpanSkipInit_WrapsAndHoistsDeclaration()
    {
        // stackalloc -> Span under [SkipLocalsInit] is unsafe. The unsafe op is
        // the initializer of a local used afterwards, so the declaration must be
        // hoisted out of the block (declared up front, assigned inside) to keep
        // the variable in scope.
        var output = DecompileNew(nameof(NewFixtures.StackAllocSkipInit));

        // Raised to the source-level `stackalloc int[n]`, not the lowered
        // `new Span<int>(stackalloc byte[...], n)` ctor shape (which never compiles).
        Assert.Contains("stackalloc int[", FirstUnsafeBlockBody(output));
        Assert.DoesNotContain("new Span", output);
        Assert.DoesNotContain("stackalloc byte[", output);
        // The declaration is hoisted above the unsafe block, the use survives.
        Assert.True(
            output.IndexOf("Span<int> s", StringComparison.Ordinal)
                < output.IndexOf("unsafe", StringComparison.Ordinal),
            "the span declaration must be hoisted above the unsafe block:\n" + output);
        Assert.Contains("s.Length", output);
        // Splitting the declaration from the stackalloc assignment loses the
        // inline `scoped` inference, so the hoisted declaration must spell
        // `scoped` to stay clean (otherwise CS9081). A stackalloc result is
        // always scoped, so this is mode-independent correctness, not a guess.
        Assert.Contains("scoped Span<int> s", output);
    }

    [Fact]
    public void NewRulesModule_StackAllocSpanDefault_EmitsNoUnsafeBlock()
    {
        // Without [SkipLocalsInit] the same stackalloc -> Span is safe under the
        // new rules; the pointer in the Span constructor's signature must not
        // trigger the compat heuristic here.
        var output = DecompileNew(nameof(NewFixtures.StackAllocDefault));

        Assert.DoesNotContain("unsafe", output);
        // Safe case keeps the inline `Span<int> s = stackalloc int[n]` form.
        Assert.Contains("stackalloc int[", output);
        Assert.DoesNotContain("new Span", output);
    }

    [Fact]
    public void LegacyModule_StackAllocSpan_EmitsNoUnsafeBlock()
    {
        // The stackalloc->Span raise is mode-independent correctness (the lowered
        // ctor shape never compiled), so legacy output raises too — just without
        // the unsafe wrapping the new rules require.
        var skipInit = DecompileLegacy(nameof(LegacyFixtures.StackAllocSkipInit));
        Assert.DoesNotContain("unsafe", skipInit);
        Assert.Contains("stackalloc int[", skipInit);
        // Legacy keeps the inline `Span<int> s = stackalloc int[n]` form, which
        // infers `scoped` on its own — no split declaration, so no `scoped`.
        Assert.DoesNotContain("scoped", skipInit);
        Assert.DoesNotContain("unsafe", DecompileLegacy(nameof(LegacyFixtures.StackAllocDefault)));
    }

    [Fact]
    public void NewRulesModule_StackAllocEventData_RendersRawStackallocInUnsafeBlock()
    {
        var output = DecompileNew(nameof(NewFixtures.StackAllocEventData));
        var block = FirstUnsafeBlockBody(output);

        Assert.Contains("byte* __stackalloc = stackalloc byte[", block);
        Assert.Contains("int* values = (int*)__stackalloc;", block);
        Assert.Contains("*values", block);
        Assert.Contains("*(values + 4)", block);
        Assert.DoesNotContain("Span", output);
    }

    [Fact]
    public void LegacyModule_StackAllocEventData_EmitsNoUnsafeBlock()
    {
        var output = DecompileLegacy(nameof(LegacyFixtures.StackAllocEventData));

        Assert.DoesNotContain("unsafe", output);
        Assert.Contains("stackalloc byte[", output);
        Assert.Contains("int* values = (int*)__stackalloc;", output);
        Assert.Contains("*values", output);
    }

    [Fact]
    public void StackAllocEventData_AnnotationSurvivesImportedClassificationAndRaising()
    {
        var (function, annotations) = ClassifyNew(nameof(NewFixtures.StackAllocEventData));

        var stackAlloc = Assert.Single(annotations, a => a.Descriptor.Id == "unsafe.stackalloc");
        Assert.Equal("byte*", stackAlloc.Detail);
        Assert.True(stackAlloc.SourceOffset >= 0);
        Assert.Single(function.Descendants.OfType<StackAllocate>());

        var output = CSharpPrinter.PrintRaised(function).Output!;

        Assert.Contains("stackalloc byte[", output);
        Assert.Single(function.Descendants.OfType<StackAllocate>());
    }

    // ---- Optimistic ("simulate") mode: render new-rules contexts for legacy input ----

    [Fact]
    public void OptimisticMode_LegacyPointerDeref_WrapsInUnsafeBlock()
    {
        // The pointer dereference leaves an IL trace (ldind), so simulate mode can
        // recover the context the new rules would require even though the legacy
        // module carries no MemorySafetyRulesAttribute. Matches conservative(New).
        var output = DecompileLegacySimulate(nameof(LegacyFixtures.DerefPointer));

        Assert.Contains("*", FirstUnsafeBlockBody(output));
    }

    [Fact]
    public void OptimisticMode_LegacyCompatPointerSignatureCall_WrapsInUnsafeBlock()
    {
        // NativeMemory.Free has a pointer in its signature — recoverable from the
        // MemberRef — so simulate mode wraps the call for legacy input too.
        var output = DecompileLegacySimulate(nameof(LegacyFixtures.FreePointer));

        Assert.Contains("Free", FirstUnsafeBlockBody(output));
    }

    [Fact]
    public void OptimisticMode_LegacyPointerlessRequiresUnsafe_NotRecoverable_EmitsNoBlock()
    {
        // A legacy same-assembly `unsafe` method with no pointers leaves NO trace:
        // legacy compilation stamps no RequiresUnsafeAttribute and the call carries
        // no pointer. There is nothing to recover, so even simulate mode emits no
        // block — the principled limit of optimistic rendering.
        var output = DecompileLegacySimulate(nameof(LegacyFixtures.CallRisky));

        Assert.DoesNotContain("unsafe", output);
    }

    [Fact]
    public void OptimisticMode_LegacyCrossAssemblyRequiresUnsafeCall_WrapsInUnsafeBlock()
    {
        // App C is NOT opted into the new rules, yet it calls A.M1 — a pointerless
        // requires-unsafe method whose attribute lives in opted-in assembly A.
        // That attribute is readable cross-assembly, so simulate mode recovers the
        // context and wraps the call; conservative mode (legacy) leaves it bare.
        var conservative = Decompile(typeof(ChainC).Assembly.Location, typeof(ChainC).FullName!, nameof(ChainC.CallChain));
        Assert.DoesNotContain("unsafe", conservative);

        var optimistic = DecompileSimulate(typeof(ChainC).Assembly.Location, typeof(ChainC).FullName!, nameof(ChainC.CallChain));
        Assert.Contains("M1()", FirstUnsafeBlockBody(optimistic));
    }

    // ---- Recompile rail: the new-rules output is valid, warning-free C# ----
    //
    // The fidelity/compile-back rails recompile *without* the
    // updated-memory-safety-rules feature, so they can never surface CS9081 on a
    // hoisted stackalloc declaration that dropped `scoped`. These tests close that
    // gap: they recompile the actual decompiled new-rules output and fail if it
    // warns. CS9081 ("a stackalloc result may be exposed outside the method") is a
    // ref-safety diagnostic independent of the new-rules feature, so the pinned
    // Roslyn package surfaces it today; enabling the feature flag below is a no-op
    // now but turns this into a full new-rules semantic check once the package
    // advances to a compiler that implements the rules.

    [Fact]
    public void NewRulesModule_StackAllocSkipInit_RecompilesScopedWithoutWarning()
    {
        var body = DecompileNew(nameof(NewFixtures.StackAllocSkipInit));
        // The hoisted span needs `scoped`; without it the recompile warns CS9081.
        Assert.Contains("scoped Span<int> s", body);

        var diagnostics = Recompile("[SkipLocalsInit] static int M(int n)", body);
        AssertNoWarningsOrErrors(diagnostics, body);
    }

    [Fact]
    public void OptimisticMode_LegacyStackAllocSkipInit_RecompilesScopedWithoutWarning()
    {
        // Simulate forces the same hoist on legacy input, so the scoped guard must
        // hold there too.
        var body = DecompileLegacySimulate(nameof(LegacyFixtures.StackAllocSkipInit));
        Assert.Contains("scoped Span<int> s", body);

        var diagnostics = Recompile("[SkipLocalsInit] static int M(int n)", body);
        AssertNoWarningsOrErrors(diagnostics, body);
    }

    [Fact]
    public void NewRulesModule_StackAllocEventData_RecompilesWithoutWarning()
    {
        var body = DecompileNew(nameof(NewFixtures.StackAllocEventData));

        var diagnostics = Recompile("static int M(int eventId)", body);
        AssertNoWarningsOrErrors(diagnostics, body);
    }

    /// <summary>
    /// Recompile a decompiled method body inside a minimal non-<c>unsafe</c>
    /// method shell (so the output's explicit <c>unsafe { }</c> blocks are
    /// actually required, not masked by an outer modifier) with no warning
    /// suppression, so a CS9081 (or any other) warning is observable.
    /// </summary>
    static ImmutableArray<Diagnostic> Recompile(string methodHeader, string body)
    {
        string source = $$"""
            using System;
            using System.Runtime.CompilerServices;
            static class __Gate
            {
                {{methodHeader}}
                {
            {{body}}
                }
            }
            """;
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview)
            .WithFeatures([new KeyValuePair<string, string>("updated-memory-safety-rules", "true")]);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions);
        var compilation = CSharpCompilation.Create(
            "__gate",
            [tree],
            RuntimeReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true));
        return compilation.GetDiagnostics();
    }

    static void AssertNoWarningsOrErrors(ImmutableArray<Diagnostic> diagnostics, string body)
    {
        var relevant = diagnostics
            .Where(d => d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => $"{d.Id}: {d.GetMessage()}")
            .ToList();
        Assert.True(
            relevant.Count == 0,
            "decompiled new-rules output must recompile clean, got:\n  "
                + string.Join("\n  ", relevant) + "\n--- body ---\n" + body);
    }

    static ImmutableArray<MetadataReference> RuntimeReferences()
    {
        var trustedPlatformAssemblies =
            (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        var references = ImmutableArray.CreateBuilder<MetadataReference>();
        foreach (var path in trustedPlatformAssemblies)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;
            try { references.Add(MetadataReference.CreateFromFile(path)); }
            catch { /* skip an unreadable TPA entry */ }
        }
        return references.ToImmutable();
    }
}
