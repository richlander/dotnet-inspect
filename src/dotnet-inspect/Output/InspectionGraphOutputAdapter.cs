using System.Globalization;
using System.Text.Json.Serialization;

using DotnetInspector.Options;
using DotnetInspector.Queries;
using ILInspector.CSharp;
using ILInspector.Metadata;
using Markout;
using Markout.Formatting;

namespace DotnetInspector.Output;

internal sealed record InspectionGraphEdgeRow(
    int EdgeId,
    string Source,
    string Relationship,
    string Target,
    int Occurrences,
    string? Evidence);

internal sealed record InspectionGraphFailureRow(
    string Failure,
    string Target,
    string Detail);

internal sealed record InspectionGraphJsonNode(
    int Id,
    string Kind,
    string Label,
    string Role,
    int[] GroupIds);

internal sealed record InspectionGraphJsonGroup(
    int Id,
    string Kind,
    string Label,
    int? ParentId);

internal sealed record InspectionGraphJsonEdge(
    int Id,
    int FromNodeId,
    int ToNodeId,
    string Source,
    string Relationship,
    string Target,
    int[] OccurrenceIds,
    string? Evidence);

internal sealed record InspectionGraphJsonDocument(
    string Mode,
    string Admission,
    string[] Subjects,
    string[] Relationships,
    InspectionGraphJsonNode[] Nodes,
    InspectionGraphJsonGroup[] Groups,
    InspectionGraphJsonEdge[] Edges,
    InspectionGraphFailureRow[] Failures);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(InspectionGraphJsonDocument))]
internal partial class InspectionGraphJsonContext : JsonSerializerContext;

internal static class InspectionGraphOutputAdapter
{
    internal static List<InspectionGraphEdgeRow> EdgeRows(
        InspectionGraphDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return
        [
            .. document.Edges.Select(edge =>
            {
                InspectionGraphNode source =
                    document.Nodes[edge.FromNodeId];
                InspectionGraphNode target =
                    document.Nodes[edge.ToNodeId];
                return new InspectionGraphEdgeRow(
                    edge.Id,
                    Label(source.Subject),
                    edge.Relationship.Id,
                    Label(target.Subject),
                    edge.OccurrenceIds.Length,
                    Evidence(document, edge));
            }),
        ];
    }

    internal static List<InspectionGraphFailureRow> FailureRows(
        InspectionGraphDocument document) =>
        [
            .. document.Failures.Select(failure =>
                new InspectionGraphFailureRow(
                    failure.Descriptor.Id,
                    Target(document, failure.Target),
                    FailureDetail(failure))),
        ];

    internal static InspectionGraphJsonDocument Json(
        InspectionGraphDocument document,
        IReadOnlyList<InspectionGraphEdgeRow> selectedRows)
    {
        InspectionGraphEdge[] edges = SelectedEdges(
            document,
            selectedRows);
        HashSet<int> selectedNodeIds = SelectedNodeIds(edges);
        HashSet<int> selectedGroupIds = SelectedGroupIds(
            document,
            selectedNodeIds);
        return new InspectionGraphJsonDocument(
            "induced-set",
            document.InducedSetRequest!.AdmissionRule.ToString(),
            [
                .. document.InducedSetRequest.Subjects.Select(Label),
            ],
            [
                .. document.InducedSetRequest.Relationships.Select(
                    static relationship => relationship.Id),
            ],
            [
                .. document.Nodes
                    .Where(node => selectedNodeIds.Contains(node.Id))
                    .Select(node =>
                    new InspectionGraphJsonNode(
                        node.Id,
                        node.Subject.Kind.ToString(),
                        Label(node.Subject),
                        node.Role.ToString(),
                        [.. node.GroupIds])),
            ],
            [
                .. document.Groups
                    .Where(group => selectedGroupIds.Contains(group.Id))
                    .Select(group =>
                    new InspectionGraphJsonGroup(
                        group.Id,
                        group.Subject.Kind.ToString(),
                        Label(group.Subject),
                        group.ParentId)),
            ],
            [
                .. edges.Select(edge =>
                    new InspectionGraphJsonEdge(
                        edge.Id,
                        edge.FromNodeId,
                        edge.ToNodeId,
                        Label(document.Nodes[edge.FromNodeId].Subject),
                        edge.Relationship.Id,
                        Label(document.Nodes[edge.ToNodeId].Subject),
                        [.. edge.OccurrenceIds],
                        Evidence(document, edge))),
            ],
            [.. FailureRows(document)]);
    }

