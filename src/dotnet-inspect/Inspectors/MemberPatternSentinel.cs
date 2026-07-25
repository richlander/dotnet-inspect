using ILInspector.Metadata;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Normalizes the leading-dot sentinel used by the <c>find</c> member lens. A leading <c>.</c>
/// auto-enables member search (e.g. <c>.Serialize</c>) and is normally stripped to form the search
/// term. The only metadata member names that genuinely begin with a dot are the constructor names
/// <c>.ctor</c> and <c>.cctor</c>, so the sentinel is preserved solely for an exact
/// (case-insensitive) match of one of those two names.
/// </summary>
public static class MemberPatternSentinel
{
    private static readonly string[] ConstructorNames = [".ctor", ".cctor"];

    /// <summary>
    /// Strips the leading sentinel <c>.</c> from a member pattern segment, preserving it only when the
    /// pattern is an exact (case-insensitive) match for the constructor names <c>.ctor</c> or
    /// <c>.cctor</c>. Every other leading-dot pattern — including globs such as <c>.c*</c> or
    /// <c>.ctor*</c> — treats the dot purely as the member-lens sentinel and strips it, because a glob
    /// against the two-name dot-prefixed domain cannot unambiguously signal constructor intent
    /// (<c>.c*</c> and <c>.ctor*</c> both intersect <c>.ctor</c>). To glob-match constructors, use the
    /// explicit member lens with a wildcard that spans the dot, e.g. <c>find "?ctor" --members</c>.
    /// </summary>
    public static string Strip(string pattern)
    {
        if (!pattern.StartsWith('.'))
            return pattern;

        foreach (var ctor in ConstructorNames)
        {
            // MatchesMemberName is the corpus member matcher's exact (non-glob) path: a
            // case-insensitive equality check. Reusing it keeps preservation aligned with search.
            if (TypeMatcher.MatchesMemberName(ctor, pattern))
                return pattern;
        }

        return pattern[1..];
    }
}
