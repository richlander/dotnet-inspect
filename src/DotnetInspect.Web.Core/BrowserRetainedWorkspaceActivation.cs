using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using NuGetFetch;

namespace DotnetInspect.Web;

internal sealed record BrowserRetainedWorkspaceActivationRequest(
    string RetainedDefinitionId,
    string Label,
    string CanonicalLocation,
    CompleteRestorationRequestBasis RestorationRequest,
    IReadOnlyDictionary<string, PackageSourceCredential> PackageSourceCredentials)
{
    internal BrowserRetainedWorkspaceActivationRequest(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket)
        : this(
            retainedDefinitionId,
            label,
            canonicalLocation,
            new CompleteRestorationRequestBasis.PacketInput(
                RequireText(canonicalPacket, nameof(canonicalPacket))),
            new ReadOnlyDictionary<string, PackageSourceCredential>(
                new Dictionary<string, PackageSourceCredential>(
                    StringComparer.Ordinal)))
    {
    }

    internal BrowserRetainedWorkspaceActivationRequest(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        CompleteRestorationRequestBasis restorationRequest)
        : this(
            retainedDefinitionId,
            label,
            canonicalLocation,
            restorationRequest,
            new ReadOnlyDictionary<string, PackageSourceCredential>(
                new Dictionary<string, PackageSourceCredential>(
                    StringComparer.Ordinal)))
    {
    }

    internal BrowserRetainedWorkspaceActivationRequest(
        string retainedDefinitionId,
        string label,
        string canonicalLocation,
        string canonicalPacket,
        IReadOnlyDictionary<string, PackageSourceCredential>
            packageSourceCredentials)
        : this(
            retainedDefinitionId,
            label,
            canonicalLocation,
            new CompleteRestorationRequestBasis.PacketInput(
                RequireText(canonicalPacket, nameof(canonicalPacket))),
            FreezeCredentials(packageSourceCredentials))
    {
    }

    internal string RetainedDefinitionId { get; } =
        RequireText(RetainedDefinitionId, nameof(RetainedDefinitionId));

    internal string Label { get; } = RequireText(Label, nameof(Label));

    internal string CanonicalLocation { get; } =
        RequireText(CanonicalLocation, nameof(CanonicalLocation));

    internal CompleteRestorationRequestBasis RestorationRequest { get; } =
        RestorationRequest
        ?? throw new ArgumentNullException(nameof(RestorationRequest));

    internal IReadOnlyDictionary<string, PackageSourceCredential>
        PackageSourceCredentials { get; } =
        FreezeCredentials(PackageSourceCredentials);

    public override string ToString() =>
        $"{nameof(BrowserRetainedWorkspaceActivationRequest)} {{ "
        + $"{nameof(RetainedDefinitionId)} = {RetainedDefinitionId}, "
        + $"{nameof(Label)} = {Label}, "
        + $"{nameof(CanonicalLocation)} = {CanonicalLocation}, "
        + $"{nameof(RestorationRequest)} = {RestorationRequest}, "
        + $"{nameof(PackageSourceCredentials)} = <redacted> }}";

    static string RequireText(string value, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }

    static IReadOnlyDictionary<string, PackageSourceCredential>
        FreezeCredentials(
        IReadOnlyDictionary<string, PackageSourceCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var snapshot = new Dictionary<
            string,
            PackageSourceCredential>(StringComparer.Ordinal);
        foreach ((string endpoint, PackageSourceCredential credential)
            in credentials)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
            ArgumentNullException.ThrowIfNull(credential);
            ArgumentException.ThrowIfNullOrWhiteSpace(credential.Username);
            if (string.IsNullOrEmpty(credential.Password))
            {
                throw new ArgumentException(
                    $"The PAT for Workspace source '{endpoint}' must not be empty.",
                    nameof(credentials));
            }
            snapshot.Add(
                endpoint,
                new PackageSourceCredential(
                    credential.Username,
                    credential.Password));
        }
        return new ReadOnlyDictionary<string, PackageSourceCredential>(
            snapshot);
    }
}

internal sealed record BrowserRetainedWorkspacePackagePresentation(
    string NavigationId,
    int ContextIndex,
    string ConsumerPackageSubjectId,
    BrowserPackageSurfaceInfo Surface);

internal sealed record BrowserRetainedWorkspacePlatformPresentation(
    string NavigationId,
    int ContextIndex,
    string Family,
    string? RuntimeIdentifier,
    BrowserPackageSurfaceInfo Surface);

internal abstract record BrowserRetainedWorkspaceAdmissionResult<T>
    where T : class
{
    private protected BrowserRetainedWorkspaceAdmissionResult() { }

    internal sealed record Admitted(T Presentation)
        : BrowserRetainedWorkspaceAdmissionResult<T>;

    internal sealed record Superseded
        : BrowserRetainedWorkspaceAdmissionResult<T>;

    internal sealed record Unavailable(string Message)
        : BrowserRetainedWorkspaceAdmissionResult<T>;
}

internal sealed record BrowserRetainedWorkspaceCleanupEvidence(string Message);

internal sealed record BrowserRetainedWorkspacePredecessor(
    string SettlementId,
    BrowserWorkspaceRealizationRetirement Retirement);

internal sealed record BrowserRetainedWorkspacePosting(
    string RetainedDefinitionId,
    string Label,
    string CanonicalLocation,
    CompleteRestorationRequestBasis RestorationRequest,
    CompleteRestorationProjection Projection,
    InspectionWorkspaceIdentity Realization,
    string RealizationId,
    long PublicationOrdinal,
    CommittedScenarioDefinitionSet Definition,
    NavigationConsumerResult Navigation,
    ImmutableArray<BrowserRetainedWorkspacePackagePresentation> Packages,
    ImmutableArray<BrowserRetainedWorkspacePlatformPresentation> Platforms,
    BrowserRetainedWorkspacePredecessor? Predecessor,
    BrowserRetainedWorkspaceCleanupEvidence? Cleanup,
    BrowserNavigationStateSlot NavigationState)
{
    internal string? CanonicalPacket =>
        Projection is CompleteRestorationProjection.Projectable projectable
            ? projectable.CanonicalPacket
            : null;
}

