using System.Collections.Immutable;
using System.Runtime.Versioning;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;

namespace DotnetInspect.Web;

internal sealed record BrowserRetainedWorkspaceActivationRequest(
    string ActivationIntentId,
    string RetainedDefinitionId,
    string Label,
    string CanonicalLocation,
    CompleteRestorationRequestBasis RestorationRequest,
    int? PresentationActiveTabIndex)
{
    internal BrowserRetainedWorkspaceActivationRequest(
        string activationIntentId,
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket,
        int? presentationActiveTabIndex)
        : this(
            activationIntentId,
            retainedDefinitionId,
            label,
            canonicalLocation,
            new CompleteRestorationRequestBasis.PacketInput(
                RequireText(canonicalPacket, nameof(canonicalPacket))),
            presentationActiveTabIndex)
    {
    }

    internal BrowserRetainedWorkspaceActivationRequest(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket)
        : this(
            retainedDefinitionId,
            retainedDefinitionId,
            label,
            canonicalLocation,
            new CompleteRestorationRequestBasis.PacketInput(
                RequireText(canonicalPacket, nameof(canonicalPacket))),
            PresentationActiveTabIndex: null)
    {
    }

    internal BrowserRetainedWorkspaceActivationRequest(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        CompleteRestorationRequestBasis restorationRequest)
        : this(
            retainedDefinitionId,
            retainedDefinitionId,
            label,
            canonicalLocation,
            restorationRequest,
            PresentationActiveTabIndex: null)
    {
    }

    internal string ActivationIntentId { get; } =
        RequireText(ActivationIntentId, nameof(ActivationIntentId));

    internal string RetainedDefinitionId { get; } =
        RequireText(RetainedDefinitionId, nameof(RetainedDefinitionId));

    internal string Label { get; } = RequireText(Label, nameof(Label));

    internal string CanonicalLocation { get; } =
        RequireText(CanonicalLocation, nameof(CanonicalLocation));

    internal CompleteRestorationRequestBasis RestorationRequest { get; } =
        RestorationRequest
        ?? throw new ArgumentNullException(nameof(RestorationRequest));

    internal int? PresentationActiveTabIndex { get; } =
        PresentationActiveTabIndex is null or >= 0
            ? PresentationActiveTabIndex
            : throw new ArgumentOutOfRangeException(
                nameof(PresentationActiveTabIndex));

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

internal sealed record BrowserRetainedWorkspaceDefinitionTab(
    string Id,
    string Kind,
    string Source,
    string? Version,
    string? Framework,
    string? RuntimeIdentifier);

internal sealed record BrowserRetainedWorkspaceDefinitionContext(
    string Id,
    ImmutableArray<string> TabIds);

internal sealed record BrowserRetainedWorkspaceDefinitionState(
    ImmutableArray<BrowserRetainedWorkspaceDefinitionTab> Tabs,
    ImmutableArray<BrowserRetainedWorkspaceDefinitionContext> Contexts,
    string? ActiveTabId,
    string? SelectedContextId);

internal sealed record BrowserRetainedWorkspacePackage(
    string NavigationId,
    string Kind,
    BrowserPackageSurfaceInfo Surface);

internal sealed record BrowserRetainedWorkspaceInstallation(
    string RetainedDefinitionId,
    string Label,
    string CanonicalLocation,
    CompleteRestorationRequestBasis RestorationRequest,
    CompleteRestorationProjection Projection,
    string RealizationId,
    InspectionWorkspaceIdentity Realization,
    long PublicationOrdinal,
    BrowserRetainedWorkspaceDefinitionState Definition,
    ImmutableArray<BrowserRetainedWorkspacePackage> Packages,
    BrowserRetainedWorkspaceNavigationState Navigation,
    BrowserRetainedWorkspacePredecessor? Predecessor)
{
    internal string? CanonicalPacket =>
        Projection is CompleteRestorationProjection.Projectable projectable
            ? projectable.CanonicalPacket
            : null;
}

internal sealed record BrowserRetainedWorkspacePreparedInstallation(
    string CanonicalPacket,
    BrowserRetainedWorkspaceDefinitionState Definition,
    ImmutableArray<BrowserRetainedWorkspacePackage> Packages,
    BrowserRetainedWorkspaceNavigationState Navigation);

internal abstract record BrowserRetainedWorkspacePreparationResult
{
    private protected BrowserRetainedWorkspacePreparationResult() { }

    internal sealed record Prepared(
        BrowserRetainedWorkspacePreparedInstallation Installation)
        : BrowserRetainedWorkspacePreparationResult;

    internal sealed record NoEffect(
        BrowserRetainedWorkspaceInstallation Installation)
        : BrowserRetainedWorkspacePreparationResult;

    internal sealed record Superseded
        : BrowserRetainedWorkspacePreparationResult;

    internal sealed record Failed(CompleteRestorationFailure Failure)
        : BrowserRetainedWorkspacePreparationResult;
}

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

    internal sealed record CleanupFailed(
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

internal abstract record BrowserRetainedWorkspacePackageActivationResult
{
    private protected BrowserRetainedWorkspacePackageActivationResult() { }

    internal sealed record Activated(BrowserRetainedWorkspacePackage Package)
        : BrowserRetainedWorkspacePackageActivationResult;

    internal sealed record Superseded
        : BrowserRetainedWorkspacePackageActivationResult;
}

internal sealed record BrowserCompleteWorkspaceActivation(
    WorkspaceRealization Realization,
    BrowserWorkspaceRealizationRetirement? Predecessor);

internal sealed record BrowserRetainedWorkspaceActivationRejection(
    string Message)
{
    internal string Message { get; } =
        !string.IsNullOrWhiteSpace(Message)
            ? Message
            : throw new ArgumentException(
                "A retained Workspace rejection requires a message.",
                nameof(Message));
}

internal sealed record BrowserRetainedWorkspaceNonInstallResult(
    WorkspaceRealizationSettlement? Settlement,
    CompleteRestorationFailure? Failure);

[SupportedOSPlatform("browser")]
internal sealed record BrowserRetainedWorkspaceInstallationDraft(
    string RetainedDefinitionId,
    string Label,
    string CanonicalLocation,
    CompleteRestorationRequestBasis RestorationRequest,
    CompleteRestorationProjection Projection,
    BrowserRetainedWorkspaceDefinitionState Definition,
    ImmutableArray<BrowserRetainedWorkspacePackage> Packages,
    BrowserRetainedWorkspaceNavigationState Navigation)
{
    internal static BrowserRetainedWorkspaceInstallationDraft Create(
        BrowserRetainedWorkspaceActivationRequest request,
        CompleteWorkspaceActivation workspace)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        bool exactRequest = (
                request.RestorationRequest,
                workspace.Request)
            switch
            {
                (CompleteRestorationRequestBasis.PacketInput expected,
                    CompleteRestorationRequestBasis.PacketInput actual) =>
                    string.Equals(
                        expected.Encoded,
                        actual.Encoded,
                        StringComparison.Ordinal),
                (CompleteRestorationRequestBasis.DefinitionInput expected,
                    CompleteRestorationRequestBasis.DefinitionInput actual) =>
                    ReferenceEquals(expected, actual),
                _ => false,
            };
        if (!exactRequest)
        {
            throw new InvalidOperationException(
                "The completed Workspace did not retain the activation request.");
        }

        (ImmutableArray<CompleteRestorationResolvedViewState> states,
            int? activeStateIndex) =
            workspace.Snapshot.Resolved switch
            {
                CompleteRestorationResolvedState.Version2 version2 =>
                    (version2.States, version2.ActiveStateIndex),
                CompleteRestorationResolvedState.Version3 version3 =>
                    (version3.States, version3.ActiveStateIndex),
                CompleteRestorationResolvedState.Version4 version4 =>
                    (version4.States, version4.ActiveStateIndex),
                _ => throw new InvalidOperationException(
                    "Unknown complete restoration resolved state."),
            };
        BrowserRetainedWorkspaceNavigationState navigation = new(
            activeStateIndex,
            [
                .. states.Select(
                    static state =>
                        new BrowserRetainedWorkspaceViewState(
                            state.NavigationId,
                            state.Definition.Subject?.Kind.ToString(),
                            state.Definition.Facet)),
            ]);
        BrowserRetainedWorkspacePreparedInstallation? prepared =
            workspace.Projection
                is CompleteRestorationProjection.Projectable
                ? BrowserCompleteRestorationHost.PrepareInstallation(
                    request,
                    workspace)
                : null;
        return new(
            request.RetainedDefinitionId,
            request.Label,
            request.CanonicalLocation,
            request.RestorationRequest,
            workspace.Projection,
            prepared?.Definition
                ?? new([], [], ActiveTabId: null, SelectedContextId: null),
            prepared?.Packages ?? [],
            prepared?.Navigation ?? navigation);
    }

    internal BrowserRetainedWorkspaceInstallation Install(
        InspectionWorkspaceIdentity realization,
        string realizationId,
        long publicationOrdinal,
        BrowserRetainedWorkspacePredecessor? predecessor) =>
        new(
            RetainedDefinitionId,
            Label,
            CanonicalLocation,
            RestorationRequest,
            Projection,
            realizationId,
            realization,
            publicationOrdinal,
            Definition,
            Packages,
            Navigation,
            predecessor);
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserPreparedWorkspaceActivation
{
    readonly object _gate = new();
    readonly BrowserWorkspaceRealizationHost _host;
    readonly BrowserWorkspaceRealizationCandidate _candidate;
    BrowserPreparedWorkspaceActivationState _state =
        BrowserPreparedWorkspaceActivationState.Pending;

    internal BrowserPreparedWorkspaceActivation(
        BrowserWorkspaceRealizationHost host,
        BrowserWorkspaceRealizationCandidate candidate,
        CompleteWorkspaceActivation workspace)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _candidate = candidate
            ?? throw new ArgumentNullException(nameof(candidate));
        Workspace = workspace
            ?? throw new ArgumentNullException(nameof(workspace));
        if (!ReferenceEquals(
                candidate.Realization,
                workspace.Workspace))
        {
            throw new ArgumentException(
                "The Browser candidate and complete Workspace must share one identity.",
                nameof(workspace));
        }
    }

    internal CompleteWorkspaceActivation Workspace { get; }

    internal bool IsPending
    {
        get
        {
            lock (_gate)
            {
                return _state is BrowserPreparedWorkspaceActivationState.Pending
                    or BrowserPreparedWorkspaceActivationState.PublishRejected;
            }
        }
    }

    internal BrowserWorkspaceRealizationCutoverResult CutOver()
    {
        lock (_gate)
        {
            if (_state != BrowserPreparedWorkspaceActivationState.Pending)
            {
                throw new InvalidOperationException(
                    "A prepared Browser Workspace can publish only once.");
            }
            _state = BrowserPreparedWorkspaceActivationState.Publishing;
        }

        BrowserWorkspaceRealizationCutoverResult result;
        try
        {
            result = _host.CutOver(_candidate);
        }
        catch
        {
            lock (_gate)
            {
                _state =
                    BrowserPreparedWorkspaceActivationState.PublishRejected;
            }
            throw;
        }
        lock (_gate)
        {
            _state =
                result is BrowserWorkspaceRealizationCutoverResult.Activated
                    ? BrowserPreparedWorkspaceActivationState.Published
                    : BrowserPreparedWorkspaceActivationState.PublishRejected;
        }
        return result;
    }

    internal async ValueTask<BrowserRetainedWorkspaceNonInstallResult>
        SettleAsync()
    {
        bool abandon;
        lock (_gate)
        {
            abandon = _state
                == BrowserPreparedWorkspaceActivationState.Pending;
            if (!abandon
                && _state
                    != BrowserPreparedWorkspaceActivationState.PublishRejected)
            {
                throw new InvalidOperationException(
                    "A prepared Browser Workspace can settle only once and cannot settle after publication.");
            }
            _state = BrowserPreparedWorkspaceActivationState.Settling;
        }

        if (abandon)
        {
            BrowserWorkspaceRealizationCandidateRetirementResult retirement =
                _host.AbandonCandidate(_candidate);
            if (retirement
                is BrowserWorkspaceRealizationCandidateRetirementResult
                    .Rejected rejected
                && rejected.Reason
                    != WorkspaceRealizationCandidateRejection.StaleCandidate)
            {
                lock (_gate)
                    _state = BrowserPreparedWorkspaceActivationState.Settled;
                return new(
                    Settlement: null,
                    new CompleteRestorationFailure.CleanupFailed(
                        $"The unpublished Browser Workspace could not retire: "
                            + $"{rejected.Reason}."));
            }
        }

        WorkspaceRealizationSettlement settlement =
            await _candidate.Settlement.ConfigureAwait(false);
        lock (_gate)
            _state = BrowserPreparedWorkspaceActivationState.Settled;
        return settlement.Succeeded
            ? new(settlement, Failure: null)
            : new(
                settlement,
                new CompleteRestorationFailure.CleanupFailed(
                    "The unpublished Browser Workspace could not be settled "
                        + $"after {settlement.Reason}."));
    }

    enum BrowserPreparedWorkspaceActivationState
    {
        Pending,
        Publishing,
        PublishRejected,
        Published,
        Settling,
        Settled,
    }
}

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

    internal BrowserRetainedWorkspaceActivationSession BeginActivation(
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
                return BrowserRetainedWorkspaceActivationSession.Completed(
                    new BrowserRetainedWorkspacePreparationResult.Failed(
                        HostFailure(
                            "The retained Workspace activation owner is closed.")),
                    new BrowserRetainedWorkspaceActivationResult.Failed(
                        HostFailure(
                            "The retained Workspace activation owner is closed.")));
            }
            if (_deactivating)
            {
                CompleteRestorationFailure failure = HostFailure(
                    "The active Browser Workspace is still draining.");
                return BrowserRetainedWorkspaceActivationSession.Completed(
                    new BrowserRetainedWorkspacePreparationResult.Failed(
                        failure),
                    new BrowserRetainedWorkspaceActivationResult.Failed(
                        failure));
            }
            if (_cleanupFailed)
            {
                var failure = new CompleteRestorationFailure.CleanupFailed(
                    "A prior Browser Workspace failed to settle.");
                return BrowserRetainedWorkspaceActivationSession.Completed(
                    new BrowserRetainedWorkspacePreparationResult.Failed(
                        failure),
                    new BrowserRetainedWorkspaceActivationResult.Failed(
                        failure));
            }

            _latestIntent?.Supersede();
            if (_active?.RetainedDefinitionId == request.RetainedDefinitionId)
            {
                _latestIntent = null;
                var noEffect =
                    new BrowserRetainedWorkspaceActivationResult.NoEffect(
                        _active);
                return BrowserRetainedWorkspaceActivationSession.Completed(
                    new BrowserRetainedWorkspacePreparationResult.NoEffect(
                        _active),
                    noEffect);
            }

            intent = new(this, request);
            _latestIntent = intent;
        }

        Task<BrowserRetainedWorkspaceActivationResult> completion =
            RunActivationAsync(intent, cancellationToken);
        return new BrowserRetainedWorkspaceActivationSession(
            intent,
            completion);
    }

    internal async Task<BrowserRetainedWorkspaceActivationResult> ActivateAsync(
        BrowserRetainedWorkspaceActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        BrowserRetainedWorkspaceActivationSession session =
            BeginActivation(request, cancellationToken);
        if (await session.Preparation.ConfigureAwait(false)
            is BrowserRetainedWorkspacePreparationResult.Prepared)
        {
            _ = session.Commit();
        }
        return await session.Completion.ConfigureAwait(false);
    }

    private async Task<BrowserRetainedWorkspaceActivationResult>
        RunActivationAsync(
            BrowserRetainedWorkspaceActivationIntent intent,
            CancellationToken cancellationToken)
    {
        using var cancellationRegistration = cancellationToken.Register(
            static state =>
                ((BrowserRetainedWorkspaceActivationIntent)state!).Cancel(),
            intent);
        BrowserPreparedWorkspaceActivation? preparedActivation = null;
        try
        {
            CompleteRestorationResult<BrowserPreparedWorkspaceActivation>
                result = await RestoreAsync(intent, cancellationToken)
                    .ConfigureAwait(false);
            BrowserRetainedWorkspaceActivationResult activation;
            if (result
                is CompleteRestorationResult<
                    BrowserPreparedWorkspaceActivation>.Activated activated)
            {
                preparedActivation = activated.Activation;
                activation = await PrepareAndCommitAsync(
                        intent,
                        activated,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                activation = result switch
                {
                    CompleteRestorationResult<
                            BrowserPreparedWorkspaceActivation>.Superseded =>
                        new BrowserRetainedWorkspaceActivationResult
                            .Superseded(),
                    CompleteRestorationResult<
                            BrowserPreparedWorkspaceActivation>.Failed failed =>
                        new BrowserRetainedWorkspaceActivationResult.Failed(
                            failed.Failure),
                    _ => throw new InvalidOperationException(
                        "Complete restoration returned an unsupported Browser outcome."),
                };
            }
            intent.CompletePreparation(activation);
            return activation;
        }
        catch (Exception failure)
        {
            if (preparedActivation is { IsPending: true })
            {
                BrowserRetainedWorkspaceNonInstallResult cleanup =
                    await preparedActivation.SettleAsync()
                        .ConfigureAwait(false);
                if (cleanup.Failure is not null)
                {
                    var combined = new AggregateException(
                        failure,
                        new InvalidOperationException(
                            cleanup.Failure.Message));
                    intent.FailPreparation(combined);
                    throw combined;
                }
            }
            intent.FailPreparation(failure);
            throw;
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
    }

    async Task<BrowserRetainedWorkspaceActivationResult>
        PrepareAndCommitAsync(
            BrowserRetainedWorkspaceActivationIntent intent,
            CompleteRestorationResult<
                BrowserPreparedWorkspaceActivation>.Activated activated,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BrowserPreparedWorkspaceActivation prepared = activated.Activation;
        BrowserRetainedWorkspacePreparedInstallation installation;
        try
        {
            installation = BrowserCompleteRestorationHost.PrepareInstallation(
                intent.Request,
                prepared.Workspace);
        }
        catch (Exception failure)
        {
            BrowserRetainedWorkspaceNonInstallResult cleanup =
                await prepared.SettleAsync().ConfigureAwait(false);
            return new BrowserRetainedWorkspaceActivationResult.Failed(
                cleanup.Failure
                    ?? HostFailure(
                        $"Browser Workspace presentation could not be prepared: "
                            + $"{failure.Message}"));
        }

        if (!intent.PublishPrepared(installation)
            || !await intent.WaitForCommitAsync().ConfigureAwait(false))
        {
            BrowserRetainedWorkspaceNonInstallResult cleanup =
                await prepared.SettleAsync().ConfigureAwait(false);
            if (cleanup.Failure is not null)
            {
                return new BrowserRetainedWorkspaceActivationResult.Failed(
                    cleanup.Failure);
            }
            return intent.Status switch
            {
                CompleteRestorationIntentStatus.Cancelled =>
                    new BrowserRetainedWorkspaceActivationResult.Failed(
                        HostFailure(
                            "Browser Workspace activation was cancelled.")),
                _ => new BrowserRetainedWorkspaceActivationResult
                    .Superseded(),
            };
        }

        BrowserRetainedWorkspaceCommitResult commit =
            intent.TryCommit(
                prepared.Workspace,
                installation,
                prepared);
        if (commit
            is BrowserRetainedWorkspaceCommitResult.Committed committed)
        {
            return new BrowserRetainedWorkspaceActivationResult.Activated(
                committed.Installation);
        }

        BrowserRetainedWorkspaceNonInstallResult settlement =
            await prepared.SettleAsync().ConfigureAwait(false);
        if (settlement.Failure is not null)
        {
            return new BrowserRetainedWorkspaceActivationResult.Failed(
                settlement.Failure);
        }
        return commit switch
        {
            BrowserRetainedWorkspaceCommitResult.Superseded =>
                new BrowserRetainedWorkspaceActivationResult.Superseded(),
            BrowserRetainedWorkspaceCommitResult.Rejected rejected =>
                new BrowserRetainedWorkspaceActivationResult.Failed(
                    HostFailure(
                        $"Browser Workspace activation was rejected: "
                            + $"{rejected.Reason}.")),
            _ => throw new InvalidOperationException(
                "The Browser Workspace commit returned an unsupported result."),
        };
    }

    internal async ValueTask<WorkspaceRealizationOperationAdmission>
        EnterOperationAsync(
            string retainedDefinitionId,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        BrowserWorkspaceRealizationHost host;
        InspectionWorkspaceIdentity expectedRealization;
        BrowserRetainedWorkspaceInstallation expectedInstallation;
        lock (_gate)
        {
            if (_active?.RetainedDefinitionId != retainedDefinitionId)
            {
                return new WorkspaceRealizationOperationAdmission.Unavailable(
                    WorkspaceRealizationOperationUnavailableReason.NoActiveRealization);
            }
            host = _host;
            expectedInstallation = _active;
            expectedRealization = expectedInstallation.Realization;
        }
        WorkspaceRealizationOperationAdmission admission =
            await host.EnterOperationAsync(
                    expectedRealization,
                    cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            return admission;
        }

        lock (_gate)
        {
            if (ReferenceEquals(_active, expectedInstallation))
                return admission;
        }
        admitted.Lease.Dispose();
        return new WorkspaceRealizationOperationAdmission.Unavailable(
            WorkspaceRealizationOperationUnavailableReason.NoActiveRealization);
    }

    internal async Task<BrowserRetainedWorkspacePackageActivationResult>
        ActivatePackageAsync(
            string retainedDefinitionId,
            string realizationId,
            string navigationId,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(realizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(navigationId);
        WorkspaceRealizationOperationAdmission admission =
            await EnterOperationAsync(
                    retainedDefinitionId,
                    cancellationToken)
                .ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            return new BrowserRetainedWorkspacePackageActivationResult
                .Superseded();
        }

        using WorkspaceRealizationOperationLease operation = admitted.Lease;
        lock (_gate)
        {
            if (_active is not { } active
                || active.RetainedDefinitionId != retainedDefinitionId
                || active.RealizationId != realizationId
                || !ReferenceEquals(
                    active.Realization,
                    operation.Realization))
            {
                return new BrowserRetainedWorkspacePackageActivationResult
                    .Superseded();
            }

            BrowserRetainedWorkspacePackage? package =
                active.Packages.FirstOrDefault(candidate =>
                    candidate.Kind == "package"
                    && candidate.NavigationId == navigationId);
            return package is null
                ? new BrowserRetainedWorkspacePackageActivationResult
                    .Superseded()
                : new BrowserRetainedWorkspacePackageActivationResult
                    .Activated(package);
        }
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
        WorkspaceRealizationSettlement? failedSettlement =
            report.Capacity.FailedSettlements.FirstOrDefault();
        lock (_gate)
        {
            _deactivating = false;
            if (failedSettlement is null && !_closing)
                _host = _hostFactory();
            else if (failedSettlement is not null)
                _cleanupFailed = true;
        }
        return failedSettlement is null
            ? new BrowserRetainedWorkspaceDeactivationResult.Deactivated(
                settlement)
            : new BrowserRetainedWorkspaceDeactivationResult.CleanupFailed(
                failedSettlement);
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

    internal BrowserSpotlightRetainedWorkspaceAdmissionResult<
        BrowserSpotlightRetainedWorkspaceHostAuthority,
        BrowserRetainedWorkspaceActivationRejection>
        AdmitSpotlightActivation(
            string sourceRetainedDefinitionId,
            BrowserSpotlightFreshWorkspaceAuthority authority,
            BrowserRetainedWorkspaceActivationRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            sourceRetainedDefinitionId);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(request);

        lock (_gate)
        {
            string? rejection = CurrentAdmissionRejection();
            if (rejection is not null)
            {
                return new BrowserSpotlightRetainedWorkspaceAdmissionResult<
                    BrowserSpotlightRetainedWorkspaceHostAuthority,
                    BrowserRetainedWorkspaceActivationRejection>.Rejected(
                        new(rejection));
            }
            if (_active is not { } source
                || source.RetainedDefinitionId
                    != sourceRetainedDefinitionId
                || !ReferenceEquals(
                    source.Realization,
                    authority.SourceWorkspace))
            {
                return new BrowserSpotlightRetainedWorkspaceAdmissionResult<
                    BrowserSpotlightRetainedWorkspaceHostAuthority,
                    BrowserRetainedWorkspaceActivationRejection>.Rejected(
                        new("The captured Spotlight source Workspace is no longer active."));
            }
            if (request.RetainedDefinitionId
                == sourceRetainedDefinitionId)
            {
                return new BrowserSpotlightRetainedWorkspaceAdmissionResult<
                    BrowserSpotlightRetainedWorkspaceHostAuthority,
                    BrowserRetainedWorkspaceActivationRejection>.Rejected(
                        new("Fresh Spotlight activation requires a distinct retained Workspace definition."));
            }

            _latestIntent?.Supersede();
            var intent = new BrowserRetainedWorkspaceActivationIntent(
                this,
                request);
            _latestIntent = intent;
            return new BrowserSpotlightRetainedWorkspaceAdmissionResult<
                BrowserSpotlightRetainedWorkspaceHostAuthority,
                BrowserRetainedWorkspaceActivationRejection>.Admitted(
                    new(this, intent, source, authority));
        }
    }

    internal async ValueTask<
        BrowserSpotlightWorkspaceRestorationResult<
            BrowserPreparedWorkspaceActivation,
            CompleteRestorationFailure>>
        RestoreSpotlightAsync(
            BrowserSpotlightRetainedWorkspaceHostAuthority authority,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!ReferenceEquals(authority.Owner, this))
            throw new ArgumentException(
                "The Spotlight authority belongs to a different retained owner.",
                nameof(authority));

        CompleteRestorationResult<BrowserPreparedWorkspaceActivation> result =
            await RestoreAsync(authority.Intent, cancellationToken)
                .ConfigureAwait(false);
        return result switch
        {
            CompleteRestorationResult<
                    BrowserPreparedWorkspaceActivation>.Activated activated =>
                new BrowserSpotlightWorkspaceRestorationResult<
                    BrowserPreparedWorkspaceActivation,
                    CompleteRestorationFailure>.Complete(
                        activated.Activation),
            CompleteRestorationResult<
                    BrowserPreparedWorkspaceActivation>.Failed failed =>
                new BrowserSpotlightWorkspaceRestorationResult<
                    BrowserPreparedWorkspaceActivation,
                    CompleteRestorationFailure>.Failed(failed.Failure),
            CompleteRestorationResult<
                    BrowserPreparedWorkspaceActivation>.Superseded =>
                new BrowserSpotlightWorkspaceRestorationResult<
                    BrowserPreparedWorkspaceActivation,
                    CompleteRestorationFailure>.Failed(
                        HostFailure(
                            "The Spotlight retained Workspace activation was superseded.")),
            _ => throw new InvalidOperationException(
                "Complete restoration returned an unsupported Spotlight outcome."),
        };
    }

    internal BrowserSpotlightRetainedWorkspacePublicationResult<
        BrowserRetainedWorkspaceInstallation,
        BrowserRetainedWorkspaceActivationRejection>
        PublishSpotlightActivation(
            BrowserSpotlightRetainedWorkspacePublicationRequest<
                BrowserSpotlightRetainedWorkspaceHostAuthority,
                BrowserPreparedWorkspaceActivation> request)
    {
        ArgumentNullException.ThrowIfNull(request);
        BrowserSpotlightRetainedWorkspaceHostAuthority authority =
            request.HostAuthority;
        if (!ReferenceEquals(authority.Owner, this)
            || !ReferenceEquals(authority.Authority, request.Authority))
        {
            return RejectedSpotlight(
                "The Spotlight publication authority is not current.");
        }

        BrowserRetainedWorkspaceCommitResult commit = TryCommit(
            authority.Intent,
            request.Activation.Workspace,
            request.Activation,
            authority.SourceInstallation);
        return commit switch
        {
            BrowserRetainedWorkspaceCommitResult.Committed committed =>
                new BrowserSpotlightRetainedWorkspacePublicationResult<
                    BrowserRetainedWorkspaceInstallation,
                    BrowserRetainedWorkspaceActivationRejection>.Published(
                        committed.Installation),
            BrowserRetainedWorkspaceCommitResult.Superseded =>
                RejectedSpotlight(
                    "The Spotlight retained Workspace activation is no longer current."),
            BrowserRetainedWorkspaceCommitResult.Rejected rejected =>
                RejectedSpotlight(
                    $"The Spotlight Workspace cutover was rejected: "
                        + $"{rejected.Reason}."),
            _ => throw new InvalidOperationException(
                "The retained Workspace commit returned an unsupported result."),
        };
    }

    internal void CompleteSpotlightActivation(
        BrowserSpotlightRetainedWorkspaceHostAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        if (!ReferenceEquals(authority.Owner, this))
            return;
        lock (_gate)
        {
            if (ReferenceEquals(_latestIntent, authority.Intent))
                _latestIntent = null;
        }
        authority.Intent.Dispose();
    }

    internal BrowserRetainedWorkspaceCommitResult TryCommit(
        BrowserRetainedWorkspaceActivationIntent intent,
        CompleteWorkspaceActivation workspace,
        BrowserRetainedWorkspacePreparedInstallation prepared,
        BrowserPreparedWorkspaceActivation activation)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(prepared);
        ArgumentNullException.ThrowIfNull(activation);
        BrowserRetainedWorkspaceInstallationDraft exact =
            BrowserRetainedWorkspaceInstallationDraft.Create(
                intent.Request,
                workspace);
        return TryCommit(
            intent,
            activation,
            requiredIncumbent: null,
            (activated, realizationId, publicationOrdinal, predecessor) =>
                new(
                    exact.RetainedDefinitionId,
                    exact.Label,
                    exact.CanonicalLocation,
                    exact.RestorationRequest,
                    exact.Projection,
                    realizationId,
                    activated.Realization.Identity,
                    publicationOrdinal,
                    prepared.Definition,
                    prepared.Packages,
                    prepared.Navigation,
                    predecessor));
    }

    internal BrowserRetainedWorkspaceCommitResult TryCommit(
        BrowserRetainedWorkspaceActivationIntent intent,
        CompleteWorkspaceActivation workspace,
        BrowserPreparedWorkspaceActivation prepared,
        BrowserRetainedWorkspaceInstallation? requiredIncumbent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(prepared);
        BrowserRetainedWorkspaceInstallationDraft draft =
            BrowserRetainedWorkspaceInstallationDraft.Create(
                intent.Request,
                workspace);
        return TryCommit(
            intent,
            prepared,
            requiredIncumbent,
            (activated, realizationId, publicationOrdinal, predecessor) =>
                draft.Install(
                    activated.Realization.Identity,
                    realizationId,
                    publicationOrdinal,
                    predecessor));
    }

    BrowserRetainedWorkspaceCommitResult TryCommit(
        BrowserRetainedWorkspaceActivationIntent intent,
        BrowserPreparedWorkspaceActivation prepared,
        BrowserRetainedWorkspaceInstallation? requiredIncumbent,
        Func<
            BrowserWorkspaceRealizationCutoverResult.Activated,
            string,
            long,
            BrowserRetainedWorkspacePredecessor?,
            BrowserRetainedWorkspaceInstallation> install)
    {
        lock (_gate)
        {
            if (_closing
                || !ReferenceEquals(_latestIntent, intent)
                || intent.Status != CompleteRestorationIntentStatus.Current
                || (requiredIncumbent is not null
                    && !ReferenceEquals(_active, requiredIncumbent)))
            {
                return new BrowserRetainedWorkspaceCommitResult.Superseded();
            }

            BrowserWorkspaceRealizationCutoverResult result =
                prepared.CutOver();
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

            long publicationOrdinal = ++_nextRealization;
            BrowserRetainedWorkspaceInstallation installation = install(
                activated,
                $"workspace-realization-{publicationOrdinal}",
                publicationOrdinal,
                predecessor);
            _active = installation;
            return new BrowserRetainedWorkspaceCommitResult.Committed(
                activated,
                installation);
        }
    }

    async Task<CompleteRestorationResult<BrowserPreparedWorkspaceActivation>>
        RestoreAsync(
            BrowserRetainedWorkspaceActivationIntent intent,
            CancellationToken cancellationToken)
    {
        CompleteRestorationPreparationResult preparation =
            intent.Request.RestorationRequest switch
            {
                CompleteRestorationRequestBasis.PacketInput packet =>
                    CompleteRestorationPreparation.FromPacket(
                        packet.Encoded,
                        intent,
                        cancellationToken),
                CompleteRestorationRequestBasis.DefinitionInput definition =>
                    CompleteRestorationPreparation.FromDefinition(
                        definition,
                        intent,
                        cancellationToken),
                _ => throw new InvalidOperationException(
                    "Unknown retained Workspace restoration source."),
            };
        var host = new BrowserCompleteRestorationHost(_host, intent);
        return await CompleteRestorationCoordinator.RestoreAsync(
                    preparation,
                    intent,
                    host,
                    _optionsFactory(),
                    cancellationToken)
                .ConfigureAwait(false);
    }

    async Task<BrowserRetainedWorkspaceActivationResult> CommitPreparedAsync(
        BrowserRetainedWorkspaceActivationIntent intent,
        CompleteRestorationResult<
            BrowserPreparedWorkspaceActivation>.Activated activated,
        BrowserRetainedWorkspaceInstallation? requiredIncumbent)
    {
        if (!ReferenceEquals(
                activated.Workspace.Intent,
                intent.Identity)
            || !ReferenceEquals(
                activated.Activation.Workspace,
                activated.Workspace))
        {
            throw new InvalidOperationException(
                "Browser activation returned a different restoration intent or Workspace.");
        }

        BrowserRetainedWorkspaceCommitResult commit = TryCommit(
            intent,
            activated.Workspace,
            activated.Activation,
            requiredIncumbent);
        if (commit
            is BrowserRetainedWorkspaceCommitResult.Committed committed)
        {
            return new BrowserRetainedWorkspaceActivationResult.Activated(
                committed.Installation);
        }

        BrowserRetainedWorkspaceNonInstallResult cleanup =
            await activated.Activation.SettleAsync().ConfigureAwait(false);
        if (cleanup.Failure is not null)
        {
            return new BrowserRetainedWorkspaceActivationResult.Failed(
                cleanup.Failure);
        }
        return commit switch
        {
            BrowserRetainedWorkspaceCommitResult.Superseded =>
                new BrowserRetainedWorkspaceActivationResult.Superseded(),
            BrowserRetainedWorkspaceCommitResult.Rejected rejected =>
                new BrowserRetainedWorkspaceActivationResult.Failed(
                    HostFailure(
                        $"Browser Workspace activation was rejected: "
                            + $"{rejected.Reason}.")),
            _ => throw new InvalidOperationException(
                "The retained Workspace commit returned an unsupported result."),
        };
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

    string? CurrentAdmissionRejection()
    {
        if (_closing)
            return "The retained Workspace activation owner is closed.";
        if (_deactivating)
            return "The active Browser Workspace is still draining.";
        if (_cleanupFailed)
            return "A prior Browser Workspace failed to settle.";
        return null;
    }

    static BrowserSpotlightRetainedWorkspacePublicationResult<
        BrowserRetainedWorkspaceInstallation,
        BrowserRetainedWorkspaceActivationRejection>
        RejectedSpotlight(string message) =>
        new BrowserSpotlightRetainedWorkspacePublicationResult<
            BrowserRetainedWorkspaceInstallation,
            BrowserRetainedWorkspaceActivationRejection>.Rejected(
                new(message));

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
internal sealed class BrowserRetainedWorkspaceActivationSession
{
    readonly BrowserRetainedWorkspaceActivationIntent? _intent;

    internal BrowserRetainedWorkspaceActivationSession(
        BrowserRetainedWorkspaceActivationIntent intent,
        Task<BrowserRetainedWorkspaceActivationResult> completion)
    {
        _intent = intent ?? throw new ArgumentNullException(nameof(intent));
        Completion = completion
            ?? throw new ArgumentNullException(nameof(completion));
        Preparation = intent.Preparation;
    }

    BrowserRetainedWorkspaceActivationSession(
        BrowserRetainedWorkspacePreparationResult preparation,
        BrowserRetainedWorkspaceActivationResult completion)
    {
        Preparation = Task.FromResult(preparation);
        Completion = Task.FromResult(completion);
    }

    internal Task<BrowserRetainedWorkspacePreparationResult> Preparation
        { get; }

    internal Task<BrowserRetainedWorkspaceActivationResult> Completion
        { get; }

    internal bool Commit() => _intent?.Commit() ?? false;

    internal void Cancel() => _intent?.Cancel();

    internal void Supersede() => _intent?.Supersede();

    internal static BrowserRetainedWorkspaceActivationSession Completed(
        BrowserRetainedWorkspacePreparationResult preparation,
        BrowserRetainedWorkspaceActivationResult completion) =>
        new(preparation, completion);
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserRetainedWorkspaceActivationIntent(
    BrowserRetainedWorkspaceActivationOwner owner,
    BrowserRetainedWorkspaceActivationRequest request)
    : ICompleteRestorationIntentAuthority, IDisposable
{
    readonly CancellationTokenSource _revocation = new();
    readonly TaskCompletionSource<BrowserRetainedWorkspacePreparationResult>
        _preparation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource<bool> _commit =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    int _status = (int)CompleteRestorationIntentStatus.Current;

    public CompleteRestorationIntentIdentity Identity { get; } = new();

    public CompleteRestorationIntentStatus Status =>
        (CompleteRestorationIntentStatus)Volatile.Read(ref _status);

    public CancellationToken Revocation => _revocation.Token;

    internal BrowserRetainedWorkspaceActivationRequest Request { get; } =
        request;

    internal Task<BrowserRetainedWorkspacePreparationResult> Preparation =>
        _preparation.Task;

    internal BrowserRetainedWorkspaceInstallation? CommittedInstallation
    {
        get;
        private set;
    }

    internal bool PublishPrepared(
        BrowserRetainedWorkspacePreparedInstallation installation)
    {
        ArgumentNullException.ThrowIfNull(installation);
        if (Status != CompleteRestorationIntentStatus.Current)
            return false;
        return _preparation.TrySetResult(
            new BrowserRetainedWorkspacePreparationResult.Prepared(
                installation));
    }

    internal Task<bool> WaitForCommitAsync() => _commit.Task;

    internal bool Commit()
    {
        if (Status != CompleteRestorationIntentStatus.Current)
            return false;
        return _commit.TrySetResult(true);
    }

    internal void CompletePreparation(
        BrowserRetainedWorkspaceActivationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _ = result switch
        {
            BrowserRetainedWorkspaceActivationResult.NoEffect noEffect =>
                _preparation.TrySetResult(
                    new BrowserRetainedWorkspacePreparationResult.NoEffect(
                        noEffect.Installation)),
            BrowserRetainedWorkspaceActivationResult.Superseded =>
                _preparation.TrySetResult(
                    new BrowserRetainedWorkspacePreparationResult.Superseded()),
            BrowserRetainedWorkspaceActivationResult.Failed failed =>
                _preparation.TrySetResult(
                    new BrowserRetainedWorkspacePreparationResult.Failed(
                        failed.Failure)),
            BrowserRetainedWorkspaceActivationResult.Activated =>
                true,
            _ => throw new InvalidOperationException(
                "Unknown Browser retained Workspace activation result."),
        };
    }

    internal void FailPreparation(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _preparation.TrySetException(failure);
    }

    internal BrowserRetainedWorkspaceCommitResult TryCommit(
        CompleteWorkspaceActivation workspace,
        BrowserRetainedWorkspacePreparedInstallation prepared,
        BrowserPreparedWorkspaceActivation activation)
    {
        BrowserRetainedWorkspaceCommitResult result =
            owner.TryCommit(this, workspace, prepared, activation);
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
            _commit.TrySetResult(false);
        }
    }

    public void Dispose() => _revocation.Dispose();
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserCompleteRestorationHost(
    BrowserWorkspaceRealizationHost host,
    BrowserRetainedWorkspaceActivationIntent intent)
    : ICompleteRestorationHost<BrowserPreparedWorkspaceActivation>
{
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
                        BrowserPreparedWorkspaceActivation>.Superseded(),
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
                    BrowserPreparedWorkspaceActivation>.Failed(cleanup);
            }
            return workspacePreparation switch
            {
                CompleteWorkspacePreparationResult.Failed failed =>
                    new CompleteRestorationHostResult<
                        BrowserPreparedWorkspaceActivation>.Failed(
                            failed.Failure),
                CompleteWorkspacePreparationResult.Superseded =>
                    new CompleteRestorationHostResult<
                        BrowserPreparedWorkspaceActivation>.Superseded(),
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
                    BrowserPreparedWorkspaceActivation>.Failed(cleanup);
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
                    BrowserPreparedWorkspaceActivation>.Failed(cleanup);
        }

        return new CompleteRestorationHostResult<
            BrowserPreparedWorkspaceActivation>.Activated(
                new(
                    host,
                    prepared.Candidate,
                    preparedWorkspace.Activation),
                preparedWorkspace.Activation);
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

    internal static BrowserRetainedWorkspacePreparedInstallation
        PrepareInstallation(
        BrowserRetainedWorkspaceActivationRequest request,
        CompleteWorkspaceActivation workspace)
    {
        CompleteRestorationProjection.Projectable projection =
            workspace.Projection
                as CompleteRestorationProjection.Projectable
            ?? throw new InvalidOperationException(
                "Packet restoration must retain its canonical packet.");
        WorkspaceSharePacket packet = WorkspaceSharePacketCodec.Decode(
            projection.CanonicalPacket);
        int? activeTabIndex =
            request.PresentationActiveTabIndex
            ?? packet.FocusedTabIndex;
        int? selectedContextIndex = packet.SelectedContextIndex;
        if (activeTabIndex is < 0
            || activeTabIndex >= packet.Tabs.Count
            || selectedContextIndex is < 0
            || selectedContextIndex >= packet.Contexts.Count)
        {
            throw new InvalidOperationException(
                "Browser Workspace installation selected an unavailable tab or context.");
        }

        ImmutableArray<BrowserRetainedWorkspaceDefinitionTab> tabs =
        [
            .. packet.Tabs.Select(
                static (tab, index) =>
                    new BrowserRetainedWorkspaceDefinitionTab(
                        $"t{index}",
                        tab.SourceKind == WorkspaceShareSourceKind.Package
                            ? "package"
                            : "group",
                        tab.Source,
                        tab.Version,
                        tab.Framework,
                        tab.RuntimeIdentifier)),
        ];
        ImmutableArray<BrowserRetainedWorkspaceDefinitionContext> contexts =
        [
            .. packet.Contexts.Select(
                static (context, index) =>
                    new BrowserRetainedWorkspaceDefinitionContext(
                        $"g{index}",
                        [
                            .. context.TabIndexes.Select(
                                static tabIndex => $"t{tabIndex}"),
                        ])),
        ];
        (ImmutableArray<CompleteRestorationResolvedViewState> states,
            int? activeStateIndex) =
            workspace.Snapshot.Resolved switch
            {
                CompleteRestorationResolvedState.Version2 version2 =>
                    (version2.States, version2.ActiveStateIndex),
                CompleteRestorationResolvedState.Version3 version3 =>
                    (version3.States, version3.ActiveStateIndex),
                CompleteRestorationResolvedState.Version4 version4 =>
                    (version4.States, version4.ActiveStateIndex),
                _ => throw new InvalidOperationException(
                    "Unknown complete restoration resolved state."),
            };
        var packages = new Dictionary<
            string,
            BrowserRetainedWorkspacePackage>(StringComparer.Ordinal);
        foreach (CompleteRestorationResolvedViewState state in states)
        {
            if (state.NavigationId is not { } navigationId
                || state.Package is not { } package)
            {
                continue;
            }
            packages.Add(
                navigationId,
                new(
                    navigationId,
                    "package",
                    BrowserPackageSurfaceProjection.Project(package)));
        }
        foreach (CompleteRestorationPlatformEvaluation platform
            in workspace.Snapshot.Platforms.Where(
                static platform => platform.Family == "runtime"))
        {
            packages.Add(
                platform.NavigationId,
                new(
                    platform.NavigationId,
                    "platform",
                    BrowserPlatformSurfaceProjection.Project(platform)));
        }
        foreach (BrowserRetainedWorkspaceDefinitionTab tab in tabs)
        {
            if ((tab.Kind == "package"
                    || (tab.Kind == "group"
                        && tab.Source == ":Platform"))
                && !packages.ContainsKey(tab.Id))
            {
                throw new InvalidOperationException(
                    $"Browser Workspace tab '{tab.Id}' has no exact presentation.");
            }
        }

        return new(
            projection.CanonicalPacket,
            new(
                tabs,
                contexts,
                activeTabIndex is int tabIndex
                    ? tabs[tabIndex].Id
                    : null,
                selectedContextIndex is int contextIndex
                    ? contexts[contextIndex].Id
                    : null),
            [.. tabs.Select(tab => packages.GetValueOrDefault(tab.Id))
                .OfType<BrowserRetainedWorkspacePackage>()],
            new BrowserRetainedWorkspaceNavigationState(
                activeStateIndex,
                [
                    .. states.Select(
                        static state =>
                            new BrowserRetainedWorkspaceViewState(
                                state.NavigationId,
                                state.Definition.Subject?.Kind.ToString(),
                                state.Definition.Facet)),
                ]));
    }

    static CompleteRestorationHostResult<
        BrowserPreparedWorkspaceActivation>.Failed Failed(string message) =>
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
            IncludePlatformPresentation = true,
        };
    }
}
