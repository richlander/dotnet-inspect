using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Regressions for adversarial-review findings on the substrate decoder (PR #1982):
/// ilLength is honored, dangling prefixes fail closed, the no. prefix operand is captured,
/// and branch/switch operands are classified.
/// </summary>
public class ReviewFixTests
{
    [Fact]
    public void Decode_honors_ilLength_and_ignores_trailing_bytes()
    {
        // ret (0x2A) followed by a stray byte that must not be decoded as a second instruction.
        byte[] il = [0x2A, 0x00];
        var mi = MethodInstructions.Decode(il, ilLength: 1, []);

        Assert.True(mi.IsComplete);
        Assert.Equal(ILOpCode.Ret, Assert.Single(mi.Instructions).OpCode);
        Assert.Null(mi.InstructionAt(1)); // the trailing byte is not an instruction boundary
    }

    [Fact]
    public void Declared_ilLength_past_buffer_is_incomplete()
    {
        byte[] il = [0x2A];
        var mi = MethodInstructions.Decode(il, ilLength: 8, []);
        Assert.False(mi.IsComplete);
    }

    [Fact]
    public void Dangling_tail_prefix_is_incomplete()
    {
        // tail. (FE 14) with no following instruction.
        byte[] il = [0xFE, 0x14];
        var mi = MethodInstructions.Decode(il, il.Length, []);
        Assert.False(mi.IsComplete);
    }

    [Fact]
    public void Dangling_constrained_prefix_is_incomplete()
    {
        // constrained. <token> (FE 16 + 4-byte type token) with no following callvirt.
        byte[] il = [0xFE, 0x16, 0x01, 0x00, 0x00, 0x1B];
        var mi = MethodInstructions.Decode(il, il.Length, []);
        Assert.False(mi.IsComplete);
    }

    [Fact]
    public void No_prefix_operand_value_is_captured()
    {
        // no. 0x04 (suppress nullcheck); ret  — the prefix flags byte must be preserved.
        byte[] il = [0xFE, 0x19, 0x04, 0x2A];
        var mi = MethodInstructions.Decode(il, il.Length, []);

        Assert.True(mi.IsComplete);
        var no = mi.Instructions[0];
        Assert.Equal(0xFE19, (int)no.OpCode);
        Assert.Equal(OperandKind.ShortInlineI, no.Operand);
        Assert.Equal(4, no.OperandValue);
    }

    [Fact]
    public void Branch_and_switch_operands_are_classified()
    {
        // br.s IL_0003 ; nop ; ret  → short branch target.
        Assert.Equal(OperandKind.ShortInlineBrTarget,
            InstructionDecoder.Decode([0x2B, 0x01, 0x00, 0x2A])[0].Operand);

        // br IL_0005 (long, 4-byte target) ; ret.
        Assert.Equal(OperandKind.InlineBrTarget,
            InstructionDecoder.Decode([0x38, 0x00, 0x00, 0x00, 0x00, 0x2A])[0].Operand);

        // switch (1 target = fallthrough) ; ret.
        byte[] sw = [0x45, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x2A];
        Assert.Equal(OperandKind.InlineSwitch, InstructionDecoder.Decode(sw)[0].Operand);
    }
}
