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
}
