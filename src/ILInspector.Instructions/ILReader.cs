// Ported from dotnet/runtime src/coreclr/tools/Common/TypeSystem/IL/ILReader.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// IL byte reader: decodes opcodes and operands from raw IL bytes. Ported
/// from dotnet/runtime Internal.IL.ILReader (the same reader ILVerify and
/// the ILC type system build on), retargeted to the BCL's ILOpCode enum and
/// extended with peeking, skipping, and branch-destination helpers. Shared
/// by both decompiler pipelines.
/// </summary>
internal ref struct ILReader
{
    private int _currentOffset;
    private readonly ReadOnlySpan<byte> _ilBytes;
    private readonly int _baseOffset;

    public readonly int Offset => _currentOffset;
    /// <summary>Absolute offset in the full IL stream (baseOffset + currentOffset).</summary>
    public readonly int AbsoluteOffset => _baseOffset + _currentOffset;
    public readonly int Size => _ilBytes.Length;
    public readonly bool HasNext => _currentOffset < _ilBytes.Length;

    public ILReader(ReadOnlySpan<byte> ilBytes, int currentOffset = 0, int baseOffset = 0)
    {
        _ilBytes = ilBytes;
        _currentOffset = currentOffset;
        _baseOffset = baseOffset;
    }

    public byte ReadILByte()
    {
        if (_currentOffset + 1 > _ilBytes.Length)
            throw new InvalidProgramException();
        return _ilBytes[_currentOffset++];
    }

    public ushort ReadILUInt16()
    {
        if (!BinaryPrimitives.TryReadUInt16LittleEndian(_ilBytes.Slice(_currentOffset), out ushort value))
            throw new InvalidProgramException();
        _currentOffset += sizeof(ushort);
        return value;
    }

    public uint ReadILUInt32()
    {
        if (!BinaryPrimitives.TryReadUInt32LittleEndian(_ilBytes.Slice(_currentOffset), out uint value))
            throw new InvalidProgramException();
        _currentOffset += sizeof(uint);
        return value;
    }

    public int ReadILToken() => (int)ReadILUInt32();

    public ulong ReadILUInt64()
    {
        if (!BinaryPrimitives.TryReadUInt64LittleEndian(_ilBytes.Slice(_currentOffset), out ulong value))
            throw new InvalidProgramException();
        _currentOffset += sizeof(ulong);
        return value;
    }

    public ILOpCode ReadILOpcode()
    {
        byte first = ReadILByte();
        if (first == 0xFE)
            return (ILOpCode)(0xFE00 | ReadILByte());
        return (ILOpCode)first;
    }

    public readonly ILOpCode PeekILOpcode()
    {
        byte first = _ilBytes[_currentOffset];
        if (first == 0xFE)
        {
            if (_currentOffset + 2 > _ilBytes.Length)
                throw new InvalidProgramException();
            return (ILOpCode)(0xFE00 | _ilBytes[_currentOffset + 1]);
        }
        return (ILOpCode)first;
    }

    /// <summary>
    /// Skips past the operand of the given opcode, advancing the offset.
    /// Returns false if the opcode is invalid or has variable size that couldn't be handled.
    /// </summary>
    public bool TrySkip(ILOpCode opcode)
    {
        if (opcode != ILOpCode.Switch)
        {
            int size = opcode.GetInstructionSize();
            if (size < 0)
                return false;
            int opcodeSize = (int)opcode <= 0xFF ? 1 : 2;
            _currentOffset += size - opcodeSize;
            return true;
        }
        else
        {
            uint count = ReadILUInt32();
            _currentOffset += checked((int)(count * 4));
            return true;
        }
    }

    /// <summary>
    /// Skips past the operand of the given opcode, advancing the offset.
    /// Throws if the opcode is invalid.
    /// </summary>
    public void Skip(ILOpCode opcode)
    {
        if (!TrySkip(opcode))
            throw new InvalidProgramException($"Cannot skip invalid opcode {opcode}");
    }

    public void Seek(int offset) => _currentOffset = offset;

    /// <summary>
    /// Reads a branch destination offset, handling both short and long forms.
    /// Returns the absolute IL offset of the branch target.
    /// </summary>
    public int ReadBranchDestination(ILOpCode opcode)
    {
        if ((opcode >= ILOpCode.Br_s && opcode <= ILOpCode.Blt_un_s) || opcode == ILOpCode.Leave_s)
            return (sbyte)ReadILByte() + AbsoluteOffset;

        Debug.Assert((opcode >= ILOpCode.Br && opcode <= ILOpCode.Blt_un) || opcode == ILOpCode.Leave);
        return (int)ReadILUInt32() + AbsoluteOffset;
    }
}
