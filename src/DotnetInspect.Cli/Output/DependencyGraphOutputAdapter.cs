using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using ILInspector.CSharp;
using ILInspector.Metadata;
using InertText;
using Markout;
using Markout.Formatting;
using DotnetInspector.Queries;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Output;

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
    DependencyGraphJsonPackageIdentity? Package,
    DependencyEvidenceGraphParentIdentityJson? Restored = null,
    DependencyEvidenceDeclarationIdentityJson? Declaration = null,
    int? ProjectionIndex = null,
    string? Authority = null);

internal sealed record DependencyGraphJsonNode(
    int Id,
    DependencyGraphJsonNodeIdentity Identity,
    string Label,
    int[] RootOccurrences);

internal sealed record DependencyGraphJsonEvidenceIdentity(
    string Kind,
    DependencyGraphJsonLibraryIdentity? AssemblyReference,
    string? PackageVersionConstraint,
    DependencyEvidenceDeclarationIdentityJson? Declaration = null,
    DependencyEvidenceEdgeIdentityJson? RestoredPackage = null,
    DependencyEvidenceGraphParentIdentityJson? ProjectParent = null,
    DependencyEvidenceGraphParentIdentityJson? ProjectDependency = null,
    int? SourceProjectionIndex = null,
    int? TargetProjectionIndex = null,
    string? Authority = null,
    int? TraversalEdgeIndex = null);

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
    DependencyGraphJsonEvidenceIdentity? EvidenceIdentity,
    Dictionary<int, int>? RootDistances = null);

internal sealed record DependencyGraphPackageProjectionJson(
    int Index,
    int NodeIndex,
    int DocumentNodeId,
    string Kind,
    string Expansion,
    int? RootOccurrenceIndex,
    DependencyEvidenceRootIdentityJson? RootIdentity,
    DependencyEvidenceGroupIdentityJson? SelectedGroup,
    string? SelectionStatus,
    string? RequestedFramework,
    string? SelectedFramework,
    DependencyEvidenceSourceIdentityJson? Source,
    int? CandidateCorrespondence,
    DependencyEvidencePackageCoordinateJson? CandidateCoordinate,
    string? CandidateKind,
    string? DiscoveryContract);

internal sealed record DependencyGraphPackageRootJson(
    int OccurrenceIndex,
    int DocumentOccurrenceIndex,
    int NodeIndex,
    int ProjectionIndex,
    string Completion,
    IReadOnlyDictionary<int, int> NodeDistances,
    IReadOnlyDictionary<int, int> ProjectionDistances,
    IReadOnlyDictionary<int, int> EdgeDistances);

internal sealed record DependencyGraphRestoredTraversalJson(
    int RootOccurrence,
    DependencyEvidenceRestoredSelectionIdentityJson Selection,
    string TopologyDigest,
    string Completion);

