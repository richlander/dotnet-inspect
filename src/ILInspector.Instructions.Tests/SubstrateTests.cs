using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;

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
    public void Decodes_read_only_span()
    {
        ReadOnlySpan<byte> il = [0x17, 0x18, 0x58, 0x2A];

        var instructions = InstructionDecoder.Decode(il);

        Assert.Equal(
            [ILOpCode.Ldc_i4_1, ILOpCode.Ldc_i4_2, ILOpCode.Add, ILOpCode.Ret],
            instructions.Select(instruction => instruction.OpCode));
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

    [Fact]
    public void Visit_streams_method_tokens_and_encoded_lengths()
    {
        const int methodToken = 0x06000001;
        byte[] il = [0x28, 0x01, 0x00, 0x00, 0x06, 0x2A];
        var visited = new List<(ILOpCode, int, int)>();

        bool completed = Visit(
            il,
            (opcode, token, length) =>
            {
                visited.Add((opcode, token, length));
                return true;
            });

        Assert.True(completed);
        Assert.Equal(
            [
                (ILOpCode.Call, methodToken, 5),
                (ILOpCode.Ret, 0, 1),
            ],
            visited);
    }

    [Fact]
    public void Visit_can_stop_before_a_malformed_suffix()
    {
        byte[] il = [0x29, 0x00, 0x00, 0x00, 0x00, 0xFE];

        bool completed = Visit(
            il,
            (_, _, _) => false);

        Assert.False(completed);
    }

    [Fact]
    public void Visit_rejects_malformed_or_dangling_input()
    {
        byte[][] malformed =
        [
            [0x28, 0x00],
            [0x45, 0x01, 0x00, 0x00, 0x00],
            [0xFE],
            [0xFE, 0x14],
        ];

        foreach (byte[] il in malformed)
        {
            Assert.Throws<BadImageFormatException>(
                () => Visit(il, (_, _, _) => true));
        }
    }

    static bool Visit(
        byte[] il,
        Func<ILOpCode, int, int, bool> visitor)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString("Visit.dll"),
            metadata.GetOrAddGuid(Guid.NewGuid()),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString("Visit"),
            new Version(1, 0, 0, 0),
            default,
            default,
            default,
            AssemblyHashAlgorithm.None);
        metadata.AddTypeDefinition(
            TypeAttributes.NotPublic,
            default,
            metadata.GetOrAddString("<Module>"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        metadata.AddTypeDefinition(
            TypeAttributes.Public,
            metadata.GetOrAddString("N"),
            metadata.GetOrAddString("T"),
            default,
            MetadataTokens.FieldDefinitionHandle(1),
            MetadataTokens.MethodDefinitionHandle(1));
        var signature = new BlobBuilder();
        new BlobEncoder(signature)
            .MethodSignature(isInstanceMethod: false)
            .Parameters(
                0,
                returnType => returnType.Void(),
                parameters => { });
        var code = new BlobBuilder(il.Length);
        code.WriteBytes(il);
        var bodies = new BlobBuilder();
        int bodyOffset = new MethodBodyStreamEncoder(bodies)
            .AddMethodBody(
                new InstructionEncoder(code),
                maxStack: 1);
        metadata.AddMethodDefinition(
            MethodAttributes.Public | MethodAttributes.Static,
            MethodImplAttributes.IL,
            metadata.GetOrAddString("M"),
            metadata.GetOrAddBlob(signature),
            bodyOffset,
            MetadataTokens.ParameterHandle(1));
        var pe = new ManagedPEBuilder(
            PEHeaderBuilder.CreateLibraryHeader(),
            new MetadataRootBuilder(metadata),
            bodies,
            flags: CorFlags.ILOnly);
        var image = new BlobBuilder();
        pe.Serialize(image);
        using var reader =
            new PEReader(ImmutableArray.Create(image.ToArray()));
        MetadataReader metadataReader =
            reader.GetMetadataReader();
        MethodDefinition method =
            metadataReader.GetMethodDefinition(
                MetadataTokens.MethodDefinitionHandle(1));
        MethodBodyBlock body =
            reader.GetMethodBody(
                method.RelativeVirtualAddress);

        return InstructionDecoder.Visit(body, visitor);
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

    [Fact]
    public void Leave_exiting_single_try_finally_flows_through_finally_before_target()
    {
        var method = DecodeFixture(nameof(BlockGraphEhFixtures.SingleLeave));
        var graph = method.Blocks;

        Assert.True(graph.IsComplete);
        var leave = SingleLeaveInstruction(method);
        var region = Assert.Single(graph.Regions, region => region.Kind == HandlerKind.Finally);
        int target = Assert.Single(leave.BranchTargets);

        Assert.Equal([region.HandlerStart], SuccessorStarts(graph, graph.BlockIndexAt(leave.Offset)));
        Assert.Equal([target], SuccessorStarts(graph, EndfinallyBlock(graph, method, region)));
    }

    [Fact]
    public void Leave_exiting_nested_try_finally_chains_finally_handlers_innermost_to_outermost()
    {
        var method = DecodeFixture(nameof(BlockGraphEhFixtures.NestedLeave));
        var graph = method.Blocks;

        Assert.True(graph.IsComplete);
        var leave = SingleLeaveInstruction(method);
        var regions = graph.Regions
            .Where(region => region.Kind == HandlerKind.Finally)
            .OrderBy(region => region.TryEnd - region.TryStart)
            .ToArray();
        Assert.Equal(2, regions.Length);
        var inner = regions[0];
        var outer = regions[1];
        int target = Assert.Single(leave.BranchTargets);

        Assert.Equal([inner.HandlerStart], SuccessorStarts(graph, graph.BlockIndexAt(leave.Offset)));
        Assert.Equal([outer.HandlerStart], SuccessorStarts(graph, EndfinallyBlock(graph, method, inner)));
        Assert.Equal([target], SuccessorStarts(graph, EndfinallyBlock(graph, method, outer)));
    }

    static DecodedInstruction SingleLeaveInstruction(MethodInstructions method)
        => Assert.Single(method.Instructions, instruction => instruction.OpCode is ILOpCode.Leave or ILOpCode.Leave_s);

    static int EndfinallyBlock(BlockGraph graph, MethodInstructions method, ExceptionRegionModel region)
        => Assert.Single(graph.Blocks, block =>
            block.Start >= region.HandlerStart
            && block.Start < region.HandlerEnd
            && method.InstructionAt(block.Start)?.OpCode == ILOpCode.Endfinally).Index;

    static int[] SuccessorStarts(BlockGraph graph, int block)
        => [.. graph.Blocks[block].Edges.Successors.Select(successor => graph.Blocks[successor].Start).Order()];

    static MethodInstructions DecodeFixture(string methodName)
    {
        using var stream = File.OpenRead(typeof(BlockGraphEhFixtures).Assembly.Location);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();
        var metadataToken = typeof(BlockGraphEhFixtures).GetMethod(methodName)!.MetadataToken;
        var method = reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(metadataToken));
        return MethodInstructions.Decode(peReader.GetMethodBody(method.RelativeVirtualAddress));
    }
}

