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
    /// <summary>
    /// Visits one method body without copying its IL or materializing decoded
    /// instructions. The visitor receives the encoded instruction length and
    /// a metadata token only for method operand opcodes, then returns whether
    /// scanning should continue.
    /// </summary>
    /// <remarks>
    /// Structural tiling and dangling-prefix validation match
    /// <see cref="Decode(ReadOnlySpan{byte})"/> when the visitor consumes the
    /// complete stream. An early visitor stop deliberately leaves the suffix
    /// unvalidated because the consumer has already found its result.
    /// </remarks>
    public static bool Visit(
        MethodBodyBlock body,
        Func<ILOpCode, int, int, bool> visitor)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(visitor);

        BlobReader reader = body.GetILReader();
        ILOpCode previous = default;
        bool hasPrevious = false;
        while (reader.RemainingBytes > 0)
        {
            int offset = reader.Offset;
            int opcodeStart = reader.Offset;
            byte first = reader.ReadByte();
            ILOpCode opcode = first == 0xFE
                ? (ILOpCode)(0xFE00 | reader.ReadByte())
                : (ILOpCode)first;
            if (!opcode.IsValid())
            {
                throw new BadImageFormatException(
                    $"Invalid opcode 0x{(int)opcode:X} at IL_{offset:X4}");
            }

            int operandToken = 0;
            if (opcode == ILOpCode.Switch)
            {
                int count = reader.ReadInt32();
                if (count < 0
                    || count > reader.RemainingBytes / sizeof(int))
                {
                    throw new BadImageFormatException(
                        $"Malformed switch at IL_{offset:X4}");
                }
                reader.Offset += count * sizeof(int);
            }
            else
            {
                int instructionSize =
                    opcode.GetInstructionSize();
                int operandSize = instructionSize
                    - (reader.Offset - opcodeStart);
                if (instructionSize < 0
                    || operandSize < 0
                    || operandSize > reader.RemainingBytes)
                {
                    throw new BadImageFormatException(
                        $"Truncated operand at IL_{offset:X4}");
                }

                if (IsMethodOperand(opcode))
                    operandToken = reader.ReadInt32();
                else
                    reader.Offset += operandSize;
            }

            previous = opcode;
            hasPrevious = true;
            int encodedLength =
                reader.Offset - opcodeStart;
            if (!visitor(
                    opcode,
                    operandToken,
                    encodedLength))
                return false;
        }

        if (hasPrevious && previous.IsPrefix())
        {
            throw new BadImageFormatException(
                $"IL ends with a dangling prefix at IL_{reader.Offset:X4}");
        }
        return true;
    }

    public static ImmutableArray<DecodedInstruction> Decode(byte[] il)
    {
        ArgumentNullException.ThrowIfNull(il);
        return Decode(il.AsSpan());
    }

    public static ImmutableArray<DecodedInstruction> Decode(ReadOnlySpan<byte> il)
    {
        try
        {
            if (!TryDecodeCore(
                il,
                int.MaxValue,
                default,
                out ImmutableArray<DecodedInstruction> instructions,
                out _))
            {
                throw new InvalidOperationException(
                    "IL decoding exceeded the maximum addressable instruction count.");
            }
            return instructions;
        }
        catch (InvalidProgramException ex)
        {
            throw NormalizeMalformedIl(ex);
        }
    }

    /// <summary>
    /// Decodes at most <paramref name="maximumInstructions"/> instructions.
    /// The limit is checked before decoding and retaining each next instruction.
    /// </summary>
    /// <remarks>
    /// A <see langword="false"/> result reports only limit exhaustion. Malformed
    /// IL reached within the admitted prefix still throws
    /// <see cref="BadImageFormatException"/>. Partial instructions are never
    /// returned. <paramref name="decodedInstructionCount"/> reports every
    /// fully decoded instruction even when a later instruction is malformed.
    /// </remarks>
    public static bool TryDecodeBounded(
        ReadOnlySpan<byte> il,
        int maximumInstructions,
        out ImmutableArray<DecodedInstruction> instructions,
        out int decodedInstructionCount) =>
        TryDecodeBounded(
            il,
            maximumInstructions,
            default,
            out instructions,
            out decodedInstructionCount);

    /// <summary>
    /// Decodes at most <paramref name="maximumInstructions"/> instructions and
    /// observes cancellation before each admitted instruction.
    /// </summary>
    public static bool TryDecodeBounded(
        ReadOnlySpan<byte> il,
        int maximumInstructions,
        CancellationToken cancellationToken,
        out ImmutableArray<DecodedInstruction> instructions,
        out int decodedInstructionCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumInstructions);

        try
        {
            return TryDecodeCore(
                il,
                maximumInstructions,
                cancellationToken,
                out instructions,
                out decodedInstructionCount);
        }
        catch (InvalidProgramException ex)
        {
            throw NormalizeMalformedIl(ex);
        }
    }

    static bool TryDecodeCore(
        ReadOnlySpan<byte> il,
        int maximumInstructions,
        CancellationToken cancellationToken,
        out ImmutableArray<DecodedInstruction> instructions,
        out int decodedInstructionCount)
    {
        instructions = [];
        decodedInstructionCount = 0;
        var builder = ImmutableArray.CreateBuilder<DecodedInstruction>();
        var reader = new ILReader(il);

        while (reader.HasNext)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (builder.Count == maximumInstructions)
            {
                decodedInstructionCount = builder.Count;
                instructions = [];
                return false;
            }

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
                operandValue = ReadSwitchTargets(
                    ref reader,
                    offset,
                    cancellationToken,
                    out targets);
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
            decodedInstructionCount = builder.Count;
        }

        // Fail closed on a dangling prefix: a prefix (tail./constrained./no./...) must be
        // followed by another instruction, so the stream must not end on one.
        if (builder.Count > 0 && builder[^1].OpCode.IsPrefix())
            throw new BadImageFormatException(
                $"IL ends with a dangling prefix at IL_{builder[^1].Offset:X4}");

        decodedInstructionCount = builder.Count;
        instructions = builder.ToImmutable();
        return true;
    }

    static BadImageFormatException NormalizeMalformedIl(
        InvalidProgramException exception) =>
        // The runtime-ported ILReader reports a truncated/invalid read (opcode,
        // branch destination, or switch count) as InvalidProgramException -
        // the JIT/ILVerify convention. This decoder's documented contract, and
        // every Analysis consumer's malformed-IL recovery gate, is
        // BadImageFormatException.
        new(
            string.IsNullOrEmpty(exception.Message)
                ? "Malformed IL"
                : exception.Message,
            exception);

    static long ReadSwitchTargets(
        ref ILReader reader,
        int offset,
        CancellationToken cancellationToken,
        out ImmutableArray<int> targets)
    {
        uint count = reader.ReadILUInt32();
        long tableEnd = (long)reader.Offset + (long)count * 4;
        if (count > int.MaxValue / 4 || tableEnd > reader.Size)
            throw new BadImageFormatException($"Malformed switch at IL_{offset:X4}");
        int baseOffset = (int)tableEnd;
        var builder = ImmutableArray.CreateBuilder<int>((int)count);
        for (uint i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.Add(baseOffset + (int)reader.ReadILUInt32());
        }
        targets = builder.MoveToImmutable();
        return count;
    }

    /// <summary>
    /// Reads the value of a non-branch, non-switch operand. Short variable/argument indices are
    /// unsigned (ECMA-335); only <see cref="OperandKind.ShortInlineI"/> is a signed int8.
    /// </summary>
    static long ReadOperandValue(ReadOnlySpan<byte> il, int operandOffset, OperandKind kind, int offset)
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

    static byte ReadU8(ReadOnlySpan<byte> il, int operandOffset, int offset)
    {
        if ((uint)operandOffset >= (uint)il.Length)
            throw new BadImageFormatException($"Truncated operand at IL_{offset:X4}");
        return il[operandOffset];
    }

    static ReadOnlySpan<byte> Slice(ReadOnlySpan<byte> il, int position, int size, int offset)
    {
        if (position < 0 || position + size > il.Length)
            throw new BadImageFormatException($"Truncated operand at IL_{offset:X4}");
        return il.Slice(position, size);
    }

    static OperandKind Classify(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Ldarg_s or ILOpCode.Ldarga_s or ILOpCode.Starg_s
            or ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Stloc_s
            => OperandKind.ShortInlineVar,
        ILOpCode.Ldarg or ILOpCode.Ldarga or ILOpCode.Starg
            or ILOpCode.Ldloc or ILOpCode.Ldloca or ILOpCode.Stloc
            => OperandKind.InlineVar,

        ILOpCode.Ldc_i4_s or ILOpCode.Unaligned or (ILOpCode)0xFE19 => OperandKind.ShortInlineI, // 0xFE19 = no.
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

        ILOpCode.Switch => OperandKind.InlineSwitch,
        _ when opcode.IsShortBranch() => OperandKind.ShortInlineBrTarget,
        _ when opcode.IsBranch() => OperandKind.InlineBrTarget,

        _ => OperandKind.None,
    };

    static bool IsMethodOperand(ILOpCode opcode)
        => opcode is
            ILOpCode.Call
            or ILOpCode.Callvirt
            or ILOpCode.Newobj
            or ILOpCode.Jmp
            or ILOpCode.Ldftn
            or ILOpCode.Ldvirtftn;
}
