using System.Diagnostics;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;

namespace DotnetInspector.PlatformHouse.Installed;

/// <summary>
/// Creates a lazy installed capability for selected-target complete reference
/// population realization.
/// </summary>
public static class InstalledPlatformSelectedReferencePopulationRealization
{
    public static PlatformReferencePopulationRealizationSource CreateSource(
        InstalledPlatformHouseAdapter adapter,
        PlatformHouseCandidateIdentity candidate)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(candidate);
        return new PlatformReferencePopulationRealizationSource(
            adapter.Capabilities.ReferenceRealization,
            async (request, target, remainingWork, association) =>
            {
                if (association is not null)
                {
                    return InvalidAttempt(
                        request,
                        target,
                        adapter.Capabilities.ReferenceRealization,
                        "installed-selected-reference-population-association");
                }

                long started = Stopwatch.GetTimestamp();
                InstalledPlatformHouseResult<
                    InstalledReferenceRealization> result =
                    await adapter.RealizeSelectedReferenceAsync(
                            request,
                            target,
                            remainingWork)
                        .ConfigureAwait(false);
                return Prepare(
                    request,
                    target,
                    result,
                    candidate,
                    remainingWork,
                    Stopwatch.GetElapsedTime(started));
            });
    }

    static PlatformReferencePopulationRealizationSourceAttempt Prepare(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        InstalledPlatformHouseResult<InstalledReferenceRealization> result,
        PlatformHouseCandidateIdentity candidate,
        PlatformHouseWorkBudget remainingWork,
        TimeSpan elapsed) =>
        result switch
        {
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded success
                when InstalledPlatformLibraryMaterializer
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
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded success =>
                InvalidAttempt(
                    request,
                    target,
                    success.Contribution.Capability,
                    success.Contribution.Generation.Name,
                    Work(success.Value, elapsed)),
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.NotSucceeded terminal =>
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
                "Unknown installed reference result."),
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
        InstalledReferenceRealization realization,
        TimeSpan elapsed) =>
        new(
            sourceOperations: 0,
            targetCandidates: 0,
            assemblies: realization.Libraries.Count,
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
        InstalledPlatformSourceDiagnostic diagnostic,
        PlatformSourceContribution contribution) =>
        contribution is not PlatformSourceContribution.Rejected
            ? null
            : diagnostic.Kind switch
            {
                InstalledPlatformSourceDiagnosticKind.InvalidRequest =>
                    PlatformHouseRejectionKind.InvalidRequest,
                InstalledPlatformSourceDiagnosticKind.InvalidCoordinate =>
                    PlatformHouseRejectionKind
                        .InvalidTargetCorrespondence,
                _ => PlatformHouseRejectionKind.InvalidOwnerResult,
            };
}
