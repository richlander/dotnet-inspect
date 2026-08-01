using System.Text;

namespace ILInspector.Decompiler.Annotations;

/// <summary>
/// Renders the <see cref="AnnotationGesture.Caret"/> gesture: a <c>^^^^</c>
/// underline on following <c>//</c> lines, pointing at the annotated statement.
/// </summary>
/// <remarks>
/// Two layout decisions, both deliberate:
/// <list type="bullet">
/// <item><b>One gutter.</b> The injected <c>//</c> anchors to the <em>member
/// declaration</em> column, not to the annotated statement's own indent. Every
/// injected comment in a member therefore shares one column, so the eye tracks a
/// single gutter instead of a ragged staircase that follows nesting depth.</item>
/// <item><b>Underline what the fact is about.</b> An <see cref="IAnnotation"/>
/// is keyed to an IL offset and carries no character span (see the contract in
/// Annotations.cs), so the span has to be recovered from the printed tree: the
/// narrowest printed node carrying that exact offset, supplied by
/// <see cref="AnnotationAnchor.ComputeCaretExtents"/>. Where the raise erased
/// the offset there is no sub-token to point at. Such a fact is listed under
/// <see cref="UnplacedMarker"/> if some other fact on the line was placed, and
/// where <em>no</em> fact on the line has an extent the underline covers the
/// trimmed statement instead — exactly what the facts are still known to be
/// about, and no more. Statement width is also the defensive answer when a
/// stacked label would not fit beside the gutter, which no CoreLib line
/// currently reaches. A span-carrying datum (a compiler diagnostic) brings its
/// own range; that is a property of the datum, not of this gesture.</item>
/// </list>
/// </remarks>
public static class AnnotationCaret
{
    // Target rendered width. Detail wraps to stay inside this where the geometry
    // allows it; a deeply indented statement with a long underline can push the
    // detail column out, which is what MinDetailWidth guards against.
    internal const int Budget = 100;
    const int MinDetailWidth = 32;

    // Past this column a trailing detail reads as a far-right sliver even when it
    // technically fits, so a long underline drops its detail to a block beneath
    // the carets instead. Short underlines keep the compact trailing form.
    const int InlineDetailMaxColumn = 40;

    // A clipped trail spends its last column on the glyph, so below two columns
    // it could not signal that it was clipped; such a caret spills to a new row
    // instead. This is a spill threshold, not a render floor — a genuinely
    // one-character expression renders one caret, never a padded two.
    const int MinTrail = 2;

    // One blank column between a trail and the next label. Without it adjacent
    // carets render as "3.^^^^4.^^^^", where the boundary is invisible.
    const int TrailGap = 1;

    // Marks a fact the anchoring layer could not place on an expression. It has
    // no number because there is no caret for a number to point at.
    const char UnplacedMarker = '-';

    // Marks a trail that could not be drawn at the width of its expression.
    const char TruncationGlyph = '~';

    /// <summary>
    /// Marks a caret line as already positioned at the enclosing member
    /// declaration column. A consumer that indents the projected body must strip
    /// this marker and leave the line's own columns alone.
    /// </summary>
    /// <remarks>
    /// The projected body is member-relative: its first statement sits at column
    /// zero and the declaration line does not exist yet. A caret comment needs
    /// three columns to the left of its first caret for <c>"// "</c>, so a
    /// statement at the body's base column has nowhere to put them and its
    /// carets would sit three columns right of what they point at. Hoisting the
    /// caret line out of the body indent recovers those columns at every nesting
    /// depth, which is why the marker exists rather than a clamp.
    /// </remarks>
    public const char HoistMarker = '\u0011';

    /// <summary>
    /// Columns the block-body indent adds to every projected line, and therefore
    /// the columns a <see cref="HoistMarker"/> line is rendered to the left of it.
    /// </summary>
    public const int BodyIndentWidth = 4;

