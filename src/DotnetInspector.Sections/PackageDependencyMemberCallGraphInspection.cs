using System.Collections.Immutable;
using System.Text.Json.Serialization;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using Inspector.Artifacts.Workspaces;
using NuGetFetch;

namespace DotnetInspector.Sections;

public sealed record PackageDependencyMemberCallGraphInspectionFocus
{
    public PackageDependencyMemberCallGraphInspectionFocus(
        Guid moduleVersionId,
        int methodToken)
    {
        if (moduleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "A dependency call-graph focus requires a module version identifier.",
                nameof(moduleVersionId));
        }

        ModuleVersionId = moduleVersionId;
        MethodToken = methodToken;
    }

    public Guid ModuleVersionId { get; }

    public int MethodToken { get; }
}

public sealed record PackageDependencyMemberCallGraphInspectionRequest
{
    public const int DefaultMaximumDependencyDepth = 8;
    public const int DefaultMaximumManifestProjections =
        WorkspaceScopeLimits.DefaultMaxPackages;
    public const int DefaultMaximumDeclarationResolutions = 1_024;

    public PackageDependencyMemberCallGraphInspectionRequest(
        PackageRootBinding root,
        PackageDependencyMemberCallGraphInspectionFocus focus,
        TraversalTargetFrameworkPolicy traversalTargetPolicy,
        MemberCallGraphCalleeNeighborhoodRequest graph,
        PackageHouseOperation realizationOperation,
        DateTimeOffset workspaceDeadline,
        int maximumDependencyDepth = DefaultMaximumDependencyDepth,
        PackageDependencyTraversalWorkBudget? traversalWorkBudget = null,
        PackageAssemblyContextRealizationOptions? realizationOptions = null,
        PackageSupplyChainBaseline supplyChainBaseline =
            PackageSupplyChainBaseline.Nothing,
        WorkspacePlan? workspacePlan = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(focus);
        ArgumentNullException.ThrowIfNull(traversalTargetPolicy);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(realizationOperation);
        if (realizationOperation.Profile
            != PackageHouseOperationProfile.Realize)
        {
            throw new ArgumentException(
                "Dependency call-graph inspection requires a PackageHouse Realize operation.",
                nameof(realizationOperation));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(
            maximumDependencyDepth);

        Root = root;
        Focus = focus;
        TraversalTargetPolicy = traversalTargetPolicy;
        Graph = graph;
        RealizationOperation = realizationOperation;
        WorkspaceDeadline = workspaceDeadline;
        MaximumDependencyDepth = maximumDependencyDepth;
        TraversalWorkBudget =
            traversalWorkBudget
            ?? new PackageDependencyTraversalWorkBudget(
                DefaultMaximumManifestProjections,
                DefaultMaximumDeclarationResolutions);
        RealizationOptions = realizationOptions;
        if (!Enum.IsDefined(supplyChainBaseline))
        {
            throw new ArgumentOutOfRangeException(
                nameof(supplyChainBaseline));
        }
        SupplyChainBaseline = supplyChainBaseline;
        WorkspacePlan = workspacePlan ?? WorkspacePlan.Empty;
    }

    public PackageRootBinding Root { get; }

    public PackageDependencyMemberCallGraphInspectionFocus Focus { get; }

    public TraversalTargetFrameworkPolicy TraversalTargetPolicy { get; }

    public MemberCallGraphCalleeNeighborhoodRequest Graph { get; }

    public PackageHouseOperation RealizationOperation { get; }

    public DateTimeOffset WorkspaceDeadline { get; }

    public int MaximumDependencyDepth { get; }

    public PackageDependencyTraversalWorkBudget TraversalWorkBudget { get; }

    public PackageAssemblyContextRealizationOptions? RealizationOptions
    {
        get;
    }

    public PackageSupplyChainBaseline SupplyChainBaseline { get; }

    public WorkspacePlan WorkspacePlan { get; }
}

public sealed class PackageDependencyMemberCallGraphInspectionSource
{
    private readonly Func<
        PackageHouseOperation,
        CancellationToken,
        PackageSourceOperationLease> _issueOperation;

    public PackageDependencyMemberCallGraphInspectionSource(
        IPackageDependencyTraversalCandidateResolver candidateResolver,
        IPackageDependencyTraversalManifestAcquirer manifestAcquirer,
        PackageHouse house,
        Func<
            PackageHouseOperation,
            CancellationToken,
            PackageSourceOperationLease> issueOperation)
    {
        CandidateResolver =
            candidateResolver
            ?? throw new ArgumentNullException(nameof(candidateResolver));
        ManifestAcquirer =
            manifestAcquirer
            ?? throw new ArgumentNullException(nameof(manifestAcquirer));
        House = house ?? throw new ArgumentNullException(nameof(house));
        _issueOperation =
            issueOperation
            ?? throw new ArgumentNullException(nameof(issueOperation));
    }

