using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

public class InstructionDecoderTests
{
    [Fact]
    public void Decodes_offsets_opcodes_and_lengths()
    {
        // ldc.i4.1; ldc.i4.2; add; ret
        byte[] il = [0x17, 0x18, 0x58, 0x2A];
        var instructions = InstructionDecoder.Decode(il);

        Assert.Equal(4, instructions.Length);
        Assert.Equal((0, ILOpCode.Ldc_i4_1, 1), (instructions[0].Offset, instructions[0].OpCode, instructions[0].NextOffset));
        Assert.Equal(ILOpCode.Add, instructions[2].OpCode);
        Assert.Equal(ILOpCode.Ret, instructions[3].OpCode);
        Assert.True(instructions[3].Exits);
        Assert.All(instructions, i => Assert.Equal(1, i.Length));
    }

    [Fact]
    public void Decodes_branch_targets_relative_to_next_offset()
    {
        // IL_0000 br.s IL_0003 ; IL_0002 nop ; IL_0003 ret
        byte[] il = [0x2B, 0x01, 0x00, 0x2A];
        var instructions = InstructionDecoder.Decode(il);

        Assert.Equal(ILOpCode.Br_s, instructions[0].OpCode);
        Assert.True(instructions[0].Branches);
        Assert.True(instructions[0].IsUnconditionalBranch);
        Assert.False(instructions[0].FallsThrough);
        Assert.Equal(3, Assert.Single(instructions[0].BranchTargets));
    }

    [Fact]
    public void Decodes_switch_targets()
    {
        // switch (2) { IL_x, IL_y } then ret. opcode 0x45, count=2, two i4 rels.
        // layout: 0:switch 1..4:count 5..8:rel0 9..12:rel1 13:ret
        byte[] il = new byte[14];
        il[0] = 0x45;
        BitConverter.GetBytes(2).CopyTo(il, 1);
        BitConverter.GetBytes(0).CopyTo(il, 5);   // rel0 -> base (13)
        BitConverter.GetBytes(0).CopyTo(il, 9);   // rel1 -> base (13)
        il[13] = 0x2A;
        var instructions = InstructionDecoder.Decode(il);

        Assert.Equal(ILOpCode.Switch, instructions[0].OpCode);
        Assert.True(instructions[0].FallsThrough);
        Assert.Equal([13, 13], instructions[0].BranchTargets);
    }

    [Fact]
    public void ShortInlineVar_index_is_unsigned()
    {
        // ldloc.s 200 (0x11 0xC8) ; ret  — index 200 must read as 200, not -56.
        byte[] il = [0x11, 0xC8, 0x2A];
        var instructions = InstructionDecoder.Decode(il);

        Assert.Equal(ILOpCode.Ldloc_s, instructions[0].OpCode);
        Assert.Equal(200, instructions[0].OperandValue);
    }

    [Fact]
    public void No_prefix_does_not_desync_decode()
    {
        // no. 0x01 (0xFE 0x19 0x01) ; ldarg.0 (0x02) ; ret (0x2A)
        // The no. prefix has a 1-byte operand; mishandling it would misread ldarg.0 as the operand.
        byte[] il = [0xFE, 0x19, 0x01, 0x02, 0x2A];
        var instructions = InstructionDecoder.Decode(il);

        Assert.Equal(3, instructions.Length);
        Assert.Equal((ILOpCode)0xFE19, instructions[0].OpCode);   // no. (not a named BCL ILOpCode member)
        Assert.Equal(3, instructions[0].NextOffset);
        Assert.Equal((3, ILOpCode.Ldarg_0), (instructions[1].Offset, instructions[1].OpCode));
        Assert.Equal((4, ILOpCode.Ret), (instructions[2].Offset, instructions[2].OpCode));
    }
}

public class BlockGraphTests
{
    [Fact]
    public void Builds_blocks_at_branch_leaders()
    {
        // IL_0000 ldc.i4.0 ; brtrue.s IL_0006 ; ldc.i4.1 ; br.s IL_0007 ; (IL_0006) ldc.i4.2 ; (IL_0007) ret
        byte[] il = [0x16, 0x2D, 0x03, 0x17, 0x2B, 0x01, 0x18, 0x2A];
        var instructions = InstructionDecoder.Decode(il);
        var graph = BlockGraph.Build(il.Length, instructions, []);

        Assert.True(graph.IsComplete);
        Assert.Equal([0, 3, 6, 7], graph.Blocks.Select(b => b.Start));
        // Block 0 branches to the fallthrough (block 1) and the brtrue target (block 2).
        Assert.Equal([1, 2], graph.Blocks[0].Edges.Successors);
        // Both block 1 (br.s) and block 2 (fallthrough) reach the ret block (block 3).
        Assert.Equal([3], graph.Blocks[1].Edges.Successors);
        Assert.Equal([3], graph.Blocks[2].Edges.Successors);
    }
}

