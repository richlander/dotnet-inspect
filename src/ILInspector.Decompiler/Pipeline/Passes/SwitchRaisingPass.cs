using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises an IL jump table into a C# <c>switch</c> statement, before
/// structuring. A <c>switch (v) goto [t0, t1, …]</c> dispatches to t_v for v in
/// [0, n) and falls through (the default) otherwise. csc lays the case bodies
/// out after the switch — and often the default body after the cases — each
/// ending by branching to a common join (the break target), by returning /
/// throwing, or (for the section physically before the join, whose branch the
/// redundant-branch pass already elided) by falling through into it.
///
/// This pass recognizes the dense single-block-section shape: every distinct
/// case target, and the default, is one block with no interior control flow
/// that ends in a terminator, a branch to the shared join, or a fall-through
/// into the join. The default may instead be a bare dispatch jumping once to
/// its body. The blocks the sections occupy must form the contiguous span
/// immediately after the switch, reach nothing else, and be entered only
/// through the table — otherwise the switch is left flat. Each section exits
/// through <c>break;</c> (its join branch, or an appended one for a
/// fall-through); the bodies are containers the structuring pass then raises.
/// </summary>
public sealed class SwitchRaisingPass : IIrPass
{
    public string Name => "switch";

    enum SectionKind { NotSimple, Terminates, Branches, FallsThrough }

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOne(function))
        {
        }
    }

    static bool FoldOne(IrFunction function)
    {
        var leaveTargets = function.Descendants.OfType<Leave>()
            .Select(leave => leave.TargetOffset)
            .ToHashSet();
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            var blocks = container.Blocks;
            for (int s = 0; s < blocks.Count; s++)
            {
                if (blocks[s].Children is [.., SwitchBranch sw] && Raise(container, s, sw, leaveTargets))
                    return true;
            }
        }
        return false;
    }

    static bool Raise(BlockContainer container, int s, SwitchBranch sw, HashSet<int> leaveTargets)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        int defaultIndex = s + 1;
        if (defaultIndex >= blocks.Count)
            return false;

        // Resolve the table, all forward of the switch.
        var caseTargets = new int[sw.TargetOffsets.Length];
        for (int i = 0; i < caseTargets.Length; i++)
        {
            if (!offsetToIndex.TryGetValue(sw.TargetOffsets[i], out caseTargets[i]) || caseTargets[i] <= s)
                return false;
        }

        var owned = new HashSet<int>();
        var fallThroughs = new List<int>();
        int? join = null;
        bool Unify(int j) => (join ??= j) == j;

        // Each distinct case target is one simple block: terminator, branch to
        // the shared join, or fall-through into the join (its branch elided).
        foreach (int target in caseTargets.Distinct())
        {
            var (kind, j) = Classify(blocks[target], offsetToIndex);
            if (kind == SectionKind.NotSimple)
                return false;
            if (kind == SectionKind.Branches && !Unify(j))
                return false;
            if (kind == SectionKind.FallsThrough)
                fallThroughs.Add(target);
            owned.Add(target);
        }

        // The default: a bare dispatch to a separate body laid out after the
        // cases, a bare jump to the join (omitted), or an inline section.
        int? defaultBody;
        var dispatch = blocks[defaultIndex];
        if (dispatch.Children is [Branch bare]
            && offsetToIndex.TryGetValue(bare.TargetOffset, out int bareTarget)
            && bareTarget > s
            && (join is null || bareTarget != join)
            && !caseTargets.Contains(bareTarget))
        {
            var (kind, j) = Classify(blocks[bareTarget], offsetToIndex);
            if (kind == SectionKind.NotSimple)
                return false;
            if (kind == SectionKind.Branches && !Unify(j))
                return false;
            if (kind == SectionKind.FallsThrough)
                fallThroughs.Add(bareTarget);
            owned.Add(defaultIndex);
            owned.Add(bareTarget);
            defaultBody = bareTarget;
        }
        else if (dispatch.Children is [Branch toJoin]
            && offsetToIndex.TryGetValue(toJoin.TargetOffset, out int dj) && join == dj)
        {
            owned.Add(defaultIndex);
            defaultBody = null;   // empty default — C# omits it
        }
        else
        {
            var (kind, j) = Classify(dispatch, offsetToIndex);
            if (kind == SectionKind.NotSimple)
                return false;
            if (kind == SectionKind.Branches && !Unify(j))
                return false;
            if (kind == SectionKind.FallsThrough)
                fallThroughs.Add(defaultIndex);
            owned.Add(defaultIndex);
            defaultBody = defaultIndex;
        }

        // A block can back exactly one section. A case target that is also the
        // default body (a case sharing the default's code) would be re-parented
        // twice when building — leave such a switch flat rather than merge them.
        if (defaultBody is { } shared && caseTargets.Contains(shared))
            return false;

        // A fall-through section must fall straight into the join (its block is
        // the one right before it). With no branch to fix the join, a single
        // fall-through defines it; more than one cannot share a fall edge.
        if (fallThroughs.Count > 0)
        {
            if (join is null)
            {
                if (fallThroughs.Count != 1)
                    return false;
                join = fallThroughs[0] + 1;
            }
            if (fallThroughs.Any(fb => fb + 1 != join))
                return false;
        }

        // The owned blocks must be exactly the contiguous span [s+1, regionEnd):
        // no foreign block interleaved, the join (if any) right past them.
        int regionEnd = join ?? owned.Max() + 1;
        if (join is { } jn && (jn <= s || owned.Contains(jn)))
            return false;
        if (owned.Count != regionEnd - defaultIndex)
            return false;
        foreach (int idx in owned)
            if (idx < defaultIndex || idx >= regionEnd)
                return false;

        // Nothing outside the switch (the block s aside, which dispatches) may
        // enter the owned blocks — including a leave from another container.
        if (!OnlyReachedByTable(blocks, owned, s, leaveTargets))
            return false;

        Build(container, s, sw, caseTargets, owned, defaultBody, join, regionEnd);
        return true;
    }

    /// <summary>How a candidate section block leaves: a terminator, one forward branch, a fall-through, or unfit (interior control flow).</summary>
    static (SectionKind Kind, int Join) Classify(Block block, Dictionary<int, int> offsetToIndex)
    {
        for (int i = 0; i < block.Children.Count - 1; i++)
            if (block.Children[i] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return (SectionKind.NotSimple, -1);
        if (block.Children.Count == 0)
            return (SectionKind.FallsThrough, -1);
        return block.Children[^1] switch
        {
            Return or Throw => (SectionKind.Terminates, -1),
            Branch branch when offsetToIndex.TryGetValue(branch.TargetOffset, out int j) => (SectionKind.Branches, j),
            Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter => (SectionKind.NotSimple, -1),
            _ => (SectionKind.FallsThrough, -1),
        };
    }

    static bool OnlyReachedByTable(IReadOnlyList<Block> blocks, HashSet<int> owned, int s, HashSet<int> leaveTargets)
    {
        var ownedOffsets = owned.Select(i => blocks[i].StartOffset).ToHashSet();
        if (ownedOffsets.Overlaps(leaveTargets))
            return false;
        for (int idx = 0; idx < blocks.Count; idx++)
        {
            if (idx == s || owned.Contains(idx))
                continue;   // the switch dispatches the table; owned-internal edges are fine
            foreach (var node in blocks[idx].Children)
                foreach (int target in Targets(node))
                    if (ownedOffsets.Contains(target))
                        return false;
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

    static void Build(
        BlockContainer container, int s, SwitchBranch sw, int[] caseTargets,
        HashSet<int> owned, int? defaultBody, int? join, int regionEnd)
    {
        var all = container.Blocks.ToList();
        int? joinOffset = join is { } j ? all[j].StartOffset : null;

        // Case labels grouped by target — the jump-table index is the label.
        var labelsByTarget = new Dictionary<int, List<int>>();
        for (int i = 0; i < caseTargets.Length; i++)
            (labelsByTarget.TryGetValue(caseTargets[i], out var list) ? list : labelsByTarget[caseTargets[i]] = []).Add(i);

        foreach (var block in all)
            block.Detach();

        var sections = new List<SwitchSection>();
        foreach (var (target, labels) in labelsByTarget.OrderBy(kv => kv.Value.Min()))
            sections.Add(new SwitchSection([.. labels], isDefault: false, SectionBody(all[target], joinOffset)));
        if (defaultBody is { } db)
            sections.Add(new SwitchSection([], isDefault: true, SectionBody(all[db], joinOffset)));

        var switchBlock = all[s];
        var value = (IrExpression)sw.DetachChildren()[0];
        sw.Detach();
        switchBlock.Add(new Switch(value, sections));

        var rebuilt = new BlockContainer();
        for (int idx = 0; idx <= s; idx++)
            rebuilt.Add(all[idx]);
        for (int idx = regionEnd; idx < all.Count; idx++)
            rebuilt.Add(all[idx]);
        container.ReplaceWith(rebuilt);
    }

    /// <summary>
    /// Wraps a detached section block in a body container, ensuring it exits
    /// the switch: a branch to the join becomes <c>break;</c>, and a
    /// fall-through section (no terminator — its join branch was elided, or it
    /// was empty) gets an explicit <c>break;</c> appended. Return/throw stay.
    /// </summary>
    static BlockContainer SectionBody(Block block, int? joinOffset)
    {
        var last = block.Children.Count > 0 ? block.Children[^1] : null;
        if (joinOffset is { } jo && last is Branch branch && branch.TargetOffset == jo)
        {
            branch.Detach();
            block.Add(new Break());
        }
        else if (last is not (Return or Throw))
        {
            block.Add(new Break());
        }
        var body = new BlockContainer();
        body.Add(block);
        return body;
    }
}
