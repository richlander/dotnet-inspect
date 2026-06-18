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
            if (!ConditionReads(loop.Condition, initializer.Index))
                continue;

            increment.Detach();
            var parts = loop.DetachChildren();  // [condition, body]
            initializer.Detach();               // reindexes the loop's slot
            context.Stepper.StepOver("raise while loop to for loop", loop);
            loop.ReplaceWith(new ForLoop(initializer, (IrExpression)parts[0], increment, (Block)parts[1]));
        }
    }

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
}
