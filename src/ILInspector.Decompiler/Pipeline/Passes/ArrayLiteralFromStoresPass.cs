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
///
/// <para>A stack-slot place may be a direct copy chain (#9249). The importer
/// mints a fresh slot for every <c>dup</c>, so a multi-element fill arrives as
/// <c>S_256 = new T[n]; S_256[0] = e0; S_257 = S_256; S_257[1] = e1; ...</c>. A
/// synthetic slot whose only store copies a single-store member of the chain
/// names the same array reference, so the chain is one place: every place
/// check applies to the union of its members, the copy statements inside the
/// fold range retire with the raise, and the one escaping read (through any
/// member) reads the allocation's slot. Nothing is retired unless the literal
/// commits. Locals never join a chain, because a typed local can be re-bound or
/// addressed. An element csc evaluated into its own single-store, single-load
/// slot immediately before the element store (a conditional element) joins the
/// run as that element's value, for a local or slot place alike: it evaluates
/// after the effect-free array load and constant index exactly as in the
/// IL.</para>
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
        Place place;
        switch (block.Children[seedIndex])
        {
            case StoreLocal { Value: NewArray } sl: place = new(false, sl.Index, sl.Value); break;
            case StoreStackSlot { Value: NewArray } ss: place = new(true, ss.Slot, ss.Value); break;
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

        // The importer mints a fresh stack slot for every `dup`, so a
        // multi-element `newarr; dup; ldc; e; stelem; dup; ...` fill reaches
        // this pass as a direct copy chain (S_257 = S_256; S_257[1] = e1; ...).
        // A synthetic slot whose only store copies a single-store member of
        // the chain names the same array reference, so the chain is one place:
        // its copy statements inside the fold range retire with the raise, and
        // every place check below applies to the union of its members. Locals
        // never join a chain — a typed local can be re-bound or addressed. The
        // store counts also serve the spilled-element rule, which applies to
        // local and slot places alike.
        var slotStores = CountSlotStores(function);

        // Find the contiguous fill run somewhere later in the same block: n
        // element stores targeting the place, in increasing constant-index
        // order starting at 0. Chain copies may precede or interleave the run.
        var retired = new List<IrNode>();
        int? runStart = null;
        for (int i = seedIndex + 1; i + length <= block.Children.Count; i++)
        {
            if (TryJoinChain(block.Children[i], place, slotStores))
            {
                retired.Add(block.Children[i]);
                continue;
            }
            if (!IsElementStore(block.Children[i], place, 0))
                continue;
            runStart = i;
            break;
        }
        if (runStart is not { } start)
            return false;

        // Walk the run. Besides chain copies, an element value csc evaluated
        // into its own spill slot right before the element store (a
        // conditional element: S_3 = c ? a : b; S_257[1] = S_3;) belongs to
        // the run: its value evaluates after the effect-free array load
        // and constant index exactly as in the IL, so it moves into its
        // element position.
        var elements = new IrExpression[length];
        var stores = new StoreElement[length];
        var absorbed = new Dictionary<int, StoreStackSlot>();
        int end = start;
        for (int k = 0; k < length; end++)
        {
            if (end >= block.Children.Count)
                return false;
            var statement = block.Children[end];
            if (IsElementStore(statement, place, k))
            {
                stores[k] = (StoreElement)statement;
                elements[k] = stores[k].Value;
                k++;
                continue;
            }
            if (TryJoinChain(statement, place, slotStores))
            {
                retired.Add(statement);
                continue;
            }
            if (k > 0
                && statement is StoreStackSlot spill
                && end + 1 < block.Children.Count
                && IsElementStore(block.Children[end + 1], place, k)
                && ((StoreElement)block.Children[end + 1]).Value is LoadStackSlot spillLoad
                && spillLoad.Slot == spill.Slot
                && slotStores.GetValueOrDefault(spill.Slot) == 1
                && CountSlotLoads(function, spill.Slot) == 1)
            {
                absorbed[k] = spill;
                continue;
            }
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
            IrExpression value = absorbed.TryGetValue(k, out var spill) ? spill.Value : elements[k];
            if (value.Descendants.Prepend(value).Any(n => IsLoad(n, place) || IsAddressOrWrite(n, place)))
                return false;
        }

        // Nothing between the allocation and the fill run other than chain
        // copies may write the place, take its address, or even read it — the
        // array reference reaching the fill run must be exactly this
        // allocation, unmutated and unaliased, and no earlier statement may
        // observe the not-yet-filled array (the combined literal's declaration
        // moves to the fill run's position, so an earlier read would otherwise
        // reference the place before its folded declaration).
        for (int i = seedIndex + 1; i < start; i++)
        {
            var statement = block.Children[i];
            if (retired.Contains(statement))
                continue;
            if (WritesOrAddresses(statement, place))
                return false;
            if (statement.Descendants.Prepend(statement).Any(n => IsLoad(n, place)))
                return false;
        }

        // After the fill run, the place must escape exactly once more (the
        // real read of the fully-built array, through any chain member) and
        // never be written or addressed again — including another element
        // store into the same array reference, which would mean the fill run
        // is not the array's only mutation.
        LoadStackSlot? escape = null;
        int loads = 0;
        foreach (var node in function.Descendants)
        {
            bool outsideRun = !IsInsideRange(node, block, seedIndex, end);
            if (outsideRun && (IsAddressOrWrite(node, place) || IsElementStoreArrayLoad(node, place)))
                return false;
            if (outsideRun && IsLoad(node, place))
            {
                loads++;
                escape = node as LoadStackSlot;
            }
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
            detachedElements[k] = absorbed.TryGetValue(k, out var spill)
                ? (IrExpression)spill.DetachChildren()[0]
                : (IrExpression)stores[k].DetachChildren()[2];
        }
        var literal = new ArrayLiteral(elementType, arrayType, detachedElements);
        // The raise subsumes the newarr, so the literal inherits its offset. Without
        // this the instruction's offset survives nowhere in the tree, and any fact
        // keyed to it (alloc.array) has no node to anchor to.
        literal.SetSourceOffset(newArray.SourceOffset);
        IrNode combined = place.IsSlot ? new StoreStackSlot(place.Index, literal) : new StoreLocal(place.Index, ArrayLocalType(block, seedIndex), literal);

        // The one escaping read may name a retired chain member; it now reads
        // the allocation's own slot.
        if (escape is not null && escape.Slot != place.Index)
            escape.ReplaceWith(new LoadStackSlot(place.Index, escape.Type));

        // Replace the first fill statement with the combined declaration, drop
        // the remaining fill statements, absorbed element spills, retired
        // chain copies, and the original (now-redundant) allocation.
        var seed = block.Children[seedIndex];
        stores[0].ReplaceWith(combined);
        for (int k = 1; k < length; k++)
            stores[k].Detach();
        foreach (var spill in absorbed.Values)
            spill.Detach();
        foreach (var copy in retired)
            copy.Detach();
        seed.Detach();
        return true;
    }

    // A direct copy `S_a = S_b;` joins the place's chain when S_b is already a
    // member and S_a is a synthetic slot with no other store anywhere in the
    // function. The chain root itself is single-store by the escape check.
    static bool TryJoinChain(IrNode statement, Place place, IReadOnlyDictionary<int, int> slotStores)
    {
        if (!place.IsSlot
            || statement is not StoreStackSlot { Value: LoadStackSlot source } copy
            || !place.Members.Contains(source.Slot)
            || place.Members.Contains(copy.Slot)
            || slotStores.GetValueOrDefault(copy.Slot) != 1)
            return false;
        place.Members.Add(copy.Slot);
        return true;
    }

    static Dictionary<int, int> CountSlotStores(IrFunction function)
    {
        var counts = new Dictionary<int, int>();
        foreach (var store in function.Descendants.OfType<StoreStackSlot>())
            counts[store.Slot] = counts.GetValueOrDefault(store.Slot) + 1;
        return counts;
    }

    static int CountSlotLoads(IrFunction function, int slot)
        => function.Descendants.Count(n => n is LoadStackSlot load && load.Slot == slot);

    static TypeRef ArrayLocalType(Block block, int seedIndex)
        => ((StoreLocal)block.Children[seedIndex]).Type;

    // The place's element type as declared at the allocation site. Stack
    // slots carry no independent declared array type (only the allocation's
    // own type reaches every load), so they always match trivially. A local
    // declares its own array type, which — thanks to array covariance — can
    // differ from the allocated array's element type (e.g. `object[] tmp =
    // new string[n];`); folding must decline rather than spell an initializer
    // whose elements don't typecheck against the narrower declared type.
    static TypeRef? PlaceElementType(Block block, int seedIndex, Place place)
    {
        if (place.IsSlot)
            return ((NewArray)place.Value).ElementType;
        var local = (StoreLocal)block.Children[seedIndex];
        return local.Type is { Kind: TypeRefKind.SzArray, ElementType: { } element } ? element : null;
    }

    static bool IsElementStore(IrNode node, Place place, int expectedIndex)
        => node is StoreElement { Index: Constant { Value: int idx } } store
            && idx == expectedIndex
            && IsLoadOf(store.Array, place);

    static bool IsLoadOf(IrExpression expr, Place place)
        => (place.IsSlot, expr) switch
        {
            (true, LoadStackSlot load) => place.Members.Contains(load.Slot),
            (false, LoadLocal load) => load.Index == place.Index,
            _ => false,
        };

    static bool IsLoad(IrNode node, Place place)
        => (place.IsSlot, node) switch
        {
            (true, LoadStackSlot load) => place.Members.Contains(load.Slot),
            (false, LoadLocal load) => load.Index == place.Index,
            _ => false,
        };

    static bool IsAddressOrWrite(IrNode node, Place place)
        => (place.IsSlot, node) switch
        {
            (true, StoreStackSlot store) => place.Members.Contains(store.Slot),
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
            (false, Fixed fixedStatement) => fixedStatement.LocalIndex == place.Index,
            _ => false,
        };

    static bool WritesOrAddresses(IrNode statement, Place place)
        => statement.Descendants.Prepend(statement).Any(n => IsAddressOrWrite(n, place) || IsElementStoreArrayLoad(n, place));

    // The array-ref load feeding an element store (inside the fill run) is not
    // itself a hazard, but any OTHER read of the place between allocation and
    // the fill run's start — e.g. an unrelated statement that happens to read
    // the not-yet-filled array — means the place is observed prematurely.
    static bool IsElementStoreArrayLoad(IrNode node, Place place)
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

    // The place being filled: the allocation's local or stack slot, plus (for
    // a slot) every direct-copy chain member found so far.
    sealed class Place(bool isSlot, int index, IrExpression value)
    {
        public bool IsSlot { get; } = isSlot;
        public int Index { get; } = index;
        public IrExpression Value { get; } = value;
        public HashSet<int> Members { get; } = [index];
    }
}
