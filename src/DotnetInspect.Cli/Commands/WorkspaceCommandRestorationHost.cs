using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Cli.Commands;

internal sealed class WorkspaceCommandRestorationIntent(
    CancellationToken revocation)
    : ICompleteRestorationIntentAuthority
{
    public CompleteRestorationIntentIdentity Identity { get; } = new();

    public CompleteRestorationIntentStatus Status =>
        revocation.IsCancellationRequested
            ? CompleteRestorationIntentStatus.Cancelled
            : CompleteRestorationIntentStatus.Current;

    public CancellationToken Revocation => revocation;
}

internal sealed class WorkspaceCommandRestorationHost :
    ICompleteRestorationHost<InspectionWorkspace>
{
    public async ValueTask<
        CompleteRestorationHostResult<InspectionWorkspace>>
        ConstructAsync(
            ICompleteRestorationIntentAuthority authority,
            CompleteRestorationPlan plan,
            CompleteWorkspacePreparationCallback prepare,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(prepare);

        var workspace = new InspectionWorkspace(plan.WorkspacePlan);
        CompleteWorkspacePreparationResult workspacePreparation;
        try
        {
            workspacePreparation = await prepare(
                workspace,
                authority.Revocation).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            await CloseAfterFailureAsync(workspace, failure)
                .ConfigureAwait(false);
            throw;
        }

        if (workspacePreparation
            is not CompleteWorkspacePreparationResult.Prepared
                preparedWorkspace)
        {
            CompleteRestorationFailure? cleanup =
                await CloseAsync(workspace)
                    .ConfigureAwait(false);
            if (cleanup is not null)
            {
                return new CompleteRestorationHostResult<
                    InspectionWorkspace>.Failed(cleanup);
            }
            return workspacePreparation switch
            {
                CompleteWorkspacePreparationResult.Failed failed =>
                    new CompleteRestorationHostResult<
                        InspectionWorkspace>.Failed(
                            failed.Failure),
                CompleteWorkspacePreparationResult.Superseded =>
                    new CompleteRestorationHostResult<
                        InspectionWorkspace>.Superseded(),
                _ => Failed(
                    "The Workspace preparation returned an unsupported result."),
            };
        }

        if (!ReferenceEquals(
                workspace.Identity,
                preparedWorkspace.Activation.Workspace))
        {
            CompleteRestorationFailure? cleanup =
                await CloseAsync(workspace)
                    .ConfigureAwait(false);
            return cleanup is null
                ? Failed(
                    "Workspace preparation did not retain the directly "
                        + "owned Workspace identity.")
                : new CompleteRestorationHostResult<
                    InspectionWorkspace>.Failed(cleanup);
        }

        return new CompleteRestorationHostResult<
            InspectionWorkspace>.Activated(
                workspace,
                preparedWorkspace.Activation);
    }

    static async ValueTask<CompleteRestorationFailure?> CloseAsync(
        InspectionWorkspace workspace)
    {
        try
        {
            InspectionWorkspaceCloseReport report =
                await workspace.CloseAsync().ConfigureAwait(false);
            return report.Succeeded
                ? null
                : new CompleteRestorationFailure.CleanupFailed(
                    "The directly owned Workspace could not release every "
                        + "participant.");
        }
        catch (Exception failure)
        {
            return new CompleteRestorationFailure.CleanupFailed(
                "The directly owned Workspace could not be closed: "
                    + failure.Message);
        }
    }

    static async ValueTask CloseAfterFailureAsync(
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
                    "DotnetInspect.Cli.WorkspaceCleanupReport"] =
                    report;
            }
        }
        catch (Exception cleanupFailure)
        {
            failure.Data[
                "DotnetInspect.Cli.WorkspaceCleanupFailure"] =
                cleanupFailure;
        }
    }

    static CompleteRestorationHostResult<
        InspectionWorkspace>.Failed Failed(string message) =>
        new(new CompleteRestorationFailure.HostConstructionFailed(message));
}
