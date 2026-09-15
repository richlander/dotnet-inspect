using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A `ldind.i4` (and friends) through a `ref`/pointer enum is typed by the opcode
// width (int), not the pointee enum — so the importer must register the pointee's
// enum shape and member names, or an int constant beside it stays bare and the
// bitwise idiom is CS0019 (`styles & 16`). The fix names the member: `styles & Gamma`.
public class RefEnumBitwiseTests
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
    public void RefEnumBitwise_NamesEnumMember()
    {
        var output = Render(nameof(CfgSampleClass.RefEnumMask));

        Assert.Contains("CfgStyles.Gamma", output);
        Assert.DoesNotContain("& 16", output);
    }
}
