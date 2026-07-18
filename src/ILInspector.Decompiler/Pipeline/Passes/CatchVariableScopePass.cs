namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Un-folds a catch clause whose folded variable is a local that lives outside
/// the clause. <see cref="EhStructuringPass"/> folds a catch-entry
/// <c>stloc L</c> of the caught exception into the clause header — the clause
/// variable becomes <c>L</c> and the store disappears. That is correct when
/// <c>L</c> is confined to the handler, and it is the shape the using/foreach/
/// async scaffold passes consume, so the fold must happen up front and this
/// correction must run <em>after</em> those passes have taken their clauses.
///
/// <para>When a plain catch clause survives to here with its folded local still
/// read, written, or addressed outside the clause, or an enclosing catch binds
/// the same slot (issue #2828), naming that local as the clause variable
/// shadows its own declaration (CS0136) and drops the assignment represented
/// by the handler-entry store.
/// Rebind the clause to a fresh variable <c>F</c> and restore the entry store as
/// <c>inner = F</c>: <c>catch (Exception F) { inner = F; … }</c>. <c>F</c> is
/// used exactly once for the #2828 shape, so csc re-elides it on recompile and
/// the restored store round-trips to the original <c>stloc L</c>.</para>
///
/// <para>Scope: only clauses in the top-level function body (whose locals index
/// the function's own slot table) and only ordinary catch clauses — a filter
/// clause shares its exception local across the <c>when</c> and the handler, so
/// moving the store into the handler body would leave the filter reading an
/// unassigned local.</para>
/// </summary>
public sealed class CatchVariableScopePass : IIrPass
{
    public string Name => "catch-variable-scope";

    public void Run(IrFunction function, PassContext context)
    {
        // Traverse the function's own scope only: a nested lambda / local
        // function numbers its locals independently, so a slot index there is a
        // different variable and must not be conflated with this scope's locals.
        var scope = DescendantsOutsideNestedFunctions(function.Body).ToList();
        foreach (var clause in scope.OfType<CatchClause>())
        {
            if (clause.VariableIndex is not { } local)
                continue;
            if (clause.Filter is not null)
                continue;
            if (!LocalUsedOutsideClause(scope, clause, local)
                && !EnclosingCatchBindsLocal(clause, local))
                continue;

            int fresh = function.AddLocal(clause.ExceptionType);
            clause.VariableIndex = fresh;

            var restore = new StoreLocal(local, function.Locals[local], new LoadLocal(fresh, clause.ExceptionType));
            context.Stepper.StepOver("restore folded catch-entry assignment for a local used outside the clause", restore);
            PrependStatement(clause.Body, restore);
        }
    }

    static bool LocalUsedOutsideClause(IReadOnlyList<IrNode> scope, CatchClause clause, int local)
    {
        var inside = new HashSet<IrNode>(DescendantsOutsideNestedFunctions(clause));
        foreach (var node in scope)
        {
            if (inside.Contains(node))
                continue;
            switch (node)
            {
                case LoadLocal load when load.Index == local:
                case StoreLocal store when store.Index == local:
                case LoadLocalAddress address when address.Index == local:
                    return true;
            }
        }
        return false;
    }

    static bool EnclosingCatchBindsLocal(CatchClause clause, int local)
    {
        for (IrNode? ancestor = clause.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is CatchClause { VariableIndex: var enclosing } && enclosing == local)
                return true;
        }
        return false;
    }

    static void PrependStatement(BlockContainer body, IrNode statement)
    {
        if (body.Blocks is [var first, ..])
        {
            var existing = first.DetachChildren();
            first.Add(statement);
            foreach (var child in existing)
                first.Add(child);
        }
        else
        {
            var block = new Block(0);
            block.Add(statement);
            body.Add(block);
        }
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
}
