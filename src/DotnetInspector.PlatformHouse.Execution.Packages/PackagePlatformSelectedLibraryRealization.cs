using System.Diagnostics;
using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Packages;

namespace DotnetInspector.PlatformHouse.Packages;

/// <summary>
/// Creates lazy package-backed capabilities for selected-target Library
/// realization.
/// </summary>
public static class PackagePlatformSelectedLibraryRealization
{
    public static PlatformLibraryRealizationSource CreateReferenceSource(
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
        return new PlatformLibraryRealizationSource(
            adapter.ReferenceRealization,
            PlatformSourceFacet.Reference,
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
                            PlatformSourceFacet.Reference,
                            "package-selected-reference-association");
                }

                return PrepareReference(
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

    public static PlatformLibraryRealizationSource
        CreateImplementationSource(
            PackagePlatformHouseAdapter adapter,
            string runtimeIdentifier,
            Func<
                PlatformHouseRequest,
                PlatformHouseWorkBudget,
                PackageSourceOperationLease>
                issueOperation,
            PlatformHouseCandidateIdentity candidate)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeIdentifier);
        ArgumentNullException.ThrowIfNull(issueOperation);
        ArgumentNullException.ThrowIfNull(candidate);
        return new PlatformLibraryRealizationSource(
            adapter.ImplementationRealization,
            PlatformSourceFacet.Implementation,
            async (request, target, remainingWork, association) =>
            {
                if (association is not null)
                {
                    return InvalidAttempt(
                        request,
                        target,
                        adapter.ImplementationRealization,
                        PlatformSourceFacet.Implementation,
                        "package-selected-implementation-association");
                }

                long started = Stopwatch.GetTimestamp();
                PackagePlatformHouseResult<
                    PackageImplementationRealization> result =
                    await adapter.RealizeSelectedImplementationAsync(
                            request,
                            target,
                            remainingWork,
                            runtimeIdentifier,
                            issueOperation(
                                request,
                                remainingWork))
                        .ConfigureAwait(false);
                return PrepareImplementation(
                    request,
                    target,
                    result,
                    candidate,
                    remainingWork,
                    Stopwatch.GetElapsedTime(started));
            });
    }

    static PlatformLibraryRealizationSourceAttempt PrepareReference(
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
                    .TryPrepareSelectedReference(
                        request,
                        target,
                        success,
                        out PlatformLibraryArtifactMaterializationItem?
                            item) =>
                new PlatformLibraryRealizationSourceAttempt.Succeeded(
                    (PlatformSourceContribution.Realization)
                        success.Contribution,
                    candidate,
                    item!,
                    Work(
                        success.Value.Libraries.Length,
                        success.Value.Libraries.Count(
                            static library =>
                                library.Documentation is not null),
                        success.Value.Libraries.Sum(
                            static library =>
                                library.TotalContentLength),
                        elapsed)),
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded success =>
                InvalidAttempt(
                    request,
                    target,
                    success.Contribution.Capability,
                    PlatformSourceFacet.Reference,
                    success.Contribution.Generation.Name,
                    Work(
                        success.Value.Libraries.Length,
                        success.Value.Libraries.Count(
                            static library =>
                                library.Documentation is not null),
                        success.Value.Libraries.Sum(
                            static library =>
                                library.TotalContentLength),
                        elapsed)),
            PackagePlatformHouseResult<
                PackageReferenceRealization>.NotSucceeded terminal =>
                new PlatformLibraryRealizationSourceAttempt.NotSucceeded(
                    terminal.Contribution,
                    RejectionKind(
                        terminal.Diagnostic,
                        terminal.Contribution),
                    PlatformLibraryRealizationAttemptWork
                        .PreserveOrReserve(
                            terminal.SourceWork,
                            terminal.Contribution,
                            remainingWork,
                            mayReadXmlDocuments: true,
                            elapsed)),
            _ => throw new InvalidOperationException(
                "Unknown package-backed reference result."),
        };

    static PlatformLibraryRealizationSourceAttempt PrepareImplementation(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PackagePlatformHouseResult<
            PackageImplementationRealization> result,
        PlatformHouseCandidateIdentity candidate,
        PlatformHouseWorkBudget remainingWork,
        TimeSpan elapsed) =>
        result switch
        {
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded success
                when PackagePlatformLibraryMaterializer
                    .TryPrepareSelectedImplementation(
                        request,
                        target,
                        success,
                        out PlatformLibraryArtifactMaterializationItem?
                            item) =>
                new PlatformLibraryRealizationSourceAttempt.Succeeded(
                    (PlatformSourceContribution.Realization)
                        success.Contribution,
                    candidate,
                    item!,
                    Work(
                        success.Value.Libraries.Length,
                        0,
                        success.Value.ConsumedBytes,
                        elapsed)),
            PackagePlatformHouseResult<
                PackageImplementationRealization>.Succeeded success =>
                InvalidAttempt(
                    request,
                    target,
                    success.Contribution.Capability,
                    PlatformSourceFacet.Implementation,
                    success.Contribution.Generation.Name,
                    Work(
                        success.Value.Libraries.Length,
                        0,
                        success.Value.ConsumedBytes,
                        elapsed)),
            PackagePlatformHouseResult<
                PackageImplementationRealization>.NotSucceeded terminal =>
                new PlatformLibraryRealizationSourceAttempt.NotSucceeded(
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
                "Unknown package-backed implementation result."),
        };

    static PlatformLibraryRealizationSourceAttempt InvalidAttempt(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        PlatformSourceCapabilityIdentity capability,
        PlatformSourceFacet facet,
        string generationName,
        PlatformHouseConsumedWork? consumedWork = null) =>
        new PlatformLibraryRealizationSourceAttempt.NotSucceeded(
            new PlatformSourceContribution.Rejected(
                facet,
                capability,
                request.Snapshot,
                PlatformSourceGeneration.Create(generationName),
                target),
            PlatformHouseRejectionKind.InvalidOwnerResult,
            consumedWork);

    static PlatformHouseConsumedWork Work(
        int assemblies,
        int xmlDocuments,
        long bytes,
        TimeSpan elapsed) =>
        new(
            sourceOperations: 0,
            targetCandidates: 0,
            assemblies,
            xmlDocuments,
            portablePdbs: 0,
            sourceDocuments: 0,
            bytes,
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
