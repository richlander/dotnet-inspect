namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises forward branch regions into nested <see cref="IfStatement"/>s —
/// the first structuring slice. Guard shapes (<c>if (c) goto M; …body…; M:</c>)
/// and diamonds (<c>if (c) goto T; …false…; goto M; T: …true…; M:</c>) nest
/// recursively. Terminator guards (<c>if (c) goto T;</c> where T is a short
/// <c>throw</c>/<c>return</c>-only block not reached by any goto) inline a copy
/// of T's statements, dissolving shared-terminator joins that otherwise break
/// strict nesting. The pass is two-phase: the whole function is validated
/// against the slice (forward branches only — loops, switch, and EH stay
/// flat) before any mutation, so a function either structures completely or
/// keeps the always-correct flat form. Conditions render the fallthrough
/// arm first, matching the current emitter's guard style.
/// </summary>
public sealed class StructuringPass : IIrPass
{
    public string Name => "structuring";

    /// <summary>A terminator block longer than this is not inlined as a guard (keeps duplication small).</summary>
    const int MaxTerminatorChildren = 3;
    const int MaxInlineRegionBlocks = 4;

    /// <summary>
    /// Per-container facts precomputed before any mutation: the block list and
    /// offset map, the offsets reached by an unconditional <c>goto</c> (so an
    /// inlined terminator never erases a label some goto still needs), the
    /// blocks dropped from the linear walk after being cloned or inlined
    /// into a guard, and a snapshot of each inlinable terminator's statements
    /// (taken before <see cref="BuildRegion"/> detaches anything, so the
    /// clone source survives mutation order).
    /// </summary>
    sealed class Ctx
    {
        public required IReadOnlyList<Block> Blocks { get; init; }
        public required Dictionary<int, int> OffsetToIndex { get; init; }
        public required HashSet<int> UnconditionalTargets { get; init; }
        public required Dictionary<int, int> ConditionalTargetCounts { get; init; }
        public required HashSet<int> BranchTargets { get; init; }
        public required HashSet<int> DroppableBlocks { get; init; }
        public required Dictionary<int, IReadOnlyList<IrNode>> TerminatorSnapshots { get; init; }
        public required HashSet<int> FallenInto { get; init; }
        public required bool IsComparisonTree { get; init; }
        public int? RegionExitLeaveTarget { get; init; }

        /// <summary>
        /// First-wins recorder for the deepest direct stop reason, populated only
        /// when the <c>--structuring-stops</c> diagnostic is active (null on every
        /// normal run). <see cref="Validate"/> records at each labelled
        /// <c>return false</c>; recursion propagating that false does not overwrite it.
        /// </summary>
        public StopRecorder? Recorder { get; init; }
    }

    /// <summary>Captures the first (innermost) stop reason for one container; later reasons are ignored.</summary>
    sealed class StopRecorder
    {
        public string? Reason { get; private set; }
        public void Record(string reason) => Reason ??= reason;
    }

    readonly record struct WhileShape(int ContinueAt, ConditionalBranch BackBranch, ConditionalBranch? ExitBranch);

    public void Run(IrFunction function, PassContext context)
    {
        if (!function.Regions.IsEmpty)
        {
            context.StructuringDiagnostics?.RecordStop("unconsumed-regions");
            return;  // unconsumed regions: the flat form is still the truth
        }
        // Containers are independent regions: the function body plus every
        // try/catch/finally body the EH pass nested. Each structures (or
        // stays flat) on its own. Walk inner containers first so a consumed
        // leave-to-finally-continuation can disappear before the outer container
        // decides whether that target block still needs a printable label.
        foreach (var container in function.Descendants.OfType<BlockContainer>().OrderByDescending(Depth).ToList())
        {
            var leaveTargets = function.Descendants.OfType<Leave>()
                .Select(leave => leave.TargetOffset)
                .ToHashSet();
            Structure(container, leaveTargets, context);
        }
    }

