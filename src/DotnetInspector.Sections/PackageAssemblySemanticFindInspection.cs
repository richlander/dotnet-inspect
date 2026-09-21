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
        InspectionEnvelope<PackageAssemblySemanticFindDocument>>
        ExecuteAsync(
            PackageAssemblySemanticFindRequest request,
            PackageSourceOperationLease sourceOperation,
            PackagePayloadAcquisitionPlan payloadAcquisition,
            CancellationToken cancellationToken = default) =>
        await ExecuteAsync(
            request,
            sourceOperation,
            payloadAcquisition,
            nonterminalSink: null,
            cancellationToken).ConfigureAwait(false);

    public static async ValueTask<
        InspectionEnvelope<PackageAssemblySemanticFindDocument>>
        ExecuteAsync(
            PackageAssemblySemanticFindRequest request,
            PackageSourceOperationLease sourceOperation,
            PackagePayloadAcquisitionPlan payloadAcquisition,
            IPackageAssemblySemanticFindNonterminalSink? nonterminalSink,
            CancellationToken cancellationToken = default)
    {
        PackageAssemblySemanticFindDocument content =
            await PackageAssemblySemanticFindQuery.ExecuteToDocumentAsync(
                request,
                sourceOperation,
                payloadAcquisition,
                nonterminalSink,
                cancellationToken).ConfigureAwait(false);

        return new(
            new ResourcePath("package-assembly-semantic-find"),
            InspectionContentKind.Document,
            content,
            new InspectionPortableProjection.NonProjectable(
                InspectionPortableProjectionFailureReason.NotSupported));
    }
}
