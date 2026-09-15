using System.Collections.Immutable;

namespace DotnetInspector.Queries.Definitions;

public sealed record CompleteRestorationExecutionOptions
{
    public required WorkspaceContextLoadOptions ContextLoad { get; init; }

    public required DateTimeOffset ScopeDeadline { get; init; }

    public required ViewFacetRegistry Facets { get; init; }

    public required NavigationFacetAvailabilityProvider FacetAvailability
    {
        get;
        init;
    }

    public ApiSurfaceProjectionLimits PackageSurfaceLimits { get; init; } =
        NavigationPackageEvaluationFactory.DefaultSurfaceLimits;

    public CompleteRestorationProjectionProvider Projection { get; init; } =
        CompleteRestorationProjections.Classify;
}

public abstract record CompleteRestorationProjection
{
    private protected CompleteRestorationProjection()
    {
    }

    public sealed record Projectable(string CanonicalPacket)
        : CompleteRestorationProjection
    {
        public string CanonicalPacket { get; } =
            CanonicalPacket
            ?? throw new ArgumentNullException(nameof(CanonicalPacket));
    }

    public sealed record NonProjectable(string Reason)
        : CompleteRestorationProjection
    {
        public string Reason { get; } =
            !string.IsNullOrWhiteSpace(Reason)
                ? Reason
                : throw new ArgumentException(
                    "A non-projectable result requires a reason.",
                    nameof(Reason));
    }
}

public abstract record CompleteRestorationResolvedState
{
    private protected CompleteRestorationResolvedState()
    {
    }

    public sealed record Version2(
        CommittedScenarioDefinitionSet Definitions,
        ImmutableArray<CompleteRestorationResolvedViewState> States,
        int? ActiveStateIndex)
        : CompleteRestorationResolvedState
    {
        public CommittedScenarioDefinitionSet Definitions { get; } =
            Definitions
            ?? throw new ArgumentNullException(nameof(Definitions));

        public ImmutableArray<CompleteRestorationResolvedViewState> States
        {
            get;
        } = !States.IsDefault
            && States.All(static state => state is not null)
                ? States
                : throw new ArgumentException(
                    "Resolved states must be an initialized immutable array.",
                    nameof(States));

        public int? ActiveStateIndex { get; } =
            (ActiveStateIndex is null && States.IsEmpty)
                || (ActiveStateIndex is >= 0
                    && ActiveStateIndex < States.Length)
                    ? ActiveStateIndex
                    : throw new ArgumentOutOfRangeException(
                        nameof(ActiveStateIndex));
    }

    public sealed record Legacy(NavigationInitialization Initialization)
        : CompleteRestorationResolvedState
    {
        public NavigationInitialization Initialization { get; } =
            Initialization
            ?? throw new ArgumentNullException(nameof(Initialization));
    }
}

public sealed record CompleteRestorationResolvedViewState
{
    internal CompleteRestorationResolvedViewState(
        CommittedViewStateDefinition definition,
        NavigationInitialization? initialization,
        NavigationLensActivationResult? lensResolution)
    {
        Definition = definition
            ?? throw new ArgumentNullException(nameof(definition));
        Initialization = initialization;
        LensResolution = lensResolution;
    }

    public CommittedViewStateDefinition Definition { get; }

    public string? NavigationId => Definition.Navigation;

    public NavigationInitialization? Initialization { get; }

    public NavigationLensActivationResult? LensResolution { get; }
}

public sealed record CompleteRestorationProjectionRequest(
    CompleteRestorationRequestBasis Request,
    CompleteWorkspaceSnapshot Snapshot);

public abstract record CompleteRestorationProjectionResult
{
    private protected CompleteRestorationProjectionResult()
    {
    }

    public sealed record Projected(CompleteRestorationProjection Projection)
        : CompleteRestorationProjectionResult
    {
        public CompleteRestorationProjection Projection { get; } =
            Projection
            ?? throw new ArgumentNullException(nameof(Projection));
    }

    public sealed record Failed(string Message)
        : CompleteRestorationProjectionResult
    {
        public string Message { get; } =
            !string.IsNullOrWhiteSpace(Message)
                ? Message
                : throw new ArgumentException(
                    "A projection failure requires a message.",
                    nameof(Message));
    }
}

public delegate CompleteRestorationProjectionResult
    CompleteRestorationProjectionProvider(
        CompleteRestorationProjectionRequest request);

public static class CompleteRestorationProjections
{
    public static CompleteRestorationProjectionResult Classify(
        CompleteRestorationProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new CompleteRestorationProjectionResult.Projected(
            request.Request switch
            {
                CompleteRestorationRequestBasis.PacketInput packet =>
                    new CompleteRestorationProjection.Projectable(
                        packet.Encoded),
                CompleteRestorationRequestBasis.DefinitionInput =>
                    new CompleteRestorationProjection.NonProjectable(
                        "Packet format 2 is not implemented for committed "
                            + "definition state."),
                _ => throw new InvalidOperationException(
                    "Unknown complete-restoration request basis."),
            });
    }
}

/// <summary>Complete state prepared inside one unpublished Workspace.</summary>
public sealed record CompleteWorkspaceSnapshot
{
    internal CompleteWorkspaceSnapshot(
        WorkspaceDefinitionSnapshot definition,
        WorkspaceScopeSnapshot scope,
        ImmutableArray<WorkspaceDeclarationContextReceipt> contexts,
        CompleteRestorationResolvedState resolved,
        NavigationOperationInitialization navigation)
    {
        Definition = definition
            ?? throw new ArgumentNullException(nameof(definition));
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        if (contexts.IsDefault
            || contexts.Any(static context => context is null))
        {
            throw new ArgumentException(
                "Context evidence must be an initialized immutable array.",
                nameof(contexts));
        }

        Contexts = contexts;
        Resolved = resolved ?? throw new ArgumentNullException(nameof(resolved));
        Navigation = navigation
            ?? throw new ArgumentNullException(nameof(navigation));
    }

