using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// One admitted traversal edge occurrence selected for Workspace destination
/// composition.
/// </summary>
public sealed class PackageDependencyWorkspaceRouteSubject
{
    internal PackageDependencyWorkspaceRouteSubject(
        PackageDependencyTraversalOutcome traversal,
        int rootOccurrenceIndex,
        int edgeIndex,
        int distance)
    {
        Traversal = traversal;
        RootOccurrenceIndex = rootOccurrenceIndex;
        EdgeIndex = edgeIndex;
        Distance = distance;
    }

    public PackageDependencyTraversalOutcome Traversal { get; }

    public int RootOccurrenceIndex { get; }

    public PackageDependencyTraversalRootResult Root =>
        Traversal.Roots[RootOccurrenceIndex];

    public int EdgeIndex { get; }

    public PackageDependencyTraversalEdge Edge =>
        Traversal.Edges[EdgeIndex];

    public int Distance { get; }
}

public enum PackageDependencyWorkspacePackageRouteSource
{
    ResolvedCandidate,
    SuppliedRoot,
}

public enum PackageDependencyWorkspaceUnavailableReason
{
    TraversalBoundary,
    PackageRootUnavailable,
}

/// <summary>
/// The typed destination of one root-relative admitted traversal edge.
/// </summary>
public abstract class PackageDependencyWorkspaceDestination
{
    private protected PackageDependencyWorkspaceDestination(
        PackageDependencyWorkspaceRouteSubject subject) =>
        Subject = subject;

    public PackageDependencyWorkspaceRouteSubject Subject { get; }

    public sealed class Package : PackageDependencyWorkspaceDestination
    {
        internal Package(
            PackageDependencyWorkspaceRouteSubject subject,
            WorkspacePackageOccurrenceDescriptor occurrence,
            PackageRootBinding binding,
            PackageDependencyWorkspacePackageRouteSource source,
            PackageDependencyEdgeRealizationEvidence? realization)
            : base(subject)
        {
            Occurrence = occurrence;
            Binding = binding;
            Source = source;
            Realization = realization;
        }

        public WorkspacePackageOccurrenceDescriptor Occurrence { get; }

        internal PackageRootBinding Binding { get; }

        public PackageDependencyWorkspacePackageRouteSource Source { get; }

        public PackageDependencyEdgeRealizationEvidence? Realization { get; }
    }

    public sealed class Platform : PackageDependencyWorkspaceDestination
    {
        internal Platform(
            PackageDependencyWorkspaceRouteSubject subject,
            PlatformDelegation delegation,
            PackageDependencyEdgeRealizationEvidence realization)
            : base(subject)
        {
            Delegation = delegation;
            Realization = realization;
        }

        public PlatformDelegation Delegation { get; }

        public PackageDependencyEdgeRealizationEvidence Realization { get; }
    }

    public sealed class Unavailable : PackageDependencyWorkspaceDestination
    {
        internal Unavailable(
            PackageDependencyWorkspaceRouteSubject subject,
            PackageDependencyWorkspaceUnavailableReason reason,
            PackageDependencyEdgeRealizationEvidence? realization,
            PackageHouseRootNoContributionReason? noContributionReason)
            : base(subject)
        {
            Reason = reason;
            Realization = realization;
            NoContributionReason = noContributionReason;
        }

        public PackageDependencyWorkspaceUnavailableReason Reason { get; }

        public PackageDependencyEdgeRealizationEvidence? Realization { get; }

        public PackageHouseRootNoContributionReason? NoContributionReason
        {
            get;
        }
    }
}

/// <summary>
/// One exact Workspace publication request for a completed traversal and its
/// destination realization evidence.
/// </summary>
public sealed record PackageDependencyWorkspaceRouteRequest
{
    public PackageDependencyWorkspaceRouteRequest(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope,
        PackageDependencyTraversalOutcome traversal,
        ImmutableArray<PackageRootBinding> rootBindings,
        ImmutableArray<PackageDependencyEdgeRealizationEvidence> realizations,
        DateTimeOffset deadline)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(traversal);

        Workspace = workspace;
        Scope = scope;
        Traversal = traversal;
        RootBindings = rootBindings;
        Realizations = realizations;
        Deadline = deadline;
    }

    public InspectionWorkspace Workspace { get; }

    public WorkspaceScopeSnapshot Scope { get; }

    public PackageDependencyTraversalOutcome Traversal { get; }

    public ImmutableArray<PackageRootBinding> RootBindings { get; }

    public ImmutableArray<PackageDependencyEdgeRealizationEvidence>
        Realizations
    { get; }

    public DateTimeOffset Deadline { get; }
}

