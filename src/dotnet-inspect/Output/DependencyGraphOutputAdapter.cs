using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Models;
using DotnetInspector.Options;
using ILInspector.CSharp;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

internal sealed record DependencyGraphEdgeRow(
    int EdgeId,
    int[] RootNodeIds,
    DependencyGraphNodeKind SourceKind,
    string SourceIdentity,
    string Source,
    string Relationship,
    DependencyGraphNodeKind TargetKind,
    string TargetIdentity,
    string Target,
    int MinimumDepth,
    DependencyGraphResolutionState Resolution);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DependencyGraphJsonDocument))]
[JsonSerializable(typeof(DependencyGraphEdgeRow))]
internal partial class DependencyGraphJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DependencyGraphJsonDocument))]
[JsonSerializable(typeof(DependencyGraphEdgeRow))]
internal partial class DependencyGraphCompactJsonContext : JsonSerializerContext;

internal static class DependencyGraphOutputAdapter
{
    public static void Write(
        DependencyGraphDocument document,
        DependsOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        IReadOnlyList<DependencyGraphEdge> selectedEdges =
            RowWindow.Apply(options.Rows, document.Edges);
        if (options.Count)
        {
            CountOutput.WriteCount(selectedEdges.Count);
            return;
        }

        OutputFormat format = options.EffectiveFormat;
        switch (format)
        {
            case OutputFormat.Json:
                WriteJson(
                    Select(document, selectedEdges),
                    options.CompactJson);
                break;
            case OutputFormat.Table:
            case OutputFormat.Tsv:
                WriteTable(
                    EdgeRows(document, selectedEdges),
                    format,
                    options.NoHeader);
                break;
            case OutputFormat.Jsonl:
                WriteJsonLines(EdgeRows(document, selectedEdges));
                break;
            case OutputFormat.Mermaid:
                WriteGraph(
                    document,
                    selectedEdges,
                    new MermaidFormatter());
                break;
            case OutputFormat.PlainText:
                WriteGraph(
                    document,
                    selectedEdges,
                    new PlainTextFormatter());
                break;
            default:
                WriteMarkdown(
                    document,
                    selectedEdges,
                    options.EmbeddedMermaid);
                break;
        }
    }

    internal static IReadOnlyList<DependencyGraphEdgeRow> EdgeRows(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdge>? selectedEdges = null)
    {
        selectedEdges ??= document.Edges;
        return
        [
            .. selectedEdges.Select(edge =>
            {
                DependencyGraphNode source = document.Nodes[edge.FromNodeId];
                DependencyGraphNode target = document.Nodes[edge.ToNodeId];
                return new DependencyGraphEdgeRow(
                    edge.Id,
                    edge.RootNodeIds,
                    source.Kind,
                    source.Identity,
                    source.Label,
                    edge.Relationship,
                    target.Kind,
                    target.Identity,
                    target.Label,
                    edge.MinimumDepth,
                    target.Resolution);
            }),
        ];
    }

    internal static Markout.Graph ToGraph(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdge>? selectedEdges = null)
    {
        selectedEdges ??= document.Edges;
        HashSet<int> selectedNodeIds =
        [
            .. document.RootNodeIds,
            .. selectedEdges.SelectMany(
                static edge => new[] { edge.FromNodeId, edge.ToNodeId }),
        ];

        var nodes = new List<Markout.GraphNode>(selectedNodeIds.Count);
        foreach (DependencyGraphNode node in document.Nodes)
        {
            if (!selectedNodeIds.Contains(node.Id))
                continue;

            nodes.Add(
                new Markout.GraphNode(Key(node.Id), node.Label)
                {
                    Emphasized = document.RootNodeIds.Contains(node.Id),
                });
        }

        var edges = new List<Markout.GraphEdge>(selectedEdges.Count);
        foreach (DependencyGraphEdge edge in selectedEdges)
        {
            edges.Add(
                new Markout.GraphEdge(
                    Key(edge.FromNodeId),
                    Key(edge.ToNodeId))
                {
                    Label = edge.Relationship,
                });
        }

        return new Markout.Graph(nodes, edges);
    }

