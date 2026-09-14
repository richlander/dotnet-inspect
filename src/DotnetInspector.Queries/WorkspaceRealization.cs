using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>
/// Exact resource-free definition state observed for one Workspace.
/// </summary>
public sealed class WorkspaceDefinitionSnapshot
{
    internal WorkspaceDefinitionSnapshot(
        InspectionWorkspaceIdentity workspace,
        WorkspaceRegistrationRevision registrations,
        WorkspaceScopeRevision scope)
    {
        Workspace = workspace;
        Identity = new();
        Registrations = registrations;
        Scope = scope;
    }

    public InspectionWorkspaceIdentity Workspace { get; }

    public WorkspaceDefinitionSnapshotIdentity Identity { get; }

    public WorkspaceRegistrationRevision Registrations { get; }

    public WorkspaceScopeRevision Scope { get; }

    public WorkspacePlan Plan => Registrations.Plan;
}

public sealed class WorkspaceDefinitionSnapshotIdentity
{
    internal WorkspaceDefinitionSnapshotIdentity() { }
}

public sealed class WorkspaceRealizationReplacementAttemptIdentity
{
    internal WorkspaceRealizationReplacementAttemptIdentity() { }
}

public sealed class WorkspaceRealizationCandidateIdentity
{
    internal WorkspaceRealizationCandidateIdentity() { }
}

public enum WorkspaceRealizationRetirementReason
{
    Replaced,
    CandidateAbandoned,
    CandidateCancelled,
    CandidateFailed,
    CandidateSuperseded,
    CoordinatorClosed,
}

public sealed record WorkspaceRealizationSettlement(
    InspectionWorkspaceIdentity Realization,
    WorkspaceRealizationRetirementReason Reason,
    InspectionWorkspaceCloseReport? Report,
    Exception? Failure);

/// <summary>
/// Observable retirement of one realization. It grants no operation access.
/// </summary>
public sealed class WorkspaceRealizationRetirement
{
    internal WorkspaceRealizationRetirement(
        InspectionWorkspaceIdentity realization,
        WorkspaceRealizationRetirementReason reason,
        Task<WorkspaceRealizationSettlement> completion)
    {
        Realization = realization;
        Reason = reason;
        Completion = completion;
    }

    public InspectionWorkspaceIdentity Realization { get; }

    public WorkspaceRealizationRetirementReason Reason { get; }

    public Task<WorkspaceRealizationSettlement> Completion { get; }
}

/// <summary>
/// One unpublished Workspace available only for construction.
/// </summary>
public sealed class WorkspaceRealizationCandidate
{
    readonly WorkspaceRealizationCoordinator _owner;
    internal readonly WorkspaceRealizationCoordinator.RealizationState State;

    internal WorkspaceRealizationCandidate(
        WorkspaceRealizationCoordinator owner,
        WorkspaceRealizationCoordinator.RealizationState state,
        WorkspaceRealizationReplacementAttemptIdentity attempt)
    {
        _owner = owner;
        State = state;
        Attempt = attempt;
        Identity = new();
    }

    internal WorkspaceRealizationCoordinator Owner => _owner;

    public WorkspaceRealizationCandidateIdentity Identity { get; }

    public WorkspaceRealizationReplacementAttemptIdentity Attempt { get; }

    public InspectionWorkspaceIdentity Realization => State.Identity;

    public WorkspacePlan OriginPlan => State.OriginPlan;

    /// <summary>
    /// Enters one construction operation while candidate admission remains
    /// open.
    /// </summary>
    public WorkspaceRealizationConstructionLease EnterConstruction() =>
        _owner.EnterConstruction(this);

    /// <summary>
    /// Completes if this candidate or its later active realization retires.
    /// </summary>
    public Task<WorkspaceRealizationSettlement> Settlement =>
        State.Settlement.Task;
}

/// <summary>
/// Live authority for one candidate-construction operation.
/// Hold the lease for the complete operation and do not retain its Workspace
/// after release.
/// </summary>
public sealed class WorkspaceRealizationConstructionLease : IDisposable
{
    WorkspaceRealizationCoordinator? _owner;
    readonly WorkspaceRealizationCoordinator.RealizationState _state;

