using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using InertText;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Output;

internal sealed record DependencyHierarchyOccurrenceRow(
    int OccurrenceId,
    int RootOccurrence,
    int ParentOccurrenceId,
    int EdgeId,
    int SourceNodeId,
    int TargetNodeId,
    int Depth,
    string SourceKind,
    string SourceIdentity,
    string Source,
    string Relationship,
    string TargetKind,
    string TargetIdentity,
    string Target,
    string Disposition,
    string Resolution,
    string? EvidenceKind,
    string? EvidenceIdentity)
{
    internal InertString SourceIdentityText { get; init; }

    internal InertString SourceText { get; init; }

    internal InertString TargetIdentityText { get; init; }

    internal InertString TargetText { get; init; }

    internal InertString? EvidenceIdentityText { get; init; }
}

internal sealed record DependencyHierarchyJsonRoot(
    int OccurrenceId,
    int RootOccurrence,
    int RootPosition,
    int NodeId,
    DependencyGraphJsonNodeIdentity Identity,
    string Label);

internal sealed record DependencyHierarchyJsonOccurrence(
    int OccurrenceId,
    int RootOccurrence,
    int ParentOccurrenceId,
    int Depth,
    int EdgeId,
    int SourceNodeId,
    int TargetNodeId,
    DependencyGraphJsonNodeIdentity SourceIdentity,
    string Relationship,
    DependencyGraphJsonNodeIdentity TargetIdentity,
    DependencyHierarchyOccurrenceDisposition Disposition,
    string Resolution,
    DependencyGraphJsonEvidenceIdentity? EvidenceIdentity,
    int? SourcePackageProjection,
    int? TargetPackageProjection,
    PackageDependencyTraversalEdgeEmissionAuthority? PackageEmissionAuthority,
    DependsPackageAuthorityFailureJson[]? PackageDiagnostics);

internal sealed record DependencyHierarchyJsonDepthBoundary(
    int OccurrenceId,
    int RootOccurrence,
    int NodeId,
    DependencyGraphJsonNodeIdentity NodeIdentity,
    int? PackageProjection,
    int MaximumDepth,
    DependencyGraphDepthBoundaryProducerKind Producer);

internal sealed record DependencyHierarchyJsonDocument(
    DependencyHierarchyJsonRoot[] Roots,
    DependencyHierarchyJsonOccurrence[] Occurrences,
    DependencyHierarchyJsonDepthBoundary[] DepthBoundaries,
    DependencyGraphJsonPackageProjection[] PackageProjections);

internal sealed record DependencyHierarchyJsonLine(
    int OccurrenceId,
    int RootOccurrence,
    int ParentOccurrenceId,
    int EdgeId,
    int Depth,
    string SourceKind,
    string SourceIdentity,
    string Source,
    string Relationship,
    string TargetKind,
    string TargetIdentity,
    string Target,
    string Disposition,
    string Resolution,
    string? EvidenceKind,
    string? EvidenceIdentity);

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DependencyHierarchyJsonDocument))]
[JsonSerializable(typeof(DependencyHierarchyJsonLine))]
internal partial class DependencyHierarchyJsonContext :
    JsonSerializerContext;

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(DependencyHierarchyJsonDocument))]
[JsonSerializable(typeof(DependencyHierarchyJsonLine))]
internal partial class DependencyHierarchyCompactJsonContext :
    JsonSerializerContext;