    public IPackageDependencyTraversalCandidateResolver CandidateResolver
    {
        get;
    }

    public IPackageDependencyTraversalManifestAcquirer ManifestAcquirer
    {
        get;
    }

    public PackageHouse House { get; }

    internal PackageSourceOperationLease IssueOperation(
        PackageHouseOperation operation,
        CancellationToken cancellationToken)
    {
        PackageSourceOperationLease sourceOperation =
            _issueOperation(operation, cancellationToken)
            ?? throw new InvalidOperationException(
                "The dependency call-graph source capability returned no Package Source operation.");
        if (sourceOperation.RequestTimeout != operation.RequestTimeout
            || sourceOperation.OperationTimeout
                != operation.OperationTimeout)
        {
            sourceOperation.Dispose();
            throw new InvalidOperationException(
                "The dependency call-graph source capability issued deadlines that differ from the PackageHouse operation.");
        }

        return sourceOperation;
    }
}

public sealed record PackageDependencyMemberCallGraphDocument(
    TraversalTargetFrameworkPolicy TraversalTargetPolicy,
    PackageDependencyTraversalSummary TraversalSummary,
    ImmutableArray<PackageDependencyMemberCallGraphInspectionRoute> Routes,
    PackageSupplyChainBaselineEvidence Baseline,
    ImmutableArray<PackageDependencyMemberCallGraphPackageSubject>
        PackageSubjects,
    InspectionGraphDocument Graph);

public sealed record PackageDependencyMemberCallGraphPackageSubject(
    int NodeId,
    string PackageId,
    string PackageVersion,
    string? TargetFramework);

public sealed record PackageDependencyMemberCallGraphInspectionRoute(
    PackageDependencyMemberCallGraphRouteSubject Subject,
    PackageDependencyMemberCallGraphInspectionDestination Destination);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(PackageDependencyMemberCallGraphInspectionDestination.Package),
    "package")]
[JsonDerivedType(
    typeof(PackageDependencyMemberCallGraphInspectionDestination.Platform),
    "platform")]
[JsonDerivedType(
    typeof(PackageDependencyMemberCallGraphInspectionDestination.Unavailable),
    "unavailable")]
public abstract record PackageDependencyMemberCallGraphInspectionDestination
{
    private PackageDependencyMemberCallGraphInspectionDestination()
    {
    }

    public sealed record Package(
        WorkspacePackageDescriptor Descriptor,
        ArtifactRootRealizationStatus RealizationStatus,
        PackageDependencyWorkspacePackageRouteSource Source)
        : PackageDependencyMemberCallGraphInspectionDestination;

    public sealed record Platform(
        PackageSourceCoordinate Coordinate,
        DotnetInspector.Platforms.PlatformFamilyTarget Target,
        PlatformSupply Supply)
        : PackageDependencyMemberCallGraphInspectionDestination;

    public sealed record Unavailable(
        PackageDependencyWorkspaceUnavailableReason Reason,
        PackageHouseRootNoContributionReason? NoContributionReason)
        : PackageDependencyMemberCallGraphInspectionDestination;
}

public enum PackageDependencyMemberCallGraphInspectionUnavailableReason
{
    RootDependencyContextUnavailable,
    RootDependencyContextFailed,
    RootWorkspaceNotCommitted,
    DependencyWorkspaceNotCommitted,
    FocusUnavailable,
    PackageContextCleanupFailed,
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(
    typeof(PackageDependencyMemberCallGraphInspectionOutcome.Available),
    "available")]
[JsonDerivedType(
    typeof(PackageDependencyMemberCallGraphInspectionOutcome.Unavailable),
    "unavailable")]
public abstract record PackageDependencyMemberCallGraphInspectionOutcome
{
    private PackageDependencyMemberCallGraphInspectionOutcome()
    {
    }

    public sealed record Available(
        PackageDependencyMemberCallGraphDocument Document)
        : PackageDependencyMemberCallGraphInspectionOutcome;

    public sealed record Unavailable(
        PackageDependencyMemberCallGraphInspectionUnavailableReason Reason,
        string Detail)
        : PackageDependencyMemberCallGraphInspectionOutcome;
}

public static class PackageDependencyMemberCallGraphInspection
{
    const string SharePath =
        "package-dependency-member-call-graph/share";

