namespace DotnetInspector.Output;

/// <summary>
/// A per-table data-row window applied by <c>--rows</c>, preserving headings and
/// table headers. A window is either <em>relative</em> — the first
/// (<see cref="Head"/>) or last (<see cref="Tail"/>) N rows, where which rows
/// those are depends on how many the table has — or <em>absolute</em>
/// (<see cref="Range"/>), naming the row numbers to keep regardless of the
/// table's size.
///
/// The two are not interchangeable, which is why this type is constructed
/// through named factories rather than a positional constructor: a bare pair of
/// numbers cannot say whether it means "two rows" or "row two", and #3364 was
/// caused by exactly that ambiguity being resolved silently in the wrong
/// direction.
///
/// <see cref="Resolve"/> is the single place these semantics are interpreted.
/// Both row limiters call it rather than branching on the window's shape, so a
/// change to what a window means cannot land in one renderer and miss another.
/// </summary>
public readonly record struct RowWindow
{
    private readonly int _count;
    private readonly int _start;
    private readonly int? _end;

    private RowWindow(RowWindowKind kind, int count, int start, int? end)
    {
        Kind = kind;
        _count = count;
        _start = start;
        _end = end;
    }

    public RowWindowKind Kind { get; }

    /// <summary>
    /// True when this window keeps every row, so a renderer can skip windowing
    /// entirely. Only a relative window can be unlimited (via a negative count);
    /// an absolute range always names a bounded start, even when open-ended.
    /// </summary>
    public bool IsUnlimited => Kind != RowWindowKind.Range && _count < 0;

    /// <summary>
    /// Keep the first <paramref name="count"/> data rows. A negative count means
    /// "no limit", which the row limiters rely on to render a table untouched.
    /// </summary>
    public static RowWindow Head(int count) => new(RowWindowKind.Head, count, 0, null);

    /// <summary>Keep the last <paramref name="count"/> data rows.</summary>
    public static RowWindow Tail(int count) => new(RowWindowKind.Tail, count, 0, null);

    /// <summary>
    /// Keep the rows numbered <paramref name="start"/> through
    /// <paramref name="end"/> inclusive, or through the last row when
    /// <paramref name="end"/> is null. Row numbers are 1-based and are the
    /// numbers a reader counts in the rendered table.
    /// </summary>
    public static RowWindow Range(int start, int? end)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(start, 1);
        if (end is int e)
            ArgumentOutOfRangeException.ThrowIfLessThan(e, start);
        return new(RowWindowKind.Range, 0, start, end);
    }

    /// <summary>
    /// Resolves this window against a table that rendered
    /// <paramref name="dataCount"/> data rows, returning the half-open range of
    /// 0-based data-row positions to keep.
    ///
    /// The result is always a valid range (<c>0 &lt;= keepStart &lt;= keepEnd
    /// &lt;= dataCount</c>), so a caller can use it without re-clamping. An
    /// absolute range that starts past the end of the table resolves to an empty
    /// window rather than an error: the rows it names simply are not there.
    ///
    /// For a Range the ordering half of that invariant holds by construction, not
    /// by clamping here: <see cref="Range"/> rejects <c>end &lt; start</c>, so
    /// <c>_end &gt;= _start</c>, and <see cref="Math.Min(int,int)"/> is monotonic.
    /// <c>RowWindowResolutionTests.Range_RefusesAnEndBeforeItsStart</c> is the gate.
    /// </summary>
    public (int KeepStart, int KeepEnd) Resolve(int dataCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(dataCount);

        switch (Kind)
        {
            case RowWindowKind.Head:
                // A negative count is "no limit"; clamping it to dataCount keeps
                // the whole table rather than emptying it.
                return (0, _count < 0 ? dataCount : Math.Min(_count, dataCount));
            case RowWindowKind.Tail:
                return (_count < 0 ? 0 : Math.Max(0, dataCount - _count), dataCount);
            default:
                var start = Math.Min(_start - 1, dataCount);
                var end = _end is int e ? Math.Min(e, dataCount) : dataCount;
                return (start, end);
        }
    }
}

/// <summary>Whether a <see cref="RowWindow"/> counts rows or names them.</summary>
public enum RowWindowKind
{
    /// <summary>The first N rows.</summary>
    Head,

    /// <summary>The last N rows.</summary>
    Tail,

    /// <summary>An absolute, 1-based, inclusive span of row numbers.</summary>
    Range,
}

/// <summary>
/// Thrown when <c>--rows</c> is combined with an invalid head/tail window
/// (both or neither). Surfaced as a one-line CLI error by the entry point.
/// </summary>
public sealed class RowWindowValidationException(string message) : Exception(message);

