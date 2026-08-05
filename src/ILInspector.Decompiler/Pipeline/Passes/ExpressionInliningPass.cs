namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Forward-substitutes single-use temporaries — the inverse of the
/// compiler's expression spilling. A store inlines into the load
/// only when the rewrite provably cannot reorder effects: single store,
/// single load, address never taken, and the load sits in the immediately
/// following statement either as its first-evaluated leaf (the stored value
/// still evaluates first) or with a pure stored value (evaluation order
/// cannot matter). Runs to fixpoint so spill chains collapse.
///
/// <para>A second mode, <see cref="InlineLiveRangeOnce"/>, collapses the
/// spilled-call-chain shape the simple mode cannot: a fluent call chain
/// (<c>xs.Where(p).Select(f)</c>) spills its receiver, each lambda, and every
/// intermediate result into reused stack slots — so a slot has several stores
/// and several loads function-wide (defeating the single-store/single-load
/// keys), and a store's use sits past an interleaved statement (defeating the
/// adjacency rule). This mode reasons per-store over its live range: a movable
/// value (an effect-free read or a non-capturing delegate creation) inlines
/// into the one load it reaches before the slot is rewritten, provided nothing
/// in between writes what the value reads. Effect-free values reorder freely;
/// the interference scan keeps a value that reads a place from crossing a write
/// to it.</para>
///
/// <para>A third mode collapses a contiguous run of two or more synthetic
/// stack-slot stores back into the direct arguments of a returned call. This is
/// the multi-argument form of the same compiler spill: each stored value was
/// evaluated in argument order and held on the evaluation stack until the call.
/// The fold is all-or-nothing and uses
/// <see cref="SpilledReceiverFold.RunPreservesEffectOrder"/> to require the
/// direct argument loads to remain unconditional and in store order.</para>
///
/// <para><c>slotsOnly</c> restricts <see cref="InlineOnce"/> to synthetic stack
/// slots (never user locals). This is the F2 mode (#2386): the pass runs a third
/// time late in the pipeline — before <see cref="SlotMaterializationPass"/> — to
/// inline the single-use spill slots that structuring and reconstruction mint
/// after the earlier runs, which would otherwise materialize as
/// <c>T S_n = expr;</c> declarations. It must stay slots-only there because by
/// that point <see cref="IncrementDecrementPass"/> has folded
/// <c>x = x + 1</c> into <c>x++</c>; a reconstructed increment hides a store
/// from the single-store/single-load keys, so inlining a user local into it
/// would emit an invalid <c>1++</c> (#2379 piece 1 census). Stack slots are the
/// compiler's spill scratch and are never the target of increment
/// reconstruction, so the slots-only late run is regression-free.</para>
/// </summary>
public sealed class ExpressionInliningPass : IIrPass
{
    public string Name => "expression-inlining";

    readonly bool _slotsOnly;

    /// <param name="slotsOnly">
    /// When true, <see cref="InlineOnce"/> considers only synthetic stack slots,
    /// not user locals — the F2 late-run contract (#2386). Defaults to false so
    /// the early full-pipeline runs are unchanged.
    /// </param>
    public ExpressionInliningPass(bool slotsOnly = false) => _slotsOnly = slotsOnly;

    public void Run(IrFunction function, PassContext context)
    {
        while (InlineReturnedCallArgumentRunOnce(function, context)
            || InlineOnce(function, context, _slotsOnly)
            || InlineLiveRangeOnce(function, context))
        {
        }
    }

    static bool InlineReturnedCallArgumentRunOnce(IrFunction function, PassContext context)
    {
        var usage = SpilledReceiverFold.CountPlaces(function);
        foreach (var @return in function.Descendants.OfType<Return>())
        {
            if (@return is not { Parent: Block block, Value: Call call })
                continue;

            int runLength = 0;
            bool directArgumentsOnly = true;
            for (int i = @return.ChildIndex - 1; i >= 0; i--)
            {
                if (block.Children[i] is not StoreStackSlot store
                    || SpilledReceiverFold.SpillLoadInside(store, call, usage) is not LoadStackSlot load)
                {
                    break;
                }

                runLength++;
                // Direct arguments are stronger than the shared helper requires:
                // no conditional or hidden-operation parent can sit between the
                // call and the load, so moving the stored value crosses only the
                // other arguments covered by RunPreservesEffectOrder.
                if (!call.Arguments.Any(argument => ReferenceEquals(argument, load))
                    || !Equals(store.Value.ResultType, load.ResultType))
                {
                    directArgumentsOnly = false;
                    break;
                }
            }

            // A single adjacent spill is already owned by InlineOnce. This mode
            // exists for an ordered argument run that must fold as one unit.
            if (runLength < 2 || !directArgumentsOnly)
                continue;

            if (SpilledReceiverFold.TryFold(
                @return,
                call,
                usage,
                context,
                "inline ordered stack-slot run into returned call arguments",
                stackSlotsOnly: true))
            {
                return true;
            }
        }
        return false;
    }

