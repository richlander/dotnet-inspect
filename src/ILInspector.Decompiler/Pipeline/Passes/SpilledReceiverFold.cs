namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Shared effect-order-preserving argument-run fold, extracted verbatim from
/// <see cref="ConstructorChainArgumentPass"/> so the constructor-chain lift and
/// <see cref="FluentChainRecompositionPass"/> reason
/// about the same soundness condition rather than maintaining parallel copies.
///
/// The shape both passes fold is identical: a sink <see cref="Call"/> preceded in
/// its block by a contiguous run of single-use spill stores whose one load each
/// sits inside the call. <see cref="ExpressionInliningPass"/> additionally uses
/// the fold for a stack-slot-only run of direct returned-call arguments. Folding
/// collapses each spill back into the call. The
/// move is only performed when it provably reorders no effect —
/// <see cref="RunPreservesEffectOrder"/> is the gate, all-or-nothing per run.
///
/// Callers differ only in which call is the sink, how it is found, and whether
/// user locals are eligible; the fold arithmetic and its safety proof are the
/// same, and live here.
/// </summary>
static class SpilledReceiverFold
{
    /// <summary>
    /// Folds the maximal contiguous run of single-use spill stores immediately
    /// preceding <paramref name="statement"/> whose one load sits inside
    /// <paramref name="sink"/>, when doing so preserves effect order. Returns true
    /// when a non-empty run was folded. <paramref name="usage"/> is the caller's
    /// current <see cref="CountPlaces"/> snapshot — a consumer that folds to a
    /// fixpoint must recompute it between folds because a fold moves loads.
    /// <paramref name="stackSlotsOnly"/> prevents the run from consuming an
    /// adjacent user local.
    /// </summary>
    public static bool TryFold(
        IrNode statement,
        Call sink,
        IReadOnlyDictionary<(bool IsSlot, int Index), Place> usage,
        PassContext context,
        string stepLabel,
        bool stackSlotsOnly = false)
    {
        if (statement.Parent is not Block block)
            return false;

        // Gather the maximal contiguous run of single-use spills immediately
        // preceding the statement (reversed to earliest-store-first).
        var run = new List<(IrNode Store, IrNode Load, IrExpression Value)>();
        for (int i = statement.ChildIndex - 1; i >= 0; i--)
        {
            if (stackSlotsOnly && block.Children[i] is not StoreStackSlot)
                break;
            if (SpillLoadInside(block.Children[i], sink, usage) is not { } load)
                break;
            run.Add((block.Children[i], load, (IrExpression)block.Children[i].Children[0]));
        }
        if (run.Count == 0)
            return false;
        run.Reverse();

        // Prove the whole run can fold without reordering effects before changing
        // anything: a per-spill greedy walk could inline a safe suffix and then
        // decline an earlier spill, stranding a partially-lifted call whose
        // already-folded effect is dropped downstream. If the run is not
        // order-safe, leave every spill in place — an un-folded chain degrades
        // honestly rather than emit reordered C#.
        if (!RunPreservesEffectOrder(run, sink))
            return false;

        foreach (var (store, load, _) in run)
        {
            var value = (IrExpression)store.DetachChildren()[0];
            store.Detach();
            context.Stepper.StepOver(stepLabel, sink);
            load.ReplaceWith(value);
        }
        return true;
    }

    /// <summary>
    /// True when folding every spill in <paramref name="run"/> back into the call
    /// preserves effect order. Each spill's value is originally produced — in store
    /// order — before the call evaluates any argument, and unconditionally. The
    /// move is safe only when every order-sensitive spill load is (1) in an
    /// unconditionally-evaluated position (never a short-circuit/ternary/switch
    /// arm, which would make an always-run store conditional), and (2) reached, in
    /// the call's evaluation order, in store order and before any inline argument's
    /// own order-sensitive read or effect. Effect-free, place-free spills
    /// (constants) reorder invisibly and constrain nothing.
    /// </summary>
    public static bool RunPreservesEffectOrder(List<(IrNode Store, IrNode Load, IrExpression Value)> run, Call call)
    {
        var storeRank = new Dictionary<IrNode, int>(ReferenceEqualityComparer.Instance);
        var spillLoads = new HashSet<IrNode>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < run.Count; i++)
        {
            spillLoads.Add(run[i].Load);
            if (!IsReorderTrivial(run[i].Value))
                storeRank[run[i].Load] = i;
        }
        if (storeRank.Count == 0)
            return true;

        // (1) An order-sensitive spill must stay unconditionally evaluated.
        foreach (var load in storeRank.Keys)
        {
            if (InConditionalPosition(load, call))
                return false;
        }

        // (2) Walk the argument tree in evaluation order: order-sensitive spill
        // loads must arrive in store order and before any inline read/effect.
        int lastRank = -1;
        bool sawBarrier = false;
        bool safe = true;

        void Visit(IrNode node)
        {
            if (!safe)
                return;
            if (storeRank.TryGetValue(node, out int rank))
            {
                if (sawBarrier || rank <= lastRank)
                    safe = false;
                else
                    lastRank = rank;
                return;
            }
            if (spillLoads.Contains(node))
                return;  // a trivial spill load folds to a constant: no barrier
            foreach (var child in node.Children)
                Visit(child);
            if (IsOrderSensitive(node))
                sawBarrier = true;
        }

