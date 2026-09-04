using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Decompiler;
using Analysis = ILInspector.Analysis;

using InspectWeb.Engine;
using InspectWeb.Engine.SourceFacade;

/// <summary>
/// Annotated source. The returned document and its viewer contract are the capability being
/// requested, so it stays with source; Analysis facts embedded in that product document do not
/// transfer ownership to another adapter.
/// </summary>
[SupportedOSPlatform("browser")]
public static partial class SourceExports
{
    /// <summary>
    /// One member's portable <c>AnnotatedSourceDocument</c>, produced by
    /// <see cref="AssemblyContextMemberProjectionQuery"/> over the participant that owns the
    /// member's implementation. The document is serialized by its owning product context, so the
    /// payload is the same artifact the CLI emits and the viewer validates.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberAnnotatedSource(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeIdentity,
        string typeQueryId,
        string memberName,
        string memberSignature,
        string selectorKey,
        int metadataToken,
        string styleOptionsJson)
    {
        _ = memberSignature;
        BrowserMemberResolution.ScopedResolution resolved =
            await BrowserMemberResolution.ImplementationMemberAsync(
                packageId,
                version,
                targetFramework,
                assemblyName,
                typeIdentity,
                memberName,
                selectorKey,
                metadataToken);
        BrowserInspectionScope scope = resolved.Scope;
        BrowserWorkspaceParticipant participant = resolved.ImplementationParticipant;
        Analysis.CallGraphMemberResolution resolution = resolved.Member;

        AssemblyMemberProjection projection = BrowserSurfaceProjection.Require(
            scope.UseImplementationParticipant(
                participant,
                (group, member) => AssemblyContextMemberProjectionQuery.ExecuteParticipant(
                    group,
                    member,
                    new AssemblyContextMemberProjectionRequest(
                        typeQueryId,
                        memberName,
                        MethodToken: resolution.BodyToken,
                        SourceDocument: true,
                        InvocationDestinations: true,
                        PrinterOptions: BrowserStyleOptions.Resolve(styleOptionsJson)))),
            $"Annotated source for '{typeQueryId}.{memberName}'");

        if (projection.Projection.SourceDocument is not { } document)
        {
            IReadOnlyList<DecompilerDiagnostic> diagnostics =
                projection.Projection.SourceDocumentFailure?.Diagnostics ?? [];
            throw new InvalidOperationException(
                diagnostics.Count > 0
                    ? $"Annotated source projection failed: "
                        + string.Join("; ", diagnostics.Select(item => item.ToString()))
                    : "Annotated source projection produced no document.");
        }

        BrowserAnnotatedSourceInvocationDestination[]? destinations =
            projection.ContextLimitation is null
                ?
                [
                    .. projection.InvocationDestinations.Select(destination =>
                        new BrowserAnnotatedSourceInvocationDestination(
                            destination.NodeId,
                            BrowserSourceWireProjection.Project(
                                BrowserCallGraphProjection.Target(
                                    destination.Target,
                                    [participant.Assembly.Identity],
                                    null,
                                    scope.SurfaceParticipants)))),
                ]
                : null;

        return JsonSerializer.Serialize(
            BrowserAnnotatedSource.Create(
                document,
                $"Annotated by dotnet-inspect from {participant.Coordinate.PackageId} "
                    + $"{participant.Coordinate.Version} {participant.Asset.Path}",
                projection.ContextLimitation is { } limitation
                    ? $"{limitation.Kind}: {limitation.Detail}"
                    : null,
                destinations,
                destinations is null
                    ? BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable
                    : BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected),
            BrowserSourceJsonContext.Default.BrowserAnnotatedSource);
    }
}
