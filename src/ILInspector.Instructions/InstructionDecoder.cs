using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// Decodes a raw IL byte stream into a typed <see cref="DecodedInstruction"/> sequence.
/// This is the one decode the typed-stack substrate owns; it is intended to replace the
/// hand-rolled decoders currently duplicated in <c>ReachingDefinitions</c> and the
/// decompiler importer. SRM-only, allocation-light, fail-closed on malformed IL.
/// </summary>
public static class InstructionDecoder
{
    public static ImmutableArray<DecodedInstruction> Decode(byte[] il)
    {
        ArgumentNullException.ThrowIfNull(il);
        var builder = ImmutableArray.CreateBuilder<DecodedInstruction>();
        int position = 0;
        while (position < il.Length)
        {
            int offset = position;
            var opcode = ReadOpcode(il, ref position);
            int operandOffset = position;
            var kind = Classify(opcode);

            long operandValue = 0;
            var targets = ImmutableArray<int>.Empty;
            bool branches = false;
            bool unconditional = false;
            bool exits = false;
            bool leaves = false;

            switch (kind)
            {
                case OperandKind.None:
                    break;
                case OperandKind.ShortInlineVar:
                case OperandKind.ShortInlineI:
                    operandValue = (sbyte)ReadByte(il, ref position, offset);
                    break;
                case OperandKind.InlineVar:
                    operandValue = BinaryPrimitives.ReadUInt16LittleEndian(Slice(il, position, 2, offset));
                    position += 2;
                    break;
                case OperandKind.InlineI:
                case OperandKind.ShortInlineR:
                case OperandKind.InlineString:
                case OperandKind.InlineMethod:
                case OperandKind.InlineField:
                case OperandKind.InlineType:
                case OperandKind.InlineSig:
                case OperandKind.InlineTok:
                    operandValue = BinaryPrimitives.ReadInt32LittleEndian(Slice(il, position, 4, offset));
                    position += 4;
                    break;
                case OperandKind.InlineI8:
                case OperandKind.InlineR:
                    operandValue = BinaryPrimitives.ReadInt64LittleEndian(Slice(il, position, 8, offset));
                    position += 8;
                    break;
                case OperandKind.ShortInlineBrTarget:
                {
                    sbyte rel = (sbyte)ReadByte(il, ref position, offset);
                    targets = [position + rel];
                    branches = true;
                    unconditional = opcode is ILOpCode.Br_s or ILOpCode.Leave_s;
                    leaves = opcode is ILOpCode.Leave_s;
                    break;
                }
                case OperandKind.InlineBrTarget:
                {
                    int rel = BinaryPrimitives.ReadInt32LittleEndian(Slice(il, position, 4, offset));
                    position += 4;
                    targets = [position + rel];
                    branches = true;
                    unconditional = opcode is ILOpCode.Br or ILOpCode.Leave;
                    leaves = opcode is ILOpCode.Leave;
                    break;
                }
                case OperandKind.InlineSwitch:
                {
                    int count = BinaryPrimitives.ReadInt32LittleEndian(Slice(il, position, 4, offset));
                    position += 4;
                    if (count < 0)
                        throw new BadImageFormatException($"Malformed switch at IL_{offset:X4}");
                    long tableEnd = (long)position + (long)count * 4;
                    if (tableEnd > il.Length)
                        throw new BadImageFormatException($"Malformed switch at IL_{offset:X4}");
                    int baseOffset = (int)tableEnd;
                    var t = ImmutableArray.CreateBuilder<int>(count);
                    for (int i = 0; i < count; i++)
                    {
                        t.Add(baseOffset + BinaryPrimitives.ReadInt32LittleEndian(Slice(il, position, 4, offset)));
                        position += 4;
                    }
                    targets = t.MoveToImmutable();
                    branches = true;     // conditional: also falls through
                    operandValue = count;
                    break;
                }
            }

            if (!branches)
            {
                exits = opcode is ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Jmp
                    or ILOpCode.Endfinally or ILOpCode.Endfilter;
            }

            // br/leave and the method-exit opcodes do not fall through; switch and
            // conditional branches do.
            bool fallsThrough = !(exits || unconditional);

            builder.Add(new DecodedInstruction(
                offset, opcode, operandOffset, position, kind, operandValue,
                targets, branches, unconditional, exits, fallsThrough, leaves));
        }

        return builder.ToImmutable();
    }

    static ILOpCode ReadOpcode(byte[] il, ref int position)
    {
        byte first = ReadByte(il, ref position, position);
        if (first != 0xFE)
            return (ILOpCode)first;
        byte second = ReadByte(il, ref position, position - 1);
        return (ILOpCode)(0xFE00 | second);
    }

    static byte ReadByte(byte[] il, ref int position, int offset)
    {
        if ((uint)position >= (uint)il.Length)
            throw new BadImageFormatException($"Malformed IL at IL_{offset:X4}");
        return il[position++];
    }

    static ReadOnlySpan<byte> Slice(byte[] il, int position, int size, int offset)
    {
        if (position < 0 || position + size > il.Length)
            throw new BadImageFormatException($"Malformed IL operand at IL_{offset:X4}");
        return il.AsSpan(position, size);
    }

    static OperandKind Classify(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Br_s or ILOpCode.Leave_s or ILOpCode.Brfalse_s or ILOpCode.Brtrue_s
            or ILOpCode.Beq_s or ILOpCode.Bge_s or ILOpCode.Bgt_s or ILOpCode.Ble_s or ILOpCode.Blt_s
            or ILOpCode.Bne_un_s or ILOpCode.Bge_un_s or ILOpCode.Bgt_un_s
            or ILOpCode.Ble_un_s or ILOpCode.Blt_un_s
            => OperandKind.ShortInlineBrTarget,

        ILOpCode.Br or ILOpCode.Leave or ILOpCode.Brfalse or ILOpCode.Brtrue
            or ILOpCode.Beq or ILOpCode.Bge or ILOpCode.Bgt or ILOpCode.Ble or ILOpCode.Blt
            or ILOpCode.Bne_un or ILOpCode.Bge_un or ILOpCode.Bgt_un
            or ILOpCode.Ble_un or ILOpCode.Blt_un
            => OperandKind.InlineBrTarget,

        ILOpCode.Switch => OperandKind.InlineSwitch,

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