    internal WorkspaceRealizationConstructionLease(
        WorkspaceRealizationCoordinator owner,
        WorkspaceRealizationCoordinator.RealizationState state)
    {
        _owner = owner;
        _state = state;
    }

    public InspectionWorkspace Workspace
    {
        get
        {
            WorkspaceRealizationCoordinator owner =
                Volatile.Read(ref _owner)
                ?? throw new ObjectDisposedException(
                    nameof(WorkspaceRealizationConstructionLease));
            return owner.GetConstructionWorkspace(_state);
        }
    }

    public void Dispose()
    {
        WorkspaceRealizationCoordinator? owner =
            Interlocked.Exchange(ref _owner, null);
        owner?.ReleaseConstruction(_state);
    }
}

/// <summary>
/// Published realization identity and its exact construction origin.
/// </summary>
public sealed class WorkspaceRealization
{
    internal WorkspaceRealization(
        InspectionWorkspaceIdentity identity,
        WorkspacePlan originPlan,
        WorkspaceDefinitionSnapshot initialDefinition)
    {
        Identity = identity;
        OriginPlan = originPlan;
        InitialDefinition = initialDefinition;
    }

    public InspectionWorkspaceIdentity Identity { get; }

    public WorkspacePlan OriginPlan { get; }

    public WorkspaceDefinitionSnapshot InitialDefinition { get; }
}

public abstract record WorkspaceRealizationCandidateStartResult
{
    private protected WorkspaceRealizationCandidateStartResult() { }

    public sealed record Prepared(
        WorkspaceRealizationCandidate Candidate,
        WorkspaceRealizationRetirement? SupersededCandidate)
        : WorkspaceRealizationCandidateStartResult;

    public sealed record Superseded(
        WorkspaceRealizationReplacementAttemptIdentity Attempt)
        : WorkspaceRealizationCandidateStartResult;

    public sealed record Closed : WorkspaceRealizationCandidateStartResult;
}

public enum WorkspaceRealizationCandidateRejection
{
    StaleCandidate,
    CompletionInProgress,
    AlreadyReady,
    NotReady,
    CoordinatorClosed,
    RuntimeUnavailable,
}

public abstract record WorkspaceRealizationCandidateCompletionResult
{
    private protected WorkspaceRealizationCandidateCompletionResult() { }

    public sealed record Ready(
        WorkspaceRealizationCandidate Candidate,
        WorkspaceDefinitionSnapshot Definition)
        : WorkspaceRealizationCandidateCompletionResult;

    public sealed record Rejected(
        WorkspaceRealizationCandidate Candidate,
        WorkspaceRealizationCandidateRejection Reason,
        ArtifactRootFailure? RuntimeFailure = null)
        : WorkspaceRealizationCandidateCompletionResult;
}

public abstract record WorkspaceRealizationCandidateRetirementResult
{
    private protected WorkspaceRealizationCandidateRetirementResult() { }

    public sealed record Retiring(
        WorkspaceRealizationRetirement Retirement)
        : WorkspaceRealizationCandidateRetirementResult;

    public sealed record Rejected(
        WorkspaceRealizationCandidateRejection Reason)
        : WorkspaceRealizationCandidateRetirementResult;
}

public abstract record WorkspaceRealizationCutoverResult
{
    private protected WorkspaceRealizationCutoverResult() { }

    public sealed record Activated(
        WorkspaceRealization Realization,
        WorkspaceRealizationRetirement? Predecessor)
        : WorkspaceRealizationCutoverResult;

    public sealed record Rejected(
        WorkspaceRealizationCandidateRejection Reason)
        : WorkspaceRealizationCutoverResult;
}

public enum WorkspaceRealizationOperationUnavailableReason
{
    NoActiveRealization,
    CoordinatorClosed,
    RuntimeUnavailable,
}

public abstract record WorkspaceRealizationOperationAdmission
{
    private protected WorkspaceRealizationOperationAdmission() { }

