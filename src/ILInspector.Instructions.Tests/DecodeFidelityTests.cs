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
            byte[] reencoded = ReEncode(instructions, il.Length);
            methods++;
            Assert.True(reencoded.AsSpan().SequenceEqual(il),
                $"{reader.GetString(method.Name)}: round-trip mismatch");
        }

        Assert.True(methods > 50, $"expected to exercise many method bodies, saw {methods}");
    }

    static byte[] ReEncode(ImmutableArray<DecodedInstruction> instructions, int ilLength)
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

            // Re-encode purely from the semantic OperandKind — no special-casing on opcode.
            // This makes the round-trip genuinely validate the decoder's classification: a
            // mis- or unclassified operand cannot be masked by a verbatim byte copy.
            WriteOperand(buf, p, ins);
        }
        return buf;
    }

    static void WriteOperand(byte[] buf, int p, DecodedInstruction ins)
    {
        switch (ins.Operand)
        {
            case OperandKind.ShortInlineBrTarget:
                buf[p] = (byte)(sbyte)(ins.BranchTargets[0] - ins.NextOffset);
                break;
            case OperandKind.InlineBrTarget:
                BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p), ins.BranchTargets[0] - ins.NextOffset);
                break;
            case OperandKind.InlineSwitch:
                BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(p), (uint)ins.BranchTargets.Length);
                p += 4;
                foreach (int target in ins.BranchTargets)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p), target - ins.NextOffset);
                    p += 4;
                }
                break;
            case OperandKind.ShortInlineVar or OperandKind.ShortInlineI:
                buf[p] = (byte)ins.OperandValue;
                break;
            case OperandKind.InlineVar:
                BinaryPrimitives.WriteUInt16LittleEndian(buf.AsSpan(p), (ushort)ins.OperandValue);
                break;
            case OperandKind.InlineI or OperandKind.ShortInlineR or OperandKind.InlineString
                or OperandKind.InlineMethod or OperandKind.InlineField or OperandKind.InlineType
                or OperandKind.InlineSig or OperandKind.InlineTok:
                BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(p), (int)ins.OperandValue);
                break;
            case OperandKind.InlineI8 or OperandKind.InlineR:
                BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(p), ins.OperandValue);
                break;
            case OperandKind.None:
                // Every operand-bearing opcode is classified, so None must mean zero operand
                // bytes. Assert it, so an unclassified operand can never be silently masked.
                Assert.True(ins.OperandOffset == ins.NextOffset,
                    $"unclassified operand bytes at IL_{ins.Offset:X4} (opcode {ins.OpCode})");
                break;
        }
    }
}
