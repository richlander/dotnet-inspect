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
            fieldLoads.Add(new FieldLoadFact(
                context.Method,
                context.Method,
                instruction.Offset,
                token,
                isStatic,
                declaringType,
                name,
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

    sealed partial class StackValueSourceResolver
    {
        static ResolvedValueSet Single(ResolvedValueSource source)
            => new([source], isResolved: true);

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
                        (TypeRef? declaringType, string? name) =
                            _resolver.ResolveFieldOwner(token);
                        return name is null
                            ? ResolvedValueSet.Unresolved
                            : Single(new ResolvedValueSource(
                                ResolvedValueSourceKind.StaticFieldLoad,
                                producerOffset)
                            {
                                Token = token,
                                Type = declaringType,
                                Name = name,
                            });
                    }
                    case ILOpCode.Ldfld:
                    {
                        int token = MethodInstructionFacts.OperandInt32(
                            instruction);
                        (TypeRef? declaringType, string? name) =
                            _resolver.ResolveFieldOwner(token);
                        if (name is null)
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
                            Type = declaringType,
                            Name = name,
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
                    return Single(new ResolvedValueSource(
                        ResolvedValueSourceKind.Argument,
                        producerOffset)
                    {
                        ArgumentIndex = access.Slot,
                    });
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
    }
}