    public static async ValueTask<
        InspectionEnvelope<
            PackageDependencyMemberCallGraphInspectionOutcome>>
        ExecuteAsync(
            PackageDependencyMemberCallGraphInspectionRequest request,
            PackageDependencyMemberCallGraphInspectionSource source,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        RealizedPackageDependencyContextResult rootContext =
            await RealizedPackageDependencyContextQuery.ExecuteAsync(
                    request.Root,
                    cancellationToken)
                .ConfigureAwait(false);
        if (rootContext
            is not RealizedPackageDependencyContextResult.Available
                availableRoot)
        {
            return Envelope(
                rootContext switch
                {
                    RealizedPackageDependencyContextResult.Unavailable
                        unavailable =>
                        Unavailable(
                            PackageDependencyMemberCallGraphInspectionUnavailableReason
                                .RootDependencyContextUnavailable,
                            $"The selected root package does not expose dependency context ({unavailable.Reason})."),
                    RealizedPackageDependencyContextResult.Failed failed =>
                        Unavailable(
                            PackageDependencyMemberCallGraphInspectionUnavailableReason
                                .RootDependencyContextFailed,
                            failed.Failure.ManifestFailure is { } manifest
                                ? $"The selected root package dependency context failed ({manifest.Reason})."
                                : "The selected root package dependency context could not be decoded."),
                    _ => throw new InvalidOperationException(
                        "Unknown realized package dependency-context outcome."),
                });
        }

        using var traversalOperation = new NuGetOperationContext(
            request.RealizationOperation.RequestTimeout,
            request.RealizationOperation.OperationTimeout,
            cancellationToken);
        PackageDependencyTraversalOutcome traversal =
            await PackageDependencyTraversalQuery.ExecuteAsync(
                    new PackageDependencyTraversalRequest(
                        [
                            new PackageDependencyTraversalRootOccurrence(
                                availableRoot.Context,
                                PackageDependencyTraversalExpansionAuthority
                                    .RecursiveSources,
                                PackageDependencyTraversalRootRecurrenceAuthority
                                    .ExactCoordinate),
                        ],
                        request.TraversalTargetPolicy,
                        source.CandidateResolver,
                        source.ManifestAcquirer,
                        request.TraversalWorkBudget,
                        request.MaximumDependencyDepth),
                    cancellationToken,
                    traversalOperation)
                .ConfigureAwait(false);
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            executions = PrepareExecutions(request, traversal);

        await using var workspace =
            new InspectionWorkspace(request.WorkspacePlan);
        WorkspaceRegistrationReadResult registrationRead =
            workspace.GetRegistrationSnapshot();
        if (registrationRead
            is not WorkspaceRegistrationReadResult.Available
                registration)
        {
            var unavailable =
                (WorkspaceRegistrationReadResult.Unavailable)
                    registrationRead;
            return Envelope(
                Unavailable(
                    PackageDependencyMemberCallGraphInspectionUnavailableReason
                        .RootWorkspaceNotCommitted,
                    $"The operation Workspace registrations were unavailable ({unavailable.RuntimeFailure})."));
        }
        WorkspaceScopeReadResult initialRead =
            await workspace.GetScopeSnapshotAsync()
                .ConfigureAwait(false);
        if (initialRead is not WorkspaceScopeReadResult.Available
            initial)
        {
            var unavailable =
                (WorkspaceScopeReadResult.Unavailable)initialRead;
            return Envelope(
                Unavailable(
                    PackageDependencyMemberCallGraphInspectionUnavailableReason
                        .RootWorkspaceNotCommitted,
                    $"The operation Workspace was unavailable ({unavailable.RuntimeFailure})."));
        }

        WorkspaceScopeOperationResult rootAdmission =
            await workspace.AddPackagesAsync(
                    initial.Snapshot.Revision,
                    initial.Snapshot.PublicationBase,
                    [request.Root],
                    request.WorkspaceDeadline,
                    cancellationToken)
                .ConfigureAwait(false);
        WorkspaceScopeSnapshot? rootedScope = rootAdmission switch
        {
            WorkspaceScopeOperationResult.Committed committed =>
                committed.Snapshot,
            WorkspaceScopeOperationResult.NoEffect noEffect =>
                noEffect.Snapshot,
            _ => null,
        };
        if (rootedScope is null)
        {
            return Envelope(
                Unavailable(
                    PackageDependencyMemberCallGraphInspectionUnavailableReason
                        .RootWorkspaceNotCommitted,
                    DescribeScopeOperation(rootAdmission)));
        }

        var lowerRequest =
            new PackageDependencyMemberCallGraphRequest(
                workspace,
                rootedScope,
                registration.Revision,
                traversal,
                [request.Root],
                executions,
                new PackageDependencyMemberCallGraphFocus(
                    rootOccurrenceIndex: 0,
                    request.Focus.ModuleVersionId,
                    request.Focus.MethodToken),
                request.Graph,
                request.WorkspaceDeadline,
                request.RealizationOptions,
                request.SupplyChainBaseline);
        PackageSourceOperationLease sourceOperation =
            source.IssueOperation(
                request.RealizationOperation,
                cancellationToken);
        PackageDependencyMemberCallGraphOutcome lowerOutcome =
            await PackageDependencyMemberCallGraphOperation.ExecuteAsync(
                    lowerRequest,
                    source.House,
                    sourceOperation)
                .ConfigureAwait(false);

        PackageDependencyMemberCallGraphInspectionOutcome content =
            Project(lowerOutcome);
        return Envelope(
            content,
            content
                is PackageDependencyMemberCallGraphInspectionOutcome
                    .Available completed
                ? Diagnostics(completed.Document)
                : []);
    }

