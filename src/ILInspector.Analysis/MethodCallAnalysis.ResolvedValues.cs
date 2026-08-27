using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.ControlFlow;
using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>
/// The resolved-value half of <see cref="MethodCallAnalysis"/>: block
/// reachability, the <see cref="ResolvedValueSet"/> union, field-store facts,
/// and the two span lowerings the C# compiler emits for a collection-expression
/// argument.
/// </summary>
/// <remarks>
/// Deliberately not a symbolic executor. It sees through exactly the transparent
/// operations the recognized emitted IL uses and returns
/// <see cref="ResolvedValueSet.Unresolved"/> for everything else, so a consumer
/// that requires proof fails closed rather than accepting an approximation.
/// </remarks>
internal static partial class MethodCallAnalysis
{
    /// <summary>
    /// Per-block entry reachability from the shared EH-aware block graph, or a
    /// default array when the graph is incomplete.
    /// </summary>
    /// <remarks>
    /// Runs the existing <see cref="ForwardDataflow"/> kernel with empty
    /// transfers purely for its CFG reachability answer, so EH survivor edges
    /// and filter/handler entries stay consistent with every other block-based
    /// consumer instead of being re-derived here.
    /// </remarks>
    static ImmutableArray<bool> ComputeBlockReachability(
        MethodBodyAnalysisContext context)
    {
        BlockGraph blocks = context.Blocks;
        if (!blocks.IsComplete || blocks.Blocks.IsEmpty)
            return default;

        var edges = new BlockEdges[blocks.Blocks.Length];
        var transfers = new GenKillSet[blocks.Blocks.Length];
        for (int index = 0; index < blocks.Blocks.Length; index++)
        {
            edges[index] = blocks.Blocks[index].Edges;
            transfers[index] = GenKillSet.Empty;
        }

        ForwardDataflowResult result = ForwardDataflow.Solve(
            edges,
            transfers,
            entry: new HashSet<int>(),
            universe: new HashSet<int>(),
            DataflowMerge.Union);

        var reachable =
            ImmutableArray.CreateBuilder<bool>(result.Blocks.Count);
        foreach (DataflowBlockState state in result.Blocks)
            reachable.Add(state.Reachable);
        return reachable.MoveToImmutable();
    }

    static bool? IsReachableAt(
        MethodBodyAnalysisContext context,
        ImmutableArray<bool> reachability,
        int offset)
    {
        if (reachability.IsDefault)
            return null;
        int index = context.Blocks.BlockIndexAt(offset);
        return index < 0 || index >= reachability.Length
            ? null
            : reachability[index];
    }

    /// <summary>
    /// Records one <see cref="FieldStoreFact"/> per physical <c>stsfld</c> or
    /// <c>stfld</c>, including stores whose value stays unresolved, and one
    /// <see cref="FieldLoadFact"/> per physical <c>ldsfld</c> or <c>ldfld</c>.
    /// </summary>
    static void CollectFieldAccesses(
        MethodBodyAnalysisContext context,
        IMethodCallResolver resolver,
        StackValueSourceResolver sources,
        ImmutableArray<bool> reachability,
        ImmutableArray<FieldStoreFact>.Builder fieldStores,
        ImmutableArray<FieldLoadFact>.Builder fieldLoads)
    {
        foreach (DecodedInstruction instruction
            in context.Instructions.Instructions)
        {
            switch (instruction.OpCode)
            {
                case ILOpCode.Stsfld:
                case ILOpCode.Stfld:
                    CollectFieldStore(instruction);
                    break;
                case ILOpCode.Ldsfld:
                case ILOpCode.Ldfld:
                    CollectFieldLoad(instruction);
                    break;
                default:
                    break;
            }
        }

        void CollectFieldStore(DecodedInstruction instruction)
        {
            bool isStatic = instruction.OpCode == ILOpCode.Stsfld;
            int token = MethodInstructionFacts.OperandInt32(instruction);
            (TypeRef? declaringType, string? name) =
                resolver.ResolveFieldOwner(token);
            FieldIdentity? fieldIdentity =
                resolver.ResolveFieldIdentity(token);
            ResolvedValueSet value = sources.ResolveStackSlot(
                instruction.Offset,
                depthFromTop: 0);
            fieldStores.Add(new FieldStoreFact(
                context.Method,
                context.Method,
                instruction.Offset,
                token,
                isStatic,
                declaringType,
                name,
                fieldIdentity,
                isStatic
                    ? -1
                    : ReceiverArgument(instruction.Offset, depthFromTop: 1),
                value,
                IsReachableAt(context, reachability, instruction.Offset)));
        }

        void CollectFieldLoad(DecodedInstruction instruction)
        {
            bool isStatic = instruction.OpCode == ILOpCode.Ldsfld;
            int token = MethodInstructionFacts.OperandInt32(instruction);
            (TypeRef? declaringType, string? name) =
                resolver.ResolveFieldOwner(token);
            FieldIdentity? fieldIdentity =
                resolver.ResolveFieldIdentity(token);
            fieldLoads.Add(new FieldLoadFact(
                context.Method,
                context.Method,
                instruction.Offset,
                token,
                isStatic,
                declaringType,
                name,
                fieldIdentity,
                isStatic
                    ? -1
                    : ReceiverArgument(instruction.Offset, depthFromTop: 0),
                IsReachableAt(context, reachability, instruction.Offset)));
        }

        int ReceiverArgument(int offset, int depthFromTop)
            => sources.ResolveStackSlot(offset, depthFromTop).Single is
                { Kind: ResolvedValueSourceKind.Argument } argument
                ? argument.ArgumentIndex
                : -1;
    }

