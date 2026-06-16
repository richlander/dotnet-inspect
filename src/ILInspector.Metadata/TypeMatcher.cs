using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ILInspector.Metadata;

/// <summary>
/// Result of a type name lookup: either a single match or a list of suggestions.
/// </summary>
public record LookupResult(string? Match, IReadOnlyList<string> Suggestions);

/// <summary>
/// Result of a member name lookup: matching names and/or suggestions.
/// </summary>
public record MemberLookupResult(IReadOnlyList<string> Matches, IReadOnlyList<string> Suggestions);

/// <summary>
/// Generic-aware type name matching for searching types.
/// Handles namespace prefixes, generic arity suffixes, and type argument notation.
/// </summary>
public static class TypeMatcher
{
    private static readonly ConcurrentDictionary<string, Regex> _globCache = new();

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
        if (EndsWithDottedSuffix(normalizedCandidate, normalizedTarget))
            return true;

        // Extract base names (before generic arity suffix)
        var candidateBase = GetBaseName(normalizedCandidate);
        var targetBase = GetBaseName(normalizedTarget);

        // Match base names
        if (candidateBase.Equals(targetBase, StringComparison.OrdinalIgnoreCase) ||
            EndsWithDottedSuffix(candidateBase, targetBase))
            return true;

        return false;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> ends with ".<paramref name="suffix"/>" (case-insensitive)
    /// — i.e. a namespace-qualified name ending in the simple name. Avoids allocating "." + suffix
    /// on every call, since this runs in the inner loop of every type scanner.
    /// </summary>
    private static bool EndsWithDottedSuffix(string candidate, string suffix)
        => candidate.Length > suffix.Length
           && candidate[candidate.Length - suffix.Length - 1] == '.'
           && candidate.AsSpan(candidate.Length - suffix.Length)
               .Equals(suffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Normalizes a type name by converting C#-style generic arguments to CLR backtick notation.
    /// "IEnumerable&lt;T&gt;" → "IEnumerable`1"
    /// "Dictionary&lt;string, int&gt;" → "Dictionary`2"
    /// "List&lt;T&gt;+Enumerator" → "List`1+Enumerator" (trailing suffix preserved)
    /// Already-normalized names like "List`1" pass through unchanged.
    /// This is the single canonical C#→CLR name converter for the tool.
    /// </summary>
    public static string Normalize(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return typeName;

        var angleIdx = typeName.IndexOf('<');
        if (angleIdx <= 0)
            return typeName;

        var closeIdx = typeName.LastIndexOf('>');
        if (closeIdx <= angleIdx)
            return typeName;

        var baseName = typeName[..angleIdx];
        int arity = CountTypeParameters(typeName.AsSpan((angleIdx + 1)..closeIdx));
        var suffix = closeIdx + 1 < typeName.Length ? typeName[(closeIdx + 1)..] : "";
        return $"{baseName}`{arity}{suffix}";
    }

    /// <summary>
    /// Normalizes a member selector by removing C# generic method type arguments.
    /// "Deserialize&lt;TValue&gt;" -> "Deserialize". Malformed/non-generic names pass through unchanged.
    /// </summary>
    public static string NormalizeMemberName(string memberName)
    {
        var angleIdx = memberName.IndexOf('<');
        if (angleIdx <= 0)
            return memberName;

        var closeIdx = memberName.LastIndexOf('>');
        if (closeIdx <= angleIdx || closeIdx != memberName.Length - 1)
            return memberName;

        return memberName[..angleIdx];
    }

    private static int CountTypeParameters(ReadOnlySpan<char> typeParams)
    {
        if (typeParams.IsEmpty || typeParams.IsWhiteSpace())
            return 0;

        int count = 1;
        int depth = 0;
        foreach (char c in typeParams)
        {
            if (c == '<') depth++;
            else if (c == '>') depth--;
            else if (c == ',' && depth == 0) count++;
        }
        return count;
    }

    /// <summary>
    /// Gets the base name without generic arity suffixes.
    /// "List`1" → "List"; "Dictionary`2.KeyCollection" → "Dictionary.KeyCollection".
    /// </summary>
    public static string GetBaseName(string typeName)
    {
        var backtickIdx = typeName.IndexOf('`');
        if (backtickIdx < 0)
            return typeName;

        var result = new System.Text.StringBuilder(typeName.Length);
        for (var i = 0; i < typeName.Length; i++)
        {
            if (typeName[i] != '`')
            {
                result.Append(typeName[i]);
                continue;
            }

            i++;
            while (i < typeName.Length && char.IsDigit(typeName[i]))
                i++;

            if (i < typeName.Length)
                result.Append(typeName[i]);
        }

        return result.ToString();
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

        List<(string Name, double Similarity)> scored = [];

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

        var digitStart = backtickIdx + 1;
        var digitEnd = digitStart;
        while (digitEnd < typeName.Length && char.IsDigit(typeName[digitEnd]))
            digitEnd++;

        return digitEnd > digitStart && int.TryParse(typeName.AsSpan(digitStart, digitEnd - digitStart), out var arity)
            ? arity
            : 0;
    }

    /// <summary>
    /// Gets the expected generic arity from a search pattern.
    /// Supports both C# notation and CLR backtick notation.
    /// "Option&lt;T&gt;" → 1, "Dictionary&lt;K,V&gt;" → 2, "Option`1" → 1, "Option" → -1 (unspecified)
    /// </summary>
    public static int GetPatternArity(string pattern)
    {
        // First check for CLR backtick notation (Option`1, Dictionary`2)
        var backtickArity = GetGenericArity(pattern);
        if (backtickArity > 0)
            return backtickArity;

        // Then check for C# angle bracket notation (Option<T>, Dictionary<K,V>)
        var startIdx = pattern.IndexOf('<');
        var endIdx = pattern.LastIndexOf('>');
        if (startIdx < 0 || endIdx <= startIdx)
            return -1; // No generic notation, arity unspecified

        // -1 (unspecified) for empty/whitespace args; otherwise the top-level type-parameter count.
        var arity = CountTypeParameters(pattern.AsSpan((startIdx + 1)..endIdx));
        return arity == 0 ? -1 : arity;
    }

    /// <summary>
    /// Tests whether <paramref name="text"/> matches a glob pattern (* and ? wildcards).
    /// Case-insensitive.
    /// </summary>
    public static bool MatchesGlob(string text, string pattern)
    {
        var regex = _globCache.GetOrAdd(pattern, p =>
        {
            var regexPattern = "^" + Regex.Escape(p)
                .Replace("\\*", ".*")
                .Replace("\\?", ".") + "$";
            return new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);
        });
        return regex.IsMatch(text);
    }

