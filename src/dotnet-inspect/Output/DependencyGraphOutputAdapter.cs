using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Models;
using DotnetInspector.Options;
using ILInspector.CSharp;
using ILInspector.Metadata;
using InertText;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

internal sealed record DependencyGraphEdgeRow(
    int EdgeId,
    int SourceNodeId,
    int TargetNodeId,
    int[] RootOccurrences,
    string SourceKind,
    string SourceIdentity,
    string Source,
    string Relationship,
    string TargetKind,
    string TargetIdentity,
    string Target,
    int MinimumDepth,
    string Resolution,
    string? EvidenceKind,
    string? EvidenceIdentity);

internal sealed record DependencyGraphJsonLibraryIdentity(
    string Kind,
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken,
    string? ModuleVersionId);

internal sealed record DependencyGraphJsonPackageIdentity(
    string Id,
    string Version);

internal sealed record DependencyGraphJsonNodeIdentity(
    string Kind,
    string? Type,
    DependencyGraphJsonLibraryIdentity? Library,
    DependencyGraphJsonPackageIdentity? Package);

internal sealed record DependencyGraphJsonNode(
    int Id,
    DependencyGraphJsonNodeIdentity Identity,
    string Label,
    int[] RootOccurrences);

internal sealed record DependencyGraphJsonEvidenceIdentity(
    string Kind,
    DependencyGraphJsonLibraryIdentity? AssemblyReference,
    string? PackageVersionConstraint);

internal sealed record DependencyGraphJsonEdge(
    int Id,
    int[] RootOccurrences,
    int SourceNodeId,
    int TargetNodeId,
    DependencyGraphJsonNodeIdentity SourceIdentity,
    string Relationship,
    DependencyGraphJsonNodeIdentity TargetIdentity,
    int MinimumDepth,
    string Resolution,
    DependencyGraphJsonEvidenceIdentity? EvidenceIdentity);

internal sealed record DependencyGraphJsonDocument(
    DependencyGraphJsonNode[] Nodes,
    DependencyGraphJsonEdge[] Edges);

internal sealed record DependencyGraphJsonLine(
    int[] RootOccurrences,
    string SourceKind,
    string SourceIdentity,
    string Source,
    string Relationship,
    string TargetKind,
    string TargetIdentity,
    string Target,
    int MinimumDepth,
    string Resolution,
    string? EvidenceKind,
    string? EvidenceIdentity);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(DependencyGraphJsonDocument))]
[JsonSerializable(typeof(DependencyGraphJsonLine))]
internal partial class DependencyGraphJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(DependencyGraphJsonDocument))]
[JsonSerializable(typeof(DependencyGraphJsonLine))]
internal partial class DependencyGraphCompactJsonContext :
    JsonSerializerContext;

internal static class DependencyGraphOutputAdapter
{
    internal static List<DependencyGraphEdgeRow> EdgeRows(
        DependencyGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return
        [
            .. document.Edges.Select(edge =>
            {
                DependencyGraphNode source =
                    document.Nodes[edge.SourceNodeId];
                DependencyGraphNode target =
                    document.Nodes[edge.TargetNodeId];
                return new DependencyGraphEdgeRow(
                    edge.Id,
                    edge.SourceNodeId,
                    edge.TargetNodeId,
                    [.. edge.RootOccurrences],
                    Kind(source.Identity),
                    IdentityText(source.Identity).ToString(),
                    source.Label.ToString(),
                    edge.Relationship,
                    Kind(target.Identity),
                    IdentityText(target.Identity).ToString(),
                    target.Label.ToString(),
                    edge.MinimumDepth,
                    edge.Resolution.ToString().ToLowerInvariant(),
                    EvidenceKind(edge.EvidenceIdentity),
                    EvidenceText(edge.EvidenceIdentity)?.ToString());
            }),
        ];
    }

    internal static void Write(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        OutputFormat format,
        bool tree,
        bool embeddedMermaid,
        bool noHeader,
        bool compactJson)
    {
        if (tree)
        {
            WriteGraph(
                document,
                rows,
                new PlainTextFormatter(),
                markWindowedFragments: true);
            return;
        }

        switch (format)
        {
            case OutputFormat.Json:
                WriteJson(document, rows, compactJson);
                break;
            case OutputFormat.Table:
            case OutputFormat.Tsv:
                WriteTable(rows, format, noHeader);
                break;
            case OutputFormat.Jsonl:
                WriteJsonLines(rows);
                break;
            case OutputFormat.Mermaid:
                WriteGraph(
                    document,
                    rows,
                    new MermaidFormatter(),
                    markWindowedFragments: false);
                break;
            case OutputFormat.Markdown when embeddedMermaid:
                WriteGraph(
                    document,
                    rows,
                    new MarkdownFormatter(MarkdownGraphMode.Mermaid),
                    markWindowedFragments: false);
                break;
            default:
                WriteGraph(
                    document,
                    rows,
                    new PlainTextFormatter(),
                    markWindowedFragments: true);
                break;
        }
    }

