using ILInspector.Decompiler.Pipeline;
using ILInspector.DecompilerHarness;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Pass")]
public class FinallyReturnTimingTests
{
    static readonly Type SampleType = typeof(FinallyReturnTimingSample);

    [Fact]
    public void NormalContinuationReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.Run(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.Run(
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.Run));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void NestedFinallyReturnStaysAfterExitedFinallys()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunNested(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunNested(
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunNested));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void ArgumentReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunArgument(
            result: 0,
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunArgument(
            result: 0,
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunArgument));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    public void AliasedLocalReturnStaysAfterFinally()
    {
        Assert.Equal(110, FinallyReturnTimingSample.RunAliasedLocal(
            loop: true,
            setValue: true,
            exit: true));
        Assert.Equal(100, FinallyReturnTimingSample.RunAliasedLocal(
            loop: true,
            setValue: false,
            exit: true));

        var (fidelity, output) = Render(nameof(FinallyReturnTimingSample.RunAliasedLocal));
        Assert.Equal(DecompilationFidelity.Full, fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void FinallyReturnTimingMethodsCompileBackExactly()
    {
        var results = FidelityCheck.Evaluate(
            SampleType.Assembly.Location,
            type => type == SampleType.FullName,
            method => method.Method is nameof(FinallyReturnTimingSample.Run)
                or nameof(FinallyReturnTimingSample.RunArgument)
                or nameof(FinallyReturnTimingSample.RunAliasedLocal));

        Assert.Equal(3, results.Count);
        Assert.All(results, result =>
            Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status));
    }

    static (DecompilationFidelity Fidelity, string Output) Render(string methodName)
    {
        using var source = MetadataSource.Open(SampleType.Assembly.Location);
        var function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            SampleType.FullName!,
            methodName));

        var result = CSharpPrinter.PrintRaised(
            function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        string output = Assert.IsType<string>(result.Output)
            .ReplaceLineEndings("\n");
        return (function.Fidelity, output);
    }

    static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int start = 0;
        while ((start = text.IndexOf(value, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += value.Length;
        }
        return count;
    }
}
