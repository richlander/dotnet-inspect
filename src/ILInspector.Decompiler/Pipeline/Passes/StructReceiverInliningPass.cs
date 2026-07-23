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
/// <item>The slot is <em>fold-safe</em> method-wide: every read of it is a
/// member-receiver address load whose enclosing statement immediately follows a
/// store to that same slot in the same block. Then each read's reaching
/// definition is the store one statement above it, so the slot is never live at
/// a block boundary and no loop back-edge or conditional bypass can route a
/// stale value to a read (a reused slot — csc packs independent temporaries into
/// one slot — folds per store/use pair because each pair is independently
/// adjacent).</item>
/// <item>The use is a member-access receiver — the instance of a
/// <see cref="LoadProperty"/> or <see cref="LoadField"/>, or the receiver of an
/// instance <see cref="Call"/> (all reads through the receiver; a write place
/// would be an illegal assignment to an rvalue member).</item>
/// <item>The use sits in the statement immediately after the store, is the only
/// read of the slot in that statement, is not in a short-circuit/ternary
/// sub-position, and nothing order-sensitive evaluates before it in that
/// statement — so the value's evaluation crosses no effect.</item>
/// </list>
///
/// <para>Store/use adjacency in one block guarantees co-execution: a branch
/// target between them would have split the block, so the pair is never entered
/// apart. The fold reasons only about basic-block-local adjacency — never
/// document order, which is an unsound liveness proxy across loop back-edges
/// (a store re-reached at the loop head) and conditional bypasses (a read
/// reached past a skipped redefinition). Requiring <em>every</em> read of the
/// slot to be a store-adjacent receiver makes each read locally defined, so the
/// fold is sound without whole-CFG liveness analysis.</para>
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
        // Every read of every slot, so the fold can require a slot to be
        // fold-safe method-wide before touching any of its stores. Reads are
        // grouped by slot index; a value load (LoadLocal) and an address load
        // (LoadLocalAddress) are both reads.
        var reads = new Dictionary<int, List<IrNode>>();
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadLocal load:
                    (reads.TryGetValue(load.Index, out var r) ? r : reads[load.Index] = []).Add(load);
                    break;
                case LoadLocalAddress address:
                    (reads.TryGetValue(address.Index, out var a) ? a : reads[address.Index] = []).Add(address);
                    break;
            }
        }

        foreach (var node in function.Descendants)
        {
            if (node is not StoreLocal store || store.Parent is not Block block)
                continue;
            if (!IsRvalueDefinition(store.Value))
                continue;
            if (!reads.TryGetValue(store.Index, out var slotReads))
                continue;

            // Fold-safety is a whole-slot property: every read of the slot must be
            // a member-receiver address load whose statement immediately follows a
            // store to the slot in the same block. Then each read's reaching
            // definition is the store directly above it, the slot is dead at every
            // block boundary, and neither a loop back-edge nor a conditional
            // bypass can carry a stale value to a read. This replaces the unsound
            // document-order live-range window: no read is ever "far" from its
            // definition, so folding a store into its adjacent use cannot strand
            // another read of the same value.
            if (!IsSlotFoldSafe(store.Index, slotReads))
                continue;

            // Adjacency: the use must live in the statement immediately after the
            // store, in the same block, and be the slot's only read there (two
            // reads would need the value evaluated twice). That makes store and
            // use co-execute and bounds the effect-order proof to one statement.
            int storeIndex = store.ChildIndex;
            if (storeIndex + 1 >= block.Children.Count)
                continue;
            var useStatement = block.Children[storeIndex + 1];

            IrNode? addressUse = null;
            bool ambiguous = false;
            foreach (var read in slotReads)
            {
                if (!ReferenceOwnership.IsInside(read, useStatement))
                    continue;
                if (addressUse is not null)
                {
                    ambiguous = true;
                    break;
                }
                addressUse = read;
            }
            if (ambiguous || addressUse is not LoadLocalAddress)
                continue;
            if (!IsMemberReceiver(addressUse))
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
    /// True when every read of <paramref name="index"/> is a member-receiver
    /// address load whose enclosing statement is immediately preceded, in the same
    /// block, by a <see cref="StoreLocal"/> to the same slot. Under that property
    /// each read's unique reaching definition is the adjacent store (a block is a
    /// basic block, so the store one statement above always executes first), the
    /// slot is never live across a block boundary, and every store's value is read
    /// only by the use(s) directly below it. A reused slot stays foldable because
    /// each store/use pair is checked independently. A single stray read — a value
    /// load, a byref argument, a loop-header read, a post-<c>if</c> read — fails
    /// the check and declines the whole slot, since document order cannot prove
    /// such a read is not reached by the store being removed.
    /// </summary>
    static bool IsSlotFoldSafe(int index, List<IrNode> slotReads)
    {
        foreach (var read in slotReads)
        {
            if (read is not LoadLocalAddress || !IsMemberReceiver(read))
                return false;
            if (!FollowsStoreToSlot(read, index))
                return false;
        }
        return true;
    }

    /// <summary>
    /// True when the block-level statement enclosing <paramref name="read"/> is
    /// immediately preceded in its block by a <see cref="StoreLocal"/> to
    /// <paramref name="index"/>. The enclosing statement is the ancestor whose
    /// parent is a <see cref="Block"/>.
    /// </summary>
    static bool FollowsStoreToSlot(IrNode read, int index)
    {
        var statement = read;
        while (statement.Parent is { } parent and not Block)
            statement = parent;
        if (statement.Parent is not Block block)
            return false;
        int position = statement.ChildIndex;
        return position > 0
            && block.Children[position - 1] is StoreLocal store
            && store.Index == index;
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
