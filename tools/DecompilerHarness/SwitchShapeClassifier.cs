using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Sub-classifies a method whose top residual is a flat switch (the
/// <c>structuring: switch-branch</c> bucket) by the structural shape of its
/// first residual <see cref="SwitchBranch"/>. Backs <c>--gaps --by-shape</c>:
/// turning "14 flat switches" into a shape histogram is what scopes the next
/// SwitchRaisingPass slice (issue #682). Reads the raised tree — run the passes
/// first. The shapes mirror what SwitchRaisingPass's acceptance gate keys on, so
/// a shape's count is roughly the size of the relaxation that would clear it.
/// </summary>
static class SwitchShapeClassifier
{
    public static string Classify(IrFunction function)
    {
        var switchBranch = function.Descendants.OfType<SwitchBranch>().FirstOrDefault();
        if (switchBranch is null)
            return "no-residual-switch";   // defensive: caller filtered to the switch bucket

        // The container whose block ends in this switch. SwitchRaisingPass only
        // folds a switch that terminates its block; a mid-block switch (the switch
        // is not the block's last child) is a distinct, unhandled shape.
        var container = function.Descendants.OfType<BlockContainer>()
            .FirstOrDefault(c => c.Blocks.Any(b => b.Children.Count > 0 && ReferenceEquals(b.Children[^1], switchBranch)));
        if (container is null)
            return "switch-not-block-terminator";

        var blocks = container.Blocks;
        int switchIndex = -1;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
        {
            offsetToIndex[blocks[i].StartOffset] = i;
            if (blocks[i].Children.Count > 0 && ReferenceEquals(blocks[i].Children[^1], switchBranch))
                switchIndex = i;
        }

        // A table target that does not resolve to a forward block — back-edges
        // (loop tables) or cross-container targets the index model can't name.
        bool allForward = switchBranch.TargetOffsets.All(t =>
            offsetToIndex.TryGetValue(t, out int ti) && ti > switchIndex);
        if (!allForward)
            return "target-backward-or-unresolved";

        bool IsTerminator(int blockIndex) =>
            blocks[blockIndex].Children.Count > 0 && blocks[blockIndex].Children[^1] is Return or Throw;

        // A case body that branches into another case body which returns/throws —
        // the case-to-shared-terminator analogue of the default fold (#680).
        foreach (int targetOffset in switchBranch.TargetOffsets.Distinct())
        {
            var block = blocks[offsetToIndex[targetOffset]];
            if (block.Children.Count > 0 && block.Children[^1] is Branch caseBranch
                && switchBranch.TargetOffsets.Contains(caseBranch.TargetOffset)
                && offsetToIndex.TryGetValue(caseBranch.TargetOffset, out int sharedIndex)
                && IsTerminator(sharedIndex))
                return "case-branches-to-shared-terminator-case";
        }

        return "other";
    }
}
