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
    /// Resolves this selector to a 1-based row index against a known row count:
    /// <c>first</c> → 1, <c>last</c> → <paramref name="count"/>, and an explicit
    /// index passes through unchanged (still range-checked by the caller).
    /// </summary>
    public int Resolve(int count) => Kind switch
    {
        RowSelectorKind.First => 1,
        RowSelectorKind.Last => count,
        _ => _index,
    };

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
