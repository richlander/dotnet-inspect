using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Raises flat exception regions into <see cref="TryCatch"/> and
/// <see cref="TryFinally"/> statements. Two-phase and transactional like
/// <see cref="StructuringPass"/>: the whole function is validated against the
/// slice before any mutation, so a function either raises completely or keeps
/// the always-correct flat form with <see cref="IrFunction.Regions"/> intact.
/// On success the regions clear — the nesting in the tree is then the truth.
///
/// The slice: catch and finally handlers (filter and fault stay flat), every
/// <c>leave</c> targeting the continuation of an enclosing construct, every
/// branch staying inside its region segment, and <c>endfinally</c> closing
/// its handler from the final block only. Bodies become nested containers,
/// which the later structuring pass raises independently — a goto-heavy try
/// body stays honestly flat inside a structured try/catch shell.
/// </summary>
public sealed class EhStructuringPass : IIrPass
{
    public string Name => "eh-structuring";

    /// <summary>One try with its contiguous handlers: a TryCatch's clauses share the protected range; a finally is always sole.</summary>
    sealed class Construct
    {
        public required int TryStart { get; init; }
        public required int TryEnd { get; init; }
        public required List<HandlerRegion> Handlers { get; init; }
        public int End => Handlers[^1].HandlerOffset + Handlers[^1].HandlerLength;
        public List<Construct> Children { get; } = [];

        public bool Contains(int offset) => offset >= TryStart && offset < End;
        public bool Contains(Construct other) => other.TryStart >= TryStart && other.End <= End;
        public bool IsFinally => Handlers is [{ Kind: HandlerKind.Finally }];
    }

    public void Run(IrFunction function)
    {
        if (function.Regions.IsEmpty)
            return;
        if (function.Regions.Any(r => r.Kind is HandlerKind.Filter or HandlerKind.Fault))
            return;
        if (function.Regions.Any(r => r.Kind is HandlerKind.Catch && r.CatchType is null))
            return;

        var blocks = function.Body.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        if (BuildForest(function.Regions, offsetToIndex) is not { } forest)
            return;
        if (!Validate(blocks, forest.All))
            return;

        function.Body.DetachChildren();
        var continuations = new Dictionary<IrNode, int>();
        var rebuilt = BuildContainer(blocks, 0, blocks.Count, forest.Roots, offsetToIndex, continuations);
        TrimTailLeaves(rebuilt, continuations);
        function.Body.ReplaceWith(rebuilt);
        function.Regions = [];
    }

    /// <summary>
    /// A leave in tail position falls through instead of printing a goto:
    /// walking up from the leave, every level sits flush at its container's
    /// end, so falling out of the construct chain reaches the same
    /// continuation the leave names — including the intervening finallys,
    /// exactly the leave's semantics.
    /// </summary>
    static void TrimTailLeaves(BlockContainer root, Dictionary<IrNode, int> continuations)
    {
        foreach (var leave in root.Descendants.OfType<Leave>().ToList())
        {
            IrNode statement = leave;
            while (true)
            {
                if (statement.Parent is not Block block || !ReferenceEquals(block.Children[^1], statement))
                    break;
                if (block.Parent is not BlockContainer container || !IsTailBlock(container, block))
                    break;
                // Falling out of this container exits which construct?
                // (Never a finally body: leave inside one fails validation.)
                IrNode? construct = container.Parent switch
                {
                    TryCatch tryCatch when ReferenceEquals(tryCatch.TryBody, container) => tryCatch,
                    CatchClause clause => clause.Parent,
                    TryFinally tryFinally when ReferenceEquals(tryFinally.TryBody, container) => tryFinally,
                    _ => null,
                };
                if (construct is null || !continuations.TryGetValue(construct, out int end))
                    break;
                if (leave.TargetOffset == end)
                {
                    leave.Detach();
                    break;
                }
                statement = construct;
            }
        }
    }