    /// <summary>
    /// Fills the resolved-value union, the receiver value, and any recognized
    /// span-argument elements on every collected call.
    /// </summary>
    static void CollectResolvedValues(
        ImmutableArray<DirectCall>.Builder calls,
        StackValueSourceResolver sources)
    {
        if (!sources.IsComplete)
            return;

        for (int index = 0; index < calls.Count; index++)
        {
            DirectCall call = calls[index];
            if (call.Kind is not (CallKind.Call
                or CallKind.CallVirtual
                or CallKind.NewObject))
            {
                continue;
            }

            int parameterCount = call.Callee.ParameterTypes.Length;
            var values =
                ImmutableArray.CreateBuilder<ResolvedValueSet>(parameterCount);
            var spans =
                ImmutableArray.CreateBuilder<SpanArgumentElements>();
            for (int argument = 0; argument < parameterCount; argument++)
            {
                values.Add(sources.ResolveArgumentValue(
                    call.ILOffset,
                    parameterCount,
                    argument));
                if (sources.ResolveSpanArgument(
                        call.ILOffset,
                        parameterCount,
                        argument)
                    is { } span)
                {
                    spans.Add(span);
                }
            }

            ResolvedValueSet? receiver =
                call.Kind is not CallKind.NewObject && call.Callee.HasThis
                    ? sources.ResolveReceiverValue(
                        call.ILOffset,
                        parameterCount)
                    : null;

            calls[index] = call with
            {
                ResolvedArgumentValues =
                    new(values.MoveToImmutable()),
                ResolvedReceiverValue = receiver,
                SpanArgumentSources = spans.Count == 0
                    ? SpanArgumentSources.Empty
                    : new(spans.ToImmutable()),
            };
        }
    }

    /// <summary>
    /// Marks calls whose block dominates every reachable ordinary
    /// <c>ret</c>, provided the body has no reachable <c>jmp</c> completion.
    /// </summary>
    static void CollectNormalReturnDominance(
        MethodBodyAnalysisContext context,
        ImmutableArray<bool> reachability,
        ImmutableArray<DirectCall>.Builder calls)
    {
        if (reachability.IsDefaultOrEmpty)
            return;

        int[] returnBlocks =
        [
            .. context.Instructions.Instructions
                .Where(instruction =>
                    instruction.OpCode == ILOpCode.Ret
                    && IsReachableAt(
                        context,
                        reachability,
                        instruction.Offset) == true)
                .Select(instruction =>
                    context.Blocks.BlockIndexAt(instruction.Offset))
                .Where(index => index >= 0)
                .Distinct(),
        ];
        bool hasJumpCompletion =
            context.Instructions.Instructions.Any(instruction =>
                instruction.OpCode == ILOpCode.Jmp
                && IsReachableAt(
                    context,
                    reachability,
                    instruction.Offset) == true);
        if (returnBlocks.Length == 0 || hasJumpCompletion)
            return;

        var dominators = Dominators.Of(
            context.Blocks.Blocks
                .Select(static block => block.Edges)
                .ToArray());
        for (int index = 0; index < calls.Count; index++)
        {
            DirectCall call = calls[index];
            int callBlock = context.Blocks.BlockIndexAt(call.ILOffset);
            if (call.IsReachable == true
                && callBlock >= 0
                && returnBlocks.All(returnBlock =>
                    dominators.Dominates(callBlock, returnBlock)))
            {
                calls[index] = call with
                {
                    DominatesEveryNormalReturn = true,
                };
            }
        }
    }

