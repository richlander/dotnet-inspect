using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the csc pin lowering into one or more nested <see cref="Fixed"/>
/// statements. C#'s <c>fixed (T* p = &amp;place) { ... }</c> compiles to a
/// <c>pinned T&amp;</c> local assigned the managed reference, an unmanaged
/// pointer derived from it (<c>ldloc; conv.u</c>) inside the pinned region, and
/// — when the region ends before the method does — a store of zero into the
/// local that unpins it:
/// <code>
///   pinned ref char V_0 = ref _firstChar;   // pin
///   uint* V_3 = (nuint)V_0;                  // derive pointer
///   ... use V_3 ...
///   V_0 = (nuint)0;                          // unpin (optional)
/// </code>
/// becomes <c>fixed (char* V_0 = &amp;_firstChar) { uint* V_3 = (uint*)V_0; ... }</c>.
/// Left flat, the pinned local renders as the non-C# <c>pinned ref T</c> with a
/// managed-reference-to-pointer cast that does not bind — every such method is
/// syntactically malformed, so it contributes no validity/fidelity signal at
/// all.
///
/// <para>A <c>[LibraryImport]</c> source-generated stub pins several arguments
/// at once (one <c>*Marshaller.GetPinnableReference</c> pin per marshalled
/// parameter), producing several pinned locals in one method — the dominant
/// <c>pinned ref</c>/<c>CS1002</c> family in CoreLib. These nest LIFO (last
/// pinned, first unpinned), so the pass raises them into stacked <c>fixed</c>
/// headers over a shared body.</para>
///
/// <para>Scoped to the well-understood shape: every pinned local pins a managed
/// reference (<c>pinned T&amp;</c>), is assigned once in the entry block, used
/// only to derive pointers (every load feeds a <see cref="Convert"/>), and is
/// either never unpinned (the pin runs to the last use) or unpinned by a single
/// later store in the same block. The shared body holds the pointer derives and
/// their uses; statements that sit between two pins must be pure pointer derives
/// (they are reordered under the innermost <c>fixed</c>, so they may not observe
/// side effects), and a pin's source may not read a pinned slot or a
/// body-written local (it is evaluated before the body). Anything outside this —
/// an escaping or address-taken pinned local, a non-derive wedged between pins,
/// a pin source that depends on the body — leaves the whole method flat. The
/// entire shape is proven before any node is detached.</para>
/// </summary>
public sealed class FixedStatementPass : IIrPass
{
    public string Name => "fixed-statement";

    /// <summary>A proven pinned local: the slot, the pointed-to element type, the defining pin store, and the optional unpin store that ends its region.</summary>
    private sealed record PinnedShape(int Slot, TypeRef ElementType, StoreLocal DefStore, StoreLocal? Unpin);

