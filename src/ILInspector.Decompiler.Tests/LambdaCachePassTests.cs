using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class LambdaCachePassTests
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

    // The cache field name and the null guard are the two visible artifacts of an
    // uncollapsed dance; a clean collapse leaves neither.
    static void AssertCacheCollapsed(string output)
    {
        Assert.DoesNotContain("<>9__", output);
        Assert.DoesNotContain("is null", output);
    }

    [Fact]
    public void CachedDelegateArgument_CollapsesDespiteInterleavedReceiver()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateArgument));

        AssertCacheCollapsed(output);
        Assert.Contains("x => x > 0", output);
        Assert.Contains("Where", output);
    }

    [Fact]
    public void CachedDelegateChain_CollapsesBothGuards()
    {
        string output = PrintRaised(nameof(CfgSampleClass.CachedDelegateChain));

        AssertCacheCollapsed(output);
        Assert.Contains("x => x > 0", output);
        Assert.Contains("x => x * 2", output);
        Assert.Contains("Where", output);
        Assert.Contains("Select", output);
    }
}
