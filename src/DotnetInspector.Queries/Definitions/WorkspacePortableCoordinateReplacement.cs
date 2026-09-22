using System.Collections.Immutable;

using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Definitions;

public sealed record WorkspacePackageCoordinateReplacementRequest
{
    public WorkspacePackageCoordinateReplacementRequest(
        string component,
        string? version = null,
        string? framework = null)
        : this(ParseComponent(component), version, framework)
    {
    }

    public WorkspacePackageCoordinateReplacementRequest(
        WorkspacePackageComponentPath component,
        string? version = null,
        string? framework = null)
    {
        Component = component
            ?? throw new ArgumentNullException(nameof(component));
        if (version is not null && string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version must not be blank.", nameof(version));
        if (framework is not null && string.IsNullOrWhiteSpace(framework))
        {
            throw new ArgumentException(
                "Framework must not be blank.",
                nameof(framework));
        }
        if (version is null && framework is null)
        {
            throw new ArgumentException(
                "At least one destination Version or Framework is required.");
        }

        Version = version;
        Framework = framework;
    }

    public WorkspacePackageComponentPath Component { get; }

    public string? Version { get; }

    public string? Framework { get; }

    private static WorkspacePackageComponentPath ParseComponent(
        string component)
    {
        if (!WorkspacePackageComponentPath.TryCreate(
                component,
                out WorkspacePackageComponentPath? path,
                out string? error))
        {
            throw new ArgumentException(error, nameof(component));
        }
        return path!;
    }
}

public enum WorkspacePortableCoordinateReplacementFailureKind
{
    InvalidInput,
    QueryStateUnsupported,
    NavigationSourceMissing,
    NavigationSourceNotDirectPackage,
    NavigationSourceAmbiguous,
    FloatingSourceUnsupported,
    SharedFrameworkChangeUnsupported,
    InvalidDestination,
    DuplicateDestinationNavigationSource,
    InputRestorationFailed,
    DestinationAcquisitionFailed,
    DestinationScopeUnavailable,
    DestinationAdmissionFailed,
    NavigationSuccessorFailed,
    NavigationSuccessorNotPrepared,
    DerivedDefinitionInvalid,
    DerivedDefinitionNonProjectable,
    Cancelled,
    CleanupFailed,
}

public sealed record WorkspacePortableCoordinateReplacementFailure
{
    public WorkspacePortableCoordinateReplacementFailure(
        WorkspacePortableCoordinateReplacementFailureKind kind,
        string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        Kind = kind;
        Detail = detail;
    }

    public WorkspacePortableCoordinateReplacementFailureKind Kind { get; }

    public string Detail { get; }
}

public sealed record WorkspacePortableCoordinateReplacementResult
{
    private WorkspacePortableCoordinateReplacementResult(
        CommittedScenarioDefinitionSet? definitions,
        WorkspacePortableCoordinateReplacementFailure? failure,
        WorkspaceScopeOperationResult? scopeResult,
        NavigationOperationResult? navigationResult,
        NavigationCoordinateRetentionResult? coordinateRetention,
        NavigationRestorationPreparationResult? navigationPreparation,
        NavigationCoordinateSuccessorFailure? navigationSuccessorFailure)
    {
        if ((definitions is null) == (failure is null))
        {
            throw new ArgumentException(
                "A replacement result requires exactly one of Definitions or Failure.");
        }
        if (definitions is not null
            && (scopeResult is null
                || navigationResult is null
                || coordinateRetention is null))
        {
            throw new ArgumentException(
                "A successful replacement requires its actual destination Scope, Navigation, and retention results.");
        }
        if (navigationPreparation is NavigationRestorationPreparationResult.Prepared)
        {
            throw new ArgumentException(
                "A portable replacement result cannot retain live destination Navigation state.");
        }
        if (navigationPreparation is not null
            && navigationSuccessorFailure is not null)
        {
            throw new ArgumentException(
                "A replacement result cannot contain both Navigation non-preparation and failure.");
        }

        Definitions = definitions;
        Failure = failure;
        ScopeResult = scopeResult;
        NavigationResult = navigationResult;
        CoordinateRetention = coordinateRetention;
        NavigationPreparation = navigationPreparation;
        NavigationSuccessorFailure = navigationSuccessorFailure;
    }

    public CommittedScenarioDefinitionSet? Definitions { get; }

    public WorkspacePortableCoordinateReplacementFailure? Failure { get; }

    public WorkspaceScopeOperationResult? ScopeResult { get; }

    public NavigationOperationResult? NavigationResult { get; }

    public NavigationCoordinateRetentionResult? CoordinateRetention { get; }

    public NavigationRestorationPreparationResult? NavigationPreparation { get; }

    public NavigationCoordinateSuccessorFailure? NavigationSuccessorFailure
    {
        get;
    }

    public bool Succeeded => Definitions is not null;

    internal static WorkspacePortableCoordinateReplacementResult Success(
        CommittedScenarioDefinitionSet definitions,
        WorkspaceScopeOperationResult scopeResult,
        NavigationOperationResult navigationResult,
        NavigationCoordinateRetentionResult coordinateRetention) =>
        new(
            definitions,
            null,
            scopeResult,
            navigationResult,
            coordinateRetention,
            null,
            null);