    static void Structure(BlockContainer container, HashSet<int> leaveTargets, PassContext context)
    {
        var blocks = container.Blocks;
        if (blocks.Count <= 1)
            return;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        // A label is needed only for an unconditional goto: conditional guards
        // to a terminator are inlined, so they impose no label. Count the
        // conditional branches per target: a terminator reached by two or more
        // is a genuine shared join that strict nesting cannot express, so it
        // is the one worth dissolving by inlining; a single-predecessor guard
        // the standard forms already raise cleanly stays untouched.
        var unconditionalTargets = new HashSet<int>();
        var conditionalTargetCounts = new Dictionary<int, int>();
        var branchTargets = new HashSet<int>();
        foreach (var block in blocks)
        {
            foreach (var child in block.Children)
            {
                if (child is Branch branch)
                {
                    unconditionalTargets.Add(branch.TargetOffset);
                    branchTargets.Add(branch.TargetOffset);
                }
                else if (child is ConditionalBranch conditional)
                {
                    conditionalTargetCounts[conditional.TargetOffset] =
                        conditionalTargetCounts.GetValueOrDefault(conditional.TargetOffset) + 1;
                    branchTargets.Add(conditional.TargetOffset);
                }
                else if (child is SwitchBranch switchBranch)
                {
                    foreach (int target in switchBranch.TargetOffsets)
                    {
                        unconditionalTargets.Add(target);
                        branchTargets.Add(target);
                    }
                }
            }
        }

        // A terminator whose only predecessors are inlined guards (no goto
        // targets it and the preceding block does not fall into it) becomes
        // dead once its guards inline — drop it from the walk. Later, past-region
        // terminating targets cloned into a guard are added to the same drop set.
        // Snapshot every
        // inlinable terminator's statements now, before BuildRegion mutates.
        // Blocks the preceding block falls through into — control reaches them
        // in program order, so they are not isolated guard leaves.
        var fallenInto = new HashSet<int>();
        for (int i = 1; i < blocks.Count; i++)
            if (FallsThrough(blocks[i - 1]))
                fallenInto.Add(blocks[i].StartOffset);

        // A return guard-leaf is only inlined inside a genuine comparison tree;
        // small selections keep their ternary/boolean shape.
        bool isComparisonTree = ComparisonTrees.IsLikely(container);

        var droppable = new HashSet<int>();
        var snapshots = new Dictionary<int, IReadOnlyList<IrNode>>();
        for (int i = 0; i < blocks.Count; i++)
        {
            if (!IsSharedTerminator(blocks[i], unconditionalTargets, conditionalTargetCounts, fallenInto, isComparisonTree))
                continue;
            snapshots[i] = blocks[i].Children.ToList();
            if (i > 0 && !FallsThrough(blocks[i - 1]))
                droppable.Add(i);
        }

        var recorder = context.StructuringDiagnostics is null ? null : new StopRecorder();
        var ctx = new Ctx
        {
            Blocks = blocks,
            OffsetToIndex = offsetToIndex,
            UnconditionalTargets = unconditionalTargets,
            ConditionalTargetCounts = conditionalTargetCounts,
            BranchTargets = branchTargets,
            DroppableBlocks = droppable,
            TerminatorSnapshots = snapshots,
            FallenInto = fallenInto,
            IsComparisonTree = isComparisonTree,
            RegionExitLeaveTarget = NormalEhContinuationTarget(container),
            Recorder = recorder,
        };

        var leaveTargetIndices = blocks
            .Select((block, index) => leaveTargets.Contains(block.StartOffset) ? index : -1)
            .Where(index => index >= 0)
            .ToList();
        if (leaveTargetIndices.Count > 0
            && leaveTargetIndices.Any(index => FindLeaveRetryLoopShape(ctx, index, blocks.Count) is null))
        {
            context.StructuringDiagnostics?.RecordStop("leave-target-in-container");
            return;
        }

        if (!Validate(ctx, 0, blocks.Count, joinIndex: blocks.Count, breakTarget: null, continueTarget: null))
        {
            context.StructuringDiagnostics?.RecordStop(recorder?.Reason ?? "unknown");
            return;
        }

        context.StructuringDiagnostics?.RecordStructured();

        context.Stepper.StepOver(
            $"structure container at IL_{blocks[0].StartOffset:X4} ({blocks.Count} blocks) into nested if/diamond regions",
            container);

        var structured = BuildRegion(ctx, 0, blocks.Count, joinIndex: blocks.Count, breakTarget: null, continueTarget: null);
        var replacement = new BlockContainer();
        replacement.Add(structured);
        container.ReplaceWith(replacement);
    }

