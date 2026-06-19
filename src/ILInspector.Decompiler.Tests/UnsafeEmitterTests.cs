using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

using LegacyFixtures = ILInspector.Decompiler.Fixtures.LegacyUnsafe.UnsafeFixtures;
using NewFixtures = ILInspector.Decompiler.Fixtures.NewUnsafe.UnsafeFixtures;

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

    static string DecompileNew(string method) =>
        Decompile(typeof(NewFixtures).Assembly.Location, typeof(NewFixtures).FullName!, method);

    static string DecompileLegacy(string method) =>
        Decompile(typeof(LegacyFixtures).Assembly.Location, typeof(LegacyFixtures).FullName!, method);

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
    public void LegacyModule_RequiresUnsafeCall_EmitsNoUnsafeBlock()
    {
        // A legacy module relies on the member `unsafe` modifier for its body
        // context, so the call to the unsafe member needs no block here.
        var output = DecompileLegacy(nameof(LegacyFixtures.CallRisky));

        Assert.DoesNotContain("unsafe", output);
    }
}
