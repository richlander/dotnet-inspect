using System.Collections.Immutable;
using DotnetInspector.Queries;
using InertText;
using NuGetFetch;

namespace DotnetInspector.Sections;

/// <summary>Projects the owner-issued package traversal into one shared graph.</summary>
public static class PackageTraversalGraphProjection
{
    public static DependencyGraphDocument Project(
        PackageDependencyTraversalOutcome traversal,
        IReadOnlyList<int> rootOccurrenceIndexes)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        ArgumentNullException.ThrowIfNull(rootOccurrenceIndexes);
        if (rootOccurrenceIndexes.Count != traversal.Roots.Length)
        {
            throw new ArgumentException(
                "Package traversal roots require one document occurrence each.",
                nameof(rootOccurrenceIndexes));
        }

        var nodes = new List<DependencyGraphNode>();
        var nodeIds = new Dictionary<DependencyGraphNodeIdentity, int>();
        int AddNode(
            DependencyGraphNodeIdentity identity,
            InertString label)
        {
            if (nodeIds.TryGetValue(identity, out int existing))
                return existing;
            int id = nodes.Count;
            nodeIds.Add(identity, id);
            nodes.Add(new DependencyGraphNode(id, identity, label));
            return id;
        }

        int[] packageNodeIds = new int[traversal.Nodes.Length];
        for (int index = 0; index < traversal.Nodes.Length; index++)
        {
            PackageSourceCoordinate coordinate =
                traversal.Nodes[index].Coordinate;
            packageNodeIds[index] = AddNode(
                new DependencyGraphNodeIdentity.Package(
                    coordinate.PackageId,
                    coordinate.Version),
                PackageLabel(coordinate.PackageId, coordinate.Version));
        }

        int[] boundaryNodeIds =
            new int[traversal.DeclarationBoundaries.Length];
        for (int index = 0;
             index < traversal.DeclarationBoundaries.Length;
             index++)
        {
            PackageDependencyTraversalDeclarationBoundaryNode boundary =
                traversal.DeclarationBoundaries[index];
            boundaryNodeIds[index] = AddNode(
                new DependencyGraphNodeIdentity.PackageBoundary(
                    boundary.SourceProjectionIndex,
                    boundary.DeclarationIdentity,
                    boundary.CanonicalPackageId,
                    boundary.CanonicalVersionConstraint),
                BoundaryLabel(
                    boundary.CanonicalPackageId,
                    boundary.CanonicalVersionConstraint,
                    "unresolved"));
        }

        int[] failedNodeIds = new int[traversal.FailedResolutions.Length];
        for (int index = 0;
             index < traversal.FailedResolutions.Length;
             index++)
        {
            PackageDependencyTraversalFailedResolutionNode failed =
                traversal.FailedResolutions[index];
            failedNodeIds[index] = AddNode(
                new DependencyGraphNodeIdentity.PackageFailure(
                    failed.SourceProjectionIndex,
                    failed.DeclarationIdentity,
                    failed.CanonicalPackageId,
                    failed.CanonicalVersionConstraint),
                BoundaryLabel(
                    failed.CanonicalPackageId,
                    failed.CanonicalVersionConstraint,
                    "resolution failed"));
        }

        int[] budgetNodeIds =
            new int[traversal.WorkBudgetDeclarations.Length];
        for (int index = 0;
             index < traversal.WorkBudgetDeclarations.Length;
             index++)
        {
            PackageDependencyTraversalWorkBudgetNode budget =
                traversal.WorkBudgetDeclarations[index];
            budgetNodeIds[index] = AddNode(
                new DependencyGraphNodeIdentity.PackageBudget(
                    budget.SourceProjectionIndex,
                    budget.DeclarationIdentity,
                    budget.CanonicalPackageId,
                    budget.CanonicalVersionConstraint),
                BoundaryLabel(
                    budget.CanonicalPackageId,
                    budget.CanonicalVersionConstraint,
                    "budget"));
        }

        var roots =
            ImmutableArray.CreateBuilder<DependencyGraphRootOccurrence>();
        foreach (PackageDependencyTraversalRootResult root in traversal.Roots)
        {
            roots.Add(
                new DependencyGraphRootOccurrence(
                    rootOccurrenceIndexes[root.OccurrenceIndex],
                    packageNodeIds[root.NodeIndex]));
        }

