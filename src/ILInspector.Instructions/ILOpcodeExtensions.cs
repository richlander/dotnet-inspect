// Ported from dotnet/runtime src/coreclr/tools/Common/TypeSystem/IL/ILOpcodeHelper.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// One-time port: the IL opcode set is ECMA-335-frozen, so no upstream sync is
// tracked. See docs/design/instruction-substrate.md ("dotnet/runtime heritage").

using System.Reflection.Metadata;

namespace ILInspector.Instructions;

/// <summary>
/// Extension methods for <see cref="ILOpCode"/> providing size, validity, and branch classification.
/// Ported from dotnet/runtime Internal.IL.ILOpcodeHelper, adapted to use BCL's ILOpCode enum.
/// </summary>
internal static class ILOpcodeExtensions
{
    private const byte VariableSize = 0xFF;
    private const byte Invalid = 0xFE;

    /// <summary>
    /// Gets the size, in bytes, of the opcode and its arguments.
    /// For <see cref="ILOpCode.Switch"/>, the caller must handle variable sizing separately.
    /// </summary>
    public static int GetInstructionSize(this ILOpCode opcode)
    {
        int index = GetIndex(opcode);
        if ((uint)index >= (uint)s_opcodeSizes.Length)
            return -1;
        byte result = s_opcodeSizes[index];
        return result is VariableSize or Invalid ? -1 : result;
    }

    /// <summary>
    /// Returns true if the opcode is a valid IL opcode.
    /// </summary>
    public static bool IsValid(this ILOpCode opcode)
    {
        int index = GetIndex(opcode);
        return (uint)index < (uint)s_opcodeSizes.Length
            && s_opcodeSizes[index] != Invalid;
    }

    /// <summary>
    /// Returns true if the opcode is a branch instruction (conditional or unconditional),
    /// including leave/leave.s.
    /// </summary>
    public static bool IsBranch(this ILOpCode opcode)
    {
        if (opcode >= ILOpCode.Br && opcode <= ILOpCode.Blt_un)
            return true;

        if (opcode >= ILOpCode.Br_s && opcode <= ILOpCode.Blt_un_s)
            return true;

        if (opcode is ILOpCode.Leave_s or ILOpCode.Leave)
            return true;

        return false;
    }

    /// <summary>
    /// Returns true if the opcode is an unconditional branch (br, br.s, leave, leave.s).
    /// </summary>
    public static bool IsUnconditionalBranch(this ILOpCode opcode)
        => opcode is ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Leave or ILOpCode.Leave_s;

    /// <summary>
    /// Maps ILOpCode to a linear index for lookup table access.
    /// Single-byte opcodes (0x00–0xFE) map directly; two-byte opcodes (0xFE00+)
    /// map to 0x100 + second byte (after the 0xFF entry for the prefix marker).
    /// </summary>
    static int GetIndex(ILOpCode opcode)
    {
        int value = (int)opcode;
        if (value <= 0xFE)
            return value;
        if ((value & 0xFF00) == 0xFE00)
            return 0x100 + (value & 0xFF);
        return -1;
    }

