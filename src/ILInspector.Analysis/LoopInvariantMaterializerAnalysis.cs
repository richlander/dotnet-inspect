using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

internal static class LoopInvariantMaterializerAnalysis
{
    internal static bool TryGetLoopInvariantSource(
        MethodBodyAnalysisContext context,
        ReachingDefinitionsResult reachingDefinitions,
        int callOffset,
        out string evidence)
    {
        evidence = "";
        if (!TryGetContainingLoop(
                callOffset,
                context.LoopRegions,
                out var loop)
            || !reachingDefinitions.IsComplete
            || !TryFindPreviousInstruction(
                context,
                callOffset,
                out var loadInstruction)
            || !MethodInstructionFacts.TryReadLocalSlot(
                loadInstruction,
                out var access)
            || access.IsStore)
        {
            return false;
        }

        var use = reachingDefinitions.Uses.FirstOrDefault(candidate =>
            candidate.Offset == loadInstruction.Offset
            && candidate.IsArgument == access.IsArgument
            && candidate.Slot == access.Slot);
        if (use is null
            || use.Address
            || use.ReachingDefinitions.Length == 0
            || reachingDefinitions.Uses.Any(candidate =>
                candidate.Address
                && candidate.IsArgument == access.IsArgument
                && candidate.Slot == access.Slot
                && candidate.Offset >= loop.Start
                && candidate.Offset <= loop.End)
            || use.ReachingDefinitions.Any(definition =>
                definition.Offset >= loop.Start
                && definition.Offset <= loop.End))
        {
            return false;
        }

        evidence = access.IsArgument
            ? $"arg{access.Slot}"
            : $"V_{access.Slot}";
        return true;
    }

    static bool TryGetContainingLoop(
        int offset,
        IReadOnlyList<(int Start, int End)> loopRegions,
        out (int Start, int End) loop)
    {
        loop = default;
        var found = false;
        foreach (var region in loopRegions)
        {
            if (offset < region.Start || offset > region.End)
                continue;
            if (!found || region.End - region.Start < loop.End - loop.Start)
                loop = region;
            found = true;
        }

        return found;
    }

    static bool TryFindPreviousInstruction(
        MethodBodyAnalysisContext context,
        int targetOffset,
        out DecodedInstruction previousInstruction)
    {
        previousInstruction = default!;
        foreach (var instruction in context.Instructions.Instructions)
        {
            if (instruction.Offset >= targetOffset)
                break;
            if (instruction.OpCode == ILOpCode.Nop)
                continue;
            previousInstruction = instruction;
        }

        return previousInstruction is not null;
    }
}
