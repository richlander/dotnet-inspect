namespace DotnetInspector.Queries.Definitions;

/// <summary>
/// Prepares the immutable Package declaration used by noun-command inspection.
/// Realization and exact-version selection remain with the ordinary owners.
/// </summary>
public static class WorkspacePackageScenarioDerivation
{
    public static WorkspaceSharePacketProjectionResult Prepare(
        WorkspaceSharePacket packet,
        string packageId,
        string? version)
    {
        ArgumentNullException.ThrowIfNull(packet);
        ArgumentException.ThrowIfNullOrWhiteSpace(packageId);

        if (packet.SelectedContextIndex is int contextIndex)
        {
            // Keep existing declarations, including floating versions and conflicts.
            // The exact-Package operation validates the restored Root, not this edit.
            foreach (int tabIndex in packet.Contexts[contextIndex].TabIndexes)
            {
                WorkspaceShareTab tab = packet.Tabs[tabIndex];
                if (tab.SourceKind == WorkspaceShareSourceKind.Package
                    && string.Equals(tab.Source, packageId,
                        StringComparison.OrdinalIgnoreCase))
                    return WorkspaceSharePacketProjectionResult.Success(packet);
            }
        }
        WorkspacePackageComponentEditResult added =
            WorkspacePackageComponentEditor.Add(
                packet, packageId, version,
                framework: packet.Contexts.Count == 0
                    ? TraversalTargetFrameworkPolicy.ProductDefault.TargetFramework
                    : null,
                runtimeIdentifier: null);
        return added.Succeeded
            ? WorkspaceSharePacketProjectionResult.Success(added.Packet!)
            : WorkspaceSharePacketProjectionResult.Failed(
                WorkspaceSharePacketProjectionFailureKind.InvalidDefinitionSet,
                "workspace.package", added.Failure!.Detail);
    }
}
