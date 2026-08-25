using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>
/// Metadata-dependent call-site facts supplied by the assembly reader without
/// exposing its reader or generic decoding scope.
/// </summary>
internal interface IMethodCallResolver
{
    MemberRef ResolveMember(int token);

    MemberRef ResolveIndirectCall(int signatureToken);

    int DefinitionToken(int operandToken);

    string? ResolveUserString(int token);
}

/// <summary>
/// Projects direct and indirect call sites from one shared decoded method body.
/// Safety policy remains owned by <see cref="MethodSafetyAnalysis"/> and is
/// delegated within the same instruction traversal.
/// </summary>
internal static class MethodCallAnalysis
{
    /// <summary>
    /// Appends results incrementally so calls and safety evidence emitted before
    /// a later recoverable metadata failure remain visible to the method-level
    /// diagnostic gate.
    /// </summary>
    internal static void Collect(
        MethodBodyAnalysisContext context,
        IMethodCallResolver resolver,
        Func<int, AllocationMultiplicity> multiplicityAt,
        ImmutableArray<DirectCall>.Builder calls,
        ImmutableArray<UnsafeEvidence>.Builder unsafeEvidence,
        bool includeIndirectOpcodes,
        bool includeCallValueFlow = true,
        ImmutableArray<MethodResultSink>.Builder? resultSinks = null)
    {
        var caller = context.Method;
        ReachingDefinitionsResult? reaching = null;
        foreach (var instruction in context.Instructions.Instructions)
        {
            int offset = instruction.Offset;
            var opcode = instruction.OpCode;
            switch (opcode)
            {
                case ILOpCode.Call:
                case ILOpCode.Callvirt:
                case ILOpCode.Newobj:
                case ILOpCode.Ldftn:
                case ILOpCode.Ldvirtftn:
                {
                    int token =
                        MethodInstructionFacts.OperandInt32(instruction);
                    var callee = resolver.ResolveMember(token);
                    var kind = ToCallKind(opcode);
                    DirectCallResult resultUse =
                        includeCallValueFlow
                            && kind is CallKind.Call
                                or CallKind.CallVirtual
                            ? ClassifyResultUse(
                                context,
                                instruction,
                                callee,
                                ref reaching)
                            : default;
                    calls.Add(new DirectCall(
                        caller,
                        callee,
                        offset,
                        token,
                        resolver.DefinitionToken(token),
                        kind,
                        context.IsInLoopRegion(offset))
                    {
                        Opcode = FormatCallOpcode(opcode),
                        ReturnAddress = instruction.NextOffset,
                        Multiplicity = multiplicityAt(offset),
                        ResultUse = resultUse.Use,
                        ResultConsumerOffset = resultUse.ConsumerOffset,
                    });
                    if (MethodSafetyAnalysis.InspectCall(
                            caller,
                            callee,
                            kind,
                            offset,
                            token)
                        is { } callEvidence)
                    {
                        unsafeEvidence.Add(callEvidence);
                    }
                    break;
                }
                case ILOpCode.Calli:
                {
                    int token =
                        MethodInstructionFacts.OperandInt32(instruction);
                    calls.Add(new DirectCall(
                        caller,
                        resolver.ResolveIndirectCall(token),
                        offset,
                        token,
                        token,
                        CallKind.CallIndirect,
                        context.IsInLoopRegion(offset))
                    {
                        Opcode = FormatCallOpcode(opcode),
                        ReturnAddress = instruction.NextOffset,
                        Multiplicity = multiplicityAt(offset),
                    });
                    unsafeEvidence.Add(
                        MethodSafetyAnalysis.CallIndirect(
                            caller,
                            offset,
                            token));
                    break;
                }
                default:
                    if (MethodSafetyAnalysis.InspectOperation(
                            caller,
                            opcode,
                            offset,
                            includeIndirectOpcodes)
                        is { } operationEvidence)
                    {
                        unsafeEvidence.Add(operationEvidence);
                    }
                    break;
            }
        }

        if (includeCallValueFlow && resultSinks is not null)
        {
            var callsByOffset = calls.ToDictionary(call => call.ILOffset);
            var sources = new StackValueSourceResolver(
                context,
                callsByOffset,
                reaching,
                resolver);
            CollectArgumentSources(calls, sources);
            CollectResultSinks(
                context,
                callsByOffset,
                resultSinks,
                sources);
            reaching = sources.ReachingDefinitions;
        }
    }