    public void Run(IrFunction function, PassContext context)
    {
        // The array/string pin form — `fixed (T* p = array)` — is the same Roslyn
        // FixedStatement lowering, but its pinned local is the whole array and the
        // null/empty guard is an if/else diamond, not a flat statement run. Raise it
        // first (independent locals), then handle the managed-reference form below.
        FixedArrayRaising.RaiseAll(function, context);

        // The rest handles the managed-reference pin (`pinned T&`). A pinned
        // array/string (a non-byref pinned element) is out of scope here.
        var pinnedSlots = Enumerable.Range(0, function.Locals.Length)
            .Where(i => function.Locals[i].Kind == TypeRefKind.Pinned
                && function.Locals[i].ElementType is { Kind: TypeRefKind.ByRef })
            .ToHashSet();
        if (pinnedSlots.Count == 0)
            return;

        if (function.Body.Blocks.Count == 0)
            return;
        var entry = function.Body.Blocks[0];

        // Prove each pinned slot independently. A pinned slot that fails its
        // proof aborts the whole rewrite: a half-raised method would mix `fixed`
        // with a leftover `pinned ref`, which is worse than leaving it all flat.
        var pins = new List<PinnedShape>(pinnedSlots.Count);
        foreach (var slot in pinnedSlots)
        {
            if (!TryProvePin(function, entry, slot, out var shape))
                return;
            pins.Add(shape);
        }

        // The first-pinned slot becomes the outermost `fixed`.
        pins.Sort((a, b) => a.DefStore.ChildIndex - b.DefStore.ChildIndex);
        int firstPin = pins[0].DefStore.ChildIndex;
        int lastPin = pins[^1].DefStore.ChildIndex;

        var pinStores = pins.Select(p => p.DefStore).ToHashSet();
        var unpins = pins.Where(p => p.Unpin is not null).Select(p => p.Unpin!).ToHashSet();

        // csc derives the unmanaged pointer one of two ways. When the pinned slot
        // is consumed inline with no copy — passed straight into a call or
        // dereferenced through its own Convert (the [LibraryImport] GetCalendars
        // shape) — the pinned slot itself is the `fixed` pointer variable. When the
        // pointer is instead copied once into its own local (`V_ptr = (T*)V_pin;
        // ... *V_ptr ...` — the SumPinnedArray shape and the indexed multi-pin
        // stubs), that copy is the redundant tail of the pin: the derived local IS
        // the fixed pointer. Fold it — use the derived slot as the `fixed` variable
        // and drop the copy — so the rendered `fixed` recompiles to csc's own
        // lowering (opcode-exact) rather than re-pinning a temp and copying it. A
        // pin is foldable when its slot is read exactly once across the method, that
        // read is the value of a single derived-pointer store into a non-pinned
        // entry-block slot, and that slot has no other defining store.
        var foldedDerives = new Dictionary<int, StoreLocal>();
        foreach (var pin in pins)
        {
            if (FoldTarget(function, entry, pinnedSlots, pin.Slot) is { } derive)
                foldedDerives[pin.Slot] = derive;
        }
        int FixedLocal(PinnedShape pin)
            => foldedDerives.TryGetValue(pin.Slot, out var d) ? d.Index : pin.Slot;

        // Non-pinned slots holding a pointer derived from a pin (`void* V = (nuint)V_pin`).
        // A use of one of these after the pin ends is invalid scoping, so any
        // statement reading one must stay under the `fixed` with the derives.
        var derivedSlots = entry.Children.OfType<StoreLocal>()
            .Where(s => !pinnedSlots.Contains(s.Index)
                && StripConverts(s.Value) is LoadLocal source && pinnedSlots.Contains(source.Index))
            .Select(s => s.Index)
            .ToHashSet();

        // The fixed body runs from the first pin to the last statement that uses
        // a pinned slot or a pin-derived pointer (the unpin stores, which we
        // drop, do not count). The window also covers every unpin so each is
        // dropped rather than left dangling as a write to an out-of-scope local.
        int lastUse = firstPin;
        foreach (var child in entry.Children)
        {
            if (child.ChildIndex <= firstPin || pinStores.Contains(child) || unpins.Contains(child))
                continue;
            if (ReferencesAnySlot(child, pinnedSlots, derivedSlots))
                lastUse = child.ChildIndex;
        }
        int windowEnd = lastUse;
        foreach (var unpin in unpins)
            windowEnd = System.Math.Max(windowEnd, unpin.ChildIndex);

        var bodyStmts = entry.Children
            .Where(c => c.ChildIndex > firstPin && c.ChildIndex <= windowEnd
                && !pinStores.Contains(c) && !unpins.Contains(c))
            .OrderBy(c => c.ChildIndex)
            .ToList();

        // A body statement wedged before the innermost pin is reordered under it,
        // so it must be a pure pointer derive (a store of a pin-derived pointer)
        // with no side effect to observe. Anything else is rejected.
        foreach (var stmt in bodyStmts)
        {
            if (stmt.ChildIndex < lastPin && !IsPointerDerive(stmt, pinnedSlots))
                return;
        }

        // Each pin source is evaluated before the body, so it may not read a
        // pinned slot or a body-written local — either would move relative to it.
        var bodyWritten = bodyStmts.OfType<StoreLocal>().Select(s => s.Index).ToHashSet();
        foreach (var pin in pins)
        {
            foreach (var load in Loads(pin.DefStore.Value))
            {
                if (pinnedSlots.Contains(load.Index) || bodyWritten.Contains(load.Index))
                    return;
            }
        }

        // Every reference to a pinned slot must live in a body statement, so
        // dropping the pin/unpin stores cannot orphan a use.
        foreach (var node in function.Descendants)
        {
            if (node is LoadLocal l && pinnedSlots.Contains(l.Index)
                && !bodyStmts.Any(stmt => ReferenceOwnership.IsInside(node, stmt)))
            {
                return;
            }
        }

        // Shape proven — detach the body, drop the inner pins and every unpin,
        // and nest the fixed statements (innermost first) over the shared body.
        // A folded derive store is dropped: its derived slot is promoted to the
        // `fixed` variable, so the copy is redundant.
        var droppedDerives = foldedDerives.Values.ToHashSet();
        foreach (var stmt in bodyStmts)
            stmt.Detach();
        foreach (var pin in pins.Skip(1))
            pin.DefStore.Detach();
        foreach (var unpin in unpins)
            unpin.Detach();

        var body = new BlockContainer();
        var bodyBlock = new Block(entry.StartOffset);
        foreach (var stmt in bodyStmts)
            if (!droppedDerives.Contains(stmt))
                bodyBlock.Add(stmt);
        body.Add(bodyBlock);

        for (int i = pins.Count - 1; i >= 1; i--)
        {
            var pin = pins[i];
            var pinSource = (IrExpression)pin.DefStore.DetachChildren()[0];
            var inner = new Fixed(pin.ElementType, FixedLocal(pin), pinSource, body);
            var wrapper = new BlockContainer();
            var wrapperBlock = new Block(entry.StartOffset);
            wrapperBlock.Add(inner);
            wrapper.Add(wrapperBlock);
            body = wrapper;
        }

        var outer = pins[0];
        var outerSource = (IrExpression)outer.DefStore.DetachChildren()[0];
        var outerFixed = new Fixed(outer.ElementType, FixedLocal(outer), outerSource, body);
        context.Stepper.StepOver("raise pinned locals to fixed statements", outer.DefStore);
        outer.DefStore.ReplaceWith(outerFixed);
    }

