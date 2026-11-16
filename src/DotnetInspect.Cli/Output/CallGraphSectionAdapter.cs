using DotnetInspect.Cli.Options;
using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Analysis;
using ILInspector.CallGraph;

namespace DotnetInspect.Cli.Output;

internal readonly record struct CallGraphOpportunityAnnotations(
    int AsyncAlternatives);

internal readonly record struct CallGraphRenderedFieldEvidence(
    IReadOnlySet<CallGraphField> GraphFields,
    IReadOnlySet<CallGraphField> FromFields,
    IReadOnlySet<CallGraphField> ToFields)
{
    internal static CallGraphRenderedFieldEvidence Empty { get; } =
        new(
            new HashSet<CallGraphField>(),
            new HashSet<CallGraphField>(),
            new HashSet<CallGraphField>());
}

internal readonly record struct CallGraphSectionOutput(
    Markout.Graph Graph,
    CallGraphRenderedFieldEvidence RenderedFieldEvidence);

/// <summary>
/// Spells the selected Member Call Graph representation for CLI output.
/// Document graphs use Markout's graph lowerings; typed edge Tables and
/// owner-issued hierarchy occurrences retain their own row/topology contracts.
/// </summary>
/// <remarks>
/// <para>
/// This is the third layer of the call-graph pipeline: <c>ILInspector</c> owns the graph facts and
/// knows no output format, Markout owns the shape and knows no call-graph vocabulary, and this
/// adapter owns everything in between — how a member is spelled, which analysis cues a
/// <c>--fields</c> request projects onto a label, which nodes are noteworthy, and how a node is
/// grouped. Neither neighbour needs to learn the other's concerns.
/// </para>
/// <para>
/// All representations share member spelling, grouping, and annotation policy.
/// Tree topology comes from the shared hierarchy owner, and Table output emits
/// selected directed edges without promoting isolated graph nodes to data rows.
/// </para>
/// </remarks>
internal static class CallGraphSectionAdapter
{
    /// <summary>
    /// Builds the section's graph.
    /// </summary>
    /// <param name="document">The complete semantic graph document.</param>
    /// <param name="projection">The projected bidirectional graph centred on the selected member.</param>
    /// <param name="spellMember">
    /// The CLI's member spelling. The projection offers a host-neutral default label, but this
    /// command already owns how a member is written everywhere else in its output, so the graph
    /// uses the same spelling rather than a second one.
    /// </param>
    /// <param name="requestedFields">
    /// Call Graph fields resolved from the <c>--fields</c>/<c>-D</c>
    /// selection.
    /// </param>
    /// <param name="hasFieldProjection">
    /// Whether the command has an explicit field or column projection. An
    /// empty <paramref name="requestedFields"/> then means the projection did
    /// not request graph fields, not that default graph cues were requested.
    /// </param>
    public static CallGraphSectionOutput ToGraph(
        InspectionGraphDocument document,
        CallGraphProjection projection,
        Func<MemberRef, string> spellMember,
        IReadOnlyList<CallGraphField>? requestedFields = null,
        bool hasFieldProjection = false,
        IReadOnlyList<CallGraphRow>? rows = null,
        IReadOnlyList<CallGraphRow>? evidenceRows = null,
        bool includeFocusInEvidence = false,
        IReadOnlyDictionary<int, CallGraphOpportunityAnnotations>?
            opportunityAnnotations = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(spellMember);
        if (document.Nodes.Length != projection.Nodes.Length
            || document.Edges.Length != projection.Rows.Length)
        {
            throw new InvalidOperationException(
                "The semantic Call Graph document does not match its presentation projection.");
        }

        foreach (InspectionGraphNode semanticNode in document.Nodes)
        {
            CallGraphNode node = projection.Nodes[semanticNode.Id];
            if (semanticNode.Subject
                    is not InspectionGraphSubject.MemberSubject
                    {
                        Identity:
                            InspectionGraphMemberIdentity.CallGraph identity,
                    }
                || identity.Member != node.Member)
            {
                throw new InvalidOperationException(
                    "The semantic Call Graph node does not match its presentation projection.");
            }
        }
        foreach (CallGraphRow row in rows ?? projection.Rows)
        {
            InspectionGraphEdge semanticEdge = document.Edges[row.Number - 1];
            if (semanticEdge.FromNodeId != row.Edge.From
                || semanticEdge.ToNodeId != row.Edge.To
                || semanticEdge.Relationship.Id
                    != CallGraphInspectionGraphCatalog.Call.Id)
            {
                throw new InvalidOperationException(
                    "The semantic Call Graph edge does not match its presentation projection.");
            }
        }

        int focusNodeId = document.Seeds
            .Single(seed =>
                seed.Role == InspectionGraphSeedRole.Primary
                && seed.Target.Kind == InspectionGraphTargetKind.Node)
            .Target.Id;

        return ToGraphCore(
            focusNodeId,
            projection.Nodes,
            projection.Rows,
            spellMember,
            requestedFields,
            hasFieldProjection,
            rows,
            evidenceRows,
            includeFocusInEvidence,
            opportunityAnnotations);
    }

