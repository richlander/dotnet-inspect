using System.Buffers;
using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;

namespace Inspector.Text;

public enum DecodedTextLineTerminator
{
    None,
    CarriageReturnLineFeed,
    CarriageReturn,
    LineFeed,
    NextLine,
    LineSeparator,
    ParagraphSeparator,
}

public enum DecodedTextBatchKind
{
    Rows,
    OversizedLine,
}

public sealed class DecodedTextPullRequest
{
    public DecodedTextPullRequest(
        int maximumCandidateRows,
        int maximumUtf16CodeUnits,
        int maximumJsonEncodedUtf8Bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumCandidateRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumUtf16CodeUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumJsonEncodedUtf8Bytes);

        MaximumCandidateRows = maximumCandidateRows;
        MaximumUtf16CodeUnits = maximumUtf16CodeUnits;
        MaximumJsonEncodedUtf8Bytes = maximumJsonEncodedUtf8Bytes;
    }

    public int MaximumCandidateRows { get; }

    public int MaximumUtf16CodeUnits { get; }

    public int MaximumJsonEncodedUtf8Bytes { get; }

    public static DecodedTextPullRequest Unbounded(
        int maximumCandidateRows) =>
        new(
            maximumCandidateRows,
            int.MaxValue,
            int.MaxValue);
}

public sealed class DecodedTextFragmentRequest
{
    public DecodedTextFragmentRequest(
        int maximumUtf16CodeUnits,
        int maximumJsonEncodedUtf8Bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumUtf16CodeUnits);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(
            maximumJsonEncodedUtf8Bytes);

        MaximumUtf16CodeUnits = maximumUtf16CodeUnits;
        MaximumJsonEncodedUtf8Bytes = maximumJsonEncodedUtf8Bytes;
    }

    public int MaximumUtf16CodeUnits { get; }

    public int MaximumJsonEncodedUtf8Bytes { get; }
}

public sealed class DecodedTextPosition
{
    private readonly object _documentIdentity;

    internal DecodedTextPosition(
        object documentIdentity,
        int utf16Offset,
        int nextLineNumber)
    {
        _documentIdentity = documentIdentity;
        Utf16Offset = utf16Offset;
        NextLineNumber = nextLineNumber;
    }

    public int Utf16Offset { get; }

    public int NextLineNumber { get; }

    internal bool Matches(object documentIdentity) =>
        ReferenceEquals(_documentIdentity, documentIdentity);
}

public sealed class DecodedTextLine
{
    private readonly ReadOnlyMemory<char> _rowText;
    private readonly int _contentLength;

    internal DecodedTextLine(
        int number,
        int start,
        ReadOnlyMemory<char> rowText,
        int contentLength,
        DecodedTextLineTerminator terminator)
    {
        Number = number;
        Start = start;
        _rowText = rowText;
        _contentLength = contentLength;
        Terminator = terminator;
        JsonEncodedUtf8Bytes =
            DecodedTextEncoding.JsonEncodedUtf8Length(rowText.Span);
    }

    public int Number { get; }

    public int Start { get; }

    public ReadOnlyMemory<char> Content =>
        _rowText[.._contentLength];

    public DecodedTextLineTerminator Terminator { get; }

    public string TerminatorText =>
        DecodedTextTerminators.Text(Terminator);

    public int Utf16CodeUnits => _rowText.Length;

    public int JsonEncodedUtf8Bytes { get; }

