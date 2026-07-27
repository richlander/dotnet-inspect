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
/// <item><b>Underline the whole statement.</b> An <see cref="IAnnotation"/> is
/// keyed to an IL offset and carries no character span (see the contract in
/// Annotations.cs), so there is no sub-token to point at. The underline covers
/// the trimmed statement — exactly what the fact is known to be about, and no
/// more. A span-carrying datum (a compiler diagnostic) can underline a narrower
/// range; that is a property of the datum, not of this gesture.</item>
/// </list>
/// </remarks>
public static class AnnotationCaret
{
    // Target rendered width. Detail wraps to stay inside this where the geometry
    // allows it; a deeply indented statement with a long underline can push the
    // detail column out, which is what MinDetailWidth guards against.
    const int Budget = 100;
    const int MinDetailWidth = 32;

    // Past this column a trailing detail reads as a far-right sliver even when it
    // technically fits, so a long underline drops its detail to a block beneath
    // the carets instead. Short underlines keep the compact trailing form.
    const int InlineDetailMaxColumn = 40;

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
    public static IReadOnlyList<string> Render(
        string lineText,
        string memberIndent,
        IReadOnlyList<IAnnotation> annotations,
        bool hoist = false)
    {
        if (annotations.Count == 0)
            return [];

        string trimmed = lineText.Trim();
        if (trimmed.Length == 0)
            return [];

        int caretStart = lineText.Length - lineText.AsSpan().TrimStart().Length;

        // "//" occupies two columns of the gutter, so the pad that carries the
        // caret out to the statement is measured from the end of that marker.
        // Hoisting buys BodyIndentWidth columns of headroom, which is enough at
        // every depth; without it a statement sitting on the gutter has none, so
        // clamp — a caller can also render a fragment with no member line above.
        int hoisted = hoist ? BodyIndentWidth : 0;
        int pad = Math.Max(1, caretStart + hoisted - memberIndent.Length - 2);
        string prefix = hoist ? HoistMarker + memberIndent : memberIndent;
        string gutter = prefix + "//";

        // Column arithmetic runs in rendered columns, where a hoisted comment
        // starts BodyIndentWidth to the left of the body it annotates.
        int commentColumn = memberIndent.Length - hoisted;
        int caretEnd = commentColumn + 2 + pad + trimmed.Length;

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
            .Append('^', trimmed.Length);

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