    /// <summary>
    /// Phase 1: pure shape check over block indices — no mutation until the
    /// whole function fits the slice. <paramref name="breakTarget"/> is the
    /// block index of the enclosing loop's exit (null outside a loop body): a
    /// forward branch there is a <c>break</c>, in or out of nested ifs.
    /// <paramref name="continueTarget"/> is the head index of the enclosing
    /// infinite (<c>while (true)</c>) loop, whose latch is an unconditional
    /// backward branch to that head (null outside such a loop body).
    /// </summary>
    static bool Validate(Ctx ctx, int start, int stop, int joinIndex, int? breakTarget, int? continueTarget)
    {
        var blocks = ctx.Blocks;
        var offsetToIndex = ctx.OffsetToIndex;
        int i = start;
        while (i < stop)
        {
            // A block left dead by inlining/cloning into an earlier guard prints nothing.
            if (ctx.DroppableBlocks.Contains(i))
            {
                i++;
                continue;
            }
            // An infinite loop head: a later latch branches unconditionally back
            // here. Its body (head..latch] is its own region whose breaks target
            // the block after the latch and whose back-edge is the latch's goto.
            // Skip when this is the head of the loop currently being validated
            // (continueTarget == i) so the body walk does not re-enter the loop.
            if (continueTarget != i && FindInfiniteLoopShape(ctx, i, stop) is { } latch)
            {
                if (!Validate(ctx, i, latch + 1, joinIndex: latch + 1, breakTarget: latch + 1, continueTarget: i))
                    return false;
                i = latch + 1;
                continue;
            }
            var block = blocks[i];
            if (block.Children.Count == 0)
            {
                // Nop-only label landing pads (Debug builds): pure fallthrough.
                i++;
                continue;
            }
            for (int s = 0; s < block.Children.Count - 1; s++)
            {
                if (block.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                {
                    ctx.Recorder?.Record("mid-block-control-flow");
                    return false;  // mid-block control flow is outside the slice
                }
            }
            switch (block.Children[^1])
            {
                case Return or Throw or Break:
                    // A straight-line terminator: the loop pass already raised
                    // the break, so this block ends its region cleanly.
                    i++;
                    break;
                case Leave leave when offsetToIndex.TryGetValue(leave.TargetOffset, out int leaveTarget)
                    && continueTarget == leaveTarget
                    && CanRaiseRetryLeave(leave):
                    i++;
                    break;
                case Leave leave when !offsetToIndex.ContainsKey(leave.TargetOffset):
                    if (IsRegionExitLeave(ctx, leave))
                    {
                        if (i + 1 != stop)
                        {
                            ctx.Recorder?.Record("leave-region-exit-not-region-end");
                            return false;
                        }
                        i = stop;
                        break;
                    }
                    // A leave that exits this container (its target is an
                    // enclosing construct's continuation, not a block here) ends
                    // its path like a return/break: control leaves the region and
                    // the printer still emits the same `goto IL_xxxx; // leave`.
                    // The target block lives in an enclosing container, which the
                    // leave-target-in-container guard (Structure) keeps flat, so
                    // its label always survives — safe to structure around here.
                    i++;
                    break;
                case Leave or EndFinally or EndFilter:
                    // Survivors of the EH pass (an early exit through outer
                    // constructs) keep their container flat: structure would
                    // erase the label their goto needs.
                    ctx.Recorder?.Record("eh-terminator-survivor");
                    return false;
                case Branch branch:
                {
                    if (!offsetToIndex.TryGetValue(branch.TargetOffset, out int branchTarget))
                    {
                        ctx.Recorder?.Record("branch-target-external");
                        return false;
                    }
                    // An unconditional branch to the enclosing loop's exit is a break.
                    if (breakTarget == branchTarget)
                    {
                        i++;
                        break;
                    }
                    // The latch of the enclosing infinite loop: an unconditional
                    // back-edge to the head, as the loop body's last block. It is
                    // the while-loop's implicit back-edge, not an exit goto.
                    if (continueTarget == branchTarget && i + 1 == stop)
                    {
                        i = stop;
                        break;
                    }
                    // csc's guarded while: br COND; BODY...; COND: brtrue BODY.
                    if (FindWhileShape(ctx, i, branchTarget, stop) is { } loop)
                    {
                        // The body's breaks target the block after the loop.
                        if (!Validate(ctx, i + 1, branchTarget, joinIndex: branchTarget, breakTarget: loop.ContinueAt, continueTarget))
                            return false;
                        i = loop.ContinueAt;
                        break;
                    }
                    // Otherwise only the region-exit goto is in the slice; it
                    // must be the region's last block.
                    if (branchTarget != joinIndex || i + 1 != stop)
                    {
                        ctx.Recorder?.Record("forward-branch-not-region-exit");
                        return false;
                    }
                    i = stop;
                    break;
                }
                case ConditionalBranch conditional:
                {
                    if (!offsetToIndex.TryGetValue(conditional.TargetOffset, out int target))
                    {
                        ctx.Recorder?.Record("cond-target-external");
                        return false;
                    }
                    // A conditional branch to the loop exit is `if (c) break;`.
                    if (breakTarget == target)
                    {
                        i++;
                        break;
                    }
                    // Terminator guard: `if (c) goto T` where T is a short
                    // throw/return-only block no goto targets. Inlining a copy
                    // of T dissolves the branch — position-independent, so T may
                    // lie past this region (the shared outer terminator case).
                    if (target > i && IsInlinableTerminator(ctx, target))
                    {
                        i++;
                        break;
                    }
                    if (target <= i)
                    {
                        ctx.Recorder?.Record("cond-backward-branch");
                        return false;  // backward = loop (later slice)
                    }
                    int falseStart = i + 1;
                    if (falseStart + 1 == target
                        && IsRegionExitTerminator(ctx, falseStart)
                        && Validate(ctx, target, stop, joinIndex, breakTarget, continueTarget))
                    {
                        i = stop;
                        break;
                    }
                    // A conditional that jumps to this region's own merge — its
                    // post-dominator, tracked as joinIndex — is an early exit to
                    // the join: `if (c) goto JOIN` selects the merge, the
                    // fallthrough body runs otherwise. This is the diamond/guard
                    // false arm reaching the common exit past its lexical
                    // boundary (joinIndex >= stop), the shape the index-range
                    // model could not name. Raise it as a guard over the rest of
                    // the region; the merge stays reached by fallthrough.
                    if (target == joinIndex)
                    {
                        if (!Validate(ctx, i + 1, stop, joinIndex, breakTarget, continueTarget))
                            return false;
                        i = stop;
                        break;
                    }
                    if (target > stop)
                    {
                        if (CanClonePastRegionTerminatorInLeaveRetryLoop(ctx, continueTarget, target)
                            || CanInlinePastRegionTarget(ctx, target))
                        {
                            i++;
                            break;
                        }
                        ctx.Recorder?.Record("cond-target-past-region");
                        return false;  // past region = out of slice (the common-exit merge)
                    }
                    // Guard: if (c) goto M with M ending this region's view.
                    // Diamond: the fallthrough arm ends with goto M past the
                    // true arm.
                    if (FindRegionExitDiamond(ctx, falseStart, target, stop, joinIndex) is { } exitDiamond)
                    {
                        if (!Validate(ctx, falseStart, target, joinIndex: exitDiamond.ExitTarget, breakTarget, continueTarget)
                            || !Validate(ctx, target, exitDiamond.LocalJoin, joinIndex: exitDiamond.ExitTarget, breakTarget, continueTarget))
                        {
                            return false;
                        }
                        i = exitDiamond.LocalJoin;
                        break;
                    }
                    if (FindDiamondJoin(blocks, offsetToIndex, falseStart, target, stop) is { } join)
                    {
                        // False arm exits by goto join; true arm falls (or
                        // returns) into join.
                        if (!Validate(ctx, falseStart, target, joinIndex: join, breakTarget, continueTarget)
                            || !Validate(ctx, target, join, joinIndex: join, breakTarget, continueTarget))
                        {
                            return false;
                        }
                        i = join;
                        break;
                    }
                    // Guard form: arm is (i+1, target), continues at target.
                    if (!Validate(ctx, falseStart, target, joinIndex: target, breakTarget, continueTarget))
                        return false;
                    i = target;
                    break;
                }
                default:
                    // A block that falls through to its successor.
                    if (i + 1 >= stop && stop != joinIndex)
                    {
                        ctx.Recorder?.Record("fallthrough-past-region");
                        return false;
                    }
                    i++;
                    break;
            }
        }
        return true;
    }

    /// <summary>
    /// csc's guarded while at block <paramref name="i"/>: it ends with
    /// <c>br COND</c> (forward), and the condition block COND consists of a
    /// single <c>ConditionalBranch</c> back to the body start (i+1). The
    /// body (i+1, COND) is its own region whose normal exit is the condition
    /// block and whose breaks target the block after it (ContinueAt).
    /// Multi-statement condition blocks are outside this slice.
    /// </summary>
    static WhileShape? FindWhileShape(Ctx ctx, int i, int conditionIndex, int stop)
    {
        var blocks = ctx.Blocks;
        var offsetToIndex = ctx.OffsetToIndex;
        if (conditionIndex <= i + 1 || conditionIndex >= stop)
            return null;
        var conditionBlock = blocks[conditionIndex];
        if (conditionBlock.Children.Count != 1
            || conditionBlock.Children[0] is not ConditionalBranch firstBranch)
        {
            return null;
        }
        if (offsetToIndex.TryGetValue(firstBranch.TargetOffset, out int bodyStart)
            && bodyStart == i + 1)
        {
            return new WhileShape(conditionIndex + 1, firstBranch, ExitBranch: null);
        }

        // Compound latch: init jumps to the first latch test; that test exits,
        // and the adjacent second test is the only branch back to the loop body.
        // This is csc's `while (a && b)` latch shape. Requiring single-statement
        // latch blocks keeps side effects in their original condition terms and
        // rejects latch blocks with extra work that cannot be folded into `&&`.
        int secondConditionIndex = conditionIndex + 1;
        if (secondConditionIndex >= stop
            || !offsetToIndex.TryGetValue(firstBranch.TargetOffset, out int exitIndex)
            || exitIndex != secondConditionIndex + 1)
        {
            return null;
        }

        var secondBlock = blocks[secondConditionIndex];
        if (secondBlock.Children.Count != 1
            || secondBlock.Children[0] is not ConditionalBranch backBranch
            || !offsetToIndex.TryGetValue(backBranch.TargetOffset, out bodyStart)
            || bodyStart != i + 1)
        {
            return null;
        }

        return CountBranchTargets(ctx, bodyStart) == 1
            ? new WhileShape(exitIndex, backBranch, firstBranch)
            : null;
    }

    static int CountBranchTargets(Ctx ctx, int targetIndex)
    {
        int targetOffset = ctx.Blocks[targetIndex].StartOffset;
        int count = 0;
        foreach (var block in ctx.Blocks)
        {
            foreach (var child in block.Children)
            {
                if (child is Branch branch && branch.TargetOffset == targetOffset)
                    count++;
                else if (child is ConditionalBranch conditional && conditional.TargetOffset == targetOffset)
                    count++;
                else if (child is SwitchBranch switchBranch)
                    count += switchBranch.TargetOffsets.Count(offset => offset == targetOffset);
            }
        }
        return count;
    }

    /// <summary>
    /// An infinite (<c>while (true)</c>) loop headed at <paramref name="head"/>:
    /// a single later block in <c>(head, stop)</c> ends with an unconditional
    /// <see cref="Branch"/> back to the head, or one or more safe EH
    /// <see cref="Leave"/> nodes target the head from a recovered protected
    /// region. Returns the latch index; the loop body is <c>[head, latch]</c>
    /// and execution continues at <c>latch + 1</c>.
    ///
    /// A conditional back-edge (a do-while, already raised, or a mid-body
    /// <c>continue</c>) keeps the container flat. Ordinary branches still require
    /// one latch; EH retry loops may have multiple retry leaves, all converted to
    /// <c>continue</c> after the loop body is built.
    /// </summary>
    static int? FindInfiniteLoopShape(Ctx ctx, int head, int stop)
        => FindBranchInfiniteLoopShape(ctx, head, stop)
            ?? FindLeaveRetryLoopShape(ctx, head, stop);

    static int? FindBranchInfiniteLoopShape(Ctx ctx, int head, int stop)
    {
        var blocks = ctx.Blocks;
        int headOffset = blocks[head].StartOffset;
        int latch = -1;
        for (int j = head + 1; j < stop; j++)
        {
            var children = blocks[j].Children;
            for (int s = 0; s < children.Count; s++)
            {
                int targetOffset = children[s] switch
                {
                    Branch b => b.TargetOffset,
                    ConditionalBranch c => c.TargetOffset,
                    _ => -1,
                };
                if (targetOffset != headOffset)
                    continue;
                // The only branch back to the head must be the latch: an
                // unconditional goto that is its block's last statement. A
                // conditional or mid-block back-edge, or a second one, is outside
                // this slice.
                if (latch != -1 || children[s] is not Branch || s != children.Count - 1)
                    return null;
                latch = j;
            }
        }
        return latch == -1 ? null : latch;
    }

    static int? FindLeaveRetryLoopShape(Ctx ctx, int head, int stop)
    {
        var blocks = ctx.Blocks;
        int headOffset = blocks[head].StartOffset;
        if (CountBranchTargets(ctx, head) != 0)
            return null;

        int latch = -1;
        for (int j = head + 1; j < stop; j++)
        {
            var retryLeaves = RetryLeavesTo(blocks[j], headOffset).ToList();
            if (retryLeaves.Count == 0)
                continue;
            if (retryLeaves.Any(leave => !CanRaiseRetryLeave(leave)))
                return null;
            latch = j;
        }

        bool hasLoopExit = latch >= 0
            && Enumerable.Range(head, latch - head + 1).Any(index => HasLoopExit(blocks[index]));
        return latch == -1 || !hasLoopExit ? null : latch;
    }

    static IEnumerable<Leave> RetryLeavesTo(Block block, int targetOffset)
        => block.Descendants.OfType<Leave>().Where(leave => leave.TargetOffset == targetOffset);

    static bool HasLoopExit(Block block)
    {
        foreach (var node in block.Descendants.Prepend(block))
        {
            if (node is Return or Throw or Break)
                return true;
        }
        return false;
    }

    static bool CanRaiseRetryLeave(Leave leave)
        => HasAncestor<TryFinally>(leave)
            && !HasAncestor<CatchClause>(leave)
            && !IsInsideFinallyBody(leave);

    static bool HasAncestor<T>(IrNode node) where T : IrNode
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (current is T)
                return true;
        return false;
    }

