using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises the csc pin lowering into a <see cref="Fixed"/> statement. C#'s
/// <c>fixed (T* p = &amp;place) { ... }</c> compiles to a <c>pinned T&amp;</c>
/// local assigned the managed reference, an unmanaged pointer derived from it
/// (<c>ldloc; conv.u</c>) inside the pinned region, and — when the region ends
/// before the method does — a store of zero into the local that unpins it:
/// <code>
///   pinned ref char V_0 = ref _firstChar;   // pin
///   uint* V_3 = (nuint)V_0;                  // derive pointer
///   ... use V_3 ...
///   V_0 = (nuint)0;                          // unpin (optional)
/// </code>
/// becomes <c>fixed (char* V_0 = &amp;_firstChar) { uint* V_3 = (uint*)V_0; ... }</c>.
/// Left flat, the pinned local renders as the non-C# <c>pinned ref T</c> with a
/// managed-reference-to-pointer cast that does not bind — every such method is
/// syntactically malformed, so it contributes no validity/fidelity
/// signal at all.
///
/// <para>Scoped to the well-understood shape: exactly one pinned local, pinning
/// a managed reference (<c>pinned T&amp;</c>), assigned once in the entry block,
/// used only to derive pointers (every load feeds a <see cref="Convert"/>), and
/// either never unpinned (the pin runs to method end — the fixed region is the
/// whole tail) or unpinned by a single later store in the same block (the fixed
/// region is the run between them). Anything outside this — multiple pinned
/// locals, array/string pinning, an escaping or address-taken pinned local, a
/// reference used before the pin or after the unpin — is left flat. The whole
/// shape is proven before any node is detached.</para>
/// </summary>
public sealed class FixedStatementPass : IIrPass
{
    public string Name => "fixed-statement";

    public void Run(IrFunction function, PassContext context)
    {
        // Exactly one pinned local, pinning a managed reference. More than one,
        // or a pinned array/string (a non-byref pinned element), is out of scope.
        var pinned = Enumerable.Range(0, function.Locals.Length)
            .Where(i => function.Locals[i].Kind == TypeRefKind.Pinned)
            .ToList();
        if (pinned is not [int idx]
            || function.Locals[idx].ElementType is not { Kind: TypeRefKind.ByRef } byRef)
        {
            return;
        }

        if (function.Body.Blocks.Count == 0)
            return;
        var entry = function.Body.Blocks[0];

        // The pinned slot's stores: a single defining store, plus at most one
        // unpinning store (a store of null/zero that ends the pinned region).
        var stores = function.Descendants.OfType<StoreLocal>().Where(s => s.Index == idx).ToList();
        if (stores is not [var defStore, ..] || !ReferenceEquals(defStore.Parent, entry))
            return;
        if (defStore.Value.ResultType is not { Kind: TypeRefKind.ByRef })
            return;
        // Pinning `this` (a value-type receiver's `ldarg.0`) has no `&this`
        // spelling; leave it flat. A static method's first parameter is also
        // index 0 but is a normal `&param`, so key on the receiver name.
        if (defStore.Value is LoadArgument { Index: 0, Name: "this" })
            return;

        StoreLocal? unpin = null;
        if (stores.Count == 2 && IsUnpin(stores[1]) && ReferenceEquals(stores[1].Parent, entry))
            unpin = stores[1];
        else if (stores.Count != 1)
            return;

        // Every other reference to the pinned slot must be a load that derives a
        // pointer (feeds a Convert). An address-of the pinned local, or a load
        // used some other way, is a shape this rewrite does not model.
        foreach (var node in function.Descendants)
        {
            switch (node)
            {
                case LoadLocalAddress a when a.Index == idx:
                    return;
                case LoadLocal l when l.Index == idx && l.Parent is not Convert:
                    return;
            }
        }

        int d = defStore.ChildIndex;
        int end = unpin?.ChildIndex ?? entry.Children.Count;
        if (unpin is not null && end <= d)
            return;
        var scope = entry.Children.Skip(d + 1).Take(end - (d + 1)).ToList();

        // The pinned region must lexically contain every pinned-local reference:
        // each load lives in one of the statements between the pin and the unpin
        // (or method end). A reference outside that run cannot be brought under
        // the fixed block without reordering, so the whole shape is rejected.
        foreach (var node in function.Descendants)
        {
            bool refsPin = node is LoadLocal l && l.Index == idx;
            if (refsPin && !scope.Any(stmt => IsInside(node, stmt)))
                return;
        }

        // Shape proven — detach the scope statements into the fixed body, drop
        // the unpin, and replace the defining store with the fixed statement.
        var body = new BlockContainer();
        var bodyBlock = new Block(entry.StartOffset);
        foreach (var stmt in scope)
        {
            stmt.Detach();
            bodyBlock.Add(stmt);
        }
        body.Add(bodyBlock);
        unpin?.Detach();
        var pinSource = (IrExpression)defStore.DetachChildren()[0];
        var fixedNode = new Fixed(byRef.ElementType!, idx, pinSource, body);
        context.Stepper.StepOver("raise pinned local to fixed statement", defStore);
        defStore.ReplaceWith(fixedNode);
    }

    /// <summary>A store that ends the pinned region: null or zero, possibly through a conv to a native pointer.</summary>
    static bool IsUnpin(StoreLocal store)
    {
        var value = store.Value;
        while (value is Convert convert)
            value = convert.Operand;
        return value is Constant { Value: null or 0 or 0L };
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
