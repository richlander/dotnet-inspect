using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

internal static class MethodBodyFlowProbe
{
    internal static bool NewObjectFeedsThrowSoon(
        MethodInstructions body,
        int position)
    {
        var visitedOffsets = new HashSet<int>();
        int index = body.InstructionIndexAtOrAfter(position);
        for (int steps = 0;
            steps < 8 && index < body.Instructions.Length;
            steps++, index++)
        {
            var instruction = body.Instructions[index];
            if (!visitedOffsets.Add(instruction.Offset))
                return false;

            var operation = instruction.OpCode;
            if (operation is ILOpCode.Throw or ILOpCode.Rethrow)
                return true;
            if (operation is ILOpCode.Br or ILOpCode.Br_s)
            {
                if (!MethodInstructionFacts.TrySingleBranchTarget(
                        instruction,
                        out int target)
                    || target < 0
                    || target >= body.Instructions[^1].NextOffset)
                {
                    return false;
                }
                index = body.InstructionIndexAtOrAfter(target) - 1;
                continue;
            }
            if (IsControlFlowDivergent(operation))
                return false;
        }
        return false;
    }

    internal static bool BoxFeedsThrowSoon(
        MethodInstructions body,
        int position)
    {
        int index = body.InstructionIndexAtOrAfter(position);
        for (int steps = 0;
            steps < 6 && index < body.Instructions.Length;
            steps++, index++)
        {
            var operation = body.Instructions[index].OpCode;
            if (operation is ILOpCode.Throw or ILOpCode.Rethrow)
                return true;
            if (IsControlFlowDivergent(operation))
                return false;
        }
        return false;
    }

    static bool IsControlFlowDivergent(ILOpCode operation)
        => operation is
            ILOpCode.Br
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
            or ILOpCode.Switch
            or ILOpCode.Ret
            or ILOpCode.Leave
            or ILOpCode.Leave_s
            or ILOpCode.Endfinally
            or ILOpCode.Endfilter;
}
