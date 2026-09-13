namespace ILInspector.Decompiler.Pipeline;

internal static class ClassicInverseAwaitRules
{
    internal static bool RequiresUnsafe(
        IrNode node, IrFunction kickoff, ClassicInverseBudget budget, bool onlyWithAwait = false)
    {
        // Reserve both this admission walk and the existing classifier's walk;
        // the statement form additionally checks for surviving await syntax.
        foreach (IrNode _ in node.DescendantsAndSelfOutsideNestedFunctions)
        {
            if (!budget.Charge() || !budget.Charge() || onlyWithAwait && !budget.Charge())
                return false;
        }
        return onlyWithAwait
            ? UnsafeAwaitOperand.WouldPlaceAwaitInUnsafeContext(
                node, kickoff.UsesUpdatedMemorySafetyRules, kickoff.SkipLocalsInit)
            : UnsafeAwaitOperand.RequiresUnsafeContext(
                node, kickoff.UsesUpdatedMemorySafetyRules, kickoff.SkipLocalsInit);
    }

    internal static bool MembersRequireUnsafe(
        IEnumerable<MethodRef> members, IrFunction kickoff, ClassicInverseBudget budget)
    {
        foreach (MethodRef member in members)
        {
            if (!budget.Charge())
                return false;
            if (UnsafeAwaitOperand.MethodRequiresUnsafe(member, kickoff.UsesUpdatedMemorySafetyRules))
                return true;
        }
        return false;
    }
}
