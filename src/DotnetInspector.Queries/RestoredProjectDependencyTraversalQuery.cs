using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using InertText;

namespace DotnetInspector.Queries;

/// <summary>
/// Projects one exact restored-project selection into typed root, project-reference, and package
/// relationships with root-relative distance, explicit depth boundaries, in-scope typed failures,
/// and stated completion. See <c>docs/design/restored-project-dependency-traversal.md</c> for the
/// contract this query owns.
/// </summary>
/// <remarks>
/// The query accepts no path, filesystem, MSBuild, restore, cache, network, logger, or renderer
/// capability. It reuses the restored-project facts owner's single admission, target-selection, and
/// selected-target walk; it never parses assets a second time. Depth admits already-materialized
/// evidence and causes no acquisition.
/// </remarks>
public static class RestoredProjectDependencyTraversalQuery
{
    /// <summary>
    /// The bound on project-relationship occurrences observed while walking one selected target.
    /// It is independent of the facts owner's graph bounds so a project-dense document cannot
    /// change published facts evidence.
    /// </summary>
    public const int MaxProjectRelationships = 16384;

    public static InspectionQuery<RestoredProjectDependencyTraversalResult> Definition { get; } =
        new("Restored project dependency traversal", InspectionCost.NetworkFree);

    public static RestoredProjectDependencyTraversalResult Execute(
        ReadOnlyMemory<byte> assetsBytes,
        RestoredProjectDependencyTraversalRequest? request = null)
    {
        RestoredProjectDependencyProjection projection =
            RestoredProjectDependencyFactsQuery.Project(assetsBytes, request?.Target);

        if (projection.Result is RestoredProjectDependencyFactsResult.Failed documentFailure)
        {
            return new RestoredProjectDependencyTraversalResult.Failed(
                new RestoredProjectDependencyTraversalFailure.Document(documentFailure.Failure));
        }

        RestoredProjectDependencyFacts facts =
            ((RestoredProjectDependencyFactsResult.Available)projection.Result).Value;

        switch (facts.Graph)
        {
            case RestoredProjectGraphResult.Unavailable:
                return new RestoredProjectDependencyTraversalResult.Unavailable(facts);
            case RestoredProjectGraphResult.Failed graphFailure:
                return new RestoredProjectDependencyTraversalResult.Failed(
                    new RestoredProjectDependencyTraversalFailure.Graph(facts, graphFailure.Failure));
            case RestoredProjectGraphResult.Available graph:
                return new RestoredProjectDependencyTraversalResult.Available(
                    Traverse(facts, graph, projection.Topology, request?.MaximumDepth));
            default:
                throw new InvalidOperationException(
                    $"Unknown restored-project graph result: {facts.Graph.GetType().FullName}");
        }
    }

    /// <summary>One outgoing relationship in the selected graph, before depth admission.</summary>
    readonly record struct Relationship(
        string ParentKey,
        string TargetKey,
        RestoredProjectGraphEdge? PackageEdge,
        RestoredProjectProjectRelationshipEvidence? ProjectRelationship);

    static RestoredProjectDependencyTraversal Traverse(
        RestoredProjectDependencyFacts facts,
        RestoredProjectGraphResult.Available graph,
        RestoredProjectGraphTopology topology,
        int? maximumDepth)
    {
        var spellings = new Dictionary<string, InertString>(StringComparer.Ordinal);
        foreach (RestoredProjectProjectNodeEvidence node in topology.ProjectNodes)
            spellings[RestoredProjectNodeKey.ForProject(node.Identity)] = node.SourceSpelling;

        var identities = new Dictionary<string, RestoredProjectGraphParentIdentity>(StringComparer.Ordinal)
        {
            [RestoredProjectNodeKey.Root] = new RestoredProjectGraphParentIdentity.Root(facts.Root),
        };
        var adjacency = new Dictionary<string, List<Relationship>>(StringComparer.Ordinal);

        void Connect(Relationship relationship, RestoredProjectGraphParentIdentity targetIdentity)
        {
            identities[relationship.TargetKey] = targetIdentity;
            if (!adjacency.TryGetValue(relationship.ParentKey, out List<Relationship>? outgoing))
            {
                outgoing = [];
                adjacency[relationship.ParentKey] = outgoing;
            }

            outgoing.Add(relationship);
        }

        foreach (RestoredProjectProjectRelationshipEvidence relationship in topology.ProjectRelationships)
        {
            Connect(
                new Relationship(
                    RestoredProjectNodeKey.For(relationship.Parent),
                    RestoredProjectNodeKey.ForProject(relationship.Dependency),
                    PackageEdge: null,
                    relationship),
                new RestoredProjectGraphParentIdentity.Project(relationship.Dependency));
        }

        foreach (RestoredProjectGraphEdge edge in graph.Edges)
        {
            Connect(
                new Relationship(
                    RestoredProjectNodeKey.For(edge.Parent),
                    RestoredProjectNodeKey.ForPackage(edge.Dependency),
                    edge,
                    ProjectRelationship: null),
                new RestoredProjectGraphParentIdentity.Package(edge.Dependency));
        }

        // Breadth-first from the explicit root, so every reached node carries its minimum
        // relationship distance and depth admission is exact rather than discovery-order dependent.
        var distances = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            [RestoredProjectNodeKey.Root] = 0,
        };
        var frontier = new Queue<string>();
        frontier.Enqueue(RestoredProjectNodeKey.Root);
        var admitted = new List<(Relationship Relationship, int Distance)>();

