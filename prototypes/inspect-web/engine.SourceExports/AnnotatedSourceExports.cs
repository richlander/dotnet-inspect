using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using ILInspector.Decompiler;
using ILInspector.Research;
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
        MemberSourceProjection source = await ProjectMemberAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            typeQueryId,
            memberName,
            memberSignature,
            selectorKey,
            metadataToken,
            styleOptionsJson,
            factRows: false);
        BrowserAnnotatedSource annotated = BrowserAnnotatedSource.Create(
            source.Document,
            source.Provenance,
            source.ContextLimitation,
            source.InvocationDestinations,
            source.DestinationUnavailableReason);
        return JsonSerializer.Serialize(
            annotated,
            BrowserSourceJsonContext.Default.BrowserAnnotatedSource);
    }

    /// <summary>
    /// One Research-issued Finding census projected through its Facts and Annotated Source views.
    /// The receipt scopes every non-null fact-row key and every document fact-id sidecar key.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberFindingCensus(
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
        MemberSourceProjection source = await ProjectMemberAsync(
            packageId,
            version,
            targetFramework,
            assemblyName,
            typeIdentity,
            typeQueryId,
            memberName,
            memberSignature,
            selectorKey,
            metadataToken,
            styleOptionsJson,
            factRows: true);
        BrowserMemberFindingCensus census = BrowserMemberFindingCensus.Create(
            source.Projection.FactCensusReceipt,
            source.Projection.Facts,
            source.Document,
            source.Projection.SourceDocumentFactIdentities,
            source.Provenance,
            source.ContextLimitation,
            source.InvocationDestinations,
            source.DestinationUnavailableReason);
        return JsonSerializer.Serialize(
            census,
            BrowserSourceJsonContext.Default.BrowserMemberFindingCensus);
    }

    static async Task<MemberSourceProjection> ProjectMemberAsync(
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
        string styleOptionsJson,
        bool factRows)
    {
        _ = memberSignature;
        await using BrowserMemberResolution.ScopedResolution resolved =
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
                        FactRows: factRows,
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

        return new MemberSourceProjection(
            projection.Projection,
            document,
            $"Annotated by dotnet-inspect from {participant.Coordinate.PackageId} "
                + $"{participant.Coordinate.Version} {participant.Asset.Path}",
            projection.ContextLimitation is { } limitation
                ? $"{limitation.Kind}: {limitation.Detail}"
                : null,
            destinations,
            destinations is null
                ? BrowserAnnotatedSourceCapabilityUnavailableReason.ContextUnavailable
                : BrowserAnnotatedSourceCapabilityUnavailableReason.NotProjected);
    }

    private sealed record MemberSourceProjection(
        ResearchViews.MemberProjectionResult Projection,
        AnnotatedSourceDocument Document,
        string Provenance,
        string? ContextLimitation,
        BrowserAnnotatedSourceInvocationDestination[]? InvocationDestinations,
        BrowserAnnotatedSourceCapabilityUnavailableReason DestinationUnavailableReason);
}
