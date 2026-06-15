namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises do-while loops — the pervasive lock-free CAS retry shape
/// (<c>BODY; if (cond) goto BODY-start;</c>) — into <see cref="DoWhileLoop"/>.
/// The bottom block's conditional back edge to an earlier block in the same
/// container becomes the while condition exactly as written (it is the
/// stay-in-loop test); the body becomes a container the structuring pass then
/// raises. Runs before <see cref="StructuringPass"/>, which leaves any
/// container holding a back edge flat.
///
/// Transactional and conservative: the loop is raised only when it is a clean
/// single-entry region with no exit branch (a break), no second back edge
/// (nested or irreducible), and no EH leave inside — otherwise it stays flat.
/// Innermost loops wrap first, so nested do-whiles compose across the fixpoint.
/// </summary>
public sealed class DoWhileLoopPass : IIrPass
{
    public string Name => "do-while";

    public void Run(IrFunction function)
    {
        while (TransformOne(function))
        {
        }
    }

    static bool TransformOne(IrFunction function)
    {
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            var blocks = container.Blocks;
            var offsetToIndex = new Dictionary<int, int>();
            for (int i = 0; i < blocks.Count; i++)
                offsetToIndex[blocks[i].StartOffset] = i;

            // The innermost back edge (smallest region) first, so a nested loop
            // raises before the loop that contains it.
            (int Header, int Bottom, ConditionalBranch Edge)? best = null;
            for (int bottom = 0; bottom < blocks.Count; bottom++)
            {
                if (blocks[bottom].Children is not [.., ConditionalBranch edge]
                    || !offsetToIndex.TryGetValue(edge.TargetOffset, out int header)
                    || header > bottom)
                {
                    continue;   // not a backward conditional branch
                }
                if (best is null || bottom - header < best.Value.Bottom - best.Value.Header)
                    best = (header, bottom, edge);
            }

            if (best is { } loop
                && Validate(blocks, offsetToIndex, loop.Header, loop.Bottom, loop.Edge))
            {
                Wrap(container, loop.Header, loop.Bottom, loop.Edge);
                return true;
            }
        }
        return false;
    }

    /// <summary>Pure shape check: the region [header, bottom] is a reducible single-entry loop with one back edge and no break.</summary>
    static bool Validate(
        IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex,
        int header, int bottom, ConditionalBranch backEdge)
    {
        for (int source = 0; source < blocks.Count; source++)
        {
            bool sourceInside = source >= header && source <= bottom;
            foreach (var node in blocks[source].Children)
            {
                // EH control flow inside the loop is outside this slice.
                if (sourceInside && node is Leave or EndFinally or EndFilter)
                    return false;

                foreach (int targetOffset in Targets(node))
                {
                    if (!offsetToIndex.TryGetValue(targetOffset, out int target))
                        return false;   // a branch leaving the container — out of slice
                    bool targetInside = target >= header && target <= bottom;

                    if (sourceInside && !targetInside)
                        return false;   // an exit branch — a break, the next slice
                    if (!sourceInside && targetInside)
                        return false;   // an external jump into the loop body
                    if (sourceInside && !ReferenceEquals(node, backEdge) && target <= source)
                        return false;   // a second back edge — nested or irreducible
                }
            }
        }
        return true;
    }

    static IEnumerable<int> Targets(IrNode node) => node switch
    {
        Branch branch => [branch.TargetOffset],
        ConditionalBranch conditional => [conditional.TargetOffset],
        SwitchBranch sw => sw.TargetOffsets,
        _ => [],
    };

    static void Wrap(BlockContainer container, int header, int bottom, ConditionalBranch backEdge)
    {
        var blocks = container.Blocks;
        var condition = (IrExpression)backEdge.DetachChildren()[0];
        backEdge.Detach();   // strip the back edge from the bottom block

        foreach (var block in blocks)
            block.Detach();

        var body = new BlockContainer();
        for (int i = header; i <= bottom; i++)
            body.Add(blocks[i]);
        var holder = new Block(blocks[header].StartOffset);
        holder.Add(new DoWhileLoop(body, condition));

        var rebuilt = new BlockContainer();
        for (int i = 0; i < header; i++)
            rebuilt.Add(blocks[i]);
        rebuilt.Add(holder);
        for (int i = bottom + 1; i < blocks.Count; i++)
            rebuilt.Add(blocks[i]);

        container.ReplaceWith(rebuilt);
    }
}
