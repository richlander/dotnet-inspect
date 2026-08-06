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
/// <see cref="UnplacedMarker"/> when the line stacks and some other fact on it
/// was placed, and where <em>no</em> fact on the line has an extent the
/// underline covers the
/// trimmed statement instead — exactly what the facts are still known to be
/// about, and no more. Statement width is also the defensive answer when a
/// stacked label would not fit beside the gutter: that rejects the stacked
/// layout for the whole line, so the line widens and no fact on it is marked
/// unplaced. No line of
/// <c>System.Private.CoreLib</c> reaches that case as the annotated-source view
/// prints it. A span-carrying datum (a compiler diagnostic) brings its own
/// range;
/// that is a property of the datum, not of this gesture.</item>
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
    /// extent; otherwise, when at least one distinct extent is placeable and
    /// every label clears the gutter, it stacks a numbered caret per distinct
    /// extent and lists any fact with no extent under
    /// <see cref="UnplacedMarker"/>. If no fact has a usable extent, or a label
    /// will not clear the gutter, the line widens instead and no fact is marked
    /// unplaced. See
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
    /// after the focus filter and summed over the five focus families —
    /// <c>--focus</c> promotes only one family to carets, so a count over every
    /// collected fact describes a render no invocation produces — 28,151 of
    /// 32,864 caret-bearing lines (85.66%) narrow to one agreed extent, 9.64%
    /// carry facts that disagree, and 4.70% have no fact with a printed node to
    /// point at. Only the middle group changes geometry: the narrowing lines and
    /// the no-extent lines both render as they do today, so 29,695 of the 32,864
    /// (90.36%) are untouched. The 85.66% is the narrowing share, not the
    /// unchanged share, and must not be quoted as the latter. Agreement is
    /// entirely a single-fact phenomenon here: 29,550 of those lines hold one
    /// fact and 95.3% of them narrow, while none of the 2,282 two-fact, 714
    /// three-fact or 318 four-or-more-fact lines narrows. Two facts of one
    /// family on one line never share an extent in this corpus, so for them
    /// the disagreement return below does all the work.
    /// That claim briefly read as false, and the reason is worth keeping. When
    /// <see cref="AnnotationAnchor"/> first learned to adopt an extent from a
    /// printed descendant it took the <em>narrowest</em> one at any depth, which
    /// collapsed two facts under one boxed call onto the same argument token and
    /// produced 64 agreeing two-fact lines. Those agreements were the defect
    /// reporting itself: descending by depth instead points each fact at the
    /// expression it is actually about, they disagree again, and the count is 0.
    /// That 95.3% is itself padded: 1,399 of the 29,550 single-fact lines hold a
    /// fact with no extent, which cannot narrow. Conditioned on the 28,151 that
    /// could, every one does -- 100%. That is close to necessary but not quite:
    /// one fact with an extent is one extent and agrees with itself, so the loop
    /// above cannot reject it, but the bounds check below still can, and would
    /// on a negative column, a non-positive length, or an extent running past
    /// <paramref name="lineLength"/> because a consumer re-wrapped the line. In
    /// this corpus none of those occurs, so the rate is 100% as measured rather
    /// than 100% by construction. The 95.3% measures how
    /// often a single fact has an extent, not how often agreement holds.
    /// The 85.66% headline is padded the other way: 29,550 of its lines carry a
    /// single fact and cannot disagree at all. That leaves 3,314, and it is
    /// padded in turn: the loop above returns null on the first fact with no
    /// extent, so a line where any fact lacks one cannot agree whatever its
    /// extents say. Removing the 356 mixed lines and the 145 multi-fact lines
    /// where no fact has an extent leaves 2,813 that could agree, and 0 do.
    /// That is what this measures, not a mechanism — what is compared is
    /// rendered extents, not offsets, and nothing prevents two facts sharing
    /// one. Density is not the only reason this returns null: 1,399 of the
    /// 4,713 null lines carry a single fact that has no extent at all.
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
    /// the former, and 356 lines are mixed — 255 of them with a single surviving
    /// extent — measured over <c>System.Private.CoreLib</c> as the
    /// annotated-source view prints it, summed over the five focus families.
    /// The focus filter matters: <c>--focus</c> promotes only one family to
    /// carets, so counting over every collected fact describes a render no
    /// invocation produces. <c>System.Tuple&lt;…&gt;.Equals</c> is the
    /// extreme: 16 facts of which 8 have an extent, so widening renders one
    /// 370-column caret and attributes nothing (measured off the render, which
    /// is the only way this figure has ever been got right: it was recorded as
    /// 370, "corrected" to 330 on a recapture of the wrong render, and measured
    /// back to 370 -- re-measure before changing it). So the placeable facts stack,
    /// and the rest are listed against the line without a caret, which is
    /// precisely what is known about them.
    /// </para>
    /// <para>
    /// Extents sharing a start column are always a nesting, so they can never
    /// share a row. Widest first puts the outer extent on the upper row, so
    /// reading down the rows moves inward. That is a presentation choice, and
    /// it is the <em>reverse</em> of the order the printer records nesting in:
    /// <c>RecordExpressionRanges</c> reverses its parent-first walk precisely so
    /// that a node is recorded after every one of its descendants, which is the
    /// descendants-before-ancestors contract the anchor relies on. Nothing here
    /// inherits that order. Widest-first orders
    /// <i>same-start</i> extents only, and it is not what clips anything: a
    /// trail is cut short only by the next label on its own row, so a nested
    /// extent sent to a different row never shortens its parent. Nor does it
    /// make each row narrower than the one above -- a later disjoint extent can
    /// be packed onto a lower row and reach further right than anything above
    /// it, which happens on 50 of the 3,169 lines that stack in
    /// System.Private.CoreLib, as the annotated-source view prints it, summed
    /// over the five focus families. Of the 2,890 clipped trails, the successor
    /// extent whose label does the clipping is nested inside the trail it clips
    /// on 2,075 (71.8%) and disjoint from it on 815 (28.2%). The distinction
    /// matters: a disjoint successor's label reaches two columns left of the
    /// extent it belongs to, so it can still overlap and clip the trail before
    /// it.
    /// </para>
    /// </remarks>
    static List<(AnnotationAnchor.CaretExtent Extent, List<IAnnotation> Facts)>? Stack(
        IReadOnlyList<IAnnotation> annotations,
        IReadOnlyDictionary<IAnnotation, AnnotationAnchor.CaretExtent>? extents,
        int lineLength,
        out List<IAnnotation> unplaced)
    {
        unplaced = [];
        if (extents is null)
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
    /// it, summed over the five focus families and counted after the focus
    /// filter, because <c>--focus</c> promotes only the requested family to
    /// carets and a figure taken over every collected fact describes a render
    /// no invocation produces. Rendering each of the 3,169 lines that stack
    /// through this method and reading the output back: 2,870 (90.6%) take a
    /// single row, 297 take two, and two take more, neither above four. That
    /// 90.6% is itself padded — 255 of those lines carry a single extent group
    /// and cannot occupy more than one row — so the rate among the 2,914 lines
    /// with something to pack is 2,615 (89.7%). That 2,914 is padded in turn, by
    /// the same structural fact this remark states below: extents sharing a start
    /// column can never share a row, so the 136 lines carrying two distinct
    /// extents at one column cannot take a single row whatever the packer does.
    /// Among the remaining 2,778 the rate is 2,615 (94.1%). "Remaining" is the
    /// careful word: those 2,778 are not all able to take one row either -- 163
    /// of them are refused by the row admission below because their extents sit
    /// too close together. They are left in deliberately. Crowding is the thing
    /// this rate exists to measure, so excluding lines for being crowded would
    /// drive it to 100% and measure nothing. A shared start column is excluded
    /// because it is a different phenomenon: two extents anchored at one column
    /// are a nesting, not a packing failure. 4,457
    /// of 7,347 trails (60.7%) render at true width, but that rate is padded by
    /// a structural immunity: the last trail on a row has no successor, so its
    /// clip limit is <c>int.MaxValue</c> and it renders at true width by
    /// construction. Those 3,169 lines occupy 3,471 rows, so 3,471 of the 4,457
    /// could not have been clipped, and they are 3,471 of the 4,457 counted as
    /// rendering at true width. Of the 3,876 trails that were actually
    /// exposed to a successor, 986 (25.4%) survived at true width. Eight of those
    /// are immune too, their extents being no longer than <c>MinTrail</c>, which
    /// row admission already reserves; among the 3,868 trails that could
    /// genuinely be cut, 978 (25.3%) survived and 2,890 (74.7%) were clipped.
    /// That is the figure with information in it.
    /// No stacked caret row is
    /// wider than the code line it annotates, which is structural rather than
    /// measured: <see cref="Stack"/> sends any extent reaching past
    /// <c>lineLength</c> to the unplaced list, labels grow leftward, and the
    /// gutter guard bails out rather than shifting a trail rightward, so the
    /// rightmost column is bounded by the line. The 0 observed on those 3,169
    /// lines checks that derivation and cannot falsify it.
    /// </para>
    /// <para>
    /// The comparison against the widening render is narrower than it first
    /// looks, and an earlier revision of this remark overstated it twice. No
    /// caret <em>glyph</em> overhangs the code line in <em>either</em> render:
    /// measured over the same 3,169 lines, both are 0, because the widening
    /// underline covers the trimmed statement, which ends within the line. What
    /// differs is the rendered <em>row</em>. Widening appends the first detail
    /// string to the caret row when it fits the inline budget, and that appended
    /// text carries the row past the end of the code line. Quoting this as 20 of
    /// 3,169 lines (0.63%) pads the denominator with the 3,149 whose detail never
    /// goes inline and which therefore cannot overhang at all: inline detail
    /// appears on exactly 20 of these lines and all 20 overhang, so the rate is
    /// 20/20. The reason it comes out whole is that on the hoisted render these
    /// figures are taken from, widening underlines the whole trimmed statement,
    /// so the caret ends where the code line ends and any appended text lands
    /// past it. That is not a universal guarantee, and should not be stated as
    /// one: a fact whose formatted text is empty appends nothing, and an
    /// un-hoisted render can clamp <c>pad</c> to its floor of 1 and push the
    /// caret past the code with no detail appended at all. Stacking never appends detail
    /// to a caret row, so it is 0. The contrast is about where detail is placed,
    /// not about bounding the underline.
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
            // guard, not a path with known traffic: it fires on 0 of the 3,169
            // lines that *qualify* to stack in System.Private.CoreLib, as the
            // annotated-source view prints it, summed over the five focus
            // families and counted after the focus filter. Qualifying is the
            // right denominator because a line this rejects does not stack by
            // construction. A fallback is detected by the absence of an "N."
            // label before the caret run, not by the absence of a caret row --
            // falling back still renders the widening caret, so the latter
            // test could never fire. Raising the margin to +6 makes this fire
            // on 306 of the 3,169 and to +200 on all of them, so the zero is a
            // property of the corpus rather than of the measurement.
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