    /// <summary>
    /// Projects one <see cref="MethodReturnFlow"/> for a non-void body: the union of proven
    /// producers across every reachable <c>ret</c>.
    /// </summary>
    /// <remarks>
    /// Emitted for non-void bodies only, and always emitted for those — an unproven return
    /// produces an unresolved fact rather than no fact, so a consumer sees the failure instead
    /// of an absent row it could read as "nothing to check".
    /// </remarks>
    static void CollectReturnFlow(
        MethodBodyAnalysisContext context,
        ImmutableArray<bool> reachability,
        StackValueSourceResolver sources,
        ImmutableArray<MethodReturnFlow>.Builder returnFlows)
    {
        if (IsCoreVoid(context.Method.ReturnType))
            return;

        var offsets = ImmutableArray.CreateBuilder<int>();
        var merged = new List<ResolvedValueSource>();
        bool resolved = true;
        foreach (DecodedInstruction instruction
            in context.Instructions.Instructions)
        {
            if (instruction.OpCode == ILOpCode.Jmp)
            {
                bool? jumpReachable =
                    IsReachableAt(
                        context,
                        reachability,
                        instruction.Offset);
                if (jumpReachable != false)
                    resolved = false;
                continue;
            }
            if (instruction.OpCode != ILOpCode.Ret)
                continue;
            bool? reachable =
                IsReachableAt(context, reachability, instruction.Offset);
            if (reachable == false)
                continue;
            offsets.Add(instruction.Offset);
            if (reachable != true)
            {
                resolved = false;
                continue;
            }

            ResolvedValueSet value =
                sources.ResolveReturnedValue(instruction.Offset);
            if (!value.IsResolved)
            {
                resolved = false;
                continue;
            }

            foreach (ResolvedValueSource candidate in value.Sources)
            {
                if (!merged.Contains(candidate))
                    merged.Add(candidate);
            }
        }

        if (merged.Count == 0)
            resolved = false;

        merged.Sort(static (left, right) =>
        {
            int byOffset = left.ILOffset.CompareTo(right.ILOffset);
            return byOffset != 0
                ? byOffset
                : ((int)left.Kind).CompareTo((int)right.Kind);
        });
        returnFlows.Add(new MethodReturnFlow(
            context.Method,
            context.Method,
            offsets.ToImmutable(),
            resolved
                ? new([.. merged], isResolved: true)
                : ResolvedValueSet.Unresolved));
    }

    static bool IsCoreVoid(TypeRef type)
        => type.Equals(TypeRef.CoreLib("System", "Void"));

    sealed partial class StackValueSourceResolver
    {
        HashSet<int>? _addressedArgumentSlots;
        Dictionary<int, LocalUse>? _argumentUsesByOffset;

        static ResolvedValueSet Single(ResolvedValueSource source)
            => new([source], isResolved: true);

        /// <summary>
        /// The proven producers of the value one <c>ret</c> hands back, expanded across a
        /// control-flow merge when the evaluation-stack join left no single producer.
        /// </summary>
        internal ResolvedValueSet ResolveReturnedValue(int returnOffset)
        {
            if (!IsComplete)
                return ResolvedValueSet.Unresolved;

            ImmutableArray<StackValue> stack =
                _stack.StackBeforeOffset(returnOffset);
            if (stack.IsEmpty)
                return ResolvedValueSet.Unresolved;

            int slotIndex = stack.Length - 1;
            if (stack[slotIndex].ProducerOffset != StackValue.NoProducer)
                return ResolveValue(stack[slotIndex].ProducerOffset, []);

            // The join collapsed the producer. The alternatives are whatever each reachable
            // predecessor left in that slot, so walk them instead of guessing.
            int blockIndex = _context.Blocks.BlockIndexAt(returnOffset);
            return blockIndex < 0
                    || !IsSlotFromBlockEntry(blockIndex, stack, slotIndex)
                ? ResolvedValueSet.Unresolved
                : ResolveMergedSlot(blockIndex, slotIndex, [], []);
        }