    static string FormatCallOpcode(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Callvirt => "callvirt",
        ILOpCode.Newobj => "newobj",
        ILOpCode.Ldftn => "ldftn",
        ILOpCode.Ldvirtftn => "ldvirtftn",
        ILOpCode.Calli => "calli",
        _ => "call",
    };

    static CallKind ToCallKind(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Call => CallKind.Call,
        ILOpCode.Callvirt => CallKind.CallVirtual,
        ILOpCode.Newobj => CallKind.NewObject,
        ILOpCode.Ldftn => CallKind.LoadFunction,
        _ => CallKind.LoadVirtualFunction,
    };

    static DirectCallResult ClassifyResultUse(
        MethodBodyAnalysisContext context,
        DecodedInstruction call,
        MemberRef callee,
        ref ReachingDefinitionsResult? reaching)
    {
        if (callee.ReturnType.Equals(
                TypeRef.CoreLib("System", "Void")))
            return default;

        int nextIndex =
            context.NextNonNopIndexAtOrAfter(call.NextOffset);
        ImmutableArray<DecodedInstruction> instructions =
            context.Instructions.Instructions;
        if (nextIndex >= instructions.Length)
            return default;

        DecodedInstruction next = instructions[nextIndex];
        if (next.OpCode == ILOpCode.Ret)
            return new(DirectCallResultUse.MethodReturn, next.Offset);
        if (next.OpCode == ILOpCode.Pop)
            return new(DirectCallResultUse.Discarded, next.Offset);
        if (!MethodInstructionFacts.TryReadLocalSlot(
                next,
                out LocalSlotAccess store)
            || !store.IsStore
            || store.IsArgument)
        {
            return default;
        }

        int argumentSlotCount =
            context.Method.ParameterTypes.Length
            + (context.Method.IsStatic ? 0 : 1);
        reaching ??= ReachingDefinitions.Analyze(
            context.Instructions,
            argumentSlotCount);
        if (!reaching.IsComplete)
            return default;
        LocalDefinition? definition = reaching.Definitions
            .FirstOrDefault(candidate =>
                candidate.Offset == next.Offset
                && candidate.Slot == store.Slot
                && !candidate.IsArgument);
        if (definition is null)
            return default;

        ImmutableArray<LocalUse> uses = reaching.UsesOf(definition);
        if (uses.Length != 1 || uses[0].Address)
            return default;

        int consumerIndex =
            context.NextNonNopIndexAtOrAfter(
                context.InstructionAt(uses[0].Offset)?.NextOffset
                    ?? uses[0].Offset);
        if (consumerIndex >= instructions.Length)
            return default;

        DecodedInstruction consumer = instructions[consumerIndex];
        return consumer.OpCode switch
        {
            ILOpCode.Ret => new(
                DirectCallResultUse.MethodReturn,
                consumer.Offset),
            ILOpCode.Call or ILOpCode.Callvirt => new(
                DirectCallResultUse.CallArgument,
                consumer.Offset),
            ILOpCode.Pop => new(
                DirectCallResultUse.Discarded,
                consumer.Offset),
            _ => default,
        };
    }

    readonly record struct DirectCallResult(
        DirectCallResultUse Use,
        int? ConsumerOffset);