/// <summary>
/// Terminal outcome of one Package dependency Workspace route composition.
/// </summary>
public abstract class PackageDependencyWorkspaceRouteOutcome
{
    private protected PackageDependencyWorkspaceRouteOutcome(
        WorkspaceScopeOperationResult scopeOperation) =>
        ScopeOperation = scopeOperation;

    public WorkspaceScopeOperationResult ScopeOperation { get; }

    public sealed class Completed : PackageDependencyWorkspaceRouteOutcome
    {
        internal Completed(
            WorkspaceScopeOperationResult scopeOperation,
            WorkspaceScopeSnapshot scope,
            ImmutableArray<PackageDependencyWorkspaceDestination>
                destinations)
            : base(scopeOperation)
        {
            Scope = scope;
            Destinations = destinations;
        }

        public WorkspaceScopeSnapshot Scope { get; }

        public ImmutableArray<PackageDependencyWorkspaceDestination>
            Destinations
        { get; }
    }

    public sealed class NotCommitted :
        PackageDependencyWorkspaceRouteOutcome
    {
        internal NotCommitted(
            WorkspaceScopeOperationResult scopeOperation)
            : base(scopeOperation)
        {
        }
    }
}

/// <summary>
/// Atomically admits realized Package destinations to Workspace Scope and
/// lowers every admitted traversal edge occurrence to a typed destination.
/// </summary>
public static class PackageDependencyWorkspaceRouteQuery
{
    public static InspectionQuery<PackageDependencyWorkspaceRouteOutcome>
        Definition { get; } =
        new(
            "Package dependency Workspace routes",
            InspectionCost.NetworkFree);

