using ILInspector.Analysis;
using ILInspector.CallGraph;
using ILInspector.Metadata;

namespace DotnetInspector.Sections.Tests;

public sealed class MemberCallGraphRepresentationTests
{
    [Fact]
    public void Hierarchy_RepresentsEveryDirectedEdgeWithCycleAndSharedNodeReferences()
    {
        CallGraphProjection projection = Pipeline();
        var hierarchy = MemberCallGraphInspection.Hierarchy(projection);
        var sink = new RecordingSink();

        MemberCallGraphHierarchyProjection.Write(hierarchy.Content, sink);

        Assert.Equal(projection.Rows, hierarchy.Content.Rows);
        Assert.Equal(
            projection.Rows.Select(static row => row.Number),
            sink.Events
                .Where(static item => item.Node.Relationship is not null)
                .Select(static item => item.Node.Relationship!.Value.Number)
                .Order());
        foreach (var item in sink.Events)
        {
            if (item.Node.Relationship is not { } relationship)
                continue;
            Assert.Equal(relationship.Edge.From, item.ParentId);
            Assert.Equal(relationship.Edge.To, item.Node.Member.Id);
        }
        Assert.Contains(sink.Events, item =>
            item.Node.IsReference && item.Node.Member.Member.Name == "Commit");
        Assert.Contains(sink.Events, item =>
            item.Node.IsReference && item.Node.Member.Member.Name == "Run");
        var roots = sink.Events.Where(static item => item.ParentId is null).ToArray();
        Assert.Equal(["Run", "Start"], roots.Select(static item => item.Node.Member.Member.Name));
        Assert.False(roots[0].IsLastSibling);
        Assert.True(roots[^1].IsLastSibling);
    }

    [Fact]
    public void StructuralEndpoints_PreserveSelectedRowsAndDisconnectedEdges()
    {
        CallGraphProjection projection = Pipeline();
        CallGraphRow[] rows =
        [
            .. projection.Rows.Where(row =>
                projection.Nodes[row.Edge.From].Member.Name is "Read" or "Start"),
        ];
        var table = MemberCallGraphInspection.Table(projection, rows);
        var hierarchy = MemberCallGraphInspection.Hierarchy(projection, rows);
        var document = MemberCallGraphInspection.Document(projection);
        var sink = new RecordingSink();

        MemberCallGraphHierarchyProjection.Write(hierarchy.Content, sink);

        Assert.Equal(rows, table.Content.Rows.Select(static row => row.Relationship));
        Assert.Equal(rows, hierarchy.Content.Rows);
        Assert.Equal(
            rows.Select(static row => row.Number).Order(),
            sink.Events
                .Where(static item => item.Node.Relationship is not null)
                .Select(static item => item.Node.Relationship!.Value.Number)
                .Order());
        Assert.Equal(
            ["Run", "Start", "Read"],
            sink.Events
                .Where(static item => item.ParentId is null)
                .Select(static item => item.Node.Member.Member.Name));
        Assert.DoesNotContain(hierarchy.Content.Nodes, static node => node.Member.Name == "Write");
        foreach (MemberCallGraphTableRow row in table.Content.Rows)
        {
            Assert.Same(projection.Nodes[row.Relationship.Edge.From], row.From);
            Assert.Same(projection.Nodes[row.Relationship.Edge.To], row.To);
            var edge = document.Content.Edges[row.Relationship.Number - 1];
            Assert.Equal(edge.FromNodeId, row.From.Id);
            Assert.Equal(edge.ToNodeId, row.To.Id);
        }
    }

    [Fact]
    public void EmptyWindow_DoesNotEraseTraversalFailureOrCatalogIncompleteness()
    {
        CallGraphProjection projection = CallGraphProjection.FromCallees(
            Node("Run", CallTreeStatus.Expanded,
                Node("Read", CallTreeStatus.DepthLimited),
                Node("Commit", CallTreeStatus.AnalysisIncomplete)));
        var diagnostics = new CatalogCallGraphDiagnostics(1, 2, 1);
        var hierarchy = MemberCallGraphInspection.Hierarchy(projection, [], diagnostics);
        var table = MemberCallGraphInspection.Table(projection, [], diagnostics);
        var sink = new RecordingSink();

        MemberCallGraphHierarchyProjection.Write(hierarchy.Content, sink);

        Assert.Empty(sink.Events);
        Assert.Empty(hierarchy.Content.Rows);
        Assert.Empty(table.Content.Rows);
        Assert.Same(diagnostics, hierarchy.Content.Diagnostics);
        Assert.Same(diagnostics, table.Content.Diagnostics);
        Assert.True(hierarchy.Content.HasUnexploredTraversalBoundary);
        Assert.True(hierarchy.Content.HasAnalysisFailureBoundary);
        Assert.True(table.Content.HasUnexploredTraversalBoundary);
        Assert.True(table.Content.HasAnalysisFailureBoundary);
    }

    static CallGraphProjection Pipeline() =>
        CallGraphProjection.Create(
            Node("Run", CallTreeStatus.Expanded,
                Node("Start", CallTreeStatus.Leaf)),
            Node("Run", CallTreeStatus.Expanded,
                Node("Read", CallTreeStatus.Expanded,
                    Node("Commit", CallTreeStatus.Expanded,
                        Node("Run", CallTreeStatus.AlreadyShown))),
                Node("Write", CallTreeStatus.Expanded,
                    Node("Commit", CallTreeStatus.AlreadyShown))));

    static CallTreeNode Node(
        string method,
        CallTreeStatus status,
        params CallTreeNode[] children) =>
        new(
            new MemberRef(
                TypeRef.Definition("Pipeline", "Pipeline", "Workflow"),
                method,
                [],
                TypeRef.CoreLib("System", "Void"),
                MemberKind.Method),
            null,
            status,
            [.. children]);

    sealed class RecordingSink : IInspectionHierarchySink<MemberCallGraphHierarchyNode>
    {
        int? _parentId;

        public List<(
            MemberCallGraphHierarchyNode Node,
            int? ParentId,
            bool IsLastSibling)> Events { get; } = [];

        public void WriteNode(
            MemberCallGraphHierarchyNode node,
            bool isLastSibling,
            Action<IInspectionHierarchySink<MemberCallGraphHierarchyNode>>? writeChildren = null)
        {
            Events.Add((node, _parentId, isLastSibling));
            int? parentId = _parentId;
            _parentId = node.Member.Id;
            writeChildren?.Invoke(this);
            _parentId = parentId;
        }
    }
}