    internal static WorkspacePortableCoordinateReplacementResult Refused(
        WorkspacePortableCoordinateReplacementFailureKind kind,
        string detail,
        WorkspaceScopeOperationResult? scopeResult = null,
        NavigationOperationResult? navigationResult = null,
        NavigationCoordinateRetentionResult? coordinateRetention = null,
        NavigationRestorationPreparationResult? navigationPreparation = null,
        NavigationCoordinateSuccessorFailure? navigationSuccessorFailure = null) =>
        new(
            null,
            new WorkspacePortableCoordinateReplacementFailure(kind, detail),
            scopeResult,
            navigationResult,
            coordinateRetention,
            navigationPreparation,
            navigationSuccessorFailure);
}

public static class WorkspacePortableCoordinateReplacement
{
    public static async ValueTask<WorkspacePortableCoordinateReplacementResult>
        ExecuteAsync(
            CommittedScenarioDefinitionSet definitions,
            WorkspacePackageCoordinateReplacementRequest request,
            CompleteRestorationExecutionOptions options,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(options);

        if (cancellationToken.IsCancellationRequested)
            return Cancelled();

        try
        {
            _ = InspectionDefinitionRegistry.NormalizeVersion(request.Version);
            _ = InspectionDefinitionRegistry.NormalizeFramework(
                request.Framework);
        }
        catch (InspectionDefinitionException failure)
        {
            return WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind
                    .InvalidDestination,
                failure.Message);
        }

