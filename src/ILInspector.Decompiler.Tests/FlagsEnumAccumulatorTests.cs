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
    static string Render(string methodName)
    {
        using var source = MetadataSource.Open(typeof(FlagsEnumAccumulatorSamples).Assembly.Location);
        var function = IrImporter.Import(source, typeof(FlagsEnumAccumulatorSamples).FullName!, methodName);
        Assert.NotNull(function);
        IrPasses.Run(function!);
        function!.CheckInvariant();
        return CSharpPrinter.Print(function).Output!;
    }

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
}
