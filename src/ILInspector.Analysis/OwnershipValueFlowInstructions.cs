using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

static class OwnershipValueFlowInstructions
{
    internal static bool TryFindNextNonNop(
        ImmutableArray<DecodedInstruction> instructions,
        int offset,
        out DecodedInstruction instruction)
    {
        foreach (DecodedInstruction candidate in instructions)
        {
            if (candidate.Offset < offset
                || candidate.OpCode == ILOpCode.Nop)
            {
                continue;
            }
            instruction = candidate;
            return true;
        }
        instruction = default!;
        return false;
    }

    internal static bool TryFindInstruction(
        ImmutableArray<DecodedInstruction> instructions,
        int offset,
        out int index,
        out DecodedInstruction instruction)
    {
        for (int i = 0; i < instructions.Length; i++)
        {
            if (instructions[i].Offset != offset)
                continue;
            index = i;
            instruction = instructions[i];
            return true;
        }
        index = -1;
        instruction = default!;
        return false;
    }

    internal static bool TryReadStoreLocal(
        DecodedInstruction instruction,
        out int slot)
    {
        slot = instruction.OpCode switch
        {
            ILOpCode.Stloc_0 => 0,
            ILOpCode.Stloc_1 => 1,
            ILOpCode.Stloc_2 => 2,
            ILOpCode.Stloc_3 => 3,
            ILOpCode.Stloc_s or ILOpCode.Stloc =>
                checked((int)instruction.OperandValue),
            _ => -1,
        };
        return slot >= 0;
    }

    internal static bool IsLoadLocalOrAddress(
        DecodedInstruction instruction,
        int slot) =>
        IsLoadLocal(instruction, slot)
        || (TryReadLoadLocalAddress(
                instruction,
                out int addressSlot)
            && addressSlot == slot);

    internal static bool IsSimpleArgumentPush(ILOpCode opcode) =>
        opcode is ILOpCode.Ldc_i4_m1
            or ILOpCode.Ldc_i4_0
            or ILOpCode.Ldc_i4_1
            or ILOpCode.Ldc_i4_2
            or ILOpCode.Ldc_i4_3
            or ILOpCode.Ldc_i4_4
            or ILOpCode.Ldc_i4_5
            or ILOpCode.Ldc_i4_6
            or ILOpCode.Ldc_i4_7
            or ILOpCode.Ldc_i4_8
            or ILOpCode.Ldc_i4_s
            or ILOpCode.Ldc_i4
            or ILOpCode.Ldarg_0
            or ILOpCode.Ldarg_1
            or ILOpCode.Ldarg_2
            or ILOpCode.Ldarg_3
            or ILOpCode.Ldarg_s
            or ILOpCode.Ldarg
            or ILOpCode.Ldloc_0
            or ILOpCode.Ldloc_1
            or ILOpCode.Ldloc_2
            or ILOpCode.Ldloc_3
            or ILOpCode.Ldloc_s
            or ILOpCode.Ldloc
            or ILOpCode.Ldnull;

    static bool IsLoadLocal(
        DecodedInstruction instruction,
        int slot) =>
        instruction.OpCode switch
        {
            ILOpCode.Ldloc_0 => slot == 0,
            ILOpCode.Ldloc_1 => slot == 1,
            ILOpCode.Ldloc_2 => slot == 2,
            ILOpCode.Ldloc_3 => slot == 3,
            ILOpCode.Ldloc_s or ILOpCode.Ldloc =>
                instruction.OperandValue == slot,
            _ => false,
        };

    internal static bool TryReadLoadLocalAddress(
        DecodedInstruction instruction,
        out int slot)
    {
        slot = instruction.OpCode switch
        {
            ILOpCode.Ldloca_s or ILOpCode.Ldloca =>
                checked((int)instruction.OperandValue),
            _ => -1,
        };
        return slot >= 0;
    }
}
