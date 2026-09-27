using CSharpText;

namespace ILInspector.Metadata;

/// <summary>
/// A broadened Type-name match tier, ordered from strongest to weakest. Direct
/// matching remains <see cref="TypeMatcher.MatchesTypeFilter(string, string)"/>;
/// these tiers rank candidates that the direct grammar did not match.
/// </summary>
public enum TypeNameMatchTier
{
    /// <summary>The name starts with the pattern.</summary>
    Prefix,

    /// <summary>The simple name contains the pattern.</summary>
    Substring,

    /// <summary>The namespace-qualified name contains the pattern.</summary>
    Path,
}

/// <summary>
/// The product's single broadened Type-name ranking: per-candidate tier
/// predicates and within-tier order shared by every Type-name search host.
/// Hosts choose which tiers they evaluate and how a tier settles a request.
/// </summary>
public static class TypeNameMatchRanking
{
    /// <summary>
    /// Whether a pattern may be broadened beyond direct matching. Wildcards
    /// already state their own breadth, and explicit generic notation carries
    /// arity that a prefix or substring cannot honor.
    /// </summary>
    public static bool IsBroadenable(string pattern) =>
        !string.IsNullOrWhiteSpace(pattern)
        && pattern.AsSpan().IndexOfAny("*?<`") < 0;

    /// <summary>
    /// Classifies <paramref name="fullName"/> into the strongest broadened tier
    /// it satisfies for <paramref name="pattern"/>, or <see langword="null"/>.
    /// A dotted pattern matches from any namespace-segment boundary of the
    /// full name; an undotted pattern matches the simple base name. A wildcard
    /// pattern is a glob fragment: the same tiers test it with a trailing or
    /// surrounding <c>*</c>. Explicit generic notation is never broadened.
    /// </summary>
    public static TypeNameMatchTier? Classify(string fullName, string pattern)
    {
        if (string.IsNullOrEmpty(fullName)
            || string.IsNullOrWhiteSpace(pattern)
            || pattern.AsSpan().IndexOfAny("<`") >= 0)
        {
            return null;
        }

        string normalized = Normalize(fullName);
        string target = pattern.Trim().Replace('+', '.');
        if (!IsBroadenable(target))
            return ClassifyGlobFragment(normalized, target);
        if (target.Contains('.'))
        {
            if (TypeMatcher.MatchesTypeFilter(fullName, target + "*"))
                return TypeNameMatchTier.Prefix;
            return normalized.Contains(target, StringComparison.OrdinalIgnoreCase)
                ? TypeNameMatchTier.Substring
                : null;
        }

        string simple = SimpleBaseName(normalized);
        if (simple.StartsWith(target, StringComparison.OrdinalIgnoreCase))
            return TypeNameMatchTier.Prefix;
        if (simple.Contains(target, StringComparison.OrdinalIgnoreCase))
            return TypeNameMatchTier.Substring;
        return normalized.Contains(target, StringComparison.OrdinalIgnoreCase)
            ? TypeNameMatchTier.Path
            : null;
    }

    private static TypeNameMatchTier? ClassifyGlobFragment(
        string normalizedFullName,
        string fragment)
    {
        string simple = SimpleBaseName(normalizedFullName);
        if (TypeMatcher.MatchesGlob(simple, fragment + "*"))
            return TypeNameMatchTier.Prefix;
        if (TypeMatcher.MatchesGlob(simple, "*" + fragment + "*"))
            return TypeNameMatchTier.Substring;
        return TypeMatcher.MatchesGlob(normalizedFullName, "*" + fragment + "*")
            ? TypeNameMatchTier.Path
            : null;
    }

    /// <summary>
    /// Within-tier order: shorter simple base names first, because the
    /// shortest completion is the most likely intent, then simple name
    /// ordinal-ignore-case. Callers keep a stable sort so equal names retain
    /// their source order.
    /// </summary>
    public static int CompareWithinTier(string leftFullName, string rightFullName)
    {
        string left = SimpleBaseName(Normalize(leftFullName));
        string right = SimpleBaseName(Normalize(rightFullName));
        int byLength = left.Length.CompareTo(right.Length);
        return byLength != 0
            ? byLength
            : StringComparer.OrdinalIgnoreCase.Compare(left, right);
    }

    private static string Normalize(string typeName) =>
        FqnParser.NormalizeTypeName(typeName).Replace('+', '.');

    private static string SimpleBaseName(string normalizedFullName) =>
        TypeMatcher.GetBaseName(TypeMatcher.GetSimpleName(normalizedFullName));
}
