using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

public class ShiftCountRenderingTests
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
    public void UIntShiftCount_IsCastToInt()
    {
        // `1 << n` over a uint count is CS0019; C# requires an int count.
        Assert.Contains("1 << (int)n", Print(nameof(CfgSampleClass.ShiftByUInt)));
    }

    [Fact]
    public void EnumShiftCount_IsCastToInt()
    {
        Assert.Contains("1 << (int)d", Print(nameof(CfgSampleClass.ShiftByEnum)));
    }

    [Fact]
    public void IntShiftCount_StaysBare()
    {
        // A plain int count already binds; no redundant cast.
        var output = Print(nameof(CfgSampleClass.ShiftByInt));
        Assert.Contains("1 << n", output);
        Assert.DoesNotContain("(int)n", output);
    }
}
