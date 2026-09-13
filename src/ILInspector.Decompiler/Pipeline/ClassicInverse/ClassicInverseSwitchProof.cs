using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

internal sealed partial class ClassicInverseLoweringProof
{
    static bool ProveSwitchCorrespondence(
        SelectionContinuation moved, SwitchExpression expression, BodyIndex raw,
        Dictionary<IrNode, string> roles, SelectionBindings bindings, ClassicInverseBudget budget)
    {
        if (!budget.Charge() || moved.IsOperand || moved.Path.Length != 2
            || raw.BlocksStartingAt(moved.Continuation.StartOffset) is not [Block head]
            || raw.BlocksStartingAt(moved.Merge.StartOffset) is not [Block merge]
            || head.Parent is not BlockContainer container || !ReferenceEquals(merge.Parent, container)
            || head.Children is not [StoreLocal selector, IrNode firstTest]
            || selector.Value is not Call getResult
            || !MemberIdentity.IsCoreLibraryType(selector.Type, "System", "Int32")
            || !ClassicInverseExpressionRules.SameTree(getResult, expression.Value, budget)
            || firstTest.SourceOffset < 0 || firstTest.SourceOffset != expression.SourceOffset
            || raw.LocalStoresFor(selector.Index) is not [StoreLocal onlySelector]
            || !ReferenceEquals(onlySelector, selector)
            || merge.Children is not [StoreLocal result, Leave]
            || moved.Merge.Children is not [StoreLocal plannedResult]
            || result.Value is not LoadLocal joined
            || result.Index != plannedResult.Index || !Equals(result.Type, plannedResult.Type)
            || !Equals(joined.Type, expression.ResultType)
            || !ClassicInverseExpressionRules.SameTree(result.Value, plannedResult.Value, budget, joined, expression))
            return false;

        var cases = new Dictionary<int, Block>();
        var nodes = new HashSet<Block>(ReferenceEqualityComparer.Instance) { head };
        var selectorReads = new HashSet<IrNode>(ReferenceEqualityComparer.Instance);
        var predecessors = new Dictionary<Block, HashSet<Block>>(ReferenceEqualityComparer.Instance);
        var origins = ImmutableArray.CreateBuilder<int>();
        var stores = new HashSet<StoreLocal>(ReferenceEqualityComparer.Instance);
        int position = raw.PositionOf(head);
        Block current = head;
        Block? defaultTarget = null;
        while (true)
        {
            if (!budget.Charge())
                return false;
            IrNode test = current.Children[^1];
            if (!ReferenceEquals(current, head) && current.Children.Count != 1)
                return false;
            if (test is not ConditionalBranch conditional
                || !Label(conditional.Condition, out int label)
                || raw.BlocksStartingAt(conditional.TargetOffset) is not [Block arm]
                || !cases.TryAdd(label, arm) || position + 1 >= container.Children.Count)
                return false;
            Block next = (Block)container.Children[++position];
            if (!raw.HasOnlySuccessors(current, arm, next))
                return false;
            Edge(current, arm);
            Edge(current, next);
            if (!Retire(conditional.Condition))
                return false;
            origins.Add(conditional.SourceOffset);
            if (next.Children is [ConditionalBranch])
            {
                if (!nodes.Add(next))
                    return false;
                current = next;
                continue;
            }
            current = next;
            break;
        }

        if (current.Children is [Branch exit])
        {
            if (!nodes.Add(current) || raw.BlocksStartingAt(exit.TargetOffset) is not [Block target]
                || !raw.HasOnlySuccessors(current, target))
                return false;
            Edge(current, target);
            defaultTarget = target;
        }
        else
            defaultTarget = current;

        var armOrder = new List<Block>();
        var seenLabels = new HashSet<int>();
        bool hasDefault = false;
        foreach (SwitchExpressionArm arm in expression.Arms)
        {
            if (!budget.Charge())
                return false;
            Block? target = null;
            if (arm.IsDefault)
            {
                if (hasDefault || !arm.Labels.IsEmpty)
                    return false;
                hasDefault = true;
                target = defaultTarget;
            }
            else
            {
                foreach (int label in arm.Labels)
                {
                    if (!budget.Charge() || !seenLabels.Add(label) || !cases.TryGetValue(label, out Block? block)
                        || target is not null && !ReferenceEquals(target, block))
                        return false;
                    target = block;
                }
            }
            if (target is null || !nodes.Add(target) || !ReferenceEquals(target.Parent, container)
                || target.Children is not [StoreLocal store, ..]
                || target.Children.Count is < 1 or > 2
                || target.Children.Count == 2 && target.Children[1] is not Branch
                || store.Index != joined.Index || !Equals(store.Type, joined.Type)
                || !ClassicInverseExpressionRules.SameTree(store.Value, arm.Value, budget, selectedTarget: joined.Type)
                || !raw.HasOnlySuccessors(target, merge))
                return false;
            if (target.Children.Count == 2 && ((Branch)target.Children[1]).TargetOffset != merge.StartOffset)
                return false;
            stores.Add(store);
            armOrder.Add(target);
            Edge(target, merge);
            roles[store] = SwitchLocalStore;
        }
        if (!hasDefault || seenLabels.Count != cases.Count || cases.Count == 0
            || raw.LocalReadsFor(joined.Index) is not [IrNode onlyJoin] || !ReferenceEquals(onlyJoin, joined)
            || raw.LocalStoresFor(joined.Index).Count != stores.Count)
            return false;
        foreach (StoreLocal store in raw.LocalStoresFor(joined.Index))
            if (!budget.Charge() || !stores.Contains(store))
                return false;
        foreach (IrNode read in raw.LocalReadsFor(selector.Index))
            if (!budget.Charge() || !selectorReads.Contains(read))
                return false;
        foreach (var (block, entries) in predecessors)
            if (!budget.Charge() || !raw.HasExactPredecessors(block, entries, budget))
                return false;
        int first = raw.PositionOf(head), last = raw.PositionOf(merge);
        if (first < 0 || last <= first || nodes.Count != last - first)
            return false;
        for (int i = first; i < last; i++)
            if (!budget.Charge() || !nodes.Contains((Block)container.Children[i]))
                return false;

        var order = new IrNode[container.Children.Count];
        for (int i = 0; i < order.Length; i++)
        {
            if (!budget.Charge())
                return false;
            order[i] = container.Children[i];
        }
        int armStart = last - armOrder.Count;
        for (int i = 0; i < armOrder.Count; i++)
            order[armStart + i] = armOrder[i];
        var uniqueOrder = new HashSet<IrNode>(ReferenceEqualityComparer.Instance);
        foreach (IrNode node in order)
            if (!budget.Charge() || !uniqueOrder.Add(node))
                return false;
        if (!bindings.EvaluationChildren.TryAdd(container, order))
            return false;
        roles[selector] = SwitchLocalStore;
        roles[joined] = SwitchLocalRead;
        bindings.SwitchResults.Add(moved.GetResult);
        bindings.TestsByJoin.Add(joined, firstTest);
        bindings.ValuesByTest.Add(firstTest, expression);
        bindings.PredicateOrigins.Add(expression.SourceOffset, origins.ToImmutable());
        return true;

        void Edge(Block from, Block to)
        {
            if (!predecessors.TryGetValue(to, out var entries))
                predecessors.Add(to, entries = new(ReferenceEqualityComparer.Instance));
            entries.Add(from);
        }

        bool Read(LoadLocal read)
        {
            if (read.Index != selector.Index || !Equals(read.Type, selector.Type))
                return false;
            selectorReads.Add(read);
            roles[read] = SwitchLocalRead;
            origins.Add(read.SourceOffset);
            return read.SourceOffset >= 0;
        }

        bool Label(IrExpression test, out int label)
        {
            label = 0;
            if (test is LogicalNot { Operand: LoadLocal read })
                return Read(read);
            if (test is Comparison { Kind: ComparisonKind.Equal, IsUnsigned: false, Left: LoadLocal value,
                Right: Constant { Value: int integer } constant }
                && MemberIdentity.IsCoreLibraryType(constant.Type, "System", "Int32"))
            {
                label = integer;
                return Read(value);
            }
            return false;
        }

        bool Retire(IrNode node)
        {
            foreach (IrNode value in node.Descendants.Prepend(node))
            {
                if (!budget.Charge() || value.SourceOffset < 0)
                    return false;
                origins.Add(value.SourceOffset);
                if (value is not LoadLocal)
                    bindings.SwitchConsumedValues.Add(value);
            }
            return true;
        }
    }
}