    /// <summary>
    /// Checks whether a full type name matches a single filter pattern (glob or exact).
    /// For globs, tries both the full name and simple name.
    /// For non-globs, delegates to <see cref="Matches"/> which handles namespace prefix and generic arity.
    /// </summary>
    public static bool MatchesTypeFilter(string fullName, string pattern)
    {
        if (pattern.Contains('*') || pattern.Contains('?'))
            return MatchesGlob(fullName, pattern) || MatchesGlob(GetSimpleName(fullName), pattern);
        return Matches(fullName, pattern);
    }

    /// <summary>
    /// Checks whether a full type name matches any filter in the set.
    /// </summary>
    public static bool MatchesAnyTypeFilter(string fullName, IEnumerable<string> patterns)
    {
        foreach (var pattern in patterns)
            if (MatchesTypeFilter(fullName, pattern))
                return true;
        return false;
    }

    /// <summary>
    /// Checks whether a member name matches any filter in the set.
    /// Supports exact (case-insensitive) and glob patterns.
    /// </summary>
    public static bool MatchesMemberFilter(string name, HashSet<string> filter)
    {
        foreach (var pattern in filter)
        {
            if (pattern.Contains('*') || pattern.Contains('?'))
            {
                if (MatchesGlob(name, pattern))
                    return true;
            }
            else
            {
                if (string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Cascading type name lookup: exact → glob → fuzzy.
    /// Returns a single match when unambiguous, or suggestions when not found / multiple glob hits.
    /// </summary>
    public static LookupResult Lookup(
        IEnumerable<string> candidates, string pattern, int maxSuggestions = 6)
    {
        var list = candidates as IList<string> ?? candidates.ToList();

        bool isGlob = pattern.Contains('*') || pattern.Contains('?');

        if (isGlob)
        {
            var hits = list.Where(c =>
                MatchesGlob(c, pattern) || MatchesGlob(GetSimpleName(c), pattern)).ToList();

            return hits.Count switch
            {
                1 => new LookupResult(hits[0], []),
                > 1 => new LookupResult(null, hits.Take(maxSuggestions).ToList()),
                _ => new LookupResult(null, [])
            };
        }

        // Exact match via Matches (handles namespace prefix, generic arity, case-insensitive)
        // When pattern has generic notation (e.g., Option<T>), prefer matching arity
        var patternArity = GetPatternArity(pattern);
        var matches = list.Where(c => Matches(c, pattern)).ToList();

        if (matches.Count > 0)
        {
            // If pattern specified arity (e.g., Option<T>), prefer candidate with matching arity
            if (patternArity >= 0)
            {
                var arityMatch = matches.FirstOrDefault(c => GetGenericArity(c) == patternArity);
                if (arityMatch != null)
                    return new LookupResult(arityMatch, []);
            }

            var normalizedPattern = Normalize(pattern);
            var exactSimpleNameMatch = matches.FirstOrDefault(c =>
                GetGenericArity(GetSimpleName(c)) == 0
                && GetSimpleName(c).Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase));
            if (exactSimpleNameMatch != null)
                return new LookupResult(exactSimpleNameMatch, []);

            var exactFullNameMatch = matches.FirstOrDefault(c =>
                GetGenericArity(c) == 0
                && c.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase));
            if (exactFullNameMatch != null)
                return new LookupResult(exactFullNameMatch, []);

            // Otherwise return first match (existing behavior for non-generic patterns)
            return new LookupResult(matches[0], []);
        }

        // Fuzzy fallback
        var suggestions = FindClosest(list, pattern, maxResults: maxSuggestions)
            .Select(s => s.Name)
            .ToList();
        return new LookupResult(null, suggestions);
    }

    /// <summary>
    /// Member name lookup with suggestions. Filters by exact/glob, then falls back to
    /// prefix + fuzzy suggestions when nothing matches.
    /// </summary>
    public static MemberLookupResult LookupMembers(
        IEnumerable<string> memberNames, IEnumerable<string> filters, int maxSuggestions = 6)
    {
        var names = memberNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var filterList = filters as IList<string> ?? filters.ToList();

        // Check each name against all filters
        var matched = names.Where(name =>
            filterList.Any(f =>
                (f.Contains('*') || f.Contains('?'))
                    ? MatchesGlob(name, f)
                    : string.Equals(name, f, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matched.Count > 0)
            return new MemberLookupResult(matched, []);

        // Build suggestions from non-glob filters
        var suggestionSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in filterList)
        {
            if (f.Contains('*') || f.Contains('?'))
                continue;

            // Prefix matches (e.g. "Deseri" → "Deserialize", "DeserializeAsync")
            foreach (var n in names)
                if (n.StartsWith(f, StringComparison.OrdinalIgnoreCase))
                    suggestionSet.Add(n);

            // Fuzzy matches
            foreach (var (name, _) in FindClosest(names, f, minSimilarity: 0.5, maxResults: maxSuggestions))
                suggestionSet.Add(name);
        }

        // Rank all suggestions by similarity to the first non-glob filter
        var rankTarget = filterList.FirstOrDefault(f => !f.Contains('*') && !f.Contains('?')) ?? "";
        var ranked = suggestionSet
            .OrderByDescending(s => StringDistance.Similarity(s, rankTarget))
            .Take(maxSuggestions)
            .ToList();
        return new MemberLookupResult([], ranked);
    }
}
