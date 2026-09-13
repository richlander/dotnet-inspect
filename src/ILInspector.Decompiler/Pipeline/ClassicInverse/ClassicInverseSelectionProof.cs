using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal sealed partial class ClassicInverseLoweringProof
{
    sealed record SelectionContinuation(
        Block Continuation,
        Block Merge,
        Call GetResult,
        IrExpression Expression,
        ImmutableArray<Block> Path,
        bool IsOperand = false);

    sealed class SelectionBindings
    {
        internal Dictionary<IrNode, IrNode> TestsByJoin { get; } =
            new(ReferenceEqualityComparer.Instance);
        internal Dictionary<IrNode, IrExpression> ValuesByTest { get; } =
            new(ReferenceEqualityComparer.Instance);
        internal Dictionary<IrNode, IrNode[]> EvaluationChildren { get; } =
            new(ReferenceEqualityComparer.Instance);
        internal Dictionary<IrNode, TypeRef> SelectedTypes { get; } =
            new(ReferenceEqualityComparer.Instance);
        internal HashSet<IrNode> PredicateStructures { get; } =
            new(ReferenceEqualityComparer.Instance);
        internal Dictionary<int, ImmutableArray<int>> PredicateOrigins { get; } = [];
        internal HashSet<IrNode> SwitchConsumedValues { get; } = new(ReferenceEqualityComparer.Instance);
        internal HashSet<Call> SwitchResults { get; } = new(ReferenceEqualityComparer.Instance);

        internal bool Swap(IrNode parent, int left, int right, ClassicInverseBudget budget)
        {
            if (!EvaluationChildren.TryGetValue(parent, out IrNode[]? children))
            {
                children = new IrNode[parent.Children.Count];
                for (int i = 0; i < children.Length; i++)
                {
                    if (!budget.Charge())
                        return false;
                    children[i] = parent.Children[i];
                }
                EvaluationChildren.Add(parent, children);
            }
            if (!budget.Charge()
                || !ReferenceEquals(children[left], parent.Children[left])
                || !ReferenceEquals(children[right], parent.Children[right]))
                return false;
            (children[left], children[right]) = (children[right], children[left]);
            return true;
        }
    }

    internal IrNode? SelectionTestForJoin(IrNode node)
        => _selections.TestsByJoin.GetValueOrDefault(node);

    internal bool ProvesSelectionValue(IrNode raw, IrNode planning)
        => _selections.ValuesByTest.TryGetValue(raw, out IrExpression? expression)
            && ReferenceEquals(expression, planning);

    internal IReadOnlyList<IrNode> RawEvaluationChildren(IrNode node)
        => _selections.EvaluationChildren.TryGetValue(node, out var children)
            ? children : node.Children;

    internal TypeRef? SelectedValueType(IrNode node)
        => _selections.SelectedTypes.GetValueOrDefault(node);

    internal bool ProvesPredicateStructure(IrNode node)
        => _selections.PredicateStructures.Contains(node);

    internal bool IsSwitchResult(Call result) => _selections.SwitchResults.Contains(result);
    internal bool IsConsumedSwitchValue(IrNode node) => _selections.SwitchConsumedValues.Contains(node);

    internal IReadOnlyDictionary<int, ImmutableArray<int>> PredicateOrigins
        => _selections.PredicateOrigins;

    static SelectionContinuation? TryFindSelectionResult(
        BodyIndex index,
        Block continuation,
        int awaiter,
        TypeRef awaiterType,
        ClassicInverseBudget budget)
    {
        var path = ImmutableArray.CreateBuilder<Block>();
        var visited = new HashSet<Block>(ReferenceEqualityComparer.Instance);
        Block merge = continuation;
        path.Add(merge);
        visited.Add(merge);
        while (merge.Children.Count == 0)
        {
            if (!budget.Charge()
                || index.SuccessorsOf(merge) is not [Block next]
                || !ReferenceEquals(next.Parent, continuation.Parent)
                || !index.HasOnlySuccessors(merge, next)
                || !index.HasOnlyPredecessors(next, merge)
                || !visited.Add(next))
                return null;
            merge = next;
            path.Add(merge);
        }
        if (path.Count < 2 || !budget.Charge()
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
                return path.Count == 2 && ReferenceEquals(coalesce.Left, current)
                    ? new(continuation, merge, result, coalesce, path.ToImmutable())
                    : null;
            }
            if (parent is Conditional conditional)
            {
                return ReferenceEquals(conditional.Condition, current)
                    ? new(continuation, merge, result, conditional, path.ToImmutable())
                    : null;
            }
            if (parent is SwitchExpression selection)
                return ReferenceEquals(selection.Value, current)
                    ? new(continuation, merge, result, selection, path.ToImmutable()) : null;
            // Discovery is not authorization: the raw predicate graph is closed
            // against every logical operand before a selection receives roles.
        }
        return null;
    }

    /// <summary>
    /// Closes prerequisite-moved coalesces and conditionals around the same
    /// raw predicate, selected values, and sole joined use. A matching
    /// GetResult offset alone cannot authorize movement across control.
    /// </summary>
    static bool ProveSelectionCorrespondence(
        BodyIndex planning,
        BodyIndex raw,
        Dictionary<IrNode, string> roles,
        ClassicInverseBudget budget,
        out SelectionBindings bindings)
    {
        bindings = new();
        var selections = new List<SelectionContinuation>(planning.SelectionContinuations);
        foreach (StoreLocal bind in planning.AwaiterBinds)
        {
            if (!budget.Charge())
                return false;
            if (bind.Value is not Call { Arguments: [IrExpression expression] } call
                || expression is not (Conditional or Coalesce) || bind.Parent is not Block merge)
                continue;
            ConditionalBranch? test = raw.BranchAt(expression.SourceOffset);
            if (test?.Parent is not Block rawHead
                || planning.BlocksStartingAt(rawHead.StartOffset) is not [Block head]
                || head.Children.Count != 0 || !planning.HasOnlySuccessors(head, merge)
                || !planning.HasOnlyPredecessors(merge, head)
                || !ReferenceEquals(head.Parent, merge.Parent))
                return false;
            selections.Add(new(head, merge, call, expression, [head, merge], IsOperand: true));
        }
        foreach (SelectionContinuation moved in selections)
        {
            if (moved.Expression is SwitchExpression selection)
            {
                if (!ProveSwitchCorrespondence(moved, selection, raw, roles, bindings, budget))
                    return false;
                continue;
            }
            if (moved.Expression is Conditional conditional)
            {
                if (!ProveConditionalCorrespondence(moved, conditional, raw, roles, bindings, budget))
                    return false;
                continue;
            }
            if (moved.Expression is not Coalesce coalesce)
                return false;
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
                || !SelectedUse(moved, merge, out StoreLocal result, out StoreLocal plannedResult)
                || result.Index != plannedResult.Index
                || !Equals(result.Type, plannedResult.Type)
                || result.SourceOffset != plannedResult.SourceOffset
                || !ClassicInverseExpressionRules.SameTree(first.Value, coalesce.Left, budget)
                || !ClassicInverseExpressionRules.SameTree(fallback.Value, coalesce.Right, budget)
                || !ClassicInverseExpressionRules.SameTree(result.Value, plannedResult.Value, budget,
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
        return ProveDeferredAwaitSpills(planning, raw, roles, bindings, budget);
    }

    static bool ProveConditionalCorrespondence(
        SelectionContinuation moved,
        Conditional expression,
        BodyIndex raw,
        Dictionary<IrNode, string> roles,
        SelectionBindings bindings,
        ClassicInverseBudget budget)
    {
        if (!budget.Charge()
            || raw.BlocksStartingAt(moved.Continuation.StartOffset) is not [Block head])
            return false;

        var slots = new HashSet<int>();
        var stores = new HashSet<StoreStackSlot>(ReferenceEqualityComparer.Instance);
        var reads = new HashSet<LoadStackSlot>(ReferenceEqualityComparer.Instance);
        IrNode? previousJoin = null;
        IrNode? previousExpression = null;
        int generation = 0;
        while (true)
        {
            if (!budget.Charge()
                || generation + 1 >= moved.Path.Length
                || head.StartOffset != moved.Path[generation].StartOffset
                || head.Children is not [ConditionalBranch test]
                || head.Parent is not BlockContainer container
                || raw.BlocksStartingAt(moved.Path[generation + 1].StartOffset) is not [Block merge]
                || test.SourceOffset < 0 || test.SourceOffset != expression.SourceOffset)
                return false;

            int first = raw.PositionOf(head);
            int end = raw.PositionOf(merge);
            if (first < 0 || end < first + 3 || !ReferenceEquals(merge.Parent, container)
                || container.Children[end - 2] is not Block whenFalse
                || container.Children[end - 1] is not Block whenTrue
                || whenFalse.Children is not [StoreStackSlot falseStore, Branch exit]
                || whenTrue.Children is not [StoreStackSlot trueStore]
                || exit.TargetOffset != merge.StartOffset)
                return false;
            if (raw.SlotLoadsIn(merge) is not [LoadStackSlot joined]
                || SelectedType(joined, expression, trueStore.Value, falseStore.Value, budget) is not { } target)
                return false;
            if (!raw.HasOnlySuccessors(whenTrue, merge)
                || !raw.HasOnlySuccessors(whenFalse, merge)
                || !raw.HasOnlyPredecessors(merge, whenTrue, whenFalse)
                || trueStore.Slot != falseStore.Slot
                || joined.Slot != trueStore.Slot
                || !ProvePredicateChain(raw, container, first, end - 3, expression,
                    whenTrue, whenFalse, previousJoin, previousExpression, bindings, budget)
                || !ClassicInverseExpressionRules.SameTree(trueStore.Value, expression.WhenTrue, budget,
                    selectedTarget: target)
                || !ClassicInverseExpressionRules.SameTree(falseStore.Value, expression.WhenFalse, budget,
                    selectedTarget: target)
                || !bindings.TestsByJoin.TryAdd(joined, test)
                || !bindings.ValuesByTest.TryAdd(test, expression)
                || !bindings.Swap(container, end - 2, end - 1, budget))
                return false;

            stores.Add(trueStore);
            stores.Add(falseStore);
            reads.Add(joined);
            slots.Add(joined.Slot);
            roles[trueStore] = SelectionStore;
            roles[falseStore] = SelectionStore;
            roles[joined] = SelectionRead;
            bindings.SelectedTypes.Add(trueStore.Value, target);
            bindings.SelectedTypes.Add(falseStore.Value, target);

            if (merge.Children is [ConditionalBranch])
            {
                Conditional? outer = null;
                for (IrNode current = expression; current.Parent is IrExpression parent; current = parent)
                {
                    if (!budget.Charge())
                        return false;
                    if (parent is Conditional next)
                    {
                        if (ReferenceEquals(next.Condition, current))
                            outer = next;
                        break;
                    }
                    if (parent is Coalesce)
                        break;
                }
                if (outer is null)
                    return false;
                previousJoin = joined;
                previousExpression = expression;
                expression = outer;
                head = merge;
                generation++;
                continue;
            }
            if (generation + 2 != moved.Path.Length
                || !SelectedUse(moved, merge, out StoreLocal result, out StoreLocal plannedResult)
                || result.Index != plannedResult.Index || !Equals(result.Type, plannedResult.Type)
                || result.SourceOffset != plannedResult.SourceOffset
                || !ClassicInverseExpressionRules.SameTree(result.Value, plannedResult.Value, budget,
                    joined, expression))
                return false;
            break;
        }

        foreach (int slot in slots)
        {
            if (!budget.Charge())
                return false;
            foreach (StoreStackSlot store in raw.SlotStoresFor(slot))
                if (!budget.Charge() || !stores.Contains(store))
                    return false;
            foreach (LoadStackSlot read in raw.SlotLoadsFor(slot))
                if (!budget.Charge() || !reads.Contains(read))
                    return false;
        }
        return true;
    }

    static bool SelectedUse(SelectionContinuation selection, Block raw,
        out StoreLocal result, out StoreLocal planning)
    {
        result = null!;
        planning = null!;
        if (!selection.IsOperand)
        {
            if (raw.Children is not [StoreLocal store, Leave]
                || selection.Merge.Children is not [StoreLocal planned])
                return false;
            result = store;
            planning = planned;
            return true;
        }
        if (raw.Children is not [StoreLocal rawBind, ConditionalBranch]
            || selection.Merge.Children is not [StoreLocal plannedBind, ConditionalBranch]
            || !ReferenceEquals(plannedBind.Value, selection.GetResult)
            || rawBind.Value is not Call { Callee.Name: "GetAwaiter" } rawCall
            || rawCall.SourceOffset != selection.GetResult.SourceOffset
            || rawCall.Callee != selection.GetResult.Callee)
            return false;
        result = rawBind;
        planning = plannedBind;
        return true;
    }

    static bool ProvePredicateChain(
        BodyIndex raw,
        BlockContainer container,
        int first,
        int last,
        Conditional expression,
        Block whenTrue,
        Block whenFalse,
        IrNode? previousJoin,
        IrNode? previousExpression,
        SelectionBindings bindings,
        ClassicInverseBudget budget)
    {
        int next = last;
        var trueEntries = new List<Block>();
        var falseEntries = new List<Block>();
        var origins = ImmutableArray.CreateBuilder<int>();
        Block? entry = Match(expression.Condition, whenTrue, whenFalse);
        foreach (Block _ in trueEntries.Concat(falseEntries))
            if (!budget.Charge())
                return false;
        return entry is not null && next == first - 1
            && ReferenceEquals(entry, container.Children[first])
            && raw.HasOnlyPredecessors(whenTrue, [.. trueEntries])
            && raw.HasOnlyPredecessors(whenFalse, [.. falseEntries])
            && bindings.PredicateOrigins.TryAdd(expression.SourceOffset, origins.ToImmutable());

        Block? Match(IrExpression condition, Block trueTarget, Block falseTarget)
        {
            if (!budget.Charge())
                return null;
            if (condition is LogicalNot { SourceOffset: < 0 } not)
            {
                bindings.PredicateStructures.Add(not);
                return Match(not.Operand, falseTarget, trueTarget);
            }
            if (condition is LogicalBinary { SourceOffset: < 0 } logical)
            {
                bindings.PredicateStructures.Add(logical);
                Block? right = Match(logical.Right, trueTarget, falseTarget);
                return right is null ? null
                    : logical.Kind == LogicalKind.And
                        ? Match(logical.Left, right, falseTarget)
                        : logical.Kind == LogicalKind.Or
                            ? Match(logical.Left, trueTarget, right) : null;
            }
            if (next < first || container.Children[next] is not Block block
                || block.Children is not [ConditionalBranch test]
                || test.SourceOffset < 0
                || test.TargetOffset != trueTarget.StartOffset
                || !ReferenceEquals(container.Children[next + 1], falseTarget)
                || !raw.HasOnlySuccessors(block, trueTarget, falseTarget)
                || next > first && !raw.HasOnlyPredecessors(block, (Block)container.Children[next - 1])
                || !ClassicInverseExpressionRules.SameTree(test.Condition, condition, budget,
                    previousJoin, previousExpression))
                return null;
            next--;
            origins.Add(test.SourceOffset);
            AddEntry(trueTarget, block);
            AddEntry(falseTarget, block);
            return block;
        }

        void AddEntry(Block target, Block block)
        {
            if (ReferenceEquals(target, whenTrue))
                trueEntries.Add(block);
            else if (ReferenceEquals(target, whenFalse))
                falseEntries.Add(block);
        }
    }

    static TypeRef? SelectedType(
        LoadStackSlot joined,
        Conditional expression,
        IrExpression whenTrue,
        IrExpression whenFalse,
        ClassicInverseBudget budget)
    {
        if (!budget.Charge())
            return null;
        if (Equals(joined.Type, expression.ResultType))
            return joined.Type;
        if (MemberIdentity.IsCoreLibraryType(joined.Type, "System", "Int32")
            && MemberIdentity.IsCoreLibraryType(expression.ResultType, "System", "Boolean")
            && MemberIdentity.IsCoreLibraryType(
                ClassicInverseExpressionRules.SinkType(joined, budget), "System", "Boolean")
            && IsBooleanValue(whenTrue) && IsBooleanValue(whenFalse))
        {
            return expression.ResultType;
        }
        return null;

        static bool IsBooleanValue(IrExpression value)
            => MemberIdentity.IsCoreLibraryType(value.ResultType, "System", "Boolean")
                || value is Constant { Value: int integer } constant && integer is 0 or 1
                    && MemberIdentity.IsCoreLibraryType(constant.Type, "System", "Int32");
    }

    /// <summary>
    /// A dup can spill the new receiver before the importer flushes an older
    /// await-result expression. Only that adjacent, single-use stack transfer
    /// may recover the earlier IL evaluation; expression children keep their
    /// ordinary ordered correspondence.
    /// </summary>
    static bool ProveDeferredAwaitSpills(
        BodyIndex planning,
        BodyIndex raw,
        Dictionary<IrNode, string> roles,
        SelectionBindings bindings,
        ClassicInverseBudget budget)
    {
        foreach (StoreStackSlot spill in raw.AllSlotStores)
        {
            if (!budget.Charge())
                return false;
            if (spill.Value is not Call { Callee.Name: "GetResult" } result
                || spill.Parent is not Block block
                || raw.GetResultsIn(block) is not [Call rawResult]
                || !ReferenceEquals(result, rawResult))
                continue;

            int position = raw.PositionOf(spill);
            if (position < 1
                || block.Children[position - 1] is not StoreStackSlot { Value: NewObject creation } prior
                || result.SourceOffset < 0 || result.SourceOffset >= creation.SourceOffset
                || creation.SourceOffset >= spill.SourceOffset
                || raw.SlotStoresFor(spill.Slot) is not [StoreStackSlot soleStore]
                || !ReferenceEquals(soleStore, spill)
                || raw.SlotLoadsFor(spill.Slot) is not [LoadStackSlot read]
                || !Equals(read.Type, result.ResultType)
                || read.SourceOffset < spill.SourceOffset
                || planning.BlocksStartingAt(block.StartOffset) is not [Block plannedBlock]
                || planning.GetResultsIn(plannedBlock) is not [Call plannedResult]
                || !ClassicInverseExpressionRules.SameTree(result, plannedResult, budget))
                continue;

            IrNode? current = read;
            while (current is not null && current is not Block)
            {
                if (!budget.Charge())
                    return false;
                current = current.Parent;
            }
            if (!ReferenceEquals(current, block))
                continue;
            bool closed = true;
            foreach (IrNode child in creation.Descendants.Prepend(creation))
            {
                if (!budget.Charge())
                    return false;
                if (child.SourceOffset <= result.SourceOffset || child is LoadStackSlot)
                    closed = false;
            }
            if (!closed)
                continue;
            if (!bindings.Swap(block, position - 1, position, budget))
                return false;
            roles[spill] = DeferredAwaitStore;
            roles[read] = DeferredAwaitRead;
        }
        return true;
    }
}
