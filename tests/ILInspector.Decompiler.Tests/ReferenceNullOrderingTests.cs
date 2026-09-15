using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// csc lowers a reference inequality (`o != null`) to `ldnull; cgt.un` — an
// unsigned ordering with a null operand. Rendered literally that is the CS0019
// `o > null`; the printer must recover the idiom as `o is not null`.
public class ReferenceNullOrderingTests
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
    public void ReferenceComparedToNullUnsigned_RendersIsNotNull()
    {
        var output = Render(nameof(CfgSampleClass.IsNotNullReference));

        Assert.Contains("is not null", output);
        Assert.DoesNotContain("> null", output);
    }

    [Fact]
    public void NegatedIsInstanceNullTest_RendersTypeTest()
    {
        var output = Render(nameof(CfgSampleClass.IsNotByteArrayMultiContent));

        Assert.Contains("content is not byte[]", output);
        Assert.DoesNotContain("<= null", output);
        Assert.DoesNotContain("as byte[]", output);
    }

    [Fact]
    public void UnsignedGreaterThanZero_StaysComparison_NotNullCheck()
    {
        // `x > 0` on a uint emits the same `cgt.un` opcode against a zero constant,
        // but the zero is an integer literal, not a reference null — the not-null
        // recovery keys on a null operand, so this must stay a `>` comparison.
        var output = Render(nameof(CfgSampleClass.UnsignedGreaterThanZero));

        Assert.Contains("x >", output);
        Assert.DoesNotContain("is not null", output);
    }

    [Fact]
    public void UnsignedComparisonBetweenValues_StaysComparison()
    {
        // A genuine unsigned ordering (`cgt.un` with no null/zero operand) must be
        // left untouched by the not-null recovery.
        var output = Render(nameof(CfgSampleClass.UnsignedGreaterThan));

        Assert.Contains("a > b", output);
        Assert.DoesNotContain("is not null", output);
    }
}
