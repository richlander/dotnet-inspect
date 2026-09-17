using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Web;

internal sealed record BrowserRetainedWorkspaceActivationRequest(
    string RetainedDefinitionId,
    string Label,
    string CanonicalLocation,
    string CanonicalPacket)
{
    internal string RetainedDefinitionId { get; } =
        RequireText(RetainedDefinitionId, nameof(RetainedDefinitionId));

    internal string Label { get; } = RequireText(Label, nameof(Label));

    internal string CanonicalLocation { get; } =
        RequireText(CanonicalLocation, nameof(CanonicalLocation));

    internal string CanonicalPacket { get; } =
        RequireText(CanonicalPacket, nameof(CanonicalPacket));

    static string RequireText(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }
}

internal sealed record BrowserRetainedWorkspaceNavigationState(
    int? ActiveStateIndex,
    ImmutableArray<BrowserRetainedWorkspaceViewState> States);

internal sealed record BrowserRetainedWorkspaceViewState(
    string? NavigationId,
    string? SubjectKind,
    string? Facet);

internal sealed record BrowserRetainedWorkspacePredecessor(
    string SettlementId,
    BrowserWorkspaceRealizationRetirement Retirement);

internal sealed record BrowserRetainedWorkspaceInstallation(
    string RetainedDefinitionId,
    string Label,
    string CanonicalLocation,
    string CanonicalPacket,
    string RealizationId,
    long PublicationOrdinal,
    BrowserRetainedWorkspaceNavigationState Navigation,
    BrowserRetainedWorkspacePredecessor? Predecessor);

internal abstract record BrowserRetainedWorkspaceActivationResult
{
    private protected BrowserRetainedWorkspaceActivationResult() { }

    internal sealed record Activated(
        BrowserRetainedWorkspaceInstallation Installation)
        : BrowserRetainedWorkspaceActivationResult;

    internal sealed record NoEffect(
        BrowserRetainedWorkspaceInstallation Installation)
        : BrowserRetainedWorkspaceActivationResult;

    internal sealed record Superseded
        : BrowserRetainedWorkspaceActivationResult;

    internal sealed record Failed(CompleteRestorationFailure Failure)
        : BrowserRetainedWorkspaceActivationResult;
}

internal abstract record BrowserRetainedWorkspaceDeactivationResult
{
    private protected BrowserRetainedWorkspaceDeactivationResult() { }

    internal sealed record Deactivated(
        WorkspaceRealizationSettlement Settlement)
        : BrowserRetainedWorkspaceDeactivationResult;

    internal sealed record NoEffect
        : BrowserRetainedWorkspaceDeactivationResult;

    internal sealed record Rejected(string Message)
        : BrowserRetainedWorkspaceDeactivationResult;
}

internal abstract record BrowserRetainedWorkspaceSettlementResult
{
    private protected BrowserRetainedWorkspaceSettlementResult() { }

    internal sealed record Settled(WorkspaceRealizationSettlement Settlement)
        : BrowserRetainedWorkspaceSettlementResult;

    internal sealed record Unknown
        : BrowserRetainedWorkspaceSettlementResult;
}

internal sealed record BrowserCompleteWorkspaceActivation(
    WorkspaceRealization Realization,
    BrowserWorkspaceRealizationRetirement? Predecessor);