    public sealed record Admitted(
        WorkspaceRealizationOperationLease Lease)
        : WorkspaceRealizationOperationAdmission;

    public sealed record Unavailable(
        WorkspaceRealizationOperationUnavailableReason Reason,
        ArtifactRootFailure? RuntimeFailure = null)
        : WorkspaceRealizationOperationAdmission;
}

/// <summary>
/// Live authority for one operation admitted to one exact realization and
/// definition snapshot.
/// </summary>
public sealed class WorkspaceRealizationOperationLease : IDisposable
{
    WorkspaceRealizationCoordinator? _owner;
    readonly WorkspaceRealizationCoordinator.RealizationState _state;

    internal WorkspaceRealizationOperationLease(
        WorkspaceRealizationCoordinator owner,
        WorkspaceRealizationCoordinator.RealizationState state,
        WorkspaceDefinitionSnapshot definition,
        WorkspaceScopeSnapshot scope)
    {
        _owner = owner;
        _state = state;
        Definition = definition;
        Scope = scope;
    }

    public InspectionWorkspaceIdentity Realization => _state.Identity;

    public WorkspaceDefinitionSnapshot Definition { get; }

    public WorkspaceScopeSnapshot Scope { get; }

    public InspectionWorkspace Workspace
    {
        get
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _owner) is null,
                this);
            return _state.Workspace;
        }
    }

    public void Dispose()
    {
        WorkspaceRealizationCoordinator? owner =
            Interlocked.Exchange(ref _owner, null);
        owner?.ReleaseOperation(_state);
    }
}

public sealed record WorkspaceRealizationCoordinatorCloseReport(
    ImmutableArray<WorkspaceRealizationSettlement> Settlements);

/// <summary>
/// Owns selection, operation admission, cutover, and drainage for one active
/// Workspace realization.
/// </summary>
public sealed class WorkspaceRealizationCoordinator : IAsyncDisposable
{
    readonly object _gate = new();
    readonly List<RetirementRecord> _retired = [];
    Task _candidateBarrier = Task.CompletedTask;
    WorkspaceRealizationReplacementAttemptIdentity? _currentAttempt;
    WorkspaceRealizationCandidate? _candidate;
    RealizationState? _active;
    TaskCompletionSource<WorkspaceRealizationCoordinatorCloseReport>?
        _closeCompletion;
    long _nextSequence;

    public WorkspaceRealization? Current
    {
        get
        {
            lock (_gate)
                return _active?.Published;
        }
    }

    public async ValueTask<WorkspaceRealizationCandidateStartResult>
        BeginCandidateAsync(WorkspacePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        WorkspaceRealizationReplacementAttemptIdentity attempt = new();
        WorkspaceRealizationRetirement? displaced = null;
        RealizationState? startClose = null;
        Task barrier;
        lock (_gate)
        {
            if (_closeCompletion is not null)
                return new WorkspaceRealizationCandidateStartResult.Closed();

            _currentAttempt = attempt;
            if (_candidate is { } current)
            {
                _candidate = null;
                (displaced, startClose) = RetireLocked(
                    current.State,
                    WorkspaceRealizationRetirementReason.CandidateSuperseded);
                _candidateBarrier = displaced.Completion;
            }
            barrier = _candidateBarrier;
        }

        if (startClose is not null)
            StartClose(startClose);
        await barrier.ConfigureAwait(false);

        lock (_gate)
        {
            if (_closeCompletion is not null)
                return new WorkspaceRealizationCandidateStartResult.Closed();
            if (!ReferenceEquals(_currentAttempt, attempt))
            {
                return new WorkspaceRealizationCandidateStartResult
                    .Superseded(attempt);
            }

            var state = new RealizationState(
                new InspectionWorkspace(plan),
                plan,
                ++_nextSequence);
            var candidate = new WorkspaceRealizationCandidate(
                this,
                state,
                attempt);
            _candidate = candidate;
            _candidateBarrier = Task.CompletedTask;
            return new WorkspaceRealizationCandidateStartResult.Prepared(
                candidate,
                displaced);
        }
    }

