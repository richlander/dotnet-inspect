using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// One root-relative resolved traversal edge selected for destination package
/// realization.
/// </summary>
public sealed class PackageDependencyEdgeRealizationSubject
{
    internal PackageDependencyEdgeRealizationSubject(
        PackageDependencyTraversalOutcome traversal,
        int rootOccurrenceIndex,
        int edgeIndex,
        int distance,
        PackageDependencyTraversalProjection sourceProjection,
        PackageDependencyTraversalProjection targetProjection,
        PackageAcquisitionCandidate candidate)
    {
        Traversal = traversal;
        RootOccurrenceIndex = rootOccurrenceIndex;
        EdgeIndex = edgeIndex;
        Distance = distance;
        SourceProjection = sourceProjection;
        TargetProjection = targetProjection;
        Candidate = candidate;
    }

    public PackageDependencyTraversalOutcome Traversal { get; }

    public int RootOccurrenceIndex { get; }

    public PackageDependencyTraversalRootResult Root =>
        Traversal.Roots[RootOccurrenceIndex];

    public int EdgeIndex { get; }

    public PackageDependencyTraversalEdge Edge =>
        Traversal.Edges[EdgeIndex];

    public int Distance { get; }

    public PackageDependencyTraversalProjection SourceProjection { get; }

    public PackageDependencyTraversalProjection TargetProjection { get; }

    public PackageAcquisitionCandidate Candidate { get; }
}

/// <summary>
/// One exact target-aware request to prepare a resolved traversal edge for
/// PackageHouse realization.
/// </summary>
public sealed record PackageDependencyEdgeRealizationRequest
{
    public PackageDependencyEdgeRealizationRequest(
        PackageDependencyTraversalOutcome traversal,
        int rootOccurrenceIndex,
        int edgeIndex,
        PackageHouseOperation operation,
        PackageHouseTargetContext targetContext,
        PlatformPruneInventory? pruningInventory = null)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(targetContext);
        if (operation.Profile != PackageHouseOperationProfile.Realize)
        {
            throw new ArgumentException(
                "Dependency-edge realization requires a PackageHouse Realize operation.",
                nameof(operation));
        }
        if (targetContext.Mode != PackageHouseTargetSelectionMode.Exact)
        {
            throw new ArgumentException(
                "Dependency-edge realization requires one exact package target.",
                nameof(targetContext));
        }
        if (!string.Equals(
                targetContext.RequestedFramework,
                traversal.TraversalTargetPolicy.TargetFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The destination package target must match the traversal target.",
                nameof(targetContext));
        }
        if ((targetContext.PlatformTarget is null)
            != (pruningInventory is null))
        {
            throw new ArgumentException(
                "Platform pruning requires both an exact platform target and its inventory, or neither.",
                nameof(pruningInventory));
        }

        Traversal = traversal;
        RootOccurrenceIndex = rootOccurrenceIndex;
        EdgeIndex = edgeIndex;
        Operation = operation;
        TargetContext = targetContext;
        PruningInventory = pruningInventory;
    }

    public PackageDependencyTraversalOutcome Traversal { get; }

    public int RootOccurrenceIndex { get; }

    public int EdgeIndex { get; }

    public PackageHouseOperation Operation { get; }

    public PackageHouseTargetContext TargetContext { get; }

    public PlatformPruneInventory? PruningInventory { get; }
}

/// <summary>
/// One exact PackageHouse execution prepared from a root-relative traversal
/// edge occurrence.
/// </summary>
public sealed class PackageDependencyEdgeRealizationExecution
{
    internal PackageDependencyEdgeRealizationExecution(
        PackageDependencyEdgeRealizationSubject subject,
        PackageHouseDependencyInput input,
        PackageHousePruningReceipt? pruning)
    {
        Subject = subject;
        Input = input;
        Pruning = pruning;
    }

    public PackageDependencyEdgeRealizationSubject Subject { get; }

    public PackageHouseDependencyInput Input { get; }

    public PackageHouseRequest Request => Input.Request;

    public PackageHousePruningReceipt? Pruning { get; }

    public bool DelegatesToPlatform =>
        Pruning?.Supply.DelegatesToPlatform is true;

    public bool Accepts(PackageHouseSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        return ReferenceEquals(settlement.Result.Request, Request);
    }

    public async Task<PackageDependencyEdgeRealizationEvidence> ExecuteAsync(
        PackageHouse house,
        PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(sourceOperation);
        PackageHouseSettlement settlement = Pruning is null
            ? await house.ExecuteAsync(
                    Request,
                    sourceOperation)
                .ConfigureAwait(false)
            : await house.ExecuteAsync(
                    Request,
                    sourceOperation,
                    Pruning)
                .ConfigureAwait(false);
        return new PackageDependencyEdgeRealizationEvidence(
            this,
            settlement);
    }

    /// <summary>
    /// Executes one sequential edge step without consuming the caller-owned
    /// Package Source operation.
    /// </summary>
    public async Task<PackageDependencyEdgeRealizationEvidence>
        ExecuteStepAsync(
            PackageHouse house,
            PackageSourceOperationLease sourceOperation)
    {
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(sourceOperation);
        PackageHouseSettlement settlement =
            await house.ExecuteStepAsync(
                    Request,
                    sourceOperation,
                    Pruning)
                .ConfigureAwait(false);
        return new PackageDependencyEdgeRealizationEvidence(
            this,
            settlement);
    }
}