        /// <summary>
        /// Unions the resolved values every reachable predecessor of
        /// <paramref name="blockIndex"/> leaves in stack slot <paramref name="slotIndex"/>.
        /// </summary>
        ResolvedValueSet ResolveMergedSlot(
            int blockIndex,
            int slotIndex,
            HashSet<int> visitedBlocks,
            HashSet<int> resolving)
        {
            if (IsExceptionEntryBlock(blockIndex)
                || !visitedBlocks.Add(blockIndex))
                return ResolvedValueSet.Unresolved;

            ImmutableArray<int> predecessors =
                PredecessorsOf(blockIndex);
            if (predecessors.IsEmpty)
                return ResolvedValueSet.Unresolved;

            var merged = new List<ResolvedValueSource>();
            foreach (int predecessor in predecessors)
            {
                ImmutableArray<StackValue> exit =
                    _stack.BlockExitAt(predecessor);
                if (exit.IsDefault)
                    continue; // Never reached, so it contributes no runtime value.
                if (slotIndex >= exit.Length)
                    return ResolvedValueSet.Unresolved;

                ResolvedValueSet value =
                    exit[slotIndex].ProducerOffset != StackValue.NoProducer
                        ? ResolveValue(
                            exit[slotIndex].ProducerOffset,
                            resolving)
                        : IsSlotFromBlockEntry(predecessor, exit, slotIndex)
                            ? ResolveMergedSlot(
                                predecessor,
                                slotIndex,
                                visitedBlocks,
                                resolving)
                            : ResolvedValueSet.Unresolved;
                if (!value.IsResolved)
                    return ResolvedValueSet.Unresolved;

                foreach (ResolvedValueSource candidate in value.Sources)
                {
                    if (!merged.Contains(candidate))
                        merged.Add(candidate);
                }
            }

            return merged.Count == 0
                ? ResolvedValueSet.Unresolved
                : new([.. merged], isResolved: true);
        }

        /// <summary>
        /// Whether <paramref name="observed"/> still holds in slot
        /// <paramref name="slotIndex"/> the value the block was entered with, which is what
        /// makes the block's own entry merge the right place to keep walking.
        /// </summary>
        /// <remarks>
        /// The slot is compared, not merely the block's instructions, so a block that pops the
        /// merged value and pushes an unrelated one in its place cannot be mistaken for a
        /// pass-through. Every in-block push records its producer, so a slot that is still
        /// <see cref="StackValue.NoProducer"/> and equal to the entry value came from the join.
        /// </remarks>
        bool IsSlotFromBlockEntry(
            int blockIndex,
            ImmutableArray<StackValue> observed,
            int slotIndex)
        {
            if (blockIndex < 0
                || blockIndex >= _context.Blocks.Blocks.Length
                || observed.IsDefault
                || slotIndex >= observed.Length)
            {
                return false;
            }

            return _stack.StackBefore.TryGetValue(
                    _context.Blocks.Blocks[blockIndex].Start,
                    out ImmutableArray<StackValue> entry)
                && slotIndex < entry.Length
                && observed[slotIndex] == entry[slotIndex];
        }

        ImmutableArray<int> PredecessorsOf(int blockIndex)
        {
            if (_predecessors.IsDefault)
            {
                var builders =
                    new List<int>[_context.Blocks.Blocks.Length];
                for (int index = 0;
                    index < _context.Blocks.Blocks.Length;
                    index++)
                {
                    foreach (int successor
                        in _context.Blocks.Blocks[index].Edges.Successors)
                    {
                        if (successor < 0 || successor >= builders.Length)
                            continue;
                        (builders[successor] ??= []).Add(index);
                    }
                }

                _predecessors =
                    [.. builders.Select(static list =>
                        list is null
                            ? ImmutableArray<int>.Empty
                            : [.. list])];
            }

            return blockIndex >= 0 && blockIndex < _predecessors.Length
                ? _predecessors[blockIndex]
                : [];
        }

