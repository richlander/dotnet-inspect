using System.Collections.Generic;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the csc pin lowering for <c>fixed (T* p = array)</c> — the array/string
/// form, where the pinned local is the whole array (a <c>pinned T[]</c>), not a
/// managed reference. The byref form (<c>fixed (T* p = &amp;place)</c>) is the
/// flat-statement shape <see cref="FixedStatementPass"/> handles directly; this is
/// the same Roslyn <c>FixedStatement</c> lowering's array variant, invoked from
/// that pass rather than registered separately.
///
/// <para>Pinning an array carries the language's null/empty guard: a null or
/// zero-length array pins to a null pointer, anything else to <c>&amp;array[0]</c>.
/// csc lowers <c>fixed (byte* p = a) { ... }</c> to
/// <code>
///   pinned byte[] V_pin = a;                       // pin
///   if (a == null || a.Length == 0) p = (byte*)0;  // empty -> null pointer
///   else                            p = &amp;V_pin[0]; // else  -> first element
///   ... use p ...
///   V_pin = null;                                  // unpin
/// </code>
/// Left flat the pinned local renders as the non-C# <c>pinned T[]</c> and the
/// guard diamond never structures, so the whole method is malformed. This pass
/// recognizes the diamond, drops it together with the pin/unpin scaffolding, and
/// wraps the body in <c>fixed (T* p = a) { ... }</c> — the language regenerates the
/// guard on recompile, so the rewrite is opcode-faithful.</para>
///
/// <para>Scoped to a single pinned-array slot whose region is one entry-block
/// guard diamond: the pin is assigned once, the derived pointer is written only by
/// the diamond's two arms (one <c>= (T*)0</c>, one <c>= &amp;pin[0]</c>), the array
/// is unpinned by a single later <c>pin = null</c> store, and every read of the
/// pinned slot lives inside the diamond (its length guard and element address).
/// Anything else leaves the method flat.</para>
///
/// <para>String pinning lowers differently: the guard writes the derived pointer
/// to a stack slot, and the non-null arm first stores
/// <c>value.GetPinnableReference()</c> into a <c>pinned char&amp;</c> local. The
/// same language spelling is <c>fixed (char* p = value)</c>, so this pass also
/// recognizes that narrower string guard and makes the stack slot the fixed
/// pointer variable.</para>
/// </summary>
internal static class FixedArrayRaising
{
    public static void RaiseAll(IrFunction function, PassContext context)
    {
        var pinnedArraySlots = Enumerable.Range(0, function.Locals.Length)
            .Where(i => function.Locals[i].Kind == TypeRefKind.Pinned
                && function.Locals[i].ElementType is { Kind: TypeRefKind.SzArray })
            .ToList();

        foreach (var slot in pinnedArraySlots)
            TryRaise(function, slot, context);

        RaiseStringPins(function, context);
    }

    static void RaiseStringPins(IrFunction function, PassContext context)
    {
        if (function.Body.Blocks.Count == 0)
            return;

        var entry = function.Body.Blocks[0];
        foreach (var guard in entry.Children.OfType<IfStatement>().Where(g => g.HasElse).OrderByDescending(g => g.ChildIndex).ToList())
        {
            TryRaiseStringPin(function, entry, guard, context);
        }
    }

