using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>The proven effect of one use of a carried resource obligation.</summary>
public enum ResourceOwnershipUseKind
{
    Released,
    Stored,
    ReturnedToCaller,
    Forwarded,
}

/// <summary>
/// One body-local ownership effect. Empty resource kinds make the effect
/// resource-neutral; a non-empty domain constrains a terminal effect.
/// </summary>
public sealed record ResourceOwnershipUse(
    ResourceOwnershipUseKind Kind,
    int ILOffset,
    ImmutableArray<ResourceOccurrenceResourceKind> ResourceKinds,
    DirectCall? Call = null,
    int CalleeParameterIndex = -1)
{
    ImmutableArray<ResourceOccurrenceResourceKind> _resourceKinds =
        ImmutableArrayValueEquality.RequireInitialized(
            ResourceKinds,
            nameof(ResourceKinds));

    public ImmutableArray<ResourceOccurrenceResourceKind> ResourceKinds
    {
        get => _resourceKinds;
        init => _resourceKinds =
            ImmutableArrayValueEquality.RequireInitialized(
                value,
                nameof(ResourceKinds));
    }

    public bool IsForwarded =>
        Kind == ResourceOwnershipUseKind.Forwarded;
}

/// <summary>
/// Body-local flow rooted at one owner-issued resource acquisition.
/// </summary>
public sealed record ResourceOwnershipAcquisitionFlow(
    ResourceOccurrenceRoot.Acquisition Obligation,
    ImmutableArray<ResourceOwnershipUse> Uses,
    bool IsComplete)
{
    ImmutableArray<ResourceOwnershipUse> _uses =
        ImmutableArrayValueEquality.RequireInitialized(
            Uses,
            nameof(Uses));

    public ImmutableArray<ResourceOwnershipUse> Uses
    {
        get => _uses;
        init => _uses = ImmutableArrayValueEquality.RequireInitialized(
            value,
            nameof(Uses));
    }
}

/// <summary>
/// Resource-neutral body-local flow rooted at one incoming parameter.
/// </summary>
public sealed record ResourceOwnershipParameterFlow(
    int ParameterIndex,
    ImmutableArray<ResourceOwnershipUse> Uses,
    bool IsComplete)
{
    ImmutableArray<ResourceOwnershipUse> _uses =
        ImmutableArrayValueEquality.RequireInitialized(
            Uses,
            nameof(Uses));

    public ImmutableArray<ResourceOwnershipUse> Uses
    {
        get => _uses;
        init => _uses = ImmutableArrayValueEquality.RequireInitialized(
            value,
            nameof(Uses));
    }
}

/// <summary>
/// Detached method-local evidence for interprocedural ownership composition.
/// It contains no IL, control-flow, or reaching-definitions state.
/// </summary>
public sealed record ResourceOwnershipMethodSummary(
    MethodIdentity Method,
    MemberRef Member,
    ImmutableArray<ResourceOwnershipAcquisitionFlow> Acquisitions,
    ImmutableArray<ResourceOwnershipParameterFlow> Parameters,
    bool IsComplete)
{
    ImmutableArray<ResourceOwnershipAcquisitionFlow> _acquisitions =
        ImmutableArrayValueEquality.RequireInitialized(
            Acquisitions,
            nameof(Acquisitions));

    ImmutableArray<ResourceOwnershipParameterFlow> _parameters =
        ImmutableArrayValueEquality.RequireInitialized(
            Parameters,
            nameof(Parameters));

    public ImmutableArray<ResourceOwnershipAcquisitionFlow> Acquisitions
    {
        get => _acquisitions;
        init => _acquisitions =
            ImmutableArrayValueEquality.RequireInitialized(
                value,
                nameof(Acquisitions));
    }

    public ImmutableArray<ResourceOwnershipParameterFlow> Parameters
    {
        get => _parameters;
        init => _parameters =
            ImmutableArrayValueEquality.RequireInitialized(
                value,
                nameof(Parameters));
    }
}

internal static class ResourceOwnershipSummaryAnalysis
{
    internal static ResourceOwnershipMethodSummary Unavailable(
        MethodBodyAnalysisContext context) =>
        new(
            context.Method,
            CallTreeMember.FromDefinition(context.Method),
            [],
            [
                .. Enumerable.Range(
                        0,
                        context.Method.ParameterTypes.Length)
                    .Select(index =>
                        new ResourceOwnershipParameterFlow(
                            index,
                            [],
                            IsComplete: false)),
            ],
            IsComplete: false);

