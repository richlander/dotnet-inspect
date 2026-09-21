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

    public bool CaptureInventory { get; init; }

    public ApiSurfaceProjectionLimits PlatformSurfaceLimits { get; init; } =
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

    public sealed record Version3(
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

    public sealed record Version4(
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
        if (request.Request is CompleteRestorationRequestBasis.PacketInput packet)
        {
            return new CompleteRestorationProjectionResult.Projected(
                new CompleteRestorationProjection.Projectable(packet.Encoded));
        }

        CommittedScenarioDefinitionSet definitions =
            request.Snapshot.Resolved switch
            {
                CompleteRestorationResolvedState.Version2 version2 =>
                    version2.Definitions,
                CompleteRestorationResolvedState.Version3 version3 =>
                    version3.Definitions,
                CompleteRestorationResolvedState.Version4 version4 =>
                    version4.Definitions,
                _ => throw new InvalidOperationException(
                    "Unknown complete restoration resolved state."),
            };
        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(definitions);
        if (projection.Succeeded)
        {
            return new CompleteRestorationProjectionResult.Projected(
                new CompleteRestorationProjection.Projectable(
                    WorkspaceSharePacketCodec.Encode(
                        projection.Packet
                        ?? throw new InvalidOperationException(
                            "A successful projection requires a packet."))));
        }

        WorkspaceSharePacketProjectionFailure failure =
            projection.Failure
            ?? throw new InvalidOperationException(
                "A failed projection requires a failure.");
        return failure.Kind switch
        {
            WorkspaceSharePacketProjectionFailureKind.NonProjectable =>
                new CompleteRestorationProjectionResult.Projected(
                    new CompleteRestorationProjection.NonProjectable(
                        failure.Message)),
            WorkspaceSharePacketProjectionFailureKind.InvalidDefinitionSet =>
                new CompleteRestorationProjectionResult.Failed(
                    failure.Message),
            _ => throw new InvalidOperationException(
                "Unknown Workspace share projection failure."),
        };
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
        NavigationOperationInitialization navigation,
        CompleteRestorationInventory? inventory)
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
        Inventory = inventory;
    }

    public WorkspaceDefinitionSnapshot Definition { get; }

    public WorkspaceScopeSnapshot Scope { get; }

    public ImmutableArray<WorkspaceDeclarationContextReceipt> Contexts
    {
        get;
    }

    public CompleteRestorationResolvedState Resolved { get; }

    public NavigationOperationInitialization Navigation { get; }

    /// <summary>Explicitly requested detached inventory, or null when omitted.</summary>
    public CompleteRestorationInventory? Inventory { get; }
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
        ImmutableArray<WorkspaceDeclarationContext> contexts,
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
        if (contexts.IsDefault
            || contexts.Length != snapshot.Contexts.Length
            || contexts.Any(static context => context is null))
        {
            throw new ArgumentException(
                "Activation contexts must match the prepared context receipts.",
                nameof(contexts));
        }
        for (int index = 0; index < contexts.Length; index++)
        {
            if (!ReferenceEquals(
                    contexts[index].Receipt,
                    snapshot.Contexts[index]))
            {
                throw new ArgumentException(
                    "Activation contexts must retain the exact prepared "
                        + "context receipts.",
                    nameof(contexts));
            }
        }
        if (snapshot.Definition.Workspace != workspace
            || snapshot.Scope.Revision.Workspace != workspace
            || snapshot.Navigation.State.Workspace != workspace)
        {
            throw new ArgumentException(
                "Every activation component must belong to the exact "
                    + "prepared Workspace.",
                nameof(snapshot));
        }

        Contexts = contexts;
        CommittedScenarioDefinitionSet definitions = snapshot.Resolved switch
        {
            CompleteRestorationResolvedState.Version2 version2 =>
                version2.Definitions,
            CompleteRestorationResolvedState.Version3 version3 =>
                version3.Definitions,
            CompleteRestorationResolvedState.Version4 version4 =>
                version4.Definitions,
            _ => throw new InvalidOperationException(
                "Unknown complete-restoration resolved state."),
        };
        if (definitions.Scenario.Context is { } selectedContext)
        {
            WorkspaceDefinition workspaceDefinition =
                definitions.Workspace
                ?? throw new InvalidOperationException(
                    "A selected context requires a Workspace definition.");
            int selectedIndex = workspaceDefinition.Contexts
                .Select((context, index) => (context, index))
                .Where(item => string.Equals(
                    item.context.Name,
                    selectedContext,
                    StringComparison.Ordinal))
                .Select(static item => item.index)
                .Single();
            SelectedContext = contexts[selectedIndex];
        }
    }

    public CompleteRestorationIntentIdentity Intent { get; }

    public CompleteRestorationRequestBasis Request { get; }

    public InspectionWorkspaceIdentity Workspace { get; }

    /// <summary>
    /// Live contexts retained only for the lifetime of the paired host
    /// activation.
    /// </summary>
    public ImmutableArray<WorkspaceDeclarationContext> Contexts { get; }

    /// <summary>The exact context selected by the restored scenario.</summary>
    public WorkspaceDeclarationContext? SelectedContext { get; }

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

