using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

using DotnetInspect.Web;
using DotnetInspect.Web.Interop.CallGraph;
using TsJsExport;

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
[JsExportJsonInput(
    nameof(QueryRetainedMemberCallGraph),
    "requestJson",
    typeof(BrowserRetainedMemberCallGraphRequest))]
[JsExportJsonOutput(
    nameof(QueryRetainedMemberCallGraph),
    typeof(BrowserCallGraph))]
[JsExportJsonInput(
    nameof(QueryMemberCallGraph),
    "pruningInventoryJson",
    typeof(BrowserCallGraphPruningInventory))]
[JsExportJsonOutput(
    nameof(QueryMemberCallGraph),
    typeof(BrowserCallGraph))]
public static partial class CallGraphExports
{
    /// <summary>
    /// A dependency-aware member call graph rooted in one exact Package row
    /// from the active retained Workspace realization.
    /// </summary>
    [JSExport]
    public static async Task<string> QueryRetainedMemberCallGraph(
        string retainedDefinitionId,
        string realizationId,
        string navigationId,
        string requestJson)
    {
        BrowserRetainedMemberCallGraphRequest request =
            JsonSerializer.Deserialize(
                requestJson,
                BrowserCallGraphJsonContext.Default
                    .BrowserRetainedMemberCallGraphRequest)
            ?? throw new ArgumentException(
                "A retained member Call Graph request is required.",
                nameof(requestJson));
        ValidateRetainedRequest(request);
        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome> envelope =
            await RetainedCallGraphOperations.QueryAsync(
                retainedDefinitionId,
                realizationId,
                navigationId,
                request.PackageId,
                request.Version,
                request.TargetFramework,
                request.AssemblyName,
                request.TypeIdentity,
                request.MemberName,
                request.SelectorKey,
                request.MetadataToken,
                request.TraversalTargetFramework,
                request.RuntimeIdentifier,
                ParsePruning(request.PruningInventory));
        return SerializeGraph(
            envelope,
            request.PackageId,
            request.Version,
            request.TargetFramework);
    }

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
        string pruningInventoryJson,
        string? runtimeIdentifier)
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
                runtimeIdentifier,
                BrowserPackageWorkspace.ProductWorkspacePlan,
                PackageSupplyChainBaseline
                    .SelfAndRegisteredEcosystems,
                ParsePruning(pruningInventoryJson));
        return SerializeGraph(
            envelope,
            packageId,
            version,
            targetFramework);
    }

    static PackageDependencyMemberCallGraphPlatformPruning? ParsePruning(
        string pruningInventoryJson)
    {
        BrowserCallGraphPruningInventory? pruningInventory =
            JsonSerializer.Deserialize(
                pruningInventoryJson,
                BrowserCallGraphJsonContext.Default
                    .BrowserCallGraphPruningInventory);
        return ParsePruning(pruningInventory);
    }

    static PackageDependencyMemberCallGraphPlatformPruning? ParsePruning(
        BrowserCallGraphPruningInventory? pruningInventory) =>
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

    static void ValidateRetainedRequest(
        BrowserRetainedMemberCallGraphRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PackageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetFramework);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AssemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TypeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MemberName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.SelectorKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            request.TraversalTargetFramework);
    }

    static string SerializeGraph(
        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome> envelope,
        string packageId,
        string version,
        string targetFramework)
    {
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

    public static Task<string> QueryMemberCallGraph(
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
        string pruningInventoryJson) =>
        QueryMemberCallGraph(
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
            traversalTargetFramework,
            pruningInventoryJson,
            runtimeIdentifier: null);

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