        var edges = ImmutableArray.CreateBuilder<DependencyGraphEdge>();
        for (int edgeIndex = 0;
             edgeIndex < traversal.Edges.Length;
             edgeIndex++)
        {
            PackageDependencyTraversalEdge edge = traversal.Edges[edgeIndex];
            int sourceNodeId =
                packageNodeIds[
                    traversal.Projections[edge.SourceProjectionIndex]
                        .NodeIndex];
            (int targetNodeId, DependencyGraphResolutionState resolution) =
                edge.Target switch
                {
                    PackageDependencyTraversalEdgeTarget.Node target =>
                        (packageNodeIds[target.NodeIndex],
                            DependencyGraphResolutionState.Resolved),
                    PackageDependencyTraversalEdgeTarget.DeclarationBoundary
                        target =>
                        (boundaryNodeIds[target.BoundaryNodeIndex],
                            DependencyGraphResolutionState.Declared),
                    PackageDependencyTraversalEdgeTarget.FailedResolution
                        target =>
                        (failedNodeIds[target.NodeIndex],
                            DependencyGraphResolutionState.Unavailable),
                    PackageDependencyTraversalEdgeTarget.WorkBudget target =>
                        (budgetNodeIds[target.NodeIndex],
                            DependencyGraphResolutionState.Unavailable),
                    _ => throw new InvalidOperationException(
                        "Unknown package traversal edge target."),
                };
            var admittedRoots = ImmutableArray.CreateBuilder<int>();
            int minimumDepth = int.MaxValue;
            for (int rootIndex = 0;
                 rootIndex < traversal.RootReachability.Length;
                 rootIndex++)
            {
                if (!traversal.RootReachability[rootIndex].IsEdgeAdmitted(
                        edgeIndex,
                        out int distance))
                {
                    continue;
                }

                admittedRoots.Add(rootOccurrenceIndexes[rootIndex]);
                minimumDepth = Math.Min(minimumDepth, distance);
            }

            edges.Add(
                new DependencyGraphEdge(
                    edges.Count,
                    sourceNodeId,
                    targetNodeId,
                    "package-dependency",
                    admittedRoots.ToImmutable(),
                    minimumDepth == int.MaxValue ? 0 : minimumDepth,
                    resolution,
                    new DependencyGraphEvidenceIdentity.PackageDeclaration(
                        edge.SourceProjectionIndex,
                        edge.Declaration.Identity,
                        edge.Declaration.SourceVersionConstraintSpelling),
                    edge.SourceProjectionIndex,
                    edge.Target
                        is PackageDependencyTraversalEdgeTarget.Node
                            targetProjection
                            ? targetProjection.ProjectionIndex
                            : null,
                    edge.Authority,
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        edge.Diagnostics))
                {
                    RuntimePackageDiagnostics = edge.Diagnostics,
                });
        }

        ImmutableArray<DependencyGraphPackageProjection> projections =
        [
            .. traversal.Projections.Select((projection, index) =>
                new DependencyGraphPackageProjection(
                    index,
                    packageNodeIds[projection.NodeIndex],
                    projection.Kind,
                    projection.Expansion,
                    projection.Evidence,
                    projection.Candidate is { } candidate
                        ? DependencyInspectionPackageCandidate.Create(candidate)
                        : null,
                    projection.RootOccurrenceIndex is { } rootIndex
                        ? rootOccurrenceIndexes[rootIndex]
                        : null,
                    DependencyInspectionPackageAuthorityFailure.CreateRange(
                        projection.Diagnostics))
                {
                    RuntimeCandidate = projection.Candidate,
                    RuntimeDiagnostics = projection.Diagnostics,
                }),
        ];
        ImmutableArray<DependencyGraphDepthBoundary> depthBoundaries =
        [
            .. traversal.DepthBoundaries.Select(boundary =>
                new DependencyGraphDepthBoundary(
                    packageNodeIds[boundary.NodeIndex],
                    boundary.ProjectionIndex,
                    boundary.MaximumDepth,
                    [
                        .. boundary.AffectedRootOccurrences.Select(
                            rootIndex =>
                                rootOccurrenceIndexes[rootIndex]),
                    ],
                    DependencyGraphDepthBoundaryProducerKind.Package)),
        ];
        return new DependencyGraphDocument(
            roots.ToImmutable(),
            [.. nodes],
            edges.ToImmutable(),
            projections,
            depthBoundaries);
    }

    private static InertString PackageLabel(string packageId, string version) =>
        InertString.Format(
            TextPolicy.Field,
            $"{new InertString(TextPolicy.Field, packageId)} "
                + $"{new InertString(TextPolicy.Field, version)}");

    private static InertString BoundaryLabel(
        string packageId, string constraint, string state) =>
        InertString.Format(
            TextPolicy.Field,
            $"{new InertString(TextPolicy.Field, packageId)} "
                + $"{new InertString(TextPolicy.Field, constraint)} ({state})");
}
