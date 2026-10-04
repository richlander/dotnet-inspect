using System.Collections.Immutable;

using DotnetInspector.Queries;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// Request-neutral disposition of one exact root-relative Package edge.
/// </summary>
public enum PackageAssemblyReferenceRouteDisposition
{
    PackageCandidate,
    PlatformDelegated,
}

/// <summary>
/// Typed reason one root-relative Package route projection cannot close.
/// </summary>
public enum PackageAssemblyReferenceRouteProjectionIncompleteReason
{
    TraversalIncomplete,
    AdmittedEdgeWithoutCandidate,
}

/// <summary>
/// One exact root-relative Package edge eligible for later AssemblyRef routing.
/// </summary>
public sealed class PackageAssemblyReferenceRouteOccurrence
{
    internal PackageAssemblyReferenceRouteOccurrence(
        PackageDependencyWorkspaceRouteSubject subject,
        PackageDependencyEdgeRealizationExecution execution,
        PackageAssemblyReferenceRouteDisposition disposition)
    {
        Subject = subject;
        Execution = execution;
        Disposition = disposition;
    }

    public PackageDependencyWorkspaceRouteSubject Subject { get; }

    public PackageDependencyEdgeRealizationExecution Execution { get; }

    public PackageAssemblyReferenceRouteDisposition Disposition { get; }
}

/// <summary>
/// Exact generation- and focal-scope-bound Package route eligibility.
/// </summary>
public sealed class PackageAssemblyReferenceRouteEligibilityReceipt
{
    internal PackageAssemblyReferenceRouteEligibilityReceipt(
        AssemblyReferenceResolutionGenerationReceipt generation,
        MemberCallGraphFocalScopeReceipt focalScope,
        PackageDependencyTraversalOutcome traversal,
        int rootOccurrenceIndex,
        ImmutableArray<PackageAssemblyReferenceRouteOccurrence> routes)
    {
        Generation = generation;
        FocalScope = focalScope;
        Traversal = traversal;
        RootOccurrenceIndex = rootOccurrenceIndex;
        Routes = routes;
    }

    public AssemblyReferenceResolutionGenerationReceipt Generation { get; }

    public MemberCallGraphFocalScopeReceipt FocalScope { get; }

    public PackageDependencyTraversalOutcome Traversal { get; }

    public int RootOccurrenceIndex { get; }

    public PackageDependencyTraversalRootResult Root =>
        Traversal.Roots[RootOccurrenceIndex];

    public ImmutableArray<PackageAssemblyReferenceRouteOccurrence> Routes
    { get; }
}

/// <summary>
/// Inputs for projecting one Package traversal root into request-neutral
/// AssemblyRef route eligibility.
/// </summary>
public sealed record PackageAssemblyReferenceRouteProjectionRequest
{
    public PackageAssemblyReferenceRouteProjectionRequest(
        AssemblyReferenceResolutionGenerationReceipt generation,
        MemberCallGraphFocalScopeReceipt focalScope,
        PackageDependencyTraversalOutcome traversal,
        int rootOccurrenceIndex,
        ImmutableArray<PackageDependencyEdgeRealizationExecution>
            edgeExecutions)
    {
        ArgumentNullException.ThrowIfNull(generation);
        ArgumentNullException.ThrowIfNull(focalScope);
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentOutOfRangeException.ThrowIfNegative(rootOccurrenceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            rootOccurrenceIndex,
            traversal.Roots.Length);
        if (!ReferenceEquals(
                generation.ScopeRevision,
                focalScope.ScopeRevision))
        {
            throw new ArgumentException(
                "The generation and focal scope must retain the exact Workspace Scope revision.",
                nameof(focalScope));
        }
        if (focalScope.FocalLength
            != MemberCallGraphFocalLength.Everything)
        {
            throw new ArgumentException(
                "The Package route projection supports only the currently implemented Everything focal scope.",
                nameof(focalScope));
        }
        if (edgeExecutions.IsDefault
            || edgeExecutions.Any(static execution => execution is null))
        {
            throw new ArgumentException(
                "Edge executions must be a non-default collection.",
                nameof(edgeExecutions));
        }

        Generation = generation;
        FocalScope = focalScope;
        Traversal = traversal;
        RootOccurrenceIndex = rootOccurrenceIndex;
        EdgeExecutions = edgeExecutions;
    }

    public AssemblyReferenceResolutionGenerationReceipt Generation { get; }

