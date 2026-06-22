using DotnetInspector.BuildEvents.Events;

namespace DotnetInspector.BuildEvents.Projections;

public sealed record BuildGraphModel(
    IReadOnlyList<BuildGraphNode> Nodes,
    IReadOnlyList<BuildGraphEdge> Edges)
{
    public static BuildGraphModel Create(BuildEventLog log)
    {
        List<BuildGraphNode> nodes = [];
        Dictionary<LogicalProjectKey, string> logicalNodeIds = [];
        Dictionary<BuildContextKey, string> executionNodeIds = [];
        List<BuildGraphEdge> edges = [];

        foreach (ProjectEvent project in log.Projects)
        {
            LogicalProjectKey logicalKey = new(
                project.ProjectFile,
                project.Dimensions.TargetFramework,
                project.Dimensions.RuntimeIdentifier,
                project.Dimensions.Configuration);

            if (!logicalNodeIds.TryGetValue(logicalKey, out string? nodeId))
            {
                nodeId = "p" + (nodes.Count + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                logicalNodeIds[logicalKey] = nodeId;
                nodes.Add(new BuildGraphNode(
                    nodeId,
                    project.ProjectFile,
                    project.Dimensions.TargetFramework,
                    project.Dimensions.RuntimeIdentifier,
                    project.Dimensions.Configuration));
            }

            executionNodeIds[project.Context.ToKey()] = nodeId;

            if (executionNodeIds.TryGetValue(project.ParentContextKey, out string? parentNodeId)
                && !string.Equals(parentNodeId, nodeId, StringComparison.Ordinal))
            {
                edges.Add(new BuildGraphEdge(parentNodeId, nodeId));
            }
        }

        return new BuildGraphModel(nodes, edges.Distinct().ToList());
    }

    private readonly record struct LogicalProjectKey(string? ProjectFile, string? TargetFramework, string? RuntimeIdentifier, string? Configuration);
}

public sealed record BuildGraphNode(
    string Id,
    string? ProjectFile,
    string? TargetFramework,
    string? RuntimeIdentifier,
    string? Configuration);

public sealed record BuildGraphEdge(string From, string To);
