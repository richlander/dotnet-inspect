using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

internal static class ArrayEscapeAnalysis
{
    // Promote an array only when reaching definitions show local element/length use.
    internal static bool ArrayProvablyStaysLocal(
        MethodBodyAnalysisContext context,
        ReachingDefinitionsResult reachingDefinitions,
        int positionAfterNewarr)
    {
        try
        {
            if (!TryReadStoreLocalDefinition(
                    context,
                    positionAfterNewarr,
                    out int slot,
                    out int storeOffset)
                || !reachingDefinitions.IsComplete)
            {
                return false;
            }

            var definition = reachingDefinitions.Definitions.FirstOrDefault(candidate =>
                !candidate.IsArgument
                && candidate.Slot == slot
                && candidate.Offset == storeOffset);
            if (definition is null)
                return false;

            foreach (var use in reachingDefinitions.UsesOf(definition))
            {
                if (use.Address
                    || !TryPositionAfterLoadLocal(
                        context,
                        use.Offset,
                        slot,
                        out int positionAfterLoad)
                    || ArrayLoadEscapes(context, positionAfterLoad))
                {
                    return false;
                }
            }

            return true;
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

    // Keep the copy row only when the materialized array is consumed locally.
    internal static bool SpanToArrayResultEscapes(
        MethodBodyAnalysisContext context,
        ReachingDefinitionsResult reachingDefinitions,
        int positionAfterCall)
    {
        try
        {
            if (!reachingDefinitions.IsComplete)
                return true;

            int firstUseIndex = context.NextNonNopIndexAtOrAfter(positionAfterCall);
            positionAfterCall = firstUseIndex < context.Instructions.Instructions.Length
                ? context.Instructions.Instructions[firstUseIndex].Offset
                : positionAfterCall;
            if (TryReadStoreLocalDefinition(
                    context,
                    positionAfterCall,
                    out int slot,
                    out int storeOffset))
            {
                var definition = reachingDefinitions.Definitions.FirstOrDefault(candidate =>
                    !candidate.IsArgument
                    && candidate.Slot == slot
                    && candidate.Offset == storeOffset);
                if (definition is null)
                    return true;

                foreach (var use in reachingDefinitions.UsesOf(definition))
                {
                    if (use.Address
                        || !TryPositionAfterLoadLocal(
                            context,
                            use.Offset,
                            slot,
                            out int positionAfterLoad)
                        || ArrayLoadEscapes(context, positionAfterLoad))
                    {
                        return true;
                    }
                }

                return false;
            }

            return ArrayLoadEscapes(context, positionAfterCall);
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException
                or IndexOutOfRangeException)
        {
            return true;
        }
    }

    static bool TryReadStoreLocalDefinition(
        MethodBodyAnalysisContext context,
        int position,
        out int slot,
        out int storeOffset)
    {
        slot = -1;
        storeOffset = position;
        if (context.InstructionAt(position) is not { } instruction
            || !MethodInstructionFacts.TryReadLocalSlot(
                instruction,
                out var access)
            || !access.IsStore
            || access.IsArgument)
        {
            return false;
        }

        slot = access.Slot;
        storeOffset = instruction.Offset;
        return true;
    }

    static bool TryPositionAfterLoadLocal(
        MethodBodyAnalysisContext context,
        int offset,
        int slot,
        out int positionAfterLoad)
    {
        positionAfterLoad = offset;
        if (context.InstructionAt(offset) is not { } instruction
            || !MethodInstructionFacts.TryReadLocalSlot(
                instruction,
                out var access)
            || access.IsStore
            || access.IsArgument
            || access.Slot != slot)
        {
            return false;
        }

        positionAfterLoad = instruction.NextOffset;
        return true;
    }

    // Track how many stack slots are pushed above the array until its first
    // consumer; only an element access or length read at the expected depth is
    // treated as local use.
    static bool ArrayLoadEscapes(
        MethodBodyAnalysisContext context,
        int position)
    {
        int extra = 0;
        for (int index = context.IndexAtOrAfter(position);
            index < context.Instructions.Instructions.Length;
            index++)
        {
            var opcode = context.Instructions.Instructions[index].OpCode;
            switch (opcode)
            {
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
                    or ILOpCode.Ldc_i8
                    or ILOpCode.Ldc_r8
                    or ILOpCode.Ldloc_0
                    or ILOpCode.Ldloc_1
                    or ILOpCode.Ldloc_2
                    or ILOpCode.Ldloc_3
                    or ILOpCode.Ldarg_0
                    or ILOpCode.Ldarg_1
                    or ILOpCode.Ldarg_2
                    or ILOpCode.Ldarg_3
                    or ILOpCode.Ldloc_s
                    or ILOpCode.Ldloca_s
                    or ILOpCode.Ldarg_s
                    or ILOpCode.Ldarga_s:
                    extra++;
                    break;
                case ILOpCode.Ldlen:
                    return extra != 0;
                case ILOpCode.Ldelem
                    or ILOpCode.Ldelem_i
                    or ILOpCode.Ldelem_i1
                    or ILOpCode.Ldelem_i2
                    or ILOpCode.Ldelem_i4
                    or ILOpCode.Ldelem_i8
                    or ILOpCode.Ldelem_r4
                    or ILOpCode.Ldelem_r8
                    or ILOpCode.Ldelem_u1
                    or ILOpCode.Ldelem_u2
                    or ILOpCode.Ldelem_u4
                    or ILOpCode.Ldelem_ref:
                    return extra != 1;
                case ILOpCode.Stelem
                    or ILOpCode.Stelem_i
                    or ILOpCode.Stelem_i1
                    or ILOpCode.Stelem_i2
                    or ILOpCode.Stelem_i4
                    or ILOpCode.Stelem_i8
                    or ILOpCode.Stelem_r4
                    or ILOpCode.Stelem_r8
                    or ILOpCode.Stelem_ref:
                    return extra != 2;
                default:
                    // Calls, returns, stores, aliases, and ambiguous stack
                    // shapes are all treated as escapes.
                    return true;
            }
        }

        return true;
    }
}
