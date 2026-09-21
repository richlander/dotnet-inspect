using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

internal static class DirectWorkspaceOperationLifetime
{
    internal static async Task CloseAsync(
        InspectionWorkspace workspace,
        string operation)
    {
        InspectionWorkspaceCloseReport report =
            await workspace.CloseAsync().ConfigureAwait(false);
        if (!report.Succeeded)
        {
            throw new InvalidOperationException(
                $"{operation} Workspace cleanup did not succeed: "
                    + $"{report.Groups.Count(static group => !group.Succeeded)} "
                    + "group release(s), "
                    + $"{report.LibraryAdmissions.Count(static admission => !admission.Succeeded)} "
                    + "Library admission(s), and "
                    + $"{report.ArtifactSessionCleanupFailures.Length} "
                    + "Artifact session cleanup(s) failed.");
        }
    }

    internal static async Task CloseAfterFailureAsync(
        InspectionWorkspace workspace,
        Exception failure)
    {
        try
        {
            InspectionWorkspaceCloseReport report =
                await workspace.CloseAsync().ConfigureAwait(false);
            if (!report.Succeeded)
            {
                failure.Data[
                    "DotnetInspector.Queries.WorkspaceCleanupReport"] =
                    report;
            }
        }
        catch (Exception cleanupFailure)
        {
            failure.Data[
                "DotnetInspector.Queries.WorkspaceCleanupFailure"] =
                cleanupFailure;
        }
    }
}
