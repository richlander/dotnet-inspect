using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Normalizes the leading-dot sentinel used by the <c>find</c> member lens. A leading <c>.</c>
/// auto-enables member search (e.g. <c>.Serialize</c>) and is normally stripped to form the search
/// term. The constructor member names <c>.ctor</c> and <c>.cctor</c> genuinely begin with a dot,
/// however, so the sentinel must not be stripped when doing so would turn a constructor query into a
/// non-matching one. The decision reuses the same matcher the corpus member search uses
/// (<see cref="TypeMatcher"/>), so case-insensitivity and glob wildcards behave identically to the
/// actual search.
/// </summary>
public static class MemberPatternSentinel
{
    private static readonly string[] ConstructorNames = [".ctor", ".cctor"];

    /// <summary>
    /// Strips the leading sentinel <c>.</c> from a member pattern segment, but preserves it when the
    /// pattern is a constructor query — i.e. when it matches <c>.ctor</c>/<c>.cctor</c> yet the
    /// stripped form would not. Patterns that already match constructors after stripping (for example
    /// <c>.*</c> or <c>.*ctor</c>) are still stripped so the sentinel behaves consistently.
    /// </summary>
    public static string Strip(string pattern)
    {
        if (!pattern.StartsWith('.'))
            return pattern;

        var stripped = pattern[1..];
        foreach (var ctor in ConstructorNames)
        {
            if (Matches(pattern, ctor) && !Matches(stripped, ctor))
                return pattern;
        }

        return stripped;
    }

    // Mirrors the corpus member-search matcher: globs (containing * or ?) go through the
    // case-insensitive glob regex, everything else is a case-insensitive exact match.
    private static bool Matches(string pattern, string memberName)
        => pattern.Contains('*') || pattern.Contains('?')
            ? TypeMatcher.MatchesGlob(memberName, pattern)
            : TypeMatcher.MatchesMemberName(memberName, pattern);
}