    internal static ResourceOwnershipMethodSummary Analyze(
        MethodBodyAnalysisContext context,
        ResourceOccurrenceAnalysisResult occurrences,
        ImmutableArray<DirectCall> directCalls)
    {
        MethodIdentity method = context.Method;
        MemberRef member = CallTreeMember.FromDefinition(method);
        if (!context.Blocks.IsComplete)
        {
            return Incomplete(
                method,
                member,
                occurrences);
        }

        ReachingDefinitionsResult reaching =
            ReachingDefinitions.Analyze(
                context.Instructions,
                method.ParameterTypes.Length
                    + (method.IsStatic ? 0 : 1));
        if (!reaching.IsComplete)
        {
            return Incomplete(
                method,
                member,
                occurrences);
        }

        IReadOnlyDictionary<int, DirectCall> calls =
            directCalls
                .Where(call =>
                    call.EvidenceMethod == method
                    && IsDirectInvocation(call))
                .ToDictionary(static call => call.ILOffset);
        IReadOnlyDictionary<int, MemberRef> members =
            calls.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Callee);

        ImmutableArray<ResourceOwnershipAcquisitionFlow> acquisitions =
        [
            .. occurrences.Roots
                .OfType<ResourceOccurrenceRoot.Acquisition>()
                .OrderBy(static root => root.Call.ILOffset)
                .Select(root =>
                    AnalyzeAcquisition(
                        root,
                        context,
                        reaching,
                        calls,
                        members,
                        occurrences)),
        ];

        var parameters =
            ImmutableArray.CreateBuilder<ResourceOwnershipParameterFlow>(
                method.ParameterTypes.Length);
        for (int parameterIndex = 0;
            parameterIndex < method.ParameterTypes.Length;
            parameterIndex++)
        {
            int argumentSlot =
                parameterIndex + (method.IsStatic ? 0 : 1);
            LocalDefinition? definition =
                reaching.Definitions.SingleOrDefault(candidate =>
                    candidate.IsArgument
                    && candidate.Slot == argumentSlot
                    && candidate.Offset == -1);
            if (definition is null)
            {
                parameters.Add(
                    new(
                        parameterIndex,
                        [],
                        IsComplete: false));
                continue;
            }

            parameters.Add(
                new(
                    parameterIndex,
                    AnalyzeDefinition(
                        definition,
                        argumentSlot,
                        isArgument: true,
                        obligation: null,
                        context,
                        reaching,
                        calls,
                        members,
                        occurrences,
                        out bool complete),
                    complete));
        }