internal abstract record BrowserRetainedWorkspaceActivationResult
{
    private protected BrowserRetainedWorkspaceActivationResult() { }

    internal sealed record Activated(
        BrowserRetainedWorkspacePosting Posting)
        : BrowserRetainedWorkspaceActivationResult;

    internal sealed record NoEffect(
        BrowserRetainedWorkspacePosting Posting)
        : BrowserRetainedWorkspaceActivationResult;

    internal sealed record Superseded
        : BrowserRetainedWorkspaceActivationResult;

    internal sealed record Failed(CompleteRestorationFailure Failure)
        : BrowserRetainedWorkspaceActivationResult;
}

internal abstract record BrowserRetainedWorkspacePreparationResult
{
    private protected BrowserRetainedWorkspacePreparationResult() { }

    internal sealed record Prepared(
        BrowserRetainedWorkspacePostingDraft Posting)
        : BrowserRetainedWorkspacePreparationResult;

    internal sealed record NoEffect(
        BrowserRetainedWorkspacePosting Posting)
        : BrowserRetainedWorkspacePreparationResult;

    internal sealed record Superseded
        : BrowserRetainedWorkspacePreparationResult;

    internal sealed record Failed(CompleteRestorationFailure Failure)
        : BrowserRetainedWorkspacePreparationResult;
}

internal abstract record BrowserRetainedWorkspaceConsumerCompletionResult
{
    private protected BrowserRetainedWorkspaceConsumerCompletionResult() { }

    internal sealed record Completed(
        bool Succeeded,
        string? Failure)
        : BrowserRetainedWorkspaceConsumerCompletionResult;

    internal sealed record Unavailable(string Message)
        : BrowserRetainedWorkspaceConsumerCompletionResult;
}

internal abstract record BrowserRetainedWorkspaceDeactivationResult
{
    private protected BrowserRetainedWorkspaceDeactivationResult() { }

    internal sealed record Deactivated(
        string CompletionReceipt,
        WorkspaceRealizationSettlement Settlement)
        : BrowserRetainedWorkspaceDeactivationResult;

    internal sealed record CleanupFailed(
        string CompletionReceipt,
        WorkspaceRealizationSettlement Settlement,
        string? NavigationFailure = null)
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

internal sealed record BrowserRetainedWorkspaceNonPostingResult(
    WorkspaceRealizationSettlement? Settlement,
    CompleteRestorationFailure? Failure);

[SupportedOSPlatform("browser")]
internal sealed record BrowserRetainedWorkspacePostingDraft(
    string RetainedDefinitionId,
    string Label,
    string CanonicalLocation,
    CompleteRestorationRequestBasis RestorationRequest,
    CompleteRestorationProjection Projection,
    CommittedScenarioDefinitionSet Definition,
    NavigationOperationInitialization Navigation,
    ImmutableArray<BrowserRetainedWorkspacePackagePresentation> Packages,
    ImmutableArray<BrowserRetainedWorkspacePlatformPresentation> Platforms)
{
    internal static BrowserRetainedWorkspacePostingDraft Create(
        BrowserRetainedWorkspaceActivationRequest request,
        CompleteWorkspaceActivation workspace,
        CompleteRestorationReadyProjection ready)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(ready);
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
        if (workspace.Snapshot.Navigation.Result.Consumer.Authority is null)
        {
            throw new InvalidOperationException(
                "The completed Workspace did not issue initial Navigation "
                    + "effect authority.");
        }

        CommittedScenarioDefinitionSet definition =
            workspace.Snapshot.Resolved switch
            {
                CompleteRestorationResolvedState.Version2 version2 =>
                    version2.Definitions,
                CompleteRestorationResolvedState.Version3 version3 =>
                    version3.Definitions,
                CompleteRestorationResolvedState.Version4 version4 =>
                    version4.Definitions,
                CompleteRestorationResolvedState.Version5 version5 =>
                    version5.Definitions,
                _ => throw new InvalidOperationException(
                    "Unknown complete restoration resolved state."),
            };
        CompleteRestorationInventory inventory =
            workspace.Snapshot.Inventory
            ?? throw new InvalidOperationException(
                "Browser restoration requires the completed candidate inventory.");
        Dictionary<string, CompleteRestorationReadyPackage> readyPackages =
            ready.Packages.ToDictionary(
            static package => package.NavigationId,
            StringComparer.Ordinal);

        return new(
            request.RetainedDefinitionId,
            request.Label,
            request.CanonicalLocation,
            request.RestorationRequest,
            workspace.Projection,
            definition,
            workspace.Snapshot.Navigation,
            [
                .. inventory.Packages.Select(
                    package =>
                        new BrowserRetainedWorkspacePackagePresentation(
                            package.NavigationId,
                            package.ContextIndex,
                            readyPackages[package.NavigationId].ConsumerPackageSubjectId,
                            BrowserPackageSurfaceProjection.Project(
                                package,
                                BrowserPackage.ProjectIcon(
                                    PackageIconQuery.Execute(
                                        readyPackages[package.NavigationId].Binding.Root))))),
            ],
            [
                .. inventory.Platforms.Select(
                    static platform =>
                        new BrowserRetainedWorkspacePlatformPresentation(
                            platform.NavigationId,
                            platform.ContextIndex,
                            platform.Family,
                            platform.RuntimeIdentifier,
                            BrowserPlatformSurfaceProjection.Project(platform))),
            ]);
    }

