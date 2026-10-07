using System.Collections.Immutable;
using System.Runtime.ExceptionServices;

using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using ILInspector.CallGraph;
using ILInspector.Metadata;
using Inspector.Artifacts.Workspaces;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Exact root package implementation member selected for dependency-aware
/// call-graph analysis.
/// </summary>
public sealed record PackageDependencyMemberCallGraphFocus
{
    public PackageDependencyMemberCallGraphFocus(
        int rootOccurrenceIndex,
        Guid moduleVersionId,
        int methodToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(rootOccurrenceIndex);
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dependency call-graph focus requires a module version identifier.",
                nameof(moduleVersionId));
        }

        RootOccurrenceIndex = rootOccurrenceIndex;
        ModuleVersionId = moduleVersionId;
        MethodToken = methodToken;
    }

    public int RootOccurrenceIndex { get; }

    public Guid ModuleVersionId { get; }

    public int MethodToken { get; }
}

/// <summary>
/// One prepared dependency traversal and the exact context required to execute
/// it as a member call graph.
/// </summary>
public sealed record PackageDependencyMemberCallGraphRequest
{
    public PackageDependencyMemberCallGraphRequest(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope,
        WorkspaceRegistrationRevision registrations,
        PackageDependencyTraversalOutcome traversal,
        ImmutableArray<PackageRootBinding> rootBindings,
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            edgeExecutions,
        PackageDependencyMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest graph,
        DateTimeOffset workspaceDeadline,
        PackageAssemblyContextRealizationOptions? realizationOptions = null,
        PackageSupplyChainBaseline supplyChainBaseline =
            PackageSupplyChainBaseline.Nothing)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(graph);

        if (!ReferenceEquals(
                registrations.Workspace,
                workspace.Identity))
        {
            throw new ArgumentException(
                "The captured registrations belong to another Workspace.",
                nameof(registrations));
        }

        Workspace = workspace;
        Scope = scope;
        Registrations = registrations;
        Traversal = traversal;
        RootBindings = rootBindings;
        EdgeExecutions = edgeExecutions;
        Focus = focus;
        Graph = graph;
        WorkspaceDeadline = workspaceDeadline;
        RealizationOptions = realizationOptions;
        if (!Enum.IsDefined(supplyChainBaseline))
        {
            throw new ArgumentOutOfRangeException(
                nameof(supplyChainBaseline));
        }
        SupplyChainBaseline = supplyChainBaseline;
    }

    public InspectionWorkspace Workspace { get; }

    public WorkspaceScopeSnapshot Scope { get; }

    public WorkspaceRegistrationRevision Registrations { get; }

    public PackageDependencyTraversalOutcome Traversal { get; }

    public ImmutableArray<PackageRootBinding> RootBindings { get; }

    public ImmutableArray<PackageDependencyEdgeRealizationExecution>
        EdgeExecutions
    { get; }

    public PackageDependencyMemberCallGraphFocus Focus { get; }

    public MemberCallGraphCalleeNeighborhoodRequest Graph { get; }

    public DateTimeOffset WorkspaceDeadline { get; }

    public PackageAssemblyContextRealizationOptions? RealizationOptions
    {
        get;
    }

    public PackageSupplyChainBaseline SupplyChainBaseline { get; }
}

/// <summary>
/// Resource-free identity of one admitted dependency edge occurrence.
/// </summary>
public sealed record PackageDependencyMemberCallGraphRouteSubject(
    int RootOccurrenceIndex,
    int EdgeIndex,
    int Distance,
    PackageSourceCoordinate Source,
    string TargetPackageId,
    string TargetVersionConstraint,
    PackageDependencyTraversalEdgeEmissionAuthority Authority);

/// <summary>
/// Detached destination of one dependency call-graph route.
/// </summary>
public abstract record PackageDependencyMemberCallGraphDestination
{
    private PackageDependencyMemberCallGraphDestination()
    {
    }

    public sealed record Package(
        WorkspacePackageOccurrenceIdentity Occurrence,
        WorkspacePackageDescriptor Descriptor,
        ArtifactRootRealizationStatus RealizationStatus,
        PackageDependencyWorkspacePackageRouteSource Source)
        : PackageDependencyMemberCallGraphDestination;

    public sealed record Platform(
        PackageSourceCoordinate Coordinate,
        PlatformFamilyTarget Target,
        PlatformSupply Supply)
        : PackageDependencyMemberCallGraphDestination;

    public sealed record Unavailable(
        PackageDependencyWorkspaceUnavailableReason Reason,
        PackageHouseRootNoContributionReason? NoContributionReason)
        : PackageDependencyMemberCallGraphDestination;
}

/// <summary>
/// Detached route retained beside one dependency-aware member graph.
/// </summary>
public sealed record PackageDependencyMemberCallGraphRoute(
    PackageDependencyMemberCallGraphRouteSubject Subject,
    PackageDependencyMemberCallGraphDestination Destination);

/// <summary>
/// Resource-free ownership classification for one uniquely attributed graph
/// node.
/// </summary>
public abstract record PackageDependencyMemberCallGraphNodeClassification(
    int NodeId)
{
    public sealed record Package(
        int NodeId,
        WorkspacePackageDescriptor Descriptor)
        : PackageDependencyMemberCallGraphNodeClassification(NodeId);

    public sealed record Platform(
        int NodeId,
        PlatformFamilyTarget Target,
        PackageRoleMemberCallGraphPlatformLibraryIdentity LibraryIdentity)
        : PackageDependencyMemberCallGraphNodeClassification(NodeId);
}

