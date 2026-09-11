using System.Collections.Immutable;
using DotnetInspect.Cli.Models;
using DotnetInspector.Queries;
using InertText;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Inspectors;

/// <summary>Assigns document addresses to owner-issued nodes and relationships; never traverses.</summary>
internal sealed class DependencyGraphComposition
{
    private readonly List<DependencyGraphRootOccurrence> _roots = [];
    private readonly List<DependencyGraphNode> _nodes = [];
    private readonly List<DependencyGraphEdge> _edges = [];
    private readonly List<DependencyGraphBoundary> _boundaries = [];
    private readonly Dictionary<DependencyGraphNodeIdentity, int> _nodeIds = [];
    private readonly Dictionary<AssemblyReferenceIdentity, int> _assemblyIds =
        new(AssemblyReferenceIdentity.EquivalentComparer);
    private readonly Dictionary<object, int> _edgeIds = [];

    internal int Node(DependencyGraphNodeIdentity identity, InertString label)
    {
        AssemblyReferenceIdentity? assemblyIdentity = identity is DependencyGraphNodeIdentity.Library
            { Identity: ManagedMetadataIdentity.Assembly assembly } ? assembly.Identity : null;
        if (assemblyIdentity is not null && _assemblyIds.TryGetValue(assemblyIdentity, out int assemblyId))
            return assemblyId;
        if (_nodeIds.TryGetValue(identity, out int id))
            return id;
        id = _nodes.Count;
        _nodeIds.Add(identity, id);
        if (assemblyIdentity is not null)
            _assemblyIds.Add(assemblyIdentity, id);
        _nodes.Add(new(id, identity, label));
        return id;
    }

    internal void Root(int occurrence, int node) => _roots.Add(new(occurrence, node));

    internal int Append(int occurrence, DependencyGraphDocument graph)
    {
        int[] nodes = [.. graph.Nodes.Select(node => Node(node.Identity, node.Label))];
        int root = nodes[graph.Roots.Single().NodeId];
        Root(occurrence, root);
        _boundaries.AddRange(graph.Boundaries.Select(boundary => boundary with
        {
            RootOccurrence = occurrence, NodeId = nodes[boundary.NodeId],
        }));
        var relationshipOccurrences =
            new Dictionary<(
                int Source,
                int Target,
                string Relationship,
                DependencyGraphEvidenceIdentity? Evidence), int>();
        foreach (DependencyGraphEdge edge in graph.Edges)
        {
            int source = nodes[edge.SourceNodeId];
            int target = nodes[edge.TargetNodeId];
            var relationship = (
                source,
                target,
                edge.Relationship,
                edge.EvidenceIdentity);
            relationshipOccurrences.TryGetValue(
                relationship,
                out int occurrenceIndex);
            relationshipOccurrences[relationship] = occurrenceIndex + 1;
            Edge(nodes[edge.SourceNodeId], nodes[edge.TargetNodeId], edge.Relationship,
                edge.Resolution, edge.EvidenceIdentity, [(occurrence, edge.MinimumDepth)],
                ("metadata", source, target, edge.Relationship,
                    edge.EvidenceIdentity, occurrenceIndex));
        }
        return root;
    }

    internal int Append(int occurrence, RestoredProjectDependencyTraversal traversal)
    {
        var nodes = new Dictionary<RestoredProjectGraphParentIdentity, int>();
        foreach (RestoredProjectTraversalNode node in traversal.Nodes)
        {
            InertString label = node.Identity switch
            {
                RestoredProjectGraphParentIdentity.Root => Field("Restored project"),
                RestoredProjectGraphParentIdentity.Project => node.SourceProjectSpelling
                    ?? throw new InvalidOperationException("A project node must carry its source spelling."),
                RestoredProjectGraphParentIdentity.Package package =>
                    Field($"{package.Identity.Coordinate.PackageId} {package.Identity.Coordinate.Version}"),
                _ => throw new InvalidOperationException("Unknown restored node identity."),
            };
            nodes.Add(node.Identity, Node(new DependencyGraphNodeIdentity.Restored(node.Identity), label));
        }
        int root = nodes[new RestoredProjectGraphParentIdentity.Root(traversal.Root)];
        Root(occurrence, root);
        _boundaries.AddRange(traversal.DepthBoundaries.Select(boundary =>
            new DependencyGraphBoundary(occurrence, nodes[boundary.Node], "Depth", boundary.MaximumDepth)));
        foreach (RestoredProjectTraversalProjectRelationship edge in traversal.ProjectRelationships)
        {
            Edge(nodes[edge.Parent],
                nodes[new RestoredProjectGraphParentIdentity.Project(edge.Dependency)],
                "project-reference", DependencyGraphResolutionState.Resolved,
                new DependencyGraphEvidenceIdentity.RestoredProject(edge),
                [(occurrence, edge.Distance)], edge.Identity);
        }
        foreach (RestoredProjectTraversalPackageRelationship relationship in traversal.PackageRelationships)
        {
            RestoredProjectGraphEdge edge = relationship.Edge;
            Edge(nodes[edge.Parent],
                nodes[new RestoredProjectGraphParentIdentity.Package(edge.Dependency)],
                "package-dependency", DependencyGraphResolutionState.Resolved,
                new DependencyGraphEvidenceIdentity.RestoredPackage(edge),
                [(occurrence, relationship.Distance)], edge.Identity);
        }
        return root;
    }

