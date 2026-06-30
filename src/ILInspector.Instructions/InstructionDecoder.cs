using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// Decodes a raw IL byte stream into a typed <see cref="DecodedInstruction"/> sequence.
/// Mechanics (opcode read, operand sizing, branch destinations) are driven by the
/// runtime-ported <see cref="ILReader"/> + opcode-size table, so prefixes (<c>no.</c>,
/// <c>unaligned.</c>), two-byte opcodes, and short operand widths follow the same rules the
/// JIT/ILVerify use. This is the one decode the typed-stack substrate owns; it is intended to
/// replace the hand-rolled decoders in <c>ReachingDefinitions</c> and the decompiler importer.
/// Malformed IL throws <see cref="BadImageFormatException"/>; the <see cref="MethodInstructions"/>
/// façade converts that to a fail-closed incomplete result.
/// </summary>
public static class InstructionDecoder
{
    public static ImmutableArray<DecodedInstruction> Decode(byte[] il)
    {
        ArgumentNullException.ThrowIfNull(il);
        var builder = ImmutableArray.CreateBuilder<DecodedInstruction>();
        var reader = new ILReader(il);

        while (reader.HasNext)
        {
            int offset = reader.Offset;
            var opcode = reader.ReadILOpcode();
            if (!opcode.IsValid())
                throw new BadImageFormatException($"Invalid opcode 0x{(int)opcode:X} at IL_{offset:X4}");

            int operandOffset = reader.Offset;
            var kind = Classify(opcode);

            long operandValue = 0;
            var targets = ImmutableArray<int>.Empty;
            bool branches = false;
            bool unconditional = false;
            bool exits = false;
            bool leaves = false;

            if (opcode == ILOpCode.Switch)
            {
                operandValue = ReadSwitchTargets(ref reader, offset, out targets);
                branches = true;
            }
            else if (opcode.IsBranch())
            {
                targets = [reader.ReadBranchDestination(opcode)];
                branches = true;
                unconditional = opcode.IsUnconditionalBranch();
                leaves = opcode is ILOpCode.Leave or ILOpCode.Leave_s;
            }
            else
            {
                operandValue = ReadOperandValue(il, operandOffset, kind, offset);
                reader.Skip(opcode);
                if (reader.Offset > il.Length)
                    throw new BadImageFormatException($"Truncated operand at IL_{offset:X4}");
                exits = opcode is ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Jmp
                    or ILOpCode.Endfinally or ILOpCode.Endfilter;
            }

            int next = reader.Offset;
            bool fallsThrough = !(exits || unconditional);

            builder.Add(new DecodedInstruction(
                offset, opcode, operandOffset, next, kind, operandValue,
                targets, branches, unconditional, exits, fallsThrough, leaves));
        }

        return builder.ToImmutable();
    }

    static long ReadSwitchTargets(ref ILReader reader, int offset, out ImmutableArray<int> targets)
    {
        uint count = reader.ReadILUInt32();
        long tableEnd = (long)reader.Offset + (long)count * 4;
        if (count > int.MaxValue / 4 || tableEnd > reader.Size)
            throw new BadImageFormatException($"Malformed switch at IL_{offset:X4}");
        int baseOffset = (int)tableEnd;
        var builder = ImmutableArray.CreateBuilder<int>((int)count);
        for (uint i = 0; i < count; i++)
            builder.Add(baseOffset + (int)reader.ReadILUInt32());
        targets = builder.MoveToImmutable();
        return count;
    }

    /// <summary>
    /// Reads the value of a non-branch, non-switch operand. Short variable/argument indices are
    /// unsigned (ECMA-335); only <see cref="OperandKind.ShortInlineI"/> is a signed int8.
    /// </summary>
    static long ReadOperandValue(byte[] il, int operandOffset, OperandKind kind, int offset)
    {
        switch (kind)
        {
            case OperandKind.None:
                return 0;
            case OperandKind.ShortInlineVar:
                return ReadU8(il, operandOffset, offset);
            case OperandKind.ShortInlineI:
                return (sbyte)ReadU8(il, operandOffset, offset);
            case OperandKind.InlineVar:
                return BinaryPrimitives.ReadUInt16LittleEndian(Slice(il, operandOffset, 2, offset));
            case OperandKind.InlineI:
            case OperandKind.ShortInlineR:
            case OperandKind.InlineString:
            case OperandKind.InlineMethod:
            case OperandKind.InlineField:
            case OperandKind.InlineType:
            case OperandKind.InlineSig:
            case OperandKind.InlineTok:
                return BinaryPrimitives.ReadInt32LittleEndian(Slice(il, operandOffset, 4, offset));
            case OperandKind.InlineI8:
            case OperandKind.InlineR:
                return BinaryPrimitives.ReadInt64LittleEndian(Slice(il, operandOffset, 8, offset));
            default:
                return 0;
        }
    }

    static byte ReadU8(byte[] il, int operandOffset, int offset)
    {
        if ((uint)operandOffset >= (uint)il.Length)
            throw new BadImageFormatException($"Truncated operand at IL_{offset:X4}");
        return il[operandOffset];
    }

    static ReadOnlySpan<byte> Slice(byte[] il, int position, int size, int offset)
    {
        if (position < 0 || position + size > il.Length)
            throw new BadImageFormatException($"Truncated operand at IL_{offset:X4}");
        return il.AsSpan(position, size);
    }

    static OperandKind Classify(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s
            or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
            => OperandKind.ShortInlineVar,
        ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg
            or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc
            => OperandKind.InlineVar,

        ILOpCode.Ldc_i4_s or ILOpCode.Unaligned => OperandKind.ShortInlineI,
        ILOpCode.Ldc_i4 => OperandKind.InlineI,
        ILOpCode.Ldc_i8 => OperandKind.InlineI8,
        ILOpCode.Ldc_r4 => OperandKind.ShortInlineR,
        ILOpCode.Ldc_r8 => OperandKind.InlineR,
        ILOpCode.Ldstr => OperandKind.InlineString,

        ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj or ILOpCode.Jmp
            or ILOpCode.Ldftn or ILOpCode.Ldvirtftn
            => OperandKind.InlineMethod,
        ILOpCode.Calli => OperandKind.InlineSig,
        ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Stfld
            or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Stsfld
            => OperandKind.InlineField,
        ILOpCode.Cpobj or ILOpCode.Ldobj or ILOpCode.Castclass or ILOpCode.Isinst
            or ILOpCode.Unbox or ILOpCode.Unbox_any or ILOpCode.Stobj or ILOpCode.Box
            or ILOpCode.Newarr or ILOpCode.Ldelema or ILOpCode.Ldelem or ILOpCode.Stelem
            or ILOpCode.Refanyval or ILOpCode.Mkrefany or ILOpCode.Initobj
            or ILOpCode.Constrained or ILOpCode.Sizeof
            => OperandKind.InlineType,
        ILOpCode.Ldtoken => OperandKind.InlineTok,

        _ => OperandKind.None,
    };
}
