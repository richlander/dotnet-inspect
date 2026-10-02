namespace QuerySpace.Rows;

/// <summary>
/// Text-valued row query keys: case-insensitive ordinal wildcard matching for
/// Equals and NotEquals, and case-insensitive ordinal ordering.
/// </summary>
/// <remarks>
/// A pattern without <c>*</c> or <c>?</c> matches by case-insensitive ordinal
/// equality. <c>*</c> matches any run of characters, including none, and
/// <c>?</c> matches exactly one character.
/// </remarks>
public static class RowQueryText
{
    /// <summary>Case-insensitive ordinal comparer used to order text keys.</summary>
    public static IComparer<string> Order { get; } = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Creates a text key whose value is <paramref name="accessor"/>'s result,
    /// with a missing value presented as the empty string.
    /// </summary>
    public static RowQueryKey<TRow> Key<TRow>(
        string key,
        Func<TRow, string?> accessor,
        bool ordered = true)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        return RowQueryKey<TRow>.Create(
            RowQueryKeyIdentity.Create(),
            key,
            [RowQueryOperator.Equals, RowQueryOperator.NotEquals],
            row => RowQueryValue<string>.Present(accessor(row) ?? ""),
            Bind,
            ordered
                ? direction => RowQueryValueOrder.Create(
                    Order,
                    direction,
                    missingLast: false)
                : null);
    }

    /// <summary>
    /// Binds Equals and NotEquals to a wildcard match against the token text;
    /// returns <see langword="null"/> for any other operator.
    /// </summary>
    public static Predicate<string>? Bind(
        RowQueryOperator operation,
        RowQueryValueToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        string pattern = token.Text;
        return operation switch
        {
            RowQueryOperator.Equals =>
                value => Matches(value, pattern),
            RowQueryOperator.NotEquals =>
                value => !Matches(value, pattern),
            _ => null,
        };
    }

    /// <summary>
    /// Returns whether <paramref name="value"/> matches the wildcard
    /// <paramref name="pattern"/>.
    /// </summary>
    public static bool Matches(string value, string pattern)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(pattern);
        if (!pattern.Contains('*') && !pattern.Contains('?'))
        {
            return string.Equals(
                value,
                pattern,
                StringComparison.OrdinalIgnoreCase);
        }

        int textIndex = 0;
        int patternIndex = 0;
        int starIndex = -1;
        int matchIndex = 0;
        while (textIndex < value.Length)
        {
            if (patternIndex < pattern.Length
                && (pattern[patternIndex] == '?'
                    || char.ToUpperInvariant(pattern[patternIndex])
                        == char.ToUpperInvariant(value[textIndex])))
            {
                textIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length
                && pattern[patternIndex] == '*')
            {
                starIndex = patternIndex++;
                matchIndex = textIndex;
            }
            else if (starIndex >= 0)
            {
                patternIndex = starIndex + 1;
                textIndex = ++matchIndex;
            }
            else
            {
                return false;
            }
        }

        while (patternIndex < pattern.Length
            && pattern[patternIndex] == '*')
        {
            patternIndex++;
        }

        return patternIndex == pattern.Length;
    }
}
