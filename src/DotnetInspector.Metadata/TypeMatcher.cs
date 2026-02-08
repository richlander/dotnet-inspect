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
    /// Finds the closest matching type names from a set of candidates, ranked by similarity.
    /// Compares using base names (without namespace or generic arity) for best results.
    /// </summary>
    /// <param name="candidates">Type names to search through</param>
    /// <param name="target">The type name to match against</param>
    /// <param name="minSimilarity">Minimum similarity score (0.0–1.0) to include in results</param>
    /// <param name="maxResults">Maximum number of results to return</param>
    public static IEnumerable<(string Name, double Similarity)> FindClosest(
        IEnumerable<string> candidates,
        string target,
        double minSimilarity = 0.6,
        int maxResults = 5)
    {
        if (string.IsNullOrEmpty(target))
            yield break;

        var targetBase = GetBaseName(GetSimpleName(Normalize(target)));

        var scored = new List<(string Name, double Similarity)>();

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate))
                continue;

            // Exact match is handled by Matches — skip here
            if (Matches(candidate, target))
                continue;

            var candidateBase = GetBaseName(GetSimpleName(Normalize(candidate)));
            var similarity = StringDistance.Similarity(candidateBase, targetBase);

            if (similarity >= minSimilarity)
            {
                scored.Add((candidate, similarity));
            }
        }

        foreach (var result in scored.OrderByDescending(s => s.Similarity).Take(maxResults))
        {
            yield return result;
        }
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
