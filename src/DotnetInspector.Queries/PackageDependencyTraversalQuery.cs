using System.Collections.Immutable;
using DotnetInspector.Packages;
using NuGetFetch;

namespace DotnetInspector.Queries;

/// <summary>
/// Starting from ordered exact package roots and their selected normalized
/// declarations, follows source-authorized exact package candidates and returns one
/// immutable, depth-bounded directed graph with root-relative reachability, typed
/// failures, and completion.
/// </summary>
/// <remarks>
/// <para>
/// This query owns exact package-node and declaration-edge composition, source-
/// relative manifest projection identity, per-root distance and edge-admission
/// relations, cycle/revisit/shared-node behavior, traversal depth and finite
/// work-budget behavior, deterministic scheduling, and traversal-local failure and
/// completion. It composes but does not own #5765 candidate resolution, package
/// source authorization and exact payload authority, or manifest fact/dependency-group
/// projection; those remain owned by <c>PackageDependencyCandidateQuery</c>
/// (<c>DotnetInspector.PackageQueries</c>), the Package Source Model, and
/// <see cref="PackageManifestFactsQuery"/>/<see cref="PackageDependencyGroupsQuery"/>
/// respectively.
/// </para>
/// <para>
/// The implementation processes one distance-major, root-order, projection-order,
/// declaration-order schedule sequentially: every candidate-resolution and manifest-
/// acquisition call is awaited in its scheduled slot before the next slot begins. This
/// makes <c>Traversal_SourceCompletionOrderDoesNotAffectResult</c> true by
/// construction rather than by reconciling concurrent completions back onto a fixed
/// schedule.
/// </para>
/// </remarks>
public static class PackageDependencyTraversalQuery
{
    public static InspectionQuery<PackageDependencyTraversalOutcome> Definition
        { get; } = new(
            "Package dependency traversal",
            InspectionCost.Unbounded);

    public static async Task<PackageDependencyTraversalOutcome> ExecuteAsync(
        PackageDependencyTraversalRequest request,
        CancellationToken cancellationToken = default,
        NuGetOperationContext? operationContext = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        using NuGetOperationContext? ownedOperation = operationContext is null
            ? new NuGetOperationContext(cancellationToken)
            : null;
        NuGetOperationContext operation = operationContext ?? ownedOperation!;
        CancellationToken token = ResolveInvocationToken(
            operation,
            cancellationToken);
        token.ThrowIfCancellationRequested();

        var engine = new Engine(request, operation, token);
        return await engine.RunAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Projects <see cref="PackageSourceManifest"/> bytes through
    /// <see cref="PackageManifestFactsQuery"/> and
    /// <see cref="PackageDependencyEvidenceQuery"/> under the typed traversal
    /// framework mode. This is the Queries-owned manifest-bytes adapter the design
    /// requires: it never acquires network evidence itself.
    /// </summary>
    internal static (PackageDependencyEvidenceRoot? Root, PackageManifestFailure? Failure)
        ProjectManifestBytes(
            PackageSourceManifest manifest,
            PackageSourceCoordinate expectedCoordinate,
            string? requestedFramework)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(expectedCoordinate);
        byte[] manifestBytes = manifest.Content.ToArray();
        PackageManifestFactsResult factsResult = PackageManifestFactsQuery.Execute(
            manifestBytes,
            expectedCoordinate);
        if (factsResult is PackageManifestFactsResult.Failed failed)
            return (null, failed.Failure);

        PackageManifestFacts facts =
            ((PackageManifestFactsResult.Available)factsResult).Value;
        PackageDependencyEvidenceInput.Package input =
            PackageDependencyEvidenceQuery.CreatePackageInput(
                facts,
                PackageDependencyEvidenceSourceKind.PackageSourceManifest,
                requestedFramework,
                sourceLabel: null,
                source: manifest.Source);
        PackageDependencyEvidenceOutcome outcome =
            PackageDependencyEvidenceQuery.Execute(
                new PackageDependencyEvidenceRequest([input]));
        return (outcome.Roots[0], null);
    }

    private static CancellationToken ResolveInvocationToken(
        NuGetOperationContext operationContext,
        CancellationToken invocationToken)
    {
        if (invocationToken != default
            && invocationToken != operationContext.CancellationToken)
        {
            throw new ArgumentException(
                "The invocation token must match the operation context's caller token.",
                nameof(invocationToken));
        }

        return operationContext.CancellationToken;
    }

