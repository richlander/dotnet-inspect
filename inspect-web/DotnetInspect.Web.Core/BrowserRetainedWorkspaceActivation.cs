using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Web;

internal sealed record BrowserRetainedWorkspaceSelection(
    string RetainedDefinitionId,
    string ActivationId,
    CompleteWorkspaceActivation Workspace,
    NavigationConsumerResult Navigation);

internal sealed record BrowserRetainedWorkspaceSettlementReference(
    string SettlementId,
    Task<WorkspaceRealizationSettlement> Completion);

internal abstract record BrowserRetainedWorkspaceSettlementResult
{
    private protected BrowserRetainedWorkspaceSettlementResult(
        string settlementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settlementId);
        SettlementId = settlementId;
    }

    internal string SettlementId { get; }

    internal sealed record Settled :
        BrowserRetainedWorkspaceSettlementResult
    {
        internal Settled(
            string settlementId,
            WorkspaceRealizationSettlement settlement)
            : base(settlementId)
        {
            Settlement = settlement
                ?? throw new ArgumentNullException(nameof(settlement));
        }

        internal WorkspaceRealizationSettlement Settlement { get; }
    }

    internal sealed record Unavailable(string Id)
        : BrowserRetainedWorkspaceSettlementResult(Id);
}

internal sealed class BrowserRetainedWorkspaceSettlementRegistry
{
    readonly object _gate = new();
    readonly int _capacity;
    readonly Dictionary<string, Entry> _entries = [];
    readonly LinkedList<string> _order = [];

    internal BrowserRetainedWorkspaceSettlementRegistry(int capacity = 64)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _capacity = capacity;
    }

    internal (int Entries, int Ordered) Counts
    {
        get
        {
            lock (_gate)
                return (_entries.Count, _order.Count);
        }
    }

    internal BrowserRetainedWorkspaceSettlementReference Register(
        Task<WorkspaceRealizationSettlement> completion)
    {
        ArgumentNullException.ThrowIfNull(completion);
        lock (_gate)
        {
            while (_entries.Count >= _capacity)
                RemoveOldestCompletedLocked();

            string settlementId = Guid.NewGuid().ToString("N");
            LinkedListNode<string> order = _order.AddLast(settlementId);
            _entries.Add(settlementId, new(completion, order));
            return new BrowserRetainedWorkspaceSettlementReference(
                settlementId,
                completion);
        }
    }

    internal async ValueTask<BrowserRetainedWorkspaceSettlementResult>
        AwaitAsync(string settlementId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settlementId);

        Entry? entry;
        lock (_gate)
            _entries.TryGetValue(settlementId, out entry);
        if (entry is null)
        {
            return new BrowserRetainedWorkspaceSettlementResult.Unavailable(
                settlementId);
        }

        WorkspaceRealizationSettlement settlement;
        try
        {
            settlement = await entry.Completion.ConfigureAwait(false);
        }
        finally
        {
            lock (_gate)
            {
                if (_entries.TryGetValue(settlementId, out Entry? current)
                    && ReferenceEquals(current, entry))
                {
                    _entries.Remove(settlementId);
                    _order.Remove(entry.Order);
                }
            }
        }
        return new BrowserRetainedWorkspaceSettlementResult.Settled(
            settlementId,
            settlement);
    }

    void RemoveOldestCompletedLocked()
    {
        LinkedListNode<string>? current = _order.First;
        while (current is not null)
        {
            LinkedListNode<string>? next = current.Next;
            Entry entry = _entries[current.Value];
            if (entry.Completion.IsCompleted)
            {
                _entries.Remove(current.Value);
                _order.Remove(current);
                return;
            }
            current = next;
        }

        throw new InvalidOperationException(
            "Unobserved Browser Workspace settlements exceeded "
                + "the bounded terminal-record capacity.");
    }

    sealed record Entry(
        Task<WorkspaceRealizationSettlement> Completion,
        LinkedListNode<string> Order);
}

internal abstract record BrowserRetainedWorkspaceActivationResult
{
    private protected BrowserRetainedWorkspaceActivationResult(
        string retainedDefinitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        RetainedDefinitionId = retainedDefinitionId;
    }

    internal string RetainedDefinitionId { get; }

    internal sealed record Activated : BrowserRetainedWorkspaceActivationResult
    {
        internal Activated(
            string retainedDefinitionId,
            BrowserRetainedWorkspaceSelection selection,
            BrowserRetainedWorkspaceSettlementReference?
                predecessorSettlement)
            : base(retainedDefinitionId)
        {
            Selection = selection;
            PredecessorSettlement = predecessorSettlement;
        }

        internal BrowserRetainedWorkspaceSelection Selection { get; }

        internal BrowserRetainedWorkspaceSettlementReference?
            PredecessorSettlement
        {
            get;
        }
    }

    internal sealed record NoEffect : BrowserRetainedWorkspaceActivationResult
    {
        internal NoEffect(
            string retainedDefinitionId,
            BrowserRetainedWorkspaceSelection selection)
            : base(retainedDefinitionId)
        {
            Selection = selection;
        }

        internal BrowserRetainedWorkspaceSelection Selection { get; }
    }

