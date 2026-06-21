using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A bitwise `&`/`|`/`^` between a signed and an unsigned integer of the same
// stack width (e.g. `ulong | long`) is CS0019 — neither operand converts to the
// other — but the bit result is identical under either interpretation, so the
// printer reinterprets the signed operand as unsigned (`mask | (ulong)flag`),
// which emits no opcode.
public class MixedSignBitwiseTests
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
    public void ULongOrLong_ReinterpretsSignedOperandAsUnsigned()
    {
        var output = Render(nameof(CfgSampleClass.OrLongIntoULong));

        Assert.Contains("mask | (ulong)flag", output);
    }
}