    internal static void WriteMarkdown(
        InspectionGraphDocument document,
        IReadOnlyList<InspectionGraphEdgeRow> rows,
        bool embeddedMermaid)
    {
        var formatter = new MarkdownFormatter(
            embeddedMermaid
                ? MarkdownGraphMode.Mermaid
                : MarkdownGraphMode.EdgeTable);
        var writer = new MarkoutWriter(Console.Out, formatter);
        writer.WriteHeading(1, "Integration graph");
        writer.WriteParagraph(
            "Explicit package set: "
            + string.Join(
                ", ",
                document.InducedSetRequest!.Subjects.Select(Label)));
        writer.WriteHeading(2, "Graph");
        if (rows.Count == 0)
        {
            writer.WriteParagraph(
                "No selected Integration relationships were found within the package set.");
        }
        else
        {
            writer.WriteGraph(ToGraph(document, rows));
        }
        WriteFailures(writer, document);
        writer.Flush();
    }

    internal static void WriteGraph(
        InspectionGraphDocument document,
        IReadOnlyList<InspectionGraphEdgeRow> rows,
        IMarkoutFormatter formatter)
    {
        var writer = new MarkoutWriter(Console.Out, formatter);
        writer.WriteGraph(ToGraph(document, rows));
        writer.Flush();
    }

    internal static void WriteTable(
        IReadOnlyList<InspectionGraphEdgeRow> rows,
        OutputFormat format,
        bool noHeader)
    {
        var writerOptions =
            OutputFormatter.CreateTableWriterOptions(
                tsv: format == OutputFormat.Tsv,
                jsonl: format == OutputFormat.Jsonl);
        var writer = new MarkoutWriter(
            Console.Out,
            new TableFormatter(!noHeader),
            writerOptions);
        writer.WriteTable(
            ["Source", "Relationship", "Target", "Occurrences", "Evidence"],
            ["source", "relationship", "target", "occurrences", "evidence"],
            [
                .. rows.Select(row => new[]
                {
                    row.Source,
                    row.Relationship,
                    row.Target,
                    row.Occurrences.ToString(CultureInfo.InvariantCulture),
                    row.Evidence ?? "",
                }),
            ]);
        writer.Flush();
    }

    static Markout.Graph ToGraph(
        InspectionGraphDocument document,
        IReadOnlyList<InspectionGraphEdgeRow> rows)
    {
        InspectionGraphEdge[] selectedEdges = SelectedEdges(
            document,
            rows);
        HashSet<int> selectedNodeIds = SelectedNodeIds(selectedEdges);

        var nodes = new List<Markout.GraphNode>();
        foreach (InspectionGraphNode node in document.Nodes)
        {
            if (!selectedNodeIds.Contains(node.Id))
                continue;
            string? group = node.GroupIds.Length == 0
                ? null
                : Label(document.Groups[node.GroupIds[0]].Subject);
            nodes.Add(
                new Markout.GraphNode(Key(node.Id), Label(node.Subject))
                {
                    Group = group,
                });
        }

        if (selectedNodeIds.Count == 0)
        {
            foreach (InspectionGraphGroup group in document.Groups)
            {
                if (group.Subject
                    is InspectionGraphSubject.PackageSubject package
                    && document.InducedSetRequest!.Subjects.Contains(
                        package))
                {
                    nodes.Add(
                        new Markout.GraphNode(
                            $"g{Key(group.Id)}",
                            Label(group.Subject)));
                }
            }
        }

        var edges = new List<Markout.GraphEdge>();
        foreach (InspectionGraphEdge edge in selectedEdges)
        {
            string? evidence = Evidence(document, edge);
            edges.Add(
                new Markout.GraphEdge(
                    Key(edge.FromNodeId),
                    Key(edge.ToNodeId))
                {
                    Label = evidence is null
                        ? edge.Relationship.Id
                        : $"{edge.Relationship.Id}: {evidence}",
                });
        }

        return new Markout.Graph(nodes, edges);
    }

    static InspectionGraphEdge[] SelectedEdges(
        InspectionGraphDocument document,
        IReadOnlyList<InspectionGraphEdgeRow> rows)
    {
        HashSet<int> selectedIds =
        [
            .. rows.Select(static row => row.EdgeId),
        ];
        return
        [
            .. document.Edges.Where(edge => selectedIds.Contains(edge.Id)),
        ];
    }

    static HashSet<int> SelectedNodeIds(
        IReadOnlyList<InspectionGraphEdge> edges) =>
        [
            .. edges.SelectMany(static edge =>
                new[] { edge.FromNodeId, edge.ToNodeId }),
        ];