    public WorkspaceDefinitionSnapshot Definition { get; }

    public WorkspaceScopeSnapshot Scope { get; }

    public ImmutableArray<WorkspaceDeclarationContextReceipt> Contexts
    {
        get;
    }

    public CompleteRestorationResolvedState Resolved { get; }

    public NavigationOperationInitialization Navigation { get; }
}

/// <summary>
/// Definitions-owned detached preparation paired with host lifetime authority
/// only after the construction adapter accepts it.
/// </summary>
public sealed record CompleteWorkspaceActivation
{
    internal CompleteWorkspaceActivation(
        CompleteRestorationIntentIdentity intent,
        CompleteRestorationRequestBasis request,
        InspectionWorkspaceIdentity workspace,
        CompleteWorkspaceSnapshot snapshot,
        CompleteRestorationProjection projection)
    {
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Workspace = workspace
            ?? throw new ArgumentNullException(nameof(workspace));
        Snapshot = snapshot
            ?? throw new ArgumentNullException(nameof(snapshot));
        Projection = projection
            ?? throw new ArgumentNullException(nameof(projection));
        if (snapshot.Definition.Workspace != workspace
            || snapshot.Scope.Revision.Workspace != workspace
            || snapshot.Navigation.State.Workspace != workspace)
        {
            throw new ArgumentException(
                "Every activation component must belong to the exact "
                    + "prepared Workspace.",
                nameof(snapshot));
        }
    }

    public CompleteRestorationIntentIdentity Intent { get; }

    public CompleteRestorationRequestBasis Request { get; }

    public InspectionWorkspaceIdentity Workspace { get; }

    public CompleteWorkspaceSnapshot Snapshot { get; }

    public CompleteRestorationProjection Projection { get; }
}

public abstract record CompleteWorkspacePreparationResult
{
    private protected CompleteWorkspacePreparationResult()
    {
    }

    public sealed record Prepared(CompleteWorkspaceActivation Activation)
        : CompleteWorkspacePreparationResult
    {
        public CompleteWorkspaceActivation Activation { get; } =
            Activation
            ?? throw new ArgumentNullException(nameof(Activation));
    }

    public sealed record Failed(CompleteRestorationFailure Failure)
        : CompleteWorkspacePreparationResult
    {
        public CompleteRestorationFailure Failure { get; } =
            Failure ?? throw new ArgumentNullException(nameof(Failure));
    }

    public sealed record Superseded : CompleteWorkspacePreparationResult;
}

public delegate ValueTask<CompleteWorkspacePreparationResult>
    CompleteWorkspacePreparationCallback(
        InspectionWorkspace workspace,
        CancellationToken revocation);

/// <summary>
/// Trusted host adapter over its existing candidate or invocation lifetime.
/// It must settle every non-success before returning.
/// </summary>
public interface ICompleteRestorationHost<TActivation>
{
    ValueTask<CompleteRestorationHostResult<TActivation>> ConstructAsync(
        ICompleteRestorationIntentAuthority authority,
        CompleteRestorationPlan plan,
        CompleteWorkspacePreparationCallback prepare,
        CancellationToken cancellationToken = default);
}

public abstract record CompleteRestorationHostResult<TActivation>
{
    private protected CompleteRestorationHostResult()
    {
    }

    public sealed record Activated(
        TActivation Activation,
        CompleteWorkspaceActivation Workspace)
        : CompleteRestorationHostResult<TActivation>
    {
        public TActivation Activation { get; } =
            Activation
            ?? throw new ArgumentNullException(nameof(Activation));

        public CompleteWorkspaceActivation Workspace { get; } =
            Workspace
            ?? throw new ArgumentNullException(nameof(Workspace));
    }

    public sealed record Failed(CompleteRestorationFailure Failure)
        : CompleteRestorationHostResult<TActivation>
    {
        public CompleteRestorationFailure Failure { get; } =
            Failure ?? throw new ArgumentNullException(nameof(Failure));
    }

    public sealed record Superseded : CompleteRestorationHostResult<TActivation>;
}

public abstract record CompleteRestorationResult<TActivation>
{
    private protected CompleteRestorationResult(
        CompleteRestorationIntentIdentity intent,
        CompleteRestorationRequestBasis request)
    {
        Intent = intent ?? throw new ArgumentNullException(nameof(intent));
        Request = request ?? throw new ArgumentNullException(nameof(request));
    }

    public CompleteRestorationIntentIdentity Intent { get; }

    public CompleteRestorationRequestBasis Request { get; }

    public sealed record Activated : CompleteRestorationResult<TActivation>
    {
        internal Activated(
            TActivation activation,
            CompleteWorkspaceActivation workspace)
            : base(workspace.Intent, workspace.Request)
        {
            Activation = activation
                ?? throw new ArgumentNullException(nameof(activation));
            Workspace = workspace;
        }

        public TActivation Activation { get; }

        public CompleteWorkspaceActivation Workspace { get; }
    }

    public sealed record Failed : CompleteRestorationResult<TActivation>
    {
        internal Failed(
            CompleteRestorationIntentIdentity intent,
            CompleteRestorationRequestBasis request,
            CompleteRestorationFailure failure)
            : base(intent, request)
        {
            Failure = failure
                ?? throw new ArgumentNullException(nameof(failure));
        }

        public CompleteRestorationFailure Failure { get; }
    }

    public sealed record Superseded : CompleteRestorationResult<TActivation>
    {
        internal Superseded(
            CompleteRestorationIntentIdentity intent,
            CompleteRestorationRequestBasis request)
            : base(intent, request)
        {
        }
    }
}

