namespace DotnetInspector.Metadata;

/// <summary>
/// Generic-aware type name matching for searching types.
/// Handles namespace prefixes, generic arity suffixes, and type argument notation.
/// </summary>
public static class TypeMatcher
{
    /// <summary>
    /// Checks if a candidate type matches a target pattern.
    /// Supports partial names, namespace-qualified names, and generic types.
    /// </summary>
    /// <param name="candidate">The full type name to check (e.g., "System.Collections.Generic.List`1")</param>
    /// <param name="target">The search pattern (e.g., "List", "IEnumerable", "IList&lt;T&gt;")</param>
    /// <returns>True if candidate matches target</returns>
    public static bool Matches(string candidate, string target)
    {
        if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(target))
            return false;

        // Normalize both for comparison
        var normalizedCandidate = Normalize(candidate);
        var normalizedTarget = Normalize(target);

        // Exact match
        if (normalizedCandidate.Equals(normalizedTarget, StringComparison.OrdinalIgnoreCase))
            return true;

        // Match without namespace (e.g., "HttpClient" matches "System.Net.Http.HttpClient")
        if (normalizedCandidate.EndsWith("." + normalizedTarget, StringComparison.OrdinalIgnoreCase))
            return true;

        // Extract base names (before generic arity suffix)
        var candidateBase = GetBaseName(normalizedCandidate);
        var targetBase = GetBaseName(normalizedTarget);

        // Match base names
        if (candidateBase.Equals(targetBase, StringComparison.OrdinalIgnoreCase) ||
            candidateBase.EndsWith("." + targetBase, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Normalizes a type name by removing generic type arguments.
    /// "IEnumerable&lt;T&gt;" → "IEnumerable"
    /// "Dictionary&lt;string, int&gt;" → "Dictionary"
    /// </summary>
    public static string Normalize(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeName;

        // Remove generic type arguments: IEnumerable<T> → IEnumerable
        var angleIdx = typeName.IndexOf('<');
        if (angleIdx > 0)
            typeName = typeName[..angleIdx];

        return typeName;
    }

    /// <summary>
    /// Gets the base name without generic arity suffix.
    /// "List`1" → "List"
    /// "Dictionary`2" → "Dictionary"
    /// </summary>
    public static string GetBaseName(string typeName)
    {
        var backtickIdx = typeName.IndexOf('`');
        return backtickIdx >= 0 ? typeName[..backtickIdx] : typeName;
    }

    /// <summary>
    /// Extracts just the type name without namespace.
    /// "System.Collections.Generic.List`1" → "List`1"
    /// </summary>
    public static string GetSimpleName(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
    }

    /// <summary>
    /// Extracts the namespace from a full type name.
    /// "System.Collections.Generic.List`1" → "System.Collections.Generic"
    /// </summary>
    public static string? GetNamespace(string fullName)
    {
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName[..lastDot] : null;
    }

    /// <summary>
    /// Checks if a type name represents a generic type.
    /// </summary>
    public static bool IsGeneric(string typeName)
    {
        return typeName.Contains('`') || typeName.Contains('<');
    }

    /// <summary>
    /// Gets the generic arity from a type name.
    /// "List`1" → 1, "Dictionary`2" → 2, "String" → 0
    /// </summary>
    public static int GetGenericArity(string typeName)
    {
        var backtickIdx = typeName.IndexOf('`');
        if (backtickIdx < 0)
            return 0;

        var arityStr = typeName[(backtickIdx + 1)..];
        // Handle cases like "List`1+Enumerator"
        var plusIdx = arityStr.IndexOf('+');
        if (plusIdx >= 0)
            arityStr = arityStr[..plusIdx];

        return int.TryParse(arityStr, out var arity) ? arity : 0;
    }
}