internal sealed record CompleteRestorationInvocation(
    ImmutableArray<PackageRootBinding> PackageRoots,
    IReadOnlyDictionary<string, PackageArtifactRootRequest> PackageRequests,
    IReadOnlyDictionary<string, NavigationPackageEvaluation>
        PackageEvaluations);

/// <summary>
/// One exact ready Package occurrence exposed only while complete restoration
/// still owns its live package evidence.
/// </summary>
public sealed record CompleteRestorationReadyPackage
{
    internal CompleteRestorationReadyPackage(
        string navigationId,
        string consumerPackageSubjectId,
        PackageRootBinding binding,
        NavigationPackageEvaluation evaluation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(navigationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumerPackageSubjectId);
        NavigationId = navigationId;
        ConsumerPackageSubjectId = consumerPackageSubjectId;
        Binding = binding ?? throw new ArgumentNullException(nameof(binding));
        Evaluation = evaluation
            ?? throw new ArgumentNullException(nameof(evaluation));
    }

    public string NavigationId { get; }

    public string ConsumerPackageSubjectId { get; }

    public PackageRootBinding Binding { get; }

    public NavigationPackageEvaluation Evaluation { get; }
}

/// <summary>
/// Validated product-order package evidence for a detached host projection.
/// The callback must not retain live bindings or evaluations after returning.
/// </summary>
public sealed record CompleteRestorationReadyProjection
{
    internal CompleteRestorationReadyProjection(
        ImmutableArray<CompleteRestorationReadyPackage> packages)
    {
        if (packages.IsDefault
            || packages.Any(static package => package is null))
        {
            throw new ArgumentException(
                "Ready Packages must be an initialized immutable array.",
                nameof(packages));
        }
        Packages = packages;
    }

    public ImmutableArray<CompleteRestorationReadyPackage> Packages { get; }
}

public delegate ValueTask CompleteRestorationProjectionOperation(
    CompleteWorkspaceActivation activation,
    CompleteRestorationReadyProjection projection,
    CancellationToken cancellationToken);

internal delegate ValueTask<ImmutableArray<PackageRootBinding>>
    CompleteRestorationPackageRootsOperation(
        ImmutableArray<PackageRootBinding> packageRoots,
        IReadOnlyDictionary<string, PackageArtifactRootRequest> packageRequests,
        CancellationToken cancellationToken);

