using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Instructions;

namespace ILInspector.Analysis;

/// <summary>The proven body-local effect of one resource-owned value.</summary>
public enum ResourceOwnershipUseKind
{
    Released,
    Stored,
    ReturnedToCaller,
    Forwarded,
}

/// <summary>A limitation that prevents complete method-local ownership evidence.</summary>
public enum ResourceOwnershipFlowLimitKind
{
    ControlFlowIncomplete,
    ReachingDefinitionsIncomplete,
    ResolutionIncomplete,
    ResolutionConflict,
    ResolutionRejected,
    UnsupportedEffect,
    AuthorityUnproven,
    ValueFlowUnsupported,
}

/// <summary>One typed method-local ownership-flow limitation.</summary>
public sealed record ResourceOwnershipFlowLimit(
    ResourceOwnershipFlowLimitKind Kind,
    int? ILOffset = null,
    ResourceEffectResolutionGap? ResolutionGap = null,
    ResolvedResourceEffect? Effect = null,
    ResourceEffectModelIdentity? Model = null,
    ResourceKindIdentity? ResourceKind = null,
    int? ParameterIndex = null);

/// <summary>
/// One body-local ownership effect. A forwarded effect retains the physical
/// call occurrence and the callee parameter that receives the value.
/// </summary>
public sealed record ResourceOwnershipUse(
    ResourceOwnershipUseKind Kind,
    int ILOffset,
    DirectCall? Call = null,
    int CalleeParameterIndex = -1,
    ResolvedResourceEffect? Effect = null,
    ResolvedResourceEffect? Authority = null)
{
    public bool IsForwarded =>
        Kind == ResourceOwnershipUseKind.Forwarded;
}

/// <summary>Ownership effects rooted at one resolved acquisition.</summary>
public sealed record ResourceAcquisitionOwnership(
    ResolvedResourceEffect Acquisition,
    ResolvedResourceKindReference ResourceKind,
    ResolvedResourceEffect? Authority,
    int AcquisitionOffset,
    ImmutableArray<ResourceOwnershipUse> Uses,
    bool IsComplete);

/// <summary>
/// Ownership effects rooted at one incoming parameter. Its resource kind is
/// intentionally established by later interprocedural composition.
/// </summary>
public sealed record ResourceParameterOwnership(
    int ParameterIndex,
    TypeRef ValueType,
    ImmutableArray<ResourceOwnershipUse> Uses,
    bool IsComplete);

/// <summary>
/// Compact body evidence retained for interprocedural ownership composition.
/// It contains no IL or control-flow graph state.
/// </summary>
public sealed record ResourceOwnershipMethodEvidence(
    MethodIdentity Method,
    MemberRef Member,
    ImmutableArray<ResourceAcquisitionOwnership> Acquisitions,
    ImmutableArray<ResourceParameterOwnership> Parameters,
    ImmutableArray<ResourceOwnershipFlowLimit> Limits,
    bool IsComplete);

internal sealed record ResourceOwnershipFlowMethodInput(
    MethodBodyAnalysisContext Context,
    ImmutableArray<DirectCall> DirectCalls);

internal static class ResourceOwnershipFlow
{
    internal static ImmutableArray<ResourceOwnershipMethodEvidence> Analyze(
        ImmutableArray<ResourceOwnershipFlowMethodInput> inputs,
        ResourceEffectResolutionOutcome outcome,
        ResourceEffectAdmission admission)
    {
        if (inputs.IsDefaultOrEmpty)
            return [];

        ResolutionView resolution = ResolutionView.Create(
            outcome,
            admission);
        return
        [
            .. inputs
                .Select(input => Analyze(input, resolution))
                .Where(static evidence =>
                    !evidence.Acquisitions.IsEmpty
                    || !evidence.Parameters.IsEmpty
                    || !evidence.IsComplete),
        ];
    }

