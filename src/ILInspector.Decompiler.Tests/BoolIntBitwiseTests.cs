using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// A bitwise `&`/`|`/`^` that mixes a bool operand (a comparison result or a
// bool-typed slot) with an integer operand combines two `i4` (0/1) values in IL,
// so `and`/`or`/`xor` is legal — but C# has no `bool & int` (CS0019).
// BinaryResult sinks the *result* to int; the printer must also materialize the
// bool operand to `(cond ? 1 : 0)`. This is the operand-side complement of the
// enum/integer and signed/unsigned operand coercions.
public class BoolIntBitwiseTests
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
    public void AndBoolIntMix_MaterializesBoolOperand()
    {
        var output = Render(nameof(CfgSampleClass.AndBoolIntMix));

        // The bool operand `a > 0` must materialize to `(a > 0 ? 1 : 0)` so the
        // `&` is `int & int`, not the CS0019 `bool & int`.
        Assert.Contains("(a > 0 ? 1 : 0) &", output);
        Assert.DoesNotContain("a > 0 &", output);
    }

    [Fact]
    public void OrBoolIntMix_MaterializesBoolOperand()
    {
        var output = Render(nameof(CfgSampleClass.OrBoolIntMix));

        // No bool operand may stand bare beside the integer `|`. The materialized
        // bool reads `(... ? 1 : 0)`; a CS0019 leak would print a bool slot/expr
        // directly against the `|`.
        Assert.Contains("? 1 : 0) |", output);
    }

    [Fact]
    public void AndTwoBools_StaysBareBoolOp()
    {
        var output = Render(nameof(CfgSampleClass.AndTwoBools));

        // Both operands are bool — a genuine logical/bitwise bool `&`. No integer
        // sibling, so no `? 1 : 0` materialization.
        Assert.Contains("a & b", output);
        Assert.DoesNotContain("? 1 : 0", output);
    }

    [Fact]
    public void OrBoolUintMix_CastsMaterializedBoolForUnsignedSibling()
    {
        var output = Render(nameof(CfgSampleClass.OrBoolUintMix));

        // The materialized bool is a signed `int`; a `uint` sibling would make the
        // bitwise op `int | uint` (CS0266, widens to long). Routing the rewrite
        // through the printer's mixed-sign reconciliation supplies the `(uint)`
        // reinterpret so the expression binds. The materialized ternary is present
        // and an unsigned cast surrounds the integer mix.
        Assert.Contains("? 1 : 0", output);
        Assert.Contains("(uint)", output);
    }

    [Fact]
    public void AndNestedConditionalBool_ParenthesizesConditionalCondition()
    {
        var output = Render(nameof(CfgSampleClass.AndNestedConditionalBool));

        // The bool operand is itself a conditional; materializing must parenthesize
        // it — `((x ? p : q) ? 1 : 0)` — or `?:` right-associativity reparses it as
        // `x ? p : (q ? 1 : 0)` (CS0173/CS0019).
        Assert.Contains("((x ? p : q) ? 1 : 0) & i", output);
    }

    [Fact]
    public void AndNestedBoolIntMix_MaterializesBothNestedMixes()
    {
        var output = Render(nameof(CfgSampleClass.AndNestedBoolIntMix));

        // Deepest-first processing must materialize BOTH the inner and outer
        // bool/int mixes. The inner `(a > 0 ? 1 : 0) & i` and the outer `... & j`
        // are both present, and no bare `bool & int` (CS0019) survives — a leaked
        // mix would leave a `) & i`/`) & j` with a bool-typed left and no `? 1 : 0`.
        Assert.Contains("(a > 0 ? 1 : 0) & i", output);
        Assert.Contains("? 1 : 0) & j", output);
    }
}