    public static async ValueTask<PackageDependencyWorkspaceRouteOutcome>
        ExecuteAsync(
            PackageDependencyWorkspaceRouteRequest request,
            CancellationToken cancellationToken = default)
    {
            ArgumentNullException.ThrowIfNull(request);
            ValidateRoots(request);
        Dictionary<(int Root, int Edge),
            PackageDependencyEdgeRealizationEvidence> realizations =
            ValidateRealizations(request);

        var plans = ImmutableArray.CreateBuilder<DestinationPlan>();
        var contributions = ImmutableArray.CreateBuilder<PackageRootBinding>();
        var contributedBindings =
            new Dictionary<
                PackageAcquisitionCandidateCorrespondence,
                PackageRootBinding>();
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
                if (!reachability.IsEdgeAdmitted(
                        edgeIndex,
                        out int distance))
                {
                    continue;
                }

                var subject = new PackageDependencyWorkspaceRouteSubject(
                    request.Traversal,
                    rootIndex,
                    edgeIndex,
                    distance);
                PackageDependencyTraversalEdge edge = subject.Edge;
                switch (edge.Authority)
                {
                    case PackageDependencyTraversalEdgeEmissionAuthority
                        .ResolvedCandidate:
                    {
                        PackageDependencyEdgeRealizationEvidence realization =
                            realizations[(rootIndex, edgeIndex)];
                        if (realization.PlatformDelegation is { } delegation)
                        {
                            plans.Add(new DestinationPlan.Platform(
                                subject,
                                delegation,
                                realization));
                            break;
                        }

                        if (realization.RootContribution
                            is PackageHouseRootContributionOutcome.Contributed
                                contributed)
                        {
                            PackageRootBinding binding;
                            if (!contributedBindings.TryGetValue(
                                    realization.Subject.Candidate
                                        .Correspondence,
                                    out binding!))
                            {
                                binding =
                                    contributed.Contribution.Binding;
                                contributedBindings.Add(
                                    realization.Subject.Candidate
                                        .Correspondence,
                                    binding);
                                contributions.Add(binding);
                            }
                            plans.Add(new DestinationPlan.Package(
                                subject,
                                binding,
                                realization));
                            break;
                        }

                        var unavailable =
                            (PackageHouseRootContributionOutcome.NoContribution)
                                realization.RootContribution;
                        plans.Add(new DestinationPlan.Unavailable(
                            subject,
                            realization,
                            unavailable.Reason));
                        break;
                    }
                    case PackageDependencyTraversalEdgeEmissionAuthority
                        .SuppliedRoot:
                    {
                        if (edge.Target
                                is not PackageDependencyTraversalEdgeTarget.Node
                                    target
                            || target.ProjectionIndex < 0
                            || target.ProjectionIndex
                                >= request.Traversal.Projections.Length)
                        {
                            throw new ArgumentException(
                                "A supplied-root edge must target one exact traversal root occurrence.",
                                nameof(request));
                        }

                        PackageDependencyTraversalProjection projection =
                            request.Traversal.Projections[
                                target.ProjectionIndex];
                        if (projection.Kind
                                != PackageDependencyTraversalProjectionKind
                                    .RootSupplied
                            || projection.NodeIndex != target.NodeIndex
                            || projection.RootOccurrenceIndex
                                is not int suppliedRootIndex
                            || suppliedRootIndex < 0
                            || suppliedRootIndex
                                >= request.RootBindings.Length)
                        {
                            throw new ArgumentException(
                                "A supplied-root edge must target one exact traversal root occurrence.",
                                nameof(request));
                        }

                        plans.Add(new DestinationPlan.SuppliedRoot(
                            subject,
                            request.RootBindings[suppliedRootIndex]));
                        break;
                    }
                    default:
                        plans.Add(new DestinationPlan.Boundary(subject));
                        break;
                }
            }
        }

        WorkspaceScopeOperationResult scopeOperation =
            await request.Workspace.AddPackagesAsync(
                    request.Scope.Revision,
                    request.Scope.PublicationBase,
                    contributions.ToImmutable(),
                    request.Deadline,
                    cancellationToken)
                .ConfigureAwait(false);
        WorkspaceScopeSnapshot? finalScope = scopeOperation switch
        {
            WorkspaceScopeOperationResult.Committed committed =>
                committed.Snapshot,
            WorkspaceScopeOperationResult.NoEffect noEffect =>
                noEffect.Snapshot,
            _ => null,
        };
        if (finalScope is null)
        {
            return new PackageDependencyWorkspaceRouteOutcome.NotCommitted(
                scopeOperation);
        }

        var destinations =
            ImmutableArray.CreateBuilder<
                PackageDependencyWorkspaceDestination>(plans.Count);
        foreach (DestinationPlan plan in plans)
        {
            destinations.Add(plan.Complete(finalScope));
        }

        return new PackageDependencyWorkspaceRouteOutcome.Completed(
            scopeOperation,
            finalScope,
            destinations.MoveToImmutable());
    }

    private static void ValidateRoots(
        PackageDependencyWorkspaceRouteRequest request)
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

        for (int index = 0; index < request.RootBindings.Length; index++)
        {
            PackageDependencyTraversalRootResult root =
                request.Traversal.Roots[index];
            PackageRootBinding binding = request.RootBindings[index];
            if (root.OccurrenceIndex != index
                || root.Occurrence.Source
                    is not PackageDependencyTraversalRootSource.RealizedPackage
                        realized
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
                request.Scope.FindPackageOccurrence(binding);
            if (occurrence?.Realization.Status
                is not ArtifactRootRealizationStatus.Ready)
            {
                throw new ArgumentException(
                    "Every traversal root binding must be Ready in the captured Workspace Scope.",
                    nameof(request));
            }

        }
    }

    private static Dictionary<(int Root, int Edge),
        PackageDependencyEdgeRealizationEvidence> ValidateRealizations(
            PackageDependencyWorkspaceRouteRequest request)
    {
        if (request.Realizations.IsDefault
            || request.Realizations.Any(
                static realization => realization is null))
        {
            throw new ArgumentException(
                "Completed edge realizations must be a non-default collection.",
                nameof(request));
        }

        var realizations =
            new Dictionary<(int Root, int Edge),
                PackageDependencyEdgeRealizationEvidence>();
        foreach (PackageDependencyEdgeRealizationEvidence realization
            in request.Realizations)
        {
            PackageDependencyEdgeRealizationSubject subject =
                realization.Subject;
            if (!ReferenceEquals(subject.Traversal, request.Traversal)
                || !realizations.TryAdd(
                    (subject.RootOccurrenceIndex, subject.EdgeIndex),
                    realization))
            {
                throw new ArgumentException(
                    "Each edge realization must belong to one unique occurrence in the request's exact traversal.",
                    nameof(request));
            }
        }

        int requiredCount = 0;
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

                requiredCount++;
                if (!realizations.ContainsKey((rootIndex, edgeIndex)))
                {
                    throw new ArgumentException(
                        "Every admitted resolved-candidate edge occurrence requires its exact completed realization.",
                        nameof(request));
                }
            }
        }
        if (realizations.Count != requiredCount)
        {
            throw new ArgumentException(
                "Realization evidence may name only admitted resolved-candidate edge occurrences.",
                nameof(request));
        }

        return realizations;
    }

    private abstract class DestinationPlan
    {
        private protected DestinationPlan(
            PackageDependencyWorkspaceRouteSubject subject) =>
            Subject = subject;

        protected PackageDependencyWorkspaceRouteSubject Subject { get; }

        internal abstract PackageDependencyWorkspaceDestination Complete(
            WorkspaceScopeSnapshot scope);

        internal sealed class Package : DestinationPlan
        {
            internal Package(
                PackageDependencyWorkspaceRouteSubject subject,
                PackageRootBinding binding,
                PackageDependencyEdgeRealizationEvidence realization)
                : base(subject)
            {
                Binding = binding;
                Realization = realization;
            }

            PackageRootBinding Binding { get; }

            PackageDependencyEdgeRealizationEvidence Realization { get; }

            internal override PackageDependencyWorkspaceDestination Complete(
                WorkspaceScopeSnapshot scope)
            {
                WorkspacePackageOccurrenceDescriptor? occurrence =
                    scope.FindExactPackageOccurrence(Binding);
                if (occurrence is null)
                {
                    throw new InvalidOperationException(
                        "The committed Workspace Scope omitted one exact admitted dependency Package.");
                }

                return new PackageDependencyWorkspaceDestination.Package(
                    Subject,
                    occurrence,
                    Binding,
                    PackageDependencyWorkspacePackageRouteSource
                        .ResolvedCandidate,
                    Realization);
            }
        }

        internal sealed class SuppliedRoot : DestinationPlan
        {
            internal SuppliedRoot(
                PackageDependencyWorkspaceRouteSubject subject,
                PackageRootBinding binding)
                : base(subject) =>
                Binding = binding;

            PackageRootBinding Binding { get; }

            internal override PackageDependencyWorkspaceDestination Complete(
                WorkspaceScopeSnapshot scope)
            {
                WorkspacePackageOccurrenceDescriptor? occurrence =
                    scope.FindExactPackageOccurrence(Binding);
                if (occurrence is null)
                {
                    throw new InvalidOperationException(
                        "The committed Workspace Scope omitted one exact supplied dependency root.");
                }

                return new PackageDependencyWorkspaceDestination.Package(
                    Subject,
                    occurrence,
                    Binding,
                    PackageDependencyWorkspacePackageRouteSource.SuppliedRoot,
                    realization: null);
            }
        }

        internal sealed class Platform : DestinationPlan
        {
            internal Platform(
                PackageDependencyWorkspaceRouteSubject subject,
                PlatformDelegation delegation,
                PackageDependencyEdgeRealizationEvidence realization)
                : base(subject)
            {
                Delegation = delegation;
                Realization = realization;
            }

            PlatformDelegation Delegation { get; }

            PackageDependencyEdgeRealizationEvidence Realization { get; }

            internal override PackageDependencyWorkspaceDestination Complete(
                WorkspaceScopeSnapshot scope) =>
                new PackageDependencyWorkspaceDestination.Platform(
                    Subject,
                    Delegation,
                    Realization);
        }

        internal sealed class Unavailable : DestinationPlan
        {
            internal Unavailable(
                PackageDependencyWorkspaceRouteSubject subject,
                PackageDependencyEdgeRealizationEvidence realization,
                PackageHouseRootNoContributionReason reason)
                : base(subject)
            {
                Realization = realization;
                Reason = reason;
            }

            PackageDependencyEdgeRealizationEvidence Realization { get; }

            PackageHouseRootNoContributionReason Reason { get; }

            internal override PackageDependencyWorkspaceDestination Complete(
                WorkspaceScopeSnapshot scope) =>
                new PackageDependencyWorkspaceDestination.Unavailable(
                    Subject,
                    PackageDependencyWorkspaceUnavailableReason
                        .PackageRootUnavailable,
                    Realization,
                    Reason);
        }

        internal sealed class Boundary : DestinationPlan
        {
            internal Boundary(
                PackageDependencyWorkspaceRouteSubject subject)
                : base(subject)
            {
            }

            internal override PackageDependencyWorkspaceDestination Complete(
                WorkspaceScopeSnapshot scope) =>
                new PackageDependencyWorkspaceDestination.Unavailable(
                    Subject,
                    PackageDependencyWorkspaceUnavailableReason
                        .TraversalBoundary,
                    realization: null,
                    noContributionReason: null);
        }
    }
}