    /// <summary>
    /// Proves the single-pinned-local shape for <paramref name="slot"/>: one
    /// defining pin store in the entry block assigning a managed reference (not a
    /// value-type receiver's <c>this</c>), at most one later unpin store, and
    /// every load feeding a pointer-deriving <see cref="Convert"/> (no address-of).
    /// </summary>
    static bool TryProvePin(IrFunction function, Block entry, int slot, out PinnedShape shape)
    {
        shape = null!;
        if (function.Locals[slot].ElementType is not { Kind: TypeRefKind.ByRef } byRef)
            return false;

        var stores = function.Descendants.OfType<StoreLocal>().Where(s => s.Index == slot).ToList();
        if (stores is not [var defStore, ..] || !ReferenceEquals(defStore.Parent, entry))
            return false;
        if (defStore.Value.ResultType is not { Kind: TypeRefKind.ByRef })
            return false;
        // Pinning `this` (a value-type receiver's `ldarg.0`) has no `&this`
        // spelling; leave it flat. A static method's first parameter is also
        // index 0 but is a normal `&param`, so key on the receiver name.
        if (defStore.Value is LoadArgument { Index: 0, Name: "this" })
            return false;

        StoreLocal? unpin = null;
        if (stores.Count == 2 && IsUnpin(stores[1]) && ReferenceEquals(stores[1].Parent, entry))
            unpin = stores[1];
        else if (stores.Count != 1)
            return false;

        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadLocalAddress a when a.Index == slot:
                    return false;
                case LoadLocal l when l.Index == slot && l.Parent is not Convert:
                    return false;
            }
        }

        shape = new PinnedShape(slot, byRef.ElementType!, defStore, unpin);
        return true;
    }

    /// <summary>
    /// Returns the single derived-pointer store <c>V_ptr = (T*)V_pin</c> that a
    /// foldable pin's slot feeds, or null when the pin is not foldable (the slot
    /// is dereferenced inline, read more than once, or its derived slot is not a
    /// once-assigned non-pinned local). When non-null, the derived slot becomes
    /// the <c>fixed</c> pointer variable and the store is dropped.
    /// </summary>
    static StoreLocal? FoldTarget(IrFunction function, Block entry, IReadOnlySet<int> pinnedSlots, int slot)
    {
        var loads = function.Descendants.OfType<LoadLocal>().Where(l => l.Index == slot).ToList();
        if (loads.Count != 1)
            return null;

        // Climb the pointer-deriving converts above the single load to its store.
        IrNode node = loads[0];
        while (node.Parent is Convert convert)
            node = convert;
        if (node.Parent is not StoreLocal derive
            || pinnedSlots.Contains(derive.Index)
            || !ReferenceEquals(derive.Parent, entry))
            return null;

        // The derived slot must be defined only by this store, so promoting it to
        // the fixed variable cannot drop a second assignment.
        var derivedStores = function.Descendants.OfType<StoreLocal>().Where(s => s.Index == derive.Index).ToList();
        return derivedStores.Count == 1 ? derive : null;
    }

    /// <summary>A pure pointer derive: a store of a pin-derived pointer (<c>V = (nuint)V_pin</c>) into a non-pinned slot — no side effect to observe under reordering.</summary>
    static bool IsPointerDerive(IrNode stmt, IReadOnlySet<int> pinnedSlots)
        => stmt is StoreLocal store
            && !pinnedSlots.Contains(store.Index)
            && StripConverts(store.Value) is LoadLocal load
            && pinnedSlots.Contains(load.Index);

    static bool ReferencesAnySlot(IrNode root, IReadOnlySet<int> pinnedSlots, IReadOnlySet<int> derivedSlots)
        => Loads(root).Any(l => pinnedSlots.Contains(l.Index) || derivedSlots.Contains(l.Index));

    static IEnumerable<LoadLocal> Loads(IrNode root)
        => root.Descendants.Prepend(root).OfType<LoadLocal>();

    static IrExpression StripConverts(IrExpression value)
    {
        while (value is Convert convert)
            value = convert.Operand;
        return value;
    }

    /// <summary>A store that ends the pinned region: null or zero, possibly through a conv to a native pointer.</summary>
    static bool IsUnpin(StoreLocal store)
    {
        var value = store.Value;
        while (value is Convert convert)
            value = convert.Operand;
        return value is Constant { Value: null or 0 or 0L };
    }
}
