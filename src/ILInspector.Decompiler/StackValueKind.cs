// Ported from dotnet/runtime src/coreclr/tools/Common/TypeSystem/IL/StackValueKind.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Reflection.Metadata;

namespace ILInspector.Decompiler;

/// <summary>
/// Corresponds to ECMA-335 I.12.3.2.1 "The evaluation stack" —
/// the categories of values that can appear on the IL evaluation stack.
/// </summary>
public enum StackValueKind
{
    /// <summary>An unknown type.</summary>
    Unknown,
    /// <summary>Any signed or unsigned integer values representable as 32 bits.</summary>
    Int32,
    /// <summary>Any signed or unsigned integer values representable as 64 bits.</summary>
    Int64,
    /// <summary>Platform pointer type (IntPtr/UIntPtr/pointers).</summary>
    NativeInt,
    /// <summary>Any floating-point value (float or double).</summary>
    Float,
    /// <summary>A managed pointer (byref).</summary>
    ByRef,
    /// <summary>An object reference.</summary>
    ObjRef,
    /// <summary>A value type that is not one of the primitive types above.</summary>
    ValueType
}

/// <summary>
/// Extension methods for <see cref="StackValueKind"/>.
/// </summary>
public static class StackValueKindExtensions
{
    /// <summary>
    /// Returns the <see cref="StackValueKind"/> produced by a load/constant opcode.
    /// Returns <see cref="StackValueKind.Unknown"/> for opcodes that don't directly
    /// indicate a stack type (e.g., calls, field loads that depend on the field type).
    /// </summary>
    public static StackValueKind ResultKind(this ILOpCode opcode) => opcode switch
    {
        // Int32 loads
        ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or
        ILOpCode.Ldc_i4_2 or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or
        ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6 or ILOpCode.Ldc_i4_7 or
        ILOpCode.Ldc_i4_8 or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4
            => StackValueKind.Int32,

        // Int64 loads
        ILOpCode.Ldc_i8 => StackValueKind.Int64,

        // Float loads
        ILOpCode.Ldc_r4 or ILOpCode.Ldc_r8 => StackValueKind.Float,

        // Conversions that produce known types
        ILOpCode.Conv_i1 or ILOpCode.Conv_i2 or ILOpCode.Conv_i4 or
        ILOpCode.Conv_u1 or ILOpCode.Conv_u2 or ILOpCode.Conv_u4 or
        ILOpCode.Conv_ovf_i1 or ILOpCode.Conv_ovf_i2 or ILOpCode.Conv_ovf_i4 or
        ILOpCode.Conv_ovf_u1 or ILOpCode.Conv_ovf_u2 or ILOpCode.Conv_ovf_u4 or
        ILOpCode.Conv_ovf_i1_un or ILOpCode.Conv_ovf_i2_un or ILOpCode.Conv_ovf_i4_un or
        ILOpCode.Conv_ovf_u1_un or ILOpCode.Conv_ovf_u2_un or ILOpCode.Conv_ovf_u4_un
            => StackValueKind.Int32,

        ILOpCode.Conv_i8 or ILOpCode.Conv_u8 or
        ILOpCode.Conv_ovf_i8 or ILOpCode.Conv_ovf_u8 or
        ILOpCode.Conv_ovf_i8_un or ILOpCode.Conv_ovf_u8_un
            => StackValueKind.Int64,

        ILOpCode.Conv_r4 or ILOpCode.Conv_r8 or ILOpCode.Conv_r_un
            => StackValueKind.Float,

        ILOpCode.Conv_i or ILOpCode.Conv_u or
        ILOpCode.Conv_ovf_i or ILOpCode.Conv_ovf_u or
        ILOpCode.Conv_ovf_i_un or ILOpCode.Conv_ovf_u_un
            => StackValueKind.NativeInt,

        // Indirect loads
        ILOpCode.Ldind_i1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_i4 or
        ILOpCode.Ldind_u1 or ILOpCode.Ldind_u2 or ILOpCode.Ldind_u4
            => StackValueKind.Int32,
        ILOpCode.Ldind_i8 => StackValueKind.Int64,
        ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 => StackValueKind.Float,
        ILOpCode.Ldind_i => StackValueKind.NativeInt,
        ILOpCode.Ldind_ref => StackValueKind.ObjRef,

        // Object operations
        ILOpCode.Ldnull => StackValueKind.ObjRef,
        ILOpCode.Ldstr => StackValueKind.ObjRef,
        ILOpCode.Castclass or ILOpCode.Isinst or ILOpCode.Box or
        ILOpCode.Newarr => StackValueKind.ObjRef,

        // Comparison/arithmetic results
        ILOpCode.Ceq or ILOpCode.Cgt or ILOpCode.Cgt_un or
        ILOpCode.Clt or ILOpCode.Clt_un => StackValueKind.Int32,

        // Address operations
        ILOpCode.Ldloca or ILOpCode.Ldloca_s or
        ILOpCode.Ldarga or ILOpCode.Ldarga_s or
        ILOpCode.Ldelema or ILOpCode.Ldflda or ILOpCode.Ldsflda
            => StackValueKind.ByRef,

        // Sizeof
        ILOpCode.Sizeof => StackValueKind.Int32,

        // Localloc
        ILOpCode.Localloc => StackValueKind.NativeInt,

        // Arglist
        ILOpCode.Arglist => StackValueKind.ValueType,

        // Len
        ILOpCode.Ldlen => StackValueKind.NativeInt,

        // Array element loads
        ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_i4 or
        ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4
            => StackValueKind.Int32,
        ILOpCode.Ldelem_i8 => StackValueKind.Int64,
        ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8 => StackValueKind.Float,
        ILOpCode.Ldelem_i => StackValueKind.NativeInt,
        ILOpCode.Ldelem_ref => StackValueKind.ObjRef,

        _ => StackValueKind.Unknown
    };
}