/// <summary>
/// A printable/projected-row selector parsed from <c>--row</c>: an explicit
/// 1-based index, or the symbolic <c>first</c>/<c>last</c> endpoints resolved
/// against the actual row count at render time.
/// </summary>
public readonly record struct RowSelector
{
    private readonly int _index;

    private RowSelector(RowSelectorKind kind, int index)
    {
        Kind = kind;
        _index = index;
    }

    public RowSelectorKind Kind { get; }

    /// <summary>The first printable/projected row.</summary>
    public static RowSelector First { get; } = new(RowSelectorKind.First, 0);

    /// <summary>The last printable/projected row.</summary>
    public static RowSelector Last { get; } = new(RowSelectorKind.Last, 0);

    /// <summary>An explicit 1-based row index (range-checked by the caller).</summary>
    public static RowSelector FromIndex(int index) => new(RowSelectorKind.Index, index);

    /// <summary>
    /// Resolves this selector to the row number it addresses, given the row
    /// numbers a projection actually rendered, in render order.
    ///
    /// This deliberately takes the rendered numbers rather than a count. A
    /// projection that drops rows carrying no value leaves gaps, so the Nth
    /// entry of the list and the row labelled N are different rows, and a count
    /// cannot tell them apart. <c>first</c>/<c>last</c> are the endpoints of the
    /// rendered sequence — the first and last rows a reader would count — not 1
    /// and <c>count</c>. An explicit index passes through as the row number it
    /// names; the caller looks it up and reports a miss.
    /// </summary>
    public int Resolve(IReadOnlyList<int> rowNumbers)
    {
        ArgumentNullException.ThrowIfNull(rowNumbers);
        ArgumentOutOfRangeException.ThrowIfZero(rowNumbers.Count);

        return Kind switch
        {
            RowSelectorKind.First => rowNumbers[0],
            RowSelectorKind.Last => rowNumbers[^1],
            _ => _index,
        };
    }

    /// <summary>
    /// Parses a <c>--row</c> token: a case-insensitive <c>first</c>/<c>last</c>
    /// keyword or an integer. Returns false for any other token.
    /// </summary>
    public static bool TryParse(string? text, out RowSelector selector)
    {
        selector = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var trimmed = text.Trim();
        if (trimmed.Equals("first", StringComparison.OrdinalIgnoreCase))
        {
            selector = First;
            return true;
        }

        if (trimmed.Equals("last", StringComparison.OrdinalIgnoreCase))
        {
            selector = Last;
            return true;
        }

        if (int.TryParse(trimmed, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var index))
        {
            selector = FromIndex(index);
            return true;
        }

        return false;
    }
}

public enum RowSelectorKind
{
    Index,
    First,
    Last,
}

/// <summary>
/// Helpers for addressing rendered rows by the number a reader arrives at when
/// counting them.
///
/// Selection and identity are the same concern here: a projection carries the
/// row number it rendered on every row, so selection looks that number up
/// instead of indexing the list positionally. Positional indexing is what made
/// <c>--row</c> address a sequence the reader cannot reconstruct, and it fails
/// silently — it returns a real row, just not the requested one.
/// </summary>
public static class RowNumbering
{
    /// <summary>
    /// Returns the position in <paramref name="rowNumbers"/> of the row labelled
    /// <paramref name="rowNumber"/>, or -1 when no rendered row carries it.
    /// </summary>
    public static int IndexOf(IReadOnlyList<int> rowNumbers, int rowNumber)
    {
        ArgumentNullException.ThrowIfNull(rowNumbers);

        for (var i = 0; i < rowNumbers.Count; i++)
        {
            if (rowNumbers[i] == rowNumber)
                return i;
        }

        return -1;
    }

    /// <summary>
    /// Describes the addressable rows for an error message. A contiguous run
    /// reads as a range; a gapped sequence is listed explicitly, because the
    /// gaps are the whole reason the requested number missed and a range would
    /// name rows that cannot be selected. Long lists are elided in the middle so
    /// the message stays one line while still showing both endpoints.
    /// </summary>
    public static string Describe(IReadOnlyList<int> rowNumbers)
    {
        ArgumentNullException.ThrowIfNull(rowNumbers);

        if (rowNumbers.Count == 0)
            return "none";
        if (rowNumbers.Count == 1)
            return rowNumbers[0].ToString(System.Globalization.CultureInfo.InvariantCulture);

        var contiguous = true;
        for (var i = 1; i < rowNumbers.Count; i++)
        {
            if (rowNumbers[i] != rowNumbers[i - 1] + 1)
            {
                contiguous = false;
                break;
            }
        }

        if (contiguous)
            return $"{rowNumbers[0]} through {rowNumbers[^1]}";

        const int shown = 8;
        if (rowNumbers.Count <= shown)
            return string.Join(", ", rowNumbers);

        var head = string.Join(", ", rowNumbers.Take(shown - 1));
        return $"{head}, … , {rowNumbers[^1]}";
    }
}
