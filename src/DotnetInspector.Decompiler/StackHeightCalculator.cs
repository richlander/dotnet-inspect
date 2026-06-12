// Ported from dotnet/runtime src/coreclr/tools/Common/TypeSystem/IL/ILStackHelper.cs
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace DotnetInspector.Decompiler;

/// <summary>
/// Computes maximum stack depth for a method body by simulating stack height
/// changes per instruction. Ported from dotnet/runtime Internal.IL.ILStackHelper,
/// adapted to use <see cref="MethodBodyContext"/> and BCL types.
/// </summary>
internal static class StackHeightCalculator
{
    const int StackHeightNotSet = int.MinValue;
    const int StackAllocThreshold = 256 / sizeof(int);

    /// <summary>
    /// Computes the maximum evaluation stack depth for the given method body.
    /// </summary>
    public static int ComputeMaxStack(MethodBodyContext context)
        => Compute(context, heightsOut: null);

    /// <summary>
    /// Per-IL-offset entry stack heights (<see cref="StackHeightNotSet"/> for
    /// offsets the linear walk never assigned). Lets conditional detection tell
    /// statement-level joins (height 0) from stack value merges (height &gt; 0).
    /// </summary>
    public static int[] ComputeOffsetEntryHeights(MethodBodyContext context)
    {
        var heights = new int[context.ILBytes.Length];
        Compute(context, heights);
        return heights;
    }

