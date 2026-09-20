using System.Diagnostics;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;

namespace DotnetInspector.PlatformHouse.Packages;

/// <summary>
/// Creates a lazy package-backed capability for selected-target complete
/// reference population realization.
/// </summary>
public static class PackagePlatformSelectedReferencePopulationRealization
{
    public static PlatformReferencePopulationRealizationSource CreateSource(
        PackagePlatformHouseAdapter adapter,
        Func<
            PlatformHouseRequest,
            PlatformHouseWorkBudget,
            PackageSourceOperationLease>
            issueOperation,
        PlatformHouseCandidateIdentity candidate)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(issueOperation);
        ArgumentNullException.ThrowIfNull(candidate);
        return new PlatformReferencePopulationRealizationSource(
            adapter.ReferenceRealization,
            async (request, target, remainingWork, association) =>
            {
                long started = Stopwatch.GetTimestamp();
                PackagePlatformHouseResult<PackageReferenceRealization>
                    result;
                switch (association)
                {
                    case PlatformTargetDiscoveryCandidate<
                        PackagePlatformTargetDiscoveryAssociation> paired
                        when paired.Target == target:
                        result =
                            await adapter.RealizeSelectedReferenceAsync(
                                    request,
                                    paired.Association.Discovery,
                                    paired.Association.Selection,
                                    remainingWork,
                                    issueOperation(
                                        request,
                                        remainingWork))
                                .ConfigureAwait(false);
                        break;
                    case null:
                        result =
                            await adapter.RealizeSelectedReferenceAsync(
                                    request,
                                    target,
                                    remainingWork,
                                    issueOperation(
                                        request,
                                        remainingWork))
                                .ConfigureAwait(false);
                        break;
                    default:
                        return InvalidAttempt(
                            request,
                            target,
                            adapter.ReferenceRealization,
                            "package-selected-reference-population-association");
                }

                return Prepare(
                    request,
                    target,
                    result,
                    candidate,
                    remainingWork,
                    Stopwatch.GetElapsedTime(started));
            },
            adapter.TargetDiscovery,
            adapter.AssociationRoute);
    }

    static PlatformReferencePopulationRealizationSourceAttempt Prepare(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PackagePlatformHouseResult<PackageReferenceRealization> result,
        PlatformHouseCandidateIdentity candidate,
        PlatformHouseWorkBudget remainingWork,
        TimeSpan elapsed) =>
        result switch
        {
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded success
                when PackagePlatformLibraryMaterializer
                    .TryPrepareSelectedReferencePopulation(
                        request,
                        target,
                        success,
                        out IReadOnlyList<
                            PlatformPopulationLibraryArtifactMaterializationItem>
                            items) =>
                new PlatformReferencePopulationRealizationSourceAttempt
                    .Succeeded(
                        (PlatformSourceContribution.Realization)
                            success.Contribution,
                        candidate,
                        items,
                        Work(success.Value, elapsed)),
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded success =>
                InvalidAttempt(
                    request,
                    target,
                    success.Contribution.Capability,
                    success.Contribution.Generation.Name,
                    Work(success.Value, elapsed)),
            PackagePlatformHouseResult<
                PackageReferenceRealization>.NotSucceeded terminal =>
                new PlatformReferencePopulationRealizationSourceAttempt
                    .NotSucceeded(
                        terminal.Contribution,
                        RejectionKind(
                            terminal.Diagnostic,
                            terminal.Contribution),
                        PlatformLibraryRealizationAttemptWork
                            .PreserveOrReserve(
                                terminal.SourceWork,
                                terminal.Contribution,
                                remainingWork,
                                mayReadXmlDocuments: false,
                                elapsed)),
            _ => throw new InvalidOperationException(
                "Unknown package-backed reference result."),
        };

    static PlatformReferencePopulationRealizationSourceAttempt InvalidAttempt(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformSourceCapabilityIdentity capability,
        string generationName,
        PlatformHouseConsumedWork? consumedWork = null) =>
        new PlatformReferencePopulationRealizationSourceAttempt.NotSucceeded(
            new PlatformSourceContribution.Rejected(
                PlatformSourceFacet.Reference,
                capability,
                request.Snapshot,
                PlatformSourceGeneration.Create(generationName),
                target),
            PlatformHouseRejectionKind.InvalidOwnerResult,
            consumedWork);

    static PlatformHouseConsumedWork Work(
        PackageReferenceRealization realization,
        TimeSpan elapsed) =>
        new(
            sourceOperations: 0,
            targetCandidates: 0,
            assemblies: realization.Libraries.Length,
            xmlDocuments: realization.Libraries.Count(
                static library => library.Documentation is not null),
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes: realization.Libraries.Sum(
                static library => library.TotalContentLength),
            forwardingHops: 0,
            targetComparisons: 0,
            elapsed);

    static PlatformHouseRejectionKind? RejectionKind(
        PackagePlatformSourceDiagnostic diagnostic,
        PlatformSourceContribution contribution) =>
        contribution is not PlatformSourceContribution.Rejected
            ? null
            : diagnostic.Kind switch
            {
                PackagePlatformSourceDiagnosticKind.InvalidSelection =>
                    PlatformHouseRejectionKind.InvalidRequest,
                PackagePlatformSourceDiagnosticKind.InvalidCoordinate
                    or PackagePlatformSourceDiagnosticKind.UnsupportedTarget =>
                        PlatformHouseRejectionKind
                            .InvalidTargetCorrespondence,
                _ => PlatformHouseRejectionKind.InvalidOwnerResult,
            };
}
