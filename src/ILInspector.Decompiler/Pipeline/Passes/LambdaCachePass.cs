namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Collapses the compiler's lazy delegate-cache idiom — the lowering that backs
/// a non-capturing lambda (and a static method group). The compiler caches the
/// one delegate instance in a static <c>&lt;&gt;9__N_M</c> field, guarded by a
/// null check:
/// <code>
///   s = &lt;&gt;9__N_M;
///   if (s is null) { t = new D(&lt;&gt;9, &lt;Outer&gt;b__N_M); &lt;&gt;9__N_M = t; s = t; }
///   // ... use s
/// </code>
/// When the cached delegate is passed as a call argument, the compiler
/// interleaves the receiver's evaluation between the load and the guard
/// (<c>s = &lt;&gt;9__; recv = …; r = s; if (s is null) …</c>) — the shape every
/// LINQ and <c>Array.FindAll</c>-style call site emits — so the load is found by
/// scanning back to it, not at a fixed offset.
/// This pass rewrites that whole dance to a single <c>s = new D(...)</c>,
/// dropping the cache field, the null guard, and the carrier slots. The cache is
/// a pure codegen artifact (re-emitting the C# restores an equivalent one), so
/// erasing it is sound for the raised view — only fidelity to the exact IL is
/// lost, which the lowered view never promises here.
///
/// <para>Anchored on the cache field's <c>&lt;&gt;9__</c> name — only this idiom
/// emits it. Runs before the second inlining pass, which folds the surviving
/// carrier slot into its use; <see cref="LambdaRaisingPass"/> then raises the
/// bare delegate creation to the lambda itself. A <see cref="NativePasses"/>
/// member (it inverts a codegen artifact, not a named Roslyn lowering).</para>
/// </summary>
public sealed class LambdaCachePass : IIrPass
{
    public string Name => "lambda-cache";

    public void Run(IrFunction function, PassContext context)
    {
        foreach (var ifStmt in function.Descendants.OfType<IfStatement>().ToList())
        {
            if (ifStmt.Parent is not Block block || ifStmt.HasElse || ifStmt.ChildIndex < 2)
                continue;
            if (ifStmt.Condition is not LogicalNot { Operand: LoadStackSlot cacheRead })
                continue;

            // Then arm: t = new D(...); <>9__N_M = t; result = t;  (one carrier slot t)
            if (ifStmt.Then.Children is not
                [
                    StoreStackSlot { Value: DelegateCreation delegateCreation } createStore,
                    StoreField { Field: { } cacheField, Value: LoadStackSlot fieldCarrier },
                    StoreStackSlot { Value: LoadStackSlot resultCarrier } resultStore,
                ])
                continue;
            if (!IsClosureCacheField(cacheField))
                continue;
            int carrierSlot = createStore.Slot;
            if (fieldCarrier.Slot != carrierSlot || resultCarrier.Slot != carrierSlot)
                continue;
            int resultSlot = resultStore.Slot;

            // The seed (result = cacheSlot) sits immediately before the guard.
            if (block.Children[ifStmt.ChildIndex - 1] is not
                    StoreStackSlot { Value: LoadStackSlot seedFrom } seedResult)
                continue;
            if (seedResult.Slot != resultSlot || seedFrom.Slot != cacheRead.Slot)
                continue;

            // The cache load (cacheSlot = <>9__N_M) sits earlier in the same
            // block, but not necessarily adjacent: when the cached delegate is an
            // argument the compiler interleaves the call receiver's evaluation
            // between the load and the guard — `s = <>9__; recv = xs; r = s; if (s
            // is null) ...`, the shape every LINQ/`Array.FindAll`-style call emits.
            // Scan back for the load rather than fixing its offset, stopping at any
            // other statement that touches the cache slot (which would mean this is
            // not the load we are erasing, so the whole-block precondition fails).
            StoreStackSlot? cacheLoad = null;
            for (int j = ifStmt.ChildIndex - 2; j >= 0; j--)
            {
                if (block.Children[j] is StoreStackSlot { Value: LoadField { Field: { } loadedField, Instance: null } } candidate
                    && candidate.Slot == cacheRead.Slot && loadedField == cacheField)
                {
                    cacheLoad = candidate;
                    break;
                }
                if (ReferencesSlot(block.Children[j], cacheRead.Slot))
                    break;
            }
            if (cacheLoad is null)
                continue;

            context.Stepper.StepOver($"collapse lazy delegate cache {cacheField.Name}", ifStmt);
            delegateCreation.Detach();
            ifStmt.ReplaceWith(new StoreStackSlot(resultSlot, delegateCreation));
            seedResult.Detach();
            cacheLoad.Detach();
        }
    }

    // The compiler names the per-lambda cache field <>9__N_M (on the static <>c
    // closure holder); the name is the unique anchor for the whole idiom.
    static bool IsClosureCacheField(FieldRef field)
        => field.Name.StartsWith("<>9__", StringComparison.Ordinal);

    // Whether the statement reads or writes the given stack slot anywhere in its
    // subtree — the guard for what may be interleaved ahead of the cache load.
    static bool ReferencesSlot(IrNode node, int slot)
        => node.Descendants.Prepend(node).Any(n =>
            n is LoadStackSlot { Slot: var read } && read == slot
            || n is StoreStackSlot { Slot: var written } && written == slot);
}