    /// <summary>One candidate-acquired or root-supplied manifest projection under construction.</summary>
    private sealed class MutableProjection
    {
        public required int NodeIndex { get; init; }

        public required PackageDependencyTraversalProjectionKind Kind { get; init; }

        public PackageDependencyTraversalProjectionExpansion Expansion { get; set; } =
            PackageDependencyTraversalProjectionExpansion.NotExpanded;

        public PackageDependencyEvidenceRoot? Evidence { get; set; }

        public PackageAcquisitionCandidate? Candidate { get; init; }

        public int? RootOccurrenceIndex { get; init; }

        public List<int> OutgoingEdgeIndexes { get; } = [];
    }

    private sealed class BoundaryNodeBuilder(
        int sourceProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity declarationIdentity,
        string canonicalPackageId,
        string canonicalVersionConstraint,
        int edgeIndex)
    {
        public int SourceProjectionIndex { get; } = sourceProjectionIndex;
        public PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity { get; } =
            declarationIdentity;
        public string CanonicalPackageId { get; } = canonicalPackageId;
        public string CanonicalVersionConstraint { get; } = canonicalVersionConstraint;
        public int EdgeIndex { get; } = edgeIndex;
    }

    private sealed class FailedResolutionNodeBuilder(
        int sourceProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity declarationIdentity,
        string canonicalPackageId,
        string canonicalVersionConstraint,
        PackageDependencyTraversalCandidateResult outcome,
        int edgeIndex)
    {
        public int SourceProjectionIndex { get; } = sourceProjectionIndex;
        public PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity { get; } =
            declarationIdentity;
        public string CanonicalPackageId { get; } = canonicalPackageId;
        public string CanonicalVersionConstraint { get; } = canonicalVersionConstraint;
        public PackageDependencyTraversalCandidateResult Outcome { get; } = outcome;
        public int EdgeIndex { get; } = edgeIndex;
    }

    private sealed class WorkBudgetNodeBuilder(
        int sourceProjectionIndex,
        PackageDependencyEvidenceDeclarationIdentity declarationIdentity,
        string canonicalPackageId,
        string canonicalVersionConstraint,
        int limit,
        int edgeIndex)
    {
        public int SourceProjectionIndex { get; } = sourceProjectionIndex;
        public PackageDependencyEvidenceDeclarationIdentity DeclarationIdentity { get; } =
            declarationIdentity;
        public string CanonicalPackageId { get; } = canonicalPackageId;
        public string CanonicalVersionConstraint { get; } = canonicalVersionConstraint;
        public int Limit { get; } = limit;
        public int EdgeIndex { get; } = edgeIndex;
    }

    private sealed record RawFailure(
        int NodeIndex,
        int ProjectionIndex,
        PackageDependencyTraversalManifestFailureDetail Detail);

