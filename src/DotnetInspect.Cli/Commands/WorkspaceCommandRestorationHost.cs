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
    ICompleteRestorationHost<WorkspaceRealizationOperationLease>,
    IAsyncDisposable
{
    readonly WorkspaceRealizationCoordinator _coordinator = new();

    public async ValueTask<
        CompleteRestorationHostResult<WorkspaceRealizationOperationLease>>
        ConstructAsync(
            ICompleteRestorationIntentAuthority authority,
            CompleteRestorationPlan plan,
            CompleteWorkspacePreparationCallback prepare,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(prepare);

        WorkspaceRealizationCandidateStartResult start =
            await _coordinator.BeginCandidateAsync(
                plan.WorkspacePlan,
                cancellationToken).ConfigureAwait(false);
        if (start
            is not WorkspaceRealizationCandidateStartResult.Prepared prepared)
        {
            return start switch
            {
                WorkspaceRealizationCandidateStartResult.Superseded =>
                    new CompleteRestorationHostResult<
                        WorkspaceRealizationOperationLease>.Superseded(),
                WorkspaceRealizationCandidateStartResult.Closed =>
                    Failed(
                        "The Workspace realization coordinator is closed."),
                _ => Failed(
                    "The Workspace realization could not be prepared."),
            };
        }

        CompleteWorkspacePreparationResult workspacePreparation;
        using (WorkspaceRealizationConstructionLease construction =
            prepared.Candidate.EnterConstruction())
        {
            workspacePreparation = await prepare(
                construction.Workspace,
                authority.Revocation).ConfigureAwait(false);
        }

        if (workspacePreparation
            is not CompleteWorkspacePreparationResult.Prepared
                preparedWorkspace)
        {
            CompleteRestorationFailure? cleanup =
                await SettleCandidateAsync(prepared.Candidate)
                    .ConfigureAwait(false);
            if (cleanup is not null)
            {
                return new CompleteRestorationHostResult<
                    WorkspaceRealizationOperationLease>.Failed(cleanup);
            }
            return workspacePreparation switch
            {
                CompleteWorkspacePreparationResult.Failed failed =>
                    new CompleteRestorationHostResult<
                        WorkspaceRealizationOperationLease>.Failed(
                            failed.Failure),
                CompleteWorkspacePreparationResult.Superseded =>
                    new CompleteRestorationHostResult<
                        WorkspaceRealizationOperationLease>.Superseded(),
                _ => Failed(
                    "The Workspace preparation returned an unsupported result."),
            };
        }

        WorkspaceRealizationCandidateCompletionResult completion =
            await _coordinator.CompleteCandidateAsync(
                prepared.Candidate,
                cancellationToken).ConfigureAwait(false);
        if (completion
            is not WorkspaceRealizationCandidateCompletionResult.Ready ready)
        {
            CompleteRestorationFailure? cleanup =
                await SettleCandidateAsync(prepared.Candidate)
                    .ConfigureAwait(false);
            if (cleanup is not null)
            {
                return new CompleteRestorationHostResult<
                    WorkspaceRealizationOperationLease>.Failed(cleanup);
            }
            var rejected =
                (WorkspaceRealizationCandidateCompletionResult.Rejected)
                    completion;
            return Failed(
                rejected.RuntimeFailure is null
                    ? $"Workspace completion was rejected: {rejected.Reason}."
                    : $"Workspace completion was rejected: {rejected.Reason}: "
                        + $"{rejected.RuntimeFailure}.");
        }

        if (!ReferenceEquals(
                ready.Definition,
                preparedWorkspace.Activation.Snapshot.Definition))
        {
            CompleteRestorationFailure? cleanup =
                await SettleCandidateAsync(prepared.Candidate)
                    .ConfigureAwait(false);
            return cleanup is null
                ? Failed(
                    "Workspace completion did not retain the exact complete-restoration definition.")
                : new CompleteRestorationHostResult<
                    WorkspaceRealizationOperationLease>.Failed(cleanup);
        }

        WorkspaceRealizationCutoverResult cutover =
            _coordinator.CutOver(prepared.Candidate);
        if (cutover is not WorkspaceRealizationCutoverResult.Activated)
        {
            CompleteRestorationFailure? cleanup =
                await SettleCandidateAsync(prepared.Candidate)
                    .ConfigureAwait(false);
            if (cleanup is not null)
            {
                return new CompleteRestorationHostResult<
                    WorkspaceRealizationOperationLease>.Failed(cleanup);
            }
            var rejected = (WorkspaceRealizationCutoverResult.Rejected)cutover;
            return Failed(
                $"Workspace activation was rejected: {rejected.Reason}.");
        }

        WorkspaceRealizationOperationAdmission admission =
            await _coordinator.EnterOperationAsync(cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            CompleteRestorationFailure? cleanup =
                await CloseCoordinatorAsync().ConfigureAwait(false);
            if (cleanup is not null)
            {
                return new CompleteRestorationHostResult<
                    WorkspaceRealizationOperationLease>.Failed(cleanup);
            }
            var unavailable =
                (WorkspaceRealizationOperationAdmission.Unavailable)admission;
            return Failed(
                unavailable.RuntimeFailure is null
                    ? $"Workspace operation admission was unavailable: "
                        + $"{unavailable.Reason}."
                    : $"Workspace operation admission was unavailable: "
                        + $"{unavailable.Reason}: "
                        + $"{unavailable.RuntimeFailure}.");
        }

        return new CompleteRestorationHostResult<
            WorkspaceRealizationOperationLease>.Activated(
                admitted.Lease,
                preparedWorkspace.Activation);
    }

    async ValueTask<CompleteRestorationFailure?> SettleCandidateAsync(
        WorkspaceRealizationCandidate candidate)
    {
        _ = _coordinator.AbandonCandidate(candidate);
        WorkspaceRealizationSettlement settlement =
            await candidate.Settlement.ConfigureAwait(false);
        return settlement.Succeeded
            ? null
            : new CompleteRestorationFailure.CleanupFailed(
                "The unpublished Workspace could not be settled after "
                    + $"{settlement.Reason}.");
    }

    async ValueTask<CompleteRestorationFailure?> CloseCoordinatorAsync()
    {
        WorkspaceRealizationCoordinatorCloseReport report =
            await _coordinator.CloseAsync().ConfigureAwait(false);
        return report.Settlements.All(static settlement =>
            settlement.Succeeded)
                ? null
                : new CompleteRestorationFailure.CleanupFailed(
                    "The Workspace realization coordinator could not settle "
                        + "every realization.");
    }

    static CompleteRestorationHostResult<
        WorkspaceRealizationOperationLease>.Failed Failed(string message) =>
        new(new CompleteRestorationFailure.HostConstructionFailed(message));

    public ValueTask DisposeAsync() => _coordinator.DisposeAsync();
}
