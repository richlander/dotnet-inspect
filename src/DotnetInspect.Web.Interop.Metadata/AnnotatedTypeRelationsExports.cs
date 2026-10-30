using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Sections;
using TsJsExport;

using DotnetInspect.Web;

namespace DotnetInspect.Web.Interop.Metadata;

[SupportedOSPlatform("browser")]
[JsExportJsonOutput(
    nameof(MetadataExports.QueryAnnotatedTypeRelations),
    typeof(BrowserAnnotatedTypeRelationsInspection))]
public static partial class MetadataExports
{
    [JSExport]
    public static async Task<string> QueryAnnotatedTypeRelations(
        string packageId,
        string version,
        string targetFramework,
        string assemblyName,
        string typeDefinitionId,
        string? runtimeIdentifier,
        string workspaceJson)
    {
        BrowserAnnotatedTypeRelationsInspection projected;
        try
        {
            (BrowserPackageRequest[] requests, int rootIndex) =
                TypeProjectionRequests(
                    packageId,
                    version,
                    targetFramework,
                    runtimeIdentifier,
                    workspaceJson);
            await using BrowserScopeResolution resolution =
                await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(
                    requests);
            BrowserInspectionScope scope = resolution.Scope;
            BrowserPackageCoordinate requestedRoot =
                resolution.RequestedCoordinates[rootIndex];
            BrowserPackageCoordinate root = scope.Coordinate(requestedRoot);
            BrowserWorkspaceParticipant participant =
                scope.SurfaceParticipant(
                    root,
                    root.CompileAsset(assemblyName));
            BrowserAnnotatedTypeRelationsExecution execution =
                BrowserAnnotatedTypeRelationsOperation.Execute(
                    scope,
                    participant,
                    typeDefinitionId);
            projected = Project(execution);
        }
        catch (Exception exception)
        {
            projected = new(
                BrowserAnnotatedTypeRelationsOutcome.Failed,
                Inspection: null,
                exception.Message);
        }

        return JsonSerializer.Serialize(
            projected,
            BrowserMetadataJsonContext.Default
                .BrowserAnnotatedTypeRelationsInspection);
    }

    static BrowserAnnotatedTypeRelationsInspection Project(
        BrowserAnnotatedTypeRelationsExecution execution) =>
        execution switch
        {
            BrowserAnnotatedTypeRelationsExecution.Available available =>
                new(
                    BrowserAnnotatedTypeRelationsOutcome.Available,
                    new(
                        AnnotatedTypeRelationsJson.Project(
                            available.Inspection.Content),
                        available.Inspection.Share,
                        available.Inspection.Diagnostics),
                    Reason: null),
            BrowserAnnotatedTypeRelationsExecution.Rejected rejected =>
                new(
                    BrowserAnnotatedTypeRelationsOutcome.Rejected,
                    Inspection: null,
                    rejected.Failure.Message),
            BrowserAnnotatedTypeRelationsExecution.Unavailable unavailable =>
                new(
                    BrowserAnnotatedTypeRelationsOutcome.Unavailable,
                    Inspection: null,
                    unavailable.Reason),
            _ => throw new InvalidOperationException(
                "Unknown Browser Annotated Type Relations execution."),
        };
}