    /// <summary>Last block of its container, ignoring trailing nop landing pads.</summary>
    static bool IsTailBlock(BlockContainer container, Block block)
    {
        for (int i = block.ChildIndex + 1; i < container.Children.Count; i++)
        {
            if (container.Children[i].Children.Count > 0)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Groups regions sharing a protected range into constructs and nests
    /// them by containment. Null when the shapes are outside the slice:
    /// non-contiguous handlers, mixed handler kinds on one range, partial
    /// overlap, or boundaries that are not block leaders.
    /// </summary>
    static (List<Construct> Roots, List<Construct> All)? BuildForest(
        ImmutableArray<HandlerRegion> regions, Dictionary<int, int> offsetToIndex)
    {
        var all = new List<Construct>();
        foreach (var group in regions.GroupBy(r => (r.TryOffset, r.TryLength)))
        {
            var handlers = group.OrderBy(r => r.HandlerOffset).ToList();
            if (handlers.Count > 1 && handlers.Any(h => h.Kind != HandlerKind.Catch))
                return null;
            var construct = new Construct
            {
                TryStart = group.Key.TryOffset,
                TryEnd = group.Key.TryOffset + group.Key.TryLength,
                Handlers = handlers,
            };
            int expected = construct.TryEnd;
            foreach (var handler in handlers)
            {
                if (handler.HandlerOffset != expected)
                    return null;
                expected = handler.HandlerOffset + handler.HandlerLength;
            }
            if (!offsetToIndex.ContainsKey(construct.TryStart)
                || !offsetToIndex.ContainsKey(construct.TryEnd)
                || handlers.Any(h => !offsetToIndex.ContainsKey(h.HandlerOffset)))
            {
                return null;
            }
            all.Add(construct);
        }

        // Outer-first ordering, then a containment stack builds the forest.
        all.Sort((a, b) => a.TryStart != b.TryStart ? a.TryStart - b.TryStart : b.End - a.End);
        var roots = new List<Construct>();
        var stack = new Stack<Construct>();
        foreach (var construct in all)
        {
            while (stack.Count > 0 && !stack.Peek().Contains(construct))
            {
                if (construct.TryStart < stack.Peek().End)
                    return null;
                stack.Pop();
            }
            if (stack.Count > 0)
            {
                // A nested construct must sit wholly inside the parent's try
                // or wholly inside one handler — never straddle.
                var parent = stack.Peek();
                bool placed = construct.End <= parent.TryEnd && construct.TryStart >= parent.TryStart
                    || parent.Handlers.Any(h =>
                        construct.TryStart >= h.HandlerOffset
                        && construct.End <= h.HandlerOffset + h.HandlerLength);
                if (!placed)
                    return null;
                parent.Children.Add(construct);
            }
            else
            {
                roots.Add(construct);
            }
            stack.Push(construct);
        }
        return (roots, all);
    }

    /// <summary>Phase 1: pure checks over the flat blocks — no mutation until the whole function fits the slice.</summary>
    static bool Validate(IReadOnlyList<Block> blocks, List<Construct> all)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            int offset = block.StartOffset;
            for (int s = 0; s < block.Children.Count; s++)
            {
                var statement = block.Children[s];
                bool isLast = s == block.Children.Count - 1;
                switch (statement)
                {
                    case Leave leave:
                    {
                        if (!isLast)
                            return false;
                        // leave exits to the continuation of an enclosing
                        // construct — possibly through several (it runs the
                        // intervening finallys, exactly C# goto-out-of-try).
                        bool enclosed = false, targetsContinuation = false;
                        foreach (var construct in all)
                        {
                            if (!construct.Contains(offset))
                                continue;
                            enclosed = true;
                            if (construct.IsFinally && offset >= construct.TryEnd)
                                return false;  // leave out of a finally is not valid IL
                            if (leave.TargetOffset == construct.End)
                                targetsContinuation = true;
                        }
                        if (!enclosed || !targetsContinuation)
                            return false;
                        break;
                    }
                    case EndFinally:
                    {
                        // Exactly the canonical close: last statement of the
                        // final block of its finally range.
                        if (!isLast)
                            return false;
                        var owner = all.FirstOrDefault(c =>
                            c.IsFinally && offset >= c.TryEnd && offset < c.End);
                        if (owner is null)
                            return false;
                        if (i + 1 < blocks.Count && blocks[i + 1].StartOffset < owner.End)
                            return false;
                        break;
                    }
                    case EndFilter:
                        return false;
                    case Branch branch:
                        if (!SameZone(all, offset, branch.TargetOffset))
                            return false;
                        break;
                    case ConditionalBranch conditional:
                        if (!SameZone(all, offset, conditional.TargetOffset))
                            return false;
                        break;
                    case SwitchBranch sw:
                        if (sw.TargetOffsets.Any(t => !SameZone(all, offset, t)))
                            return false;
                        break;
                }
            }
        }
        return ValidateCaughtExceptions(blocks, all);
    }

    /// <summary>
    /// The caught exception may appear exactly two ways: the handler-entry
    /// consumption (a store the transform folds into the clause header, or
    /// the discard pop) and the bare rethrow. Anything else — the value
    /// flowing across blocks, inline consumption — is outside the slice.
    /// </summary>
    static bool ValidateCaughtExceptions(IReadOnlyList<Block> blocks, List<Construct> all)
    {
        foreach (var block in blocks)
        {
            var handler = InnermostCatchHandler(all, block.StartOffset);
            for (int s = 0; s < block.Children.Count; s++)
            {
                var statement = block.Children[s];
                bool isEntry = handler is not null && block.StartOffset == handler.HandlerOffset && s == 0;
                bool entryConsumption = isEntry && statement switch
                {
                    StoreLocal { Value: CaughtException } => true,
                    ExpressionStatement { Expression: CaughtException } => true,
                    _ => false,
                };
                if (entryConsumption)
                    continue;
                if (statement is Throw { Value: CaughtException { Type: null } } && handler is not null)
                    continue;  // rethrow
                foreach (var node in statement.Descendants.Prepend(statement))
                {
                    if (node is CaughtException)
                        return false;
                }
            }
        }
        return true;
    }

