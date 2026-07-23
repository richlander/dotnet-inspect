namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds a single-use struct rvalue temporary back into its member-access
/// receiver: <c>V = obj.Prop; ... V.Member ...</c> → <c>obj.Prop.Member</c>.
///
/// <para>csc cannot take the address of an rvalue, so a member access on a
/// struct-returning property/method receiver (<c>daylight.TimeOfDay.TimeOfDay</c>)
/// is lowered to a spill: <c>stloc V (= daylight.TimeOfDay); ldloca V; call
/// get_TimeOfDay</c>. That leading spill store makes an otherwise-pure guard
/// block impure, which blocks <see cref="StructuringPass"/> from nesting the
/// guard chain — and therefore <see cref="BooleanFoldingPass"/> from
/// recomposing it into a single <c>&amp;&amp;</c> return (issue #3051). In
/// straight-line code it just leaves an ugly <c>T V = a.Prop; ... V.Member</c>.</para>
///
/// <para>This pass runs immediately before <see cref="StructuringPass"/>, once
/// the guard-forming passes have coalesced each spill store and its use into
/// adjacent statements of one block. It matches a <see cref="StoreLocal"/>
/// immediately followed (same block) by the local's single address-load use in
/// a member-receiver position and — when the store's value is a fresh rvalue and
/// the move reorders no effect — replaces the receiver with the value and
/// removes the store. Because C# re-introduces the identical spill when the
/// folded source is recompiled, the round-trip is opcode-preserving.</para>
///
/// <para>Soundness gates (all required):</para>
/// <list type="bullet">
/// <item>The store's value is a value-producing rvalue (a <see cref="Call"/> or
/// <see cref="LoadProperty"/> with a non-<c>ByRef</c> result), never an
/// address/ref/place — a byref temp aliases real storage, so folding could
/// change defensive-copy or write semantics.</item>
/// <item>That value has exactly one reader in its live range: the sole read of
/// the local between this store and the next store to it (in document order,
/// which is fallthrough order for the acyclic guard shape) is the address-load
/// use. A reused slot (csc packs independent temporaries into one slot) folds
/// per live range.</item>
/// <item>The use is a member-access receiver — the instance of a
/// <see cref="LoadProperty"/> or <see cref="LoadField"/>, or the receiver of an
/// instance <see cref="Call"/> (all reads through the receiver; a write place
/// would be an illegal assignment to an rvalue member).</item>
/// <item>The use sits in the statement immediately after the store, is not in a
/// short-circuit/ternary sub-position, and nothing order-sensitive evaluates
/// before it in that statement — so the value's evaluation crosses no effect.</item>
/// </list>
///
/// <para>Store/use adjacency in one block guarantees co-execution: a branch
/// target between them would have split the block, so the pair is never entered
/// apart. That, with the single-reader-in-window check, makes the fold sound
/// without control-flow analysis.</para>
/// </summary>
public sealed class StructReceiverInliningPass : IIrPass
{
    public string Name => "struct-receiver-inlining";

    public void Run(IrFunction function, PassContext context)
    {
        // Fold one site per pass, then re-scan: a chained spill
        // (`V1 = a.P; V0 = V1.Q; ... V0.R`) exposes the earlier store only once
        // the later one is removed, and re-scanning avoids reasoning about the
        // index/document-order shifts a fold causes.
        while (TryFoldOne(function, context))
        {
        }
    }

