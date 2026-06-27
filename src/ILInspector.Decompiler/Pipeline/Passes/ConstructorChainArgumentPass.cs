namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Collapses a constructor-chain call's spilled argument temporaries into the
/// call so it lands as the body's first statement, where the printer lifts it
/// to a <c>: base(args)</c> / <c>: this(args)</c> signature initializer.
///
/// The compiler spills any base/this argument that carries control flow — the
/// ubiquitous <c>base(message ?? SR.Default)</c> exception shape — into a
/// temporary the general inliner then declines to fold: the chain receiver is
/// evaluated first, and <see cref="TypeRef"/> cannot yet prove it is a class
/// rather than a mutable byref struct, so it reads as an impure leaf the stored
/// value may not be reordered past. But the receiver of a constructor's own
/// base/this call is the object under construction — an immutable reference with
/// no observable evaluation effect — so moving an argument's computation past it
/// never reorders anything. This pass makes that one safe move
/// <see cref="ConstructorChainPass"/> set up: it inlines each single-use
/// argument temp stored in the run of statements immediately preceding the
/// chain call. Left in place the call prints as an invalid <c>base(temp);</c>
/// body statement (CS0175) that drops its argument on recompile.
/// </summary>
public sealed class ConstructorChainArgumentPass : IIrPass
{
    public string Name => "constructor-chain-argument";

    public void Run(IrFunction function, PassContext context)
    {
        if (!function.Signature.HasThis)
            return;

        // ConstructorChainPass has already canonicalized the receiver to `this`.
        if (FindChainCall(function) is not { } call
            || call.Parent is not ExpressionStatement statement
            || statement.Parent is not Block block)
        {
            return;
        }

        var usage = CountPlaces(function);

        // Gather the maximal contiguous run of single-use argument spills
        // immediately preceding the call (reversed to earliest-store-first).
        var run = new List<(IrNode Store, IrNode Load, IrExpression Value)>();
        for (int i = statement.ChildIndex - 1; i >= 0; i--)
        {
            if (SpillLoadInside(block.Children[i], call, usage) is not { } load)
                break;
            run.Add((block.Children[i], load, (IrExpression)block.Children[i].Children[0]));
        }
        if (run.Count == 0)
            return;
        run.Reverse();

        // Prove the whole run can fold without reordering effects before changing
        // anything: a per-spill greedy walk could inline a safe suffix and then
        // decline an earlier spill, stranding a partially-lifted call whose
        // already-folded effect is dropped when diagnostics replace it. If the run
        // is not order-safe, leave every spill in place — an un-lifted chain
        // degrades honestly rather than emit reordered C#.
        if (!RunPreservesEffectOrder(run, call))
            return;

        foreach (var (store, load, _) in run)
        {
            var value = (IrExpression)store.DetachChildren()[0];
            store.Detach();
            context.Stepper.StepOver("inline spilled base/this constructor argument", call);
            load.ReplaceWith(value);
        }
    }

    static Call? FindChainCall(IrFunction function)
    {
        foreach (var node in function.Descendants)
        {
            if (node is Call { Callee: { Name: ".ctor", HasThis: true } } call
                && call.Arguments is [LoadArgument { Index: 0 }, ..])
            {
                return call;
            }
        }
        return null;
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
    static bool RunPreservesEffectOrder(List<(IrNode Store, IrNode Load, IrExpression Value)> run, Call call)
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
    static IrNode? SpillLoadInside(IrNode node, Call call, Dictionary<(bool IsSlot, int Index), Place> usage)
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
        return ReferenceOwnership.IsInside(load, call) ? load : null;
    }

    /// <summary>
    /// Nodes whose evaluation reads a place or produces an effect, so their order
    /// relative to a moved value matters: place reads and addresses, calls and
    /// object/delegate creation, stores, throw, and the raised effectful expression
    /// forms (property getter, await, increment/decrement) earlier passes leave in
    /// place. Pure leaves (constants, sizeof, ldtoken, argument/receiver loads) are
    /// not barriers.
    /// </summary>
    static bool IsOrderSensitive(IrNode node)
        => node is LoadField or LoadLocal or LoadStackSlot or LoadElement or LoadIndirect
            or LoadProperty or LoadFieldAddress or LoadElementAddress
            or Call or CallIndirect or NewObject or DelegateCreation
            or StoreField or StoreProperty or StoreElement or StoreIndirect
            or Throw or AwaitExpression or IncrementDecrement;

    static Dictionary<(bool IsSlot, int Index), Place> CountPlaces(IrFunction function)
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
    sealed class Place
    {
        public List<IrNode> Loads { get; } = [];
        public int Stores { get; set; }
        public bool AddressTaken { get; set; }
    }
}