/// <summary>
/// Resource-free route-plan evidence that one exact intrinsic CoreLib call
/// occurrence may advance past its non-participating package context.
/// </summary>
public sealed class
    PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
{
    internal
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt(
        WorkspaceScopeRevisionIdentity scopeRevision,
        PackageIntrinsicCoreLibraryCallOccurrenceEvidence occurrence)
    {
        ScopeRevision = scopeRevision;
        Occurrence = occurrence;
    }

    public WorkspaceScopeRevisionIdentity ScopeRevision { get; }

    public PackageIntrinsicCoreLibraryCallOccurrenceEvidence Occurrence
    {
        get;
    }
}

public enum PackageDependencyMemberCallGraphFailureReason
{
    FocusUnavailable,
    PackageContextCleanupFailed,
}

public enum PackageDependencyIntrinsicCoreLibraryContinuationKind
{
    Published,
    OutsideOperationScope,
    TargetUnavailable,
    ApplicabilityUnavailable,
    ApplicabilityRejected,
    ApplicabilityIncomplete,
    WorkspaceRejected,
    WorkspaceFailed,
}

public sealed record PackageDependencyIntrinsicCoreLibraryContinuationEvidence(
    PackageDependencyIntrinsicCoreLibraryContinuationKind Kind,
    PlatformFamilyTarget? Target,
    string? Detail);

/// <summary>
/// Terminal result of one dependency-aware package member call-graph
/// operation.
/// </summary>
public abstract record PackageDependencyMemberCallGraphOutcome
{
    private PackageDependencyMemberCallGraphOutcome()
    {
    }

    public sealed record Completed(
        TraversalTargetFrameworkPolicy TraversalTargetPolicy,
        PackageDependencyTraversalSummary TraversalSummary,
        WorkspaceScopeRevisionIdentity ScopeRevision,
        MemberCallGraphFocalScopeReceipt FocalScope,
        ImmutableArray<
            PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt>
            IntrinsicCoreLibraryContextNonParticipation,
        PackageDependencyIntrinsicCoreLibraryContinuationEvidence?
            IntrinsicCoreLibraryContinuation,
        ImmutableArray<PackageDependencyMemberCallGraphRoute> Routes,
        PackageSupplyChainBaselineEvidence Baseline,
        ImmutableArray<PackageDependencyMemberCallGraphNodeClassification>
            NodeClassifications,
        InspectionGraphDocument Graph)
        : PackageDependencyMemberCallGraphOutcome;

    public sealed record WorkspaceNotCommitted(
        WorkspaceScopeOperationResult ScopeOperation)
        : PackageDependencyMemberCallGraphOutcome;

    public sealed record Failed(
        PackageDependencyMemberCallGraphFailureReason Reason,
        string Detail,
        PackageRoleMemberCallGraphFailure? GraphFailure = null,
        PackageRoleCleanupReport? Cleanup = null)
        : PackageDependencyMemberCallGraphOutcome;
}