[SupportedOSPlatform("browser")]
internal sealed class BrowserRetainedWorkspaceActivationOwner :
    IAsyncDisposable
{
    readonly object _gate = new();
    readonly Func<BrowserWorkspaceRealizationHost> _hostFactory;
    readonly Func<CompleteRestorationExecutionOptions> _optionsFactory;
    readonly Dictionary<string, Task<WorkspaceRealizationSettlement>>
        _settlements = new(StringComparer.Ordinal);
    BrowserWorkspaceRealizationHost _host;
    BrowserRetainedWorkspaceActivationIntent? _latestIntent;
    BrowserRetainedWorkspaceInstallation? _active;
    long _nextRealization;
    long _nextSettlement;
    bool _deactivating;
    bool _cleanupFailed;
    bool _closing;
    Task? _disposeCompletion;

    internal BrowserRetainedWorkspaceActivationOwner(
        Func<CompleteRestorationExecutionOptions> optionsFactory,
        Func<BrowserWorkspaceRealizationHost>? hostFactory = null)
    {
        _optionsFactory = optionsFactory
            ?? throw new ArgumentNullException(nameof(optionsFactory));
        _hostFactory = hostFactory
            ?? (static () => new BrowserWorkspaceRealizationHost());
        _host = _hostFactory();
    }

    internal BrowserRetainedWorkspaceInstallation? Active
    {
        get
        {
            lock (_gate)
                return _active;
        }
    }

    internal BrowserWorkspaceRealizationCapacitySnapshot Capacity
    {
        get
        {
            lock (_gate)
                return _host.Capacity;
        }
    }

    internal async Task<BrowserRetainedWorkspaceActivationResult> ActivateAsync(
        BrowserRetainedWorkspaceActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        BrowserRetainedWorkspaceActivationIntent intent;
        lock (_gate)
        {
            if (_closing)
            {
                return new BrowserRetainedWorkspaceActivationResult.Failed(
                    HostFailure("The retained Workspace activation owner is closed."));
            }
            if (_deactivating)
            {
                return new BrowserRetainedWorkspaceActivationResult.Failed(
                    HostFailure(
                        "The active Browser Workspace is still draining."));
            }
            if (_cleanupFailed)
            {
                return new BrowserRetainedWorkspaceActivationResult.Failed(
                    new CompleteRestorationFailure.CleanupFailed(
                        "A prior Browser Workspace failed to settle."));
            }

            _latestIntent?.Supersede();
            if (_active?.RetainedDefinitionId == request.RetainedDefinitionId)
            {
                _latestIntent = null;
                return new BrowserRetainedWorkspaceActivationResult.NoEffect(
                    _active);
            }

            intent = new(this, request);
            _latestIntent = intent;
        }

        using var cancellationRegistration = cancellationToken.Register(
            static state =>
                ((BrowserRetainedWorkspaceActivationIntent)state!).Cancel(),
            intent);
        CompleteRestorationResult<BrowserCompleteWorkspaceActivation> result;
        try
        {
            CompleteRestorationPreparationResult preparation =
                CompleteRestorationPreparation.FromPacket(
                    request.CanonicalPacket,
                    intent,
                    cancellationToken);
            var host = new BrowserCompleteRestorationHost(_host, intent);
            result = await CompleteRestorationCoordinator.RestoreAsync(
                        preparation,
                        intent,
                        host,
                        _optionsFactory(),
                        cancellationToken)
                    .ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_latestIntent, intent))
                    _latestIntent = null;
            }
            cancellationRegistration.Dispose();
            intent.Dispose();
        }

        return result switch
        {
            CompleteRestorationResult<
                    BrowserCompleteWorkspaceActivation>.Activated activated =>
                Activated(intent, activated),
            CompleteRestorationResult<
                    BrowserCompleteWorkspaceActivation>.Superseded =>
                new BrowserRetainedWorkspaceActivationResult.Superseded(),
            CompleteRestorationResult<
                    BrowserCompleteWorkspaceActivation>.Failed failed =>
                new BrowserRetainedWorkspaceActivationResult.Failed(
                    failed.Failure),
            _ => throw new InvalidOperationException(
                "Complete restoration returned an unsupported Browser outcome."),
        };
    }

    internal async ValueTask<WorkspaceRealizationOperationAdmission>
        EnterOperationAsync(
            string retainedDefinitionId,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        BrowserWorkspaceRealizationHost host;
        lock (_gate)
        {
            if (_active?.RetainedDefinitionId != retainedDefinitionId)
            {
                return new WorkspaceRealizationOperationAdmission.Unavailable(
                    WorkspaceRealizationOperationUnavailableReason.NoActiveRealization);
            }
            host = _host;
        }
        return await host.EnterOperationAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    internal async Task<BrowserRetainedWorkspaceDeactivationResult>
        DeactivateAsync(
            string retainedDefinitionId,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        BrowserWorkspaceRealizationHost retiredHost;
        InspectionWorkspaceIdentity activeRealization;
        lock (_gate)
        {
            if (_closing)
            {
                return new BrowserRetainedWorkspaceDeactivationResult.Rejected(
                    "The retained Workspace activation owner is closed.");
            }
            if (_deactivating)
            {
                return new BrowserRetainedWorkspaceDeactivationResult.Rejected(
                    "The active Browser Workspace is already draining.");
            }
            if (_latestIntent is not null)
            {
                return new BrowserRetainedWorkspaceDeactivationResult.Rejected(
                    "A retained Workspace activation is still pending.");
            }
            if (_active is null)
            {
                return new BrowserRetainedWorkspaceDeactivationResult.NoEffect();
            }
            if (_active.RetainedDefinitionId != retainedDefinitionId)
            {
                return new BrowserRetainedWorkspaceDeactivationResult.Rejected(
                    "A different retained Workspace definition is active.");
            }

            retiredHost = _host;
            activeRealization = retiredHost.Current?.Identity
                ?? throw new InvalidOperationException(
                    "An active retained definition requires an active realization.");
            _active = null;
            _deactivating = true;
        }

        BrowserWorkspaceRealizationHostCloseReport report =
            await retiredHost.CloseAsync()
                .ConfigureAwait(false);
        WorkspaceRealizationSettlement settlement =
            report.Coordinator.Settlements.Single(candidate =>
                ReferenceEquals(
                    candidate.Realization,
                    activeRealization));
        lock (_gate)
        {
            _deactivating = false;
            if (report.Capacity.FailedSettlements.IsEmpty && !_closing)
                _host = _hostFactory();
            else if (!report.Capacity.FailedSettlements.IsEmpty)
                _cleanupFailed = true;
        }
        return report.Capacity.FailedSettlements.IsEmpty
            ? new BrowserRetainedWorkspaceDeactivationResult.Deactivated(
                settlement)
            : new BrowserRetainedWorkspaceDeactivationResult.Rejected(
                "The active Workspace could not be settled.");
    }

    internal async Task<BrowserRetainedWorkspaceSettlementResult>
        ObserveSettlementAsync(
            string settlementId,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settlementId);
        Task<WorkspaceRealizationSettlement>? completion;
        lock (_gate)
            _settlements.TryGetValue(settlementId, out completion);
        if (completion is null)
            return new BrowserRetainedWorkspaceSettlementResult.Unknown();

        WorkspaceRealizationSettlement settlement =
            await completion.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_gate)
            _settlements.Remove(settlementId);
        return new BrowserRetainedWorkspaceSettlementResult.Settled(settlement);
    }

    internal BrowserRetainedWorkspaceCommitResult TryCommit(
        BrowserRetainedWorkspaceActivationIntent intent,
        CompleteWorkspaceActivation workspace,
        Func<BrowserWorkspaceRealizationCutoverResult> cutOver)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(cutOver);

        lock (_gate)
        {
            if (_closing
                || !ReferenceEquals(_latestIntent, intent)
                || intent.Status != CompleteRestorationIntentStatus.Current)
            {
                return new BrowserRetainedWorkspaceCommitResult.Superseded();
            }

            BrowserWorkspaceRealizationCutoverResult result = cutOver();
            if (result
                is not BrowserWorkspaceRealizationCutoverResult.Activated
                    activated)
            {
                return new BrowserRetainedWorkspaceCommitResult.Rejected(
                    ((BrowserWorkspaceRealizationCutoverResult.Rejected)result)
                        .Reason);
            }

            BrowserRetainedWorkspacePredecessor? predecessor = null;
            if (activated.Predecessor is not null)
            {
                string settlementId = $"workspace-settlement-{++_nextSettlement}";
                _settlements.Add(
                    settlementId,
                    activated.Predecessor.Completion);
                predecessor = new(settlementId, activated.Predecessor);
            }

            CompleteRestorationProjection.Projectable projection =
                workspace.Projection
                    as CompleteRestorationProjection.Projectable
                ?? throw new InvalidOperationException(
                    "Packet restoration must retain its canonical packet.");
            (ImmutableArray<CompleteRestorationResolvedViewState> states,
                int? activeStateIndex) =
                workspace.Snapshot.Resolved switch
                {
                    CompleteRestorationResolvedState.Version2 version2 =>
                        (version2.States, version2.ActiveStateIndex),
                    CompleteRestorationResolvedState.Version3 version3 =>
                        (version3.States, version3.ActiveStateIndex),
                    _ => throw new InvalidOperationException(
                        "Unknown complete restoration resolved state."),
                };
            long publicationOrdinal = ++_nextRealization;
            var installation = new BrowserRetainedWorkspaceInstallation(
                intent.Request.RetainedDefinitionId,
                intent.Request.Label,
                intent.Request.CanonicalLocation,
                projection.CanonicalPacket,
                $"workspace-realization-{publicationOrdinal}",
                publicationOrdinal,
                new BrowserRetainedWorkspaceNavigationState(
                    activeStateIndex,
                    [
                        .. states.Select(
                            static state =>
                                new BrowserRetainedWorkspaceViewState(
                                    state.NavigationId,
                                    state.Definition.Subject?.Kind.ToString(),
                                    state.Definition.Facet)),
                    ]),
                predecessor);
            _active = installation;
            return new BrowserRetainedWorkspaceCommitResult.Committed(
                activated,
                installation);
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_disposeCompletion is not null)
                return new(_disposeCompletion);

            _closing = true;
            _latestIntent?.Cancel();
            _latestIntent = null;
            _active = null;
            _disposeCompletion = DisposeHostAsync(_host);
            return new(_disposeCompletion);
        }
    }

    static async Task DisposeHostAsync(
        BrowserWorkspaceRealizationHost host) =>
        await host.DisposeAsync().ConfigureAwait(false);

    static BrowserRetainedWorkspaceActivationResult Activated(
        BrowserRetainedWorkspaceActivationIntent intent,
        CompleteRestorationResult<
            BrowserCompleteWorkspaceActivation>.Activated activated)
    {
        if (!ReferenceEquals(
                activated.Workspace.Intent,
                intent.Identity))
        {
            throw new InvalidOperationException(
                "Browser activation returned a different restoration intent.");
        }
        return new BrowserRetainedWorkspaceActivationResult.Activated(
            intent.CommittedInstallation
            ?? throw new InvalidOperationException(
                "Browser activation completed without an installation."));
    }

    static CompleteRestorationFailure HostFailure(string message) =>
        new CompleteRestorationFailure.HostConstructionFailed(message);
}

