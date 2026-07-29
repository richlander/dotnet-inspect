using System.Globalization;

namespace DotnetInspector.Output;

/// <summary>
/// How a <see cref="RowSpec"/> addresses rows.
/// </summary>
public enum RowSpecKind
{
    /// <summary>
    /// A relative count of rows (<c>6</c>), anchored by a direction the spec
    /// does not itself carry: <c>--head</c> takes the first N, <c>--tail</c> the
    /// last N.
    /// </summary>
    Count,

    /// <summary>
    /// An absolute row range (<c>2..10</c>, <c>2+10</c>, <c>10..</c>). A range
    /// names rows outright, so it is complete on its own and a direction applied
    /// to it is incoherent.
    /// </summary>
    Range,
}

/// <summary>
/// A row selection parsed from <c>--rows</c>.
///
/// The grammar has exactly four forms, and the distinction between the first
/// and the rest is the point:
///
/// <list type="bullet">
///   <item><description><c>N</c> — a <see cref="RowSpecKind.Count"/> of N rows, like <c>head -n</c>. The digits are a quantity.</description></item>
///   <item><description><c>N..M</c> — an inclusive <see cref="RowSpecKind.Range"/>. The digits are positions, so <c>2..10</c> is nine rows.</description></item>
///   <item><description><c>N+K</c> — a range written as start plus count: K rows beginning at N.</description></item>
///   <item><description><c>N..</c> — a range from N to the end of the section.</description></item>
/// </list>
///
/// So <c>6</c> and <c>6..</c> both mention six but mean different things: the
/// first is "six rows", the second is "from row six". That is inherent to
/// offering both a count and a range in one flag, and is why the two forms parse
/// into different <see cref="RowSpecKind"/>s rather than into one normalized
/// shape — a caller that must reject one of them (a direction cannot apply to an
/// absolute range) has to be able to tell them apart.
/// </summary>
public readonly record struct RowSpec
{
    private readonly int _count;
    private readonly int _start;
    private readonly int? _end;

    private RowSpec(RowSpecKind kind, int count, int start, int? end)
    {
        Kind = kind;
        _count = count;
        _start = start;
        _end = end;
    }

    /// <summary>Which of the two addressing modes this spec uses.</summary>
    public RowSpecKind Kind { get; }

    /// <summary>
    /// For <see cref="RowSpecKind.Count"/>, the number of rows requested.
    /// Throws for a range, which has no single count to report; use
    /// <see cref="Start"/>/<see cref="End"/>, or <see cref="RowCount"/> for the
    /// extent either kind covers.
    /// </summary>
    public int Count => Kind == RowSpecKind.Count
        ? _count
        : throw WrongKind(nameof(Count), RowSpecKind.Count);

    /// <summary>
    /// For <see cref="RowSpecKind.Range"/>, the 1-based first row, inclusive.
    /// Throws for a count, whose first row is not known until a direction and a
    /// section supply one.
    /// </summary>
    public int Start => Kind == RowSpecKind.Range
        ? _start
        : throw WrongKind(nameof(Start), RowSpecKind.Range);

    /// <summary>
    /// For <see cref="RowSpecKind.Range"/>, the 1-based last row, inclusive, or
    /// <see langword="null"/> when the range runs to the end of the section
    /// (<c>N..</c>). Throws for a count, so that a caller cannot read the
    /// open-ended <see langword="null"/> out of a spec that is not a range at all.
    /// </summary>
    public int? End => Kind == RowSpecKind.Range
        ? _end
        : throw WrongKind(nameof(End), RowSpecKind.Range);

    /// <summary>
    /// The kind-specific members throw rather than returning a default, because
    /// every default available here is a lie a caller would act on: a
    /// <see cref="Start"/> of 0 is not a row, and a <see cref="Contains"/> of
    /// false is indistinguishable from a genuine miss. A spec is a two-case
    /// union, so reading the wrong case is a caller bug, and it should surface
    /// where it happens rather than as rows quietly going missing downstream.
    /// </summary>
    private InvalidOperationException WrongKind(string member, RowSpecKind required)
        => new($"{member} is only meaningful for a {required} row spec; this spec is {Kind}. Check {nameof(Kind)} first.");

    /// <summary>True when this spec names an absolute range rather than a count.</summary>
    public bool IsRange => Kind == RowSpecKind.Range;

    /// <summary>
    /// True for <c>N..</c>, whose end is whatever the section renders. False for
    /// a count, which is not a range at all.
    /// </summary>
    public bool IsOpenEnded => Kind == RowSpecKind.Range && _end is null;

    /// <summary>A count of N rows, anchored by a separate direction.</summary>
    public static RowSpec FromCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return new RowSpec(RowSpecKind.Count, count, 0, null);
    }

    /// <summary>An inclusive range. A null <paramref name="end"/> runs to the end of the section.</summary>
    public static RowSpec FromRange(int start, int? end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(start, 1);
        if (end is { } e)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(e, start);
        }

        return new RowSpec(RowSpecKind.Range, 0, start, end);
    }

    /// <summary>
    /// The number of rows this spec selects at most, or <see langword="null"/>
    /// only when that genuinely depends on the section — an open-ended
    /// <c>N..</c>. A count knows its own extent (<c>6</c> selects six rows
    /// whichever direction anchors it), so it reports it rather than pleading
    /// ignorance; what a count does not know is *which* rows, not how many.
    /// </summary>
    public int? RowCount => Kind switch
    {
        RowSpecKind.Count => _count,
        _ => _end is { } e ? e - _start + 1 : null,
    };

    /// <summary>
    /// True when <paramref name="rowNumber"/> falls inside this range.
    ///
    /// Throws for a count. Returning false there would answer a question the
    /// spec cannot answer — a count names no absolute rows until a direction and
    /// a section supply them — and would do it in the one way a caller cannot
    /// detect, since a false is exactly what a genuine miss looks like. A caller
    /// that filters rows through this would silently select nothing.
    /// </summary>
    public bool Contains(int rowNumber)
    {
        if (Kind != RowSpecKind.Range)
            throw WrongKind(nameof(Contains), RowSpecKind.Range);

        return rowNumber >= _start && (_end is not { } e || rowNumber <= e);
    }

    /// <summary>
    /// Parses a <c>--rows</c> token. Returns false with a caller-renderable
    /// <paramref name="error"/> describing the grammar violation.
    ///
    /// The grammar is deliberately closed: anything outside the four documented
    /// forms is rejected rather than guessed at. Row selection that quietly
    /// reinterprets its input is the defect this flag exists to replace.
    /// </summary>
    public static bool TryParse(string? text, out RowSpec spec, out string? error)
    {
        spec = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "a row selection is required, such as 6, 2..10, 2+10, or 10..";
            return false;
        }

        var token = text.Trim();

        // A colon form is rejected outright rather than left to fail as a generic
        // parse error. `2:10` is a slice in Python's sense -- 0-based and
        // end-exclusive -- so for identical digits it would name a different set
        // of rows than `2..10` by exactly one row at each edge. Silently accepting
        // it under `..` semantics, or rejecting it without explanation, both leave
        // the user to discover the discrepancy from the output.
        if (token.Contains(':', StringComparison.Ordinal))
        {
            error = $"'{token}' uses ':' which is not a row range. Use '..' for an inclusive range (2..10 is nine rows) or '+' for start plus count (2+10 is ten rows).";
            return false;
        }

        var rangeIndex = token.IndexOf("..", StringComparison.Ordinal);
        if (rangeIndex >= 0)
            return TryParseRange(token, rangeIndex, ref spec, ref error);

        var plusIndex = token.IndexOf('+', StringComparison.Ordinal);
        if (plusIndex > 0)
            return TryParseStartPlusCount(token, plusIndex, ref spec, ref error);

        if (!TryParseComponent(token, out var count))
        {
            error = $"'{token}' is not a row selection. Use a count (6), an inclusive range (2..10), a start plus count (2+10), or an open range (10..).";
            return false;
        }

        if (count < 1)
        {
            error = $"a row count must be 1 or greater (got {count}).";
            return false;
        }

        spec = FromCount(count);
        return true;
    }

    private static bool TryParseRange(string token, int rangeIndex, ref RowSpec spec, ref string? error)
    {
        var startText = token[..rangeIndex];
        var endText = token[(rangeIndex + 2)..];

        if (endText.Contains("..", StringComparison.Ordinal) || endText.Contains('+', StringComparison.Ordinal))
        {
            error = $"'{token}' has more than one range operator. Use a single range such as 2..10.";
            return false;
        }

        if (startText.Length == 0)
        {
            error = $"'{token}' has no start row. An open range needs a start, as in 10.. ; to begin at the first row use 1..{endText}.";
            return false;
        }

        if (!TryParseComponent(startText, out var start))
        {
            error = $"'{startText}' is not a row number in '{token}'.";
            return false;
        }

        if (start < 1)
        {
            error = $"row numbers start at 1 (got {start} in '{token}').";
            return false;
        }

        // `N..` is open-ended: the end is whatever the section renders.
        if (endText.Length == 0)
        {
            spec = FromRange(start, null);
            return true;
        }

        if (!TryParseComponent(endText, out var end))
        {
            error = $"'{endText}' is not a row number in '{token}'.";
            return false;
        }

        if (end < start)
        {
            error = $"'{token}' ends before it starts. A range is inclusive and ascending, so use {end}..{start} to select those rows.";
            return false;
        }

        spec = FromRange(start, end);
        return true;
    }

    private static bool TryParseStartPlusCount(string token, int plusIndex, ref RowSpec spec, ref string? error)
    {
        var startText = token[..plusIndex];
        var countText = token[(plusIndex + 1)..];

        if (countText.Contains('+', StringComparison.Ordinal))
        {
            error = $"'{token}' has more than one '+'. Use a single start plus count such as 2+10.";
            return false;
        }

        if (!TryParseComponent(startText, out var start) || !TryParseComponent(countText, out var count))
        {
            error = $"'{token}' is not a start plus count. Use two row numbers such as 2+10.";
            return false;
        }

        if (start < 1)
        {
            error = $"row numbers start at 1 (got {start} in '{token}').";
            return false;
        }

        if (count < 1)
        {
            error = $"a row count must be 1 or greater (got {count} in '{token}').";
            return false;
        }

        // Computed in long so a start plus count that lands past int.MaxValue is
        // reported as out of range rather than wrapping into a valid-looking row.
        var end = (long)start + count - 1;
        if (end > int.MaxValue)
        {
            error = $"'{token}' selects rows past the largest addressable row.";
            return false;
        }

        spec = FromRange(start, (int)end);
        return true;
    }

    /// <summary>
    /// Parses one digits-only component. A leading sign is rejected here rather
    /// than parsed and range-checked later, so that <c>2..-1</c> reads as a
    /// malformed range instead of silently becoming an empty one.
    /// </summary>
    private static bool TryParseComponent(string text, out int value)
    {
        value = 0;
        if (text.Length == 0)
            return false;

        foreach (var c in text)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Renders this spec back into its canonical token, so an error or an echo
    /// shows the same spelling the grammar accepts.
    /// </summary>
    public override string ToString() => Kind switch
    {
        RowSpecKind.Count => _count.ToString(CultureInfo.InvariantCulture),
        _ when _end is null => $"{_start}..",
        _ => $"{_start}..{_end}",
    };
}