    static bool IsInsideFinallyBody(Leave leave)
    {
        for (var current = leave.Parent; current is not null; current = current.Parent)
        {
            if (current.Parent is TryFinally tryFinally && ReferenceEquals(current, tryFinally.FinallyBody))
                return true;
        }
        return false;
    }

    /// <summary>The <c>true</c> condition of a raised <c>while (true)</c> loop.</summary>
    static Constant TrueLiteral() => new(true, TypeRef.CoreLib("System", "Boolean"));

    /// <summary>
    /// A diamond whose arms both end by branching to the enclosing region's
    /// tracked join. The local C# merge is this region's lexical stop; the
    /// branch target is outside that stop, so the ordinary contiguous diamond
    /// helper must not return it as the local join or it would swallow the next
    /// sibling block.
    /// </summary>
    static (int LocalJoin, int ExitTarget)? FindRegionExitDiamond(Ctx ctx, int falseStart, int trueStart, int stop, int joinIndex)
    {
        if (falseStart >= trueStart || trueStart >= stop)
            return null;
        if (trueStart != falseStart + 1 || stop != trueStart + 1)
            return null;
        if (!EndsWithRegionExit(ctx, trueStart - 1, joinIndex))
            return null;
        if (!EndsWithRegionExit(ctx, stop - 1, joinIndex))
            return null;
        if (!IsStraightLineUntilFinalBranch(ctx.Blocks[trueStart - 1])
            || !IsStraightLineUntilFinalBranch(ctx.Blocks[stop - 1]))
        {
            return null;
        }
        return (stop, joinIndex);
    }

