using System.Collections.Immutable;

using ILInspector.Analysis;
using ILInspector.CallGraph;
using DotnetInspector.Queries;

namespace DotnetInspector.Sections;

public sealed record MemberCallGraphTableRow(
    CallGraphRow Relationship,
    CallGraphNode From,
    CallGraphNode To);

public sealed record MemberCallGraphTable(
    CallGraphNode Focus,
    ImmutableArray<CallGraphNode> Nodes,
    ImmutableArray<MemberCallGraphTableRow> Rows,
    bool HasLoopEdges,
    CatalogCallGraphDiagnostics Diagnostics,
    bool HasUnexploredTraversalBoundary,
    bool HasAnalysisFailureBoundary);

public sealed record MemberCallGraphHierarchy(
    CallGraphNode Focus,
    ImmutableArray<CallGraphNode> Nodes,
    ImmutableArray<CallGraphRow> Rows,
    CatalogCallGraphDiagnostics Diagnostics,
    bool HasUnexploredTraversalBoundary,
    bool HasAnalysisFailureBoundary);

public sealed record MemberCallGraphHierarchyNode(
    CallGraphNode Member,
    CallGraphRow? Relationship,
    bool IsReference);

public static partial class MemberCallGraphInspection
{
    public static InspectionEnvelope<InspectionGraphDocument> Document(
        CallGraphProjection projection,
        CatalogCallGraphDiagnostics? diagnostics = null) =>
        Execute(CallGraphInspectionGraphAdapter.Create(
            projection,
            diagnostics ?? CatalogCallGraphDiagnostics.Empty));

    public static InspectionEnvelope<MemberCallGraphTable> Table(
        CallGraphProjection projection,
        IReadOnlyList<CallGraphRow>? rows = null,
        CatalogCallGraphDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return new(
            new(
                projection.Focus,
                projection.Nodes,
                [
                    .. (rows ?? projection.Rows).Select(row =>
                        new MemberCallGraphTableRow(
                            row,
                            projection.Nodes[row.Edge.From],
                            projection.Nodes[row.Edge.To])),
                ],
                projection.Edges.Any(static edge => edge.AnyCallInLoop),
                diagnostics ?? CatalogCallGraphDiagnostics.Empty,
                projection.HasUnexploredTraversalBoundary,
                projection.HasAnalysisFailureBoundary),
            Share,
            []);
    }

    public static InspectionEnvelope<MemberCallGraphHierarchy> Hierarchy(
        CallGraphProjection projection,
        IReadOnlyList<CallGraphRow>? rows = null,
        CatalogCallGraphDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ImmutableArray<CallGraphRow> selected = [.. rows ?? projection.Rows];
        HashSet<int> nodeIds = [projection.Focus.Id];
        foreach (CallGraphRow row in selected)
        {
            nodeIds.Add(row.Edge.From);
            nodeIds.Add(row.Edge.To);
        }
        return new(
            new(
                projection.Focus,
                [.. projection.Nodes.Where(node => nodeIds.Contains(node.Id))],
                selected,
                diagnostics ?? CatalogCallGraphDiagnostics.Empty,
                projection.HasUnexploredTraversalBoundary,
                projection.HasAnalysisFailureBoundary),
            Share,
            []);
    }
}

public static class MemberCallGraphHierarchyProjection
{
    public static void Write(
        MemberCallGraphHierarchy hierarchy,
        IInspectionHierarchySink<MemberCallGraphHierarchyNode> sink)
    {
        ArgumentNullException.ThrowIfNull(hierarchy);
        ArgumentNullException.ThrowIfNull(sink);
        if (hierarchy.Rows.IsEmpty)
            return;

        Dictionary<int, CallGraphNode> nodes =
            hierarchy.Nodes.ToDictionary(static node => node.Id);
        ILookup<int, CallGraphRow> outgoing =
            hierarchy.Rows.ToLookup(static row => row.Edge.From);
        var visited = new HashSet<int>();
        var roots = new List<CallGraphNode>();
        AddRoot(hierarchy.Focus);
        foreach (CallGraphNode node in hierarchy.Nodes)
            AddRoot(node);

        visited.Clear();
        for (int index = 0; index < roots.Count; index++)
        {
            WriteNode(
                roots[index],
                relationship: null,
                isLastSibling: index == roots.Count - 1,
                sink);
        }

        void AddRoot(CallGraphNode root)
        {
            if (!visited.Add(root.Id))
                return;
            roots.Add(root);
            var pending = new Stack<int>();
            pending.Push(root.Id);
            while (pending.TryPop(out int nodeId))
            {
                foreach (CallGraphRow row in outgoing[nodeId])
                {
                    if (visited.Add(row.Edge.To))
                        pending.Push(row.Edge.To);
                }
            }
        }

        void WriteNode(
            CallGraphNode node,
            CallGraphRow? relationship,
            bool isLastSibling,
            IInspectionHierarchySink<MemberCallGraphHierarchyNode> destination)
        {
            bool isReference = !visited.Add(node.Id);
            CallGraphRow[] children =
                isReference ? [] : [.. outgoing[node.Id]];
            destination.WriteNode(
                new(node, relationship, isReference),
                isLastSibling,
                children.Length == 0
                    ? null
                    : childSink =>
                    {
                        for (int index = 0; index < children.Length; index++)
                        {
                            CallGraphRow child = children[index];
                            WriteNode(
                                nodes[child.Edge.To],
                                child,
                                index == children.Length - 1,
                                childSink);
                        }
                    });
        }
    }
}