    public MemberCallGraphFocalScopeReceipt FocalScope { get; }

    public PackageDependencyTraversalOutcome Traversal { get; }

    public int RootOccurrenceIndex { get; }

    public ImmutableArray<PackageDependencyEdgeRealizationExecution>
        EdgeExecutions
    { get; }
}

/// <summary>
/// Closed result of projecting one Package traversal root into request-neutral
/// AssemblyRef route eligibility.
/// </summary>
public abstract record PackageAssemblyReferenceRouteProjectionOutcome
{
    private protected PackageAssemblyReferenceRouteProjectionOutcome(
        AssemblyReferenceResolutionGenerationReceipt generation,
        MemberCallGraphFocalScopeReceipt focalScope,
        PackageDependencyTraversalOutcome traversal,
        int rootOccurrenceIndex,
        ImmutableArray<PackageAssemblyReferenceRouteOccurrence> routes)
    {
        Generation = generation;
        FocalScope = focalScope;
        Traversal = traversal;
        RootOccurrenceIndex = rootOccurrenceIndex;
        Routes = routes;
    }

    public AssemblyReferenceResolutionGenerationReceipt Generation { get; }

    public MemberCallGraphFocalScopeReceipt FocalScope { get; }

    public PackageDependencyTraversalOutcome Traversal { get; }

    public int RootOccurrenceIndex { get; }

    public ImmutableArray<PackageAssemblyReferenceRouteOccurrence> Routes
    { get; }

    public sealed record Completed :
        PackageAssemblyReferenceRouteProjectionOutcome
    {
        internal Completed(
            PackageAssemblyReferenceRouteEligibilityReceipt receipt)
            : base(
                receipt.Generation,
                receipt.FocalScope,
                receipt.Traversal,
                receipt.RootOccurrenceIndex,
                receipt.Routes) =>
            Receipt = receipt;

        public PackageAssemblyReferenceRouteEligibilityReceipt Receipt
        { get; }
    }

    public sealed record Incomplete :
        PackageAssemblyReferenceRouteProjectionOutcome
    {
        internal Incomplete(
            AssemblyReferenceResolutionGenerationReceipt generation,
            MemberCallGraphFocalScopeReceipt focalScope,
            PackageDependencyTraversalOutcome traversal,
            int rootOccurrenceIndex,
            ImmutableArray<PackageAssemblyReferenceRouteOccurrence> routes,
            PackageAssemblyReferenceRouteProjectionIncompleteReason
                reason,
            PackageDependencyWorkspaceRouteSubject? boundary)
            : base(
                generation,
                focalScope,
                traversal,
                rootOccurrenceIndex,
                routes)
        {
            Reason = reason;
            Boundary = boundary;
        }

        public PackageAssemblyReferenceRouteProjectionIncompleteReason
            Reason
        { get; }

        public string Description =>
            Reason switch
            {
                PackageAssemblyReferenceRouteProjectionIncompleteReason
                    .TraversalIncomplete =>
                    "The reachable PackageRef snapshot is not complete.",
                PackageAssemblyReferenceRouteProjectionIncompleteReason
                    .AdmittedEdgeWithoutCandidate =>
                    "The reachable PackageRef snapshot contains an admitted edge without exact candidate evidence.",
                _ => throw new InvalidOperationException(
                    $"Unknown Package route projection reason '{Reason}'."),
            };

        public PackageDependencyWorkspaceRouteSubject? Boundary { get; }
    }
}