    internal BrowserRetainedWorkspacePosting Publish(
        InspectionWorkspaceIdentity realization,
        string realizationId,
        long publicationOrdinal,
        BrowserRetainedWorkspacePredecessor? predecessor,
        BrowserRetainedWorkspaceCleanupEvidence? cleanup)
    {
        var navigationState = new BrowserNavigationStateSlot(
            Navigation,
            InspectionViewFacetCatalog.Registry);
        return new(
            RetainedDefinitionId,
            Label,
            CanonicalLocation,
            RestorationRequest,
            Projection,
            realization,
            realizationId,
            publicationOrdinal,
            Definition,
            navigationState.Initialization,
            Packages,
            Platforms,
            predecessor,
            cleanup,
            navigationState);
    }
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
        CompleteWorkspaceActivation workspace,
        BrowserRetainedWorkspacePostingDraft posting)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _candidate = candidate
            ?? throw new ArgumentNullException(nameof(candidate));
        Workspace = workspace
            ?? throw new ArgumentNullException(nameof(workspace));
        Posting = posting
            ?? throw new ArgumentNullException(nameof(posting));
        if (!ReferenceEquals(
                candidate.Realization,
                workspace.Workspace))
        {
            throw new ArgumentException(
                "The Browser candidate and complete Workspace must share one identity.",
                nameof(workspace));
        }
        if (!ReferenceEquals(
                posting.Navigation,
                workspace.Snapshot.Navigation))
        {
            throw new ArgumentException(
                "The Browser posting must retain the exact completed "
                    + "Navigation initialization.",
                nameof(posting));
        }
    }

    internal CompleteWorkspaceActivation Workspace { get; }

    internal BrowserRetainedWorkspacePostingDraft Posting { get; }

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

    internal async ValueTask<BrowserRetainedWorkspaceNonPostingResult>
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
internal sealed class BrowserRetainedWorkspaceActivationSession
{
    readonly BrowserRetainedWorkspaceActivationOwner? _owner;
    readonly BrowserRetainedWorkspaceActivationIntent? _intent;

    internal BrowserRetainedWorkspaceActivationSession(
        BrowserRetainedWorkspaceActivationOwner owner,
        BrowserRetainedWorkspaceActivationIntent intent,
        Task<BrowserRetainedWorkspaceActivationResult> activation)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _intent = intent ?? throw new ArgumentNullException(nameof(intent));
        Activation = activation ?? throw new ArgumentNullException(nameof(activation));
        Preparation = intent.Preparation;
        Receipt = intent.Receipt;
    }

    BrowserRetainedWorkspaceActivationSession(
        string receipt,
        BrowserRetainedWorkspacePreparationResult preparation,
        BrowserRetainedWorkspaceActivationResult activation)
    {
        Receipt = receipt;
        Preparation = Task.FromResult(preparation);
        Activation = Task.FromResult(activation);
    }

    internal string Receipt { get; }

    internal Task<BrowserRetainedWorkspacePreparationResult> Preparation
        { get; }

    internal Task<BrowserRetainedWorkspaceActivationResult> Activation
        { get; }

    internal bool Commit() =>
        _owner?.TryBeginConsumerCommit(_intent!) ?? false;

    internal bool Cancel() =>
        _owner?.TryCancelActivation(_intent!) ?? false;

    internal BrowserRetainedWorkspaceConsumerCompletionResult Complete(
        bool succeeded,
        string? failure) =>
        _owner?.CompleteConsumerActivation(_intent!, succeeded, failure)
        ?? new BrowserRetainedWorkspaceConsumerCompletionResult.Unavailable(
            "The retained Workspace activation is already complete.");

    internal static BrowserRetainedWorkspaceActivationSession Completed(
        string receipt,
        BrowserRetainedWorkspacePreparationResult preparation,
        BrowserRetainedWorkspaceActivationResult activation) =>
        new(receipt, preparation, activation);
}

