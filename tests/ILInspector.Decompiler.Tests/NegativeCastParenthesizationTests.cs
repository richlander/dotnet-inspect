using ILInspector.Decompiler.Pipeline;
using System.Numerics;

namespace ILInspector.Decompiler.Tests;

// A cast whose operand begins with a unary `-`/`+` is parsed as binary
// subtraction/addition (CS0075) unless the cast target is a predefined keyword
// type the parser treats as cast-disambiguating. `nint`/`nuint` are contextual
// keywords (and named types are not), so `(nint)-1` misparses — the printer must
// parenthesize the operand: `(nint)(-1)`.
public class NegativeCastParenthesizationTests
{
    static string Render(string methodName)
        => Render(typeof(CfgSampleClass), methodName);

    static string Render(Type declaringType, string methodName)
    {
        using var source = MetadataSource.Open(declaringType.Assembly.Location);
        var function = IrImporter.Import(source, declaringType.FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

    [Fact]
    public void NegativeNativeIntCast_ParenthesizesOperand()
    {
        var output = Render(nameof(CfgSampleClass.NegativeNativeInt));

        Assert.Contains("(nint)(-1)", output);
        Assert.DoesNotContain("(nint)-1", output);
    }

    [Fact]
    public void NegativeUserDefinedConversion_ParenthesizesOperand()
    {
        var output = Render(typeof(NegativeConversionSpecimens), nameof(NegativeConversionSpecimens.NegativeBigInteger));

        Assert.Contains("(BigInteger)(-128)", output);
        Assert.DoesNotContain("(BigInteger)-128", output);
    }

    [Fact]
    public void PositiveUserDefinedConversion_StaysBare()
    {
        var output = Render(typeof(NegativeConversionSpecimens), nameof(NegativeConversionSpecimens.PositiveBigInteger));

        Assert.Contains("(BigInteger)128", output);
        Assert.DoesNotContain("(BigInteger)(128)", output);
    }
}

public class NegativeConversionSpecimens
{
    public static BigInteger NegativeBigInteger() => -128;
    public static BigInteger PositiveBigInteger() => 128;
}
