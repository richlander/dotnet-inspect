using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse.Packages;

/// <summary>
/// Adapts one successful package-backed reference snapshot to the
/// source-neutral Platform assembly-reference executor.
/// </summary>
public static class PackagePlatformAssemblyReferenceResolver
{
    /// <summary>
    /// Prepares one successful package-backed result for source-neutral policy
    /// settlement.
    /// </summary>
    public static PlatformAssemblyReferenceSourceAttempt PrepareAttempt(
        PlatformHouseRequest request,
        PackagePlatformHouseResult<
            PackageReferenceRealization>.Succeeded reference,
        PlatformHouseCandidateIdentity candidate)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(candidate);
        return new PlatformAssemblyReferenceSourceAttempt.Succeeded(
            candidate,
            ReferenceItem(request, reference));
    }

    /// <summary>
    /// Prepares one terminal package-backed result for source-neutral policy
    /// settlement.
    /// </summary>
    public static PlatformAssemblyReferenceSourceAttempt PrepareAttempt(
        PackagePlatformHouseResult<
            PackageReferenceRealization>.NotSucceeded reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new PlatformAssemblyReferenceSourceAttempt.NotSucceeded(
            reference.Contribution,
            RejectionKind(reference));
    }

    /// <summary>
    /// Projects either a successful package reference snapshot or its
    /// resource-free source-terminal result.
    /// </summary>
    public static ValueTask<
        PlatformHouseOutcome<AssemblyBindingDecision>> ResolveAsync(
            PlatformHouseRequest request,
            PackagePlatformHouseResult<
                PackageReferenceRealization> reference,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference switch
        {
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded success =>
                    ResolveAsync(request, success, consumedWork),
            PackagePlatformHouseResult<
                PackageReferenceRealization>.NotSucceeded terminal =>
                    ValueTask.FromResult(
                        PlatformHouseAssemblyReferenceResolver
                            .ProjectSourceTerminal(
                                request,
                                terminal.Contribution,
                                consumedWork,
                                RejectionKind(terminal))),
            _ => throw new InvalidOperationException(
                "Unknown package-backed Platform result."),
        };
    }

    public static ValueTask<
        PlatformHouseOutcome<AssemblyBindingDecision>> ResolveAsync(
            PlatformHouseRequest request,
            PackagePlatformHouseResult<
                PackageReferenceRealization>.Succeeded reference,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(consumedWork);
        request.CancellationToken.ThrowIfCancellationRequested();

        return PlatformHouseAssemblyReferenceResolver.ResolveAsync(
            request,
            ReferenceItem(request, reference),
            consumedWork);
    }

    static PlatformLibraryArtifactMaterializationItem ReferenceItem(
        PlatformHouseRequest request,
        PackagePlatformHouseResult<
            PackageReferenceRealization>.Succeeded reference)
    {
        AssemblyReferenceIdentity? requestedIdentity =
            request.Operation
                is PlatformHouseOperation.ResolveAssemblyReference
                {
                    Request.Target:
                        AssemblyBindingTarget.AssemblyReference target,
                }
                ? target.Identity
                : null;
        PackageReferenceLibrary? library =
            requestedIdentity is null
                ? reference.Value.Libraries.FirstOrDefault()
                : reference.Value.Libraries.FirstOrDefault(
                    candidate =>
                        AssemblyReferenceIdentity.EquivalentComparer.Equals(
                            requestedIdentity,
                            candidate.Identity))
                    ?? reference.Value.Libraries.FirstOrDefault();
        if (library is null)
        {
            throw new ArgumentException(
                "A successful package-backed reference realization must contain a library.",
                nameof(reference));
        }

        return PackagePlatformLibraryMaterializer.ReferenceItem(
            reference,
            library);
    }

    static PlatformHouseRejectionKind? RejectionKind(
        PackagePlatformHouseResult<
            PackageReferenceRealization>.NotSucceeded terminal) =>
        terminal.Contribution is not PlatformSourceContribution.Rejected
            ? null
            : terminal.Diagnostic.Kind switch
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
