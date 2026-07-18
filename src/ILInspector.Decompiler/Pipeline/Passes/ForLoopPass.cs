namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises while loops to for loops — the inverse of the compiler's for
/// lowering. The shape is structural, not heuristic: the statement
/// immediately before a <see cref="WhileLoop"/> stores the variable the
/// condition reads, and the body's last statement steps that same variable.
/// Runs after structuring; the loop must already be a tree node.
/// </summary>
public sealed class ForLoopPass : IIrPass
{
    public string Name => "for-loops";

    public void Run(IrFunction function, PassContext context)
    {
        var targetSourcesByScope = new Dictionary<IrNode, Dictionary<int, List<IrNode>>>();

        foreach (var loop in function.Descendants.OfType<WhileLoop>().ToList())
        {
            if (loop.Parent is not Block container)
                continue;
            int slot = loop.ChildIndex;
            if (slot == 0)
                continue;
            if (container.Children[slot - 1] is not StoreLocal initializer)
                continue;
            if (loop.Body.Children.Count == 0
                || loop.Body.Children[^1] is not StoreLocal increment
                || increment.Index != initializer.Index)
            {
                continue;
            }
            // A checked user-defined increment (i = op_CheckedIncrement(i)) has no
            // for-header spelling that preserves the checked overload: a `checked
            // { i++; }` block is invalid in a for header, and a bare `checked(i++)`
            // expression is CS0201 there. Keep such loops as while (in either the
            // initializer or increment slot) so the checked increment localizes to
            // a `checked { i++; }` statement. See #1712.
            if (IsCheckedUserIncrement(increment.Value) || IsCheckedUserIncrement(initializer.Value))
                continue;
            if (!ConditionReads(loop.Condition, initializer.Index))
                continue;
            if (ContainsCurrentLoopContinue(loop.Body))
                continue;

            var scope = EnclosingFunctionScope(loop, function);
            if (!targetSourcesByScope.TryGetValue(scope, out var targetSources))
            {
                targetSources = CollectTargetSources(scope);
                targetSourcesByScope.Add(scope, targetSources);
            }

            IReadOnlyList<Leave> protectedContinues = [];
            if (increment.SourceOffset >= 0
                && targetSources.TryGetValue(increment.SourceOffset, out var targeters))
            {
                var candidates = new List<Leave>(targeters.Count);
                bool allTargetsConsumed = true;
                foreach (var targeter in targeters)
                {
                    if (targeter is not Leave leave
                        || !ProtectedRegionControlFlow.CanRaiseLeave(leave, loop)
                        || !BelongsToLoop(leave, loop))
                    {
                        allTargetsConsumed = false;
                        break;
                    }
                    candidates.Add(leave);
                }
                if (!allTargetsConsumed)
                    continue;
                protectedContinues = candidates;
            }

            increment.Detach();
            var parts = loop.DetachChildren();  // [condition, body]
            initializer.Detach();               // reindexes the loop's slot
            context.Stepper.StepOver("raise while loop to for loop", loop);
            loop.ReplaceWith(new ForLoop(initializer, (IrExpression)parts[0], increment, (Block)parts[1]));

            foreach (var leave in protectedContinues)
            {
                var next = new Continue(ContinueOrigin.ProtectedRegionLeaveToForIncrement);
                next.InheritSourceOffset(leave);
                leave.ReplaceWith(next);
            }
        }
    }

    static IrNode EnclosingFunctionScope(IrNode node, IrFunction function)
    {
        for (IrNode? current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is IrFunction or Lambda or LocalFunctionStatement)
                return current;
        }
        return function;
    }

    static Dictionary<int, List<IrNode>> CollectTargetSources(IrNode scope)
    {
        var targets = new Dictionary<int, List<IrNode>>();
        foreach (var node in DescendantsOutsideNestedFunctions(scope))
        {
            switch (node)
            {
                case Branch branch:
                    AddTargetSource(targets, branch.TargetOffset, branch);
                    break;
                case ConditionalBranch branch:
                    AddTargetSource(targets, branch.TargetOffset, branch);
                    break;
                case SwitchBranch branch:
                    foreach (int target in branch.TargetOffsets)
                        AddTargetSource(targets, target, branch);
                    break;
                case Leave leave:
                    AddTargetSource(targets, leave.TargetOffset, leave);
                    break;
            }
        }
        return targets;
    }

    static void AddTargetSource(Dictionary<int, List<IrNode>> targets, int offset, IrNode source)
    {
        if (!targets.TryGetValue(offset, out var sources))
        {
            sources = [];
            targets.Add(offset, sources);
        }
        sources.Add(source);
    }

    static IEnumerable<IrNode> DescendantsOutsideNestedFunctions(IrNode node)
    {
        foreach (var child in node.Children)
        {
            yield return child;
            if (child is Lambda or LocalFunctionStatement)
                continue;
            foreach (var descendant in DescendantsOutsideNestedFunctions(child))
                yield return descendant;
        }
    }

    /// <summary>True when a loop increment store value is a user-defined <c>op_CheckedIncrement</c>/<c>op_CheckedDecrement</c> call — a checked increment that has no for-header spelling.</summary>
    static bool IsCheckedUserIncrement(IrExpression value)
        => value is Call { Callee: { HasThis: false, Name: "op_CheckedIncrement" or "op_CheckedDecrement" } };

    static bool ConditionReads(IrExpression condition, int localIndex)
    {
        if (condition is LoadLocal { } load && load.Index == localIndex)
            return true;
        foreach (var node in condition.Descendants)
        {
            if (node is LoadLocal inner && inner.Index == localIndex)
                return true;
        }
        return false;
    }

    static bool BelongsToLoop(IrNode node, WhileLoop loop)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, loop))
                return true;
            if (current is WhileLoop or DoWhileLoop or ForLoop or ForeachStatement
                or Lambda or LocalFunctionStatement or IrFunction)
                return false;
        }
        return false;
    }

    static bool ContainsCurrentLoopContinue(IrNode node)
    {
        foreach (var child in node.Children)
        {
            if (child is Continue)
                return true;
            if (child is WhileLoop or DoWhileLoop or ForLoop or ForeachStatement)
                continue;
            if (ContainsCurrentLoopContinue(child))
                return true;
        }
        return false;
    }
}