    private static void WriteMarkdown(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdge> selectedEdges,
        bool embeddedMermaid)
    {
        if (embeddedMermaid)
        {
            var writer = new MarkoutWriter(
                Console.Out,
                new MarkdownFormatter(MarkdownGraphMode.Mermaid));
            writer.WriteHeading(1, document.Title);
            writer.WriteGraph(ToGraph(document, selectedEdges));
            writer.Flush();
            return;
        }

        var headingWriter = new MarkoutWriter(
            Console.Out,
            new MarkdownFormatter());
        headingWriter.WriteHeading(1, document.Title);
        if (selectedEdges.Count != document.Edges.Length)
        {
            headingWriter.WriteParagraph(
                "Windowed dependency graph fragment; only the selected "
                + "logical edges are shown.");
        }
        headingWriter.Flush();

        WriteGraph(
            document,
            selectedEdges,
            new PlainTextFormatter());
    }

    private static void WriteGraph(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdge> selectedEdges,
        IMarkoutFormatter formatter)
    {
        var writer = new MarkoutWriter(Console.Out, formatter);
        writer.WriteGraph(ToGraph(document, selectedEdges));
        writer.Flush();
    }

    private static void WriteTable(
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        OutputFormat format,
        bool noHeader)
    {
        var writer = new MarkoutWriter(
            Console.Out,
            new TableFormatter(!noHeader),
            OutputFormatter.CreateTableWriterOptions(
                tsv: format == OutputFormat.Tsv,
                jsonl: false));
        writer.WriteTable(
            [
                "Roots",
                "Source Kind",
                "Source Identity",
                "Source",
                "Relationship",
                "Target Kind",
                "Target Identity",
                "Target",
                "Minimum Depth",
                "Resolution",
            ],
            [
                "roots",
                "source_kind",
                "source_identity",
                "source",
                "relationship",
                "target_kind",
                "target_identity",
                "target",
                "minimum_depth",
                "resolution",
            ],
            [
                .. rows.Select(row => new[]
                {
                    string.Join(",", row.RootNodeIds),
                    row.SourceKind.ToString(),
                    CSharpIdentifier.ContainRenderedText(
                        row.SourceIdentity),
                    row.Source,
                    row.Relationship,
                    row.TargetKind.ToString(),
                    CSharpIdentifier.ContainRenderedText(
                        row.TargetIdentity),
                    row.Target,
                    row.MinimumDepth.ToString(
                        CultureInfo.InvariantCulture),
                    row.Resolution.ToString(),
                }),
            ]);
        writer.Flush();
    }

    private static void WriteJsonLines(
        IReadOnlyList<DependencyGraphEdgeRow> rows)
    {
        foreach (DependencyGraphEdgeRow row in rows)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    row,
                    DependencyGraphCompactJsonContext.Default
                        .DependencyGraphEdgeRow));
        }
    }

    private static void WriteJson(
        DependencyGraphJsonDocument document,
        bool compact)
    {
        Console.WriteLine(
            compact
                ? JsonSerializer.Serialize(
                    document,
                    DependencyGraphCompactJsonContext.Default
                        .DependencyGraphJsonDocument)
                : JsonSerializer.Serialize(
                    document,
                    DependencyGraphJsonContext.Default
                        .DependencyGraphJsonDocument));
    }

    internal static DependencyGraphJsonDocument Select(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdge> selectedEdges)
    {
        HashSet<int> selectedNodeIds =
        [
            .. document.RootNodeIds,
            .. selectedEdges.SelectMany(
                static edge => new[] { edge.FromNodeId, edge.ToNodeId }),
        ];
        return new DependencyGraphJsonDocument(
            document.Title,
            document.RootNodeIds,
            [
                .. document.Nodes.Where(node =>
                    selectedNodeIds.Contains(node.Id)),
            ],
            [.. selectedEdges]);
    }

    private static string Key(int id) =>
        id.ToString(CultureInfo.InvariantCulture);
}
