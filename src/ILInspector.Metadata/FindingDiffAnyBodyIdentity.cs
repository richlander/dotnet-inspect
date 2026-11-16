using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Instructions;
using InertText;

namespace ILInspector.Metadata;

public static partial class FindingDiffAny
{
    /// <summary>
    /// Encodes one present method body as the detached atom stream whose byte
    /// equality is the Body policy's equality.
    /// </summary>
    internal static OpaqueByteRange EncodeBodyIdentity(
        Side side,
        MethodDefinitionHandle handle,
        out int ilBytes)
    {
        MethodDefinition method = side.Md.GetMethodDefinition(handle);
        if (method.RelativeVirtualAddress == 0)
            throw new ArgumentException("The method has no body.", nameof(handle));

        RawMethodBodyView body = RawMethodBodyView.Read(side.Pe, method.RelativeVirtualAddress);
        ilBytes = body.IL.Length;
        var writer = new BodyIdentityWriter(Math.Max(64, ilBytes * 2));
        try
        {
            EncodeBodyIdentity(side, body, writer);
            return OpaqueByteRange.Copy(writer.Written);
        }
        catch (InvalidProgramException ex)
        {
            throw new BadImageFormatException("The IL ends inside an instruction.", ex);
        }
    }

    private static void EncodeBodyIdentity(Side side, RawMethodBodyView body, BodyIdentityWriter writer)
    {
        var reader = new ILReader(body.IlBytes);
        int[] starts = new int[256];
        bool[] admitted = new bool[256];
        var targets = new List<(int Position, int Offset)>();
        int count = 0;
        bool prefixed = false;

        while (reader.HasNext)
        {
            if (count == starts.Length)
            {
                Array.Resize(ref starts, count * 2);
                Array.Resize(ref admitted, count * 2);
            }
            starts[count] = reader.Offset;
            admitted[count] = !prefixed;
            count++;

            ILOpCode opCode = BodyWalk.ReadOpCode(ref reader);
            if (BodyWalk.IsLoadInt32(opCode))
            {
                writer.Int32((int)ILOpCode.Ldc_i4);
                writer.Int32(BodyWalk.LoadedInt32(opCode, ref reader));
                prefixed = false;
                continue;
            }

            writer.Int32((int)BodyWalk.Family(opCode));
            prefixed = opCode.IsPrefix();
            OperandKind operand = opCode.GetOperandKind();
            switch (operand)
            {
                case OperandKind.None:
                    break;
                case OperandKind.ShortInlineVar:
                case OperandKind.ShortInlineI:
                    writer.Byte(reader.ReadILByte());
                    break;
                case OperandKind.InlineVar:
                    writer.UInt16(reader.ReadILUInt16());
                    break;
                case OperandKind.InlineI:
                case OperandKind.ShortInlineR:
                    writer.UInt32(reader.ReadILUInt32());
                    break;
                case OperandKind.InlineI8:
                case OperandKind.InlineR:
                    writer.UInt64(reader.ReadILUInt64());
                    break;
                case OperandKind.ShortInlineBrTarget:
                case OperandKind.InlineBrTarget:
                    targets.Add((writer.ReserveInt32(), reader.ReadBranchDestination(opCode)));
                    break;
                case OperandKind.InlineSwitch:
                {
                    uint arms = reader.ReadILUInt32();
                    if (arms > (uint)(reader.Size - reader.Offset) / sizeof(int))
                        throw new BadImageFormatException("The switch table does not fit the body.");
                    writer.UInt32(arms);
                    int baseOffset = reader.Offset + sizeof(int) * (int)arms;
                    for (uint i = 0; i < arms; i++)
                        targets.Add((writer.ReserveInt32(), baseOffset + (int)reader.ReadILUInt32()));
                    break;
                }
                case OperandKind.InlineString:
                    side.Strings.WriteValue(reader.ReadILToken(), writer);
                    break;
                case OperandKind.InlineSig:
                    WriteSignature(side, MetadataTokens.EntityHandle(reader.ReadILToken()), writer);
                    break;
                default:
                {
                    EntityHandle operandHandle = MetadataTokens.EntityHandle(reader.ReadILToken());
                    BodyWalk.RequireOperandKind(operand, side, operandHandle);
                    SymbolKey<FindingDiffAnyPart> key = side.ReferenceKey(operandHandle).Detach();
                    writer.Bytes(side.Interner.ExpandDetached(key, side.Transient));
                    break;
                }
            }
        }
        if (prefixed)
            throw new BadImageFormatException("The IL ends with a prefix.");

        foreach ((int position, int offset) in targets)
            writer.PatchInt32(position, BodyWalk.TargetIndex(starts, admitted, count, offset));

        writer.Int32(count);
        WriteExceptionRegions(side, body, starts, admitted, count, reader.Size, writer);
    }

