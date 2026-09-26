using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Queries;

namespace DotnetInspect.Web;

internal sealed class BrowserWorkspaceRealizationAttemptIdentity
{
    internal BrowserWorkspaceRealizationAttemptIdentity() { }
}

internal sealed record BrowserWorkspaceRealizationCapacitySnapshot(
    int Limit,
    int Charged,
    ImmutableArray<WorkspaceRealizationSettlement> FailedSettlements);

internal sealed class BrowserWorkspaceRealizationCandidate
{
    readonly TaskCompletionSource<WorkspaceRealizationSettlement> _settlement =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal BrowserWorkspaceRealizationCandidate(
        BrowserWorkspaceRealizationAttemptIdentity attempt,
        WorkspaceRealizationCandidate candidate)
    {
        Attempt = attempt;
        Candidate = candidate;
    }

    internal BrowserWorkspaceRealizationAttemptIdentity Attempt { get; }

    internal WorkspaceRealizationCandidate Candidate { get; }

    internal InspectionWorkspaceIdentity Realization =>
        Candidate.Realization;

    internal WorkspaceRealizationConstructionLease EnterConstruction() =>
        Candidate.EnterConstruction();

    internal Task<WorkspaceRealizationSettlement> Settlement =>
        _settlement.Task;

    internal void CompleteSettlement(
        WorkspaceRealizationSettlement settlement) =>
        _settlement.TrySetResult(settlement);
}

internal sealed class BrowserWorkspaceRealizationRetirement
{
    internal BrowserWorkspaceRealizationRetirement(
        WorkspaceRealizationRetirementReason reason,
        BrowserWorkspaceRealizationCandidate candidate)
    {
        Reason = reason;
        Candidate = candidate;
    }

    internal WorkspaceRealizationRetirementReason Reason { get; }

    internal InspectionWorkspaceIdentity Realization =>
        Candidate.Realization;

    internal BrowserWorkspaceRealizationCandidate Candidate { get; }

    internal Task<WorkspaceRealizationSettlement> Completion =>
        Candidate.Settlement;
}

internal abstract record BrowserWorkspaceRealizationCandidateStartResult
{
    private protected BrowserWorkspaceRealizationCandidateStartResult() { }

    internal sealed record Prepared(
        BrowserWorkspaceRealizationCandidate Candidate)
        : BrowserWorkspaceRealizationCandidateStartResult;

    internal sealed record Superseded
        : BrowserWorkspaceRealizationCandidateStartResult;

    internal sealed record CapacityUnavailable(
        BrowserWorkspaceRealizationCapacitySnapshot Capacity)
        : BrowserWorkspaceRealizationCandidateStartResult;

    internal sealed record Closed
        : BrowserWorkspaceRealizationCandidateStartResult;
}

internal abstract record BrowserWorkspaceRealizationCutoverResult
{
    private protected BrowserWorkspaceRealizationCutoverResult() { }

    internal sealed record Activated(
        WorkspaceRealization Realization,
        BrowserWorkspaceRealizationRetirement? Predecessor)
        : BrowserWorkspaceRealizationCutoverResult;

    internal sealed record Rejected(
        WorkspaceRealizationCandidateRejection Reason)
        : BrowserWorkspaceRealizationCutoverResult;
}

internal abstract record BrowserWorkspaceRealizationCandidateRetirementResult
{
    private protected BrowserWorkspaceRealizationCandidateRetirementResult() { }

    internal sealed record Retiring(
        BrowserWorkspaceRealizationRetirement Retirement)
        : BrowserWorkspaceRealizationCandidateRetirementResult;

    internal sealed record Rejected(
        WorkspaceRealizationCandidateRejection Reason)
        : BrowserWorkspaceRealizationCandidateRetirementResult;
}

internal sealed record BrowserWorkspaceRealizationHostCloseReport(
    WorkspaceReplacementCoordinatorCloseReport Coordinator,
    BrowserWorkspaceRealizationCapacitySnapshot Capacity);

/// <summary>
/// Browser-owned aggregate admission around one Workspace realization
/// coordinator.
/// </summary>
[SupportedOSPlatform("browser")]
internal sealed class BrowserWorkspaceRealizationHost : IAsyncDisposable
{
    internal const int MaxChargedRealizations = 4;

