using System.Collections.Immutable;

namespace ILInspector.Decompiler;

/// <summary>One source line in an annotated document's original UTF-16 coordinates.</summary>
public sealed record AnnotatedSourceTextLine(
    int LineIndex,
    int Start,
    int TerminatorLength,
    string Text);

/// <summary>One line-relative piece of an absolute annotated-source span.</summary>
public readonly record struct AnnotatedSourceLineSpan(
    int LineIndex,
    int Column,
    int Length);

/// <summary>
/// Projects absolute UTF-16 annotated-source spans into line-relative pieces
/// without changing the document's newline convention.
/// </summary>
public sealed class AnnotatedSourceTextMap
{
    readonly string _text;

    public AnnotatedSourceTextMap(string text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
        Lines = SplitLines(text);
    }

    public ImmutableArray<AnnotatedSourceTextLine> Lines { get; }

    public ImmutableArray<AnnotatedSourceLineSpan> Project(AnnotatedSourceSpan span)
    {
        if (span.Start < 0 || span.Length <= 0 || span.Start > _text.Length - span.Length)
            throw new ArgumentOutOfRangeException(nameof(span), "The span must select text within the document.");

        int end = span.Start + span.Length;
        var pieces = ImmutableArray.CreateBuilder<AnnotatedSourceLineSpan>();
        foreach (var line in Lines)
        {
            int lineEnd = line.Start + line.Text.Length;
            int start = Math.Max(span.Start, line.Start);
            int pieceEnd = Math.Min(end, lineEnd);
            if (start < pieceEnd)
            {
                pieces.Add(new AnnotatedSourceLineSpan(
                    line.LineIndex,
                    start - line.Start,
                    pieceEnd - start));
            }
        }
        return pieces.ToImmutable();
    }

    static ImmutableArray<AnnotatedSourceTextLine> SplitLines(string text)
    {
        var lines = ImmutableArray.CreateBuilder<AnnotatedSourceTextLine>();
        int start = 0;
        int lineIndex = 0;
        while (start < text.Length)
        {
            int end = start;
            while (end < text.Length && text[end] is not '\r' and not '\n')
                end++;

            int terminatorLength = 0;
            if (end < text.Length)
            {
                terminatorLength = text[end] == '\r'
                    && end + 1 < text.Length
                    && text[end + 1] == '\n'
                        ? 2
                        : 1;
            }

            lines.Add(new AnnotatedSourceTextLine(
                lineIndex++,
                start,
                terminatorLength,
                text[start..end]));
            start = end + terminatorLength;
        }

        if (text.Length == 0 || start == text.Length && EndsWithLineTerminator(text))
            lines.Add(new AnnotatedSourceTextLine(lineIndex, start, 0, ""));

        return lines.ToImmutable();
    }

    static bool EndsWithLineTerminator(string text)
        => text.Length > 0 && text[^1] is '\r' or '\n';
}