    /// <summary>Strips a leading <see cref="HoistMarker"/>, reporting whether one was present.</summary>
    public static bool TryHoist(string line, out string text)
    {
        if (line.Length > 0 && line[0] == HoistMarker)
        {
            text = line[1..];
            return true;
        }
        text = line;
        return false;
    }

    /// <summary>
    /// Removes every <see cref="HoistMarker"/> from <paramref name="text"/>.
    /// The marker is an in-band layout signal and would print as a control
    /// character, so an output path that cannot honour the hoist must still
    /// strip it rather than pass it through.
    /// </summary>
    public static string Flatten(string text)
        => text.IndexOf(HoistMarker) < 0 ? text : text.Replace(HoistMarker.ToString(), "");

    /// <summary>
    /// Renders the caret block for <paramref name="annotations"/> under
    /// <paramref name="lineText"/>, returning one or more already-indented
    /// comment lines. Returns an empty list when there is nothing to point at
    /// (a blank line, or no annotations).
    /// </summary>
    /// <param name="lineText">The annotated source line, as rendered.</param>
    /// <param name="memberIndent">Leading whitespace of the member declaration line — the shared gutter.</param>
    /// <param name="annotations">The annotations promoted to the caret gesture on this line.</param>
    /// <param name="hoist">
    /// When true, each returned line carries a leading <see cref="HoistMarker"/>
    /// and is laid out as if it will be rendered <see cref="BodyIndentWidth"/>
    /// columns left of the code lines. See the marker's remarks for why.
    /// </param>
    /// <param name="extents">
    /// Per-fact underline extents from
    /// <see cref="AnnotationAnchor.ComputeCaretExtents"/>. When every fact on
    /// the line points at the same characters the line narrows to that one
    /// extent; otherwise it stacks a numbered caret per distinct extent and
    /// lists any fact with no extent under <see cref="UnplacedMarker"/>. See
    /// <see cref="Agreed"/> and <see cref="Stack"/>.
    /// </param>
    public static IReadOnlyList<string> Render(
        string lineText,
        string memberIndent,
        IReadOnlyList<IAnnotation> annotations,
        bool hoist = false,
        IReadOnlyDictionary<IAnnotation, AnnotationAnchor.CaretExtent>? extents = null)
    {
        if (annotations.Count == 0)
            return [];

        string trimmed = lineText.Trim();
        if (trimmed.Length == 0)
            return [];

        int statementColumn = lineText.Length - lineText.AsSpan().TrimStart().Length;

        // "//" occupies two columns of the gutter, so the pad that carries the
        // caret out to the statement is measured from the end of that marker.
        // Hoisting buys BodyIndentWidth columns of headroom, which is enough at
        // every depth; without it a statement sitting on the gutter has none, so
        // clamp — a caller can also render a fragment with no member line above.
        int hoisted = hoist ? BodyIndentWidth : 0;

        // All column arithmetic below is in *rendered* columns. A hoisted caret
        // line escapes the body indent that the code lines receive, so the code
        // is that much further right than its position in this stream; an
        // un-hoisted line is indented alongside the code, so the shift is zero
        // and the relative geometry is unchanged.
        int commentColumn = memberIndent.Length;
        string gutter = (hoist ? HoistMarker + memberIndent : memberIndent) + "//";

        // Agreement is the common case and keeps the compact single-underline
        // geometry. Only where it fails is there anything to stack: the facts
        // point at different characters, or some fact has no extent at all. A
        // line with no placeable fact still has nothing to point at, so it
        // widens as before — that is the Count: > 0 guard.
        var agreed = Agreed(annotations, extents, lineText.Length);
        if (agreed is null
            && Stack(annotations, extents, lineText.Length, out var unplaced) is { Count: > 0 } stacked
            && RenderStacked(stacked, unplaced, gutter, commentColumn, hoisted, hoist ? 1 : 0) is { } stackedLines)
        {
            return stackedLines;
        }

        var extent = agreed ?? new AnnotationAnchor.CaretExtent(statementColumn, trimmed.Length);
        int caretColumn = extent.Column + hoisted;

        int pad = Math.Max(1, caretColumn - commentColumn - 2);
        int caretEnd = commentColumn + 2 + pad + extent.Length;

        // Prefer trailing the detail on the caret line. When the underline is so
        // long that doing so leaves no usable width, drop the detail to its own
        // lines on a modest fixed column instead of wrapping into a sliver.
        bool inline = caretEnd <= InlineDetailMaxColumn && caretEnd + 1 + MinDetailWidth <= Budget;
        int detailColumn = inline ? caretEnd + 1 : commentColumn + 5;
        int width = Math.Max(MinDetailWidth, Budget - detailColumn);

        var lines = new List<string>();
        var caretLine = new StringBuilder()
            .Append(gutter)
            .Append(' ', pad)
            .Append('^', extent.Length);

        // Each fact starts on its own line so a multi-fact statement stays
        // readable; only the first can share the caret line.
        bool first = true;
        foreach (var annotation in annotations)
        {
            foreach (string chunk in Wrap(AnnotationText.Format(annotation), width))
            {
                if (first && inline)
                {
                    caretLine.Append(' ').Append(chunk);
                    lines.Add(caretLine.ToString());
                }
                else
                {
                    if (first)
                        lines.Add(caretLine.ToString());
                    lines.Add(gutter + new string(' ', detailColumn - commentColumn - 2) + chunk);
                }
                first = false;
            }
        }

        // Every fact formatted empty: still show where the caret points.
        if (first)
            lines.Add(caretLine.ToString());
        return lines;
    }

