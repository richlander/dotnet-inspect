using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A cast whose operand begins with a unary `-`/`+` is parsed as binary
// subtraction/addition (CS0075) unless the cast target is a predefined keyword
// type the parser treats as cast-disambiguating. `nint`/`nuint` are contextual
// keywords (and named types are not), so `(nint)-1` misparses — the printer must
// parenthesize the operand: `(nint)(-1)`.
public class NegativeCastParenthesizationTests
{
    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
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
}