    static ResourceOwnershipMethodEvidence Analyze(
        ResourceOwnershipFlowMethodInput input,
        ResolutionView resolution)
    {
        MethodBodyAnalysisContext context = input.Context;
        MethodIdentity method = context.Method;
        MemberRef member = CallTreeMember.FromDefinition(method);
        var limits =
            ImmutableArray.CreateBuilder<ResourceOwnershipFlowLimit>();
        limits.AddRange(resolution.LimitsFor(method.MetadataToken));
        IReadOnlyDictionary<int, ImmutableArray<ResolvedResourceEffect>>
            effects = resolution.EffectsFor(method.MetadataToken);

        if (!context.Blocks.IsComplete)
        {
            AddEffectScopedLimits(
                limits,
                ResourceOwnershipFlowLimitKind.ControlFlowIncomplete,
                effects);
            return new(method, member, [], [], limits.ToImmutable(), false);
        }

        ReachingDefinitionsResult reaching =
            ReachingDefinitions.Analyze(
                context.Instructions,
                method.ParameterTypes.Length
                    + (method.IsStatic ? 0 : 1));
        if (!reaching.IsComplete)
        {
            AddEffectScopedLimits(
                limits,
                ResourceOwnershipFlowLimitKind
                    .ReachingDefinitionsIncomplete,
                effects);
            return new(method, member, [], [], limits.ToImmutable(), false);
        }

        IReadOnlyDictionary<int, DirectCall> calls =
            input.DirectCalls
                .Where(IsDirectInvocation)
                .ToDictionary(static call => call.ILOffset);
        IReadOnlyDictionary<int, MemberRef> members =
            calls.ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Callee);
        IReadOnlySet<int> incompleteCallOffsets =
            resolution.IncompleteCallOffsetsFor(
                method.MetadataToken);
        var handledEffects = new HashSet<ResolvedResourceEffect>(
            ReferenceEqualityComparer.Instance);

        var acquisitions =
            ImmutableArray.CreateBuilder<ResourceAcquisitionOwnership>();
        foreach ((int offset, ImmutableArray<ResolvedResourceEffect> atCall)
            in effects.OrderBy(static pair => pair.Key))
        {
            if (!calls.TryGetValue(offset, out DirectCall? call))
                continue;

            foreach (ResolvedResourceEffect effect in atCall)
            {
                if (effect.Effect is not ResourceEffect.Acquire acquire)
                    continue;
                handledEffects.Add(effect);

                if (acquire.Target is not ResourceEffectLocation.Return
                    || acquire.When
                        is not ResourceEffectCompletion.NormalReturn
                    || acquire.Lender is not null
                    || effect.ResourceKinds is not [var resourceKind])
                {
                    limits.Add(
                        new(
                            ResourceOwnershipFlowLimitKind.UnsupportedEffect,
                            offset,
                            Effect: effect));
                    continue;
                }

                if (!TryResolveAuthority(
                        context,
                        call,
                        acquire.Correspondence,
                        resourceKind,
                        effects,
                        out ResolvedResourceEffect? authority))
                {
                    limits.Add(
                        new(
                            ResourceOwnershipFlowLimitKind.AuthorityUnproven,
                            offset,
                            Effect: effect,
                            ResourceKind: resourceKind.Identity));
                    continue;
                }

                if (!ArrayPoolUseClassifier.TryFindNextNonNop(
                        context.Instructions.Instructions,
                        call.ReturnAddress ?? call.ILOffset,
                        out var store)
                    || !ArrayPoolUseClassifier.TryReadStoreLocal(
                        store,
                        out int slot))
                {
                    limits.Add(
                        new(
                            ResourceOwnershipFlowLimitKind
                                .ValueFlowUnsupported,
                            offset,
                            Effect: effect,
                            ResourceKind: resourceKind.Identity));
                    continue;
                }

                LocalDefinition? definition =
                    reaching.Definitions.FirstOrDefault(candidate =>
                        !candidate.IsArgument
                        && candidate.Slot == slot
                        && candidate.Offset == store.Offset);
                if (definition is null)
                {
                    limits.Add(
                        new(
                            ResourceOwnershipFlowLimitKind
                                .ReachingDefinitionsIncomplete,
                            store.Offset,
                            Effect: effect,
                            ResourceKind: resourceKind.Identity));
                    continue;
                }

                DefinitionAnalysis flow = AnalyzeDefinition(
                    definition,
                    slot,
                    isArgument: false,
                    effect,
                    resourceKind,
                    resourceKind.Identity,
                    rootParameterIndex: null,
                    authority,
                    context,
                    reaching,
                    calls,
                    members,
                    effects,
                    incompleteCallOffsets,
                    handledEffects,
                    limits);
                acquisitions.Add(
                    new(
                        effect,
                        resourceKind,
                        authority,
                        offset,
                        flow.Uses,
                        flow.IsComplete));
            }
        }

