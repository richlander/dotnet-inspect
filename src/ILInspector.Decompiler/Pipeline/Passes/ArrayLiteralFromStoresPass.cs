namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises a compiler-emitted array construction-then-fill sequence — an
/// allocation (<c>T[] tmp = new T[n];</c>) followed, later in the same block,
/// by a contiguous run of index stores in increasing order (<c>tmp[0] = e0;
/// ... tmp[n-1] = e_{n-1};</c>) — into a single <see cref="ArrayLiteral"/> store
/// (<c>T[] tmp = new T[] { e0, ..., e_{n-1} };</c>). This is the general,
/// element-by-element counterpart to <see cref="RvaSpanPass"/>'s RVA-backed
/// blob decode: it covers arrays the compiler fills with computed values rather
/// than a compile-time-embedded byte blob — most commonly a <c>params object[]</c>
/// argument array spilled ahead of the call it feeds.
///
/// <para>The allocation and the fill run need not be adjacent: <c>csc</c>
/// allocates the params array early (receiver/earlier-argument evaluation
/// order) but stores each element only at the point the source actually reads
/// it, which can sit past intervening statements with real effects (a chained
/// call). Moving only the allocation — never an element value — past those
/// statements is safe: an array reference newly allocated and not yet readable
/// from anywhere else has no observable identity or ordering effect, so the
/// combined literal is placed at the position of the fill run (where the
/// element values were actually evaluated), not the original allocation site.
/// </para>
/// </summary>
public sealed class ArrayLiteralFromStoresPass : IIrPass
{
    public string Name => "array-literal-from-stores";

    // A defensive cap: real params/initializer arrays are small (single digits
    // to a few dozen elements). An absurd compile-time length is not a
    // realistic array-literal shape and would otherwise walk a huge run.
    const int MaxLength = 4096;