    static bool TryFoldOne(IrFunction function, PassContext context)
    {
        // Document order (a single pre-order index) doubles as a total order for
        // the live-range window test. A StoreLocal is visited before its value
        // subtree, so a self-referential read inside the value lands inside the
        // window and is (conservatively) counted.
        var docOrder = new Dictionary<IrNode, int>(ReferenceEqualityComparer.Instance);
        var stores = new Dictionary<int, List<int>>();
        var reads = new Dictionary<int, List<(int Pos, IrNode Node, bool IsAddress)>>();
        var blockPositionByOffset = new Dictionary<int, int>();
        var branches = new List<(int TargetOffset, int Position)>();
        int position = 0;
        foreach (var node in function.Descendants)
        {
            docOrder[node] = position;
            switch (node)
            {
                case StoreLocal store:
                    (stores.TryGetValue(store.Index, out var s) ? s : stores[store.Index] = []).Add(position);
                    break;
                case LoadLocal load:
                    (reads.TryGetValue(load.Index, out var r) ? r : reads[load.Index] = []).Add((position, load, false));
                    break;
                case LoadLocalAddress address:
                    (reads.TryGetValue(address.Index, out var a) ? a : reads[address.Index] = []).Add((position, address, true));
                    break;
                case Block b:
                    blockPositionByOffset[b.StartOffset] = position;
                    break;
                case Branch br:
                    branches.Add((br.TargetOffset, position));
                    break;
                case ConditionalBranch cbr:
                    branches.Add((cbr.TargetOffset, position));
                    break;
            }
            position++;
        }

        // Loop back-edges make the forward document-order window an unsound
        // liveness proxy: a store inside a loop can flow, on a later iteration,
        // to a read that precedes it in document order (the top of the loop). A
        // back-edge is a branch whose target block precedes the branch; the loop
        // it closes spans [targetBlockPosition, branchPosition].
        var backEdgeSpans = new List<(int Start, int End)>();
        foreach (var (targetOffset, branchPosition) in branches)
        {
            if (blockPositionByOffset.TryGetValue(targetOffset, out int targetPosition)
                && targetPosition < branchPosition)
            {
                backEdgeSpans.Add((targetPosition, branchPosition));
            }
        }

        foreach (var node in function.Descendants)
        {
            if (node is not StoreLocal store || store.Parent is not Block block)
                continue;
            if (!IsRvalueDefinition(store.Value))
                continue;

            int storePosition = docOrder[store];
            int nextStorePosition = int.MaxValue;
            if (stores.TryGetValue(store.Index, out var storePositions))
            {
                foreach (int p in storePositions)
                {
                    if (p > storePosition && p < nextStorePosition)
                        nextStorePosition = p;
                }
            }

            // Exactly one read of the local in this store's live range, and it is
            // an address load (a value load in range would read the temp we are
            // about to remove).
            if (!reads.TryGetValue(store.Index, out var localReads))
                continue;

            // A back-edge that spans this store can route execution, on a later
            // iteration, back to a read of the local that precedes the store in
            // document order — so folding this store away would leave that
            // loop-carried read observing a stale value. The forward window
            // cannot see that read; decline when any exists inside a spanning
            // loop.
            if (HasLoopCarriedReader(store.Index, storePosition, localReads, backEdgeSpans))
                continue;

            (int Pos, IrNode Node, bool IsAddress)? only = null;
            bool ambiguous = false;
            foreach (var read in localReads)
            {
                if (read.Pos <= storePosition || read.Pos >= nextStorePosition)
                    continue;
                if (only is not null)
                {
                    ambiguous = true;
                    break;
                }
                only = read;
            }
            if (ambiguous || only is not { IsAddress: true } use)
                continue;

            var addressUse = use.Node;
            if (!IsMemberReceiver(addressUse))
                continue;

            // Adjacency: the use must live in the statement immediately after the
            // store, in the same block. That makes store and use co-execute and
            // bounds the effect-order proof to a single statement.
            int storeIndex = store.ChildIndex;
            if (storeIndex + 1 >= block.Children.Count)
                continue;
            var useStatement = block.Children[storeIndex + 1];
            if (!ReferenceOwnership.IsInside(addressUse, useStatement))
                continue;
            if (!EvaluatesFirstWithoutBarrier(addressUse, useStatement))
                continue;

            var value = (IrExpression)store.DetachChildren()[0];
            store.Detach();
            context.Stepper.StepOver(
                $"inline struct rvalue temp {store.Index} into its member receiver",
                addressUse.Parent ?? addressUse);
            addressUse.ReplaceWith(value);
            return true;
        }
        return false;
    }

