using System.Runtime.Versioning;

using DotnetInspector.Queries;
using ILInspector.Metadata;

namespace DotnetInspect.Web;

internal sealed record BrowserTypeExtensionCandidatePopulation(
    MetadataExtensionReceiverSelection Receiver,
    AssemblyContextResult<MetadataExtensionRelationPopulationOutcome> Result);

internal static class BrowserTypeExtensionCandidateInspectionOperation
{
    internal const int MaximumRowsPerParticipant = 512;

    [SupportedOSPlatform("browser")]
    internal static BrowserTypeExtensionCandidatePopulation Execute(
        BrowserInspectionScope scope,
        AssemblyReferenceIdentity receiverAssembly,
        MetadataTypeDefinitionName receiverType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(receiverAssembly);
        ArgumentNullException.ThrowIfNull(receiverType);
        cancellationToken.ThrowIfCancellationRequested();

        var receiver = new MetadataExtensionReceiverSelection(
            receiverAssembly,
            receiverType);
        AssemblyContextResult<MetadataExtensionRelationPopulationOutcome>
            result = scope.UseSurface(
                group => AssemblyContextExtensionCandidatesQuery.Execute(
                    group,
                    new AssemblyContextExtensionCandidatePopulationRequest(
                        receiver,
                        MetadataOperationPolicy.Unbounded,
                        count: new(),
                        rows: new(MaximumRowsPerParticipant)),
                    cancellationToken));
        return new(receiver, result);
    }
}