/// <summary>
/// One live generation-bound package call graph. The projection and its
/// assembly-context groups remain active until the generation is closed.
/// </summary>
public sealed class PackageDependencyMemberCallGraphGeneration :
    IAsyncDisposable
{
    PackageAssemblyContextProjection? _projection;
    PackageAssemblyContextCompletion? _completion;

    internal PackageDependencyMemberCallGraphGeneration(
        WorkspaceScopeSnapshot scope,
        WorkspaceRegistrationRevision registrations,
        MemberCallGraphFocalScopeReceipt focalScope,
        AssemblyReferenceResolutionGenerationReceipt generation,
        ImmutableArray<PackageRootBinding> graphBindings,
        PackageRoleMemberCallGraphOutcome outcome,
        PackageAssemblyContextProjection projection,
        PackageAssemblyContextCompletion completion)
    {
        Scope = scope;
        Registrations = registrations;
        FocalScope = focalScope;
        Generation = generation;
        GraphBindings = graphBindings;
        Outcome = outcome;
        _projection = projection;
        _completion = completion;
    }

    public WorkspaceScopeSnapshot Scope { get; }

    public WorkspaceRegistrationRevision Registrations { get; }

    public MemberCallGraphFocalScopeReceipt FocalScope { get; }

    public AssemblyReferenceResolutionGenerationReceipt Generation
    { get; }

    public ImmutableArray<PackageRootBinding> GraphBindings { get; }

    public PackageRoleMemberCallGraphOutcome Outcome { get; }

    public PackageAssemblyReferenceBindingEvidence
        CreateAssemblyReferenceContinuationEvidence(
        PackageAssemblyReferenceCallOccurrenceEvidence predecessor)
    {
        PackageAssemblyContextProjection projection =
            Volatile.Read(ref _projection)
            ?? throw new ObjectDisposedException(
                nameof(PackageDependencyMemberCallGraphGeneration));
        if (Outcome
            is not PackageRoleMemberCallGraphOutcome.Available available)
        {
            throw new InvalidOperationException(
                "An unavailable graph generation cannot issue AssemblyRef continuation evidence.");
        }

        return PackageRoleMemberCallGraphQuery.CreateContinuationEvidence(
            projection,
            available.Document,
            predecessor);
    }

    public PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
        CreateIntrinsicCoreLibraryContinuationEvidence(
        PackageIntrinsicCoreLibraryCallOccurrenceEvidence occurrence)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        return new(
            Scope.Revision.Identity,
            occurrence);
    }

    public PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
        CreateIntrinsicCoreLibraryContinuationEvidence(
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt
            predecessor)
    {
        ArgumentNullException.ThrowIfNull(predecessor);
        if (Outcome
            is not PackageRoleMemberCallGraphOutcome.Available available)
        {
            throw new InvalidOperationException(
                "An unavailable graph generation cannot issue intrinsic CoreLib continuation evidence.");
        }

        CallGraphCallSiteEvidence expected =
            predecessor.Occurrence.CallSite;
        PackageIntrinsicCoreLibraryCallOccurrenceEvidence[] occurrences =
        [
            .. available.IntrinsicCoreLibraryOccurrences.Where(
                occurrence =>
                    occurrence.CallSite.CallerModuleVersionId
                        == expected.CallerModuleVersionId
                    && occurrence.CallSite.CallerMethodToken
                        == expected.CallerMethodToken
                    && occurrence.CallSite.ILOffset
                        == expected.ILOffset
                    && occurrence.CallSite.OperandToken
                        == expected.OperandToken
                    && occurrence.Correspondence.Type.Equals(
                        predecessor.Occurrence.Correspondence.Type)),
        ];
        if (occurrences.Length == 0
            || occurrences.Any(
                occurrence =>
                    occurrence.OccurrenceId != occurrences[0].OccurrenceId
                    || !occurrence.Correspondence.Type.Equals(
                        occurrences[0].Correspondence.Type)))
        {
            throw new InvalidOperationException(
                "The successor graph does not retain one exact physical intrinsic CoreLib occurrence.");
        }

        return new(
            Scope.Revision.Identity,
            occurrences[0]);
    }

    public async ValueTask<PackageRoleCleanupReport> CloseAsync()
    {
        PackageAssemblyContextProjection? projection =
            Interlocked.Exchange(ref _projection, null);
        PackageAssemblyContextCompletion? completion =
            Interlocked.Exchange(ref _completion, null);
        if (projection is null || completion is null)
        {
            throw new ObjectDisposedException(
                nameof(PackageDependencyMemberCallGraphGeneration));
        }

        ExceptionDispatchInfo? projectionFailure = null;
        try
        {
            await projection.ReturnAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            projectionFailure = ExceptionDispatchInfo.Capture(exception);
        }

        PackageRoleCleanupReport cleanup =
            await completion.CloseAsync().ConfigureAwait(false);
        projectionFailure?.Throw();
        return cleanup;
    }

    public async ValueTask DisposeAsync()
    {
        if (_projection is not null)
            await CloseAsync().ConfigureAwait(false);
    }
}

public abstract record PackageDependencyMemberCallGraphPreparationOutcome
{
    private PackageDependencyMemberCallGraphPreparationOutcome()
    {
    }

    public sealed record Prepared(
        PackageDependencyMemberCallGraphPreparation Value)
        : PackageDependencyMemberCallGraphPreparationOutcome;

    public sealed record WorkspaceNotCommitted(
        WorkspaceScopeOperationResult ScopeOperation)
        : PackageDependencyMemberCallGraphPreparationOutcome;
}

public sealed class PackageDependencyMemberCallGraphPreparation
{
    internal PackageDependencyMemberCallGraphPreparation(
        PackageDependencyWorkspaceRouteOutcome.Completed routes,
        PackageDependencyTraversalOutcome traversal,
        ImmutableArray<PackageRootBinding> graphBindings,
        PackageRootIdentity root)
    {
        Routes = routes;
        Traversal = traversal;
        GraphBindings = graphBindings;
        Root = root;
    }

    internal PackageDependencyWorkspaceRouteOutcome.Completed Routes
    { get; }

    internal PackageDependencyTraversalOutcome Traversal { get; }

    public WorkspaceScopeSnapshot Scope => Routes.Scope;

    public ImmutableArray<PackageRootBinding> GraphBindings { get; }

    public PackageRootIdentity Root { get; }

    public PackageRootBinding ResolvePackageBinding(
        PackageDependencyEdgeRealizationSubject route)
    {
        ArgumentNullException.ThrowIfNull(route);
        PackageDependencyWorkspaceDestination.Package? selected = null;
        foreach (PackageDependencyWorkspaceDestination.Package package
            in Routes.Destinations.OfType<
                PackageDependencyWorkspaceDestination.Package>())
        {
            if (!ReferenceEquals(
                    package.Realization?.Execution.Subject,
                    route))
            {
                continue;
            }
            if (selected is not null)
            {
                throw new InvalidOperationException(
                    "One dependency route cannot contribute multiple exact Package bindings.");
            }
            selected = package;
        }

        if (selected is null
            || selected.Source
                != PackageDependencyWorkspacePackageRouteSource
                    .ResolvedCandidate
            || selected.Realization?.RootContribution
                is not PackageHouseRootContributionOutcome.Contributed
            || !ReferenceEquals(
                Routes.Scope.FindExactPackageOccurrence(selected.Binding),
                selected.Occurrence))
        {
            throw new InvalidOperationException(
                "A selected dependency supplier must retain its exact contributed Package binding.");
        }
        return selected.Binding;
    }