    public DecodedTextLineFragment ReadFragment(
        int contentOffset,
        DecodedTextFragmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(contentOffset);
        if (contentOffset > _contentLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contentOffset),
                "The fragment offset exceeds the line content.");
        }

        ReadOnlySpan<char> content = Content.Span;
        if (contentOffset > 0
            && contentOffset < content.Length
            && char.IsHighSurrogate(content[contentOffset - 1])
            && char.IsLowSurrogate(content[contentOffset]))
        {
            throw new ArgumentException(
                "The fragment offset splits a UTF-16 surrogate pair.",
                nameof(contentOffset));
        }

        int consumed = 0;
        int jsonBytes = 0;
        while (contentOffset + consumed < content.Length)
        {
            int scalarLength = DecodedTextEncoding.ScalarLength(
                content,
                contentOffset + consumed);
            int scalarJsonBytes =
                DecodedTextEncoding.JsonEncodedUtf8Length(
                    content.Slice(
                        contentOffset + consumed,
                        scalarLength));
            if (consumed + scalarLength
                    > request.MaximumUtf16CodeUnits
                || jsonBytes + scalarJsonBytes
                    > request.MaximumJsonEncodedUtf8Bytes)
            {
                break;
            }

            consumed += scalarLength;
            jsonBytes = checked(jsonBytes + scalarJsonBytes);
        }

        bool contentComplete =
            contentOffset + consumed == content.Length;
        int terminatorLength =
            DecodedTextTerminators.Utf16Length(Terminator);
        int terminatorJsonBytes =
            DecodedTextEncoding.JsonEncodedUtf8Length(
                TerminatorText.AsSpan());
        bool lineComplete =
            contentComplete
            && consumed + terminatorLength
                <= request.MaximumUtf16CodeUnits
            && jsonBytes + terminatorJsonBytes
                <= request.MaximumJsonEncodedUtf8Bytes;

        if (lineComplete)
        {
            jsonBytes = checked(jsonBytes + terminatorJsonBytes);
        }
        else if (consumed == 0)
        {
            throw new ArgumentException(
                "The fragment limits cannot contain the next Unicode "
                + "scalar or complete line terminator.",
                nameof(request));
        }

        return new DecodedTextLineFragment(
            Number,
            Start,
            contentOffset,
            Content.Slice(contentOffset, consumed),
            lineComplete
                ? Terminator
                : DecodedTextLineTerminator.None,
            lineComplete ? null : contentOffset + consumed,
            consumed + (lineComplete ? terminatorLength : 0),
            jsonBytes);
    }
}

public sealed class DecodedTextLineFragment
{
    internal DecodedTextLineFragment(
        int lineNumber,
        int lineStart,
        int contentOffset,
        ReadOnlyMemory<char> content,
        DecodedTextLineTerminator terminator,
        int? nextContentOffset,
        int utf16CodeUnits,
        int jsonEncodedUtf8Bytes)
    {
        LineNumber = lineNumber;
        LineStart = lineStart;
        ContentOffset = contentOffset;
        Content = content;
        Terminator = terminator;
        NextContentOffset = nextContentOffset;
        Utf16CodeUnits = utf16CodeUnits;
        JsonEncodedUtf8Bytes = jsonEncodedUtf8Bytes;
    }

    public int LineNumber { get; }

    public int LineStart { get; }

    public int ContentOffset { get; }

    public int Start => checked(LineStart + ContentOffset);

    public ReadOnlyMemory<char> Content { get; }

    public DecodedTextLineTerminator Terminator { get; }

    public string TerminatorText =>
        DecodedTextTerminators.Text(Terminator);

    public int? NextContentOffset { get; }

    public bool IsLineComplete => NextContentOffset is null;

    public int Utf16CodeUnits { get; }

    public int JsonEncodedUtf8Bytes { get; }
}

public sealed class DecodedTextBatch
{
    internal DecodedTextBatch(
        DecodedTextBatchKind kind,
        ImmutableArray<DecodedTextLine> lines,
        DecodedTextPosition? continuation,
        int utf16CodeUnits,
        int jsonEncodedUtf8Bytes)
    {
        Kind = kind;
        Lines = lines;
        Continuation = continuation;
        Utf16CodeUnits = utf16CodeUnits;
        JsonEncodedUtf8Bytes = jsonEncodedUtf8Bytes;
    }

    public DecodedTextBatchKind Kind { get; }

