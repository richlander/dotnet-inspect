namespace DotnetInspector.Models;

internal enum DependencyGraphNodeKind
{
    Type,
    Library,
    Package,
}

internal enum DependencyGraphResolutionState
{
    Resolved,
    Unresolved,
    Rejected,
}

internal sealed record DependencyGraphNode(
    int Id,
    DependencyGraphNodeKind Kind,
    string Identity,
    string Label,
    DependencyGraphResolutionState Resolution);

internal sealed record DependencyGraphEdge(
    int Id,
    int[] RootNodeIds,
    int FromNodeId,
    int ToNodeId,
    string Relationship,
    int MinimumDepth);

internal sealed record DependencyGraphDocument(
    string Title,
    int[] RootNodeIds,
    DependencyGraphNode[] Nodes,
    DependencyGraphEdge[] Edges)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public int RootNodeId => RootNodeIds[0];
}

internal sealed record DependencyGraphJsonDocument(
    string Title,
    int[] RootNodeIds,
    DependencyGraphNode[] Nodes,
    DependencyGraphEdge[] Edges);

internal sealed class DependencyGraphBuilder
{
    private readonly Dictionary<(DependencyGraphNodeKind Kind, string Identity), int>
        _nodeIds = new();
    private readonly Dictionary<(int Source, int Target, string Relationship), int>
        _edgeIds = new();
    private readonly List<DependencyGraphNode> _nodes = [];
    private readonly List<DependencyGraphEdge> _edges = [];
    private readonly string _title;

    public DependencyGraphBuilder(
        string title,
        DependencyGraphNodeKind rootKind,
        string rootIdentity,
        string rootLabel)
    {
        _title = title;
        RootNodeId = AddNode(
            rootKind,
            rootIdentity,
            rootLabel,
            DependencyGraphResolutionState.Resolved);
    }

    public int RootNodeId { get; }

    public int AddNode(
        DependencyGraphNodeKind kind,
        string identity,
        string label,
        DependencyGraphResolutionState resolution)
    {
        var key = (kind, CanonicalIdentity(kind, identity));
        if (_nodeIds.TryGetValue(key, out int existing))
            return existing;

        int id = _nodes.Count;
        _nodeIds.Add(key, id);
        _nodes.Add(
            new DependencyGraphNode(
                id,
                kind,
                identity,
                label,
                resolution));
        return id;
    }

    public void AddEdge(
        int fromNodeId,
        int toNodeId,
        string relationship,
        int depth)
    {
        var key = (fromNodeId, toNodeId, relationship);
        if (_edgeIds.TryGetValue(key, out int existingId))
        {
            DependencyGraphEdge existing = _edges[existingId];
            if (depth < existing.MinimumDepth)
            {
                _edges[existingId] =
                    existing with { MinimumDepth = depth };
            }
            return;
        }

        int id = _edges.Count;
        _edgeIds.Add(key, id);
        _edges.Add(
            new DependencyGraphEdge(
                id,
                [RootNodeId],
                fromNodeId,
                toNodeId,
                relationship,
                depth));
    }

    public DependencyGraphDocument Build() =>
        new(_title, [RootNodeId], [.. _nodes], [.. _edges]);

    private static string CanonicalIdentity(
        DependencyGraphNodeKind kind,
        string identity) =>
        kind switch
        {
            DependencyGraphNodeKind.Type
                or DependencyGraphNodeKind.Package =>
                    identity.ToUpperInvariant(),
            DependencyGraphNodeKind.Library
                when OperatingSystem.IsWindows() =>
                    identity.ToUpperInvariant(),
            _ => identity,
        };
}