    static void CollectResultSinks(
        MethodBodyAnalysisContext context,
        IReadOnlyDictionary<int, DirectCall> callsByOffset,
        ImmutableArray<MethodResultSink>.Builder resultSinks,
        StackValueSourceResolver sources)
    {
        foreach (DecodedInstruction instruction
            in context.Instructions.Instructions)
        {
            MethodResultSinkKind kind;
            if (instruction.OpCode == ILOpCode.Ret)
            {
                kind = MethodResultSinkKind.MethodReturn;
            }
            else if (instruction.OpCode is ILOpCode.Call or ILOpCode.Callvirt
                && callsByOffset.TryGetValue(
                    instruction.Offset,
                    out DirectCall? call)
                && call.Callee.ParameterTypes.Length == 1)
            {
                kind = MethodResultSinkKind.SingleArgumentCall;
            }
            else
            {
                continue;
            }

            SinkSources sinkSources = ResolveSinkSources(
                instruction,
                sources);
            resultSinks.Add(new MethodResultSink(
                context.Method,
                context.Method,
                instruction.Offset,
                kind,
                sinkSources.CallOffsets,
                sinkSources.IsComplete));
        }
    }

    static SinkSources ResolveSinkSources(
        DecodedInstruction sink,
        StackValueSourceResolver sources)
    {
        SourceSet source = sources.SinkSource(sink.Offset);
        return new(source.CallOffsets, source.IsComplete);
    }

    static void CollectArgumentSources(
        ImmutableArray<DirectCall>.Builder calls,
        StackValueSourceResolver sources)
    {
        if (!sources.IsComplete)
            return;

        for (int index = 0; index < calls.Count; index++)
        {
            DirectCall call = calls[index];
            if (call.Kind is not (CallKind.Call or CallKind.CallVirtual))
                continue;

            var arguments =
                ImmutableArray.CreateBuilder<CallArgumentSource>(
                    call.Callee.ParameterTypes.Length);
            for (int argument = 0;
                argument < call.Callee.ParameterTypes.Length;
                argument++)
            {
                SourceSet source = sources.CallArgumentSource(
                    call.ILOffset,
                    call.Callee.ParameterTypes.Length,
                    argument);
                arguments.Add(new CallArgumentSource(
                    argument,
                    source.CallOffsets,
                    source.IsComplete));
            }

            CallReceiverSource? receiver = null;
            if (call.Callee.HasThis)
            {
                SourceSet source = sources.CallReceiverSource(
                    call.ILOffset,
                    call.Callee.ParameterTypes.Length);
                receiver = new(
                    source.CallOffsets,
                    source.IsComplete);
            }

            calls[index] = call with
            {
                ArgumentSources = new(
                    arguments.MoveToImmutable()),
                FirstArgumentStringLiteral =
                    call.Callee.ParameterTypes.Length == 0
                        ? null
                        : sources.CallArgumentStringLiteral(
                            call.ILOffset,
                            call.Callee.ParameterTypes.Length,
                            argumentIndex: 0),
                ReceiverSource = receiver,
            };
        }
    }

    static bool IsDirectResultCall(DirectCall call)
        => call.Kind is CallKind.Call or CallKind.CallVirtual
            && IsNonVoid(call);

    sealed class StackValueSourceResolver
    {
        readonly MethodBodyAnalysisContext _context;
        readonly IReadOnlyDictionary<int, DirectCall> _callsByOffset;
        readonly IMethodCallResolver _resolver;
        readonly TypedStackResult _stack;
        ReachingDefinitionsResult? _reaching;

        internal StackValueSourceResolver(
            MethodBodyAnalysisContext context,
            IReadOnlyDictionary<int, DirectCall> callsByOffset,
            ReachingDefinitionsResult? reaching,
            IMethodCallResolver resolver)
        {
            _context = context;
            _callsByOffset = callsByOffset;
            _resolver = resolver;
            _reaching = reaching;
            _stack = context.Instructions.InterpretStack(
                !context.Method.ReturnType.Equals(
                    TypeRef.CoreLib("System", "Void")),
                new CallStackTypeResolver(callsByOffset.Values));
        }

        internal bool IsComplete => _stack.IsComplete;

        internal ReachingDefinitionsResult? ReachingDefinitions
            => _reaching;

        internal SourceSet SinkSource(int sinkOffset)
            => SourceAt(_stack.StackBeforeOffset(sinkOffset), -1);

