using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class IndexFromEndPassTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function.CheckInvariant();
        return function!;
    }

    [Fact]
    public void ArrayLengthMinusConstant_RaisesToIndexFromEnd()
    {
        var function = Raised(nameof(CfgSampleClass.LastElement));

        var index = Assert.Single(function.Descendants.OfType<IndexFromEnd>());
        Assert.Equal(1, Assert.IsType<Constant>(index.Offset).Value);
        Assert.Single(function.Descendants.OfType<LoadElement>());
    }

    [Fact]
    public void PrintRaised_RendersArrayIndexFromEnd()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.LastElement))).Output;

        Assert.NotNull(output);
        Assert.Equal("return a[^1];", output.ReplaceLineEndings("\n").Trim());
    }

    [Fact]
    public void PrintRaised_RendersStringIndexFromEnd()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.LastChar))).Output;

        Assert.NotNull(output);
        Assert.Equal("return s[^1];", output.ReplaceLineEndings("\n").Trim());
    }
}
