using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// csc lowers a pointer `null` comparison (`p == null`) to `ldc.i4.0; conv.u;
// ceq`, so the zero reaches the printer as a native-int constant (often through
// a Convert). Comparing a pointer to that integer is CS0019; the printer must
// recover the `p == null` form. C# forbids `is null` on pointer types (CS8521),
// so the equality operator is required, not the is-pattern.
public class PointerNullComparisonTests
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
    public void PointerComparedToZero_RendersNullComparison()
    {
        var output = Render(nameof(CfgSampleClass.PointerNullBranch));

        Assert.Contains("== null", output);
        Assert.DoesNotContain("(nuint)0", output);
        Assert.DoesNotContain("== 0", output);
        Assert.DoesNotContain("is null", output);
    }
}