    static HashSet<int> SelectedGroupIds(
        InspectionGraphDocument document,
        IReadOnlySet<int> selectedNodeIds)
    {
        HashSet<int> selected =
        [
            .. document.Nodes
                .Where(node => selectedNodeIds.Contains(node.Id))
                .SelectMany(static node => node.GroupIds),
        ];
        if (selectedNodeIds.Count == 0)
        {
            selected.UnionWith(
                document.Groups
                    .Where(group =>
                        group.Subject
                            is InspectionGraphSubject.PackageSubject
                                package
                        && document.InducedSetRequest!.Subjects.Contains(
                            package))
                    .Select(static group => group.Id));
        }

        foreach (int groupId in selected.ToArray())
        {
            int? parentId = document.Groups[groupId].ParentId;
            while (parentId is int id)
            {
                selected.Add(id);
                parentId = document.Groups[id].ParentId;
            }
        }
        return selected;
    }

    static void WriteFailures(
        MarkoutWriter writer,
        InspectionGraphDocument document)
    {
        List<InspectionGraphFailureRow> failures = FailureRows(document);
        if (failures.Count == 0)
            return;
        writer.WriteHeading(2, "Failures");
        writer.WriteTable(
            ["Failure", "Target", "Detail"],
            ["failure", "target", "detail"],
            [
                .. failures.Select(failure => new[]
                {
                    failure.Failure,
                    failure.Target,
                    failure.Detail,
                }),
            ]);
    }

    static string? Evidence(
        InspectionGraphDocument document,
        InspectionGraphEdge edge)
    {
        string[] values =
        [
            .. edge.OccurrenceIds
                .Select(id => document.Occurrences[id].Evidence)
                .Select(static evidence => evidence switch
                {
                    InspectionGraphIntegrationEvidence integration =>
                        integration.Integration,
                    InspectionGraphOpportunityEvidence opportunity =>
                        opportunity.Integration,
                    _ => null,
                })
                .OfType<string>()
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];
        return values.Length == 0 ? null : string.Join(", ", values);
    }

    static string FailureDetail(InspectionGraphFailure failure)
    {
        if (failure.Evidence
            is not InspectionGraphIntegrationFailureEvidence evidence)
        {
            return failure.Evidence?.Descriptor.Id
                ?? "No failure detail was supplied.";
        }

        return string.Join(
            ", ",
            evidence.Details
                .Select(static detail =>
                    $"{detail.Producer}: {detail.Kind}")
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
    }

    static string Target(
        InspectionGraphDocument document,
        InspectionGraphTarget? target) =>
        target switch
        {
            null => "graph",
            { Kind: InspectionGraphTargetKind.Node, Id: var id } =>
                Label(document.Nodes[id].Subject),
            { Kind: InspectionGraphTargetKind.Group, Id: var id } =>
                Label(document.Groups[id].Subject),
            { Kind: InspectionGraphTargetKind.Edge, Id: var id } =>
                $"edge {id}",
            { Kind: InspectionGraphTargetKind.Occurrence, Id: var id } =>
                $"occurrence {id}",
            _ => "graph",
        };

    static string Label(InspectionGraphSubject subject) =>
        CSharpIdentifier.ContainRenderedText(
            subject switch
            {
                InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.AcquiredApi member,
                } => member.Member.Format(
                    ILInspector.MetadataPrimitives.MemberAnchorFormat
                        .Qualified),
                InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.CallGraph member,
                } => member.Member.Name,
                InspectionGraphSubject.TypeSubject
                {
                    Identity:
                        InspectionGraphTypeIdentity.AcquiredDefinition type,
                } => type.Type.ToMetadataFullName(),
                InspectionGraphSubject.TypeSubject
                {
                    Identity:
                        InspectionGraphTypeIdentity.Structural type,
                } => type.Type.ToDisplayString(),
                InspectionGraphSubject.AssemblySubject
                {
                    Identity:
                        InspectionGraphAssemblyIdentity.Acquired assembly,
                } => assembly.Assembly.Name,
                InspectionGraphSubject.AssemblySubject
                {
                    Identity:
                        InspectionGraphAssemblyIdentity.Metadata assembly,
                } => assembly.Assembly.Name,
                InspectionGraphSubject.PackageSubject
                {
                    Identity:
                        InspectionGraphPackageIdentity.Realized package,
                } => $"{package.Package.PackageId}@{package.Package.Version}",
                _ => subject.Kind.ToString(),
            });

    static string Key(int id) =>
        id.ToString(CultureInfo.InvariantCulture);
}
