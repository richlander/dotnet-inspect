using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Annotations;

/// <summary>
/// Anchors offset-keyed annotations onto the statements of a raised tree — the
/// bridge that lets one annotation set drive the C# view co-equally with the IL
/// view. Anchoring is by IL-offset RANGE, not exact match, which is the crux:
/// an allocation can be erased by the raise (a box behind <c>object o = x;</c>
/// raises to <c>return x;</c>), so no surviving node carries its exact offset —
/// but the offset still falls within the enclosing statement's range, so the
/// fact lands on the right line. The tightest containing statement wins, so a
/// fact inside a loop body anchors to the inner statement, not the loop header.
/// </summary>
public static class AnnotationAnchor
{
    /// <summary>
    /// Maps each annotation to the raised statement it belongs on. Statements
    /// are block children at every nesting depth; an annotation whose offset no
    /// statement range covers binds to the nearest preceding statement, so it is
    /// never dropped (positive-only: a fact is always shown somewhere).
    /// </summary>
    public static IReadOnlyDictionary<IrNode, IReadOnlyList<IAnnotation>> Anchor(
        IrFunction raised, IReadOnlyList<IAnnotation> annotations)
    {
        var statements = ComputeSpans(raised);

        var map = new Dictionary<IrNode, List<IAnnotation>>();
        foreach (var annotation in annotations)
        {
            var owner = Best(statements, annotation.SourceOffset);
            if (owner is null)
                continue;
            if (!map.TryGetValue(owner, out var list))
                map[owner] = list = [];
            list.Add(annotation);
        }

        return map.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<IAnnotation>)[.. entry.Value.OrderBy(a => a.SourceOffset)]);
    }

    /// <summary>
    /// The IL-offset range of every statement in the raised tree (a statement is
    /// a block child at any nesting depth; its range is the min/max
    /// <see cref="IrNode.SourceOffset"/> of its subtree). Shared by annotation
    /// anchoring and the mixed source view, which buckets IL instructions onto
    /// the same statements by the same ranges.
    /// </summary>
    public static List<StatementSpan> ComputeSpans(IrFunction raised)
    {
        var statements = new List<StatementSpan>();
        foreach (var block in raised.Descendants.OfType<Block>())
        {
            foreach (var statement in block.Children)
            {
                int min = int.MaxValue, max = int.MinValue;
                foreach (var node in Self(statement))
                {
                    if (node.SourceOffset < 0)
                        continue;
                    min = Math.Min(min, node.SourceOffset);
                    max = Math.Max(max, node.SourceOffset);
                }
                if (max >= 0)
                    statements.Add(new StatementSpan(statement, min, max));
            }
        }
        return statements;
    }

    /// <summary>
    /// The tightest statement whose offset range contains <paramref name="offset"/>;
    /// failing containment, the statement with the greatest start at or before it
    /// (the one the offset falls into or just after); failing that, the earliest.
    /// </summary>
    public static IrNode? Best(List<StatementSpan> statements, int offset)
    {
        StatementSpan? containing = null;
        StatementSpan? preceding = null;
        StatementSpan? earliest = null;
        foreach (var span in statements)
        {
            if (earliest is null || span.Min < earliest.Value.Min)
                earliest = span;
            if (span.Min <= offset && (preceding is null || span.Min > preceding.Value.Min))
                preceding = span;
            if (span.Min <= offset && offset <= span.Max)
            {
                int width = span.Max - span.Min;
                if (containing is null || width < containing.Value.Max - containing.Value.Min)
                    containing = span;
            }
        }
        return (containing ?? preceding ?? earliest)?.Statement;
    }

    /// <summary>
    /// The characters a caret should underline for one fact: a
    /// <paramref name="Column"/> and <paramref name="Length"/> into the printed
    /// line the fact is anchored to.
    /// </summary>
    /// <remarks>
    /// This is deliberately in <em>line-relative text</em> coordinates rather
    /// than node identity, so a consumer holding only the rendered line can
    /// place the underline. It narrows the caret <em>within</em> the line the
    /// fact already anchors to; it never moves a fact to another line.
    /// </remarks>
    public readonly record struct CaretExtent(int Column, int Length);

    /// <summary>
    /// The narrowest printed extent for each annotation that has one — the
    /// printed node carrying exactly the fact's own IL offset, which is what the
    /// fact is about, rather than the whole statement containing it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anchoring answers <em>which line</em>; this answers <em>which characters
    /// on it</em>. The two are separate because they fail separately: a fact
    /// whose exact offset was erased by the raise still anchors to a covering
    /// statement, but has no sub-token to point at, so it is simply absent here
    /// and the caller keeps the statement-wide underline. Measured over
    /// <c>System.Private.CoreLib</c>, 34,008 of 37,800 facts (89.97%) get an
    /// extent.
    /// </para>
    /// <para>
    /// Several printed nodes can carry one offset — the importer stamps a whole
    /// subtree from a single instruction — so the narrowest printed range wins,
    /// that being the one that names the fact most precisely.
    /// </para>
    /// <para>
    /// An extent is produced only when the narrow node prints on the same line
    /// as the statement the fact anchors to. Where a statement wraps and the
    /// sub-expression prints on a continuation line (270 facts in the same
    /// corpus), narrowing would put the underline under a line the fact is not
    /// attached to; those keep the statement-wide caret until the caret block
    /// can be emitted against the narrow node's own line.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<IAnnotation, CaretExtent> ComputeCaretExtents(
        IReadOnlyList<IAnnotation> annotations,
        List<StatementSpan> statements,
        PrintedRangeMap printedRanges)
    {
        var extents = new Dictionary<IAnnotation, CaretExtent>();
        if (annotations.Count == 0 || printedRanges.Count == 0)
            return extents;

        var narrowest = NarrowestPrintedByOffset(printedRanges);
        if (narrowest.Count == 0)
            return extents;

        // Indexed by the same coordinates TryGetLineColumn reports, so a column
        // from there indexes straight into these lines. Split on '\n' alone: a
        // '\r' left at a line's end is whitespace and is trimmed below.
        var lines = printedRanges.Output.Split('\n');

        foreach (var annotation in annotations)
        {
            if (!narrowest.TryGetValue(annotation.SourceOffset, out var node))
                continue;
            if (Best(statements, annotation.SourceOffset) is not { } owner)
                continue;
            if (!TryGetPrintedLine(owner, printedRanges, out int ownerLine))
                continue;
            if (!printedRanges.TryGetLineColumn(node, out int line, out int column, out int length))
                continue;
            if (line != ownerLine || line >= lines.Length)
                continue;
            if (!TryTrimToPrinted(lines[line], ref column, ref length))
                continue;
            extents[annotation] = new CaretExtent(column, length);
        }
        return extents;
    }

    /// <summary>
    /// Shrinks an extent onto the printed characters it covers, dropping
    /// whitespace at either end.
    /// </summary>
    /// <remarks>
    /// A statement's printed range begins at its line's indent, so when the
    /// narrowest node carrying an offset is the statement itself the raw extent
    /// covers the leading whitespace and a caret drawn from it would start left
    /// of the code. Measured over <c>System.Private.CoreLib</c> that is 161 of
    /// 25,682 narrowed lines. Trimming makes such an extent coincide with the
    /// statement-wide default rather than mis-drawing, and leaves every extent
    /// that already named an expression untouched.
    /// </remarks>
    internal static bool TryTrimToPrinted(string lineText, ref int column, ref int length)
    {
        if (column < 0 || length <= 0 || column >= lineText.Length)
            return false;
        int end = Math.Min(column + length, lineText.Length);
        int start = column;
        while (start < end && char.IsWhiteSpace(lineText[start]))
            start++;
        while (end > start && char.IsWhiteSpace(lineText[end - 1]))
            end--;
        if (end <= start)
            return false;
        column = start;
        length = end - start;
        return true;
    }

    /// <summary>
    /// The narrowest printed node carrying each source offset. Built once per
    /// function, because it is a scan of the whole range map and every fact
    /// queries it.
    /// </summary>
    /// <remarks>
    /// A <see cref="LoadStackSlot"/> prints as <c>S_n</c> and a
    /// <see cref="CaughtException"/> as <c>__exception</c>: stand-ins the raise
    /// emits where it could not recover the value an instruction produced, or
    /// where the value has no C# spelling. Each carries the offset of the
    /// instruction that consumed it, so on a line like
    /// <c>return new ConstructorInvoker(S_0);</c> it is narrower than the
    /// <c>new</c> it sits inside and would win on width alone, underlining the
    /// argument instead of the allocation the fact is about. Width is therefore
    /// only the tie-break among real expressions; a stand-in wins only when
    /// nothing else carries the offset, which is the case where it genuinely is
    /// the value on the line (<c>_ = S_0;</c>). Measured over
    /// <c>System.Private.CoreLib</c>, preferring real expressions moves 50 of
    /// 10,664 <c>alloc.new</c> underlines onto the allocation; no fact in that
    /// corpus shares an offset with a printed <c>__exception</c>, which is
    /// excluded because it is the same shape, not because it was observed.
    /// </remarks>
    static Dictionary<int, IrNode> NarrowestPrintedByOffset(PrintedRangeMap printedRanges)
    {
        var narrowest = new Dictionary<int, IrNode>();
        var widths = new Dictionary<int, int>();
        var standIn = new HashSet<int>();
        foreach (var printed in printedRanges)
        {
            int offset = printed.Node.SourceOffset;
            if (offset < 0)
                continue;
            bool slot = printed.Node is LoadStackSlot or CaughtException;
            if (widths.TryGetValue(offset, out int best))
            {
                bool bestIsSlot = standIn.Contains(offset);
                if (slot && !bestIsSlot)
                    continue;
                int width = printed.Characters.End.Value - printed.Characters.Start.Value;
                if (slot == bestIsSlot && best <= width)
                    continue;
            }
            widths[offset] = printed.Characters.End.Value - printed.Characters.Start.Value;
            narrowest[offset] = printed.Node;
            if (slot)
                standIn.Add(offset);
            else
                standIn.Remove(offset);
        }
        return narrowest;
    }

    /// <summary>
    /// Finds the printed line for an anchored statement, climbing to the nearest
    /// printed ancestor when the owner belongs to an inline expression body (for
    /// example, a raised lambda). Facts stay visible instead of being dropped.
    /// </summary>
    public static bool TryGetPrintedLine(
        IrNode owner,
        PrintedRangeMap printedRanges,
        out int line)
    {
        for (var current = owner; current is not null; current = current.Parent)
            if (printedRanges.TryGetLine(current, out line))
                return true;
        line = 0;
        return false;
    }

    static IEnumerable<IrNode> Self(IrNode node)
    {
        yield return node;
        foreach (var descendant in node.Descendants)
            yield return descendant;
    }

    public readonly record struct StatementSpan(IrNode Statement, int Min, int Max);
}
