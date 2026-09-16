using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;

namespace DotnetInspector.Sections;

/// <summary>
/// Executes one package assembly-semantic request into a package-grain
/// host-neutral inspection envelope.
/// </summary>
public static class PackageAssemblySemanticQueryInspection
{
    public static async ValueTask<
        InspectionEnvelope<PackageAssemblySemanticQueryDocument>>
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
        InspectionEnvelope<PackageAssemblySemanticQueryDocument>>
        ExecuteAsync(
            PackageAssemblySemanticFindRequest request,
            PackageSourceOperationLease sourceOperation,
            PackagePayloadAcquisitionPlan payloadAcquisition,
            IPackageAssemblySemanticQueryNonterminalSink? nonterminalSink,
            CancellationToken cancellationToken = default)
    {
        var bridge = nonterminalSink is null
            ? null
            : new SinkBridge(nonterminalSink);
        PackageAssemblySemanticFindDocument evidence =
            await PackageAssemblySemanticFindQuery.ExecuteToDocumentAsync(
                request,
                sourceOperation,
                payloadAcquisition,
                bridge,
                cancellationToken).ConfigureAwait(false);

        return new(
            new PackageAssemblySemanticQueryDocument(evidence),
            new InspectionShare.NonProjectable(
                "package-assembly-semantic-query/share",
                "Package assembly-semantic Query requests do not yet have a canonical Workspace Share projection."));
    }

    private sealed class SinkBridge(
        IPackageAssemblySemanticQueryNonterminalSink sink)
        : IPackageAssemblySemanticFindNonterminalSink
    {
        public ValueTask ReportAsync(
            PackageAssemblySemanticFindCandidateOutcome outcome,
            CancellationToken cancellationToken) =>
            sink.ReportAsync(
                PackageAssemblySemanticQueryCandidateOutcome.From(outcome),
                cancellationToken);
    }
}
