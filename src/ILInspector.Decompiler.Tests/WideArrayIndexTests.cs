using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

[Trait("Area", "Printer")]
public class WideArrayIndexTests
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

    static string Print(string methodName)
        => CSharpPrinter.Print(Raised(methodName)).Output!.ReplaceLineEndings("\n").Trim();

    [Fact]
    public void LongArrayIndexLoad_RendersBareIndex()
    {
        // The compiler-inserted conv.ovf.i (checked (nint) range conversion) is
        // implicit for a long index, so it must not be spelled explicitly.
        Assert.Equal("return a[i];", Print(nameof(CfgSampleClass.LongArrayIndex)));
    }

    [Fact]
    public void ULongArrayIndexLoad_RendersBareIndex()
    {
        // conv.ovf.i.un for a ulong index is likewise implicit.
        Assert.Equal("return a[i];", Print(nameof(CfgSampleClass.ULongArrayIndex)));
    }

    [Fact]
    public void LongArrayIndexStore_RendersBareIndex()
    {
        Assert.Equal("a[i] = v;", Print(nameof(CfgSampleClass.LongArrayIndexStore)));
    }

    [Fact]
    public void LongArrayIndexRef_RendersBareIndex()
    {
        Assert.Equal("return ref a[i];", Print(nameof(CfgSampleClass.LongArrayIndexRef)));
    }

    [Fact]
    public void LongArrayIndexExpression_RendersBareIndex()
    {
        // Only the outer index range conversion is stripped; the long operand
        // expression is spelled as-is (its own add opcode is unaffected).
        Assert.Equal("return a[i + j];", Print(nameof(CfgSampleClass.LongArrayIndexExpr)));
    }

    [Fact]
    public void IntArrayIndexLoad_IsUnchanged()
    {
        // An int index needs no range conversion, so there is nothing to strip.
        Assert.Equal("return a[0];", Print(nameof(CfgSampleClass.FirstElement)));
    }
}
