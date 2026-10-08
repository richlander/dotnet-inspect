using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

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
        string traversalTargetFramework,
        string pruningInventoryJson)
    {
        _ = memberSignature;
        _ = typeQueryId;

        BrowserCallGraphPruningInventory? pruningInventory =
            JsonSerializer.Deserialize(
                pruningInventoryJson,
                BrowserCallGraphJsonContext.Default
                    .BrowserCallGraphPruningInventory);
        PackageDependencyMemberCallGraphPlatformPruning? pruning =
            pruningInventory is
                {
                    Version: { } targetVersion,
                    Supplies: { } supplies,
                }
                ? BrowserPackageDependencyMemberCallGraphContinuationSource
                    .CreateCurrentRuntimePruning(
                        pruningInventory.Tfm,
                        targetVersion,
                        supplies.Select(
                            static supply =>
                                $"{supply.Package}|{supply.Version}"))
                : null;
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
                    .SelfAndRegisteredEcosystems,
                pruning);
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
        BrowserDirectUseClusterInspectionInfo inspection =
            await BrowserDirectUseClusterOperation.ExecuteAsync(
                sourcePackageId,
                sourceVersion,
                sourceTargetFramework,
                sourceAssembly,
                targetPackageId,
                targetVersion,
                targetTargetFramework,
                targetAssembly,
                selectedCluster);
        BrowserDirectUseClusterInspection result =
            BrowserDirectUseClusterWireProjection.Project(
                inspection);
        return JsonSerializer.Serialize(
            result,
            BrowserCallGraphJsonContext.Default
                .BrowserDirectUseClusterInspection);
    }

}
