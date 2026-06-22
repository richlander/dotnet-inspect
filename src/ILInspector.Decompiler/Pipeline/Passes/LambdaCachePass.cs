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
        while (FoldFlatCache(function, context.Stepper))
        {
        }

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
            if (!IsDelegateCacheField(cacheField))
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

    static bool FoldFlatCache(IrFunction function, Stepper stepper)
    {
        var leaveTargets = function.Descendants.OfType<Leave>()
            .Select(leave => leave.TargetOffset)
            .ToHashSet();
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            if (TryFoldFlatCache(container, leaveTargets, stepper))
                return true;
        }
        return false;
    }

    static bool TryFoldFlatCache(BlockContainer container, HashSet<int> leaveTargets, Stepper stepper)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        for (int i = 0; i + 1 < blocks.Count; i++)
        {
            var block = blocks[i];
            if (block.Children.Count == 0
                || block.Children[^1] is not ConditionalBranch { Condition: LoadStackSlot cacheRead } branch)
            {
                continue;
            }
            if (!offsetToIndex.TryGetValue(branch.TargetOffset, out int joinIndex)
                || joinIndex != i + 2
                || leaveTargets.Contains(blocks[i + 1].StartOffset))
            {
                continue;
            }

            var createBlock = blocks[i + 1];
            if (createBlock.Children is not
                [
                    StoreStackSlot { Value: DelegateCreation delegateCreation } createStore,
                    StoreField { Field: { } cacheField, Instance: null, Value: LoadStackSlot fieldCarrier },
                    StoreStackSlot { Value: LoadStackSlot resultCarrier } resultStore,
                ])
            {
                continue;
            }
            if (!IsDelegateCacheField(cacheField))
                continue;
            int carrierSlot = createStore.Slot;
            if (fieldCarrier.Slot != carrierSlot || resultCarrier.Slot != carrierSlot)
                continue;

            if (FindSeedAndLoad(block, cacheRead.Slot, resultStore.Slot, cacheField) is not { } prior)
                continue;
            if (HasExternalEntry(blocks, i + 1))
                continue;

            delegateCreation.Detach();
            var replacement = new StoreStackSlot(resultStore.Slot, delegateCreation);
            replacement.InheritSourceOffset(createStore);
            stepper.StepOver($"collapse flat lazy delegate cache {cacheField.Name}", branch);
            branch.Detach();
            prior.Seed.Detach();
            prior.CacheLoad.Detach();
            block.Add(replacement);
            createBlock.Detach();
            return true;
        }
        return false;
    }

    sealed record PriorStores(StoreStackSlot Seed, StoreStackSlot CacheLoad);

    static PriorStores? FindSeedAndLoad(Block block, int cacheSlot, int resultSlot, FieldRef cacheField)
    {
        StoreStackSlot? seed = null;
        for (int j = block.Children.Count - 2; j >= 0; j--)
        {
            if (seed is null)
            {
                if (block.Children[j] is StoreStackSlot { Slot: var seedSlot, Value: LoadStackSlot from }
                    && seedSlot == resultSlot && from.Slot == cacheSlot)
                {
                    seed = (StoreStackSlot)block.Children[j];
                    continue;
                }
                if (ReferencesSlot(block.Children[j], cacheSlot))
                    return null;
                continue;
            }

            if (block.Children[j] is StoreStackSlot { Slot: var loadedSlot, Value: LoadField { Field: { } loadedField, Instance: null } } candidate
                && loadedSlot == cacheSlot && loadedField == cacheField)
            {
                return new PriorStores(seed, candidate);
            }
            if (ReferencesSlot(block.Children[j], cacheSlot))
                return null;
        }
        return null;
    }

    static bool HasExternalEntry(IReadOnlyList<Block> blocks, int targetIndex)
    {
        int targetOffset = blocks[targetIndex].StartOffset;
        for (int i = 0; i < blocks.Count; i++)
        {
            if (i == targetIndex - 1 || i == targetIndex)
                continue;
            foreach (var node in blocks[i].Children)
                foreach (int target in Targets(node))
                    if (target == targetOffset)
                        return true;
        }
        return false;
    }

    static IEnumerable<int> Targets(IrNode node) => node switch
    {
        Branch branch => [branch.TargetOffset],
        ConditionalBranch conditional => [conditional.TargetOffset],
        SwitchBranch sw => sw.TargetOffsets,
        Leave leave => [leave.TargetOffset],
        _ => [],
    };

    // The compiler names the per-lambda cache field <>9__N_M (on the static <>c
    // closure holder) and static method-group cache fields <N>__MethodName (on
    // the <>O holder); the generated field name is the anchor for the idiom.
    static bool IsDelegateCacheField(FieldRef field)
        => field.Name.StartsWith("<>9__", StringComparison.Ordinal)
            || (field.DeclaringType.Name.EndsWith("+<>O", StringComparison.Ordinal)
                && field.Name.StartsWith("<", StringComparison.Ordinal)
                && field.Name.Contains(">__", StringComparison.Ordinal));

    // Whether the statement reads or writes the given stack slot anywhere in its
    // subtree — the guard for what may be interleaved ahead of the cache load.
    static bool ReferencesSlot(IrNode node, int slot)
        => node.Descendants.Prepend(node).Any(n =>
            n is LoadStackSlot { Slot: var read } && read == slot
            || n is StoreStackSlot { Slot: var written } && written == slot);
}
