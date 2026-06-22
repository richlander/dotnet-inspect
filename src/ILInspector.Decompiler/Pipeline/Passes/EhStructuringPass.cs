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

    sealed record FilterInfo(TypeRef ExceptionType, int? VariableIndex, IrExpression Condition);

    public void Run(IrFunction function, PassContext context)
    {
        if (function.Regions.IsEmpty)
            return;
        if (function.Regions.Any(r => r.Kind is HandlerKind.Fault))
            return;
        if (function.Regions.Any(r => r.Kind is HandlerKind.Catch && r.CatchType is null))
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

    static bool ValidateFilters(IrFunction function, IReadOnlyList<Block> blocks, List<Construct> all, Dictionary<int, int> offsetToIndex)
    {
        foreach (var handler in all.SelectMany(c => c.Handlers).Where(h => h.Kind == HandlerKind.Filter))
        {
            var preferredVariable = PeekHandlerEntryVariable(blocks, handler, offsetToIndex);
            if (TryBuildFilter(function, blocks, offsetToIndex, handler, allocateVariable: false, preferredVariable: preferredVariable?.Index, preferredVariableType: preferredVariable?.Type) is null)
                return false;
        }
        return true;
    }

    static (int Index, TypeRef Type)? PeekHandlerEntryVariable(IReadOnlyList<Block> blocks, HandlerRegion handler, Dictionary<int, int> offsetToIndex)
    {
        if (!offsetToIndex.TryGetValue(handler.HandlerOffset, out int handlerIndex))
            return null;
        return blocks[handlerIndex].Children is [StoreLocal { Value: CaughtException } store, ..]
            ? (store.Index, store.Type)
            : null;
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
                    if (node is not CaughtException)
                        continue;
                    // A CaughtException outside any catch handler cannot be spelled.
                    if (handler is null)
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
                    clauses.Add(new CatchClause(handler.CatchType!, body) { VariableIndex = variable });
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

    static FilterInfo? TryBuildFilter(IReadOnlyList<Block> blocks, Dictionary<int, int> offsetToIndex, HandlerRegion handler)
        => TryBuildFilter(null, blocks, offsetToIndex, handler, allocateVariable: false, preferredVariable: null, preferredVariableType: null);

    static FilterInfo? TryBuildFilter(
        IrFunction? function,
        IReadOnlyList<Block> blocks,
        Dictionary<int, int> offsetToIndex,
        HandlerRegion handler,
        bool allocateVariable,
        int? preferredVariable,
        TypeRef? preferredVariableType)
    {
        if (handler.Kind != HandlerKind.Filter
            || !offsetToIndex.TryGetValue(handler.FilterOffset, out int filterStart)
            || !offsetToIndex.TryGetValue(handler.HandlerOffset, out int handlerStart))
        {
            return null;
        }

        var filterBlocks = blocks.Skip(filterStart).Take(handlerStart - filterStart).ToArray();
        if (TryBuildSimpleCatchAllFilter(filterBlocks) is { } simple)
            return simple;
        if (TryBuildExceptionCaptureFilter(filterBlocks) is { } capture)
            return capture;
        if (TryBuildGlobalExceptionHandlerFilter(filterBlocks) is { } globalHandler)
            return globalHandler;

        if (TryBuildIOExceptionFilter(filterBlocks) is { } ioFilter)
        {
            if (preferredVariable is not null && ioFilter.VariableIndex != preferredVariable)
                return null;
            if (preferredVariableType is not null && !ioFilter.ExceptionType.Equals(preferredVariableType))
                return null;
            if (ioFilter.VariableIndex is { } ioVariable
                && LocalReferencedOutsideFilterHandler(blocks, handler, ioVariable))
            {
                return null;
            }
            return ioFilter;
        }

        return TryBuildTypedExceptionFilter(
            function,
            blocks,
            filterBlocks,
            handler,
            allocateVariable,
            preferredVariable,
            preferredVariableType);
    }

    static FilterInfo? TryBuildSimpleCatchAllFilter(IReadOnlyList<Block> filterBlocks)
    {
        if (filterBlocks is not [var filter]
            || filter.Children is not [ExpressionStatement { Expression: CaughtException }, EndFilter { Value: var value }])
        {
            return null;
        }

        if (BoolFilterCondition(value) is not { } condition)
            return null;

        return new FilterInfo(TypeRef.CoreLib("System", "Object"), null, condition);
    }

    static IrExpression? BoolFilterCondition(IrExpression value)
    {
        // csc sometimes normalizes a bool filter through `(flag == false) > false`;
        // recover the source bool so the catch filter stays readable and valid.
        if (value is Comparison
            {
                Kind: ComparisonKind.GreaterThan,
                IsUnsigned: true,
                Left: Comparison
                {
                    Kind: ComparisonKind.Equal,
                    Left: var operand,
                    Right: Constant { Value: false or 0 },
                },
                Right: Constant { Value: false or 0 },
            })
        {
            return CloneFilterValue(operand);
        }

        if (value.ResultType is { Namespace: "System", Name: "Boolean", Assembly: TypeRef.CoreLibrary })
            return CloneFilterValue(value);

        return null;
    }

    static IrExpression? CloneFilterValue(IrExpression value) => value switch
    {
        LoadArgument argument => new LoadArgument(argument.Index, argument.Name, argument.Type),
        LoadLocal local => new LoadLocal(local.Index, local.Type),
        Constant constant => new Constant(constant.Value, constant.Type),
        _ => null,
    };

    static FilterInfo? TryBuildExceptionCaptureFilter(IReadOnlyList<Block> filterBlocks)
    {
        if (filterBlocks is not
            [
                var typeTest,
                var falseArm,
                var typedException,
                var end,
            ])
        {
            return null;
        }

        if (typeTest.Children is not
            [
                StoreStackSlot { Slot: var exceptionSlot, Value: IsInstance { Type: var exceptionType, Operand: CaughtException } },
                StoreStackSlot { Slot: var copiedExceptionSlot, Value: LoadStackSlot copiedException },
                ConditionalBranch { Condition: LoadStackSlot testedException, TargetOffset: var typedExceptionOffset },
            ]
            || !exceptionType.Equals(TypeRef.CoreLib("System", "Exception"))
            || copiedException.Slot != exceptionSlot
            || testedException.Slot != exceptionSlot
            || typedExceptionOffset != typedException.StartOffset)
        {
            return null;
        }

        if (falseArm.Children is not
            [
                StoreStackSlot { Slot: var verdictSlot, Value: Constant { Value: false or 0 } },
                Branch { TargetOffset: var endOffset },
            ]
            || endOffset != end.StartOffset)
        {
            return null;
        }

        if (typedException.Children is not
            [
                StoreLocal { Index: var variableIndex, Type: var storedExceptionType, Value: LoadStackSlot storedException },
                StoreStackSlot
                {
                    Slot: var trueVerdictSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: var conditionValue,
                        Right: Constant { Value: false or 0 },
                    },
                },
            ]
            || storedException.Slot != copiedExceptionSlot
            || !storedExceptionType.Equals(exceptionType)
            || trueVerdictSlot != verdictSlot
            || BoolFilterCondition(conditionValue) is not { } condition)
        {
            return null;
        }

        if (end.Children is not [EndFilter { Value: LoadStackSlot finalVerdict }]
            || finalVerdict.Slot != verdictSlot)
        {
            return null;
        }

        return new FilterInfo(exceptionType, variableIndex, condition);
    }

    static FilterInfo? TryBuildGlobalExceptionHandlerFilter(IReadOnlyList<Block> filterBlocks)
    {
        if (filterBlocks is not
            [
                var typeTest,
                var falseArm,
                var typedException,
                var end,
            ])
        {
            return null;
        }

        if (typeTest.Children is not
            [
                StoreStackSlot { Slot: var exceptionSlot, Value: IsInstance { Type: var exceptionType, Operand: CaughtException } },
                StoreStackSlot { Slot: var copiedExceptionSlot, Value: LoadStackSlot copiedException },
                ConditionalBranch { Condition: LoadStackSlot testedException, TargetOffset: var typedExceptionOffset },
            ]
            || !exceptionType.Equals(TypeRef.CoreLib("System", "Exception"))
            || copiedException.Slot != exceptionSlot
            || testedException.Slot != exceptionSlot
            || typedExceptionOffset != typedException.StartOffset)
        {
            return null;
        }

        if (falseArm.Children is not
            [
                StoreStackSlot { Slot: var verdictSlot, Value: Constant { Value: false or 0 } },
                Branch { TargetOffset: var endOffset },
            ]
            || endOffset != end.StartOffset)
        {
            return null;
        }

        if (typedException.Children is not
            [
                StoreStackSlot
                {
                    Slot: var trueVerdictSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: Call
                        {
                            Callee: var callee,
                            IsVirtual: var isVirtual,
                            Arguments: [LoadStackSlot handledException],
                        } call,
                        Right: Constant { Value: false or 0 },
                    },
                },
            ]
            || trueVerdictSlot != verdictSlot
            || handledException.Slot != copiedExceptionSlot
            || call.ConstrainedTo is not null
            || isVirtual
            || callee.Name != "IsHandledByGlobalHandler"
            || callee.DeclaringType.Name != "ExceptionHandling"
            || !callee.ReturnType.Equals(TypeRef.CoreLib("System", "Boolean")))
        {
            return null;
        }

        if (end.Children is not [EndFilter { Value: LoadStackSlot finalVerdict }]
            || finalVerdict.Slot != verdictSlot)
        {
            return null;
        }

        var condition = new Call(callee, isVirtual: false, [new CaughtException(exceptionType)]);
        return new FilterInfo(exceptionType, null, condition);
    }

    static FilterInfo? TryBuildIOExceptionFilter(IReadOnlyList<Block> filterBlocks)
    {
        if (filterBlocks is not
            [
                var typeTest,
                var falseArm,
                var typedException,
                var innerExceptionTest,
                var directMatch,
                var join,
                var end,
            ])
        {
            return null;
        }

        if (typeTest.Children is not
            [
                StoreStackSlot { Slot: var exceptionSlot, Value: IsInstance { Type: var exceptionType, Operand: CaughtException } },
                StoreStackSlot { Slot: var copiedExceptionSlot, Value: LoadStackSlot copiedException },
                ConditionalBranch { Condition: LoadStackSlot testedException, TargetOffset: var typedExceptionOffset },
            ]
            || copiedException.Slot != exceptionSlot
            || testedException.Slot != exceptionSlot
            || typedExceptionOffset != typedException.StartOffset)
        {
            return null;
        }

        if (falseArm.Children is not
            [
                StoreStackSlot { Slot: var verdictSlot, Value: Constant { Value: false or 0 } },
                Branch { TargetOffset: var endOffset },
            ]
            || endOffset != end.StartOffset)
        {
            return null;
        }

        if (typedException.Children is not
            [
                StoreLocal { Index: var variableIndex, Type: var storedExceptionType, Value: LoadStackSlot storedException },
                ConditionalBranch { Condition: IsInstance { Type: var testedType, Operand: LoadLocal directOperand }, TargetOffset: var directMatchOffset },
            ]
            || storedException.Slot != copiedExceptionSlot
            || !storedExceptionType.Equals(exceptionType)
            || directOperand.Index != variableIndex
            || directMatchOffset != directMatch.StartOffset)
        {
            return null;
        }

        if (innerExceptionTest.Children is not
            [
                StoreStackSlot
                {
                    Slot: var innerResultSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: IsInstance
                        {
                            Type: var innerTestedType,
                            Operand: Call { Callee: var innerExceptionGetter, IsVirtual: true, Arguments: [LoadLocal innerReceiver] },
                        },
                        Right: Constant { Value: null },
                    },
                },
                Branch { TargetOffset: var joinOffset },
            ]
            || !innerTestedType.Equals(testedType)
            || innerReceiver.Index != variableIndex
            || joinOffset != join.StartOffset)
        {
            return null;
        }

        if (directMatch.Children is not
            [
                StoreStackSlot { Slot: var directResultSlot, Value: Constant { Value: true or 1 } },
            ]
            || directResultSlot != innerResultSlot)
        {
            return null;
        }

        if (join.Children is not
            [
                StoreStackSlot
                {
                    Slot: var joinedVerdictSlot,
                    Value: Comparison
                    {
                        Kind: ComparisonKind.GreaterThan,
                        IsUnsigned: true,
                        Left: LoadStackSlot joinedResult,
                        Right: Constant { Value: false or 0 },
                    },
                },
            ]
            || joinedVerdictSlot != verdictSlot
            || joinedResult.Slot != innerResultSlot)
        {
            return null;
        }

        if (end.Children is not [EndFilter { Value: LoadStackSlot finalVerdict }]
            || finalVerdict.Slot != verdictSlot)
        {
            return null;
        }

        var variable = new LoadLocal(variableIndex, exceptionType);
        var direct = new IsInstance(testedType, new LoadLocal(variableIndex, exceptionType));
        var inner = new IsInstance(testedType, new Call(innerExceptionGetter, isVirtual: true, [variable]));
        return new FilterInfo(exceptionType, variableIndex, new LogicalBinary(LogicalKind.Or, direct, inner));
    }

    static FilterInfo? TryBuildTypedExceptionFilter(
        IrFunction? function,
        IReadOnlyList<Block> blocks,
        IReadOnlyList<Block> filterBlocks,
        HandlerRegion handler,
        bool allocateVariable,
        int? preferredVariable,
        TypeRef? preferredVariableType)
    {
        if (filterBlocks.Count != 4)
            return null;

        var exceptionAliases = new HashSet<int>();
        var exceptionAliasTypes = new Dictionary<int, TypeRef>();
        var exceptionAliasRoots = new Dictionary<int, int>();
        var entryChildren = filterBlocks[0].Children;
        if (entryChildren.Count == 0
            || entryChildren[^1] is not ConditionalBranch { Condition: LoadStackSlot isInstLoad } branch)
        {
            return null;
        }
        foreach (var entryStore in entryChildren.Take(entryChildren.Count - 1))
        {
            if (entryStore is not StoreStackSlot store)
                return null;
            switch (store.Value)
            {
                case IsInstance { Operand: CaughtException, Type: var type }:
                    exceptionAliases.Add(store.Slot);
                    exceptionAliasTypes[store.Slot] = type;
                    exceptionAliasRoots[store.Slot] = store.Slot;
                    break;
                case LoadStackSlot load when exceptionAliases.Contains(load.Slot):
                    exceptionAliases.Add(store.Slot);
                    exceptionAliasTypes[store.Slot] = exceptionAliasTypes[load.Slot];
                    exceptionAliasRoots[store.Slot] = exceptionAliasRoots[load.Slot];
                    break;
                default:
                    return null;
            }
        }

        if (!exceptionAliasTypes.TryGetValue(isInstLoad.Slot, out var exceptionType))
            return null;
        if (!IsSupportedCatchFilterType(exceptionType))
            return null;
        if (preferredVariable is not null && !exceptionType.Equals(preferredVariableType))
            return null;
        int testedRoot = exceptionAliasRoots[isInstLoad.Slot];
        var testedAliases = exceptionAliasRoots
            .Where(kvp => kvp.Value == testedRoot)
            .Select(kvp => kvp.Key)
            .ToHashSet();

        var falseBlock = filterBlocks[1];
        var trueBlock = filterBlocks.FirstOrDefault(block => block.StartOffset == branch.TargetOffset);
        var endFilterBlock = filterBlocks[^1];
        if (trueBlock is null || ReferenceEquals(trueBlock, falseBlock) || ReferenceEquals(trueBlock, endFilterBlock))
            return null;
        if (endFilterBlock.Children is not [EndFilter { Value: LoadStackSlot resultLoad }])
            return null;
        if (falseBlock.Children is not [StoreStackSlot { Value: Constant { Value: 0 } } falseStore, Branch falseBranch]
            || falseStore.Slot != resultLoad.Slot
            || falseBranch.TargetOffset != endFilterBlock.StartOffset)
        {
            return null;
        }

        var trueChildren = trueBlock.Children;
        Branch? trueBranch = null;
        if (trueChildren is [.., Branch branchToEnd])
        {
            if (branchToEnd.TargetOffset != endFilterBlock.StartOffset)
                return null;
            trueBranch = branchToEnd;
        }
        var trueStatements = trueBranch is null ? trueChildren : trueChildren.Take(trueChildren.Count - 1).ToList();
        if (trueStatements.Count == 0 || trueStatements[^1] is not StoreStackSlot predicateStore || predicateStore.Slot != resultLoad.Slot)
            return null;
        if (predicateStore.Value is Constant { Value: 0 })
            return null;
        foreach (var statement in trueStatements.Take(trueStatements.Count - 1))
            if (statement is not StoreLocal)
                return null;

        var exceptionLocalStoreNodes = filterBlocks
            .SelectMany(b => b.Children)
            .OfType<StoreLocal>()
            .Where(s => s.Value is LoadStackSlot load
                && testedAliases.Contains(load.Slot)
                && s.Type.Equals(exceptionType))
            .ToList();
        var exceptionLocalStores = exceptionLocalStoreNodes
            .Select(s => s.Index)
            .Distinct()
            .ToList();
        var existingVariable = exceptionLocalStores.Count > 0 ? exceptionLocalStores[0] : (int?)null;

        int selectedVariable = preferredVariable ?? existingVariable ?? (allocateVariable && function is not null ? function.AddLocal(exceptionType, "e") : -1);
        if (selectedVariable >= 0 && LocalReferencedOutsideFilterHandler(blocks, handler, selectedVariable))
            return null;
        if (selectedVariable >= 0 && !ValidateSelectedFilterLocal(filterBlocks, testedAliases, selectedVariable, exceptionType))
            return null;

        var ignoredExceptionStores = new HashSet<StoreLocal>();
        foreach (var store in exceptionLocalStoreNodes.Where(s => s.Index != selectedVariable))
        {
            bool loaded = blocks.Any(block => block.Descendants.OfType<LoadLocal>().Any(load => load.Index == store.Index)
                || block.Descendants.OfType<LoadLocalAddress>().Any(load => load.Index == store.Index));
            if (loaded)
                continue;
            ignoredExceptionStores.Add(store);
        }
        var filter = (IrExpression)predicateStore.Value.Clone();
        if (!ReplaceExceptionAliasLoads(filter, testedAliases, selectedVariable, exceptionType, out var replacementRoot))
            return null;
        filter = (IrExpression)(replacementRoot ?? filter);
        if (!InlineFilterLocalLoads(filter, blocks, filterBlocks, ignoredExceptionStores, testedAliases, selectedVariable, exceptionType, out replacementRoot))
            return null;
        filter = (IrExpression)(replacementRoot ?? filter);
        if (ContainsExceptionAliasLoad(filter, exceptionAliases))
            return null;
        filter = NormalizeFilterCondition(filter);
        return new FilterInfo(exceptionType, selectedVariable >= 0 ? selectedVariable : null, filter);
    }

    static bool InlineFilterLocalLoads(
        IrNode node,
        IReadOnlyList<Block> allBlocks,
        IReadOnlyList<Block> filterBlocks,
        HashSet<StoreLocal> ignoredExceptionStores,
        HashSet<int> exceptionAliases,
        int exceptionVariable,
        TypeRef exceptionType,
        out IrNode? replacementRoot)
    {
        replacementRoot = null;
        var stores = new Dictionary<int, StoreLocal>();
        foreach (var store in filterBlocks.SelectMany(b => b.Children).OfType<StoreLocal>())
        {
            if (store.Index == exceptionVariable || ignoredExceptionStores.Contains(store))
                continue;
            if (!IsInlineSafeFilterTemp(store.Value, exceptionAliases))
                return false;
            if (stores.ContainsKey(store.Index))
                return false;
            stores[store.Index] = store;
        }
        if (stores.Count == 0)
            return true;
        foreach (var index in stores.Keys)
        {
            int storeCount = allBlocks.SelectMany(b => b.Descendants).OfType<StoreLocal>().Count(s => s.Index == index);
            int loadCount = allBlocks.SelectMany(b => b.Descendants).OfType<LoadLocal>().Count(l => l.Index == index);
            int addressCount = allBlocks.SelectMany(b => b.Descendants).OfType<LoadLocalAddress>().Count(l => l.Index == index);
            if (storeCount != 1 || loadCount != 1 || addressCount != 0)
                return false;
        }

        var replacements = new List<(IrNode Old, IrNode New)>();
        var usedStores = new Dictionary<int, int>();
        bool ok = true;
        Visit(node);
        if (!ok)
            return false;
        if (usedStores.Count != stores.Count)
            return false;
        foreach (var (old, replacement) in replacements)
        {
            if (ReferenceEquals(old, node))
                replacementRoot = replacement;
            else
                old.ReplaceWith(replacement);
        }
        return true;

        void Visit(IrNode current)
        {
            if (!ok)
                return;
            if (current is LoadLocal load && stores.TryGetValue(load.Index, out var store))
            {
                if (usedStores.TryGetValue(load.Index, out int useCount))
                {
                    ok = false;
                    return;
                }
                usedStores[load.Index] = useCount + 1;
                var clone = (IrExpression)store.Value.Clone();
                if (!ReplaceExceptionAliasLoads(clone, exceptionAliases, exceptionVariable, exceptionType, out var root))
                {
                    ok = false;
                    return;
                }
                replacements.Add((current, root ?? clone));
                return;
            }
            foreach (var child in current.Children)
                Visit(child);
        }
    }

    static bool IsInlineSafeFilterTemp(IrExpression value, HashSet<int> exceptionAliases)
        => value is Constant
            || value is LoadStackSlot load && exceptionAliases.Contains(load.Slot);

    static bool IsSupportedCatchFilterType(TypeRef type)
        => type.Kind == TypeRefKind.Definition
            && type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == "System"
            && type.DeclaredValueTypeHint != ValueTypeHint.ValueType
            && (type.Name == "Exception" || type.Name.EndsWith("Exception", StringComparison.Ordinal));

    static bool ContainsExceptionAliasLoad(IrNode node, HashSet<int> aliases)
        => node is LoadStackSlot load && aliases.Contains(load.Slot)
            || node.Children.Any(child => ContainsExceptionAliasLoad(child, aliases));

    static bool ValidateSelectedFilterLocal(
        IReadOnlyList<Block> filterBlocks,
        HashSet<int> testedAliases,
        int localIndex,
        TypeRef exceptionType)
    {
        bool hasAliasStore = false;
        bool hasLoad = false;
        foreach (var node in filterBlocks.SelectMany(block => block.Descendants))
        {
            switch (node)
            {
                case StoreLocal store when store.Index == localIndex:
                    if (store.Type.Equals(exceptionType)
                        && store.Value is LoadStackSlot aliasLoad
                        && testedAliases.Contains(aliasLoad.Slot))
                    {
                        hasAliasStore = true;
                        break;
                    }
                    return false;
                case LoadLocal localLoad when localLoad.Index == localIndex:
                    hasLoad = true;
                    break;
                case LoadLocalAddress address when address.Index == localIndex:
                    return false;
            }
        }
        return !hasLoad || hasAliasStore;
    }

    static bool LocalReferencedOutsideFilterHandler(IReadOnlyList<Block> blocks, HandlerRegion handler, int localIndex)
    {
        int scopeStart = handler.FilterOffset;
        int scopeEnd = handler.HandlerOffset + handler.HandlerLength;
        return blocks.Any(block => (block.StartOffset < scopeStart || block.StartOffset >= scopeEnd)
            && block.Descendants.Any(node => node switch
            {
                StoreLocal store => store.Index == localIndex,
                LoadLocal load => load.Index == localIndex,
                LoadLocalAddress address => address.Index == localIndex,
                _ => false,
            }));
    }

    static IrExpression NormalizeFilterCondition(IrExpression expression)
        => expression is Comparison
        {
            Kind: ComparisonKind.GreaterThan,
            IsUnsigned: true,
            Left.ResultType: { Namespace: "System", Name: "Boolean" },
            Right: Constant { Value: false }
        } comparison
            ? (IrExpression)comparison.DetachChildren()[0]
            : expression;

    static bool ReplaceExceptionAliasLoads(
        IrNode node,
        HashSet<int> aliases,
        int localIndex,
        TypeRef localType,
        out IrNode? replacementRoot)
    {
        replacementRoot = null;
        var replacements = new List<(IrNode Old, IrNode New)>();
        bool ok = true;
        Visit(node);
        if (!ok)
            return false;
        foreach (var (old, replacement) in replacements)
        {
            if (ReferenceEquals(old, node))
                replacementRoot = replacement;
            else
                old.ReplaceWith(replacement);
        }
        return true;

        void Visit(IrNode current)
        {
            if (!ok)
                return;
            switch (current)
            {
                case LoadStackSlot load when aliases.Contains(load.Slot):
                    replacements.Add((current, new LoadLocal(localIndex, localType)));
                    return;
                case CaughtException:
                    ok = false;
                    return;
            }
            foreach (var child in current.Children)
                Visit(child);
        }
    }

    static void ReplaceCaughtExceptions(BlockContainer body, int localIndex, TypeRef localType)
    {
        Visit(body);

        void Visit(IrNode node)
        {
            if (node is CaughtException caught)
            {
                if (!IsBareRethrow(caught))
                    caught.ReplaceWith(new LoadLocal(localIndex, localType));
                return;
            }
            if (node is TryCatch nested)
            {
                Visit(nested.TryBody);
                return;
            }
            foreach (var child in node.Children.ToList())
                Visit(child);
        }
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
        LoadArgument argument => new LoadArgument(argument.Index, argument.Name, argument.Type),
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
