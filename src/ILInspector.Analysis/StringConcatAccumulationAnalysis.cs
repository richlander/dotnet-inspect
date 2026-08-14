using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

internal static class StringConcatAccumulationAnalysis
{
    const int ArgumentSlotBias = 1 << 20;

    // Track whether the stored accumulator reaches a Concat argument with an
    // abstract boolean stack over the argument-producing block.
    internal static bool AccumulatesIntoSource(
        MethodBodyAnalysisContext context,
        int concatOffset,
        int storeOffset,
        int concatArgumentCount,
        IOptimizationOpportunityResolver resolver)
    {
        try
        {
            if (concatOffset < 0
                || concatArgumentCount <= 0
                || context.InstructionAt(storeOffset) is not { } storeInstruction
                || !MethodInstructionFacts.TryReadLocalSlot(
                    storeInstruction,
                    out var storeAccess)
                || !storeAccess.IsStore)
            {
                return false;
            }

            int storeKey = SlotKey(storeAccess);
            int blockStart = 0;
            foreach (var instruction in context.Instructions.Instructions)
            {
                if (instruction.Offset >= concatOffset)
                    break;
                bool isLocal = MethodInstructionFacts.TryReadLocalSlot(
                    instruction,
                    out var access);
                if (instruction.NextOffset <= concatOffset
                    && ((isLocal && access.IsStore)
                        || EndsArgumentBlock(instruction.OpCode)))
                {
                    blockStart = instruction.NextOffset;
                }
            }

            var stack = new List<bool>();
            for (int index = context.IndexAtOrAfter(blockStart);
                index < context.Instructions.Instructions.Length;
                index++)
            {
                var instruction = context.Instructions.Instructions[index];
                if (instruction.Offset >= concatOffset)
                    break;
                if (MethodInstructionFacts.TryReadLocalSlot(
                        instruction,
                        out var access))
                {
                    // A store starts a new block; model desynchronization is
                    // not evidence of accumulation.
                    if (access.IsStore)
                        return false;
                    stack.Add(SlotKey(access) == storeKey);
                    continue;
                }
                // Unmodeled opcodes and stack underflow are not evidence.
                if (!ApplyStackEffect(instruction, stack, resolver))
                    return false;
            }

            if (stack.Count < concatArgumentCount)
                return false;
            for (int index = stack.Count - concatArgumentCount;
                index < stack.Count;
                index++)
            {
                if (stack[index])
                    return true;
            }

            return false;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException)
        {
            return false;
        }
    }

    static int SlotKey(LocalSlotAccess access)
        => (access.IsArgument ? ArgumentSlotBias : 0) | access.Slot;