    public int ResolveReferencingProjection(
        PackageDependencyMemberCallGraphRequest request,
        PackageRootIdentity originPackage)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(originPackage);
        if (!ReferenceEquals(
                Traversal,
                request.Traversal))
        {
            throw new ArgumentException(
                "The referencing Package must belong to the prepared traversal.",
                nameof(request));
        }
        int? projectionIndex = null;
        for (int rootIndex = 0;
            rootIndex < request.RootBindings.Length;
            rootIndex++)
        {
            if (ReferenceEquals(
                    request.RootBindings[rootIndex].Root.Identity,
                    originPackage))
            {
                int rootProjection =
                    request.Traversal.Roots[rootIndex].ProjectionIndex;
                if (projectionIndex is not null
                    && projectionIndex != rootProjection)
                {
                    throw new InvalidOperationException(
                        "The referencing Package identifies multiple traversal projections.");
                }
                projectionIndex = rootProjection;
            }
        }

        foreach (PackageDependencyWorkspaceDestination.Package package
            in Routes.Destinations.OfType<
                PackageDependencyWorkspaceDestination.Package>())
        {
            if (!ReferenceEquals(
                    package.Binding.Root.Identity,
                    originPackage))
                continue;
            if (package.Subject.Edge.Target
                is not PackageDependencyTraversalEdgeTarget.Node node)
            {
                throw new InvalidOperationException(
                    "The referencing Package does not retain an exact traversal projection.");
            }
            if (projectionIndex is not null
                && projectionIndex != node.ProjectionIndex)
            {
                throw new InvalidOperationException(
                    "The referencing Package identifies multiple traversal projections.");
            }
            projectionIndex = node.ProjectionIndex;
        }
        return projectionIndex
            ?? throw new InvalidOperationException(
                "The referencing Package is not an exact traversal participant.");
    }
}

/// <summary>
/// Executes one completed package dependency traversal and returns detached
/// route and external-focused call-graph evidence.
/// </summary>
public static class PackageDependencyMemberCallGraphOperation
{
    public static InspectionQuery<PackageDependencyMemberCallGraphOutcome>
        Definition
    { get; } =
        new(
            "Package dependency member call graph",
            InspectionCost.Unbounded);

