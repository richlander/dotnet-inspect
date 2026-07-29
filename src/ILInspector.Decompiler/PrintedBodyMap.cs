using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler;

/// <summary>
/// Where one node's characters landed, in text coordinates.
/// </summary>
/// <param name="Kind">The node kind that printed these characters, e.g. <c>NewObject</c>.</param>
/// <param name="Line">0-based line within the printed body.</param>
/// <param name="Column">0-based column within <paramref name="Line"/>.</param>
/// <param name="Length">Length in characters.</param>
public readonly record struct PrintedNodeSpan(string Kind, int Line, int Column, int Length);

/// <summary>
/// One fact, positioned at the characters it is about.
/// </summary>
/// <param name="Descriptor">The fact family's id, e.g. <c>alloc.new</c>.</param>
/// <param name="Kind">The node kind the fact was found on.</param>
/// <param name="Line">0-based line within the printed body.</param>
/// <param name="Column">0-based column within <paramref name="Line"/>.</param>
/// <param name="Length">Length in characters, or <c>-1</c> when the fact could not be narrowed to a single line.</param>
/// <param name="Detail">Rendered specifics, e.g. the allocated type name.</param>
/// <param name="SourceOffset">IL offset of the originating instruction, or <c>-1</c> when unknown.</param>
public readonly record struct PrintedAnnotationSpan(
    string Descriptor,
    string Kind,
    int Line,
    int Column,
    int Length,
    string? Detail,
    int SourceOffset);

/// <summary>
/// A printed body plus the positions of everything known about it, in text
/// coordinates only.
/// </summary>
/// <remarks>
/// <para>
/// This is the map a consumer outside the decompiler can actually use. The rich
/// map the printer builds (<see cref="PrintedRangeMap"/>) is keyed by
/// <see cref="IrNode"/>, whose identity is the CLR object reference, so it is
/// only meaningful while its object graph is alive and in this process. Nothing
/// here is a reference: a line, a column, a length, and a name. It serialises,
/// travels, and replays.
/// </para>
/// <para>
/// It is also the separation of concerns the caret gesture wants. Rendering a
/// <c>^^^^</c> underline needs a position and a label, not an IR node, so a
/// renderer can consume this map alone — and the same map can be rendered as
/// side annotations, as carets, or as JSON, because the choice of gesture is the
/// printer's, not the datum's.
/// </para>
/// <para>
/// The two lists answer different questions and are deliberately not merged:
/// <see cref="Nodes"/> is the full structural picture of what printed where,
/// while <see cref="Annotations"/> is the much smaller set of facts worth
/// reporting. A caret renderer needs only the second; a tool correlating
/// structure to text needs the first.
/// </para>
/// </remarks>
/// <param name="Lines">The printed body, split into lines.</param>
/// <param name="Nodes">Every node whose characters could be placed on a single line.</param>
/// <param name="Annotations">Every fact, positioned at the narrowest node that printed on one line.</param>
public sealed record PrintedBodyMap(
    IReadOnlyList<string> Lines,
    IReadOnlyList<PrintedNodeSpan> Nodes,
    IReadOnlyList<PrintedAnnotationSpan> Annotations)
{
    /// <summary>
    /// Orders facts by position, then by everything else that can distinguish
    /// two of them.
    /// </summary>
    /// <remarks>
    /// The tail comparisons are not decoration. Facts arrive keyed by a
    /// dictionary, whose enumeration order is not a contract, and
    /// <see cref="List{T}.Sort(Comparison{T})"/> is not stable, so any pair the
    /// comparison leaves equal may come out in either order — and the payload
    /// would then differ between two runs over identical input, which later reads
    /// as a real change. Totality is the property that makes the serialised form
    /// reproducible, so it is tested directly rather than inferred from a sort
    /// that happens to agree today.
    /// </remarks>
    internal static int Compare(PrintedAnnotationSpan a, PrintedAnnotationSpan b)
    {
        int c = a.Line.CompareTo(b.Line);
        if (c != 0) return c;
        c = a.Column.CompareTo(b.Column);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Descriptor, b.Descriptor);
        if (c != 0) return c;
        c = a.SourceOffset.CompareTo(b.SourceOffset);
        if (c != 0) return c;
        c = a.Length.CompareTo(b.Length);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Kind, b.Kind);
        if (c != 0) return c;
        return string.CompareOrdinal(a.Detail, b.Detail);
    }

    /// <summary>An empty map.</summary>
    public static PrintedBodyMap Empty { get; } = new([], [], []);

    /// <summary>
    /// Projects the printer's node-keyed ranges, and any facts anchored to those
    /// nodes, into text coordinates.
    /// </summary>
    /// <remarks>
    /// A node whose characters span a line break is omitted from
    /// <see cref="Nodes"/>, because it has no single column. A fact on such a
    /// node still appears in <see cref="Annotations"/>, carrying the line it
    /// starts on and a <see cref="PrintedAnnotationSpan.Length"/> of <c>-1</c>:
    /// dropping the fact would lose a real observation, so the position degrades
    /// rather than the fact disappearing, and the sentinel says so explicitly
    /// instead of a caller inferring it from a suspicious zero.
    /// </remarks>
    /// <param name="ranges">The printer's node-keyed character ranges.</param>
    /// <param name="annotations">Facts keyed by the node they were found on, or null for a structural map only.</param>
    /// <returns>A map holding no references to the IR.</returns>
    public static PrintedBodyMap Create(
        PrintedRangeMap ranges,
        IReadOnlyDictionary<IrNode, IReadOnlyList<IAnnotation>>? annotations = null)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        string[] lines = ranges.Output.Length == 0
            ? []
            : ranges.Output.Split('\n');

        var nodes = new List<PrintedNodeSpan>(ranges.Count);
        foreach (var printed in ranges)
        {
            if (ranges.TryGetLineColumn(printed.Node, out int line, out int column, out int length))
                nodes.Add(new PrintedNodeSpan(printed.Node.GetType().Name, line, column, length));
        }

        var facts = new List<PrintedAnnotationSpan>();
        if (annotations is not null)
        {
            foreach (var (node, found) in annotations)
            {
                if (!ranges.TryGetLine(node, out int line))
                    continue;
                bool placed = ranges.TryGetLineColumn(node, out _, out int column, out int length);
                string kind = node.GetType().Name;
                foreach (var annotation in found)
                {
                    facts.Add(new PrintedAnnotationSpan(
                        annotation.Descriptor.Id,
                        kind,
                        line,
                        placed ? column : 0,
                        placed ? length : -1,
                        annotation.Detail,
                        annotation.SourceOffset));
                }
            }
        }

        facts.Sort(Compare);

        return new PrintedBodyMap(lines, nodes, facts);
    }
}