        foreach (var argument in call.Arguments)
            Visit(argument);
        return safe;
    }

    /// <summary>
    /// True when any node between <paramref name="load"/> and <paramref name="call"/>
    /// only conditionally evaluates its children — a ternary, <c>??</c>, short-circuit
    /// <c>&amp;&amp;</c>/<c>||</c>, <c>?.</c>, or switch expression. Inlining an
    /// unconditional pre-call store into such a position would make it run conditionally.
    /// </summary>
    static bool InConditionalPosition(IrNode load, Call call)
    {
        for (var current = load.Parent; current is not null && !ReferenceEquals(current, call); current = current.Parent)
        {
            if (current is Conditional or Coalesce or LogicalBinary or NullConditional
                or SwitchExpression or SwitchExpressionArm)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A value safe to evaluate at any point relative to other effects: it reads
    /// no place and produces no observable effect, so reordering it is invisible.
    /// A value that reads a place (field/local/element/indirect) is order-sensitive
    /// — an intervening store or call could change what it reads — so it is not
    /// trivial even without an effect of its own.
    /// </summary>
    static bool IsReorderTrivial(IrExpression value)
        => value is Constant or SizeOf or LoadToken;

    /// <summary>The single load of <paramref name="node"/> when it is a single-use
    /// spill store whose load sits inside <paramref name="call"/>; otherwise null.</summary>
    public static IrNode? SpillLoadInside(IrNode node, IrExpression call, IReadOnlyDictionary<(bool IsSlot, int Index), Place> usage)
    {
        (bool IsSlot, int Index)? key = node switch
        {
            StoreLocal store => (false, store.Index),
            StoreStackSlot store => (true, store.Slot),
            _ => null,
        };
        if (key is not { } place
            || !usage.TryGetValue(place, out var record)
            || record.AddressTaken
            || record.Stores != 1
            || record.Loads.Count != 1)
        {
            return null;
        }
        var load = record.Loads[0];
        if (!ReferenceOwnership.IsInside(load, call))
            return null;

        // The load must sit in the same body as the sink. A load reached only by
        // crossing a nested-function boundary (a lambda or local function passed as
        // an argument/receiver) is evaluated in a deferred, separately-scoped body:
        // folding the eager, unconditional pre-call store into it would turn an
        // always-run effect into a deferred (or, if the delegate is never invoked,
        // absent) one, and the function-scope usage map cannot speak for that body's
        // own slot numbering. Since lambda raising, a captured outer local prints as
        // an in-body load of that local, so this shape is reachable; decline it.
        return CrossesNestedFunctionBoundary(load, call) ? null : load;
    }

    /// <summary>
    /// True when a <see cref="Lambda"/> or <see cref="LocalFunctionStatement"/> body
    /// lies on the parent chain strictly between <paramref name="load"/> and
    /// <paramref name="call"/> — i.e. the load is inside a nested function that is
    /// itself nested inside the sink call. A load and sink in the same nested body
    /// (the boundary sitting above the call) does not cross and is unaffected.
    /// </summary>
    static bool CrossesNestedFunctionBoundary(IrNode load, IrExpression call)
    {
        for (var current = load.Parent; current is not null && !ReferenceEquals(current, call); current = current.Parent)
        {
            if (current is Lambda or LocalFunctionStatement)
                return true;
        }
        return false;
    }

    /// <summary>
    /// True unless <paramref name="node"/> is provably reorder-pure — i.e. anything
    /// other than a constant, <c>sizeof</c>, <c>ldtoken</c>, or an argument/receiver
    /// load. Everything else (place reads, calls, stores, allocations, casts and
    /// other potentially-throwing or order-sensitive operations) is treated as a
    /// barrier, so a moved value can never cross it. Deny-list by design: an
    /// unrecognized node is conservatively a barrier rather than silently reorderable.
    /// </summary>
    static bool IsOrderSensitive(IrNode node)
        => node is not (Constant or SizeOf or LoadToken or LoadArgument);

    public static Dictionary<(bool IsSlot, int Index), Place> CountPlaces(IrFunction function)
    {
        var places = new Dictionary<(bool IsSlot, int Index), Place>();

        Place Entry(bool isSlot, int index)
        {
            if (!places.TryGetValue((isSlot, index), out var entry))
                places[(isSlot, index)] = entry = new Place();
            return entry;
        }

        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadLocal load: Entry(false, load.Index).Loads.Add(load); break;
                case StoreLocal store: Entry(false, store.Index).Stores++; break;
                case LoadLocalAddress address: Entry(false, address.Index).AddressTaken = true; break;
                case LoadStackSlot load: Entry(true, load.Slot).Loads.Add(load); break;
                case StoreStackSlot store: Entry(true, store.Slot).Stores++; break;
            }
        }
        return places;
    }

    public sealed class Place
    {
        public List<IrNode> Loads { get; } = [];
        public int Stores { get; set; }
        public bool AddressTaken { get; set; }
    }
}