internal abstract record BrowserRetainedWorkspaceCommitResult
{
    private protected BrowserRetainedWorkspaceCommitResult() { }

    internal sealed record Committed(
        BrowserWorkspaceRealizationCutoverResult.Activated Cutover,
        BrowserRetainedWorkspaceInstallation Installation)
        : BrowserRetainedWorkspaceCommitResult;

    internal sealed record Superseded
        : BrowserRetainedWorkspaceCommitResult;

    internal sealed record Rejected(
        WorkspaceRealizationCandidateRejection Reason)
        : BrowserRetainedWorkspaceCommitResult;
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserRetainedWorkspaceActivationIntent(
    BrowserRetainedWorkspaceActivationOwner owner,
    BrowserRetainedWorkspaceActivationRequest request)
    : ICompleteRestorationIntentAuthority, IDisposable
{
    readonly CancellationTokenSource _revocation = new();
    int _status = (int)CompleteRestorationIntentStatus.Current;

    public CompleteRestorationIntentIdentity Identity { get; } = new();

    public CompleteRestorationIntentStatus Status =>
        (CompleteRestorationIntentStatus)Volatile.Read(ref _status);

    public CancellationToken Revocation => _revocation.Token;

    internal BrowserRetainedWorkspaceActivationRequest Request { get; } =
        request;

    internal BrowserRetainedWorkspaceInstallation? CommittedInstallation
    {
        get;
        private set;
    }

    internal BrowserRetainedWorkspaceCommitResult TryCommit(
        CompleteWorkspaceActivation workspace,
        Func<BrowserWorkspaceRealizationCutoverResult> cutOver)
    {
        BrowserRetainedWorkspaceCommitResult result =
            owner.TryCommit(this, workspace, cutOver);
        if (result
            is BrowserRetainedWorkspaceCommitResult.Committed committed)
        {
            CommittedInstallation = committed.Installation;
        }
        return result;
    }

    internal void Supersede() =>
        Revoke(CompleteRestorationIntentStatus.Superseded);

    internal void Cancel() =>
        Revoke(CompleteRestorationIntentStatus.Cancelled);

    void Revoke(CompleteRestorationIntentStatus status)
    {
        if (Interlocked.CompareExchange(
                ref _status,
                (int)status,
                (int)CompleteRestorationIntentStatus.Current)
            == (int)CompleteRestorationIntentStatus.Current)
        {
            _revocation.Cancel();
        }
    }

    public void Dispose() => _revocation.Dispose();
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserCompleteRestorationHost(
    BrowserWorkspaceRealizationHost host,
    BrowserRetainedWorkspaceActivationIntent intent)
    : ICompleteRestorationHost<BrowserCompleteWorkspaceActivation>
{
    public async ValueTask<
        CompleteRestorationHostResult<BrowserCompleteWorkspaceActivation>>
        ConstructAsync(
            ICompleteRestorationIntentAuthority authority,
            CompleteRestorationPlan plan,
            CompleteWorkspacePreparationCallback prepare,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(prepare);
        if (!ReferenceEquals(authority, intent))
        {
            return Failed(
                "The Browser host received a different activation intent.");
        }

        BrowserWorkspaceRealizationCandidateStartResult start =
            await host.BeginCandidateAsync(
                    plan.WorkspacePlan,
                    cancellationToken)
                .ConfigureAwait(false);
        if (start
            is not BrowserWorkspaceRealizationCandidateStartResult.Prepared
                prepared)
        {
            return start switch
            {
                BrowserWorkspaceRealizationCandidateStartResult.Superseded =>
                    new CompleteRestorationHostResult<
                        BrowserCompleteWorkspaceActivation>.Superseded(),
                BrowserWorkspaceRealizationCandidateStartResult
                        .CapacityUnavailable unavailable =>
                    Failed(
                        $"The Browser Workspace capacity of "
                        + $"{unavailable.Capacity.Limit} realizations is unavailable."),
                BrowserWorkspaceRealizationCandidateStartResult.Closed =>
                    Failed("The Browser Workspace host is closed."),
                _ => Failed(
                    "The Browser Workspace realization could not be prepared."),
            };
        }

        CompleteWorkspacePreparationResult workspacePreparation;
        using (WorkspaceRealizationConstructionLease construction =
            prepared.Candidate.EnterConstruction())
        {
            workspacePreparation = await prepare(
                    construction.Workspace,
                    authority.Revocation)
                .ConfigureAwait(false);
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
                    BrowserCompleteWorkspaceActivation>.Failed(cleanup);
            }
            return workspacePreparation switch
            {
                CompleteWorkspacePreparationResult.Failed failed =>
                    new CompleteRestorationHostResult<
                        BrowserCompleteWorkspaceActivation>.Failed(
                            failed.Failure),
                CompleteWorkspacePreparationResult.Superseded =>
                    new CompleteRestorationHostResult<
                        BrowserCompleteWorkspaceActivation>.Superseded(),
                _ => Failed(
                    "The Browser Workspace preparation returned an unsupported result."),
            };
        }

        WorkspaceRealizationCandidateCompletionResult completion =
            await host.CompleteCandidateAsync(
                    prepared.Candidate,
                    cancellationToken)
                .ConfigureAwait(false);
        if (completion
            is not WorkspaceRealizationCandidateCompletionResult.Ready ready)
        {
            CompleteRestorationFailure? cleanup =
                await SettleCandidateAsync(prepared.Candidate)
                    .ConfigureAwait(false);
            if (cleanup is not null)
            {
                return new CompleteRestorationHostResult<
                    BrowserCompleteWorkspaceActivation>.Failed(cleanup);
            }
            var rejected =
                (WorkspaceRealizationCandidateCompletionResult.Rejected)
                    completion;
            return Failed(
                rejected.RuntimeFailure is null
                    ? $"Browser Workspace completion was rejected: "
                        + $"{rejected.Reason}."
                    : $"Browser Workspace completion was rejected: "
                        + $"{rejected.Reason}: {rejected.RuntimeFailure}.");
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
                    "Browser Workspace completion did not retain the exact restoration definition.")
                : new CompleteRestorationHostResult<
                    BrowserCompleteWorkspaceActivation>.Failed(cleanup);
        }

        BrowserRetainedWorkspaceCommitResult commit =
            intent.TryCommit(
                preparedWorkspace.Activation,
                () => host.CutOver(prepared.Candidate));
        if (commit
            is BrowserRetainedWorkspaceCommitResult.Committed committed)
        {
            return new CompleteRestorationHostResult<
                BrowserCompleteWorkspaceActivation>.Activated(
                    new(
                        committed.Cutover.Realization,
                        committed.Cutover.Predecessor),
                    preparedWorkspace.Activation);
        }

        CompleteRestorationFailure? settlement =
            await SettleCandidateAsync(prepared.Candidate)
                .ConfigureAwait(false);
        if (settlement is not null)
        {
            return new CompleteRestorationHostResult<
                BrowserCompleteWorkspaceActivation>.Failed(settlement);
        }
        return commit switch
        {
            BrowserRetainedWorkspaceCommitResult.Superseded =>
                new CompleteRestorationHostResult<
                    BrowserCompleteWorkspaceActivation>.Superseded(),
            BrowserRetainedWorkspaceCommitResult.Rejected rejected =>
                Failed(
                    $"Browser Workspace activation was rejected: "
                    + $"{rejected.Reason}."),
            _ => throw new InvalidOperationException(
                "The Browser Workspace commit returned an unsupported result."),
        };
    }