[SupportedOSPlatform("browser")]
internal sealed partial class BrowserRetainedWorkspaceActivationOwner :
    IAsyncDisposable
{
    readonly object _gate = new();
    readonly Func<BrowserWorkspaceRealizationHost> _hostFactory;
    readonly Func<CompleteRestorationExecutionOptions> _optionsFactory;
    readonly Dictionary<string, Task<WorkspaceRealizationSettlement>>
        _settlements = new(StringComparer.Ordinal);
    BrowserWorkspaceRealizationHost _host;
    BrowserRetainedWorkspaceActivationIntent? _latestIntent;
    BrowserRetainedWorkspacePosting? _active;
    string? _deactivationCompletionReceipt;
    bool _deactivationSettlementFailed;
    bool _activationCommitPending;
    long _nextActivationReceipt;
    long _nextDeactivationReceipt;
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
        InitializeTypeFind();
    }

    internal BrowserRetainedWorkspacePosting? Active
    {
        get
        {
            lock (_gate)
                return _active;
        }
    }

    internal NavigationAuthorityResult RecordConsumerPosting(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority) =>
        ApplyNavigationAuthority(
            realizationId,
            publicationOrdinal,
            authority,
            static (slot, effect) =>
                slot.RecordConsumerPosting(effect));

    internal bool ValidateNavigationAuthority(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(realizationId);
        ArgumentNullException.ThrowIfNull(authority);
        BrowserNavigationStateSlot slot;
        lock (_gate)
        {
            if (_active is not { } active
                || active.RealizationId != realizationId
                || active.PublicationOrdinal != publicationOrdinal)
            {
                return false;
            }
            slot = active.NavigationState;
        }
        return slot.ValidateAuthority(authority);
    }

    internal NavigationAuthorityResult Acknowledge(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority) =>
        ApplyNavigationAuthority(
            realizationId,
            publicationOrdinal,
            authority,
            static (slot, effect) => slot.Acknowledge(effect));

    internal NavigationAuthorityResult Abandon(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority) =>
        ApplyNavigationAuthority(
            realizationId,
            publicationOrdinal,
            authority,
            static (slot, effect) => slot.Abandon(effect));

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
            string receipt =
                $"workspace-activation-{++_nextActivationReceipt}";
            if (_closing)
            {
                CompleteRestorationFailure failure = HostFailure(
                    "The retained Workspace activation owner is closed.");
                return BrowserRetainedWorkspaceActivationSession.Completed(
                    receipt,
                    new BrowserRetainedWorkspacePreparationResult.Failed(
                        failure),
                    new BrowserRetainedWorkspaceActivationResult.Failed(
                        failure));
            }
            if (_deactivating)
            {
                CompleteRestorationFailure failure = HostFailure(
                    "The active Browser Workspace is still draining.");
                return BrowserRetainedWorkspaceActivationSession.Completed(
                    receipt,
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
                    receipt,
                    new BrowserRetainedWorkspacePreparationResult.Failed(
                        failure),
                    new BrowserRetainedWorkspaceActivationResult.Failed(
                        failure));
            }
            if (_activationCommitPending)
            {
                CompleteRestorationFailure failure = HostFailure(
                    "A retained Workspace activation is awaiting consumer completion.");
                return BrowserRetainedWorkspaceActivationSession.Completed(
                    receipt,
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
                    receipt,
                    new BrowserRetainedWorkspacePreparationResult.NoEffect(
                        _active),
                    noEffect);
            }

            intent = new(receipt, request);
            _latestIntent = intent;
        }

        Task<BrowserRetainedWorkspaceActivationResult> activation =
            RunActivationAsync(intent, cancellationToken);
        return new(this, intent, activation);
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
        BrowserRetainedWorkspaceActivationResult activation =
            await session.Activation.ConfigureAwait(false);
        if (activation is BrowserRetainedWorkspaceActivationResult.Activated)
        {
            if (session.Complete(succeeded: true, failure: null)
                is not BrowserRetainedWorkspaceConsumerCompletionResult
                    .Completed)
            {
                throw new InvalidOperationException(
                    "The compatibility activation could not complete its "
                        + "consumer transaction.");
            }
        }
        return activation;
    }

    async Task<BrowserRetainedWorkspaceActivationResult> RunActivationAsync(
        BrowserRetainedWorkspaceActivationIntent intent,
        CancellationToken cancellationToken)
    {
        using var cancellationRegistration = cancellationToken.Register(
            static state =>
                ((BrowserRetainedWorkspaceActivationIntent)state!).Cancel(),
            intent);
        BrowserPreparedWorkspaceActivation? preparedActivation = null;
        bool awaitingConsumerCompletion = false;
        try
        {
            CompleteRestorationResult<BrowserPreparedWorkspaceActivation>
                result = await RestoreAsync(intent, cancellationToken)
                    .ConfigureAwait(false);
            if (result
                is CompleteRestorationResult<
                    BrowserPreparedWorkspaceActivation>.Activated activated)
            {
                preparedActivation = activated.Activation;
                if (!intent.PublishPrepared(preparedActivation.Posting)
                    || !await intent.WaitForCommitAsync().ConfigureAwait(false))
                {
                    BrowserRetainedWorkspaceNonPostingResult cleanup =
                        await preparedActivation.SettleAsync()
                            .ConfigureAwait(false);
                    if (cleanup.Failure is not null)
                    {
                        var failed =
                            new BrowserRetainedWorkspaceActivationResult.Failed(
                                cleanup.Failure);
                        intent.CompletePreparation(failed);
                        return failed;
                    }

                    BrowserRetainedWorkspaceActivationResult cancelled =
                        intent.Status switch
                        {
                            CompleteRestorationIntentStatus.Cancelled =>
                                new BrowserRetainedWorkspaceActivationResult
                                    .Superseded(),
                            _ => new BrowserRetainedWorkspaceActivationResult
                                .Superseded(),
                        };
                    intent.CompletePreparation(cancelled);
                    return cancelled;
                }

                BrowserRetainedWorkspaceActivationResult activation =
                    await CommitPreparedAsync(
                        intent,
                        activated,
                        requiredIncumbent: null)
                    .ConfigureAwait(false);
                if (activation
                    is BrowserRetainedWorkspaceActivationResult.Activated)
                {
                    awaitingConsumerCompletion = true;
                }
                intent.CompletePreparation(activation);
                return activation;
            }
            BrowserRetainedWorkspaceActivationResult completed = result switch
            {
                CompleteRestorationResult<
                        BrowserPreparedWorkspaceActivation>.Superseded =>
                    new BrowserRetainedWorkspaceActivationResult.Superseded(),
                CompleteRestorationResult<
                        BrowserPreparedWorkspaceActivation>.Failed failed =>
                    new BrowserRetainedWorkspaceActivationResult.Failed(
                        failed.Failure),
                _ => throw new InvalidOperationException(
                    "Complete restoration returned an unsupported Browser outcome."),
            };
            intent.CompletePreparation(completed);
            return completed;
        }
        catch (Exception failure)
        {
            if (preparedActivation is { IsPending: true })
            {
                BrowserRetainedWorkspaceNonPostingResult cleanup =
                    await preparedActivation.SettleAsync()
                        .ConfigureAwait(false);
                if (cleanup.Failure is not null)
                {
                    throw new AggregateException(
                        failure,
                        new InvalidOperationException(
                            cleanup.Failure.Message));
                }
            }
            intent.FailPreparation(failure);
            throw;
        }
        finally
        {
            if (!awaitingConsumerCompletion)
                FinishActivation(intent);
            cancellationRegistration.Dispose();
        }
    }

    internal bool TryBeginConsumerCommit(
        BrowserRetainedWorkspaceActivationIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        lock (_gate)
        {
            if (_closing
                || _deactivating
                || _activationCommitPending
                || !ReferenceEquals(_latestIntent, intent)
                || !intent.Commit())
            {
                return false;
            }

            _activationCommitPending = true;
            return true;
        }
    }

    internal bool TryCancelActivation(
        BrowserRetainedWorkspaceActivationIntent intent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        lock (_gate)
        {
            if (_activationCommitPending
                || !ReferenceEquals(_latestIntent, intent))
            {
                return false;
            }

            intent.Cancel();
            return true;
        }
    }

    internal BrowserRetainedWorkspaceConsumerCompletionResult
        CompleteConsumerActivation(
            BrowserRetainedWorkspaceActivationIntent intent,
            bool succeeded,
            string? failure)
    {
        ArgumentNullException.ThrowIfNull(intent);
        BrowserRetainedWorkspacePosting active;
        lock (_gate)
        {
            if (!_activationCommitPending
                || !ReferenceEquals(_latestIntent, intent)
                || _active?.RetainedDefinitionId
                    != intent.Request.RetainedDefinitionId)
            {
                return new BrowserRetainedWorkspaceConsumerCompletionResult
                    .Unavailable(
                        "The retained Workspace activation receipt is not "
                            + "awaiting consumer completion.");
            }

            active = _active;
        }

        if (!succeeded)
        {
            NavigationEffectAuthority authority =
                active.Navigation.Authority
                ?? throw new InvalidOperationException(
                    "A committed retained Workspace must carry Navigation "
                        + "effect authority until consumer completion.");
            NavigationAuthorityResult abandonment = Abandon(
                active.RealizationId,
                active.PublicationOrdinal,
                authority);
            if (abandonment
                is not NavigationAuthorityResult.Accepted
                and not NavigationAuthorityResult.InvalidAuthority)
            {
                throw new InvalidOperationException(
                    $"Navigation abandonment returned {abandonment}.");
            }
        }

        lock (_gate)
        {
            if (!_activationCommitPending
                || !ReferenceEquals(_latestIntent, intent)
                || !ReferenceEquals(_active, active))
            {
                return new BrowserRetainedWorkspaceConsumerCompletionResult
                    .Unavailable(
                        "The retained Workspace activation receipt is not "
                            + "awaiting consumer completion.");
            }
            _activationCommitPending = false;
            _latestIntent = null;
        }

        intent.Dispose();
        return new BrowserRetainedWorkspaceConsumerCompletionResult.Completed(
            succeeded,
            succeeded
                ? null
                : string.IsNullOrWhiteSpace(failure)
                    ? "Retained Workspace consumer completion failed."
                    : failure);
    }

    void FinishActivation(BrowserRetainedWorkspaceActivationIntent intent)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_latestIntent, intent))
            {
                _latestIntent = null;
                _activationCommitPending = false;
            }
        }
        intent.Dispose();
    }

    internal async ValueTask<WorkspaceRealizationOperationAdmission>
        EnterOperationAsync(
            string retainedDefinitionId,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        BrowserWorkspaceRealizationHost host;
        InspectionWorkspaceIdentity expectedRealization;
        BrowserRetainedWorkspacePosting expectedPosting;
        lock (_gate)
        {
            if (_active?.RetainedDefinitionId != retainedDefinitionId)
            {
                return new WorkspaceRealizationOperationAdmission.Unavailable(
                    WorkspaceRealizationOperationUnavailableReason.NoActiveRealization);
            }
            host = _host;
            expectedPosting = _active;
            expectedRealization = expectedPosting.Realization;
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
            if (ReferenceEquals(_active, expectedPosting))
                return admission;
        }
        admitted.Lease.Dispose();
        return new WorkspaceRealizationOperationAdmission.Unavailable(
            WorkspaceRealizationOperationUnavailableReason.NoActiveRealization);
    }

    internal Task<BrowserRetainedWorkspaceAdmissionResult<BrowserRetainedWorkspacePackagePresentation>>
        AdmitPackageAsync(
            string retainedDefinitionId,
            string realizationId,
            string navigationId,
            CancellationToken cancellationToken = default) =>
        AdmitRowAsync(
            retainedDefinitionId, realizationId, navigationId, "Package",
            static (active, id) => active.Packages.FirstOrDefault(
                candidate => candidate.NavigationId == id),
            cancellationToken);

    internal Task<BrowserRetainedWorkspaceAdmissionResult<BrowserRetainedWorkspacePlatformPresentation>>
        AdmitPlatformAsync(
            string retainedDefinitionId,
            string realizationId,
            string navigationId,
            CancellationToken cancellationToken = default) =>
        AdmitRowAsync(
            retainedDefinitionId, realizationId, navigationId, "Platform",
            static (active, id) => active.Platforms.FirstOrDefault(
                candidate => candidate.NavigationId == id),
            cancellationToken);

    async Task<BrowserRetainedWorkspaceAdmissionResult<T>> AdmitRowAsync<T>(
        string retainedDefinitionId,
        string realizationId,
        string navigationId,
        string rowKind,
        Func<BrowserRetainedWorkspacePosting, string, T?> select,
        CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(realizationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(navigationId);
        WorkspaceRealizationOperationAdmission admission =
            await EnterOperationAsync(
                retainedDefinitionId,
                cancellationToken).ConfigureAwait(false);
        if (admission
            is not WorkspaceRealizationOperationAdmission.Admitted admitted)
        {
            return new BrowserRetainedWorkspaceAdmissionResult<T>.Superseded();
        }

        using WorkspaceRealizationOperationLease operation = admitted.Lease;
        lock (_gate)
        {
            if (_active is not { } active
                || active.RetainedDefinitionId != retainedDefinitionId
                || active.RealizationId != realizationId
                || !ReferenceEquals(active.Realization, operation.Realization))
            {
                return new BrowserRetainedWorkspaceAdmissionResult<T>.Superseded();
            }

            T? presentation = select(active, navigationId);
            return presentation is null
                ? new BrowserRetainedWorkspaceAdmissionResult<T>.Unavailable(
                    $"Navigation row '{navigationId}' is not a {rowKind} in the active Workspace.")
                : new BrowserRetainedWorkspaceAdmissionResult<T>.Admitted(presentation);
        }
    }

    internal async Task<BrowserRetainedWorkspaceDeactivationResult>
        DeactivateAsync(
            string retainedDefinitionId,
            CancellationToken cancellationToken = default)
    {
        BrowserRetainedWorkspaceDeactivationResult result =
            await BeginDeactivationAsync(
                    retainedDefinitionId,
                    cancellationToken)
                .ConfigureAwait(false);
        string? receipt = result switch
        {
            BrowserRetainedWorkspaceDeactivationResult.Deactivated
                deactivated => deactivated.CompletionReceipt,
            BrowserRetainedWorkspaceDeactivationResult.CleanupFailed
                failed => failed.CompletionReceipt,
            _ => null,
        };
        if (receipt is not null)
        {
            _ = CompleteConsumerDeactivation(
                receipt,
                succeeded: true,
                failure: null);
        }
        return result;
    }

    internal async Task<BrowserRetainedWorkspaceDeactivationResult>
        BeginDeactivationAsync(
            string retainedDefinitionId,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(retainedDefinitionId);
        BrowserWorkspaceRealizationHost retiredHost;
        InspectionWorkspaceIdentity activeRealization;
        BrowserRetainedWorkspacePosting activePosting;
        string completionReceipt;
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

            activePosting = _active;
            retiredHost = _host;
            activeRealization = retiredHost.Current?.Identity
                ?? throw new InvalidOperationException(
                    "An active retained definition requires an active realization.");
            completionReceipt =
                $"workspace-deactivation-{++_nextDeactivationReceipt}";
            _active = null;
            _deactivating = true;
            _deactivationCompletionReceipt = completionReceipt;
        }

        string? navigationFailure = null;
        try
        {
            activePosting.NavigationState.Retire();
        }
        catch (Exception failure)
        {
            navigationFailure =
                "The active Navigation state retired with a callback failure: "
                    + failure.Message;
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
            _deactivationSettlementFailed =
                failedSettlement is not null || navigationFailure is not null;
        }
        return failedSettlement is null && navigationFailure is null
            ? new BrowserRetainedWorkspaceDeactivationResult.Deactivated(
                completionReceipt,
                settlement)
            : new BrowserRetainedWorkspaceDeactivationResult.CleanupFailed(
                completionReceipt,
                failedSettlement ?? settlement,
                navigationFailure);
    }

    internal BrowserRetainedWorkspaceConsumerCompletionResult
        CompleteConsumerDeactivation(
            string completionReceipt,
            bool succeeded,
            string? failure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(completionReceipt);
        lock (_gate)
        {
            if (!_deactivating
                || _deactivationCompletionReceipt != completionReceipt)
            {
                return new BrowserRetainedWorkspaceConsumerCompletionResult
                    .Unavailable(
                        "The retained Workspace deactivation receipt is not "
                            + "awaiting consumer completion.");
            }

            _deactivationCompletionReceipt = null;
            _deactivating = false;
            if (_deactivationSettlementFailed)
            {
                _cleanupFailed = true;
            }
            else if (!_closing)
            {
                _host = _hostFactory();
                ReplaceTypeFindHost();
            }
            _deactivationSettlementFailed = false;
        }

        return new BrowserRetainedWorkspaceConsumerCompletionResult.Completed(
            succeeded,
            succeeded
                ? null
                : string.IsNullOrWhiteSpace(failure)
                    ? "Retained Workspace consumer completion failed."
                    : failure);
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
                $"workspace-spotlight-{++_nextActivationReceipt}",
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
        BrowserRetainedWorkspacePosting,
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
            authority.SourcePosting);
        return commit switch
        {
            BrowserRetainedWorkspaceCommitResult.Committed committed =>
                new BrowserSpotlightRetainedWorkspacePublicationResult<
                    BrowserRetainedWorkspacePosting,
                    BrowserRetainedWorkspaceActivationRejection>.Published(
                        committed.Posting),
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
        BrowserPreparedWorkspaceActivation prepared,
        BrowserRetainedWorkspacePosting? requiredIncumbent)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(prepared);
        BrowserRetainedWorkspacePostingDraft draft =
            prepared.Posting;
        if (!ReferenceEquals(prepared.Workspace, workspace))
        {
            throw new ArgumentException(
                "The prepared Browser posting belongs to a different "
                    + "complete Workspace.",
                nameof(workspace));
        }

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
            BrowserRetainedWorkspaceCleanupEvidence? navigationCleanup = null;
            if (activated.Predecessor is not null)
            {
                string settlementId = $"workspace-settlement-{++_nextSettlement}";
                _settlements.Add(
                    settlementId,
                    activated.Predecessor.Completion);
                predecessor = new(settlementId, activated.Predecessor);
            }
            if (_active is { } previous)
            {
                try
                {
                    previous.NavigationState.Retire();
                }
                catch (Exception failure)
                {
                    navigationCleanup = new(
                        "The predecessor Navigation state retired with a "
                            + $"callback failure: {failure.Message}");
                }
            }

            long publicationOrdinal = ++_nextRealization;
            BrowserRetainedWorkspacePosting posting = draft.Publish(
                activated.Realization.Identity,
                $"workspace-realization-{publicationOrdinal}",
                publicationOrdinal,
                predecessor,
                navigationCleanup);
            _active = posting;
            return new BrowserRetainedWorkspaceCommitResult.Committed(
                activated,
                posting);
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
        var projection =
            new BrowserCompleteRestorationProjectionCapture(intent.Request);
        var host = new BrowserCompleteRestorationHost(
            _host,
            intent,
            projection);
        CompleteRestorationExecutionOptions options = _optionsFactory();
        if (preparation is CompleteRestorationPreparationResult.Ready ready)
        {
            options = BrowserCompleteRestorationOptions.BindPackageSources(
                options,
                ready.Plan.ConfiguredPackageSources,
                intent.Request.PackageSourceCredentials);
        }
        return await CompleteRestorationCoordinator.RestoreWithProjectionAsync(
                    preparation,
                    intent,
                    host,
                    options with { CaptureInventory = true },
                    projection.CaptureAsync,
                    cancellationToken)
                .ConfigureAwait(false);
    }

    async Task<BrowserRetainedWorkspaceActivationResult> CommitPreparedAsync(
        BrowserRetainedWorkspaceActivationIntent intent,
        CompleteRestorationResult<
            BrowserPreparedWorkspaceActivation>.Activated activated,
        BrowserRetainedWorkspacePosting? requiredIncumbent)
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
                committed.Posting);
        }

        BrowserRetainedWorkspaceNonPostingResult cleanup =
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
        BrowserRetainedWorkspaceActivationIntent? committedIntent = null;
        Task disposal;
        lock (_gate)
        {
            if (_disposeCompletion is not null)
                return new(_disposeCompletion);

            _closing = true;
            _latestIntent?.Cancel();
            if (_activationCommitPending)
                committedIntent = _latestIntent;
            _latestIntent = null;
            _activationCommitPending = false;
            _deactivationCompletionReceipt = null;
            _deactivating = false;
            BrowserRetainedWorkspacePosting? active = _active;
            _active = null;
            DisposeTypeFind();
            _disposeCompletion = DisposeHostAsync(_host, active);
            disposal = _disposeCompletion;
        }
        committedIntent?.Dispose();
        return new(disposal);
    }

    static async Task DisposeHostAsync(
        BrowserWorkspaceRealizationHost host,
        BrowserRetainedWorkspacePosting? active)
    {
        Exception? navigationFailure = null;
        try
        {
            active?.NavigationState.Retire();
        }
        catch (Exception failure)
        {
            navigationFailure = failure;
        }

        try
        {
            await host.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception hostFailure) when (navigationFailure is not null)
        {
            throw new AggregateException(navigationFailure, hostFailure);
        }
        if (navigationFailure is not null)
            throw navigationFailure;
    }

    NavigationAuthorityResult ApplyNavigationAuthority(
        string realizationId,
        long publicationOrdinal,
        NavigationEffectAuthority authority,
        Func<
            BrowserNavigationStateSlot,
            NavigationEffectAuthority,
            NavigationAuthorityResult> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(realizationId);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(operation);
        BrowserNavigationStateSlot slot;
        lock (_gate)
        {
            if (_active is not { } active
                || active.RealizationId != realizationId
                || active.PublicationOrdinal != publicationOrdinal)
            {
                return NavigationAuthorityResult.InvalidAuthority;
            }
            slot = active.NavigationState;
        }
        return operation(slot, authority);
    }

    string? CurrentAdmissionRejection()
    {
        if (_closing)
            return "The retained Workspace activation owner is closed.";
        if (_deactivating)
            return "The active Browser Workspace is still draining.";
        if (_activationCommitPending)
            return "A retained Workspace activation is awaiting consumer completion.";
        if (_cleanupFailed)
            return "A prior Browser Workspace failed to settle.";
        return null;
    }

    static BrowserSpotlightRetainedWorkspacePublicationResult<
        BrowserRetainedWorkspacePosting,
        BrowserRetainedWorkspaceActivationRejection>
        RejectedSpotlight(string message) =>
        new BrowserSpotlightRetainedWorkspacePublicationResult<
            BrowserRetainedWorkspacePosting,
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
        BrowserRetainedWorkspacePosting Posting)
        : BrowserRetainedWorkspaceCommitResult;

    internal sealed record Superseded
        : BrowserRetainedWorkspaceCommitResult;

    internal sealed record Rejected(
        WorkspaceRealizationCandidateRejection Reason)
        : BrowserRetainedWorkspaceCommitResult;
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserRetainedWorkspaceActivationIntent(
    string receipt,
    BrowserRetainedWorkspaceActivationRequest request)
    : ICompleteRestorationIntentAuthority, IDisposable
{
    readonly CancellationTokenSource _revocation = new();
    readonly TaskCompletionSource<BrowserRetainedWorkspacePreparationResult>
        _preparation = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource<bool> _commit = new(
        TaskCreationOptions.RunContinuationsAsynchronously);
    int _status = (int)CompleteRestorationIntentStatus.Current;
    int _phase;

    public CompleteRestorationIntentIdentity Identity { get; } = new();

    public CompleteRestorationIntentStatus Status =>
        (CompleteRestorationIntentStatus)Volatile.Read(ref _status);

    public CancellationToken Revocation => _revocation.Token;

    internal string Receipt { get; } =
        !string.IsNullOrWhiteSpace(receipt)
            ? receipt
            : throw new ArgumentException(
                "An activation receipt is required.",
                nameof(receipt));

    internal BrowserRetainedWorkspaceActivationRequest Request { get; } =
        request;

    internal Task<BrowserRetainedWorkspacePreparationResult> Preparation =>
        _preparation.Task;

    internal bool PublishPrepared(
        BrowserRetainedWorkspacePostingDraft posting)
    {
        ArgumentNullException.ThrowIfNull(posting);
        if (Status != CompleteRestorationIntentStatus.Current)
            return false;
        if (Interlocked.CompareExchange(ref _phase, 1, 0) != 0)
            return false;
        if (Status != CompleteRestorationIntentStatus.Current)
            return false;
        return _preparation.TrySetResult(
            new BrowserRetainedWorkspacePreparationResult.Prepared(posting));
    }

    internal Task<bool> WaitForCommitAsync() => _commit.Task;

    internal bool Commit()
    {
        if (Status != CompleteRestorationIntentStatus.Current)
            return false;
        if (Interlocked.CompareExchange(ref _phase, 2, 1) != 1)
            return false;
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
                        noEffect.Posting)),
            BrowserRetainedWorkspaceActivationResult.Superseded =>
                _preparation.TrySetResult(
                    new BrowserRetainedWorkspacePreparationResult.Superseded()),
            BrowserRetainedWorkspaceActivationResult.Failed failed =>
                _preparation.TrySetResult(
                    new BrowserRetainedWorkspacePreparationResult.Failed(
                        failed.Failure)),
            BrowserRetainedWorkspaceActivationResult.Activated => true,
            _ => throw new InvalidOperationException(
                "Unknown Browser retained Workspace activation result."),
        };
    }

    internal void FailPreparation(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _preparation.TrySetException(failure);
    }

    internal void Supersede() =>
        Revoke(CompleteRestorationIntentStatus.Superseded);

    internal void Cancel() =>
        Revoke(CompleteRestorationIntentStatus.Cancelled);

    void Revoke(CompleteRestorationIntentStatus status)
    {
        if (Volatile.Read(ref _phase) == 2)
            return;
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
internal sealed class BrowserCompleteRestorationProjectionCapture(
    BrowserRetainedWorkspaceActivationRequest request)
{
    readonly object _gate = new();
    BrowserRetainedWorkspacePostingDraft? _posting;

    internal ValueTask CaptureAsync(
        CompleteWorkspaceActivation activation,
        CompleteRestorationReadyProjection projection,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BrowserRetainedWorkspacePostingDraft posting =
            BrowserRetainedWorkspacePostingDraft.Create(
                request,
                activation,
                projection);
        lock (_gate)
        {
            if (_posting is not null)
            {
                throw new InvalidOperationException(
                    "Complete restoration projected Browser presentation "
                        + "more than once.");
            }
            _posting = posting;
        }
        return ValueTask.CompletedTask;
    }

    internal BrowserRetainedWorkspacePostingDraft Take(
        CompleteWorkspaceActivation activation)
    {
        lock (_gate)
        {
            BrowserRetainedWorkspacePostingDraft posting =
                _posting
                ?? throw new InvalidOperationException(
                    "Complete restoration omitted Browser presentation.");
            if (!ReferenceEquals(
                    posting.Navigation,
                    activation.Snapshot.Navigation))
            {
                throw new InvalidOperationException(
                    "Complete restoration projected a different Navigation "
                        + "initialization.");
            }
            _posting = null;
            return posting;
        }
    }
}

[SupportedOSPlatform("browser")]
internal sealed class BrowserCompleteRestorationHost(
    BrowserWorkspaceRealizationHost host,
    BrowserRetainedWorkspaceActivationIntent intent,
    BrowserCompleteRestorationProjectionCapture projection)
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
        try
        {
            using WorkspaceRealizationConstructionLease construction =
                prepared.Candidate.EnterConstruction();
            workspacePreparation = await prepare(
                        construction.Workspace,
                        authority.Revocation)
                    .ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            CompleteRestorationFailure? cleanup =
                await SettleCandidateAsync(prepared.Candidate)
                    .ConfigureAwait(false);
            if (cleanup is not null)
            {
                throw new AggregateException(
                    failure,
                    new InvalidOperationException(cleanup.Message));
            }
            throw;
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

        BrowserRetainedWorkspacePostingDraft posting;
        try
        {
            posting = projection.Take(preparedWorkspace.Activation);
        }
        catch (Exception failure)
        {
            CompleteRestorationFailure? cleanup =
                await SettleCandidateAsync(prepared.Candidate)
                    .ConfigureAwait(false);
            if (cleanup is not null)
            {
                throw new AggregateException(
                    failure,
                    new InvalidOperationException(cleanup.Message));
            }
            throw;
        }

        return new CompleteRestorationHostResult<
            BrowserPreparedWorkspaceActivation>.Activated(
                new(
                    host,
                    prepared.Candidate,
                    preparedWorkspace.Activation,
                    posting),
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
            PlatformSurfaceLimits = BrowserApiSurfacePolicy.Limits,
        };
    }

    internal static CompleteRestorationExecutionOptions BindPackageSources(
        CompleteRestorationExecutionOptions options,
        IReadOnlyList<WorkspacePackageSourceDefinition> definitions,
        IReadOnlyDictionary<string, PackageSourceCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(credentials);
        WorkspacePackageSourceBindingResult binding =
            WorkspacePackageSourceBinding.Create(definitions, credentials);
        if (binding is WorkspacePackageSourceBindingResult.Rejected rejected)
        {
            return Deny(options, rejected.Message);
        }

        WorkspacePackageSourceBindingPlan plan =
            ((WorkspacePackageSourceBindingResult.Bound)binding).Plan;
        if (plan.Sources.Count == 0)
            return options;

        if (plan.UnboundAuthenticationRequirements.FirstOrDefault()
            is { } missing)
        {
            return Deny(
                options,
                $"Workspace source '{missing.Endpoint}' requires an explicit Basic "
                    + "credential for this page session; credential providers "
                    + "are unavailable in Browser/Wasm.");
        }

        return options with
        {
            ContextLoad = options.ContextLoad with
            {
                SourceAuthorization =
                    new UniformPackageSourceAuthorization(plan.Sources),
            },
        };
    }

    static CompleteRestorationExecutionOptions Deny(
        CompleteRestorationExecutionOptions options,
        string reason) =>
        options with
        {
            ContextLoad = options.ContextLoad with
            {
                SourceAuthorization =
                    new DeniedWorkspacePackageSourceAuthorization(reason),
            },
        };

    private sealed class DeniedWorkspacePackageSourceAuthorization(
        string reason) : IPackageSourceAuthorization
    {
        public PackageSourceAuthorization AuthorizeSourcesFor(
            string packageId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(packageId);
            return PackageSourceAuthorization.Deny(reason);
        }
    }
}
