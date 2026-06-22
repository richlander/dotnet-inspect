using ILInspector.Decompiler.Pipeline;

/// <summary>
/// Sub-classifies a method already in the <c>eh-entangled</c> conditional-branch
/// bucket (<see cref="ConditionalBranchShapeClassifier"/>) into the subshapes of
/// the EH-aware structuring burndown (#1089). The EH regions are already
/// recovered as <see cref="TryFinally"/>/<see cref="TryCatch"/> nodes; what stays
/// flat is the branch/leave interacting with them. Returns the single
/// most-blocking subshape per method, hardest first (so a method's bucket names
/// the work that must land for it to fully raise):
/// <list type="bullet">
/// <item><c>filter</c> — a surviving <see cref="EndFilter"/>: an exception filter
///   the structurer left flat. Highest EH-legality risk (slice 7).</item>
/// <item><c>leave-retry-loop</c> — a surviving <see cref="Leave"/> whose target is
///   at or before its own block: a backward leave (a retry loop around the
///   region, e.g. <c>Interop.Sys::GetCwd</c>). Overlaps loop-residue (slice 6).</item>
/// <item><c>handler-internal</c> — a residual <see cref="ConditionalBranch"/> inside
///   a <see cref="CatchClause"/> body (handler-scope risk, slice 7).</item>
/// <item><c>region-internal</c> — a residual branch inside a try/finally body but
///   not crossing out: the safest reduction (slice 3).</item>
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
        if (function.Descendants.OfType<EndFilter>().Any())
            return "filter";

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