    /// <summary>
    /// The one extent every fact on the line points at, or null when they do not
    /// agree — in which case the line either stacks a caret per extent, or
    /// widens to the whole statement. See <see cref="Stack"/>.
    /// </summary>
    /// <remarks>
    /// Agreement is the common case and is worth keeping separate: measured over
    /// <c>System.Private.CoreLib</c> as the annotated-source view prints it,
    /// 25,628 of 31,640 caret-bearing lines (81.00%) narrow to one agreed
    /// extent, 10.70% carry facts that disagree, and 8.30% have no fact with a
    /// printed node to point at. Density and disagreement are the same
    /// phenomenon — more facts on a line means more distinct offsets on it — so
    /// 27,414 of those lines hold a single fact and 92.1% of them narrow, while
    /// narrowing falls to 12.1% at two facts, 1.1% at three, and 0% at four or
    /// more. Density is not the only reason this returns null, though: 2,156 of
    /// the 6,012 null lines carry a single fact that has no extent at all.
    /// </remarks>
    static AnnotationAnchor.CaretExtent? Agreed(
        IReadOnlyList<IAnnotation> annotations,
        IReadOnlyDictionary<IAnnotation, AnnotationAnchor.CaretExtent>? extents,
        int lineLength)
    {
        if (extents is null || extents.Count == 0)
            return null;

        AnnotationAnchor.CaretExtent? agreed = null;
        foreach (var annotation in annotations)
        {
            if (!extents.TryGetValue(annotation, out var extent))
                return null;
            if (agreed is { } seen && seen != extent)
                return null;
            agreed = extent;
        }

        // An extent is measured against the printer's own output, so it can only
        // disagree with the line handed here if a consumer re-wrapped the text.
        // Widen to the statement rather than throw inside a display path.
        return agreed is { } final && final.Column >= 0 && final.Length > 0
            && final.Column + final.Length <= lineLength
            ? final
            : null;
    }