    public async ValueTask<WorkspaceRealizationCandidateCompletionResult>
        CompleteCandidateAsync(
            WorkspaceRealizationCandidate candidate,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        RealizationState state;
        InspectionWorkspace workspace;
        Task constructionDrain;
        lock (_gate)
        {
            if (!ReferenceEquals(candidate.Owner, this)
                || !ReferenceEquals(candidate.State, _candidate?.State))
            {
                return new WorkspaceRealizationCandidateCompletionResult
                    .Rejected(
                        candidate,
                        WorkspaceRealizationCandidateRejection.StaleCandidate);
            }
            if (_closeCompletion is not null)
            {
                return new WorkspaceRealizationCandidateCompletionResult
                    .Rejected(
                        candidate,
                        WorkspaceRealizationCandidateRejection.CoordinatorClosed);
            }
            if (candidate.State.Phase == RealizationPhase.Ready)
            {
                return new WorkspaceRealizationCandidateCompletionResult
                    .Rejected(
                        candidate,
                        WorkspaceRealizationCandidateRejection.AlreadyReady);
            }
            if (candidate.State.Phase == RealizationPhase.Completing)
            {
                return new WorkspaceRealizationCandidateCompletionResult
                    .Rejected(
                        candidate,
                        WorkspaceRealizationCandidateRejection
                            .CompletionInProgress);
            }
            state = candidate.State;
            workspace = state.Workspace;
            state.Phase = RealizationPhase.Completing;
            constructionDrain = state.ConstructionCount == 0
                ? Task.CompletedTask
                : state.BeginConstructionDrain();
        }

        ArtifactRootResult<WorkspaceRealizationOperationSnapshot> captured;
        try
        {
            await constructionDrain.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            lock (_gate)
            {
                if (_closeCompletion is not null)
                {
                    return new WorkspaceRealizationCandidateCompletionResult
                        .Rejected(
                            candidate,
                            WorkspaceRealizationCandidateRejection
                                .CoordinatorClosed);
                }
                if (!ReferenceEquals(state, _candidate?.State)
                    || state.Phase != RealizationPhase.Completing)
                {
                    return new WorkspaceRealizationCandidateCompletionResult
                        .Rejected(
                            candidate,
                            WorkspaceRealizationCandidateRejection
                                .StaleCandidate);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            captured =
                await workspace.CaptureRealizationOperationSnapshotAsync(
                    cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            RetireCandidate(
                candidate,
                WorkspaceRealizationRetirementReason.CandidateCancelled);
            throw;
        }
        catch
        {
            RetireCandidate(
                candidate,
                WorkspaceRealizationRetirementReason.CandidateFailed);
            throw;
        }
        if (captured
            is ArtifactRootResult<WorkspaceRealizationOperationSnapshot>
                .Rejected unavailable)
        {
            RealizationState? startClose = null;
            WorkspaceRealizationCandidateRejection? changed = null;
            lock (_gate)
            {
                if (_closeCompletion is not null)
                {
                    changed =
                        WorkspaceRealizationCandidateRejection
                            .CoordinatorClosed;
                }
                else if (!ReferenceEquals(state, _candidate?.State)
                    || state.Phase != RealizationPhase.Completing)
                {
                    changed =
                        WorkspaceRealizationCandidateRejection.StaleCandidate;
                }
                else
                {
                    _candidate = null;
                    _currentAttempt = null;
                    WorkspaceRealizationRetirement retirement;
                    (retirement, startClose) = RetireLocked(
                        state,
                        WorkspaceRealizationRetirementReason.CandidateFailed);
                    _candidateBarrier = retirement.Completion;
                }
            }
            if (startClose is not null)
                StartClose(startClose);
            if (changed is { } rejection)
            {
                return new WorkspaceRealizationCandidateCompletionResult
                    .Rejected(candidate, rejection);
            }
            return new WorkspaceRealizationCandidateCompletionResult.Rejected(
                candidate,
                WorkspaceRealizationCandidateRejection.RuntimeUnavailable,
                unavailable.Failure);
        }

        WorkspaceRealizationOperationSnapshot snapshot =
            ((ArtifactRootResult<WorkspaceRealizationOperationSnapshot>
                .Available)captured).Value;
        lock (_gate)
        {
            if (!ReferenceEquals(candidate.Owner, this))
            {
                return new WorkspaceRealizationCandidateCompletionResult
                    .Rejected(
                        candidate,
                    WorkspaceRealizationCandidateRejection.StaleCandidate);
            }
            if (_closeCompletion is not null)
            {
                return new WorkspaceRealizationCandidateCompletionResult
                    .Rejected(
                        candidate,
                        WorkspaceRealizationCandidateRejection.CoordinatorClosed);
            }
            if (!ReferenceEquals(state, _candidate?.State)
                || state.Phase != RealizationPhase.Completing)
            {
                return new WorkspaceRealizationCandidateCompletionResult
                    .Rejected(
                        candidate,
                        WorkspaceRealizationCandidateRejection.StaleCandidate);
            }
            if (snapshot.Scope.Preparing is not null)
            {
                state.Phase = RealizationPhase.Preparing;
                return new WorkspaceRealizationCandidateCompletionResult
                    .Rejected(
                        candidate,
                        WorkspaceRealizationCandidateRejection.NotReady);
            }

            state.InitialDefinition = snapshot.Definition;
            state.Phase = RealizationPhase.Ready;
            return new WorkspaceRealizationCandidateCompletionResult.Ready(
                candidate,
                snapshot.Definition);
        }
    }

    public WorkspaceRealizationCutoverResult CutOver(
        WorkspaceRealizationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        WorkspaceRealizationRetirement? predecessor = null;
        RealizationState? startClose = null;
        WorkspaceRealization realization;
        lock (_gate)
        {
            if (!ReferenceEquals(candidate.Owner, this))
            {
                return new WorkspaceRealizationCutoverResult.Rejected(
                        WorkspaceRealizationCandidateRejection.StaleCandidate);
            }
            if (_closeCompletion is not null)
            {
                return new WorkspaceRealizationCutoverResult.Rejected(
                    WorkspaceRealizationCandidateRejection.CoordinatorClosed);
            }
            if (!ReferenceEquals(candidate.State, _candidate?.State))
            {
                return new WorkspaceRealizationCutoverResult.Rejected(
                    WorkspaceRealizationCandidateRejection.StaleCandidate);
            }
            if (candidate.State.Phase != RealizationPhase.Ready)
            {
                return new WorkspaceRealizationCutoverResult.Rejected(
                    WorkspaceRealizationCandidateRejection.NotReady);
            }

            RealizationState successor = candidate.State;
            successor.Phase = RealizationPhase.Active;
            successor.AdmissionOpen = true;
            successor.Published = new WorkspaceRealization(
                successor.Workspace.Identity,
                successor.OriginPlan,
                successor.InitialDefinition!);
            realization = successor.Published;

            if (_active is { } current)
            {
                (predecessor, startClose) = RetireLocked(
                    current,
                    WorkspaceRealizationRetirementReason.Replaced);
            }
            _active = successor;
            _candidate = null;
            _currentAttempt = null;
            _candidateBarrier = Task.CompletedTask;
        }

        if (startClose is not null)
            StartClose(startClose);
        return new WorkspaceRealizationCutoverResult.Activated(
            realization,
            predecessor);
    }

    public WorkspaceRealizationCandidateRetirementResult AbandonCandidate(
        WorkspaceRealizationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        WorkspaceRealizationRetirement retirement;
        RealizationState? startClose;
        lock (_gate)
        {
            if (!ReferenceEquals(candidate.Owner, this))
            {
                return new WorkspaceRealizationCandidateRetirementResult
                    .Rejected(
                        WorkspaceRealizationCandidateRejection.StaleCandidate);
            }
            if (_closeCompletion is not null)
            {
                return new WorkspaceRealizationCandidateRetirementResult
                    .Rejected(
                        WorkspaceRealizationCandidateRejection.CoordinatorClosed);
            }
            if (!ReferenceEquals(candidate.State, _candidate?.State))
            {
                return new WorkspaceRealizationCandidateRetirementResult
                    .Rejected(
                        WorkspaceRealizationCandidateRejection.StaleCandidate);
            }

            _candidate = null;
            _currentAttempt = null;
            (retirement, startClose) = RetireLocked(
                candidate.State,
                WorkspaceRealizationRetirementReason.CandidateAbandoned);
            _candidateBarrier = retirement.Completion;
        }

        if (startClose is not null)
            StartClose(startClose);
        return new WorkspaceRealizationCandidateRetirementResult.Retiring(
            retirement);
    }

    public async ValueTask<WorkspaceRealizationOperationAdmission>
        EnterOperationAsync(CancellationToken cancellationToken = default)
    {
        RealizationState state;
        lock (_gate)
        {
            if (_closeCompletion is not null)
            {
                return new WorkspaceRealizationOperationAdmission.Unavailable(
                    WorkspaceRealizationOperationUnavailableReason
                        .CoordinatorClosed);
            }
            if (_active is not { AdmissionOpen: true } current)
            {
                return new WorkspaceRealizationOperationAdmission.Unavailable(
                    WorkspaceRealizationOperationUnavailableReason
                        .NoActiveRealization);
            }

            state = current;
            state.OperationCount++;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArtifactRootResult<WorkspaceRealizationOperationSnapshot> captured =
                await state.Workspace.CaptureRealizationOperationSnapshotAsync(
                    cancellationToken).ConfigureAwait(false);
            if (captured
                is ArtifactRootResult<WorkspaceRealizationOperationSnapshot>
                    .Rejected unavailable)
            {
                ReleaseOperation(state);
                return new WorkspaceRealizationOperationAdmission.Unavailable(
                    WorkspaceRealizationOperationUnavailableReason
                        .RuntimeUnavailable,
                    unavailable.Failure);
            }

            WorkspaceRealizationOperationSnapshot snapshot =
                ((ArtifactRootResult<WorkspaceRealizationOperationSnapshot>
                    .Available)captured).Value;
            return new WorkspaceRealizationOperationAdmission.Admitted(
                new WorkspaceRealizationOperationLease(
                    this,
                    state,
                    snapshot.Definition,
                    snapshot.Scope));
        }
        catch
        {
            ReleaseOperation(state);
            throw;
        }
    }

    public Task<WorkspaceRealizationCoordinatorCloseReport> CloseAsync()
    {
        TaskCompletionSource<WorkspaceRealizationCoordinatorCloseReport>
            completion;
        List<RealizationState> startClose = [];
        RetirementRecord[] retired;
        lock (_gate)
        {
            if (_closeCompletion is not null)
                return _closeCompletion.Task;

            completion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _closeCompletion = completion;
            _currentAttempt = new();

            if (_candidate is { } candidate)
            {
                _candidate = null;
                WorkspaceRealizationRetirement retirement;
                RealizationState? close;
                (retirement, close) = RetireLocked(
                    candidate.State,
                    WorkspaceRealizationRetirementReason.CoordinatorClosed);
                _candidateBarrier = retirement.Completion;
                if (close is not null)
                    startClose.Add(close);
            }

            if (_active is { } active)
            {
                _active = null;
                var activeRetirement = RetireLocked(
                    active,
                    WorkspaceRealizationRetirementReason.CoordinatorClosed);
                RealizationState? close = activeRetirement.StartClose;
                if (close is not null)
                    startClose.Add(close);
            }
            retired = [.. _retired.OrderBy(record => record.Sequence)];
        }

        foreach (RealizationState state in startClose)
            StartClose(state);
        _ = CompleteCloseAsync(retired, completion);
        return completion.Task;
    }

    public async ValueTask DisposeAsync()
    {
        WorkspaceRealizationCoordinatorCloseReport report =
            await CloseAsync().ConfigureAwait(false);
        Exception[] failures =
        [
            .. report.Settlements
                .Where(settlement => settlement.Failure is not null)
                .Select(settlement => settlement.Failure!),
        ];
        if (failures.Length > 0)
            throw new AggregateException(failures);
    }

    internal WorkspaceRealizationConstructionLease EnterConstruction(
        WorkspaceRealizationCandidate candidate)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _closeCompletion is not null,
                this);
            if (!ReferenceEquals(candidate.State, _candidate?.State)
                || candidate.State.Phase != RealizationPhase.Preparing)
            {
                throw new InvalidOperationException(
                    "The candidate is no longer open for construction.");
            }
            candidate.State.ConstructionCount++;
            return new WorkspaceRealizationConstructionLease(
                this,
                candidate.State);
        }
    }

