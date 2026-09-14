using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one package assembly-semantic Find request into the shared
/// host-neutral inspection envelope.
/// </summary>
public static class PackageAssemblySemanticFindInspection
{
    public static async ValueTask<
        InspectionEnvelope<PackageAssemblySemanticFindResult>>
        ExecuteAsync(
            PackageAssemblySemanticFindRequest request,
            PackageSourceOperationLease sourceOperation,
            PackagePayloadAcquisitionPlan payloadAcquisition,
            CancellationToken cancellationToken = default) =>
        await ExecuteAsync(
            request,
            sourceOperation,
            payloadAcquisition,
            observer: null,
            cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
        InspectionEnvelope<PackageAssemblySemanticFindResult>>
        ExecuteAsync(
            PackageAssemblySemanticFindRequest request,
            PackageSourceOperationLease sourceOperation,
            PackagePayloadAcquisitionPlan payloadAcquisition,
            IPackageAssemblySemanticFindObserver? observer,
            CancellationToken cancellationToken = default)
    {
        PackageAssemblySemanticFindResult content =
            await PackageAssemblySemanticFindQuery.ExecuteToResultAsync(
                request,
                sourceOperation,
                payloadAcquisition,
                observer,
                cancellationToken).ConfigureAwait(false);

        return new(
            content,
            new InspectionShare.NonProjectable(
                "package-assembly-semantic-find/share",
                "Package assembly-semantic Find requests do not yet have a canonical Workspace Share projection."));
    }
}