        bool IsExceptionEntryBlock(int blockIndex)
        {
            if (blockIndex < 0
                || blockIndex >= _context.Blocks.Blocks.Length)
            {
                return false;
            }

            int start = _context.Blocks.Blocks[blockIndex].Start;
            return _context.Blocks.Regions.Any(region =>
                start == region.HandlerStart
                || region.Kind == HandlerKind.Filter
                    && start == region.FilterStart);
        }

        ImmutableArray<ImmutableArray<int>> _predecessors;

        internal ResolvedValueSet ResolveArgumentValue(
            int callOffset,
            int parameterCount,
            int argumentIndex)
        {
            ImmutableArray<StackValue> stack =
                _stack.StackBeforeOffset(callOffset);
            int stackIndex = stack.Length - parameterCount + argumentIndex;
            return stackIndex < 0 || stackIndex >= stack.Length
                ? ResolvedValueSet.Unresolved
                : ResolveValue(stack[stackIndex].ProducerOffset, []);
        }

        internal ResolvedValueSet ResolveReceiverValue(
            int callOffset,
            int parameterCount)
        {
            ImmutableArray<StackValue> stack =
                _stack.StackBeforeOffset(callOffset);
            int stackIndex = stack.Length - parameterCount - 1;
            return stackIndex < 0 || stackIndex >= stack.Length
                ? ResolvedValueSet.Unresolved
                : ResolveValue(stack[stackIndex].ProducerOffset, []);
        }

        /// <summary>
        /// The resolved value sitting <paramref name="depthFromTop"/> slots below
        /// the top of the evaluation stack entering <paramref name="offset"/>.
        /// </summary>
        internal ResolvedValueSet ResolveStackSlot(
            int offset,
            int depthFromTop)
            => ResolveStackSlot(offset, depthFromTop, []);

        ResolvedValueSet ResolveStackSlot(
            int offset,
            int depthFromTop,
            HashSet<int> resolving)
        {
            ImmutableArray<StackValue> stack =
                _stack.StackBeforeOffset(offset);
            int index = stack.Length - 1 - depthFromTop;
            return index < 0 || index >= stack.Length
                ? ResolvedValueSet.Unresolved
                : ResolveValue(stack[index].ProducerOffset, resolving);
        }

