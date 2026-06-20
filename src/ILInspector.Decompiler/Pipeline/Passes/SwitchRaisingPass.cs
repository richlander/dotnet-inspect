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
/// A section is the single-entry region rooted at a case target (or the
/// default body): the maximal run of blocks reachable from the head whose every
/// other block is entered only from within the region. A simple case is one
/// block; a case carrying an <c>if</c>/<c>?:</c> is several blocks with interior
/// control flow, which the structuring pass raises once the region is wrapped as
/// the section body. The regions must tile the contiguous span immediately after
/// the switch, share one join (or terminate), leave that join only through an
/// unconditional branch / fall-through (a conditional or switch escaping a
/// section is left flat for soundness), and be entered only through the table —
/// otherwise the switch is left flat. The default may instead be a bare dispatch
/// jumping once to its body, an omitted jump to the join, the case body the table
/// falls through to (the block right after the switch is itself a case target, so
/// <c>default:</c> folds onto it), or — when it jumps into a case body that
/// returns / throws — a <c>default:</c> label folded onto that shared case section
/// (<c>case N: default: throw;</c>). Each section exits
/// through <c>break;</c> (its join branch, or an appended one for a fall-through);
/// the bodies are containers the structuring pass then raises.
/// </summary>
public sealed class SwitchRaisingPass : IIrPass
{
    public string Name => "switch";

    public void Run(IrFunction function, PassContext context)
    {
        while (FoldOne(function, context.Stepper))
        {
        }
    }

    static bool FoldOne(IrFunction function, Stepper stepper)
    {
        var leaveTargets = function.Descendants.OfType<Leave>()
            .Select(leave => leave.TargetOffset)
            .ToHashSet();
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
        {
            var blocks = container.Blocks;
            for (int s = 0; s < blocks.Count; s++)
            {
                if (blocks[s].Children is [.., SwitchBranch sw] && Raise(container, s, sw, leaveTargets, stepper))
                    return true;
            }
        }
        return false;
    }

    static bool Raise(BlockContainer container, int s, SwitchBranch sw, HashSet<int> leaveTargets, Stepper stepper)
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

        var preds = BuildPredecessors(blocks, s, caseTargets, offsetToIndex);

        var owned = new HashSet<int>();
        int? join = null;
        bool Unify(int j) => (join ??= j) == j;

        // Each distinct case target grows into its single-entry region; the
        // region's exits unify to the shared join (or it terminates).
        var regions = new Dictionary<int, List<int>>();
        foreach (int target in caseTargets.Distinct())
        {
            if (!GrowRegion(blocks, target, s, offsetToIndex, preds, out var region, out var exits))
                return false;
            if (owned.Overlaps(region))
                return false;
            foreach (int e in exits)
                if (!Unify(e))
                    return false;
            regions[target] = region;
            owned.UnionWith(region);
        }

        // The default: a bare dispatch to a separate body laid out after the
        // cases, a bare jump to the join (omitted), an inline section, or — when
        // it routes into a case body that terminates — a `default:` label folded
        // onto that shared case section.
        int? defaultBodyHead = null;
        int? defaultSharesTarget = null;
        var dispatch = blocks[defaultIndex];
        if (caseTargets.Contains(defaultIndex))
        {
            // The block immediately after the switch is itself a case body: the
            // table's fall-through default lands on it, so the `default:` label
            // folds onto that case's section (`case N: default: …`). It is already
            // owned by that case — no separate default section is built.
            defaultSharesTarget = defaultIndex;
        }
        else if (dispatch.Children is [Branch d] && offsetToIndex.TryGetValue(d.TargetOffset, out int dt) && dt > s)
        {
            bool isCase = caseTargets.Contains(dt);
            if (isCase && regions.TryGetValue(dt, out var sharedRegion) && SectionTerminates(blocks, sharedRegion, offsetToIndex))
            {
                // Default jumps to a case body that returns/throws: fold the
                // `default:` label onto that section (`case N: default: throw;`).
                owned.Add(defaultIndex);
                defaultSharesTarget = dt;
            }
            else if (!isCase && join is { } jn && dt == jn)
            {
                // Empty default — a bare jump to the join; C# omits it.
                owned.Add(defaultIndex);
            }
            else if (!isCase)
            {
                // A separate default body laid out after the cases.
                if (!GrowRegion(blocks, dt, s, offsetToIndex, preds, out var region, out var exits))
                    return false;
                if (owned.Overlaps(region))
                    return false;
                foreach (int e in exits)
                    if (!Unify(e))
                        return false;
                owned.Add(defaultIndex);
                owned.UnionWith(region);
                regions[dt] = region;
                defaultBodyHead = dt;
            }
            else
            {
                return false;   // dispatch into a non-terminating case: leave flat
            }
        }
        else
        {
            // An inline default section beginning right after the switch.
            if (!GrowRegion(blocks, defaultIndex, s, offsetToIndex, preds, out var region, out var exits))
                return false;
            if (owned.Overlaps(region))
                return false;
            foreach (int e in exits)
                if (!Unify(e))
                    return false;
            owned.UnionWith(region);
            regions[defaultIndex] = region;
            defaultBodyHead = defaultIndex;
        }

