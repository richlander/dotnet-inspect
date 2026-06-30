using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ILInspector.Instructions;

namespace ILInspector.Instructions.Tests;

/// <summary>
/// Decode-fidelity invariants over real compiled IL (this test assembly's own method bodies):
/// a faithful decode exactly tiles the IL bytes and every branch target lands on an instruction
/// boundary. These are oracle-free checks — a desync (e.g. a mishandled prefix) breaks tiling at once.
/// </summary>
public class DecodeFidelityTests
{
    [Fact]
    public void Decoded_stream_tiles_il_and_branch_targets_align()
    {
        using var pe = new PEReader(File.OpenRead(typeof(DecodeFidelityTests).Assembly.Location));
        var reader = pe.GetMetadataReader();

        int methods = 0;
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            MethodBodyBlock body;
            try { body = pe.GetMethodBody(method.RelativeVirtualAddress); }
            catch { continue; }
            byte[] il = body.GetILBytes() ?? [];
            if (il.Length == 0)
                continue;

            var instructions = InstructionDecoder.Decode(il);
            methods++;
            string name = reader.GetString(method.Name);

            // Tiling: contiguous, starts at 0, ends exactly at il.Length, no gaps/overlaps.
            Assert.Equal(0, instructions[0].Offset);
            for (int i = 1; i < instructions.Length; i++)
                Assert.True(instructions[i - 1].NextOffset == instructions[i].Offset,
                    $"{name}: gap/overlap at IL_{instructions[i].Offset:X4}");
            Assert.Equal(il.Length, instructions[^1].NextOffset);

            // Branch-target alignment: every target is a real instruction offset.
            var offsets = instructions.Select(x => x.Offset).ToHashSet();
            foreach (var ins in instructions)
                foreach (int target in ins.BranchTargets)
                    Assert.True(offsets.Contains(target),
                        $"{name}: branch target IL_{target:X4} not on an instruction boundary");
        }

        Assert.True(methods > 50, $"expected to exercise many method bodies, saw {methods}");
    }

    [Fact]
    public void Decoded_stream_round_trips_to_original_il()
    {
        // Absolute fidelity: re-encode the IL from the decoder's semantic fields (opcode,
        // operand value, branch targets) and require it to reproduce the original bytes.
        // Branch displacements and typed operands are reconstructed, not copied — so a decode
        // that loses or mis-widths an operand, or mis-computes a target, fails here.
        using var pe = new PEReader(File.OpenRead(typeof(DecodeFidelityTests).Assembly.Location));
        var reader = pe.GetMetadataReader();

        int methods = 0;
        foreach (var handle in reader.MethodDefinitions)
        {
            var method = reader.GetMethodDefinition(handle);
            if (method.RelativeVirtualAddress == 0)
                continue;
            MethodBodyBlock body;
            try { body = pe.GetMethodBody(method.RelativeVirtualAddress); }
            catch { continue; }
            byte[] il = body.GetILBytes() ?? [];
            if (il.Length == 0)
                continue;

            var instructions = InstructionDecoder.Decode(il);
            byte[] reencoded = ReEncode(instructions, il, il.Length);
            methods++;
            Assert.True(reencoded.AsSpan().SequenceEqual(il),
                $"{reader.GetString(method.Name)}: round-trip mismatch");
        }

        Assert.True(methods > 50, $"expected to exercise many method bodies, saw {methods}");
    }

    static byte[] ReEncode(ImmutableArray<DecodedInstruction> instructions, byte[] original, int ilLength)
    {
        var buf = new byte[ilLength];
        foreach (var ins in instructions)
        {
            int p = ins.Offset;
            int op = (int)ins.OpCode;
            if (op > 0xFF)
            {
                buf[p++] = 0xFE;
                buf[p++] = (byte)(op & 0xFF);
            }
            else
            {
                buf[p++] = (byte)op;
            }

            if (ins.OpCode == ILOpCode.Switch)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(p), (uint)ins.BranchTargets.Length);
                p += 4;
                foreach (int target in ins.BranchTargets)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p), target - ins.NextOffset);
                    p += 4;
                }
            }
            else if (ins.Branches)
            {
                int rel = ins.BranchTargets[0] - ins.NextOffset;
                if (IsShortBranch(ins.OpCode))
                    buf[p++] = (byte)(sbyte)rel;
                else
                {
                    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p), rel);
                    p += 4;
                }
            }
            else
            {
                p = WriteOperand(buf, p, ins, original);
            }
        }
        return buf;
    }

    static int WriteOperand(byte[] buf, int p, DecodedInstruction ins, byte[] original)
    {
        switch (ins.Operand)
        {
            case OperandKind.ShortInlineVar or OperandKind.ShortInlineI:
                buf[p++] = (byte)ins.OperandValue;
                break;
            case OperandKind.InlineVar:
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(p), (ushort)ins.OperandValue);
                p += 2;
                break;
            case OperandKind.InlineI or OperandKind.ShortInlineR or OperandKind.InlineString
                or OperandKind.InlineMethod or OperandKind.InlineField or OperandKind.InlineType
                or OperandKind.InlineSig or OperandKind.InlineTok:
                BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p), (int)ins.OperandValue);
                p += 4;
                break;
            case OperandKind.InlineI8 or OperandKind.InlineR:
                BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(p), ins.OperandValue);
                p += 8;
                break;
            case OperandKind.None:
                // Uninterpreted operand bytes (e.g. the no. prefix flags byte) carry no structural
                // meaning the decoder needs; copy them verbatim so the round-trip stays exact.
                for (int i = ins.OperandOffset; i < ins.NextOffset; i++)
                    buf[p++] = original[i];
                break;
        }
        return p;
    }

    static bool IsShortBranch(ILOpCode opcode)
        => (opcode >= ILOpCode.Br_s && opcode <= ILOpCode.Blt_un_s) || opcode == ILOpCode.Leave_s;
}