public static class CompleteRestorationCoordinator
{
    public static async ValueTask<CompleteRestorationResult<TActivation>>
        RestoreAsync<TActivation>(
            CompleteRestorationPreparationResult preparation,
            ICompleteRestorationIntentAuthority authority,
            ICompleteRestorationHost<TActivation> host,
            CompleteRestorationExecutionOptions options,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ContextLoad);
        ArgumentNullException.ThrowIfNull(options.Facets);
        ArgumentNullException.ThrowIfNull(options.FacetAvailability);
        ArgumentNullException.ThrowIfNull(options.PackageSurfaceLimits);
        ArgumentNullException.ThrowIfNull(options.Projection);
        if (!ReferenceEquals(preparation.Intent, authority.Identity))
        {
            throw new ArgumentException(
                "Preparation and execution must use the same exact host "
                    + "intent identity.",
                nameof(authority));
        }

        if (CurrentnessFailure(authority) is { } unavailable)
        {
            return ResultForUnavailable<TActivation>(
                preparation.Intent,
                preparation.Request,
                unavailable);
        }

        switch (preparation)
        {
            case CompleteRestorationPreparationResult.Failed failed:
                return new CompleteRestorationResult<TActivation>.Failed(
                    preparation.Intent,
                    preparation.Request,
                    failed.Failure);
            case CompleteRestorationPreparationResult.Superseded:
                return new CompleteRestorationResult<TActivation>.Superseded(
                    preparation.Intent,
                    preparation.Request);
        }

        var ready =
            (CompleteRestorationPreparationResult.Ready)preparation;

        CompleteWorkspaceActivation? callbackActivation = null;
        CompleteRestorationHostResult<TActivation> hostResult =
            await host.ConstructAsync(
                authority,
                ready.Plan,
                async (workspace, revocation) =>
                {
                    CompleteWorkspacePreparationResult result =
                        await PrepareWorkspaceAsync(
                            ready.Plan,
                            workspace,
                            authority,
                            options,
                            revocation,
                            cancellationToken).ConfigureAwait(false);
                    if (result
                        is CompleteWorkspacePreparationResult.Prepared prepared)
                    {
                        callbackActivation = prepared.Activation;
                    }

                    return result;
                },
                cancellationToken).ConfigureAwait(false);