internal sealed record DependencyGraphJsonDocument(
    DependencyGraphJsonNode[] Nodes,
    DependencyGraphJsonEdge[] Edges,
    DependencyGraphPackageProjectionJson[]? PackageProjections = null,
    DependencyGraphBoundary[]? Boundaries = null,
    DependencyGraphPackageRootJson[]? PackageRoots = null,
    DependencyGraphRestoredTraversalJson[]? RestoredTraversals = null);

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
        DependencyGraphDocument document,
        RowWindow? window = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        return
        [
            .. RowWindow.Apply(window, document.Edges).Select(edge =>
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
            WriteTree(Console.Out, document, rows);
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
                WriteTree(Console.Out, document, rows);
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
        WriteRows(writer, rows);
        writer.Flush();
    }

    internal static void WriteRows(MarkoutWriter writer, IReadOnlyList<DependencyGraphEdgeRow> rows)
    {
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
        DependencyGraphJsonDocument json = ToJson(document, rows);
        Console.WriteLine(JsonSerializer.Serialize(json,
            compact ? DependencyGraphCompactJsonContext.Default.DependencyGraphJsonDocument
                : DependencyGraphJsonContext.Default.DependencyGraphJsonDocument));
    }

    internal static DependencyGraphJsonDocument ToJson(
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows,
        PackageDependencyTraversalOutcome? traversal = null,
        DependencyEvidenceSourceTokens? tokens = null,
        IReadOnlyList<int>? packageRootOccurrences = null,
        IReadOnlyDictionary<int, RestoredProjectDependencyTraversalResult>? restoredTraversals = null)
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
                        JsonEvidenceIdentity(edge.EvidenceIdentity),
                        edge.RootDistances.Count == 0 ? null : edge.RootDistances.ToDictionary())),
            ], Boundaries: [.. document.Boundaries.Where(boundary => selectedNodeIds.Contains(boundary.NodeId))]);
        if (restoredTraversals is not null)
            json = json with
            {
                RestoredTraversals = [.. restoredTraversals.OrderBy(pair => pair.Key)
                    .Where(pair => pair.Value is RestoredProjectDependencyTraversalResult.Available)
                    .Select(pair =>
                    {
                        var restored = ((RestoredProjectDependencyTraversalResult.Available)pair.Value).Value;
                        return new DependencyGraphRestoredTraversalJson(pair.Key,
                            DependencyEvidenceRestoredSelectionIdentityJson.Create(restored.Identity.Selection),
                            restored.Identity.TopologyDigest, restored.Completion.ToString());
                    })],
            };
        if (traversal is null)
            return json;
        var correspondences = new Dictionary<PackageAcquisitionCandidateCorrespondence, int>();
        var packageNodes = document.Nodes.Where(node => node.Identity is DependencyGraphNodeIdentity.Coordinate)
            .ToDictionary(node => ((DependencyGraphNodeIdentity.Coordinate)node.Identity).Value, node => node.Id);
        foreach (var projection in traversal.Projections)
        {
            if (projection.Candidate is { } candidate && !correspondences.ContainsKey(candidate.Correspondence))
                correspondences.Add(candidate.Correspondence, correspondences.Count);
            if (projection.Evidence?.Provenance is PackageDependencyEvidenceRootProvenance.Package package)
                tokens?.Project(package.Source);
        }
        HashSet<int> visiblePackageNodes = [.. traversal.Nodes.Select((node, index) => (node, index))
            .Where(pair => selectedNodeIds.Contains(packageNodes[pair.node.Coordinate])).Select(pair => pair.index)];
        HashSet<int> visibleProjections = [.. traversal.Projections.Select((projection, index) => (projection, index))
            .Where(pair => visiblePackageNodes.Contains(pair.projection.NodeIndex)).Select(pair => pair.index)];
        HashSet<int> visibleTraversalEdges = [.. document.Edges.Where(edge => selectedEdgeIds.Contains(edge.Id))
            .Select(edge => edge.EvidenceIdentity).OfType<DependencyGraphEvidenceIdentity.Declaration>()
            .Select(evidence => evidence.TraversalEdgeIndex)];
        return json with
        {
            PackageProjections = [.. traversal.Projections.Select((projection, index) => (projection, index))
                .Where(pair => visibleProjections.Contains(pair.index)).Select(pair =>
            {
                var (projection, index) = pair;
                int? correspondence = projection.Candidate is { } candidate
                    ? correspondences[candidate.Correspondence] : null;
                int documentNode = packageNodes[traversal.Nodes[projection.NodeIndex].Coordinate];
                return new DependencyGraphPackageProjectionJson(index, projection.NodeIndex, documentNode,
                    projection.Kind.ToString(), projection.Expansion.ToString(),
                    projection.RootOccurrenceIndex,
                    DependencyEvidenceRootIdentityJson.CreateOptional(projection.Evidence?.Identity),
                    DependencyEvidenceGroupIdentityJson.CreateOptional(projection.Evidence?.Selection.SelectedGroup),
                    projection.Evidence?.Selection.Status.ToString(),
                    projection.Evidence?.Selection.RequestedFramework?.ToString(),
                    projection.Evidence?.Selection.SelectedFramework?.ToString(),
                    tokens?.Project(projection.Evidence?.Provenance is
                        PackageDependencyEvidenceRootProvenance.Package package ? package.Source : null),
                    correspondence,
                    projection.Candidate is { } value
                        ? DependencyEvidencePackageCoordinateJson.Create(value.Coordinate) : null,
                    projection.Candidate?.Kind.ToString(),
                    projection.Candidate?.DiscoveryContract?.ToString());
            })],
            PackageRoots = [.. traversal.Roots.Select(root =>
            {
                PackageDependencyTraversalReachability reachability = traversal.RootReachability[root.OccurrenceIndex];
                return new DependencyGraphPackageRootJson(root.OccurrenceIndex,
                    packageRootOccurrences is not null ? packageRootOccurrences[root.OccurrenceIndex] : root.OccurrenceIndex,
                    root.NodeIndex, root.ProjectionIndex, root.Completion.ToString(),
                    reachability.NodeDistances.Where(pair => visiblePackageNodes.Contains(pair.Key)).ToDictionary(),
                    reachability.ProjectionDistances.Where(pair => visibleProjections.Contains(pair.Key)).ToDictionary(),
                    reachability.EdgeDistances.Where(pair => visibleTraversalEdges.Contains(pair.Key)).ToDictionary());
            })],
        };
    }

    internal static Markout.Graph ToGraph(
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
                            ? $"(fragment) {GraphLabel(node)}"
                            : GraphLabel(node))
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

        string GraphLabel(DependencyGraphNode node) =>
            node.Label.ToString()
            + string.Concat(
                document.Boundaries
                    .Where(boundary => boundary.NodeId == node.Id)
                    .Select(BoundarySuffix)
                    .Where(static suffix => suffix.Length > 0)
                    .Distinct(StringComparer.Ordinal));
    }

    internal static void WriteTree(
        TextWriter output,
        DependencyGraphDocument document,
        IReadOnlyList<DependencyGraphEdgeRow> rows)
    {
        var outgoing = rows.GroupBy(row => row.SourceNodeId)
            .ToDictionary(group => group.Key, group => group.ToArray());
        var emitted = new HashSet<int>();
        var seenNodes = new HashSet<int>();
        var forest = new List<TreeNode>();
        foreach (DependencyGraphRootOccurrence root in document.Roots)
        {
            var children = new List<TreeNode>();
            string marker = seenNodes.Add(root.NodeId) ? "" : "(revisit) ";
            forest.Add(new TreeNode(marker + Label(root.NodeId, root.OccurrenceIndex)) { Children = children });
            Populate(root.NodeId, root.OccurrenceIndex, children);
        }
        // A row window can remove the path from every explicit root. Render its
        // remaining component without drawing an unselected connecting edge.
        foreach (DependencyGraphEdgeRow row in rows)
        {
            if (emitted.Contains(row.EdgeId))
                continue;
            int root = row.RootOccurrences.FirstOrDefault();
            var children = new List<TreeNode>();
            forest.Add(new TreeNode("(fragment) " + Label(row.SourceNodeId, root)) { Children = children });
            seenNodes.Add(row.SourceNodeId);
            Populate(row.SourceNodeId, root, children);
        }
        var writer = MarkoutWriter.Create(output, new PlainTextFormatter(), new MarkoutWriterOptions());
        foreach (TreeNode root in forest)
        {
            output.WriteLine(root.Text);
            writer.WriteTree([.. root.Children ?? []]);
            writer.Flush();
        }

        string Label(int node, int root)
        {
            string label = document.Nodes[node].Label.ToString();
            foreach (DependencyGraphBoundary boundary in document.Boundaries
                .Where(boundary => boundary.NodeId == node && boundary.RootOccurrence == root))
                label += BoundarySuffix(boundary);
            return label;
        }

        void Populate(int start, int root, List<TreeNode> children)
        {
            var active = new HashSet<int> { start };
            var stack = new Stack<(int Node, int Next, List<TreeNode> Children)>();
            stack.Push((start, 0, children));
            while (stack.TryPop(out var frame))
            {
                if (!outgoing.TryGetValue(frame.Node, out var edges) || frame.Next >= edges.Length)
                {
                    active.Remove(frame.Node);
                    continue;
                }

                DependencyGraphEdgeRow edge = edges[frame.Next];
                stack.Push((frame.Node, frame.Next + 1, frame.Children));
                if (!edge.RootOccurrences.Contains(root))
                    continue;
                bool first = !emitted.Contains(edge.EdgeId);
                if (!first && !CanReachUnemitted(edge.TargetNodeId, root, active))
                    continue;
                emitted.Add(edge.EdgeId);
                bool cycle = active.Contains(edge.TargetNodeId);
                bool revisit = !seenNodes.Add(edge.TargetNodeId);
                string marker = cycle ? "(cycle) (revisit) " : revisit || !first ? "(revisit) " : "";
                var nested = new List<TreeNode>();
                frame.Children.Add(new TreeNode(marker + Label(edge.TargetNodeId, root)) { Children = nested });
                if (cycle)
                    continue;
                active.Add(edge.TargetNodeId);
                stack.Push((edge.TargetNodeId, 0, nested));
            }
        }

        bool CanReachUnemitted(int start, int root, HashSet<int> active)
        {
            var visited = new HashSet<int>(active);
            var pending = new Stack<int>();
            pending.Push(start);
            while (pending.TryPop(out int node))
            {
                if (!visited.Add(node) || !outgoing.TryGetValue(node, out var edges))
                    continue;
                foreach (DependencyGraphEdgeRow edge in edges)
                {
                    if (!edge.RootOccurrences.Contains(root))
                        continue;
                    if (!emitted.Contains(edge.EdgeId))
                        return true;
                    pending.Push(edge.TargetNodeId);
                }
            }
            return false;
        }
    }

    private static string BoundarySuffix(
        DependencyGraphBoundary boundary) =>
        boundary.Kind switch
        {
            "Depth" => $" (depth {boundary.MaximumDepth})",
            "Source" => " (source boundary)",
            nameof(PackageDependencyEvidenceSelectionStatus.NoDependencyGroups) =>
                " (no dependency groups)",
            nameof(PackageDependencyEvidenceSelectionStatus
                .NoMatchingTargetFramework) =>
                " (no matching target framework)",
            nameof(PackageDependencyEvidenceSelectionStatus.Unavailable) =>
                " (dependency selection unavailable)",
            _ => "",
        };

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

    internal static InertString IdentityText(
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
            DependencyGraphNodeIdentity.Coordinate package =>
                Field($"{package.Value.PackageId}@{package.Value.Version}"),
            DependencyGraphNodeIdentity.Declaration or DependencyGraphNodeIdentity.Restored =>
                Field(JsonSerializer.Serialize(JsonIdentity(identity),
                    DependencyDocumentCompactJsonContext.Default.DependencyGraphJsonNodeIdentity)),
            _ => throw new InvalidOperationException(
                "Unknown dependency graph node identity."),
        };

    internal static DependencyGraphJsonNodeIdentity JsonIdentity(
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
            DependencyGraphNodeIdentity.Coordinate package => new(
                Kind(identity), null, null,
                new(package.Value.PackageId, package.Value.Version)),
            DependencyGraphNodeIdentity.Restored restored => new(
                Kind(identity), null, null, null,
                Restored: DependencyEvidenceGraphParentIdentityJson.Create(restored.Value)),
            DependencyGraphNodeIdentity.Declaration declaration => new(
                Kind(identity), null, null, null,
                Declaration: DependencyEvidenceDeclarationIdentityJson.Create(declaration.Value),
                ProjectionIndex: declaration.ProjectionIndex, Authority: declaration.Authority.ToString()),
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
            DependencyGraphEvidenceIdentity.Declaration => "package-declaration",
            DependencyGraphEvidenceIdentity.RestoredPackage => "restored-package",
            DependencyGraphEvidenceIdentity.RestoredProject => "project-reference",
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
            DependencyGraphEvidenceIdentity.Declaration or DependencyGraphEvidenceIdentity.RestoredPackage
                or DependencyGraphEvidenceIdentity.RestoredProject =>
                Field(JsonSerializer.Serialize(JsonEvidenceIdentity(identity),
                    DependencyDocumentCompactJsonContext.Default.DependencyGraphJsonEvidenceIdentity)),
            _ => throw new InvalidOperationException(
                "Unknown dependency evidence identity."),
        };

    internal static DependencyGraphJsonEvidenceIdentity?
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
            DependencyGraphEvidenceIdentity.Declaration declaration => new(
                "package-declaration", null, declaration.Value.CanonicalVersionConstraint,
                Declaration: DependencyEvidenceDeclarationIdentityJson.Create(declaration.Value.Identity),
                SourceProjectionIndex: declaration.SourceProjectionIndex,
                TargetProjectionIndex: declaration.TargetProjectionIndex,
                Authority: declaration.Authority.ToString(),
                TraversalEdgeIndex: declaration.TraversalEdgeIndex),
            DependencyGraphEvidenceIdentity.RestoredPackage restored => new(
                "restored-package", null, restored.Value.CanonicalVersionConstraint,
                RestoredPackage: DependencyEvidenceEdgeIdentityJson.Create(restored.Value.Identity)),
            DependencyGraphEvidenceIdentity.RestoredProject restored => new(
                "project-reference", null, null,
                ProjectParent: DependencyEvidenceGraphParentIdentityJson.Create(restored.Value.Parent),
                ProjectDependency: DependencyEvidenceGraphParentIdentityJson.Create(
                    new RestoredProjectGraphParentIdentity.Project(restored.Value.Dependency))),
            _ => throw new InvalidOperationException(
                "Unknown dependency evidence identity."),
        };

    private static InertString Field(string value) =>
        new(TextPolicy.Field, value);
}