        var parameters =
            ImmutableArray.CreateBuilder<ResourceParameterOwnership>();
        bool hasReleaseEffects = effects.Values.Any(static atCall =>
            atCall.Any(static effect =>
                effect.Effect is ResourceEffect.Release));
        for (int parameterIndex = 0;
            parameterIndex < method.ParameterTypes.Length;
            parameterIndex++)
        {
            TypeRef parameterType = method.ParameterTypes[parameterIndex];
            bool isResourceValueType =
                resolution.IsResourceValueType(parameterType);
            bool isArrayCompatibilityType =
                parameterType.Kind == TypeRefKind.SzArray;
            if (!isResourceValueType
                && !isArrayCompatibilityType
                && !hasReleaseEffects)
                continue;

            int slot = parameterIndex + (method.IsStatic ? 0 : 1);
            LocalDefinition? definition =
                reaching.Definitions.SingleOrDefault(candidate =>
                    candidate.IsArgument
                    && candidate.Slot == slot
                    && candidate.Offset == -1);
            if (definition is null)
            {
                limits.Add(
                    new(
                        ResourceOwnershipFlowLimitKind
                            .ReachingDefinitionsIncomplete,
                        ResourceKind:
                            isArrayCompatibilityType
                                ? ArrayPoolResourceEffectModel.BufferKind
                                : null,
                        ParameterIndex: parameterIndex));
                parameters.Add(
                    new(
                        parameterIndex,
                        parameterType,
                        [],
                        IsComplete: false));
                continue;
            }

            var parameterLimits =
                ImmutableArray.CreateBuilder<ResourceOwnershipFlowLimit>();
            DefinitionAnalysis flow = AnalyzeDefinition(
                definition,
                slot,
                isArgument: true,
                rootEffect: null,
                resourceKind: null,
                projectedResourceKind:
                    isArrayCompatibilityType
                        ? ArrayPoolResourceEffectModel.BufferKind
                        : null,
                rootParameterIndex: parameterIndex,
                acquisitionAuthority: null,
                context,
                reaching,
                calls,
                members,
                effects,
                incompleteCallOffsets,
                handledEffects,
                parameterLimits);
            if (!isResourceValueType
                && !isArrayCompatibilityType
                && !flow.Uses.Any(static use =>
                    use.Effect is not null)
                && !parameterLimits.Any(static limit =>
                    limit.Kind
                        == ResourceOwnershipFlowLimitKind
                            .UnsupportedEffect))
            {
                continue;
            }

            limits.AddRange(parameterLimits);
            parameters.Add(
                new(
                    parameterIndex,
                    parameterType,
                    flow.Uses,
                    flow.IsComplete));
        }

        foreach ((int offset, ImmutableArray<ResolvedResourceEffect> atCall)
            in effects)
        {
            foreach (ResolvedResourceEffect effect in atCall)
            {
                if (handledEffects.Contains(effect)
                    || effect.Effect is ResourceEffect.Resource
                        or ResourceEffect.Authority)
                {
                    continue;
                }

                limits.Add(
                    new(
                        ResourceOwnershipFlowLimitKind.UnsupportedEffect,
                        offset,
                        Effect: effect));
            }
        }

