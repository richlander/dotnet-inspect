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
        bool includeIndirectOpcodes)
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
                        kind is CallKind.Call or CallKind.CallVirtual
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
}