    static void TryRaiseStringPin(IrFunction function, Block entry, IfStatement guard, PassContext context)
    {
        if (guard.Then.Children is not [StoreStackSlot thenStore]
            || guard.Else is not { Children: [StoreLocal pinStore, StoreStackSlot elseStore] }
            || thenStore.Slot != elseStore.Slot
            || !IsNullPointer(thenStore.Value)
            || function.Locals[pinStore.Index] is not { Kind: TypeRefKind.Pinned, ElementType: { Kind: TypeRefKind.ByRef } byRef }
            || PinSource(pinStore.Value) is not { } source
            || !IsString(source.ResultType)
            || !ConditionNullChecksSource(guard.Condition, source)
            || StripConverts(elseStore.Value) is not LoadLocal pinLoad
            || pinLoad.Index != pinStore.Index)
        {
            return;
        }

        int pointerSlot = thenStore.Slot;
        var pointerAliases = PointerAliases(function, entry, guard.ChildIndex, pointerSlot);

        var unpins = entry.Children
            .OfType<StoreLocal>()
            .Where(store => store.Index == pinStore.Index && store != pinStore)
            .ToList();
        if (unpins.Count > 1 || unpins.Any(store => !IsUnpin(store)))
            return;
        var unpin = unpins.SingleOrDefault();

        int bodyEnd = unpin?.ChildIndex - 1 ?? LastPointerUseIndex(entry, guard.ChildIndex + 1, pointerSlot, pointerAliases);
        if (bodyEnd <= guard.ChildIndex)
            return;

        var bodyStmts = entry.Children
            .Where(c => c.ChildIndex > guard.ChildIndex && c.ChildIndex <= bodyEnd)
            .ToList();
        if (bodyStmts.Count == 0)
            return;

        foreach (var node in function.Descendants)
        {
            if (node is StoreLocal store && store.Index == pinStore.Index && store != pinStore && store != unpin)
                return;
            if (node is LoadLocal load && load.Index == pinStore.Index && load != pinLoad)
                return;
            if (node is StoreStackSlot stackStore && stackStore.Slot == pointerSlot && stackStore != thenStore && stackStore != elseStore)
                return;
            if (ReferencesPinnedPointer(node, pointerSlot, pointerAliases)
                && node != thenStore && node != elseStore
                && !bodyStmts.Any(stmt => ReferenceOwnership.IsInside(node, stmt)))
            {
                return;
            }
        }

        foreach (var stmt in bodyStmts)
            stmt.Detach();
        unpin?.Detach();

        var body = new BlockContainer();
        var bodyBlock = new Block(entry.StartOffset);
        foreach (var stmt in bodyStmts)
            bodyBlock.Add(stmt);
        body.Add(bodyBlock);

        var fixedStatement = new Fixed(
            byRef.ElementType!,
            pointerSlot,
            (IrExpression)source.Clone(),
            body,
            sourceIsAddress: false,
            localIsStackSlot: true,
            localStackSlotType: thenStore.Value.ResultType);
        context.Stepper.StepOver("raise pinned string to fixed statement", guard);
        guard.ReplaceWith(fixedStatement);
    }