    internal InspectionWorkspace GetConstructionWorkspace(
        RealizationState state)
    {
        lock (_gate)
        {
            if (state.Phase
                is not (RealizationPhase.Preparing
                    or RealizationPhase.Completing
                    or RealizationPhase.Draining))
            {
                throw new InvalidOperationException(
                    "The candidate construction operation is no longer live.");
            }
            return state.Workspace;
        }
    }

    internal void ReleaseConstruction(RealizationState state)
    {
        TaskCompletionSource? completion = null;
        bool startClose = false;
        lock (_gate)
        {
            if (state.ConstructionCount <= 0)
            {
                throw new InvalidOperationException(
                    "A Workspace realization construction lease was released more than once.");
            }
            state.ConstructionCount--;
            if (state.ConstructionCount == 0
                && state.Phase == RealizationPhase.Completing)
            {
                completion = state.ConstructionDrain;
                state.ConstructionDrain = null;
            }
            if (state.ConstructionCount == 0
                && state.OperationCount == 0
                && state.Phase == RealizationPhase.Draining
                && !state.CloseStarted)
            {
                state.CloseStarted = true;
                startClose = true;
            }
        }
        completion?.TrySetResult();
        if (startClose)
            StartClose(state);
    }

    void RetireCandidate(
        WorkspaceRealizationCandidate candidate,
        WorkspaceRealizationRetirementReason reason)
    {
        RealizationState? startClose = null;
        lock (_gate)
        {
            if (!ReferenceEquals(candidate.Owner, this)
                || !ReferenceEquals(candidate.State, _candidate?.State))
            {
                return;
            }

            _candidate = null;
            _currentAttempt = null;
            WorkspaceRealizationRetirement retirement;
            (retirement, startClose) = RetireLocked(
                candidate.State,
                reason);
            _candidateBarrier = retirement.Completion;
        }

        if (startClose is not null)
            StartClose(startClose);
    }

