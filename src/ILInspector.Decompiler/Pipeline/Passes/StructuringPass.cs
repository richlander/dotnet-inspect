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

    /// <summary>
    /// Per-container facts precomputed before any mutation: the block list and
    /// offset map, the offsets reached by an unconditional <c>goto</c> (so an
    /// inlined terminator never erases a label some goto still needs), the
    /// terminator blocks whose only predecessors are inlined guards (dropped
    /// from the linear walk), and a snapshot of each inlinable terminator's
    /// statements (taken before <see cref="BuildRegion"/> detaches anything,
    /// so the clone source survives mutation order).
    /// </summary>
    sealed class Ctx
    {
        public required IReadOnlyList<Block> Blocks { get; init; }
        public required Dictionary<int, int> OffsetToIndex { get; init; }
        public required HashSet<int> UnconditionalTargets { get; init; }
        public required Dictionary<int, int> ConditionalTargetCounts { get; init; }
        public required HashSet<int> DroppableTerminators { get; init; }
        public required Dictionary<int, IReadOnlyList<IrNode>> TerminatorSnapshots { get; init; }
        public required HashSet<int> FallenInto { get; init; }
        public required bool IsComparisonTree { get; init; }
    }

    public void Run(IrFunction function, PassContext context)
    {
        if (!function.Regions.IsEmpty)
            return;  // unconsumed regions: the flat form is still the truth
        // Surviving leaves are the one cross-container goto (an early exit
        // through outer constructs); their target blocks must keep printing
        // a label, so their containers stay flat.
        var leaveTargets = function.Descendants.OfType<Leave>()
            .Select(leave => leave.TargetOffset)
            .ToHashSet();
        // Containers are independent regions: the function body plus every
        // try/catch/finally body the EH pass nested. Each structures (or
        // stays flat) on its own — a goto-heavy handler does not flatten
        // the rest of the method.
        foreach (var container in function.Descendants.OfType<BlockContainer>().ToList())
            Structure(container, leaveTargets, context);
    }

    static void Structure(BlockContainer container, HashSet<int> leaveTargets, PassContext context)
    {
        var blocks = container.Blocks;
        if (blocks.Count <= 1)
            return;
        if (leaveTargets.Count > 0 && blocks.Any(b => leaveTargets.Contains(b.StartOffset)))
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
        foreach (var block in blocks)
        {
            foreach (var child in block.Children)
            {
                if (child is Branch branch)
                    unconditionalTargets.Add(branch.TargetOffset);
                else if (child is ConditionalBranch conditional)
                    conditionalTargetCounts[conditional.TargetOffset] =
                        conditionalTargetCounts.GetValueOrDefault(conditional.TargetOffset) + 1;
            }
        }

        // A terminator whose only predecessors are inlined guards (no goto
        // targets it and the preceding block does not fall into it) becomes
        // dead once its guards inline — drop it from the walk. Snapshot every
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

        var ctx = new Ctx
        {
            Blocks = blocks,
            OffsetToIndex = offsetToIndex,
            UnconditionalTargets = unconditionalTargets,
            ConditionalTargetCounts = conditionalTargetCounts,
            DroppableTerminators = droppable,
            TerminatorSnapshots = snapshots,
            FallenInto = fallenInto,
            IsComparisonTree = isComparisonTree,
        };

        if (!Validate(ctx, 0, blocks.Count, joinIndex: blocks.Count, breakTarget: null))
            return;

        context.Stepper.StepOver(
            $"structure container at IL_{blocks[0].StartOffset:X4} ({blocks.Count} blocks) into nested if/diamond regions",
            container);

        var structured = BuildRegion(ctx, 0, blocks.Count, joinIndex: blocks.Count, breakTarget: null);
        var replacement = new BlockContainer();
        replacement.Add(structured);
        container.ReplaceWith(replacement);
    }

    /// <summary>
    /// Phase 1: pure shape check over block indices — no mutation until the
    /// whole function fits the slice. <paramref name="breakTarget"/> is the
    /// block index of the enclosing loop's exit (null outside a loop body): a
    /// forward branch there is a <c>break</c>, in or out of nested ifs.
    /// </summary>
    static bool Validate(Ctx ctx, int start, int stop, int joinIndex, int? breakTarget)
    {
        var blocks = ctx.Blocks;
        var offsetToIndex = ctx.OffsetToIndex;
        int i = start;
        while (i < stop)
        {
            // A terminator left dead by inlining its guards prints nothing.
            if (ctx.DroppableTerminators.Contains(i))
            {
                i++;
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
                    return false;  // mid-block control flow is outside the slice
            }
            switch (block.Children[^1])
            {
                case Return or Throw or Break:
                    // A straight-line terminator: the loop pass already raised
                    // the break, so this block ends its region cleanly.
                    i++;
                    break;
                case Leave or EndFinally or EndFilter:
                    // Survivors of the EH pass (an early exit through outer
                    // constructs) keep their container flat: structure would
                    // erase the label their goto needs.
                    return false;
                case Branch branch:
                {
                    if (!offsetToIndex.TryGetValue(branch.TargetOffset, out int branchTarget))
                        return false;
                    // An unconditional branch to the enclosing loop's exit is a break.
                    if (breakTarget == branchTarget)
                    {
                        i++;
                        break;
                    }
                    // csc's guarded while: br COND; BODY...; COND: brtrue BODY.
                    if (FindWhileShape(blocks, offsetToIndex, i, branchTarget, stop) is { } loop)
                    {
                        // The body's breaks target the block after the loop.
                        if (!Validate(ctx, i + 1, branchTarget, joinIndex: branchTarget, breakTarget: loop.ContinueAt))
                            return false;
                        i = loop.ContinueAt;
                        break;
                    }
                    // Otherwise only the region-exit goto is in the slice; it
                    // must be the region's last block.
                    if (branchTarget != joinIndex || i + 1 != stop)
                        return false;
                    i = stop;
                    break;
                }
                case ConditionalBranch conditional:
                {
                    if (!offsetToIndex.TryGetValue(conditional.TargetOffset, out int target))
                        return false;
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
                    if (target <= i || target > stop)
                        return false;  // backward = loop (later slice); past region = out of slice
                    // Guard: if (c) goto M with M ending this region's view.
                    // Diamond: the fallthrough arm ends with goto M past the
                    // true arm.
                    int falseStart = i + 1;
                    if (FindDiamondJoin(blocks, offsetToIndex, falseStart, target, stop) is { } join)
                    {
                        // False arm exits by goto join; true arm falls (or
                        // returns) into join.
                        if (!Validate(ctx, falseStart, target, joinIndex: join, breakTarget)
                            || !Validate(ctx, target, join, joinIndex: join, breakTarget))
                        {
                            return false;
                        }
                        i = join;
                        break;
                    }
                    // Guard form: arm is (i+1, target), continues at target.
                    if (!Validate(ctx, falseStart, target, joinIndex: target, breakTarget))
                        return false;
                    i = target;
                    break;
                }
                default:
                    // A block that falls through to its successor.
                    if (i + 1 >= stop && stop != joinIndex)
                        return false;
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
    static (int ContinueAt, ConditionalBranch BackBranch)? FindWhileShape(
        IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int i, int conditionIndex, int stop)
    {
        if (conditionIndex <= i + 1 || conditionIndex >= stop)
            return null;
        var conditionBlock = blocks[conditionIndex];
        if (conditionBlock.Children.Count != 1
            || conditionBlock.Children[0] is not ConditionalBranch backBranch
            || !offsetToIndex.TryGetValue(backBranch.TargetOffset, out int bodyStart)
            || bodyStart != i + 1)
        {
            return null;
        }
        return (conditionIndex + 1, backBranch);
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
        int conditionalPredecessors = conditionalTargetCounts.GetValueOrDefault(block.StartOffset);
        return block.Children[^1] switch
        {
            Throw => conditionalPredecessors >= 2,
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

    /// <summary>A terminator block that may be inlined into a guard at <paramref name="index"/>.</summary>
    static bool IsInlinableTerminator(Ctx ctx, int index) =>
        IsSharedTerminator(ctx.Blocks[index], ctx.UnconditionalTargets, ctx.ConditionalTargetCounts, ctx.FallenInto, ctx.IsComparisonTree);

    /// <summary>Whether control reaching the end of this block continues into its successor (vs. returning, throwing, or branching away).</summary>
    static bool FallsThrough(Block block) =>
        block.Children.Count == 0
        || block.Children[^1] is not (Return or Throw or Branch or Leave or EndFinally or EndFilter);

    /// <summary>Phase 2: same walk, moving statements into the structured tree. Mirrors Validate exactly; shapes were already proven.</summary>
    static Block BuildRegion(Ctx ctx, int start, int stop, int joinIndex, int? breakTarget)
    {
        var blocks = ctx.Blocks;
        var offsetToIndex = ctx.OffsetToIndex;
        var result = new Block(blocks[start].StartOffset);
        int i = start;
        while (i < stop)
        {
            if (ctx.DroppableTerminators.Contains(i))
            {
                i++;
                continue;
            }
            var block = blocks[i];
            if (block.Children.Count == 0)
            {
                i++;
                continue;
            }
            var statements = block.DetachChildren();
            var last = statements[^1];
            for (int s = 0; s < statements.Count - 1; s++)
                result.Add(statements[s]);
            switch (last)
            {
                case Return or Throw or Break:
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
                    if (FindWhileShape(blocks, offsetToIndex, i, branchTarget, stop) is { } loop)
                    {
                        var body = BuildRegion(ctx, i + 1, branchTarget, joinIndex: branchTarget, breakTarget: loop.ContinueAt);
                        var condition = (IrExpression)loop.BackBranch.DetachChildren()[0];
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
                    if (FindDiamondJoin(blocks, offsetToIndex, falseStart, target, stop) is { } join)
                    {
                        // Fallthrough arm first, current-emitter guard style:
                        // the negated condition selects it.
                        var thenArm = BuildRegion(ctx, falseStart, target, joinIndex: join, breakTarget);
                        var elseArm = BuildRegion(ctx, target, join, joinIndex: join, breakTarget);
                        result.Add(new IfStatement(Negate(condition), thenArm, elseArm));
                        i = join;
                        break;
                    }
                    var arm = BuildRegion(ctx, falseStart, target, joinIndex: target, breakTarget);
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

    /// <summary>Negation delegates to the shared type-aware duals (see <see cref="Conditions"/>).</summary>
    static IrExpression Negate(IrExpression condition) => Conditions.Negate(condition);
}
