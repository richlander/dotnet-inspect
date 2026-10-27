using DotnetInspector.Queries;
using ILInspector.Research;

using Markout;
using Markout.Formatting;

namespace DotnetInspect.Cli.Output;

internal static class LibraryDependencyStructureOutputAdapter
{
    internal static void WriteMermaid(
        LibraryDependencyStructureQueryResult.Available available,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var writer = new MarkoutWriter(
            output,
            new MermaidFormatter());
        writer.WriteGraph(ToGraph(available));
        writer.Flush();
    }

    internal static Markout.Graph ToGraph(
        LibraryDependencyStructureQueryResult.Available available)
    {
        ArgumentNullException.ThrowIfNull(available);

        IReadOnlyList<LibraryDependencyNamespaceEdge> edges =
            available.Rows.NamespaceEdges;
        HashSet<string>? selectedNamespaces =
            edges.Count == available.Document.NamespaceEdges.Length
                ? null
                :
                [
                    .. edges.SelectMany(static edge =>
                    new[]
                    {
                        edge.SourceNamespace,
                        edge.TargetNamespace,
                    }),
                ];

        List<Markout.GraphNode> nodes =
        [
            .. available.Document.Namespaces
                .Where(node =>
                    selectedNamespaces is null
                    || selectedNamespaces.Contains(node.Namespace))
                .Select(node => new Markout.GraphNode(
                    Key(node.Namespace),
                    Label(node, available.Document.Cycles.Length))
                {
                    Emphasized = node.CycleIndex is not null,
                }),
        ];
        List<Markout.GraphEdge> graphEdges =
        [
            .. edges.Select(static edge => new Markout.GraphEdge(
                Key(edge.SourceNamespace),
                Key(edge.TargetNamespace))
            {
                Label = EdgeLabel(edge),
            }),
        ];

        return new Markout.Graph(nodes, graphEdges);
    }

    /// <summary>
    /// Names one cycle by its 1-based position among all cycles, so the tag
    /// never reads as a count ("cycle 1 of 1", not "cycle 0").
    /// </summary>
    internal static string CycleName(int cycleIndex, int cycleCount) =>
        $"cycle {cycleIndex + 1} of {cycleCount}";

    internal static string NamespaceName(string @namespace) =>
        @namespace.Length == 0 ? "<global namespace>" : @namespace;

    private static string Label(
        LibraryDependencyNamespaceNode node,
        int cycleCount)
    {
        string name = NamespaceName(node.Namespace);
        string cycle = node.CycleIndex is { } index
            ? $", {CycleName(index, cycleCount)}"
            : "";
        string typeLabel = node.TypeCount == 1 ? "type" : "types";
        return $"{name} ({node.TypeCount} {typeLabel}, "
            + $"level {node.Level}{cycle})";
    }

    private static string EdgeLabel(LibraryDependencyNamespaceEdge edge)
    {
        LibraryDependencyCounts counts = edge.Counts;
        return $"{counts.Total} {Pluralize(counts.Total, "relationship")} "
            + $"({counts.Invocations} "
            + $"{Pluralize(counts.Invocations, "call")}, "
            + $"{counts.FunctionReferences} "
            + $"{Pluralize(
                counts.FunctionReferences,
                "function reference")})";
    }

    private static string Key(string @namespace) =>
        $"namespace:{@namespace}";

    private static string Pluralize(int count, string singular) =>
        count == 1 ? singular : singular + "s";
}
