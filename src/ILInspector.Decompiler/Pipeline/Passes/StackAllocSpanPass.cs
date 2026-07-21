namespace ILInspector.Decompiler.Pipeline;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Raises the compiler's lowering of <c>Span&lt;T&gt; s = stackalloc T[n]</c> back
/// into a source-level <c>stackalloc T[n]</c>. The source form lowers to a
/// <c>localloc</c> of <c>n * sizeof(T)</c> bytes fed to the <c>Span&lt;T&gt;(void*,
/// int)</c> constructor, which left flat renders as
/// <c>new Span&lt;T&gt;(stackalloc byte[...], n)</c> — and that does not compile:
/// a <c>stackalloc</c> in argument position types as <c>Span&lt;byte&gt;</c>, not
/// <c>void*</c>, and the byte-count expression carries a <c>nuint</c> that will not
/// implicitly convert to <c>int</c>. Rewriting to <c>stackalloc T[n]</c> (target-typed
/// to the span) round-trips to the same lowering.
///
/// <para>The element type is the span's type argument; the element count is the
/// constructor's <c>length</c> argument (the byte size on the inner
/// <see cref="StackAllocate"/> is the redundant <c>n * sizeof(T)</c> and is dropped).
/// This raise is independent of the memory-safety rules — the lowered shape never
/// compiled — so the pass runs unconditionally. Whether the result needs an
/// <c>unsafe</c> context (only under <c>[SkipLocalsInit]</c>, per the stackalloc
/// rule) is decided later by the printer.</para>
///
/// <para>The pointer argument is usually the stackalloc directly, but
/// <see cref="StackAllocInitializerPass"/> (#2869) can leave the recovered
/// initializer sitting behind a compiler-owned stack slot indirection instead
/// — <c>slot = stackalloc T[n] {...}; ...; new Span&lt;T&gt;((void*)slot, n)</c> —
/// because that pass only replaces the slot's stored value, never the later
/// constructor call. This pass also resolves that indirection, but only when the
/// slot is exclusively owned by the one store/load pair reaching this
/// constructor (see <see cref="TryResolveOwnedSlotSource"/>), so the allocation
/// is never moved out from under some other reader or writer of the slot.</para>
/// </summary>
public sealed class StackAllocSpanPass : IIrPass
{
    public string Name => "stackalloc-span";

    public void Run(IrFunction function, PassContext context)
    {
        var storesBySlot = GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function)
            .OfType<StoreStackSlot>()
            .GroupBy(s => s.Slot)
            .ToDictionary(g => g.Key, g => g.ToList());
        var loadsBySlot = GenericDeclarationPatternProof.DescendantsOutsideNestedFunctions(function)
            .OfType<LoadStackSlot>()
            .GroupBy(s => s.Slot)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var newObject in function.Descendants.OfType<NewObject>().ToList())
        {
            if (!MemberIdentity.IsStackAllocSpanConstructor(newObject, out var element))
                continue;

            // Span<T>(void* pointer, int length): the pointer is the stackalloc,
            // the length is the logical element count.
            if (newObject.Arguments is not [{ } pointer, var count])
                continue;

            StoreStackSlot? ownedStore = null;
            IEnumerable<IrExpression>? elements;

            if (IsStackAllocPointer(pointer))
            {
                elements = GetInitializerElements(pointer);
            }
            else if (TryResolveOwnedSlotSource(pointer, storesBySlot, loadsBySlot, out var slotSource, out ownedStore))
            {
                elements = GetInitializerElements(slotSource);
            }
            else
            {
                continue;
            }

            count.Detach();
            var raised = new StackAllocArray(element, count, newObject.ResultType, elements);
            raised.InheritSourceOffset(newObject);
            context.Stepper.StepOver("raise Span-over-stackalloc to stackalloc T[n]", newObject);
            newObject.ReplaceWith(raised);
            ownedStore?.Detach();
        }
    }

    /// <summary>
    /// Resolves a Span constructor's pointer argument through a compiler-owned
    /// stack slot indirection. Only raises when the slot is exclusively owned by
    /// this one store/load pair — no other store or load of the slot anywhere in
    /// the function — and the store precedes the load's statement in the same
    /// block, so detaching the store cannot move or discard the allocation from
    /// under some other reader or writer, nor reorder any other observable effect.
    /// </summary>
    static bool TryResolveOwnedSlotSource(
        IrExpression pointer,
        Dictionary<int, List<StoreStackSlot>> storesBySlot,
        Dictionary<int, List<LoadStackSlot>> loadsBySlot,
        out IrExpression source,
        out StoreStackSlot? ownedStore)
    {
        source = null!;
        ownedStore = null;

        LoadStackSlot? load = pointer as LoadStackSlot;
        if (load == null && pointer is Convert { IsChecked: false, Operand: LoadStackSlot loadOperand, Target: { } target } && IsPointerLikeTarget(target))
            load = loadOperand;
        if (load == null)
            return false;

        if (!storesBySlot.TryGetValue(load.Slot, out var stores) || stores.Count != 1)
            return false;
        if (!loadsBySlot.TryGetValue(load.Slot, out var loads) || loads.Count != 1 || loads[0] != load)
            return false;

        var store = stores[0];
        if (!IsStackAllocPointer(store.Value) || store.Parent is not Block parentBlock)
            return false;

        var loadStatement = GetStatement(load);
        if (loadStatement == null || loadStatement.Parent != parentBlock || loadStatement.ChildIndex <= store.ChildIndex)
            return false; // Escaped, reordered, or not yet defined at this use.

        source = store.Value;
        ownedStore = store;
        return true;
    }

    static IrNode? GetStatement(IrNode node)
    {
        while (node.Parent != null && node.Parent is not Block)
            node = node.Parent;
        return node.Parent is Block ? node : null;
    }

    static IEnumerable<IrExpression>? GetInitializerElements(IrExpression pointer)
    {
        if (pointer is StackAllocArray { HasInitializer: true } sa)
        {
            var elements = sa.Elements.ToArray().Cast<IrExpression>().ToList();
            foreach (var e in elements) e.Detach();
            return elements;
        }
        if (pointer is Convert { Operand: StackAllocArray { HasInitializer: true } sa2 })
        {
            var elements = sa2.Elements.ToArray().Cast<IrExpression>().ToList();
            foreach (var e in elements) e.Detach();
            return elements;
        }
        return null;
    }

    static bool IsStackAllocPointer(IrExpression pointer)
    {
        if (pointer is StackAllocate or StackAllocArray)
            return true;

        return pointer is Convert
        {
            IsChecked: false,
            Operand: StackAllocate or StackAllocArray,
            Target: { } target,
        } && IsPointerLikeTarget(target);
    }

    static bool IsPointerLikeTarget(TypeRef target)
        => target.Kind == TypeRefKind.Pointer
            || target is { Kind: TypeRefKind.Definition, Assembly: TypeRef.CoreLibrary, Namespace: "System", Name: "IntPtr" or "UIntPtr" };
}