    public ImmutableArray<DecodedTextLine> Lines { get; }

    public DecodedTextPosition? Continuation { get; }

    public bool IsComplete => Continuation is null;

    public int? ExactLineCount =>
        IsComplete
            ? Lines[^1].Number
            : null;

    public int Utf16CodeUnits { get; }

    public int JsonEncodedUtf8Bytes { get; }
}

public sealed class DecodedTextDocument
{
    private readonly string _text;
    private readonly object _identity = new();

    public DecodedTextDocument(string text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public int Utf16Length => _text.Length;

    public DecodedTextPosition Start =>
        new(_identity, utf16Offset: 0, nextLineNumber: 1);

    public DecodedTextBatch Pull(
        DecodedTextPosition position,
        DecodedTextPullRequest request)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(request);
        if (!position.Matches(_identity))
        {
            throw new ArgumentException(
                "The position belongs to a different decoded document.",
                nameof(position));
        }

        var lines = ImmutableArray.CreateBuilder<DecodedTextLine>();
        DecodedTextPosition? current = position;
        int utf16CodeUnits = 0;
        int jsonEncodedUtf8Bytes = 0;
        while (lines.Count < request.MaximumCandidateRows)
        {
            (DecodedTextLine line, DecodedTextPosition? next) =
                ReadLine(current);
            bool oversized =
                line.Utf16CodeUnits
                    > request.MaximumUtf16CodeUnits
                || line.JsonEncodedUtf8Bytes
                    > request.MaximumJsonEncodedUtf8Bytes;
            bool exceedsBatch =
                utf16CodeUnits + (long)line.Utf16CodeUnits
                    > request.MaximumUtf16CodeUnits
                || jsonEncodedUtf8Bytes
                    + (long)line.JsonEncodedUtf8Bytes
                    > request.MaximumJsonEncodedUtf8Bytes;
            if (lines.Count != 0 && (oversized || exceedsBatch))
            {
                return CreateBatch(
                    DecodedTextBatchKind.Rows,
                    lines,
                    current,
                    utf16CodeUnits,
                    jsonEncodedUtf8Bytes);
            }

            lines.Add(line);
            utf16CodeUnits =
                checked(utf16CodeUnits + line.Utf16CodeUnits);
            jsonEncodedUtf8Bytes =
                checked(
                    jsonEncodedUtf8Bytes
                    + line.JsonEncodedUtf8Bytes);
            current = next;

            if (oversized)
            {
                return CreateBatch(
                    DecodedTextBatchKind.OversizedLine,
                    lines,
                    current,
                    utf16CodeUnits,
                    jsonEncodedUtf8Bytes);
            }

            if (current is null)
            {
                return CreateBatch(
                    DecodedTextBatchKind.Rows,
                    lines,
                    continuation: null,
                    utf16CodeUnits,
                    jsonEncodedUtf8Bytes);
            }
        }

        return CreateBatch(
            DecodedTextBatchKind.Rows,
            lines,
            current,
            utf16CodeUnits,
            jsonEncodedUtf8Bytes);
    }

    private (DecodedTextLine Line, DecodedTextPosition? Next)
        ReadLine(DecodedTextPosition position)
    {
        int lineStart = position.Utf16Offset;
        if (lineStart < 0 || lineStart > _text.Length)
        {
            throw new ArgumentException(
                "The decoded-text position is outside the document.",
                nameof(position));
        }

        int index = lineStart;
        DecodedTextLineTerminator terminator =
            DecodedTextLineTerminator.None;
        while (index < _text.Length)
        {
            terminator = DecodedTextTerminators.At(_text, index);
            if (terminator != DecodedTextLineTerminator.None)
                break;

            index++;
        }

        int terminatorLength =
            DecodedTextTerminators.Utf16Length(terminator);
        var line = new DecodedTextLine(
            position.NextLineNumber,
            lineStart,
            _text.AsMemory(
                lineStart,
                index - lineStart + terminatorLength),
            index - lineStart,
            terminator);
        DecodedTextPosition? next =
            terminator == DecodedTextLineTerminator.None
                ? null
                : new DecodedTextPosition(
                    _identity,
                    index + terminatorLength,
                    checked(position.NextLineNumber + 1));
        return (line, next);
    }