    internal void ReleaseOperation(RealizationState state)
    {
        bool startClose = false;
        lock (_gate)
        {
            if (state.OperationCount <= 0)
            {
                throw new InvalidOperationException(
                    "A Workspace realization operation lease was released more than once.");
            }
            state.OperationCount--;
            if (state.OperationCount == 0
                && state.ConstructionCount == 0
                && state.Phase == RealizationPhase.Draining
                && !state.CloseStarted)
            {
                state.CloseStarted = true;
                startClose = true;
            }
        }

        if (startClose)
            StartClose(state);
    }

    (WorkspaceRealizationRetirement Retirement, RealizationState? StartClose)
        RetireLocked(
            RealizationState state,
            WorkspaceRealizationRetirementReason reason)
    {
        if (state.Retirement is not null)
            return (state.Retirement, null);

        state.AdmissionOpen = false;
        state.Phase = RealizationPhase.Draining;
        TaskCompletionSource? constructionDrain = state.ConstructionDrain;
        state.ConstructionDrain = null;
        state.Retirement = new WorkspaceRealizationRetirement(
            state.Identity,
            reason,
            state.Settlement.Task);
        _retired.Add(new(state.Sequence, state.Settlement.Task));
        constructionDrain?.TrySetResult();
        if (state.ConstructionCount == 0
            && state.OperationCount == 0)
        {
            state.CloseStarted = true;
            return (state.Retirement, state);
        }
        return (state.Retirement, null);
    }

