using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A bitwise OR that mixes a bool flag (a branchless `cgt.un`) with an integer is
// an integer result, not a bool. Typing the `or` by its left operand read it as
// bool, so a wider-integer return wrapped it in the spurious `(...) ? 1 : 0`
// (CS0029). The bitwise result must take the integer operand's type.
public class BoolBitwiseOrTests
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
    public void BoolBitwiseOr_IsIntegerResult_NotBool()
    {
        var output = Render(nameof(CfgSampleClass.BoolBitwiseOrWidened));

        // The bitwise OR is an integer, so it must flow straight into the uint
        // return. The bug typed it as bool and wrapped the whole OR in a
        // `(<or>) ? 1 : 0` materialization (CS0029) — uniquely marked by a
        // closing paren before `? 1 : 0` (the legitimate `a ? 1 : 0` operand is
        // preceded by `(a `, never `) `).
        Assert.Contains("| c", output);
        Assert.DoesNotContain(") ? 1 : 0", output);
    }
}
