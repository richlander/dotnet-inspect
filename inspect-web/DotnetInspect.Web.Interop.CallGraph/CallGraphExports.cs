using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using TsJsExport;

using DotnetInspect.Web;
using DotnetInspect.Web.Interop.CallGraph;

namespace DotnetInspect.Web.Interop.CallGraph;

/// <summary>
/// Package and platform call-graph expansion. Both traversals return the same browser call-graph
/// contract.
/// </summary>
/// <remarks>
/// Graph-target member projection stays in the metadata facade because it projects one API member
/// after navigation rather than expanding topology.
/// </remarks>
[SupportedOSPlatform("browser")]
[JsExportJsonOutput(
    nameof(QueryDirectUseClusters),
    typeof(BrowserDirectUseClusterInspection))]
public static partial class CallGraphExports
{
    /// <summary>
    /// A dependency-aware member call graph over one exact root package and an independent
    /// traversal target.
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
        string traversalTargetFramework)
    {
        _ = memberSignature;
        _ = typeQueryId;

        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome> envelope =
            await BrowserPackageWorkspace
                .QueryDependencyMemberCallGraphAsync(
                packageId,
                version,
                targetFramework,
                traversalTargetFramework,
                assemblyName,
                typeIdentity,
                memberName,
                selectorKey,
                metadataToken,
                BrowserPackageWorkspace.ProductWorkspacePlan,
                PackageSupplyChainBaseline
                    .SelfAndRegisteredEcosystems);
        if (envelope.Content
            is PackageDependencyMemberCallGraphInspectionOutcome
                .Unavailable unavailable)
        {
            throw new InvalidOperationException(
                $"The dependency-aware member call graph was unavailable ({unavailable.Reason}): {unavailable.Detail}");
        }
        var available =
            (PackageDependencyMemberCallGraphInspectionOutcome.Available)
                envelope.Content;
        BrowserCallGraph graph =
            BrowserCallGraphWireProjection.Project(
                BrowserCallGraphProjection.Project(
                    available.Document,
                    packageId,
                    version,
                    targetFramework),
                envelope.Diagnostics);

        // Keep JSON return provenance outside async cleanup for the generated typed facade.
        return JsonSerializer.Serialize(
            graph,
            BrowserCallGraphJsonContext.Default.BrowserCallGraph);
    }

    /// <summary>
    /// Direct-Use Clusters for one exact pair of package Libraries, optionally
    /// scoped to one observed cluster ordinal.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryDirectUseClusters(
        string sourcePackageId,
        string sourceVersion,
        string sourceTargetFramework,
        string sourceAssembly,
        string targetPackageId,
        string targetVersion,
        string targetTargetFramework,
        string targetAssembly,
        int selectedCluster)
    {
        if (selectedCluster < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectedCluster),
                "A Direct-Use Cluster ordinal cannot be negative.");
        }

        BrowserPackageRequest[] requests =
            SameCoordinate(
                sourcePackageId,
                sourceVersion,
                sourceTargetFramework,
                targetPackageId,
                targetVersion,
                targetTargetFramework)
                ?
                [
                    new(
                        sourcePackageId,
                        sourceVersion,
                        sourceTargetFramework),
                ]
                :
                [
                    new(
                        sourcePackageId,
                        sourceVersion,
                        sourceTargetFramework),
                    new(
                        targetPackageId,
                        targetVersion,
                        targetTargetFramework),
                ];
        await using BrowserScopeResolution resolution =
            await BrowserPackageWorkspace.RunPackageOperationAsync(
                deadline => BrowserPackageWorkspace.ResolveAndOpenScopeAsync(
                    requests,
                    deadline.Token),
                BrowserPackageWorkspace.PackageOperationTimeout);
        BrowserInspectionScope scope = resolution.Scope;
        BrowserPackageCoordinate sourceCoordinate =
            resolution.RequestedCoordinates[0];
        BrowserPackageCoordinate targetCoordinate =
            requests.Length == 1
                ? sourceCoordinate
                : resolution.RequestedCoordinates[1];
        BrowserWorkspaceParticipant source =
            scope.LibraryParticipant(sourceCoordinate, sourceAssembly);
        BrowserWorkspaceParticipant target =
            scope.LibraryParticipant(targetCoordinate, targetAssembly);
        InspectionEnvelope<AssemblyPairCallUseInspectionOutcome>
            pairInspection =
                scope.UseImplementation(
                    group => AssemblyPairCallUseInspection.Execute(
                        group,
                        source.Assembly,
                        target.Assembly));
        InspectionEnvelope<AssemblyPairDirectUseClusterInspectionOutcome>
            clusterInspection =
                AssemblyPairDirectUseClusterInspection.Execute(
                    pairInspection);
        BrowserDirectUseClusterInspection result =
            BrowserDirectUseClusterWireProjection.Project(
                BrowserDirectUseClusterProjection.Project(
                    clusterInspection,
                    selectedCluster == 0 ? null : selectedCluster));
        return JsonSerializer.Serialize(
            result,
            BrowserCallGraphJsonContext.Default
                .BrowserDirectUseClusterInspection);
    }

    static bool SameCoordinate(
        string firstPackageId,
        string firstVersion,
        string firstTargetFramework,
        string secondPackageId,
        string secondVersion,
        string secondTargetFramework) =>
        firstPackageId.Equals(
            secondPackageId,
            StringComparison.OrdinalIgnoreCase)
        && firstVersion.Equals(
            secondVersion,
            StringComparison.OrdinalIgnoreCase)
        && firstTargetFramework.Equals(
            secondTargetFramework,
            StringComparison.OrdinalIgnoreCase);
}