    internal sealed record Failed : BrowserRetainedWorkspaceActivationResult
    {
        internal Failed(
            string retainedDefinitionId,
            CompleteRestorationFailure failure)
            : base(retainedDefinitionId)
        {
            Failure = failure
                ?? throw new ArgumentNullException(nameof(failure));
        }

        internal CompleteRestorationFailure Failure { get; }
    }

    internal sealed record Superseded(string Id)
        : BrowserRetainedWorkspaceActivationResult(Id);
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserRetainedWorkspaceActivationCoordinator
    : IAsyncDisposable
{
    readonly object _gate = new();
    readonly BrowserWorkspaceRealizationHost _realizationHost;
    readonly BrowserCompleteRestorationHost _restorationHost;
    readonly Func<CompleteRestorationExecutionOptions> _options;
    readonly BrowserRetainedWorkspaceSettlementRegistry _settlements = new();
    IntentAuthority? _currentIntent;
    BrowserRetainedWorkspaceSelection? _active;
    bool _closing;

    internal BrowserRetainedWorkspaceActivationCoordinator(
        BrowserWorkspaceRealizationHost realizationHost,
        Func<CompleteRestorationExecutionOptions> options)
    {
        _realizationHost = realizationHost
            ?? throw new ArgumentNullException(nameof(realizationHost));
        _restorationHost = new(realizationHost);
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    internal static BrowserRetainedWorkspaceActivationCoordinator
        CreateProduction()
    {
        ViewFacetRegistry facets = InspectionViewFacetCatalog.Registry;
        var available = new ViewFacetAvailabilitySnapshot(
            facets.Descriptors.Select(
                descriptor => new ViewFacetAvailabilityFact(
                    descriptor.Id,
                    ViewFacetAvailability.Available.Instance)));
        return new(
            new BrowserWorkspaceRealizationHost(),
            () => new CompleteRestorationExecutionOptions
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
                },
                ScopeDeadline = DateTimeOffset.UtcNow
                    .Add(BrowserPackageWorkspace.PackageOperationTimeout),
                Facets = facets,
                FacetAvailability = (_, _) => available,
                PackageSurfaceLimits = BrowserApiSurfacePolicy.Limits,
            });
    }

    internal BrowserRetainedWorkspaceSelection? Active
    {
        get
        {
            lock (_gate)
                return _active;
        }
    }

    internal WorkspaceRealizationSettlement[] FailedSettlements
        => [.. _realizationHost.Capacity.FailedSettlements];

    internal async ValueTask<BrowserRetainedWorkspaceSettlementResult>
        AwaitSettlementAsync(string settlementId) =>
        await _settlements.AwaitAsync(settlementId).ConfigureAwait(false);