        internal SourceSet CallArgumentSource(
            int callOffset,
            int parameterCount,
            int argumentIndex)
        {
            ImmutableArray<StackValue> stack =
                _stack.StackBeforeOffset(callOffset);
            int stackIndex = stack.Length - parameterCount + argumentIndex;
            return stackIndex < 0 || stackIndex >= stack.Length
                ? SourceSet.Incomplete
                : Resolve(stack[stackIndex].ProducerOffset, []);
        }

        internal SourceSet CallReceiverSource(
            int callOffset,
            int parameterCount)
        {
            ImmutableArray<StackValue> stack =
                _stack.StackBeforeOffset(callOffset);
            int stackIndex = stack.Length - parameterCount - 1;
            return stackIndex < 0 || stackIndex >= stack.Length
                ? SourceSet.Incomplete
                : Resolve(stack[stackIndex].ProducerOffset, []);
        }

        internal string? CallArgumentStringLiteral(
            int callOffset,
            int parameterCount,
            int argumentIndex)
        {
            ImmutableArray<StackValue> stack =
                _stack.StackBeforeOffset(callOffset);
            int stackIndex =
                stack.Length - parameterCount + argumentIndex;
            return stackIndex < 0 || stackIndex >= stack.Length
                ? null
                : ResolveStringLiteral(
                    stack[stackIndex].ProducerOffset,
                    []);
        }

        SourceSet SourceAt(
            ImmutableArray<StackValue> stack,
            int stackIndex,
            HashSet<int>? resolving = null)
        {
            if (!IsComplete)
                return SourceSet.Incomplete;

            int index = stackIndex < 0
                ? stack.Length + stackIndex
                : stackIndex;
            return index < 0 || index >= stack.Length
                ? SourceSet.Incomplete
                : Resolve(stack[index].ProducerOffset, resolving ?? []);
        }

        SourceSet Resolve(int producerOffset, HashSet<int> resolving)
        {
            if (producerOffset == StackValue.NoProducer
                || !resolving.Add(producerOffset))
            {
                return SourceSet.Incomplete;
            }

            try
            {
                if (_callsByOffset.TryGetValue(
                        producerOffset,
                        out DirectCall? call)
                    && IsDirectResultCall(call))
                {
                    return new([call.ILOffset], IsComplete: true);
                }

                DecodedInstruction? producer =
                    _context.InstructionAt(producerOffset);
                if (producer is not { } instruction
                    || !MethodInstructionFacts.TryReadLocalSlot(
                        instruction,
                        out LocalSlotAccess access)
                    || access.IsStore
                    || access.IsArgument)
                {
                    return SourceSet.Incomplete;
                }

                ReachingDefinitionsResult reaching =
                    _reaching ??= ILInspector.Analysis.ReachingDefinitions.Analyze(
                        _context.Instructions,
                        _context.Method.ParameterTypes.Length
                            + (_context.Method.IsStatic ? 0 : 1));
                if (!reaching.IsComplete)
                    return SourceSet.Incomplete;

                if (reaching.Uses.Any(candidate =>
                    !candidate.IsArgument
                    && candidate.Slot == access.Slot
                    && candidate.Address))
                {
                    return SourceSet.Incomplete;
                }

                LocalUse? use = reaching.Uses.FirstOrDefault(candidate =>
                    !candidate.IsArgument
                    && candidate.Slot == access.Slot
                    && candidate.Offset == instruction.Offset);
                if (use is null
                    || use.Address
                    || use.ReachingDefinitions.IsEmpty)
                {
                    return SourceSet.Incomplete;
                }

                var sourceOffsets = new SortedSet<int>();
                foreach (LocalDefinition definition
                    in use.ReachingDefinitions)
                {
                    SourceSet source = SourceAt(
                        _stack.StackBeforeOffset(definition.Offset),
                        -1,
                        resolving);
                    if (!source.IsComplete)
                        return SourceSet.Incomplete;

                    sourceOffsets.UnionWith(source.CallOffsets);
                }

                return new([.. sourceOffsets], IsComplete: true);
            }
            finally
            {
                resolving.Remove(producerOffset);
            }
        }

