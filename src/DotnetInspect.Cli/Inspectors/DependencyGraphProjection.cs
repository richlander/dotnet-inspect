using System.Collections.Immutable;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Services;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using InertText;
using NuGetFetch;

namespace DotnetInspect.Cli.Inspectors;

internal static class DependencyGraphProjection
{
    internal static DependencyGraphDocument Type(
        TypeDependencyResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.MatchedType is null)
            throw new ArgumentException(
                "A type dependency graph requires a matched root.",
                nameof(result));

        var builder = new Builder(
            new DependencyGraphNodeIdentity.Type(result.MatchedType),
            Field(result.MatchedType));
        foreach (TypeDependencyRelationship relationship in
                 result.Relationships.OrderBy(
                     static relationship => relationship.Ordinal))
        {
            builder.AddEdge(
                new DependencyGraphNodeIdentity.Type(
                    relationship.SourceTypeName),
                Field(relationship.SourceTypeName),
                new DependencyGraphNodeIdentity.Type(
                    relationship.TargetTypeName),
                Field(relationship.TargetTypeName),
                relationship.Kind switch
                {
                    TypeDependencyRelationshipKind.BaseType => "base-type",
                    _ => "interface",
                },
                DependencyGraphResolutionState.Declared,
                evidenceIdentity: null);
        }
        foreach (TypeDependencyDepthBoundary boundary in
                 result.DepthBoundaries)
        {
            builder.AddDepthBoundary(
                new DependencyGraphNodeIdentity.Type(
                    boundary.TypeName),
                boundary.MaximumDepth,
                DependencyGraphDepthBoundaryProducerKind.Type);
        }