    public static CallGraphSectionOutput ToGraph(
        MemberCallGraphHierarchy hierarchy,
        Func<MemberRef, string> spellMember,
        IReadOnlyList<CallGraphField>? requestedFields,
        bool hasFieldProjection,
        IReadOnlyDictionary<int, CallGraphOpportunityAnnotations>?
            opportunityAnnotations) =>
        ToGraphCore(
            hierarchy.Focus.Id,
            hierarchy.Nodes,
            hierarchy.Rows,
            spellMember,
            requestedFields,
            hasFieldProjection,
            includeFocusInEvidence: true,
            opportunityAnnotations: opportunityAnnotations);

    public static CallGraphSectionOutput ToGraph(
        MemberCallGraphTable table,
        Func<MemberRef, string> spellMember,
        IReadOnlyList<CallGraphField>? requestedFields,
        bool hasFieldProjection,
        IReadOnlyDictionary<int, CallGraphOpportunityAnnotations>?
            opportunityAnnotations) =>
        ToGraphCore(
            table.Focus.Id,
            table.Nodes,
            [.. table.Rows.Select(static row => row.Relationship)],
            spellMember,
            requestedFields,
            hasFieldProjection,
            opportunityAnnotations: opportunityAnnotations);

    private static CallGraphSectionOutput ToGraphCore(
        int focusNodeId,
        IReadOnlyList<CallGraphNode> sourceNodes,
        IReadOnlyList<CallGraphRow> sourceRows,
        Func<MemberRef, string> spellMember,
        IReadOnlyList<CallGraphField>? requestedFields,
        bool hasFieldProjection,
        IReadOnlyList<CallGraphRow>? rows = null,
        IReadOnlyList<CallGraphRow>? evidenceRows = null,
        bool includeFocusInEvidence = false,
        IReadOnlyDictionary<int, CallGraphOpportunityAnnotations>?
            opportunityAnnotations = null)
    {
        var selectedRows = rows ?? sourceRows;
        HashSet<int>? selectedNodeIds = null;
        if (rows is not null)
        {
            selectedNodeIds = [focusNodeId];
            foreach (var row in selectedRows)
            {
                selectedNodeIds.Add(row.Edge.From);
                selectedNodeIds.Add(row.Edge.To);
            }
        }

        var nodes = new List<Markout.GraphNode>(
            selectedNodeIds?.Count ?? sourceNodes.Count);
        foreach (CallGraphNode node in sourceNodes)
        {
            if (selectedNodeIds is not null && !selectedNodeIds.Contains(node.Id))
                continue;

            nodes.Add(new Markout.GraphNode(
                Key(node.Id),
                Label(
                    node,
                    spellMember,
                    requestedFields,
                    hasFieldProjection,
                    opportunityAnnotations,
                    node.Kind))
            {
                Group = Group(node.Kind),
                // The selected member is what the reader asked about; every sink that can
                // distinguish it should.
                Emphasized = node.Id == focusNodeId,
            });
        }

        var edges = new List<Markout.GraphEdge>(selectedRows.Count);
        foreach (CallGraphRow row in selectedRows)
        {
            CallGraphEdge edge = row.Edge;
            edges.Add(
                new Markout.GraphEdge(
                    Key(edge.From),
                    Key(edge.To))
                {
                    Label = LoopLabel(edge),
                });
        }

        var graphFields = new HashSet<CallGraphField>();
        var fromFields = new HashSet<CallGraphField>();
        var toFields = new HashSet<CallGraphField>();
        if (hasFieldProjection && requestedFields is { Count: > 0 })
        {
            var fromEvidenceNodeIds = new HashSet<int>();
            var toEvidenceNodeIds = new HashSet<int>();
            foreach (CallGraphRow row in evidenceRows ?? selectedRows)
            {
                fromEvidenceNodeIds.Add(row.Edge.From);
                toEvidenceNodeIds.Add(row.Edge.To);
            }

            foreach (CallGraphNode node in sourceNodes)
            {
                bool isFromEvidence = fromEvidenceNodeIds.Contains(node.Id);
                bool isToEvidence = toEvidenceNodeIds.Contains(node.Id);
                bool isGraphEvidence =
                    isFromEvidence
                    || isToEvidence
                    || includeFocusInEvidence
                    && node.Id == focusNodeId;
                if (!isGraphEvidence)
                    continue;

                foreach (CallGraphField field in requestedFields)
                {
                    if (Annotation(node.Perf, field) is not null
                        || opportunityAnnotations is not null
                        && opportunityAnnotations.TryGetValue(
                            node.Id,
                            out CallGraphOpportunityAnnotations opportunities)
                        && OpportunityAnnotation(opportunities, field) is not null)
                    {
                        graphFields.Add(field);
                        if (isFromEvidence)
                            fromFields.Add(field);
                        if (isToEvidence)
                            toFields.Add(field);
                    }
                }
            }
        }

        return new CallGraphSectionOutput(
            new Markout.Graph(
                nodes,
                edges,
                focusKey: Key(focusNodeId)),
            new CallGraphRenderedFieldEvidence(
                graphFields,
                fromFields,
                toFields));
    }

