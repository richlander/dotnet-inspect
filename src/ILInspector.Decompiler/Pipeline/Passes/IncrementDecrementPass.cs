namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Folds the compiler's <c>dup</c>-based increment/decrement idiom back into the
/// <c>++x</c>/<c>x++</c>/<c>--x</c>/<c>x--</c> operator the source spelled.
///
/// A pre/post increment or decrement used as a value — <c>a[--i] = src[j++];</c>,
/// <c>return list[index++];</c> — lowers to a <c>dup</c>: the updated (or
/// pre-update) value is duplicated, one copy stored to the local and the other
/// consumed in place. The importer raises the <c>dup</c> into a single-use stack
/// slot, leaving a three-statement shape:
///
/// <code>
/// S = x;      x = S + 1;   ... use S ...   ≡   ... x++ ...   (post-increment)
/// S = x - 1;  x = S;       ... use S ...   ≡   ... --x ...   (pre-decrement)
/// </code>
///
/// The slot capture and the local update spill the value into two extra locals
/// on recompile (no <c>dup</c>); folding them into the operator at the use site
/// restores the <c>dup</c> exactly. The fold only fires when the captured value
/// is read once downstream and the local is untouched between its update and
/// that read, so moving the side effect to the use site reorders nothing.
/// </summary>
public sealed class IncrementDecrementPass : IIrPass
{
    public string Name => "increment-decrement";

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOnce(function))
        {
        }
    }

    static bool FoldOnce(IrFunction function)
    {
        foreach (var block in function.Descendants.OfType<Block>())
        {
            for (int i = 0; i + 1 < block.Children.Count; i++)
            {
                if (TryFold(function, block, i))
                    return true;
            }
        }
        return false;
    }

    readonly record struct PlaceRef(bool IsLocal, int Index, string Name, TypeRef Type);

    static bool TryFold(IrFunction function, Block block, int i)
    {
        if (block.Children[i] is not StoreStackSlot slotStore)
            return false;
        var update = block.Children[i + 1];
        if (PlaceOf(update) is not { } place || StoreValue(update) is not { } updateValue)
            return false;

        int slot = slotStore.Slot;
        bool isIncrement;
        bool isPrefix;

        if (IsPlaceLoad(slotStore.Value, place)
            && updateValue is Binary { IsChecked: false, Kind: var postKind } post
            && post.Left is LoadStackSlot postLoad && postLoad.Slot == slot
            && post.Right is Constant { Value: 1 }
            && postKind is BinaryKind.Add or BinaryKind.Subtract)
        {
            // S = x; x = S ± 1;  →  x++ / x--
            isPrefix = false;
            isIncrement = postKind is BinaryKind.Add;
        }
        else if (slotStore.Value is Binary { IsChecked: false, Kind: var preKind } pre
            && IsPlaceLoad(pre.Left, place)
            && pre.Right is Constant { Value: 1 }
            && preKind is BinaryKind.Add or BinaryKind.Subtract
            && updateValue is LoadStackSlot preLoad && preLoad.Slot == slot)
        {
            // S = x ± 1; x = S;  →  ++x / --x
            isPrefix = true;
            isIncrement = preKind is BinaryKind.Add;
        }
        else
        {
            return false;
        }

        // The dup slot must be written once and read exactly twice: the local
        // update above, plus one downstream consumer.
        var loads = function.Descendants.OfType<LoadStackSlot>().Where(l => l.Slot == slot).ToList();
        if (function.Descendants.OfType<StoreStackSlot>().Count(s => s.Slot == slot) != 1)
            return false;
        if (loads.Count != 2 || loads.Count(l => IsInside(l, update)) != 1)
            return false;
        if (loads.FirstOrDefault(l => !IsInside(l, update)) is not { } useLoad)
            return false;

        // The consumer must be a later statement in this same block.
        if (StatementOf(useLoad, block) is not { } useStatement)
            return false;
        int useIndex = useStatement.ChildIndex;
        if (useIndex <= i + 1)
            return false;

        // Moving the ±1 to the use site is sound only if the place is neither
        // read nor written between its update and that use — otherwise an
        // intervening access would observe the pre-fold (already updated) value.
        for (int k = i + 2; k <= useIndex; k++)
        {
            if (ReferencesPlace(block.Children[k], place))
                return false;
        }

        useLoad.ReplaceWith(new IncrementDecrement(ClonePlace(place), isIncrement, isPrefix));
        update.Detach();
        slotStore.Detach();
        return true;
    }

    static PlaceRef? PlaceOf(IrNode store) => store switch
    {
        StoreLocal s => new PlaceRef(true, s.Index, "", s.Type),
        StoreArgument s => new PlaceRef(false, s.Index, s.Name, s.Type),
        _ => null,
    };

    static IrExpression? StoreValue(IrNode store) => store switch
    {
        StoreLocal s => s.Value,
        StoreArgument s => s.Value,
        _ => null,
    };

    static bool IsPlaceLoad(IrExpression expression, PlaceRef place) => place.IsLocal
        ? expression is LoadLocal local && local.Index == place.Index
        : expression is LoadArgument argument && argument.Index == place.Index;

    static IrExpression ClonePlace(PlaceRef place) => place.IsLocal
        ? new LoadLocal(place.Index, place.Type)
        : new LoadArgument(place.Index, place.Name, place.Type);

    static bool ReferencesPlace(IrNode node, PlaceRef place)
    {
        foreach (var current in (IEnumerable<IrNode>)[node, .. node.Descendants])
        {
            bool hit = place.IsLocal
                ? current is LoadLocal { } l && l.Index == place.Index
                    || current is StoreLocal { } s && s.Index == place.Index
                    || current is LoadLocalAddress { } a && a.Index == place.Index
                : current is LoadArgument { } la && la.Index == place.Index
                    || current is StoreArgument { } sa && sa.Index == place.Index
                    || current is LoadArgumentAddress { } aa && aa.Index == place.Index;
            if (hit)
                return true;
        }
        return false;
    }

    static IrNode? StatementOf(IrNode node, Block block)
    {
        for (var current = node; current.Parent is not null; current = current.Parent)
        {
            if (ReferenceEquals(current.Parent, block))
                return current;
        }
        return null;
    }

    static bool IsInside(IrNode node, IrNode root)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
                return true;
        }
        return false;
    }
}
