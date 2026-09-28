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

public sealed class DecodedTextPullLimits
{
    public DecodedTextPullLimits(
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

    public static DecodedTextPullLimits ForCandidateRows(
        int maximumCandidateRows) =>
        new(
            maximumCandidateRows,
            int.MaxValue,
            int.MaxValue);
}

public sealed class DecodedTextLineLimitException
    : InvalidOperationException
{
    internal DecodedTextLineLimitException(
        int lineNumber,
        int utf16CodeUnits,
        int maximumUtf16CodeUnits,
        int jsonEncodedUtf8Bytes,
        int maximumJsonEncodedUtf8Bytes)
        : base(
            $"Decoded text line {lineNumber:N0} requires "
            + $"{utf16CodeUnits:N0} UTF-16 code units and "
            + $"{jsonEncodedUtf8Bytes:N0} JSON-encoded UTF-8 bytes, "
            + "which exceeds the pull limits of "
            + $"{maximumUtf16CodeUnits:N0} UTF-16 code units and "
            + $"{maximumJsonEncodedUtf8Bytes:N0} JSON-encoded UTF-8 bytes.")
    {
        LineNumber = lineNumber;
        Utf16CodeUnits = utf16CodeUnits;
        MaximumUtf16CodeUnits = maximumUtf16CodeUnits;
        JsonEncodedUtf8Bytes = jsonEncodedUtf8Bytes;
        MaximumJsonEncodedUtf8Bytes = maximumJsonEncodedUtf8Bytes;
    }

    public int LineNumber { get; }

    public int Utf16CodeUnits { get; }

    public int MaximumUtf16CodeUnits { get; }

    public int JsonEncodedUtf8Bytes { get; }

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

public readonly struct DecodedTextCursor
{
    private readonly object? _documentIdentity;

    internal DecodedTextCursor(
        object documentIdentity,
        int utf16Offset,
        int nextLineNumber,
        int? exactLineCount = null)
    {
        _documentIdentity = documentIdentity;
        Utf16Offset = utf16Offset;
        NextLineNumber = nextLineNumber;
        ExactLineCount = exactLineCount;
    }

    public bool IsComplete => ExactLineCount is not null;

    public int? ExactLineCount { get; }

    internal int Utf16Offset { get; }

    internal int NextLineNumber { get; }

    internal bool Matches(object documentIdentity) =>
        ReferenceEquals(_documentIdentity, documentIdentity);
}

public readonly struct DecodedTextLineSlice
{
    private readonly ReadOnlyMemory<char> _rowText;
    private readonly int _contentLength;

    internal DecodedTextLineSlice(
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
    }

    public int Number { get; }

    public int Start { get; }

    public ReadOnlyMemory<char> Content =>
        _rowText[.._contentLength];

    public DecodedTextLineTerminator Terminator { get; }

    public string TerminatorText =>
        DecodedTextTerminators.Text(Terminator);

    public int Utf16CodeUnits => _rowText.Length;

    public int JsonEncodedUtf8Bytes =>
        DecodedTextEncoding.JsonEncodedUtf8Length(_rowText.Span);

    internal ReadOnlyMemory<char> RowText => _rowText;
}

public sealed class DecodedTextLine
{
    private readonly ReadOnlyMemory<char> _rowText;
    private readonly int _contentLength;

    internal DecodedTextLine(DecodedTextLineSlice slice)
    {
        Number = slice.Number;
        Start = slice.Start;
        _rowText = slice.RowText;
        _contentLength = slice.Content.Length;
        Terminator = slice.Terminator;
        JsonEncodedUtf8Bytes =
            DecodedTextEncoding.JsonEncodedUtf8Length(_rowText.Span);
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
}

public sealed class DecodedTextBatch
{
    internal DecodedTextBatch(
        ImmutableArray<DecodedTextLine> lines,
        DecodedTextPosition? continuation,
        int utf16CodeUnits,
        int jsonEncodedUtf8Bytes)
    {
        Lines = lines;
        Continuation = continuation;
        Utf16CodeUnits = utf16CodeUnits;
        JsonEncodedUtf8Bytes = jsonEncodedUtf8Bytes;
    }

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
    private static readonly SearchValues<char> s_lineTerminators =
        SearchValues.Create("\r\n\u0085\u2028\u2029");

    private readonly string _text;
    private readonly object _identity = new();

    public DecodedTextDocument(string text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    public int Utf16Length => _text.Length;

    public DecodedTextPosition Start =>
        new(_identity, utf16Offset: 0, nextLineNumber: 1);

    public DecodedTextCursor StartCursor =>
        new(_identity, utf16Offset: 0, nextLineNumber: 1);

    public bool TryReadLine(
        ref DecodedTextCursor cursor,
        out DecodedTextLineSlice line)
    {
        if (!cursor.Matches(_identity))
        {
            throw new ArgumentException(
                "The cursor belongs to a different decoded document.",
                nameof(cursor));
        }

        if (cursor.IsComplete)
        {
            line = default;
            return false;
        }

        line = ReadLine(cursor, out DecodedTextCursor next);
        cursor = next;
        return true;
    }

    public DecodedTextBatch Pull(
        DecodedTextPosition position,
        DecodedTextPullLimits limits)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentNullException.ThrowIfNull(limits);
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
        while (lines.Count < limits.MaximumCandidateRows)
        {
            (DecodedTextLine line, DecodedTextPosition? next) =
                ReadLine(current);
            bool oversized =
                line.Utf16CodeUnits
                    > limits.MaximumUtf16CodeUnits
                || line.JsonEncodedUtf8Bytes
                    > limits.MaximumJsonEncodedUtf8Bytes;
            bool exceedsBatch =
                utf16CodeUnits + (long)line.Utf16CodeUnits
                    > limits.MaximumUtf16CodeUnits
                || jsonEncodedUtf8Bytes
                    + (long)line.JsonEncodedUtf8Bytes
                    > limits.MaximumJsonEncodedUtf8Bytes;
            if (lines.Count != 0 && (oversized || exceedsBatch))
            {
                return CreateBatch(
                    lines,
                    current,
                    utf16CodeUnits,
                    jsonEncodedUtf8Bytes);
            }

            if (oversized)
            {
                throw new DecodedTextLineLimitException(
                    line.Number,
                    line.Utf16CodeUnits,
                    limits.MaximumUtf16CodeUnits,
                    line.JsonEncodedUtf8Bytes,
                    limits.MaximumJsonEncodedUtf8Bytes);
            }

            lines.Add(line);
            utf16CodeUnits =
                checked(utf16CodeUnits + line.Utf16CodeUnits);
            jsonEncodedUtf8Bytes =
                checked(
                    jsonEncodedUtf8Bytes
                    + line.JsonEncodedUtf8Bytes);
            current = next;

            if (current is null)
            {
                return CreateBatch(
                    lines,
                    continuation: null,
                    utf16CodeUnits,
                    jsonEncodedUtf8Bytes);
            }
        }
        return CreateBatch(
            lines,
            current,
            utf16CodeUnits,
            jsonEncodedUtf8Bytes);
    }

    private (DecodedTextLine Line, DecodedTextPosition? Next)
        ReadLine(DecodedTextPosition position)
    {
        var cursor = new DecodedTextCursor(
            _identity,
            position.Utf16Offset,
            position.NextLineNumber);
        DecodedTextLineSlice slice =
            ReadLine(cursor, out DecodedTextCursor next);
        var line = new DecodedTextLine(slice);
        DecodedTextPosition? nextPosition =
            next.IsComplete
                ? null
                : new DecodedTextPosition(
                    _identity,
                    next.Utf16Offset,
                    next.NextLineNumber);
        return (line, nextPosition);
    }

    private DecodedTextLineSlice ReadLine(
        DecodedTextCursor cursor,
        out DecodedTextCursor next)
    {
        int lineStart = cursor.Utf16Offset;
        if (lineStart < 0 || lineStart > _text.Length)
        {
            throw new ArgumentException(
                "The decoded-text cursor is outside the document.",
                nameof(cursor));
        }

        int relativeTerminator =
            _text.AsSpan(lineStart).IndexOfAny(s_lineTerminators);
        int index =
            relativeTerminator < 0
                ? _text.Length
                : checked(lineStart + relativeTerminator);
        DecodedTextLineTerminator terminator =
            relativeTerminator < 0
                ? DecodedTextLineTerminator.None
                : DecodedTextTerminators.At(_text, index);

        int terminatorLength =
            DecodedTextTerminators.Utf16Length(terminator);
        var line = new DecodedTextLineSlice(
            cursor.NextLineNumber,
            lineStart,
            _text.AsMemory(
                lineStart,
                index - lineStart + terminatorLength),
            index - lineStart,
            terminator);
        next =
            terminator == DecodedTextLineTerminator.None
                ? new DecodedTextCursor(
                    _identity,
                    utf16Offset: _text.Length,
                    cursor.NextLineNumber,
                    exactLineCount: cursor.NextLineNumber)
                : new DecodedTextCursor(
                    _identity,
                    index + terminatorLength,
                    checked(cursor.NextLineNumber + 1));
        return line;
    }

    private static DecodedTextBatch CreateBatch(
        ImmutableArray<DecodedTextLine>.Builder lines,
        DecodedTextPosition? continuation,
        int utf16CodeUnits,
        int jsonEncodedUtf8Bytes) =>
        new(
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
