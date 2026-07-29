namespace DotnetInspector.Output;

/// <summary>
/// A per-table data-row window applied by <c>--rows</c>: keep the first
/// (<see cref="FromEnd"/> false, from <c>--head N</c>) or last
/// (<see cref="FromEnd"/> true, from <c>--tail N</c>) <see cref="Count"/> data
/// rows of each rendered table, preserving headings and table headers. A
/// negative <see cref="Count"/> is treated as "no limit" by the row limiters.
/// </summary>
public readonly record struct RowWindow(int Count, bool FromEnd);

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