    // The projection's dense ids are the node identity. They are opaque to Markout and never
    // emitted, so they carry no display meaning and need none.
    private static string Key(int id) => id.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Groups a node by how it relates to the analyzed assembly set, which is the distinction a
    /// reader needs when a graph crosses a library boundary. In-assembly nodes are ungrouped so a
    /// single-library graph stays a plain graph with no clustering and no extra table columns.
    /// </summary>
    internal static string HierarchyLabel(
        MemberCallGraphHierarchyNode node,
        string memberLabel)
    {
        string prefix = node.IsReference ? "(revisit) " : "";
        string? edgeLabel = node.Relationship is { } relationship
            ? LoopLabel(relationship.Edge)
            : null;
        return $"{prefix}{memberLabel}"
            + (edgeLabel is null ? "" : $" ({edgeLabel})");
    }

    internal static (string[] Headers, string[] Keys, string[][] Rows) ToTable(
        MemberCallGraphTable table,
        Markout.Graph graph)
    {
        // Generic graph tables also emit isolated nodes; these rows are edges only.
        bool grouped = table.Nodes.Any(static node =>
            node.Kind == CallGraphNodeKind.External);
        var headers = new List<string> { "From" };
        if (grouped)
            headers.Add("From Group");
        headers.Add("To");
        if (grouped)
            headers.Add("To Group");
        if (table.HasLoopEdges)
            headers.Add("Label");
        var labels = graph.Nodes.ToDictionary(
            static node => node.Key,
            static node => node.Label);
        string[][] rows =
        [
            .. table.Rows.Select(row =>
            {
                var cells = new List<string> { labels[Key(row.From.Id)] };
                if (grouped)
                    cells.Add(Group(row.From.Kind) ?? "");
                cells.Add(labels[Key(row.To.Id)]);
                if (grouped)
                    cells.Add(Group(row.To.Kind) ?? "");
                if (table.HasLoopEdges)
                    cells.Add(LoopLabel(row.Relationship.Edge) ?? "");
                return cells.ToArray();
            }),
        ];
        return (
            headers.ToArray(),
            [.. headers.Select(static header =>
                header.ToLowerInvariant().Replace(' ', '_'))],
            rows);
    }

    private static string? LoopLabel(CallGraphEdge edge) =>
        !edge.AnyCallInLoop
            ? null
            : edge.CallSiteIds.IsEmpty
                && !string.IsNullOrEmpty(edge.LegacyLoopHint)
                    ? edge.LegacyLoopHint
                    : edge.Origin == CallGraphEdgeOrigin.Callers
                        ? "loop call"
                        : "loop";

    private static string? Group(CallGraphNodeKind role) => role switch
    {
        CallGraphNodeKind.External => "External",
        _ => null,
    };

