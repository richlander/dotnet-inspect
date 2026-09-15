using System;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Tests;

// #2990: a 64-bit-backed [Flags] enum OR/AND accumulation lowers into the enum's
// Int64 underlying space and spills long accumulator slots. When an enum operand
// sits on the RIGHT of an `or`/`and` (the IL accumulation order), the OR chain must
// stay enum-typed. The pre-fix behavior collapsed the chain to `long`, so bare
// flag arms rendered as `... | (long)32768` — CS0019 (operator '|' cannot be applied
// to 'FlagCaps64' and 'long').
public class FlagsEnumAccumulatorTests
{
    static string Render(string methodName, PrinterOptions? options = null)
    {
        using var source = MetadataSource.Open(typeof(FlagsEnumAccumulatorSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(FlagsEnumAccumulatorSamples).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function, options).Output!;
    }

    // #3009 sub-part 3: with WrapSplittableExpressions on, the long fully-raised OR
    // chain — here inside the `(int)` return cast — breaks one operand per line with
    // the operator LEADING each continuation line. Off by default (covered by the
    // sub-part 1/2 tests above, which render inline).
    [Fact]
    public void FlagsEnumAccumulator_WrapOption_BreaksOrChainWithLeadingOperator()
    {
        var output = Render(
            nameof(FlagsEnumAccumulatorSamples.Accumulate),
            new PrinterOptions { WrapSplittableExpressions = true });

        Assert.Contains(
            "return (int)(FlagCaps64.Protocol\n"
                + "    | (interactive ? (server & FlagCaps64.Interactive) : FlagCaps64.None)\n"
                + "    | server & FlagCaps64.LoadLocal\n"
                + "    | FlagCaps64.Secure\n"
                + "    | server & FlagCaps64.MultiStatements\n"
                + "    | FlagCaps64.MultiResults);",
            output);
    }

    // Default options keep the whole accumulation on one line: the wrapping is a
    // pure opt-in whitespace choice, token-identical to the inline form.
    [Fact]
    public void FlagsEnumAccumulator_DefaultOptions_StaysInline()
    {
        var inline = Render(nameof(FlagsEnumAccumulatorSamples.Accumulate));
        var wrapped = Render(
            nameof(FlagsEnumAccumulatorSamples.Accumulate),
            new PrinterOptions { WrapSplittableExpressions = true });

        Assert.DoesNotContain("\n    |", inline);
        Assert.Contains("\n    |", wrapped);
        Assert.Equal(Tokens(inline), Tokens(wrapped));
    }

    static string[] Tokens(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void FlagsEnumAccumulator_KeepsEnumTyping_NoBareLongOrEnum()
    {
        var output = Render(nameof(FlagsEnumAccumulatorSamples.Accumulate));

        // The constant flag arms must render as enum members, not bare long constants
        // OR'd against the enum. The #2990 defect produced `... | (long)32768` (CS0019).
        Assert.DoesNotContain("| (long)", output);
        Assert.DoesNotContain("(long)32768", output);

        // The OR chain surfaces the enum type: the flag members appear by name.
        Assert.Contains("FlagCaps64.Secure", output);
        Assert.Contains("FlagCaps64.MultiStatements", output);
        Assert.Contains("FlagCaps64.MultiResults", output);
    }

    // #3009 sub-part 1: the spilled accumulator slot that holds a bare integer flag
    // constant is only ever consumed as the enum in the OR chain, so it must testify —
    // and materialize/fold — as the enum, not the `long` IL storage width. The
    // pre-fix shape was `long S_0 = (long)512; ... (FlagCaps64)S_0 | ...`.
    [Fact]
    public void FlagsEnumAccumulator_FullyRaisesConstantSlot_NoLongSlotOrCast()
    {
        var output = Render(nameof(FlagsEnumAccumulatorSamples.Accumulate));

        // No long-typed accumulator slot declaration survives.
        Assert.DoesNotContain("long S_", output);
        // No per-use cast of the accumulator slot back to the enum.
        Assert.DoesNotContain("(FlagCaps64)S_", output);
        // The 512 constant folded to its enum member and inlined into the chain.
        Assert.DoesNotContain("(long)512", output);
        Assert.Contains("FlagCaps64.Protocol", output);
    }

    // #3009 sub-part 2: with the accumulator fully raised, the remaining ternary
    // slot (`FlagCaps64 S_1 = interactive ? ... : ...;`) is a side-effect-free,
    // non-throwing value, so ExpressionInliningPass inlines it into its single
    // use in the OR chain. No spilled slot local survives; the whole method
    // collapses to one fully-raised expression.
    [Fact]
    public void FlagsEnumAccumulator_InlinesTernarySlot_NoSlotLocalSurvives()
    {
        var output = Render(nameof(FlagsEnumAccumulatorSamples.Accumulate));

        // No spilled stack-slot local of any type survives the inline.
        Assert.DoesNotContain("S_1", output);
        Assert.DoesNotContain("FlagCaps64 S_", output);
        // The ternary now appears inline as an operand of the OR chain.
        Assert.Contains("interactive ? (server & FlagCaps64.Interactive) : FlagCaps64.None", output);
    }
}
