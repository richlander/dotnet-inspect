using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class IdentityConvertPassTests
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

    static string Print(string methodName) => CSharpPrinter.Print(Raised(methodName)).Output!;

    [Fact]
    public void NarrowingBitwiseConvert_IsKept()
    {
        // `a | b` of two bytes is `int` in C#, so the trailing conv.u1 narrows a
        // genuinely wider value. The IR types the `or` as byte (ECMA "wider operand
        // wins"), which would look like an identity — but dropping it yields
        // `return a | b;`, which is CS0266. The convert must survive.
        var function = Raised(nameof(CfgSampleClass.OrTwoBytes));

        var convert = Assert.Single(function.Descendants.OfType<Pipeline.Convert>());
        Assert.Equal("Byte", convert.Target.Name);
        Assert.IsType<Binary>(convert.Operand);
    }

    [Fact]
    public void NarrowingBitwiseConvert_RendersCast()
    {
        var output = Print(nameof(CfgSampleClass.OrTwoBytes));

        Assert.Contains("return (byte)(a | b);", output);
    }

    [Fact]
    public void ArrayLengthIdentityConvert_IsStillDropped()
    {
        // The int -> int conv.i4 over an array length is a true no-op in C# too;
        // the pass must keep dropping it so `a.Length` renders without a cast.
        var function = Raised(nameof(CfgSampleClass.ArrayLen));

        Assert.Empty(function.Descendants.OfType<Pipeline.Convert>());
        Assert.Contains("return a.Length;", CSharpPrinter.Print(function).Output);
    }

    [Fact]
    public void FloatPrecisionConvert_IsKept()
    {
        // `(float)(a * b)` rounds the F-precision product to float32 before the add.
        // The operand `a * b` is already Single, so source and target IR types match
        // and the conv.r4 would look like an identity — but dropping it changes the
        // result. IdentityConvertPass must keep the float convert.
        var function = Raised(nameof(CfgSampleClass.MidRoundFloat));

        var convert = Assert.Single(function.Descendants.OfType<Pipeline.Convert>());
        Assert.Equal("Single", convert.Target.Name);
    }

    [Fact]
    public void FloatPrecisionConvert_RendersCast()
    {
        var output = Print(nameof(CfgSampleClass.MidRoundFloat));

        Assert.Contains("(float)(a * b)", output);
    }

    [Fact]
    public void DoublePrecisionConvert_IsKept()
    {
        // conv.r8 over an already-Double operand rounds to float64; not an identity.
        var function = Raised(nameof(CfgSampleClass.MidRoundDouble));

        var convert = Assert.Single(function.Descendants.OfType<Pipeline.Convert>());
        Assert.Equal("Double", convert.Target.Name);
    }

    [Fact]
    public void DoublePrecisionConvert_RendersCast()
    {
        var output = Print(nameof(CfgSampleClass.MidRoundDouble));

        Assert.Contains("(double)(a * b)", output);
    }
}