    internal void Append(PackageDependencyTraversalOutcome traversal, IReadOnlyList<int> occurrences)
    {
        int[] nodes = [.. traversal.Nodes.Select(node => Node(
            new DependencyGraphNodeIdentity.Coordinate(node.Coordinate),
            Field($"{node.Coordinate.PackageId} {node.Coordinate.Version}")))];
        for (int i = 0; i < traversal.Roots.Length; i++)
            Root(occurrences[i], nodes[traversal.Roots[i].NodeIndex]);
        for (int i = 0; i < traversal.Projections.Length; i++)
        {
            PackageDependencyTraversalProjection projection =
                traversal.Projections[i];
            if (projection.Evidence?.Selection.Status
                is not { } selectionStatus
                || selectionStatus
                    == PackageDependencyEvidenceSelectionStatus.Selected)
            {
                continue;
            }

            for (int root = 0; root < traversal.RootReachability.Length; root++)
            {
                if (traversal.RootReachability[root]
                    .ProjectionDistances.ContainsKey(i))
                {
                    _boundaries.Add(new(
                        occurrences[root],
                        nodes[projection.NodeIndex],
                        selectionStatus.ToString(),
                        MaximumDepth: null));
                }
            }
        }
        foreach (var boundary in traversal.DepthBoundaries)
        {
            foreach (int root in boundary.AffectedRootOccurrences)
                _boundaries.Add(new(occurrences[root], nodes[boundary.NodeIndex], "Depth", boundary.MaximumDepth));
        }

        for (int i = 0; i < traversal.Edges.Length; i++)
        {
            PackageDependencyTraversalEdge edge = traversal.Edges[i];
            int target;
            int? targetProjection = null;
            DotnetInspector.Packages.PackageAcquisitionCandidate? candidate = null;
            var resolution = DependencyGraphResolutionState.Declared;
            switch (edge.Target)
            {
                case PackageDependencyTraversalEdgeTarget.Node node:
                    target = nodes[node.NodeIndex];
                    targetProjection = node.ProjectionIndex;
                    candidate = traversal.Projections[node.ProjectionIndex].Candidate;
                    resolution = DependencyGraphResolutionState.Resolved;
                    break;
                case PackageDependencyTraversalEdgeTarget.DeclarationBoundary:
                case PackageDependencyTraversalEdgeTarget.FailedResolution:
                case PackageDependencyTraversalEdgeTarget.WorkBudget:
                    target = Node(
                        new DependencyGraphNodeIdentity.Declaration(
                            edge.SourceProjectionIndex, edge.Declaration.Identity, edge.Authority),
                        Field($"{edge.Declaration.Identity.CanonicalPackageId} {edge.Declaration.CanonicalVersionConstraint}"));
                    if (edge.Target is not PackageDependencyTraversalEdgeTarget.DeclarationBoundary)
                        resolution = DependencyGraphResolutionState.Unavailable;
                    break;
                default:
                    throw new InvalidOperationException("Unknown package traversal target.");
            }
            List<(int Root, int Distance)> roots = [];
            for (int r = 0; r < traversal.RootReachability.Length; r++)
            {
                if (traversal.RootReachability[r].IsEdgeAdmitted(i, out int distance))
                    roots.Add((occurrences[r], distance));
            }
            Edge(nodes[traversal.Projections[edge.SourceProjectionIndex].NodeIndex],
                target, "package-dependency", resolution,
                new DependencyGraphEvidenceIdentity.Declaration(
                    edge.SourceProjectionIndex, edge.Declaration, edge.Authority,
                    targetProjection, candidate, i),
                roots, ("package-traversal", i));
            if (edge.Target is PackageDependencyTraversalEdgeTarget.DeclarationBoundary)
            {
                foreach ((int root, _) in roots)
                    _boundaries.Add(new(root, target, "Source", null));
            }
        }
    }

    internal DependencyGraphDocument Build() => new(
        [.. _roots.OrderBy(root => root.OccurrenceIndex)], [.. _nodes],
        [.. _edges.OrderBy(edge => edge.RootOccurrences.Min()).ThenBy(edge => edge.Id)
            .Select((edge, id) => edge with { Id = id })])
    {
        Boundaries = [.. _boundaries.Distinct()],
    };

    private void Edge(
        int source, int target, string relationship,
        DependencyGraphResolutionState resolution, DependencyGraphEvidenceIdentity? evidence,
        IEnumerable<(int Root, int Distance)> roots, object identity)
    {
        ImmutableDictionary<int, int> distances = roots.ToImmutableDictionary(
            root => root.Root, root => root.Distance);
        if (_edgeIds.TryGetValue(identity, out int existing))
        {
            DependencyGraphEdge previous = _edges[existing];
            distances = previous.RootDistances.SetItems(distances);
            _edges[existing] = previous with
            {
                RootOccurrences = [.. distances.Keys.Order()],
                MinimumDepth = distances.Values.Min(),
                RootDistances = distances,
            };
            return;
        }
        int id = _edges.Count;
        _edgeIds.Add(identity, id);
        _edges.Add(new(id, source, target, relationship, [.. distances.Keys.Order()],
            distances.Values.Min(), resolution, evidence) { RootDistances = distances });
    }

    private static InertString Field(string value) => new(TextPolicy.Field, value);
}
