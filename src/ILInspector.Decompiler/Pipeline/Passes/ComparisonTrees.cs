namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Recognizes the block container csc emits for a sparse <c>switch</c>: a chain
/// or tree of constant comparisons on one value, each arm storing its result and
/// jumping to a shared tail. The return-merge and guard-leaf inlining that raise
/// it as nested <c>if/else</c> are gated on this so they fire only for genuine
/// multi-way selection — a two- or three-way ternary stays on the path the
/// folding/boolean passes already raise cleanly, rather than being duplicated
/// into guard clauses.
/// </summary>
static class ComparisonTrees
{
    /// <summary>
    /// Four distinct constant tests is the floor: enough that the ternary and
    /// boolean-folding passes will not collapse the selection, so the structuring
    /// path is the one that produces readable C#. Below it the smaller selection
    /// shapes (ternaries, short-circuit &amp;&amp;/||) are left untouched.
    /// </summary>
    const int MinComparisons = 4;

    public static bool IsLikely(BlockContainer container)
    {
        int comparisons = 0;
        foreach (var block in container.Blocks)
            if (block.Children.Count > 0
                && block.Children[^1] is ConditionalBranch { Condition: Comparison comparison }
                && (comparison.Left is Constant || comparison.Right is Constant))
                comparisons++;
        return comparisons >= MinComparisons;
    }
}
