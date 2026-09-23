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
        using (sourceOperation)
        {
            PackageAssemblySemanticFindQuery.ValidateExecution(
                request,
                sourceOperation,
                payloadAcquisition,
                cancellationToken);
            sourceOperation.CancellationToken.ThrowIfCancellationRequested();
            NuGetFetch.PackageSourceTimeout? deadline =
                PackageAssemblySemanticQueryCompletion.OperationDeadline(
                    request.Population);
            if (deadline is not null)
            {
                sourceOperation.ValidatePopulationOwnership(
                    request.Population);
                return CreateEnvelope(
                    new PackageAssemblySemanticQueryDocument(
                        request.Population,
                        deadline));
            }

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

            return CreateEnvelope(
                new PackageAssemblySemanticQueryDocument(
                    evidence,
                    bridge?.Outcomes));
        }
    }

    private static InspectionEnvelope<PackageAssemblySemanticQueryDocument>
        CreateEnvelope(PackageAssemblySemanticQueryDocument document) =>
        new(
            document,
            new InspectionShare.NonProjectable(
                "package-assembly-semantic-query/share",
                "Package assembly-semantic Query requests do not yet have a canonical Workspace Share projection."));

    private sealed class SinkBridge(
        IPackageAssemblySemanticQueryNonterminalSink sink)
        : IPackageAssemblySemanticFindNonterminalSink
    {
        internal List<PackageAssemblySemanticQueryCandidateOutcome> Outcomes
            { get; } = [];

        public async ValueTask ReportAsync(
            PackageAssemblySemanticFindCandidateOutcome outcome,
            CancellationToken cancellationToken)
        {
            PackageAssemblySemanticQueryCandidateOutcome projected =
                PackageAssemblySemanticQueryCandidateOutcome.From(outcome);
            await sink.ReportAsync(
                projected,
                cancellationToken).ConfigureAwait(false);
            Outcomes.Add(projected);
        }
    }
}