    void StartClose(RealizationState state)
    {
        Task<InspectionWorkspaceCloseReport> close;
        try
        {
            close = state.Workspace.CloseAsync();
        }
        catch (Exception failure)
        {
            CompleteSettlement(state, report: null, failure);
            return;
        }

        _ = ObserveCloseAsync(state, close);
    }

    async Task ObserveCloseAsync(
        RealizationState state,
        Task<InspectionWorkspaceCloseReport> close)
    {
        InspectionWorkspaceCloseReport? report = null;
        Exception? failure = null;
        try
        {
            report = await close.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            report = state.Workspace.CloseReport;
            failure = exception;
        }
        CompleteSettlement(state, report, failure);
    }

    void CompleteSettlement(
        RealizationState state,
        InspectionWorkspaceCloseReport? report,
        Exception? failure)
    {
        WorkspaceRealizationRetirement retirement;
        lock (_gate)
        {
            state.Phase = RealizationPhase.Settled;
            retirement =
                state.Retirement
                ?? throw new InvalidOperationException(
                    "A realization settled without a retirement reason.");
            _ = state.DetachWorkspace();
        }
        state.Settlement.TrySetResult(new WorkspaceRealizationSettlement(
            state.Identity,
            retirement.Reason,
            report,
            failure));
    }

