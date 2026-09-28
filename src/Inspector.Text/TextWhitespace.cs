using System.Collections.Immutable;

namespace Inspector.Text;

/// <summary>Whether two texts are identical, differ only in whitespace, or differ in content.</summary>
public enum TextPairOutcome
{
    /// <summary>The texts are ordinal-equal.</summary>
    Identical,

    /// <summary>The texts differ, and removing every whitespace character leaves equal texts.</summary>
    WhitespaceOnly,

    /// <summary>The whitespace-stripped texts differ.</summary>
    Changed,
}

/// <summary>The facts that describe one whitespace edit. An edit may carry several.</summary>
[Flags]
public enum TextWhitespaceEditKinds
{
    None = 0,

    /// <summary>The two gaps hold different numbers of line boundaries.</summary>
    LineBreaks = 1 << 0,

    /// <summary>A corresponding line boundary is spelled differently (CRLF, CR, or LF).</summary>
    TerminatorSpelling = 1 << 1,

    /// <summary>Whitespace at the start of a line differs.</summary>
    Indentation = 1 << 2,

    /// <summary>Whitespace at the end of a line differs.</summary>
    Trailing = 1 << 3,

    /// <summary>The spaces or tabs on a blank line differ.</summary>
    BlankLineContent = 1 << 4,

    /// <summary>Whitespace between two non-whitespace characters on one line differs, present on both sides.</summary>
    Spacing = 1 << 5,

    /// <summary>Whitespace between two non-whitespace characters on one line is present on one side only.</summary>
    Separation = 1 << 6,

    /// <summary>The text ends with a line boundary on one side only.</summary>
    FinalLineTerminator = 1 << 7,
}

/// <summary>
/// A zero-based position over logical lines, with the column in UTF-16 code units. The position
/// just past a final line boundary is line <c>lineCount</c>, column 0.
/// </summary>
public readonly record struct TextPosition(int Line, int Column);

/// <summary>
/// One whitespace edit: the half-open Before and After ranges of a gap between aligned
/// non-whitespace characters whose contents differ, and the kinds that describe the difference.
/// </summary>
public sealed record TextWhitespaceEdit(
    TextPosition BeforeStart,
    TextPosition BeforeEnd,
    TextPosition AfterStart,
    TextPosition AfterEnd,
    TextWhitespaceEditKinds Kinds);

/// <summary>The characterization of two texts: an outcome and, when whitespace-only, every edit.</summary>
public sealed record TextPairCharacterization(
    TextPairOutcome Outcome,
    ImmutableArray<TextWhitespaceEdit> Edits);

/// <summary>
/// Whitespace facts for text differences, as defined by the text whitespace characterization
/// design. Whitespace is exactly U+0020, U+0009, and the CR and LF characters that form logical
/// line boundaries; every other character, including no-break and zero-width spaces, is content.
/// </summary>
public static class TextWhitespace
{
    /// <summary>Whether <paramref name="character"/> is whitespace under this characterization.</summary>
    public static bool IsWhitespace(char character)
        => character is ' ' or '\t' or '\r' or '\n';

    /// <summary>
    /// Characterizes two complete texts. The outcome is decisive and linear; a whitespace-only
    /// outcome carries the unique, complete set of edits.
    /// </summary>
    public static TextPairCharacterization Characterize(string before, string after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeLines = new TextLines(before);
        var afterLines = new TextLines(after);
        return Characterize(
            beforeLines,
            0,
            before.Length,
            afterLines,
            0,
            after.Length,
            endsText: true);
    }

    /// <summary>Returns <paramref name="text"/> with every whitespace character removed.</summary>
    internal static string Strip(ReadOnlySpan<char> text)
    {
        int count = 0;
        foreach (char character in text)
        {
            if (!IsWhitespace(character))
                count++;
        }

        if (count == text.Length)
            return text.ToString();

        return string.Create(count, text.ToString(), static (destination, source) =>
        {
            int index = 0;
            foreach (char character in source)
            {
                if (!IsWhitespace(character))
                    destination[index++] = character;
            }
        });
    }

    /// <summary>
    /// Characterizes the Before span <c>[beforeStart, beforeEnd)</c> against the After span
    /// <c>[afterStart, afterEnd)</c>. Both spans start at a line start or immediately after an
    /// anchor's content, and <paramref name="endsText"/> states whether both spans end their texts.
    /// </summary>
    internal static TextPairCharacterization Characterize(
        TextLines before,
        int beforeStart,
        int beforeEnd,
        TextLines after,
        int afterStart,
        int afterEnd,
        bool endsText)
    {
        ReadOnlySpan<char> beforeSpan = before.Text.AsSpan(beforeStart, beforeEnd - beforeStart);
        ReadOnlySpan<char> afterSpan = after.Text.AsSpan(afterStart, afterEnd - afterStart);
        if (beforeSpan.SequenceEqual(afterSpan))
            return new TextPairCharacterization(TextPairOutcome.Identical, []);

        var edits = ImmutableArray.CreateBuilder<TextWhitespaceEdit>();
        int i = beforeStart;
        int j = afterStart;
        while (true)
        {
            int gapBeforeStart = i;
            int gapAfterStart = j;
            while (i < beforeEnd && IsWhitespace(before.Text[i]))
                i++;
            while (j < afterEnd && IsWhitespace(after.Text[j]))
                j++;

            bool atStart = gapBeforeStart == beforeStart;
            bool beforeDone = i == beforeEnd;
            bool afterDone = j == afterEnd;
            if (beforeDone != afterDone)
                return new TextPairCharacterization(TextPairOutcome.Changed, []);

            ReadOnlySpan<char> beforeGap = before.Text.AsSpan(gapBeforeStart, i - gapBeforeStart);
            ReadOnlySpan<char> afterGap = after.Text.AsSpan(gapAfterStart, j - gapAfterStart);
            if (!beforeGap.SequenceEqual(afterGap))
            {
                TextWhitespaceEditKinds kinds = ClassifyGap(
                    beforeGap,
                    afterGap,
                    atStart,
                    atEnd: beforeDone,
                    endsText);
                edits.Add(new TextWhitespaceEdit(
                    before.PositionOf(gapBeforeStart),
                    before.PositionOf(i),
                    after.PositionOf(gapAfterStart),
                    after.PositionOf(j),
                    kinds));
            }

            if (beforeDone)
                break;

            if (before.Text[i] != after.Text[j])
                return new TextPairCharacterization(TextPairOutcome.Changed, []);

            i++;
            j++;
        }

        return new TextPairCharacterization(TextPairOutcome.WhitespaceOnly, edits.ToImmutable());
    }

