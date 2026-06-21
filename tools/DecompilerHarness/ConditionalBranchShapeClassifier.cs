using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Sub-classifies a method in the <c>structuring: conditional-branch</c> bucket by
/// the structural shape of the residual control flow around its first surviving
/// <see cref="ConditionalBranch"/>. Backs <c>--gaps --by-shape</c>: turning the
/// flat bucket count into a shape histogram is what scopes the next
/// <see cref="StructuringPass"/> slice (issue #921).
///
/// <para>Unlike <see cref="SwitchShapeClassifier"/> — which reads the imported
/// (pre-pass) tree because the passes flatten an unraised switch — a residual
/// conditional branch survives in the FINISHED tree: when the structuring slice
/// rejects a container it keeps the always-correct flat block list, so the
/// surviving <c>if (c) goto</c> is read directly from the post-pass function.</para>
///
/// <para>The buckets are checked in priority order, most-blocking first, and each
/// was verified against a <c>--dump</c> of a representative method. They mirror the
/// container-level stop reasons of <c>--structuring-stops</c>, attributed here to
/// the method that lands in the bucket:</para>
/// <list type="bullet">
/// <item><c>loop-residue</c> — the container has a backward branch (a do/while,
///   <c>continue</c>, or iterator state machine the loop passes did not raise,
///   e.g. TypeSourceComposer::ShortenSegment); out of scope for forward
///   structuring.</item>
/// <item><c>eh-entangled</c> — an unstructured EH terminator (<see cref="Leave"/>/
///   <see cref="EndFinally"/>/<see cref="EndFilter"/>) survives, so the conditional
///   sits in or beside a try/catch/finally the EH pass left flat (e.g.
///   TypeSourceComposer::ComposeFields).</item>
/// <item><c>comparison-tree</c> — four or more constant comparisons on a value (a
///   csc sparse <c>switch</c> or range cascade lowered to an if-tree, e.g.
///   HttpClientFactory::IsNonPublic); a dedicated multi-way raise.</item>
/// <item><c>shared-forward-merge</c> — a forward target reached by two or more
///   branches: a genuine shared join past a region that strict nesting cannot
///   name (the <c>cond-target-past-region</c>/<c>forward-branch-not-region-exit</c>
///   family, e.g. TypeSourceComposer::ComposeMembers).</item>
/// <item><c>nonnested-forward-guards</c> — only forward branches, each to a
///   single-predecessor target, whose ranges interleave instead of nesting: the
///   csc lowering for an <c>is</c>-pattern / type-test dispatch or a short-circuit
///   guard chain (e.g. CSharpPrinter::IsUnsafeOperation, SamePlace). The
///   single-predecessor sibling of <c>shared-forward-merge</c> — the index-range
///   slice rejects the overlap. (Had the forward branches nested cleanly the
///   method would already be raised, so this is never a trivial single guard.)</item>
/// </list>
/// </summary>
static class ConditionalBranchShapeClassifier
{
    /// <summary>Mirrors ComparisonTrees.MinComparisons (internal to the decompiler): the sparse-switch floor.</summary>
    const int MinComparisons = 4;

    public static string Classify(IrFunction function)
    {
        // The container holding the first residual conditional branch. A fully
        // raised method keeps none; the caller filtered to the bucket that does.
        BlockContainer? container = null;
        foreach (var c in function.Descendants.OfType<BlockContainer>())
        {
            if (c.Blocks.Any(b => b.Children.Count > 0 && b.Children.Any(n => n is ConditionalBranch)))
            {
                container = c;
                break;
            }
        }
        if (container is null)
            return "no-residual-conditional";   // defensive: caller filtered to the bucket

        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        // 1. Loop residue: any branch in the container targets a block at or before
        // the branching block — a back-edge, the signature of an unraised loop or a
        // mid-body continue. Forward structuring cannot consume it.
        for (int i = 0; i < blocks.Count; i++)
            foreach (int t in BranchTargets(blocks[i]))
                if (offsetToIndex.TryGetValue(t, out int ti) && ti <= i)
                    return "loop-residue";

        // 2. EH entanglement: an unstructured EH terminator survives anywhere in the
        // method, so the EH pass left a try/catch/finally flat and the conditional
        // cannot structure across its boundary.
        if (function.Descendants.Any(n => n is Leave or EndFinally or EndFilter))
            return "eh-entangled";

        // 3. Comparison tree: four or more constant comparisons (csc's sparse switch
        // or range cascade as an if-tree). Mirrors ComparisonTrees.IsLikely.
        int constantComparisons = 0;
        foreach (var block in blocks)
            if (block.Children.Count > 0
                && block.Children[^1] is ConditionalBranch { Condition: Comparison comparison }
                && (comparison.Left is Constant || comparison.Right is Constant))
                constantComparisons++;
        if (constantComparisons >= MinComparisons)
            return "comparison-tree";

        // 4. Shared forward merge: a forward target reached by two or more branches.
        // A genuine multi-goto join that strict nesting cannot express — the merge
        // past the region the index-range model could not name.
        var forwardPredecessors = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            foreach (int t in BranchTargets(blocks[i]))
                if (offsetToIndex.TryGetValue(t, out int ti) && ti > i)
                    forwardPredecessors[ti] = forwardPredecessors.GetValueOrDefault(ti) + 1;
        if (forwardPredecessors.Values.Any(count => count >= 2))
            return "shared-forward-merge";

        // Only forward branches remain, each to a single-predecessor target: had
        // they nested cleanly the method would be raised, so their ranges
        // interleave (the is-pattern/type-test dispatch and short-circuit chains).
        return "nonnested-forward-guards";
    }

    static IEnumerable<int> BranchTargets(Block block)
    {
        foreach (var node in block.Children)
        {
            switch (node)
            {
                case Branch b: yield return b.TargetOffset; break;
                case ConditionalBranch c: yield return c.TargetOffset; break;
                case SwitchBranch sw:
                    foreach (int t in sw.TargetOffsets) yield return t;
                    break;
                case Leave lv: yield return lv.TargetOffset; break;
            }
        }
    }
}