internal static class DependencyHierarchyOutputAdapter
{
    internal static List<DependencyHierarchyOccurrenceRow> Rows(
        DependencyHierarchyDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        DependencyGraphDocument graph = document.BackingGraph;
        Dictionary<DependencyHierarchyOccurrenceIdentity,
            HierarchyOccurrenceContext> contextsByOccurrence =
            ContextsByOccurrence(document);
        Dictionary<int, DependencyGraphEdge> edgesById =
            graph.Edges.ToDictionary(static edge => edge.Id);

        return
        [
            .. document.Occurrences.Select(occurrence =>
            {
                DependencyGraphEdge edge =
                    edgesById[occurrence.IncomingEdgeId];
                int sourceNodeId =
                    contextsByOccurrence[occurrence.ParentIdentity].NodeId;
                DependencyGraphNode source = graph.Nodes[sourceNodeId];
                DependencyGraphNode target =
                    graph.Nodes[occurrence.TargetNodeId];
                InertString sourceIdentity =
                    DependencyGraphOutputAdapter.IdentityText(
                        source.Identity);
                InertString targetIdentity =
                    DependencyGraphOutputAdapter.IdentityText(
                        target.Identity);
                InertString? evidenceIdentity =
                    DependencyGraphOutputAdapter.EvidenceText(
                        edge.EvidenceIdentity);
                return new DependencyHierarchyOccurrenceRow(
                    occurrence.Identity.Value,
                    occurrence.RootOccurrence.Value,
                    occurrence.ParentIdentity.Value,
                    edge.Id,
                    sourceNodeId,
                    occurrence.TargetNodeId,
                    occurrence.Depth,
                    DependencyGraphOutputAdapter.Kind(source.Identity),
                    sourceIdentity.ToString(),
                    source.Label.ToString(),
                    edge.Relationship,
                    DependencyGraphOutputAdapter.Kind(target.Identity),
                    targetIdentity.ToString(),
                    target.Label.ToString(),
                    occurrence.Disposition.ToString().ToLowerInvariant(),
                    edge.Resolution.ToString().ToLowerInvariant(),
                    DependencyGraphOutputAdapter.EvidenceKind(
                        edge.EvidenceIdentity),
                    evidenceIdentity?.ToString())
                {
                    SourceIdentityText = sourceIdentity,
                    SourceText = source.Label,
                    TargetIdentityText = targetIdentity,
                    TargetText = target.Label,
                    EvidenceIdentityText = evidenceIdentity,
                };
            }),
        ];
    }

    internal static void Write(
        DependencyHierarchyDocument document,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows,
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
        DependencyHierarchyDocument document,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows,
        IMarkoutFormatter formatter,
        bool markWindowedFragments)
    {
        var writer = new MarkoutWriter(Console.Out, formatter);
        writer.WriteGraph(
            ToGraph(document, rows, markWindowedFragments));
        writer.Flush();
    }