    /// <summary>
    /// Groups the line's facts by the characters they point at, ordered by start
    /// column and widest first at a tie. Facts with no recoverable extent are
    /// returned separately in <paramref name="unplaced"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A line can be mixed: some facts are about an expression and some are only
    /// known to be about the statement. Widening the whole line because of the
    /// latter discards placement the anchoring layer successfully recovered for
    /// the former, and 615 lines in the corpus are mixed — 499 of them with a
    /// single surviving extent. <c>System.Tuple&lt;…&gt;.Equals</c> is the
    /// extreme: 16 facts of which 8 have an extent, so widening renders one
    /// 330-column caret and attributes nothing. So the placeable facts stack,
    /// and the rest are listed against the line without a caret, which is
    /// precisely what is known about them.
    /// </para>
    /// <para>
    /// Extents sharing a start column are always a nesting, so they can never
    /// share a row. Widest first makes each row narrower than the one above it,
    /// so reading down the rows is zooming in, and it matches the order the
    /// printer records nesting in.
    /// </para>
    /// </remarks>
    static List<(AnnotationAnchor.CaretExtent Extent, List<IAnnotation> Facts)>? Stack(
        IReadOnlyList<IAnnotation> annotations,
        IReadOnlyDictionary<IAnnotation, AnnotationAnchor.CaretExtent>? extents,
        int lineLength,
        out List<IAnnotation> unplaced)
    {
        unplaced = [];
        if (extents is null || extents.Count == 0)
            return null;

        var groups = new List<(AnnotationAnchor.CaretExtent Extent, List<IAnnotation> Facts)>();
        foreach (var annotation in annotations)
        {
            // Same bound as Agreed: an extent is measured against the printer's
            // own output, so it can only fall outside the line handed here if a
            // consumer re-wrapped the text. Treat it as unplaceable rather than
            // throw inside a display path.
            if (!extents.TryGetValue(annotation, out var extent)
                || extent.Column < 0 || extent.Length <= 0
                || extent.Column + extent.Length > lineLength)
            {
                unplaced.Add(annotation);
                continue;
            }

            int existing = groups.FindIndex(group => group.Extent == extent);
            if (existing < 0)
                groups.Add((extent, [annotation]));
            else
                groups[existing].Facts.Add(annotation);
        }

        groups.Sort((left, right) => left.Extent.Column != right.Extent.Column
            ? left.Extent.Column.CompareTo(right.Extent.Column)
            : right.Extent.Length.CompareTo(left.Extent.Length));
        return groups;
    }