        ResolvedValueSet ResolveValue(
            int producerOffset,
            HashSet<int> resolving)
        {
            if (!IsComplete
                || producerOffset == StackValue.NoProducer
                || !resolving.Add(producerOffset))
            {
                return ResolvedValueSet.Unresolved;
            }

            try
            {
                if (_context.InstructionAt(producerOffset)
                    is not { } instruction)
                {
                    return ResolvedValueSet.Unresolved;
                }

                if (_callsByOffset.TryGetValue(
                        producerOffset,
                        out DirectCall? call))
                {
                    return ResolveCallValue(call, producerOffset);
                }

                switch (instruction.OpCode)
                {
                    case >= ILOpCode.Ldc_i4_m1 and <= ILOpCode.Ldc_i4_8:
                        return Single(new ResolvedValueSource(
                            ResolvedValueSourceKind.Int32Literal,
                            producerOffset)
                        {
                            Int32Value =
                                instruction.OpCode - ILOpCode.Ldc_i4_0,
                        });
                    case ILOpCode.Ldc_i4_s:
                    case ILOpCode.Ldc_i4:
                        return Single(new ResolvedValueSource(
                            ResolvedValueSourceKind.Int32Literal,
                            producerOffset)
                        {
                            Int32Value = MethodInstructionFacts.OperandInt32(
                                instruction),
                        });
                    case ILOpCode.Ldnull:
                        return Single(new ResolvedValueSource(
                            ResolvedValueSourceKind.NullReference,
                            producerOffset));
                    case ILOpCode.Ldstr:
                    {
                        int token = MethodInstructionFacts.OperandInt32(
                            instruction);
                        string? text = _resolver.ResolveUserString(token);
                        return text is null
                            ? ResolvedValueSet.Unresolved
                            : Single(new ResolvedValueSource(
                                ResolvedValueSourceKind.StringLiteral,
                                producerOffset)
                            {
                                Token = token,
                                StringValue = text,
                            });
                    }
                    case ILOpCode.Ldtoken:
                    {
                        int token = MethodInstructionFacts.OperandInt32(
                            instruction);
                        TypeRef type = _resolver.ResolveType(token);
                        return type.Kind == TypeRefKind.Unsupported
                            ? ResolvedValueSet.Unresolved
                            : Single(new ResolvedValueSource(
                                ResolvedValueSourceKind.TypeHandle,
                                producerOffset)
                            {
                                Token = token,
                                Type = type,
                            });
                    }
                    case ILOpCode.Ldsfld:
                    {
                        int token = MethodInstructionFacts.OperandInt32(
                            instruction);
                        FieldIdentity? field =
                            _resolver.ResolveFieldIdentity(token);
                        return field is null
                            ? ResolvedValueSet.Unresolved
                            : Single(new ResolvedValueSource(
                                ResolvedValueSourceKind.StaticFieldLoad,
                                producerOffset)
                            {
                                Token = token,
                                Type = field.DeclaringType,
                                Name = field.Name,
                                FieldIdentity = field,
                            });
                    }
                    case ILOpCode.Ldfld:
                    {
                        int token = MethodInstructionFacts.OperandInt32(
                            instruction);
                        FieldIdentity? field =
                            _resolver.ResolveFieldIdentity(token);
                        if (field is null)
                            return ResolvedValueSet.Unresolved;
                        ResolvedValueSet receiver = ResolveStackSlot(
                            producerOffset,
                            depthFromTop: 0,
                            resolving);
                        if (receiver.Single is not
                            { Kind: ResolvedValueSourceKind.Argument } slot)
                        {
                            return ResolvedValueSet.Unresolved;
                        }
                        return Single(new ResolvedValueSource(
                            ResolvedValueSourceKind.InstanceFieldLoad,
                            producerOffset)
                        {
                            Token = token,
                            Type = field.DeclaringType,
                            Name = field.Name,
                            FieldIdentity = field,
                            ArgumentIndex = slot.ArgumentIndex,
                        });
                    }
                    // Transparent: the verifier-visible type changes but the
                    // reference does not, and the duplicate is the same value.
                    case ILOpCode.Castclass:
                    case ILOpCode.Dup:
                        return ResolveStackSlot(
                            producerOffset,
                            depthFromTop: 0,
                            resolving);
                    default:
                        break;
                }

                if (!MethodInstructionFacts.TryReadLocalSlot(
                        instruction,
                        out LocalSlotAccess access)
                    || access.IsStore)
                {
                    return ResolvedValueSet.Unresolved;
                }

                if (access.IsArgument)
                {
                    return IsOriginalArgumentLoad(
                        access.Slot,
                        instruction.Offset)
                        ? Single(new ResolvedValueSource(
                            ResolvedValueSourceKind.Argument,
                            producerOffset)
                        {
                            ArgumentIndex = access.Slot,
                        })
                        : ResolvedValueSet.Unresolved;
                }

                return ResolveLocalLoad(instruction, access, resolving);
            }
            catch (OverflowException)
            {
                return ResolvedValueSet.Unresolved;
            }
            finally
            {
                resolving.Remove(producerOffset);
            }
        }

        ResolvedValueSet ResolveCallValue(DirectCall call, int producerOffset)
        {
            if (call.Kind is CallKind.NewObject)
            {
                return Single(new ResolvedValueSource(
                    ResolvedValueSourceKind.NewObjectResult,
                    producerOffset)
                {
                    Token = call.OperandToken,
                    Type = call.Callee.DeclaringType,
                });
            }

            return call.Kind is CallKind.Call or CallKind.CallVirtual
                && IsNonVoid(call)
                    ? Single(new ResolvedValueSource(
                        ResolvedValueSourceKind.CallResult,
                        producerOffset)
                    {
                        Token = call.OperandToken,
                        Type = call.Callee.ReturnType,
                        Name = call.Callee.Name,
                    })
                    : ResolvedValueSet.Unresolved;
        }