    private static void WriteSignature(Side side, EntityHandle handle, BodyIdentityWriter writer)
    {
        if (handle.Kind != HandleKind.StandaloneSignature)
            throw new BadImageFormatException("A calli operand is not a stand-alone signature.");
        BlobReader signature = side.Md.GetBlobReader(
            side.Md.GetStandaloneSignature((StandaloneSignatureHandle)handle).Signature);
        writer.Bytes(signature.ReadBytes(signature.Length));
    }

    private static void WriteExceptionRegions(
        Side side,
        RawMethodBodyView body,
        int[] starts,
        bool[] admitted,
        int count,
        int length,
        BodyIdentityWriter writer)
    {
        var regions = new List<RawExceptionRegion>();
        RawExceptionRegionEnumerator source = body.ExceptionRegions;
        while (source.MoveNext(out RawExceptionRegion region))
        {
            RequireBoundary(region.TryOffset);
            RequireBoundary(region.TryOffset + region.TryLength);
            RequireBoundary(region.HandlerOffset);
            RequireBoundary(region.HandlerOffset + region.HandlerLength);
            if (region.Kind == ExceptionRegionKind.Filter)
                RequireBoundary(region.FilterOffset);
            regions.Add(region);
        }

        writer.Int32(regions.Count);
        foreach (RawExceptionRegion region in regions)
        {
            writer.Int32((int)region.Kind);
            if (region.Kind == ExceptionRegionKind.Catch)
            {
                BodyWalk.RequireOperandKind(OperandKind.InlineType, side, region.CatchType);
                SymbolKey<FindingDiffAnyPart> key = side.ReferenceKey(region.CatchType).Detach();
                writer.Bytes(side.Interner.ExpandDetached(key, side.Transient));
            }
            writer.Int32(Boundary(region.TryOffset));
            writer.Int32(Boundary(region.TryOffset + region.TryLength));
            writer.Int32(Boundary(region.HandlerOffset));
            writer.Int32(Boundary(region.HandlerOffset + region.HandlerLength));
            if (region.Kind == ExceptionRegionKind.Filter)
                writer.Int32(Boundary(region.FilterOffset));
        }

        void RequireBoundary(int offset) => _ = Boundary(offset);

        int Boundary(int offset)
        {
            if (offset == length)
                return count;
            if (offset < 0 || offset > length)
                throw new BadImageFormatException($"Exception clause boundary IL_{offset:X4} lies outside the body.");
            return BodyWalk.TargetIndex(starts, admitted, count, offset);
        }
    }

    internal sealed class BodyIdentityWriter
    {
        private byte[] _buffer;
        private int _length;

        public BodyIdentityWriter(int capacity) => _buffer = new byte[capacity];

        public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _length);

        public void Byte(byte value)
        {
            Ensure(sizeof(byte));
            _buffer[_length++] = value;
        }

        public void UInt16(ushort value)
        {
            Ensure(sizeof(ushort));
            BinaryPrimitives.WriteUInt16LittleEndian(_buffer.AsSpan(_length), value);
            _length += sizeof(ushort);
        }

        public void Int32(int value)
        {
            Ensure(sizeof(int));
            BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(_length), value);
            _length += sizeof(int);
        }

        public void UInt32(uint value)
        {
            Ensure(sizeof(uint));
            BinaryPrimitives.WriteUInt32LittleEndian(_buffer.AsSpan(_length), value);
            _length += sizeof(uint);
        }

        public void UInt64(ulong value)
        {
            Ensure(sizeof(ulong));
            BinaryPrimitives.WriteUInt64LittleEndian(_buffer.AsSpan(_length), value);
            _length += sizeof(ulong);
        }

        public void Bytes(ReadOnlySpan<byte> value)
        {
            Int32(value.Length);
            Ensure(value.Length);
            value.CopyTo(_buffer.AsSpan(_length));
            _length += value.Length;
        }

        public void Bytes(OpaqueByteRange value)
        {
            Int32(value.Length);
            Ensure(value.Length);
            for (int i = 0; i < value.Length; i++)
                _buffer[_length++] = value.ByteAt(i);
        }

        public int ReserveInt32()
        {
            int position = _length;
            Int32(0);
            return position;
        }

        public void PatchInt32(int position, int value)
            => BinaryPrimitives.WriteInt32LittleEndian(_buffer.AsSpan(position, sizeof(int)), value);

        private void Ensure(int additional)
        {
            int required = checked(_length + additional);
            if (required <= _buffer.Length)
                return;
            Array.Resize(ref _buffer, Math.Max(required, checked(_buffer.Length * 2)));
        }
    }
}
