using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class FixedArrayStatementTests
{
    static IrFunction Raised(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return function!;
    }

    [Fact]
    public void ArrayPin_RaisesToFixedStatement()
    {
        var function = Raised(nameof(CfgSampleClass.FixedWholeArray));

        // The null/empty guard diamond and the pin/unpin scaffolding collapse into a
        // single array-form fixed statement (source rendered as-is, not `&place`).
        var fixedStatement = Assert.Single(function.Descendants.OfType<Fixed>());
        Assert.False(fixedStatement.SourceIsAddress);
        Assert.Equal("Byte", fixedStatement.ElementType.Name);
        Assert.Empty(function.Descendants.OfType<IfStatement>());
    }

    [Fact]
    public void ArrayPin_RendersValidFixed()
    {
        var output = CSharpPrinter.Print(Raised(nameof(CfgSampleClass.FixedWholeArray))).Output;

        Assert.NotNull(output);
        Assert.Contains("fixed (byte* p = ", output);
        Assert.DoesNotContain("pinned", output);
    }
}