internal delegate ValueTask CompleteRestorationWorkspaceOperation(
    InspectionWorkspace workspace,
    CompleteWorkspaceActivation activation,
    CompleteRestorationInvocation invocation,
    CancellationToken cancellationToken);

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
    public static ValueTask<CompleteRestorationResult<TActivation>>
        RestoreAsync<TActivation>(
            CompleteRestorationPreparationResult preparation,
            ICompleteRestorationIntentAuthority authority,
            ICompleteRestorationHost<TActivation> host,
            CompleteRestorationExecutionOptions options,
            CancellationToken cancellationToken = default) =>
        RestoreCoreAsync(
            preparation,
            authority,
            host,
            options,
            packageRootsOperation: null,
            operation: null,
            cancellationToken);

    public static ValueTask<CompleteRestorationResult<TActivation>>
        RestoreWithProjectionAsync<TActivation>(
            CompleteRestorationPreparationResult preparation,
            ICompleteRestorationIntentAuthority authority,
            ICompleteRestorationHost<TActivation> host,
            CompleteRestorationExecutionOptions options,
            CompleteRestorationProjectionOperation operation,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return RestoreCoreAsync(
            preparation,
            authority,
            host,
            options,
            packageRootsOperation: null,
            async (_, activation, invocation, token) =>
                await operation(
                    activation,
                    CreateReadyProjection(activation, invocation),
                    token).ConfigureAwait(false),
            cancellationToken);
    }

    internal static ValueTask<CompleteRestorationResult<TActivation>>
        RestoreWithOperationAsync<TActivation>(
            CompleteRestorationPreparationResult preparation,
            ICompleteRestorationIntentAuthority authority,
            ICompleteRestorationHost<TActivation> host,
            CompleteRestorationExecutionOptions options,
            CompleteRestorationPackageRootsOperation packageRootsOperation,
            CompleteRestorationWorkspaceOperation operation,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(packageRootsOperation);
        ArgumentNullException.ThrowIfNull(operation);
        return RestoreCoreAsync(
            preparation,
            authority,
            host,
            options,
            packageRootsOperation,
            operation,
            cancellationToken);
    }

    private static async ValueTask<CompleteRestorationResult<TActivation>>
        RestoreCoreAsync<TActivation>(
            CompleteRestorationPreparationResult preparation,
            ICompleteRestorationIntentAuthority authority,
            ICompleteRestorationHost<TActivation> host,
            CompleteRestorationExecutionOptions options,
            CompleteRestorationPackageRootsOperation? packageRootsOperation,
            CompleteRestorationWorkspaceOperation? operation,
            CancellationToken cancellationToken)
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
        if (FindInvalidFacet(ready.Plan.Recipe, options.Facets)
            is { } invalidFacet)
        {
            return new CompleteRestorationResult<TActivation>.Failed(
                preparation.Intent,
                preparation.Request,
                new CompleteRestorationFailure.SelectorResolutionFailed(
                    invalidFacet));
        }

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
                            packageRootsOperation,
                            operation,
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

    private static CommittedSelectorResolutionFailure? FindInvalidFacet(
        CompleteRestorationRecipe recipe,
        ViewFacetRegistry facets)
    {
        CommittedScenarioDefinitionSet definitions = recipe switch
        {
            CompleteRestorationRecipe.Version2 version2 =>
                version2.Definitions,
            CompleteRestorationRecipe.Version3 version3 =>
                version3.Definitions,
            CompleteRestorationRecipe.Version4 version4 =>
                version4.Definitions,
            _ => throw new InvalidOperationException(
                "Unknown complete restoration recipe."),
        };
        if (definitions.View is not { } view)
            return null;

        for (int index = 0; index < view.States.Count; index++)
        {
            CommittedViewStateDefinition state = view.States[index];
            if (state.Facet is not { } facet)
                continue;

            if (!facets.TryGetDescriptor(
                facet,
                out ViewFacetDescriptor? descriptor))
            {
                return new CommittedSelectorResolutionFailure(
                    CommittedSelectorResolutionFailureKind.InvalidFacet,
                    index,
                    state.Navigation,
                    $"View state {index} facet '{facet}' is not registered.");
            }

            StructuralSubjectKind subjectKind = state.Subject switch
            {
                PortableSubjectRequest.Workspace =>
                    StructuralSubjectKind.Workspace,
                PortableSubjectRequest.Package =>
                    StructuralSubjectKind.Package,
                PortableSubjectRequest.Library =>
                    StructuralSubjectKind.Library,
                PortableSubjectRequest.Type =>
                    StructuralSubjectKind.Type,
                PortableSubjectRequest.Member =>
                    StructuralSubjectKind.Member,
                _ => throw new InvalidOperationException(
                    "An exact committed facet requires an exact subject."),
            };
            if (descriptor.Kind != subjectKind)
            {
                return new CommittedSelectorResolutionFailure(
                    CommittedSelectorResolutionFailureKind.InvalidFacet,
                    index,
                    state.Navigation,
                    $"View state {index} facet '{facet}' does not apply to "
                        + $"the {subjectKind} subject.");
            }
        }

        return null;
    }

    private static async ValueTask<CompleteWorkspacePreparationResult>
        PrepareWorkspaceAsync(
            CompleteRestorationPlan plan,
            InspectionWorkspace workspace,
            ICompleteRestorationIntentAuthority authority,
            CompleteRestorationExecutionOptions options,
            CompleteRestorationPackageRootsOperation? packageRootsOperation,
            CompleteRestorationWorkspaceOperation? operation,
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
            var contexts =
                ImmutableArray.CreateBuilder<WorkspaceDeclarationContext>(
                    plan.WorkspacePlan.Contexts.Length);
            var contextLoads =
                ImmutableArray.CreateBuilder<
                    WorkspaceContextLoadOutcome.Loaded>(
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
                contexts.Add(context);
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

                contextLoads.Add(loaded);
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

            ImmutableArray<WorkspaceContextLoadOutcome.Loaded> loadedContexts =
                contextLoads.MoveToImmutable();
            PackageNavigationRequestResolution packageRequests =
                ResolvePackageNavigationRequests(
                    plan,
                    loadedContexts);
            if (packageRequests.Failure is not null)
            {
                return new CompleteWorkspacePreparationResult.Failed(
                    packageRequests.Failure);
            }
            ImmutableArray<PackageRootBinding> roots =
                DistinctPackageRoots(packageRoots);
            if (packageRootsOperation is not null)
            {
                roots = await packageRootsOperation(
                    roots,
                    packageRequests.Requests!,
                    token).ConfigureAwait(false);
                if (roots.IsDefault
                    || roots.Any(static root => root is null))
                {
                    throw new InvalidOperationException(
                    "The Package Root operation returned an invalid collection.");
                }
            }
            WorkspaceScopeOperationResult scopeResult =
                await workspace.AddPackagesAsync(
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
                packageRequests.Requests!,
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

            CompleteRestorationInventory? inventory = null;
            if (options.CaptureInventory)
            {
                CompleteRestorationInventoryCapture captured =
                    CompleteRestorationInventories.Capture(
                        plan,
                        loadedContexts,
                        roots,
                        packageRequests.Requests!,
                        resolved.PackageEvaluations!,
                        options.PlatformSurfaceLimits,
                        token);
                if (captured.Failure is { } inventoryFailure)
                    return new CompleteWorkspacePreparationResult.Failed(
                        inventoryFailure);
                inventory = captured.Inventory;
            }

            var snapshot = new CompleteWorkspaceSnapshot(
                snapshotAvailable.Value.Definition,
                snapshotAvailable.Value.Scope,
                contextReceipts.MoveToImmutable(),
                resolved.State!,
                preparedNavigation.Initialization,
                inventory);
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
            var activation = new CompleteWorkspaceActivation(
                plan.Intent,
                plan.Request,
                workspace.Identity,
                contexts.MoveToImmutable(),
                snapshot,
                projection);
            if (operation is not null)
            {
                await operation(
                    workspace,
                    activation,
                    new CompleteRestorationInvocation(
                        roots,
                        packageRequests.Requests!,
                        resolved.PackageEvaluations!),
                    token).ConfigureAwait(false);
            }

            return new CompleteWorkspacePreparationResult.Prepared(activation);
        }
        catch (OperationCanceledException)
        {
            return CurrentnessFailure(authority) is { } unavailable
                ? PreparationForUnavailable(unavailable)
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

    private static CompleteRestorationReadyProjection CreateReadyProjection(
        CompleteWorkspaceActivation activation,
        CompleteRestorationInvocation invocation)
    {
        NavigationOperationInitialization navigation =
            activation.Snapshot.Navigation;
        ImmutableArray<NavigationPackageDescriptor> productPackages =
            navigation.State.CurrentSnapshot.Packages;
        ImmutableArray<NavigationConsumerPackageDescriptor> consumerPackages =
            navigation.Result.Consumer.Snapshot.Packages;
        if (productPackages.Length != consumerPackages.Length)
        {
            throw new InvalidOperationException(
                "The restored Navigation state and consumer projection "
                    + "disagree on Package count.");
        }

        var ready =
            ImmutableArray.CreateBuilder<CompleteRestorationReadyPackage>(
                invocation.PackageEvaluations.Count);
        var navigationIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (NavigationPackageDescriptor product in productPackages)
        {
            KeyValuePair<string, NavigationPackageEvaluation>[] evaluations =
            [
                .. invocation.PackageEvaluations.Where(
                    pair => ReferenceEquals(
                        pair.Value.Occurrence.Occurrence,
                        product.Occurrence)),
            ];
            if (evaluations.Length == 0)
                continue;
            if (evaluations.Length != 1
                || !navigationIds.Add(evaluations[0].Key))
            {
                throw new InvalidOperationException(
                    "A restored ready Package occurrence did not map to one "
                        + "unique Navigation row.");
            }

            string navigationId = evaluations[0].Key;
            NavigationPackageEvaluation evaluation = evaluations[0].Value;
            if (!invocation.PackageRequests.TryGetValue(
                    navigationId,
                    out PackageArtifactRootRequest request))
            {
                throw new InvalidOperationException(
                    $"Restored Navigation row '{navigationId}' omitted its "
                        + "exact Package request.");
            }

            PackageRootBinding[] bindings =
            [
                .. invocation.PackageRoots.Where(
                    binding => PackageArtifactRootRequest.From(binding)
                        == request),
            ];
            if (bindings.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Restored Navigation row '{navigationId}' did not retain "
                        + "one exact Package Root binding.");
            }

            NavigationConsumerPackageDescriptor[] consumers =
            [
                .. consumerPackages.Where(
                    package => package.Order == product.Order),
            ];
            if (consumers.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Restored Navigation row '{navigationId}' did not map to "
                        + "one consumer Package subject.");
            }

            ready.Add(new(
                navigationId,
                consumers[0].Subject.Id,
                bindings[0],
                evaluation));
        }
        if (ready.Count != invocation.PackageEvaluations.Count)
        {
            throw new InvalidOperationException(
                "Complete restoration omitted a ready Navigation Package "
                    + "from the projection.");
        }
        return new(ready.MoveToImmutable());
    }

    private static PackageNavigationRequestResolution
        ResolvePackageNavigationRequests(
            CompleteRestorationPlan plan,
            ImmutableArray<WorkspaceContextLoadOutcome.Loaded> contexts)
    {
        var requests =
            new Dictionary<string, PackageArtifactRootRequest>(
                plan.PackageSources.Count,
                StringComparer.Ordinal);
        foreach ((string navigationId, PackageNavigationSource source)
            in plan.PackageSources)
        {
            if (source.ContextIndex < 0
                || source.ContextIndex >= contexts.Length
                || source.ContextIndex >= plan.WorkspacePlan.Contexts.Length)
            {
                return PackageRequestFailure(
                    navigationId,
                    "references an unavailable realized context.");
            }

            WorkspaceContextInput input =
                plan.WorkspacePlan.Contexts[source.ContextIndex];
            if (source.MemberIndex < 0
                || source.MemberIndex >= input.Members.Count)
            {
                return PackageRequestFailure(
                    navigationId,
                    "references an unavailable declared member.");
            }

            WorkspaceMemberCoordinate declared =
                input.Members[source.MemberIndex];
            RealizedMemberCoordinate.Package[] realized =
            [
                .. contexts[source.ContextIndex].Members
                    .Where(member =>
                        member.Declared == declared)
                    .Select(member => member.Realized)
                    .OfType<RealizedMemberCoordinate.Package>()
                    .Distinct(),
            ];
            if (realized.Length != 1)
            {
                return PackageRequestFailure(
                    navigationId,
                    realized.Length == 0
                        ? "has no realized Package declaration."
                        : "has multiple realized Package declarations.");
            }

            PackageRootBinding[] bindings =
            [
                .. contexts[source.ContextIndex].PackageRoots.Where(
                    binding =>
                    {
                        PackageArtifactRootRequest request =
                            PackageArtifactRootRequest.From(binding);
                        return request.Coordinate == realized[0]
                            && string.Equals(
                            request.CompileTargetFramework,
                                source.EffectiveCoordinate.Framework,
                                StringComparison.Ordinal)
                            && string.Equals(
                                request.SelectionRuntimeIdentifier,
                                source.EffectiveCoordinate.RuntimeIdentifier,
                                StringComparison.Ordinal);
                    }),
            ];
            if (bindings.Length != 1)
            {
                return PackageRequestFailure(
                    navigationId,
                    bindings.Length == 0
                        ? "has no acquired Package Root."
                        : "has multiple acquired Package Roots.");
            }

            requests.Add(
                navigationId,
                PackageArtifactRootRequest.From(bindings[0]));
        }

        return new(requests, null);
    }

    private static PackageNavigationRequestResolution PackageRequestFailure(
        string navigationId,
        string message) =>
        new(
            null,
            new CompleteRestorationFailure.SelectorResolutionFailed(
                new CommittedSelectorResolutionFailure(
                    CommittedSelectorResolutionFailureKind
                        .PackageOccurrenceMissing,
                    StateIndex: null,
                    navigationId,
                    $"Navigation row '{navigationId}' {message}")));

    private static async ValueTask<ResolvedPreparation> ResolveAsync(
        CompleteRestorationPlan plan,
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope,
        ImmutableArray<PackageRootBinding> roots,
        IReadOnlyDictionary<string, PackageArtifactRootRequest>
            packageRequests,
        CompleteRestorationExecutionOptions options,
        CancellationToken cancellationToken)
    {
        (CommittedScenarioDefinitionSet definitions, int schemaVersion) =
            plan.Recipe switch
            {
                CompleteRestorationRecipe.Version2 version2 =>
                    (version2.Definitions,
                        InspectionDefinitionSchema.Version2),
                CompleteRestorationRecipe.Version3 version3 =>
                    (version3.Definitions,
                        InspectionDefinitionSchema.Version3),
                CompleteRestorationRecipe.Version4 version4 =>
                    (version4.Definitions,
                        InspectionDefinitionSchema.Version4),
                _ => throw new InvalidOperationException(
                    "Unknown complete restoration recipe."),
            };
        var facts =
            ImmutableArray.CreateBuilder<
                CommittedPackageStateResolutionFacts>();
        var packageEvaluations =
            new Dictionary<string, NavigationPackageEvaluation>(
                StringComparer.Ordinal);
        foreach (NavigationTabDefinition tab
            in definitions.Navigation?.Tabs ?? [])
        {
            if (tab.Coordinate
                is not DefinitionMemberCoordinate.PackageCoordinate)
            {
                continue;
            }
            if (!packageRequests.TryGetValue(
                tab.Id,
                out PackageArtifactRootRequest packageRequest))
            {
                throw new InvalidOperationException(
                    $"Prepared navigation row '{tab.Id}' has no exact "
                        + "Package request.");
            }

            PackageEvaluationResult evaluated =
                await EvaluatePackageAsync(
                    tab.Id,
                    packageRequest,
                    workspace,
                    scope,
                    roots,
                    options.PackageSurfaceLimits,
                    cancellationToken).ConfigureAwait(false);
            if (evaluated.Failure is not null)
                return new(null, null, null, null, evaluated.Failure);
            facts.Add(new(tab.Id, evaluated.Package!));
            packageEvaluations.Add(tab.Id, evaluated.Package!);
        }

        CommittedScenarioSelectorResolutionResult selector =
            CommittedScenarioSelectorResolver.Resolve(
                definitions,
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
                null,
                new CompleteRestorationFailure
                    .SelectorResolutionFailed(failed.Failure));
        }

        CommittedScenarioSelectorResolution resolution =
            ((CommittedScenarioSelectorResolutionResult.Resolved)selector)
                .Resolution;
        DetachedCommittedResult detached =
            DetachCommitted(resolution, schemaVersion, options);
        if (detached.Failure is not null)
            return new(null, null, null, null, detached.Failure);
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
            packageEvaluations,
            null);
    }

    private static DetachedCommittedResult DetachCommitted(
        CommittedScenarioSelectorResolution resolution,
        int schemaVersion,
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

        ImmutableArray<CompleteRestorationResolvedViewState> detachedStates =
            states.MoveToImmutable();
        int? detachedActiveStateIndex =
            activeStateIndex < 0 ? null : activeStateIndex;
        CompleteRestorationResolvedState resolvedState = schemaVersion switch
        {
            InspectionDefinitionSchema.Version2 =>
                new CompleteRestorationResolvedState.Version2(
                    resolution.Definitions,
                    detachedStates,
                    detachedActiveStateIndex),
            InspectionDefinitionSchema.Version3 =>
                new CompleteRestorationResolvedState.Version3(
                    resolution.Definitions,
                    detachedStates,
                    detachedActiveStateIndex),
            InspectionDefinitionSchema.Version4 =>
                new CompleteRestorationResolvedState.Version4(
                    resolution.Definitions,
                    detachedStates,
                    detachedActiveStateIndex),
            _ => throw new InvalidOperationException(
                "Unknown committed definition schema version."),
        };
        return new(resolvedState, null);
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
            PackageArtifactRootRequest request,
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
                    occurrence.Occurrence.Correspondence
                        is PackageArtifactRootCorrespondence correspondence
                    && correspondence.Matches(request)),
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
                PackageArtifactRootRequest.From(binding) == request),
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
        IReadOnlyDictionary<string, NavigationPackageEvaluation>?
            PackageEvaluations,
        CompleteRestorationFailure? Failure);

    private sealed record PackageEvaluationResult(
        NavigationPackageEvaluation? Package,
        CompleteRestorationFailure? Failure);

    private sealed record PackageNavigationRequestResolution(
        IReadOnlyDictionary<string, PackageArtifactRootRequest>? Requests,
        CompleteRestorationFailure? Failure);

    private sealed record DetachedCommittedResult(
        CompleteRestorationResolvedState? State,
        CompleteRestorationFailure? Failure);
}