    static bool InlineOnce(IrFunction function, PassContext context, bool slotsOnly)
    {
        var locals = new Dictionary<(bool IsSlot, int Index), (List<IrNode> Loads, List<IrNode> Stores, bool AddressTaken)>();
        var argumentAddresses = new HashSet<int>();

        (List<IrNode> Loads, List<IrNode> Stores, bool AddressTaken) Entry(bool isSlot, int index)
        {
            if (!locals.TryGetValue((isSlot, index), out var entry))
                locals[(isSlot, index)] = entry = ([], [], false);
            return entry;
        }

        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadLocal load: Entry(false, load.Index).Loads.Add(load); break;
                case StoreLocal store when store.Parent is Block: Entry(false, store.Index).Stores.Add(store); break;
                case LoadLocalAddress address: locals[(false, address.Index)] = Entry(false, address.Index) with { AddressTaken = true }; break;
                case LoadArgumentAddress argumentAddress: argumentAddresses.Add(argumentAddress.Index); break;
                case StoreArgument argumentStore: argumentAddresses.Add(argumentStore.Index); break;
                case LoadStackSlot load: Entry(true, load.Slot).Loads.Add(load); break;
                case StoreStackSlot store when store.Parent is Block: Entry(true, store.Slot).Stores.Add(store); break;
            }
        }

        foreach (var ((isSlot, _), (loads, stores, addressTaken)) in locals)
        {
            // F2 (#2386): the late run inlines only synthetic stack slots. A user
            // local reaching a reconstructed `x++` looks single-use (the folded
            // increment hides its store), so inlining a constant into it would
            // emit an invalid `1++`.
            if (slotsOnly && !isSlot)
                continue;
            if (addressTaken || loads.Count != 1 || stores.Count != 1)
                continue;
            var store = stores[0];
            var load = loads[0];
            if (IsInsideCatchFilter(load))
                continue;
            // A load that is the target of an increment/decrement is an lvalue,
            // not a value use: replacing it with the stored expression yields an
            // invalid `1++`. Unreachable from real IL today (a reconstructed
            // increment's operand is always a local/argument place, and its dup
            // slot is read twice so it is never single-load), but guarded so the
            // pass is correct by construction rather than by reachability (#2386
            // adversarial review).
            if (load.Parent is IncrementDecrement)
                continue;
            var block = (Block)store.Parent!;
            IrNode next;
            if (store.ChildIndex + 1 < block.Children.Count)
            {
                next = block.Children[store.ChildIndex + 1];
            }
            else if (FallthroughFirstStatement(function, block) is { } following)
            {
                // The store ends its block; the only path to the next block
                // is the fallthrough edge (no branches target it), so the
                // following block's first statement is "next".
                next = following;
            }
            else
            {
                continue;
            }
            if (!ReferenceOwnership.IsInside(load, next))
                continue;
            if (store is StoreLocal { Value: LoadField { Instance: null } } && IsLockObject(load))
                continue;  // keep copied static lock receivers; inlining can fail to bind in reconstructed shells

            if (store is StoreLocal typedStore && !typedStore.Type.Equals(typedStore.Value.ResultType))
                continue;  // the local declaration carries a required cast/type witness

            bool pure = IsPure(store is StoreLocal sl ? sl.Value : ((StoreStackSlot)store).Value, locals, argumentAddresses, function);
            bool firstLeaf = IsFirstEvaluatedLeaf(load, next);
            // A non-pure value must stay unconditionally evaluated: the spilled
            // store always ran, so its effectful value may only land where it
            // still always runs — never inside a `?:`/`??`/`&&`/`||`/`?.`/switch/
            // `??=` arm. This guards BOTH inline paths, including `firstLeaf`: a
            // node that exposes its own CONDITIONAL child as `Children[0]` (a
            // `??=` whose only/first child is its guarded fallback) makes
            // `IsFirstEvaluatedLeaf` misreport that fallback as first-evaluated,
            // so the firstLeaf path alone would move the effect into the guarded
            // position (#3500 adversarial review, GPT + Gemini). Pure values are
            // effect-free and non-throwing, so conditional evaluation is
            // unobservable and they skip this.
            if (!pure && !LoadIsUnconditionallyEvaluated(load, next))
                continue;
            // A non-pure value must also not cross a hidden operation that runs
            // before or between a container's children — a record clone, an object
            // construction, an interpolated-string handler build with interleaved
            // appends, or a collection/array allocation. Universal, so it guards
            // the firstLeaf path too: these operations precede even the first
            // evaluated child (#3500 adversarial review, GPT + Gemini).
            if (!pure && LoadCrossesHiddenOperation(load, next))
                continue;
            // A value that is neither the first-evaluated leaf nor pure normally
            // cannot defer to its load: it would move past whatever `next`
            // evaluates first, reordering effects or which exception surfaces. It
            // IS safe when everything evaluated before the load is itself pure
            // (effect-free and non-throwing) — the deferral then crosses nothing
            // observable. This is the spilled receiver-then-value shape a field or
            // array store leaves behind: `this._f = o ?? new()` spills `this` and
            // the coalesce across the `??` branch, and once the receiver spill
            // collapses to a pure `this`/argument load the value spill follows it
            // back into the store (a value-type `this` stays impure, so a struct
            // receiver keeps the value spilled). Restricted to synthetic stack
            // slots — the compiler's spill scratch. A user local carries source
            // meaning and later passes (foreach, deconstruction, merged-slot
            // naming) reshape constructs around it, so deferring one changes
            // already-raised code and drops its source name. Unconditional
            // evaluation is already established above (the non-pure guard), so
            // only the ordering and type-witness conditions remain here. A slot
            // whose stored value type differs from the type at which it is loaded
            // carries a type reconciliation the materialized `T S_n = ...`
            // declaration would spell (e.g. an object-merged ternary narrowed to
            // an unresolved, possibly value-type, target); inlining would drop
            // that witness, so require the two to agree — the analogue of the
            // StoreLocal type witness guard above.
            bool precedingPure = isSlot && !firstLeaf && !pure
                && store is StoreStackSlot { Value.ResultType: { } slotValueType }
                && load is LoadStackSlot { ResultType: { } slotLoadType }
                && slotValueType.Equals(slotLoadType)
                && PrecedingEvaluationIsPure(load, next, locals, argumentAddresses, function);
            if (!firstLeaf && !pure && !precedingPure)
                continue;  // inlining would move the computation past whatever evaluates before the load
            // Purity proves the value has no effect and cannot throw, but a value
            // deferred to a NON-first-leaf load also moves past `next`'s prefix.
            // If that prefix writes a place the value reads, the deferred value
            // would observe the mutated place. A pure value reads only arguments
            // and locals (IsPure admits nothing else), so guard exactly those
            // against any conflicting write anywhere in `next` — a store, a
            // compound assignment, an increment, a `ref`/`out` escape, a catch or
            // loop or pattern binding (#3133 adversarial review; see Writes).
            if (!firstLeaf && DefersPastConflictingWrite(store is StoreLocal s2 ? s2.Value : ((StoreStackSlot)store).Value, next))
                continue;

            var value = (IrExpression)store.DetachChildren()[0];

            context.Stepper.StepOver(
                $"inline {(isSlot ? "stack slot" : "local")} {(store is StoreLocal s ? s.Index : ((StoreStackSlot)store).Slot)} into its single use",
                load);

            store.Detach();
            load.ReplaceWith(value);
            return true;
        }
        return false;
    }

    static bool IsLockObject(IrNode node)
        => node.Parent is Lock lockNode && ReferenceEquals(lockNode.LockObject, node);

    // A place the dataflow tracks: a method argument, a local, or a stack slot.
    enum PlaceKind { Argument, Local, Slot }

    /// <summary>
    /// One inline of a movable value into the single load it reaches within its
    /// live range — the spilled-call-chain collapse. Unlike <see cref="InlineOnce"/>
    /// this tolerates a slot that is reused (several stores/loads function-wide)
    /// and a use that sits past interleaved statements, by reasoning over the
    /// store's live range instead of function-wide counts.
    /// </summary>
    static bool InlineLiveRangeOnce(IrFunction function, PassContext context)
    {
        var argumentAddresses = new HashSet<int>();
        var addressTakenLocals = new HashSet<int>();
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadArgumentAddress a: argumentAddresses.Add(a.Index); break;
                case StoreArgument s: argumentAddresses.Add(s.Index); break;
                case LoadLocalAddress a: addressTakenLocals.Add(a.Index); break;
            }
        }

        foreach (var block in function.Descendants.OfType<Block>())
        {
            for (int si = 0; si < block.Children.Count; si++)
            {
                // Only stack slots — the compiler's spill scratch — are inlined
                // here. A user local carries source meaning and the structuring
                // passes shape constructs (??=, deconstruction) around it; moving
                // those reshapes already-raised code into a different opcode stream.
                if (block.Children[si] is not StoreStackSlot store)
                    continue;
                var (targetKind, targetIndex, value) = (PlaceKind.Slot, store.Slot, store.Value);
                if (!TryMovableReads(value, out var reads))
                    continue;
                if (reads.Any(r => !ReadIsStable(r, argumentAddresses, addressTakenLocals, function)))
                    continue;
                // Confine to a slot whose every store and load lives in this one
                // block. Reaching-definition analysis across blocks is what the
                // simple mode's function-wide single-load key stands in for; a
                // block-local slot makes the forward scan below exact, so a store
                // in a successor block (or read back across a loop edge) can never
                // be the definition we silently drop. The spilled call chain is
                // block-local by construction.
                if (!IsConfinedToBlock(function, targetKind, targetIndex, block))
                    continue;

                // Walk forward to the one load this store reaches: the first
                // statement that loads the slot is its use (even if that statement
                // also rewrites the slot — the load on the right evaluates first).
                // Bail on a write to anything the value reads before the use, and
                // require the definition to be dead after that single use (a second
                // load before the slot is rewritten would read a value we moved).
                IrNode? use = null;
                IrNode? useStatement = null;
                bool blocked = false;
                for (int k = si + 1; k < block.Children.Count; k++)
                {
                    var stmt = block.Children[k];
                    var uses = LoadsOf(stmt, targetKind, targetIndex);
                    bool rewritesTarget = Writes(stmt, targetKind, targetIndex);
                    if (uses.Count > 0)
                    {
                        if (use is not null || uses.Count > 1)
                        {
                            blocked = true;       // a second use of this definition
                            break;
                        }
                        use = uses[0];
                        useStatement = stmt;
                        if (rewritesTarget)
                            break;                // redefined in the same statement, after the use
                        continue;
                    }
                    if (rewritesTarget)
                    {
                        blocked = use is null;    // dead before use ⇒ leave it alone
                        break;
                    }
                    if (use is null
                        && (reads.Any(r => Writes(stmt, r.Kind, r.Index))
                            || WritesStaticDelegateTarget(stmt, value)
                            || (RequiresFirstEvaluation(value) && HasObservableEffect(stmt))))
                    {
                        blocked = true;
                        break;
                    }
                }
                if (blocked || use is null || useStatement is null)
                    continue;

                // An increment/decrement target is an lvalue, never a value use;
                // replacing it with the moved expression yields an invalid `1++`
                // (see InlineOnce). Correct-by-construction guard (#2386).
                if (use.Parent is IncrementDecrement)
                    continue;

                // A value that reads places must still evaluate first at the use
                // site; an effect-free value that reads nothing can land anywhere.
                if ((reads.Count > 0 || RequiresFirstEvaluation(value)) && !IsFirstEvaluatedLeaf(use, useStatement))
                    continue;

                var inlined = (IrExpression)block.Children[si].DetachChildren()[0];
                context.Stepper.StepOver(
                    $"inline {(targetKind == PlaceKind.Slot ? "stack slot" : "local")} {targetIndex} into its live-range use",
                    use);
                block.Children[si].Detach();
                use.ReplaceWith(inlined);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// A value safe to recompute at a later point: it produces no observable
    /// effect, so reordering it is invisible as long as the places it reads are
    /// unchanged in between (the caller's interference scan). A non-capturing
    /// delegate creation qualifies — its target is the static <c>&lt;&gt;c.&lt;&gt;9</c>
    /// singleton, so it reads nothing the method can mutate.
    /// </summary>
    static bool TryMovableReads(IrExpression value, out List<(PlaceKind Kind, int Index)> reads)
    {
        reads = [];
        switch (value)
        {
            case Constant or SizeOf or LoadToken:
                return true;
            case LoadArgument argument:
                reads.Add((PlaceKind.Argument, argument.Index));
                return true;
            case LoadLocal load:
                reads.Add((PlaceKind.Local, load.Index));
                return true;
            case LoadStackSlot load:
                reads.Add((PlaceKind.Slot, load.Slot));
                return true;
            case DelegateCreation { Target: LoadField { Instance: null } }:
                return true;
            default:
                return false;
        }
    }

    // A read is stable to move only if no escaped address could let an
    // intervening call mutate it: arg whose address is never taken (and not a
    // byref value-type `this`), or a local never addressed. A CONFIRMED
    // reference-type `this` is a plain object reference, so it stays stable.
    static bool ReadIsStable(
        (PlaceKind Kind, int Index) read, HashSet<int> argumentAddresses, HashSet<int> addressTakenLocals, IrFunction function)
        => read.Kind switch
        {
            PlaceKind.Argument => !argumentAddresses.Contains(read.Index)
                && !(function.Signature.HasThis && read.Index == 0 && !ReceiverThisIsPure(function)),
            PlaceKind.Local => !addressTakenLocals.Contains(read.Index),
            _ => true,
        };

    // True when every load and store of the place sits inside one block, so its
    // whole live range is the straight-line statements the forward scan reads.
    static bool IsConfinedToBlock(IrFunction function, PlaceKind kind, int index, Block block)
    {
        foreach (var node in function.Descendants)
        {
            bool touches = kind switch
            {
                PlaceKind.Slot => node is LoadStackSlot ls && ls.Slot == index || node is StoreStackSlot ss && ss.Slot == index,
                PlaceKind.Local => node is LoadLocal ll && ll.Index == index || node is StoreLocal sl && sl.Index == index,
                _ => false,
            };
            if (touches && !ReferenceEquals(EnclosingBlock(node), block))
                return false;
        }
        return true;
    }

    static Block? EnclosingBlock(IrNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (current is Block block)
                return block;
        }
        return null;
    }

    static List<IrNode> LoadsOf(IrNode statement, PlaceKind kind, int index)
        => [.. statement.Descendants.Prepend(statement).Where(n => kind switch
        {
            PlaceKind.Slot => n is LoadStackSlot s && s.Slot == index,
            PlaceKind.Local => n is LoadLocal l && l.Index == index,
            _ => false,
        })];

    // A pure value deferred to a non-first-leaf load must not read a place that
    // `next` writes: enumerate the value's argument/local reads (IsPure admits no
    // other place read) and block if `next` mutates one. Conservative over the
    // whole statement — a write strictly after the load only costs a missed
    // collapse, never correctness.
    static bool DefersPastConflictingWrite(IrExpression value, IrNode next)
    {
        foreach (var node in value.Descendants.Prepend(value))
        {
            switch (node)
            {
                case LoadArgument argument when Writes(next, PlaceKind.Argument, argument.Index): return true;
                case LoadLocal load when Writes(next, PlaceKind.Local, load.Index): return true;
            }
        }
        return false;
    }

    static bool Writes(IrNode statement, PlaceKind kind, int index)
        => statement.Descendants.Prepend(statement).Any(n => WritesNode(n, kind, index));

    // True when a single node binds or mutates the given place. A deferred pure
    // value reads only arguments and locals (IsPure admits nothing else), but the
    // guard also covers slots for the live-range collapse.
    //
    // Completeness is the point here: an omitted writer would let a deferred pure
    // value observe a mutated place (the #3133 adversarial review repeatedly
    // surfaced missing writers — IncrementDecrement, `??=`, tuple deconstruction,
    // catch bindings, foreach/using/fixed headers, and pattern bindings). Two
    // structural facts bound the writer set so it stays complete:
    //   * Every INDIRECT write to a local or argument — a `ref`/`out` call or
    //     constructor argument, an `InitObject`, any store through an address — is
    //     mediated by a `LoadLocalAddress`/`LoadArgumentAddress` node (see
    //     DefiniteAssignment.IsVerifiedOutLocal). Detecting an escaped address
    //     covers all of them without enumerating callee shapes.
    //   * Every DIRECT binding writer names its place through a `LocalIndex` /
    //     `VariableIndex` on the node itself. BindingWriterCoverageTests pins that
    //     the node types carrying such a binding index are exactly those handled
    //     below, so a newly added binding node fails that test until it is added
    //     here.
    static bool WritesNode(IrNode n, PlaceKind kind, int index) => kind switch
    {
        PlaceKind.Slot =>
            (n is StoreStackSlot s && s.Slot == index)
            || (n is IncrementDecrement { Target: LoadStackSlot isl } && isl.Slot == index),
        PlaceKind.Local =>
            (n is StoreLocal l && l.Index == index)
            || (n is IncrementDecrement { Target: LoadLocal il } && il.Index == index)
            || (n is LoadLocalAddress la && la.Index == index)
            || (n is NullCoalescingAssignment nca && nca.LocalIndex == index)
            || (n is DeconstructionAssignment dal && dal.Targets.Any(t => t.Kind == DeconstructionTargetKind.Local && t.LocalIndex == index))
            || (n is CatchClause cc && cc.VariableIndex == index)
            || (n is Fixed fx && fx.LocalIndex == index)
            || (n is UsingStatement us && us.LocalIndex == index)
            || (n is ForeachStatement fe && fe.LocalIndex == index)
            || (n is IsPattern ip && ip.LocalIndex == index)
            || (n is RecursivePropertyDeclarationPattern rp && rp.LocalIndex == index)
            || (n is UnionSwitchExpressionArm ua && ua.LocalIndex == index)
            || (n is PatternSwitchExpressionArm pa && (pa.LocalIndex == index || pa.Subpattern?.LocalIndex == index)),
        PlaceKind.Argument =>
            (n is StoreArgument a && a.Index == index)
            || (n is IncrementDecrement { Target: LoadArgument ia } && ia.Index == index)
            || (n is LoadArgumentAddress aa && aa.Index == index)
            || (n is DeconstructionAssignment daa && daa.Targets.Any(t => t.Kind == DeconstructionTargetKind.Argument && t.ArgumentIndex == index)),
        _ => false,
    };

    static bool WritesStaticDelegateTarget(IrNode statement, IrExpression value)
        => value is DelegateCreation { Target: LoadField { Instance: null, Field: var field } }
            && statement.Descendants.Prepend(statement)
                .OfType<StoreField>()
                .Any(store => !store.HasInstance && store.Field.Equals(field));

    static bool ReadsStaticDelegateTarget(IrExpression value)
        => value is DelegateCreation { Target: LoadField { Instance: null } };

    static bool RequiresFirstEvaluation(IrExpression value)
        => value is DelegateCreation { Target: LoadField { Instance: null }, Method: var method }
            && !GeneratedCodeIdentity.IsNonCapturingLambdaMethod(method);

    static bool HasObservableEffect(IrNode statement)
        => statement.Descendants.Prepend(statement).Any(static node => node is
            Call or CallIndirect or NewObject or DelegateCreation or StoreField or StoreProperty or StoreElement or Throw);

    /// <summary>
    /// The first statement of the block after <paramref name="block"/>, when
    /// fallthrough is its only incoming edge: no branch targets it, and both
    /// blocks sit in exactly the same exception regions — physical adjacency
    /// across a region boundary is not normal control flow, and moving a
    /// computation across one changes which instructions are protected.
    /// </summary>
    static IrNode? FallthroughFirstStatement(IrFunction function, Block block)
    {
        // The block's own container, not function.Body: after EH structuring
        // blocks live in nested containers, and indexing the top-level list
        // with a nested ChildIndex would alias an unrelated block. Staying
        // inside one container also keeps the edge inside one region.
        if (block.Parent is not BlockContainer container)
            return null;
        var blocks = container.Blocks;
        int index = block.ChildIndex;
        if (index + 1 >= blocks.Count)
            return null;
        var following = blocks[index + 1];
        if (following.Children.Count == 0)
            return null;
        if (!SameRegions(function, block.StartOffset, following.StartOffset))
            return null;
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case Branch b when b.TargetOffset == following.StartOffset:
                case ConditionalBranch c when c.TargetOffset == following.StartOffset:
                case Leave l when l.TargetOffset == following.StartOffset:
                    return null;
                case SwitchBranch s when s.TargetOffsets.Contains(following.StartOffset):
                    return null;
            }
        }
        return following.Children[0];
    }

    /// <summary>True when both offsets sit inside exactly the same try, handler, and filter ranges.</summary>
    static bool SameRegions(IrFunction function, int offsetA, int offsetB)
    {
        foreach (var region in function.Regions)
        {
            if (Inside(offsetA, region.TryOffset, region.TryLength) != Inside(offsetB, region.TryOffset, region.TryLength))
                return false;
            if (Inside(offsetA, region.HandlerOffset, region.HandlerLength) != Inside(offsetB, region.HandlerOffset, region.HandlerLength))
                return false;
            if (region.Kind == HandlerKind.Filter
                && Inside(offsetA, region.FilterOffset, region.HandlerOffset - region.FilterOffset)
                    != Inside(offsetB, region.FilterOffset, region.HandlerOffset - region.FilterOffset))
            {
                return false;
            }
        }
        return true;

        static bool Inside(int offset, int start, int length) => offset >= start && offset < start + length;
    }

    static bool IsInsideCatchFilter(IrNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            if (current is CatchClause clause)
                return clause.Filter is { } filter && ReferenceOwnership.IsInside(node, filter);
        }
        return false;
    }

    /// <summary>True when the node is the first thing the statement evaluates: the spine of first children.</summary>
    static bool IsFirstEvaluatedLeaf(IrNode node, IrNode statement)
    {
        var current = statement;
        while (current.Children.Count > 0)
        {
            current = current.Children[0];
            if (ReferenceEquals(current, node))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True when <c>this</c> (arg 0) of an instance method loads a plain,
    /// non-reassignable object reference rather than a byref managed pointer —
    /// i.e. the declaring type is a CONFIRMED reference type. The only
    /// value-type bases are the corelib <c>System.ValueType</c> (struct) and
    /// <c>System.Enum</c> (enum); any other resolved base is a class. An
    /// unresolved base (<c>null</c>, including <c>System.Object</c> itself) stays
    /// conservative — treated as possibly byref, so the receiver is not moved.
    /// </summary>
    static bool ReceiverThisIsPure(IrFunction function)
        => function.BaseType is { } baseType
            && baseType is not { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "ValueType" or "Enum" };

    /// <summary>
    /// True when everything evaluated before <paramref name="load"/> within
    /// <paramref name="statement"/> is pure — effect-free and non-throwing.
    /// Walks the path from the statement root down to the load; at each level the
    /// children before the path-child are its left siblings, fully evaluated
    /// before the load, so each must be pure. The operations ON the path normally
    /// sit above the load and execute AFTER their operands, so they are not part
    /// of the preceding evaluation. Nodes that instead perform a hidden operation
    /// before or between their children (a record clone, an object construction,
    /// a container allocation, interleaved handler appends) are rejected earlier
    /// by the universal <see cref="LoadCrossesHiddenOperation"/> guard, so this
    /// walk only has to prove the left siblings pure.
    /// </summary>
    static bool PrecedingEvaluationIsPure(
        IrNode load,
        IrNode statement,
        Dictionary<(bool IsSlot, int Index), (List<IrNode> Loads, List<IrNode> Stores, bool AddressTaken)> locals,
        HashSet<int> argumentAddresses,
        IrFunction function)
    {
        var node = statement;
        while (!ReferenceEquals(node, load))
        {
            IrNode? onPath = null;
            foreach (var child in node.Children)
            {
                if (ReferenceEquals(child, load) || ReferenceOwnership.IsInside(load, child))
                {
                    onPath = child;
                    break;
                }
                if (child is not IrExpression expression || !IsPure(expression, locals, argumentAddresses, function))
                    return false;
            }
            if (onPath is null)
                return false;
            node = onPath;
        }
        return true;
    }

    /// <summary>
    /// True when <paramref name="parent"/> performs an observable operation
    /// before or between its children, up to and including <paramref name="child"/>,
    /// so a non-pure value moved into <paramref name="child"/> crosses that
    /// operation. This is the third soundness hazard for the deferred fold — after
    /// impure left siblings (<see cref="PrecedingEvaluationIsPure"/>) and
    /// conditional evaluation (<see cref="LoadIsUnconditionallyEvaluated"/>) —
    /// and the only one that also affects the <c>firstLeaf</c> path, because these
    /// operations run BEFORE the first evaluated child rather than after the last.
    ///
    /// <para><c>receiver with { M = v }</c> clones the receiver (a possibly
    /// effectful or throwing copy constructor, and the clone snapshots the
    /// receiver's other fields) before evaluating any initializer value, so only
    /// the receiver is preceding-operation-free. <c>new T(args) { M = v }</c> runs
    /// the constructor before the initializers (the creation is itself non-pure,
    /// so the left-sibling-pure check already blocks it, but reject explicitly so
    /// the guard does not depend on that).</para>
    ///
    /// <para>A <c>$"...{a}...{b}..."</c> interpolated string constructs a
    /// <c>DefaultInterpolatedStringHandler</c> and appends each literal and
    /// formatted value in turn, INTERLEAVED with hole evaluation: the append of an
    /// earlier hole runs before a later hole is evaluated, and appending an
    /// earlier <see cref="IFormattable"/> or custom-handler value runs arbitrary
    /// user code. A <c>[a, b, ..s]</c> collection expression, a
    /// <c>new T[] { a, b }</c> array literal, a <c>ReadOnlySpan&lt;T&gt;</c> span
    /// literal, and the initializer form of a <c>stackalloc T[n] { a, b }</c>
    /// allocate the container (<c>newobj</c> list / <c>newarr</c> / <c>CreateSpan</c>
    /// / <c>localloc</c>) before evaluating any element, and a collection expression
    /// may additionally interleave <c>Add</c> calls or spread enumeration
    /// (<c>MoveNext</c>, user code) between elements. For all of these, no element
    /// is preceding-operation-free. A <c>stackalloc</c>'s <c>count</c> is the
    /// exception: it is evaluated before the <c>localloc</c>, so it stays foldable.</para>
    ///
    /// <para>The container allocation before the FIRST element runs no user code,
    /// but it is still program-observable: it reserves an object identity and can
    /// rent a pooled buffer, so a non-pure value that reads allocation order (e.g.
    /// <c>ArrayPool&lt;char&gt;.Shared.Rent</c> identity) or that runs only if the
    /// allocation did not first throw observes the reordering (#3500 adversarial
    /// review, GPT + Gemini). Rejecting the first element is therefore correct,
    /// not merely conservative, and measured to cost nothing on the corpus.</para>
    ///
    /// <para>Multi-child expressions whose own operation runs strictly AFTER all
    /// operands are NOT hidden-operation nodes and keep the <c>false</c> default:
    /// a call / <c>new T(args)</c> / local-function invocation / indexer pushes its
    /// arguments then invokes (the <c>newobj</c>/<c>call</c> is last),
    /// <see cref="AnonymousObject"/> and a tuple literal construct after all element
    /// values, and a plain <c>new T[n]</c> / <c>stackalloc T[n]</c> without an
    /// initializer evaluates its size before the single allocation. Every other
    /// expression's own operation runs strictly after all of its operands, so the
    /// default is <c>false</c>.</para>
    /// </summary>
    static bool ChildFollowsHiddenOperation(IrNode parent, IrNode child) => parent switch
    {
        WithExpression w => !ReferenceEquals(child, w.Receiver),
        ObjectInitializerExpression o => !ReferenceEquals(child, o.Creation),
        InterpolatedStringExpression => true,
        CollectionExpression => true,
        ArrayLiteral => true,
        SpanLiteral => true,
        StackAllocArray s => !ReferenceEquals(child, s.Count),
        _ => false,
    };

    /// <summary>
    /// Walks the path from <paramref name="statement"/> to <paramref name="load"/>
    /// and returns true if any parent performs a hidden operation before or between
    /// the on-path child (see <see cref="ChildFollowsHiddenOperation"/>). Universal:
    /// runs ahead of both the <c>firstLeaf</c> and <c>precedingPure</c> paths,
    /// because a container's allocation and interleaved appends precede even its
    /// first child, so the firstLeaf path is not exempt.
    /// </summary>
    static bool LoadCrossesHiddenOperation(IrNode load, IrNode statement)
    {
        var node = statement;
        while (!ReferenceEquals(node, load))
        {
            IrNode? onPath = null;
            foreach (var child in node.Children)
            {
                if (ReferenceEquals(child, load) || ReferenceOwnership.IsInside(load, child))
                {
                    onPath = child;
                    break;
                }
            }
            if (onPath is null)
                return false;
            if (ChildFollowsHiddenOperation(node, onPath))
                return true;
            node = onPath;
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="load"/> is evaluated on every execution of
    /// <paramref name="statement"/> — no ancestor between the statement root and
    /// the load evaluates its path-child conditionally. Walks the path and
    /// rejects at the first short-circuiting or branching parent
    /// (<c>?:</c>, <c>??</c>, <c>&amp;&amp;</c>/<c>||</c>, <c>?.</c>, switch
    /// expressions, <c>??=</c>) whose evaluated child is not its always-evaluated
    /// leading operand. The spilled store this pass folds was an unconditional
    /// statement, so its value must land in an unconditional position; inlining
    /// it into a guarded arm would change a non-pure value from always-run to
    /// conditionally-run (#3500 adversarial review).
    /// </summary>
    static bool LoadIsUnconditionallyEvaluated(IrNode load, IrNode statement)
    {
        var node = statement;
        while (!ReferenceEquals(node, load))
        {
            IrNode? onPath = null;
            foreach (var child in node.Children)
            {
                if (ReferenceEquals(child, load) || ReferenceOwnership.IsInside(load, child))
                {
                    onPath = child;
                    break;
                }
            }
            if (onPath is null)
                return false;
            // Below the root statement the path must stay within operand
            // expressions. A nested statement, block, container, catch/finally
            // region, or switch arm is a control-flow boundary whose body may not
            // run on every entry; rather than enumerate them, reject any
            // non-expression parent below the root.
            if (!ReferenceEquals(node, statement) && node is not IrExpression)
                return false;
            if (!ChildIsUnconditional(node, onPath))
                return false;
            node = onPath;
        }
        return true;
    }

    /// <summary>
    /// True when <paramref name="parent"/> evaluates <paramref name="child"/> on
    /// every evaluation of itself. This is a DENYLIST: the overwhelming majority
    /// of expression forms evaluate all of their operands left-to-right and
    /// unconditionally, so the default is <c>true</c>; only the short-circuiting,
    /// branching, and null-coalescing forms defer a child, and each defers only
    /// past its always-evaluated leading operand (the condition, the
    /// <c>??</c>/<c>&amp;&amp;</c>/<c>||</c> left operand, the switch scrutinee, or
    /// the <c>??=</c> receiver/index). A <c>?.</c> guards its whole member
    /// subtree, so none of its children is unconditional. The three
    /// <see cref="IrNode"/> <c>??=</c> statement forms appear here too because one
    /// can be the walk's root <paramref name="statement"/>, which the caller's
    /// non-expression guard exempts. Non-root control boundaries (blocks, loops,
    /// try regions, switch arms, lambda bodies) are rejected by that guard, and
    /// pattern nodes need no case because their only non-scrutinee children are
    /// pure <see cref="Constant"/>s. Tuple <c>==</c>/<c>!=</c>
    /// (<see cref="TupleBinaryExpression"/>) is deliberately absent: C# evaluates
    /// every element of both operand tuples before any element comparison, so its
    /// components are unconditional (verified by compiled canary, #3500).
    /// </summary>
    static bool ChildIsUnconditional(IrNode parent, IrNode child) => parent switch
    {
        Conditional c => ReferenceEquals(child, c.Condition),
        Coalesce c => ReferenceEquals(child, c.Left),
        LogicalBinary l => ReferenceEquals(child, l.Left),
        NullConditional => false,
        SwitchExpression s => ReferenceEquals(child, s.Value),
        PatternSwitchExpression s => ReferenceEquals(child, s.Value),
        UnionSwitchExpression s => ReferenceEquals(child, s.Value),
        TupleSwitchExpression s => s.Components.Any(component => ReferenceEquals(component, child)),
        NullCoalescingAssignment => false,
        NullCoalescingFieldAssignment f => ReferenceEquals(child, f.Instance),
        NullCoalescingFieldAssignmentExpression f => ReferenceEquals(child, f.Instance),
        NullCoalescingPropertyAssignment p => !ReferenceEquals(child, p.Value),
        _ => true,
    };

    /// <summary>Expressions whose evaluation cannot observe or produce effects, so reordering them is invisible.</summary>
    static bool IsPure(
        IrExpression value,
        Dictionary<(bool IsSlot, int Index), (List<IrNode> Loads, List<IrNode> Stores, bool AddressTaken)> locals,
        HashSet<int> argumentAddresses,
        IrFunction function) => value switch
    {
        Constant or SizeOf or LoadToken => true,
        // Reads are reorder-safe only if nothing can mutate the place from
        // inside an expression: stores are statement-level in this IR, so
        // the remaining hazard is a call writing through an escaped address.
        // For instance methods, arg 0 is a byref managed pointer only for a
        // value-type receiver, which any instance call can mutate through; for
        // a CONFIRMED reference type it is a plain, non-reassignable object
        // reference, so the receiver load is pure. An unknown/unresolved
        // declaring type stays conservative (treated as possibly byref).
        LoadArgument argument => !argumentAddresses.Contains(argument.Index)
            && !(function.Signature.HasThis && argument.Index == 0 && !ReceiverThisIsPure(function)),
        LoadLocal load => !locals.TryGetValue((false, load.Index), out var entry) || !entry.AddressTaken,
        // Side-effect-free, non-throwing composites: pure iff every operand is
        // pure. Purity here must imply "cannot throw" as well as "no effect",
        // because a pure value is deferred to its load site — moving a value
        // that could throw past a prefix that could also throw (or have an
        // effect) would change which exception surfaces. So division and
        // remainder (DivideByZero/Overflow) and checked arithmetic/conversions
        // (Overflow) are excluded; neg/not, bitwise/shift, unchecked
        // arithmetic, comparisons, and conditionals never throw.
        LogicalNot not => IsPure(not.Operand, locals, argumentAddresses, function),
        Unary unary => IsPure(unary.Operand, locals, argumentAddresses, function),
        Comparison comparison =>
            IsPure(comparison.Left, locals, argumentAddresses, function)
            && IsPure(comparison.Right, locals, argumentAddresses, function),
        LogicalBinary logical =>
            IsPure(logical.Left, locals, argumentAddresses, function)
            && IsPure(logical.Right, locals, argumentAddresses, function),
        Binary { Kind: not (BinaryKind.Divide or BinaryKind.Remainder), IsChecked: false } binary =>
            IsPure(binary.Left, locals, argumentAddresses, function)
            && IsPure(binary.Right, locals, argumentAddresses, function),
        Conditional conditional =>
            IsPure(conditional.Condition, locals, argumentAddresses, function)
            && IsPure(conditional.WhenTrue, locals, argumentAddresses, function)
            && IsPure(conditional.WhenFalse, locals, argumentAddresses, function),
        _ => false,
    };
}
