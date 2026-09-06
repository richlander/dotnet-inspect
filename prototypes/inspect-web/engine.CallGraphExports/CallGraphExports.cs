using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using Analysis = ILInspector.Analysis;

// The generated wwwroot/inspect-web-call-graph.js module binds exports.CallGraphExports.*, so
// this type stays in the global namespace. Its helpers and wire records live in
// InspectWeb.Engine.CallGraphFacade.
using InspectWeb.Engine;
using InspectWeb.Engine.CallGraphFacade;

/// <summary>
/// Package and platform call-graph expansion. Both traversals return the same browser call-graph
/// contract.
/// </summary>
/// <remarks>
/// Graph-target member projection stays in the metadata facade because it projects one API member
/// after navigation rather than expanding topology.
/// </remarks>
[SupportedOSPlatform("browser")]
public static partial class CallGraphExports
{
    /// <summary>
    /// A progressively acquired member call graph, produced by <see cref="MemberCallGraphSession"/>
    /// over one workspace spanning every package the site currently has open. Callers in a sibling
    /// package are only visible when that package is a participant of the same binding-consistent
    /// group, so the workspace is opened over the whole set rather than one package at a time.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryMemberCallGraph(
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
        string workspaceJson)
    {
        _ = memberSignature;
        _ = typeQueryId;

        (BrowserPackageRequest[] requests, int rootIndex) =
            MemberCallGraphRequests(
                packageId,
                version,
                targetFramework,
                workspaceJson);

        BrowserCallGraph graph;
        await using (BrowserScopeResolution resolution =
            await BrowserPackageWorkspace.ResolveAndOpenScopeAsync(requests))
        {
            if (resolution.RequestedCoordinates.Length != requests.Length)
            {
                throw new InvalidOperationException(
                    "The selected Call Graph context did not preserve its "
                    + "distinct package coordinates.");
            }
            BrowserInspectionScope scope = resolution.Scope;
            BrowserPackageCoordinate rootCoordinate =
                scope.Coordinate(resolution.RequestedCoordinates[rootIndex]);
            BrowserMemberResolution.Resolved resolved =
                BrowserMemberResolution.ResolveImplementationMember(
                    scope,
                    rootCoordinate,
                    assemblyName,
                    typeIdentity,
                    memberName,
                    selectorKey,
                    metadataToken);
            BrowserWorkspaceParticipant participant = resolved.ImplementationParticipant;
            Analysis.CallGraphMemberResolution memberResolution = resolved.Member;

            MemberCallGraphView view = scope.UseImplementation(group =>
            {
                using var session = new MemberCallGraphSession(
                    group,
                    participant.Assembly,
                    memberResolution.BodyToken);
                return session.HasCrossLibraryScope ? session.CrossLibrary() : session.Callers();
            });

            graph = BrowserCallGraphWireProjection.Project(
                BrowserCallGraphProjection.Project(scope, view));
        }

        // Keep JSON return provenance outside async cleanup for the generated typed facade.
        return JsonSerializer.Serialize(
            graph,
            BrowserCallGraphJsonContext.Default.BrowserCallGraph);
    }

    internal static (BrowserPackageRequest[] Requests, int RootIndex)
        MemberCallGraphRequests(
            string packageId,
            string version,
            string targetFramework,
            string workspaceJson)
    {
        BrowserWorkspacePackage[] workspace =
            JsonSerializer.Deserialize(
                workspaceJson,
                BrowserCallGraphJsonContext.Default.BrowserWorkspacePackageArray) ?? [];
        if (workspace.Length == 0)
        {
            return (
                [new BrowserPackageRequest(packageId, version, targetFramework)],
                0);
        }

        BrowserPackageRequest[] requests =
        [
            .. workspace.Select(entry => new BrowserPackageRequest(
                entry.Package,
                entry.Version,
                string.IsNullOrWhiteSpace(entry.Framework)
                    ? null
                    : entry.Framework)),
        ];
        int[] rootIndexes =
        [
            .. requests.Select((request, index) => (request, index))
                .Where(entry =>
                    string.Equals(
                        entry.request.PackageId,
                        packageId,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        entry.request.Version,
                        version,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(
                        entry.request.TargetFramework ?? "",
                        targetFramework,
                        StringComparison.OrdinalIgnoreCase))
                .Select(entry => entry.index),
        ];
        if (rootIndexes.Length != 1)
        {
            throw new InvalidOperationException(
                "The selected Call Graph context must contain the active "
                + "package coordinate exactly once.");
        }
        return (requests, rootIndexes[0]);
    }
}