    public static async ValueTask<
        PackageDependencyMemberCallGraphGeneration> ExecuteGenerationAsync(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope,
        WorkspaceRegistrationRevision registrations,
        ImmutableArray<PackageRootBinding> graphBindings,
        PackageRootIdentity root,
        PackageDependencyMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest graph,
        PackageSupplyChainBaseline supplyChainBaseline,
        PackageAssemblyContextRealizationOptions? realizationOptions,
        IEnumerable<PackageAssemblyContextAdditionalPackageAsset>
            additionalImplementationAssets,
        IEnumerable<PackageAssemblyContextPlatformLibrary>
            platformLibraries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(registrations);
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(
            additionalImplementationAssets);
        ArgumentNullException.ThrowIfNull(platformLibraries);
        cancellationToken.ThrowIfCancellationRequested();

        PackageSupplyChainBaselinePolicy baseline =
            PackageSupplyChainBaselinePolicy.Create(
                [root.PackageId],
                supplyChainBaseline,
                registrations);
        PackageAssemblyContextCompletionOperation contextOperation =
            workspace.PreparePackageAssemblyContextCompletion(
                graphBindings,
                additionalImplementationAssets,
                platformLibraries,
                realizationOptions);
        PackageAssemblyContextCompletion completion =
            await contextOperation.ExecuteAsync(
                    contextOperation.Identity)
                .ConfigureAwait(false);
        PackageAssemblyContextProjection? projection = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            projection = completion.CreateProjection(graphBindings);
            PackageRoleMemberCallGraphOutcome outcome =
                PackageRoleMemberCallGraphQuery.ExecuteWithCancellation(
                    projection,
                    new PackageRoleMemberCallGraphFocus(
                        root,
                        focus.ModuleVersionId,
                        focus.MethodToken),
                    graph,
                    baseline,
                    cancellationToken);
            MemberCallGraphFocalScopeReceipt focalScope =
                MemberCallGraphFocalScopeReceipt.CaptureEverything(
                    scope,
                    registrations);
            return new(
                scope,
                registrations,
                focalScope,
                AssemblyReferenceResolutionGenerationReceipt.Capture(
                    scope,
                    focalScope),
                graphBindings,
                outcome,
                projection,
                completion);
        }
        catch
        {
            if (projection is not null)
                await projection.ReturnAsync().ConfigureAwait(false);
            await completion.CloseAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static async Task<PackageDependencyMemberCallGraphOutcome>
        ExecuteAsync(
            PackageDependencyMemberCallGraphRequest request,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(sourceOperation);
        CancellationToken cancellationToken =
            sourceOperation.CancellationToken;
        PackageDependencyMemberCallGraphPreparationOutcome prepared =
            await PrepareAsync(request, house, sourceOperation)
                .ConfigureAwait(false);
        if (prepared
            is PackageDependencyMemberCallGraphPreparationOutcome
                .WorkspaceNotCommitted notCommitted)
        {
            return new PackageDependencyMemberCallGraphOutcome
                .WorkspaceNotCommitted(notCommitted.ScopeOperation);
        }

        PackageDependencyMemberCallGraphPreparation preparation =
            ((PackageDependencyMemberCallGraphPreparationOutcome.Prepared)
                prepared).Value;
        return await ExecutePreparedAsync(
                request,
                preparation,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public static async ValueTask<
        PackageDependencyMemberCallGraphPreparationOutcome> PrepareAsync(
            PackageDependencyMemberCallGraphRequest request,
            PackageHouse house,
            PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(sourceOperation);

        CancellationToken cancellationToken =
            sourceOperation.CancellationToken;
        ImmutableArray<PackageDependencyEdgeRealizationEvidence>
            realizationEvidence;
        using (sourceOperation)
        {
            ImmutableArray<PackageDependencyEdgeRealizationExecution>
                executions = ValidateAndOrder(request);
            ValidateSourceOperation(executions, sourceOperation);
            var realizations =
                ImmutableArray.CreateBuilder<
                    PackageDependencyEdgeRealizationEvidence>(
                    executions.Length);
            foreach (PackageDependencyEdgeRealizationExecution execution
                in executions)
            {
                ObserveCancellation(sourceOperation);
                realizations.Add(
                    await execution.ExecuteStepAsync(
                            house,
                            sourceOperation)
                        .ConfigureAwait(false));
                ObserveCancellation(sourceOperation);
            }
            realizationEvidence = realizations.MoveToImmutable();
        }

        PackageDependencyWorkspaceRouteOutcome routeOutcome =
            await PackageDependencyWorkspaceRouteQuery.ExecuteAsync(
                    new PackageDependencyWorkspaceRouteRequest(
                        request.Workspace,
                        request.Scope,
                        request.Traversal,
                        request.RootBindings,
                        realizationEvidence,
                        request.WorkspaceDeadline),
                    cancellationToken)
                .ConfigureAwait(false);
        if (routeOutcome
            is PackageDependencyWorkspaceRouteOutcome.NotCommitted
                notCommitted)
        {
            return new PackageDependencyMemberCallGraphPreparationOutcome
                .WorkspaceNotCommitted(notCommitted.ScopeOperation);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var completedRoutes =
            (PackageDependencyWorkspaceRouteOutcome.Completed)
                routeOutcome;
        ImmutableArray<PackageRootBinding> graphBindings =
            GraphBindings(
                request,
                completedRoutes);
        PackageRootIdentity root =
            request.RootBindings[
                request.Focus.RootOccurrenceIndex].Root.Identity;
        return new PackageDependencyMemberCallGraphPreparationOutcome
            .Prepared(
                new(
                    completedRoutes,
                    request.Traversal,
                    graphBindings,
                    root));
    }

    public static async ValueTask<PackageDependencyMemberCallGraphOutcome>
        ExecutePreparedAsync(
        PackageDependencyMemberCallGraphRequest request,
        PackageDependencyMemberCallGraphPreparation preparation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preparation);
        PackageDependencyWorkspaceRouteOutcome.Completed completedRoutes =
            preparation.Routes;
        ImmutableArray<PackageRootBinding> graphBindings =
            preparation.GraphBindings;
        PackageRootIdentity root = preparation.Root;
        PackageDependencyMemberCallGraphGeneration? graphGeneration = null;
        ExceptionDispatchInfo? graphFailure = null;
        PackageRoleCleanupReport? cleanup = null;
        try
        {
            graphGeneration = await ExecuteGenerationAsync(
                    request.Workspace,
                    completedRoutes.Scope,
                    request.Registrations,
                    graphBindings,
                    root,
                    request.Focus,
                    request.Graph,
                    request.SupplyChainBaseline,
                    request.RealizationOptions,
                    additionalImplementationAssets: [],
                    platformLibraries: [],
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            graphFailure = ExceptionDispatchInfo.Capture(exception);
        }
        finally
        {
            if (graphGeneration is not null)
            {
                try
                {
                    cleanup = await graphGeneration.CloseAsync()
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    graphFailure ??=
                        ExceptionDispatchInfo.Capture(exception);
                }
            }
        }

        PackageDependencyMemberCallGraphOutcome.Failed? cleanupFailure =
            cleanup is null
                ? null
                : SettleGraphPhase(cleanup, graphFailure);
        graphFailure?.Throw();
        if (cleanupFailure is not null)
        {
            return cleanupFailure;
        }

        cancellationToken.ThrowIfCancellationRequested();
        return CompletePreparedGeneration(
            request,
            preparation,
            graphGeneration!,
            cleanup!);
    }

    public static PackageDependencyMemberCallGraphOutcome
        CompletePreparedGeneration(
        PackageDependencyMemberCallGraphRequest request,
        PackageDependencyMemberCallGraphPreparation preparation,
        PackageDependencyMemberCallGraphGeneration generation,
        PackageRoleCleanupReport cleanup,
        PackageDependencyIntrinsicCoreLibraryContinuationEvidence?
            intrinsicCoreLibraryContinuation = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(preparation);
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(cleanup);
        PackageDependencyMemberCallGraphOutcome.Failed? cleanupFailure =
            SettleGraphPhase(cleanup, graphFailure: null);
        if (cleanupFailure is not null)
            return cleanupFailure;

        PackageDependencyWorkspaceRouteOutcome.Completed completedRoutes =
            preparation.Routes;
        PackageRoleMemberCallGraphOutcome graphOutcome =
            generation.Outcome;
        if (graphOutcome
            is PackageRoleMemberCallGraphOutcome.Unavailable unavailable)
        {
            return new PackageDependencyMemberCallGraphOutcome.Failed(
                PackageDependencyMemberCallGraphFailureReason
                    .FocusUnavailable,
                unavailable.Failure.Detail,
                unavailable.Failure);
        }

        var availableGraph =
            (PackageRoleMemberCallGraphOutcome.Available)graphOutcome!;
        return new PackageDependencyMemberCallGraphOutcome.Completed(
            request.Traversal.TraversalTargetPolicy,
            request.Traversal.Summary,
            generation.Scope.Revision.Identity,
            generation.FocalScope,
            BindIntrinsicCoreLibraryContextNonParticipation(
                generation.Scope.Revision.Identity,
                availableGraph.IntrinsicCoreLibraryOccurrences),
            intrinsicCoreLibraryContinuation,
            DetachRoutes(completedRoutes),
            PackageSupplyChainBaselinePolicy.Create(
                [preparation.Root.PackageId],
                request.SupplyChainBaseline,
                generation.Registrations).Evidence,
            DetachNodeClassifications(
                availableGraph.NodePackages,
                availableGraph.NodePlatforms,
                generation.Scope,
                generation.GraphBindings),
            availableGraph.Document);
    }

    internal static ImmutableArray<
        PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt>
        BindIntrinsicCoreLibraryContextNonParticipation(
        WorkspaceScopeRevisionIdentity scopeRevision,
        ImmutableArray<PackageIntrinsicCoreLibraryCallOccurrenceEvidence>
            occurrences)
    {
        ArgumentNullException.ThrowIfNull(scopeRevision);
        return
        [
            .. occurrences.Select(
                occurrence =>
                    new PackageDependencyIntrinsicCoreLibraryContextNonParticipationReceipt(
                        scopeRevision,
                        occurrence)),
        ];
    }

    internal static PackageDependencyMemberCallGraphOutcome.Failed?
        SettleGraphPhase(
            PackageRoleCleanupReport cleanup,
            ExceptionDispatchInfo? graphFailure)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        if (cleanup.Groups.Any(
                static group =>
                    group is PackageRoleGroupCleanupRecord.Failed))
        {
            return new PackageDependencyMemberCallGraphOutcome.Failed(
                PackageDependencyMemberCallGraphFailureReason
                    .PackageContextCleanupFailed,
                "The package implementation context could not be released completely.",
                Cleanup: cleanup);
        }

        graphFailure?.Throw();
        return null;
    }

    private static ImmutableArray<
        PackageDependencyEdgeRealizationExecution> ValidateAndOrder(
        PackageDependencyMemberCallGraphRequest request)
    {
        if (!ReferenceEquals(
                request.Scope.Revision.Workspace,
                request.Workspace.Identity))
        {
            throw new ArgumentException(
                "The captured Scope belongs to another Workspace.",
                nameof(request));
        }
        if (request.RootBindings.IsDefault
            || request.RootBindings.Length != request.Traversal.Roots.Length
            || request.RootBindings.Any(static binding => binding is null))
        {
            throw new ArgumentException(
                "Every traversal root occurrence requires one exact Package Root binding.",
                nameof(request));
        }
        if (request.Traversal.RootReachability.Length
            != request.Traversal.Roots.Length)
        {
            throw new ArgumentException(
                "Traversal root reachability must align with root occurrences.",
                nameof(request));
        }
        for (int index = 0;
            index < request.RootBindings.Length;
            index++)
        {
            PackageDependencyTraversalRootResult root =
                request.Traversal.Roots[index];
            PackageRootBinding binding = request.RootBindings[index];
            if (root.OccurrenceIndex != index
                || root.Occurrence.Source
                    is not PackageDependencyTraversalRootSource
                        .RealizedPackage realized
                || !ReferenceEquals(
                    realized.Context.Subject.ContentGeneration,
                    binding.ContentGenerationIdentity)
                || !ReferenceEquals(
                    realized.Context.Subject.Selection,
                    binding.SelectionIdentity)
                || !realized.Context.Subject.RootRequest.Equals(
                    binding.CreateReacquisitionRequest()))
            {
                throw new ArgumentException(
                    "A traversal root binding must retain the root context's exact realized generation and selection.",
                    nameof(request));
            }

            WorkspacePackageOccurrenceDescriptor? occurrence =
                request.Scope.FindExactPackageOccurrence(binding);
            if (occurrence?.Realization.Status
                is not ArtifactRootRealizationStatus.Ready)
            {
                throw new ArgumentException(
                    "Every traversal root binding must identify its exact retained Ready occurrence in the captured Workspace Scope.",
                    nameof(request));
            }
        }
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            request.Focus.RootOccurrenceIndex,
            request.RootBindings.Length);
        if (request.EdgeExecutions.IsDefault
            || request.EdgeExecutions.Any(static execution =>
                execution is null))
        {
            throw new ArgumentException(
                "Edge executions must be a non-default collection.",
                nameof(request));
        }

        var executions =
            new Dictionary<(int Root, int Edge),
                PackageDependencyEdgeRealizationExecution>();
        foreach (PackageDependencyEdgeRealizationExecution execution
            in request.EdgeExecutions)
        {
            PackageDependencyEdgeRealizationSubject subject =
                execution.Subject;
            if (!ReferenceEquals(
                    subject.Traversal,
                    request.Traversal)
                || !executions.TryAdd(
                    (subject.RootOccurrenceIndex, subject.EdgeIndex),
                    execution))
            {
                throw new ArgumentException(
                    "Each edge execution must belong to one unique occurrence in the request's exact traversal.",
                    nameof(request));
            }
        }

        var ordered =
            ImmutableArray.CreateBuilder<
                PackageDependencyEdgeRealizationExecution>();
        for (int rootIndex = 0;
            rootIndex < request.Traversal.Roots.Length;
            rootIndex++)
        {
            PackageDependencyTraversalReachability reachability =
                request.Traversal.RootReachability[rootIndex];
            for (int edgeIndex = 0;
                edgeIndex < request.Traversal.Edges.Length;
                edgeIndex++)
            {
                if (!reachability.IsEdgeAdmitted(edgeIndex, out _)
                    || request.Traversal.Edges[edgeIndex].Authority
                        != PackageDependencyTraversalEdgeEmissionAuthority
                            .ResolvedCandidate)
                {
                    continue;
                }

                if (!executions.Remove(
                        (rootIndex, edgeIndex),
                        out PackageDependencyEdgeRealizationExecution?
                            execution))
                {
                    throw new ArgumentException(
                        "Every admitted resolved-candidate edge occurrence requires one exact prepared execution.",
                        nameof(request));
                }
                ordered.Add(execution);
            }
        }
        if (executions.Count != 0)
        {
            throw new ArgumentException(
                "Edge executions may name only admitted resolved-candidate edge occurrences.",
                nameof(request));
        }

        return ordered.ToImmutable();
    }

    private static void ValidateSourceOperation(
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            executions,
        PackageSourceOperationLease sourceOperation)
    {
        foreach (PackageDependencyEdgeRealizationExecution execution
            in executions)
        {
            if (sourceOperation.RequestTimeout
                    != execution.Request.Operation.RequestTimeout
                || sourceOperation.OperationTimeout
                    != execution.Request.Operation.OperationTimeout)
            {
                throw new ArgumentException(
                    "Every PackageHouse edge request must match the shared Package Source operation deadlines.",
                    nameof(sourceOperation));
            }
        }

        sourceOperation.ValidateCandidateOwnership(
            [
                .. executions.Select(execution =>
                    execution.Subject.Candidate),
            ]);
    }

    private static ImmutableArray<PackageRootBinding> GraphBindings(
        PackageDependencyMemberCallGraphRequest request,
        PackageDependencyWorkspaceRouteOutcome.Completed routes)
    {
        var selected =
            new Dictionary<string, GraphBindingSelection>(
                StringComparer.OrdinalIgnoreCase);
        SelectExactGraphBinding(
            routes.Scope,
            selected,
            request.RootBindings[
                request.Focus.RootOccurrenceIndex],
            distance: int.MinValue);
        for (int rootIndex = 0;
            rootIndex < request.RootBindings.Length;
            rootIndex++)
        {
            if (rootIndex != request.Focus.RootOccurrenceIndex)
            {
                SelectExactGraphBinding(
                    routes.Scope,
                    selected,
                    request.RootBindings[rootIndex],
                    distance: int.MinValue + 1);
            }
        }
        foreach (PackageDependencyWorkspaceDestination.Package package
            in routes.Destinations.OfType<
                PackageDependencyWorkspaceDestination.Package>())
        {
            if (package.Source
                    != PackageDependencyWorkspacePackageRouteSource
                        .ResolvedCandidate
                || package.Realization?.RootContribution
                    is not PackageHouseRootContributionOutcome.Contributed)
            {
                continue;
            }
            PackageRootBinding binding = package.Binding;
            WorkspacePackageOccurrenceDescriptor? occurrence =
                routes.Scope.FindExactPackageOccurrence(binding);
            if (!ReferenceEquals(occurrence, package.Occurrence))
            {
                throw new InvalidOperationException(
                    "A completed dependency Package route must retain the exact contributed binding or fail before graph construction.");
            }
            SelectGraphBinding(
                selected,
                occurrence,
                binding,
                package.Subject.Distance);
        }

        var candidates =
            new Dictionary<WorkspacePackageOccurrenceIdentity,
                PackageRootBinding>(ReferenceEqualityComparer.Instance);
        foreach (GraphBindingSelection selection in selected.Values)
        {
            candidates.Add(
                selection.Occurrence.Occurrence.Identity,
                selection.Binding);
        }
        var bindings = ImmutableArray.CreateBuilder<PackageRootBinding>();
        foreach (WorkspacePackageOccurrenceDescriptor occurrence
            in routes.Scope.Packages)
        {
            if (!candidates.TryGetValue(
                    occurrence.Occurrence.Identity,
                    out PackageRootBinding? binding))
            {
                continue;
            }
            bindings.Add(binding);
        }

        if (bindings.Count == 0)
        {
            throw new InvalidOperationException(
                "Dependency call-graph composition requires at least one routed Package Root.");
        }
        return bindings.ToImmutable();
    }

    private static void SelectExactGraphBinding(
        WorkspaceScopeSnapshot scope,
        Dictionary<string, GraphBindingSelection> selections,
        PackageRootBinding binding,
        int distance)
    {
        WorkspacePackageOccurrenceDescriptor? occurrence =
            scope.FindExactPackageOccurrence(binding);
        if (occurrence is null)
        {
            throw new InvalidOperationException(
                "A dependency call-graph root must retain its exact final Workspace occurrence.");
        }
        SelectGraphBinding(
            selections,
            occurrence,
            binding,
            distance);
    }

    private static void SelectGraphBinding(
        Dictionary<string, GraphBindingSelection> selections,
        WorkspacePackageOccurrenceDescriptor occurrence,
        PackageRootBinding binding,
        int distance)
    {
        if (selections.TryGetValue(
                binding.Root.PackageId,
                out GraphBindingSelection? selected)
            && (selected.Distance < distance
                || selected.Distance == distance
                    && PackageVersionPrecedence.Compare(
                            selected.Binding.Root.PackageVersion,
                            binding.Root.PackageVersion) >= 0))
        {
            return;
        }
        selections[binding.Root.PackageId] =
            new GraphBindingSelection(
                occurrence,
                binding,
                distance);
    }

    private sealed record GraphBindingSelection(
        WorkspacePackageOccurrenceDescriptor Occurrence,
        PackageRootBinding Binding,
        int Distance);

    private static ImmutableArray<PackageDependencyMemberCallGraphRoute>
        DetachRoutes(
            PackageDependencyWorkspaceRouteOutcome.Completed routes) =>
        [
            .. routes.Destinations.Select(destination =>
                new PackageDependencyMemberCallGraphRoute(
                    DetachSubject(destination.Subject),
                    DetachDestination(destination))),
        ];

    private static ImmutableArray<
        PackageDependencyMemberCallGraphNodeClassification>
        DetachNodeClassifications(
        ImmutableArray<PackageRoleMemberCallGraphNodePackage> nodePackages,
        ImmutableArray<PackageRoleMemberCallGraphNodePlatform> nodePlatforms,
        WorkspaceScopeSnapshot scope,
        ImmutableArray<PackageRootBinding> bindings)
    {
        var descriptors =
            new Dictionary<PackageRootIdentity, WorkspacePackageDescriptor>(
                ReferenceEqualityComparer.Instance);
        foreach (PackageRootBinding binding in bindings)
        {
            WorkspacePackageOccurrenceDescriptor occurrence =
                scope.FindExactPackageOccurrence(binding)
                ?? throw new InvalidOperationException(
                    "A graph package binding was not retained in the completed Workspace Scope.");
            descriptors.Add(
                binding.Root.Identity,
                occurrence.Occurrence.Package);
        }

        var classifications =
            ImmutableArray.CreateBuilder<
                PackageDependencyMemberCallGraphNodeClassification>(
                    nodePackages.Length + nodePlatforms.Length);
        classifications.AddRange(
            nodePackages.Select(nodePackage =>
                new PackageDependencyMemberCallGraphNodeClassification
                    .Package(
                        nodePackage.NodeId,
                        descriptors.TryGetValue(
                                nodePackage.Package,
                                out WorkspacePackageDescriptor? descriptor)
                            ? descriptor
                            : throw new InvalidOperationException(
                                "A graph node named a package Root outside the completed graph bindings."))));
        classifications.AddRange(
            nodePlatforms.Select(nodePlatform =>
                new PackageDependencyMemberCallGraphNodeClassification
                    .Platform(
                        nodePlatform.NodeId,
                        nodePlatform.Target,
                        nodePlatform.LibraryIdentity)));
        return
        [
            .. classifications.OrderBy(
                static classification => classification.NodeId),
        ];
    }

    private static PackageDependencyMemberCallGraphRouteSubject
        DetachSubject(
            PackageDependencyWorkspaceRouteSubject subject) =>
        new(
            subject.RootOccurrenceIndex,
            subject.EdgeIndex,
            subject.Distance,
            subject.Edge.SourceCoordinate,
            subject.Edge.Declaration.CanonicalPackageId,
            subject.Edge.Declaration.CanonicalVersionConstraint,
            subject.Edge.Authority);

    private static PackageDependencyMemberCallGraphDestination
        DetachDestination(
            PackageDependencyWorkspaceDestination destination) =>
        destination switch
        {
            PackageDependencyWorkspaceDestination.Package package =>
                new PackageDependencyMemberCallGraphDestination.Package(
                    package.Occurrence.Occurrence.Identity,
                    package.Occurrence.Occurrence.Package,
                    package.Occurrence.Realization.Status,
                    package.Source),
            PackageDependencyWorkspaceDestination.Platform platform =>
                new PackageDependencyMemberCallGraphDestination.Platform(
                    platform.Delegation.Coordinate,
                    platform.Delegation.Target,
                    platform.Delegation.Supply),
            PackageDependencyWorkspaceDestination.Unavailable unavailable =>
                new PackageDependencyMemberCallGraphDestination.Unavailable(
                    unavailable.Reason,
                    unavailable.NoContributionReason),
            _ => throw new InvalidOperationException(
                "Unknown dependency Workspace destination."),
        };

    private static void ObserveCancellation(
        PackageSourceOperationLease sourceOperation)
    {
        sourceOperation.CancellationToken.ThrowIfCancellationRequested();
        sourceOperation.ThrowIfExpired();
        sourceOperation.OperationCancellationToken.ThrowIfCancellationRequested();
    }
}
