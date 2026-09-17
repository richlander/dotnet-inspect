using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Web;

internal sealed record BrowserPreparedWorkspaceActivation(
    BrowserWorkspaceRealizationCandidate Candidate,
    CompleteWorkspaceActivation Workspace);

[SupportedOSPlatform("browser")]
internal sealed class BrowserCompleteRestorationHost(
    BrowserWorkspaceRealizationHost realizationHost)
    : ICompleteRestorationHost<BrowserPreparedWorkspaceActivation>
{
    readonly BrowserWorkspaceRealizationHost _realizationHost =
        realizationHost
        ?? throw new ArgumentNullException(nameof(realizationHost));

    public async ValueTask<
        CompleteRestorationHostResult<BrowserPreparedWorkspaceActivation>>
        ConstructAsync(
            ICompleteRestorationIntentAuthority authority,
            CompleteRestorationPlan plan,
            CompleteWorkspacePreparationCallback prepare,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(prepare);

        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                authority.Revocation);
        CancellationToken operationToken = operationCancellation.Token;
        BrowserWorkspaceRealizationCandidateStartResult started;
        try
        {
            started = await _realizationHost.BeginCandidateAsync(
                plan.WorkspacePlan,
                operationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return authority.Status
                    == CompleteRestorationIntentStatus.Superseded
                ? new CompleteRestorationHostResult<
                    BrowserPreparedWorkspaceActivation>.Superseded()
                : new CompleteRestorationHostResult<
                    BrowserPreparedWorkspaceActivation>.Failed(
                        new CompleteRestorationFailure.Cancelled(
                            "Browser Workspace restoration was cancelled."));
        }
        if (started
            is BrowserWorkspaceRealizationCandidateStartResult.Superseded)
        {
            return new CompleteRestorationHostResult<
                BrowserPreparedWorkspaceActivation>.Superseded();
        }
        if (started is BrowserWorkspaceRealizationCandidateStartResult.Closed)
        {
            return Failed(
                "The Browser Workspace realization host is closed.");
        }
        if (started
            is BrowserWorkspaceRealizationCandidateStartResult
                .CapacityUnavailable unavailable)
        {
            return Failed(
                $"Browser Workspace realization capacity is unavailable "
                    + $"({unavailable.Capacity.Charged}/"
                    + $"{unavailable.Capacity.Limit} charged).");
        }

        BrowserWorkspaceRealizationCandidate candidate =
            ((BrowserWorkspaceRealizationCandidateStartResult.Prepared)started)
                .Candidate;
        CompleteWorkspacePreparationResult preparation;
        try
        {
            using WorkspaceRealizationConstructionLease construction =
                candidate.EnterConstruction();
            preparation = await prepare(
                construction.Workspace,
                authority.Revocation).ConfigureAwait(false);
        }
        catch
        {
            await RetireAsync(candidate, cancelled: false)
                .ConfigureAwait(false);
            throw;
        }

        if (preparation is CompleteWorkspacePreparationResult.Failed failed)
        {
            CompleteRestorationFailure? cleanup =
                await RetireAsync(candidate, cancelled: false)
                    .ConfigureAwait(false);
            return cleanup is null
                ? new CompleteRestorationHostResult<
                    BrowserPreparedWorkspaceActivation>.Failed(failed.Failure)
                : new CompleteRestorationHostResult<
                    BrowserPreparedWorkspaceActivation>.Failed(cleanup);
        }
        if (preparation is CompleteWorkspacePreparationResult.Superseded
            || !IsCurrent(authority))
        {
            CompleteRestorationFailure? cleanup =
                await RetireAsync(candidate, cancelled: true)
                    .ConfigureAwait(false);
            return cleanup is null
                ? new CompleteRestorationHostResult<
                    BrowserPreparedWorkspaceActivation>.Superseded()
                : new CompleteRestorationHostResult<
                    BrowserPreparedWorkspaceActivation>.Failed(cleanup);
        }

        WorkspaceRealizationCandidateCompletionResult completion;
        try
        {
            completion = await _realizationHost.CompleteCandidateAsync(
                candidate,
                operationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            CompleteRestorationFailure? cleanup =
                await RetireAsync(candidate, cancelled: true)
                    .ConfigureAwait(false);
            return cleanup is null
                ? authority.Status
                    == CompleteRestorationIntentStatus.Superseded
                    ? new CompleteRestorationHostResult<
                        BrowserPreparedWorkspaceActivation>.Superseded()
                    : new CompleteRestorationHostResult<
                        BrowserPreparedWorkspaceActivation>.Failed(
                            new CompleteRestorationFailure.Cancelled(
                                "Browser Workspace restoration was cancelled."))
                : new CompleteRestorationHostResult<
                    BrowserPreparedWorkspaceActivation>.Failed(cleanup);
        }
        if (completion
            is not WorkspaceRealizationCandidateCompletionResult.Ready)
        {
            CompleteRestorationFailure? cleanup =
                await SettleRejectedCompletionAsync(candidate)
                    .ConfigureAwait(false);
            if (cleanup is not null)
            {
                return new CompleteRestorationHostResult<
                    BrowserPreparedWorkspaceActivation>.Failed(cleanup);
            }

            var rejected =
                (WorkspaceRealizationCandidateCompletionResult.Rejected)
                    completion;
            return Failed(
                $"The Browser Workspace candidate could not become ready: "
                    + $"{rejected.Reason}.");
        }
        if (!IsCurrent(authority))
        {
            CompleteRestorationFailure? cleanup =
                await RetireAsync(candidate, cancelled: true)
                    .ConfigureAwait(false);
            return cleanup is null
                ? new CompleteRestorationHostResult<
                    BrowserPreparedWorkspaceActivation>.Superseded()
                : new CompleteRestorationHostResult<
                    BrowserPreparedWorkspaceActivation>.Failed(cleanup);
        }

        CompleteWorkspaceActivation activation =
            ((CompleteWorkspacePreparationResult.Prepared)preparation)
                .Activation;
        return new CompleteRestorationHostResult<
            BrowserPreparedWorkspaceActivation>.Activated(
                new BrowserPreparedWorkspaceActivation(
                    candidate,
                    activation),
                activation);
    }

    async ValueTask<CompleteRestorationFailure?> SettleRejectedCompletionAsync(
        BrowserWorkspaceRealizationCandidate candidate)
    {
        if (!candidate.Settlement.IsCompleted)
        {
            BrowserWorkspaceRealizationCandidateRetirementResult retirement =
                _realizationHost.AbandonCandidate(candidate);
            if (retirement
                is BrowserWorkspaceRealizationCandidateRetirementResult
                    .Rejected)
            {
                return new CompleteRestorationFailure.HostConstructionFailed(
                    "The Browser Workspace candidate was rejected without a "
                        + "settlement.");
            }
        }

        WorkspaceRealizationSettlement settlement =
            await candidate.Settlement.ConfigureAwait(false);
        return settlement.Succeeded
            ? null
            : CleanupFailure(settlement);
    }

    async ValueTask<CompleteRestorationFailure?> RetireAsync(
        BrowserWorkspaceRealizationCandidate candidate,
        bool cancelled)
    {
        BrowserWorkspaceRealizationCandidateRetirementResult retirement =
            cancelled
                ? _realizationHost.CancelCandidate(candidate)
                : _realizationHost.AbandonCandidate(candidate);
        WorkspaceRealizationSettlement settlement = retirement switch
        {
            BrowserWorkspaceRealizationCandidateRetirementResult.Retiring
                retiring =>
                    await retiring.Retirement.Completion.ConfigureAwait(false),
            BrowserWorkspaceRealizationCandidateRetirementResult.Rejected =>
                await candidate.Settlement.ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "Unknown Browser candidate-retirement outcome."),
        };
        return settlement.Succeeded
            ? null
            : CleanupFailure(settlement);
    }

    static bool IsCurrent(ICompleteRestorationIntentAuthority authority) =>
        authority.Status == CompleteRestorationIntentStatus.Current
        && !authority.Revocation.IsCancellationRequested;

    static CompleteRestorationHostResult<BrowserPreparedWorkspaceActivation>
        Failed(string message) =>
        new CompleteRestorationHostResult<BrowserPreparedWorkspaceActivation>
            .Failed(
                new CompleteRestorationFailure.HostConstructionFailed(message));

    static CompleteRestorationFailure CleanupFailure(
        WorkspaceRealizationSettlement settlement) =>
        new CompleteRestorationFailure.CleanupFailed(
            $"Browser Workspace candidate settlement failed during "
                + $"{settlement.Reason}.");
}
