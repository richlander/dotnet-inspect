using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.CSharp;
using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Output;

internal static class ExternalCallGraphOutputAdapter
{
    const string Boundary = "boundary";
    const string Connector = "connector";
    const string UnclassifiedBoundary = "unclassified-boundary";

    public static int Write(
        InspectionGraphDocument document,
        ExternalCallGraphOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);

        if (!CliSemanticRowSelection.TrySelectOrApplyLegacy(
                options.RowSelection,
                options.Rows,
                BuildRows(document),
                "External call graph",
                failure =>
                    $"External call graph row selection stage "
                    + $"{failure.Failure.StageNumber} requires edge "
                    + $"{failure.Failure.RequiredPosition}, but only "
                    + $"{failure.Failure.AvailableCount} edges are available.",
                out IReadOnlyList<ExternalCallGraphRow> rows))
        {
            return 1;
        }

        if (options.Count)
        {
            CountOutput.WriteCount(rows.Count);
        }
        else if (options.Tree)
        {
            WriteGraph(
                document,
                rows,
                OutputFormat.PlainText,
                GraphTitle(options));
        }
        else
        {
            switch (options.Format)
            {
                case OutputFormat.Markdown:
                    WriteGraph(
                        document,
                        rows,
                        OutputFormat.Markdown,
                        GraphTitle(options),
                        embeddedMermaid: options.EmbeddedMermaid);
                    break;
                case OutputFormat.PlainText:
                    WriteGraph(
                        document,
                        rows,
                        OutputFormat.PlainText,
                        GraphTitle(options));
                    break;
                case OutputFormat.Mermaid:
                    WriteGraph(
                        document,
                        rows,
                        OutputFormat.Mermaid,
                        GraphTitle(options));
                    break;
                case OutputFormat.Table:
                    WriteTable(rows, options.NoHeader);
                    break;
                case OutputFormat.Tsv:
                    WriteTsv(rows, options.NoHeader);
                    break;
                case OutputFormat.Jsonl:
                    WriteJsonl(rows);
                    break;
                case OutputFormat.Json:
                    WriteJson(document, rows);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(options),
                        options.Format,
                        "Unsupported output format.");
            }
        }

        WriteLimits(document);
        WriteFailures(document);
        return document.Failures.IsEmpty ? 0 : 1;
    }

    static ExternalCallGraphRow[] BuildRows(
        InspectionGraphDocument document)
    {
        IReadOnlyDictionary<int, InspectionGraphNode> nodes =
            document.Nodes.ToDictionary(static node => node.Id);
        IReadOnlyDictionary<int, InspectionGraphOccurrence> occurrences =
            document.Occurrences.ToDictionary(
                static occurrence => occurrence.Id);
        ILookup<int, InspectionGraphCharacteristic> characteristics =
            document.Characteristics
                .Where(static characteristic =>
                    characteristic.Target.Kind
                        == InspectionGraphTargetKind.Edge)
                .ToLookup(static characteristic =>
                    characteristic.Target.Id);

        var rows = new ExternalCallGraphRow[document.Edges.Length];
        for (int index = 0; index < document.Edges.Length; index++)
        {
            InspectionGraphEdge edge = document.Edges[index];
            MemberNode source = ReadMember(nodes[edge.FromNodeId]);
            MemberNode target = ReadMember(nodes[edge.ToNodeId]);
            string role = ReadRole(edge, characteristics[edge.Id]);
            CallGraphCallSiteEvidence[] receipts =
            [
                .. edge.OccurrenceIds
                    .Select(id => occurrences[id].Evidence)
                    .OfType<CallGraphCallSiteEvidence>()
                    .OrderBy(static evidence =>
                        evidence.CallerModuleVersionId)
                    .ThenBy(static evidence =>
                        evidence.CallerMethodToken)
                    .ThenBy(static evidence => evidence.ILOffset)
                    .ThenBy(static evidence =>
                        evidence.OperandToken),
            ];
            rows[index] = new ExternalCallGraphRow(
                edge.Id,
                source.Label,
                source.Assembly,
                role,
                target.Label,
                target.Assembly,
                receipts.Length,
                string.Join(
                    " | ",
                    receipts.Select(FormatReceipt)));
        }

        return rows;
    }

    static string ReadRole(
        InspectionGraphEdge edge,
        IEnumerable<InspectionGraphCharacteristic> characteristics)
    {
        InspectionGraphCharacteristic[] roles =
        [
            .. characteristics.Where(static characteristic =>
                characteristic.Descriptor.Id
                    == ExternalFocusedCallGraphInspectionCatalog
                        .EdgeRole.Id),
        ];
        if (roles.Length != 1
            || roles[0].Value is not InspectionGraphValue.Token token
            || token.Value
                is not (Boundary
                    or Connector
                    or UnclassifiedBoundary))
        {
            throw new InspectionQueryException(
                $"External-focused edge {edge.Id} must carry exactly one supported role.");
        }

        return token.Value;
    }

    static MemberNode ReadMember(InspectionGraphNode node)
    {
        if (node.Subject
                is not InspectionGraphSubject.MemberSubject
                {
                    Identity:
                        InspectionGraphMemberIdentity.CallGraph callGraph,
                })
        {
            throw new InspectionQueryException(
                $"External-focused node {node.Id} is not a call-graph member.");
        }

        return new MemberNode(
            node.Id,
            CSharpIdentifier.ContainRenderedText(
                callGraph.Member.ToQualifiedDisplayString()),
            CSharpIdentifier.ContainRenderedText(
                ReadDefinitionAssembly(
                    callGraph.Member.DeclaringType)));
    }

    static string ReadDefinitionAssembly(TypeRef type)
    {
        while (type.Kind == TypeRefKind.GenericInstance
            && type.ElementType is not null)
        {
            type = type.ElementType;
        }

        return type.Assembly;
    }

    static string FormatReceipt(CallGraphCallSiteEvidence receipt) =>
        $"{receipt.CallerModuleVersionId:D} "
        + $"method=0x{receipt.CallerMethodToken:X8} "
        + $"il=IL_{receipt.ILOffset:X4} "
        + $"operand=0x{receipt.OperandToken:X8} "
        + $"call={receipt.CallKind} "
        + $"dispatch={receipt.DispatchKind} "
        + $"loop={receipt.InLoop.ToString().ToLowerInvariant()}";

    static void WriteGraph(
        InspectionGraphDocument document,
        IReadOnlyList<ExternalCallGraphRow> rows,
        OutputFormat format,
        string title,
        bool embeddedMermaid = false)
    {
        Markout.Graph graph = BuildGraph(document, rows);
        if (format == OutputFormat.Markdown)
        {
            Console.Write("# ");
            Console.WriteLine(title);
            Console.WriteLine();
            WriteFocus(document);
            Console.WriteLine();
            if (rows.Count == 0)
            {
                Console.WriteLine(
                    "No out-of-baseline package calls were found in the explicit package context.");
                return;
            }
        }

        IMarkoutFormatter formatter = format switch
        {
            OutputFormat.Mermaid => new MermaidFormatter(),
            OutputFormat.PlainText => new PlainTextFormatter(),
            _ => new MarkdownFormatter(
                embeddedMermaid
                    ? MarkdownGraphMode.Mermaid
                    : MarkdownGraphMode.EdgeTable),
        };
        var writer = new MarkoutWriter(Console.Out, formatter);
        writer.WriteGraph(graph);
        writer.Flush();
    }

    static string GraphTitle(ExternalCallGraphOptions options) =>
        options.SupplyChainBaseline
            is MemberCallGraphSupplyChainBaseline.Nothing
            ? "External Call Graph"
            : "Supply Chain Call Graph";

    static void WriteFocus(InspectionGraphDocument document)
    {
        InspectionGraphSeed seed =
            document.Seeds.Single(static seed =>
                seed.Role == InspectionGraphSeedRole.Primary);
        InspectionGraphNode node = document.Nodes.Single(item =>
            seed.Target.Kind == InspectionGraphTargetKind.Node
            && item.Id == seed.Target.Id);
        MemberNode member = ReadMember(node);
        Console.WriteLine(
            $"Focus: `{member.Label}`");
    }

    static Markout.Graph BuildGraph(
        InspectionGraphDocument document,
        IReadOnlyList<ExternalCallGraphRow> rows)
    {
        InspectionGraphSeed seed =
            document.Seeds.Single(static item =>
                item.Role == InspectionGraphSeedRole.Primary);
        HashSet<int> selectedEdgeIds =
        [
            .. rows.Select(static row => row.EdgeId),
        ];
        InspectionGraphEdge[] edges =
        [
            .. document.Edges.Where(edge =>
                selectedEdgeIds.Contains(edge.Id)),
        ];
        HashSet<int> nodeIds =
        [
            seed.Target.Id,
            .. edges.SelectMany(static edge =>
                new[] { edge.FromNodeId, edge.ToNodeId }),
        ];
        IReadOnlyDictionary<int, InspectionGraphNode> nodes =
            document.Nodes.ToDictionary(static node => node.Id);

        Markout.GraphNode[] graphNodes =
        [
            .. nodeIds
                .Order()
                .Select(nodeId =>
                {
                    MemberNode member = ReadMember(nodes[nodeId]);
                    return new Markout.GraphNode(
                        $"N{member.Id}",
                        member.Label)
                    {
                        Group =
                            string.IsNullOrWhiteSpace(member.Assembly)
                                ? null
                                : member.Assembly,
                        Emphasized = nodeId == seed.Target.Id,
                    };
                }),
        ];
        Markout.GraphEdge[] graphEdges =
        [
            .. edges.Select(edge =>
            {
                string role = ReadRole(
                    edge,
                    document.Characteristics.Where(characteristic =>
                        characteristic.Target.Kind
                            == InspectionGraphTargetKind.Edge
                        && characteristic.Target.Id == edge.Id));
                int callSites = edge.OccurrenceIds.Count(id =>
                    document.Occurrences[id].Evidence
                        is CallGraphCallSiteEvidence);
                return new Markout.GraphEdge(
                    $"N{edge.FromNodeId}",
                    $"N{edge.ToNodeId}")
                {
                    Label = $"{role} ({callSites})",
                };
            }),
        ];

        return new Markout.Graph(
            graphNodes,
            graphEdges,
            focusKey: $"N{seed.Target.Id}");
    }

    static void WriteTable(
        IReadOnlyList<ExternalCallGraphRow> rows,
        bool noHeader)
    {
        var writer = new MarkoutWriter(
            Console.Out,
            new TableFormatter(!noHeader));
        writer.WriteTable(
            [
                "Source",
                "Source Assembly",
                "Role",
                "Target",
                "Target Assembly",
                "Call Sites",
                "Evidence",
            ],
            [
                "source",
                "source_assembly",
                "role",
                "target",
                "target_assembly",
                "call_sites",
                "evidence",
            ],
            [
                .. rows.Select(static row => new[]
                {
                    row.Source,
                    row.SourceAssembly,
                    row.Role,
                    row.Target,
                    row.TargetAssembly,
                    row.CallSites.ToString(),
                    row.Evidence,
                }),
            ]);
        writer.Flush();
    }

    static void WriteTsv(
        IReadOnlyList<ExternalCallGraphRow> rows,
        bool noHeader)
    {
        var writer = new MarkoutWriter(
            Console.Out,
            new TableFormatter(!noHeader),
            OutputFormatter.CreateTableWriterOptions(
                tsv: true,
                jsonl: false));
        writer.WriteTable(
            [
                "Source",
                "Source Assembly",
                "Role",
                "Target",
                "Target Assembly",
                "Call Sites",
                "Evidence",
            ],
            [
                "source",
                "source_assembly",
                "role",
                "target",
                "target_assembly",
                "call_sites",
                "evidence",
            ],
            [
                .. rows.Select(static row => new[]
                {
                    row.Source,
                    row.SourceAssembly,
                    row.Role,
                    row.Target,
                    row.TargetAssembly,
                    row.CallSites.ToString(),
                    row.Evidence,
                }),
            ]);
        writer.Flush();
    }

    static void WriteJsonl(
        IReadOnlyList<ExternalCallGraphRow> rows)
    {
        foreach (ExternalCallGraphRow row in rows)
        {
            Console.WriteLine(
                JsonSerializer.Serialize(
                    row,
                    ExternalCallGraphJsonContext.Default
                        .ExternalCallGraphRow));
        }
    }

    static void WriteJson(
        InspectionGraphDocument document,
        IReadOnlyList<ExternalCallGraphRow> rows)
    {
        InspectionGraphSeed seed =
            document.Seeds.Single(static item =>
                item.Role == InspectionGraphSeedRole.Primary);
        MemberNode focus = ReadMember(
            document.Nodes.Single(node =>
                node.Id == seed.Target.Id));
        int maxNodes = AssertedMaxNodes(document);
        var projection = new ExternalCallGraphJsonDocument(
            focus.Label,
            focus.Assembly,
            "outgoing",
            document.NeighborhoodRequest!.MaxDepth,
            maxNodes,
            [.. rows],
            [
                .. document.Limits.Select(static limit =>
                    limit.Descriptor.Id).Distinct(StringComparer.Ordinal),
            ],
            [
                .. document.Failures.Select(static failure =>
                    failure.Descriptor.Id).Distinct(StringComparer.Ordinal),
            ]);
        Console.WriteLine(
            JsonSerializer.Serialize(
                projection,
                ExternalCallGraphJsonContext.Default
                    .ExternalCallGraphJsonDocument));
    }

    static int AssertedMaxNodes(InspectionGraphDocument document)
    {
        CallGraphTraversalNodeBoundEvidence[] evidence =
        [
            .. document.Limits
                .Where(static limit =>
                    limit.Descriptor.Id
                        == CallGraphInspectionGraphCatalog
                            .TraversalNodeBound.Id)
                .Select(static limit => limit.Evidence)
                .OfType<CallGraphTraversalNodeBoundEvidence>(),
        ];
        if (evidence.Length != 1)
        {
            throw new InspectionQueryException(
                "The external-focused graph must carry exactly one traversal node bound.");
        }

        return evidence[0].MaxNodes;
    }

    static void WriteLimits(InspectionGraphDocument document)
    {
        foreach (IGrouping<
            (string Id, string Evidence),
            InspectionGraphLimit> group in document.Limits
                .Where(static limit =>
                    limit.Descriptor.Id
                        is not ("queries.neighborhood-depth-bound"
                            or "call.traversal-node-bound"))
                .GroupBy(limit => (
                    limit.Descriptor.Id,
                    string.Join(
                        "\n",
                        FormatDiagnosticEvidence(limit.Evidence)))))
        {
            CommandError.WriteWarning(
                $"External call graph is incomplete: {group.Key.Id}.");
            if (group.Count() > 1)
            {
                CommandError.WriteLine(
                    $"  Affected graph targets: {group.Count()}");
            }
            foreach (string detail in group.Key.Evidence.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries))
            {
                CommandError.WriteLine($"  {detail}");
            }
        }
    }

    static void WriteFailures(InspectionGraphDocument document)
    {
        foreach (InspectionGraphFailure failure in document.Failures)
        {
            CommandError.Write(
                $"External call graph failed: {failure.Descriptor.Id}.",
                FormatDiagnosticEvidence(failure.Evidence));
        }
    }

    static string[] FormatDiagnosticEvidence(
        IInspectionGraphDiagnosticEvidence? evidence) =>
        evidence switch
        {
            null => [],
            CallGraphCorrespondenceIncompleteEvidence correspondence =>
            [
                $"Incomplete nodes: {correspondence.IncompleteNodeCount}",
                $"Incomplete edges: {correspondence.IncompleteEdgeCount}",
                $"Binding identity conflicts: {correspondence.BindingIdentityConflictCount}",
            ],
            _ => [$"Evidence: {evidence.Descriptor.Id}"],
        };

    sealed record MemberNode(
        int Id,
        string Label,
        string Assembly);

}

internal sealed record ExternalCallGraphRow(
    int EdgeId,
    string Source,
    string SourceAssembly,
    string Role,
    string Target,
    string TargetAssembly,
    int CallSites,
    string Evidence);

internal sealed record ExternalCallGraphJsonDocument(
    string Focus,
    string FocusAssembly,
    string Direction,
    int MaxDepth,
    int MaxNodes,
    ImmutableArray<ExternalCallGraphRow> Edges,
    ImmutableArray<string> Limits,
    ImmutableArray<string> Failures);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(ExternalCallGraphRow))]
[JsonSerializable(typeof(ExternalCallGraphJsonDocument))]
internal partial class ExternalCallGraphJsonContext
    : JsonSerializerContext;
