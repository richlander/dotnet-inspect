using System.Diagnostics;
using DotnetInspector.Platforms;

namespace DotnetInspector.PlatformHouse;

/// <summary>
/// Realizes one implementation Library for an already exact Platform target.
/// </summary>
public static class PlatformHouseExactImplementationLibraryExecutor
{
    public static async ValueTask<
        PlatformLibraryArtifactMaterializationOutcome> ExecuteAsync(
        PlatformHouseRequest request,
        PlatformLibraryRealizationSource source,
        string identityPrefix)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(identityPrefix);

        request.CancellationToken.ThrowIfCancellationRequested();
        if (request.Target is not PlatformTargetDemand.Exact exact
            || request.Operation is not PlatformHouseOperation.Realize
            {
                View: PlatformViewDemand.Implementation,
                Population: PlatformPopulationDemand.Library,
            }
            || source.Facet != PlatformSourceFacet.Implementation
            || source.SelectedAssociationCapability is not null
            || request.Sources.SelectionFor(
                    PlatformSourceFacet.Implementation)
                is not { Capabilities.Count: 1 } selection
            || !ReferenceEquals(
                selection.Capabilities[0],
                source.Capability)
            || !request.Sources.Authorizes(
                source.Facet,
                source.Capability))
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Rejected(
                    request,
                    ZeroConsumed(),
                    PlatformHouseRejectionKind.InvalidSourcePlan,
                    $"{identityPrefix}.source-set-invalid"));
        }
        if (request.Work.MaxSourceOperations == 0
            || request.Work.MaxAssemblies == 0
            || request.Work.MaxBytes == 0
            || request.Work.MaxDuration == TimeSpan.Zero)
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Incomplete(
                    request,
                    ZeroConsumed(),
                    $"{identityPrefix}.work-incomplete"));
        }

        long started = Stopwatch.GetTimestamp();
        PlatformHouseWorkBudget remaining = new(
            request.Work.MaxSourceOperations - 1,
            request.Work.MaxTargetCandidates,
            request.Work.MaxAssemblies,
            request.Work.MaxXmlDocuments,
            request.Work.MaxPortablePdbs,
            request.Work.MaxSourceDocuments,
            request.Work.MaxBytes,
            request.Work.MaxForwardingHops,
            request.Work.MaxDuration);
        PlatformLibraryRealizationSourceAttempt attempt =
            await source.RealizeAsync(
                    request,
                    exact.Target,
                    remaining,
                    selectedAssociation: null)
                .ConfigureAwait(false);
        PlatformHouseConsumedWork consumed = Consumed(
            attempt.ConsumedWork,
            Stopwatch.GetElapsedTime(started));
        IReadOnlyList<PlatformSourceSettlement> settlements =
        [
            new(
                attempt.Contribution,
                attempt is PlatformLibraryRealizationSourceAttempt.Succeeded
                    ? PlatformSourceSettlementDisposition.Selected
                    : PlatformSourceSettlementDisposition.OutcomeRelevant),
        ];

        if (!AttemptCorresponds(
                request,
                exact.Target,
                source,
                attempt))
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Rejected(
                    request,
                    consumed,
                    PlatformHouseRejectionKind.InvalidOwnerResult,
                    $"{identityPrefix}.invalid-source-attempt",
                    retainedSettlements: settlements));
        }
        if (PlatformHouseLibraryRealizer.ExceedsBudget(
                consumed,
                request))
        {
            return Terminal(
                PlatformHouseLibraryRealizer.Incomplete(
                    request,
                    consumed,
                    $"{identityPrefix}.work-incomplete",
                    retainedSettlements: settlements));
        }

        if (attempt
            is PlatformLibraryRealizationSourceAttempt.Succeeded succeeded)
        {
            return await PlatformHouseArtifactMaterializer.MaterializeAsync(
                    request,
                    PlatformViewDemand.Implementation,
                    [succeeded.Item],
                    consumed,
                    identityPrefix)
                .ConfigureAwait(false);
        }

        var terminal =
            (PlatformLibraryRealizationSourceAttempt.NotSucceeded)attempt;
        PlatformLibraryRealizationResult realization =
            terminal.Contribution switch
            {
                PlatformSourceContribution.Unavailable =>
                    PlatformHouseLibraryRealizer.Unavailable(
                        request,
                        consumed,
                        terminal.Contribution,
                        $"{identityPrefix}.unavailable",
                        retainedSettlements: settlements),
                PlatformSourceContribution.Rejected =>
                    PlatformHouseLibraryRealizer.Rejected(
                        request,
                        consumed,
                        terminal.RejectionKind
                            ?? PlatformHouseRejectionKind.InvalidOwnerResult,
                        $"{identityPrefix}.rejected",
                        retainedSettlements: settlements),
                PlatformSourceContribution.Incomplete =>
                    PlatformHouseLibraryRealizer.Incomplete(
                        request,
                        consumed,
                        $"{identityPrefix}.incomplete",
                        retainedSettlements: settlements),
                PlatformSourceContribution.Failed =>
                    PlatformHouseLibraryRealizer.Failed(
                        request,
                        consumed,
                        [terminal.Contribution],
                        [PlatformHouseFailureKind.Source],
                        cancellationObserved: false,
                        evidenceName: $"{identityPrefix}.failed",
                        retainedSettlements: settlements),
                _ => throw new InvalidOperationException(
                    "Unknown exact implementation realization attempt."),
            };
        return Terminal(realization);
    }

    private static bool AttemptCorresponds(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformLibraryRealizationSource source,
        PlatformLibraryRealizationSourceAttempt attempt) =>
        attempt is not null
        && ReferenceEquals(attempt.Contribution.Request, request.Snapshot)
        && attempt.Contribution.ExactTarget == target
        && attempt.Contribution.Facet == source.Facet
        && ReferenceEquals(
            attempt.Contribution.Capability,
            source.Capability);

    private static PlatformHouseConsumedWork Consumed(
        PlatformHouseConsumedWork attempt,
        TimeSpan elapsed) =>
        new(
            sourceOperations: 1,
            targetCandidates: 0,
            assemblies: attempt.Assemblies,
            xmlDocuments: attempt.XmlDocuments,
            portablePdbs: attempt.PortablePdbs,
            sourceDocuments: attempt.SourceDocuments,
            bytes: attempt.Bytes,
            forwardingHops: attempt.ForwardingHops,
            targetComparisons: 0,
            elapsed);

    private static PlatformLibraryArtifactMaterializationOutcome Terminal(
        PlatformLibraryRealizationResult realization) =>
        new PlatformLibraryArtifactMaterializationOutcome.Terminal(
            (PlatformLibraryRealizationResult.Terminal)realization);

    private static PlatformHouseConsumedWork ZeroConsumed() =>
        new(
            sourceOperations: 0,
            targetCandidates: 0,
            assemblies: 0,
            xmlDocuments: 0,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: 0,
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed: TimeSpan.Zero);
}
