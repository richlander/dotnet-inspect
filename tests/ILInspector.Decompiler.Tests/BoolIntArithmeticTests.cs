using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A comparison consumed by integer arithmetic is a 0/1 i4 value in IL, but C#
// requires the value-context spelling `(cond ? 1 : 0)` rather than `int + bool`.
public class BoolIntArithmeticTests
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
    public void MaterializedComparisonFeedsAddAsInteger()
    {
        var output = Render(nameof(CfgSampleClass.BoolArithmeticChunkCount));

        Assert.Contains("? 1 : 0", output);
        Assert.DoesNotContain("+ (value.Length % 10240 > 0)", output);
        Assert.DoesNotContain("+ ((value.Length % 10240) > 0)", output);
    }

    [Fact]
    public void MaterializesBothComparisonOperands()
    {
        var output = Render(nameof(CfgSampleClass.BoolArithmeticTwoComparisons));

        Assert.Contains("(left > 0 ? 1 : 0) + (right > 0 ? 1 : 0)", output);
        Assert.DoesNotContain("left > 0 +", output);
        Assert.DoesNotContain("+ right > 0", output);
    }
}