    internal async ValueTask<BrowserRetainedWorkspaceActivationResult>
        ActivatePacketAsync(
            string retainedDefinitionId,
            string packet,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        ArgumentNullException.ThrowIfNull(packet);
        if (cancellationToken.IsCancellationRequested)
        {
            return Failed(
                retainedDefinitionId,
                new CompleteRestorationFailure.Cancelled(
                    "Browser retained Workspace activation was cancelled."));
        }

        IntentAuthority authority;
        lock (_gate)
        {
            if (_closing)
            {
                return Failed(
                    retainedDefinitionId,
                    new CompleteRestorationFailure.HostConstructionFailed(
                        "The Browser retained Workspace coordinator is closed."));
            }
            if (_active is { } active
                && string.Equals(
                    active.RetainedDefinitionId,
                    retainedDefinitionId,
                    StringComparison.Ordinal))
            {
                _currentIntent?.Supersede();
                _currentIntent = null;
                return new BrowserRetainedWorkspaceActivationResult.NoEffect(
                    retainedDefinitionId,
                    active);
            }

            _currentIntent?.Supersede();
            authority = new IntentAuthority(cancellationToken);
            _currentIntent = authority;
        }

        try
        {
            CompleteRestorationPreparationResult preparation =
                CompleteRestorationPreparation.FromPacket(packet, authority);
            CompleteRestorationResult<BrowserPreparedWorkspaceActivation>
                restoration =
                    await CompleteRestorationCoordinator.RestoreAsync(
                        preparation,
                        authority,
                        _restorationHost,
                        _options(),
                        cancellationToken).ConfigureAwait(false);

            if (restoration
                is CompleteRestorationResult<
                    BrowserPreparedWorkspaceActivation>.Failed failed)
            {
                return RestorationFailure(
                    retainedDefinitionId,
                    failed.Failure,
                    authority.Status);
            }
            if (restoration
                is CompleteRestorationResult<
                    BrowserPreparedWorkspaceActivation>.Superseded)
            {
                return new BrowserRetainedWorkspaceActivationResult
                    .Superseded(retainedDefinitionId);
            }

            var prepared =
                (CompleteRestorationResult<
                    BrowserPreparedWorkspaceActivation>.Activated)restoration;
            BrowserWorkspaceRealizationCutoverResult cutover;
            BrowserRetainedWorkspaceSelection? selection = null;
            BrowserRetainedWorkspaceSettlementReference?
                predecessorSettlement = null;
            lock (_gate)
            {
                if (_closing
                    || !ReferenceEquals(_currentIntent, authority)
                    || authority.Status
                        != CompleteRestorationIntentStatus.Current
                    || authority.Revocation.IsCancellationRequested)
                {
                    authority.Supersede();
                    cutover = new BrowserWorkspaceRealizationCutoverResult
                        .Rejected(
                            WorkspaceRealizationCandidateRejection
                                .StaleCandidate);
                }
                else
                {
                    cutover = _realizationHost.CutOver(
                        prepared.Activation.Candidate);
                    if (cutover
                        is BrowserWorkspaceRealizationCutoverResult.Activated
                            activated)
                    {
                        selection = new BrowserRetainedWorkspaceSelection(
                            retainedDefinitionId,
                            Guid.NewGuid().ToString("N"),
                            prepared.Workspace,
                            prepared.Workspace.Snapshot.Navigation.Result
                                .Consumer);
                        _active = selection;
                        if (activated.Predecessor is { } predecessor)
                        {
                            predecessorSettlement =
                                _settlements.Register(
                                    predecessor.Completion);
                        }
                    }
                }
            }

            if (cutover
                is BrowserWorkspaceRealizationCutoverResult.Activated
                    )
            {
                return new BrowserRetainedWorkspaceActivationResult.Activated(
                    retainedDefinitionId,
                    selection!,
                    predecessorSettlement);
            }

            CompleteRestorationFailure? cleanup =
                await SettleUnpublishedAsync(prepared.Activation.Candidate)
                    .ConfigureAwait(false);
            var rejected =
                (BrowserWorkspaceRealizationCutoverResult.Rejected)cutover;
            if (cleanup is not null)
            {
                return Failed(retainedDefinitionId, cleanup);
            }
            if (authority.Status
                == CompleteRestorationIntentStatus.Superseded)
            {
                return new BrowserRetainedWorkspaceActivationResult
                    .Superseded(retainedDefinitionId);
            }
            return Failed(
                retainedDefinitionId,
                new CompleteRestorationFailure.HostConstructionFailed(
                    $"The Browser Workspace candidate could not cut over: "
                        + $"{rejected.Reason}."));
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_currentIntent, authority))
                    _currentIntent = null;
            }
            authority.Dispose();
        }
    }

    async ValueTask<CompleteRestorationFailure?> SettleUnpublishedAsync(
        BrowserWorkspaceRealizationCandidate candidate)
    {
        WorkspaceRealizationSettlement settlement;
        if (candidate.Settlement.IsCompleted)
        {
            settlement = await candidate.Settlement.ConfigureAwait(false);
        }
        else
        {
            BrowserWorkspaceRealizationCandidateRetirementResult retirement =
                _realizationHost.AbandonCandidate(candidate);
            settlement = retirement
                is BrowserWorkspaceRealizationCandidateRetirementResult
                    .Retiring retiring
                    ? await retiring.Retirement.Completion
                        .ConfigureAwait(false)
                    : await candidate.Settlement.ConfigureAwait(false);
        }

        if (settlement.Succeeded)
            return null;

        return new CompleteRestorationFailure.CleanupFailed(
            $"Browser Workspace candidate settlement failed during "
                + $"{settlement.Reason}.");
    }

    public async ValueTask DisposeAsync()
    {
        lock (_gate)
        {
            if (_closing)
                return;
            _closing = true;
            _currentIntent?.Cancel();
        }

        await _realizationHost.DisposeAsync().ConfigureAwait(false);
    }

    static BrowserRetainedWorkspaceActivationResult Failed(
        string retainedDefinitionId,
        CompleteRestorationFailure failure) =>
        new BrowserRetainedWorkspaceActivationResult.Failed(
            retainedDefinitionId,
            failure);

    internal static BrowserRetainedWorkspaceActivationResult
        RestorationFailure(
            string retainedDefinitionId,
            CompleteRestorationFailure failure,
            CompleteRestorationIntentStatus status) =>
        failure is CompleteRestorationFailure.CleanupFailed
            || status != CompleteRestorationIntentStatus.Superseded
            ? Failed(retainedDefinitionId, failure)
            : new BrowserRetainedWorkspaceActivationResult.Superseded(
                retainedDefinitionId);

    sealed class IntentAuthority :
        ICompleteRestorationIntentAuthority,
        IDisposable
    {
        readonly CancellationTokenSource _revocation = new();
        readonly CancellationTokenRegistration _cancellationRegistration;
        int _status = (int)CompleteRestorationIntentStatus.Current;

        internal IntentAuthority(CancellationToken cancellationToken)
        {
            _cancellationRegistration = cancellationToken.Register(
                static state => ((IntentAuthority)state!).Cancel(),
                this);
        }

        public CompleteRestorationIntentIdentity Identity { get; } = new();

        public CompleteRestorationIntentStatus Status =>
            (CompleteRestorationIntentStatus)Volatile.Read(ref _status);

        public CancellationToken Revocation => _revocation.Token;

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

        public void Dispose()
        {
            _cancellationRegistration.Dispose();
            _revocation.Dispose();
        }
    }
}