    static void TryRaise(IrFunction function, int pin, PassContext context)
    {
        // The pinned slot is assigned exactly twice: the defining pin store and a
        // later `pin = null` unpin, both in the same block.
        var stores = function.Descendants.OfType<StoreLocal>().Where(s => s.Index == pin).ToList();
        if (stores is not [var defStore, var unpin]
            || !IsNullStore(unpin)
            || defStore.Parent is not Block block
            || !ReferenceEquals(unpin.Parent, block))
        {
            return;
        }

        // The guard diamond is an if/else immediately after the pin store; each arm
        // assigns the same derived-pointer slot — one to a null pointer, the other to
        // the address of the pinned array's first element.
        if (block.Children.ElementAtOrDefault(defStore.ChildIndex + 1) is not IfStatement { HasElse: true } guard)
            return;
        if (ArmStore(guard.Then) is not { } thenStore || ArmStore(guard.Else!) is not { } elseStore)
            return;
        if (thenStore.Index != elseStore.Index)
            return;

        var (zeroStore, addressStore) =
            IsNullPointer(thenStore.Value) && ElementAddressType(addressValue: elseStore.Value, pin) is not null
                ? (thenStore, elseStore)
                : IsNullPointer(elseStore.Value) && ElementAddressType(addressValue: thenStore.Value, pin) is not null
                    ? (elseStore, thenStore)
                    : (null, null);
        if (zeroStore is null || addressStore is null)
            return;
        if (ElementAddressType(addressStore.Value, pin) is not { } elementType)
            return;

        int pointerSlot = thenStore.Index;

        // The pinned slot is consumed only inside the diamond (its length guard and
        // the element-address arm) — never the body — so dropping it orphans nothing.
        foreach (var load in function.Descendants.OfType<LoadLocal>())
        {
            if (load.Index == pin && !ReferenceOwnership.IsInside(load, guard))
                return;
        }

        // The body runs from after the diamond to just before the unpin; every
        // statement there belongs under the fixed. A use of the pointer after the
        // unpin would be out of scope, so require the unpin to follow the diamond.
        if (unpin.ChildIndex <= guard.ChildIndex)
            return;
        var bodyStmts = block.Children
            .Where(c => c.ChildIndex > guard.ChildIndex && c.ChildIndex < unpin.ChildIndex)
            .ToList();

        // The derived pointer is written only by the diamond's arms; a body write
        // would mean the slot outlives the fixed region.
        foreach (var node in function.Descendants)
        {
            if (node is StoreLocal s && s.Index == pointerSlot && s != zeroStore && s != addressStore)
                return;
        }
        foreach (var reference in function.Descendants.Where(node => ReferencesPointerSlot(node, pointerSlot)))
        {
            if (reference is StoreLocal store && (store == zeroStore || store == addressStore))
                continue;
            if (!bodyStmts.Any(stmt => ReferenceOwnership.IsInside(reference, stmt)))
                return;
        }

        // Shape proven. Lift the array source out of the pin store, detach the body,
        // and replace the diamond with `fixed (T* ptr = array) { body }`. The pin
        // store and the unpin are dropped — the language re-emits them.
        var source = (IrExpression)defStore.DetachChildren()[0];
        foreach (var stmt in bodyStmts)
            stmt.Detach();

        var body = new BlockContainer();
        var bodyBlock = new Block(block.StartOffset);
        foreach (var stmt in bodyStmts)
            bodyBlock.Add(stmt);
        body.Add(bodyBlock);

        var fixedStatement = new Fixed(elementType, pointerSlot, source, body, sourceIsAddress: false);
        context.Stepper.StepOver("raise pinned array to fixed statement", defStore);
        guard.ReplaceWith(fixedStatement);
        defStore.Detach();
        unpin.Detach();
    }

    /// <summary>The single store an if-arm makes, or null when the arm is not exactly one StoreLocal.</summary>
    static StoreLocal? ArmStore(Block arm)
        => arm.Children is [StoreLocal store] ? store : null;

    /// <summary>The element type when <paramref name="addressValue"/> is <c>&amp;pin[0]</c> (through pointer converts); otherwise null.</summary>
    static TypeRef? ElementAddressType(IrExpression addressValue, int pin)
        => StripConverts(addressValue) is LoadElementAddress
        {
            Array: LoadLocal arrayLoad,
            Index: Constant { Value: 0 },
            ElementType: { } elementType,
        } && arrayLoad.Index == pin
            ? elementType
            : null;

    static bool IsNullPointer(IrExpression value)
        => StripConverts(value) is Constant { Value: null or 0 or 0L };

    static bool IsNullStore(StoreLocal store)
        => StripConverts(store.Value) is Constant { Value: null or 0 or 0L };

    static bool IsUnpin(StoreLocal store)
        => StripConverts(store.Value) is Constant { Value: null or 0 or 0L };

    static IrExpression? PinSource(IrExpression value)
        => value is Call { Callee.Name: "GetPinnableReference", Callee.HasThis: true, Arguments: [var source] }
            ? source
            : null;

    static bool IsString(TypeRef? type)
        => type is { Kind: TypeRefKind.Definition, Namespace: "System", Name: "String" };

