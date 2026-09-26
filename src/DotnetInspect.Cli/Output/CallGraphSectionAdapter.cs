using DotnetInspect.Cli.Options;
using DotnetInspector.Queries;
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
/// Turns the semantic <see cref="InspectionGraphDocument"/> into the generic
/// <see cref="Markout.Graph"/> shape the writer lowers per format. The producer
/// projection remains available only for presentation annotations and row
/// selection that are not L1 graph facts.
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
/// It emits one shape for every sink. A Mermaid diagram, a Markdown edge table, and a plain-text
/// tree are the same graph lowered three ways rather than three independently maintained
/// renderings that can disagree.
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

        int focusNodeId = document.Seeds
            .Single(seed =>
                seed.Role == InspectionGraphSeedRole.Primary
                && seed.Target.Kind == InspectionGraphTargetKind.Node)
            .Target.Id;

        var selectedRows = rows ?? projection.Rows;
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
            selectedNodeIds?.Count ?? document.Nodes.Length);
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
                    semanticNode.Role))
            {
                Group = Group(semanticNode.Role),
                // The selected member is what the reader asked about; every sink that can
                // distinguish it should.
                Emphasized = node.Id == focusNodeId,
            });
        }

        var edges = new List<Markout.GraphEdge>(selectedRows.Count);
        var selectedEdgeIds = selectedRows
            .Select(static row => row.Number - 1)
            .ToHashSet();
        foreach (InspectionGraphEdge semanticEdge in document.Edges)
        {
            if (!selectedEdgeIds.Contains(semanticEdge.Id))
                continue;
            CallGraphEdge edge = projection.Rows[semanticEdge.Id].Edge;
            if (semanticEdge.FromNodeId != edge.From
                || semanticEdge.ToNodeId != edge.To
                || semanticEdge.Relationship.Id
                    != CallGraphInspectionGraphCatalog.Call.Id)
            {
                throw new InvalidOperationException(
                    "The semantic Call Graph edge does not match its presentation projection.");
            }
            edges.Add(
                new Markout.GraphEdge(
                    Key(semanticEdge.FromNodeId),
                    Key(semanticEdge.ToNodeId))
                {
                    Label = edge.AnyCallInLoop
                        ? edge.CallSiteIds.IsEmpty
                            && !string.IsNullOrEmpty(
                                edge.LegacyLoopHint)
                                ? edge.LegacyLoopHint
                                : edge.Origin
                                    == CallGraphEdgeOrigin.Callers
                                    ? "loop call"
                                    : "loop"
                        : null,
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

            foreach (CallGraphNode node in projection.Nodes)
            {
                bool isFromEvidence = fromEvidenceNodeIds.Contains(node.Id);
                bool isToEvidence = toEvidenceNodeIds.Contains(node.Id);
                bool isGraphEvidence =
                    isFromEvidence
                    || isToEvidence
                    || includeFocusInEvidence
                    && node.Id == projection.Focus.Id;
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
    private static string? Group(InspectionGraphNodeRole role) => role switch
    {
        InspectionGraphNodeRole.External => "External",
        _ => null,
    };

    private static string Label(
        CallGraphNode node,
        Func<MemberRef, string> spellMember,
        IReadOnlyList<CallGraphField>? requestedFields,
        bool hasFieldProjection,
        IReadOnlyDictionary<int, CallGraphOpportunityAnnotations>?
            opportunityAnnotations,
        InspectionGraphNodeRole role)
    {
        var member = spellMember(node.Member);
        var suffixes = new List<string>();

        switch (role)
        {
            // A boundary node is a place the graph stopped, not a leaf. Say so, or a reader reads
            // a depth limit as "this method calls nothing".
            case InspectionGraphNodeRole.Truncated:
                suffixes.Add("…");
                break;
            // Grouping clusters this node in sinks that draw containers, but a tree or a plain
            // table has no container to draw, so the fact also has to survive in the label.
            case InspectionGraphNodeRole.External:
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