    static async Task CompleteCloseAsync(
        RetirementRecord[] retired,
        TaskCompletionSource<WorkspaceRealizationCoordinatorCloseReport>
            completion)
    {
        WorkspaceRealizationSettlement[] settlements =
            new WorkspaceRealizationSettlement[retired.Length];
        for (int index = 0; index < retired.Length; index++)
        {
            settlements[index] =
                await retired[index].Settlement.ConfigureAwait(false);
        }
        completion.TrySetResult(new(
            ImmutableArray.Create(settlements)));
    }

    internal sealed class RealizationState
    {
        InspectionWorkspace? _workspace;

        internal RealizationState(
            InspectionWorkspace workspace,
            WorkspacePlan originPlan,
            long sequence)
        {
            _workspace = workspace;
            Identity = workspace.Identity;
            OriginPlan = originPlan;
            Sequence = sequence;
        }

        internal InspectionWorkspaceIdentity Identity { get; }
        internal InspectionWorkspace Workspace =>
            _workspace
            ?? throw new ObjectDisposedException(
                nameof(InspectionWorkspace),
                "The Workspace realization has settled.");
        internal InspectionWorkspace? WorkspaceReference => _workspace;
        internal WorkspacePlan OriginPlan { get; }
        internal long Sequence { get; }
        internal RealizationPhase Phase { get; set; } =
            RealizationPhase.Preparing;
        internal bool AdmissionOpen { get; set; }
        internal int ConstructionCount { get; set; }
        internal TaskCompletionSource? ConstructionDrain { get; set; }
        internal int OperationCount { get; set; }
        internal bool CloseStarted { get; set; }
        internal WorkspaceDefinitionSnapshot? InitialDefinition { get; set; }
        internal WorkspaceRealization? Published { get; set; }
        internal WorkspaceRealizationRetirement? Retirement { get; set; }
        internal TaskCompletionSource<WorkspaceRealizationSettlement>
            Settlement { get; } = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        internal Task BeginConstructionDrain()
        {
            ConstructionDrain = new(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return ConstructionDrain.Task;
        }

        internal InspectionWorkspace DetachWorkspace()
        {
            InspectionWorkspace workspace =
                _workspace
                ?? throw new InvalidOperationException(
                    "The Workspace realization was already detached.");
            _workspace = null;
            return workspace;
        }
    }

    sealed record RetirementRecord(
        long Sequence,
        Task<WorkspaceRealizationSettlement> Settlement);

    internal enum RealizationPhase
    {
        Preparing,
        Completing,
        Ready,
        Active,
        Draining,
        Settled,
    }
}

internal sealed record WorkspaceRealizationOperationSnapshot(
    WorkspaceDefinitionSnapshot Definition,
    WorkspaceScopeSnapshot Scope);