    static ImmutableArray<PackageDependencyEdgeRealizationExecution>
        PrepareExecutions(
            PackageDependencyMemberCallGraphInspectionRequest request,
            PackageDependencyTraversalOutcome traversal)
    {
        var executions =
            ImmutableArray.CreateBuilder<
                PackageDependencyEdgeRealizationExecution>();
        PackageHouseTargetContext target =
            PackageHouseTargetContext.Exact(
                request.TraversalTargetPolicy.TargetFramework);
        for (var rootIndex = 0;
            rootIndex < traversal.Roots.Length;
            rootIndex++)
        {
            PackageDependencyTraversalReachability reachability =
                traversal.RootReachability[rootIndex];
            for (var edgeIndex = 0;
                edgeIndex < traversal.Edges.Length;
                edgeIndex++)
            {
                if (!reachability.IsEdgeAdmitted(edgeIndex, out _)
                    || traversal.Edges[edgeIndex].Authority
                        != PackageDependencyTraversalEdgeEmissionAuthority
                            .ResolvedCandidate)
                {
                    continue;
                }

                executions.Add(
                    PackageDependencyEdgeRealizationQuery.Execute(
                        new PackageDependencyEdgeRealizationRequest(
                            traversal,
                            rootIndex,
                            edgeIndex,
                            request.RealizationOperation,
                            target)));
            }
        }

        return executions.ToImmutable();
    }

    static PackageDependencyMemberCallGraphInspectionOutcome Project(
        PackageDependencyMemberCallGraphOutcome outcome) =>
        outcome switch
        {
            PackageDependencyMemberCallGraphOutcome.Completed completed =>
                new PackageDependencyMemberCallGraphInspectionOutcome
                    .Available(
                        new PackageDependencyMemberCallGraphDocument(
                            completed.TraversalTargetPolicy,
                            completed.TraversalSummary,
                            [
                                .. completed.Routes.Select(Project),
                            ],
                            completed.Baseline,
                            [
                                .. completed.NodePackages.Select(
                                    static nodePackage =>
                                        new PackageDependencyMemberCallGraphPackageSubject(
                                            nodePackage.NodeId,
                                            nodePackage.Descriptor.PackageId,
                                            nodePackage.Descriptor.PackageVersion,
                                            nodePackage.Descriptor.TargetFramework)),
                            ],
                            completed.Graph)),
            PackageDependencyMemberCallGraphOutcome.WorkspaceNotCommitted
                notCommitted =>
                Unavailable(
                    PackageDependencyMemberCallGraphInspectionUnavailableReason
                        .DependencyWorkspaceNotCommitted,
                    DescribeScopeOperation(notCommitted.ScopeOperation)),
            PackageDependencyMemberCallGraphOutcome.Failed failed =>
                Unavailable(
                    failed.Reason switch
                    {
                        PackageDependencyMemberCallGraphFailureReason
                                .FocusUnavailable =>
                            PackageDependencyMemberCallGraphInspectionUnavailableReason
                                .FocusUnavailable,
                        PackageDependencyMemberCallGraphFailureReason
                                .PackageContextCleanupFailed =>
                            PackageDependencyMemberCallGraphInspectionUnavailableReason
                                .PackageContextCleanupFailed,
                        _ => throw new InvalidOperationException(
                            "Unknown dependency call-graph failure reason."),
                    },
                    failed.Detail),
            _ => throw new InvalidOperationException(
                "Unknown dependency member call-graph outcome."),
        };