        DefinitionMutationPreparation mutation;
        try
        {
            DefinitionMutationPreparationResult prepared =
                PrepareDefinitionMutation(definitions, request);
            if (prepared.Failure is not null)
                return prepared.Failure;
            mutation = prepared.Mutation!;
        }
        catch (InspectionDefinitionException failure)
        {
            return WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind.InvalidInput,
                failure.Message);
        }
        catch (ArgumentException failure)
        {
            return WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind.InvalidInput,
                failure.Message);
        }

        var authority = new FiniteIntentAuthority();
        CompleteRestorationPreparationResult restorationPreparation =
            CompleteRestorationPreparation.FromCommittedDefinitions(
                definitions,
                authority,
                cancellationToken);
        WorkspacePortableCoordinateReplacementResult? operationResult = null;
        var host = new FiniteWorkspaceHost();
        CompleteRestorationResult<FiniteActivation> restoration =
            await CompleteRestorationCoordinator.RestoreWithOperationAsync(
                restorationPreparation,
                authority,
                host,
                options,
                async (roots, requests, token) =>
                {
                    SourceRootPreparation source =
                        await PrepareSourceRootsAsync(
                            roots,
                            requests,
                            mutation,
                            options,
                            token).ConfigureAwait(false);
                    if (source.Failure is not null)
                        operationResult = source.Failure;
                    return source.Roots;
                },
                async (workspace, activation, invocation, token) =>
                {
                    if (operationResult is not null)
                        return;
                    operationResult = await ExecuteInWorkspaceAsync(
                        workspace,
                        activation,
                        invocation,
                        mutation,
                        options,
                        token).ConfigureAwait(false);
                },
                cancellationToken).ConfigureAwait(false);

        if (restoration is CompleteRestorationResult<FiniteActivation>.Failed
            { Failure: CompleteRestorationFailure.CleanupFailed cleanup })
        {
            return WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind.CleanupFailed,
                cleanup.Message,
                operationResult?.ScopeResult,
                operationResult?.NavigationResult,
                operationResult?.CoordinateRetention,
                operationResult?.NavigationPreparation,
                operationResult?.NavigationSuccessorFailure);
        }
        if (operationResult is not null)
            return operationResult;

        return restoration switch
        {
            CompleteRestorationResult<FiniteActivation>.Failed failed =>
                WorkspacePortableCoordinateReplacementResult.Refused(
                    failed.Failure
                        is CompleteRestorationFailure.Cancelled
                        ? WorkspacePortableCoordinateReplacementFailureKind
                            .Cancelled
                        : WorkspacePortableCoordinateReplacementFailureKind
                            .InputRestorationFailed,
                    failed.Failure.Message),
            CompleteRestorationResult<FiniteActivation>.Superseded =>
                WorkspacePortableCoordinateReplacementResult.Refused(
                    WorkspacePortableCoordinateReplacementFailureKind
                        .InputRestorationFailed,
                    "The finite input restoration was superseded."),
            _ => WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind
                    .InputRestorationFailed,
                "Input restoration completed without executing the associated replacement."),
        };
    }

    private static DefinitionMutationPreparationResult
        PrepareDefinitionMutation(
            CommittedScenarioDefinitionSet definitions,
            WorkspacePackageCoordinateReplacementRequest request)
    {
        if (definitions.Workspace is not { } workspace
            || definitions.Navigation is not { } navigation
            || definitions.View is not { } view)
        {
            return Failed(
                WorkspacePortableCoordinateReplacementFailureKind.InvalidInput,
                "Portable Package-coordinate replacement requires a complete workspace-backed scenario.");
        }
        if (view.States.Any(static state =>
            state.Queries.Count != 0 || state.Libraries.Count != 0))
        {
            return Failed(
                WorkspacePortableCoordinateReplacementFailureKind
                    .QueryStateUnsupported,
                "Query-bearing committed state is not supported by portable Package-coordinate replacement.");
        }

        NavigationTabDefinition[] matchingTabs =
        [
            .. navigation.Tabs.Where(candidate =>
                candidate.Coordinate
                    is DefinitionMemberCoordinate.PackageCoordinate package
                && WorkspacePackageComponentPath.Create(package)
                    == request.Component),
        ];
        if (matchingTabs.Length == 0)
        {
            return Failed(
                WorkspacePortableCoordinateReplacementFailureKind
                    .NavigationSourceMissing,
                $"Workspace Package component '{request.Component}' is absent.");
        }
        if (matchingTabs.Length != 1)
        {
            return Failed(
                WorkspacePortableCoordinateReplacementFailureKind
                    .NavigationSourceAmbiguous,
                $"Workspace Package component '{request.Component}' matches "
                    + $"{matchingTabs.Length} Navigation rows.");
        }
        NavigationTabDefinition tab = matchingTabs[0];
        if (tab.Coordinate
            is not DefinitionMemberCoordinate.PackageCoordinate tabPackage)
        {
            return Failed(
                WorkspacePortableCoordinateReplacementFailureKind
                    .NavigationSourceNotDirectPackage,
                $"Workspace Package component '{request.Component}' is not a "
                    + "direct Package source.");
        }

        IReadOnlyList<PackageNavigationSource> positions =
            InspectionDefinitionRegistry
                .ResolvePackageNavigationSourcePositions(
                    workspace,
                    tab,
                    definitions.NavigationTargetMatchMode);
        if (positions.Count != 1)
        {
            return Failed(
                positions.Count == 0
                    ? WorkspacePortableCoordinateReplacementFailureKind
                        .NavigationSourceMissing
                    : WorkspacePortableCoordinateReplacementFailureKind
                        .NavigationSourceAmbiguous,
                positions.Count == 0
                    ? $"Workspace Package component '{request.Component}' has "
                        + "no direct Package member position."
                    : $"Workspace Package component '{request.Component}' "
                        + $"matches {positions.Count} direct Package member positions.");
        }

        PackageNavigationSource source = positions[0];
        if (source.EffectiveCoordinate.Version is null)
        {
            return Failed(
                WorkspacePortableCoordinateReplacementFailureKind
                    .FloatingSourceUnsupported,
                $"Workspace Package component '{request.Component}' selects "
                    + "a floating Package source.");
        }

        string destinationVersion =
            request.Version is null
                ? source.EffectiveCoordinate.Version
                : InspectionDefinitionRegistry.NormalizeVersion(
                    request.Version)
                    ?? throw new InvalidOperationException(
                        "A requested version cannot normalize to null.");
        string? destinationFramework =
            request.Framework is null
                ? source.EffectiveCoordinate.Framework
                : InspectionDefinitionRegistry.NormalizeFramework(
                    request.Framework);
        bool frameworkChanged = !string.Equals(
            destinationFramework,
            source.EffectiveCoordinate.Framework,
            StringComparison.Ordinal);
        WorkspaceContextDefinition sourceContext =
            workspace.Contexts[source.ContextIndex];
        if (frameworkChanged
            && (sourceContext.Members.Count != 1
                || sourceContext.Subscribe is not null))
        {
            return Failed(
                WorkspacePortableCoordinateReplacementFailureKind
                    .SharedFrameworkChangeUnsupported,
                "A Framework change requires a singleton unsubscribed workspace context.");
        }

        WorkspaceDefinition destinationWorkspace = ReplaceWorkspaceCoordinate(
            workspace,
            source,
            destinationVersion,
            destinationFramework,
            frameworkChanged);
        CommittedNavigationDefinition destinationNavigation =
            ReplaceNavigationCoordinate(
                navigation,
                tab,
                tabPackage,
                destinationVersion,
                destinationFramework,
                frameworkChanged);
        try
        {
            _ = InspectionDefinitionRegistry.ResolvePackageNavigationSources(
                destinationWorkspace,
                destinationNavigation,
                definitions.NavigationTargetMatchMode);
            NavigationTabDefinition destinationTab =
                destinationNavigation.Tabs.Single(
                    candidate => candidate.Id == tab.Id);
            if (InspectionDefinitionRegistry
                    .ResolvePackageNavigationSourcePositions(
                        destinationWorkspace,
                        destinationTab,
                        definitions.NavigationTargetMatchMode)
                    .Count != 1)
            {
                return Failed(
                    WorkspacePortableCoordinateReplacementFailureKind
                        .DuplicateDestinationNavigationSource,
                    "The requested destination would not identify one unique direct Package member position.");
            }
        }
        catch (InspectionDefinitionException failure)
        {
            return Failed(
                WorkspacePortableCoordinateReplacementFailureKind
                    .DuplicateDestinationNavigationSource,
                failure.Message);
        }

        return new(
            new DefinitionMutationPreparation(
                definitions,
                tab.Id,
                source,
                frameworkChanged
                    || !string.Equals(
                        destinationVersion,
                        source.EffectiveCoordinate.Version,
                        StringComparison.OrdinalIgnoreCase),
                destinationWorkspace,
                destinationNavigation),
            null);
    }

    private static WorkspaceDefinition ReplaceWorkspaceCoordinate(
        WorkspaceDefinition workspace,
        PackageNavigationSource source,
        string destinationVersion,
        string? destinationFramework,
        bool frameworkChanged)
    {
        WorkspaceContextDefinition context =
            workspace.Contexts[source.ContextIndex];
        var members = context.Members.ToArray();
        var package =
            (DefinitionMemberCoordinate.PackageCoordinate)
                members[source.MemberIndex];
        string? contextFramework = context.Framework;
        string? memberFramework = package.Framework;
        if (frameworkChanged)
        {
            bool memberOnly =
                context.Framework is null && package.Framework is not null;
            contextFramework = memberOnly ? null : destinationFramework;
            memberFramework =
                package.Framework is null ? null : destinationFramework;
        }
        members[source.MemberIndex] =
            new DefinitionMemberCoordinate.PackageCoordinate(
                package.Id,
                destinationVersion,
                memberFramework,
                package.RuntimeIdentifier);

        var contexts = workspace.Contexts.ToArray();
        contexts[source.ContextIndex] = new WorkspaceContextDefinition(
            context.Name,
            contextFramework,
            context.RuntimeIdentifier,
            context.Subscribe,
            members);
        return new WorkspaceDefinition(
            workspace.SchemaVersion,
            workspace.Id,
            contexts,
            workspace.Title,
            workspace.Description,
            workspace.Groups,
            workspace.Registrations,
            workspace.PackageSources);
    }

    private static CommittedNavigationDefinition ReplaceNavigationCoordinate(
        CommittedNavigationDefinition navigation,
        NavigationTabDefinition sourceTab,
        DefinitionMemberCoordinate.PackageCoordinate sourcePackage,
        string destinationVersion,
        string? destinationFramework,
        bool frameworkChanged)
    {
        string? coordinateFramework = sourcePackage.Framework;
        string? tabFramework = sourceTab.Framework;
        if (frameworkChanged)
        {
            coordinateFramework =
                sourcePackage.Framework is null
                    ? null
                    : destinationFramework;
            tabFramework =
                sourceTab.Framework is null
                    ? null
                    : destinationFramework;
        }

        var tabs = navigation.Tabs.ToArray();
        int index = Array.FindIndex(
            tabs,
            candidate => candidate.Id == sourceTab.Id);
        tabs[index] = new NavigationTabDefinition(
            sourceTab.Id,
            new DefinitionMemberCoordinate.PackageCoordinate(
                sourcePackage.Id,
                destinationVersion,
                coordinateFramework,
                sourcePackage.RuntimeIdentifier),
            framework: tabFramework,
            runtimeIdentifier: sourceTab.RuntimeIdentifier);
        return new CommittedNavigationDefinition(
            navigation.SchemaVersion,
            navigation.Id,
            tabs,
            navigation.Focus);
    }

    private static async ValueTask<SourceRootPreparation>
        PrepareSourceRootsAsync(
            ImmutableArray<PackageRootBinding> roots,
            IReadOnlyDictionary<string, PackageArtifactRootRequest> requests,
            DefinitionMutationPreparation mutation,
            CompleteRestorationExecutionOptions options,
            CancellationToken cancellationToken)
    {
        if (!requests.TryGetValue(
                mutation.NavigationId,
                out PackageArtifactRootRequest sourceRequest))
        {
            return Failed(
                "The restored input omitted the selected Package row's exact invocation-local request.");
        }
        PackageRootBinding[] sourceBindings =
        [
            .. roots.Where(
                binding =>
                    PackageArtifactRootRequest.From(binding)
                        == sourceRequest),
        ];
        if (sourceBindings.Length != 1)
        {
            return Failed(
                "The restored input did not retain one exact selected Package Root binding.");
        }

        PackageRootReacquisitionRequest reacquisitionRequest =
            sourceBindings[0].CreateReacquisitionRequest();
        PackageRootAcquisitionOutcome sourceReacquisition =
            await PackageRootAcquisition.AcquireAsync(
                reacquisitionRequest,
                options.ContextLoad,
                cancellationToken).ConfigureAwait(false);
        if (sourceReacquisition
            is PackageRootAcquisitionOutcome.Failed sourceFailure)
        {
            return Failed(
                $"The selected Package Root could not be rebound from its exact owner-issued request: {sourceFailure.Message}");
        }
        var reacquired =
            (PackageRootAcquisitionOutcome.Acquired)sourceReacquisition;
        PackageSourceAuthorization sourceAuthorization =
            options.ContextLoad.SourceAuthorization.AuthorizeSourcesFor(
                reacquisitionRequest.Coordinate.PackageId);
        PackageRootProducerAuthorization.MatchResult producerMatch =
            PackageRootProducerAuthorization.Match(
                sourceAuthorization.Sources,
                reacquisitionRequest.Coordinate.Producer);
        if (producerMatch.Candidates.Count != 1)
        {
            return Failed(
                "The selected Package Root producer did not map to one exact authorized source.");
        }
        PackageProducerIdentity producer =
            PackageSourceClientFactory.GetProducerIdentity(
                producerMatch.Candidates[0].Source);
        PackageRootBinding sourceBinding =
            PackageRootBinding.CreateFromReacquiredResolved(
                reacquired.Payload,
                reacquisitionRequest,
                producer);

        var enriched =
            ImmutableArray.CreateBuilder<PackageRootBinding>(roots.Length);
        foreach (PackageRootBinding root in roots)
        {
            enriched.Add(
                PackageArtifactRootRequest.From(root) == sourceRequest
                    ? sourceBinding
                    : root);
        }
        return new(enriched.MoveToImmutable(), null);

        SourceRootPreparation Failed(string detail) =>
            new(
                roots,
                WorkspacePortableCoordinateReplacementResult.Refused(
                    WorkspacePortableCoordinateReplacementFailureKind
                        .InputRestorationFailed,
                    detail));
    }

    private static async ValueTask<WorkspacePortableCoordinateReplacementResult>
        ExecuteInWorkspaceAsync(
            InspectionWorkspace workspace,
            CompleteWorkspaceActivation activation,
            CompleteRestorationInvocation invocation,
            DefinitionMutationPreparation mutation,
            CompleteRestorationExecutionOptions options,
            CancellationToken cancellationToken)
    {
        try
        {
            if (!invocation.PackageRequests.TryGetValue(
                    mutation.NavigationId,
                    out PackageArtifactRootRequest sourceRequest)
                || !invocation.PackageEvaluations.TryGetValue(
                    mutation.NavigationId,
                    out NavigationPackageEvaluation? sourcePackage)
                || sourcePackage is null)
            {
                return WorkspacePortableCoordinateReplacementResult.Refused(
                    WorkspacePortableCoordinateReplacementFailureKind
                        .InputRestorationFailed,
                    "The restored input omitted the selected Package row's exact invocation-local facts.");
            }

            PackageRootBinding[] sourceBindings =
            [
                .. invocation.PackageRoots.Where(
                    binding =>
                        PackageArtifactRootRequest.From(binding)
                            == sourceRequest),
            ];
            if (sourceBindings.Length != 1)
            {
                return WorkspacePortableCoordinateReplacementResult.Refused(
                    WorkspacePortableCoordinateReplacementFailureKind
                        .InputRestorationFailed,
                    "The restored input did not retain one exact selected Package Root binding.");
            }
            PackageRootBinding sourceBinding = sourceBindings[0];

            CompleteRestorationResolvedViewState sourceState =
                ResolvedStates(activation.Snapshot.Resolved).Single(
                    state => state.NavigationId == mutation.NavigationId);
            NavigationInitialization sourceInitialization =
                sourceState.Initialization
                ?? throw new InvalidOperationException(
                    "A direct Package row requires exact restored Navigation initialization.");
            NavigationRestorationPreparationResult sourceNavigation =
                NavigationTransitions.PrepareRestoration(
                    workspace.Identity,
                    new NavigationEvaluationFacts(
                        activation.Snapshot.Scope,
                        sourcePackage,
                        options.FacetAvailability),
                    options.Facets,
                    sourceInitialization);
            if (sourceNavigation
                is not NavigationRestorationPreparationResult.Prepared
                    preparedSource)
            {
                return WorkspacePortableCoordinateReplacementResult.Refused(
                    WorkspacePortableCoordinateReplacementFailureKind
                        .InputRestorationFailed,
                    "The selected restored Package row did not produce a complete operation-local Navigation basis.");
            }

            PackageRootBinding destinationBinding;
            if (!mutation.CoordinateChanged)
            {
                destinationBinding = sourceBinding;
            }
            else
            {
                WorkspaceContextDefinition destinationContext =
                    mutation.DestinationWorkspace.Contexts[
                        mutation.Source.ContextIndex];
                var destinationInput = new WorkspaceContextInput
                {
                    Framework = destinationContext.Framework,
                    RuntimeIdentifier = destinationContext.RuntimeIdentifier,
                    Members =
                    [
                        DefinitionCoordinateLowering.ToWorkspaceMember(
                            destinationContext.Members[
                                mutation.Source.MemberIndex]),
                    ],
                };
                WorkspacePackageRootAcquisitionOutcome acquisition =
                    await WorkspaceContextLoader.AcquirePackageRootAsync(
                        destinationInput,
                        options.ContextLoad,
                        cancellationToken).ConfigureAwait(false);
                if (acquisition
                    is WorkspacePackageRootAcquisitionOutcome.Failed failed)
                {
                    string detail = failed.Failures.IsEmpty
                        ? "The exact destination Package Root was unavailable."
                        : string.Join(
                            " ",
                            failed.Failures.Select(
                                static failure => failure.Message));
                    return WorkspacePortableCoordinateReplacementResult.Refused(
                        WorkspacePortableCoordinateReplacementFailureKind
                            .DestinationAcquisitionFailed,
                        detail);
                }
                destinationBinding =
                    ((WorkspacePackageRootAcquisitionOutcome.Acquired)acquisition)
                        .Root;
                ProducerBindingPreparation destinationPreparation =
                    await ReacquireWithProducerAsync(
                        destinationBinding,
                        options,
                        cancellationToken).ConfigureAwait(false);
                if (destinationPreparation.Binding is null)
                {
                    return WorkspacePortableCoordinateReplacementResult.Refused(
                        WorkspacePortableCoordinateReplacementFailureKind
                            .DestinationAcquisitionFailed,
                        destinationPreparation.Failure
                            ?? "The exact destination Package producer was unavailable.");
                }
                destinationBinding = destinationPreparation.Binding;
            }

            var roots =
                ImmutableArray.CreateBuilder<PackageRootBinding>(
                    invocation.PackageRoots.Length);
            foreach (PackageRootBinding root in invocation.PackageRoots)
            {
                roots.Add(
                    PackageArtifactRootRequest.From(root) == sourceRequest
                        ? destinationBinding
                        : root);
            }

            return await ExecuteInSuccessorWorkspaceAsync(
                workspace,
                preparedSource,
                sourceBinding,
                destinationBinding,
                roots.MoveToImmutable(),
                mutation,
                options,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind.Cancelled,
                "Portable Package-coordinate replacement was cancelled.");
        }
    }

    private static async ValueTask<WorkspacePortableCoordinateReplacementResult>
        ExecuteInSuccessorWorkspaceAsync(
            InspectionWorkspace sourceWorkspace,
            NavigationRestorationPreparationResult.Prepared preparedSource,
            PackageRootBinding sourceBinding,
            PackageRootBinding destinationBinding,
            ImmutableArray<PackageRootBinding> destinationRoots,
            DefinitionMutationPreparation mutation,
            CompleteRestorationExecutionOptions options,
            CancellationToken cancellationToken)
    {
        WorkspacePlan plan =
            InspectionDefinitionRegistry.CreateCompleteRestorationWorkspacePlan(
                mutation.DestinationWorkspace);
        var destinationWorkspace = new InspectionWorkspace(plan);
        WorkspacePortableCoordinateReplacementResult operationResult;
        try
        {
            operationResult = await PopulateAndPrepareSuccessorAsync(
                sourceWorkspace,
                preparedSource,
                sourceBinding,
                destinationWorkspace,
                destinationBinding,
                destinationRoots,
                mutation,
                options,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            operationResult = WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind.Cancelled,
                "Portable Package-coordinate replacement was cancelled.");
        }
        catch
        {
            await destinationWorkspace.CloseAsync().ConfigureAwait(false);
            throw;
        }

        InspectionWorkspaceCloseReport close =
            await destinationWorkspace.CloseAsync().ConfigureAwait(false);
        if (!close.Succeeded)
        {
            return WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind
                    .CleanupFailed,
                "The successor Workspace did not close cleanly.",
                operationResult.ScopeResult,
                operationResult.NavigationResult,
                operationResult.CoordinateRetention,
                operationResult.NavigationPreparation,
                operationResult.NavigationSuccessorFailure);
        }

        return operationResult;
    }

    private static async ValueTask<WorkspacePortableCoordinateReplacementResult>
        PopulateAndPrepareSuccessorAsync(
            InspectionWorkspace sourceWorkspace,
            NavigationRestorationPreparationResult.Prepared preparedSource,
            PackageRootBinding sourceBinding,
            InspectionWorkspace destinationWorkspace,
            PackageRootBinding destinationBinding,
            ImmutableArray<PackageRootBinding> destinationRoots,
            DefinitionMutationPreparation mutation,
            CompleteRestorationExecutionOptions options,
            CancellationToken cancellationToken)
    {
        WorkspaceScopeReadResult initial =
            await destinationWorkspace.GetScopeSnapshotAsync()
                .ConfigureAwait(false);
        if (initial is not WorkspaceScopeReadResult.Available available)
        {
            return WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind
                    .DestinationScopeUnavailable,
                "The successor Workspace's initial Scope was unavailable.");
        }

        WorkspaceScopeOperationResult scopeResult =
            await destinationWorkspace.AddPackagesAsync(
                available.Snapshot.Revision,
                destinationRoots,
                options.ScopeDeadline,
                cancellationToken).ConfigureAwait(false);
        WorkspaceScopeSnapshot? destinationScope = scopeResult switch
        {
            WorkspaceScopeOperationResult.Committed committed =>
                committed.Snapshot,
            WorkspaceScopeOperationResult.NoEffect noEffect =>
                noEffect.Snapshot,
            _ => null,
        };
        if (destinationScope is null)
        {
            return WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind
                    .DestinationAdmissionFailed,
                $"The successor Workspace settled Package admission as {scopeResult.GetType().Name}.",
                scopeResult);
        }

        NavigationCoordinateSuccessorPreparationResult successor =
            await NavigationCoordinateSuccessorQuery.PrepareAsync(
                sourceWorkspace,
                preparedSource.Initialization.State,
                sourceBinding,
                destinationWorkspace,
                destinationScope,
                destinationBinding,
                options.Facets,
                options.FacetAvailability,
                cancellationToken).ConfigureAwait(false);
        if (successor
            is NavigationCoordinateSuccessorPreparationResult.Failed failed)
        {
            return WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind
                    .NavigationSuccessorFailed,
                failed.Failure.Detail,
                scopeResult,
                navigationSuccessorFailure: failed.Failure);
        }
        if (successor
            is NavigationCoordinateSuccessorPreparationResult.NotPrepared
                notPrepared)
        {
            return WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind
                    .NavigationSuccessorNotPrepared,
                NavigationPreparationDetail(notPrepared.Preparation),
                scopeResult,
                coordinateRetention: notPrepared.Retention,
                navigationPreparation: notPrepared.Preparation);
        }

        var prepared =
            (NavigationCoordinateSuccessorPreparationResult.Prepared)successor;
        NavigationOperationResult navigationResult =
            prepared.Initialization.Result;
        NavigationCoordinateRetentionResult coordinateRetention =
            prepared.Retention;
        if (!mutation.CoordinateChanged)
        {
            return WorkspacePortableCoordinateReplacementResult.Success(
                mutation.SourceDefinitions,
                scopeResult,
                navigationResult,
                coordinateRetention);
        }

        CommittedScenarioDefinitionSet derived;
        try
        {
            derived = BuildDerivedDefinitions(
                mutation,
                prepared.Initialization.State.CurrentSnapshot);
            ValidateDerivedDefinitions(derived);
        }
        catch (Exception failure)
            when (failure is InspectionDefinitionException
                or ArgumentException
                or InvalidOperationException)
        {
            return WorkspacePortableCoordinateReplacementResult.Refused(
                WorkspacePortableCoordinateReplacementFailureKind
                    .DerivedDefinitionInvalid,
                failure.Message,
                scopeResult,
                navigationResult,
                coordinateRetention);
        }

        WorkspaceSharePacketProjectionResult projection =
            WorkspaceSharePacketTransposer.ToPacket(
                derived,
                cancellationToken);
        if (!projection.Succeeded)
        {
            WorkspaceSharePacketProjectionFailure failure =
                projection.Failure
                ?? throw new InvalidOperationException(
                    "A failed derived projection requires a reason.");
            return WorkspacePortableCoordinateReplacementResult.Refused(
                failure.Kind
                    == WorkspaceSharePacketProjectionFailureKind
                        .NonProjectable
                    ? WorkspacePortableCoordinateReplacementFailureKind
                        .DerivedDefinitionNonProjectable
                    : WorkspacePortableCoordinateReplacementFailureKind
                        .DerivedDefinitionInvalid,
                failure.Message,
                scopeResult,
                navigationResult,
                coordinateRetention);
        }

        return WorkspacePortableCoordinateReplacementResult.Success(
            derived,
            scopeResult,
            navigationResult,
            coordinateRetention);
    }

    private static string NavigationPreparationDetail(
        NavigationRestorationPreparationResult preparation) =>
        preparation switch
        {
            NavigationRestorationPreparationResult.Unavailable unavailable =>
                unavailable.Message,
            NavigationRestorationPreparationResult.Failed failed =>
                failed.Message,
            NavigationRestorationPreparationResult.Rejected rejected =>
                rejected.Message,
            _ => throw new ArgumentOutOfRangeException(
                nameof(preparation),
                "A successor non-preparation result must be unavailable, failed, or rejected."),
        };

    private static CommittedScenarioDefinitionSet BuildDerivedDefinitions(
        DefinitionMutationPreparation mutation,
        NavigationWorkspaceSnapshot destination)
    {
        CommittedScenarioDefinitionSet source = mutation.SourceDefinitions;
        CommittedViewDefinition view = source.View
            ?? throw new InvalidOperationException(
                "A complete committed scenario requires a view.");
        var states = view.States.ToArray();
        int stateIndex = Array.FindIndex(
            states,
            state => state.Navigation == mutation.NavigationId);
        if (stateIndex < 0)
        {
            throw new InvalidOperationException(
                "The selected navigation row has no committed view state.");
        }

        CommittedViewStateDefinition sourceState = states[stateIndex];
        if (sourceState.Subject is null)
        {
            states[stateIndex] =
                new CommittedViewStateDefinition(mutation.NavigationId);
        }
        else if (sourceState.Subject is PortableSubjectRequest.Workspace
            && sourceState.Context is null)
        {
            states[stateIndex] = new CommittedViewStateDefinition(
                mutation.NavigationId,
                sourceState.Subject,
                sourceState.Context,
                sourceState.Facet);
        }
        else
        {
            PortableSubjectRequest subject = ToPortableSubject(destination);
            PortableRetainedSubjectContext? context =
                ToPortableContext(destination.RetainedContext);
            if (subject is PortableSubjectRequest.Workspace
                && context is PortableRetainedSubjectContext.Package)
            {
                context = null;
            }
            states[stateIndex] = new CommittedViewStateDefinition(
                mutation.NavigationId,
                subject,
                context,
                destination.LensOutcome.Basis
                    is NavigationLensEvaluationBasis.ExactRequest exact
                        ? exact.Request.Facet.Value
                        : null);
        }
        var destinationView = new CommittedViewDefinition(
            view.SchemaVersion,
            view.Id,
            states);
        return new CommittedScenarioDefinitionSet(
            source.Scenario,
            mutation.DestinationWorkspace,
            mutation.DestinationNavigation,
            destinationView,
            source.Catalogs,
            source.NavigationTargetMatchMode);
    }

    private static PortableSubjectRequest ToPortableSubject(
        NavigationWorkspaceSnapshot snapshot)
    {
        StructuralSubjectIdentity active = snapshot.ActiveSubject;
        NavigationRetainedSubjectContext? context =
            snapshot.RetainedContext;
        if (active == snapshot.Workspace)
            return new PortableSubjectRequest.Workspace();
        if (context is null)
        {
            throw new InvalidOperationException(
                "A non-Workspace destination subject requires retained Package context.");
        }
        if (active == context.Package)
            return new PortableSubjectRequest.Package();
        if (active == context.Library)
            return new PortableSubjectRequest.Library();
        if (active == context.Type)
            return new PortableSubjectRequest.Type();
        if (active == context.Member)
            return new PortableSubjectRequest.Member();
        throw new InvalidOperationException(
            "The completed destination subject is outside its retained path.");
    }

    private static PortableRetainedSubjectContext? ToPortableContext(
        NavigationRetainedSubjectContext? context)
    {
        if (context is null)
            return null;
        if (context.Library is null)
            return new PortableRetainedSubjectContext.Package();
        if (context.Library
            is StructuralSubjectIdentity.AllLibrariesSubject)
        {
            return new PortableRetainedSubjectContext.AllLibraries();
        }

        var library =
            (StructuralSubjectIdentity.LibrarySubject)context.Library;
        PortableLibraryIdentity identity =
            PortableIdentityProjection.FromAssembly(
                library.Identity.Assembly);
        if (context.Type is null)
            return new PortableRetainedSubjectContext.Library(identity);
        if (context.Member is null)
        {
            return new PortableRetainedSubjectContext.Type(
                identity,
                context.Type.Identity.Type);
        }
        return new PortableRetainedSubjectContext.Member(
            identity,
            context.Type.Identity.Type,
            memberAnchor:
                context.Member.Identity.Member.Fingerprint);
    }

    private static void ValidateDerivedDefinitions(
        CommittedScenarioDefinitionSet definitions)
    {
        var registry = new InspectionDefinitionRegistry();
        foreach (InspectionDefinitionRecord record in definitions.Records)
            registry.Add(record);
        _ = registry.PrepareScenario(definitions.Scenario.Id);
    }

    private static IReadOnlyList<CompleteRestorationResolvedViewState>
        ResolvedStates(CompleteRestorationResolvedState resolved) =>
        resolved switch
        {
            CompleteRestorationResolvedState.Version2 version2 =>
                version2.States,
            CompleteRestorationResolvedState.Version3 version3 =>
                version3.States,
            CompleteRestorationResolvedState.Version4 version4 =>
                version4.States,
            CompleteRestorationResolvedState.Version5 version5 =>
                version5.States,
            _ => throw new InvalidOperationException(
                "Unknown complete restoration state version."),
        };

    private static DefinitionMutationPreparationResult Failed(
        WorkspacePortableCoordinateReplacementFailureKind kind,
        string detail) =>
        new(
            null,
            WorkspacePortableCoordinateReplacementResult.Refused(
                kind,
                detail));

    private static WorkspacePortableCoordinateReplacementResult Cancelled() =>
        WorkspacePortableCoordinateReplacementResult.Refused(
            WorkspacePortableCoordinateReplacementFailureKind.Cancelled,
            "Portable Package-coordinate replacement was cancelled.");

    private static async ValueTask<ProducerBindingPreparation>
        ReacquireWithProducerAsync(
            PackageRootBinding binding,
            CompleteRestorationExecutionOptions options,
            CancellationToken cancellationToken)
    {
        PackageRootReacquisitionRequest request =
            binding.CreateReacquisitionRequest();
        PackageRootAcquisitionOutcome outcome =
            await PackageRootAcquisition.AcquireAsync(
                request,
                options.ContextLoad,
                cancellationToken).ConfigureAwait(false);
        if (outcome is PackageRootAcquisitionOutcome.Failed failure)
            return new(null, failure.Message);

        var acquired = (PackageRootAcquisitionOutcome.Acquired)outcome;
        PackageSourceAuthorization authorization =
            options.ContextLoad.SourceAuthorization.AuthorizeSourcesFor(
                request.Coordinate.PackageId);
        PackageRootProducerAuthorization.MatchResult producerMatch =
            PackageRootProducerAuthorization.Match(
                authorization.Sources,
                request.Coordinate.Producer);
        if (producerMatch.Candidates.Count != 1)
        {
            return new(
                null,
                "The Package producer did not map to one exact authorized source.");
        }
        PackageProducerIdentity producer =
            PackageSourceClientFactory.GetProducerIdentity(
                producerMatch.Candidates[0].Source);
        return new(
            PackageRootBinding.CreateFromReacquiredResolved(
                acquired.Payload,
                request,
                producer),
            null);
    }

    private sealed record DefinitionMutationPreparation(
        CommittedScenarioDefinitionSet SourceDefinitions,
        string NavigationId,
        PackageNavigationSource Source,
        bool CoordinateChanged,
        WorkspaceDefinition DestinationWorkspace,
        CommittedNavigationDefinition DestinationNavigation);

    private sealed record DefinitionMutationPreparationResult(
        DefinitionMutationPreparation? Mutation,
        WorkspacePortableCoordinateReplacementResult? Failure);

    private sealed record SourceRootPreparation(
        ImmutableArray<PackageRootBinding> Roots,
        WorkspacePortableCoordinateReplacementResult? Failure);

    private sealed record ProducerBindingPreparation(
        PackageRootBinding? Binding,
        string? Failure);

    private sealed class FiniteIntentAuthority :
        ICompleteRestorationIntentAuthority
    {
        public CompleteRestorationIntentIdentity Identity { get; } = new();

        public CompleteRestorationIntentStatus Status =>
            CompleteRestorationIntentStatus.Current;

        public CancellationToken Revocation => CancellationToken.None;
    }

    private sealed class FiniteActivation;

    private sealed class FiniteWorkspaceHost :
        ICompleteRestorationHost<FiniteActivation>
    {
        public async ValueTask<
            CompleteRestorationHostResult<FiniteActivation>>
            ConstructAsync(
                ICompleteRestorationIntentAuthority authority,
                CompleteRestorationPlan plan,
                CompleteWorkspacePreparationCallback prepare,
                CancellationToken cancellationToken = default)
        {
            var workspace = new InspectionWorkspace(plan.WorkspacePlan);
            CompleteWorkspacePreparationResult result;
            try
            {
                result = await prepare(
                    workspace,
                    authority.Revocation).ConfigureAwait(false);
            }
            catch
            {
                _ = await workspace.CloseAsync().ConfigureAwait(false);
                throw;
            }

            InspectionWorkspaceCloseReport close =
                await workspace.CloseAsync().ConfigureAwait(false);
            if (!close.Succeeded)
            {
                return new CompleteRestorationHostResult<FiniteActivation>
                    .Failed(
                        new CompleteRestorationFailure.CleanupFailed(
                            "The finite replacement Workspace did not close cleanly."));
            }

            return result switch
            {
                CompleteWorkspacePreparationResult.Prepared prepared =>
                    new CompleteRestorationHostResult<FiniteActivation>
                        .Activated(
                            new FiniteActivation(),
                            prepared.Activation),
                CompleteWorkspacePreparationResult.Failed failed =>
                    new CompleteRestorationHostResult<FiniteActivation>
                        .Failed(failed.Failure),
                CompleteWorkspacePreparationResult.Superseded =>
                    new CompleteRestorationHostResult<FiniteActivation>
                        .Superseded(),
                _ => throw new InvalidOperationException(
                    "Unknown finite Workspace preparation result."),
            };
        }
    }
}