/// <summary>
/// The completed PackageHouse settlement retained with the exact traversal
/// edge occurrence that authorized it.
/// </summary>
public sealed class PackageDependencyEdgeRealizationEvidence
{
    internal PackageDependencyEdgeRealizationEvidence(
        PackageDependencyEdgeRealizationExecution execution,
        PackageHouseSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(settlement);
        if (!execution.Accepts(settlement))
        {
            throw new ArgumentException(
                "The PackageHouse settlement belongs to another dependency-edge execution.",
                nameof(settlement));
        }

        Execution = execution;
        Settlement = settlement;
        RootContribution =
            PackageHouseRootContributionAdapter.Create(settlement);
    }

    public PackageDependencyEdgeRealizationExecution Execution { get; }

    public PackageDependencyEdgeRealizationSubject Subject =>
        Execution.Subject;

    public PackageHouseSettlement Settlement { get; }

    public PackageHouseResult Result => Settlement.Result;

    public PackageHouseRootContributionOutcome RootContribution { get; }

    public PlatformDelegation? PlatformDelegation =>
        (Result as PackageHouseResult.Delegated)?.Delegation;
}

/// <summary>
/// Prepares exact PackageHouse realization from one resolved, root-relative
/// package traversal edge.
/// </summary>
public static class PackageDependencyEdgeRealizationQuery
{
    public static PackageDependencyEdgeRealizationExecution Execute(
        PackageDependencyEdgeRealizationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        PackageDependencyEdgeRealizationSubject subject =
            SelectSubject(request);
        PackageDependencyEvidenceRoot root =
            subject.SourceProjection.Evidence!;
        var resolution = new PackageDependencyCandidateResult.Resolved(
            subject.Edge.Declaration,
            subject.Candidate,
            subject.Edge.Diagnostics);
        PackageHouseDependencyInput input =
            PackageHouseDependencyInputAdapter.Create(
                root,
                resolution,
                request.Operation,
                request.TargetContext,
                PackageHouseAssetSelectionKind.Compile,
                PackageHouseLibraryHandoffMode.PackageOnly);
        PackageHousePruningReceipt? pruning =
            request.PruningInventory is null
                ? null
                : PackageHousePruningReceipt.Evaluate(
                    input.Request,
                    request.PruningInventory);
        return new(
            subject,
            input,
            pruning);
    }

    private static PackageDependencyEdgeRealizationSubject SelectSubject(
        PackageDependencyEdgeRealizationRequest request)
    {
        PackageDependencyTraversalOutcome traversal = request.Traversal;
        ArgumentOutOfRangeException.ThrowIfNegative(
            request.RootOccurrenceIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            request.RootOccurrenceIndex,
            traversal.Roots.Length);
        ArgumentOutOfRangeException.ThrowIfNegative(request.EdgeIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(
            request.EdgeIndex,
            traversal.Edges.Length);

        PackageDependencyTraversalRootResult root =
            traversal.Roots[request.RootOccurrenceIndex];
        if (root.OccurrenceIndex != request.RootOccurrenceIndex
            || traversal.RootReachability.Length
                <= request.RootOccurrenceIndex
            || !traversal.RootReachability[request.RootOccurrenceIndex]
                .IsEdgeAdmitted(request.EdgeIndex, out int distance))
        {
            throw new ArgumentException(
                "The traversal edge is not admitted for the requested root occurrence.",
                nameof(request));
        }

        PackageDependencyTraversalEdge edge =
            traversal.Edges[request.EdgeIndex];
        if (edge.Authority
                != PackageDependencyTraversalEdgeEmissionAuthority
                    .ResolvedCandidate
            || edge.Target
                is not PackageDependencyTraversalEdgeTarget.Node target)
        {
            throw new ArgumentException(
                "Dependency-edge realization requires a resolved-candidate traversal edge.",
                nameof(request));
        }
        if (edge.SourceProjectionIndex < 0
            || edge.SourceProjectionIndex >= traversal.Projections.Length
            || target.ProjectionIndex < 0
            || target.ProjectionIndex >= traversal.Projections.Length
            || target.NodeIndex < 0
            || target.NodeIndex >= traversal.Nodes.Length)
        {
            throw new ArgumentException(
                "The traversal edge references an unavailable projection or node.",
                nameof(request));
        }

        PackageDependencyTraversalProjection sourceProjection =
            traversal.Projections[edge.SourceProjectionIndex];
        PackageDependencyTraversalProjection targetProjection =
            traversal.Projections[target.ProjectionIndex];
        if (sourceProjection.NodeIndex < 0
            || sourceProjection.NodeIndex >= traversal.Nodes.Length)
        {
            throw new ArgumentException(
                "The traversal edge references an unavailable source node.",
                nameof(request));
        }
        PackageAcquisitionCandidate candidate =
            targetProjection.Candidate
            ?? throw new ArgumentException(
                "A resolved-candidate traversal edge must retain its target candidate.",
                nameof(request));
        if (sourceProjection.Evidence is null
            || targetProjection.Kind
                != PackageDependencyTraversalProjectionKind
                    .CandidateAcquired
            || targetProjection.NodeIndex != target.NodeIndex
            || candidate.Coordinate
                != traversal.Nodes[target.NodeIndex].Coordinate
            || edge.SourceCoordinate
                != traversal.Nodes[sourceProjection.NodeIndex].Coordinate)
        {
            throw new ArgumentException(
                "The traversal edge does not retain the exact source and target projection correspondence required for realization.",
                nameof(request));
        }

        return new(
            traversal,
            request.RootOccurrenceIndex,
            request.EdgeIndex,
            distance,
            sourceProjection,
            targetProjection,
            candidate);
    }
}
