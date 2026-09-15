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
/// The slice: catch and finally handlers, plus narrow generated filter shapes
/// that can be re-spelled as C# <c>catch when</c> clauses. Unsupported filters
/// and faults stay flat. Every <c>leave</c> must target the continuation of an
/// enclosing construct, every branch must stay inside its region segment, and
/// <c>endfinally</c>/<c>endfilter</c> must close its handler/filter from the final
/// block only. Bodies become nested
/// containers, which the later structuring pass raises independently — a
/// goto-heavy try body stays honestly flat inside a structured try/catch shell.
/// </summary>
public sealed partial class EhStructuringPass : IIrPass
{
    public string Name => "eh-structuring";
    static TypeRef CatchAllType => TypeRef.CoreLib("System", "Object");

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

    public void Run(IrFunction function, PassContext context)
    {
        if (function.Regions.IsEmpty)
            return;
        if (function.Regions.Any(r => r.Kind is HandlerKind.Fault))
            return;

        var blocks = function.Body.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        if (BuildForest(function.Regions, offsetToIndex) is not { } forest)
            return;
        if (!Validate(blocks, forest.All, offsetToIndex))
            return;
        if (!ValidateFilters(function, blocks, forest.All, offsetToIndex))
            return;

        function.Body.DetachChildren();
        var continuations = new Dictionary<IrNode, int>();
        var rebuilt = BuildContainer(function, blocks, 0, blocks.Count, forest.Roots, offsetToIndex, continuations);
        TrimTailLeaves(rebuilt, continuations);
        InlineReturnLeaves(rebuilt);
        SynthesizeInlineCatchVariables(function, rebuilt);
        context.Stepper.StepOver("raise exception regions into try/catch/finally", function.Body);
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
            if (handlers.Count > 1 && handlers.Any(h => h.Kind is not (HandlerKind.Catch or HandlerKind.Filter)))
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
                if (handler.Kind == HandlerKind.Filter)
                {
                    if (handler.FilterOffset != expected
                        || handler.HandlerOffset <= handler.FilterOffset
                        || !offsetToIndex.ContainsKey(handler.FilterOffset))
                    {
                        return null;
                    }
                    expected = handler.HandlerOffset;
                }
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
    static bool Validate(IReadOnlyList<Block> blocks, List<Construct> all, Dictionary<int, int> offsetToIndex)
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
                        if (!enclosed)
                            return false;
                        // A leave targets either an enclosing construct's
                        // continuation (trimmed or printed as a goto) or a
                        // one-statement return/throw block — the multi-return
                        // idiom the build phase inlines back into the body.
                        if (!targetsContinuation
                            && !TargetsOutsideContainingConstructs(all, offset, leave.TargetOffset)
                            && !TargetsWithinEnclosingSegment(blocks, all, offsetToIndex, offset, leave.TargetOffset)
                            && !IsInlineableReturn(blocks, offsetToIndex, leave.TargetOffset))
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
                    {
                        if (!isLast)
                            return false;
                        var owner = all.FirstOrDefault(c =>
                            c.Handlers.Any(h => h.Kind == HandlerKind.Filter
                                && offset >= h.FilterOffset
                                && offset < h.HandlerOffset));
                        if (owner is null)
                            return false;
                        var handler = owner.Handlers.Single(h => h.Kind == HandlerKind.Filter
                            && offset >= h.FilterOffset
                            && offset < h.HandlerOffset);
                        if (i + 1 < blocks.Count && blocks[i + 1].StartOffset < handler.HandlerOffset)
                            return false;
                        break;
                    }
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
        // Inline (non-entry, non-rethrow) value uses of the caught exception, by
        // handler. A handler that uses the exception inline must use it exactly once
        // for SynthesizeInlineCatchVariables to bind a variable opcode-exactly; see
        // its remarks for why two or more cannot round-trip.
        var inlineUses = new Dictionary<int, int>();
        foreach (var block in blocks)
        {
            if (InsideFilter(all, block.StartOffset))
                continue;

            var handler = InnermostCatchHandler(all, block.StartOffset);
            for (int s = 0; s < block.Children.Count; s++)
            {
                var statement = block.Children[s];
                bool isEntry = handler is not null && block.StartOffset == handler.HandlerOffset && s == 0;
                bool catchAll = handler is { Kind: HandlerKind.Catch, CatchType: null };
                bool entryDiscard = isEntry && statement is ExpressionStatement { Expression: CaughtException };
                if (entryDiscard)
                    continue;
                bool entryStore = isEntry && statement is StoreLocal { Value: CaughtException };
                if (entryStore)
                {
                    if (catchAll)
                        return false;
                    continue;
                }
                if (statement is Throw { Value: CaughtException { Type: null } } && handler is not null)
                    continue;  // rethrow
                foreach (var node in statement.Descendants.Prepend(statement))
                {
                    if (node is not CaughtException)
                        continue;
                    // A CaughtException outside any catch handler cannot be spelled.
                    if (handler is null)
                        return false;
                    if (catchAll)
                        return false;
                    inlineUses[handler.HandlerOffset] = inlineUses.GetValueOrDefault(handler.HandlerOffset) + 1;
                }
            }
        }
        // Two or more inline uses in one handler only come from a duped exception in
        // hand-written/obfuscated IL; binding a variable would re-introduce a store
        // the original lacked. Leave such a method flat rather than mis-raise it.
        if (inlineUses.Values.Any(count => count > 1))
            return false;
        return true;
    }