        return new(
            method,
            member,
            acquisitions,
            parameters.MoveToImmutable(),
            IsComplete: true);
    }

    static ResourceOwnershipMethodSummary Incomplete(
        MethodIdentity method,
        MemberRef member,
        ResourceOccurrenceAnalysisResult occurrences) =>
        new(
            method,
            member,
            [
                .. occurrences.Roots
                    .OfType<ResourceOccurrenceRoot.Acquisition>()
                    .OrderBy(static root => root.Call.ILOffset)
                    .Select(root =>
                        new ResourceOwnershipAcquisitionFlow(
                            root,
                            [],
                            IsComplete: false)),
            ],
            [
                .. Enumerable.Range(0, method.ParameterTypes.Length)
                    .Select(index =>
                        new ResourceOwnershipParameterFlow(
                            index,
                            [],
                            IsComplete: false)),
            ],
            IsComplete: false);

    static ResourceOwnershipAcquisitionFlow AnalyzeAcquisition(
        ResourceOccurrenceRoot.Acquisition root,
        MethodBodyAnalysisContext context,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, DirectCall> calls,
        IReadOnlyDictionary<int, MemberRef> members,
        ResourceOccurrenceAnalysisResult occurrences)
    {
        if (!OwnershipValueFlowInstructions.TryFindNextNonNop(
                context.Instructions.Instructions,
                root.Call.ILOffset + 1,
                out DecodedInstruction store)
            || !OwnershipValueFlowInstructions.TryReadStoreLocal(
                store,
                out int slot))
        {
            return new(root, [], IsComplete: false);
        }

        LocalDefinition? definition =
            reaching.Definitions.SingleOrDefault(candidate =>
                !candidate.IsArgument
                && candidate.Slot == slot
                && candidate.Offset == store.Offset);
        if (definition is null)
            return new(root, [], IsComplete: false);

        ImmutableArray<ResourceOwnershipUse> uses =
            AnalyzeDefinition(
                definition,
                slot,
                isArgument: false,
                root,
                context,
                reaching,
                calls,
                members,
                occurrences,
                out bool complete);
        return new(root, uses, complete);
    }

    static ImmutableArray<ResourceOwnershipUse> AnalyzeDefinition(
        LocalDefinition definition,
        int slot,
        bool isArgument,
        ResourceOccurrenceRoot.Acquisition? obligation,
        MethodBodyAnalysisContext context,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, DirectCall> calls,
        IReadOnlyDictionary<int, MemberRef> members,
        ResourceOccurrenceAnalysisResult occurrences,
        out bool complete)
    {
        var uses =
            ImmutableArray.CreateBuilder<ResourceOwnershipUse>();
        complete = !HasRelevantLimitation(
            occurrences,
            obligation,
            isArgument ? slot : null);

        foreach (LocalUse use in reaching.UsesOf(definition))
        {
            if (use.Address)
            {
                complete = false;
                continue;
            }

            ValueUse classification = ClassifyUse(
                context.Instructions.Instructions,
                members,
                use.Offset,
                slot,
                isArgument);
            switch (classification.Kind)
            {
                case ValueUseKind.Local:
                    break;
                case ValueUseKind.Store:
                    uses.Add(
                        GenericUse(
                            ResourceOwnershipUseKind.Stored,
                            classification.OperationOffset));
                    break;
                case ValueUseKind.Return:
                    uses.Add(
                        GenericUse(
                            ResourceOwnershipUseKind.ReturnedToCaller,
                            classification.OperationOffset));
                    break;
                case ValueUseKind.Call:
                    if (!calls.TryGetValue(
                            classification.OperationOffset,
                            out DirectCall? call)
                        || classification.ParameterIndex < 0)
                    {
                        complete = false;
                        break;
                    }

                    uses.Add(
                        new(
                            ResourceOwnershipUseKind.Forwarded,
                            classification.OperationOffset,
                            [],
                            call,
                            classification.ParameterIndex));
                    foreach (ImmutableArray<
                        ResourceOccurrenceResourceKind> domain
                        in ReleaseDomains(
                            occurrences,
                            obligation,
                            isArgument ? slot : null,
                            classification.OperationOffset,
                            classification.ParameterIndex))
                    {
                        uses.Add(
                            new(
                                ResourceOwnershipUseKind.Released,
                                classification.OperationOffset,
                                domain,
                                CalleeParameterIndex:
                                    classification.ParameterIndex));
                    }
                    break;
                default:
                    complete = false;
                    break;
            }
        }

        return
        [
            .. uses
                .OrderBy(static use => use.ILOffset)
                .ThenBy(static use => use.CalleeParameterIndex)
                .ThenBy(static use => use.Kind),
        ];
    }

    static ResourceOwnershipUse GenericUse(
        ResourceOwnershipUseKind kind,
        int offset) =>
        new(kind, offset, []);

    static bool HasRelevantLimitation(
        ResourceOccurrenceAnalysisResult occurrences,
        ResourceOccurrenceRoot.Acquisition? obligation,
        int? argumentSlot) =>
        occurrences.Limitations.Any(limitation =>
            (obligation is not null
                && ReferenceEquals(limitation.Root, obligation))
            || (argumentSlot is int slot
                && limitation.Root
                    is ResourceOccurrenceRoot.IncomingArgument incoming
                && incoming.ArgumentIndex == slot));

    static IEnumerable<ImmutableArray<
        ResourceOccurrenceResourceKind>> ReleaseDomains(
        ResourceOccurrenceAnalysisResult occurrences,
        ResourceOccurrenceRoot.Acquisition? obligation,
        int? argumentSlot,
        int callOffset,
        int calleeParameterIndex)
    {
        foreach (ResourceOccurrence occurrence
            in occurrences.Occurrences.Where(candidate =>
                candidate.ILOffset == callOffset
                && candidate.Operations.Contains(
                    ResourceOccurrenceOperationKind.Release)
                && (obligation is not null
                    ? ReferenceEquals(candidate.Root, obligation)
                    : argumentSlot is int slot
                        && candidate.Root
                            is ResourceOccurrenceRoot.IncomingArgument incoming
                        && incoming.ArgumentIndex == slot)))
        {
            if (occurrence.Effects.Any(effect =>
                    effect.Effect is ResourceEffect.Release release
                    && release.Source
                        is ResourceEffectLocation.Parameter parameter
                    && parameter.Index == calleeParameterIndex))
            {
                yield return occurrence.Root.ResourceKinds;
            }
        }
    }

    static bool IsDirectInvocation(DirectCall call) =>
        call.Kind is CallKind.Call
            or CallKind.CallVirtual
            or CallKind.NewObject;

    static ValueUse ClassifyUse(
        ImmutableArray<DecodedInstruction> instructions,
        IReadOnlyDictionary<int, MemberRef> calls,
        int loadOffset,
        int slot,
        bool isArgument)
    {
        if (!OwnershipValueFlowInstructions.TryFindInstruction(
                instructions,
                loadOffset,
                out int index,
                out DecodedInstruction load)
            || !IsLoadSlotOrAddress(load, slot, isArgument))
        {
            return ValueUse.Unknown;
        }

        int extra = 0;
        for (int i = index + 1; i < instructions.Length; i++)
        {
            DecodedInstruction instruction = instructions[i];
            ILOpCode opcode = instruction.OpCode;
            if (OwnershipValueFlowInstructions.IsSimpleArgumentPush(opcode))
            {
                extra++;
                continue;
            }
            if (opcode == ILOpCode.Ldlen)
                return extra == 0 ? ValueUse.Local : ValueUse.Unknown;
            if (IsElementRead(opcode))
                return extra == 1 ? ValueUse.Local : ValueUse.Unknown;
            if (IsElementStore(opcode))
                return extra == 2 ? ValueUse.Local : ValueUse.Unknown;
            if (IsFieldStore(opcode))
                return ValueUse.StoreAt(instruction.Offset);
            if (IsLocalStore(opcode))
                return ValueUse.Unknown;
            if (opcode == ILOpCode.Ret)
            {
                return extra == 0
                    ? ValueUse.ReturnAt(instruction.Offset)
                    : ValueUse.Unknown;
            }
            if (calls.TryGetValue(instruction.Offset, out MemberRef? callee))
            {
                int parameterIndex =
                    callee.ParameterTypes.Length - extra - 1;
                int consumedArguments =
                    callee.ParameterTypes.Length
                    + (instruction.OpCode != ILOpCode.Newobj && callee.HasThis
                        ? 1
                        : 0);
                if (consumedArguments <= extra)
                {
                    extra -= consumedArguments;
                    if (instruction.OpCode == ILOpCode.Newobj
                        || !FrameworkIdentity.IsCoreLibraryType(
                            callee.ReturnType,
                            "System",
                            "Void"))
                    {
                        extra++;
                    }
                    continue;
                }
                return ValueUse.CallAt(
                    instruction.Offset,
                    parameterIndex);
            }
            return ValueUse.Unknown;
        }

        return ValueUse.Unknown;
    }

    static bool IsLoadSlotOrAddress(
        DecodedInstruction instruction,
        int slot,
        bool isArgument) =>
        isArgument
            ? instruction.OpCode switch
            {
                ILOpCode.Ldarg_0 => slot == 0,
                ILOpCode.Ldarg_1 => slot == 1,
                ILOpCode.Ldarg_2 => slot == 2,
                ILOpCode.Ldarg_3 => slot == 3,
                ILOpCode.Ldarg_s or ILOpCode.Ldarg
                    or ILOpCode.Ldarga_s or ILOpCode.Ldarga
                    => instruction.OperandValue == slot,
                _ => false,
            }
            : OwnershipValueFlowInstructions.IsLoadLocalOrAddress(
                instruction,
                slot);

    static bool IsElementRead(ILOpCode opcode) =>
        opcode is ILOpCode.Ldelem
            or ILOpCode.Ldelem_i
            or ILOpCode.Ldelem_i1
            or ILOpCode.Ldelem_i2
            or ILOpCode.Ldelem_i4
            or ILOpCode.Ldelem_i8
            or ILOpCode.Ldelem_r4
            or ILOpCode.Ldelem_r8
            or ILOpCode.Ldelem_u1
            or ILOpCode.Ldelem_u2
            or ILOpCode.Ldelem_u4
            or ILOpCode.Ldelem_ref;

    static bool IsElementStore(ILOpCode opcode) =>
        opcode is ILOpCode.Stelem
            or ILOpCode.Stelem_i
            or ILOpCode.Stelem_i1
            or ILOpCode.Stelem_i2
            or ILOpCode.Stelem_i4
            or ILOpCode.Stelem_i8
            or ILOpCode.Stelem_r4
            or ILOpCode.Stelem_r8
            or ILOpCode.Stelem_ref;

    static bool IsFieldStore(ILOpCode opcode) =>
        opcode is ILOpCode.Stfld or ILOpCode.Stsfld;

    static bool IsLocalStore(ILOpCode opcode) =>
        opcode is ILOpCode.Stloc_0
            or ILOpCode.Stloc_1
            or ILOpCode.Stloc_2
            or ILOpCode.Stloc_3
            or ILOpCode.Stloc_s
            or ILOpCode.Stloc;

    enum ValueUseKind
    {
        Local,
        Store,
        Return,
        Call,
        Unknown,
    }

    readonly record struct ValueUse(
        ValueUseKind Kind,
        int OperationOffset = -1,
        int ParameterIndex = -1)
    {
        internal static ValueUse Local { get; } =
            new(ValueUseKind.Local);
        internal static ValueUse Unknown { get; } =
            new(ValueUseKind.Unknown);
        internal static ValueUse StoreAt(int offset) =>
            new(ValueUseKind.Store, offset);
        internal static ValueUse ReturnAt(int offset) =>
            new(ValueUseKind.Return, offset);
        internal static ValueUse CallAt(
            int offset,
            int parameterIndex) =>
            new(ValueUseKind.Call, offset, parameterIndex);
    }
}
