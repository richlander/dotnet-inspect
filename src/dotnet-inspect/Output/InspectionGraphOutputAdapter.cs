using System.Globalization;
using System.Text.Json;
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
    string? SourceAssembly,
    string? SourceGroup,
    string Relationship,
    string Target,
    string? TargetAssembly,
    string? TargetGroup,
    int Occurrences,
    string? Evidence);

internal sealed record InspectionGraphJsonLine(
    string Source,
    string? SourceAssembly,
    string? SourceGroup,
    string Relationship,
    string Target,
    string? TargetAssembly,
    string? TargetGroup,
    int Occurrences,
    string? Evidence);

internal sealed record InspectionGraphFailureRow(
    string Failure,
    string Target,
    string Detail);

internal sealed record InspectionGraphJsonFailureDetail(
    string Producer,
    string Kind,
    InspectionGraphJsonAssemblyReference? Reference,
    string? AcquisitionFailureKind,
    string? AcquisitionFailureDetail,
    string? ErrorType,
    string? ErrorMessage);

internal sealed record InspectionGraphJsonAssemblyReference(
    string Name,
    string? Version,
    string? Culture,
    string? PublicKeyToken);

internal sealed record InspectionGraphJsonFailure(
    string Failure,
    string Target,
    string? TargetKind,
    int? TargetId,
    InspectionGraphJsonFailureDetail[] Details);

internal sealed record InspectionGraphJsonNode(
    int Id,
    string Kind,
    string Label,
    string? Assembly,
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
    string? SourceAssembly,
    string? SourceGroup,
    string Relationship,
    string Target,
    string? TargetAssembly,
    string? TargetGroup,
    int Occurrences,
    string? Evidence);

internal sealed record InspectionGraphJsonDocument(
    string Mode,
    string Admission,
    string[] Subjects,
    string[] Relationships,
    InspectionGraphJsonNode[] Nodes,
    InspectionGraphJsonGroup[] Groups,
    InspectionGraphJsonEdge[] Edges,
    InspectionGraphJsonFailure[] Failures);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(InspectionGraphJsonDocument))]
[JsonSerializable(typeof(InspectionGraphJsonLine))]
internal partial class InspectionGraphJsonContext : JsonSerializerContext;

internal sealed class InspectionGraphOutputAdapter
{
    readonly IReadOnlyDictionary<
        AssemblyAcquisitionRegistration,
        string> _assemblyLabels;

    internal InspectionGraphOutputAdapter(
        WorkspaceContextLoadOutcome.Loaded context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _assemblyLabels =
            context.Group.Participants.ToDictionary(
                static participant =>
                    participant.Assembly.Registration,
                static participant =>
                    CSharpIdentifier.ContainRenderedText(
                        AssemblyIdentityFormatter.Format(
                            participant.Assembly.Identity)));
    }