    public void Run(IrFunction function, PassContext context)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var block in function.Descendants.OfType<Block>().ToList())
            {
                for (int i = 0; i < block.Children.Count; i++)
                {
                    if (TryFold(function, block, i, context))
                    {
                        changed = true;
                        break;  // block's children shifted; rescan next outer loop
                    }
                }
                if (changed)
                    break;
            }
        }
    }

    static bool TryFold(IrFunction function, Block block, int seedIndex, PassContext context)
    {
        (bool IsSlot, int Index, IrExpression Value) place;
        switch (block.Children[seedIndex])
        {
            case StoreLocal { Value: NewArray } sl: place = (false, sl.Index, sl.Value); break;
            case StoreStackSlot { Value: NewArray } ss: place = (true, ss.Slot, ss.Value); break;
            default: return false;
        }
        var newArray = (NewArray)place.Value;
        if (newArray.Length is not Constant { Value: int length } || length is <= 0 or > MaxLength)
            return false;

        // The place's declared element type must match the allocated array's
        // element type. Arrays are covariant (T[] tmp = new U[n]; is legal
        // when U : T), so a store element's value can be a T that is not a
        // valid U — folding into `new U[] { ... }` would spell an initializer
        // whose elements don't typecheck against U, or silently drop the
        // runtime ArrayTypeMismatchException the original stelem could throw.
        var placeElementType = PlaceElementType(block, seedIndex, place);
        if (placeElementType is null || !placeElementType.Equals(newArray.ElementType))
            return false;

        // Find the contiguous fill run somewhere later in the same block: n
        // consecutive StoreElement statements targeting this place, in
        // increasing constant-index order starting at 0.
        int? runStart = null;
        for (int i = seedIndex + 1; i + length <= block.Children.Count; i++)
        {
            if (!IsElementStore(block.Children[i], place, 0))
                continue;
            runStart = i;
            break;
        }
        if (runStart is not { } start)
            return false;
        for (int k = 0; k < length; k++)
        {
            if (!IsElementStore(block.Children[start + k], place, k))
                return false;
        }

        // No fill value may itself read, write, or address the place: an
        // ArrayLiteral evaluates every element before the combined store
        // commits the array reference, so a value that observes the place
        // (e.g. a self-referential `tmp[0] = tmp;`) would read whatever the
        // place held before this statement — not the array being built —
        // once folded.
        for (int k = 0; k < length; k++)
        {
            var value = ((StoreElement)block.Children[start + k]).Value;
            if (value.Descendants.Prepend(value).Any(n => IsLoad(n, place) || IsAddressOrWrite(n, place)))
                return false;
        }

        // Nothing between the allocation and the fill run may write the place,
        // take its address, or even read it — the array reference reaching the
        // fill run must be exactly this allocation, unmutated and unaliased,
        // and no earlier statement may observe the not-yet-filled array (the
        // combined literal's declaration moves to the fill run's position, so
        // an earlier read would otherwise reference the place before its
        // folded declaration).
        for (int i = seedIndex + 1; i < start; i++)
        {
            var statement = block.Children[i];
            if (WritesOrAddresses(statement, place))
                return false;
            if (statement.Descendants.Prepend(statement).Any(n => IsLoad(n, place)))
                return false;
        }

        // After the fill run, the place must escape exactly once more (the
        // real read of the fully-built array) and never be written or
        // addressed again — including another element store into the same
        // array reference, which would mean the fill run is not the array's
        // only mutation.
        int loads = 0;
        foreach (var node in function.Descendants)
        {
            bool outsideRun = !IsInsideRange(node, block, seedIndex, start + length);
            if (outsideRun && (IsAddressOrWrite(node, place) || IsElementStoreArrayLoad(node, place)))
                return false;
            if (outsideRun && IsLoad(node, place))
                loads++;
        }
        if (loads != 1)
            return false;

        context.Stepper.StepOver(
            $"raise array-literal fill of {(place.IsSlot ? "stack slot" : "local")} {place.Index}", block.Children[start]);

        var elementType = newArray.ElementType;
        var arrayType = newArray.ResultType!;
        var detachedElements = new IrExpression[length];
        for (int k = 0; k < length; k++)
        {
            var storeElement = (StoreElement)block.Children[start + k];
            detachedElements[k] = (IrExpression)storeElement.DetachChildren()[2];
        }
        var literal = new ArrayLiteral(elementType, arrayType, detachedElements);
        IrNode combined = place.IsSlot ? new StoreStackSlot(place.Index, literal) : new StoreLocal(place.Index, ArrayLocalType(block, seedIndex), literal);

        // Replace the first fill statement with the combined declaration, drop
        // the remaining fill statements and the original (now-redundant)
        // allocation.
        block.Children[start].ReplaceWith(combined);
        for (int k = length - 1; k >= 1; k--)
            block.Children[start + k].Detach();
        block.Children[seedIndex].Detach();
        return true;
    }

    static TypeRef ArrayLocalType(Block block, int seedIndex)
        => ((StoreLocal)block.Children[seedIndex]).Type;

    // The place's element type as declared at the allocation site. Stack
    // slots carry no independent declared array type (only the allocation's
    // own type reaches every load), so they always match trivially. A local
    // declares its own array type, which — thanks to array covariance — can
    // differ from the allocated array's element type (e.g. `object[] tmp =
    // new string[n];`); folding must decline rather than spell an initializer
    // whose elements don't typecheck against the narrower declared type.
    static TypeRef? PlaceElementType(Block block, int seedIndex, (bool IsSlot, int Index, IrExpression Value) place)
    {
        if (place.IsSlot)
            return ((NewArray)place.Value).ElementType;
        var local = (StoreLocal)block.Children[seedIndex];
        return local.Type is { Kind: TypeRefKind.SzArray, ElementType: { } element } ? element : null;
    }

    static bool IsElementStore(IrNode node, (bool IsSlot, int Index, IrExpression Value) place, int expectedIndex)
        => node is StoreElement { Index: Constant { Value: int idx } } store
            && idx == expectedIndex
            && IsLoadOf(store.Array, place);

    static bool IsLoadOf(IrExpression expr, (bool IsSlot, int Index, IrExpression Value) place)
        => (place.IsSlot, expr) switch
        {
            (true, LoadStackSlot load) => load.Slot == place.Index,
            (false, LoadLocal load) => load.Index == place.Index,
            _ => false,
        };

    static bool IsLoad(IrNode node, (bool IsSlot, int Index, IrExpression Value) place)
        => (place.IsSlot, node) switch
        {
            (true, LoadStackSlot load) => load.Slot == place.Index,
            (false, LoadLocal load) => load.Index == place.Index,
            _ => false,
        };

    static bool IsAddressOrWrite(IrNode node, (bool IsSlot, int Index, IrExpression Value) place)
        => (place.IsSlot, node) switch
        {
            (true, StoreStackSlot store) => store.Slot == place.Index,
            (false, StoreLocal store) => store.Index == place.Index,
            (false, LoadLocalAddress address) => address.Index == place.Index,
            // Structured nodes past this point in the pipeline (after
            // structuring/pattern-raising) can also (re)bind a local index
            // without going through StoreLocal — e.g. a deconstruction target,
            // ??=, a bound is-pattern/recursive-property-pattern local, a
            // foreach/using iteration/resource variable, a catch-clause
            // exception variable, a union-switch-arm pattern local, or a
            // fixed-pointer variable. Any of these binding
            // the place's index between the allocation and the fill run (or
            // after it, before the one expected read) is as much a hazard as
            // an ordinary StoreLocal (adversarial review finding).
            (false, DeconstructionTarget { Kind: DeconstructionTargetKind.Local } target) => target.LocalIndex == place.Index,
            (false, NullCoalescingAssignment assignment) => assignment.LocalIndex == place.Index,
            (false, IsPattern isPattern) => isPattern.LocalIndex == place.Index,
            (false, RecursivePropertyDeclarationPattern pattern) => pattern.LocalIndex == place.Index,
            (false, ForeachStatement foreachStatement) => foreachStatement.LocalIndex == place.Index,
            (false, UsingStatement usingStatement) => usingStatement.LocalIndex == place.Index,
            (false, CatchClause catchClause) => catchClause.VariableIndex == place.Index,
            (false, UnionSwitchExpressionArm arm) => arm.LocalIndex == place.Index,
            (_, Fixed fixedStatement) => fixedStatement.LocalIsStackSlot == place.IsSlot && fixedStatement.LocalIndex == place.Index,
            _ => false,
        };

    static bool WritesOrAddresses(IrNode statement, (bool IsSlot, int Index, IrExpression Value) place)
        => statement.Descendants.Prepend(statement).Any(n => IsAddressOrWrite(n, place) || IsElementStoreArrayLoad(n, place));

    // The array-ref load feeding an element store (inside the fill run) is not
    // itself a hazard, but any OTHER read of the place between allocation and
    // the fill run's start — e.g. an unrelated statement that happens to read
    // the not-yet-filled array — means the place is observed prematurely.
    static bool IsElementStoreArrayLoad(IrNode node, (bool IsSlot, int Index, IrExpression Value) place)
        => node is StoreElement { Array: var array } && IsLoadOf(array, place);

    static bool IsInsideRange(IrNode node, Block block, int startIndex, int endIndexExclusive)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (!ReferenceEquals(current.Parent, block))
                continue;
            return current.ChildIndex >= startIndex && current.ChildIndex < endIndexExclusive;
        }
        return false;
    }
}