/// <summary>
/// Projects exact Package Dependency Traversal evidence without acquiring
/// package payloads or correlating one AssemblyRef request.
/// </summary>
public static class PackageAssemblyReferenceRouteProjection
{
    public static PackageAssemblyReferenceRouteProjectionOutcome Project(
        PackageAssemblyReferenceRouteProjectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PackageDependencyTraversalOutcome traversal = request.Traversal;
        if (traversal.RootReachability.Length != traversal.Roots.Length
            || traversal.Roots[request.RootOccurrenceIndex].OccurrenceIndex
                != request.RootOccurrenceIndex)
        {
            throw new ArgumentException(
                "Traversal reachability must align with its root occurrences.",
                nameof(request));
        }

        var provided =
            new Dictionary<int, PackageDependencyEdgeRealizationExecution>();
        PackageDependencyTraversalReachability reachability =
            traversal.RootReachability[request.RootOccurrenceIndex];
        foreach (PackageDependencyEdgeRealizationExecution execution
            in request.EdgeExecutions)
        {
            PackageDependencyEdgeRealizationSubject subject =
                execution.Subject;
            if (!ReferenceEquals(subject.Traversal, traversal)
                || subject.RootOccurrenceIndex
                    != request.RootOccurrenceIndex
                || (uint)subject.EdgeIndex
                    >= (uint)traversal.Edges.Length
                || !reachability.IsEdgeAdmitted(
                    subject.EdgeIndex,
                    out _)
                || traversal.Edges[subject.EdgeIndex].Authority
                    != PackageDependencyTraversalEdgeEmissionAuthority
                        .ResolvedCandidate
                || !provided.TryAdd(subject.EdgeIndex, execution))
            {
                throw new ArgumentException(
                    "Each edge execution must belong to one unique admitted resolved-candidate edge of the requested traversal root.",
                    nameof(request));
            }
        }

        var subjects =
            new List<PackageDependencyWorkspaceRouteSubject>();
        for (int edgeIndex = 0;
            edgeIndex < traversal.Edges.Length;
            edgeIndex++)
        {
            if (reachability.IsEdgeAdmitted(
                    edgeIndex,
                    out int distance))
            {
                subjects.Add(
                    new(
                        traversal,
                        request.RootOccurrenceIndex,
                        edgeIndex,
                        distance));
            }
        }
        subjects.Sort(
            static (left, right) =>
            {
                int byDistance = left.Distance.CompareTo(right.Distance);
                return byDistance != 0
                    ? byDistance
                    : left.EdgeIndex.CompareTo(right.EdgeIndex);
            });

        var routes =
            ImmutableArray.CreateBuilder<
                PackageAssemblyReferenceRouteOccurrence>(
                subjects.Count);
        PackageDependencyWorkspaceRouteSubject? incompleteBoundary = null;
        foreach (PackageDependencyWorkspaceRouteSubject subject
            in subjects)
        {
            PackageDependencyTraversalEdge edge =
                traversal.Edges[subject.EdgeIndex];
            if (edge.Authority
                != PackageDependencyTraversalEdgeEmissionAuthority
                    .ResolvedCandidate)
            {
                incompleteBoundary ??= subject;
                continue;
            }
            if (!provided.Remove(
                    subject.EdgeIndex,
                    out PackageDependencyEdgeRealizationExecution?
                        execution))
            {
                throw new ArgumentException(
                    "Every admitted resolved-candidate edge requires one exact prepared execution.",
                    nameof(request));
            }

            routes.Add(
                new(
                    subject,
                    execution,
                    execution.DelegatesToPlatform
                        ? PackageAssemblyReferenceRouteDisposition
                            .PlatformDelegated
                        : PackageAssemblyReferenceRouteDisposition
                            .PackageCandidate));
        }
        if (provided.Count != 0)
        {
            throw new ArgumentException(
                "Edge executions may name only admitted resolved-candidate edges for the requested root.",
                nameof(request));
        }

        ImmutableArray<PackageAssemblyReferenceRouteOccurrence>
            projectedRoutes = routes.ToImmutable();
        PackageDependencyTraversalRootResult root =
            traversal.Roots[request.RootOccurrenceIndex];
        if (root.Completion
            != PackageDependencyTraversalRootCompletion.Complete)
        {
            return new PackageAssemblyReferenceRouteProjectionOutcome
                .Incomplete(
                    request.Generation,
                    request.FocalScope,
                    traversal,
                    request.RootOccurrenceIndex,
                    projectedRoutes,
                    PackageAssemblyReferenceRouteProjectionIncompleteReason
                        .TraversalIncomplete,
                    boundary: null);
        }
        if (incompleteBoundary is not null)
        {
            return new PackageAssemblyReferenceRouteProjectionOutcome
                .Incomplete(
                    request.Generation,
                    request.FocalScope,
                    traversal,
                    request.RootOccurrenceIndex,
                    projectedRoutes,
                    PackageAssemblyReferenceRouteProjectionIncompleteReason
                        .AdmittedEdgeWithoutCandidate,
                    incompleteBoundary);
        }

        return new PackageAssemblyReferenceRouteProjectionOutcome
            .Completed(
                new(
                    request.Generation,
                    request.FocalScope,
                    traversal,
                    request.RootOccurrenceIndex,
                    projectedRoutes));
    }
}