        return hostResult switch
        {
            CompleteRestorationHostResult<TActivation>.Activated activated
                when ReferenceEquals(
                    activated.Workspace,
                    callbackActivation) =>
                new CompleteRestorationResult<TActivation>.Activated(
                    activated.Activation,
                    activated.Workspace),
            CompleteRestorationHostResult<TActivation>.Activated =>
                throw new InvalidOperationException(
                    "The host must return the exact Definitions activation "
                        + "issued by its construction callback."),
            CompleteRestorationHostResult<TActivation>.Failed failed =>
                new CompleteRestorationResult<TActivation>.Failed(
                    preparation.Intent,
                    preparation.Request,
                    failed.Failure),
            CompleteRestorationHostResult<TActivation>.Superseded =>
                new CompleteRestorationResult<TActivation>.Superseded(
                    preparation.Intent,
                    preparation.Request),
            _ => throw new InvalidOperationException(
                "Unknown complete-restoration host result."),
        };
    }

    private static async ValueTask<CompleteWorkspacePreparationResult>
        PrepareWorkspaceAsync(
            CompleteRestorationPlan plan,
            InspectionWorkspace workspace,
            ICompleteRestorationIntentAuthority authority,
            CompleteRestorationExecutionOptions options,
            CancellationToken constructionRevocation,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(
            authority.Revocation,
            constructionRevocation,
            cancellationToken);
        CancellationToken token = stop.Token;
        try
        {
            if (CurrentnessFailure(authority) is { } unavailable)
            {
                return unavailable is CompleteRestorationFailure
                    .AuthorityUnavailable authorityFailure
                        && authorityFailure.Status
                            == CompleteRestorationIntentStatus.Superseded
                    ? new CompleteWorkspacePreparationResult.Superseded()
                    : new CompleteWorkspacePreparationResult.Failed(
                        unavailable);
            }

            WorkspaceContextLoadOptions loadOptions =
                options.ContextLoad with
                {
                    IncludePackageRootBindings = true,
                };
            var contextReceipts =
                ImmutableArray.CreateBuilder<
                    WorkspaceDeclarationContextReceipt>(
                        plan.WorkspacePlan.Contexts.Length);
            var packageRoots =
                ImmutableArray.CreateBuilder<PackageRootBinding>();
            for (int index = 0;
                index < plan.WorkspacePlan.Contexts.Length;
                index++)
            {
                token.ThrowIfCancellationRequested();
                WorkspaceDeclarationContext context =
                    await WorkspaceContextLoader.LoadDeclarationContextAsync(
                        workspace,
                        plan.WorkspacePlan.Contexts[index],
                        loadOptions,
                        token).ConfigureAwait(false);
                contextReceipts.Add(context.Receipt);
                if (CurrentnessFailure(authority) is { } contextStale)
                    return PreparationForUnavailable(contextStale);
                if (context.ContextLoadOutcome
                    is not WorkspaceContextLoadOutcome.Loaded loaded)
                {
                    return new CompleteWorkspacePreparationResult.Failed(
                        new CompleteRestorationFailure.ContextLoadFailed(
                            index,
                            context.ContextLoadOutcome
                                ?? throw new InvalidOperationException(
                                    "The context loader returned no outcome.")));
                }

                packageRoots.AddRange(loaded.PackageRoots);
            }

            WorkspaceScopeReadResult initial =
                await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
            if (CurrentnessFailure(authority) is { } scopeReadStale)
                return PreparationForUnavailable(scopeReadStale);
            if (initial is not WorkspaceScopeReadResult.Available available)
            {
                return new CompleteWorkspacePreparationResult.Failed(
                    new CompleteRestorationFailure.ScopeReadFailed(
                        ((WorkspaceScopeReadResult.Unavailable)initial)
                            .RuntimeFailure));
            }

            ImmutableArray<PackageRootBinding> roots =
                DistinctPackageRoots(packageRoots);
            WorkspaceScopeOperationResult scopeResult =
                await workspace.ReplaceScopeAsync(
                    available.Snapshot.Revision,
                    roots,
                    options.ScopeDeadline,
                    token).ConfigureAwait(false);
            if (CurrentnessFailure(authority) is { } scopeMutationStale)
                return PreparationForUnavailable(scopeMutationStale);
            WorkspaceScopeSnapshot scope = scopeResult switch
            {
                WorkspaceScopeOperationResult.Committed committed =>
                    committed.Snapshot,
                WorkspaceScopeOperationResult.NoEffect noEffect =>
                    noEffect.Snapshot,
                _ => null!,
            };
            if (scope is null)
            {
                return new CompleteWorkspacePreparationResult.Failed(
                    new CompleteRestorationFailure.ScopeMutationFailed(
                        scopeResult));
            }

            ResolvedPreparation resolved = await ResolveAsync(
                plan,
                workspace,
                scope,
                roots,
                options,
                token).ConfigureAwait(false);
            if (CurrentnessFailure(authority) is { } resolutionStale)
                return PreparationForUnavailable(resolutionStale);
            if (resolved.Failure is not null)
            {
                return new CompleteWorkspacePreparationResult.Failed(
                    resolved.Failure);
            }

            if (CurrentnessFailure(authority) is { } stale)
            {
                return stale is CompleteRestorationFailure
                    .AuthorityUnavailable authorityFailure
                        && authorityFailure.Status
                            == CompleteRestorationIntentStatus.Superseded
                    ? new CompleteWorkspacePreparationResult.Superseded()
                    : new CompleteWorkspacePreparationResult.Failed(stale);
            }

            NavigationRestorationPreparationResult navigation =
                NavigationTransitions.PrepareRestoration(
                    workspace.Identity,
                    new NavigationEvaluationFacts(
                        scope,
                        resolved.Package,
                        options.FacetAvailability),
                    options.Facets,
                    resolved.Initialization!);
            if (CurrentnessFailure(authority) is { } navigationStale)
                return PreparationForUnavailable(navigationStale);
            if (navigation
                is not NavigationRestorationPreparationResult.Prepared
                    preparedNavigation)
            {
                return new CompleteWorkspacePreparationResult.Failed(
                    new CompleteRestorationFailure.NavigationFailed(
                        navigation));
            }

            ArtifactRootResult<WorkspaceRealizationOperationSnapshot>
                snapshotResult =
                    await workspace.CaptureRealizationOperationSnapshotAsync(
                        token).ConfigureAwait(false);
            if (CurrentnessFailure(authority) is { } snapshotStale)
                return PreparationForUnavailable(snapshotStale);
            if (snapshotResult
                is not ArtifactRootResult<
                    WorkspaceRealizationOperationSnapshot>.Available
                        snapshotAvailable)
            {
                return new CompleteWorkspacePreparationResult.Failed(
                    new CompleteRestorationFailure.SnapshotFailed(
                        ((ArtifactRootResult<
                            WorkspaceRealizationOperationSnapshot>.Rejected)
                                snapshotResult).Failure));
            }
            if (!ReferenceEquals(
                    snapshotAvailable.Value.Definition.Plan,
                    plan.WorkspacePlan))
            {
                return new CompleteWorkspacePreparationResult.Failed(
                    new CompleteRestorationFailure
                                .WorkspaceConstructionFailed(
                                    "The host constructed the Workspace from a "
                                        + "different plan."));
            }

            var snapshot = new CompleteWorkspaceSnapshot(
                snapshotAvailable.Value.Definition,
                snapshotAvailable.Value.Scope,
                contextReceipts.MoveToImmutable(),
                resolved.State!,
                preparedNavigation.Initialization);
            CompleteRestorationProjectionResult projectionResult =
                options.Projection(
                    new CompleteRestorationProjectionRequest(
                        plan.Request,
                        snapshot));
            if (projectionResult is null)
            {
                throw new InvalidOperationException(
                    "The projection provider returned no outcome.");
            }
            if (CurrentnessFailure(authority) is { } projectionStale)
                return PreparationForUnavailable(projectionStale);
            if (projectionResult
                is CompleteRestorationProjectionResult.Failed
                    projectionFailed)
            {
                return new CompleteWorkspacePreparationResult.Failed(
                    new CompleteRestorationFailure.ProjectionFailed(
                        projectionFailed.Message));
            }
            CompleteRestorationProjection projection =
                ((CompleteRestorationProjectionResult.Projected)
                    projectionResult).Projection;
            return new CompleteWorkspacePreparationResult.Prepared(
                new CompleteWorkspaceActivation(
                    plan.Intent,
                    plan.Request,
                    workspace.Identity,
                    snapshot,
                    projection));
        }
        catch (OperationCanceledException)
        {
            return CurrentnessFailure(authority)
                is CompleteRestorationFailure.AuthorityUnavailable
                    authorityFailure
                    && authorityFailure.Status
                        == CompleteRestorationIntentStatus.Superseded
                ? new CompleteWorkspacePreparationResult.Superseded()
                : new CompleteWorkspacePreparationResult.Failed(
                    new CompleteRestorationFailure.Cancelled(
                        "Workspace restoration was cancelled."));
        }
    }

    private static ImmutableArray<PackageRootBinding> DistinctPackageRoots(
        IEnumerable<PackageRootBinding> roots)
    {
        var requests = new HashSet<PackageArtifactRootRequest>();
        var distinct = ImmutableArray.CreateBuilder<PackageRootBinding>();
        foreach (PackageRootBinding root in roots)
        {
            if (requests.Add(PackageArtifactRootRequest.From(root)))
                distinct.Add(root);
        }

        return distinct.ToImmutable();
    }

    private static async ValueTask<ResolvedPreparation> ResolveAsync(
        CompleteRestorationPlan plan,
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope,
        ImmutableArray<PackageRootBinding> roots,
        CompleteRestorationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        if (plan.Recipe is CompleteRestorationRecipe.Version2 version2)
        {
            var facts =
                ImmutableArray.CreateBuilder<
                    CommittedPackageStateResolutionFacts>();
            foreach (NavigationTabDefinition tab
                in version2.Definitions.Navigation?.Tabs ?? [])
            {
                if (tab.Coordinate
                    is not DefinitionMemberCoordinate.PackageCoordinate
                        coordinate)
                {
                    continue;
                }

                PackageEvaluationResult evaluated =
                    await EvaluatePackageAsync(
                        tab.Id,
                        coordinate,
                        workspace,
                        scope,
                        roots,
                        options.PackageSurfaceLimits,
                        cancellationToken).ConfigureAwait(false);
                if (evaluated.Failure is not null)
                    return new(null, null, null, evaluated.Failure);
                facts.Add(new(tab.Id, evaluated.Package!));
            }

            CommittedScenarioSelectorResolutionResult selector =
                CommittedScenarioSelectorResolver.Resolve(
                    version2.Definitions,
                    workspace.Identity,
                    scope,
                    facts.ToImmutable());
            if (selector
                is CommittedScenarioSelectorResolutionResult.Failed failed)
            {
                return new(
                    null,
                    null,
                    null,
                    new CompleteRestorationFailure
                        .SelectorResolutionFailed(failed.Failure));
            }

            CommittedScenarioSelectorResolution resolution =
                ((CommittedScenarioSelectorResolutionResult.Resolved)selector)
                    .Resolution;
            DetachedVersion2Result detached =
                DetachVersion2(resolution, options);
            if (detached.Failure is not null)
                return new(null, null, null, detached.Failure);
            NavigationInitialization initialization =
                resolution.ActiveState
                    is ResolvedCommittedPackageViewState activePackageState
                    ? PreparePackageInitialization(
                        activePackageState.Initialization,
                        activePackageState)
                    : resolution.Activation
                ?? new NavigationInitialization(
                    StructuralSubjectIdentity.ForWorkspace(
                        workspace.Identity));
            NavigationPackageEvaluation? package =
                resolution.ActiveState
                    is ResolvedCommittedPackageViewState packageState
                        ? packageState.Package
                        : null;
            return new(
                initialization,
                package,
                detached.State,
                null);
        }

        var legacy =
            (CompleteRestorationRecipe.LegacyDirectPackage)plan.Recipe;
        ResolvedNavigation navigation =
            legacy.Scenario.Navigation
            ?? throw new InvalidOperationException(
                "A direct-Package legacy recipe requires Navigation.");
        ResolvedNavigationTab focused = navigation.FocusTab;
        var focusedCoordinate =
            (WorkspaceMemberCoordinate.PackageMember)focused.Coordinate;
        var legacyCoordinate =
            new DefinitionMemberCoordinate.PackageCoordinate(
                focusedCoordinate.PackageId,
                focusedCoordinate.Version,
                focusedCoordinate.Framework,
                focusedCoordinate.RuntimeIdentifier);
        PackageEvaluationResult legacyPackage = await EvaluatePackageAsync(
            focused.Id,
            legacyCoordinate,
            workspace,
            scope,
            roots,
            options.PackageSurfaceLimits,
            cancellationToken).ConfigureAwait(false);
        if (legacyPackage.Failure is not null)
            return new(null, null, null, legacyPackage.Failure);

        LegacyInitializationResult lowered =
            LegacyInitialization(
                legacy,
                legacyPackage.Package!,
                options);
        return lowered.Failure is not null
            ? new(null, null, null, lowered.Failure)
            : new(
                lowered.Initialization,
                legacyPackage.Package,
                new CompleteRestorationResolvedState.Legacy(
                    lowered.Initialization!),
                null);
    }

    private static DetachedVersion2Result DetachVersion2(
        CommittedScenarioSelectorResolution resolution,
        CompleteRestorationExecutionOptions options)
    {
        var states =
            ImmutableArray.CreateBuilder<
                CompleteRestorationResolvedViewState>(
                    resolution.States.Length);
        int activeStateIndex = -1;
        for (int index = 0; index < resolution.States.Length; index++)
        {
            ResolvedCommittedViewState state = resolution.States[index];
            NavigationInitialization? initialization =
                (state as ResolvedCommittedNavigableViewState)
                    ?.Initialization;
            if (state
                is ResolvedCommittedPackageViewState resolvedPackageState
                && initialization is not null)
            {
                initialization = PreparePackageInitialization(
                    initialization,
                    resolvedPackageState);
            }
            NavigationLensActivationResult? lensResolution = null;
            if (initialization?.Lens is { } lens)
            {
                NavigationSubjectInventory? inventory =
                    state is ResolvedCommittedPackageViewState packageState
                        ? NavigationWorkspaceSnapshotEvaluation
                            .ClassifySubjectInventory(
                                StructuralSubjectIdentity.ForPackage(
                                    StructuralSubjectIdentity.ForWorkspace(
                                        resolution.Workspace),
                                    packageState.Package.Occurrence
                                        .Occurrence),
                                packageState.Package)
                        : null;
                lensResolution = NavigationLensActivation.ResolveExact(
                    lens,
                    options.Facets,
                    options.FacetAvailability(lens.Subject, inventory));
                if (lensResolution
                    is NavigationLensActivationResult.Rejected rejected)
                {
                    return new(
                        null,
                        new CompleteRestorationFailure
                            .SelectorResolutionFailed(
                                new CommittedSelectorResolutionFailure(
                                    CommittedSelectorResolutionFailureKind
                                        .InvalidFacet,
                                    index,
                                    state.NavigationId,
                                    $"View state {index} facet was rejected: "
                                        + $"{rejected.Rejection}.")));
                }
            }

            states.Add(
                new CompleteRestorationResolvedViewState(
                    state.Definition,
                    initialization,
                    lensResolution));
            if (ReferenceEquals(state, resolution.ActiveState))
                activeStateIndex = index;
        }

        return new(
            new CompleteRestorationResolvedState.Version2(
                resolution.Definitions,
                states.MoveToImmutable(),
                activeStateIndex < 0 ? null : activeStateIndex),
            null);
    }

    private static NavigationInitialization PreparePackageInitialization(
        NavigationInitialization initialization,
        ResolvedCommittedPackageViewState state)
    {
        if (initialization.Context is not null
            || initialization.Subject
                is StructuralSubjectIdentity.WorkspaceSubject)
        {
            return initialization;
        }

        StructuralSubjectIdentity.PackageSubject package =
            StructuralSubjectIdentity.ForPackage(
                StructuralSubjectIdentity.ForWorkspace(
                    state.Package.Occurrence.Occurrence.Identity
                        .WorkspaceIdentity),
                state.Package.Occurrence.Occurrence);
        return initialization with
        {
            Context = new NavigationRetainedSubjectContext(package),
        };
    }

    private static async ValueTask<PackageEvaluationResult>
        EvaluatePackageAsync(
            string navigationId,
            DefinitionMemberCoordinate.PackageCoordinate coordinate,
            InspectionWorkspace workspace,
            WorkspaceScopeSnapshot scope,
            ImmutableArray<PackageRootBinding> roots,
            ApiSurfaceProjectionLimits surfaceLimits,
            CancellationToken cancellationToken)
    {
        WorkspacePackageOccurrenceDescriptor[] occurrences =
        [
            .. scope.Packages.Where(
                occurrence =>
                    CommittedScenarioSelectorResolver.MatchesCoordinate(
                        coordinate,
                        occurrence.Occurrence.Package)),
        ];
        if (occurrences.Length != 1)
        {
            var failure = new CommittedSelectorResolutionFailure(
                occurrences.Length == 0
                    ? CommittedSelectorResolutionFailureKind
                        .PackageOccurrenceMissing
                    : CommittedSelectorResolutionFailureKind
                        .PackageOccurrenceAmbiguous,
                StateIndex: null,
                navigationId,
                occurrences.Length == 0
                    ? $"Navigation row '{navigationId}' matched no Package "
                        + "occurrence in the fresh Workspace."
                    : $"Navigation row '{navigationId}' matched multiple "
                        + "Package occurrences in the fresh Workspace.");
            return new(
                null,
                new CompleteRestorationFailure
                    .SelectorResolutionFailed(failure));
        }

        WorkspacePackageOccurrenceDescriptor occurrence = occurrences[0];
        PackageRootBinding[] bindings =
        [
            .. roots.Where(binding =>
                occurrence.Occurrence.Correspondence
                    is PackageArtifactRootCorrespondence correspondence
                && correspondence.Matches(
                    PackageArtifactRootRequest.From(binding))),
        ];
        if (bindings.Length != 1
            || occurrence.Realization.Status
                is not ArtifactRootRealizationStatus.Ready ready)
        {
            return new(
                null,
                new CompleteRestorationFailure.PackageEvaluationFailed(
                    navigationId,
                    bindings.Length != 1
                        ? "The exact Package Root binding is missing or "
                            + "ambiguous."
                        : "The exact Package occurrence is not ready."));
        }

        PackageRootBinding binding = bindings[0];
        ArtifactRootResult<NavigationPackageEvaluation> evaluated =
            await workspace.ExecutePackageRootQueryAsync(
                (PackageArtifactRootCorrespondence)
                    occurrence.Occurrence.Correspondence,
                ready.Generation,
                (realization, token) =>
                    ValueTask.FromResult(
                        NavigationPackageEvaluationFactory.Create(
                            occurrence,
                            binding,
                            realization,
                            ApiSurfaceScope.PublicWithNonPublicTypes,
                            surfaceLimits,
                            token)),
                cancellationToken: cancellationToken).ConfigureAwait(false);
        return evaluated switch
        {
            ArtifactRootResult<NavigationPackageEvaluation>.Available
                available => new(available.Value, null),
            ArtifactRootResult<NavigationPackageEvaluation>.Rejected rejected =>
                new(
                    null,
                    new CompleteRestorationFailure.PackageEvaluationFailed(
                        navigationId,
                        $"Package Root query rejected: {rejected.Failure}.",
                        rejected.Failure)),
            _ => throw new InvalidOperationException(
                "Unknown Package Root query result."),
        };
    }

    private static LegacyInitializationResult LegacyInitialization(
        CompleteRestorationRecipe.LegacyDirectPackage legacy,
        NavigationPackageEvaluation package,
        CompleteRestorationExecutionOptions options)
    {
        ResolvedScenario scenario = legacy.Scenario;
        ViewDefinition? view = scenario.View;
        StructuralSubjectIdentity.WorkspaceSubject workspace =
            StructuralSubjectIdentity.ForWorkspace(
                package.Occurrence.Occurrence.Identity.WorkspaceIdentity);
        StructuralSubjectIdentity.PackageSubject packageSubject =
            StructuralSubjectIdentity.ForPackage(
                workspace,
                package.Occurrence.Occurrence);
        StructuralSubjectIdentity? subject =
            view is null
                || view.Type is null
                    && view.MemberAnchor is null
                    && view.MemberSignature is null
                    && view.MemberKey is null
                    && view.Lens is null
                    && view.Section is null
                ? null
                : packageSubject;
        var context = new NavigationRetainedSubjectContext(packageSubject);

        string? facet = null;
        if (view?.Type is { } typeSelector)
        {
            NavigationSubjectInventory inventory =
                NavigationWorkspaceSnapshotEvaluation.ClassifySubjectInventory(
                    packageSubject,
                    package);
            if (inventory.Types.Evidence.Any(
                static evidence =>
                    evidence
                        is not NavigationInventoryEvidence
                            .ProjectedMemberIdentityFailure))
            {
                return LegacyFailure(
                    "The legacy Type selector cannot be resolved exactly "
                        + "because the acquired Type inventory is incomplete.");
            }
            NavigationTypeInventoryRow[] matches =
            [
                .. inventory.Libraries
                    .SelectMany(static library => library.Types.Rows)
                    .Where(type => LegacyTypeMatches(
                        legacy.Source,
                        typeSelector,
                        type,
                        package)),
            ];
            if (matches.Length != 1)
            {
                return LegacyFailure(
                    matches.Length == 0
                        ? "The legacy Type selector matched no acquired Type."
                        : "The legacy Type selector is ambiguous.");
            }

            NavigationTypeInventoryRow type = matches[0];
            subject = type.Subject;
            context = new NavigationRetainedSubjectContext(
                packageSubject,
                type.Subject.Library,
                type.Subject);
            if (view.MemberAnchor is not null
                || view.MemberSignature is not null)
            {
                NavigationMemberInventoryRow[] members =
                [
                    .. type.Members.Where(member =>
                        member.ContainingType == type.Subject
                        && member.Subject.DeclaringType == type.Subject
                        && (view.MemberAnchor is { } anchor
                            ? string.Equals(
                                member.Subject.Identity.Member.Fingerprint,
                                anchor,
                                StringComparison.Ordinal)
                            : string.Equals(
                                member.Subject.Identity.Member
                                    .CanonicalSignature,
                                view.MemberSignature,
                                StringComparison.Ordinal))),
                ];
                if (members.Length != 1)
                {
                    return LegacyFailure(
                        members.Length == 0
                            ? "The legacy Member selector matched no acquired "
                                + "Member."
                            : "The legacy Member selector is ambiguous.");
                }

                subject = members[0].Subject;
                if (view.MemberKey is { } memberKey
                    && !string.Equals(
                        memberKey,
                        $"{members[0].ProducerRow.Kind}:"
                            + members[0].ProducerRow.Name,
                        StringComparison.Ordinal))
                {
                    return LegacyFailure(
                        "The legacy memberKey does not match the resolved "
                            + "Member group.");
                }
                context = new NavigationRetainedSubjectContext(
                    packageSubject,
                    type.Subject.Library,
                    type.Subject,
                    members[0].Subject);
            }
            else if (view.MemberKey is not null)
            {
                return LegacyFailure(
                    "A legacy memberKey requires memberAnchor or "
                        + "memberSignature.");
            }
        }
        else if (view?.MemberKey is not null)
        {
            return LegacyFailure(
                "A legacy memberKey requires an exact Type and stable Member "
                    + "selector.");
        }

        LegacyFacetResult mapped =
            MapLegacyFacet(
                legacy.Source,
                view,
                subject?.Kind ?? StructuralSubjectKind.Package);
        if (mapped.Failure is not null)
            return LegacyFailure(mapped.Failure);
        facet = mapped.Facet;
        if (facet is "library.integrations"
            or "library.opportunities"
            or "library.analysis"
            or "library.metadata")
        {
            if (package.Libraries.IsEmpty)
            {
                return LegacyFailure(
                    "The legacy aggregate Library subject has no acquired "
                        + "Libraries.");
            }

            var all = StructuralSubjectIdentity.ForAllLibraries(packageSubject);
            subject = all;
            context = new NavigationRetainedSubjectContext(
                packageSubject,
                all);
        }

        NavigationLensIdentity? lens = facet is null
            ? null
            : new NavigationLensIdentity(
                subject
                    ?? throw new InvalidOperationException(
                        "A mapped legacy facet requires an exact subject."),
                new ViewFacetId(facet));
        if (lens is not null)
        {
            NavigationSubjectInventory inventory =
                NavigationWorkspaceSnapshotEvaluation
                    .ClassifySubjectInventory(packageSubject, package);
            NavigationLensActivationResult resolution =
                NavigationLensActivation.ResolveExact(
                    lens,
                    options.Facets,
                    options.FacetAvailability(lens.Subject, inventory));
            if (resolution is NavigationLensActivationResult.Rejected)
            {
                return LegacyFailure(
                    $"Legacy facet '{facet}' is unknown or inapplicable.");
            }
        }

        return new(
            new NavigationInitialization(subject, context, lens),
            null);
    }

    private static bool LegacyTypeMatches(
        CompleteRestorationLegacySource source,
        string selector,
        NavigationTypeInventoryRow type,
        NavigationPackageEvaluation package)
    {
        if (source is CompleteRestorationLegacySource.DefinitionV1)
        {
            return string.Equals(
                selector,
                type.Subject.Identity.Type.ToMetadataFullName(),
                StringComparison.Ordinal);
        }

        string definitionId =
            type.ProducerRow.DefinitionName?.ToEscapedFullName()
            ?? type.ProducerRow.MetadataName
            ?? type.ProducerRow.Name;
        int duplicates = package.Surface.Assemblies.Assemblies
            .OfType<AssemblyContextEntry<AssemblyApiSurface>.Available>()
            .SelectMany(static entry => entry.Value.Surface.Types)
            .Count(candidate =>
                candidate.DefinitionName == type.ProducerRow.DefinitionName
                && string.Equals(
                    candidate.Namespace ?? "",
                    type.ProducerRow.Namespace ?? "",
                    StringComparison.Ordinal)
                && string.Equals(
                    candidate.MetadataName ?? candidate.Name,
                    type.ProducerRow.MetadataName ?? type.ProducerRow.Name,
                    StringComparison.Ordinal));
        string key = duplicates > 1
            ? $"{LibraryFor(type, package).Asset.AssemblyName}:"
                + definitionId
            : definitionId;
        return string.Equals(selector, key, StringComparison.Ordinal);
    }

    private static NavigationLibraryEvaluation LibraryFor(
        NavigationTypeInventoryRow type,
        NavigationPackageEvaluation package) =>
        package.Libraries.Single(
            library =>
                StructuralSubjectIdentity.ForLibrary(
                    type.Subject.Library.Package,
                    library.Library)
                == type.Subject.Library);

    private static LegacyFacetResult MapLegacyFacet(
        CompleteRestorationLegacySource source,
        ViewDefinition? view,
        StructuralSubjectKind subject)
    {
        string? lens = view?.Lens;
        string? section = view?.Section;
        if (subject is StructuralSubjectKind.Member)
        {
            if (lens is not null
                && lens is not "api"
                and not "metadata"
                and not "source")
            {
                return new(null, "The legacy parent Type lens is invalid.");
            }

            return (source, section) switch
            {
                (_, null) =>
                    new("member.overview", null),
                (CompleteRestorationLegacySource.PacketV1, "overview") =>
                    new("member.overview", null),
                (CompleteRestorationLegacySource.PacketV1, "call-graph") =>
                    new("member.call-graph", null),
                (CompleteRestorationLegacySource.PacketV1, "facts") =>
                    new("member.facts", null),
                (CompleteRestorationLegacySource.PacketV1, "source") =>
                    new("member.source", null),
                (CompleteRestorationLegacySource.PacketV1, "annotated") =>
                    new("member.annotated-source", null),
                (CompleteRestorationLegacySource.DefinitionV1, "Call Graph") =>
                    new("member.call-graph", null),
                _ => new(null, $"Unknown legacy Member section '{section}'."),
            };
        }

        if (subject is StructuralSubjectKind.Type)
        {
            if (section is not null)
            {
                if (lens is not null)
                {
                    return new(
                        null,
                        "A legacy Type view cannot carry both lens and "
                            + "section.");
                }
                return source
                        is CompleteRestorationLegacySource.DefinitionV1
                    && section == "Methods"
                    ? new("type.api", null)
                    : new(
                        null,
                        $"Unknown legacy Type section '{section}'.");
            }

            return lens switch
            {
                null => new(null, null),
                "api" => new("type.api", null),
                "metadata" => new("type.metadata", null),
                "source" => new("type.source", null),
                _ => new(null, $"Unknown legacy Type lens '{lens}'."),
            };
        }

        if (section is not null)
        {
            return new(
                null,
                "A legacy Package view cannot carry a member section.");
        }

        return lens switch
        {
            null => new(null, null),
            "overview" => new("package.overview", null),
            "dependencies" => new("package.dependencies", null),
            "integrations" => new("library.integrations", null),
            "opportunities" => new("library.opportunities", null),
            "analysis" => new("library.analysis", null),
            "metadata" => new("library.metadata", null),
            _ => new(null, $"Unknown legacy Package lens '{lens}'."),
        };
    }

    private static LegacyInitializationResult LegacyFailure(string message) =>
        new(
            null,
            new CompleteRestorationFailure.LegacyLoweringFailed(message));

    private static CompleteWorkspacePreparationResult
        PreparationForUnavailable(
            CompleteRestorationFailure failure) =>
        failure is CompleteRestorationFailure.AuthorityUnavailable
            authorityFailure
                && authorityFailure.Status
                    == CompleteRestorationIntentStatus.Superseded
            ? new CompleteWorkspacePreparationResult.Superseded()
            : new CompleteWorkspacePreparationResult.Failed(failure);

    private static CompleteRestorationResult<TActivation>
        ResultForUnavailable<TActivation>(
            CompleteRestorationIntentIdentity intent,
            CompleteRestorationRequestBasis request,
            CompleteRestorationFailure failure) =>
        failure is CompleteRestorationFailure.AuthorityUnavailable
            authorityFailure
                && authorityFailure.Status
                    == CompleteRestorationIntentStatus.Superseded
            ? new CompleteRestorationResult<TActivation>.Superseded(
                intent,
                request)
            : new CompleteRestorationResult<TActivation>.Failed(
                intent,
                request,
                failure);

    private static CompleteRestorationFailure? CurrentnessFailure(
        ICompleteRestorationIntentAuthority authority) =>
        authority.Status switch
        {
            CompleteRestorationIntentStatus.Current
                when !authority.Revocation.IsCancellationRequested => null,
            CompleteRestorationIntentStatus.Current =>
                new CompleteRestorationFailure.Cancelled(
                    "The restoration intent was cancelled."),
            CompleteRestorationIntentStatus.Superseded =>
                new CompleteRestorationFailure.AuthorityUnavailable(
                    CompleteRestorationIntentStatus.Superseded,
                    "The restoration intent was superseded."),
            CompleteRestorationIntentStatus status =>
                new CompleteRestorationFailure.AuthorityUnavailable(
                    status,
                    $"The restoration intent is "
                        + $"{status.ToString().ToLowerInvariant()}."),
        };

    private sealed record ResolvedPreparation(
        NavigationInitialization? Initialization,
        NavigationPackageEvaluation? Package,
        CompleteRestorationResolvedState? State,
        CompleteRestorationFailure? Failure);

    private sealed record PackageEvaluationResult(
        NavigationPackageEvaluation? Package,
        CompleteRestorationFailure? Failure);

    private sealed record LegacyInitializationResult(
        NavigationInitialization? Initialization,
        CompleteRestorationFailure? Failure);

    private sealed record LegacyFacetResult(
        string? Facet,
        string? Failure);

    private sealed record DetachedVersion2Result(
        CompleteRestorationResolvedState.Version2? State,
        CompleteRestorationFailure? Failure);
}