    static PackageDependencyMemberCallGraphInspectionRoute Project(
        PackageDependencyMemberCallGraphRoute route) =>
        new(
            route.Subject,
            route.Destination switch
            {
                PackageDependencyMemberCallGraphDestination.Package package =>
                    new PackageDependencyMemberCallGraphInspectionDestination
                        .Package(
                            package.Descriptor,
                            package.RealizationStatus,
                            package.Source),
                PackageDependencyMemberCallGraphDestination.Platform platform =>
                    new PackageDependencyMemberCallGraphInspectionDestination
                        .Platform(
                            platform.Coordinate,
                            platform.Target,
                            platform.Supply),
                PackageDependencyMemberCallGraphDestination.Unavailable
                    unavailable =>
                    new PackageDependencyMemberCallGraphInspectionDestination
                        .Unavailable(
                            unavailable.Reason,
                            unavailable.NoContributionReason),
                _ => throw new InvalidOperationException(
                    "Unknown dependency call-graph route destination."),
            });

    static PackageDependencyMemberCallGraphInspectionOutcome.Unavailable
        Unavailable(
            PackageDependencyMemberCallGraphInspectionUnavailableReason reason,
            string detail) =>
        new(reason, detail);

    static InspectionEnvelope<
        PackageDependencyMemberCallGraphInspectionOutcome> Envelope(
            PackageDependencyMemberCallGraphInspectionOutcome content,
            IEnumerable<InspectionDiagnostic>? diagnostics = null) =>
        new(
            content,
            new InspectionShare.NonProjectable(
                SharePath,
                "Dependency-aware member call-graph plans do not yet have a canonical Workspace Share projection."),
            diagnostics);

    static ImmutableArray<InspectionDiagnostic> Diagnostics(
        PackageDependencyMemberCallGraphDocument document)
    {
        var diagnostics = ImmutableArray.CreateBuilder<InspectionDiagnostic>();
        foreach (PackageDependencyMemberCallGraphInspectionRoute route
            in document.Routes)
        {
            if (route.Destination
                is not PackageDependencyMemberCallGraphInspectionDestination
                    .Unavailable unavailable)
            {
                continue;
            }

            diagnostics.Add(
                new InspectionDiagnostic(
                    "package-dependency-member-call-graph.route-unavailable",
                    InspectionDiagnosticSeverity.Warning,
                    $"Dependency route to {route.Subject.TargetPackageId} was unavailable ({unavailable.Reason}).",
                    route.Subject.TargetPackageId));
        }

        foreach (InspectionGraphLimit limit in document.Graph.Limits
            .Where(static limit =>
                limit.Descriptor.Id
                    is not ("queries.neighborhood-depth-bound"
                        or "call.traversal-node-bound")))
        {
            diagnostics.Add(
                new InspectionDiagnostic(
                    "package-dependency-member-call-graph.graph-limit",
                    InspectionDiagnosticSeverity.Warning,
                    $"Call-graph analysis reached limit {limit.Descriptor.Id}.",
                    limit.Descriptor.Id));
        }

        foreach (InspectionGraphFailure failure in document.Graph.Failures)
        {
            diagnostics.Add(
                new InspectionDiagnostic(
                    "package-dependency-member-call-graph.graph-failure",
                    InspectionDiagnosticSeverity.Error,
                    $"Call-graph analysis reported failure {failure.Descriptor.Id}.",
                    failure.Descriptor.Id));
        }

        return diagnostics.ToImmutable();
    }

    static string DescribeScopeOperation(
        WorkspaceScopeOperationResult result) =>
        result switch
        {
            WorkspaceScopeOperationResult.Rejected rejected =>
                $"The Workspace rejected package publication ({rejected.Reason}).",
            WorkspaceScopeOperationResult.Failed failed =>
                $"Workspace package publication failed ({failed.Failure}).",
            WorkspaceScopeOperationResult.Unavailable unavailable =>
                $"The Workspace was unavailable ({unavailable.RuntimeFailure}).",
            WorkspaceScopeOperationResult.Cancelled =>
                "Workspace package publication was cancelled.",
            WorkspaceScopeOperationResult.Superseded =>
                "Workspace package publication was superseded.",
            WorkspaceScopeOperationResult.Committed
                or WorkspaceScopeOperationResult.NoEffect =>
                throw new InvalidOperationException(
                    "A completed Workspace publication cannot be described as unavailable."),
            _ => throw new InvalidOperationException(
                "Unknown Workspace Scope operation outcome."),
        };
}