    // Indexed by GetIndex(opcode). Values are total instruction size in bytes.
    // VariableSize (0xFF) = switch; Invalid (0xFE) = not a valid opcode.
    static readonly byte[] s_opcodeSizes =
    [
        1, // nop = 0x00
        1, // break = 0x01
        1, // ldarg.0 = 0x02
        1, // ldarg.1 = 0x03
        1, // ldarg.2 = 0x04
        1, // ldarg.3 = 0x05
        1, // ldloc.0 = 0x06
        1, // ldloc.1 = 0x07
        1, // ldloc.2 = 0x08
        1, // ldloc.3 = 0x09
        1, // stloc.0 = 0x0a
        1, // stloc.1 = 0x0b
        1, // stloc.2 = 0x0c
        1, // stloc.3 = 0x0d
        2, // ldarg.s = 0x0e
        2, // ldarga.s = 0x0f
        2, // starg.s = 0x10
        2, // ldloc.s = 0x11
        2, // ldloca.s = 0x12
        2, // stloc.s = 0x13
        1, // ldnull = 0x14
        1, // ldc.i4.m1 = 0x15
        1, // ldc.i4.0 = 0x16
        1, // ldc.i4.1 = 0x17
        1, // ldc.i4.2 = 0x18
        1, // ldc.i4.3 = 0x19
        1, // ldc.i4.4 = 0x1a
        1, // ldc.i4.5 = 0x1b
        1, // ldc.i4.6 = 0x1c
        1, // ldc.i4.7 = 0x1d
        1, // ldc.i4.8 = 0x1e
        2, // ldc.i4.s = 0x1f
        5, // ldc.i4 = 0x20
        9, // ldc.i8 = 0x21
        5, // ldc.r4 = 0x22
        9, // ldc.r8 = 0x23
        Invalid, // 0x24
        1, // dup = 0x25
        1, // pop = 0x26
        5, // jmp = 0x27
        5, // call = 0x28
        5, // calli = 0x29
        1, // ret = 0x2a
        2, // br.s = 0x2b
        2, // brfalse.s = 0x2c
        2, // brtrue.s = 0x2d
        2, // beq.s = 0x2e
        2, // bge.s = 0x2f
        2, // bgt.s = 0x30
        2, // ble.s = 0x31
        2, // blt.s = 0x32
        2, // bne.un.s = 0x33
        2, // bge.un.s = 0x34
        2, // bgt.un.s = 0x35
        2, // ble.un.s = 0x36
        2, // blt.un.s = 0x37
        5, // br = 0x38
        5, // brfalse = 0x39
        5, // brtrue = 0x3a
        5, // beq = 0x3b
        5, // bge = 0x3c
        5, // bgt = 0x3d
        5, // ble = 0x3e
        5, // blt = 0x3f
        5, // bne.un = 0x40
        5, // bge.un = 0x41
        5, // bgt.un = 0x42
        5, // ble.un = 0x43
        5, // blt.un = 0x44
        VariableSize, // switch = 0x45
        1, // ldind.i1 = 0x46
        1, // ldind.u1 = 0x47
        1, // ldind.i2 = 0x48
        1, // ldind.u2 = 0x49
        1, // ldind.i4 = 0x4a
        1, // ldind.u4 = 0x4b
        1, // ldind.i8 = 0x4c
        1, // ldind.i = 0x4d
        1, // ldind.r4 = 0x4e
        1, // ldind.r8 = 0x4f
        1, // ldind.ref = 0x50
        1, // stind.ref = 0x51
        1, // stind.i1 = 0x52
        1, // stind.i2 = 0x53
        1, // stind.i4 = 0x54
        1, // stind.i8 = 0x55
        1, // stind.r4 = 0x56
        1, // stind.r8 = 0x57
        1, // add = 0x58
        1, // sub = 0x59
        1, // mul = 0x5a
        1, // div = 0x5b
        1, // div.un = 0x5c
        1, // rem = 0x5d
        1, // rem.un = 0x5e
        1, // and = 0x5f
        1, // or = 0x60
        1, // xor = 0x61
        1, // shl = 0x62
        1, // shr = 0x63
        1, // shr.un = 0x64
        1, // neg = 0x65
        1, // not = 0x66
        1, // conv.i1 = 0x67
        1, // conv.i2 = 0x68
        1, // conv.i4 = 0x69
        1, // conv.i8 = 0x6a
        1, // conv.r4 = 0x6b
        1, // conv.r8 = 0x6c
        1, // conv.u4 = 0x6d
        1, // conv.u8 = 0x6e
        5, // callvirt = 0x6f
        5, // cpobj = 0x70
        5, // ldobj = 0x71
        5, // ldstr = 0x72
        5, // newobj = 0x73
        5, // castclass = 0x74
        5, // isinst = 0x75
        1, // conv.r.un = 0x76
        Invalid, // 0x77
        Invalid, // 0x78
        5, // unbox = 0x79
        1, // throw = 0x7a
        5, // ldfld = 0x7b
        5, // ldflda = 0x7c
        5, // stfld = 0x7d
        5, // ldsfld = 0x7e
        5, // ldsflda = 0x7f
        5, // stsfld = 0x80
        5, // stobj = 0x81
        1, // conv.ovf.i1.un = 0x82
        1, // conv.ovf.i2.un = 0x83
        1, // conv.ovf.i4.un = 0x84
        1, // conv.ovf.i8.un = 0x85
        1, // conv.ovf.u1.un = 0x86
        1, // conv.ovf.u2.un = 0x87
        1, // conv.ovf.u4.un = 0x88
        1, // conv.ovf.u8.un = 0x89
        1, // conv.ovf.i.un = 0x8a
        1, // conv.ovf.u.un = 0x8b
        5, // box = 0x8c
        5, // newarr = 0x8d
        1, // ldlen = 0x8e
        5, // ldelema = 0x8f
        1, // ldelem.i1 = 0x90
        1, // ldelem.u1 = 0x91
        1, // ldelem.i2 = 0x92
        1, // ldelem.u2 = 0x93
        1, // ldelem.i4 = 0x94
        1, // ldelem.u4 = 0x95
        1, // ldelem.i8 = 0x96
        1, // ldelem.i = 0x97
        1, // ldelem.r4 = 0x98
        1, // ldelem.r8 = 0x99
        1, // ldelem.ref = 0x9a
        1, // stelem.i = 0x9b
        1, // stelem.i1 = 0x9c
        1, // stelem.i2 = 0x9d
        1, // stelem.i4 = 0x9e
        1, // stelem.i8 = 0x9f
        1, // stelem.r4 = 0xa0
        1, // stelem.r8 = 0xa1
        1, // stelem.ref = 0xa2
        5, // ldelem = 0xa3
        5, // stelem = 0xa4
        5, // unbox.any = 0xa5
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, // 0xa6-0xae
        Invalid, Invalid, Invalid, Invalid, // 0xaf-0xb2
        1, // conv.ovf.i1 = 0xb3
        1, // conv.ovf.u1 = 0xb4
        1, // conv.ovf.i2 = 0xb5
        1, // conv.ovf.u2 = 0xb6
        1, // conv.ovf.i4 = 0xb7
        1, // conv.ovf.u4 = 0xb8
        1, // conv.ovf.i8 = 0xb9
        1, // conv.ovf.u8 = 0xba
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, // 0xbb-0xc1
        5, // refanyval = 0xc2
        1, // ckfinite = 0xc3
        Invalid, Invalid, // 0xc4-0xc5
        5, // mkrefany = 0xc6
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, // 0xc7-0xcf
        5, // ldtoken = 0xd0
        1, // conv.u2 = 0xd1
        1, // conv.u1 = 0xd2
        1, // conv.i = 0xd3
        1, // conv.ovf.i = 0xd4
        1, // conv.ovf.u = 0xd5
        1, // add.ovf = 0xd6
        1, // add.ovf.un = 0xd7
        1, // mul.ovf = 0xd8
        1, // mul.ovf.un = 0xd9
        1, // sub.ovf = 0xda
        1, // sub.ovf.un = 0xdb
        1, // endfinally = 0xdc
        5, // leave = 0xdd
        2, // leave.s = 0xde
        1, // stind.i = 0xdf
        1, // conv.u = 0xe0
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, // 0xe1-0xe7
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, // 0xe8-0xee
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, // 0xef-0xf5
        Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, Invalid, // 0xf6-0xfc
        Invalid, // 0xfd
        1, // prefix1 = 0xfe (marker only, not a real instruction)
        Invalid, // 0xff
        // Two-byte opcodes (0xFE00+), indexed at 0xFF + second byte
        2, // arglist = 0xfe00
        2, // ceq = 0xfe01
        2, // cgt = 0xfe02
        2, // cgt.un = 0xfe03
        2, // clt = 0xfe04
        2, // clt.un = 0xfe05
        6, // ldftn = 0xfe06
        6, // ldvirtftn = 0xfe07
        Invalid, // 0xfe08
        4, // ldarg = 0xfe09
        4, // ldarga = 0xfe0a
        4, // starg = 0xfe0b
        4, // ldloc = 0xfe0c
        4, // ldloca = 0xfe0d
        4, // stloc = 0xfe0e
        2, // localloc = 0xfe0f
        Invalid, // 0xfe10
        2, // endfilter = 0xfe11
        3, // unaligned. = 0xfe12
        2, // volatile. = 0xfe13
        2, // tail. = 0xfe14
        6, // initobj = 0xfe15
        6, // constrained. = 0xfe16
        2, // cpblk = 0xfe17
        2, // initblk = 0xfe18
        3, // no. = 0xfe19
        2, // rethrow = 0xfe1a
        Invalid, // 0xfe1b
        6, // sizeof = 0xfe1c
        2, // refanytype = 0xfe1d
        2, // readonly. = 0xfe1e
    ];
}
