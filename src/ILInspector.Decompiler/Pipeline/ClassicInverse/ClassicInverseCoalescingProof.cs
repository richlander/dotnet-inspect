namespace ILInspector.Decompiler.Pipeline;

internal sealed partial class ClassicInverseLoweringProof
{
    sealed record CoalescedContinuation(
        Block Continuation,
        Block Merge,
        Call GetResult,
        Coalesce Expression);

    sealed class CoalescingBindings
    {
        internal Dictionary<IrNode, ConditionalBranch> TestsByJoin { get; } =
            new(ReferenceEqualityComparer.Instance);
        internal Dictionary<IrNode, Coalesce> ValuesByTest { get; } =
            new(ReferenceEqualityComparer.Instance);
    }

    internal ConditionalBranch? CoalescingTestForJoin(IrNode node)
        => _coalescing.TestsByJoin.GetValueOrDefault(node);

    internal bool ProvesCoalescingValue(IrNode raw, IrNode planning)
        => _coalescing.ValuesByTest.TryGetValue(raw, out Coalesce? expression)
            && ReferenceEquals(expression, planning);

    static CoalescedContinuation? TryFindCoalescedResult(
        BodyIndex index,
        Block continuation,
        int awaiter,
        TypeRef awaiterType,
        ClassicInverseBudget budget)
    {
        if (!budget.Charge()
            || continuation.Children.Count != 0
            || index.SuccessorsOf(continuation) is not [Block merge]
            || !index.HasOnlySuccessors(continuation, merge)
            || !index.HasOnlyPredecessors(merge, continuation)
            || index.GetResultsIn(merge) is not [Call result]
            || !IsAwaiterGetResult(result, awaiter, awaiterType))
        {
            return null;
        }

        for (IrNode current = result; current.Parent is IrExpression parent; current = parent)
        {
            if (!budget.Charge())
                return null;
            if (parent is Coalesce coalesce)
            {
                return ReferenceEquals(coalesce.Left, current)
                    ? new(continuation, merge, result, coalesce)
                    : null;
            }
        }
        return null;
    }

    /// <summary>
    /// The empty planning continuation may precede a coalesce only when the raw
    /// continuation evaluates its left value once, branches on that very value,
    /// and carries either it or the null-only fallback into the same sole use.
    /// Pairing the two operands and the surrounding use keeps all work on its
    /// original side of the null test; a matching GetResult offset alone cannot.
    /// </summary>
    static bool ProveCoalescingCorrespondence(
        BodyIndex planning,
        BodyIndex raw,
        Dictionary<IrNode, string> roles,
        ClassicInverseBudget budget,
        out CoalescingBindings bindings)
    {
        bindings = new();
        foreach (CoalescedContinuation moved in planning.CoalescedContinuations)
        {
            if (!budget.Charge()
                || raw.BlocksStartingAt(moved.Continuation.StartOffset) is not [Block head]
                || raw.BlocksStartingAt(moved.Merge.StartOffset) is not [Block merge]
                || head.Children is not
                [
                    StoreStackSlot first,
                    StoreStackSlot { Value: LoadStackSlot carried } carry,
                    ConditionalBranch { Condition: LoadStackSlot tested } test,
                ]
                || first.Slot == carry.Slot
                || carried.Slot != first.Slot
                || tested.Slot != first.Slot
                || first.Value.ResultType is not { } leftType
                || !(TypeFamilies.Of(leftType) == StackFamily.O
                    || leftType.DeclaredValueTypeHint == ValueTypeHint.ReferenceType)
                || !Equals(carried.Type, leftType)
                || !Equals(tested.Type, leftType)
                || test.TargetOffset != merge.StartOffset
                || test.SourceOffset < 0
                || test.SourceOffset != moved.Expression.SourceOffset
                || raw.SuccessorsOf(head).Count != 2)
            {
                return false;
            }

            Block fallbackBlock = raw.SuccessorsOf(head)[0];
            if (ReferenceEquals(fallbackBlock, merge))
                fallbackBlock = raw.SuccessorsOf(head)[1];
            if (!raw.HasOnlySuccessors(head, merge, fallbackBlock)
                || !raw.HasOnlyPredecessors(fallbackBlock, head)
                || !raw.HasOnlySuccessors(fallbackBlock, merge)
                || !raw.HasOnlyPredecessors(merge, head, fallbackBlock)
                || fallbackBlock.Children is not [StoreStackSlot fallback]
                || fallback.Slot != carry.Slot
                || raw.SlotStoresFor(first.Slot) is not [StoreStackSlot soleFirst]
                || !ReferenceEquals(first, soleFirst)
                || raw.SlotLoadsFor(first.Slot).Count != 2
                || !raw.SlotLoadsFor(first.Slot).Contains(carried)
                || !raw.SlotLoadsFor(first.Slot).Contains(tested)
                || raw.SlotStoresFor(carry.Slot).Count != 2
                || !raw.SlotStoresFor(carry.Slot).Contains(carry)
                || !raw.SlotStoresFor(carry.Slot).Contains(fallback)
                || raw.SlotLoadsFor(carry.Slot) is not [LoadStackSlot joined]
                || !Equals(joined.Type, moved.Expression.ResultType)
                || merge.Children is not [StoreLocal result, Leave]
                || moved.Merge.Children is not [StoreLocal plannedResult]
                || result.Index != plannedResult.Index
                || !Equals(result.Type, plannedResult.Type)
                || result.SourceOffset != plannedResult.SourceOffset
                || !SameCoalescingExpression(first.Value, moved.Expression.Left, budget)
                || !SameCoalescingExpression(fallback.Value, moved.Expression.Right, budget)
                || !SameCoalescingExpression(result.Value, plannedResult.Value, budget,
                    joined, moved.Expression)
                || !budget.Charge()
                || !bindings.TestsByJoin.TryAdd(joined, test)
                || !bindings.ValuesByTest.TryAdd(test, moved.Expression))
            {
                return false;
            }

            roles[first] = CoalesceStore;
            roles[carry] = CoalesceStore;
            roles[fallback] = CoalesceStore;
            roles[carried] = CoalesceRead;
            roles[tested] = CoalesceRead;
            roles[joined] = CoalesceRead;
        }
        return true;
    }

    static bool SameCoalescingExpression(
        IrNode raw,
        IrNode planning,
        ClassicInverseBudget budget,
        LoadStackSlot? joined = null,
        Coalesce? replacement = null)
        => ClassicInverseExpressionRules.SameTree(raw, planning, budget, joined, replacement);
}