    private static void WriteGraph(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        IMarkoutFormatter formatter,
        bool markWindowedFragments)
    {
        var writer = new MarkoutWriter(Console.Out, formatter);
        writer.WriteGraph(
            ToGraph(document, rows, markWindowedFragments));
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
                "Evidence Kind",
                "Evidence Identity",
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
                "evidence_kind",
                "evidence_identity",
            ],
            [
                .. rows.Select(row => new[]
                {
                    string.Join(",", row.RootOccurrences),
                    row.SourceKind,
                    row.SourceIdentity,
                    row.Source,
                    row.Relationship,
                    row.TargetKind,
                    row.TargetIdentity,
                    row.Target,
                    row.MinimumDepth.ToString(CultureInfo.InvariantCulture),
                    row.Resolution,
                    row.EvidenceKind ?? "",
                    row.EvidenceIdentity ?? "",
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
                    new DependencyGraphJsonLine(
                        row.RootOccurrences,
                        row.SourceKind,
                        row.SourceIdentity,
                        row.Source,
                        row.Relationship,
                        row.TargetKind,
                        row.TargetIdentity,
                        row.Target,
                        row.MinimumDepth,
                        row.Resolution,
                        row.EvidenceKind,
                        row.EvidenceIdentity),
                    DependencyGraphCompactJsonContext.Default
                        .DependencyGraphJsonLine));
        }
    }

    private static void WriteJson(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        bool compact)
    {
        HashSet<int> selectedEdgeIds =
        [
            .. rows.Select(static row => row.EdgeId),
        ];
        HashSet<int> selectedNodeIds =
        [
            .. document.Roots.Select(static root => root.NodeId),
            .. rows.SelectMany(static row =>
                new[] { row.SourceNodeId, row.TargetNodeId }),
        ];
        var rootOccurrencesByNode = document.Roots
            .GroupBy(static root => root.NodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static root => root.OccurrenceIndex)
                    .ToArray());
        var json = new DependencyGraphJsonDocument(
            [
                .. document.Nodes
                    .Where(node => selectedNodeIds.Contains(node.Id))
                    .Select(node => new DependencyGraphJsonNode(
                        node.Id,
                        JsonIdentity(node.Identity),
                        node.Label.ToString(),
                        rootOccurrencesByNode.TryGetValue(
                            node.Id,
                            out int[]? occurrences)
                            ? occurrences
                            : [])),
            ],
            [
                .. document.Edges
                    .Where(edge => selectedEdgeIds.Contains(edge.Id))
                    .Select(edge => new DependencyGraphJsonEdge(
                        edge.Id,
                        [.. edge.RootOccurrences],
                        edge.SourceNodeId,
                        edge.TargetNodeId,
                        JsonIdentity(
                            document.Nodes[edge.SourceNodeId].Identity),
                        edge.Relationship,
                        JsonIdentity(
                            document.Nodes[edge.TargetNodeId].Identity),
                        edge.MinimumDepth,
                        edge.Resolution.ToString().ToLowerInvariant(),
                        JsonEvidenceIdentity(
                            edge.EvidenceIdentity))),
            ]);
        Console.WriteLine(
            JsonSerializer.Serialize(
                json,
                compact
                    ? DependencyGraphCompactJsonContext.Default
                        .DependencyGraphJsonDocument
                    : DependencyGraphJsonContext.Default
                        .DependencyGraphJsonDocument));
    }

    private static Markout.Graph ToGraph(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        bool markWindowedFragments)
    {
        HashSet<int> selectedNodeIds =
        [
            .. document.Roots.Select(static root => root.NodeId),
            .. rows.SelectMany(static row =>
                new[] { row.SourceNodeId, row.TargetNodeId }),
        ];
        HashSet<int> rootNodeIds =
        [
            .. document.Roots.Select(static root => root.NodeId),
        ];
        HashSet<int> fragmentNodeIds = markWindowedFragments
            ? WindowedFragmentNodeIds(document, rows, rootNodeIds)
            : [];
        bool labelRelationships =
            document.Edges.Select(static edge => edge.Relationship)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any();
        return new Markout.Graph(
            [
                .. document.Nodes
                    .Where(node => selectedNodeIds.Contains(node.Id))
                    .Select(node => new Markout.GraphNode(
                        Key(node.Id),
                        fragmentNodeIds.Contains(node.Id)
                            ? $"(fragment) {node.Label}"
                            : node.Label.ToString())
                    {
                        Emphasized = rootNodeIds.Contains(node.Id),
                    }),
            ],
            [
                .. rows.Select(row => new Markout.GraphEdge(
                    Key(row.SourceNodeId),
                    Key(row.TargetNodeId))
                {
                    Label = labelRelationships
                        ? row.Relationship
                        : null,
                }),
            ]);
    }

    private static HashSet<int> WindowedFragmentNodeIds(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        HashSet<int> rootNodeIds)
    {
        HashSet<int> selectedEdgeIds =
        [
            .. rows.Select(static row => row.EdgeId),
        ];
        var selectedOutgoing = rows
            .GroupBy(static row => row.SourceNodeId)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static row => row.TargetNodeId)
                    .ToArray());
        var reachable = new HashSet<int>(rootNodeIds);
        var pendingReachable = new Stack<int>(rootNodeIds);
        while (pendingReachable.TryPop(out int current))
        {
            if (!selectedOutgoing.TryGetValue(
                    current,
                    out int[]? targets))
            {
                continue;
            }
            foreach (int target in targets)
            {
                if (reachable.Add(target))
                    pendingReachable.Push(target);
            }
        }

        HashSet<int> unreachableNodeIds =
        [
            .. rows.SelectMany(static row =>
                    new[] { row.SourceNodeId, row.TargetNodeId })
                .Where(nodeId => !reachable.Contains(nodeId)),
        ];
        HashSet<int> unreachableTargetIds =
        [
            .. rows
                .Where(row =>
                    unreachableNodeIds.Contains(row.SourceNodeId)
                    && unreachableNodeIds.Contains(row.TargetNodeId))
                .Select(static row => row.TargetNodeId),
        ];
        HashSet<int> naturalRoots =
        [
            .. rows
                .Select(static row => row.SourceNodeId)
                .Where(nodeId =>
                    unreachableNodeIds.Contains(nodeId)
                    && !unreachableTargetIds.Contains(nodeId)),
        ];
        if (naturalRoots.Count > 0)
        {
            unreachableNodeIds.ExceptWith(
                NodesReachableFrom(naturalRoots, selectedOutgoing));
        }

        var fragments = new HashSet<int>(naturalRoots);
        var adjacency = new Dictionary<int, HashSet<int>>();
        foreach (DependencyGraphEdgeRow row in rows)
        {
            if (!unreachableNodeIds.Contains(row.SourceNodeId)
                || !unreachableNodeIds.Contains(row.TargetNodeId))
            {
                continue;
            }
            AddNeighbor(row.SourceNodeId, row.TargetNodeId);
            AddNeighbor(row.TargetNodeId, row.SourceNodeId);
        }

        var remaining = new HashSet<int>(adjacency.Keys);
        while (remaining.Count > 0)
        {
            int start = remaining.First();
            var component = new HashSet<int>();
            var pending = new Stack<int>();
            pending.Push(start);
            while (pending.TryPop(out int current))
            {
                if (!remaining.Remove(current))
                    continue;
                component.Add(current);
                foreach (int neighbor in adjacency[current])
                    pending.Push(neighbor);
            }

            DependencyGraphEdge? omittedIncoming = document.Edges
                .FirstOrDefault(edge =>
                    component.Contains(edge.TargetNodeId)
                    && !selectedEdgeIds.Contains(edge.Id));
            fragments.Add(
                omittedIncoming?.TargetNodeId
                    ?? rows.First(row =>
                        component.Contains(row.SourceNodeId)).SourceNodeId);
        }

        return fragments;

        static HashSet<int> NodesReachableFrom(
            IEnumerable<int> starts,
            IReadOnlyDictionary<int, int[]> outgoing)
        {
            var result = new HashSet<int>(starts);
            var pending = new Stack<int>(starts);
            while (pending.TryPop(out int current))
            {
                if (!outgoing.TryGetValue(current, out int[]? targets))
                    continue;
                foreach (int target in targets)
                {
                    if (result.Add(target))
                        pending.Push(target);
                }
            }
            return result;
        }

        void AddNeighbor(int source, int target)
        {
            if (!adjacency.TryGetValue(source, out HashSet<int>? neighbors))
            {
                neighbors = [];
                adjacency.Add(source, neighbors);
            }
            neighbors.Add(target);
        }
    }

    private static string Key(int id) =>
        id.ToString(CultureInfo.InvariantCulture);

    private static string Kind(DependencyGraphNodeIdentity identity) =>
        identity.Kind.ToString().ToLowerInvariant();

    private static InertString IdentityText(
        DependencyGraphNodeIdentity identity) =>
        identity switch
        {
            DependencyGraphNodeIdentity.Type type =>
                Field(type.Name),
            DependencyGraphNodeIdentity.Library library =>
                LibraryIdentityText(library.Identity),
            DependencyGraphNodeIdentity.Package package =>
                InertString.Format(
                    TextPolicy.Field,
                    $"{Field(package.Id)}@{Field(package.Version)}"),
            _ => throw new InvalidOperationException(
                "Unknown dependency graph node identity."),
        };

    private static DependencyGraphJsonNodeIdentity JsonIdentity(
        DependencyGraphNodeIdentity identity) =>
        identity switch
        {
            DependencyGraphNodeIdentity.Type type => new(
                Kind(identity),
                Field(type.Name).ToString(),
                Library: null,
                Package: null),
            DependencyGraphNodeIdentity.Library library => new(
                Kind(identity),
                Type: null,
                JsonLibraryIdentity(library.Identity),
                Package: null),
            DependencyGraphNodeIdentity.Package package => new(
                Kind(identity),
                Type: null,
                Library: null,
                new DependencyGraphJsonPackageIdentity(
                    Field(package.Id).ToString(),
                    Field(package.Version).ToString())),
            _ => throw new InvalidOperationException(
                "Unknown dependency graph node identity."),
        };

    private static InertString LibraryIdentityText(
        ManagedMetadataIdentity identity) =>
        identity switch
        {
            ManagedMetadataIdentity.Assembly assembly =>
                Field(AssemblyIdentityFormatter.Format(
                    assembly.Identity)),
            ManagedMetadataIdentity.Module module =>
                InertString.Format(
                    TextPolicy.Field,
                    $"{Field(module.Name)}@{module.ModuleVersionId:D}"),
            _ => throw new InvalidOperationException(
                "Unknown managed metadata identity."),
        };

    private static DependencyGraphJsonLibraryIdentity
        JsonLibraryIdentity(ManagedMetadataIdentity identity) =>
        identity switch
        {
            ManagedMetadataIdentity.Assembly assembly => new(
                "assembly",
                Field(assembly.Identity.Name).ToString(),
                assembly.Identity.Version?.ToString(),
                assembly.Identity.Culture is { } culture
                    ? Field(culture).ToString()
                    : null,
                assembly.Identity.PublicKeyToken is { } token
                    ? Field(token).ToString()
                    : null,
                ModuleVersionId: null),
            ManagedMetadataIdentity.Module module => new(
                "module",
                Field(module.Name).ToString(),
                Version: null,
                Culture: null,
                PublicKeyToken: null,
                ModuleVersionId:
                    module.ModuleVersionId.ToString("D")),
            _ => throw new InvalidOperationException(
                "Unknown managed metadata identity."),
        };

    private static string? EvidenceKind(
        DependencyGraphEvidenceIdentity? identity) =>
        identity switch
        {
            null => null,
            DependencyGraphEvidenceIdentity.AssemblyReference =>
                "assembly-reference",
            DependencyGraphEvidenceIdentity.PackageVersionConstraint =>
                "package-version-constraint",
            _ => throw new InvalidOperationException(
                "Unknown dependency evidence identity."),
        };

    private static InertString? EvidenceText(
        DependencyGraphEvidenceIdentity? identity) =>
        identity switch
        {
            null => null,
            DependencyGraphEvidenceIdentity.AssemblyReference assembly =>
                Field(AssemblyIdentityFormatter.Format(
                    assembly.Identity)),
            DependencyGraphEvidenceIdentity.PackageVersionConstraint
                constraint => constraint.Value,
            _ => throw new InvalidOperationException(
                "Unknown dependency evidence identity."),
        };

    private static DependencyGraphJsonEvidenceIdentity?
        JsonEvidenceIdentity(
            DependencyGraphEvidenceIdentity? identity) =>
        identity switch
        {
            null => null,
            DependencyGraphEvidenceIdentity.AssemblyReference assembly =>
                new(
                    "assembly-reference",
                    JsonLibraryIdentity(
                        new ManagedMetadataIdentity.Assembly(
                            assembly.Identity)),
                    PackageVersionConstraint: null),
            DependencyGraphEvidenceIdentity.PackageVersionConstraint
                constraint =>
                new(
                    "package-version-constraint",
                    AssemblyReference: null,
                    constraint.Value.ToString()),
            _ => throw new InvalidOperationException(
                "Unknown dependency evidence identity."),
        };

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);
}