        ImmutableArray<ResourceOwnershipFlowLimit> retainedLimits =
            limits
                .Distinct()
                .OrderBy(static limit => limit.ILOffset ?? -1)
                .ThenBy(static limit => limit.Kind)
                .ToImmutableArray();
        bool complete =
            retainedLimits.IsEmpty
            && acquisitions.All(static item => item.IsComplete)
            && parameters.All(static item => item.IsComplete);
        return new(
            method,
            member,
            acquisitions.ToImmutable(),
            parameters.ToImmutable(),
            retainedLimits,
            complete);
    }

    static void AddEffectScopedLimits(
        ImmutableArray<ResourceOwnershipFlowLimit>.Builder limits,
        ResourceOwnershipFlowLimitKind kind,
        IReadOnlyDictionary<int, ImmutableArray<ResolvedResourceEffect>>
            effects)
    {
        ResolvedResourceEffect[] methodEffects =
        [
            .. effects.Values
                .SelectMany(static atCall => atCall)
                .Distinct(),
        ];
        if (methodEffects.Length == 0)
        {
            limits.Add(new(kind));
            return;
        }

        foreach (ResolvedResourceEffect effect in methodEffects)
            limits.Add(new(kind, Effect: effect));
    }

    static DefinitionAnalysis AnalyzeDefinition(
        LocalDefinition definition,
        int slot,
        bool isArgument,
        ResolvedResourceEffect? rootEffect,
        ResolvedResourceKindReference? resourceKind,
        ResourceKindIdentity? projectedResourceKind,
        int? rootParameterIndex,
        ResolvedResourceEffect? acquisitionAuthority,
        MethodBodyAnalysisContext context,
        ReachingDefinitionsResult reaching,
        IReadOnlyDictionary<int, DirectCall> calls,
        IReadOnlyDictionary<int, MemberRef> members,
        IReadOnlyDictionary<int, ImmutableArray<ResolvedResourceEffect>>
            effects,
        IReadOnlySet<int> incompleteCallOffsets,
        HashSet<ResolvedResourceEffect> handledEffects,
        ImmutableArray<ResourceOwnershipFlowLimit>.Builder limits)
    {
        var uses = ImmutableArray.CreateBuilder<ResourceOwnershipUse>();
        bool complete = true;
        foreach (LocalUse use in reaching.UsesOf(definition))
        {
            if (use.Address)
            {
                complete = false;
                limits.Add(
                    new(
                        ResourceOwnershipFlowLimitKind.ValueFlowUnsupported,
                        use.Offset,
                        Effect: rootEffect,
                        ResourceKind: projectedResourceKind,
                        ParameterIndex: rootParameterIndex));
                continue;
            }

            ImmutableArray<ReleaseMatch> releases = [];
            ImmutableArray<ResolvedResourceEffect> incompleteReleases = [];
            bool? ClassifyRelease(int operationOffset, int parameterIndex)
            {
                if (!calls.TryGetValue(
                        operationOffset,
                        out DirectCall? operation)
                    || !effects.TryGetValue(
                        operationOffset,
                        out ImmutableArray<ResolvedResourceEffect> atCall))
                {
                    return incompleteCallOffsets.Contains(operationOffset)
                        ? null
                        : false;
                }

                ReleaseMatchOutcome match = MatchRelease(
                    operation,
                    parameterIndex,
                    resourceKind,
                    acquisitionAuthority,
                    isArgument,
                    atCall,
                    effects,
                    handledEffects,
                    context);
                releases = match.Matches;
                incompleteReleases = match.IncompleteEffects;
                return !match.Matches.IsEmpty
                    ? true
                    : !match.IncompleteEffects.IsEmpty
                        ? null
                        : false;
            }

            ArrayPoolUseClassifier.UseClassification classification =
                ArrayPoolUseClassifier.ClassifyUse(
                    context.Instructions.Instructions,
                    members,
                    use.Offset,
                    slot,
                    isArgument: isArgument,
                    classifyRelease: ClassifyRelease);
            switch (classification.Kind)
            {
                case ArrayPoolUseClassifier.UseKind.Release:
                    if (releases.IsEmpty)
                    {
                        complete = false;
                        limits.Add(
                            new(
                                ResourceOwnershipFlowLimitKind
                                    .ValueFlowUnsupported,
                                classification.OperationOffset,
                                Effect: rootEffect,
                                ResourceKind: projectedResourceKind,
                                ParameterIndex: rootParameterIndex));
                        break;
                    }
                    foreach (ReleaseMatch release in releases)
                    {
                        uses.Add(
                            new(
                                ResourceOwnershipUseKind.Released,
                                classification.OperationOffset,
                                release.Call,
                                release.ParameterIndex,
                                release.Effect,
                                release.Authority));
                    }
                    if (!incompleteReleases.IsEmpty)
                    {
                        complete = false;
                        foreach (ResolvedResourceEffect effect
                            in incompleteReleases)
                        {
                            limits.Add(
                                new(
                                    ResourceOwnershipFlowLimitKind
                                        .UnsupportedEffect,
                                    classification.OperationOffset,
                                    Effect: effect,
                                    ResourceKind:
                                        effect.ResourceKinds.IsEmpty
                                            ? projectedResourceKind
                                            : null,
                                    ParameterIndex: rootParameterIndex));
                        }
                    }
                    break;
                case ArrayPoolUseClassifier.UseKind.Store:
                    uses.Add(
                        new(
                            ResourceOwnershipUseKind.Stored,
                            classification.OperationOffset));
                    break;
                case ArrayPoolUseClassifier.UseKind.Return:
                    uses.Add(
                        new(
                            ResourceOwnershipUseKind.ReturnedToCaller,
                            classification.OperationOffset));
                    break;
                case ArrayPoolUseClassifier.UseKind.Forward:
                    if (calls.TryGetValue(
                            classification.OperationOffset,
                            out DirectCall? call)
                        && classification.ParameterIndex >= 0)
                    {
                        uses.Add(
                            new(
                                ResourceOwnershipUseKind.Forwarded,
                                classification.OperationOffset,
                                call,
                                classification.ParameterIndex));
                    }
                    else
                    {
                        complete = false;
                        limits.Add(
                            new(
                                ResourceOwnershipFlowLimitKind
                                    .ValueFlowUnsupported,
                                classification.OperationOffset,
                                Effect: rootEffect,
                                ResourceKind: projectedResourceKind,
                                ParameterIndex: rootParameterIndex));
                    }
                    break;
                case ArrayPoolUseClassifier.UseKind.LocalUse:
                    break;
                default:
                    complete = false;
                    if (incompleteReleases.IsEmpty)
                    {
                        limits.Add(
                            new(
                                ResourceOwnershipFlowLimitKind
                                    .ValueFlowUnsupported,
                                classification.OperationOffset,
                                Effect: rootEffect,
                                ResourceKind: projectedResourceKind,
                                ParameterIndex: rootParameterIndex));
                    }
                    else
                    {
                        foreach (ResolvedResourceEffect effect
                            in incompleteReleases)
                        {
                            limits.Add(
                                new(
                                    ResourceOwnershipFlowLimitKind
                                        .UnsupportedEffect,
                                    classification.OperationOffset,
                                    Effect: effect,
                                    ResourceKind:
                                        effect.ResourceKinds.IsEmpty
                                            ? projectedResourceKind
                                            : null,
                                    ParameterIndex:
                                        rootParameterIndex));
                        }
                    }
                    break;
            }
        }

        return new(
            uses
                .OrderBy(static use => use.ILOffset)
                .ThenBy(static use => use.Kind)
                .ToImmutableArray(),
            complete);
    }

    static ReleaseMatchOutcome MatchRelease(
        DirectCall call,
        int parameterIndex,
        ResolvedResourceKindReference? resourceKind,
        ResolvedResourceEffect? acquisitionAuthority,
        bool isArgument,
        ImmutableArray<ResolvedResourceEffect> atCall,
        IReadOnlyDictionary<int, ImmutableArray<ResolvedResourceEffect>>
            effects,
        HashSet<ResolvedResourceEffect> handledEffects,
        MethodBodyAnalysisContext context)
    {
        var matches = ImmutableArray.CreateBuilder<ReleaseMatch>();
        var incompleteEffects =
            ImmutableArray.CreateBuilder<ResolvedResourceEffect>();
        foreach (ResolvedResourceEffect effect in atCall)
        {
            if (effect.Effect is not ResourceEffect.Release release)
            {
                continue;
            }

            bool supportedSource =
                release.Source
                    is ResourceEffectLocation.Parameter source
                && source.Index == parameterIndex;
            bool receiverSource =
                release.Source is ResourceEffectLocation.Receiver
                && parameterIndex == -1;
            if (!supportedSource && !receiverSource)
                continue;
            handledEffects.Add(effect);

            ResolvedResourceKindReference? releaseKind =
                effect.ResourceKinds is [var exactKind]
                    ? exactKind
                    : null;
            if (release.Kind is not null
                && (releaseKind is null
                    || resourceKind is not null
                        && !releaseKind.Equals(resourceKind)))
            {
                continue;
            }

            if (receiverSource)
            {
                incompleteEffects.Add(effect);
                continue;
            }

            if (release.When
                    is not ResourceEffectCompletion.NormalReturn
                || release.Observation is not null)
            {
                incompleteEffects.Add(effect);
                continue;
            }

            if (!TryResolveAuthority(
                    context,
                    call,
                    release.Correspondence,
                    releaseKind ?? resourceKind,
                    effects,
                    out ResolvedResourceEffect? authority))
            {
                incompleteEffects.Add(effect);
                continue;
            }
            if (release.Correspondence is not null
                && !isArgument
                && (acquisitionAuthority is null
                    || authority is null
                    || !SameAuthority(
                        acquisitionAuthority,
                        authority)))
            {
                continue;
            }

            matches.Add(
                new(
                    call,
                    parameterIndex,
                    effect,
                    authority));
        }

        return new(
            matches.ToImmutable(),
            incompleteEffects
                .Distinct()
                .ToImmutableArray());
    }

    static bool TryResolveAuthority(
        MethodBodyAnalysisContext context,
        DirectCall call,
        ResourceEffectLocation? correspondence,
        ResolvedResourceKindReference? resourceKind,
        IReadOnlyDictionary<int, ImmutableArray<ResolvedResourceEffect>>
            effects,
        out ResolvedResourceEffect? authority)
    {
        authority = null;
        if (correspondence is null)
            return true;
        if (correspondence is not ResourceEffectLocation.Receiver
            || !TryReceiverProducerOffset(
                context,
                call,
                out int sourceOffset)
            || !effects.TryGetValue(
                sourceOffset,
                out ImmutableArray<ResolvedResourceEffect> atSource))
        {
            return false;
        }

        ResolvedResourceEffect[] matches =
        [
            .. atSource.Where(candidate =>
                candidate.Effect is ResourceEffect.Authority
                    {
                        Target: ResourceEffectLocation.Return,
                    }
                && (resourceKind is null
                    || candidate.ResourceKinds.Any(resourceKind.Equals))),
        ];
        if (matches.Length != 1)
            return false;
        authority = matches[0];
        return true;
    }

    static bool TryReceiverProducerOffset(
        MethodBodyAnalysisContext context,
        DirectCall call,
        out int sourceOffset)
    {
        if (call.ResolvedReceiverValue?.Single is
            {
                IsCallResult: true,
            } resolved)
        {
            sourceOffset = resolved.ILOffset;
            return true;
        }

        sourceOffset = -1;
        int blockIndex =
            context.Blocks.BlockIndexAt(call.ILOffset);
        if (blockIndex < 0)
            return false;
        var block = context.Blocks.Blocks[blockIndex];
        ImmutableArray<DecodedInstruction> instructions =
            context.Instructions.Instructions;
        int remainingParameters =
            call.Callee.ParameterTypes.Length;
        for (int index = instructions.Length - 1;
            index >= 0;
            index--)
        {
            var instruction = instructions[index];
            if (instruction.Offset < block.Start)
                return false;
            if (instruction.Offset >= call.ILOffset
                || instruction.OpCode
                    == ILOpCode.Nop)
            {
                continue;
            }
            if (remainingParameters > 0)
            {
                if (!ArrayPoolUseClassifier.IsSimpleArgumentPush(
                        instruction.OpCode))
                {
                    return false;
                }
                remainingParameters--;
                continue;
            }

            sourceOffset = instruction.Offset;
            return true;
        }
        return false;
    }

    static bool SameAuthority(
        ResolvedResourceEffect left,
        ResolvedResourceEffect right)
    {
        if (!left.ResourceKinds.SequenceEqual(right.ResourceKinds)
            || left.Effect is not ResourceEffect.Authority leftAuthority
            || right.Effect is not ResourceEffect.Authority rightAuthority)
        {
            return false;
        }

        return (leftAuthority.Key, rightAuthority.Key) switch
        {
            (ResourceAuthorityKey.Value, ResourceAuthorityKey.Value) =>
                left.PhysicalInvocation.Equals(
                    right.PhysicalInvocation),
            (ResourceAuthorityKey.Singleton,
                ResourceAuthorityKey.Singleton) =>
                left.AuthorityKeyArguments.SequenceEqual(
                    right.AuthorityKeyArguments),
            _ => false,
        };
    }

    static bool IsDirectInvocation(DirectCall call) =>
        call.Kind is CallKind.Call
            or CallKind.CallVirtual
            or CallKind.NewObject;

    readonly record struct DefinitionAnalysis(
        ImmutableArray<ResourceOwnershipUse> Uses,
        bool IsComplete);

    sealed record ReleaseMatch(
        DirectCall Call,
        int ParameterIndex,
        ResolvedResourceEffect Effect,
        ResolvedResourceEffect? Authority);

    readonly record struct ReleaseMatchOutcome(
        ImmutableArray<ReleaseMatch> Matches,
        ImmutableArray<ResolvedResourceEffect> IncompleteEffects);

    sealed class ResolutionView
    {
        readonly Dictionary<
            int,
            Dictionary<int, ImmutableArray<ResolvedResourceEffect>>>
            _effects;
        readonly Dictionary<
            int,
            ImmutableArray<ResourceOwnershipFlowLimit>> _limits;
        readonly Dictionary<int, IReadOnlySet<int>> _incompleteCallOffsets;
        readonly ImmutableArray<ResourceOwnershipFlowLimit> _globalLimits;
        readonly ImmutableHashSet<TypeRef> _resourceValueTypes;

        ResolutionView(
            Dictionary<
                int,
                Dictionary<int, ImmutableArray<ResolvedResourceEffect>>>
                effects,
            Dictionary<
                int,
                ImmutableArray<ResourceOwnershipFlowLimit>> limits,
            Dictionary<int, IReadOnlySet<int>> incompleteCallOffsets,
            ImmutableArray<ResourceOwnershipFlowLimit> globalLimits,
            ImmutableHashSet<TypeRef> resourceValueTypes)
        {
            _effects = effects;
            _limits = limits;
            _incompleteCallOffsets = incompleteCallOffsets;
            _globalLimits = globalLimits;
            _resourceValueTypes = resourceValueTypes;
        }

        internal static ResolutionView Create(
            ResourceEffectResolutionOutcome outcome,
            ResourceEffectAdmission admission)
        {
            ImmutableArray<ResolvedResourceEffect> effects = outcome switch
            {
                ResourceEffectResolutionOutcome.Complete complete =>
                    complete.Snapshot.Effects,
                ResourceEffectResolutionOutcome.Incomplete partial =>
                    partial.Effects,
                _ => [],
            };
            ResourceEffectResolutionReceipt? receipt = outcome switch
            {
                ResourceEffectResolutionOutcome.Complete completed =>
                    completed.Receipt,
                ResourceEffectResolutionOutcome.Incomplete partial =>
                    partial.Receipt,
                ResourceEffectResolutionOutcome.Conflict conflicted =>
                    conflicted.Receipt,
                _ => null,
            };
            var effectsByMethod = effects
                .GroupBy(static effect =>
                    effect.DirectCall.Call.EvidenceMethod.MetadataToken)
                .ToDictionary(
                    static group => group.Key,
                    static group => group
                        .GroupBy(static effect =>
                            effect.DirectCall.Call.ILOffset)
                        .ToDictionary(
                            static offsets => offsets.Key,
                            static offsets => offsets.ToImmutableArray()));
            ImmutableHashSet<TypeRef> resourceValueTypes =
                effects.SelectMany(ResourceValueTypes)
                    .ToImmutableHashSet();

            var callsByInvocation =
                new Dictionary<
                    GraphNodeStorageKey,
                    DirectCallDefinitionResolution>();
            var incompleteCallOffsets =
                new Dictionary<int, HashSet<int>>();
            if (receipt is not null)
            {
                foreach (DirectCallDefinitionResolution result
                    in receipt.Population.Results)
                {
                    callsByInvocation[result.PhysicalInvocation] = result;
                    if (result
                        is DirectCallDefinitionResolution.Resolved)
                    {
                        continue;
                    }
                    int token =
                        result.Call.EvidenceMethod.MetadataToken;
                    if (!incompleteCallOffsets.TryGetValue(
                            token,
                            out HashSet<int>? offsets))
                    {
                        offsets = [];
                        incompleteCallOffsets.Add(token, offsets);
                    }
                    offsets.Add(result.Call.ILOffset);
                }
            }

            var limits =
                new Dictionary<
                    int,
                    ImmutableArray<ResourceOwnershipFlowLimit>.Builder>();
            var global =
                ImmutableArray.CreateBuilder<ResourceOwnershipFlowLimit>();
            void AddLimit(
                GraphNodeStorageKey? invocation,
                ResourceOwnershipFlowLimit limit)
            {
                if (invocation is not null
                    && callsByInvocation.TryGetValue(
                        invocation,
                        out DirectCallDefinitionResolution? result))
                {
                    int token =
                        result.Call.EvidenceMethod.MetadataToken;
                    if (!limits.TryGetValue(token, out var methodLimits))
                    {
                        methodLimits =
                            ImmutableArray.CreateBuilder<
                                ResourceOwnershipFlowLimit>();
                        limits.Add(token, methodLimits);
                    }
                    methodLimits.Add(
                        limit with { ILOffset = result.Call.ILOffset });
                }
                else
                {
                    if (limit.ResolutionGap?.Kind is
                        ResourceEffectResolutionGapKind
                            .InterfaceApplicationIncomplete
                        or ResourceEffectResolutionGapKind
                            .PopulationIncomplete)
                    {
                        return;
                    }
                    global.Add(limit);
                }
            }

            ImmutableArray<ResourceEffectModelIdentity> ModelsFor(
                ResourceEffectResolutionGap gap,
                ImmutableArray<ResourceEffectTargetEvaluation>
                    evaluations)
            {
                ImmutableArray<ResourceEffectModelIdentity> models =
                [
                    .. evaluations
                        .Where(evaluation =>
                            evaluation.Gaps.Contains(gap))
                        .Select(static evaluation => evaluation.Model)
                        .Distinct(),
                ];
                if (!models.IsEmpty)
                    return models;
                return
                [
                    .. admission.Models
                        .Select(static model => model.Identity)
                        .Distinct(),
                ];
            }

            void AddGap(
                ResourceEffectResolutionGap gap,
                ImmutableArray<ResourceEffectTargetEvaluation>
                    evaluations)
            {
                foreach (ResourceEffectModelIdentity model
                    in ModelsFor(gap, evaluations))
                {
                    AddLimit(
                        gap.PhysicalInvocation,
                        new(
                            ResourceOwnershipFlowLimitKind
                                .ResolutionIncomplete,
                            ResolutionGap: gap,
                            Model: model));
                }
            }

            if (outcome
                is ResourceEffectResolutionOutcome.Incomplete incomplete)
            {
                foreach (ResourceEffectResolutionGap gap in incomplete.Gaps)
                    AddGap(gap, incomplete.Evaluations);
            }
            else if (outcome
                is ResourceEffectResolutionOutcome.Conflict conflict)
            {
                foreach (AdmittedResourceEffectModel model
                    in admission.Models)
                {
                    global.Add(
                        new(
                            ResourceOwnershipFlowLimitKind
                                .ResolutionConflict,
                            Model: model.Identity));
                }
                foreach (ResourceEffectConflict item in conflict.Conflicts)
                {
                    foreach (ResolvedResourceEffect effect in item.Effects)
                    {
                        AddLimit(
                            item.PhysicalInvocation,
                            new(
                                ResourceOwnershipFlowLimitKind
                                    .ResolutionConflict,
                                Effect: effect));
                    }
                }
                foreach (ResourceEffectResolutionGap gap in conflict.Gaps)
                    AddGap(gap, conflict.Evaluations);
            }
            else if (outcome
                is ResourceEffectResolutionOutcome.Rejected)
            {
                foreach (AdmittedResourceEffectModel model
                    in admission.Models)
                {
                    global.Add(
                        new(
                            ResourceOwnershipFlowLimitKind
                                .ResolutionRejected,
                            Model: model.Identity));
                }
            }

            return new(
                effectsByMethod,
                limits.ToDictionary(
                    static pair => pair.Key,
                    static pair => pair.Value.ToImmutable()),
                incompleteCallOffsets.ToDictionary(
                    static pair => pair.Key,
                    static pair => (IReadOnlySet<int>)pair.Value),
                global.ToImmutable(),
                resourceValueTypes);
        }

        static IEnumerable<TypeRef> ResourceValueTypes(
            ResolvedResourceEffect effect)
        {
            MemberRef member = effect.DirectCall.Call.Callee;
            switch (effect.Effect)
            {
                case ResourceEffect.Acquire
                    {
                        Target: ResourceEffectLocation.Return,
                    }:
                    yield return member.ReturnType;
                    break;
                case ResourceEffect.Release
                    {
                        Source:
                            ResourceEffectLocation.Parameter source,
                    }
                    when source.Index >= 0
                        && source.Index < member.ParameterTypes.Length:
                    yield return member.ParameterTypes[source.Index];
                    break;
                case ResourceEffect.Release
                    {
                        Source: ResourceEffectLocation.Receiver,
                    }:
                    yield return member.DeclaringType;
                    break;
            }
        }

        internal IReadOnlyDictionary<
            int,
            ImmutableArray<ResolvedResourceEffect>> EffectsFor(
                int methodToken) =>
            _effects.TryGetValue(methodToken, out var effects)
                ? effects
                : new Dictionary<
                    int,
                    ImmutableArray<ResolvedResourceEffect>>();

        internal ImmutableArray<ResourceOwnershipFlowLimit> LimitsFor(
            int methodToken) =>
            _limits.TryGetValue(methodToken, out var limits)
                ? _globalLimits.AddRange(limits)
                : _globalLimits;

        internal IReadOnlySet<int> IncompleteCallOffsetsFor(
            int methodToken) =>
            _incompleteCallOffsets.TryGetValue(
                methodToken,
                out IReadOnlySet<int>? offsets)
                ? offsets
                : new HashSet<int>();

        internal bool IsResourceValueType(TypeRef type) =>
            _resourceValueTypes.Contains(type);
    }
}