    static bool InsideFilter(List<Construct> all, int offset)
        => all.SelectMany(c => c.Handlers).Any(h =>
            h.Kind == HandlerKind.Filter && offset >= h.FilterOffset && offset < h.HandlerOffset);

    static HandlerRegion? InnermostCatchHandler(List<Construct> all, int offset)
    {
        Construct? best = null;
        HandlerRegion? bestHandler = null;
        foreach (var construct in all)
        {
            foreach (var handler in construct.Handlers)
            {
                bool insideCatch = handler.Kind == HandlerKind.Catch
                    && offset >= handler.HandlerOffset
                    && offset < handler.HandlerOffset + handler.HandlerLength;
                bool insideFilter = handler.Kind == HandlerKind.Filter
                    && offset >= handler.FilterOffset
                    && offset < handler.HandlerOffset + handler.HandlerLength;
                if (!insideCatch && !insideFilter)
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
    /// True when a leave exits every EH construct containing <paramref name="offset"/>
    /// and lands on an ordinary block outside them. C# can spell this as a
    /// <c>goto</c> out of a <c>try</c>; the runtime still runs the intervening
    /// finally blocks, preserving the original <c>leave</c> semantics. This is the
    /// retry-loop shape in helpers such as <c>Interop.Sys.GetCwd</c>.
    /// </summary>
    static bool TargetsOutsideContainingConstructs(List<Construct> all, int offset, int targetOffset)
    {
        bool enclosed = false;
        foreach (var construct in all)
        {
            if (!construct.Contains(offset))
                continue;
            enclosed = true;
            if (construct.Contains(targetOffset))
                return false;
        }
        return enclosed;
    }

    /// <summary>
    /// True when a leave exits an inner construct but lands in the same try/catch
    /// segment of an enclosing construct, without entering a sibling region. C#
    /// can spell this as a goto to a label after/before the nested try statement.
    /// </summary>
    static bool TargetsWithinEnclosingSegment(
        IReadOnlyList<Block> blocks,
        List<Construct> all,
        Dictionary<int, int> offsetToIndex,
        int offset,
        int targetOffset)
    {
        if (!offsetToIndex.TryGetValue(targetOffset, out int targetIndex)
            || !HasPrintableLandingStatement(blocks[targetIndex]))
        {
            return false;
        }

        var source = Zone(all, offset);
        var target = Zone(all, targetOffset);
        if (source.Construct is null
            || target.Construct is null
            || ReferenceEquals(source.Construct, target.Construct)
            || source.Construct.Contains(targetOffset)
            || !target.Construct.Contains(offset))
        {
            return false;
        }

        return SegmentWithin(target.Construct, offset) == target.Segment;
    }

    static bool HasPrintableLandingStatement(Block block)
        => block.Children.Any(statement => statement is not Leave);

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

    static int SegmentWithin(Construct construct, int offset)
    {
        if (offset >= construct.TryStart && offset < construct.TryEnd)
            return -1;
        for (int h = 0; h < construct.Handlers.Count; h++)
        {
            var handler = construct.Handlers[h];
            if (offset >= handler.HandlerOffset && offset < handler.HandlerOffset + handler.HandlerLength)
                return h;
        }
        return -2;
    }

    /// <summary>Phase 2: rebuilds a block range as a container, nesting each construct into its statement node. Shapes were already proven.</summary>
    static BlockContainer BuildContainer(
        IrFunction function,
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
            holder.Add(BuildConstruct(function, blocks, construct, offsetToIndex, endIndex, continuations));
            container.Add(holder);
            i = offsetToIndex.TryGetValue(construct.End, out int continuation) ? continuation : endIndex;
        }
        for (; i < endIndex; i++)
            container.Add(blocks[i]);
        return container;
    }

    static IrNode BuildConstruct(
        IrFunction function,
        IReadOnlyList<Block> blocks, Construct construct,
        Dictionary<int, int> offsetToIndex, int sliceEnd,
        Dictionary<IrNode, int> continuations)
    {
        var inTry = construct.Children.Where(c => c.End <= construct.TryEnd).ToList();
        var tryBody = BuildContainer(
            function,
            blocks, offsetToIndex[construct.TryStart], offsetToIndex[construct.TryEnd], inTry, offsetToIndex, continuations);

        IrNode node;
        if (construct.IsFinally)
        {
            var handler = construct.Handlers[0];
            var finallyBody = BuildHandlerBody(function, blocks, construct, handler, offsetToIndex, sliceEnd, continuations);
            TrimTrailingEndFinally(finallyBody);
            node = new TryFinally(tryBody, finallyBody);
        }
        else
        {
            var clauses = new List<CatchClause>();
            foreach (var handler in construct.Handlers)
            {
                var handlerEntryVariable = handler.Kind == HandlerKind.Filter
                    ? PeekHandlerEntryVariable(blocks, handler, offsetToIndex)
                    : null;
                var body = BuildHandlerBody(function, blocks, construct, handler, offsetToIndex, sliceEnd, continuations);
                int? variable = FoldEntryConsumption(body);
                if (handler.Kind == HandlerKind.Filter)
                {
                    var filter = TryBuildFilter(function, blocks, offsetToIndex, handler, allocateVariable: true, preferredVariable: variable, preferredVariableType: handlerEntryVariable?.Type)!;
                    if (filter.VariableIndex is { } filterLocal)
                        ReplaceCaughtExceptions(body, filterLocal, filter.ExceptionType);
                    clauses.Add(new CatchClause(filter.ExceptionType, body, filter.Condition) { VariableIndex = filter.VariableIndex });
                }
                else
                {
                    clauses.Add(new CatchClause(handler.CatchType ?? CatchAllType, body) { VariableIndex = variable });
                }
            }
            node = new TryCatch(tryBody, clauses);
        }
        continuations[node] = construct.End;
        return node;
    }

    static BlockContainer BuildHandlerBody(
        IrFunction function,
        IReadOnlyList<Block> blocks, Construct construct, HandlerRegion handler,
        Dictionary<int, int> offsetToIndex, int sliceEnd,
        Dictionary<IrNode, int> continuations)
    {
        int handlerEnd = handler.HandlerOffset + handler.HandlerLength;
        int endIndex = offsetToIndex.TryGetValue(handlerEnd, out int index) ? index : sliceEnd;
        var children = construct.Children
            .Where(c => c.TryStart >= handler.HandlerOffset && c.End <= handlerEnd)
            .ToList();
        return BuildContainer(function, blocks, offsetToIndex[handler.HandlerOffset], endIndex, children, offsetToIndex, continuations);
    }

    static void TrimTrailingEndFinally(BlockContainer body)
    {
        if (body.Blocks is [.., var last] && last.Children is [.., EndFinally end])
            end.Detach();
    }

    /// <summary>
    /// After structuring, a surviving leave whose target is a one-statement
    /// return/throw block is the multi-return idiom: a <c>return X;</c> inside
    /// the try compiled to a leave to a shared return block, so a try with more
    /// than one return became several leaves to distinct return blocks. Inline
    /// the return/throw at the leave site — a C# <c>return</c> inside a try runs
    /// exactly the finallys the leave did — and drop each return block once it
    /// is unreachable (no remaining reference, not reached by falling out of the
    /// previous block). The single normal-continuation return stays put. With
    /// the leaves gone the structuring pass raises the bodies.
    /// </summary>
    static void InlineReturnLeaves(BlockContainer root)
    {
        var byOffset = new Dictionary<int, Block>();
        foreach (var block in root.Descendants.OfType<Block>())
            byOffset.TryAdd(block.StartOffset, block);

        foreach (var leave in root.Descendants.OfType<Leave>().ToList())
            if (byOffset.TryGetValue(leave.TargetOffset, out var target) && CloneTerminator(target) is { } clone)
                leave.ReplaceWith(clone);

        // The multi-return blocks sit in the top-level slice after the
        // constructs; once their leaves are inlined they are unreachable.
        RemoveDeadReturns(root, ReferencedOffsets(root));
    }

    static HashSet<int> ReferencedOffsets(BlockContainer root)
    {
        var referenced = new HashSet<int>();
        foreach (var node in root.Descendants)
        {
            switch (node)
            {
                case Leave leave: referenced.Add(leave.TargetOffset); break;
                case Branch branch: referenced.Add(branch.TargetOffset); break;
                case ConditionalBranch conditional: referenced.Add(conditional.TargetOffset); break;
                case SwitchBranch sw: foreach (int t in sw.TargetOffsets) referenced.Add(t); break;
            }
        }
        return referenced;
    }

    static void RemoveDeadReturns(BlockContainer container, HashSet<int> referenced)
    {
        // Detach in place (the root container has no parent to replace through),
        // back to front so a removal never shifts an index still to visit. The
        // dead check reads the original predecessor — sound because the removed
        // blocks are themselves terminators, so dropping them never makes a kept
        // block fall-through-reachable.
        var blocks = container.Blocks.ToList();
        for (int i = blocks.Count - 1; i > 0; i--)
        {
            if (blocks[i].Children is [Return or Throw]
                && !referenced.Contains(blocks[i].StartOffset)
                && TerminatesUnconditionally(blocks[i - 1]))
            {
                blocks[i].Detach();
            }
        }
    }

    static bool TerminatesUnconditionally(Block block)
        => block.Children is [.., Return or Throw or Branch or Leave or EndFinally or EndFilter];

    /// <summary>True when the block at the offset is a one-statement return/throw whose value clones trivially.</summary>
    static bool IsInlineableReturn(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, int targetOffset)
        => offsetToIndex.TryGetValue(targetOffset, out int index) && CloneTerminator(blocks[index]) is not null;

    /// <summary>A fresh return/throw cloning the block's sole statement, or null when it is not a trivially-cloneable return/throw.</summary>
    static IrNode? CloneTerminator(Block block)
    {
        if (block.Children is not [var only])
            return null;
        return only switch
        {
            Return { Value: null } => new Return(null),
            Return { Value: { } value } when CloneSimple(value) is { } clone => new Return(clone),
            Throw { Value: { } value } when CloneSimple(value) is { } clone => new Throw(clone),
            _ => null,
        };
    }

    /// <summary>Clones a side-effect-free leaf value; null when the value is anything more complex.</summary>
    static IrExpression? CloneSimple(IrExpression value) => value switch
    {
        Constant constant => new Constant(constant.Value, constant.Type),
        LoadLocal local => new LoadLocal(local.Index, local.Type),
        LoadArgument argument => new LoadArgument(
            argument.Index,
            argument.Name,
            argument.Type,
            argument.Parameter),
        _ => null,
    };

    /// <summary>
    /// Binds a catch variable for a clause that uses the caught exception as a
    /// value without an entry store. Release leaves a <em>once-used</em> catch
    /// variable on the stack — <c>catch (E ex) { throw new TIE(ex); }</c> and
    /// filter-only uses such as <c>catch (E ex) when (F(ex))</c> import as a
    /// single bare <see cref="CaughtException"/> in value position, with no
    /// <c>E ex = …</c> to fold. C# has no spelling for the stack exception, so
    /// synthesize a local, make it the clause's variable, and rewrite that one use
    /// to read it. Recompiling re-elides the store, so the round trip is
    /// opcode-exact.
    ///
    /// <para>Gated to exactly one value use, which is the only shape this elision
    /// produces: any C# that reads the exception twice forces the store, taking the
    /// entry-store fold path instead. A handler with two or more inline
    /// <see cref="CaughtException"/> values can only come from hand-written or
    /// obfuscated IL (a duped exception on the stack); binding one variable there
    /// would re-introduce a store the original lacked — a roundtrip the proof does
    /// not cover — so it is left unconsumed. A bare rethrow (<c>throw;</c>) names no
    /// variable and is not a value use.</para>
    /// </summary>
    static void SynthesizeInlineCatchVariables(IrFunction function, BlockContainer root)
    {
        foreach (var clause in root.Descendants.OfType<CatchClause>())
        {
            if (clause.VariableIndex is not null)
                continue;
            if (clause.ExceptionType.Equals(CatchAllType))
                continue;
            var uses = clause.Descendants.OfType<CaughtException>()
                .Where(caught => InnermostCatchClause(caught) == clause && !IsBareRethrow(caught))
                .ToList();
            if (uses.Count != 1)
                continue;

            int slot = function.AddLocal(clause.ExceptionType);
            clause.VariableIndex = slot;
            uses[0].ReplaceWith(new LoadLocal(slot, clause.ExceptionType));
        }
    }

    static bool IsBareRethrow(CaughtException caught)
        => caught.Type is null && caught.Parent is Throw { } throwNode && ReferenceEquals(throwNode.Value, caught);

    static CatchClause? InnermostCatchClause(IrNode node)
    {
        for (var parent = node.Parent; parent is not null; parent = parent.Parent)
            if (parent is CatchClause clause)
                return clause;
        return null;
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
