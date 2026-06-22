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
/// <item><c>region-internal</c> — a residual branch inside a try/finally body, not
///   crossing out: the safest reduction (slice 3).</item>
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
        if (residualBranches.Any(branch => HasAncestor<TryFinally>(branch) || HasAncestor<TryCatch>(branch)))
            return "region-internal";
        if (residualBranches.Count > 0)
            return "prologue-epilogue-guard";

        return "leave-exit-merge";
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
