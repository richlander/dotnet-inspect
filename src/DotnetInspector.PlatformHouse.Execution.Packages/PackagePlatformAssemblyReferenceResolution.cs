using DotnetInspector.Platforms.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse.Packages;

/// <summary>
/// Adapts one successful package-backed reference snapshot to the
/// source-neutral Platform assembly-reference executor.
/// </summary>
public static class PackagePlatformAssemblyReferenceResolver
{
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

        PlatformLibraryArtifactMaterializationItem item =
            PackagePlatformLibraryMaterializer.ReferenceItem(
                reference,
                library);
        return PlatformHouseAssemblyReferenceResolver.ResolveAsync(
            request,
            item,
            consumedWork);
    }
}
