using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// C# promotes every sub-int (byte/sbyte/short/ushort/char) binary result to
// int — `hi - 0xD800` over a char is `int`, not `char` — so storing it into a
// `uint` slot needs an explicit `(uint)` cast (CS0266 without it). The IR keeps
// the narrow char result type, so the printer must report the promoted `int`
// type and re-insert the cast; the cast is a same-width reinterpret that emits
// no conv, so the body stays opcode-exact (pinned in FidelityGateTests).
public class SubIntPromotionCastTests
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
    public void SubIntBinaryStoredToUInt_GetsExplicitUIntCast()
    {
        var output = Render(nameof(CfgSampleClass.SubIntPromotionToUInt));

        Assert.Contains("uint a = (uint)(hi - 55296);", output);
        Assert.Contains("uint b = (uint)(lo - 56320);", output);
    }
}
