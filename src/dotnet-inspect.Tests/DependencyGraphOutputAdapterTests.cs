using System.Collections.Immutable;
using System.Text.Json;
using DotnetInspector.Inspectors;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Services;
using InertText;

namespace DotnetInspector.Tests;

[Collection("Console")]
public sealed class DependencyGraphOutputAdapterTests
{
    [Fact]
    public async Task SharedDag_PreservesEdgesAndRootAcrossGraphSinks()
    {
        DependencyGraphDocument document = SharedDag();
        List<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document);

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
        string compactJson = await RenderAsync(
            document,
            rows,
            OutputFormat.Json,
            compactJson: true);

        Assert.Equal(5, rows.Count);
        Assert.Contains("Root", tree, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(tree, "Shared"));
        Assert.Contains("(revisit) Shared", tree, StringComparison.Ordinal);
        Assert.Equal(1, Occurrences(tree, "Leaf"));

        Assert.StartsWith("graph TD", mermaid, StringComparison.Ordinal);
        Assert.Contains("Root", mermaid, StringComparison.Ordinal);
        Assert.True(
            MermaidEdgeCount(mermaid) == 5,
            mermaid);

        Assert.Equal(5, DataLineCount(table));
        Assert.Equal(
            5,
            jsonLines.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries).Length);
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(
            5,
            parsed.RootElement.GetProperty("edges").GetArrayLength());
        Assert.True(NonEmptyLineCount(json) > 1);
        Assert.Equal(1, NonEmptyLineCount(compactJson));
        Assert.Contains(
            parsed.RootElement.GetProperty("nodes").EnumerateArray(),
            static node => node.GetProperty("root_occurrences")
                .GetArrayLength() == 1);
    }

    [Fact]
    public async Task RowWindow_SelectsTheSameLogicalEdgesInEverySink()
    {
        DependencyGraphDocument document = SharedDag();
        IReadOnlyList<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document)
                .Skip(2)
                .Take(2)
                .ToArray();

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

        Assert.Contains("Root", tree, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(tree, "Shared"));
        Assert.Contains("(fragment) A", tree, StringComparison.Ordinal);
        Assert.Contains("(fragment) B", tree, StringComparison.Ordinal);
        Assert.DoesNotContain("Leaf", tree, StringComparison.Ordinal);
        Assert.True(
            MermaidEdgeCount(mermaid) == 2,
            mermaid);
        Assert.Contains("Root", mermaid, StringComparison.Ordinal);
        Assert.Equal(2, DataLineCount(table));
        Assert.Equal(
            2,
            jsonLines.Split(
                Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries).Length);
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(
            2,
            parsed.RootElement.GetProperty("edges").GetArrayLength());
    }

    [Fact]
    public async Task RowWindow_MarksDetachedCycleAsFragment()
    {
        DependencyGraphDocument document = DetachedCycle();
        IReadOnlyList<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document)
                .Skip(1)
                .ToArray();

        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);

        Assert.Contains("(fragment) A", tree, StringComparison.Ordinal);
        Assert.Contains("(revisit)", tree, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceAuthoredLabelsRemainInertAcrossSinks()
    {
        const string Hazard = "Shared\u202Ename";
        DependencyGraphDocument document = SharedDag(Hazard);
        List<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document);

        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);
        string table = await RenderAsync(
            document,
            rows,
            OutputFormat.Table);
        string json = await RenderAsync(
            document,
            rows,
            OutputFormat.Json);

        Assert.DoesNotContain(Hazard, tree, StringComparison.Ordinal);
        Assert.DoesNotContain(Hazard, table, StringComparison.Ordinal);
        Assert.DoesNotContain(Hazard, json, StringComparison.Ordinal);
        Assert.Contains("\\u202E", tree, StringComparison.Ordinal);
        Assert.Contains("\\u202E", table, StringComparison.Ordinal);
        Assert.Contains("\\u202E", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootOnlyGraph_RemainsVisibleWithoutInventingAnEdge()
    {
        DependencyGraphDocument document = new(
            [new DependencyGraphRootOccurrence(1, 0)],
            [
                new DependencyGraphNode(
                    0,
                    new DependencyGraphNodeIdentity.Type(
                        "System.IDisposable"),
                    new InertString(
                        TextPolicy.Field,
                        "System.IDisposable")),
            ],
            []);
        List<DependencyGraphEdgeRow> rows =
            DependencyGraphOutputAdapter.EdgeRows(document);

        string tree = await RenderAsync(
            document,
            rows,
            OutputFormat.PlainText);
        string mermaid = await RenderAsync(
            document,
            rows,
            OutputFormat.Mermaid);
        string json = await RenderAsync(
            document,
            rows,
            OutputFormat.Json);

        Assert.Empty(rows);
        Assert.Contains("System.IDisposable", tree, StringComparison.Ordinal);
        Assert.Contains("System.IDisposable", mermaid, StringComparison.Ordinal);
        using JsonDocument parsed = JsonDocument.Parse(json);
        Assert.Equal(
            1,
            parsed.RootElement.GetProperty("nodes").GetArrayLength());
        Assert.Equal(
            0,
            parsed.RootElement.GetProperty("edges").GetArrayLength());
    }

    [Fact]
    public void EmptyPackageRoot_UsesCanonicalSemanticVersion()
    {
        DependencyGraphDocument document =
            DependencyGraphProjection.Package(
                new PackageDependencyGraphResult.Empty(
                    "Example",
                    "1.0",
                    "Example",
                    "1.0",
                    "No additional dependencies.",
                    PackageDependencyGraphResult.EmptyKind.SelectedGroup));

        DependencyGraphNode root = Assert.Single(document.Nodes);
        var identity = Assert.IsType<
            DependencyGraphNodeIdentity.Package>(
                root.Identity);
        Assert.Equal("1.0.0", identity.Version);
    }

    [Fact]
    public async Task MermaidRelationshipLabelsDoNotDependOnTheRowWindow()
    {
        DependencyGraphDocument original = SharedDag();
        DependencyGraphDocument document = original with
        {
            Edges =
            [
                original.Edges[0] with { Relationship = "base-type" },
                .. original.Edges.Skip(1).Select(edge =>
                    edge with { Relationship = "interface" }),
            ],
        };
        DependencyGraphEdgeRow selected =
            DependencyGraphOutputAdapter.EdgeRows(document)[1];

        string mermaid = await RenderAsync(
            document,
            [selected],
            OutputFormat.Mermaid);

        Assert.Contains("interface", mermaid, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageProjection_PreservesUnavailableResolution()
    {
        var root =
            new PackageDependencyIdentity("Root.Package", "1.0.0");
        var target =
            new PackageDependencyIdentity("Missing.Package", "2.0.0");
        var graph = new PackageDependencyGraphResult.Graph(
            root.PackageId,
            root.Version,
            root.PackageId,
            root.Version,
            new PackageDependencyGraph(
                [
                    new PackageDependencyGraphNode(root, Author: null),
                    new PackageDependencyGraphNode(target, Author: null),
                ],
                [
                    new PackageDependencyRelationship(
                        root,
                        target,
                        "[2.0.0, )",
                        PackageDependencyResolutionState.Unavailable,
                        Ordinal: 0),
                ],
                []));

        DependencyGraphEdgeRow row = Assert.Single(
            DependencyGraphOutputAdapter.EdgeRows(
                DependencyGraphProjection.Package(graph)));

        Assert.Equal("unavailable", row.Resolution);
    }

    private static async Task<string> RenderAsync(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        OutputFormat format,
        bool compactJson = false)
    {
        (string output, string error) =
            await ConsoleCapture.RunAsync(() =>
                DependencyGraphOutputAdapter.Write(
                    document,
                    rows,
                    format,
                    tree: false,
                    embeddedMermaid: false,
                    noHeader: false,
                    compactJson));
        Assert.Empty(error);
        return output;
    }

    private static DependencyGraphDocument SharedDag(
        string sharedLabel = "Shared")
    {
        static InertString Label(string value) =>
            new(TextPolicy.Field, value);

        ImmutableArray<DependencyGraphNode> nodes =
        [
            new(
                0,
                new DependencyGraphNodeIdentity.Package("root", "1.0.0"),
                Label("Root")),
            new(
                1,
                new DependencyGraphNodeIdentity.Package("a", "1.0.0"),
                Label("A")),
            new(
                2,
                new DependencyGraphNodeIdentity.Package("b", "1.0.0"),
                Label("B")),
            new(
                3,
                new DependencyGraphNodeIdentity.Package(
                    "shared",
                    "1.0.0"),
                Label(sharedLabel)),
            new(
                4,
                new DependencyGraphNodeIdentity.Package("leaf", "1.0.0"),
                Label("Leaf")),
        ];
        ImmutableArray<DependencyGraphEdge> edges =
        [
            Edge(0, 0, 1, 1),
            Edge(1, 0, 2, 1),
            Edge(2, 1, 3, 2),
            Edge(3, 2, 3, 2),
            Edge(4, 3, 4, 3),
        ];
        return new DependencyGraphDocument(
            [new DependencyGraphRootOccurrence(1, 0)],
            nodes,
            edges);
    }

    private static DependencyGraphDocument DetachedCycle()
    {
        static InertString Label(string value) =>
            new(TextPolicy.Field, value);

        return new DependencyGraphDocument(
            [new DependencyGraphRootOccurrence(1, 0)],
            [
                new(
                    0,
                    new DependencyGraphNodeIdentity.Package(
                        "owner",
                        "1.0.0"),
                    Label("Owner")),
                new(
                    1,
                    new DependencyGraphNodeIdentity.Package("a", "1.0.0"),
                    Label("A")),
                new(
                    2,
                    new DependencyGraphNodeIdentity.Package("b", "1.0.0"),
                    Label("B")),
            ],
            [
                Edge(0, 0, 1, 1),
                Edge(1, 1, 2, 2),
                Edge(2, 2, 1, 3),
            ]);
    }

    private static DependencyGraphEdge Edge(
        int id,
        int source,
        int target,
        int depth) =>
        new(
            id,
            source,
            target,
            "package-dependency",
            [1],
            depth,
            DependencyGraphResolutionState.Resolved,
            EvidenceIdentity: null);

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
