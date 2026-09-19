using DotnetInspector.Platforms.Installed;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse.Installed;

/// <summary>
/// Adapts one installed reference result to the source-neutral Platform
/// assembly-reference executor.
/// </summary>
public static class InstalledPlatformAssemblyReferenceResolver
{
    public static ValueTask<
        PlatformHouseOutcome<AssemblyBindingDecision>> ResolveAsync(
            PlatformHouseRequest request,
            InstalledPlatformHouseResult<
                InstalledReferenceRealization> reference,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return reference switch
        {
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded success =>
                    ResolveAsync(request, success, consumedWork),
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.NotSucceeded terminal =>
                    ValueTask.FromResult(
                        PlatformHouseAssemblyReferenceResolver
                            .ProjectSourceTerminal(
                                request,
                                terminal.Contribution,
                                consumedWork,
                                RejectionKind(terminal))),
            _ => throw new InvalidOperationException(
                "Unknown installed Platform result."),
        };
    }

    public static ValueTask<
        PlatformHouseOutcome<AssemblyBindingDecision>> ResolveAsync(
            PlatformHouseRequest request,
            InstalledPlatformHouseResult<
                InstalledReferenceRealization>.Succeeded reference,
            PlatformHouseConsumedWork consumedWork)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(consumedWork);
        request.CancellationToken.ThrowIfCancellationRequested();

        AssemblyReferenceIdentity? requestedIdentity =
            request.Operation
                is PlatformHouseOperation.ResolveAssemblyReference
                {
                    Request.Target:
                        AssemblyBindingTarget.AssemblyReference target,
                }
                ? target.Identity
                : null;
        InstalledReferenceLibrary? library =
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
                "A successful installed reference realization must contain a library.",
                nameof(reference));
        }

        PlatformLibraryArtifactMaterializationItem item =
            InstalledPlatformLibraryMaterializer.CreateReferenceItem(
                reference,
                library);
        return PlatformHouseAssemblyReferenceResolver.ResolveAsync(
            request,
            item,
            consumedWork);
    }

    static PlatformHouseRejectionKind? RejectionKind(
        InstalledPlatformHouseResult<
            InstalledReferenceRealization>.NotSucceeded terminal) =>
        terminal.Contribution is not PlatformSourceContribution.Rejected
            ? null
            : terminal.Diagnostic.Kind switch
            {
                InstalledPlatformSourceDiagnosticKind.InvalidRequest =>
                    PlatformHouseRejectionKind.InvalidRequest,
                InstalledPlatformSourceDiagnosticKind.InvalidCoordinate =>
                    PlatformHouseRejectionKind
                        .InvalidTargetCorrespondence,
                _ => PlatformHouseRejectionKind.InvalidOwnerResult,
            };
}
