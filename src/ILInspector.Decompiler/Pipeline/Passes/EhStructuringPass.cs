using System.Collections.Immutable;
using System.Reflection.Metadata;
using ILInspector.Instructions;
using ILInspector.Metadata;

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
        public InstructionExceptionRegionId? ProtectedRegion { get; init; }
        public IReadOnlyDictionary<HandlerRegion, InstructionExceptionClause>
            ClauseFacts
        { get; init; } =
            new Dictionary<HandlerRegion, InstructionExceptionClause>(
                ReferenceEqualityComparer.Instance);
        public int End => Handlers[^1].HandlerOffset + Handlers[^1].HandlerLength;
        public List<Construct> Children { get; } = [];

        public bool Contains(int offset) => offset >= TryStart && offset < End;
        public bool Contains(Construct other) => other.TryStart >= TryStart && other.End <= End;
        public bool IsFinally => Handlers is [{ Kind: HandlerKind.Finally }];
        public InstructionExceptionClause? FactsFor(HandlerRegion handler) =>
            ClauseFacts.GetValueOrDefault(handler);
    }

    public void Run(IrFunction function, PassContext context)
    {
        if (function.Regions.IsEmpty)
            return;

        InstructionExceptionFlowFacts? exceptionFlow;
        switch (function.ExceptionFlow)
        {
            case null when function.IsMetadataBacked:
                function.ExceptionFactFailure =
                    "Metadata-backed EH structuring has no correlated Instructions evidence.";
                return;
            case null:
                exceptionFlow = null;
                break;
            case InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Available available:
                exceptionFlow = available.Value;
                break;
            case InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Unavailable unavailable:
                function.ExceptionFactFailure =
                    $"Instructions exception-flow evidence is unavailable "
                    + $"({unavailable.Reason}): {unavailable.Detail}";
                return;
            case InstructionExceptionFlowResult<
                InstructionExceptionFlowFacts>.Ambiguous ambiguous:
                function.ExceptionFactFailure =
                    $"Instructions exception-flow evidence is ambiguous: "
                    + ambiguous.Detail;
                return;
            default:
                throw new InvalidOperationException(
                    "Unknown Instructions exception-flow result.");
        }

        if (function.Regions.Any(r => r.Kind is HandlerKind.Fault))
            return;

        var blocks = function.Body.Blocks;
        var offsetToIndex = new Dictionary<int, int>();
        for (int i = 0; i < blocks.Count; i++)
            offsetToIndex[blocks[i].StartOffset] = i;

        if (BuildForest(
                function.Regions,
                function.ExceptionClauseImports,
                exceptionFlow,
                offsetToIndex,
                out string? evidenceFailure) is not { } forest)
        {
            if (evidenceFailure is not null)
                function.ExceptionFactFailure = evidenceFailure;
            return;
        }
        if (!Validate(
                blocks,
                forest.All,
                offsetToIndex,
                exceptionFlow))
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
        function.ExceptionFactFailure = null;
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
        ImmutableArray<HandlerRegion> regions,
        ImmutableArray<DecompilerExceptionClauseImport> imports,
        InstructionExceptionFlowFacts? exceptionFlow,
        Dictionary<int, int> offsetToIndex,
        out string? evidenceFailure)
    {
        evidenceFailure = null;
        List<Construct>? all = exceptionFlow is null
            ? BuildLegacyConstructs(regions)
            : BuildCorrelatedConstructs(
                regions,
                imports,
                exceptionFlow,
                out evidenceFailure);
        if (all is null)
            return null;

        foreach (Construct construct in all)
        {
            List<HandlerRegion> handlers = construct.Handlers;
            if (handlers.Count > 1 && handlers.Any(h => h.Kind is not (HandlerKind.Catch or HandlerKind.Filter)))
                return null;
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
        }

        // Outer-first ordering, then a containment stack builds the forest.
        all.Sort((a, b) => a.TryStart != b.TryStart ? a.TryStart - b.TryStart : b.End - a.End);
        var roots = new List<Construct>();
        var stack = new Stack<Construct>();
        foreach (var construct in all)
        {
            while (stack.Count > 0
                   && !ContainsConstruct(
                       stack.Peek(),
                       construct,
                       exceptionFlow))
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
                bool placed = SegmentWithin(
                    parent,
                    construct.TryStart,
                    exceptionFlow) != -2;
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

    static bool ContainsConstruct(
        Construct outer,
        Construct inner,
        InstructionExceptionFlowFacts? exceptionFlow)
        => exceptionFlow is null
            ? outer.Contains(inner)
            : ContainsAt(outer, inner.TryStart, exceptionFlow);

    static List<Construct> BuildLegacyConstructs(
        ImmutableArray<HandlerRegion> regions)
    {
        var all = new List<Construct>();
        foreach (var group in regions.GroupBy(
                     region => (region.TryOffset, region.TryLength)))
        {
            all.Add(new Construct
            {
                TryStart = group.Key.TryOffset,
                TryEnd = group.Key.TryOffset + group.Key.TryLength,
                Handlers = group.OrderBy(
                    region => region.HandlerOffset).ToList(),
            });
        }

        return all;
    }

    static List<Construct>? BuildCorrelatedConstructs(
        ImmutableArray<HandlerRegion> regions,
        ImmutableArray<DecompilerExceptionClauseImport> imports,
        InstructionExceptionFlowFacts exceptionFlow,
        out string? evidenceFailure)
    {
        evidenceFailure = null;
        if (imports.Length != regions.Length
            || imports.Length != exceptionFlow.Clauses.Length)
        {
            evidenceFailure =
                "Decompiler exception-clause imports do not cover the complete owner-issued catalog.";
            return null;
        }

        for (int index = 0; index < imports.Length; index++)
        {
            DecompilerExceptionClauseImport import = imports[index];
            InstructionExceptionClause facts = exceptionFlow.Clauses[index];
            if (!ReferenceEquals(import.Region, regions[index])
                || !ReferenceEquals(import.Facts, facts)
                || facts.Id.Body != exceptionFlow.Body)
            {
                evidenceFailure =
                    "Decompiler exception-clause association does not preserve the owner-issued identity.";
                return null;
            }

            if (facts.Kind != ExceptionRegionKind.Catch)
                continue;

            MethodExceptionCatchType? catchType = facts.Clause.CatchType;
            bool valid = catchType?.Name switch
            {
                MetadataTypeNameResult.Resolved =>
                    catchType.MetadataToken != 0
                    && import.Region.CatchType is not null,
                MetadataTypeNameResult.Absent =>
                    catchType.MetadataToken == 0
                    && import.Region.CatchType is null,
                MetadataTypeNameResult.Rejected => false,
                _ => false,
            };
            if (!valid)
            {
                evidenceFailure =
                    $"Metadata catch-type evidence is unavailable for clause "
                    + $"{facts.Id.Ordinal}.";
                return null;
            }
        }

        var all = new List<Construct>();
        foreach (IGrouping<
                     InstructionExceptionRegionId,
                     DecompilerExceptionClauseImport> group in imports.GroupBy(
                         import => import.Facts.ProtectedRegion))
        {
            InstructionExceptionRegion? protectedRegion =
                exceptionFlow.Regions.SingleOrDefault(
                    region => region.Id == group.Key);
            if (protectedRegion is null)
            {
                evidenceFailure =
                    "Instructions did not publish the protected region named by an imported clause.";
                return null;
            }

            List<DecompilerExceptionClauseImport> clauses = group.ToList();
            var clauseFacts =
                new Dictionary<HandlerRegion, InstructionExceptionClause>(
                    ReferenceEqualityComparer.Instance);
            foreach (DecompilerExceptionClauseImport clause in clauses)
                clauseFacts.Add(clause.Region, clause.Facts);

            all.Add(new Construct
            {
                TryStart = protectedRegion.Extent.Start,
                TryEnd = protectedRegion.Extent.End,
                Handlers = clauses.Select(
                    clause => clause.Region).ToList(),
                ProtectedRegion = protectedRegion.Id,
                ClauseFacts = clauseFacts,
            });
        }

        return all;
    }

    /// <summary>Phase 1: pure checks over the flat blocks — no mutation until the whole function fits the slice.</summary>
    static bool Validate(
        IReadOnlyList<Block> blocks,
        List<Construct> all,
        Dictionary<int, int> offsetToIndex,
        InstructionExceptionFlowFacts? exceptionFlow)
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
                            if (exceptionFlow is not null
                                && !AvailableNormalTransfer(
                                    exceptionFlow,
                                    statement.SourceOffset,
                                    leave.TargetOffset,
                                    InstructionNormalTransferKind.Leave))
                            {
                                return false;
                            }
                            // leave exits to the continuation of an enclosing
                            // construct — possibly through several (it runs the
                            // intervening finallys, exactly C# goto-out-of-try).
                            bool enclosed = false, targetsContinuation = false;
                            foreach (var construct in all)
                            {
                                if (!ContainsAt(
                                        construct,
                                        offset,
                                        exceptionFlow))
                                    continue;
                                enclosed = true;
                                if (construct.IsFinally
                                    && InHandlerAt(
                                        construct,
                                        construct.Handlers[0],
                                        offset,
                                        exceptionFlow))
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
                                && !TargetsOutsideContainingConstructs(
                                    all,
                                    offset,
                                    leave.TargetOffset,
                                    exceptionFlow)
                                && !TargetsWithinEnclosingSegment(
                                    blocks,
                                    all,
                                    offsetToIndex,
                                    offset,
                                    leave.TargetOffset,
                                    exceptionFlow)
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
                                c.IsFinally
                                && InHandlerAt(
                                    c,
                                    c.Handlers[0],
                                    offset,
                                    exceptionFlow));
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
                                c.Handlers.Any(h =>
                                    h.Kind == HandlerKind.Filter
                                    && InFilterAt(
                                        c,
                                        h,
                                        offset,
                                        exceptionFlow)));
                            if (owner is null)
                                return false;
                            var handler = owner.Handlers.Single(h =>
                                h.Kind == HandlerKind.Filter
                                && InFilterAt(
                                    owner,
                                    h,
                                    offset,
                                    exceptionFlow));
                            if (i + 1 < blocks.Count && blocks[i + 1].StartOffset < handler.HandlerOffset)
                                return false;
                            break;
                        }
                    case Return:
                        {
                            if (!isLast)
                                return false;
                            if (exceptionFlow is not null
                                && (statement.SourceOffset < 0
                                    || exceptionFlow.NormalTransferAt(
                                        statement.SourceOffset,
                                        logicalDestinationOffset: null) is not
                                        InstructionExceptionFlowResult<
                                            InstructionNormalTransfer>.Available
                                        {
                                            Value.Kind:
                                                    InstructionNormalTransferKind.Return,
                                        }))
                            {
                                return false;
                            }
                            break;
                        }
                    case Branch branch:
                        if (exceptionFlow is not null
                            ? !AvailableNormalTransfer(
                                exceptionFlow,
                                statement.SourceOffset,
                                branch.TargetOffset,
                                InstructionNormalTransferKind.Branch)
                            : !SameZone(all, offset, branch.TargetOffset))
                            return false;
                        break;
                    case ConditionalBranch conditional:
                        if (exceptionFlow is not null
                            ? !AvailableNormalTransfer(
                                exceptionFlow,
                                statement.SourceOffset,
                                conditional.TargetOffset,
                                InstructionNormalTransferKind.Branch)
                            : !SameZone(
                                all,
                                offset,
                                conditional.TargetOffset))
                            return false;
                        break;
                    case SwitchBranch sw:
                        if (sw.TargetOffsets.Any(target =>
                            exceptionFlow is not null
                                ? !AvailableNormalTransfer(
                                    exceptionFlow,
                                    statement.SourceOffset,
                                    target,
                                    InstructionNormalTransferKind.Branch)
                                : !SameZone(all, offset, target)))
                            return false;
                        break;
                }
            }
        }
        return ValidateCaughtExceptions(blocks, all, exceptionFlow);
    }

    static bool AvailableNormalTransfer(
        InstructionExceptionFlowFacts exceptionFlow,
        int sourceOffset,
        int destinationOffset,
        InstructionNormalTransferKind expectedKind)
    {
        if (sourceOffset < 0)
            return false;

        return exceptionFlow.NormalTransferAt(
            sourceOffset,
            destinationOffset) is
            InstructionExceptionFlowResult<
                InstructionNormalTransfer>.Available available
            && available.Value.Kind == expectedKind;
    }

    /// <summary>
    /// The caught exception may appear exactly two ways: the handler-entry
    /// consumption (a store the transform folds into the clause header, or
    /// the discard pop) and the bare rethrow. Anything else — the value
    /// flowing across blocks, inline consumption — is outside the slice.
    /// </summary>
    static bool ValidateCaughtExceptions(
        IReadOnlyList<Block> blocks,
        List<Construct> all,
        InstructionExceptionFlowFacts? exceptionFlow)
    {
        // Inline (non-entry, non-rethrow) value uses of the caught exception, by
        // handler. A handler that uses the exception inline must use it exactly once
        // for SynthesizeInlineCatchVariables to bind a variable opcode-exactly; see
        // its remarks for why two or more cannot round-trip.
        var inlineUses = new Dictionary<int, int>();
        foreach (var block in blocks)
        {
            if (InsideFilter(
                    all,
                    block.StartOffset,
                    exceptionFlow))
                continue;

            var handler = InnermostCatchHandler(
                all,
                block.StartOffset,
                exceptionFlow);
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

    static bool InsideFilter(
        List<Construct> all,
        int offset,
        InstructionExceptionFlowFacts? exceptionFlow)
        => all.Any(construct => construct.Handlers.Any(handler =>
            handler.Kind == HandlerKind.Filter
            && InFilterAt(
                construct,
                handler,
                offset,
                exceptionFlow)));

    static HandlerRegion? InnermostCatchHandler(
        List<Construct> all,
        int offset,
        InstructionExceptionFlowFacts? exceptionFlow)
    {
        Construct? best = null;
        HandlerRegion? bestHandler = null;
        foreach (var construct in all)
        {
            foreach (var handler in construct.Handlers)
            {
                bool insideCatch =
                    handler.Kind == HandlerKind.Catch
                    && InHandlerAt(
                        construct,
                        handler,
                        offset,
                        exceptionFlow);
                bool insideFilter =
                    handler.Kind == HandlerKind.Filter
                    && (InFilterAt(
                            construct,
                            handler,
                            offset,
                            exceptionFlow)
                        || InHandlerAt(
                            construct,
                            handler,
                            offset,
                            exceptionFlow));
                if (!insideCatch && !insideFilter)
                {
                    continue;
                }
                if (best is null
                    || ContainsConstruct(
                        best,
                        construct,
                        exceptionFlow))
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
    static bool TargetsOutsideContainingConstructs(
        List<Construct> all,
        int offset,
        int targetOffset,
        InstructionExceptionFlowFacts? exceptionFlow)
    {
        bool enclosed = false;
        foreach (var construct in all)
        {
            if (!ContainsAt(construct, offset, exceptionFlow))
                continue;
            enclosed = true;
            if (ContainsAt(construct, targetOffset, exceptionFlow))
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
        int targetOffset,
        InstructionExceptionFlowFacts? exceptionFlow)
    {
        if (!offsetToIndex.TryGetValue(targetOffset, out int targetIndex)
            || !HasPrintableLandingStatement(blocks[targetIndex]))
        {
            return false;
        }

        var source = Zone(all, offset, exceptionFlow);
        var target = Zone(all, targetOffset, exceptionFlow);
        if (source.Construct is null
            || target.Construct is null
            || ReferenceEquals(source.Construct, target.Construct)
            || ContainsAt(
                source.Construct,
                targetOffset,
                exceptionFlow)
            || !ContainsAt(
                target.Construct,
                offset,
                exceptionFlow))
        {
            return false;
        }

        return SegmentWithin(
            target.Construct,
            offset,
            exceptionFlow) == target.Segment;
    }

    static bool HasPrintableLandingStatement(Block block)
        => block.Children.Any(statement => statement is not Leave);

    /// <summary>
    /// True when both offsets sit in the same segment of the same innermost
    /// construct — branches never cross a region boundary in the slice.
    /// </summary>
    static bool SameZone(List<Construct> all, int offsetA, int offsetB)
        => Zone(all, offsetA, exceptionFlow: null)
            == Zone(all, offsetB, exceptionFlow: null);

    static (Construct? Construct, int Segment) Zone(
        List<Construct> all,
        int offset,
        InstructionExceptionFlowFacts? exceptionFlow)
    {
        Construct? best = null;
        foreach (var construct in all)
        {
            if (ContainsAt(construct, offset, exceptionFlow)
                && (best is null
                    || ContainsConstruct(
                        best,
                        construct,
                        exceptionFlow)))
                best = construct;
        }
        if (best is null)
            return (null, -2);
        int segment = SegmentWithin(best, offset, exceptionFlow);
        return (best, segment);
    }

    static int SegmentWithin(
        Construct construct,
        int offset,
        InstructionExceptionFlowFacts? exceptionFlow)
    {
        if (InProtectedAt(construct, offset, exceptionFlow))
            return -1;
        for (int h = 0; h < construct.Handlers.Count; h++)
        {
            var handler = construct.Handlers[h];
            if (InHandlerAt(
                    construct,
                    handler,
                    offset,
                    exceptionFlow))
            {
                return h;
            }
        }
        return -2;
    }

    static bool ContainsAt(
        Construct construct,
        int offset,
        InstructionExceptionFlowFacts? exceptionFlow)
        => exceptionFlow is null
            ? construct.Contains(offset)
            : InProtectedAt(construct, offset, exceptionFlow)
                || construct.Handlers.Any(handler =>
                    InFilterAt(
                        construct,
                        handler,
                        offset,
                        exceptionFlow)
                    || InHandlerAt(
                        construct,
                        handler,
                        offset,
                        exceptionFlow));

    static bool InProtectedAt(
        Construct construct,
        int offset,
        InstructionExceptionFlowFacts? exceptionFlow)
    {
        if (exceptionFlow is null)
            return offset >= construct.TryStart && offset < construct.TryEnd;

        return construct.ProtectedRegion is { } region
            && LocationAt(exceptionFlow, offset).Any(
                location => location.Id == region);
    }

    static bool InFilterAt(
        Construct construct,
        HandlerRegion handler,
        int offset,
        InstructionExceptionFlowFacts? exceptionFlow)
    {
        if (exceptionFlow is null)
        {
            return handler.Kind == HandlerKind.Filter
                && offset >= handler.FilterOffset
                && offset < handler.HandlerOffset;
        }

        return construct.FactsFor(handler)?.FilterRegion is { } region
            && LocationAt(exceptionFlow, offset).Any(
                location => location.Id == region);
    }

    static bool InHandlerAt(
        Construct construct,
        HandlerRegion handler,
        int offset,
        InstructionExceptionFlowFacts? exceptionFlow)
    {
        if (exceptionFlow is null)
        {
            return offset >= handler.HandlerOffset
                && offset < handler.HandlerOffset + handler.HandlerLength;
        }

        return construct.FactsFor(handler) is { } clause
            && LocationAt(exceptionFlow, offset).Any(
                location => location.Id == clause.HandlerRegion);
    }

    static ImmutableArray<InstructionExceptionRegion> LocationAt(
        InstructionExceptionFlowFacts exceptionFlow,
        int offset)
        => exceptionFlow.LocationAt(offset) is
            InstructionExceptionFlowResult<
                ImmutableArray<InstructionExceptionRegion>>.Available available
                ? available.Value
                : [];

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
            node = new TryFinally(tryBody, finallyBody)
            {
                ExceptionClause = construct.FactsFor(handler),
            };
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
                    clauses.Add(new CatchClause(
                        filter.ExceptionType,
                        body,
                        filter.Condition)
                    {
                        VariableIndex = filter.VariableIndex,
                        ExceptionClause = construct.FactsFor(handler),
                    });
                }
                else
                {
                    clauses.Add(new CatchClause(
                        handler.CatchType ?? CatchAllType,
                        body)
                    {
                        VariableIndex = variable,
                        ExceptionClause = construct.FactsFor(handler),
                    });
                }
            }
            node = new TryCatch(tryBody, clauses)
            {
                ExceptionProtectedRegion = construct.ProtectedRegion,
            };
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