    static HandlerRegion? InnermostCatchHandler(List<Construct> all, int offset)
    {
        Construct? best = null;
        HandlerRegion? bestHandler = null;
        foreach (var construct in all)
        {
            foreach (var handler in construct.Handlers)
            {
                if (handler.Kind != HandlerKind.Catch
                    || offset < handler.HandlerOffset
                    || offset >= handler.HandlerOffset + handler.HandlerLength)
                {
                    continue;
                }
                if (best is null || best.Contains(construct))
                {
                    best = construct;
                    bestHandler = handler;
                }
            }
        }
        return bestHandler;
    }

    /// <summary>
    /// True when both offsets sit in the same segment of the same innermost
    /// construct — branches never cross a region boundary in the slice.
    /// </summary>
    static bool SameZone(List<Construct> all, int offsetA, int offsetB)
        => Zone(all, offsetA) == Zone(all, offsetB);

    static (Construct? Construct, int Segment) Zone(List<Construct> all, int offset)
    {
        Construct? best = null;
        foreach (var construct in all)
        {
            if (construct.Contains(offset) && (best is null || best.Contains(construct)))
                best = construct;
        }
        if (best is null)
            return (null, -2);
        if (offset < best.TryEnd)
            return (best, -1);
        for (int h = 0; h < best.Handlers.Count; h++)
        {
            var handler = best.Handlers[h];
            if (offset >= handler.HandlerOffset && offset < handler.HandlerOffset + handler.HandlerLength)
                return (best, h);
        }
        return (best, -2);
    }

    /// <summary>Phase 2: rebuilds a block range as a container, nesting each construct into its statement node. Shapes were already proven.</summary>
    static BlockContainer BuildContainer(
        IReadOnlyList<Block> blocks, int startIndex, int endIndex,
        List<Construct> constructs, Dictionary<int, int> offsetToIndex,
        Dictionary<IrNode, int> continuations)
    {
        var container = new BlockContainer();
        int i = startIndex;
        foreach (var construct in constructs.OrderBy(c => c.TryStart))
        {
            for (; i < offsetToIndex[construct.TryStart]; i++)
                container.Add(blocks[i]);
            var holder = new Block(construct.TryStart);
            holder.Add(BuildConstruct(blocks, construct, offsetToIndex, endIndex, continuations));
            container.Add(holder);
            i = offsetToIndex.TryGetValue(construct.End, out int continuation) ? continuation : endIndex;
        }
        for (; i < endIndex; i++)
            container.Add(blocks[i]);
        return container;
    }

    static IrNode BuildConstruct(
        IReadOnlyList<Block> blocks, Construct construct,
        Dictionary<int, int> offsetToIndex, int sliceEnd,
        Dictionary<IrNode, int> continuations)
    {
        var inTry = construct.Children.Where(c => c.End <= construct.TryEnd).ToList();
        var tryBody = BuildContainer(
            blocks, offsetToIndex[construct.TryStart], offsetToIndex[construct.TryEnd], inTry, offsetToIndex, continuations);

        IrNode node;
        if (construct.IsFinally)
        {
            var handler = construct.Handlers[0];
            var finallyBody = BuildHandlerBody(blocks, construct, handler, offsetToIndex, sliceEnd, continuations);
            TrimTrailingEndFinally(finallyBody);
            node = new TryFinally(tryBody, finallyBody);
        }
        else
        {
            var clauses = new List<CatchClause>();
            foreach (var handler in construct.Handlers)
            {
                var body = BuildHandlerBody(blocks, construct, handler, offsetToIndex, sliceEnd, continuations);
                int? variable = FoldEntryConsumption(body);
                clauses.Add(new CatchClause(handler.CatchType!, body) { VariableIndex = variable });
            }
            node = new TryCatch(tryBody, clauses);
        }
        continuations[node] = construct.End;
        return node;
    }

    static BlockContainer BuildHandlerBody(
        IReadOnlyList<Block> blocks, Construct construct, HandlerRegion handler,
        Dictionary<int, int> offsetToIndex, int sliceEnd,
        Dictionary<IrNode, int> continuations)
    {
        int handlerEnd = handler.HandlerOffset + handler.HandlerLength;
        int endIndex = offsetToIndex.TryGetValue(handlerEnd, out int index) ? index : sliceEnd;
        var children = construct.Children
            .Where(c => c.TryStart >= handler.HandlerOffset && c.End <= handlerEnd)
            .ToList();
        return BuildContainer(blocks, offsetToIndex[handler.HandlerOffset], endIndex, children, offsetToIndex, continuations);
    }

    static void TrimTrailingEndFinally(BlockContainer body)
    {
        if (body.Blocks is [.., var last] && last.Children is [.., EndFinally end])
            end.Detach();
    }

    /// <summary>Folds the handler-entry store into the clause's variable; the discard pop just disappears.</summary>
    static int? FoldEntryConsumption(BlockContainer body)
    {
        if (body.Blocks is not [var first, ..] || first.Children.Count == 0)
            return null;
        switch (first.Children[0])
        {
            case StoreLocal { Value: CaughtException } store:
                store.Detach();
                return store.Index;
            case ExpressionStatement { Expression: CaughtException } discard:
                discard.Detach();
                return null;
            default:
                return null;
        }
    }
}