public class StackTypeInterpreterTests
{
    [Fact]
    public void Tracks_types_and_provenance_through_a_binary_op()
    {
        // ldc.i4.1 ; ldc.i4.2 ; add ; ret  (returns int)
        byte[] il = [0x17, 0x18, 0x58, 0x2A];
        var mi = MethodInstructions.Decode(il, il.Length, []);
        var ts = mi.InterpretStack(methodReturnsValue: true);

        Assert.True(mi.IsComplete);
        Assert.True(ts.IsComplete);

        var beforeAdd = ts.StackBeforeOffset(2);
        Assert.Equal(
            [(StackType.Int32, 0), (StackType.Int32, 1)],
            beforeAdd.Select(v => (v.Type, v.ProducerOffset)));

        // After the add, ret sees one int32 produced by the add at offset 2.
        var beforeRet = ts.StackBeforeOffset(3);
        var top = Assert.Single(beforeRet);
        Assert.Equal((StackType.Int32, 2), (top.Type, top.ProducerOffset));
    }

    [Fact]
    public void Merges_disagreeing_producers_to_no_provenance_but_keeps_the_type()
    {
        // Two paths push an int32 from different offsets, then join at ret.
        byte[] il = [0x16, 0x2D, 0x03, 0x17, 0x2B, 0x01, 0x18, 0x2A];
        var ts = MethodInstructions.Decode(il, il.Length, []).InterpretStack(methodReturnsValue: true);

        Assert.True(ts.IsComplete);
        var beforeRet = ts.StackBeforeOffset(7);
        var merged = Assert.Single(beforeRet);
        Assert.Equal(StackType.Int32, merged.Type);                 // both paths agree on the type
        Assert.Equal(StackValue.NoProducer, merged.ProducerOffset); // but provenance is ambiguous
    }

    [Fact]
    public void Reports_incomplete_when_a_call_signature_is_unresolved()
    {
        // ldarg.0 ; call <token 06000001> ; ret  — default resolver cannot resolve the call.
        byte[] il = [0x02, 0x28, 0x01, 0x00, 0x00, 0x06, 0x2A];
        var ts = MethodInstructions.Decode(il, il.Length, []).InterpretStack(methodReturnsValue: false);

        Assert.False(ts.IsComplete);
        Assert.Contains("Unresolved", ts.IncompleteReason);
    }

    [Fact]
    public void Malformed_il_fails_closed_instead_of_throwing()
    {
        // call (0x28) with a truncated 4-byte token — decode would run off the end.
        byte[] il = [0x28, 0x01, 0x00];
        var mi = MethodInstructions.Decode(il, il.Length, []);

        Assert.False(mi.IsComplete);
        Assert.False(mi.Blocks.IsComplete);
        Assert.NotNull(mi.Blocks.IncompleteReason);
    }

    [Theory]
    [InlineData(new byte[] { 0x38 })]              // br <int32> with no destination (ILReader path)
    [InlineData(new byte[] { 0x2B })]              // br.s <int8> with no destination (ILReader path)
    [InlineData(new byte[] { 0xFE })]              // dangling two-byte opcode prefix (ILReader path)
    [InlineData(new byte[] { 0x45, 0x01 })]        // switch with a truncated count (ILReader path)
    [InlineData(new byte[] { 0x28, 0x01, 0x00 })]  // call <token> with a truncated operand (operand path)
    public void Decode_throws_BadImageFormat_on_truncated_il(byte[] il)
    {
        // The substrate exposes a single malformed-IL exception: BadImageFormatException. The
        // runtime-ported ILReader uses InvalidProgramException internally for truncated
        // opcode/branch/switch reads, so InstructionDecoder.Decode must normalize it at the
        // boundary — otherwise consumers (ReachingDefinitions, LibraryBodyIndex) leak the wrong
        // exception type past their recovery gate.
        Assert.Throws<BadImageFormatException>(() => InstructionDecoder.Decode(il));
    }
}
