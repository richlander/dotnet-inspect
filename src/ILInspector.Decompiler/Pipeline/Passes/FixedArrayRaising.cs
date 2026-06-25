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
            if (load.Index == pin && !IsInside(load, guard))
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
            if (!bodyStmts.Any(stmt => IsInside(reference, stmt)))
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

    static bool IsInside(IrNode node, IrNode root)
    {
        for (var current = node; current is not null; current = current.Parent)
            if (ReferenceEquals(current, root))
                return true;
        return false;
    }
}
