using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

internal static class StackGuardFallbackAnalysis
{
    internal static bool IsFallbackAllocation(
        MethodBodyAnalysisContext context,
        int allocationOffset,
        IOptimizationOpportunityResolver resolver)
    {
        const int NoStackGuardCondition = 0;
        const int DirectResult = 1;
        const int DirectStored = 2;
        const int DirectLoaded = 3;
        const int ZeroAfterDirect = 4;
        const int InvertedResult = 5;
        const int InvertedStored = 6;
        const int InvertedLoaded = 7;

        try
        {
            int conditionState = NoStackGuardCondition;
            int conditionSlot = -1;
            foreach (var instruction in context.Instructions.Instructions)
            {
                if (instruction.Offset >= allocationOffset)
                    break;

                var opcode = instruction.OpCode;
                if (opcode is ILOpCode.Call or ILOpCode.Callvirt)
                {
                    int token = MethodInstructionFacts.OperandInt32(instruction);
                    var call = resolver.ResolveMember(token);
                    conditionState = call.Name == "TryEnterOnCurrentStack"
                        ? DirectResult
                        : NoStackGuardCondition;
                    conditionSlot = -1;
                    continue;
                }
                if (opcode == ILOpCode.Ldc_i4_0
                    && conditionState == DirectResult)
                {
                    conditionState = ZeroAfterDirect;
                    continue;
                }
                if (opcode == ILOpCode.Ceq
                    && conditionState == ZeroAfterDirect)
                {
                    conditionState = InvertedResult;
                    continue;
                }
                if (MethodInstructionFacts.TryReadLocalSlot(
                        instruction,
                        out var access))
                {
                    if (!access.IsArgument
                        && access.IsStore
                        && conditionState is DirectResult
                            or DirectLoaded
                            or InvertedResult
                            or InvertedLoaded)
                    {
                        conditionSlot = access.Slot;
                        conditionState = conditionState is DirectResult
                            or DirectLoaded
                                ? DirectStored
                                : InvertedStored;
                        continue;
                    }
                    if (!access.IsArgument
                        && !access.IsStore
                        && access.Slot == conditionSlot)
                    {
                        if (conditionState == DirectStored)
                        {
                            conditionState = DirectLoaded;
                            continue;
                        }
                        if (conditionState == InvertedStored)
                        {
                            conditionState = InvertedLoaded;
                            continue;
                        }
                    }

                    conditionState = NoStackGuardCondition;
                    conditionSlot = -1;
                    continue;
                }
                if (opcode is ILOpCode.Brtrue
                    or ILOpCode.Brtrue_s
                    or ILOpCode.Brfalse
                    or ILOpCode.Brfalse_s)
                {
                    if (MethodInstructionFacts.TrySingleBranchTarget(
                            instruction,
                            out int branchTarget)
                        && branchTarget > allocationOffset
                        && BranchSkipsFallback(opcode, conditionState))
                    {
                        return true;
                    }

                    conditionState = NoStackGuardCondition;
                    conditionSlot = -1;
                    continue;
                }
                if (opcode == ILOpCode.Nop)
                    continue;

                conditionState = NoStackGuardCondition;
                conditionSlot = -1;
            }

            return false;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException
                or IndexOutOfRangeException)
        {
            return false;
        }

        static bool BranchSkipsFallback(
            ILOpCode opcode,
            int conditionState)
            => opcode switch
            {
                ILOpCode.Brtrue or ILOpCode.Brtrue_s =>
                    conditionState is DirectResult or DirectLoaded,
                ILOpCode.Brfalse or ILOpCode.Brfalse_s =>
                    conditionState is InvertedResult or InvertedLoaded,
                _ => false,
            };
    }
}
