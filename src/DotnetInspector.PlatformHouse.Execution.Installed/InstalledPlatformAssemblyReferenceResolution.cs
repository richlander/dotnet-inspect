using DotnetInspector.Platforms.Installed;
using ILInspector.Metadata;

namespace DotnetInspector.PlatformHouse.Installed;

/// <summary>
/// Adapts one successful installed reference snapshot to the source-neutral
/// Platform assembly-reference executor.
/// </summary>
public static class InstalledPlatformAssemblyReferenceResolver
{
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
}