    readonly object _gate = new();
    readonly SemaphoreSlim _candidateStartGate = new(1, 1);
    readonly WorkspaceReplacementCoordinator _coordinator = new();
    readonly List<Charge> _charges = [];
    TaskCompletionSource _capacityChanged = NewSignal();
    TaskCompletionSource? _beginOperationsDrained;
    TaskCompletionSource<BrowserWorkspaceRealizationHostCloseReport>?
        _closeCompletion;
    BrowserWorkspaceRealizationAttemptIdentity? _latestAttempt;
    CancellationTokenSource? _latestBeginSupersession;
    BrowserWorkspaceRealizationCandidate? _candidate;
    Charge? _active;
    int _beginOperations;
    bool _reserved;
    bool _closing;

    internal WorkspaceRealization? Current => _coordinator.Current;

    internal BrowserWorkspaceRealizationCapacitySnapshot Capacity
    {
        get
        {
            lock (_gate)
                return CapacityLocked();
        }
    }

    internal async ValueTask<
        BrowserWorkspaceRealizationCandidateStartResult> BeginCandidateAsync(
            WorkspacePlan plan,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        BrowserWorkspaceRealizationAttemptIdentity attempt;
        var supersession = new CancellationTokenSource();
        BrowserWorkspaceRealizationCandidate? displaced;
        lock (_gate)
        {
            if (_closing)
            {
                supersession.Dispose();
                return new BrowserWorkspaceRealizationCandidateStartResult
                    .Closed();
            }

            _beginOperations++;
            attempt = new();
            _latestBeginSupersession?.Cancel();
            _latestAttempt = attempt;
            _latestBeginSupersession = supersession;
            displaced = _candidate;
            SignalCapacityChangedLocked();
        }

        using var operationCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                supersession.Token);
        CancellationToken operationToken = operationCancellation.Token;
        bool startGateEntered = false;
        bool reserved = false;
        try
        {
            if (displaced is not null)
            {
                BrowserWorkspaceRealizationCandidateRetirementResult
                    retirement = RetireCandidate(
                        displaced,
                        _coordinator.SupersedeCandidate(
                            displaced.Candidate));
                if (retirement
                    is BrowserWorkspaceRealizationCandidateRetirementResult
                        .Retiring retiring)
                {
                    await retiring.Retirement.Completion
                        .WaitAsync(operationToken)
                        .ConfigureAwait(false);
                }
                else if (retirement
                    is BrowserWorkspaceRealizationCandidateRetirementResult
                        .Rejected
                    {
                        Reason:
                            WorkspaceRealizationCandidateRejection
                                .CoordinatorClosed,
                    })
                {
                    return new BrowserWorkspaceRealizationCandidateStartResult
                        .Closed();
                }
            }

            await _candidateStartGate
                .WaitAsync(operationToken)
                .ConfigureAwait(false);
            startGateEntered = true;

            lock (_gate)
            {
                if (_closing)
                {
                    return new BrowserWorkspaceRealizationCandidateStartResult
                        .Closed();
                }
                if (!ReferenceEquals(_latestAttempt, attempt))
                {
                    return new BrowserWorkspaceRealizationCandidateStartResult
                        .Superseded();
                }

            }

            ReservationResult reservation =
                await ReserveAsync(attempt, operationToken)
                    .ConfigureAwait(false);
            if (reservation.Kind == ReservationKind.Superseded)
            {
                return new BrowserWorkspaceRealizationCandidateStartResult
                    .Superseded();
            }
            if (reservation.Kind == ReservationKind.Closed)
            {
                return new BrowserWorkspaceRealizationCandidateStartResult
                    .Closed();
            }
            if (reservation.Capacity is { } unavailable)
            {
                return new BrowserWorkspaceRealizationCandidateStartResult
                    .CapacityUnavailable(unavailable);
            }
            reserved = true;

            WorkspaceRealizationCandidateStartResult started =
                await _coordinator.BeginCandidateAsync(
                        plan,
                        operationToken)
                    .ConfigureAwait(false);
            if (started
                is WorkspaceRealizationCandidateStartResult.Prepared prepared)
            {
                if (prepared.SupersededCandidate is not null)
                {
                    _ = _coordinator.CancelCandidate(prepared.Candidate);
                    throw new InvalidOperationException(
                        "The Browser host reached candidate creation with an unpublished coordinator candidate still present.");
                }

                var candidate = new BrowserWorkspaceRealizationCandidate(
                    attempt,
                    prepared.Candidate);
                var charge = new Charge(candidate);
                bool current;
                bool cancelled;
                lock (_gate)
                {
                    ConsumeReservationLocked(charge);
                    reserved = false;
                    current =
                        !_closing
                        && ReferenceEquals(_latestAttempt, attempt);
                    cancelled = operationToken.IsCancellationRequested;
                    if (current && !cancelled)
                        _candidate = candidate;
                }
                _ = ObserveSettlementAsync(
                    charge,
                    prepared.Candidate.Settlement);

                if (!current || cancelled)
                {
                    WorkspaceRealizationCandidateRetirementResult retirement =
                        cancelled
                            ? _coordinator.CancelCandidate(
                                prepared.Candidate)
                            : _coordinator.SupersedeCandidate(
                                prepared.Candidate);
                    _ = RetireCandidate(candidate, retirement);
                    if (cancelled)
                        operationToken.ThrowIfCancellationRequested();
                    return new
                        BrowserWorkspaceRealizationCandidateStartResult
                            .Superseded();
                }

                return new
                    BrowserWorkspaceRealizationCandidateStartResult.Prepared(
                        candidate);
            }

            return started switch
            {
                WorkspaceRealizationCandidateStartResult.Superseded =>
                    new BrowserWorkspaceRealizationCandidateStartResult
                        .Superseded(),
                WorkspaceRealizationCandidateStartResult.Closed =>
                    new BrowserWorkspaceRealizationCandidateStartResult
                        .Closed(),
                _ => throw new InvalidOperationException(
                    "The Workspace realization coordinator returned an unknown candidate-start outcome."),
            };
        }
        catch (OperationCanceledException)
            when (supersession.IsCancellationRequested
                && !cancellationToken.IsCancellationRequested)
        {
            lock (_gate)
            {
                return _closing
                    ? new
                        BrowserWorkspaceRealizationCandidateStartResult
                            .Closed()
                    : new
                        BrowserWorkspaceRealizationCandidateStartResult
                            .Superseded();
            }
        }
        finally
        {
            if (reserved)
            {
                lock (_gate)
                    ReleaseReservationLocked();
            }
            if (startGateEntered)
                _candidateStartGate.Release();
            EndBeginOperation(attempt, supersession);
            supersession.Dispose();
        }
    }

    internal ValueTask<WorkspaceRealizationCandidateCompletionResult>
        CompleteCandidateAsync(
            BrowserWorkspaceRealizationCandidate candidate,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return _coordinator.CompleteCandidateAsync(
            candidate.Candidate,
            cancellationToken);
    }

    internal BrowserWorkspaceRealizationCutoverResult CutOver(
        BrowserWorkspaceRealizationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        WorkspaceRealizationCutoverResult result;
        Charge? predecessor;
        lock (_gate)
        {
            if (_closing)
            {
                return new BrowserWorkspaceRealizationCutoverResult.Rejected(
                    WorkspaceRealizationCandidateRejection.CoordinatorClosed);
            }
            if (!ReferenceEquals(_latestAttempt, candidate.Attempt)
                || !ReferenceEquals(_candidate, candidate))
            {
                return new BrowserWorkspaceRealizationCutoverResult.Rejected(
                    WorkspaceRealizationCandidateRejection.StaleCandidate);
            }

            predecessor = _active;
            result = _coordinator.CutOver(candidate.Candidate);
            if (result is WorkspaceRealizationCutoverResult.Activated)
            {
                _active = ChargeFor(candidate);
                _candidate = null;
                SignalCapacityChangedLocked();
            }
        }

        return result switch
        {
            WorkspaceRealizationCutoverResult.Activated activated =>
                new BrowserWorkspaceRealizationCutoverResult.Activated(
                    activated.Realization,
                    Predecessor(activated.Predecessor, predecessor)),
            WorkspaceRealizationCutoverResult.Rejected rejected =>
                new BrowserWorkspaceRealizationCutoverResult.Rejected(
                    rejected.Reason),
            _ => throw new InvalidOperationException(
                "The Workspace realization coordinator returned an unknown cutover outcome."),
        };
    }

    internal BrowserWorkspaceRealizationCandidateRetirementResult
        AbandonCandidate(BrowserWorkspaceRealizationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return RetireCandidate(
            candidate,
            _coordinator.AbandonCandidate(candidate.Candidate));
    }

    internal BrowserWorkspaceRealizationCandidateRetirementResult
        CancelCandidate(BrowserWorkspaceRealizationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return RetireCandidate(
            candidate,
            _coordinator.CancelCandidate(candidate.Candidate));
    }

    internal ValueTask<WorkspaceRealizationOperationAdmission>
        EnterOperationAsync(
            CancellationToken cancellationToken = default) =>
        _coordinator.EnterOperationAsync(cancellationToken);

    internal async ValueTask<WorkspaceRealizationOperationAdmission>
        EnterOperationAsync(
            InspectionWorkspaceIdentity expectedRealization,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedRealization);
        WorkspaceRealizationOperationAdmission admission =
            await _coordinator.EnterOperationAsync(cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted
            || ReferenceEquals(
                admitted.Lease.Realization,
                expectedRealization))
        {
            return admission;
        }

        admitted.Lease.Dispose();
        return new WorkspaceRealizationOperationAdmission.Unavailable(
            WorkspaceRealizationOperationUnavailableReason.NoActiveRealization);
    }

    internal Task<BrowserWorkspaceRealizationHostCloseReport> CloseAsync()
    {
        TaskCompletionSource<BrowserWorkspaceRealizationHostCloseReport>
            completion;
        Task beginOperations;
        lock (_gate)
        {
            if (_closeCompletion is not null)
                return _closeCompletion.Task;

            completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _closeCompletion = completion;
            _closing = true;
            _latestAttempt = null;
            _latestBeginSupersession?.Cancel();
            _latestBeginSupersession = null;
            _candidate = null;
            beginOperations = _beginOperations == 0
                ? Task.CompletedTask
                : (_beginOperationsDrained = NewSignal()).Task;
            SignalCapacityChangedLocked();
        }

        _ = CompleteCloseAsync(
            _coordinator.CloseAsync(),
            beginOperations,
            completion);
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        BrowserWorkspaceRealizationHostCloseReport report =
            await CloseAsync().ConfigureAwait(false);
        Exception[] failures =
        [
            .. report.Capacity.FailedSettlements.Select(
                static settlement =>
                    settlement.Failure
                    ?? new WorkspaceRealizationSettlementException(
                        settlement)),
        ];
        if (failures.Length > 0)
            throw new AggregateException(failures);
    }

    async ValueTask<ReservationResult> ReserveAsync(
        BrowserWorkspaceRealizationAttemptIdentity attempt,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_closing)
                    return new(ReservationKind.Closed);
                if (!ReferenceEquals(_latestAttempt, attempt))
                    return new(ReservationKind.Superseded);
                if (_charges.Count < MaxChargedRealizations)
                {
                    if (_reserved)
                    {
                        throw new InvalidOperationException(
                            "More than one Browser realization reservation was admitted.");
                    }
                    _reserved = true;
                    return new(ReservationKind.Reserved);
                }

                bool mayRelease = _charges.Any(
                    charge =>
                        !ReferenceEquals(charge, _active)
                        && charge.Settlement is null);
                if (!mayRelease)
                {
                    return new(
                        ReservationKind.CapacityUnavailable,
                        CapacityLocked());
                }
                changed = _capacityChanged.Task;
            }

            await changed
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
    }

    BrowserWorkspaceRealizationCandidateRetirementResult RetireCandidate(
        BrowserWorkspaceRealizationCandidate candidate,
        WorkspaceRealizationCandidateRetirementResult result)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_candidate, candidate))
                _candidate = null;
            SignalCapacityChangedLocked();
        }

        return result switch
        {
            WorkspaceRealizationCandidateRetirementResult.Retiring retiring =>
                new BrowserWorkspaceRealizationCandidateRetirementResult
                    .Retiring(
                        Retirement(retiring.Retirement, candidate)),
            WorkspaceRealizationCandidateRetirementResult.Rejected rejected =>
                new BrowserWorkspaceRealizationCandidateRetirementResult
                    .Rejected(rejected.Reason),
            _ => throw new InvalidOperationException(
                "The Workspace realization coordinator returned an unknown candidate-retirement outcome."),
        };
    }

    static BrowserWorkspaceRealizationRetirement Retirement(
        WorkspaceRealizationRetirement retirement,
        BrowserWorkspaceRealizationCandidate candidate)
    {
        if (!ReferenceEquals(
                retirement.Realization,
                candidate.Realization))
        {
            throw new InvalidOperationException(
                "The coordinator retired a different realization than the Browser candidate.");
        }
        return new(retirement.Reason, candidate);
    }

    static BrowserWorkspaceRealizationRetirement? Predecessor(
        WorkspaceRealizationRetirement? retirement,
        Charge? predecessor)
    {
        if (retirement is null)
        {
            if (predecessor is not null)
            {
                throw new InvalidOperationException(
                    "The coordinator omitted the Browser host's active predecessor.");
            }
            return null;
        }
        if (predecessor is null)
        {
            throw new InvalidOperationException(
                "The coordinator returned an untracked predecessor.");
        }
        return Retirement(retirement, predecessor.Candidate);
    }

    async Task ObserveSettlementAsync(
        Charge charge,
        Task<WorkspaceRealizationSettlement> completion)
    {
        WorkspaceRealizationSettlement settlement =
            await completion.ConfigureAwait(false);
        lock (_gate)
        {
            if (charge.Settlement is not null)
                return;

            charge.Settlement = settlement;
            if (ReferenceEquals(_candidate, charge.Candidate))
                _candidate = null;
            if (ReferenceEquals(_active, charge))
                _active = null;
            if (settlement.Succeeded)
                _charges.Remove(charge);
            SignalCapacityChangedLocked();
        }
        charge.Candidate.CompleteSettlement(settlement);
    }

    Charge ChargeFor(BrowserWorkspaceRealizationCandidate candidate) =>
        _charges.FirstOrDefault(
            charge => ReferenceEquals(
                charge.Candidate,
                candidate))
        ?? throw new InvalidOperationException(
            "The Workspace realization candidate is not charged to this Browser host.");

    void ConsumeReservationLocked(Charge charge)
    {
        if (!_reserved)
        {
            throw new InvalidOperationException(
                "The Browser realization candidate has no reserved charge.");
        }
        _reserved = false;
        _charges.Add(charge);
        SignalCapacityChangedLocked();
    }

    void ReleaseReservationLocked()
    {
        if (!_reserved)
        {
            throw new InvalidOperationException(
                "The Browser realization reservation was released more than once.");
        }
        _reserved = false;
        SignalCapacityChangedLocked();
    }

    void EndBeginOperation(
        BrowserWorkspaceRealizationAttemptIdentity attempt,
        CancellationTokenSource supersession)
    {
        TaskCompletionSource? drained = null;
        lock (_gate)
        {
            if (_beginOperations <= 0)
            {
                throw new InvalidOperationException(
                    "The Browser realization begin operation settled more than once.");
            }
            _beginOperations--;
            if (ReferenceEquals(_latestAttempt, attempt)
                && _candidate is null)
            {
                _latestAttempt = null;
            }
            if (ReferenceEquals(
                    _latestBeginSupersession,
                    supersession))
            {
                _latestBeginSupersession = null;
            }
            if (_closing && _beginOperations == 0)
            {
                drained = _beginOperationsDrained;
                _beginOperationsDrained = null;
            }
            SignalCapacityChangedLocked();
        }
        drained?.TrySetResult();
    }

    async Task CompleteCloseAsync(
        Task<WorkspaceReplacementCoordinatorCloseReport> coordinatorClose,
        Task beginOperations,
        TaskCompletionSource<BrowserWorkspaceRealizationHostCloseReport>
            completion)
    {
        WorkspaceReplacementCoordinatorCloseReport coordinator =
            await coordinatorClose.ConfigureAwait(false);
        await beginOperations.ConfigureAwait(false);
        foreach (WorkspaceRealizationSettlement settlement
            in coordinator.Settlements)
        {
            Charge? charge;
            lock (_gate)
            {
                charge = _charges.FirstOrDefault(
                    candidate => ReferenceEquals(
                        candidate.Candidate.Realization,
                        settlement.Realization));
            }
            if (charge is not null)
                await charge.Candidate.Settlement.ConfigureAwait(false);
        }
        completion.TrySetResult(new(
            coordinator,
            Capacity));
    }

    BrowserWorkspaceRealizationCapacitySnapshot CapacityLocked() =>
        new(
            MaxChargedRealizations,
            _charges.Count,
            [
                .. _charges
                    .Where(charge => charge.Settlement is { Succeeded: false })
                    .Select(charge => charge.Settlement!),
            ]);

    void SignalCapacityChangedLocked()
    {
        TaskCompletionSource changed = _capacityChanged;
        _capacityChanged = NewSignal();
        changed.TrySetResult();
    }

    static TaskCompletionSource NewSignal() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    sealed class Charge(
        BrowserWorkspaceRealizationCandidate candidate)
    {
        internal BrowserWorkspaceRealizationCandidate Candidate { get; } =
            candidate;

        internal WorkspaceRealizationSettlement? Settlement { get; set; }
    }

    readonly record struct ReservationResult(
        ReservationKind Kind,
        BrowserWorkspaceRealizationCapacitySnapshot? Capacity = null);

    enum ReservationKind
    {
        Reserved,
        Superseded,
        Closed,
        CapacityUnavailable,
    }
}