public static class BlockGraphEhFixtures
{
    static int s_sink;

    public static void SingleLeave()
    {
        try
        {
            goto Done;
        }
        finally
        {
            Sink(1);
        }

    Done:
        Sink(2);
    }

    public static void NestedLeave()
    {
        try
        {
            try
            {
                goto Done;
            }
            finally
            {
                Sink(1);
            }
        }
        finally
        {
            Sink(2);
        }

    Done:
        Sink(3);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Sink(int value) => s_sink += value;
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

    [Theory]
    [InlineData(0x65)] // neg
    [InlineData(0x66)] // not
    public void Value_changing_unary_operations_stamp_their_own_provenance(
        byte operation)
    {
        byte[] il = [0x17, operation, 0x2A];
        TypedStackResult stack = MethodInstructions
            .Decode(il, il.Length, [])
            .InterpretStack(methodReturnsValue: true);

        Assert.True(stack.IsComplete);
        StackValue returned = Assert.Single(stack.StackBeforeOffset(2));
        Assert.Equal(StackType.Int32, returned.Type);
        Assert.Equal(1, returned.ProducerOffset);
    }

    /// <summary>
    /// Gate for <see cref="TypedStackResult.BlockExit"/>: a merge erases provenance at the join,
    /// so the value each predecessor contributed is only recoverable from what that block left on
    /// the stack when it exited. An unvisited block keeps a default exit, which is how a consumer
    /// tells "never reached" from "reached and left nothing".
    /// </summary>
    [Fact]
    public void Retains_per_block_exit_stacks_including_unreached_blocks()
    {
        // ldc.i4.0 ; brtrue.s +3 ; ldc.i4.1 ; br.s +1 ; ldc.i4.2 ; ret
        // then an unreachable ldc.i4.3 ; ret past the return.
        byte[] il = [0x16, 0x2D, 0x03, 0x17, 0x2B, 0x01, 0x18, 0x2A, 0x19, 0x2A];
        var mi = MethodInstructions.Decode(il, il.Length, []);
        var ts = mi.InterpretStack(methodReturnsValue: true);

        Assert.True(ts.IsComplete);
        Assert.Equal(mi.Blocks.Blocks.Length, ts.BlockExit.Length);

        // The join at ret has no producer, but each predecessor's exit still names one.
        Assert.Equal(
            StackValue.NoProducer,
            Assert.Single(ts.StackBeforeOffset(7)).ProducerOffset);
        Assert.Equal(
            [3, 6],
            mi.Blocks.Blocks
                .Select((_, index) => index)
                .Where(index => mi.Blocks.Blocks[index].Edges.Successors
                    .Contains(mi.Blocks.BlockIndexAt(7)))
                .Select(index => Assert.Single(ts.BlockExitAt(index)).ProducerOffset)
                .Order());

        int unreached = mi.Blocks.BlockIndexAt(8);
        Assert.True(unreached >= 0);
        Assert.True(ts.BlockExitAt(unreached).IsDefault);
        Assert.True(ts.BlockExitAt(mi.Blocks.Blocks.Length).IsDefault);
    }

    [Fact]
    public void Reports_incomplete_when_a_call_signature_is_unresolved()
    {        // ldarg.0 ; call <token 06000001> ; ret  — default resolver cannot resolve the call.
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
