using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ExpressionInliningPassTests
{
    static string PrintRaised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);

        var result = CSharpPrinter.PrintRaised(function!, method => IrImporter.Import(source, method));
        Assert.True(result.Succeeded, string.Join("\n", result.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(result.Output);
        return result.Output!.ReplaceLineEndings("\n").Trim();
    }

    // The collapsed cache leaves the chain spilled across reused stack slots
    // (`S_0 = xs; S_1 = x => ...; S_0 = Where(S_0, S_1); ...`). Live-range
    // inlining folds those temps into the call arguments, leaving one statement.
    [Fact]
    public void CachedDelegateArgument_InlinesToSingleCall()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateArgument));

        Assert.Equal(1, output.Count(c => c == ';'));
        Assert.StartsWith("return ", output);
        Assert.Contains("Where", output);
        Assert.Contains("x => x > 0", output);
    }

    [Fact]
    public void CachedDelegateChain_InlinesToSingleNestedExpression()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateChain));

        Assert.Equal(1, output.Count(c => c == ';'));
        Assert.StartsWith("return ", output);
        // The first call's result feeds the second as its receiver argument.
        int where = output.IndexOf("Where", StringComparison.Ordinal);
        int select = output.IndexOf("Select", StringComparison.Ordinal);
        Assert.True(where >= 0 && select >= 0 && select < where,
            $"expected Select(Where(...), ...) nesting, got: {output}");
        Assert.Contains("x => x > 0", output);
        Assert.Contains("x => x * 2", output);
    }
}