        return builder.Build();
    }

    internal static DependencyGraphDocument Library(
        LibraryDependencyGraphResult.Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        LibraryMetadataService.AssemblyReferenceGraph referenceGraph =
            graph.ReferenceGraph;
        var builder = new Builder(
            new DependencyGraphNodeIdentity.Library(referenceGraph.Root),
            Field(graph.AssemblyName));

        foreach (LibraryMetadataService.AssemblyReferenceRelationship
                 relationship in referenceGraph.Relationships.OrderBy(
                     static relationship => relationship.Ordinal))
        {
            builder.AddEdge(
                new DependencyGraphNodeIdentity.Library(
                    relationship.Source),
                LibraryLabel(
                    relationship.Source,
                    FindNode(referenceGraph.Nodes, relationship.Source)),
                new DependencyGraphNodeIdentity.Library(
                    relationship.Target),
                LibraryLabel(
                    relationship.Target,
                    FindNode(referenceGraph.Nodes, relationship.Target)),
                "assembly-reference",
                relationship.ResolutionFailure switch
                {
                    AssemblyReferenceResolutionFailure.Unavailable =>
                        DependencyGraphResolutionState.Unavailable,
                    AssemblyReferenceResolutionFailure.Rejected =>
                        DependencyGraphResolutionState.Rejected,
                    null when relationship.IsResolved =>
                        DependencyGraphResolutionState.Resolved,
                    null => DependencyGraphResolutionState.Declared,
                    _ => throw new InvalidOperationException(
                        "Unknown assembly reference resolution failure."),
                },
                new DependencyGraphEvidenceIdentity.AssemblyReference(
                    relationship.RequestedTarget));
        }
        foreach (LibraryMetadataService.AssemblyReferenceDepthBoundary
                 boundary in referenceGraph.DepthBoundaries)
        {
            builder.AddDepthBoundary(
                new DependencyGraphNodeIdentity.Library(
                    boundary.Identity),
                boundary.MaximumDepth,
                DependencyGraphDepthBoundaryProducerKind.Library);
        }

        return builder.Build();
    }

    internal static DependencyGraphDocument Library(
        LibraryDependencyGraphResult.Empty empty)
    {
        ArgumentNullException.ThrowIfNull(empty);
        return new Builder(
            new DependencyGraphNodeIdentity.Library(empty.Identity),
            Field(empty.AssemblyName)).Build();
    }

    internal static DependencyGraphDocument Package(
        PackageDependencyGraphResult.Graph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        PackageDependencyGraph dependencyGraph = graph.DependencyGraph;
        var rootIdentity = new PackageDependencyIdentity(
            graph.ManifestPackageName,
            graph.ManifestVersion);
        var builder = new Builder(
            PackageIdentity(rootIdentity),
            Field(graph.Title));

        foreach (PackageDependencyRelationship relationship in
                 dependencyGraph.Relationships.OrderBy(
                     static relationship => relationship.Ordinal))
        {
            builder.AddEdge(
                PackageIdentity(relationship.Source),
                PackageLabel(
                    relationship.Source,
                    FindNode(
                        dependencyGraph.Nodes,
                        relationship.Source)?.Author),
                PackageIdentity(relationship.Target),
                PackageLabel(
                    relationship.Target,
                    FindNode(
                        dependencyGraph.Nodes,
                        relationship.Target)?.Author),
                "package-dependency",
                relationship.Resolution switch
                {
                    PackageDependencyResolutionState.Resolved =>
                        DependencyGraphResolutionState.Resolved,
                    PackageDependencyResolutionState.Unavailable =>
                        DependencyGraphResolutionState.Unavailable,
                    _ => DependencyGraphResolutionState.Declared,
                },
                new DependencyGraphEvidenceIdentity
                    .PackageVersionConstraint(
                        Field(relationship.VersionConstraint)));
        }

        return builder.Build();
    }

    internal static DependencyGraphDocument Package(
        PackageDependencyGraphResult.Empty empty)
    {
        ArgumentNullException.ThrowIfNull(empty);
        var rootIdentity = new PackageDependencyIdentity(
            empty.ManifestPackageName,
            empty.ManifestVersion);
        return new Builder(
            PackageIdentity(rootIdentity),
            Field($"{empty.PackageName} ({empty.Version})")).Build();
    }

    internal static DependencyGraphDocument Package(
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
                    edge.Diagnostics));
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
                    projection.Candidate,
                    projection.RootOccurrenceIndex is { } rootIndex
                        ? rootOccurrenceIndexes[rootIndex]
                        : null,
                    projection.Diagnostics)),
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

    internal static DependencyGraphDocument RestoredProject(
        RestoredProjectDependencyTraversal traversal,
        int rootOccurrenceIndex,
        InertString rootLabel)
    {
        ArgumentNullException.ThrowIfNull(traversal);
        var nodes = new List<DependencyGraphNode>();
        var nodeIds =
            new Dictionary<RestoredProjectGraphParentIdentity, int>();
        foreach (RestoredProjectTraversalNode node in traversal.Nodes)
        {
            int id = nodes.Count;
            nodeIds.Add(node.Identity, id);
            nodes.Add(
                new DependencyGraphNode(
                    id,
                    RestoredIdentity(node.Identity),
                    node.Identity is RestoredProjectGraphParentIdentity.Root
                        ? rootLabel
                        : node.Identity
                            is RestoredProjectGraphParentIdentity.Project
                                ? node.SourceProjectSpelling!.Value
                                : RestoredPackageLabel(node.Identity)));
        }

        var edges = ImmutableArray.CreateBuilder<DependencyGraphEdge>();
        foreach (RestoredProjectTraversalProjectRelationship relationship in
                 traversal.ProjectRelationships)
        {
            edges.Add(
                new DependencyGraphEdge(
                    edges.Count,
                    nodeIds[relationship.Parent],
                    nodeIds[
                        new RestoredProjectGraphParentIdentity.Project(
                            relationship.Dependency)],
                    "project-reference",
                    [rootOccurrenceIndex],
                    relationship.Distance,
                    DependencyGraphResolutionState.Resolved,
                    new DependencyGraphEvidenceIdentity
                        .RestoredProjectRelationship(
                            relationship.Identity)));
        }
        foreach (RestoredProjectTraversalPackageRelationship relationship in
                 traversal.PackageRelationships)
        {
            edges.Add(
                new DependencyGraphEdge(
                    edges.Count,
                    nodeIds[relationship.Edge.Parent],
                    nodeIds[
                        new RestoredProjectGraphParentIdentity.Package(
                            relationship.Edge.Dependency)],
                    "package-dependency",
                    [rootOccurrenceIndex],
                    relationship.Distance,
                    DependencyGraphResolutionState.Resolved,
                    new DependencyGraphEvidenceIdentity
                        .RestoredPackageRelationship(
                            relationship.Edge.Identity)));
        }

        int rootNodeId = nodeIds[
            new RestoredProjectGraphParentIdentity.Root(traversal.Root)];
        ImmutableArray<DependencyGraphDepthBoundary> depthBoundaries =
        [
            .. traversal.DepthBoundaries.Select(boundary =>
                new DependencyGraphDepthBoundary(
                    nodeIds[boundary.Node],
                    PackageProjectionId: null,
                    boundary.MaximumDepth,
                    [rootOccurrenceIndex],
                    DependencyGraphDepthBoundaryProducerKind.Restored)),
        ];
        return new DependencyGraphDocument(
            [new DependencyGraphRootOccurrence(
                rootOccurrenceIndex,
                rootNodeId)],
            [.. nodes],
            edges.ToImmutable(),
            [],
            depthBoundaries);
    }

    internal static DependencyGraphDocument PackageRoot(
        PackageDependencyEvidenceRoot root,
        int rootOccurrenceIndex)
    {
        ArgumentNullException.ThrowIfNull(root);
        PackageSourceCoordinate coordinate =
            ((PackageDependencyEvidenceRootIdentity.Package)root.Identity)
                .Coordinate;
        return RootOnly(
            new DependencyGraphNodeIdentity.Package(
                coordinate.PackageId,
                coordinate.Version),
            root.Display,
            rootOccurrenceIndex);
    }

    internal static DependencyGraphDocument RestoredRoot(
        RestoredProjectDependencyFacts facts,
        InertString label,
        int rootOccurrenceIndex)
    {
        ArgumentNullException.ThrowIfNull(facts);
        return RootOnly(
            new DependencyGraphNodeIdentity.RestoredRoot(facts.Root),
            label,
            rootOccurrenceIndex);
    }

    internal static DependencyGraphDocument WithRootOccurrence(
        DependencyGraphDocument document,
        int occurrenceIndex)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Roots.Length != 1)
        {
            throw new ArgumentException(
                "A single-root graph is required.",
                nameof(document));
        }

        return document with
        {
            Roots =
            [
                new DependencyGraphRootOccurrence(
                    occurrenceIndex,
                    document.Roots[0].NodeId),
            ],
            Edges =
            [
                .. document.Edges.Select(edge => edge with
                {
                    RootOccurrences = [occurrenceIndex],
                }),
            ],
            DepthBoundaries =
            [
                .. document.DepthBoundaries.Select(boundary => boundary with
                {
                    RootOccurrences = [occurrenceIndex],
                }),
            ],
        };
    }

    internal static DependencyGraphDocument Combine(
        IEnumerable<DependencyGraphDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var nodes = new List<DependencyGraphNode>();
        var nodeIds = new Dictionary<DependencyGraphNodeIdentity, int>(
            CombinedNodeIdentityComparer.Instance);
        var roots = new List<DependencyGraphRootOccurrence>();
        var pendingEdges = new List<(int Ordinal, DependencyGraphEdge Edge)>();
        var packageProjections =
            new List<DependencyGraphPackageProjection>();
        var depthBoundaries =
            new List<DependencyGraphDepthBoundary>();

        foreach (DependencyGraphDocument document in documents)
        {
            var projectionRemap = new Dictionary<int, int>();
            foreach (DependencyGraphPackageProjection projection in
                     document.PackageProjections)
            {
                projectionRemap.Add(
                    projection.Id,
                    packageProjections.Count);
                packageProjections.Add(
                    projection with
                    {
                        Id = packageProjections.Count,
                    });
            }

            var remap = new Dictionary<int, int>();
            foreach (DependencyGraphNode node in document.Nodes)
            {
                DependencyGraphNodeIdentity identity = RemapProjectionIdentity(
                    node.Identity,
                    projectionRemap);
                if (!nodeIds.TryGetValue(identity, out int id))
                {
                    id = nodes.Count;
                    nodeIds.Add(identity, id);
                    nodes.Add(node with
                    {
                        Id = id,
                        Identity = identity,
                    });
                }
                remap.Add(node.Id, id);
            }
            foreach (int projectionIndex in projectionRemap.Values)
            {
                DependencyGraphPackageProjection projection =
                    packageProjections[projectionIndex];
                packageProjections[projectionIndex] = projection with
                {
                    NodeId = remap[projection.NodeId],
                };
            }

            roots.AddRange(
                document.Roots.Select(root => root with
                {
                    NodeId = remap[root.NodeId],
                }));
            foreach (DependencyGraphEdge edge in document.Edges)
            {
                pendingEdges.Add((
                    pendingEdges.Count,
                    edge with
                    {
                        SourceNodeId = remap[edge.SourceNodeId],
                        TargetNodeId = remap[edge.TargetNodeId],
                        EvidenceIdentity = RemapProjectionIdentity(
                            edge.EvidenceIdentity,
                            projectionRemap),
                        SourcePackageProjectionId =
                            RemapProjectionIndex(
                                edge.SourcePackageProjectionId,
                                projectionRemap),
                        TargetPackageProjectionId =
                            RemapProjectionIndex(
                                edge.TargetPackageProjectionId,
                                projectionRemap),
                    }));
            }
            depthBoundaries.AddRange(
                document.DepthBoundaries.Select(boundary => boundary with
                {
                    NodeId = remap[boundary.NodeId],
                    PackageProjectionId = RemapProjectionIndex(
                        boundary.PackageProjectionId,
                        projectionRemap),
                }));
        }

        var coalesced = new List<(int Ordinal, DependencyGraphEdge Edge)>();
        var edgeIndexes =
            new Dictionary<DependencyGraphEdgeKey, int>();
        foreach ((int ordinal, DependencyGraphEdge edge) in pendingEdges)
        {
            var key = new DependencyGraphEdgeKey(
                edge.SourceNodeId,
                edge.TargetNodeId,
                edge.Relationship,
                edge.Resolution,
                edge.EvidenceIdentity);
            if (!edgeIndexes.TryGetValue(key, out int existingIndex))
            {
                edgeIndexes.Add(key, coalesced.Count);
                coalesced.Add((ordinal, edge));
                continue;
            }

            (int existingOrdinal, DependencyGraphEdge existing) =
                coalesced[existingIndex];
            coalesced[existingIndex] = (
                Math.Min(existingOrdinal, ordinal),
                existing with
                {
                    RootOccurrences =
                    [
                        .. existing.RootOccurrences
                            .Concat(edge.RootOccurrences)
                            .Distinct()
                            .Order(),
                    ],
                    MinimumDepth = Math.Min(
                        existing.MinimumDepth,
                        edge.MinimumDepth),
                });
        }

        DependencyGraphEdge[] orderedEdges =
        [
            .. coalesced
                .OrderBy(entry =>
                    entry.Edge.RootOccurrences.IsEmpty
                        ? int.MaxValue
                        : entry.Edge.RootOccurrences.Min())
                .ThenBy(entry => entry.Edge.MinimumDepth)
                .ThenBy(entry => entry.Ordinal)
                .Select((entry, index) => entry.Edge with { Id = index }),
        ];
        return new DependencyGraphDocument(
            [.. roots.OrderBy(static root => root.OccurrenceIndex)],
            [.. nodes],
            [.. orderedEdges],
            [.. packageProjections],
            [.. depthBoundaries]);
    }

    private static DependencyGraphDocument RootOnly(
        DependencyGraphNodeIdentity identity,
        InertString label,
        int rootOccurrenceIndex) =>
        new(
            [new DependencyGraphRootOccurrence(rootOccurrenceIndex, 0)],
            [new DependencyGraphNode(0, identity, label)],
            [],
            [],
            []);

    private static DependencyGraphNodeIdentity RemapProjectionIdentity(
        DependencyGraphNodeIdentity identity,
        IReadOnlyDictionary<int, int> projectionRemap) =>
        identity switch
        {
            DependencyGraphNodeIdentity.PackageBoundary boundary =>
                boundary with
                {
                    SourceProjectionIndex =
                        projectionRemap[boundary.SourceProjectionIndex],
                },
            DependencyGraphNodeIdentity.PackageFailure failure =>
                failure with
                {
                    SourceProjectionIndex =
                        projectionRemap[failure.SourceProjectionIndex],
                },
            DependencyGraphNodeIdentity.PackageBudget budget =>
                budget with
                {
                    SourceProjectionIndex =
                        projectionRemap[budget.SourceProjectionIndex],
                },
            _ => identity,
        };

    private static DependencyGraphEvidenceIdentity? RemapProjectionIdentity(
        DependencyGraphEvidenceIdentity? identity,
        IReadOnlyDictionary<int, int> projectionRemap) =>
        identity is DependencyGraphEvidenceIdentity.PackageDeclaration
            declaration
            ? declaration with
            {
                SourceProjectionIndex =
                    projectionRemap[declaration.SourceProjectionIndex],
            }
            : identity;

    private static int? RemapProjectionIndex(
        int? projectionIndex,
        IReadOnlyDictionary<int, int> projectionRemap) =>
        projectionIndex is { } value ? projectionRemap[value] : null;

    private static DependencyGraphNodeIdentity.Package PackageIdentity(
        PackageDependencyIdentity identity) =>
        new(identity.PackageId, identity.Version);

    private static PackageDependencyGraphNode? FindNode(
        IReadOnlyList<PackageDependencyGraphNode> nodes,
        PackageDependencyIdentity identity) =>
        nodes.FirstOrDefault(node =>
            StringComparer.OrdinalIgnoreCase.Equals(
                node.Identity.PackageId,
                identity.PackageId)
            && StringComparer.OrdinalIgnoreCase.Equals(
                node.Identity.Version,
                identity.Version));

    private static AssemblyReferenceNode? FindNode(
        IReadOnlyList<AssemblyReferenceNode> nodes,
        ManagedMetadataIdentity identity) =>
        identity is ManagedMetadataIdentity.Assembly assembly
            ? nodes.FirstOrDefault(node =>
                (node.ResolvedIdentity
                    ?? new AssemblyReferenceIdentity(
                        node.Name,
                        Version.TryParse(
                            node.Version,
                            out Version? version)
                            ? version
                            : null,
                        Culture: null,
                        node.PublicKeyToken))
                .IsEquivalentTo(assembly.Identity))
            : null;

    private static InertString LibraryLabel(
        ManagedMetadataIdentity identity,
        AssemblyReferenceNode? node)
    {
        if (identity is ManagedMetadataIdentity.Module module)
            return Field(module.Name);

        var assembly = identity as ManagedMetadataIdentity.Assembly
            ?? throw new InvalidOperationException(
                "Unknown managed metadata identity.");
        if (node is null)
            return Field(AssemblyIdentityFormatter.Format(assembly.Identity));

        string version = assembly.Identity.Version?.ToString() ?? "";
        string label = !string.IsNullOrEmpty(node.Company)
            ? $"{assembly.Identity.Name} {version} [{node.Company}]"
            : $"{assembly.Identity.Name} {version}";
        if (node.ResolutionFailure is { } failure)
        {
            label +=
                $" ({failure.ToString().ToLowerInvariant()})";
        }
        return Field(label);
    }

    private static InertString PackageLabel(
        PackageDependencyIdentity identity,
        string? author)
    {
        InertString id = Field(identity.PackageId);
        InertString version = Field(identity.Version);
        return string.IsNullOrEmpty(author)
            ? InertString.Format(
                TextPolicy.Field,
                $"{id} {version}")
            : InertString.Format(
                TextPolicy.Field,
                $"{id} {version} [{Field(author)}]");
    }

    private static InertString PackageLabel(
        string packageId,
        string version) =>
        InertString.Format(
            TextPolicy.Field,
            $"{Field(packageId)} {Field(version)}");

    private static InertString BoundaryLabel(
        string packageId,
        string constraint,
        string state) =>
        InertString.Format(
            TextPolicy.Field,
            $"{Field(packageId)} {Field(constraint)} ({state})");

    private static DependencyGraphNodeIdentity RestoredIdentity(
        RestoredProjectGraphParentIdentity identity) =>
        identity switch
        {
            RestoredProjectGraphParentIdentity.Root root =>
                new DependencyGraphNodeIdentity.RestoredRoot(root.Identity),
            RestoredProjectGraphParentIdentity.Project project =>
                new DependencyGraphNodeIdentity.RestoredProject(
                    project.Identity),
            RestoredProjectGraphParentIdentity.Package package =>
                new DependencyGraphNodeIdentity.RestoredPackage(
                    package.Identity),
            _ => throw new InvalidOperationException(
                "Unknown restored-project node identity."),
        };

    private static InertString RestoredPackageLabel(
        RestoredProjectGraphParentIdentity identity)
    {
        RestoredProjectPackageNodeIdentity package =
            ((RestoredProjectGraphParentIdentity.Package)identity).Identity;
        return PackageLabel(
            package.Coordinate.PackageId,
            package.Coordinate.Version);
    }

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);

    private sealed class Builder(
        DependencyGraphNodeIdentity rootIdentity,
        InertString rootLabel)
    {
        private readonly List<DependencyGraphNode> _nodes =
        [
            new(0, rootIdentity, rootLabel),
        ];
        private readonly Dictionary<DependencyGraphNodeIdentity, int>
            _nodeIds =
            new(BuilderNodeIdentityComparer.Instance)
            {
                [rootIdentity] = 0,
            };
        private readonly List<PendingEdge> _edges = [];
        private readonly List<DependencyGraphDepthBoundary>
            _depthBoundaries = [];

        internal void AddEdge(
            DependencyGraphNodeIdentity source,
            InertString sourceLabel,
            DependencyGraphNodeIdentity target,
            InertString targetLabel,
            string relationship,
            DependencyGraphResolutionState resolution,
            DependencyGraphEvidenceIdentity? evidenceIdentity)
        {
            int sourceId = AddNode(source, sourceLabel);
            int targetId = AddNode(target, targetLabel);
            _edges.Add(
                new PendingEdge(
                    sourceId,
                    targetId,
                    relationship,
                    resolution,
                    evidenceIdentity));
        }

        internal void AddDepthBoundary(
            DependencyGraphNodeIdentity identity,
            int maximumDepth,
            DependencyGraphDepthBoundaryProducerKind producer)
        {
            if (!_nodeIds.TryGetValue(identity, out int nodeId))
            {
                throw new InvalidOperationException(
                    "A depth boundary must identify an existing graph node.");
            }

            _depthBoundaries.Add(
                new DependencyGraphDepthBoundary(
                    nodeId,
                    PackageProjectionId: null,
                    maximumDepth,
                    [1],
                    producer));
        }

        internal DependencyGraphDocument Build()
        {
            Dictionary<int, int> distances = MinimumDistances();
            return new DependencyGraphDocument(
                [new DependencyGraphRootOccurrence(1, 0)],
                [.. _nodes],
                [
                    .. _edges.Select((edge, index) =>
                        new DependencyGraphEdge(
                            index,
                            edge.SourceNodeId,
                            edge.TargetNodeId,
                            edge.Relationship,
                            [1],
                            distances.TryGetValue(
                                edge.SourceNodeId,
                                out int sourceDepth)
                                ? sourceDepth + 1
                                : 0,
                            edge.Resolution,
                            edge.EvidenceIdentity)),
                ],
                [],
                [.. _depthBoundaries]);
        }

        private int AddNode(
            DependencyGraphNodeIdentity identity,
            InertString label)
        {
            if (_nodeIds.TryGetValue(identity, out int id))
                return id;

            id = _nodes.Count;
            _nodeIds.Add(identity, id);
            _nodes.Add(new DependencyGraphNode(id, identity, label));
            return id;
        }

        private Dictionary<int, int> MinimumDistances()
        {
            var distances = new Dictionary<int, int> { [0] = 0 };
            var pending = new Queue<int>();
            pending.Enqueue(0);
            while (pending.Count > 0)
            {
                int source = pending.Dequeue();
                int distance = distances[source] + 1;
                foreach (PendingEdge edge in _edges.Where(
                             edge => edge.SourceNodeId == source))
                {
                    if (distances.TryGetValue(
                            edge.TargetNodeId,
                            out int previous)
                        && previous <= distance)
                    {
                        continue;
                    }

                    distances[edge.TargetNodeId] = distance;
                    pending.Enqueue(edge.TargetNodeId);
                }
            }

            return distances;
        }

        private sealed record PendingEdge(
            int SourceNodeId,
            int TargetNodeId,
            string Relationship,
            DependencyGraphResolutionState Resolution,
            DependencyGraphEvidenceIdentity? EvidenceIdentity);
    }

    private sealed class CombinedNodeIdentityComparer :
        IEqualityComparer<DependencyGraphNodeIdentity>
    {
        internal static CombinedNodeIdentityComparer Instance { get; } =
            new();

        public bool Equals(
            DependencyGraphNodeIdentity? x,
            DependencyGraphNodeIdentity? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            if (x is DependencyGraphNodeIdentity.Library left
                && y is DependencyGraphNodeIdentity.Library right)
            {
                return ManagedMetadataIdentityEquals(
                    left.Identity,
                    right.Identity);
            }
            return EqualityComparer<DependencyGraphNodeIdentity>.Default
                .Equals(x, y);
        }

        public int GetHashCode(DependencyGraphNodeIdentity obj) =>
            obj is DependencyGraphNodeIdentity.Library library
                ? ManagedMetadataIdentityHashCode(library.Identity)
                : obj.GetHashCode();
    }

    private sealed class BuilderNodeIdentityComparer :
        IEqualityComparer<DependencyGraphNodeIdentity>
    {
        internal static BuilderNodeIdentityComparer Instance { get; } =
            new();

        public bool Equals(
            DependencyGraphNodeIdentity? x,
            DependencyGraphNodeIdentity? y)
        {
            if (ReferenceEquals(x, y))
                return true;
            return (x, y) switch
            {
                (DependencyGraphNodeIdentity.Library left,
                    DependencyGraphNodeIdentity.Library right) =>
                        ManagedMetadataIdentityEquals(
                            left.Identity,
                            right.Identity),
                (DependencyGraphNodeIdentity.Package left,
                    DependencyGraphNodeIdentity.Package right) =>
                        StringComparer.OrdinalIgnoreCase.Equals(
                            left.Id,
                            right.Id)
                        && StringComparer.OrdinalIgnoreCase.Equals(
                            left.Version,
                            right.Version),
                _ => EqualityComparer<DependencyGraphNodeIdentity>.Default
                    .Equals(x, y),
            };
        }

        public int GetHashCode(DependencyGraphNodeIdentity obj)
        {
            if (obj is DependencyGraphNodeIdentity.Library library)
                return ManagedMetadataIdentityHashCode(library.Identity);
            if (obj is DependencyGraphNodeIdentity.Package package)
            {
                var hash = new HashCode();
                hash.Add(
                    DependencyGraphNodeKind.Package);
                hash.Add(package.Id, StringComparer.OrdinalIgnoreCase);
                hash.Add(package.Version, StringComparer.OrdinalIgnoreCase);
                return hash.ToHashCode();
            }
            return obj.GetHashCode();
        }
    }

    private static bool ManagedMetadataIdentityEquals(
        ManagedMetadataIdentity left,
        ManagedMetadataIdentity right) =>
        (left, right) switch
        {
            (ManagedMetadataIdentity.Assembly leftAssembly,
                ManagedMetadataIdentity.Assembly rightAssembly) =>
                    leftAssembly.Identity.IsEquivalentTo(
                        rightAssembly.Identity),
            (ManagedMetadataIdentity.Module leftModule,
                ManagedMetadataIdentity.Module rightModule) =>
                    leftModule == rightModule,
            _ => false,
        };

    private static int ManagedMetadataIdentityHashCode(
        ManagedMetadataIdentity identity)
    {
        var hash = new HashCode();
        switch (identity)
        {
            case ManagedMetadataIdentity.Assembly assembly:
                hash.Add(0);
                hash.Add(
                    AssemblyReferenceIdentity.EquivalentComparer.GetHashCode(
                        assembly.Identity));
                break;
            case ManagedMetadataIdentity.Module module:
                hash.Add(1);
                hash.Add(module);
                break;
            default:
                throw new InvalidOperationException(
                    "Unknown managed metadata identity.");
        }
        return hash.ToHashCode();
    }

    private readonly record struct DependencyGraphEdgeKey(
        int SourceNodeId,
        int TargetNodeId,
        string Relationship,
        DependencyGraphResolutionState Resolution,
        DependencyGraphEvidenceIdentity? EvidenceIdentity);
}
