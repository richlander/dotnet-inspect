using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace DotnetInspector.Sections;

[JsonConverter(typeof(DependencyHierarchyOccurrenceIdentityJsonConverter))]
public readonly record struct DependencyHierarchyOccurrenceIdentity(
    DependencyRootOccurrenceIdentity RootOccurrence,
    int Value);

sealed class DependencyHierarchyOccurrenceIdentityJsonConverter
    : JsonConverter<DependencyHierarchyOccurrenceIdentity>
{
    public override DependencyHierarchyOccurrenceIdentity Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException(
                $"{nameof(DependencyHierarchyOccurrenceIdentity)} must be an object.");
        }

        DependencyRootOccurrenceIdentity rootOccurrence = default;
        int value = 0;
        int seenProperties = 0;
        string rootOccurrenceName = JsonName(
            options,
            nameof(DependencyHierarchyOccurrenceIdentity.RootOccurrence));
        string valueName = JsonName(
            options,
            nameof(DependencyHierarchyOccurrenceIdentity.Value));

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException(
                    $"{nameof(DependencyHierarchyOccurrenceIdentity)} requires property names.");
            }

            string propertyName = reader.GetString()!;
            if (!reader.Read())
            {
                throw new JsonException(
                    $"{nameof(DependencyHierarchyOccurrenceIdentity)} is incomplete.");
            }

            if (PropertyMatches(propertyName, rootOccurrenceName, options))
            {
                ObserveProperty(ref seenProperties, 1 << 0, options);
                var typeInfo =
                    (JsonTypeInfo<DependencyRootOccurrenceIdentity>)
                    options.GetTypeInfo(
                        typeof(DependencyRootOccurrenceIdentity));
                rootOccurrence =
                    JsonSerializer.Deserialize(ref reader, typeInfo);
            }
            else if (PropertyMatches(propertyName, valueName, options))
            {
                ObserveProperty(ref seenProperties, 1 << 1, options);
                value = reader.GetInt32();
            }
            else if (options.UnmappedMemberHandling
                == JsonUnmappedMemberHandling.Disallow)
            {
                throw new JsonException(
                    $"{nameof(DependencyHierarchyOccurrenceIdentity)} contains an unknown property.");
            }
            else
            {
                reader.Skip();
            }
        }

        if (reader.TokenType != JsonTokenType.EndObject)
        {
            throw new JsonException(
                $"{nameof(DependencyHierarchyOccurrenceIdentity)} is incomplete.");
        }

        return new(rootOccurrence, value);
    }

    public override void Write(
        Utf8JsonWriter writer,
        DependencyHierarchyOccurrenceIdentity value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName(
            JsonName(
                options,
                nameof(DependencyHierarchyOccurrenceIdentity.RootOccurrence)));
        var typeInfo =
            (JsonTypeInfo<DependencyRootOccurrenceIdentity>)
            options.GetTypeInfo(typeof(DependencyRootOccurrenceIdentity));
        JsonSerializer.Serialize(writer, value.RootOccurrence, typeInfo);
        writer.WriteNumber(
            JsonName(
                options,
                nameof(DependencyHierarchyOccurrenceIdentity.Value)),
            value.Value);
        writer.WriteEndObject();
    }

    private static string JsonName(
        JsonSerializerOptions options,
        string name) =>
        options.PropertyNamingPolicy?.ConvertName(name) ?? name;

    private static bool PropertyMatches(
        string actual,
        string expected,
        JsonSerializerOptions options) =>
        string.Equals(
            actual,
            expected,
            options.PropertyNameCaseInsensitive
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);

    private static void ObserveProperty(
        ref int seenProperties,
        int property,
        JsonSerializerOptions options)
    {
        if (!options.AllowDuplicateProperties
            && (seenProperties & property) != 0)
        {
            throw new JsonException(
                $"{nameof(DependencyHierarchyOccurrenceIdentity)} contains a duplicate property.");
        }

        seenProperties |= property;
    }
}

public enum DependencyHierarchyOccurrenceDisposition
{
    Expanded,
    Revisit,
    Cycle,
}

public sealed record DependencyHierarchyRootOccurrence(
    DependencyHierarchyOccurrenceIdentity Identity,
    int RootPosition,
    int NodeId,
    int Depth)
{
    public DependencyRootOccurrenceIdentity RootOccurrence =>
        Identity.RootOccurrence;
}

public sealed record DependencyHierarchyOccurrence(
    DependencyHierarchyOccurrenceIdentity Identity,
    DependencyHierarchyOccurrenceIdentity ParentIdentity,
    int TargetNodeId,
    int IncomingEdgeId,
    int Depth,
    DependencyHierarchyOccurrenceDisposition Disposition)
{
    public DependencyRootOccurrenceIdentity RootOccurrence =>
        Identity.RootOccurrence;
}