    private static string Label(
        CallGraphNode node,
        Func<MemberRef, string> spellMember,
        IReadOnlyList<CallGraphField>? requestedFields,
        bool hasFieldProjection,
        IReadOnlyDictionary<int, CallGraphOpportunityAnnotations>?
            opportunityAnnotations,
        CallGraphNodeKind role)
    {
        var member = spellMember(node.Member);
        var suffixes = new List<string>();

        switch (role)
        {
            // A boundary node is a place the graph stopped, not a leaf. Say so, or a reader reads
            // a depth limit as "this method calls nothing".
            case CallGraphNodeKind.Truncated:
                suffixes.Add("…");
                break;
            // Grouping clusters this node in sinks that draw containers, but a tree or a plain
            // table has no container to draw, so the fact also has to survive in the label.
            case CallGraphNodeKind.External:
                suffixes.Add("external");
                break;
        }

        if (hasFieldProjection)
        {
            foreach (CallGraphField field in requestedFields ?? [])
            {
                if (Annotation(node.Perf, field) is { } annotation)
                    suffixes.Add(annotation);
                if (opportunityAnnotations is not null
                    && opportunityAnnotations.TryGetValue(
                        node.Id,
                        out CallGraphOpportunityAnnotations opportunities)
                    && OpportunityAnnotation(
                        opportunities,
                        field) is { } opportunityAnnotation)
                {
                    suffixes.Add(opportunityAnnotation);
                }
            }
        }
        else if (node.Perf is { } perf)
        {
            if (perf.Fanout > 0)
                suffixes.Add($"fanout {perf.Fanout}");
            if (perf.Fanin > 0)
                suffixes.Add($"fanin {perf.Fanin}");
            if (perf.MaxDepth > 1)
                suffixes.Add($"depth {perf.MaxDepth}");
            if (!string.IsNullOrEmpty(perf.RootKind))
                suffixes.Add(perf.RootKind);
            if (!string.IsNullOrEmpty(perf.Source))
                suffixes.Add($"from {perf.Source}");
        }

        return suffixes.Count > 0 ? $"{member} ({string.Join(", ", suffixes)})" : member;
    }

    static string? OpportunityAnnotation(
        CallGraphOpportunityAnnotations opportunities,
        CallGraphField field) =>
        field == CallGraphField.AsyncAlternatives
            && opportunities.AsyncAlternatives > 0
                ? $"async alternatives {opportunities.AsyncAlternatives}"
                : null;

    private static string? RootAnnotation(CallTreePerf perf)
    {
        // The Root field combines the reverse-graph classification (target/entrypoint) with the
        // source assembly for callers pulled in from the --bin/--project/--caller-package scope, so
        // reach evidence can name the caller library when requested.
        var parts = new List<string>(2);
        if (!string.IsNullOrEmpty(perf.RootKind))
            parts.Add(perf.RootKind);
        if (!string.IsNullOrEmpty(perf.Source))
            parts.Add($"from {perf.Source}");
        return parts.Count > 0 ? string.Join(" ", parts) : null;
    }

    private static string? Annotation(
        CallTreePerf? perf,
        CallGraphField field)
    {
        if (perf is null)
            return null;

        var signals = perf.SignalsOrNone;
        return field switch
        {
            CallGraphField.Fanin => $"fanin {perf.Fanin}",
            CallGraphField.Fanout => $"fanout {perf.Fanout}",
            CallGraphField.Depth => $"depth {perf.MaxDepth}",
            CallGraphField.Loop => perf.InLoop
                ? perf.LoopHint ?? "loop"
                : null,
            CallGraphField.Root => RootAnnotation(perf),
            CallGraphField.Source => perf.Source is { } source
                ? $"from {source}"
                : null,
            CallGraphField.Allocations => signals.Allocations > 0
                ? $"alloc {signals.Allocations}"
                : null,
            CallGraphField.Copies => signals.Copies > 0
                ? $"copy {signals.Copies}"
                : null,
            CallGraphField.Unsafe => signals.Unsafe ? "unsafe" : null,
            CallGraphField.Reflection => signals.Reflection > 0
                ? $"reflection {signals.Reflection}"
                : null,
            CallGraphField.Throws => signals.Throws > 0
                ? $"throw {signals.Throws}"
                : null,
            CallGraphField.Catches => signals.Catches > 0
                ? $"catch {signals.Catches}"
                : null,
            CallGraphField.Finallys => signals.Finallys > 0
                ? $"finally {signals.Finallys}"
                : null,
            CallGraphField.ExceptionTypes =>
                signals.ExceptionTypes.Length > 0
                ? "exceptions " + string.Join(",", signals.ExceptionTypes)
                : null,
            CallGraphField.EvidenceIL => EvidenceIL(signals),
            _ => null,
        };
    }

    // Compact IL receipts for the projected signals: the offsets of the signal-bearing
    // instructions (newobj/newarr/throw/ldftn/reflection calls).
    private static string? EvidenceIL(MethodSignals signals)
    {
        var offsets = signals.Evidence;
        if (offsets.Length == 0)
            return null;
        return "il " + string.Join(",", offsets.Select(offset => $"IL_{offset:X4}"));
    }
}