    private static void WriteTable(
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows,
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
                "Occurrence",
                "Root",
                "Parent Occurrence",
                "Edge ID",
                "Depth",
                "Source Kind",
                "Source Identity",
                "Source",
                "Relationship",
                "Target Kind",
                "Target Identity",
                "Target",
                "Disposition",
                "Resolution",
                "Evidence Kind",
                "Evidence Identity",
            ],
            [
                "occurrence",
                "root",
                "parent_occurrence",
                "edge_id",
                "depth",
                "source_kind",
                "source_identity",
                "source",
                "relationship",
                "target_kind",
                "target_identity",
                "target",
                "disposition",
                "resolution",
                "evidence_kind",
                "evidence_identity",
            ],
            [
                .. rows.Select(row => new[]
                {
                    row.OccurrenceId.ToString(
                        CultureInfo.InvariantCulture),
                    row.RootOccurrence.ToString(
                        CultureInfo.InvariantCulture),
                    row.ParentOccurrenceId.ToString(
                        CultureInfo.InvariantCulture),
                    row.EdgeId.ToString(CultureInfo.InvariantCulture),
                    row.Depth.ToString(CultureInfo.InvariantCulture),
                    row.SourceKind,
                    row.SourceIdentity,
                    row.Source,
                    row.Relationship,
                    row.TargetKind,
                    row.TargetIdentity,
                    row.Target,
                    row.Disposition,
                    row.Resolution,
                    row.EvidenceKind ?? "",
                    row.EvidenceIdentity ?? "",
                }),
            ]);
        writer.Flush();
    }

    private static void WriteJsonLines(
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows)
    {
        foreach (DependencyHierarchyOccurrenceRow row in rows)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new DependencyHierarchyJsonLine(
                        row.OccurrenceId,
                        row.RootOccurrence,
                        row.ParentOccurrenceId,
                        row.EdgeId,
                        row.Depth,
                        row.SourceKind,
                        row.SourceIdentity,
                        row.Source,
                        row.Relationship,
                        row.TargetKind,
                        row.TargetIdentity,
                        row.Target,
                        row.Disposition,
                        row.Resolution,
                        row.EvidenceKind,
                        row.EvidenceIdentity),
                    DependencyHierarchyCompactJsonContext.Default
                        .DependencyHierarchyJsonLine));
        }
    }

    private static void WriteJson(
        DependencyHierarchyDocument document,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows,
        bool compact)
    {
        DependencyHierarchyJsonDocument json =
            CreateJsonDocument(document, rows);
        Console.WriteLine(
            JsonSerializer.Serialize(
                json,
                compact
                    ? DependencyHierarchyCompactJsonContext.Default
                        .DependencyHierarchyJsonDocument
                    : DependencyHierarchyJsonContext.Default
                        .DependencyHierarchyJsonDocument));
    }

    internal static DependencyHierarchyJsonDocument CreateJsonDocument(
        DependencyHierarchyDocument document,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows,
        DependencyEvidenceSourceTokens? tokens = null,
        bool includePackageSelectionEvidence = true,
        bool includePackageDeclarationEvidence = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rows);
        DependencyGraphDocument graph = document.BackingGraph;
        tokens ??= DependencyGraphOutputAdapter.CreatePackageTokens(graph);
        Dictionary<int, DependencyGraphEdge> edgesById =
            graph.Edges.ToDictionary(static edge => edge.Id);
        Dictionary<DependencyHierarchyOccurrenceIdentity,
            HierarchyOccurrenceContext> contextsByOccurrence =
            ContextsByOccurrence(document);
        HashSet<DependencyHierarchyOccurrenceIdentity> contextOccurrences =
        [
            .. document.Roots.Select(static root => root.Identity),
            .. rows.Select(row => Identity(row)),
            .. rows.Select(row => ParentIdentity(row)),
        ];
        HashSet<int> selectedNodeIds =
        [
            .. contextOccurrences.Select(identity =>
                contextsByOccurrence[identity].NodeId),
        ];
        Dictionary<DependencyHierarchyOccurrenceIdentity,
            DependencyHierarchyOccurrence> occurrencesByIdentity =
            document.Occurrences.ToDictionary(
                static occurrence => occurrence.Identity);

        return new DependencyHierarchyJsonDocument(
            [
                .. document.Roots.Select(root =>
                {
                    DependencyGraphNode node = graph.Nodes[root.NodeId];
                    return new DependencyHierarchyJsonRoot(
                        root.Identity.Value,
                        root.RootOccurrence.Value,
                        root.RootPosition,
                        root.NodeId,
                        DependencyGraphOutputAdapter.JsonIdentity(
                            node.Identity),
                        node.Label.ToString());
                }),
            ],
            [
                .. rows.Select(row =>
                {
                    DependencyGraphEdge edge = edgesById[row.EdgeId];
                    DependencyHierarchyOccurrence occurrence =
                        occurrencesByIdentity[Identity(row)];
                    return new DependencyHierarchyJsonOccurrence(
                        row.OccurrenceId,
                        row.RootOccurrence,
                        row.ParentOccurrenceId,
                        row.Depth,
                        row.EdgeId,
                        row.SourceNodeId,
                        row.TargetNodeId,
                        DependencyGraphOutputAdapter.JsonIdentity(
                            graph.Nodes[row.SourceNodeId].Identity),
                        row.Relationship,
                        DependencyGraphOutputAdapter.JsonIdentity(
                            graph.Nodes[row.TargetNodeId].Identity),
                        occurrence.Disposition,
                        row.Resolution,
                        DependencyGraphOutputAdapter.JsonEvidenceIdentity(
                            edge.EvidenceIdentity),
                        edge.SourcePackageProjectionId,
                        edge.TargetPackageProjectionId,
                        edge.PackageEmissionAuthority,
                        edge.RuntimePackageDiagnostics.IsDefaultOrEmpty
                            ? null
                            : [
                                .. edge.RuntimePackageDiagnostics.Select(
                                    diagnostic =>
                                        DependsPackageAuthorityFailureJson
                                            .Create(diagnostic, tokens)),
                            ]);
                }),
            ],
            [
                .. graph.DepthBoundaries
                    .SelectMany(boundary =>
                        boundary.RootOccurrences.Select(rootOccurrence =>
                            BoundaryOccurrence(
                                document,
                                contextsByOccurrence,
                                boundary,
                                rootOccurrence))
                            .Where(static identity => identity is not null)
                            .Select(identity => (
                                Boundary: boundary,
                                Identity: identity!.Value)))
                    .Where(item =>
                        contextOccurrences.Contains(item.Identity))
                    .Select(item => new DependencyHierarchyJsonDepthBoundary(
                        item.Identity.Value,
                        item.Identity.RootOccurrence.Value,
                        item.Boundary.NodeId,
                        DependencyGraphOutputAdapter.JsonIdentity(
                            graph.Nodes[item.Boundary.NodeId].Identity),
                        item.Boundary.PackageProjectionId,
                        item.Boundary.MaximumDepth,
                        item.Boundary.Producer)),
            ],
            [
                .. graph.PackageProjections
                    .Where(projection =>
                        selectedNodeIds.Contains(projection.NodeId))
                    .Select(projection =>
                        DependencyGraphOutputAdapter.JsonPackageProjection(
                            projection,
                            tokens,
                            includePackageSelectionEvidence,
                            includePackageDeclarationEvidence)),
            ]);
    }

    internal static Markout.Graph ToGraph(
        DependencyHierarchyDocument document,
        IReadOnlyList<DependencyHierarchyOccurrenceRow> rows,
        bool markWindowedFragments)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(rows);
        DependencyGraphDocument graph = document.BackingGraph;
        Dictionary<DependencyHierarchyOccurrenceIdentity,
            HierarchyOccurrenceContext> contextsByOccurrence =
            ContextsByOccurrence(document);
        HashSet<DependencyHierarchyOccurrenceIdentity> selected =
        [
            .. rows.Select(Identity),
        ];
        var graphNodes = new List<Markout.GraphNode>();
        var graphEdges = new List<Markout.GraphEdge>();
        var emittedNodes =
            new HashSet<DependencyHierarchyOccurrenceIdentity>();

        foreach (DependencyHierarchyRootOccurrence root in document.Roots)
        {
            AddNode(
                root.Identity,
                root.NodeId,
                emphasized: true,
                marker: null,
                includeBoundary: true);
        }

        foreach (DependencyHierarchyOccurrenceRow row in rows)
        {
            DependencyHierarchyOccurrenceIdentity parent =
                ParentIdentity(row);
            AddNode(
                parent,
                row.SourceNodeId,
                emphasized: parent.Value == 0,
                marker:
                    markWindowedFragments
                    && parent.Value != 0
                    && !selected.Contains(parent)
                        ? "(fragment) "
                        : null,
                includeBoundary: true);

            DependencyHierarchyOccurrenceIdentity identity =
                Identity(row);
            AddNode(
                identity,
                row.TargetNodeId,
                emphasized: false,
                marker: row.Disposition switch
                {
                    "revisit" => "(revisit) ",
                    "cycle" => "(cycle) ",
                    _ => null,
                },
                includeBoundary: row.Disposition == "expanded");
            graphEdges.Add(
                new Markout.GraphEdge(Key(parent), Key(identity))
                {
                    Label = row.Relationship,
                });
        }

        return new Markout.Graph(graphNodes, graphEdges);

        void AddNode(
            DependencyHierarchyOccurrenceIdentity identity,
            int nodeId,
            bool emphasized,
            string? marker,
            bool includeBoundary)
        {
            if (!emittedNodes.Add(identity))
                return;
            int[] depths = includeBoundary
                ? BoundaryDepths(
                    graph,
                    contextsByOccurrence[identity],
                    identity.RootOccurrence.Value)
                : [];
            string boundaryMarker = depths.Length switch
            {
                0 => "",
                1 => $"(bounded at depth {depths[0]}) ",
                _ => $"(bounded at depths {string.Join(", ", depths)}) ",
            };
            graphNodes.Add(
                new Markout.GraphNode(
                    Key(identity),
                    $"{marker}{boundaryMarker}{graph.Nodes[nodeId].Label}")
                {
                    Emphasized = emphasized,
                });
        }
    }

    private static Dictionary<DependencyHierarchyOccurrenceIdentity,
        HierarchyOccurrenceContext> ContextsByOccurrence(
            DependencyHierarchyDocument document)
    {
        DependencyGraphDocument graph = document.BackingGraph;
        Dictionary<int, DependencyGraphEdge> edgesById =
            graph.Edges.ToDictionary(static edge => edge.Id);
        Dictionary<int, int> rootPackageProjections =
            graph.PackageProjections
                .Where(static projection =>
                    projection.RootOccurrence is not null)
                .ToDictionary(
                    static projection => projection.RootOccurrence!.Value,
                    static projection => projection.Id);
        var result =
            new Dictionary<DependencyHierarchyOccurrenceIdentity,
                HierarchyOccurrenceContext>();
        foreach (DependencyHierarchyRootOccurrence root in document.Roots)
        {
            result.Add(
                root.Identity,
                new HierarchyOccurrenceContext(
                    root.NodeId,
                    rootPackageProjections.TryGetValue(
                        root.RootOccurrence.Value,
                        out int projectionId)
                            ? projectionId
                            : null));
        }
        foreach (DependencyHierarchyOccurrence occurrence in
                 document.Occurrences)
        {
            result.Add(
                occurrence.Identity,
                new HierarchyOccurrenceContext(
                    occurrence.TargetNodeId,
                    edgesById[occurrence.IncomingEdgeId]
                        .TargetPackageProjectionId));
        }
        return result;
    }

    private static DependencyHierarchyOccurrenceIdentity?
        BoundaryOccurrence(
            DependencyHierarchyDocument document,
            IReadOnlyDictionary<DependencyHierarchyOccurrenceIdentity,
                HierarchyOccurrenceContext> contextsByOccurrence,
            DependencyGraphDepthBoundary boundary,
            int rootOccurrence)
    {
        var boundaryContext = new HierarchyOccurrenceContext(
            boundary.NodeId,
            boundary.PackageProjectionId);
        DependencyHierarchyRootOccurrence? root =
            document.Roots.FirstOrDefault(candidate =>
                candidate.RootOccurrence.Value == rootOccurrence
                && contextsByOccurrence[candidate.Identity]
                    == boundaryContext);
        if (root is not null)
            return root.Identity;

        return document.Occurrences
            .FirstOrDefault(occurrence =>
                occurrence.RootOccurrence.Value == rootOccurrence
                && contextsByOccurrence[occurrence.Identity]
                    == boundaryContext
                && occurrence.Disposition
                    == DependencyHierarchyOccurrenceDisposition.Expanded)
            ?.Identity;
    }

    private static int[] BoundaryDepths(
        DependencyGraphDocument graph,
        HierarchyOccurrenceContext context,
        int rootOccurrence) =>
    [
        .. graph.DepthBoundaries
            .Where(boundary =>
                boundary.NodeId == context.NodeId
                && boundary.PackageProjectionId
                    == context.PackageProjectionId
                && boundary.RootOccurrences.Contains(rootOccurrence))
            .Select(static boundary => boundary.MaximumDepth)
            .Distinct()
            .Order(),
    ];

    private static DependencyHierarchyOccurrenceIdentity Identity(
        DependencyHierarchyOccurrenceRow row) =>
        new(
            new DependencyRootOccurrenceIdentity(row.RootOccurrence),
            row.OccurrenceId);

    private static DependencyHierarchyOccurrenceIdentity ParentIdentity(
        DependencyHierarchyOccurrenceRow row) =>
        new(
            new DependencyRootOccurrenceIdentity(row.RootOccurrence),
            row.ParentOccurrenceId);

    private static string Key(
        DependencyHierarchyOccurrenceIdentity identity) =>
        $"r{identity.RootOccurrence.Value}:o{identity.Value}";

    private readonly record struct HierarchyOccurrenceContext(
        int NodeId,
        int? PackageProjectionId);
}
