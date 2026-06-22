using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

/// <summary>
/// StructuringPass raises csc's infinite-loop lowering — an unconditional
/// backward branch to a loop head — into a <c>while (true)</c> loop whose exits
/// are the body's own break/return statements. Verified for both the
/// return-only form and the conditional-break form, with and without symbols.
/// </summary>
public class InfiniteLoopStructuringTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    static IrFunction RaisedWithoutSymbols(string methodName)
    {
        using var source = MetadataSource.OpenWithoutSymbols(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }

    static bool IsWhileTrue(WhileLoop loop) =>
        loop.Condition is Constant { Value: true };

    [Fact]
    public void ReturnOnlyInfiniteLoop_RaisesToWhileTrue()
    {
        var function = Raised(nameof(CfgSampleClass.WhileTrueWithReturns));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop), "the back-edge loop must raise to while (true)");
        // The two early returns survive as guards inside the loop body; no goto
        // back-edge remains anywhere in the structured tree.
        Assert.Equal(2, loop.Body.Descendants.OfType<Return>().Count());
        Assert.Empty(function.Descendants.OfType<Branch>());
    }

    [Fact]
    public void BreakInfiniteLoop_RaisesToWhileTrueWithBreak()
    {
        var function = Raised(nameof(CfgSampleClass.WhileTrueWithBreak));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop), "the back-edge loop must raise to while (true)");
        Assert.Single(loop.Body.Descendants.OfType<Break>());
        Assert.Empty(function.Descendants.OfType<Branch>());
    }

    [Fact]
    public void WhileTrueWithReturns_PrintsWhileTrueAndGuards()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.WhileTrueWithReturns))).Output;

        Assert.Contains("while (true)", output);
        Assert.Contains("return", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void WhileTrueWithBreak_PrintsBreak()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.WhileTrueWithBreak))).Output;

        Assert.Contains("while (true)", output);
        Assert.Contains("break;", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void InfiniteLoop_WithoutSymbols_StillRaisesToWhileTrue()
    {
        var function = RaisedWithoutSymbols(nameof(CfgSampleClass.WhileTrueWithReturns));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop));
        Assert.Empty(function.Descendants.OfType<Branch>());
    }

    [Fact]
    public void ForEverLoop_RaisesToWhileTrue_LikeWhileTrue()
    {
        // `for (;;)` and `while (true)` share the unconditional-back-branch lowering
        // — no IL anchor distinguishes them — so the for(;;) source recovers as
        // while (true) just like the while(true) source does.
        var function = Raised(nameof(CfgSampleClass.ForEverLoopWithReturn));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop), "the for(;;) back-edge loop must raise to while (true)");
        Assert.Empty(function.Descendants.OfType<Branch>());
        var output = CSharpPrinter.Print(function).Output;
        Assert.Contains("while (true)", output);
        Assert.DoesNotContain("goto", output);
    }

    [Fact]
    public void InfiniteLoopWithMidBodyContinue_DeclinesAndStaysFlat()
    {
        // A mid-body `continue` is a second back-edge to the loop head. The
        // infinite-loop shape used to decline it; cond-backward self-loop
        // recovery now lets the method fully structure without stray gotos.
        var function = Raised(nameof(CfgSampleClass.InfiniteLoopWithContinue));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop));
        Assert.Empty(function.Descendants.OfType<Branch>());
        Assert.DoesNotContain("goto", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void TryFinallyRetryLoop_RaisesToWhileTrueWithContinue()
    {
        var function = Raised(nameof(CfgSampleClass.TryFinallyRetryLoop));

        var loop = Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.True(IsWhileTrue(loop));
        Assert.Single(loop.Body.Descendants.OfType<TryFinally>());
        Assert.Single(loop.Body.Descendants.OfType<Continue>());
        Assert.Empty(function.Descendants.OfType<Leave>());

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("while (true)", output);
        Assert.Contains("continue;", output);
        Assert.Contains("finally", output);
        Assert.DoesNotContain("goto", output);
    }
}
