using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspect.Cli.Views;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class DependencyHierarchyOutputAdapterTests
{
    [Fact]
    public async Task SharedDagPreservesParentOccurrencesAcrossSinks()
    {
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(SharedDag());
        List<DependencyHierarchyOccurrenceRow> rows =
            DependencyHierarchyOutputAdapter.Rows(document);

        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);
        string mermaid = await RenderAsync(
            document,
            rows,
            OutputFormat.Mermaid);
        string table = await RenderAsync(
            document,
            rows,
            OutputFormat.Table);
        string json = await RenderAsync(
            document,
            rows,
            OutputFormat.Json);
        string jsonLines = await RenderAsync(
            document,
            rows,
            OutputFormat.Jsonl);

        Assert.Equal(5, rows.Count);
        Assert.Equal(2, Occurrences(tree, "Shared"));
        Assert.Contains("(revisit) Shared", tree, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(tree, "Leaf"));
        Assert.Equal(5, MermaidEdgeCount(mermaid));
        Assert.Equal(5, DataLineCount(table));
        Assert.Equal(5, NonEmptyLineCount(jsonLines));

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Single(parsed.RootElement.GetProperty("roots").EnumerateArray());
        JsonElement[] occurrences =
        [
            .. parsed.RootElement.GetProperty("occurrences")
                .EnumerateArray(),
        ];
        Assert.Equal(5, occurrences.Length);
        Assert.Single(
            occurrences,
            static occurrence =>
                occurrence.GetProperty("disposition").GetString()
                == "Revisit");
    }

    [Fact]
    public async Task MultipleRootsRetainIndependentSharedRelationships()
    {
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(MultiRootShared());
        List<DependencyHierarchyOccurrenceRow> rows =
            DependencyHierarchyOutputAdapter.Rows(document);

        string json = await RenderAsync(
            document,
            rows,
            OutputFormat.Json);

        Assert.Equal(4, rows.Count);
        Assert.Equal(2, rows.Count(static row => row.EdgeId == 2));
        Assert.Equal(
            [1, 2],
            rows.Where(static row => row.EdgeId == 2)
                .Select(static row => row.RootOccurrence));

        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(
            2,
            parsed.RootElement.GetProperty("roots").GetArrayLength());
        Assert.Equal(
            4,
            parsed.RootElement.GetProperty("occurrences").GetArrayLength());
    }

    [Fact]
    public async Task RowWindowRetainsParentContextWithoutInventingEdges()
    {
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(SharedDag());
        DependencyHierarchyOccurrenceRow selected =
            DependencyHierarchyOutputAdapter.Rows(document)
                .Single(static row => row.EdgeId == 4);

        string tree = await RenderAsync(
            document,
            [selected],
            OutputFormat.PlainText);
        string mermaid = await RenderAsync(
            document,
            [selected],
            OutputFormat.Mermaid);
        string json = await RenderAsync(
            document,
            [selected],
            OutputFormat.Json);

        Assert.Contains("(fragment) Shared", tree, StringComparison.Ordinal);
        Assert.Contains("Leaf", tree, StringComparison.Ordinal);
        Assert.Equal(1, MermaidEdgeCount(mermaid));
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Single(
            parsed.RootElement.GetProperty("occurrences").EnumerateArray());
    }

    [Fact]
    public async Task DepthBoundaryNamesTheExpandedOccurrence()
    {
        DependencyGraphDocument graph = SharedDag() with
        {
            DepthBoundaries =
            [
                new DependencyGraphDepthBoundary(
                    NodeId: 3,
                    PackageProjectionId: null,
                    MaximumDepth: 2,
                    [1],
                    DependencyGraphDepthBoundaryProducerKind.Restored),
            ],
        };
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(graph);
        DependencyHierarchyOccurrenceRow expandedShared =
            DependencyHierarchyOutputAdapter.Rows(document)
                .Single(static row =>
                    row.Target == "Shared"
                    && row.Disposition == "expanded");

        string json = await RenderAsync(
            document,
            [expandedShared],
            OutputFormat.Json);

        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement boundary = Assert.Single(
            parsed.RootElement.GetProperty("depth_boundaries")
                .EnumerateArray());
        Assert.Equal(
            expandedShared.OccurrenceId,
            boundary.GetProperty("occurrence_id").GetInt32());
        Assert.Equal(
            expandedShared.RootOccurrence,
            boundary.GetProperty("root_occurrence").GetInt32());
    }

    [Fact]
    public async Task PackageDepthBoundaryNamesItsProjectionOccurrence()
    {
        DependencyGraphDocument graph = Graph(
            [new DependencyGraphRootOccurrence(1, 0)],
            ["Root", "Shared"],
            [
                Edge(
                    0,
                    0,
                    1,
                    [1],
                    sourceProjection: 0,
                    targetProjection: 1),
                Edge(
                    1,
                    0,
                    1,
                    [1],
                    sourceProjection: 0,
                    targetProjection: 2),
            ],
            [
                Projection(0, 0, rootOccurrence: 1),
                Projection(1, 1),
                Projection(2, 1),
            ],
            [
                new DependencyGraphDepthBoundary(
                    NodeId: 1,
                    PackageProjectionId: 2,
                    MaximumDepth: 1,
                    [1],
                    DependencyGraphDepthBoundaryProducerKind.Package),
            ]);
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(graph);
        List<DependencyHierarchyOccurrenceRow> rows =
            DependencyHierarchyOutputAdapter.Rows(document);

        string json = await RenderAsync(
            document,
            rows,
            OutputFormat.Json);
        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);

        using JsonDocument parsed = JsonDocument.Parse(json);
        JsonElement boundary = Assert.Single(
            parsed.RootElement.GetProperty("depth_boundaries")
                .EnumerateArray());
        DependencyHierarchyOccurrenceRow projectedOccurrence =
            rows.Single(static row => row.EdgeId == 1);
        Assert.Equal(
            projectedOccurrence.OccurrenceId,
            boundary.GetProperty("occurrence_id").GetInt32());
        Assert.Equal(1, Occurrences(tree, "(bounded at depth 1)"));
    }

    [Fact]
    public async Task RowOrientedOutputsRetainBackingEdgeIdentity()
    {
        DependencyHierarchyDocument document =
            DependencyHierarchyDocument.Create(SharedDag());
        List<DependencyHierarchyOccurrenceRow> rows =
            DependencyHierarchyOutputAdapter.Rows(document);

        string jsonLines = await RenderAsync(
            document,
            rows,
            OutputFormat.Jsonl);
        string table = await RenderAsync(
            document,
            rows,
            OutputFormat.Table);

        using JsonDocument parsed = JsonDocument.Parse(
            jsonLines.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0]);
        Assert.Equal(
            rows[0].EdgeId,
            parsed.RootElement.GetProperty("edge_id").GetInt32());
        Assert.Contains("Edge ID", table, StringComparison.Ordinal);

        DependsHierarchyOccurrenceView view =
            DependsHierarchyOccurrenceView.From(rows[0]);
        Assert.Equal(rows[0].EdgeId, view.EdgeId);
    }

    private static async Task<string> RenderAsync(
        DependencyHierarchyDocument document,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows,
        OutputFormat format)
    {
        (string output, string error) =
            await ConsoleCapture.RunAsync(() =>
                DependencyHierarchyOutputAdapter.Write(
                    document,
                    rows,
                    format,
                    tree: false,
                    embeddedMermaid: false,
                    noHeader: false,
                    compactJson: false));
        Assert.Empty(error);
        return output;
    }

    private static DependencyGraphDocument SharedDag() =>
        Graph(
            [new DependencyGraphRootOccurrence(1, 0)],
            ["Root", "A", "B", "Shared", "Leaf"],
            [
                Edge(0, 0, 1, [1]),
                Edge(1, 0, 2, [1]),
                Edge(2, 1, 3, [1]),
                Edge(3, 2, 3, [1]),
                Edge(4, 3, 4, [1]),
            ]);

    private static DependencyGraphDocument MultiRootShared() =>
        Graph(
            [
                new DependencyGraphRootOccurrence(1, 0),
                new DependencyGraphRootOccurrence(2, 1),
            ],
            ["First", "Second", "Shared", "Leaf"],
            [
                Edge(0, 0, 2, [1]),
                Edge(1, 1, 2, [2]),
                Edge(2, 2, 3, [1, 2]),
            ]);

    private static DependencyGraphDocument Graph(
        ImmutableArray<DependencyGraphRootOccurrence> roots,
        ImmutableArray<string> labels,
        ImmutableArray<DependencyGraphEdge> edges,
        ImmutableArray<DependencyGraphPackageProjection> projections = default,
        ImmutableArray<DependencyGraphDepthBoundary> boundaries = default) =>
        new(
            roots,
            [
                .. labels.Select((label, index) =>
                    new DependencyGraphNode(
                        index,
                        new DependencyGraphNodeIdentity.Package(
                            label.ToLowerInvariant(),
                            "1.0.0"),
                        new InertString(TextPolicy.Field, label))),
            ],
            edges,
            projections.IsDefault ? [] : projections,
            boundaries.IsDefault ? [] : boundaries);

    private static DependencyGraphEdge Edge(
        int id,
        int source,
        int target,
        ImmutableArray<int> roots,
        int? sourceProjection = null,
        int? targetProjection = null) =>
        new(
            id,
            source,
            target,
            "package-dependency",
            roots,
            MinimumDepth: 1,
            DependencyGraphResolutionState.Resolved,
            EvidenceIdentity: null,
            sourceProjection,
            targetProjection);

    private static DependencyGraphPackageProjection Projection(
        int id,
        int nodeId,
        int? rootOccurrence = null) =>
        new(
            id,
            nodeId,
            PackageDependencyTraversalProjectionKind.CandidateAcquired,
            PackageDependencyTraversalProjectionExpansion.Expanded,
            Evidence: null,
            Candidate: null,
            rootOccurrence,
            []);

    private static int Occurrences(string value, string expected) =>
        value.Split(expected, StringSplitOptions.None).Length - 1;

    private static int DataLineCount(string table) =>
        table.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Count();

    private static int NonEmptyLineCount(string value) =>
        value.Split(
            '\n',
            StringSplitOptions.RemoveEmptyEntries).Length;

    private static int MermaidEdgeCount(string mermaid) =>
        mermaid.Split('\n').Count(static line =>
            line.Contains("-->", StringComparison.Ordinal)
            || line.Contains("-.->", StringComparison.Ordinal));
}
