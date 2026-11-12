using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;

using DotnetInspector.Libraries;
using DotnetInspector.Presentation;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using ILInspector.Research;
using TsJsExport;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryRetainedPlatformTypeProjection),
    typeof(BrowserTypeMetadata))]
public static partial class MetadataExports
{
    [JSExport]
    public static async Task<string> QueryRetainedPlatformTypeProjection(
        string retainedDefinitionId,
        string realizationId,
        string navigationId,
        string typeIdentity) =>
        JsonSerializer.Serialize(
            await RetainedTypeProjectionAsync(
                BrowserRetainedWorkspaceActivationRegistry.Owner,
                retainedDefinitionId, realizationId, navigationId,
                typeIdentity).ConfigureAwait(false),
            BrowserMetadataJsonContext.Default.BrowserTypeMetadata);

    internal static async Task<BrowserTypeMetadata> RetainedTypeProjectionAsync(
        BrowserRetainedWorkspaceActivationOwner owner,
        string retainedDefinitionId,
        string realizationId,
        string navigationId,
        string typeIdentity,
        CancellationToken cancellationToken = default)
    {
        MetadataTypeDefinitionName type =
            BrowserExactMemberPolicy.ParseTypeIdentity(typeIdentity);
        var signature = await InspectRetainedPlatformAsync(
                owner, retainedDefinitionId, realizationId, navigationId,
                (library, lease, token) => ValueTask.FromResult(
                    TypeSignatureInspectionOperation.Execute(
                        new(library,
                            new(type, BrowserApiSurfacePolicy.ExtractionBounds)),
                        lease, token)),
                cancellationToken).ConfigureAwait(false);
        var metadata = await InspectRetainedPlatformAsync(
                owner, retainedDefinitionId, realizationId, navigationId,
                (library, lease, token) => ValueTask.FromResult(
                    TypeProjectionInspectionOperation.Execute(
                        new(library, type,
                            BrowserApiSurfacePolicy.ExtractionBounds,
                            IncludeRelationships: true),
                        lease, token)),
                cancellationToken).ConfigureAwait(false);
        ResearchViews.TypeProjectionResult? projection =
            (metadata.Content as LibraryTypeProjectionInspectionOutcome.Available)?.Projection;
        return new BrowserTypeMetadata(
            ExactTypeInspection: null,
            Hierarchy: null,
            GraphNodes: [.. (projection?.Graph?.Nodes ?? []).Select(static node =>
                new BrowserTypeGraphNode(node.Id, node.DisplayName,
                    node.Role.ToString().ToLowerInvariant()))],
            GraphEdges: [.. (projection?.Graph?.Edges ?? []).Select(static edge =>
                new BrowserTypeGraphEdge(edge.FromId, edge.ToId,
                    edge.Kind.ToString().ToLowerInvariant()))],
            TypeDependencyInspection: null,
            InspectionFailures:
            [
                .. metadata.Diagnostics.Select(static diagnostic =>
                    diagnostic.Summary.ToString()),
                .. signature.Diagnostics.Select(static diagnostic =>
                    diagnostic.Summary.ToString()),
            ])
        {
            TypeSignatureInspection = new(
                TypeSignatureInspectionJson.SerializeContent(signature.Content),
                signature.Share, signature.Diagnostics),
            TypeSignaturePresentation =
                signature.Content is TypeSignatureInspectionOutcome.Available available
                    ? ExactTypeInfoTsvPresentation.Project(available.Signature)
                    : null,
            TypeProjectionInspection = projection is null
                ? null
                : new(new(projection.Identity.Kind,
                    [.. projection.Interfaces], [.. projection.DerivedTypes],
                    [.. projection.Attributes], projection.EnumUnderlyingType),
                    metadata.Share, metadata.Diagnostics),
        };
    }

    private static async ValueTask<InspectionEnvelope<TContent>>
        InspectRetainedPlatformAsync<TContent>(
            BrowserRetainedWorkspaceActivationOwner owner,
            string retainedDefinitionId,
            string realizationId,
            string navigationId,
            Func<LibraryReference, LibraryOperationLease, CancellationToken,
                ValueTask<InspectionEnvelope<TContent>>> inspect,
            CancellationToken cancellationToken = default)
    {
        BrowserRetainedWorkspacePlatformOperationAdmission admission =
            await owner.EnterPlatformOperationAsync(
                retainedDefinitionId, realizationId, navigationId,
                cancellationToken).ConfigureAwait(false);
        if (admission is not BrowserRetainedWorkspacePlatformOperationAdmission.Admitted admitted)
            throw new InvalidOperationException(
                "The exact retained Platform realization is unavailable.");
        using WorkspaceRealizationOperationLease operation = admitted.Operation;
        if (admitted.Presentation.NativeActivation is not { } activation)
            throw new InvalidOperationException(
                "The retained Platform has no source-native Library authority.");
        CompleteRestorationSubjectResult<InspectionEnvelope<TContent>> result =
            await CompleteRestorationSelectedSubjectOperation.ExecuteAdmittedAsync(
                operation, activation,
                (target, lease, token) =>
                    inspect(target.Occurrence.Library, lease, token),
                cancellationToken).ConfigureAwait(false);
        if (result is not CompleteRestorationSubjectResult<
            InspectionEnvelope<TContent>>.Completed completed)
            throw new InvalidOperationException(
                "The retained Library could not supply its exact operation.");
        if (owner.Active is not { } active
            || active.RetainedDefinitionId != retainedDefinitionId
            || active.RealizationId != realizationId
            || !active.Platforms.Any(candidate =>
                ReferenceEquals(candidate, admitted.Presentation)))
            throw new InvalidOperationException(
                "The retained Platform realization was superseded.");
        return completed.Content;
    }
}