    static bool ApplyStackEffect(
        DecodedInstruction instruction,
        List<bool> stack,
        IOptimizationOpportunityResolver resolver)
    {
        switch (instruction.OpCode)
        {
            case ILOpCode.Nop:
                return true;
            case ILOpCode.Ldc_i4_m1
                or ILOpCode.Ldc_i4_0
                or ILOpCode.Ldc_i4_1
                or ILOpCode.Ldc_i4_2
                or ILOpCode.Ldc_i4_3
                or ILOpCode.Ldc_i4_4
                or ILOpCode.Ldc_i4_5
                or ILOpCode.Ldc_i4_6
                or ILOpCode.Ldc_i4_7
                or ILOpCode.Ldc_i4_8
                or ILOpCode.Ldnull
                or ILOpCode.Ldc_i4_s
                or ILOpCode.Ldc_i4
                or ILOpCode.Ldc_r4
                or ILOpCode.Ldstr
                or ILOpCode.Ldsfld
                or ILOpCode.Ldsflda
                or ILOpCode.Ldtoken
                or ILOpCode.Ldftn
                or ILOpCode.Sizeof
                or ILOpCode.Ldc_i8
                or ILOpCode.Ldc_r8
                or ILOpCode.Ldloca_s
                or ILOpCode.Ldarga_s
                or ILOpCode.Ldloca
                or ILOpCode.Ldarga:
                stack.Add(false);
                return true;
            case ILOpCode.Conv_i1
                or ILOpCode.Conv_i2
                or ILOpCode.Conv_i4
                or ILOpCode.Conv_i8
                or ILOpCode.Conv_r4
                or ILOpCode.Conv_r8
                or ILOpCode.Conv_u4
                or ILOpCode.Conv_u8
                or ILOpCode.Conv_u2
                or ILOpCode.Conv_u1
                or ILOpCode.Conv_i
                or ILOpCode.Conv_u
                or ILOpCode.Conv_r_un
                or ILOpCode.Neg
                or ILOpCode.Not
                or ILOpCode.Ldlen
                or ILOpCode.Ldind_i1
                or ILOpCode.Ldind_u1
                or ILOpCode.Ldind_i2
                or ILOpCode.Ldind_u2
                or ILOpCode.Ldind_i4
                or ILOpCode.Ldind_u4
                or ILOpCode.Ldind_i8
                or ILOpCode.Ldind_i
                or ILOpCode.Ldind_r4
                or ILOpCode.Ldind_r8
                or ILOpCode.Ldind_ref
                or ILOpCode.Ldfld
                or ILOpCode.Ldflda
                or ILOpCode.Ldobj
                or ILOpCode.Castclass
                or ILOpCode.Isinst
                or ILOpCode.Unbox
                or ILOpCode.Unbox_any
                or ILOpCode.Box:
                return Pop(stack, 1) && Push(stack);
            case ILOpCode.Add
                or ILOpCode.Sub
                or ILOpCode.Mul
                or ILOpCode.Div
                or ILOpCode.Div_un
                or ILOpCode.Rem
                or ILOpCode.Rem_un
                or ILOpCode.And
                or ILOpCode.Or
                or ILOpCode.Xor
                or ILOpCode.Shl
                or ILOpCode.Shr
                or ILOpCode.Shr_un
                or ILOpCode.Ceq
                or ILOpCode.Cgt
                or ILOpCode.Cgt_un
                or ILOpCode.Clt
                or ILOpCode.Clt_un
                or ILOpCode.Ldelem_i1
                or ILOpCode.Ldelem_u1
                or ILOpCode.Ldelem_i2
                or ILOpCode.Ldelem_u2
                or ILOpCode.Ldelem_i4
                or ILOpCode.Ldelem_u4
                or ILOpCode.Ldelem_i8
                or ILOpCode.Ldelem_i
                or ILOpCode.Ldelem_r4
                or ILOpCode.Ldelem_r8
                or ILOpCode.Ldelem_ref
                or ILOpCode.Ldelem
                or ILOpCode.Ldelema:
                return Pop(stack, 2) && Push(stack);
            case ILOpCode.Dup:
                if (stack.Count == 0)
                    return false;
                stack.Add(stack[^1]);
                return true;
            case ILOpCode.Pop:
                return Pop(stack, 1);
            case ILOpCode.Call
                or ILOpCode.Callvirt
                or ILOpCode.Newobj:
            {
                int token = MethodInstructionFacts.OperandInt32(instruction);
                var callee = resolver.ResolveMember(token);
                if (callee.Kind == MemberKind.Unsupported)
                    return false;
                int pops = callee.ParameterTypes.Length
                    + (instruction.OpCode != ILOpCode.Newobj && callee.HasThis
                        ? 1
                        : 0);
                if (!Pop(stack, pops))
                    return false;
                if (instruction.OpCode == ILOpCode.Newobj
                    || callee.ReturnType.Name != "Void")
                {
                    stack.Add(false);
                }
                return true;
            }
            default:
                // An unmodeled opcode cannot establish accumulation.
                return false;
        }
    }

    static bool Pop(List<bool> stack, int count)
    {
        if (stack.Count < count)
            return false;
        stack.RemoveRange(stack.Count - count, count);
        return true;
    }

    static bool Push(List<bool> stack)
    {
        stack.Add(false);
        return true;
    }

    static bool EndsArgumentBlock(ILOpCode opcode)
        => opcode is ILOpCode.Stfld
            or ILOpCode.Stsfld
            or ILOpCode.Stobj
            or ILOpCode.Stelem
            or ILOpCode.Stelem_i
            or ILOpCode.Stelem_i1
            or ILOpCode.Stelem_i2
            or ILOpCode.Stelem_i4
            or ILOpCode.Stelem_i8
            or ILOpCode.Stelem_r4
            or ILOpCode.Stelem_r8
            or ILOpCode.Stelem_ref
            or ILOpCode.Stind_i
            or ILOpCode.Stind_i1
            or ILOpCode.Stind_i2
            or ILOpCode.Stind_i4
            or ILOpCode.Stind_i8
            or ILOpCode.Stind_r4
            or ILOpCode.Stind_r8
            or ILOpCode.Stind_ref
            or ILOpCode.Ret
            or ILOpCode.Throw
            or ILOpCode.Rethrow
            or ILOpCode.Leave
            or ILOpCode.Leave_s
            or ILOpCode.Br
            or ILOpCode.Br_s
            or ILOpCode.Brtrue
            or ILOpCode.Brtrue_s
            or ILOpCode.Brfalse
            or ILOpCode.Brfalse_s
            or ILOpCode.Beq
            or ILOpCode.Beq_s
            or ILOpCode.Bne_un
            or ILOpCode.Bne_un_s
            or ILOpCode.Bge
            or ILOpCode.Bge_s
            or ILOpCode.Bgt
            or ILOpCode.Bgt_s
            or ILOpCode.Ble
            or ILOpCode.Ble_s
            or ILOpCode.Blt
            or ILOpCode.Blt_s
            or ILOpCode.Bge_un
            or ILOpCode.Bge_un_s
            or ILOpCode.Bgt_un
            or ILOpCode.Bgt_un_s
            or ILOpCode.Ble_un
            or ILOpCode.Ble_un_s
            or ILOpCode.Blt_un
            or ILOpCode.Blt_un_s
            or ILOpCode.Switch;
}