    static TextWhitespaceEditKinds ClassifyGap(
        ReadOnlySpan<char> before,
        ReadOnlySpan<char> after,
        bool atStart,
        bool atEnd,
        bool endsText)
    {
        GapShape beforeShape = GapShape.Parse(before);
        GapShape afterShape = GapShape.Parse(after);
        var kinds = TextWhitespaceEditKinds.None;

        if (atEnd && endsText && beforeShape.EndsWithBoundary != afterShape.EndsWithBoundary)
            kinds |= TextWhitespaceEditKinds.FinalLineTerminator;

        if (beforeShape.Boundaries.Length != afterShape.Boundaries.Length)
            return kinds | TextWhitespaceEditKinds.LineBreaks;

        for (int index = 0; index < beforeShape.Boundaries.Length; index++)
        {
            if (beforeShape.Boundaries[index] != afterShape.Boundaries[index])
                kinds |= TextWhitespaceEditKinds.TerminatorSpelling;
        }

        int lastSegment = beforeShape.Segments.Length - 1;
        for (int index = 0; index <= lastSegment; index++)
        {
            string beforeSegment = beforeShape.Segments[index];
            string afterSegment = afterShape.Segments[index];
            if (beforeSegment == afterSegment)
                continue;

            bool startsLine = index > 0 || atStart;
            bool endsLine = index < lastSegment || atEnd;
            kinds |= (startsLine, endsLine) switch
            {
                (true, true) => TextWhitespaceEditKinds.BlankLineContent,
                (true, false) => TextWhitespaceEditKinds.Indentation,
                (false, true) => TextWhitespaceEditKinds.Trailing,
                (false, false) => beforeSegment.Length > 0 && afterSegment.Length > 0
                    ? TextWhitespaceEditKinds.Spacing
                    : TextWhitespaceEditKinds.Separation,
            };
        }

        return kinds;
    }

    /// <summary>A whitespace gap split into space-and-tab segments and the boundaries between them.</summary>
    readonly record struct GapShape(
        ImmutableArray<string> Segments,
        ImmutableArray<string> Boundaries,
        bool EndsWithBoundary)
    {
        public static GapShape Parse(ReadOnlySpan<char> gap)
        {
            var segments = ImmutableArray.CreateBuilder<string>();
            var boundaries = ImmutableArray.CreateBuilder<string>();
            int segmentStart = 0;
            int index = 0;
            while (index < gap.Length)
            {
                char character = gap[index];
                if (character is not ('\r' or '\n'))
                {
                    index++;
                    continue;
                }

                segments.Add(gap[segmentStart..index].ToString());
                int length = character == '\r' && index + 1 < gap.Length && gap[index + 1] == '\n'
                    ? 2
                    : 1;
                boundaries.Add(gap.Slice(index, length).ToString());
                index += length;
                segmentStart = index;
            }

            segments.Add(gap[segmentStart..].ToString());
            bool endsWithBoundary = gap.Length > 0 && gap[^1] is '\r' or '\n';
            return new GapShape(segments.ToImmutable(), boundaries.ToImmutable(), endsWithBoundary);
        }
    }
}

/// <summary>
/// The logical lines of one text under the <see cref="TextFindings"/> analysis-line model: CRLF,
/// CR, and LF are boundaries, empty text has no lines, and a final boundary ends the last line
/// without starting another.
/// </summary>
internal sealed class TextLines
{
    readonly int[] _starts;
    readonly int[] _contentEnds;

    public TextLines(string text)
    {
        Text = text;
        var starts = new List<int>();
        var contentEnds = new List<int>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char character = text[i];
            if (character is not ('\r' or '\n'))
                continue;

            starts.Add(start);
            contentEnds.Add(i);
            if (character == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                i++;
            start = i + 1;
        }

        if (start < text.Length)
        {
            starts.Add(start);
            contentEnds.Add(text.Length);
        }

        _starts = [.. starts];
        _contentEnds = [.. contentEnds];
    }

    public string Text { get; }

    public int Count => _starts.Length;

    public int StartOf(int line) => _starts[line];

    public int ContentEndOf(int line) => _contentEnds[line];

    public string ContentOf(int line)
        => Text[_starts[line].._contentEnds[line]];

    public bool IsTerminated(int line)
        => _contentEnds[line] < Text.Length;

    public TextPosition PositionOf(int offset)
    {
        int line = Array.BinarySearch(_starts, offset);
        if (line < 0)
            line = ~line - 1;
        if (line < 0)
            return new TextPosition(0, offset);

        int column = offset - _starts[line];
        if (offset > _contentEnds[line])
        {
            // Only the offset just past the last line's final boundary lies beyond a line's
            // content without starting another line.
            return new TextPosition(line + 1, 0);
        }

        return new TextPosition(line, column);
    }
}