    private static DecodedTextBatch CreateBatch(
        DecodedTextBatchKind kind,
        ImmutableArray<DecodedTextLine>.Builder lines,
        DecodedTextPosition? continuation,
        int utf16CodeUnits,
        int jsonEncodedUtf8Bytes) =>
        new(
            kind,
            lines.ToImmutable(),
            continuation,
            utf16CodeUnits,
            jsonEncodedUtf8Bytes);
}

internal static class DecodedTextTerminators
{
    internal static DecodedTextLineTerminator At(
        string text,
        int index) =>
        text[index] switch
        {
            '\r' when index + 1 < text.Length
                && text[index + 1] == '\n' =>
                DecodedTextLineTerminator.CarriageReturnLineFeed,
            '\r' => DecodedTextLineTerminator.CarriageReturn,
            '\n' => DecodedTextLineTerminator.LineFeed,
            '\u0085' => DecodedTextLineTerminator.NextLine,
            '\u2028' => DecodedTextLineTerminator.LineSeparator,
            '\u2029' => DecodedTextLineTerminator.ParagraphSeparator,
            _ => DecodedTextLineTerminator.None,
        };

    internal static int Utf16Length(
        DecodedTextLineTerminator terminator) =>
        terminator switch
        {
            DecodedTextLineTerminator.None => 0,
            DecodedTextLineTerminator.CarriageReturnLineFeed => 2,
            DecodedTextLineTerminator.CarriageReturn
                or DecodedTextLineTerminator.LineFeed
                or DecodedTextLineTerminator.NextLine
                or DecodedTextLineTerminator.LineSeparator
                or DecodedTextLineTerminator.ParagraphSeparator =>
                1,
            _ => throw new ArgumentOutOfRangeException(
                nameof(terminator)),
        };

    internal static string Text(
        DecodedTextLineTerminator terminator) =>
        terminator switch
        {
            DecodedTextLineTerminator.None => "",
            DecodedTextLineTerminator.CarriageReturnLineFeed => "\r\n",
            DecodedTextLineTerminator.CarriageReturn => "\r",
            DecodedTextLineTerminator.LineFeed => "\n",
            DecodedTextLineTerminator.NextLine => "\u0085",
            DecodedTextLineTerminator.LineSeparator => "\u2028",
            DecodedTextLineTerminator.ParagraphSeparator => "\u2029",
            _ => throw new ArgumentOutOfRangeException(
                nameof(terminator)),
        };
}

internal static class DecodedTextEncoding
{
    internal static int ScalarLength(
        ReadOnlySpan<char> text,
        int index) =>
        char.IsHighSurrogate(text[index])
        && index + 1 < text.Length
        && char.IsLowSurrogate(text[index + 1])
            ? 2
            : 1;

    internal static int JsonEncodedUtf8Length(
        ReadOnlySpan<char> text)
    {
        Span<char> encoded = stackalloc char[12];
        int total = 0;
        for (int index = 0; index < text.Length;)
        {
            int scalarLength = ScalarLength(text, index);
            OperationStatus status =
                JavaScriptEncoder.Default.Encode(
                    text.Slice(index, scalarLength),
                    encoded,
                    out int consumed,
                    out int written,
                    isFinalBlock: true);
            if (status != OperationStatus.Done
                || consumed != scalarLength)
            {
                throw new ArgumentException(
                    "Decoded text cannot be represented by the default "
                    + "JSON encoder.",
                    nameof(text));
            }

            total = checked(
                total
                + Encoding.UTF8.GetByteCount(encoded[..written]));
            index += scalarLength;
        }

        return total;
    }
}