        // A node whose expansion produced a typed failure has evidence beyond it even when that
        // evidence resolved to no relationship, so it is a depth boundary rather than a leaf.
        var failureOwners = new HashSet<string>(StringComparer.Ordinal);
        foreach (RestoredProjectGraphFailureOccurrence occurrence in topology.FailureOccurrences)
        {
            if (occurrence.OwnerNodeKey is { } owner)
                failureOwners.Add(owner);
        }

        while (frontier.Count > 0)
        {
            string parentKey = frontier.Dequeue();
            int parentDistance = distances[parentKey];
            if (maximumDepth is int bound && parentDistance >= bound)
                continue;

            if (!adjacency.TryGetValue(parentKey, out List<Relationship>? outgoing))
                continue;

            foreach (Relationship relationship in outgoing)
            {
                admitted.Add((relationship, parentDistance + 1));

                // A shared target, diamond, or cycle keeps every distinct relationship; only the
                // node's own first (minimum) discovery expands further.
                if (distances.TryAdd(relationship.TargetKey, parentDistance + 1))
                    frontier.Enqueue(relationship.TargetKey);
            }
        }

        ImmutableArray<RestoredProjectTraversalNode> nodes =
            [.. distances
                .OrderBy(entry => NodeRank(identities[entry.Key]))
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new RestoredProjectTraversalNode(
                    identities[entry.Key],
                    entry.Value,
                    identities[entry.Key] is RestoredProjectGraphParentIdentity.Project
                        ? spellings[entry.Key]
                        : null))];

        ImmutableArray<RestoredProjectTraversalProjectRelationship> projectRelationships =
            [.. admitted
                .Where(entry => entry.Relationship.ProjectRelationship is not null)
                .OrderBy(entry => entry.Relationship.ParentKey, StringComparer.Ordinal)
                .ThenBy(entry => entry.Relationship.TargetKey, StringComparer.Ordinal)
                .Select(entry =>
                {
                    RestoredProjectProjectRelationshipEvidence evidence = entry.Relationship.ProjectRelationship!;
                    return new RestoredProjectTraversalProjectRelationship(
                        new RestoredProjectProjectRelationshipIdentity(evidence.Parent, evidence.Dependency),
                        evidence.Parent,
                        evidence.Dependency,
                        spellings[entry.Relationship.TargetKey],
                        entry.Distance);
                })];

        ImmutableArray<RestoredProjectTraversalPackageRelationship> packageRelationships =
            [.. admitted
                .Where(entry => entry.Relationship.PackageEdge is not null)
                .OrderBy(entry => entry.Relationship.ParentKey, StringComparer.Ordinal)
                .ThenBy(
                    entry => entry.Relationship.PackageEdge!.Dependency.Coordinate.PackageId,
                    StringComparer.Ordinal)
                .ThenBy(
                    entry => entry.Relationship.PackageEdge!.Dependency.Coordinate.Version,
                    StringComparer.Ordinal)
                .Select(entry => new RestoredProjectTraversalPackageRelationship(
                    entry.Relationship.PackageEdge!,
                    entry.Distance))];

        ImmutableArray<RestoredProjectGraphFailure> failures =
            AdmittedFailures(topology, distances, maximumDepth);

        ImmutableArray<RestoredProjectTraversalDepthBoundary> depthBoundaries = maximumDepth is int depth
            ? [.. distances
                .Where(entry => entry.Value == depth
                    && (adjacency.ContainsKey(entry.Key) || failureOwners.Contains(entry.Key)))
                .OrderBy(entry => NodeRank(identities[entry.Key]))
                .ThenBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new RestoredProjectTraversalDepthBoundary(identities[entry.Key], depth))]
            : [];

        RestoredProjectTraversalCompletion completion = !failures.IsEmpty
            ? RestoredProjectTraversalCompletion.Partial
            : depthBoundaries.IsEmpty
                ? RestoredProjectTraversalCompletion.Complete
                : RestoredProjectTraversalCompletion.DepthBounded;

        var identity = new RestoredProjectTraversalIdentity(
            facts.SelectionIdentity,
            ComputeTopologyDigest(
                maximumDepth,
                nodes,
                projectRelationships,
                packageRelationships,
                depthBoundaries,
                failures,
                completion));

        return new RestoredProjectDependencyTraversal(
            facts,
            identity,
            maximumDepth,
            nodes,
            projectRelationships,
            packageRelationships,
            depthBoundaries,
            failures,
            completion);
    }

    static int NodeRank(RestoredProjectGraphParentIdentity identity) => identity switch
    {
        RestoredProjectGraphParentIdentity.Root => 0,
        RestoredProjectGraphParentIdentity.Project => 1,
        RestoredProjectGraphParentIdentity.Package => 2,
        _ => throw new InvalidOperationException(
            $"Unknown restored-project node: {identity.GetType().FullName}"),
    };

    /// <summary>
    /// Selects the graph-phase failure occurrences that lie inside the admitted depth. A failure
    /// belongs to the expansion of its owning node, so it is in scope exactly when a relationship
    /// from that node would have been admitted. An unbounded traversal answers over the whole
    /// selected graph and therefore retains every occurrence.
    /// </summary>
    static ImmutableArray<RestoredProjectGraphFailure> AdmittedFailures(
        RestoredProjectGraphTopology topology,
        Dictionary<string, int> distances,
        int? maximumDepth)
    {
        var counts = new Dictionary<RestoredProjectGraphFailureReason, int>();
        void Record(RestoredProjectGraphFailureReason reason) =>
            counts[reason] = counts.TryGetValue(reason, out int count) ? count + 1 : 1;

        foreach (RestoredProjectGraphFailureOccurrence occurrence in topology.FailureOccurrences)
        {
            if (occurrence.OwnerNodeKey is not { } owner || maximumDepth is not int bound)
            {
                Record(occurrence.Reason);
                continue;
            }

            if (distances.TryGetValue(owner, out int distance) && distance < bound)
                Record(occurrence.Reason);
        }

        if (topology.RelationshipLimitExceeded)
            Record(RestoredProjectGraphFailureReason.ConfiguredLimitExceeded);

        return
            [.. counts
                .OrderBy(entry => entry.Key, Comparer<RestoredProjectGraphFailureReason>.Default)
                .Select(entry => new RestoredProjectGraphFailure(entry.Key, entry.Value))];
    }

    /// <summary>
    /// A typed, length-prefixed canonical encoding of the admitted topology. Every text field is
    /// written as its UTF-16 length, a separator, and the field itself, and every collection is
    /// preceded by its element count, so no identity segment can imitate a field or collection
    /// boundary. Only canonical or opaque identity currency participates: no authored spelling,
    /// local path, or JSON property position reaches the digest.
    /// </summary>
    static string ComputeTopologyDigest(
        int? maximumDepth,
        ImmutableArray<RestoredProjectTraversalNode> nodes,
        ImmutableArray<RestoredProjectTraversalProjectRelationship> projectRelationships,
        ImmutableArray<RestoredProjectTraversalPackageRelationship> packageRelationships,
        ImmutableArray<RestoredProjectTraversalDepthBoundary> depthBoundaries,
        ImmutableArray<RestoredProjectGraphFailure> failures,
        RestoredProjectTraversalCompletion completion)
    {
        var text = new StringBuilder();
        Field(text, "rpdt/1");
        Count(text, maximumDepth ?? -1);

        Count(text, nodes.Length);
        foreach (RestoredProjectTraversalNode node in nodes)
        {
            Count(text, NodeRank(node.Identity));
            Field(text, RestoredProjectNodeKey.For(node.Identity));
            Count(text, node.MinimumDistance);
        }

        Count(text, projectRelationships.Length);
        foreach (RestoredProjectTraversalProjectRelationship relationship in projectRelationships)
        {
            Field(text, RestoredProjectNodeKey.For(relationship.Parent));
            Field(text, RestoredProjectNodeKey.ForProject(relationship.Dependency));
            Count(text, relationship.Distance);
        }

        Count(text, packageRelationships.Length);
        foreach (RestoredProjectTraversalPackageRelationship relationship in packageRelationships)
        {
            RestoredProjectGraphEdge edge = relationship.Edge;
            Field(text, RestoredProjectNodeKey.For(edge.Parent));
            Field(text, RestoredProjectNodeKey.ForPackage(edge.Dependency));
            Field(text, edge.CanonicalVersionConstraint);
            Count(text, (int)edge.Role);
            Field(text, edge.DeclarationAssociation?.PivotIdentity ?? "");
            Count(text, relationship.Distance);
        }

        Count(text, depthBoundaries.Length);
        foreach (RestoredProjectTraversalDepthBoundary boundary in depthBoundaries)
        {
            Field(text, RestoredProjectNodeKey.For(boundary.Node));
            Count(text, boundary.MaximumDepth);
        }

        Count(text, failures.Length);
        foreach (RestoredProjectGraphFailure failure in failures)
        {
            Count(text, (int)failure.Reason);
            Count(text, failure.Count);
        }

        Count(text, (int)completion);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    static void Field(StringBuilder text, string value) =>
        text.Append(value.Length.ToString(CultureInfo.InvariantCulture))
            .Append(':')
            .Append(value)
            .Append(';');

    static void Count(StringBuilder text, int value) =>
        text.Append('#').Append(value.ToString(CultureInfo.InvariantCulture)).Append(';');
}
