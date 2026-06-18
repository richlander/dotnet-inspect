namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Eliminates a return-accumulator temporary — the inverse of the compiler's
/// habit of spilling a method's result into a synthetic local when the value
/// is produced inside an exception region or a lock and the
/// <c>ret</c> sits after the region closes. The decompiler reconstructs
/// <c>try { V = x + 1; } finally { ... } return V;</c>; the original source was
/// <c>try { return x + 1; } finally { ... }</c>. Recompiling the temp form makes
/// Roslyn re-zero-init the synthesized local, so the recompiled IL gains a dead
/// leading <c>ldc.i4.0; stloc</c> the original never had.
///
/// The pass is sound by construction: it acts only on a local that is never
/// address-taken and whose every load is the direct operand of a
/// <see cref="Return"/>, and it applies a candidate only when every store and
/// every load is consumed by the plan (all-or-nothing). A store feeding a
/// return is rewritten into a <see cref="Return"/> in place — adjacent
/// <c>StoreLocal V; return V;</c> pairs, and the terminal fall-through stores of
/// a structured statement (try/finally try body, try/catch arms, lock body,
/// if/else arms) the trailing <c>return V;</c> follows. Switch is deliberately
/// excluded: a switch <em>expression</em> is lowered through its own result
/// accumulator, so reproducing per-arm returns would diverge from the original.
///
/// This is the structural raiser for the pure-return-accumulator subset; the
/// printer's definite-assignment dead-init drop (CSharpPrinter, PR #611) is the
/// conservative fallback for every accumulator this pass deliberately leaves
/// standing — switch-expression results, multi-use locals (a value used past
/// its return), and control flow this pass does not model. Those keep their
/// temp but lose the redundant <c>= default</c>; only here does the temp vanish
/// entirely. The two compose without overlap: a local this pass sinks has no
/// stores or loads left, so the printer never emits a declaration for it.
/// </summary>
public sealed class ReturnSinkingPass : IIrPass
{
    public string Name => "return-sinking";

    public void Run(IrFunction function, PassContext context)
    {
        while (SinkOnce(function))
        {
        }
    }

    static bool SinkOnce(IrFunction function)
    {
        var stores = new Dictionary<int, List<StoreLocal>>();
        var returnLoads = new Dictionary<int, List<Return>>();
        var addressTaken = new HashSet<int>();
        var escapingLoad = new HashSet<int>();

        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case StoreLocal store:
                    (stores.TryGetValue(store.Index, out var list) ? list : stores[store.Index] = []).Add(store);
                    break;
                case LoadLocalAddress address:
                    addressTaken.Add(address.Index);
                    break;
                case LoadLocal load when load.Parent is Return ret && ReferenceEquals(ret.Value, load):
                    (returnLoads.TryGetValue(load.Index, out var rl) ? rl : returnLoads[load.Index] = []).Add(ret);
                    break;
                case LoadLocal load:
                    // A load that is not a return's operand: the local carries
                    // its value somewhere else, so it is not a pure return
                    // accumulator and must be left alone.
                    escapingLoad.Add(load.Index);
                    break;
            }
        }

        foreach (var (index, indexStores) in stores)
        {
            if (addressTaken.Contains(index) || escapingLoad.Contains(index))
                continue;
            if (!returnLoads.TryGetValue(index, out var returns) || returns.Count == 0)
                continue;
            if (TryPlan(index, indexStores, returns) is { } plan)
            {
                Apply(plan);
                return true;
            }
        }
        return false;
    }

    sealed record Plan(List<(StoreLocal Store, Break? Break)> Folds, List<Return> Returns);

    static Plan? TryPlan(int index, List<StoreLocal> indexStores, List<Return> returns)
    {
        var folds = new List<(StoreLocal, Break?)>();
        var consumed = new HashSet<StoreLocal>(ReferenceEqualityComparer.Instance);

        foreach (var ret in returns)
        {
            if (ret.Parent is not Block block || ret.ChildIndex == 0)
                return null;
            var prev = block.Children[ret.ChildIndex - 1];

            if (prev is StoreLocal adjacent && adjacent.Index == index)
            {
                if (!consumed.Add(adjacent))
                    return null;
                folds.Add((adjacent, null));
                continue;
            }

            var tails = CollectStmtTails(prev, index);
            if (tails is null)
                return null;
            foreach (var tail in tails)
            {
                if (!consumed.Add(tail.Store))
                    return null;
                folds.Add(tail);
            }
        }

        // Soundness: refuse unless the plan consumes every store of the local.
        // Every load is already a return we delete, so this leaves no orphan
        // read of an undefined accumulator and no store that should have been
        // a return but was missed.
        if (consumed.Count != indexStores.Count)
            return null;

        return new Plan(folds, returns);
    }

    static void Apply(Plan plan)
    {
        foreach (var (store, brk) in plan.Folds)
        {
            var value = (IrExpression)store.DetachChildren()[0];
            brk?.Detach();
            store.ReplaceWith(new Return(value));
        }
        foreach (var ret in plan.Returns)
            ret.Detach();
    }

    /// <summary>
    /// The terminal fall-through stores of <paramref name="index"/> a trailing
    /// <c>return index</c> consumes when it follows the structured statement, or
    /// null when the statement is not a clean accumulator sink (every reachable
    /// fall-through tail must be a <c>StoreLocal index</c>).
    /// </summary>
    static List<(StoreLocal Store, Break? Break)>? CollectStmtTails(IrNode statement, int index)
    {
        switch (statement)
        {
            case TryFinally tryFinally:
                // The finally cannot produce the returned value; only the try
                // body's fall-through tail feeds the trailing return.
                return CollectContainerTail(tryFinally.TryBody, index);
            case Lock @lock:
                return CollectContainerTail(@lock.Body, index);
            case TryCatch tryCatch:
            {
                var result = CollectContainerTail(tryCatch.TryBody, index);
                if (result is null)
                    return null;
                foreach (var clause in tryCatch.Clauses)
                {
                    var clauseTail = CollectContainerTail(clause.Body, index);
                    if (clauseTail is null)
                        return null;
                    result.AddRange(clauseTail);
                }
                return result;
            }
            case Switch:
                // Do not sink into a switch: csc lowers a switch *expression*
                // through its own result accumulator (each arm stores, the
                // epilogue loads and returns), so reproducing per-arm returns
                // diverges from the original stream rather than matching it.
                return null;
            case IfStatement { HasElse: true } ifStatement:
            {
                var thenTail = CollectBlockTail(ifStatement.Then, index);
                var elseTail = CollectBlockTail(ifStatement.Else!, index);
                if (thenTail is null || elseTail is null)
                    return null;
                thenTail.AddRange(elseTail);
                return thenTail;
            }
            default:
                return null;
        }
    }

    static List<(StoreLocal Store, Break? Break)>? CollectContainerTail(BlockContainer container, int index)
    {
        var blocks = container.Blocks;
        return blocks.Count == 0 ? null : CollectBlockTail(blocks[^1], index);
    }

    static List<(StoreLocal Store, Break? Break)>? CollectBlockTail(Block block, int index)
    {
        if (block.Children.Count == 0)
            return null;
        var last = block.Children[^1];
        if (last is StoreLocal store && store.Index == index)
            return [(store, null)];
        if (last is Break brk
            && block.Children.Count >= 2
            && block.Children[^2] is StoreLocal stored
            && stored.Index == index)
        {
            return [(stored, brk)];
        }
        return CollectStmtTails(last, index);
    }
}
