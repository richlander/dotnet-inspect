using ILInspector.Decompiler.Pipeline;

/// <summary>
/// Sub-classifies a method already in the <c>eh-entangled</c> conditional-branch
/// bucket (<see cref="ConditionalBranchShapeClassifier"/>) into the subshapes of
/// the EH-aware structuring burndown (#1089). Returns the single most-blocking
/// subshape per method, hardest first (so a method's bucket names the work that
/// must land for it to fully raise).
///
/// <para>The EH structuring pass is transactional: a method either recovers its
/// regions completely into <see cref="TryFinally"/>/<see cref="TryCatch"/> nodes,
/// or keeps the whole EH flat (no region nodes). So the first split is whether EH
/// structured at all — if it did not, the blocker is the EH pass, not
/// conditional-branch structuring:</para>
/// <list type="bullet">
/// <item><c>filter</c> — EH stayed flat with a surviving <see cref="EndFilter"/>:
///   the EH pass bails on exception filters. Fix is filter support in the EH pass.</item>
/// <item><c>eh-unstructured</c> — EH stayed flat for another reason (fault,
///   filterless catch, or a nesting the EH pass declines); no region nodes. Fix is
///   in the EH pass; conditional-branch structuring can only follow.</item>
/// </list>
/// <para>When EH <em>did</em> structure into nodes, the residual branch/leave is
/// classified against the recovered regions:</para>
/// <list type="bullet">
/// <item><c>leave-retry-loop</c> — a backward <see cref="Leave"/> (a retry loop
///   around the region, e.g. <c>Interop.Sys::GetCwd</c>). Overlaps loop-residue (slice 6).</item>
/// <item><c>handler-internal</c> — a residual branch inside a <see cref="CatchClause"/>
///   body (handler-scope risk, slice 7).</item>
/// <item><c>region-internal</c> — a residual branch inside a try/finally body whose
///   region body is otherwise consumable: the genuine EH-safe reduction (slice 3).</item>
/// <item><c>region-internal-shared-merge</c> — inside a region body too, but blocked
///   by a non-EH shape (shared forward merge / cond-target-past-region), so it
///   belongs with the shared-merge work (#1081), not an EH-safe reduction (#1096).</item>
/// <item><c>prologue-epilogue-guard</c> — every residual branch lies outside every
///   recovered region (prologue/epilogue of an EH-bearing method, slice 4).</item>
/// <item><c>leave-exit-merge</c> — the remainder: forward leaves to a common
///   post-region merge (slice 5).</item>
/// </list>
/// </summary>
static class EhShapeClassifier
{
    public static string Classify(IrFunction function)
    {
        // Transactional EH structuring: regions are either fully recovered as
        // nodes or the whole method stays flat. No region node => the EH itself
        // did not structure, so the blocker is the EH pass, not the conditional
        // branch — split those out first.
        bool ehStructured = function.Descendants.Any(n => n is TryFinally or TryCatch);
        if (!ehStructured)
            return function.Descendants.OfType<EndFilter>().Any() ? "filter" : "eh-unstructured";

        foreach (var leave in function.Descendants.OfType<Leave>())
            if (EnclosingBlockOffset(leave) is { } from && leave.TargetOffset <= from)
                return "leave-retry-loop";

        var residualBranches = function.Descendants.OfType<ConditionalBranch>().ToList();
        if (residualBranches.Any(branch => HasAncestor<CatchClause>(branch)))
            return "handler-internal";
        var regionBranches = residualBranches
            .Where(branch => HasAncestor<TryFinally>(branch) || HasAncestor<TryCatch>(branch))
            .ToList();
        if (regionBranches.Count > 0)
        {
            // A branch inside a recovered region body is a pure EH-safe reduction
            // (slice 3) only if the body is otherwise consumable. If its enclosing
            // container carries a shared forward merge (>=2 branches to one forward
            // target) or a branch targeting past the region, the real blocker is
            // that non-EH shape — the same shared-forward-merge / cond-target-past-
            // region work as #1081, not an EH boundary. Route it there instead of
            // advertising a false EH-safe count (#1096).
            bool nonEhBlocker = regionBranches.Any(branch =>
                EnclosingContainer(branch) is { } container
                && (HasSharedForwardMerge(container) || HasConditionalTargetPastContainer(container)));
            return nonEhBlocker ? "region-internal-shared-merge" : "region-internal";
        }
        if (residualBranches.Count > 0)
            return "prologue-epilogue-guard";

        return "leave-exit-merge";
    }

    /// <summary>The nearest <see cref="BlockContainer"/> ancestor — the region body the branch sits in.</summary>
    static BlockContainer? EnclosingContainer(IrNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (current is BlockContainer container)
                return container;
        return null;
    }

    /// <summary>A forward target reached by two or more branches (the shared-merge signature, mirroring <see cref="ConditionalBranchShapeClassifier"/>).</summary>
    static bool HasSharedForwardMerge(BlockContainer container)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;
        var forwardPredecessors = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            foreach (int target in BranchTargets(blocks[i]))
                if (offsetToIndex.TryGetValue(target, out int ti) && ti > i)
                    forwardPredecessors[ti] = forwardPredecessors.GetValueOrDefault(ti) + 1;
        return forwardPredecessors.Values.Any(count => count >= 2);
    }

    /// <summary>A conditional branch whose target is no block in this container — the cond-target-past-region shape.</summary>
    static bool HasConditionalTargetPastContainer(BlockContainer container)
    {
        var offsets = container.Blocks.Select(block => block.StartOffset).ToHashSet();
        return container.Blocks
            .SelectMany(block => block.Children)
            .OfType<ConditionalBranch>()
            .Any(branch => !offsets.Contains(branch.TargetOffset));
    }

    static IEnumerable<int> BranchTargets(Block block)
    {
        foreach (var node in block.Children)
            switch (node)
            {
                case Branch b: yield return b.TargetOffset; break;
                case ConditionalBranch c: yield return c.TargetOffset; break;
                case SwitchBranch sw: foreach (int t in sw.TargetOffsets) yield return t; break;
                case Leave lv: yield return lv.TargetOffset; break;
            }
    }

    static int? EnclosingBlockOffset(IrNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (current is Block block)
                return block.StartOffset;
        return null;
    }

    static bool HasAncestor<T>(IrNode node) where T : IrNode
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (current is T)
                return true;
        return false;
    }
}