    internal List<InspectionGraphEdgeRow> EdgeRows(
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
                    AssemblyLabel(source.Subject),
                    GroupLabel(document, source),
                    edge.Relationship.Id,
                    Label(target.Subject),
                    AssemblyLabel(target.Subject),
                    GroupLabel(document, target),
                    edge.OccurrenceIds.Length,
                    Evidence(document, edge));
            }),
        ];
    }

    internal List<InspectionGraphFailureRow> FailureRows(
        InspectionGraphDocument document) =>
        [
            .. document.Failures.Select(failure =>
                new InspectionGraphFailureRow(
                    failure.Descriptor.Id,
                    Target(document, failure.Target),
                    FailureDetail(failure))),
        ];

    internal InspectionGraphJsonDocument Json(
        InspectionGraphDocument document,
        IReadOnlyList<InspectionGraphEdgeRow> selectedRows)
    {
        InspectionGraphEdge[] edges = SelectedEdges(
            document,
            selectedRows);
        HashSet<int> selectedNodeIds = SelectedNodeIds(edges);
        selectedNodeIds.UnionWith(
            document.Failures
                .Where(static failure =>
                    failure.Target
                        is { Kind: InspectionGraphTargetKind.Node })
                .Select(static failure => failure.Target!.Value.Id));
        HashSet<int> selectedGroupIds = SelectedGroupIds(
            document,
            selectedNodeIds,
            edges.Length > 0 || document.Edges.IsEmpty);
        selectedGroupIds.UnionWith(
            document.Failures
                .Where(static failure =>
                    failure.Target
                        is { Kind: InspectionGraphTargetKind.Group })
                .Select(static failure => failure.Target!.Value.Id));
        AddGroupAncestors(document, selectedGroupIds);
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
                        AssemblyLabel(node.Subject),
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
                        AssemblyLabel(
                            document.Nodes[edge.FromNodeId].Subject),
                        GroupLabel(
                            document,
                            document.Nodes[edge.FromNodeId]),
                        edge.Relationship.Id,
                        Label(document.Nodes[edge.ToNodeId].Subject),
                        AssemblyLabel(
                            document.Nodes[edge.ToNodeId].Subject),
                        GroupLabel(
                            document,
                            document.Nodes[edge.ToNodeId]),
                        edge.OccurrenceIds.Length,
                        Evidence(document, edge))),
            ],
            [.. document.Failures.Select(failure =>
                JsonFailure(document, failure))]);
    }

    internal void WriteMarkdown(
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
                document.Edges.IsEmpty
                    ? "No selected Integration relationships were found within the package set."
                    : "No Integration relationships are selected by the row window.");
            if (document.Edges.IsEmpty)
            {
                if (embeddedMermaid)
                {
                    writer.WriteGraph(
                        ToGraph(
                            document,
                            rows,
                            includeIsolatedPackages: true,
                            includeGroupInNodeLabel: false));
                }
                else
                {
                    writer.WriteList(
                        [
                            .. document.InducedSetRequest!.Subjects
                                .Select(Label),
                        ]);
                }
            }
        }
        else
        {
            writer.WriteGraph(
                ToGraph(
                    document,
                    rows,
                    includeIsolatedPackages: embeddedMermaid));
        }
        WriteFailures(writer, document);
        writer.Flush();
    }

    internal void WriteGraph(
        InspectionGraphDocument document,
        IReadOnlyList<InspectionGraphEdgeRow> rows,
        IMarkoutFormatter formatter,
        bool includeGroupInNodeLabel)
    {
        var writer = new MarkoutWriter(Console.Out, formatter);
        writer.WriteGraph(
            ToGraph(
                document,
                rows,
                includeIsolatedPackages: true,
                includeGroupInNodeLabel));
        writer.Flush();
    }

    internal void WriteTable(
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
            [
                "Source",
                "Source Assembly",
                "Source Group",
                "Relationship",
                "Target",
                "Target Assembly",
                "Target Group",
                "Occurrences",
                "Evidence",
            ],
            [
                "source",
                "source_assembly",
                "source_group",
                "relationship",
                "target",
                "target_assembly",
                "target_group",
                "occurrences",
                "evidence",
            ],
            [
                .. rows.Select(row => new[]
                {
                    row.Source,
                    row.SourceAssembly ?? "",
                    row.SourceGroup ?? "",
                    row.Relationship,
                    row.Target,
                    row.TargetAssembly ?? "",
                    row.TargetGroup ?? "",
                    row.Occurrences.ToString(CultureInfo.InvariantCulture),
                    row.Evidence ?? "",
                }),
            ]);
        writer.Flush();
    }

    internal static void WriteJsonLines(
        IReadOnlyList<InspectionGraphEdgeRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        foreach (InspectionGraphEdgeRow row in rows)
        {
            var jsonLine = new InspectionGraphJsonLine(
                row.Source,
                row.SourceAssembly,
                row.SourceGroup,
                row.Relationship,
                row.Target,
                row.TargetAssembly,
                row.TargetGroup,
                row.Occurrences,
                row.Evidence);
            Console.WriteLine(
                JsonSerializer.Serialize(
                    jsonLine,
                    InspectionGraphJsonContext.Default
                        .InspectionGraphJsonLine));
        }
    }

    Markout.Graph ToGraph(
        InspectionGraphDocument document,
        IReadOnlyList<InspectionGraphEdgeRow> rows,
        bool includeIsolatedPackages,
        bool includeGroupInNodeLabel = false)
    {
        InspectionGraphEdge[] selectedEdges = SelectedEdges(
            document,
            rows);
        HashSet<int> selectedNodeIds = SelectedNodeIds(selectedEdges);
        HashSet<int> representedGroupIds =
        [
            .. document.Nodes
                .Where(node => selectedNodeIds.Contains(node.Id))
                .SelectMany(static node => node.GroupIds),
        ];

        var nodes = new List<Markout.GraphNode>();
        foreach (InspectionGraphNode node in document.Nodes)
        {
            if (!selectedNodeIds.Contains(node.Id))
                continue;
            string? group = node.GroupIds.Length == 0
                ? null
                : Label(document.Groups[node.GroupIds[0]].Subject);
            string label = EndpointLabel(node.Subject);
            if (includeGroupInNodeLabel && group is not null)
                label = $"{label} [{group}]";
            nodes.Add(
                new Markout.GraphNode(Key(node.Id), label)
                {
                    Group = group,
                });
        }

        if (includeIsolatedPackages
            && (selectedEdges.Length > 0 || document.Edges.IsEmpty))
        {
            foreach (InspectionGraphGroup group in document.Groups)
            {
                if (group.Subject
                    is InspectionGraphSubject.PackageSubject package
                    && document.InducedSetRequest!.Subjects.Contains(
                        package)
                    && !representedGroupIds.Contains(group.Id))
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
        IReadOnlySet<int> selectedNodeIds,
        bool includeRequestedPackages)
    {
        HashSet<int> selected =
        [
            .. document.Nodes
                .Where(node => selectedNodeIds.Contains(node.Id))
                .SelectMany(static node => node.GroupIds),
        ];
        if (includeRequestedPackages)
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

        AddGroupAncestors(document, selected);
        return selected;
    }

    static void AddGroupAncestors(
        InspectionGraphDocument document,
        HashSet<int> selected)
    {
        foreach (int groupId in selected.ToArray())
        {
            int? parentId = document.Groups[groupId].ParentId;
            while (parentId is int id)
            {
                selected.Add(id);
                parentId = document.Groups[id].ParentId;
            }
        }
    }

    InspectionGraphJsonFailure JsonFailure(
        InspectionGraphDocument document,
        InspectionGraphFailure failure)
    {
        InspectionGraphTarget? target = failure.Target;
        InspectionGraphJsonFailureDetail[] details =
            failure.Evidence
                is InspectionGraphIntegrationFailureEvidence evidence
                ?
                [
                    .. evidence.Details.Select(static detail =>
                        new InspectionGraphJsonFailureDetail(
                            detail.Producer,
                            detail.Kind.ToString(),
                            detail.Reference is null
                                ? null
                                : new InspectionGraphJsonAssemblyReference(
                                    ContainRequired(
                                        detail.Reference.Name),
                                    detail.Reference.Version?.ToString(),
                                    Contain(detail.Reference.Culture),
                                    Contain(
                                        detail.Reference.PublicKeyToken)),
                            detail.AcquisitionFailure?.Kind.ToString(),
                            Contain(
                                detail.AcquisitionFailure?.Detail),
                            Contain(
                                detail.Error?.GetType().FullName),
                            Contain(detail.Error?.Message))),
                ]
                : [];
        return new InspectionGraphJsonFailure(
            failure.Descriptor.Id,
            Target(document, target),
            target?.Kind.ToString(),
            target?.Id,
            details);
    }

    static string? Contain(string? value) =>
        value is null
            ? null
            : ContainRequired(value);

    static string ContainRequired(string value) =>
        CSharpIdentifier.ContainRenderedText(value);

    static string? GroupLabel(
        InspectionGraphDocument document,
        InspectionGraphNode node) =>
        node.GroupIds.IsEmpty
            ? null
            : string.Join(
                ", ",
                node.GroupIds
                    .Select(id => Label(document.Groups[id].Subject))
                    .Distinct(StringComparer.Ordinal));

    void WriteFailures(
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
                .Select(static detail => FailureDetail(detail))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
    }

    static string FailureDetail(
        InspectionGraphIntegrationFailureDetail detail)
    {
        var parts = new List<string>
        {
            $"{detail.Producer}: {detail.Kind}",
        };
        if (detail.Reference is not null)
        {
            parts.Add(
                "reference "
                + CSharpIdentifier.ContainRenderedText(
                    AssemblyIdentityFormatter.Format(detail.Reference)));
        }
        if (detail.AcquisitionFailure is not null)
        {
            parts.Add(
                "acquisition "
                + detail.AcquisitionFailure.Kind
                + ": "
                + CSharpIdentifier.ContainRenderedText(
                    detail.AcquisitionFailure.Detail));
        }
        if (detail.Error is not null)
        {
            parts.Add(
                detail.Error.GetType().Name
                + ": "
                + CSharpIdentifier.ContainRenderedText(
                    detail.Error.Message));
        }
        return string.Join("; ", parts);
    }

    string Target(
        InspectionGraphDocument document,
        InspectionGraphTarget? target) =>
        target switch
        {
            null => "graph",
            { Kind: InspectionGraphTargetKind.Node, Id: var id } =>
                EndpointLabel(document.Nodes[id].Subject),
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

    string EndpointLabel(InspectionGraphSubject subject)
    {
        string label = Label(subject);
        string? assembly = AssemblyLabel(subject);
        return assembly is null
            ? label
            : $"{label} [{assembly}]";
    }

    string? AssemblyLabel(InspectionGraphSubject subject) =>
        subject switch
        {
            InspectionGraphSubject.MemberSubject
            {
                Identity:
                    InspectionGraphMemberIdentity.AcquiredApi member,
            } => AssemblyLabel(member.Registration),
            InspectionGraphSubject.TypeSubject
            {
                Identity:
                    InspectionGraphTypeIdentity.AcquiredDefinition type,
            } => AssemblyLabel(type.Registration),
            InspectionGraphSubject.TypeSubject
            {
                Identity:
                    InspectionGraphTypeIdentity.Structural type,
            } => string.IsNullOrWhiteSpace(type.Type.Assembly)
                ? null
                : CSharpIdentifier.ContainRenderedText(
                    type.Type.Assembly),
            InspectionGraphSubject.AssemblySubject
            {
                Identity:
                    InspectionGraphAssemblyIdentity.Acquired assembly,
            } => CSharpIdentifier.ContainRenderedText(
                AssemblyIdentityFormatter.Format(assembly.Assembly)),
            InspectionGraphSubject.AssemblySubject
            {
                Identity:
                    InspectionGraphAssemblyIdentity.Metadata assembly,
            } => CSharpIdentifier.ContainRenderedText(
                AssemblyIdentityFormatter.Format(assembly.Assembly)),
            _ => null,
        };

    string? AssemblyLabel(
        AssemblyAcquisitionRegistration registration) =>
        _assemblyLabels.GetValueOrDefault(registration);

    static string Key(int id) =>
        id.ToString(CultureInfo.InvariantCulture);
}