    static int Compute(MethodBodyContext context, int[]? heightsOut)
    {
        var ilReader = new ILReader(context.ILBytes);
        int stackHeight = 0;
        int maxStack = 0;

        Span<int> stackHeights = heightsOut is not null
            ? heightsOut.AsSpan(0, ilReader.Size)
            : ilReader.Size <= StackAllocThreshold
                ? stackalloc int[StackAllocThreshold].Slice(0, ilReader.Size)
                : new int[ilReader.Size];

        stackHeights.Fill(StackHeightNotSet);

        // Catch and filter clauses push the exception object onto the stack
        foreach (var region in context.ExceptionRegions)
        {
            if (region.Kind == ExceptionRegionKind.Catch)
            {
                if ((uint)region.HandlerOffset < (uint)stackHeights.Length)
                    stackHeights[region.HandlerOffset] = 1;
            }
            else if (region.Kind == ExceptionRegionKind.Filter)
            {
                if ((uint)region.FilterOffset < (uint)stackHeights.Length)
                    stackHeights[region.FilterOffset] = 1;
                if ((uint)region.HandlerOffset < (uint)stackHeights.Length)
                    stackHeights[region.HandlerOffset] = 1;
            }
        }

        while (ilReader.HasNext)
        {
            int currentOffset = ilReader.Offset;
            if ((uint)currentOffset >= (uint)stackHeights.Length)
                break;

            ILOpCode opcode = ilReader.ReadILOpcode();

            if (stackHeight == StackHeightNotSet)
                stackHeight = stackHeights[currentOffset];

            if (stackHeight == StackHeightNotSet)
                stackHeight = 0;

            Debug.Assert(stackHeights[currentOffset] == StackHeightNotSet
                || stackHeights[currentOffset] == stackHeight);
            stackHeights[currentOffset] = stackHeight;

            bool hasReadOperand = false;
            switch (opcode)
            {
                // Push 1
                case ILOpCode.Arglist:
                case ILOpCode.Dup:
                case ILOpCode.Ldc_i4:
                case ILOpCode.Ldc_i4_0:
                case ILOpCode.Ldc_i4_1:
                case ILOpCode.Ldc_i4_2:
                case ILOpCode.Ldc_i4_3:
                case ILOpCode.Ldc_i4_4:
                case ILOpCode.Ldc_i4_5:
                case ILOpCode.Ldc_i4_6:
                case ILOpCode.Ldc_i4_7:
                case ILOpCode.Ldc_i4_8:
                case ILOpCode.Ldc_i4_m1:
                case ILOpCode.Ldc_i4_s:
                case ILOpCode.Ldc_i8:
                case ILOpCode.Ldc_r4:
                case ILOpCode.Ldc_r8:
                case ILOpCode.Ldftn:
                case ILOpCode.Ldnull:
                case ILOpCode.Ldsfld:
                case ILOpCode.Ldsflda:
                case ILOpCode.Ldstr:
                case ILOpCode.Ldtoken:
                case ILOpCode.Ldarg:
                case ILOpCode.Ldarg_0:
                case ILOpCode.Ldarg_1:
                case ILOpCode.Ldarg_2:
                case ILOpCode.Ldarg_3:
                case ILOpCode.Ldarg_s:
                case ILOpCode.Ldarga:
                case ILOpCode.Ldarga_s:
                case ILOpCode.Ldloc:
                case ILOpCode.Ldloc_0:
                case ILOpCode.Ldloc_1:
                case ILOpCode.Ldloc_2:
                case ILOpCode.Ldloc_3:
                case ILOpCode.Ldloc_s:
                case ILOpCode.Ldloca:
                case ILOpCode.Ldloca_s:
                case ILOpCode.Sizeof:
                    stackHeight += 1;
                    break;

                // Pop 1
                case ILOpCode.Add:
                case ILOpCode.Add_ovf:
                case ILOpCode.Add_ovf_un:
                case ILOpCode.And:
                case ILOpCode.Ceq:
                case ILOpCode.Cgt:
                case ILOpCode.Cgt_un:
                case ILOpCode.Clt:
                case ILOpCode.Clt_un:
                case ILOpCode.Div:
                case ILOpCode.Div_un:
                case ILOpCode.Initobj:
                case ILOpCode.Ldelem:
                case ILOpCode.Ldelem_i:
                case ILOpCode.Ldelem_i1:
                case ILOpCode.Ldelem_i2:
                case ILOpCode.Ldelem_i4:
                case ILOpCode.Ldelem_i8:
                case ILOpCode.Ldelem_r4:
                case ILOpCode.Ldelem_r8:
                case ILOpCode.Ldelem_ref:
                case ILOpCode.Ldelem_u1:
                case ILOpCode.Ldelem_u2:
                case ILOpCode.Ldelem_u4:
                case ILOpCode.Ldelema:
                case ILOpCode.Mkrefany:
                case ILOpCode.Mul:
                case ILOpCode.Mul_ovf:
                case ILOpCode.Mul_ovf_un:
                case ILOpCode.Or:
                case ILOpCode.Pop:
                case ILOpCode.Rem:
                case ILOpCode.Rem_un:
                case ILOpCode.Shl:
                case ILOpCode.Shr:
                case ILOpCode.Shr_un:
                case ILOpCode.Stsfld:
                case ILOpCode.Sub:
                case ILOpCode.Sub_ovf:
                case ILOpCode.Sub_ovf_un:
                case ILOpCode.Xor:
                case ILOpCode.Starg:
                case ILOpCode.Starg_s:
                case ILOpCode.Stloc:
                case ILOpCode.Stloc_0:
                case ILOpCode.Stloc_1:
                case ILOpCode.Stloc_2:
                case ILOpCode.Stloc_3:
                case ILOpCode.Stloc_s:
                case ILOpCode.Switch:
                    Debug.Assert(stackHeight > 0);
                    stackHeight -= 1;
                    break;

                case ILOpCode.Throw:
                    Debug.Assert(stackHeight > 0);
                    stackHeight = StackHeightNotSet;
                    break;

                // Branches
                case ILOpCode.Br:
                case ILOpCode.Leave:
                case ILOpCode.Brfalse:
                case ILOpCode.Brtrue:
                case ILOpCode.Beq:
                case ILOpCode.Bge:
                case ILOpCode.Bge_un:
                case ILOpCode.Bgt:
                case ILOpCode.Bgt_un:
                case ILOpCode.Ble:
                case ILOpCode.Ble_un:
                case ILOpCode.Blt:
                case ILOpCode.Blt_un:
                case ILOpCode.Bne_un:
                case ILOpCode.Br_s:
                case ILOpCode.Leave_s:
                case ILOpCode.Brfalse_s:
                case ILOpCode.Brtrue_s:
                case ILOpCode.Beq_s:
                case ILOpCode.Bge_s:
                case ILOpCode.Bge_un_s:
                case ILOpCode.Bgt_s:
                case ILOpCode.Bgt_un_s:
                case ILOpCode.Ble_s:
                case ILOpCode.Ble_un_s:
                case ILOpCode.Blt_s:
                case ILOpCode.Blt_un_s:
                case ILOpCode.Bne_un_s:
                    {
                        int target = ilReader.ReadBranchDestination(opcode);
                        hasReadOperand = true;

                        int adjustment;
                        bool isConditional;
                        bool isLeave = opcode is ILOpCode.Leave or ILOpCode.Leave_s;
                        if (opcode is ILOpCode.Br or ILOpCode.Br_s || isLeave)
                        {
                            isConditional = false;
                            adjustment = 0;
                        }
                        else if (opcode is ILOpCode.Brfalse or ILOpCode.Brfalse_s
                                 or ILOpCode.Brtrue_s or ILOpCode.Brtrue)
                        {
                            isConditional = true;
                            adjustment = 1;
                        }
                        else
                        {
                            isConditional = true;
                            adjustment = 2;
                        }

                        Debug.Assert(stackHeight >= adjustment);
                        stackHeight -= adjustment;

                        // leave/leave.s empties the evaluation stack (ECMA-335 III.3.42)
                        int targetHeight = isLeave ? 0 : stackHeight;

                        if ((uint)target < (uint)stackHeights.Length)
                        {
                            Debug.Assert(stackHeights[target] == StackHeightNotSet
                                || stackHeights[target] == targetHeight);

                            if (target > currentOffset)
                                stackHeights[target] = targetHeight;
                        }

                        if (!isConditional)
                            stackHeight = StackHeightNotSet;
                    }
                    break;

                // Calls — need method signature to determine stack delta
                case ILOpCode.Call:
                case ILOpCode.Calli:
                case ILOpCode.Callvirt:
                case ILOpCode.Newobj:
                    {
                        int token = ilReader.ReadILToken();
                        hasReadOperand = true;

                        int adjustment = ComputeCallStackDelta(context.Reader, token, opcode);
                        Debug.Assert(stackHeight >= adjustment);
                        stackHeight -= adjustment;
                    }
                    break;

                case ILOpCode.Ret:
                    {
                        if (context.HasReturnValue)
                            stackHeight -= 1;
                        Debug.Assert(stackHeight == 0);
                        stackHeight = StackHeightNotSet;
                    }
                    break;

                // Pop 2
                case ILOpCode.Cpobj:
                case ILOpCode.Stfld:
                case ILOpCode.Stind_i:
                case ILOpCode.Stind_i1:
                case ILOpCode.Stind_i2:
                case ILOpCode.Stind_i4:
                case ILOpCode.Stind_i8:
                case ILOpCode.Stind_r4:
                case ILOpCode.Stind_r8:
                case ILOpCode.Stind_ref:
                case ILOpCode.Stobj:
                    Debug.Assert(stackHeight > 1);
                    stackHeight -= 2;
                    break;

                // Pop 3
                case ILOpCode.Cpblk:
                case ILOpCode.Initblk:
                case ILOpCode.Stelem:
                case ILOpCode.Stelem_i:
                case ILOpCode.Stelem_i1:
                case ILOpCode.Stelem_i2:
                case ILOpCode.Stelem_i4:
                case ILOpCode.Stelem_i8:
                case ILOpCode.Stelem_r4:
                case ILOpCode.Stelem_r8:
                case ILOpCode.Stelem_ref:
                    Debug.Assert(stackHeight > 2);
                    stackHeight -= 3;
                    break;

                // No stack change
                case ILOpCode.Break:
                case ILOpCode.Constrained:
                case ILOpCode.Nop:
                case ILOpCode.Readonly:
                case ILOpCode.Tail:
                case ILOpCode.Unaligned:
                case ILOpCode.Volatile:
                    break;

                case ILOpCode.Endfilter:
                    Debug.Assert(stackHeight > 0);
                    stackHeight = StackHeightNotSet;
                    break;

                case ILOpCode.Jmp:
                case ILOpCode.Rethrow:
                case ILOpCode.Endfinally:
                    stackHeight = StackHeightNotSet;
                    break;

                // Stack-neutral (pop 1, push 1)
                case ILOpCode.Box:
                case ILOpCode.Castclass:
                case ILOpCode.Ckfinite:
                case ILOpCode.Conv_i:
                case ILOpCode.Conv_i1:
                case ILOpCode.Conv_i2:
                case ILOpCode.Conv_i4:
                case ILOpCode.Conv_i8:
                case ILOpCode.Conv_ovf_i:
                case ILOpCode.Conv_ovf_i_un:
                case ILOpCode.Conv_ovf_i1:
                case ILOpCode.Conv_ovf_i1_un:
                case ILOpCode.Conv_ovf_i2:
                case ILOpCode.Conv_ovf_i2_un:
                case ILOpCode.Conv_ovf_i4:
                case ILOpCode.Conv_ovf_i4_un:
                case ILOpCode.Conv_ovf_i8:
                case ILOpCode.Conv_ovf_i8_un:
                case ILOpCode.Conv_ovf_u:
                case ILOpCode.Conv_ovf_u_un:
                case ILOpCode.Conv_ovf_u1:
                case ILOpCode.Conv_ovf_u1_un:
                case ILOpCode.Conv_ovf_u2:
                case ILOpCode.Conv_ovf_u2_un:
                case ILOpCode.Conv_ovf_u4:
                case ILOpCode.Conv_ovf_u4_un:
                case ILOpCode.Conv_ovf_u8:
                case ILOpCode.Conv_ovf_u8_un:
                case ILOpCode.Conv_r_un:
                case ILOpCode.Conv_r4:
                case ILOpCode.Conv_r8:
                case ILOpCode.Conv_u:
                case ILOpCode.Conv_u1:
                case ILOpCode.Conv_u2:
                case ILOpCode.Conv_u4:
                case ILOpCode.Conv_u8:
                case ILOpCode.Isinst:
                case ILOpCode.Ldfld:
                case ILOpCode.Ldflda:
                case ILOpCode.Ldind_i:
                case ILOpCode.Ldind_i1:
                case ILOpCode.Ldind_i2:
                case ILOpCode.Ldind_i4:
                case ILOpCode.Ldind_i8:
                case ILOpCode.Ldind_r4:
                case ILOpCode.Ldind_r8:
                case ILOpCode.Ldind_ref:
                case ILOpCode.Ldind_u1:
                case ILOpCode.Ldind_u2:
                case ILOpCode.Ldind_u4:
                case ILOpCode.Ldlen:
                case ILOpCode.Ldobj:
                case ILOpCode.Ldvirtftn:
                case ILOpCode.Localloc:
                case ILOpCode.Neg:
                case ILOpCode.Newarr:
                case ILOpCode.Not:
                case ILOpCode.Refanytype:
                case ILOpCode.Refanyval:
                case ILOpCode.Unbox:
                case ILOpCode.Unbox_any:
                    Debug.Assert(stackHeight > 0);
                    break;

                default:
                    Debug.Fail($"Unknown opcode: {opcode}");
                    break;
            }

            if (!hasReadOperand)
            {
                if (!ilReader.TrySkip(opcode))
                    break;
            }

            if (stackHeight > 0)
                maxStack = Math.Max(maxStack, stackHeight);
        }

        return maxStack;
    }