    static bool IsStraightLineUntilFinalBranch(Block block)
    {
        for (int s = 0; s < block.Children.Count - 1; s++)
        {
            if (block.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return false;
        }

        return true;
    }

    static bool CanInlinePastRegionTarget(Ctx ctx, int target)
        => !ctx.UnconditionalTargets.Contains(ctx.Blocks[target].StartOffset)
            && !ctx.FallenInto.Contains(ctx.Blocks[target].StartOffset)
            && TryBuildPastRegionTarget(ctx, target, out _, out _);

    static bool CanClonePastRegionTerminator(Ctx ctx, int target)
        => TryClonePastRegionTerminator(ctx, target, out _);

    static bool CanClonePastRegionTerminatorInLeaveRetryLoop(Ctx ctx, int? continueTarget, int target)
        => continueTarget is { } head
            && IsLeaveRetryLoopHead(ctx, head)
            && CanClonePastRegionTerminator(ctx, target);

    static bool IsLeaveRetryLoopHead(Ctx ctx, int head)
        => FindLeaveRetryLoopShape(ctx, head, ctx.Blocks.Count) is not null;

    static bool TryClonePastRegionTerminator(Ctx ctx, int target, out Block body)
    {
        body = null!;
        if (target < 0 || target >= ctx.Blocks.Count)
            return false;
        if (!IsTerminatorBlock(ctx.Blocks[target]) || ContainsConstructorChainCall(ctx.Blocks[target]))
            return false;

        var inlineCtx = CreateInlineCtx([CloneBlock(ctx.Blocks[target])]);
        if (!Validate(inlineCtx, 0, 1, joinIndex: 1, breakTarget: null, continueTarget: null))
            return false;

        body = BuildRegion(inlineCtx, 0, 1, joinIndex: 1, breakTarget: null, continueTarget: null);
        return true;
    }

    static bool TryBuildPastRegionTarget(Ctx ctx, int target, out Block body, out int inlinedStop)
    {
        body = null!;
        inlinedStop = -1;
        int maxStop = Math.Min(ctx.Blocks.Count, target + MaxInlineRegionBlocks);
        for (int stop = target + 1; stop <= maxStop; stop++)
        {
            if (!EndsWithTerminator(ctx.Blocks[stop - 1]))
                continue;
            if (Enumerable.Range(target, stop - target).Any(i => ContainsConstructorChainCall(ctx.Blocks[i])))
                continue;

            var clonedBlocks = new List<Block>(stop - target);
            for (int i = target; i < stop; i++)
                clonedBlocks.Add(CloneBlock(ctx.Blocks[i]));

            var inlineCtx = CreateInlineCtx(clonedBlocks);
            if (!Validate(inlineCtx, 0, clonedBlocks.Count, joinIndex: clonedBlocks.Count, breakTarget: null, continueTarget: null))
                continue;

            body = BuildRegion(inlineCtx, 0, clonedBlocks.Count, joinIndex: clonedBlocks.Count, breakTarget: null, continueTarget: null);
            inlinedStop = stop;
            return true;
        }
        return false;
    }

    static bool EndsWithTerminator(Block block)
        => block.Children.Count > 0 && block.Children[^1] is Return or Throw;

    static Block CloneBlock(Block source)
    {
        var clone = new Block(source.StartOffset);
        foreach (var statement in source.Children)
            clone.Add(statement.Clone());
        return clone;
    }

    static Ctx CreateInlineCtx(IReadOnlyList<Block> blocks)
    {
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        var unconditionalTargets = new HashSet<int>();
        var conditionalTargetCounts = new Dictionary<int, int>();
        var branchTargets = new HashSet<int>();
        foreach (var block in blocks)
        {
            foreach (var child in block.Children)
            {
                if (child is Branch branch)
                {
                    unconditionalTargets.Add(branch.TargetOffset);
                    branchTargets.Add(branch.TargetOffset);
                }
                else if (child is ConditionalBranch conditional)
                {
                    conditionalTargetCounts[conditional.TargetOffset] =
                        conditionalTargetCounts.GetValueOrDefault(conditional.TargetOffset) + 1;
                    branchTargets.Add(conditional.TargetOffset);
                }
                else if (child is SwitchBranch switchBranch)
                {
                    foreach (int target in switchBranch.TargetOffsets)
                    {
                        unconditionalTargets.Add(target);
                        branchTargets.Add(target);
                    }
                }
            }
        }

        var fallenInto = new HashSet<int>();
        for (int i = 1; i < blocks.Count; i++)
            if (FallsThrough(blocks[i - 1]))
                fallenInto.Add(blocks[i].StartOffset);

        return new Ctx
        {
            Blocks = blocks,
            OffsetToIndex = offsetToIndex,
            UnconditionalTargets = unconditionalTargets,
            ConditionalTargetCounts = conditionalTargetCounts,
            BranchTargets = branchTargets,
            DroppableBlocks = [],
            TerminatorSnapshots = new Dictionary<int, IReadOnlyList<IrNode>>(),
            FallenInto = fallenInto,
            IsComparisonTree = false,
            RegionExitLeaveTarget = null,
            Recorder = null,
        };
    }

    static bool EndsWithRegionExit(Ctx ctx, int blockIndex, int targetIndex)
        => EndsWithBranchTo(ctx, blockIndex, targetIndex)
            || IsRegionExitTerminator(ctx, blockIndex);

    static bool IsRegionExitTerminator(Ctx ctx, int blockIndex)
    {
        if (blockIndex < 0 || blockIndex >= ctx.Blocks.Count)
            return false;
        var children = ctx.Blocks[blockIndex].Children;
        return children.Count > 0
            && children[^1] is Leave leave
            && IsRegionExitLeave(ctx, leave);
    }

    static bool IsRegionExitLeave(Ctx ctx, Leave leave)
        => ctx.RegionExitLeaveTarget == leave.TargetOffset;

    static bool EndsWithBranchTo(Ctx ctx, int blockIndex, int targetIndex)
    {
        if (blockIndex < 0 || blockIndex >= ctx.Blocks.Count)
            return false;
        var children = ctx.Blocks[blockIndex].Children;
        return children.Count > 0
            && children[^1] is Branch branch
            && ctx.OffsetToIndex.TryGetValue(branch.TargetOffset, out int branchTarget)
            && branchTarget == targetIndex;
    }

    /// <summary>The diamond join: the false arm's last block ends with a goto past the true arm; null means guard shape.</summary>
    static int? FindDiamondJoin(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int falseStart, int trueStart, int stop)
    {
        if (falseStart >= trueStart)
            return null;
        if (blocks[trueStart - 1].Children.Count == 0)
            return null;
        if (blocks[trueStart - 1].Children[^1] is Branch branch
            && offsetToIndex.TryGetValue(branch.TargetOffset, out int join)
            && join > trueStart && join <= stop)
        {
            return join;
        }
        return null;
    }

    /// <summary>
    /// A block that is a short <c>throw</c>/<c>return</c>-only terminator: at
    /// most <see cref="MaxTerminatorChildren"/> statements, the last a
    /// <see cref="Return"/> or <see cref="Throw"/>, with no control flow among
    /// the rest. Duplicating such a block is always semantics-preserving.
    /// </summary>
    static bool IsTerminatorBlock(Block block)
    {
        int count = block.Children.Count;
        if (count == 0 || count > MaxTerminatorChildren)
            return false;
        if (block.Children[^1] is not (Return or Throw))
            return false;
        for (int s = 0; s < count - 1; s++)
        {
            if (block.Children[s] is Branch or ConditionalBranch or SwitchBranch or Leave or EndFinally or EndFilter)
                return false;
        }
        return true;
    }

    /// <summary>
    /// A terminator block worth inlining into the conditional guard(s) that
    /// reach it: a short <c>throw</c>/<c>return</c>-ending block
    /// (<see cref="IsTerminatorBlock"/>) that no unconditional goto targets (so
    /// inlining erases no needed label). Two cases:
    ///   • <c>throw</c> — only when two or more conditionals reach it: a genuine
    ///     shared throw join strict nesting cannot express. A single
    ///     <c>if (c) throw;</c> already raises cleanly as the standard guard.
    ///   • <c>return</c> — even a single conditional, raising it as a guard
    ///     clause (<c>if (c) { …; return x; }</c>). This is the comparison-tree
    ///     case body the return-merge pass left ending in <c>return</c>: a leaf
    ///     reached only by its one equality test, jumping past its region to the
    ///     (now dissolved) tail. Inlining it is what lets the tree nest at all.
    /// </summary>
    static bool IsSharedTerminator(Block block, HashSet<int> unconditionalTargets, Dictionary<int, int> conditionalTargetCounts, HashSet<int> fallenInto, bool isComparisonTree)
    {
        if (!IsTerminatorBlock(block) || unconditionalTargets.Contains(block.StartOffset))
            return false;
        // A base/this constructor-chain call must stay the body's first statement
        // for the `: base(...)` lift to fire; inlining (which duplicates/nests the
        // block) would strand it as a body statement (CS0175). Leave such a
        // terminator to the standard nested form.
        if (ContainsConstructorChainCall(block))
            return false;
        int conditionalPredecessors = conditionalTargetCounts.GetValueOrDefault(block.StartOffset);
        return block.Children[^1] switch
        {
            // A shared throw join that strict nesting cannot express. Two forms:
            //   • two or more conditionals reach it — a genuine multi-goto join.
            //   • one conditional reaches it AND it is also fallen into — the
            //     `if (A || B) throw;` shape whose inner guard carries a prologue
            //     (so OrChainGuardPass, which needs pure inner guards, cannot
            //     flatten it): `if (A) goto T; <setup>; if (B') goto J; T: throw;`.
            //     Inlining duplicates the throw into the A-guard clause (always
            //     sound for a terminator) and the fallthrough copy stays. A clean
            //     standalone `if (c) throw;` is never fallen into, so it keeps its
            //     standard-guard raising untouched.
            Throw => conditionalPredecessors >= 2
                || (conditionalPredecessors >= 1 && fallenInto.Contains(block.StartOffset)),
            // A shared return-tail merge is NOT inlined here: a return block with
            // two or more conditional predecessors is also exactly the false-exit
            // of an `&&`/`||` short-circuit guard chain (`if (a > 0 && b > 0)
            // return X; return Y;` lowers to two conditionals both targeting the
            // `return Y` block). Inlining the shared return into each guard splits
            // the chain before it can combine into a single `if (a && b)`,
            // changing the recompiled opcodes (the #640 fidelity canary). A
            // genuine shared return-tail merge (String::Trim) needs the combiner
            // to defer to it first — a separate slice, not this terminator rule.
            // A comparison-tree case body: a return leaf reached only by its
            // equality test and never by fallthrough, in a container that is a
            // genuine multi-way tree. Inlining it as a guard clause is what lets
            // the tree nest. Gated to trees so a small selection's return is left
            // to the ternary/boolean passes rather than duplicated into guards.
            Return => isComparisonTree
                && conditionalPredecessors >= 1
                && !fallenInto.Contains(block.StartOffset),
            _ => false,
        };
    }

    /// <summary>
    /// Whether the block contains a base/this constructor-chain call
    /// (<c>Call .ctor</c> on the under-construction <c>this</c>). Such a call
    /// must remain the constructor body's first statement for the later
    /// <c>: base(...)</c>/<c>: this(...)</c> lift; duplicating or nesting it
    /// would leave an invalid bare chain call (CS0175).
    /// </summary>
    static bool ContainsConstructorChainCall(Block block)
    {
        foreach (var node in block.Descendants)
        {
            if (node is Call { Callee: { Name: ".ctor", HasThis: true } } call
                && call.Arguments is [LoadArgument { Index: 0 }, ..])
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>A terminator block that may be inlined into a guard at <paramref name="index"/>.</summary>
    static bool IsInlinableTerminator(Ctx ctx, int index) =>
        IsSharedTerminator(ctx.Blocks[index], ctx.UnconditionalTargets, ctx.ConditionalTargetCounts, ctx.FallenInto, ctx.IsComparisonTree);

    /// <summary>Whether control reaching the end of this block continues into its successor (vs. returning, throwing, or branching away).</summary>
    static bool FallsThrough(Block block) =>
        block.Children.Count == 0
        || block.Children[^1] is not (Return or Throw or Branch or Leave or EndFinally or EndFilter);

    /// <summary>Phase 2: same walk, moving statements into the structured tree. Mirrors Validate exactly; shapes were already proven.</summary>
    static Block BuildRegion(Ctx ctx, int start, int stop, int joinIndex, int? breakTarget, int? continueTarget)
    {
        var blocks = ctx.Blocks;
        var offsetToIndex = ctx.OffsetToIndex;
        var result = new Block(blocks[start].StartOffset);
        int i = start;
        while (i < stop)
        {
            // Mirrors Validate: dropped blocks were already cloned/inlined.
            if (ctx.DroppableBlocks.Contains(i))
            {
                i++;
                continue;
            }
            // An infinite loop head (mirrors Validate): wrap the body
            // (head..latch] in `while (true)`. The latch's back-edge goto is the
            // implicit loop edge, dropped by the body's Branch case below.
            if (continueTarget != i && FindInfiniteLoopShape(ctx, i, stop) is { } latch)
            {
                var loopBody = BuildRegion(ctx, i, latch + 1, joinIndex: latch + 1, breakTarget: latch + 1, continueTarget: i);
                ReplaceRetryLeavesWithContinues(loopBody, blocks[i].StartOffset);
                result.Add(new WhileLoop(TrueLiteral(), loopBody));
                i = latch + 1;
                continue;
            }
            var block = blocks[i];
            if (block.Children.Count == 0)
            {
                i++;
                continue;
            }
            var statements = block.DetachChildren();
            if (ctx.BranchTargets.Contains(block.StartOffset) && statements.Count > 0)
                statements[0].SetSourceOffset(block.StartOffset);
            var last = statements[^1];
            for (int s = 0; s < statements.Count - 1; s++)
                result.Add(statements[s]);
            switch (last)
            {
                case Return or Throw or Break:
                    result.Add(last);
                    i++;
                    break;
                case Leave leave when offsetToIndex.TryGetValue(leave.TargetOffset, out int leaveTarget)
                    && continueTarget == leaveTarget
                    && CanRaiseRetryLeave(leave):
                {
                    var next = new Continue();
                    next.InheritSourceOffset(leave);
                    result.Add(next);
                    i++;
                    break;
                }
                case Leave leave when !offsetToIndex.ContainsKey(leave.TargetOffset):
                    if (IsRegionExitLeave(ctx, leave))
                    {
                        i = stop;
                        break;
                    }
                    // A container-exiting leave (mirrors Validate): emit it as the
                    // path terminator; the printer renders the `goto IL_xxxx`.
                    result.Add(last);
                    i++;
                    break;
                case Branch branch:
                {
                    int branchTarget = offsetToIndex[branch.TargetOffset];
                    if (breakTarget == branchTarget)
                    {
                        result.Add(new Break());
                        i++;
                        break;
                    }
                    // The infinite loop's latch: drop the back-edge goto; the
                    // enclosing WhileLoop carries the loop edge.
                    if (continueTarget == branchTarget && i + 1 == stop)
                    {
                        i = stop;
                        break;
                    }
                    if (FindWhileShape(ctx, i, branchTarget, stop) is { } loop)
                    {
                        var body = BuildRegion(ctx, i + 1, branchTarget, joinIndex: branchTarget, breakTarget: loop.ContinueAt, continueTarget);
                        var condition = BuildWhileCondition(loop);
                        result.Add(new WhileLoop(condition, body));
                        i = loop.ContinueAt;
                        break;
                    }
                    i = stop;  // the region-exit goto disappears into structure
                    break;
                }
                case ConditionalBranch conditional:
                {
                    int target = offsetToIndex[conditional.TargetOffset];
                    var condition = (IrExpression)conditional.DetachChildren()[0];
                    // A conditional branch to the loop exit raises to `if (c) break;`
                    // — the taken path is the break, so the condition is not negated.
                    if (breakTarget == target)
                    {
                        var breakArm = new Block(block.StartOffset);
                        breakArm.Add(new Break());
                        result.Add(new IfStatement(condition, breakArm, null));
                        i++;
                        break;
                    }
                    // Terminator guard: the taken path is the inlined terminator,
                    // so the condition is not negated. T's statements are cloned
                    // from the pre-mutation snapshot.
                    if (target > i && IsInlinableTerminator(ctx, target))
                    {
                        var guardArm = new Block(block.StartOffset);
                        foreach (var statement in ctx.TerminatorSnapshots[target])
                            guardArm.Add(statement.Clone());
                        result.Add(new IfStatement(condition, guardArm, null));
                        i++;
                        break;
                    }
                    int falseStart = i + 1;
                    if (falseStart + 1 == target && IsRegionExitTerminator(ctx, falseStart))
                    {
                        var takenArm = BuildRegion(ctx, target, stop, joinIndex, breakTarget, continueTarget);
                        result.Add(new IfStatement(condition, takenArm, null));
                        i = stop;
                        break;
                    }
                    // A conditional jumping to this region's merge (joinIndex) is
                    // an early exit to the join: raise it as a guard over the rest
                    // of the region, the negated condition selecting the
                    // fallthrough body (mirrors Validate's merge-exit recovery).
                    if (target == joinIndex)
                    {
                        var exitArm = BuildRegion(ctx, i + 1, stop, joinIndex, breakTarget, continueTarget);
                        result.Add(new IfStatement(Negate(condition), exitArm, null));
                        i = stop;
                        break;
                    }
                    if (target > stop
                        && continueTarget is { } loopHead
                        && IsLeaveRetryLoopHead(ctx, loopHead)
                        && TryClonePastRegionTerminator(ctx, target, out var clonedTerminatorArm))
                    {
                        result.Add(new IfStatement(condition, clonedTerminatorArm, null));
                        i++;
                        break;
                    }
                    if (target > stop && TryBuildPastRegionTarget(ctx, target, out var pastRegionArm, out int inlinedStop))
                    {
                        result.Add(new IfStatement(condition, pastRegionArm, null));
                        for (int drop = target; drop < inlinedStop; drop++)
                            ctx.DroppableBlocks.Add(drop);
                        i++;
                        break;
                    }
                    if (FindRegionExitDiamond(ctx, falseStart, target, stop, joinIndex) is { } exitDiamond)
                    {
                        var thenArm = BuildRegion(ctx, falseStart, target, joinIndex: exitDiamond.ExitTarget, breakTarget, continueTarget);
                        var elseArm = BuildRegion(ctx, target, exitDiamond.LocalJoin, joinIndex: exitDiamond.ExitTarget, breakTarget, continueTarget);
                        result.Add(new IfStatement(Negate(condition), thenArm, elseArm));
                        i = exitDiamond.LocalJoin;
                        break;
                    }
                    if (FindDiamondJoin(blocks, offsetToIndex, falseStart, target, stop) is { } join)
                    {
                        // Fallthrough arm first, current-emitter guard style:
                        // the negated condition selects it.
                        var thenArm = BuildRegion(ctx, falseStart, target, joinIndex: join, breakTarget, continueTarget);
                        var elseArm = BuildRegion(ctx, target, join, joinIndex: join, breakTarget, continueTarget);
                        result.Add(new IfStatement(Negate(condition), thenArm, elseArm));
                        i = join;
                        break;
                    }
                    var arm = BuildRegion(ctx, falseStart, target, joinIndex: target, breakTarget, continueTarget);
                    result.Add(new IfStatement(Negate(condition), arm, null));
                    i = target;
                    break;
                }
                default:
                    result.Add(last);
                    i++;
                    break;
            }
        }
        return result;
    }

    static void ReplaceRetryLeavesWithContinues(IrNode root, int targetOffset)
    {
        foreach (var leave in root.Descendants.OfType<Leave>()
            .Where(leave => leave.TargetOffset == targetOffset && CanRaiseRetryLeave(leave))
            .ToList())
        {
            var next = new Continue();
            next.InheritSourceOffset(leave);
            leave.ReplaceWith(next);
        }
    }

    static IrExpression BuildWhileCondition(WhileShape loop)
    {
        var backCondition = (IrExpression)loop.BackBranch.DetachChildren()[0];
        if (loop.ExitBranch is null)
            return backCondition;

        var exitCondition = (IrExpression)loop.ExitBranch.DetachChildren()[0];
        return new LogicalBinary(LogicalKind.And, Negate(exitCondition), backCondition);
    }

    /// <summary>Negation delegates to the shared type-aware duals (see <see cref="Conditions"/>).</summary>
    static IrExpression Negate(IrExpression condition) => Conditions.Negate(condition);

    static int Depth(IrNode node)
    {
        int depth = 0;
        for (var current = node.Parent; current is not null; current = current.Parent)
            depth++;
        return depth;
    }

    static int? NormalEhContinuationTarget(BlockContainer container)
    {
        // Row #1089/5's conservative first cut: only the try body of a recovered
        // finally. Try/catch async-dispose scaffolds keep their flat evidence for
        // UsingStatementPass and later EH-specific rows can widen this gate.
        IrNode? construct = container.Parent switch
        {
            TryFinally tryFinally when ReferenceEquals(tryFinally.TryBody, container) => tryFinally,
            _ => null,
        };
        if (construct?.Parent is not Block holder || holder.Parent is not BlockContainer outer)
            return null;

        var blocks = outer.Blocks;
        return holder.ChildIndex + 1 < blocks.Count
            ? blocks[holder.ChildIndex + 1].StartOffset
            : null;
    }
}
