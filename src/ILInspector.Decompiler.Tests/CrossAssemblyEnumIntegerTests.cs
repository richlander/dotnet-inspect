using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A cross-assembly enum (System.DayOfWeek, System.AttributeTargets — CoreLib, not
// this test assembly) resolves to TypeShape.Unknown on decompile: EnsureTypeMaps
// only walks the target assembly's own type defs, so no referenced enum's shape is
// registered. A sibling int therefore never retypes, leaving `enum == int` /
// `enum & int` (CS0019). The printer recognizes the enum structurally and casts the
// integer operand to the enum type. The same-assembly enums (CfgPriority, CfgStyles)
// resolve to TypeShape.Enum and are unaffected — covered by RefEnumBitwiseTests.
public class CrossAssemblyEnumIntegerTests
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
    public void EnumComparedToInt_CastsIntegerToEnum()
    {
        var output = Render(nameof(CfgSampleClass.CrossAssemblyEnumEqualsInt));

        Assert.Contains("(DayOfWeek)", output);
        // The bare `day == code` (enum == int) would be CS0019.
        Assert.DoesNotContain("== code", output);
    }

    [Fact]
    public void EnumBitwiseWithInt_CastsIntegerToEnum()
    {
        var output = Render(nameof(CfgSampleClass.CrossAssemblyEnumBitwise));

        Assert.Contains("(AttributeTargets)", output);
        // The bare `t & 4` (enum & int) would be CS0019.
        Assert.DoesNotContain("& 4", output);
    }
}
