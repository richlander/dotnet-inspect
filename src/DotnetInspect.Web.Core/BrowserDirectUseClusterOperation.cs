using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Sections;

namespace DotnetInspect.Web;

[SupportedOSPlatform("browser")]
internal static class BrowserDirectUseClusterOperation
{
    internal static async Task<BrowserDirectUseClusterInspectionInfo> ExecuteAsync(
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
        return BrowserDirectUseClusterProjection.Project(
            clusterInspection,
            selectedCluster == 0 ? null : selectedCluster);
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
