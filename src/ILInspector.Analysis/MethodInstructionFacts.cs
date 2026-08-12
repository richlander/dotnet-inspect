using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

internal readonly record struct LocalSlotAccess(
    int Slot,
    bool IsArgument,
    bool IsStore);

internal static class MethodInstructionFacts
{
    internal static bool TryReadLocalSlot(
        DecodedInstruction instruction,
        out LocalSlotAccess access)
    {
        access = default;
        switch (instruction.OpCode)
        {
            case ILOpCode.Ldloc_0:
                access = new(0, false, false);
                return true;
            case ILOpCode.Ldloc_1:
                access = new(1, false, false);
                return true;
            case ILOpCode.Ldloc_2:
                access = new(2, false, false);
                return true;
            case ILOpCode.Ldloc_3:
                access = new(3, false, false);
                return true;
            case ILOpCode.Ldloc_s:
            case ILOpCode.Ldloc:
                access = new(OperandInt32(instruction), false, false);
                return true;
            case ILOpCode.Stloc_0:
                access = new(0, false, true);
                return true;
            case ILOpCode.Stloc_1:
                access = new(1, false, true);
                return true;
            case ILOpCode.Stloc_2:
                access = new(2, false, true);
                return true;
            case ILOpCode.Stloc_3:
                access = new(3, false, true);
                return true;
            case ILOpCode.Stloc_s:
            case ILOpCode.Stloc:
                access = new(OperandInt32(instruction), false, true);
                return true;
            case ILOpCode.Ldarg_0:
                access = new(0, true, false);
                return true;
            case ILOpCode.Ldarg_1:
                access = new(1, true, false);
                return true;
            case ILOpCode.Ldarg_2:
                access = new(2, true, false);
                return true;
            case ILOpCode.Ldarg_3:
                access = new(3, true, false);
                return true;
            case ILOpCode.Ldarg_s:
            case ILOpCode.Ldarg:
                access = new(OperandInt32(instruction), true, false);
                return true;
            case ILOpCode.Starg_s:
            case ILOpCode.Starg:
                access = new(OperandInt32(instruction), true, true);
                return true;
            default:
                return false;
        }
    }

    static int OperandInt32(DecodedInstruction instruction)
        => checked((int)instruction.OperandValue);
}
