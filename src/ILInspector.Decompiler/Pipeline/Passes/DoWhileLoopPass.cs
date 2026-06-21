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
/// single-entry region with one back edge, no EH leave inside, and exits only
/// to its single canonical exit block — those exits raise to <c>break;</c> (an
/// unconditional branch) or <c>if (cond) break;</c> (a conditional one). A
/// second back edge (nested or irreducible) or a second distinct exit target
/// (which would need a labeled break) keeps it flat. Innermost loops wrap
/// first, so nested do-whiles compose across the fixpoint.
/// </summary>
public sealed class DoWhileLoopPass : IIrPass
{
    public string Name => "do-while";

    public void Run(IrFunction function, PassContext context)
    {
        while (TransformOne(function, context.Stepper))
        {
        }
    }

    static bool TransformOne(IrFunction function, Stepper stepper)
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
                Wrap(container, loop.Header, loop.Bottom, loop.Edge, stepper);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Pure shape check: the region [header, bottom] is a reducible loop whose
    /// only back edge is <paramref name="backEdge"/> and whose only exits are
    /// breaks to the single canonical exit block (the block right after the
    /// loop, where the back edge's false path also lands). The ordinary shape is
    /// single-entry; a single-block self-loop also permits external branches to
    /// its header, because that is the loop's only entry and appears in csc's
    /// partition-style nested scan loops.
    /// </summary>
    static bool Validate(
        IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex,
        int header, int bottom, ConditionalBranch backEdge)
    {
        // The loop exits, normally, by falling through past the bottom test to
        // the next block — so that block is the one and only place a break may
        // target. A second distinct exit target would need a labeled break.
        int exitIndex = bottom + 1;

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
                    {
                        if (target != exitIndex)
                            return false;   // a non-canonical exit — needs a labeled break
                        continue;           // a break to the loop's single exit
                    }
                    if (!sourceInside && targetInside)
                    {
                        if (header == bottom && target == header)
                            continue;   // external branch to a single-block loop header
                        return false;   // an external jump into the loop body
                    }
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

    static void Wrap(BlockContainer container, int header, int bottom, ConditionalBranch backEdge, Stepper stepper)
    {
        var blocks = container.Blocks;
        var condition = (IrExpression)backEdge.DetachChildren()[0];
        backEdge.Detach();   // strip the back edge from the bottom block

        // Raise exit branches to break before the bodies move. The canonical
        // exit (validated as the only exit target) is the block right after the
        // loop; an unconditional branch there becomes `break;`, a conditional
        // branch becomes `if (cond) break;` (the false path keeps falling
        // through, exactly as the branch did).
        if (bottom + 1 < blocks.Count)
        {
            int exitOffset = blocks[bottom + 1].StartOffset;
            for (int i = header; i <= bottom; i++)
            {
                if (blocks[i].Children is not [.., var lastNode])
                    continue;
                if (lastNode is Branch branch && branch.TargetOffset == exitOffset)
                {
                    branch.Detach();
                    blocks[i].Add(new Break());
                }
                else if (lastNode is ConditionalBranch exit && exit.TargetOffset == exitOffset)
                {
                    var breakCondition = (IrExpression)exit.DetachChildren()[0];
                    exit.Detach();
                    var thenArm = new Block(blocks[i].StartOffset);
                    thenArm.Add(new Break());
                    blocks[i].Add(new IfStatement(breakCondition, thenArm, null));
                }
            }
        }

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

        stepper.StepOver("raise back-edge loop to do-while", container);
        container.ReplaceWith(rebuilt);
    }
}