    /// <summary>
    /// The sequential, deterministic distance-major traversal engine for one request.
    /// </summary>
    private sealed class Engine(
        PackageDependencyTraversalRequest request,
        NuGetOperationContext operation,
        CancellationToken token)
    {
        private readonly List<PackageSourceCoordinate> _nodeCoordinates = [];
        private readonly Dictionary<PackageSourceCoordinate, int>
            _nodeIndexByCoordinate = [];
        private readonly List<List<int>> _nodeProjections = [];

        private readonly List<MutableProjection> _projections = [];
        private readonly Dictionary<PackageAcquisitionCandidateCorrespondence, int>
            _projectionIndexByCorrespondence = [];

        private readonly List<PackageDependencyTraversalEdge> _edges = [];

        private readonly List<BoundaryNodeBuilder> _boundaryNodes = [];
        private readonly List<FailedResolutionNodeBuilder> _failedResolutionNodes = [];
        private readonly List<WorkBudgetNodeBuilder> _workBudgetNodes = [];
        private readonly List<RawFailure> _failures = [];

        private readonly Dictionary<int, List<int>> _projectionCaringRoots = [];
        private readonly Dictionary<int, List<int>> _edgeAdmittingRoots = [];
        private readonly Dictionary<int, List<int>> _depthBoundaryRoots = [];
        private readonly HashSet<int> _manifestBudgetFailureProjections = [];

        private int _manifestProjectionsUsed;
        private int _declarationResolutionsUsed;

        private Dictionary<int, int>[] _rootNodeDistance = [];
        private Dictionary<int, int>[] _rootProjectionDistance = [];
        private Dictionary<int, int>[] _rootEdgeDistance = [];
        private bool[] _rootDepthBounded = [];
        private int[] _rootOwnNodeIndex = [];
        private int[] _rootOwnProjectionIndex = [];

        public async Task<PackageDependencyTraversalOutcome> RunAsync()
        {
            int rootCount = request.Roots.Length;
            _rootNodeDistance = new Dictionary<int, int>[rootCount];
            _rootProjectionDistance = new Dictionary<int, int>[rootCount];
            _rootEdgeDistance = new Dictionary<int, int>[rootCount];
            _rootDepthBounded = new bool[rootCount];
            _rootOwnNodeIndex = new int[rootCount];
            _rootOwnProjectionIndex = new int[rootCount];

            var currentLevel = new List<(int RootIndex, int ProjectionIndex)>();
            for (int r = 0; r < rootCount; r++)
            {
                _rootNodeDistance[r] = [];
                _rootProjectionDistance[r] = [];
                _rootEdgeDistance[r] = [];

                PackageDependencyTraversalRootOccurrence occurrence =
                    request.Roots[r];
                int nodeIndex = GetOrCreateNode(occurrence.Coordinate);
                int projectionIndex = CreateRootProjection(occurrence, r, nodeIndex);
                _rootOwnNodeIndex[r] = nodeIndex;
                _rootOwnProjectionIndex[r] = projectionIndex;
                _rootNodeDistance[r][nodeIndex] = 0;
                _rootProjectionDistance[r][projectionIndex] = 0;
                currentLevel.Add((r, projectionIndex));
            }

            int distance = 0;
            while (currentLevel.Count > 0)
            {
                var nextLevel = new List<(int RootIndex, int ProjectionIndex)>();
                foreach ((int rootIndex, int projectionIndex) in currentLevel)
                {
                    token.ThrowIfCancellationRequested();
                    await VisitAsync(
                        rootIndex,
                        projectionIndex,
                        distance,
                        nextLevel).ConfigureAwait(false);
                }

                currentLevel = nextLevel;
                distance++;
            }

            return BuildOutcome();
        }

        private async Task VisitAsync(
            int rootIndex,
            int projectionIndex,
            int distance,
            List<(int RootIndex, int ProjectionIndex)> nextLevel)
        {
            bool canExpand = request.MaxDepth is null
                || distance < request.MaxDepth.Value;
            if (!canExpand)
            {
                MutableProjection depthBoundedProjection = _projections[projectionIndex];
                bool hasKnownFrontier = distance > 0
                    || GetSelectedDeclarations(
                        depthBoundedProjection.Evidence!,
                        out _).Length > 0;
                if (hasKnownFrontier)
                {
                    _rootDepthBounded[rootIndex] = true;
                    AddCaring(_depthBoundaryRoots, projectionIndex, rootIndex);
                }
                return;
            }

            AddCaring(_projectionCaringRoots, projectionIndex, rootIndex);
            MutableProjection projection = _projections[projectionIndex];
            if (projection.Expansion
                == PackageDependencyTraversalProjectionExpansion.NotExpanded)
            {
                await EnsureExpandedAsync(projection, projectionIndex)
                    .ConfigureAwait(false);
            }

            foreach (int edgeIndex in projection.OutgoingEdgeIndexes)
            {
                token.ThrowIfCancellationRequested();
                PackageDependencyTraversalEdge edge = _edges[edgeIndex];
                if (!IsEdgeAdmitted(rootIndex, edge))
                    continue;

                int targetDistance = distance + 1;
                AddCaring(_edgeAdmittingRoots, edgeIndex, rootIndex);
                _rootEdgeDistance[rootIndex][edgeIndex] = targetDistance;
                if (edge.Target is not PackageDependencyTraversalEdgeTarget.Node node)
                    continue;

                if (!_rootNodeDistance[rootIndex].ContainsKey(node.NodeIndex))
                    _rootNodeDistance[rootIndex][node.NodeIndex] = targetDistance;

                if (!_rootProjectionDistance[rootIndex]
                        .ContainsKey(node.ProjectionIndex))
                {
                    _rootProjectionDistance[rootIndex][node.ProjectionIndex] =
                        targetDistance;
                    nextLevel.Add((rootIndex, node.ProjectionIndex));
                }
            }
        }

        private async Task EnsureExpandedAsync(
            MutableProjection projection,
            int projectionIndex)
        {
            if (projection.Evidence is null)
            {
                if (_manifestProjectionsUsed
                    >= request.WorkBudget.MaxManifestProjections)
                {
                    if (_manifestBudgetFailureProjections.Add(projectionIndex))
                    {
                        RecordFailure(
                            projection.NodeIndex,
                            projectionIndex,
                            new PackageDependencyTraversalManifestFailureDetail
                                .ManifestProjectionBudgetExhausted(
                                    request.WorkBudget.MaxManifestProjections));
                    }
                    return;
                }

                _manifestProjectionsUsed++;
                await AcquireManifestAsync(projection, projectionIndex)
                    .ConfigureAwait(false);
            }

            if (projection.Evidence is not null
                && projection.Expansion
                    == PackageDependencyTraversalProjectionExpansion.NotExpanded)
            {
                await ExpandDeclarationsAsync(projection, projectionIndex)
                    .ConfigureAwait(false);
                projection.Expansion =
                    PackageDependencyTraversalProjectionExpansion.Expanded;
            }
        }

        private async Task AcquireManifestAsync(
            MutableProjection projection,
            int projectionIndex)
        {
            PackageDependencyTraversalManifestResult manifestResult =
                await request.ManifestAcquirer.AcquireAsync(
                    projection.Candidate!,
                    token,
                    operation).ConfigureAwait(false);
            switch (manifestResult)
            {
                case PackageDependencyTraversalManifestResult.Acquired acquired:
                    if (!projection.Candidate!.Authorities.Any(
                            evidence => ReferenceEquals(
                                evidence.Authority.Association,
                                acquired.Manifest.Source.Association)))
                    {
                        RecordFailure(
                            projection.NodeIndex,
                            projectionIndex,
                            new PackageDependencyTraversalManifestFailureDetail
                                .Identity(
                                    new PackageManifestFailure(
                                        PackageManifestFailureReason
                                            .InvalidIdentityContract)));
                        projection.Expansion =
                            PackageDependencyTraversalProjectionExpansion.Expanded;
                        return;
                    }

                    (PackageDependencyEvidenceRoot? projectedRoot,
                        PackageManifestFailure? manifestFailure) =
                        ProjectManifestBytes(
                            acquired.Manifest,
                            projection.Candidate!.Coordinate,
                            request.FrameworkMode.RequestedFramework);
                    if (manifestFailure is not null)
                    {
                        RecordFailure(
                            projection.NodeIndex,
                            projectionIndex,
                            new PackageDependencyTraversalManifestFailureDetail
                                .Identity(manifestFailure));
                        projection.Expansion =
                            PackageDependencyTraversalProjectionExpansion.Expanded;
                        return;
                    }

                    projection.Evidence = projectedRoot;
                    return;
                case PackageDependencyTraversalManifestResult.Failed failedResult:
                    RecordFailure(
                        projection.NodeIndex,
                        projectionIndex,
                        new PackageDependencyTraversalManifestFailureDetail
                            .Acquisition(failedResult.Failures));
                    projection.Expansion =
                        PackageDependencyTraversalProjectionExpansion.Expanded;
                    return;
                case PackageDependencyTraversalManifestResult.Incomplete incomplete:
                    RecordFailure(
                        projection.NodeIndex,
                        projectionIndex,
                        new PackageDependencyTraversalManifestFailureDetail
                            .IncompleteAcquisition(incomplete.Failures));
                    projection.Expansion =
                        PackageDependencyTraversalProjectionExpansion.Expanded;
                    return;
                default:
                    throw new InvalidOperationException(
                        "Unknown package dependency traversal manifest result.");
            }
        }

        private async Task ExpandDeclarationsAsync(
            MutableProjection projection,
            int projectionIndex)
        {
            ImmutableArray<PackageDependencyEvidenceDeclaration> declarations =
                GetSelectedDeclarations(
                    projection.Evidence!,
                    out ImmutableArray<
                        PackageDependencyEvidenceDeclarationFailure>
                        selectedGroupFailures);
            foreach (PackageDependencyEvidenceDeclarationFailure failure in
                     selectedGroupFailures)
            {
                RecordFailure(
                    projection.NodeIndex,
                    projectionIndex,
                    new PackageDependencyTraversalManifestFailureDetail.Declaration(
                        failure));
            }

            PackageDependencyTraversalExpansionAuthority expansionKind =
                projection.RootOccurrenceIndex is int occurrenceIndex
                    ? request.Roots[occurrenceIndex].Authority
                    : PackageDependencyTraversalExpansionAuthority.RecursiveSources;
            PackageSourceCoordinate sourceCoordinate =
                _nodeCoordinates[projection.NodeIndex];
            foreach (PackageDependencyEvidenceDeclaration declaration in declarations)
            {
                token.ThrowIfCancellationRequested();
                if (expansionKind
                    == PackageDependencyTraversalExpansionAuthority
                        .DirectDeclarationsOnly)
                {
                    AddDirectBoundaryEdge(
                        projection,
                        projectionIndex,
                        sourceCoordinate,
                        declaration);
                    continue;
                }

                if (_declarationResolutionsUsed
                    >= request.WorkBudget.MaxDeclarationResolutions)
                {
                    AddWorkBudgetEdge(
                        projection,
                        projectionIndex,
                        sourceCoordinate,
                        declaration);
                    continue;
                }

                _declarationResolutionsUsed++;
                PackageDependencyTraversalCandidateResult result =
                    await request.CandidateResolver.ResolveAsync(
                        declaration,
                        token,
                        operation).ConfigureAwait(false);
                AddResolutionEdge(
                    projection,
                    projectionIndex,
                    sourceCoordinate,
                    declaration,
                    result);
            }
        }

        private void AddDirectBoundaryEdge(
            MutableProjection projection,
            int projectionIndex,
            PackageSourceCoordinate sourceCoordinate,
            PackageDependencyEvidenceDeclaration declaration)
        {
            int edgeIndex = _edges.Count;
            int boundaryIndex = _boundaryNodes.Count;
            _boundaryNodes.Add(new BoundaryNodeBuilder(
                projectionIndex,
                declaration.Identity,
                declaration.CanonicalPackageId,
                declaration.CanonicalVersionConstraint,
                edgeIndex));
            _edges.Add(new PackageDependencyTraversalEdge(
                projectionIndex,
                sourceCoordinate,
                declaration,
                new PackageDependencyTraversalEdgeTarget.DeclarationBoundary(
                    boundaryIndex),
                PackageDependencyTraversalEdgeEmissionAuthority.DirectBoundary,
                []));
            projection.OutgoingEdgeIndexes.Add(edgeIndex);
        }

        private void AddWorkBudgetEdge(
            MutableProjection projection,
            int projectionIndex,
            PackageSourceCoordinate sourceCoordinate,
            PackageDependencyEvidenceDeclaration declaration)
        {
            int edgeIndex = _edges.Count;
            int budgetIndex = _workBudgetNodes.Count;
            _workBudgetNodes.Add(new WorkBudgetNodeBuilder(
                projectionIndex,
                declaration.Identity,
                declaration.CanonicalPackageId,
                declaration.CanonicalVersionConstraint,
                request.WorkBudget.MaxDeclarationResolutions,
                edgeIndex));
            _edges.Add(new PackageDependencyTraversalEdge(
                projectionIndex,
                sourceCoordinate,
                declaration,
                new PackageDependencyTraversalEdgeTarget.WorkBudget(budgetIndex),
                PackageDependencyTraversalEdgeEmissionAuthority.WorkBudgetBoundary,
                []));
            projection.OutgoingEdgeIndexes.Add(edgeIndex);
        }

        private void AddResolutionEdge(
            MutableProjection projection,
            int projectionIndex,
            PackageSourceCoordinate sourceCoordinate,
            PackageDependencyEvidenceDeclaration declaration,
            PackageDependencyTraversalCandidateResult result)
        {
            int edgeIndex = _edges.Count;
            if (result is PackageDependencyTraversalCandidateResult.Resolved resolved)
            {
                int targetProjectionIndex = GetOrCreateCandidateProjection(
                    resolved.Candidate);
                int targetNodeIndex = _projections[targetProjectionIndex].NodeIndex;
                _edges.Add(new PackageDependencyTraversalEdge(
                    projectionIndex,
                    sourceCoordinate,
                    declaration,
                    new PackageDependencyTraversalEdgeTarget.Node(
                        targetNodeIndex,
                        targetProjectionIndex),
                    PackageDependencyTraversalEdgeEmissionAuthority.ResolvedCandidate,
                    resolved.Diagnostics));
                projection.OutgoingEdgeIndexes.Add(edgeIndex);
                return;
            }

            int failedIndex = _failedResolutionNodes.Count;
            _failedResolutionNodes.Add(new FailedResolutionNodeBuilder(
                projectionIndex,
                declaration.Identity,
                declaration.CanonicalPackageId,
                declaration.CanonicalVersionConstraint,
                result,
                edgeIndex));
            _edges.Add(new PackageDependencyTraversalEdge(
                projectionIndex,
                sourceCoordinate,
                declaration,
                new PackageDependencyTraversalEdgeTarget.FailedResolution(
                    failedIndex),
                PackageDependencyTraversalEdgeEmissionAuthority.FailedResolution,
                []));
            projection.OutgoingEdgeIndexes.Add(edgeIndex);
        }

        private int GetOrCreateNode(PackageSourceCoordinate coordinate)
        {
            if (_nodeIndexByCoordinate.TryGetValue(coordinate, out int existing))
                return existing;

            int index = _nodeCoordinates.Count;
            _nodeCoordinates.Add(coordinate);
            _nodeProjections.Add([]);
            _nodeIndexByCoordinate[coordinate] = index;
            return index;
        }

        private int CreateRootProjection(
            PackageDependencyTraversalRootOccurrence occurrence,
            int rootIndex,
            int nodeIndex)
        {
            int index = _projections.Count;
            _projections.Add(new MutableProjection
            {
                NodeIndex = nodeIndex,
                Kind = PackageDependencyTraversalProjectionKind.RootSupplied,
                Evidence = occurrence.Root,
                Candidate = null,
                RootOccurrenceIndex = rootIndex,
            });
            _nodeProjections[nodeIndex].Add(index);
            return index;
        }

        private int GetOrCreateCandidateProjection(
            PackageAcquisitionCandidate candidate)
        {
            if (_projectionIndexByCorrespondence.TryGetValue(
                    candidate.Correspondence,
                    out int existing))
            {
                return existing;
            }

            int nodeIndex = GetOrCreateNode(candidate.Coordinate);
            int index = _projections.Count;
            _projections.Add(new MutableProjection
            {
                NodeIndex = nodeIndex,
                Kind = PackageDependencyTraversalProjectionKind.CandidateAcquired,
                Evidence = null,
                Candidate = candidate,
                RootOccurrenceIndex = null,
            });
            _nodeProjections[nodeIndex].Add(index);
            _projectionIndexByCorrespondence[candidate.Correspondence] = index;
            return index;
        }

        private void RecordFailure(
            int nodeIndex,
            int projectionIndex,
            PackageDependencyTraversalManifestFailureDetail detail) =>
            _failures.Add(new RawFailure(nodeIndex, projectionIndex, detail));

        private static void AddCaring(
            Dictionary<int, List<int>> map,
            int key,
            int rootIndex)
        {
            if (!map.TryGetValue(key, out List<int>? list))
            {
                list = [];
                map[key] = list;
            }

            if (!list.Contains(rootIndex))
                list.Add(rootIndex);
        }

        private static ImmutableArray<PackageDependencyEvidenceDeclaration>
            GetSelectedDeclarations(
                PackageDependencyEvidenceRoot root,
                out ImmutableArray<PackageDependencyEvidenceDeclarationFailure>
                    selectedGroupFailures)
        {
            selectedGroupFailures = [];
            var available =
                (PackageDependencyEvidenceDeclarationResult.Available)
                    root.Declaration;
            if (root.Selection.SelectedGroup is not { } selectedGroup)
                return [];

            foreach (PackageDependencyEvidenceGroup group in available.Groups)
            {
                if (!group.Identity.Equals(selectedGroup))
                    continue;

                selectedGroupFailures =
                [
                    .. available.Failures.Where(
                        failure => GroupOf(failure) is { } failureGroup
                            && failureGroup.Equals(selectedGroup)),
                ];

                return group.Declarations;
            }

            return [];
        }

        private bool IsEdgeAdmitted(
            int rootIndex,
            PackageDependencyTraversalEdge edge)
        {
            PackageDependencyTraversalExpansionAuthority rootAuthority =
                request.Roots[rootIndex].Authority;
            if (rootAuthority
                == PackageDependencyTraversalExpansionAuthority.RecursiveSources)
            {
                return edge.Authority
                    != PackageDependencyTraversalEdgeEmissionAuthority.DirectBoundary;
            }

            return edge.Authority
                    == PackageDependencyTraversalEdgeEmissionAuthority.DirectBoundary
                && _projections[edge.SourceProjectionIndex].RootOccurrenceIndex
                    == rootIndex;
        }

        private static PackageDependencyEvidenceGroupIdentity? GroupOf(
            PackageDependencyEvidenceDeclarationFailure failure) => failure switch
        {
            PackageDependencyEvidenceDeclarationFailure
                    .ConflictingPackageDeclaration conflicting =>
                conflicting.Group,
            PackageDependencyEvidenceDeclarationFailure
                    .InvalidPackageDeclaration invalid =>
                invalid.Group,
            _ => null,
        };

        private PackageDependencyTraversalOutcome BuildOutcome()
        {
            ImmutableArray<PackageDependencyTraversalNode> nodes =
            [
                .. _nodeCoordinates.Select(
                    (coordinate, index) => new PackageDependencyTraversalNode(
                        coordinate,
                        [.. _nodeProjections[index]])),
            ];
            ImmutableArray<PackageDependencyTraversalProjection> projections =
            [
                .. _projections.Select(
                    projection => new PackageDependencyTraversalProjection(
                        projection.NodeIndex,
                        projection.Kind,
                        projection.Expansion,
                        projection.Evidence,
                        projection.Candidate,
                        projection.RootOccurrenceIndex,
                        [.. projection.OutgoingEdgeIndexes])),
            ];
            ImmutableArray<PackageDependencyTraversalEdge> edges = [.. _edges];
            ImmutableArray<PackageDependencyTraversalDeclarationBoundaryNode>
                boundaries =
                [
                    .. _boundaryNodes.Select(
                        builder => new PackageDependencyTraversalDeclarationBoundaryNode(
                            builder.SourceProjectionIndex,
                            builder.DeclarationIdentity,
                            builder.CanonicalPackageId,
                            builder.CanonicalVersionConstraint,
                            AffectedRoots(_edgeAdmittingRoots, builder.EdgeIndex))),
                ];
            ImmutableArray<PackageDependencyTraversalFailedResolutionNode>
                failedResolutions =
                [
                    .. _failedResolutionNodes.Select(
                        builder => new PackageDependencyTraversalFailedResolutionNode(
                            builder.SourceProjectionIndex,
                            builder.DeclarationIdentity,
                            builder.CanonicalPackageId,
                            builder.CanonicalVersionConstraint,
                            builder.Outcome,
                            AffectedRoots(_edgeAdmittingRoots, builder.EdgeIndex))),
                ];
            ImmutableArray<PackageDependencyTraversalWorkBudgetNode>
                workBudgetDeclarations =
                [
                    .. _workBudgetNodes.Select(
                        builder => new PackageDependencyTraversalWorkBudgetNode(
                            builder.SourceProjectionIndex,
                            builder.DeclarationIdentity,
                            builder.CanonicalPackageId,
                            builder.CanonicalVersionConstraint,
                            PackageDependencyTraversalWorkBudgetKind
                                .DeclarationResolution,
                            builder.Limit,
                            AffectedRoots(_edgeAdmittingRoots, builder.EdgeIndex))),
                ];
            ImmutableArray<PackageDependencyTraversalFailure> failures =
            [
                .. _failures.Select(
                    raw => new PackageDependencyTraversalFailure(
                        raw.NodeIndex,
                        raw.ProjectionIndex,
                        raw.Detail,
                        AffectedRoots(_projectionCaringRoots, raw.ProjectionIndex))),
            ];
            ImmutableArray<PackageDependencyTraversalDepthBoundary>
                depthBoundaries =
                request.MaxDepth is int maximumDepth
                    ?
                    [
                        .. _depthBoundaryRoots
                            .OrderBy(pair => pair.Key)
                            .Select(pair =>
                                new PackageDependencyTraversalDepthBoundary(
                                    _projections[pair.Key].NodeIndex,
                                    pair.Key,
                                    maximumDepth,
                                    [.. pair.Value])),
                    ]
                    : [];

            bool[] hasPartialCause = new bool[request.Roots.Length];

            for (int edgeIndex = 0; edgeIndex < _edges.Count; edgeIndex++)
            {
                PackageDependencyTraversalEdge edge = _edges[edgeIndex];
                if (edge.Authority
                    is not (PackageDependencyTraversalEdgeEmissionAuthority
                            .FailedResolution
                        or PackageDependencyTraversalEdgeEmissionAuthority
                            .WorkBudgetBoundary))
                {
                    continue;
                }

                if (!_edgeAdmittingRoots.TryGetValue(edgeIndex, out List<int>? roots))
                    continue;
                foreach (int rootIndex in roots)
                    hasPartialCause[rootIndex] = true;
            }

            foreach (RawFailure failure in _failures)
            {
                if (!_projectionCaringRoots.TryGetValue(
                        failure.ProjectionIndex,
                        out List<int>? roots))
                {
                    continue;
                }

                foreach (int rootIndex in roots)
                    hasPartialCause[rootIndex] = true;
            }

            var rootResults =
                ImmutableArray.CreateBuilder<PackageDependencyTraversalRootResult>(
                    request.Roots.Length);
            var reachability =
                ImmutableArray.CreateBuilder<PackageDependencyTraversalReachability>(
                    request.Roots.Length);
            int completeRoots = 0;
            int depthBoundedRoots = 0;
            int sourceBoundedRoots = 0;
            int partialRoots = 0;
            for (int r = 0; r < request.Roots.Length; r++)
            {
                PackageDependencyTraversalRootOccurrence occurrence =
                    request.Roots[r];
                bool ownProjectionHasEdges =
                    _projections[_rootOwnProjectionIndex[r]].OutgoingEdgeIndexes
                        .Count > 0;
                PackageDependencyTraversalRootCompletion completion =
                    hasPartialCause[r]
                        ? PackageDependencyTraversalRootCompletion.Partial
                    : occurrence.Authority
                        == PackageDependencyTraversalExpansionAuthority
                            .DirectDeclarationsOnly
                        && ownProjectionHasEdges
                        ? PackageDependencyTraversalRootCompletion.SourceBounded
                    : _rootDepthBounded[r]
                        ? PackageDependencyTraversalRootCompletion.DepthBounded
                        : PackageDependencyTraversalRootCompletion.Complete;
                switch (completion)
                {
                    case PackageDependencyTraversalRootCompletion.Complete:
                        completeRoots++;
                        break;
                    case PackageDependencyTraversalRootCompletion.DepthBounded:
                        depthBoundedRoots++;
                        break;
                    case PackageDependencyTraversalRootCompletion.SourceBounded:
                        sourceBoundedRoots++;
                        break;
                    default:
                        partialRoots++;
                        break;
                }

                rootResults.Add(new PackageDependencyTraversalRootResult(
                    r,
                    occurrence,
                    _rootOwnNodeIndex[r],
                    _rootOwnProjectionIndex[r],
                    completion));
                reachability.Add(new PackageDependencyTraversalReachability(
                    _rootNodeDistance[r].ToImmutableDictionary(),
                    _rootProjectionDistance[r].ToImmutableDictionary(),
                    _rootEdgeDistance[r].ToImmutableDictionary()));
            }

            return new PackageDependencyTraversalOutcome(
                rootResults.MoveToImmutable(),
                reachability.MoveToImmutable(),
                nodes,
                projections,
                edges,
                boundaries,
                failedResolutions,
                workBudgetDeclarations,
                failures,
                depthBoundaries,
                new PackageDependencyTraversalSummary(
                    completeRoots,
                    depthBoundedRoots,
                    sourceBoundedRoots,
                    partialRoots));
        }

        private static ImmutableArray<int> AffectedRoots(
            Dictionary<int, List<int>> map,
            int key) =>
            map.TryGetValue(key, out List<int>? roots) ? [.. roots] : [];
    }
}
