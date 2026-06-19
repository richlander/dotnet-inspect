using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

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
        Assert.DoesNotContain("unsafe", DecompileLegacy(nameof(LegacyFixtures.StackAllocDefault)));
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
}
