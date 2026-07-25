namespace DotnetInspector.Options;

/// <summary>
/// Normalizes the leading-dot sentinel used by the <c>find</c> member lens. A leading <c>.</c>
/// auto-enables member search (e.g. <c>.Serialize</c>) and is stripped to form the search term.
/// <c>.ctor</c> and <c>.cctor</c> are the only real metadata member names that begin with a dot, so
/// they are preserved verbatim — stripping them would turn an exact constructor search into a
/// non-matching <c>ctor</c>/<c>cctor</c> query.
/// </summary>
public static class MemberPatternSentinel
{
    /// <summary>
    /// Strips a single leading sentinel <c>.</c> from a member pattern segment, preserving the
    /// constructor member names <c>.ctor</c> and <c>.cctor</c>.
    /// </summary>
    public static string Strip(string pattern)
        => pattern is ".ctor" or ".cctor"
            ? pattern
            : pattern.StartsWith('.') ? pattern[1..] : pattern;
}
