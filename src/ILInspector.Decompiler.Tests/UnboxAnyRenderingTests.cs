using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class UnboxAnyRenderingTests
{
    static string Print(string methodName)
    {
        using var source = MetadataSource.Open(typeof(CfgSampleClass).Assembly.Location);
        var function = IrImporter.Import(source, typeof(CfgSampleClass).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function!).Output!;
    }

    [Fact]
    public void UnboxOverBox_KeepsObjectIntermediary()
    {
        // `box T; unbox.any byte` is `(byte)(object)x`. Collapsing the box to a plain
        // `(byte)x` over a generic type parameter is CS0030 (no direct conversion).
        var output = Print(nameof(CfgSampleClass.GreaterAsByte));

        Assert.Contains("(byte)(object)", output);
        // The bare `(byte)left`/`(byte)right` form (no object cast) must not appear.
        Assert.DoesNotContain("(byte)(left)", output);
        Assert.DoesNotContain("(byte)(right)", output);
    }
}
