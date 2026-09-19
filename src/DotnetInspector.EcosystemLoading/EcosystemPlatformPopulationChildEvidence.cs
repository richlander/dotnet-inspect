using DotnetInspector.PlatformHouse;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.EcosystemLoading;

/// <summary>
/// Exact resource-free PlatformHouse request and population receipt retained
/// by one Ecosystem child settlement.
/// </summary>
public sealed class EcosystemPlatformPopulationChildEvidence
{
    internal EcosystemPlatformPopulationChildEvidence(
        PlatformPopulationRealizationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        Receipt = receipt;
        Request = receipt.HouseReceipt.Request;
    }

    public PlatformHouseRequestSnapshot Request { get; }
    public PlatformPopulationRealizationReceipt Receipt { get; }
}

/// <summary>
/// Type-correct projection of PlatformHouse population settlements into one
/// bound Ecosystem loader request.
/// </summary>
public static class EcosystemPlatformPopulationChildProjection
{
    public static EcosystemPopulationCompletedChild PlatformCompletedChild<
        TInputs>(
        this EcosystemPopulationLoadRequest<TInputs> request,
        EcosystemPopulationChildRequestIdentity childRequest,
        EcosystemPopulationChildReceiptIdentity childReceipt,
        PlatformPopulationRealizationResult.Completed population,
        ArtifactSetSession artifacts)
        where TInputs : class, IEcosystemPopulationLoadInputs
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(childRequest);
        ArgumentNullException.ThrowIfNull(childReceipt);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(artifacts);
        ValidateChildAssociation(request, childRequest, childReceipt);
        if (population.Value.Members
            .SelectMany(static member => member.Library.Contents)
            .Any(
                content => !ReferenceEquals(
                    content.ArtifactReference.Generation,
                    artifacts.Generation)))
        {
            throw new ArgumentException(
                "The Artifact session must own every completed Platform population content item.",
                nameof(artifacts));
        }

        var settlement = new EcosystemPopulationChildSettlement(
            request.Identity,
            childRequest,
            childReceipt,
            EcosystemPopulationChildSettlementKind.Completed,
            EcosystemPopulationChildCompletionKind.Members,
            new EcosystemPlatformPopulationChildEvidence(
                population.Receipt));
        EcosystemPopulationLibraryOwnership[] ownerships =
        [
            .. population.Value.Members.Select(
                (member, index) =>
                    new EcosystemPopulationLibraryOwnership(
                        population.Owners[index],
                        member.Role switch
                        {
                            PlatformPopulationMemberRole.Focus =>
                                EcosystemPopulationLibraryRole.Focus,
                            PlatformPopulationMemberRole.BindingSupport =>
                                EcosystemPopulationLibraryRole.BindingSupport,
                            _ => throw new InvalidOperationException(
                                "Unknown Platform population member role."),
                        })),
        ];
        return new EcosystemPopulationCompletedChild(
            settlement,
            ownerships,
            artifacts);
    }

    public static EcosystemPopulationChildSettlement PlatformTerminalChild<
        TInputs>(
        this EcosystemPopulationLoadRequest<TInputs> request,
        EcosystemPopulationChildRequestIdentity childRequest,
        EcosystemPopulationChildReceiptIdentity childReceipt,
        PlatformPopulationRealizationResult.Terminal population)
        where TInputs : class, IEcosystemPopulationLoadInputs
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(childRequest);
        ArgumentNullException.ThrowIfNull(childReceipt);
        ArgumentNullException.ThrowIfNull(population);
        ValidateChildAssociation(request, childRequest, childReceipt);

        EcosystemPopulationChildSettlementKind kind =
            population.Outcome switch
            {
                PlatformHouseOutcome<
                    PlatformPopulationRealizationValue>.Unavailable =>
                    EcosystemPopulationChildSettlementKind.Unavailable,
                PlatformHouseOutcome<
                    PlatformPopulationRealizationValue>.Ambiguous =>
                    EcosystemPopulationChildSettlementKind.Ambiguous,
                PlatformHouseOutcome<
                    PlatformPopulationRealizationValue>.Incomplete =>
                    EcosystemPopulationChildSettlementKind.Incomplete,
                PlatformHouseOutcome<
                    PlatformPopulationRealizationValue>.Rejected =>
                    EcosystemPopulationChildSettlementKind.Rejected,
                PlatformHouseOutcome<
                    PlatformPopulationRealizationValue>.Failed =>
                    EcosystemPopulationChildSettlementKind.Failed,
                _ => throw new InvalidOperationException(
                    "Unknown terminal Platform population outcome."),
            };
        return new EcosystemPopulationChildSettlement(
            request.Identity,
            childRequest,
            childReceipt,
            kind,
            completionKind: null,
            platformEvidence: new EcosystemPlatformPopulationChildEvidence(
                population.Receipt));
    }

    static void ValidateChildAssociation<TInputs>(
        EcosystemPopulationLoadRequest<TInputs> request,
        EcosystemPopulationChildRequestIdentity childRequest,
        EcosystemPopulationChildReceiptIdentity childReceipt)
        where TInputs : class, IEcosystemPopulationLoadInputs
    {
        if (!ReferenceEquals(childRequest.ParentRequest, request.Identity))
        {
            throw new ArgumentException(
                "The child request must be issued by this exact loader request.",
                nameof(childRequest));
        }
        if (!ReferenceEquals(childReceipt.Request, childRequest))
        {
            throw new ArgumentException(
                "The child receipt must settle the exact child request.",
                nameof(childReceipt));
        }
    }
}