        string? ResolveStringLiteral(
            int producerOffset,
            HashSet<int> resolving)
        {
            if (producerOffset == StackValue.NoProducer
                || !resolving.Add(producerOffset))
            {
                return null;
            }

            try
            {
                DecodedInstruction? producer =
                    _context.InstructionAt(producerOffset);
                if (producer is not { } instruction)
                    return null;
                if (instruction.OpCode == ILOpCode.Ldstr)
                {
                    return _resolver.ResolveUserString(
                        MethodInstructionFacts.OperandInt32(
                            instruction));
                }
                if (!MethodInstructionFacts.TryReadLocalSlot(
                        instruction,
                        out LocalSlotAccess access)
                    || access.IsStore
                    || access.IsArgument)
                {
                    return null;
                }

                ReachingDefinitionsResult reaching =
                    _reaching ??=
                        ILInspector.Analysis.ReachingDefinitions
                            .Analyze(
                                _context.Instructions,
                                _context.Method.ParameterTypes.Length
                                    + (_context.Method.IsStatic
                                        ? 0
                                        : 1));
                if (!reaching.IsComplete
                    || reaching.Uses.Any(candidate =>
                        !candidate.IsArgument
                        && candidate.Slot == access.Slot
                        && candidate.Address))
                {
                    return null;
                }

                LocalUse? use =
                    reaching.Uses.FirstOrDefault(candidate =>
                        !candidate.IsArgument
                        && candidate.Slot == access.Slot
                        && candidate.Offset == instruction.Offset);
                if (use is null
                    || use.Address
                    || use.ReachingDefinitions.IsEmpty)
                {
                    return null;
                }

                string? literal = null;
                foreach (LocalDefinition definition
                    in use.ReachingDefinitions)
                {
                    ImmutableArray<StackValue> stack =
                        _stack.StackBeforeOffset(
                            definition.Offset);
                    if (stack.IsEmpty)
                        return null;
                    string? candidate = ResolveStringLiteral(
                        stack[^1].ProducerOffset,
                        resolving);
                    if (candidate is null
                        || literal is not null
                            && !string.Equals(
                                literal,
                                candidate,
                                StringComparison.Ordinal))
                    {
                        return null;
                    }
                    literal = candidate;
                }

                return literal;
            }
            finally
            {
                resolving.Remove(producerOffset);
            }
        }

        sealed class CallStackTypeResolver(
            IEnumerable<DirectCall> calls)
            : IStackTypeResolver
        {
            readonly IReadOnlyDictionary<int, DirectCall> _calls =
                calls
                    .Where(call => call.Kind is
                        CallKind.Call
                        or CallKind.CallVirtual
                        or CallKind.NewObject)
                    .GroupBy(call => call.OperandToken)
                    .ToDictionary(group => group.Key, group => group.First());

            public bool TryResolveCall(
                int methodToken,
                bool isNewObj,
                out int popCount,
                out bool pushes,
                out StackType pushType)
            {
                popCount = -1;
                pushes = false;
                pushType = StackType.Unknown;
                if (!_calls.TryGetValue(methodToken, out DirectCall? call)
                    || isNewObj != (call.Kind == CallKind.NewObject))
                {
                    return false;
                }

                popCount = call.Callee.ParameterTypes.Length
                    + (isNewObj || !call.Callee.HasThis ? 0 : 1);
                pushes = isNewObj || IsNonVoid(call);
                return true;
            }
        }
    }

    static bool IsNonVoid(DirectCall call)
        => !call.Callee.ReturnType.Equals(
            TypeRef.CoreLib("System", "Void"));

    readonly record struct SinkSources(
        ImmutableArray<int> CallOffsets,
        bool IsComplete);

    readonly record struct SourceSet(
        ImmutableArray<int> CallOffsets,
        bool IsComplete)
    {
        internal static SourceSet Incomplete => new([], IsComplete: false);
    }
}