        /// <summary>
        /// Merges the resolved values of every definition reaching an
        /// unaddressed local load. Any addressed use of the slot anywhere in the
        /// body leaves the load unresolved, because a byref write cannot be
        /// attributed without alias analysis.
        /// </summary>
        ResolvedValueSet ResolveLocalLoad(
            DecodedInstruction instruction,
            LocalSlotAccess access,
            HashSet<int> resolving)
        {
            ReachingDefinitionsResult reaching = EnsureReachingDefinitions();
            if (!reaching.IsComplete
                || reaching.Uses.Any(candidate =>
                    !candidate.IsArgument
                    && candidate.Slot == access.Slot
                    && candidate.Address))
            {
                return ResolvedValueSet.Unresolved;
            }

            LocalUse? use = reaching.Uses.FirstOrDefault(candidate =>
                !candidate.IsArgument
                && candidate.Slot == access.Slot
                && candidate.Offset == instruction.Offset);
            if (use is null
                || use.Address
                || use.ReachingDefinitions.IsEmpty)
            {
                return ResolvedValueSet.Unresolved;
            }

            var merged = new List<ResolvedValueSource>();
            foreach (LocalDefinition definition in use.ReachingDefinitions)
            {
                ResolvedValueSet source = ResolveStackSlot(
                    definition.Offset,
                    depthFromTop: 0,
                    resolving);
                if (!source.IsResolved)
                    return ResolvedValueSet.Unresolved;
                foreach (ResolvedValueSource candidate in source.Sources)
                {
                    if (!merged.Contains(candidate))
                        merged.Add(candidate);
                }
            }

            merged.Sort(static (left, right) =>
            {
                int byOffset = left.ILOffset.CompareTo(right.ILOffset);
                return byOffset != 0
                    ? byOffset
                    : ((int)left.Kind).CompareTo((int)right.Kind);
            });
            return new([.. merged], isResolved: true);
        }

        ReachingDefinitionsResult EnsureReachingDefinitions()
            => _reaching ??= ILInspector.Analysis.ReachingDefinitions.Analyze(
                _context.Instructions,
                _context.Method.ParameterTypes.Length
                    + (_context.Method.IsStatic ? 0 : 1));

        /// <summary>
        /// Whether this exact <c>ldarg</c> still holds the incoming argument: its only reaching
        /// definition is the entry definition <see cref="ReachingDefinitions"/> seeds at
        /// <see cref="ILInspector.Analysis.ReachingDefinitions.EntryDefinitionOffset"/>, and the
        /// slot is never addressed anywhere in the body.
        /// </summary>
        /// <remarks>
        /// A <c>starg</c> rewrites the slot, a merge can carry either value, and an
        /// <c>ldarga</c> lets a byref write land without a visible store — none of which can be
        /// attributed without alias analysis, so all three stay unresolved. An <c>ldarga</c>
        /// itself pushes a managed pointer rather than the argument, and is rejected by the same
        /// addressed-slot test. <c>MethodCallResolvedValueTests.LeavesReassignedArgumentValuesUnresolved</c>,
        /// <c>LeavesMergedArgumentValuesUnresolved</c>, and
        /// <c>LeavesAddressedArgumentValuesUnresolved</c> gate the boundary.
        /// </remarks>
        bool IsOriginalArgumentLoad(int slot, int offset)
        {
            ReachingDefinitionsResult reaching = EnsureReachingDefinitions();
            if (!reaching.IsComplete)
                return false;

            EnsureArgumentUseIndex(reaching);
            return !_addressedArgumentSlots!.Contains(slot)
                && _argumentUsesByOffset!.TryGetValue(offset, out LocalUse? use)
                && use.Slot == slot
                && !use.Address
                && use.ReachingDefinitions is
                    [
                        {
                            Offset:
                                ILInspector.Analysis.ReachingDefinitions
                                    .EntryDefinitionOffset,
                        },
                    ];
        }

        void EnsureArgumentUseIndex(ReachingDefinitionsResult reaching)
        {
            if (_argumentUsesByOffset is not null)
                return;

            var addressed = new HashSet<int>();
            var usesByOffset = new Dictionary<int, LocalUse>();
            foreach (LocalUse use in reaching.Uses)
            {
                if (!use.IsArgument)
                    continue;
                if (use.Address)
                    addressed.Add(use.Slot);
                usesByOffset[use.Offset] = use;
            }

            _addressedArgumentSlots = addressed;
            _argumentUsesByOffset = usesByOffset;
        }
    }
}
