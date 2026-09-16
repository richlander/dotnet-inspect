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
        return new(
            evidence.Method,
            evidence.Member,
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
            ],
            [
                .. evidence.Parameters
                    .Where(static parameter =>
                        parameter.ValueType.Kind == TypeRefKind.SzArray)
                    .Select(static parameter =>
                        new ArrayPoolParameterOwnership(
                            parameter.ParameterIndex,
                            [
                                .. parameter.Uses.Select(
                                    use => ProjectUse(
                                        use,
                                        resourceKind: null)),
                            ],
                            parameter.IsComplete)),
            ],
            evidence.IsComplete);
    }

    static ArrayPoolOwnershipUse ProjectUse(
        ResourceOwnershipUse use,
        ResolvedResourceKindReference? resourceKind) =>
        new(
            use.Kind switch
            {
                ResourceOwnershipUseKind.Released
                    when resourceKind?.Identity
                            == ArrayPoolResourceEffectModel.BufferKind
                        || use.Effect?.ResourceKinds.Any(kind =>
                            kind.Identity
                                == ArrayPoolResourceEffectModel.BufferKind)
                            == true =>
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
