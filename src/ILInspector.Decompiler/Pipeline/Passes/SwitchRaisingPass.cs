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
///
/// When that model leaves the switch flat, a second attempt
/// (<see cref="RaiseCaseTargetJoin"/>) handles tables whose default routes into
/// shared case bodies: one case target is the post-switch join, so cases reaching
/// it are empty <c>break;</c> sections and the default may break to it through a
/// conditional (<c>if (c) break;</c>) and fall through into a terminating case.
///
/// A third attempt (<see cref="RaiseSwitchExpressionReturn"/>) handles
/// value-producing tables — the source of a C# <c>switch</c> expression: every
/// case target (and the default) is a one-block value block assigning a single
/// local that the join then returns, so the whole table is one value
/// (<c>return v switch { … };</c>).
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
                if (blocks[s].Children is [.., SwitchBranch sw]
                    && (RaiseSwitchExpressionReturn(container, s, sw, stepper)
                        || Raise(container, s, sw, leaveTargets, stepper)
                        || RaiseCaseTargetJoin(container, s, sw, leaveTargets, stepper)))
                    return true;
                if (blocks[s].Children is [.., ConditionalBranch]
                    && RaiseStringEqualityChain(container, s, leaveTargets, stepper))
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

    /// <summary>
    /// A second raising attempt, tried only when <see cref="Raise"/> leaves the
    /// switch flat: for switches whose default <em>routes into</em> shared case
    /// bodies. One case target is the post-switch <em>join</em> — the
    /// continuation — so the cases reaching it are empty <c>break;</c> sections
    /// and the rest of the table (the other case targets and the default) tile the
    /// span before it. The default may break to the join through a conditional
    /// (<c>if (c) break;</c>) and may fall through into a single-block terminating
    /// case, whose terminator is duplicated into the default body (C# forbids
    /// falling from <c>default:</c> into a case). This is the
    /// TraceLoggingMetadataCollector::AddArray shape.
    /// </summary>
    static bool RaiseCaseTargetJoin(BlockContainer container, int s, SwitchBranch sw, HashSet<int> leaveTargets, Stepper stepper)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        int defaultIndex = s + 1;
        if (defaultIndex >= blocks.Count)
            return false;

        var caseTargets = new int[sw.TargetOffsets.Length];
        for (int i = 0; i < caseTargets.Length; i++)
            if (!offsetToIndex.TryGetValue(sw.TargetOffsets[i], out caseTargets[i]) || caseTargets[i] <= s)
                return false;

        // The default head being a case target is the SpinLock fold (handled by Raise).
        if (caseTargets.Contains(defaultIndex))
            return false;

        var preds = BuildPredecessors(blocks, s, caseTargets, offsetToIndex);
        foreach (int joinCandidate in caseTargets.Distinct())
        {
            if (TryCaseTargetJoin(container, blocks, s, sw, caseTargets, defaultIndex,
                    offsetToIndex, preds, leaveTargets, joinCandidate, stepper))
                return true;
        }
        return false;
    }

    static bool TryCaseTargetJoin(
        BlockContainer container, IReadOnlyList<Block> blocks, int s, SwitchBranch sw,
        int[] caseTargets, int defaultIndex, Dictionary<int, int> offsetToIndex,
        Dictionary<int, List<int>> preds, HashSet<int> leaveTargets, int join, Stepper stepper)
    {
        if (join <= s)
            return false;
        int joinOffset = blocks[join].StartOffset;

        var owned = new HashSet<int>();
        var regions = new Dictionary<int, List<int>>();
        var terminatingCases = new HashSet<int>();
        bool anyExitToJoin = false;

        // Every distinct case target other than the join grows into a region that
        // terminates or exits only (unconditionally) to the join.
        foreach (int target in caseTargets.Distinct())
        {
            if (target == join)
                continue;
            if (!GrowRegion(blocks, target, s, offsetToIndex, preds, out var region, out var exits))
                return false;
            if (owned.Overlaps(region))
                return false;
            foreach (int e in exits)
                if (e != join)
                    return false;
            if (exits.Count == 0)
                terminatingCases.Add(target);
            else if (!ExitsAreUnconditional(blocks, region, offsetToIndex))
                return false;
            else
                anyExitToJoin = true;
            regions[target] = region;
            owned.UnionWith(region);
        }

        // The default region: breaks to the join (conditionally or not) and may
        // fall through into a single-block terminating case (whose terminator is
        // cloned into the default body).
        if (!GrowRegion(blocks, defaultIndex, s, offsetToIndex, preds, out var defaultRegion, out var defaultExits))
            return false;
        if (owned.Overlaps(defaultRegion))
            return false;
        int? fallThroughCase = null;
        foreach (int e in defaultExits)
        {
            if (e == join)
            {
                anyExitToJoin = true;
                continue;
            }
            if (terminatingCases.Contains(e) && regions[e] is [int only] && only == e)
            {
                if (fallThroughCase is { } existing && existing != e)
                    return false;
                fallThroughCase = e;
                continue;
            }
            return false;
        }
        if (!DefaultExitsAreBreakable(blocks, defaultRegion, offsetToIndex, joinOffset))
            return false;
        regions[defaultIndex] = defaultRegion;
        owned.UnionWith(defaultRegion);

        // The join must be a genuine merge a section breaks to — never an arbitrary
        // terminating case (which would wrongly empty-case it).
        if (!anyExitToJoin)
            return false;

        // The owned blocks must tile [s+1, join) exactly, with the join just past them.
        int regionEnd = join;
        if (owned.Contains(join))
            return false;
        if (owned.Count != regionEnd - defaultIndex)
            return false;
        foreach (int idx in owned)
            if (idx < defaultIndex || idx >= regionEnd)
                return false;

        if (!OnlyReachedByTable(blocks, owned, s, leaveTargets))
            return false;

        BuildCaseTargetJoin(container, s, sw, caseTargets, regions, defaultIndex, join,
            fallThroughCase, regionEnd, stepper);
        return true;
    }

    /// <summary>
    /// A third raising attempt, tried only when both statement-raisers leave the
    /// switch flat: a value-producing jump table that is the source of a C# switch
    /// expression. Every case target (and the default) is a one-block <em>value
    /// block</em> — a single <c>StoreLocal L = expr</c> that then reaches a common
    /// join — and the join is exactly <c>return L</c>, reading L once. Such a table
    /// is one value: <c>return v switch { labels =&gt; expr, …, _ =&gt; expr };</c>.
    /// The default may itself be a value block or a single conditional choosing
    /// between two value blocks (lowered to a <c>?:</c> arm). This is the
    /// AssemblyNameParser::IsWhiteSpace shape.
    /// </summary>
    static bool RaiseSwitchExpressionReturn(BlockContainer container, int s, SwitchBranch sw, Stepper stepper)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        int defaultIndex = s + 1;
        if (defaultIndex >= blocks.Count)
            return false;

        var caseTargets = new int[sw.TargetOffsets.Length];
        for (int i = 0; i < caseTargets.Length; i++)
            if (!offsetToIndex.TryGetValue(sw.TargetOffsets[i], out caseTargets[i]) || caseTargets[i] <= s)
                return false;

        // The default head being a case target is the SpinLock fold (handled by Raise).
        if (caseTargets.Contains(defaultIndex))
            return false;

        // Every case target must be a value block; they must agree on one local
        // and one join. Probe the first to fix L and J, then require the rest match.
        int? local = null;
        int? joinIndex = null;
        bool Probe(int idx)
        {
            if (!TryValueBlock(blocks, idx, offsetToIndex, out int l, out int j))
                return false;
            if (local is { } el && el != l)
                return false;
            if (joinIndex is { } ej && ej != j)
                return false;
            local = l;
            joinIndex = j;
            return true;
        }

        foreach (int target in caseTargets.Distinct())
            if (!Probe(target))
                return false;

        // The default arm: a value block, a bare jump to a separate value block
        // laid out after the cases, or one conditional over two value blocks.
        var owned = new HashSet<int>(caseTargets) { defaultIndex };
        IrExpression defaultArm;
        if (TryValueBlock(blocks, defaultIndex, offsetToIndex, out _, out _))
        {
            if (!Probe(defaultIndex))
                return false;
            defaultArm = (IrExpression)ValueBlockExpr(blocks[defaultIndex]).Clone();
        }
        else if (blocks[defaultIndex].Children is [Branch defaultJump]
            && offsetToIndex.TryGetValue(defaultJump.TargetOffset, out int defaultValueIdx)
            && defaultValueIdx > s
            && TryValueBlock(blocks, defaultValueIdx, offsetToIndex, out _, out _)
            && Probe(defaultValueIdx))
        {
            owned.Add(defaultValueIdx);
            defaultArm = (IrExpression)ValueBlockExpr(blocks[defaultValueIdx]).Clone();
        }
        else if (blocks[defaultIndex].Children is [ConditionalBranch dispatch]
            && offsetToIndex.TryGetValue(dispatch.TargetOffset, out int whenTrueIdx)
            && whenTrueIdx > s)
        {
            int whenFalseIdx = defaultIndex + 1;
            if (whenFalseIdx >= blocks.Count || !Probe(whenTrueIdx) || !Probe(whenFalseIdx))
                return false;
            owned.Add(whenTrueIdx);
            owned.Add(whenFalseIdx);
            var condition = (IrExpression)dispatch.Children[0].Clone();
            defaultArm = new Conditional(
                condition,
                (IrExpression)ValueBlockExpr(blocks[whenTrueIdx]).Clone(),
                (IrExpression)ValueBlockExpr(blocks[whenFalseIdx]).Clone());
        }
        else
        {
            return false;
        }

        int join = joinIndex!.Value;
        int theLocal = local!.Value;

        // The join reads the local exactly once and returns it.
        if (blocks[join].Children is not [Return { Value: LoadLocal joinRead }] || joinRead.Index != theLocal)
            return false;

        // The local is private to this dispatch: assigned only in the owned value
        // blocks, read only at the join. Otherwise raising would drop a live store.
        var ownedBlocks = owned.Select(i => blocks[i]).ToHashSet();
        bool InOwned(IrNode node)
        {
            for (var current = node; current is not null; current = current.Parent)
                if (current is Block block && ownedBlocks.Contains(block))
                    return true;
            return false;
        }
        foreach (var node in container.Descendants)
        {
            if (node is StoreLocal store && store.Index == theLocal && !InOwned(store))
                return false;
            if (node is LoadLocal load && load.Index == theLocal && !ReferenceEquals(load, joinRead))
                return false;
        }

        // The owned blocks must tile [s+1, join) exactly, the join just past them,
        // and nothing outside the table may enter them.
        if (join <= s || owned.Contains(join))
            return false;
        if (owned.Count != join - defaultIndex)
            return false;
        foreach (int idx in owned)
            if (idx < defaultIndex || idx >= join)
                return false;
        if (!OnlyReachedByTable(blocks, owned, s, []))
            return false;

        BuildSwitchExpression(container, s, sw, caseTargets, defaultArm, join, stepper);
        return true;
    }

    /// <summary>
    /// A value block is one block that assigns a single local and then reaches a
    /// join — either by an unconditional branch to it, or by falling through to
    /// the next block. Returns the assigned local and the join's block index.
    /// </summary>
    static bool TryValueBlock(IReadOnlyList<Block> blocks, int idx, Dictionary<int, int> offsetToIndex, out int local, out int joinIndex)
    {
        local = -1;
        joinIndex = -1;
        var block = blocks[idx];
        switch (block.Children)
        {
            case [StoreLocal store]:
                local = store.Index;
                joinIndex = idx + 1;
                return joinIndex < blocks.Count;
            case [StoreLocal store, Branch branch]:
                if (!offsetToIndex.TryGetValue(branch.TargetOffset, out joinIndex))
                    return false;
                local = store.Index;
                return true;
            default:
                return false;
        }
    }

    static IrExpression ValueBlockExpr(Block block) => ((StoreLocal)block.Children[0]).Value;

    /// <summary>Replaces a value-producing jump table with <c>return v switch { … };</c>: the arms group case labels by their value block, and the default arm is supplied by the recognizer.</summary>
    static void BuildSwitchExpression(BlockContainer container, int s, SwitchBranch sw, int[] caseTargets, IrExpression defaultArm, int join, Stepper stepper)
    {
        var all = container.Blocks.ToList();

        var labelsByTarget = new Dictionary<int, List<int>>();
        for (int i = 0; i < caseTargets.Length; i++)
            (labelsByTarget.TryGetValue(caseTargets[i], out var list) ? list : labelsByTarget[caseTargets[i]] = []).Add(i);

        var arms = new List<SwitchExpressionArm>();
        foreach (var (target, labels) in labelsByTarget.OrderBy(kv => kv.Value.Min()))
            arms.Add(new SwitchExpressionArm([.. labels], isDefault: false, (IrExpression)ValueBlockExpr(all[target]).Clone()));
        arms.Add(new SwitchExpressionArm([], isDefault: true, defaultArm));

        var value = (IrExpression)sw.DetachChildren()[0];
        sw.Detach();

        foreach (var block in all)
            block.Detach();

        var switchBlock = all[s];
        switchBlock.Add(new Return(new SwitchExpression(value, arms)));

        var rebuilt = new BlockContainer();
        for (int idx = 0; idx <= s; idx++)
            rebuilt.Add(all[idx]);
        for (int idx = join + 1; idx < all.Count; idx++)
            rebuilt.Add(all[idx]);
        stepper.StepOver("raise value-producing jump table to switch expression", container);
        container.ReplaceWith(rebuilt);
    }

    /// <summary>The default region escapes only through a conditional / unconditional branch to the join, a fall-through, or a terminator — every conditional must target the join (it becomes <c>if (c) break;</c>).</summary>
    static bool DefaultExitsAreBreakable(IReadOnlyList<Block> blocks, List<int> region, Dictionary<int, int> offsetToIndex, int joinOffset)
    {
        foreach (int idx in region)
        {
            var last = blocks[idx].Children.Count > 0 ? blocks[idx].Children[^1] : null;
            switch (last)
            {
                case ConditionalBranch conditional:
                    if (conditional.TargetOffset != joinOffset)
                        return false;
                    break;
                case Branch branch:
                    if (branch.TargetOffset != joinOffset)
                        return false;
                    break;
                case SwitchBranch:
                    return false;
            }
        }
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

    /// <summary>Jump-table indices as <c>int</c> case-label constants.</summary>
    static ImmutableArray<Constant> IntLabels(IEnumerable<int> indices)
        => [.. indices.Select(i => new Constant(i, TypeRef.CoreLib("System", "Int32")))];

    /// <summary>String literals as <c>string</c> case-label constants.</summary>
    static ImmutableArray<Constant> StringLabels(IEnumerable<string> literals)
        => [.. literals.Select(l => new Constant(l, TypeRef.CoreLib("System", "String")))];

    /// <summary>
    /// Raises csc's small switch-on-string lowering — a run of
    /// <c>if (v == "lit") goto case;</c> equality tests (each a
    /// <c>string.op_Equality</c> call whose true branch jumps to a case body),
    /// ending in a branch to the default — back into a C# <c>switch</c>
    /// statement. Recompiling the flat goto chain inverts the second and later
    /// branch polarities (csc folds <c>if (c) goto next; goto other; next:</c>
    /// into <c>brfalse other</c>), so the gotos never round-trip opcode-exact;
    /// the <c>switch</c> form does. Larger string switches csc lowers through a
    /// computed hash + bucket tree are a different shape and stay flat.
    ///
    /// The case bodies must tile the contiguous span after the dispatch chain,
    /// each entered only through the chain and exiting through a terminator or an
    /// unconditional branch to one shared join — the same single-entry-region
    /// model the jump-table raise uses; anything else is left flat for soundness.
    /// </summary>
    static bool RaiseStringEqualityChain(BlockContainer container, int s, HashSet<int> leaveTargets, Stepper stepper)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        // The dispatch chain: a run of equality tests against the same value, each
        // branching to a case body when equal. The first test may share its block
        // with the straight-line setup that precedes the switch (e.g. spilling the
        // governing expression to a temp); the rest are single-statement blocks.
        var caseLabels = new List<string>();
        var caseTargetOffsets = new List<int>();

        if (blocks[s].Children is not [.., ConditionalBranch first]
            || !TryStringEqualityTest(first.Condition, out var firstValue, out var firstLiteral))
            return false;
        var value = firstValue;
        caseLabels.Add(firstLiteral);
        caseTargetOffsets.Add(first.TargetOffset);

        int idx = s + 1;
        while (idx < blocks.Count
            && blocks[idx].Children is [ConditionalBranch cb]
            && TryStringEqualityTest(cb.Condition, out var testValue, out var literal)
            && SameValue(value, testValue))
        {
            caseLabels.Add(literal);
            caseTargetOffsets.Add(cb.TargetOffset);
            idx++;
        }

        if (caseTargetOffsets.Count < 2 || idx >= blocks.Count)
            return false;   // need at least two string cases to be a switch

        // The block after the chain branches to the default (or is the default body).
        int defaultOffset;
        int dispatchEnd;
        if (blocks[idx].Children is [Branch br])
        {
            defaultOffset = br.TargetOffset;
            dispatchEnd = idx;
        }
        else
        {
            defaultOffset = blocks[idx].StartOffset;
            dispatchEnd = idx - 1;
        }

        if (!offsetToIndex.TryGetValue(defaultOffset, out int defaultIndex) || defaultIndex <= dispatchEnd)
            return false;

        var caseTargets = new int[caseTargetOffsets.Count];
        for (int k = 0; k < caseTargets.Length; k++)
            if (!offsetToIndex.TryGetValue(caseTargetOffsets[k], out caseTargets[k]) || caseTargets[k] <= dispatchEnd)
                return false;

        var preds = ChainPredecessors(blocks, offsetToIndex);

        var owned = new HashSet<int>();
        int? join = null;
        bool Unify(int j) => (join ??= j) == j;
        var regions = new Dictionary<int, List<int>>();

        foreach (int target in caseTargets.Distinct())
        {
            if (!GrowRegion(blocks, target, dispatchEnd, offsetToIndex, preds, out var region, out var exits))
                return false;
            if (owned.Overlaps(region))
                return false;
            foreach (int e in exits)
                if (!Unify(e))
                    return false;
            regions[target] = region;
            owned.UnionWith(region);
        }

        int? defaultBodyHead = null;
        int? defaultSharesTarget = null;
        if (caseTargets.Contains(defaultIndex))
        {
            // The default jumps into a shared case body: fold `default:` onto it.
            defaultSharesTarget = defaultIndex;
        }
        else if (join is { } jn && defaultIndex == jn)
        {
            // An empty default — falls straight to the join; C# omits the label.
        }
        else
        {
            if (!GrowRegion(blocks, defaultIndex, dispatchEnd, offsetToIndex, preds, out var region, out var exits))
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

        // The body blocks must tile the contiguous span [dispatchEnd+1, regionEnd).
        int regionEnd = join ?? (owned.Count == 0 ? dispatchEnd + 1 : owned.Max() + 1);
        if (join is { } j2 && (j2 <= dispatchEnd || owned.Contains(j2)))
            return false;
        int firstBody = dispatchEnd + 1;
        if (owned.Count != regionEnd - firstBody)
            return false;
        foreach (int o in owned)
            if (o < firstBody || o >= regionEnd)
                return false;

        foreach (var region in regions.Values)
            if (!ExitsAreUnconditional(blocks, region, offsetToIndex))
                return false;

        if (!OnlyReachedByChain(blocks, owned, s, dispatchEnd, leaveTargets))
            return false;

        BuildStringSwitch(container, s, dispatchEnd, value, caseTargets, caseLabels,
            regions, defaultBodyHead, defaultSharesTarget, join, regionEnd, stepper);
        return true;
    }

    /// <summary>A <c>string.op_Equality(value, "literal")</c> test, in either argument order.</summary>
    static bool TryStringEqualityTest(IrExpression condition, out IrExpression value, out string literal)
    {
        value = null!;
        literal = null!;
        if (condition is Call { Callee: { Name: "op_Equality", DeclaringType: { Namespace: "System", Name: "String" } }, Arguments: var args }
            && args.Count == 2)
        {
            if (args[1] is Constant { Value: string right })
            {
                value = args[0];
                literal = right;
                return true;
            }
            if (args[0] is Constant { Value: string left })
            {
                value = args[1];
                literal = left;
                return true;
            }
        }
        return false;
    }

    /// <summary>Structural equality for the switch governing value — the simple loads csc emits (a parameter or a temp local).</summary>
    static bool SameValue(IrExpression a, IrExpression b) => (a, b) switch
    {
        (LoadArgument x, LoadArgument y) => x.Index == y.Index,
        (LoadLocal x, LoadLocal y) => x.Index == y.Index,
        _ => false,
    };

    /// <summary>Predecessor edges across every block, read from each block's successors.</summary>
    static Dictionary<int, List<int>> ChainPredecessors(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex)
    {
        var preds = new Dictionary<int, List<int>>();
        for (int idx = 0; idx < blocks.Count; idx++)
            if (TrySuccessors(blocks, idx, offsetToIndex, out var succs))
                foreach (int t in succs)
                    (preds.TryGetValue(t, out var list) ? list : preds[t] = []).Add(idx);
        return preds;
    }

    /// <summary>No block outside the dispatch chain (or the owned bodies) may branch into a body.</summary>
    static bool OnlyReachedByChain(IReadOnlyList<Block> blocks, HashSet<int> owned, int s, int dispatchEnd, HashSet<int> leaveTargets)
    {
        var ownedOffsets = owned.Select(i => blocks[i].StartOffset).ToHashSet();
        if (ownedOffsets.Overlaps(leaveTargets))
            return false;
        for (int idx = 0; idx < blocks.Count; idx++)
        {
            if ((idx >= s && idx <= dispatchEnd) || owned.Contains(idx))
                continue;   // the dispatch chain legitimately jumps into the bodies
            foreach (var node in blocks[idx].Children)
                foreach (int target in Targets(node))
                    if (ownedOffsets.Contains(target))
                        return false;
        }
        return true;
    }

    static void BuildStringSwitch(
        BlockContainer container, int s, int dispatchEnd, IrExpression value, int[] caseTargets,
        List<string> caseLabels, Dictionary<int, List<int>> regions, int? defaultBodyHead,
        int? defaultSharesTarget, int? join, int regionEnd, Stepper stepper)
    {
        var all = container.Blocks.ToList();
        int? joinOffset = join is { } j ? all[j].StartOffset : null;

        // Case labels grouped by target, in first-appearance order.
        var labelsByTarget = new Dictionary<int, List<string>>();
        var targetOrder = new List<int>();
        for (int i = 0; i < caseTargets.Length; i++)
        {
            if (!labelsByTarget.TryGetValue(caseTargets[i], out var list))
            {
                labelsByTarget[caseTargets[i]] = list = [];
                targetOrder.Add(caseTargets[i]);
            }
            list.Add(caseLabels[i]);
        }

        var switchValue = (IrExpression)value.Clone();

        foreach (var block in all)
            block.Detach();

        var sections = new List<SwitchSection>();
        foreach (int target in targetOrder)
            sections.Add(new SwitchSection(StringLabels(labelsByTarget[target]), isDefault: target == defaultSharesTarget,
                SectionBody(regions[target].Select(i => all[i]).ToList(), joinOffset)));
        if (defaultBodyHead is { } dh)
            sections.Add(new SwitchSection([], isDefault: true,
                SectionBody(regions[dh].Select(i => all[i]).ToList(), joinOffset)));

        // Keep the straight-line setup that precedes the switch; replace only the
        // trailing dispatch test with the raised statement.
        var switchBlock = all[s];
        switchBlock.Children[^1].Detach();
        switchBlock.Add(new Switch(switchValue, sections));

        var rebuilt = new BlockContainer();
        for (int i = 0; i < s; i++)
            rebuilt.Add(all[i]);
        rebuilt.Add(switchBlock);
        for (int i = regionEnd; i < all.Count; i++)
            rebuilt.Add(all[i]);
        stepper.StepOver("raise switch-on-string equality chain to switch", container);
        container.ReplaceWith(rebuilt);
    }

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
            sections.Add(new SwitchSection(IntLabels(labels), isDefault: target == defaultSharesTarget,
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

    static void BuildCaseTargetJoin(
        BlockContainer container, int s, SwitchBranch sw, int[] caseTargets,
        Dictionary<int, List<int>> regions, int defaultIndex, int join,
        int? fallThroughCase, int regionEnd, Stepper stepper)
    {
        var all = container.Blocks.ToList();
        int joinOffset = all[join].StartOffset;

        var labelsByTarget = new Dictionary<int, List<int>>();
        for (int i = 0; i < caseTargets.Length; i++)
            (labelsByTarget.TryGetValue(caseTargets[i], out var list) ? list : labelsByTarget[caseTargets[i]] = []).Add(i);

        // Clone the shared terminator before detaching: the default falls into a
        // single-block terminating case, which C# cannot do, so its body is
        // duplicated into the default section.
        var duplicatedTerminator = fallThroughCase is { } fc
            ? all[fc].Children.Select(child => child.Clone()).ToList()
            : null;

        foreach (var block in all)
            block.Detach();

        var sections = new List<SwitchSection>();
        foreach (var (target, labels) in labelsByTarget.OrderBy(kv => kv.Value.Min()))
        {
            if (target == join)
                sections.Add(new SwitchSection(IntLabels(labels), isDefault: false, EmptyBreakBody()));
            else
                sections.Add(new SwitchSection(IntLabels(labels), isDefault: false,
                    SectionBody(regions[target].Select(i => all[i]).ToList(), joinOffset)));
        }
        sections.Add(new SwitchSection([], isDefault: true,
            DefaultSectionBody(regions[defaultIndex].Select(i => all[i]).ToList(), joinOffset, duplicatedTerminator)));

        var switchBlock = all[s];
        var value = (IrExpression)sw.DetachChildren()[0];
        sw.Detach();
        switchBlock.Add(new Switch(value, sections));

        var rebuilt = new BlockContainer();
        for (int idx = 0; idx <= s; idx++)
            rebuilt.Add(all[idx]);
        for (int idx = regionEnd; idx < all.Count; idx++)
            rebuilt.Add(all[idx]);
        stepper.StepOver("raise IL jump table to switch (default routes into cases)", container);
        container.ReplaceWith(rebuilt);
    }

    /// <summary>A case whose target is the join carries no body — just <c>break;</c>.</summary>
    static BlockContainer EmptyBreakBody()
    {
        var block = new Block();
        block.Add(new Break());
        var body = new BlockContainer();
        body.Add(block);
        return body;
    }

    /// <summary>
    /// Wraps the default region, turning each branch to the join into a
    /// <c>break;</c> — a conditional branch becomes <c>if (c) break;</c> — and
    /// appending the duplicated terminator the default falls through into.
    /// </summary>
    static BlockContainer DefaultSectionBody(List<Block> sectionBlocks, int joinOffset, List<IrNode>? duplicatedTerminator)
    {
        foreach (var block in sectionBlocks)
        {
            var last = block.Children.Count > 0 ? block.Children[^1] : null;
            if (last is ConditionalBranch conditional && conditional.TargetOffset == joinOffset)
            {
                var condition = (IrExpression)conditional.DetachChildren()[0];
                conditional.Detach();
                var thenArm = new Block();
                thenArm.Add(new Break());
                block.Add(new IfStatement(condition, thenArm, elseArm: null));
            }
            else if (last is Branch branch && branch.TargetOffset == joinOffset)
            {
                branch.Detach();
                block.Add(new Break());
            }
        }

        var tail = sectionBlocks[^1];
        if (duplicatedTerminator is not null)
            foreach (var node in duplicatedTerminator)
                tail.Add(node);

        var tailLast = tail.Children.Count > 0 ? tail.Children[^1] : null;
        if (tailLast is not (Return or Throw or Break or Branch or ConditionalBranch or SwitchBranch))
            tail.Add(new Break());

        var body = new BlockContainer();
        foreach (var block in sectionBlocks)
            body.Add(block);
        return body;
    }
}
