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

        using var source = MetadataSource.Open(SampleType.Assembly.Location);
        var function = Assert.IsType<IrFunction>(IrImporter.Import(
            source,
            SampleType.FullName!,
            nameof(FinallyReturnTimingSample.Run)));

        var result = CSharpPrinter.PrintRaised(
            function,
            method => IrImporter.Import(source, method),
            typesProvablyDisjoint: source.AreProvablyDisjoint);
        string output = Assert.IsType<string>(result.Output)
            .ReplaceLineEndings("\n");

        Assert.Equal(DecompilationFidelity.Full, function.Fidelity);
        Assert.Equal(1, CountOccurrences(output, "return result;"));
        Assert.EndsWith("return result;\n", output);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public void NormalContinuationReturnCompilesBackExactly()
    {
        var result = Assert.Single(FidelityCheck.Evaluate(
            SampleType.Assembly.Location,
            type => type == SampleType.FullName,
            method => method.Method == nameof(FinallyReturnTimingSample.Run)));

        Assert.Equal(FidelityCheck.CompileBackStatus.Exact, result.Status);
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