    /// <summary>
    /// A value the fold may move into a receiver position: a fresh rvalue whose
    /// evaluation reproduces the compiler's own spill on recompile. A method call
    /// or property getter returning a value (never a managed reference) qualifies;
    /// an address/ref/place (<c>ldloca</c>/<c>ldflda</c>/<c>ldelema</c>, a field
    /// or element access) does not — a byref temp aliases real storage, so a
    /// mutating member or an assignment through it would diverge.
    /// </summary>
    static bool IsRvalueDefinition(IrExpression value) => value switch
    {
        Call call => call.ResultType is not { Kind: TypeRefKind.ByRef },
        LoadProperty property => property.ResultType is not { Kind: TypeRefKind.ByRef },
        _ => false,
    };

    /// <summary>
    /// True when a read of the local precedes the store in document order yet sits
    /// inside a loop that also contains the store (a back-edge span covering the
    /// store position). Such a read is re-reached after the store on a later
    /// iteration, so it observes this store's value; removing the store would make
    /// it read a stale value. The forward single-reader window never sees this
    /// read (it is at a lower document position), which is why it is checked
    /// separately.
    /// </summary>
    static bool HasLoopCarriedReader(
        int index,
        int storePosition,
        List<(int Pos, IrNode Node, bool IsAddress)> localReads,
        List<(int Start, int End)> backEdgeSpans)
    {
        foreach (var (start, end) in backEdgeSpans)
        {
            if (start > storePosition || storePosition > end)
                continue;
            foreach (var read in localReads)
            {
                if (read.Pos >= start && read.Pos < storePosition)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when <paramref name="address"/> is the receiver of a member read: the
    /// instance of a property/field load, or the receiver argument of an instance
    /// call. Write places (<see cref="StoreProperty"/>/<see cref="StoreField"/>)
    /// and address-of receivers are excluded — folding an rvalue into them would
    /// assign to, or take the address of, a temporary.
    /// </summary>
    static bool IsMemberReceiver(IrNode address) => address.Parent switch
    {
        LoadProperty property => ReferenceEquals(property.Instance, address),
        LoadField field => ReferenceEquals(field.Instance, address),
        Call call => call.Callee.HasThis
            && call.Arguments.Count > 0
            && ReferenceEquals(call.Arguments[0], address),
        _ => false,
    };

    /// <summary>
    /// True when, walking <paramref name="statement"/> in evaluation order,
    /// <paramref name="target"/> is reached before any order-sensitive node and
    /// without passing through a conditionally-evaluated position (short-circuit,
    /// ternary, coalesce, null-conditional, switch arm). Both conditions ensure
    /// moving an unconditional pre-statement store into <paramref name="target"/>
    /// neither reorders it past an effect nor makes it run conditionally.
    /// </summary>
    static bool EvaluatesFirstWithoutBarrier(IrNode target, IrNode statement)
    {
        for (var current = target.Parent; current is not null && !ReferenceEquals(current, statement); current = current.Parent)
        {
            if (current is Conditional or Coalesce or LogicalBinary or NullConditional
                or SwitchExpression or SwitchExpressionArm)
            {
                return false;
            }
        }

        bool found = false;
        bool safe = true;

        void Visit(IrNode node)
        {
            if (found || !safe)
                return;
            if (ReferenceEquals(node, target))
            {
                found = true;
                return;
            }
            foreach (var child in node.Children)
            {
                Visit(child);
                if (found || !safe)
                    return;
            }
            // Registered post-children: a parent's own effect (a call, a place
            // read) evaluates after its operands, so a target inside those
            // operands is reached first and is safe.
            if (IsOrderSensitive(node))
                safe = false;
        }

        Visit(statement);
        return found && safe;
    }

    /// <summary>
    /// Deny-list: anything but a constant, <c>sizeof</c>, <c>ldtoken</c>, or an
    /// argument load is treated as an evaluation barrier, so a moved value can
    /// never cross it. Mirrors <see cref="SpilledReceiverFold"/> — an unrecognized
    /// node is conservatively a barrier rather than silently reorderable.
    /// </summary>
    static bool IsOrderSensitive(IrNode node)
        => node is not (Constant or SizeOf or LoadToken or LoadArgument);
}
