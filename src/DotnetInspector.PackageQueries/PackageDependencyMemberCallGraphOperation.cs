using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
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
        PackageDependencyTraversalOutcome traversal,
        ImmutableArray<PackageRootBinding> rootBindings,
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            edgeExecutions,
        PackageDependencyMemberCallGraphFocus focus,
        MemberCallGraphCalleeNeighborhoodRequest graph,
        DateTimeOffset workspaceDeadline,
        PackageAssemblyContextRealizationOptions? realizationOptions = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(graph);

        Workspace = workspace;
        Scope = scope;
        Traversal = traversal;
        RootBindings = rootBindings;
        EdgeExecutions = edgeExecutions;
        Focus = focus;
        Graph = graph;
        WorkspaceDeadline = workspaceDeadline;
        RealizationOptions = realizationOptions;
    }

    public InspectionWorkspace Workspace { get; }

    public WorkspaceScopeSnapshot Scope { get; }

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

public enum PackageDependencyMemberCallGraphFailureReason
{
    FocusUnavailable,
    PackageContextCleanupFailed,
}

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
        ImmutableArray<PackageDependencyMemberCallGraphRoute> Routes,
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

    public static async Task<PackageDependencyMemberCallGraphOutcome>
        ExecuteAsync(
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
            return new PackageDependencyMemberCallGraphOutcome
                .WorkspaceNotCommitted(notCommitted.ScopeOperation);
        }

        var completedRoutes =
            (PackageDependencyWorkspaceRouteOutcome.Completed)
                routeOutcome;
        ImmutableArray<PackageRootBinding> graphBindings =
            GraphBindings(
                request,
                completedRoutes);
        PackageAssemblyContextCompletionOperation contextOperation =
            request.Workspace.PreparePackageAssemblyContextCompletion(
                graphBindings,
                request.RealizationOptions);
        PackageAssemblyContextCompletion completion =
            await contextOperation.ExecuteAsync(
                    contextOperation.Identity)
                .ConfigureAwait(false);

        PackageRoleMemberCallGraphOutcome? graphOutcome = null;
        PackageRoleCleanupReport cleanup;
        PackageAssemblyContextProjection? projection = null;
        try
        {
            projection = completion.CreateProjection(graphBindings);
            PackageRootIdentity root =
                request.RootBindings[
                    request.Focus.RootOccurrenceIndex].Root.Identity;
            graphOutcome = PackageRoleMemberCallGraphQuery.Execute(
                projection,
                new PackageRoleMemberCallGraphFocus(
                    root,
                    request.Focus.ModuleVersionId,
                    request.Focus.MethodToken),
                request.Graph);
        }
        finally
        {
            if (projection is not null)
            {
                await projection.ReturnAsync()
                    .ConfigureAwait(false);
            }
            cleanup = await completion.CloseAsync()
                .ConfigureAwait(false);
        }

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

        if (graphOutcome
            is PackageRoleMemberCallGraphOutcome.Unavailable unavailable)
        {
            return new PackageDependencyMemberCallGraphOutcome.Failed(
                PackageDependencyMemberCallGraphFailureReason
                    .FocusUnavailable,
                unavailable.Failure.Detail,
                unavailable.Failure);
        }

        InspectionGraphDocument graph =
            ((PackageRoleMemberCallGraphOutcome.Available)graphOutcome!)
                .Document;
        return new PackageDependencyMemberCallGraphOutcome.Completed(
            request.Traversal.TraversalTargetPolicy,
            request.Traversal.Summary,
            completedRoutes.Scope.Revision.Identity,
            DetachRoutes(completedRoutes),
            graph);
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
        var candidates = new List<PackageRootBinding>(
            request.RootBindings.Length + routes.Destinations.Length);
        candidates.Add(
            request.RootBindings[
                request.Focus.RootOccurrenceIndex]);
        for (int rootIndex = 0;
            rootIndex < request.RootBindings.Length;
            rootIndex++)
        {
            if (rootIndex != request.Focus.RootOccurrenceIndex)
                candidates.Add(request.RootBindings[rootIndex]);
        }
        foreach (PackageDependencyWorkspaceDestination.Package package
            in routes.Destinations.OfType<
                PackageDependencyWorkspaceDestination.Package>())
        {
            if (package.Source
                    != PackageDependencyWorkspacePackageRouteSource
                        .ResolvedCandidate
                || package.Realization?.RootContribution
                    is not PackageHouseRootContributionOutcome.Contributed
                        contributed)
            {
                continue;
            }
            candidates.Add(contributed.Contribution.Binding);
        }

        var bindings = ImmutableArray.CreateBuilder<PackageRootBinding>();
        var admitted = new HashSet<WorkspacePackageOccurrenceIdentity>(
            ReferenceEqualityComparer.Instance);
        foreach (WorkspacePackageOccurrenceDescriptor occurrence
            in routes.Scope.Packages)
        {
            PackageRootBinding? binding = candidates.FirstOrDefault(
                candidate => ReferenceEquals(
                    routes.Scope.FindExactPackageOccurrence(candidate),
                    occurrence));
            if (binding is null
                || !admitted.Add(occurrence.Occurrence.Identity))
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

    private static ImmutableArray<PackageDependencyMemberCallGraphRoute>
        DetachRoutes(
            PackageDependencyWorkspaceRouteOutcome.Completed routes) =>
        [
            .. routes.Destinations.Select(destination =>
                new PackageDependencyMemberCallGraphRoute(
                    DetachSubject(destination.Subject),
                    DetachDestination(destination))),
        ];

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