    async ValueTask<CompleteRestorationFailure?> SettleCandidateAsync(
        BrowserWorkspaceRealizationCandidate candidate)
    {
        BrowserWorkspaceRealizationCandidateRetirementResult retirement =
            host.AbandonCandidate(candidate);
        if (retirement
            is BrowserWorkspaceRealizationCandidateRetirementResult.Rejected
                rejected
            && rejected.Reason
                != WorkspaceRealizationCandidateRejection.StaleCandidate)
        {
            return new CompleteRestorationFailure.CleanupFailed(
                $"The unpublished Browser Workspace could not retire: "
                    + $"{rejected.Reason}.");
        }

        WorkspaceRealizationSettlement settlement =
            await candidate.Settlement.ConfigureAwait(false);
        return settlement.Succeeded
            ? null
            : new CompleteRestorationFailure.CleanupFailed(
                "The unpublished Browser Workspace could not be settled after "
                    + $"{settlement.Reason}.");
    }

    static CompleteRestorationHostResult<
        BrowserCompleteWorkspaceActivation>.Failed Failed(string message) =>
        new(new CompleteRestorationFailure.HostConstructionFailed(message));
}

[SupportedOSPlatform("browser")]
internal static class BrowserCompleteRestorationOptions
{
    internal static CompleteRestorationExecutionOptions Create()
    {
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        var executableEntries = new ViewFacetAvailabilitySnapshot(
            registry.Descriptors.Select(
                static descriptor =>
                    new ViewFacetAvailabilityFact(
                        descriptor.Id,
                        ViewFacetAvailability.Available.Instance)));
        return new CompleteRestorationExecutionOptions
        {
            ContextLoad = new WorkspaceContextLoadOptions
            {
                HttpClient = BrowserPackageWorkspace.NetworkClient,
                SourceAuthorization =
                    BrowserPackageWorkspace.PackageSourceAuthorization,
                PackageStore =
                    BrowserPackageWorkspace.SessionPackageStore,
                PackageTransferPolicy =
                    BrowserPackageWorkspace.PackageTransferPolicy,
                PayloadLimits = BrowserPackageWorkspace.PackageLimits,
                IncludePackageRootBindings = true,
            },
            ScopeDeadline = DateTimeOffset.UtcNow.Add(
                BrowserPackageWorkspace.PackageChangesOperationTimeout),
            Facets = registry,
            FacetAvailability = (_, _) => executableEntries,
        };
    }
}