    /// <summary>
    /// Computes the net stack adjustment for a call instruction by decoding the
    /// method signature from the metadata token.
    /// </summary>
    static int ComputeCallStackDelta(MetadataReader reader, int token, ILOpCode opcode)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            int paramCount;
            bool isStatic;
            bool hasReturn;

            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                    var methodDef = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                    var defSig = methodDef.DecodeSignature(Metadata.SignatureDecoder.Instance, genericContext: null);
                    paramCount = defSig.ParameterTypes.Length;
                    isStatic = methodDef.Attributes.HasFlag(System.Reflection.MethodAttributes.Static);
                    hasReturn = defSig.ReturnType is not ("System.Void" or "void");
                    break;

                case HandleKind.MemberReference:
                    var memberRef = reader.GetMemberReference((MemberReferenceHandle)handle);
                    var refSig = memberRef.DecodeMethodSignature(Metadata.SignatureDecoder.Instance, genericContext: null);
                    paramCount = refSig.ParameterTypes.Length;
                    isStatic = !refSig.Header.IsInstance;
                    hasReturn = refSig.ReturnType is not ("System.Void" or "void");
                    break;

                case HandleKind.MethodSpecification:
                    var spec = reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                    return ComputeCallStackDelta(reader, MetadataTokens.GetToken(spec.Method), opcode);

                default:
                    return 0;
            }

            int adjustment = paramCount;
            if (opcode == ILOpCode.Newobj)
            {
                adjustment--;
            }
            else
            {
                if (opcode == ILOpCode.Calli)
                    adjustment++;
                if (!isStatic)
                    adjustment++;
                if (hasReturn)
                    adjustment--;
            }

            return adjustment;
        }
        catch
        {
            return 0;
        }
    }
}
