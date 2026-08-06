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
                    && (RaiseSwitchExpressionReturn(container, s, sw, leaveTargets, stepper)
                        || Raise(container, s, sw, leaveTargets, stepper)
                        || RaiseCaseTargetJoin(container, s, sw, leaveTargets, stepper)))
                    return true;
                if (blocks[s].Children is [.., ConditionalBranch]
                    && (RaiseComparisonChainSwitchExpression(container, s, leaveTargets, stepper)
                        || RaiseStringLengthBucketSwitch(container, s, leaveTargets, stepper)
                        || RaiseStringHashSwitch(container, s, leaveTargets, stepper)
                        || RaiseStringEqualityChain(container, s, leaveTargets, stepper)
                        || RaiseSparseIntSwitch(container, s, leaveTargets, stepper)))
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

        // Each distinct case target grows into its single-entry region; the
        // region's exits unify to the shared join (or it terminates).
        var regions = new Dictionary<int, List<int>>();
        foreach (int target in caseTargets.Distinct())
            if (!TryAddOwnedRegion(blocks, target, s, caseTargets, offsetToIndex,
                    preds, regions, owned, ref join))
                return false;

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
                if (!TryAddOwnedRegion(blocks, dt, s, caseTargets, offsetToIndex,
                        preds, regions, owned, ref join))
                    return false;
                owned.Add(defaultIndex);
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
            if (!TryAddOwnedRegion(blocks, defaultIndex, s, caseTargets, offsetToIndex,
                    preds, regions, owned, ref join))
                return false;
            defaultBodyHead = defaultIndex;
        }

        // The owned blocks must tile the contiguous span [s+1, regionEnd): the
        // join (if any) lies just past them, and no foreign block is interleaved.
        int regionEnd = join ?? owned.Max() + 1;
        if (!OwnsTiledRegion(owned, defaultIndex, join, regionEnd))
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
    /// (<c>if (c) break;</c>), may fall through into a single-block terminating
    /// case whose terminator is duplicated into the default body (C# forbids
    /// falling from <c>default:</c> into a case), or may itself be one of the
    /// terminating case targets. The join is proven either by an owned section
    /// exiting to it or by a predecessor before the switch reaching it as a shared
    /// continuation. These are the TraceLoggingMetadataCollector::AddArray and
    /// NLoptSolver::CheckInequalityConstraintAvailability shapes.
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
        bool defaultSharesTarget = caseTargets.Contains(defaultIndex);

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

        int? fallThroughCase = null;
        if (!defaultSharesTarget)
        {
            // A separate default region breaks to the join (conditionally or not)
            // and may fall through into a single-block terminating case, whose
            // terminator is cloned into the default body.
            if (!GrowRegion(blocks, defaultIndex, s, offsetToIndex, preds, out var defaultRegion, out var defaultExits))
                return false;
            if (owned.Overlaps(defaultRegion))
                return false;
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
        }
        if (regions.Values.Any(region => ContainsBreakTargetingOutsideRegion(blocks, region)))
            return false;

        // The join must be a genuine merge a section breaks to — never an arbitrary
        // terminating case (which would wrongly empty-case it). A predecessor
        // before the switch is independent proof that the target is the shared
        // continuation rather than a table-owned case body.
        bool reachedFromBeforeSwitch = preds.TryGetValue(join, out var joinPredecessors)
            && joinPredecessors.Any(predecessor => predecessor < s);
        if (!anyExitToJoin && !reachedFromBeforeSwitch)
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

        BuildCaseTargetJoin(container, s, sw, caseTargets, regions, defaultIndex,
            defaultSharesTarget, join, fallThroughCase, regionEnd, stepper);
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
    static bool RaiseSwitchExpressionReturn(BlockContainer container, int s, SwitchBranch sw, HashSet<int> leaveTargets, Stepper stepper)
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
        if (!OnlyReachedByTable(blocks, owned, s, leaveTargets))
            return false;
        if (!JoinOnlyReachedByValueBlocks(blocks, owned, join, leaveTargets))
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

    /// <summary>
    /// Raises csc's <em>comparison-chain</em> value switch-expression lowering
    /// into <c>local = value switch { … };</c>. When the case labels are too few
    /// or too sparse for a jump table, csc dispatches a <c>switch</c> expression
    /// with a linear equality chain (<c>if (v == k) goto arm;</c>, the <c>v == 0</c>
    /// arm emitted as the <c>brfalse</c>/<c>!v</c> form) ending in a branch to the
    /// default arm. Every arm — including the default — assigns one dedicated
    /// result temp and converges to a single block that reads that temp exactly
    /// once (a <c>return v</c> tail, or a <c>w = v</c> copy when the switch feeds
    /// an enclosing expression). That single-read result temp is the faithful
    /// switch-expression signal: a hand-written <c>if/else</c> cascade returns or
    /// assigns each arm directly and has no such convergence temp, so it is
    /// declined. Relational pivots are declined (they leave the collected span
    /// untiled). Runs before structuring, mirroring the jump-table raiser above.
    /// </summary>
    static bool RaiseComparisonChainSwitchExpression(BlockContainer container, int s, HashSet<int> leaveTargets, Stepper stepper)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        // Collect the linear equality dispatch. The first block may carry leading
        // setup (the value computation); the rest are single-statement tests.
        IrExpression? value = null;
        var caseLabels = new List<int>();
        var caseTargetOffsets = new List<int>();
        int? defaultOffset = null;
        bool sawEquality = false;
        int idx = s;
        int dispatchEnd = s - 1;
        while (idx < blocks.Count)
        {
            var children = blocks[idx].Children;
            IrNode? term = idx == s
                ? (children.Count > 0 ? children[^1] : null)
                : (children is [var only] ? only : null);

            if (term is ConditionalBranch cb && TryEqualityLabel(cb.Condition, out var testValue, out int label))
            {
                if (value is null)
                    value = testValue;
                else if (!PlaceIdentity.SameVariable(value, testValue))
                    break;
                if (cb.Condition is Comparison)
                    sawEquality = true;   // a real `v == k` (not just the `!v` == 0 form)
                if (caseLabels.Contains(label))
                    return false;         // duplicate case label (CS0152)
                caseLabels.Add(label);
                caseTargetOffsets.Add(cb.TargetOffset);
                dispatchEnd = idx;
                idx++;
                continue;
            }

            if (term is Branch br)
            {
                defaultOffset = br.TargetOffset;
                dispatchEnd = idx;
                break;
            }

            break;   // the first arm/body block
        }

        if (value is not (LoadLocal or LoadArgument) || !sawEquality || caseLabels.Count < 2)
            return false;

        // A case label recorded from IL is a signed int32. When the governing
        // value is an unsigned uint, a label with the high bit set (e.g.
        // uint.MaxValue, IL ldc.i4.m1) is stored as negative and would misprint
        // as an invalid signed switch label (CS0031) — the SwitchExpressionArm
        // printer path does not reinterpret unsigned labels. Decline only that
        // case and leave it to the other raisers. Small uint labels and all
        // int/byte/short/ushort/enum labels are non-negative here and print
        // faithfully (enum labels render as member names via the governing type).
        TypeRef governingType = value is LoadLocal ll ? ll.Type : ((LoadArgument)value).Type;
        if (governingType.Equals(TypeRef.CoreLib("System", "UInt32")) && caseLabels.Any(label => label < 0))
            return false;

        // Every SwitchExpressionArm label recorded here is an int32 that prints as
        // an integer literal, but a Boolean governing value needs bool constant
        // patterns — int does not convert to bool (CS0029), so `b switch { 0 => …,
        // 1 => … }` would not compile. csc never emits this shape on a bool (a
        // bool switch lowers to a single brtrue/brfalse, not an equality chain
        // with `== k` tests), so this only fires on arbitrary or obfuscated
        // non-csc IL; decline it and leave the if/else intact.
        if (governingType.Equals(TypeRef.CoreLib("System", "Boolean")))
            return false;

        // No explicit default branch: the last test falls through to the default arm.
        if (defaultOffset is null)
        {
            if (dispatchEnd + 1 >= blocks.Count)
                return false;
            defaultOffset = blocks[dispatchEnd + 1].StartOffset;
        }

        var caseTargets = new int[caseTargetOffsets.Count];
        for (int k = 0; k < caseTargets.Length; k++)
            if (!offsetToIndex.TryGetValue(caseTargetOffsets[k], out caseTargets[k]) || caseTargets[k] <= dispatchEnd)
                return false;
        if (!offsetToIndex.TryGetValue(defaultOffset.Value, out int defaultIndex) || defaultIndex <= dispatchEnd)
            return false;

        // Every arm and the default is a value block assigning one shared local and
        // converging to one join.
        int? theLocal = null;
        int? joinIndex = null;
        bool Probe(int i)
        {
            if (!TryValueBlock(blocks, i, offsetToIndex, out int l, out int j))
                return false;
            if (theLocal is { } el && el != l)
                return false;
            if (joinIndex is { } ej && ej != j)
                return false;
            theLocal = l;
            joinIndex = j;
            return true;
        }

        var owned = new HashSet<int>();
        foreach (int target in caseTargets.Distinct())
        {
            if (!Probe(target))
                return false;
            owned.Add(target);
        }
        if (!Probe(defaultIndex))
            return false;
        owned.Add(defaultIndex);
        var defaultArm = (IrExpression)ValueBlockExpr(blocks[defaultIndex]).Clone();

        int join = joinIndex!.Value;
        int local = theLocal!.Value;

        // The result temp is private to this switch: assigned only in the owned
        // value blocks and read exactly once, in the join. This single-read
        // convergence temp is what a hand-written if/else cascade lacks.
        LoadLocal? joinRead = null;
        foreach (var node in container.Descendants)
        {
            if (node is StoreLocal store && store.Index == local && !owned.Contains(BlockIndexOf(blocks, store)))
                return false;
            if (node is LoadLocal load && load.Index == local)
            {
                if (joinRead is not null)
                    return false;   // more than one read — not a convergence temp
                joinRead = load;
            }
        }
        if (joinRead is null || BlockIndexOf(blocks, joinRead) != join)
            return false;

        // The owned value blocks must tile [dispatchEnd+1, join) exactly, with the
        // join just past them and nothing outside the chain entering the bodies.
        if (join <= dispatchEnd || owned.Contains(join))
            return false;
        if (owned.Count != join - (dispatchEnd + 1))
            return false;
        foreach (int o in owned)
            if (o <= dispatchEnd || o >= join)
                return false;
        if (!OnlyReachedByChain(blocks, owned, s, dispatchEnd, leaveTargets))
            return false;
        if (!JoinOnlyReachedByValueBlocks(blocks, owned, join, leaveTargets))
            return false;

        // Build `value switch { k => armk, …, _ => default }`, grouping labels by
        // their shared value block in first-appearance order.
        var labelsByTarget = new Dictionary<int, List<int>>();
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

        var arms = new List<SwitchExpressionArm>();
        foreach (int target in targetOrder)
            arms.Add(new SwitchExpressionArm(
                [.. labelsByTarget[target]], isDefault: false, (IrExpression)ValueBlockExpr(blocks[target]).Clone()));
        arms.Add(new SwitchExpressionArm([], isDefault: true, defaultArm));

        var switchExpression = new SwitchExpression((IrExpression)value.Clone(), arms);

        // The result temp's declared type is taken from its own arm stores.
        var localType = ((StoreLocal)blocks[defaultIndex].Children[0]).Type;

        // Rewrite the dispatch head: keep its leading setup, drop the trailing
        // dispatch branch, and assign the result temp the switch expression. Drop
        // the dispatch + arm blocks; keep the join (its single read of the temp
        // stays, and later inlining folds the temp into it).
        var all = container.Blocks.ToList();
        var head = all[s];
        head.Children[^1].Detach();   // the trailing dispatch ConditionalBranch/Branch
        head.Add(new StoreLocal(local, localType, switchExpression));

        foreach (var block in all)
            block.Detach();

        var rebuilt = new BlockContainer();
        for (int i = 0; i <= s; i++)
            rebuilt.Add(all[i]);
        for (int i = join; i < all.Count; i++)
            rebuilt.Add(all[i]);
        stepper.StepOver("raise value-producing comparison chain to switch expression", container);
        container.ReplaceWith(rebuilt);
        return true;
    }

    /// <summary>The block index owning a node, or -1 if it is not inside a block of <paramref name="blocks"/>.</summary>
    static int BlockIndexOf(IReadOnlyList<Block> blocks, IrNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
            if (current is Block block)
            {
                for (int i = 0; i < blocks.Count; i++)
                    if (ReferenceEquals(blocks[i], block))
                        return i;
                return -1;
            }
        return -1;
    }

    /// <summary>
    /// An int equality dispatch test yielding the governing value and its case
    /// label: <c>v == k</c> / <c>k == v</c>, or the <c>brfalse</c> form <c>!v</c>
    /// for the <c>0</c> arm.
    /// </summary>
    static bool TryEqualityLabel(IrExpression condition, out IrExpression value, out int label)
    {
        value = null!;
        label = 0;
        if (condition is LogicalNot { Operand: { } operand })
        {
            value = operand;
            label = 0;
            return true;
        }
        if (condition is Comparison { Kind: ComparisonKind.Equal } cmp)
        {
            if (cmp.Right is Constant { Value: int right })
            {
                value = cmp.Left;
                label = right;
                return true;
            }
            if (cmp.Left is Constant { Value: int left })
            {
                value = cmp.Right;
                label = left;
                return true;
            }
        }
        return false;
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

    static bool TryAddOwnedRegion(
        IReadOnlyList<Block> blocks,
        int head,
        int dispatchBoundary,
        int[] caseTargets,
        Dictionary<int, int> offsetToIndex,
        Dictionary<int, List<int>> predecessors,
        Dictionary<int, List<int>> regions,
        HashSet<int> owned,
        ref int? join)
    {
        if (!GrowRegion(blocks, head, dispatchBoundary, offsetToIndex, predecessors, out var region, out var exits))
            return false;
        if (owned.Overlaps(region))
            return false;
        int? originalJoin = join;
        foreach (int exit in exits)
            if (!TryUnify(blocks, caseTargets, offsetToIndex, owned, ref join, exit))
            {
                join = originalJoin;
                return false;
            }
        regions[head] = region;
        owned.UnionWith(region);
        return true;
    }

    static bool OwnsTiledRegion(HashSet<int> owned, int firstBody, int? join, int regionEnd)
    {
        if (join is { } j && (j < firstBody || owned.Contains(j)))
            return false;
        if (owned.Count != regionEnd - firstBody)
            return false;
        foreach (int index in owned)
            if (index < firstBody || index >= regionEnd)
                return false;
        return true;
    }

    /// <summary>
    /// Decides whether exit <paramref name="j"/> unifies with the current
    /// candidate join in <paramref name="join"/>: two exits unify not only when
    /// identical, but when one flows into the other through a chain of plain,
    /// unclaimed, single-successor blocks (see <see cref="ChasesTo"/>) — e.g. a
    /// case-local "no match" arm and a shared miss-handler that falls into the
    /// same terminating return. The upstream (earlier) block becomes the join,
    /// so the chain is naturally emitted once as trailing code after the switch;
    /// the caller's own tiling check afterward still rejects the join if some
    /// other owned block ends up past it.
    ///
    /// Shared by <see cref="Raise"/> and <see cref="FinishSwitchRaise"/> — the
    /// two independent case-region-growing raisers — so a fix to this decision
    /// applies to both instead of risking the two drifting out of sync (see
    /// issue #2971).
    /// </summary>
    static bool TryUnify(IReadOnlyList<Block> blocks, int[] caseTargets, Dictionary<int, int> offsetToIndex, HashSet<int> owned, ref int? join, int j)
    {
        if (join is not { } existing)
        {
            join = j;
            return true;
        }
        if (existing == j)
            return true;
        if (ChasesTo(blocks, existing, j, caseTargets, offsetToIndex, owned))
            return true;   // existing join already flows into j — keep it
        if (ChasesTo(blocks, j, existing, caseTargets, offsetToIndex, owned))
        {
            join = j;   // j is upstream of the existing join — adopt it
            return true;
        }
        return false;
    }

    /// <summary>
    /// True if, walking forward from <paramref name="from"/> through blocks that
    /// are plain single-successor pass-through (not a case's own entry point, and
    /// not already claimed by another section), we reach <paramref name="to"/>.
    /// Used to recognize a case-local "miss" arm chaining into a shared
    /// miss-handler that itself falls into the true join, so the two exits
    /// still unify (the earlier one becomes the join and the chain prints once,
    /// as ordinary code after the switch).
    /// </summary>
    static bool ChasesTo(IReadOnlyList<Block> blocks, int from, int to, int[] caseTargets, Dictionary<int, int> offsetToIndex, HashSet<int> owned)
    {
        var visited = new HashSet<int>();
        int cur = from;
        while (cur != to)
        {
            if (!visited.Add(cur) || caseTargets.Contains(cur) || owned.Contains(cur))
                return false;
            if (!TrySuccessors(blocks, cur, offsetToIndex, out var succs) || succs.Count != 1)
                return false;
            cur = succs[0];
        }
        return true;
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
        // Same-SCC membership answers "is t reachable from its own predecessor p"
        // for the back-edge test below. Reachability is a static property of the
        // CFG, so the strongly-connected components of the post-switch subgraph
        // are computed once here and reused for every predecessor test, instead
        // of re-running a per-predecessor forward DFS on each growth iteration.
        int[] sccId = ComputeSectionSccIds(blocks, s, offsetToIndex);

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
                    // A block joins the single-entry region when every predecessor
                    // is already inside — or is a back-edge source, i.e. reachable
                    // from t itself. A section-internal loop's header has such a
                    // downstream predecessor (the back-edge); ignoring it lets the
                    // header — and then the loop body, whose predecessors become
                    // members — interiorize, so the switch owns a section that
                    // still holds an unraised loop (StructuringPass raises it once
                    // the section is a container). This relaxation admits some
                    // foreign entries (OnlyReachedByTable does not screen
                    // fall-through edges), but any such region is rejected later by
                    // the OwnsTiledRegion contiguous-span check, which requires the
                    // owned blocks to exactly tile the case span.
                    //
                    // p is already a predecessor of t (edge p -> t exists), so t
                    // reaches p — the back-edge condition — exactly when t and p
                    // lie in the same strongly-connected component. A predecessor
                    // at or before the switch (p <= s) is outside the subgraph and
                    // has scc id -1, so it never matches the case-target t (t > s).
                    bool interior = preds.TryGetValue(t, out var ps)
                        && ps.All(p => members.Contains(p) || sccId[t] == sccId[p]);
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

    /// <summary>
    /// Labels each block with the strongly-connected component it belongs to
    /// within the post-switch subgraph — the blocks with index &gt; <paramref
    /// name="s"/> (the switch block), with edges taken from <see
    /// cref="TrySuccessors"/> and restricted to that same index range. Blocks at
    /// or before the switch, and any block whose successors cannot be analyzed,
    /// are outside the subgraph and receive id -1. Two blocks share a component
    /// exactly when each is forward-reachable from the other over section-internal
    /// edges, which <see cref="GrowRegion"/> uses to recognize a back-edge
    /// predecessor (a loop body block only reachable through its own header) so
    /// the header interiorizes into the single-entry region instead of becoming a
    /// false exit. The traversal is iterative (an explicit work stack) so deep
    /// control-flow graphs from untrusted assemblies cannot overflow the runtime
    /// stack, and runs in O(V + E).
    /// </summary>
    static int[] ComputeSectionSccIds(IReadOnlyList<Block> blocks, int s, Dictionary<int, int> offsetToIndex)
    {
        int n = blocks.Count;
        var sccId = new int[n];
        var index = new int[n];
        var low = new int[n];
        var onStack = new bool[n];
        for (int i = 0; i < n; i++)
        {
            sccId[i] = -1;
            index[i] = -1;
        }

        List<int> SectionSuccs(int u)
        {
            var result = new List<int>();
            if (!TrySuccessors(blocks, u, offsetToIndex, out var succs))
                return result;
            foreach (int t in succs)
                if (t > s)
                    result.Add(t);
            return result;
        }

        var component = new Stack<int>();
        var work = new Stack<(int Node, int Next, List<int> Succs)>();
        int nextIndex = 0;
        int nextScc = 0;

        for (int root = s + 1; root < n; root++)
        {
            if (index[root] != -1)
                continue;

            index[root] = low[root] = nextIndex++;
            component.Push(root);
            onStack[root] = true;
            work.Push((root, 0, SectionSuccs(root)));

            while (work.Count > 0)
            {
                var (v, next, succs) = work.Pop();
                if (next < succs.Count)
                {
                    work.Push((v, next + 1, succs));
                    int w = succs[next];
                    if (index[w] == -1)
                    {
                        index[w] = low[w] = nextIndex++;
                        component.Push(w);
                        onStack[w] = true;
                        work.Push((w, 0, SectionSuccs(w)));
                    }
                    else if (onStack[w] && index[w] < low[v])
                    {
                        low[v] = index[w];
                    }
                }
                else
                {
                    if (low[v] == index[v])
                    {
                        while (true)
                        {
                            int u = component.Pop();
                            onStack[u] = false;
                            sccId[u] = nextScc;
                            if (u == v)
                                break;
                        }
                        nextScc++;
                    }
                    if (work.Count > 0)
                    {
                        var parent = work.Peek();
                        if (low[v] < low[parent.Node])
                            low[parent.Node] = low[v];
                    }
                }
            }
        }

        return sccId;
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

    /// <summary>A break with no region-local loop/switch owner would bind to the new switch after reparenting.</summary>
    static bool ContainsBreakTargetingOutsideRegion(IReadOnlyList<Block> blocks, List<int> region)
    {
        foreach (int idx in region)
        {
            var root = blocks[idx];
            foreach (var @break in root.Descendants.OfType<Break>())
            {
                for (var ancestor = @break.Parent; ancestor is not null; ancestor = ancestor.Parent)
                {
                    if (ReferenceEquals(ancestor, root))
                        return true;
                    if (ancestor is WhileLoop or DoWhileLoop or ForLoop or ForeachStatement or Switch)
                        break;
                }
            }
        }
        return false;
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

    static bool JoinOnlyReachedByValueBlocks(IReadOnlyList<Block> blocks, HashSet<int> owned, int join, HashSet<int> leaveTargets)
    {
        int joinOffset = blocks[join].StartOffset;
        if (leaveTargets.Contains(joinOffset))
            return false;
        for (int idx = 0; idx < blocks.Count; idx++)
        {
            if (owned.Contains(idx))
                continue;
            foreach (var node in blocks[idx].Children)
                foreach (int target in Targets(node))
                    if (target == joinOffset)
                        return false;
        }
        return true;
    }

    static IEnumerable<int> Targets(IrNode node) => node switch
    {
        Branch branch => [branch.TargetOffset],
        ConditionalBranch conditional => [conditional.TargetOffset],
        SwitchBranch sw => sw.TargetOffsets,
        Leave leave => [leave.TargetOffset],
        _ => [],
    };

    /// <summary>Jump-table indices as <c>int</c> case-label constants.</summary>
    static ImmutableArray<Constant> IntLabels(IEnumerable<int> indices)
        => [.. indices.Select(IntConst)];

    /// <summary>A single <c>int</c> case-label constant.</summary>
    static Constant IntConst(int value) => new(value, TypeRef.CoreLib("System", "Int32"));

    /// <summary>A single <c>string</c> case-label constant.</summary>
    static Constant StringConst(string value) => new(value, TypeRef.CoreLib("System", "String"));

    /// <summary>
    /// Raises csc's small switch-on-string lowering — a run of
    /// <c>if (v == "lit") goto case;</c> equality tests (each a
    /// <c>string.op_Equality</c> call whose true branch jumps to a case body),
    /// ending in a branch to the default — back into a C# <c>switch</c>
    /// statement. Recompiling the flat goto chain inverts the second and later
    /// branch polarities (csc folds <c>if (c) goto next; goto other; next:</c>
    /// into <c>brfalse other</c>), so the gotos never round-trip opcode-exact;
    /// the <c>switch</c> form does. Larger bucketed string switches are handled
    /// by the sibling recognizers below.
    ///
    /// The case bodies must tile the contiguous span after the dispatch chain,
    /// each entered only through the chain and exiting through a terminator or an
    /// unconditional branch to one shared join — the same single-entry-region
    /// model the jump-table raise uses; anything else is left flat for soundness.
    /// </summary>
    static bool RaiseStringEqualityChain(BlockContainer container, int s, HashSet<int> leaveTargets, Stepper stepper)
    {
        var blocks = container.Blocks;

        // The dispatch chain: a run of equality tests against the same value, each
        // branching to a case body when equal. The first test may share its block
        // with the straight-line setup that precedes the switch (e.g. spilling the
        // governing expression to a temp); the rest are single-statement blocks.
        if (blocks[s].Children is not [.., ConditionalBranch first]
            || !TryStringEqualityTest(first.Condition, out var value, out var firstLiteral))
            return false;
        var caseLabels = new List<Constant> { StringConst(firstLiteral) };
        var caseTargetOffsets = new List<int> { first.TargetOffset };

        int idx = s + 1;
        while (idx < blocks.Count
            && blocks[idx].Children is [ConditionalBranch cb]
            && TryStringEqualityTest(cb.Condition, out var testValue, out var literal)
            && PlaceIdentity.SameVariable(value, testValue))
        {
            // C# forbids duplicate case labels (CS0152); a repeated literal is not a
            // source switch. Decline rather than emit an uncompilable duplicate-label
            // switch — matching the hash / length-bucket raisers' guards.
            if (caseLabels.Any(label => Equals(label.Value, literal)))
                return false;
            caseLabels.Add(StringConst(literal));
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

        return FinishSwitchRaise(container, s, dispatchEnd, value, caseTargetOffsets,
            caseLabels, defaultOffset, leaveTargets, stepper);
    }

    /// <summary>
    /// Raises csc's larger switch-on-string lowering: a generated
    /// <c>&lt;PrivateImplementationDetails&gt;.ComputeStringHash</c> dispatch tree
    /// first narrows to hash buckets, then exact <c>String.op_Equality</c> tests
    /// enter the real case bodies. The hash tree and failed-bucket branches are
    /// scaffolding; the C# source is a single <c>switch</c> on the original string.
    /// </summary>
    static bool RaiseStringHashSwitch(BlockContainer container, int s, HashSet<int> leaveTargets, Stepper stepper)
    {
        var blocks = container.Blocks;
        if (!TryStringHashSetup(blocks[s], out var hashLocal, out var value, out var replaceFromChild))
            return false;

        var caseLabels = new List<Constant>();
        var caseTargetOffsets = new List<int>();
        var dispatchOffsets = new HashSet<int>();
        int? defaultOffset = null;
        bool DefaultConsistent(int off) => (defaultOffset ??= off) == off;

        int idx = s;
        int dispatchEnd = s - 1;
        while (idx < blocks.Count)
        {
            var children = blocks[idx].Children;
            IrNode? term = idx == s
                ? (children is [.., ConditionalBranch or Branch] ? children[^1] : null)
                : children switch
                {
                    [] => null,
                    [ConditionalBranch or Branch] => children[0],
                    _ => null,
                };

            if (term is null && children.Count == 0)
            {
                dispatchOffsets.Add(blocks[idx].StartOffset);
                dispatchEnd = idx++;
                continue;
            }

            if (term is ConditionalBranch cb)
            {
                if (TryStringEqualityTest(cb.Condition, out var testValue, out var literal))
                {
                    if (!PlaceIdentity.SameVariable(value, testValue))
                        return false;
                    if (caseLabels.Any(label => Equals(label.Value, literal)))
                        return false;
                    caseLabels.Add(StringConst(literal));
                    caseTargetOffsets.Add(cb.TargetOffset);
                }
                else if (!IsHashComparison(cb.Condition, hashLocal))
                {
                    break;
                }
            }
            else if (term is Branch branch)
            {
                if (!DefaultConsistent(branch.TargetOffset))
                    return false;
            }
            else
            {
                break;
            }

            dispatchOffsets.Add(blocks[idx].StartOffset);
            dispatchEnd = idx;
            idx++;
        }

        if (caseTargetOffsets.Count < 2 || defaultOffset is not { } def)
            return false;

        var caseTargets = caseTargetOffsets.ToHashSet();
        for (int i = s; i <= dispatchEnd; i++)
        {
            foreach (int target in blocks[i].Children.SelectMany(Targets))
            {
                if (!dispatchOffsets.Contains(target)
                    && target != def
                    && !caseTargets.Contains(target))
                {
                    return false;
                }
            }
        }

        return FinishSwitchRaise(container, s, dispatchEnd, value, caseTargetOffsets,
            caseLabels, def, leaveTargets, stepper, replaceFromChild);
    }

    /// <summary>
    /// Raises csc's length/character-bucket switch-on-string lowering. Current
    /// Roslyn often prefilters larger string switches by null, length, and one
    /// indexed character before exact <c>String.op_Equality</c> leaves enter the
    /// real case bodies. The bucket tests are scaffolding; the source is a
    /// <c>switch</c> on the original string.
    /// </summary>
    static bool RaiseStringLengthBucketSwitch(BlockContainer container, int s, HashSet<int> leaveTargets, Stepper stepper)
    {
        var blocks = container.Blocks;
        if (blocks[s].Children is not [ConditionalBranch { Condition: LogicalNot nullTest } first]
            || nullTest.Operand is not { } value)
        {
            return false;
        }

        var caseLabels = new List<Constant>();
        var caseTargetOffsets = new List<int>();
        var dispatchOffsets = new HashSet<int>();
        var bucketLocals = new HashSet<int>();
        int? defaultOffset = first.TargetOffset;
        bool DefaultConsistent(int off) => (defaultOffset ??= off) == off;

        int idx = s;
        int dispatchEnd = s - 1;
        while (idx < blocks.Count)
        {
            var children = blocks[idx].Children;
            StoreLocal? store = null;
            IrNode? term;
            if (children is [])
            {
                term = null;
            }
            else if (children is [ConditionalBranch or Branch])
            {
                term = children[0];
            }
            else if (children is [StoreLocal st, ConditionalBranch branch])
            {
                store = st;
                term = branch;
            }
            else
            {
                term = null;
            }

            if (term is null && children.Count == 0)
            {
                dispatchOffsets.Add(blocks[idx].StartOffset);
                dispatchEnd = idx++;
                continue;
            }

            if (store is not null && !TryStringCharBucketStore(store, value, bucketLocals))
                break;

            if (term is ConditionalBranch cb)
            {
                if (TryStringEqualityTest(cb.Condition, out var testValue, out var literal))
                {
                    if (!PlaceIdentity.SameVariable(value, testValue))
                        return false;
                    if (caseLabels.Any(label => Equals(label.Value, literal)))
                        return false;
                    caseLabels.Add(StringConst(literal));
                    caseTargetOffsets.Add(cb.TargetOffset);
                }
                else if (IsStringDefaultGuard(cb.Condition, value))
                {
                    if (!DefaultConsistent(cb.TargetOffset))
                        return false;
                }
                else if (!IsStringCharBucketComparison(cb.Condition, bucketLocals))
                {
                    break;
                }
            }
            else if (term is Branch branch)
            {
                if (!DefaultConsistent(branch.TargetOffset))
                    return false;
            }
            else
            {
                break;
            }

            dispatchOffsets.Add(blocks[idx].StartOffset);
            dispatchEnd = idx;
            idx++;
        }

        if (caseTargetOffsets.Count < 2 || defaultOffset is not { } def)
            return false;

        var caseTargets = caseTargetOffsets.ToHashSet();
        for (int i = s; i <= dispatchEnd; i++)
        {
            foreach (int target in blocks[i].Children.SelectMany(Targets))
            {
                if (!dispatchOffsets.Contains(target)
                    && target != def
                    && !caseTargets.Contains(target))
                {
                    return false;
                }
            }
        }

        return FinishSwitchRaise(container, s, dispatchEnd, value, caseTargetOffsets,
            caseLabels, def, leaveTargets, stepper, replaceFromChild: 0);
    }

    /// <summary>
    /// Raises csc's sparse switch-on-int lowering. When the case labels are too
    /// scattered for a jump table, csc emits a binary-search dispatch: a tree of
    /// relational pivots (<c>if (v &gt; k) …</c>) partitioning the value range,
    /// whose leaves are linear <c>if (v == k) goto case;</c> equality chains, each
    /// chain ending in a branch to the shared default. The decompiler renders that
    /// as nested <c>if</c>s; recompiling them re-derives a different branch shape,
    /// so they never round-trip opcode-exact. Collecting every equality leaf back
    /// into a <c>switch (v) { case k: … }</c> lets csc re-emit the original tree.
    ///
    /// The dispatch blocks (pivots, equality tests, and chain-terminating branches)
    /// are contiguous and precede the case bodies; pivots must route within that
    /// region, and the bodies must tile the span after it through the same
    /// single-entry-region model the other raises use. Anything irregular is left
    /// flat for soundness.
    /// </summary>
    static bool RaiseSparseIntSwitch(BlockContainer container, int s, HashSet<int> leaveTargets, Stepper stepper)
    {
        var blocks = container.Blocks;

        // The first dispatch block carries the governing value (often a spilled
        // temp) and ends in a comparison against it — an equality test (a case) or
        // a relational pivot.
        if (blocks[s].Children is not [.., ConditionalBranch firstBranch]
            || !TryIntComparison(firstBranch.Condition, out var value, out _, out _))
            return false;

        var caseLabels = new List<Constant>();
        var caseTargetOffsets = new List<int>();
        var pivotTargets = new List<int>();
        var dispatchOffsets = new HashSet<int>();
        int? defaultOffset = null;
        bool DefaultConsistent(int off) => (defaultOffset ??= off) == off;

        int idx = s;
        int dispatchEnd = s - 1;
        while (idx < blocks.Count)
        {
            // The first block may carry leading setup; the rest are single statements.
            var children = blocks[idx].Children;
            IrNode? term = idx == s
                ? (children is [.., ConditionalBranch or Branch] ? children[^1] : null)
                : (children is [ConditionalBranch] or [Branch] ? children[0] : null);

            if (term is ConditionalBranch cb
                && TryIntComparison(cb.Condition, out var testValue, out int constant, out bool isEqual)
                && PlaceIdentity.SameVariable(value, testValue))
            {
                if (isEqual)
                {
                    // C# forbids duplicate case labels (CS0152); a repeated constant is
                    // not a source switch. Decline rather than emit an uncompilable
                    // duplicate-label switch (mirrors the string raisers' guards).
                    if (caseLabels.Any(label => Equals(label.Value, constant)))
                        return false;
                    caseLabels.Add(IntConst(constant));
                    caseTargetOffsets.Add(cb.TargetOffset);
                }
                else
                {
                    pivotTargets.Add(cb.TargetOffset);   // a range pivot, not a case
                }
            }
            else if (term is Branch br && DefaultConsistent(br.TargetOffset))
            {
                // A chain terminator branching to the shared default.
            }
            else
            {
                break;   // the first case/default body block
            }

            dispatchOffsets.Add(blocks[idx].StartOffset);
            dispatchEnd = idx;
            idx++;
        }

        if (caseLabels.Count < 2)
            return false;   // a single test is an `if`, not a switch

        // Every relational pivot must branch back into the dispatch region.
        foreach (int t in pivotTargets)
            if (!dispatchOffsets.Contains(t))
                return false;

        // A switch with no explicit default branch falls through to the block
        // immediately after the dispatch region.
        if (defaultOffset is null)
        {
            if (dispatchEnd + 1 >= blocks.Count)
                return false;
            defaultOffset = blocks[dispatchEnd + 1].StartOffset;
        }

        return FinishSwitchRaise(container, s, dispatchEnd, value, caseTargetOffsets,
            caseLabels, defaultOffset.Value, leaveTargets, stepper);
    }

    /// <summary>
    /// Shared tail for the equality-chain raises: given the dispatch range, the
    /// governing value, and the collected case labels/targets plus the default,
    /// grow each case body into a single-entry region, verify the bodies tile the
    /// span after the dispatch, and emit the <c>switch</c> statement. Returns false
    /// (leaving the flat form) if anything is irregular.
    /// </summary>
    static bool FinishSwitchRaise(BlockContainer container, int s, int dispatchEnd, IrExpression value,
        List<int> caseTargetOffsets, List<Constant> caseLabels, int defaultOffset,
        HashSet<int> leaveTargets, Stepper stepper, int? replaceFromChild = null)
    {
        var blocks = container.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        if (!offsetToIndex.TryGetValue(defaultOffset, out int defaultIndex) || defaultIndex <= dispatchEnd)
            return false;

        var caseTargets = new int[caseTargetOffsets.Count];
        for (int k = 0; k < caseTargets.Length; k++)
            if (!offsetToIndex.TryGetValue(caseTargetOffsets[k], out caseTargets[k]) || caseTargets[k] <= dispatchEnd)
                return false;

        var preds = ChainPredecessors(blocks, offsetToIndex);

        var owned = new HashSet<int>();
        int? join = null;
        var regions = new Dictionary<int, List<int>>();

        foreach (int target in caseTargets.Distinct())
            if (!TryAddOwnedRegion(blocks, target, dispatchEnd, caseTargets, offsetToIndex,
                    preds, regions, owned, ref join))
                return false;

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
            if (!TryAddOwnedRegion(blocks, defaultIndex, dispatchEnd, caseTargets, offsetToIndex,
                    preds, regions, owned, ref join))
                return false;
            defaultBodyHead = defaultIndex;
        }

        // The body blocks must tile the contiguous span [dispatchEnd+1, regionEnd).
        int regionEnd = join ?? (owned.Count == 0 ? dispatchEnd + 1 : owned.Max() + 1);
        int firstBody = dispatchEnd + 1;
        if (!OwnsTiledRegion(owned, firstBody, join, regionEnd))
            return false;

        foreach (var region in regions.Values)
            if (!ExitsAreUnconditional(blocks, region, offsetToIndex))
                return false;

        if (!OnlyReachedByChain(blocks, owned, s, dispatchEnd, leaveTargets))
            return false;

        BuildSwitchStatement(container, s, dispatchEnd, value, caseTargets, caseLabels,
            regions, defaultBodyHead, defaultSharesTarget, join, regionEnd, stepper, replaceFromChild);
        return true;
    }

    static bool TryStringHashSetup(Block block, out int hashLocal, out IrExpression value, out int replaceFromChild)
    {
        hashLocal = -1;
        value = null!;
        replaceFromChild = -1;

        for (int i = 0; i < block.Children.Count - 1; i++)
        {
            if (block.Children[i] is StoreLocal
                {
                    Value: Call { Arguments: [var hashInput] } call,
                } store
                && GeneratedCodeIdentity.IsStringHashHelper(call.Callee))
            {
                hashLocal = store.Index;
                value = hashInput;
                replaceFromChild = i;
                return true;
            }
        }

        return false;
    }

    static bool TryStringCharBucketStore(StoreLocal store, IrExpression value, HashSet<int> bucketLocals)
    {
        if (store.Value is not LoadProperty property
            || !MemberIdentity.IsStringCharsGetter(property)
            || property.Instance is not { } instance
            || !PlaceIdentity.SameVariable(value, instance)
            || property.IndexArguments is not [Constant { Value: int }])
        {
            return false;
        }

        bucketLocals.Add(store.Index);
        return true;
    }

    /// <summary>A <c>string.op_Equality(value, "literal")</c> test, in either argument order.</summary>
    static bool TryStringEqualityTest(IrExpression condition, out IrExpression value, out string literal)
    {
        value = null!;
        literal = null!;
        if (condition is Call { Arguments: var args } call
            && MemberIdentity.IsStringEquality(call))
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

    static bool IsHashComparison(IrExpression condition, int hashLocal)
        => condition is Comparison
        {
            Kind: ComparisonKind.Equal or ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
                or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual,
            Left: LoadLocal left,
            Right: Constant { Value: int or uint },
        } && left.Index == hashLocal;

    static bool IsStringDefaultGuard(IrExpression condition, IrExpression value)
    {
        if (condition is LogicalNot { Operand: var operand })
            return PlaceIdentity.SameVariable(value, operand);

        return condition is Comparison
        {
            Kind: ComparisonKind.NotEqual,
            Left: LoadProperty property,
            Right: Constant { Value: int },
        }
        && MemberIdentity.IsStringLengthGetter(property)
        && property.Instance is { } instance
        && PlaceIdentity.SameVariable(value, instance);
    }

    static bool IsStringCharBucketComparison(IrExpression condition, HashSet<int> bucketLocals)
        => condition is Comparison
        {
            Kind: ComparisonKind.Equal or ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
                or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual,
            Left: LoadLocal left,
            Right: Constant { Value: char or int },
        } && bucketLocals.Contains(left.Index);

    /// <summary>
    /// An integer comparison <c>v &lt;op&gt; const</c> (in either operand order)
    /// against an <c>int</c> constant — the equality leaves and relational pivots
    /// of csc's sparse switch dispatch. <paramref name="isEqual"/> distinguishes a
    /// case test (<c>==</c>) from a range pivot.
    /// </summary>
    static bool TryIntComparison(IrExpression condition, out IrExpression value, out int constant, out bool isEqual)
    {
        value = null!;
        constant = 0;
        isEqual = false;
        if (condition is not Comparison cmp
            || cmp.Kind is not (ComparisonKind.Equal or ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual
                                or ComparisonKind.GreaterThan or ComparisonKind.GreaterThanOrEqual))
            return false;
        if (cmp.Right is Constant { Value: int right })
        {
            value = cmp.Left;
            constant = right;
            isEqual = cmp.Kind == ComparisonKind.Equal;
            return true;
        }
        if (cmp.Left is Constant { Value: int left })
        {
            value = cmp.Right;
            constant = left;
            isEqual = cmp.Kind == ComparisonKind.Equal;
            return true;
        }
        return false;
    }

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

    static void BuildSwitchStatement(
        BlockContainer container, int s, int dispatchEnd, IrExpression value, int[] caseTargets,
        IReadOnlyList<Constant> caseLabels, Dictionary<int, List<int>> regions, int? defaultBodyHead,
        int? defaultSharesTarget, int? join, int regionEnd, Stepper stepper, int? replaceFromChild = null)
    {
        var all = container.Blocks.ToList();
        int? joinOffset = join is { } j ? all[j].StartOffset : null;

        // Case labels grouped by target, in first-appearance order.
        var labelsByTarget = new Dictionary<int, List<Constant>>();
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
            sections.Add(new SwitchSection([.. labelsByTarget[target]], isDefault: target == defaultSharesTarget,
                SectionBody(regions[target].Select(i => all[i]).ToList(), joinOffset)));
        if (defaultBodyHead is { } dh)
            sections.Add(new SwitchSection([], isDefault: true,
                SectionBody(regions[dh].Select(i => all[i]).ToList(), joinOffset)));

        // Keep the straight-line setup that precedes the switch; replace only the
        // trailing dispatch test with the raised statement.
        var switchBlock = all[s];
        int firstReplaced = replaceFromChild ?? switchBlock.Children.Count - 1;
        while (switchBlock.Children.Count > firstReplaced)
            switchBlock.Children[^1].Detach();
        switchBlock.Add(new Switch(switchValue, sections));

        var rebuilt = new BlockContainer();
        for (int i = 0; i < s; i++)
            rebuilt.Add(all[i]);
        rebuilt.Add(switchBlock);
        for (int i = regionEnd; i < all.Count; i++)
            rebuilt.Add(all[i]);
        stepper.StepOver("raise switch equality chain to switch", container);
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
        Dictionary<int, List<int>> regions, int defaultIndex, bool defaultSharesTarget, int join,
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
                sections.Add(new SwitchSection(IntLabels(labels),
                    isDefault: defaultSharesTarget && target == defaultIndex, EmptyBreakBody()));
            else
                sections.Add(new SwitchSection(IntLabels(labels),
                    isDefault: defaultSharesTarget && target == defaultIndex,
                    SectionBody(regions[target].Select(i => all[i]).ToList(), joinOffset)));
        }
        if (!defaultSharesTarget)
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
        stepper.StepOver("raise IL jump table to switch (case target is continuation)", container);
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
