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
    /// <c>System.Private.CoreLib</c> as the annotated-source view prints it —
    /// that is, with callee bodies imported — 35,498 of 37,800 facts (93.91%)
    /// get an extent. Every figure quoted in this file is from that render.
    /// The C#-only overlay prints the same members without importing callee
    /// bodies, which shifts a few printed ranges and yields 35,502; a figure
    /// here is only meaningful against a stated render, because the printed
    /// text is what extents are measured in.
    /// </para>
    /// <para>
    /// Several printed nodes can carry one offset — the importer stamps a whole
    /// subtree from a single instruction — so the narrowest printed range wins,
    /// that being the one that names the fact most precisely.
    /// </para>
    /// <para>
    /// An extent is produced only when the narrow node prints on the same line
    /// as the statement the fact anchors to. Where a statement wraps and the
    /// sub-expression prints on a continuation line (283 facts in the same
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

        // No `narrowest.Count == 0` bail-out: an empty map means no printed node
        // carries a source offset, which is exactly a case adoption can still
        // answer, since it looks *inside* unprinted owners. Returning early
        // would skip it. That never happens on System.Private.CoreLib -- 0
        // functions of those the annotated-source view prints -- so this is a
        // shape correction, not a behaviour change measurable on this corpus.
        var narrowest = NarrowestPrintedByOffset(printedRanges);

        // Indexed by the same coordinates TryGetLineColumn reports, so a column
        // from there indexes straight into these lines. Split on '\n' alone: a
        // '\r' left at a line's end is whitespace and is trimmed below.
        var lines = printedRanges.Output.Split('\n');
        var adopted = AdoptedPrintedByOffset(annotations, statements, printedRanges, narrowest);

        foreach (var annotation in annotations)
        {
            if (!narrowest.TryGetValue(annotation.SourceOffset, out var node)
                && !adopted.TryGetValue(annotation.SourceOffset, out node))
                continue;
            if (Best(statements, annotation.SourceOffset) is not { } owner)
                continue;
            if (!TryGetPrintedLine(owner, printedRanges, out int ownerLine))
                continue;
            // A failure here zeroes the out parameters, so dropping this guard
            // would not change a result -- length 0 is rejected by the trim
            // either way. It stays because relying on that is a trap.
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
    /// of the code. Measured over <c>System.Private.CoreLib</c> as the
    /// annotated-source view prints it, the trim moves 205 of the 35,498
    /// extents and rejects none of them. Trimming makes such
    /// an extent coincide with the statement-wide default rather than
    /// mis-drawing, and leaves every extent that already named an expression
    /// untouched.
    /// <para>
    /// The clamp on the far end is defensive rather than load-bearing today:
    /// <see cref="PrintedRangeMap.TryGetLineColumn"/> refuses any range that
    /// crosses a line break, so of the 35,498 ranges the caller delivers here,
    /// 0 overhang the line and 650 end exactly on its last character. It is
    /// kept, and gated, because this is an internal helper taking a
    /// caller-supplied range: the cost is one
    /// <see cref="Math.Min(int, int)"/> and the alternative is an out-of-range
    /// read the first time a second caller passes a range this one would not.
    /// </para>
    /// </remarks>
    internal static bool TryTrimToPrinted(string lineText, ref int column, ref int length)
    {
        // length <= 0 is redundant with the end <= start rejection below, and is
        // kept because this is a documented precondition of an internal helper,
        // not an accident of the loop bounds.
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
    /// 10,646 <c>alloc.new</c> underlines onto the allocation; no fact in that
    /// corpus shares an offset with a printed <c>__exception</c>, which is
    /// excluded because it is the same shape, not because it was observed.
    /// </remarks>
    internal static Dictionary<int, IrNode> NarrowestPrintedByOffset(PrintedRangeMap printedRanges)
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
    /// Extents for offsets whose owning node prints nothing, taken from the
    /// nearest printed node inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="NarrowestPrintedByOffset"/> can only answer for an offset some
    /// printed node carries, and a whole class of facts is about a node C# has
    /// no syntax for. Boxing is the case that dominates: <c>Box</c> emits no
    /// characters, so an <c>alloc.box</c> fact stamped with the box's offset has
    /// no printed owner, and before this the fact fell back to the
    /// statement-wide underline. What the reader wants pointed at is the value
    /// being boxed, and that node <em>is</em> printed, one level down.
    /// </para>
    /// <para>
    /// Measured over <c>System.Private.CoreLib</c> as the annotated-source view
    /// prints it: of 37,800 facts, 4,143 had no extent. For 2,573 the offset
    /// belonged to a node that exists but prints nothing, and 1,907 of those
    /// have a printed descendant — 1,899 of them boxes. Those 1,907 offsets
    /// adopt 1,023 <c>LoadArgument</c>, 498 <c>LoadLocal</c>, 129 <c>Call</c>,
    /// 107 <c>LoadIndirect</c>, 75 <c>Convert</c>, 42 <c>Binary</c>, 14
    /// <c>Constant</c>, 11 <c>LoadField</c>, 5 <c>LoadStackSlot</c> and three
    /// singletons. That is the population this recovers, and it is counted per
    /// distinct (function, offset) pair. A count of matching nodes
    /// <em>visited</em> is 4,262, more than twice as large, and describes
    /// nothing the renderer produces: statement subtrees overlap, so the same
    /// owner node is reached more than once (2,567 revisits), and separately
    /// 66 offsets carry more than one distinct unprinted owner. Either way the
    /// renderer draws one extent per offset, so the offset is the unit.
    /// </para>
    /// <para>
    /// Descent only, never ascent. A printed <em>ancestor</em> is available for
    /// a further 576, but an ancestor's range is wider than the fact — it is the
    /// statement-wide underline this is trying to escape — so adopting it would
    /// dress up today's fallback as a narrow extent while pointing at the same
    /// characters. Those keep the fallback and are honest about it.
    /// </para>
    /// <para>
    /// Built only for offsets the annotations actually ask about and that
    /// <paramref name="narrowest"/> cannot answer, so a body whose facts all
    /// anchor to printed nodes walks no extra tree.
    /// </para>
    /// </remarks>
    internal static Dictionary<int, IrNode> AdoptedPrintedByOffset(
        IReadOnlyList<IAnnotation> annotations,
        List<StatementSpan> statements,
        PrintedRangeMap printedRanges,
        Dictionary<int, IrNode> narrowest)
    {
        var adopted = new Dictionary<int, IrNode>();
        HashSet<int>? wanted = null;
        foreach (var annotation in annotations)
        {
            if (narrowest.ContainsKey(annotation.SourceOffset) || annotation.SourceOffset < 0)
                continue;
            wanted ??= [];
            wanted.Add(annotation.SourceOffset);
        }
        if (wanted is null)
            return adopted;

        var level = new List<IrNode>();
        var next = new List<IrNode>();
        var chosen = new Dictionary<int, (int Start, int Width)>();
        foreach (var span in statements)
        {
            foreach (var node in Self(span.Statement))
            {
                int offset = node.SourceOffset;
                if (offset < 0 || !wanted.Contains(offset))
                    continue;
                // The owner prints nothing; look inside it for something that does.
                if (printedRanges.TryGetRange(node, out _))
                    continue;
                if (NearestPrinted(node, printedRanges, ref level, ref next) is not { } inner
                    || !printedRanges.TryGetRange(inner, out var innerRange))
                    continue;
                // One offset can be carried by several unprinted owners naming
                // different places. That is rare but never benign: of the 1,907
                // offsets adoption resolves, 66 carry more than one distinct
                // unprinted owner, and on every one of those 66 the owners pick
                // different nodes -- each a group of `Box` nodes over different
                // locals of equal width. No choice among them is more correct
                // than another, so the requirement is only that it be a function
                // of the ranges rather than of the walk: leftmost, and narrowest
                // where two start together. On this corpus that agrees with what
                // the unarbitrated walk already produced on all 66, so it changes
                // no rendered output here; it exists so the choice cannot move
                // when traversal order does.
                if (chosen.TryGetValue(offset, out var best)
                    && (best.Start < innerRange.Start.Value
                        || (best.Start == innerRange.Start.Value
                            && best.Width <= innerRange.End.Value - innerRange.Start.Value)))
                    continue;
                chosen[offset] = (innerRange.Start.Value, innerRange.End.Value - innerRange.Start.Value);
                adopted[offset] = inner;
            }
        }
        return adopted;
    }

    /// <summary>
    /// The shallowest printed descendant of <paramref name="node"/>, searched
    /// breadth-first so depth decides and width only breaks a tie within one
    /// level.
    /// </summary>
    /// <remarks>
    /// Depth is the whole point, and an earlier revision of this helper got it
    /// wrong by taking the narrowest printed descendant at any depth. For
    /// <c>box(Foo(x))</c> the narrowest printed node under the box is the
    /// argument <c>x</c>, not the call <c>Foo(x)</c> whose result is boxed, so
    /// the caret underlined characters the fact is not about. Measured over
    /// <c>System.Private.CoreLib</c> as the annotated-source view prints it,
    /// the two rules disagree on 252 of the 1,907 offsets adoption resolves:
    /// 119 over <c>Call</c>, 56 and 19 over <c>Convert</c>, 36 and 6 over
    /// <c>Binary</c>, and the rest single figures. Depth is right in every one
    /// of them, because what a box boxes is its operand.
    /// <para>
    /// The width tie-break never runs on that corpus: at every descent, no node
    /// had two printed descendants at its shallowest printed level. It is kept
    /// because "never here" is a measurement rather than a guarantee, and
    /// preferring the widest at a level keeps the caret over as much of the
    /// expression as the printer will name.
    /// </para>
    /// </remarks>
    static IrNode? NearestPrinted(
        IrNode node,
        PrintedRangeMap printedRanges,
        ref List<IrNode> level,
        ref List<IrNode> next)
    {
        level.Clear();
        level.AddRange(node.Children);
        while (level.Count > 0)
        {
            next.Clear();
            IrNode? best = null;
            int bestWidth = 0;
            foreach (var candidate in level)
            {
                // A zero-width range names no characters to point at, so such a
                // node is descended through rather than adopted.
                int width = printedRanges.TryGetRange(candidate, out var range)
                    ? range.End.Value - range.Start.Value
                    : 0;
                if (width > 0)
                {
                    if (width > bestWidth)
                    {
                        bestWidth = width;
                        best = candidate;
                    }
                }
                else if (best is null)
                {
                    next.AddRange(candidate.Children);
                }
            }
            if (best is not null)
                return best;
            (level, next) = (next, level);
        }
        return null;
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