        // The owned blocks must tile the contiguous span [s+1, regionEnd): the
        // join (if any) lies just past them, and no foreign block is interleaved.
        int regionEnd = join ?? owned.Max() + 1;
        if (join is { } j2 && (j2 <= s || owned.Contains(j2)))
            return false;
        if (owned.Count != regionEnd - defaultIndex)
            return false;
        foreach (int idx in owned)
            if (idx < defaultIndex || idx >= regionEnd)
                return false;

        // Every section escapes to the join only through an unconditional branch,
        // a fall-through, or a terminator — never a conditional/switch (which
        // would need `if (c) break;` synthesis we do not model here).
        foreach (var region in regions.Values)
            if (!ExitsAreUnconditional(blocks, region, offsetToIndex))
                return false;

        // Nothing outside the switch (the block s aside, which dispatches) may
        // enter the owned blocks — including a leave from another container.
        if (!OnlyReachedByTable(blocks, owned, s, leaveTargets))
            return false;

        Build(container, s, sw, caseTargets, regions, defaultBodyHead, defaultSharesTarget, join, regionEnd, stepper);
        return true;
    }

    /// <summary>Forward-edge predecessor map over the region [s, end), with the switch block's edges drawn from the table and its fall-through default.</summary>
    static Dictionary<int, List<int>> BuildPredecessors(IReadOnlyList<Block> blocks, int s, int[] caseTargets, Dictionary<int, int> offsetToIndex)
    {
        var preds = new Dictionary<int, List<int>>();
        void Add(int from, int to) => (preds.TryGetValue(to, out var list) ? list : preds[to] = []).Add(from);

        foreach (int target in caseTargets.Distinct())
            Add(s, target);
        if (s + 1 < blocks.Count)
            Add(s, s + 1);   // fall-through to the default dispatch

        // Scan every block (not just the region after the switch): a block before
        // the switch may branch into the join, which must keep the join out of a
        // section's single-entry region. The switch block itself ends in a
        // SwitchBranch (TrySuccessors returns false), so its edges stay table-only.
        for (int idx = 0; idx < blocks.Count; idx++)
        {
            if (idx == s)
                continue;
            if (!TrySuccessors(blocks, idx, offsetToIndex, out var succs))
                continue;   // opaque block; OnlyReachedByTable is the safety net
            foreach (int t in succs)
                Add(idx, t);
        }
        return preds;
    }

    /// <summary>Successor block indices (including the conditional / no-terminator fall-through), or false for an unsupported section shape.</summary>
    static bool TrySuccessors(IReadOnlyList<Block> blocks, int idx, Dictionary<int, int> offsetToIndex, out List<int> succs)
    {
        succs = [];
        var block = blocks[idx];
        for (int i = 0; i < block.Children.Count - 1; i++)
            if (block.Children[i] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return false;

        var last = block.Children.Count > 0 ? block.Children[^1] : null;
        switch (last)
        {
            case Return or Throw:
                return true;
            case Branch branch:
                if (!offsetToIndex.TryGetValue(branch.TargetOffset, out int bt))
                    return false;
                succs.Add(bt);
                return true;
            case ConditionalBranch conditional:
                if (!offsetToIndex.TryGetValue(conditional.TargetOffset, out int ct))
                    return false;
                succs.Add(ct);
                if (idx + 1 < blocks.Count)
                    succs.Add(idx + 1);
                return true;
            case SwitchBranch or Leave or EndFinally or EndFilter:
                return false;
            default:
                if (idx + 1 < blocks.Count)
                    succs.Add(idx + 1);
                return true;
        }
    }

    /// <summary>Grows the single-entry region rooted at <paramref name="head"/>: a block joins when all its predecessors are already inside. Exits are the targets that escape it.</summary>
    static bool GrowRegion(IReadOnlyList<Block> blocks, int head, int s, Dictionary<int, int> offsetToIndex,
        Dictionary<int, List<int>> preds, out List<int> region, out HashSet<int> exits)
    {
        region = [];
        exits = [];
        var members = new SortedSet<int> { head };

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (int b in members.ToList())
            {
                if (!TrySuccessors(blocks, b, offsetToIndex, out var succs))
                    return false;
                foreach (int t in succs)
                {
                    if (members.Contains(t))
                        continue;
                    if (t <= s)
                        return false;   // backward into / before the switch — a loop
                    bool interior = preds.TryGetValue(t, out var ps) && ps.All(members.Contains);
                    if (interior)
                    {
                        members.Add(t);
                        changed = true;
                    }
                }
            }
        }

        foreach (int b in members)
        {
            TrySuccessors(blocks, b, offsetToIndex, out var succs);
            foreach (int t in succs)
                if (!members.Contains(t))
                    exits.Add(t);
        }
        region = members.ToList();
        return true;
    }

    static bool SectionTerminates(IReadOnlyList<Block> blocks, List<int> region, Dictionary<int, int> offsetToIndex)
    {
        foreach (int b in region)
        {
            TrySuccessors(blocks, b, offsetToIndex, out var succs);
            if (succs.Any(t => !region.Contains(t)))
                return false;
        }
        return true;
    }

    /// <summary>A section may only escape to the join through an unconditional branch or fall-through, never a conditional/switch edge.</summary>
    static bool ExitsAreUnconditional(IReadOnlyList<Block> blocks, List<int> region, Dictionary<int, int> offsetToIndex)
    {
        foreach (int idx in region)
        {
            var block = blocks[idx];
            var last = block.Children.Count > 0 ? block.Children[^1] : null;
            switch (last)
            {
                case ConditionalBranch conditional:
                    if (!offsetToIndex.TryGetValue(conditional.TargetOffset, out int ct) || !region.Contains(ct))
                        return false;
                    if (idx + 1 >= blocks.Count || !region.Contains(idx + 1))
                        return false;
                    break;
                case SwitchBranch:
                    return false;
            }
        }
        return true;
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
        Dictionary<int, List<int>> regions, int? defaultBodyHead, int? defaultSharesTarget,
        int? join, int regionEnd, Stepper stepper)
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
            sections.Add(new SwitchSection([.. labels], isDefault: target == defaultSharesTarget,
                SectionBody(regions[target].Select(i => all[i]).ToList(), joinOffset)));
        if (defaultBodyHead is { } dh)
            sections.Add(new SwitchSection([], isDefault: true,
                SectionBody(regions[dh].Select(i => all[i]).ToList(), joinOffset)));

        var switchBlock = all[s];
        var value = (IrExpression)sw.DetachChildren()[0];
        sw.Detach();
        switchBlock.Add(new Switch(value, sections));

        var rebuilt = new BlockContainer();
        for (int idx = 0; idx <= s; idx++)
            rebuilt.Add(all[idx]);
        for (int idx = regionEnd; idx < all.Count; idx++)
            rebuilt.Add(all[idx]);
        stepper.StepOver("raise IL jump table to switch", container);
        container.ReplaceWith(rebuilt);
    }

    /// <summary>
    /// Wraps a detached section's blocks in a body container, ensuring it exits
    /// the switch: every branch to the join becomes <c>break;</c>, and a section
    /// whose last block falls into the join (no terminator — its join branch was
    /// elided, or it was empty) gets an explicit <c>break;</c> appended. Interior
    /// control flow is left for the structuring pass; return/throw stay.
    /// </summary>
    static BlockContainer SectionBody(List<Block> sectionBlocks, int? joinOffset)
    {
        foreach (var block in sectionBlocks)
        {
            var last = block.Children.Count > 0 ? block.Children[^1] : null;
            if (joinOffset is { } jo && last is Branch branch && branch.TargetOffset == jo)
            {
                branch.Detach();
                block.Add(new Break());
            }
        }

        var tail = sectionBlocks[^1];
        var tailLast = tail.Children.Count > 0 ? tail.Children[^1] : null;
        if (tailLast is not (Return or Throw or Break or Branch or ConditionalBranch or SwitchBranch))
            tail.Add(new Break());

        var body = new BlockContainer();
        foreach (var block in sectionBlocks)
            body.Add(block);
        return body;
    }
}
