using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>The proven effect of one use of an ArrayPool-owned array.</summary>
public enum ArrayPoolOwnershipUseKind
{
    ReturnedToPool,
    Stored,
    ReturnedToCaller,
    Forwarded,
}

/// <summary>
/// One body-local ownership effect. A forwarded effect retains the physical
/// call occurrence and the callee parameter that receives the array.
/// </summary>
public sealed record ArrayPoolOwnershipUse(
    ArrayPoolOwnershipUseKind Kind,
    int ILOffset,
    DirectCall? Call = null,
    int CalleeParameterIndex = -1)
{
    public bool IsForwarded =>
        Kind == ArrayPoolOwnershipUseKind.Forwarded;
}

/// <summary>Ownership effects rooted at one ArrayPool&lt;T&gt;.Shared.Rent.</summary>
public sealed record ArrayPoolRentOwnership(
    int RentOffset,
    ImmutableArray<ArrayPoolOwnershipUse> Uses,
    bool IsComplete);

/// <summary>Ownership effects rooted at one array parameter.</summary>
public sealed record ArrayPoolParameterOwnership(
    int ParameterIndex,
    ImmutableArray<ArrayPoolOwnershipUse> Uses,
    bool IsComplete);

/// <summary>
/// Compact body evidence retained for interprocedural ownership composition.
/// It contains no IL or control-flow graph state.
/// </summary>
public sealed record ArrayPoolOwnershipMethodEvidence(
    MethodIdentity Method,
    MemberRef Member,
    ImmutableArray<ArrayPoolRentOwnership> Rents,
    ImmutableArray<ArrayPoolParameterOwnership> Parameters,
    bool IsComplete);

static class ArrayPoolOwnershipProjection
{
    internal static ImmutableArray<ArrayPoolOwnershipMethodEvidence> Project(
        ImmutableArray<ResourceOwnershipMethodEvidence> methods) =>
    [
        .. methods
            .Select(Project)
            .Where(static evidence =>
                !evidence.Rents.IsEmpty
                || !evidence.Parameters.IsEmpty
                || !evidence.IsComplete),
    ];

    static ArrayPoolOwnershipMethodEvidence Project(
        ResourceOwnershipMethodEvidence evidence)
    {
        ImmutableArray<ArrayPoolRentOwnership> rents =
        [
            .. evidence.Acquisitions
                .Where(static acquisition =>
                    acquisition.ResourceKind.Identity
                        == ArrayPoolResourceEffectModel.BufferKind)
                .Select(static acquisition =>
                    new ArrayPoolRentOwnership(
                        acquisition.AcquisitionOffset,
                        [
                            .. acquisition.Uses.Select(
                                use => ProjectUse(
                                    use,
                                    acquisition.ResourceKind)),
                        ],
                        acquisition.IsComplete)),
        ];
        ImmutableArray<ArrayPoolParameterOwnership> parameters =
        [
            .. evidence.Parameters
                .Where(static parameter =>
                    parameter.ValueType.Kind == TypeRefKind.SzArray)
                .Select(parameter =>
                    ProjectParameter(evidence, parameter)),
        ];
        bool hasArrayParameter = evidence.Member.ParameterTypes.Any(
            static parameter => parameter.Kind == TypeRefKind.SzArray);
        bool hasArrayPoolEvidence =
            !rents.IsEmpty
            || !parameters.IsEmpty
            || hasArrayParameter;
        bool isComplete =
            rents.All(static rent => rent.IsComplete)
            && parameters.All(static parameter => parameter.IsComplete)
            && !evidence.Limits.Any(limit =>
                IsArrayPoolRelevant(limit, hasArrayPoolEvidence));

        return new(
            evidence.Method,
            evidence.Member,
            rents,
            parameters,
            isComplete);
    }

    static ArrayPoolParameterOwnership ProjectParameter(
        ResourceOwnershipMethodEvidence method,
        ResourceParameterOwnership parameter)
    {
        ResourceOwnershipFlowLimit[] parameterLimits =
        [
            .. method.Limits.Where(limit =>
                limit.ParameterIndex == parameter.ParameterIndex),
        ];
        bool isComplete =
            parameter.IsComplete
            || (parameterLimits.Length > 0
                && parameterLimits.All(limit =>
                    !IsArrayPoolRelevant(
                        limit,
                        hasArrayPoolEvidence: true)));
        var uses = parameter.Uses
            .Select(use => ProjectUse(
                use,
                resourceKind: null))
            .ToList();
        foreach (ResourceOwnershipFlowLimit limit
            in parameterLimits.Where(limit =>
                !IsArrayPoolRelevant(
                    limit,
                    hasArrayPoolEvidence: true)))
        {
            if (TryProjectForwarded(limit, out var forwarded)
                && !uses.Any(use =>
                    use.ILOffset == forwarded.ILOffset
                    && use.CalleeParameterIndex
                        == forwarded.CalleeParameterIndex))
            {
                uses.Add(forwarded);
            }
        }
        return new(
            parameter.ParameterIndex,
            [
                .. uses
                    .OrderBy(static use => use.ILOffset)
                    .ThenBy(static use => use.Kind),
            ],
            isComplete);
    }

    static bool TryProjectForwarded(
        ResourceOwnershipFlowLimit limit,
        out ArrayPoolOwnershipUse use)
    {
        if (limit.ILOffset is int offset
            && limit.Effect is
            {
                Effect: ResourceEffect.Release
                {
                    Source: ResourceEffectLocation.Parameter source,
                },
            } effect)
        {
            use = new(
                ArrayPoolOwnershipUseKind.Forwarded,
                offset,
                effect.DirectCall.Call,
                source.Index);
            return true;
        }

        use = null!;
        return false;
    }

    static bool IsArrayPoolRelevant(
        ResourceOwnershipFlowLimit limit,
        bool hasArrayPoolEvidence)
    {
        if (limit.ResourceKind is { } resourceKind)
        {
            return resourceKind
                == ArrayPoolResourceEffectModel.BufferKind;
        }
        if (limit.Effect is { } effect)
        {
            return AppliesToArrayPoolObligation(effect)
                || effect.Sources.Any(static source =>
                    source.Model.Equals(
                        ArrayPoolResourceEffectModel.Identity));
        }
        if (limit.Model is { } model)
            return model.Equals(ArrayPoolResourceEffectModel.Identity);
        return hasArrayPoolEvidence;
    }

    static bool AppliesToArrayPoolObligation(
        ResolvedResourceEffect effect) =>
        !effect.ResourceKinds.Any()
        || effect.ResourceKinds.Any(static kind =>
            kind.Identity
                == ArrayPoolResourceEffectModel.BufferKind);

    static ArrayPoolOwnershipUse ProjectUse(
        ResourceOwnershipUse use,
        ResolvedResourceKindReference? resourceKind) =>
        new(
            use.Kind switch
            {
                ResourceOwnershipUseKind.Released
                    when resourceKind?.Identity
                            == ArrayPoolResourceEffectModel.BufferKind
                        || use.Effect is { } effect
                            && AppliesToArrayPoolObligation(effect) =>
                    ArrayPoolOwnershipUseKind.ReturnedToPool,
                ResourceOwnershipUseKind.Stored =>
                    ArrayPoolOwnershipUseKind.Stored,
                ResourceOwnershipUseKind.ReturnedToCaller =>
                    ArrayPoolOwnershipUseKind.ReturnedToCaller,
                _ => ArrayPoolOwnershipUseKind.Forwarded,
            },
            use.ILOffset,
            use.Call,
            use.CalleeParameterIndex);
}
