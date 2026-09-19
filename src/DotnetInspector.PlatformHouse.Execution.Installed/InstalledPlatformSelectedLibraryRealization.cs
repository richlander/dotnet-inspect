using System.Diagnostics;
using DotnetInspector.Platforms;
using DotnetInspector.Platforms.Installed;

namespace DotnetInspector.PlatformHouse.Installed;

/// <summary>
/// Creates lazy installed capabilities for selected-target Library
/// realization.
/// </summary>
public static class InstalledPlatformSelectedLibraryRealization
{
    public static PlatformLibraryRealizationSource CreateReferenceSource(
        InstalledPlatformHouseAdapter adapter,
        PlatformHouseCandidateIdentity candidate)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(candidate);
        return new PlatformLibraryRealizationSource(
            adapter.Capabilities.ReferenceRealization,
            PlatformSourceFacet.Reference,
            async (request, target, remainingWork, association) =>
            {
                if (association is not null)
                {
                    return InvalidAttempt(
                        request,
                        target,
                        adapter.Capabilities.ReferenceRealization,
                        PlatformSourceFacet.Reference,
                        "installed-selected-reference-association");
                }

                long started = Stopwatch.GetTimestamp();
                InstalledPlatformHouseResult<
                    InstalledReferenceRealization> result =
                    await adapter.RealizeSelectedReferenceAsync(
                            request,
                            target,
                            remainingWork)
                        .ConfigureAwait(false);
                return PrepareReference(
                    request,
                    target,
                    result,
                    candidate,
                    Stopwatch.GetElapsedTime(started));
            });
    }

    public static PlatformLibraryRealizationSource
        CreateImplementationSource(
            InstalledPlatformHouseAdapter adapter,
            PlatformHouseCandidateIdentity candidate)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(candidate);
        return new PlatformLibraryRealizationSource(
            adapter.Capabilities.ImplementationRealization,
            PlatformSourceFacet.Implementation,
            async (request, target, remainingWork, association) =>
            {
                if (association is not null)
                {
                    return InvalidAttempt(
                        request,
                        target,
                        adapter.Capabilities.ImplementationRealization,
                        PlatformSourceFacet.Implementation,
                        "installed-selected-implementation-association");
                }

                long started = Stopwatch.GetTimestamp();
                InstalledPlatformHouseResult<
                    InstalledImplementationRealization> result =
                    await adapter.RealizeSelectedImplementationAsync(
                            request,
                            target,
                            remainingWork)
                        .ConfigureAwait(false);
                return PrepareImplementation(
                    request,
                    target,
                    result,
                    candidate,
                    Stopwatch.GetElapsedTime(started));
            });
    }

    static PlatformLibraryRealizationSourceAttempt PrepareReference(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        InstalledPlatformHouseResult<InstalledReferenceRealization> result,
        PlatformHouseCandidateIdentity candidate,
        TimeSpan elapsed) =>
        result switch
        {
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded success
                when InstalledPlatformLibraryMaterializer
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
                        success.Value.Libraries.Count,
                        success.Value.Libraries.Count(
                            static library =>
                                library.Documentation is not null),
                        success.Value.Libraries.Sum(
                            static library =>
                                library.TotalContentLength),
                        elapsed)),
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded success =>
                InvalidAttempt(
                    request,
                    target,
                    success.Contribution.Capability,
                    PlatformSourceFacet.Reference,
                    success.Contribution.Generation.Name,
                    Work(
                        success.Value.Libraries.Count,
                        success.Value.Libraries.Count(
                            static library =>
                                library.Documentation is not null),
                        success.Value.Libraries.Sum(
                            static library =>
                                library.TotalContentLength),
                        elapsed)),
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.NotSucceeded terminal =>
                new PlatformLibraryRealizationSourceAttempt.NotSucceeded(
                    terminal.Contribution,
                    RejectionKind(terminal.Diagnostic, terminal.Contribution),
                    WithElapsed(terminal.SourceWork, elapsed)),
            _ => throw new InvalidOperationException(
                "Unknown installed reference result."),
        };

    static PlatformLibraryRealizationSourceAttempt PrepareImplementation(
        PlatformHouseRequest request,
        PlatformFamilyTarget target,
        InstalledPlatformHouseResult<
            InstalledImplementationRealization> result,
        PlatformHouseCandidateIdentity candidate,
        TimeSpan elapsed) =>
        result switch
        {
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded success
                when InstalledPlatformLibraryMaterializer
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
                        success.Value.Libraries.Count,
                        0,
                        success.Value.Libraries.Sum(
                            static library => library.ContentLength),
                        elapsed)),
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.Succeeded success =>
                InvalidAttempt(
                    request,
                    target,
                    success.Contribution.Capability,
                    PlatformSourceFacet.Implementation,
                    success.Contribution.Generation.Name,
                    Work(
                        success.Value.Libraries.Count,
                        0,
                        success.Value.Libraries.Sum(
                            static library => library.ContentLength),
                        elapsed)),
            InstalledPlatformHouseResult<
                InstalledImplementationRealization>.NotSucceeded terminal =>
                new PlatformLibraryRealizationSourceAttempt.NotSucceeded(
                    terminal.Contribution,
                    RejectionKind(terminal.Diagnostic, terminal.Contribution),
                    WithElapsed(terminal.SourceWork, elapsed)),
            _ => throw new InvalidOperationException(
                "Unknown installed implementation result."),
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

    static PlatformHouseConsumedWork WithElapsed(
        PlatformHouseConsumedWork? work,
        TimeSpan elapsed) =>
        Work(
            work?.Assemblies ?? 0,
            work?.XmlDocuments ?? 0,
            work?.Bytes ?? 0,
            elapsed + (work?.Elapsed ?? TimeSpan.Zero));

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