    static bool ConditionNullChecksSource(IrExpression condition, IrExpression source)
        => condition is LogicalNot { Operand: { } nullChecked }
            && SameLoadPlace(nullChecked, source);

    static bool SameLoadPlace(IrExpression left, IrExpression right)
        => left switch
        {
            LoadArgument leftArgument when right is LoadArgument rightArgument
                => leftArgument.Index == rightArgument.Index && leftArgument.Name == rightArgument.Name,
            LoadLocal leftLocal when right is LoadLocal rightLocal
                => leftLocal.Index == rightLocal.Index,
            _ => false,
        };

    static bool ReferencesStackSlot(IrNode node, int slot) => node switch
    {
        LoadStackSlot load => load.Slot == slot,
        StoreStackSlot store => store.Slot == slot,
        _ => false,
    };

    static bool StatementReferencesStackSlot(IrNode node, int slot)
        => ReferencesStackSlot(node, slot)
            || node.Descendants.Any(descendant => ReferencesStackSlot(descendant, slot));

    static HashSet<int> PointerAliases(IrFunction function, Block entry, int guardIndex, int pointerSlot)
    {
        var aliases = new HashSet<int>();
        bool changed;
        do
        {
            changed = false;
            foreach (var store in function.Descendants.OfType<StoreLocal>())
            {
                if (!IsAfterEntryStatement(store, entry, guardIndex) || store.Type.Kind != TypeRefKind.Pointer)
                    continue;
                if (ValueReferencesPinnedPointer(store.Value, pointerSlot, aliases)
                    && aliases.Add(store.Index))
                {
                    changed = true;
                }
            }
        } while (changed);
        return aliases;
    }

    static bool IsAfterEntryStatement(IrNode node, Block entry, int guardIndex)
    {
        for (var current = node; current.Parent is not null; current = current.Parent)
            if (ReferenceEquals(current.Parent, entry))
                return current.ChildIndex > guardIndex;
        return false;
    }

    static bool ValueReferencesPinnedPointer(IrNode value, int pointerSlot, IReadOnlySet<int> aliases)
        => ReferencesPinnedPointer(value, pointerSlot, aliases)
            || value.Descendants.Any(descendant => ReferencesPinnedPointer(descendant, pointerSlot, aliases));

    static bool ReferencesPointerAlias(IrNode node, IReadOnlySet<int> aliases) => node switch
    {
        LoadLocal load => aliases.Contains(load.Index),
        LoadLocalAddress address => aliases.Contains(address.Index),
        StoreLocal store => aliases.Contains(store.Index),
        _ => false,
    };

    static bool ReferencesPinnedPointer(IrNode node, int pointerSlot, IReadOnlySet<int> aliases)
        => ReferencesStackSlot(node, pointerSlot) || ReferencesPointerAlias(node, aliases);

    static bool StatementReferencesPinnedPointer(IrNode node, int pointerSlot, IReadOnlySet<int> aliases)
        => ReferencesPinnedPointer(node, pointerSlot, aliases)
            || node.Descendants.Any(descendant => ReferencesPinnedPointer(descendant, pointerSlot, aliases));

    static int LastPointerUseIndex(Block entry, int start, int pointerSlot, IReadOnlySet<int> aliases)
    {
        int last = start - 1;
        foreach (var child in entry.Children.Where(c => c.ChildIndex >= start))
            if (StatementReferencesPinnedPointer(child, pointerSlot, aliases))
                last = child.ChildIndex;
        return last;
    }

    static bool ReferencesPointerSlot(IrNode node, int pointerSlot) => node switch
    {
        LoadLocal load => load.Index == pointerSlot,
        LoadLocalAddress address => address.Index == pointerSlot,
        StoreLocal store => store.Index == pointerSlot,
        _ => false,
    };

    static IrExpression StripConverts(IrExpression value)
    {
        while (value is Convert convert)
            value = convert.Operand;
        return value;
    }
}
