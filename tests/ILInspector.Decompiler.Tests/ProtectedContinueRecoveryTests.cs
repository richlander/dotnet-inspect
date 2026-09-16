using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ProtectedContinueRecoveryTests
{
    [Fact]
    public void TryAndCatchContinues_RaiseWithOwningForLoop()
    {
        var function = Raised(nameof(CfgSampleClass.Issue2861_ForLoopTryAndCatchContinues));

        var loop = Assert.Single(function.Descendants.OfType<ForLoop>());
        var tryCatch = Assert.Single(loop.Body.Descendants.OfType<TryCatch>());
        Assert.Contains(tryCatch.TryBody.Descendants, node => node is Continue);
        Assert.Contains(tryCatch.Clauses, clause => clause.Body.Descendants.OfType<Continue>().Any());
        Assert.Equal(2, loop.Body.Descendants.OfType<Continue>().Count());
        Assert.Empty(function.Descendants.OfType<Leave>());
        Assert.Empty(function.Descendants.OfType<WhileLoop>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("for (int i = 0; i < 10; i++)", output);
        Assert.Equal(2, output.Split("continue;", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("goto", output);
        Assert.DoesNotContain("IL_", output);
    }

    [Fact]
    public void NestedLoopLeaveToOuterIncrement_PreservesWhileFallback()
    {
        var function = Raised(nameof(CfgSampleClass.Issue2861_NestedProtectedLeaveToOuterIncrement));

        Assert.Single(function.Descendants.OfType<WhileLoop>());
        Assert.Single(function.Descendants.OfType<ForLoop>());
        Assert.Equal(2, function.Descendants.OfType<Leave>().Count());
        Assert.Empty(function.Descendants.OfType<Continue>());
        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);

        var output = CSharpPrinter.Print(function).Output!;
        Assert.Contains("while (", output);
        Assert.Contains("for (int j = 0; j < 3; j++)", output);
        Assert.Contains("goto IL_", output);
    }

    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function;
    }
}
