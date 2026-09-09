using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Output;
using DotnetInspector.Services;
using ILInspector.Metadata;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Tests;

public class DependencyGraphOutputAdapterTests
{
    [Fact]
    public void TypeProjection_PreservesSharedTargetEdges()
    {
        var result = new TypeDependencyResult(
            "Graph.IDiamond",
            [])
        {
            Relationships =
            [
                Relationship("Graph.IDiamond", "Graph.ILeft", 0),
                Relationship("Graph.ILeft", "Graph.IShared", 1),
                Relationship("Graph.IDiamond", "Graph.IRight", 0),
                Relationship("Graph.IRight", "Graph.IShared", 1),
            ],
        };

        DependencyGraphDocument document =
            DependencyGraphProjection.FromType(result);

        Assert.Equal(4, document.Edges.Length);
        DependencyGraphNode shared = Assert.Single(
            document.Nodes,
            node => node.Label == "Graph.IShared");
        Assert.Equal(
            2,
            document.Edges.Count(
                edge => edge.ToNodeId == shared.Id));
    }

    [Fact]
    public void LibraryProjection_ReconstructsEverySharedReferenceEdge()
    {
        string directory = Path.GetTempPath();
        var graph = new LibraryDependencyGraphResult.Graph(
            "Root",
            Path.Combine(directory, "Root.dll"),
            [
                Reference("Left", directory, depth: 0),
                Reference("Shared", directory, depth: 1),
                Reference("Right", directory, depth: 0),
                Reference("Shared", directory, depth: 1),
            ]);

        DependencyGraphDocument document =
            DependencyGraphProjection.FromLibrary(graph);

        Assert.Equal(4, document.Edges.Length);
        DependencyGraphNode shared = Assert.Single(
            document.Nodes,
            node => node.Label.StartsWith(
                "Shared ",
                StringComparison.Ordinal));
        Assert.Equal(
            2,
            document.Edges.Count(
                edge => edge.ToNodeId == shared.Id));
    }

    [Fact]
    public void PackageProjection_PreservesSharedTargetAndRoot()
    {
        var shared = new DependencyNode(
            "Shared",
            "1.0.0",
            null,
            []);
        var graph = new PackageDependencyGraphResult.Graph(
            "Root",
            "1.0.0",
            "Root",
            "1.0.0",
            [
                new DependencyNode("Left", "1.0.0", null, [shared]),
                new DependencyNode("Right", "1.0.0", null, [shared]),
            ]);

        DependencyGraphDocument document =
            DependencyGraphProjection.FromPackage(graph);

        Assert.Equal("Root 1.0.0", document.Nodes[document.RootNodeId].Label);
        Assert.Equal(4, document.Edges.Length);
        DependencyGraphNode sharedNode = Assert.Single(
            document.Nodes,
            node => node.Label == "Shared 1.0.0");
        Assert.Equal(
            2,
            document.Edges.Count(
                edge => edge.ToNodeId == sharedNode.Id));
    }

    [Fact]
    public void SelectedEdgeSequence_DrivesRowsGraphsAndJson()
    {
        DependencyGraphDocument document = FourEdgeDocument();
        IReadOnlyList<DependencyGraphEdge> selected =
            RowWindow.Apply(
                RowWindow.Head(2),
                document.Edges);

        IReadOnlyList<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(
                document,
                selected);
        Markout.Graph graph =
            DependencyGraphOutputAdapter.ToGraph(
                document,
                selected);
        DependencyGraphJsonDocument json =
            DependencyGraphOutputAdapter.Select(
                document,
                selected);

        Assert.Equal([0, 1], rows.Select(row => row.EdgeId));
        Assert.Equal(2, graph.Edges.Count());
        Assert.Equal([0, 1], json.Edges.Select(edge => edge.Id));
    }

    [Fact]
    public void MermaidGraph_IncludesExplicitRootAndSelectedEdges()
    {
        DependencyGraphDocument document = FourEdgeDocument();
        var writer = MarkoutWriter.Create(new MermaidFormatter());
        writer.WriteGraph(
            DependencyGraphOutputAdapter.ToGraph(document));

        string mermaid = writer.ToString();

        Assert.Contains("Root", mermaid, StringComparison.Ordinal);
        Assert.Contains("Left", mermaid, StringComparison.Ordinal);
        Assert.Contains("Right", mermaid, StringComparison.Ordinal);
        Assert.Contains("Shared", mermaid, StringComparison.Ordinal);
        Assert.Equal(
            document.Edges.Length,
            mermaid.Split(
                "-->",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void PlainTextGraph_MarksSharedTargetAsRevisit()
    {
        DependencyGraphDocument document = FourEdgeDocument();
        var writer = MarkoutWriter.Create(new PlainTextFormatter());
        writer.WriteGraph(
            DependencyGraphOutputAdapter.ToGraph(document));

        string text = writer.ToString();

        Assert.Contains(
            "(revisit) Shared",
            text,
            StringComparison.Ordinal);
    }

    private static TypeDependencyRelationship Relationship(
        string source,
        string target,
        int depth) =>
        new(
            source,
            target,
            TypeDependencyRelationshipKind.Interface,
            depth,
            TargetResolved: true);

    private static AssemblyReferenceNode Reference(
        string name,
        string directory,
        int depth) =>
        new()
        {
            Name = name,
            Version = "1.0.0.0",
            PublicKeyToken = "0000000000000000",
            Path = Path.Combine(directory, $"{name}.dll"),
            Depth = depth,
        };

    private static DependencyGraphDocument FourEdgeDocument()
    {
        var builder = new DependencyGraphBuilder(
            "Root",
            DependencyGraphNodeKind.Package,
            "root",
            "Root");
        int left = builder.AddNode(
            DependencyGraphNodeKind.Package,
            "left",
            "Left",
            DependencyGraphResolutionState.Resolved);
        int right = builder.AddNode(
            DependencyGraphNodeKind.Package,
            "right",
            "Right",
            DependencyGraphResolutionState.Resolved);
        int shared = builder.AddNode(
            DependencyGraphNodeKind.Package,
            "shared",
            "Shared",
            DependencyGraphResolutionState.Resolved);
        builder.AddEdge(builder.RootNodeId, left, "package dependency", 0);
        builder.AddEdge(left, shared, "package dependency", 1);
        builder.AddEdge(builder.RootNodeId, right, "package dependency", 0);
        builder.AddEdge(right, shared, "package dependency", 1);
        return builder.Build();
    }
}