public sealed record DependencyHierarchyDocument(
    DependencyGraphDocument BackingGraph,
    ImmutableArray<DependencyHierarchyRootOccurrence> Roots,
    ImmutableArray<DependencyHierarchyOccurrence> Occurrences)
{
    public static DependencyHierarchyDocument Empty { get; } =
        new(
            new DependencyGraphDocument([], [], [], [], []),
            [],
            []);

    public static DependencyHierarchyDocument Create(
        DependencyGraphDocument graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ValidateGraph(graph);

        var outgoingBySource =
            new Dictionary<int, List<DependencyGraphEdge>>();
        var admittedEdgeIdsByRoot = graph.Roots.ToDictionary(
            static root => root.OccurrenceIndex,
            static _ => new HashSet<int>());
        foreach (DependencyGraphEdge edge in graph.Edges)
        {
            if (!outgoingBySource.TryGetValue(
                    edge.SourceNodeId,
                    out List<DependencyGraphEdge>? outgoing))
            {
                outgoing = [];
                outgoingBySource.Add(edge.SourceNodeId, outgoing);
            }
            outgoing.Add(edge);
            foreach (int rootOccurrence in edge.RootOccurrences)
                admittedEdgeIdsByRoot[rootOccurrence].Add(edge.Id);
        }
        Dictionary<int, int> rootPackageProjections =
            graph.PackageProjections
                .Where(static projection =>
                    projection.RootOccurrence is not null)
                .ToDictionary(
                    static projection => projection.RootOccurrence!.Value,
                    static projection => projection.Id);

        var roots =
            ImmutableArray.CreateBuilder<DependencyHierarchyRootOccurrence>(
                graph.Roots.Length);
        var occurrences =
            ImmutableArray.CreateBuilder<DependencyHierarchyOccurrence>();

        for (int rootPosition = 0;
             rootPosition < graph.Roots.Length;
             rootPosition++)
        {
            DependencyGraphRootOccurrence root = graph.Roots[rootPosition];
            var rootOccurrence = new DependencyRootOccurrenceIdentity(
                root.OccurrenceIndex);
            var rootIdentity = new DependencyHierarchyOccurrenceIdentity(
                rootOccurrence,
                Value: 0);
            roots.Add(
                new DependencyHierarchyRootOccurrence(
                    rootIdentity,
                    rootPosition,
                    root.NodeId,
                    Depth: 0));

            var emittedEdgeIds = new HashSet<int>();
            HashSet<int> admittedEdgeIds =
                admittedEdgeIdsByRoot[root.OccurrenceIndex];
            int? rootPackageProjectionId =
                rootPackageProjections.TryGetValue(
                    root.OccurrenceIndex,
                    out int projectionId)
                    ? projectionId
                    : null;
            var rootContext = new ExpansionContext(
                root.NodeId,
                rootPackageProjectionId);
            var expandedContexts = new HashSet<ExpansionContext>
            {
                rootContext,
            };
            var queue = new Queue<ExpansionFrame>();
            queue.Enqueue(
                new ExpansionFrame(
                    rootContext,
                    rootIdentity,
                    Depth: 0,
                    ImmutableHashSet.Create(rootContext)));
            int nextOccurrence = 1;

            while (queue.TryDequeue(out ExpansionFrame frame))
            {
                foreach (DependencyGraphEdge edge in Outgoing(
                    frame.Context.NodeId))
                {
                    if (!admittedEdgeIds.Contains(edge.Id)
                        || edge.SourcePackageProjectionId
                            != frame.Context.PackageProjectionId)
                    {
                        continue;
                    }

                    emittedEdgeIds.Add(edge.Id);
                    var identity =
                        new DependencyHierarchyOccurrenceIdentity(
                            rootOccurrence,
                            nextOccurrence++);
                    var targetContext = new ExpansionContext(
                        edge.TargetNodeId,
                        edge.TargetPackageProjectionId);
                    DependencyHierarchyOccurrenceDisposition disposition;
                    if (frame.Ancestors.Contains(targetContext))
                    {
                        disposition =
                            DependencyHierarchyOccurrenceDisposition.Cycle;
                    }
                    else if (!expandedContexts.Add(targetContext))
                    {
                        disposition =
                            DependencyHierarchyOccurrenceDisposition.Revisit;
                    }
                    else
                    {
                        disposition =
                            DependencyHierarchyOccurrenceDisposition.Expanded;
                    }

                    int depth = frame.Depth + 1;
                    occurrences.Add(
                        new DependencyHierarchyOccurrence(
                            identity,
                            frame.OccurrenceIdentity,
                            edge.TargetNodeId,
                            edge.Id,
                            depth,
                            disposition));

                    if (disposition
                        == DependencyHierarchyOccurrenceDisposition.Expanded)
                    {
                        queue.Enqueue(
                            new ExpansionFrame(
                                targetContext,
                                identity,
                                depth,
                                frame.Ancestors.Add(targetContext)));
                    }
                }
            }

            int[] unreachableEdgeIds =
            [
                .. graph.Edges
                    .Where(edge =>
                        admittedEdgeIds.Contains(edge.Id)
                        && !emittedEdgeIds.Contains(edge.Id))
                    .Select(static edge => edge.Id),
            ];
            if (unreachableEdgeIds.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Dependency graph root occurrence {root.OccurrenceIndex} admits unreachable edges: {string.Join(", ", unreachableEdgeIds)}.");
            }
        }

        return new DependencyHierarchyDocument(
            graph,
            roots.ToImmutable(),
            occurrences.ToImmutable());

        IReadOnlyList<DependencyGraphEdge> Outgoing(int nodeId) =>
            outgoingBySource.TryGetValue(
                nodeId,
                out List<DependencyGraphEdge>? outgoing)
                ? outgoing
                : [];
    }

    public bool Equals(DependencyHierarchyDocument? other) =>
        ReferenceEquals(this, other)
        || other is not null
        && BackingGraph == other.BackingGraph
        && DependencyValueEquality.SequenceEqual(Roots, other.Roots)
        && DependencyValueEquality.SequenceEqual(
            Occurrences,
            other.Occurrences);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(BackingGraph);
        DependencyValueEquality.AddSequenceHashCode(ref hash, Roots);
        DependencyValueEquality.AddSequenceHashCode(ref hash, Occurrences);
        return hash.ToHashCode();
    }

    private static void ValidateGraph(DependencyGraphDocument graph)
    {
        if (graph.Roots.IsDefault
            || graph.Nodes.IsDefault
            || graph.Edges.IsDefault
            || graph.PackageProjections.IsDefault
            || graph.DepthBoundaries.IsDefault)
        {
            throw new InvalidOperationException(
                "Dependency graph arrays must be initialized.");
        }

        for (int index = 0; index < graph.Nodes.Length; index++)
        {
            if (graph.Nodes[index].Id != index)
            {
                throw new InvalidOperationException(
                    $"Dependency graph node {graph.Nodes[index].Id} does not match its document position {index}.");
            }
        }

        var rootOccurrences = new HashSet<int>();
        var rootsByOccurrence =
            new Dictionary<int, DependencyGraphRootOccurrence>();
        foreach (DependencyGraphRootOccurrence root in graph.Roots)
        {
            ValidateNodeId(graph, root.NodeId, "root");
            if (!rootOccurrences.Add(root.OccurrenceIndex))
            {
                throw new InvalidOperationException(
                    $"Dependency graph root occurrence {root.OccurrenceIndex} is duplicated.");
            }
            rootsByOccurrence.Add(root.OccurrenceIndex, root);
        }

        var packageProjectionIds = new HashSet<int>();
        var projectedRootOccurrences = new HashSet<int>();
        for (int index = 0; index < graph.PackageProjections.Length; index++)
        {
            DependencyGraphPackageProjection projection =
                graph.PackageProjections[index];
            if (!packageProjectionIds.Add(projection.Id))
            {
                throw new InvalidOperationException(
                    $"Dependency graph package projection {projection.Id} is duplicated.");
            }
            if (projection.Id != index)
            {
                throw new InvalidOperationException(
                    $"Dependency graph package projection {projection.Id} does not match its document position {index}.");
            }
            ValidateNodeId(graph, projection.NodeId, "package projection");
            if (projection.RootOccurrence is not { } rootOccurrence)
                continue;

            if (!rootsByOccurrence.TryGetValue(
                rootOccurrence,
                out DependencyGraphRootOccurrence? root))
            {
                throw new InvalidOperationException(
                    $"Dependency graph package projection {projection.Id} names unknown root occurrence {rootOccurrence}.");
            }
            if (!projectedRootOccurrences.Add(rootOccurrence))
            {
                throw new InvalidOperationException(
                    $"Dependency graph root occurrence {rootOccurrence} has more than one package projection.");
            }
            if (projection.NodeId != root.NodeId)
            {
                throw new InvalidOperationException(
                    $"Dependency graph package projection {projection.Id} does not match root occurrence {rootOccurrence}'s node.");
            }
        }

        var edgeIds = new HashSet<int>();
        foreach (DependencyGraphEdge edge in graph.Edges)
        {
            if (!edgeIds.Add(edge.Id))
            {
                throw new InvalidOperationException(
                    $"Dependency graph edge {edge.Id} is duplicated.");
            }
            ValidateNodeId(graph, edge.SourceNodeId, "edge source");
            ValidateNodeId(graph, edge.TargetNodeId, "edge target");
            ValidatePackageProjection(
                graph,
                edge.SourcePackageProjectionId,
                edge.SourceNodeId,
                edge.Id,
                "source");
            ValidatePackageProjection(
                graph,
                edge.TargetPackageProjectionId,
                edge.TargetNodeId,
                edge.Id,
                "target");
            if (edge.RootOccurrences.IsDefault)
            {
                throw new InvalidOperationException(
                    $"Dependency graph edge {edge.Id} has an uninitialized root-occurrence set.");
            }
            if (edge.RootOccurrences.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"Dependency graph edge {edge.Id} is not admitted by any root occurrence.");
            }

            var edgeRootOccurrences = new HashSet<int>();
            foreach (int rootOccurrence in edge.RootOccurrences)
            {
                if (!edgeRootOccurrences.Add(rootOccurrence))
                {
                    throw new InvalidOperationException(
                        $"Dependency graph edge {edge.Id} duplicates root occurrence {rootOccurrence}.");
                }
                if (!rootOccurrences.Contains(rootOccurrence))
                {
                    throw new InvalidOperationException(
                        $"Dependency graph edge {edge.Id} names unknown root occurrence {rootOccurrence}.");
                }
            }
        }

        foreach (DependencyGraphDepthBoundary boundary in
                 graph.DepthBoundaries)
        {
            ValidateNodeId(graph, boundary.NodeId, "depth boundary");
            ValidateDepthBoundaryPackageProjection(graph, boundary);
            if (boundary.RootOccurrences.IsEmpty)
            {
                throw new InvalidOperationException(
                    $"Dependency graph depth boundary for node {boundary.NodeId} is not associated with any root occurrence.");
            }

            var boundaryRootOccurrences = new HashSet<int>();
            foreach (int rootOccurrence in boundary.RootOccurrences)
            {
                if (!boundaryRootOccurrences.Add(rootOccurrence))
                {
                    throw new InvalidOperationException(
                        $"Dependency graph depth boundary for node {boundary.NodeId} duplicates root occurrence {rootOccurrence}.");
                }
                if (!rootOccurrences.Contains(rootOccurrence))
                {
                    throw new InvalidOperationException(
                        $"Dependency graph depth boundary for node {boundary.NodeId} names unknown root occurrence {rootOccurrence}.");
                }
            }
        }
    }

    private static void ValidateDepthBoundaryPackageProjection(
        DependencyGraphDocument graph,
        DependencyGraphDepthBoundary boundary)
    {
        if (boundary.PackageProjectionId is not { } projectionId)
            return;

        if ((uint)projectionId >= (uint)graph.PackageProjections.Length)
        {
            throw new InvalidOperationException(
                $"Dependency graph depth boundary for node {boundary.NodeId} names package projection {projectionId} outside the package projection table.");
        }
        if (graph.PackageProjections[projectionId].NodeId != boundary.NodeId)
        {
            throw new InvalidOperationException(
                $"Dependency graph depth boundary package projection {projectionId} does not match node {boundary.NodeId}.");
        }
    }

    private static void ValidatePackageProjection(
        DependencyGraphDocument graph,
        int? packageProjectionId,
        int nodeId,
        int edgeId,
        string role)
    {
        if (packageProjectionId is not { } projectionId)
            return;

        if ((uint)projectionId >= (uint)graph.PackageProjections.Length)
        {
            throw new InvalidOperationException(
                $"Dependency graph edge {edgeId} {role} package projection {projectionId} is outside the package projection table.");
        }
        if (graph.PackageProjections[projectionId].NodeId != nodeId)
        {
            throw new InvalidOperationException(
                $"Dependency graph edge {edgeId} {role} package projection {projectionId} does not match node {nodeId}.");
        }
    }

    private static void ValidateNodeId(
        DependencyGraphDocument graph,
        int nodeId,
        string role)
    {
        if ((uint)nodeId >= (uint)graph.Nodes.Length)
        {
            throw new InvalidOperationException(
                $"Dependency graph {role} node {nodeId} is outside the node table.");
        }
    }

    private readonly record struct ExpansionContext(
        int NodeId,
        int? PackageProjectionId);

    private readonly record struct ExpansionFrame(
        ExpansionContext Context,
        DependencyHierarchyOccurrenceIdentity OccurrenceIdentity,
        int Depth,
        ImmutableHashSet<ExpansionContext> Ancestors);
}
