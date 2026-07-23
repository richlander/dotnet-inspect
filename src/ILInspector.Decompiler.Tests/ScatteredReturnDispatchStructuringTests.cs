using System;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// Issue #2978: a nested type-pattern <c>switch</c> expression whose shared
/// <c>_ =&gt; Fail</c> default is reached by two conditional guards at different
/// nesting levels, with a self-contained sibling region (an inner
/// <c>string</c>-length switch) interleaved between them in block order.
/// <see cref="StructuringPass"/> must classify the two guards as a scattered
/// dispatch and duplicate the default return into each guard; otherwise the
/// failing <c>int</c> arm (<c>i &lt;= 0</c>) falls off the end of the non-void
/// method, leaving the <c>out</c> parameter unassigned (CS0177).
///
/// The discriminator that recognizes this shape (an interior block entered by a
/// jump from before the guard span) must not fire on a contiguous short-circuit
/// <c>&amp;&amp;</c> guard chain, which threads entirely within its own span and
/// must stay combined under one condition — the #640 fidelity canary. The two
/// canaries below (<see cref="ScatteredReturnDispatchSample.PlainAnd"/> and
/// <see cref="ScatteredReturnDispatchSample.ThrowTernaryChain"/>) pin that the
/// shared return is not unrolled and the condition stays a single <c>&amp;&amp;</c>.
/// </summary>
[Trait("Area", "Pass")]
public class ScatteredReturnDispatchStructuringTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(ScatteredReturnDispatchSample).Assembly.Location);
        var function = IrImporter.Import(source, typeof(ScatteredReturnDispatchSample).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    static string Print(string methodName) =>
        CSharpPrinter.Print(Raised(methodName)).Output!.ReplaceLineEndings("\n").TrimEnd();

    // ── Compiler-backed positive ───────────────────────────────────────────

    [Fact]
    public void ScatteredDefault_IsDuplicatedIntoBothGuards_SoEveryPathReturns()
    {
        string output = Print(nameof(ScatteredReturnDispatchSample.Dispatch));

        // The failing int arm (i <= 0) now returns the duplicated default instead
        // of falling off the end — the CS0177 fix.
        int guard = output.IndexOf("if (i <= 0)", StringComparison.Ordinal);
        Assert.True(guard >= 0, output);
        Assert.Contains("return Fail(out x);", output[guard..]);

        // Every path returns: the method body ends with an unconditional return.
        Assert.EndsWith("return Win(i, out x);", output);

        // The default is duplicated into each guard rather than dropped: no goto
        // label survives and the raised body has no fall-through terminator.
        Assert.DoesNotContain("goto", output);
    }

    // ── Compiler-backed negatives (the #640 canary) ────────────────────────

    [Fact]
    public void ContiguousShortCircuitChain_StaysCombined_NotUnrolled()
    {
        string output = Print(nameof(ScatteredReturnDispatchSample.PlainAnd));

        // Two contiguous guards on the shared `return 2` stay one condition.
        Assert.Contains("if (a > 0 && b > 0)", output);
        Assert.EndsWith("return 2;", output);
    }

    [Fact]
    public void ThrowTernaryInsideChain_StaysCombined_NotScattered()
    {
        string output = Print(nameof(ScatteredReturnDispatchSample.ThrowTernaryChain));

        // The throw sub-expression is a within-span arm of the same short-circuit
        // condition, so the chain is not treated as a scattered dispatch: the
        // shared `return false` is not duplicated into each guard.
        Assert.Contains("if (a)", output);
        Assert.Contains("throw new Exception();", output);
        Assert.EndsWith("return false;", output);
    }
}