    /// <summary>
    /// Packs the stacked carets onto as few rows as fit and renders them above
    /// the numbered fact list, or null when a caret has no room for its label.
    /// </summary>
    /// <remarks>
    /// A caret is drawn at its true width. It is clipped — its last column
    /// becoming <c>~</c> — only where the trail would collide with the label of
    /// the next caret on the same row, because width is information
    /// <see cref="AnnotationAnchor.ComputeCaretExtents"/> worked to recover, so
    /// this either states it truthfully or marks that it could not. Measured
    /// over <c>System.Private.CoreLib</c> as the annotated-source view prints
    /// it, by rendering each of the 3,385 lines that stack through this method
    /// and reading the output back: 3,056 (90.3%) take a single row, 326 take
    /// two, and three take more, none above four. Restricted to the 2,886
    /// multi-extent lines, 2,557 (88.6%) take one row, and 3,949 of 6,986
    /// trails (56.5%) render at true width. No stacked caret row is wider than
    /// the code line it annotates, on any of those 3,385 lines.
    /// </remarks>
    static IReadOnlyList<string>? RenderStacked(
        List<(AnnotationAnchor.CaretExtent Extent, List<IAnnotation> Facts)> groups,
        IReadOnlyList<IAnnotation> unplaced,
        string gutter,
        int commentColumn,
        int hoisted,
        int markerWidth)
    {
        int Column(int index) => groups[index].Extent.Column + hoisted;
        string Label(int index) => $"{index + 1}.";
        int LabelStart(int index) => Column(index) - Label(index).Length;

        // A trail must keep MinTrail columns for a later caret to be clipped
        // into rather than overwritten, unless the expression is genuinely
        // shorter than that — in which case its own width is the whole trail.
        int Reserved(int index) => Math.Min(groups[index].Extent.Length, MinTrail);

        var rows = new List<List<int>>();
        for (int i = 0; i < groups.Count; i++)
        {
            // The gutter owns everything left of commentColumn + 2. A label that
            // will not fit after it cannot be drawn at the column it points at,
            // and shifting it would make it point somewhere else. This is a
            // guard, not a path with known traffic: it fires on 0 of the 3,385
            // CoreLib lines that stack.
            if (LabelStart(i) < commentColumn + 2)
                return null;

            var row = rows.Find(candidate =>
                Column(candidate[^1]) + Reserved(candidate[^1]) + TrailGap <= LabelStart(i));
            if (row is null)
                rows.Add(row = []);
            row.Add(i);
        }

        var lines = new List<string>();
        foreach (var row in rows)
        {
            var caretLine = new StringBuilder(gutter);
            for (int position = 0; position < row.Count; position++)
            {
                int index = row[position];
                int length = groups[index].Extent.Length;

                // The next label on this row is the only thing a trail can run
                // into; the last trail on a row has the rest of the line.
                int limit = position + 1 < row.Count
                    ? LabelStart(row[position + 1]) - TrailGap - Column(index)
                    : int.MaxValue;
                int width = Math.Min(length, limit);

                while (caretLine.Length - markerWidth < LabelStart(index))
                    caretLine.Append(' ');
                caretLine.Append(Label(index));
                caretLine.Append('^', width < length ? width - 1 : width);
                if (width < length)
                    caretLine.Append(TruncationGlyph);
            }
            lines.Add(caretLine.ToString());
        }

        // Every fact is left-aligned under one column so the numbers line up and
        // wrapping stays predictable, which a column-aligned list cannot offer:
        // its texts would stagger across the width of the line and each wrap.
        int labelWidth = $"{groups.Count}.".Length + 1;
        int detailColumn = commentColumn + 4 + labelWidth;
        int width2 = Math.Max(MinDetailWidth, Budget - detailColumn);
        for (int i = 0; i < groups.Count; i++)
        {
            string prefix = Label(i).PadRight(labelWidth);
            foreach (var annotation in groups[i].Facts)
            {
                foreach (string chunk in Wrap(AnnotationText.Format(annotation), width2))
                {
                    lines.Add(gutter + "  " + prefix + chunk);
                    prefix = new string(' ', labelWidth);
                }
            }
        }

        // A fact with no recoverable extent gets no number, because there is no
        // caret for a number to refer to. UnplacedMarker says what is true of
        // it: this line, and nothing narrower.
        foreach (var annotation in unplaced)
        {
            string prefix = $"{UnplacedMarker}".PadRight(labelWidth);
            foreach (string chunk in Wrap(AnnotationText.Format(annotation), width2))
            {
                lines.Add(gutter + "  " + prefix + chunk);
                prefix = new string(' ', labelWidth);
            }
        }
        return lines;
    }

    /// <summary>Leading whitespace of the first non-blank line — the member declaration gutter.</summary>
    public static string MemberIndent(IReadOnlyList<string> lines)
    {
        foreach (string line in lines)
        {
            if (line.AsSpan().TrimStart().Length == 0)
                continue;
            return line[..(line.Length - line.AsSpan().TrimStart().Length)];
        }
        return "";
    }

    // Greedy word wrap. A single token longer than the width is emitted whole
    // rather than split: these details carry type names and paths, and breaking
    // one mid-token would make it unsearchable.
    static IReadOnlyList<string> Wrap(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
            return [];
        if (text.Length <= width)
            return [text];

        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > width)
            {
                chunks.Add(current.ToString());
                current.Clear();
            }
            if (current.Length > 0)
                current.Append(' ');
            current.Append(word);
        }
        if (current.Length > 0)
            chunks.Add(current.ToString());
        return chunks;
    }
}
