using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CSharpText;
using ILInspector.MetadataPrimitives;

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

        // Normalize both for comparison. Metadata APIs and analysis rows render nested types
        // with '.', while users often paste CLR-style '+'. Treat those separators as
        // equivalent for lookup, without changing Normalize's public formatting contract.
        var normalizedCandidate = NormalizeForLookup(candidate);
        var normalizedTarget = NormalizeForLookup(target);
        return MatchesNormalized(normalizedCandidate, normalizedTarget);
    }

    /// <summary>
    /// Matches lookup-normalized type names without repeating normalization in
    /// candidate-scanning loops.
    /// </summary>
    public static bool MatchesNormalized(
        string normalizedCandidate,
        string normalizedTarget)
    {
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

    private static string NormalizeForLookup(string typeName)
        => FqnParser.NormalizeTypeName(typeName).Replace('+', '.');

    /// <summary>
    /// Gets the base name without generic arity suffixes.
    /// "List`1" → "List"; "Dictionary`2.KeyCollection" → "Dictionary.KeyCollection".
    /// </summary>
    /// <remarks>
    /// Only a canonical <c>`N</c> suffix is an arity suffix
    /// (<see cref="MetadataNameArity"/>), so a search key keeps a backtick that
    /// is name text: <c>Ns.Widget`1Extra</c> stays itself instead of becoming
    /// <c>Ns.WidgetExtra</c> and matching an unrelated type. This is search and
    /// display text whose nesting spelling is already flattened, not an identity
    /// contract.
    /// </remarks>
    public static string GetBaseName(string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        return MetadataNameArity.StripFromFlattenedName(typeName);
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
    /// Extracts a display-friendly attribute name by stripping a trailing
    /// "Attribute" suffix and the namespace.
    /// "System.ObsoleteAttribute" → "Obsolete"
    /// </summary>
    public static string GetShortAttributeName(string fullName)
    {
        var name = fullName.EndsWith("Attribute", StringComparison.Ordinal)
            ? fullName[..^9]
            : fullName;
        return GetSimpleName(name);
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

        var normalizedTarget = NormalizeForLookup(target);
        var targetBase = GetBaseName(GetSimpleName(normalizedTarget));

        List<(string Name, double Similarity)> scored = [];

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrEmpty(candidate))
                continue;

            // Exact match is handled by Matches — skip here
            var normalizedCandidate = NormalizeForLookup(candidate);
            if (MatchesNormalized(normalizedCandidate, normalizedTarget))
                continue;

            var candidateBase =
                GetBaseName(GetSimpleName(normalizedCandidate));
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
    /// <remarks>
    /// The arity is the one declared by the innermost component that carries a
    /// canonical <c>`N</c> suffix, so a nested spelling
    /// (<c>Dictionary`2.KeyCollection</c>) answers 2 and both sides of a match
    /// answer alike. Only <see cref="MetadataNameArity"/>'s canonical form
    /// counts, so <c>Widget`1Extra</c>, <c>Widget`0</c>, <c>Widget`+1</c>, and an
    /// out-of-range count are 0 rather than a parsed number.
    /// </remarks>
    public static int GetGenericArity(string typeName)
    {
        ArgumentNullException.ThrowIfNull(typeName);
        int arity = 0;
        foreach (MetadataNameComponent component in MetadataNameArity.EnumerateComponents(typeName))
        {
            if (component.Arity > 0)
                arity = component.Arity;
        }

        return arity;
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
        var arity = FqnParser.CountTypeParameters(pattern.AsSpan((startIdx + 1)..endIdx));
        return arity == 0 ? -1 : arity;
    }

    /// <summary>
    /// Returns true when the user supplied generic syntax, including malformed
    /// or zero-arity syntax that must not be treated as an unspecified arity.
    /// </summary>
    public static bool HasExplicitGenericNotation(string pattern) =>
        pattern.Contains('`') || pattern.Contains('<');

    /// <summary>
    /// Returns true when wildcard syntax remains after valid generic arguments
    /// are normalized away.
    /// </summary>
    public static bool IsTypeGlobPattern(string pattern)
    {
        var matchPattern = GetTypeMatchPattern(pattern);
        return matchPattern.Contains('*') || matchPattern.Contains('?');
    }

    private static string GetTypeMatchPattern(string pattern) =>
        HasExplicitGenericNotation(pattern)
            ? FqnParser.NormalizeTypeName(pattern)
            : pattern;

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
    /// Explicit generic notation preserves exact arity; other non-globs use
    /// <see cref="Matches"/> for namespace and base-name matching.
    /// </summary>
    public static bool MatchesTypeFilter(string fullName, string pattern)
    {
        var matchPattern = GetTypeMatchPattern(pattern);
        if (IsTypeGlobPattern(pattern))
            return MatchesGlob(fullName, matchPattern)
                || MatchesGlob(GetSimpleName(fullName), matchPattern);
        if (HasExplicitGenericNotation(pattern))
        {
            var normalizedFullName = NormalizeForLookup(fullName);
            var normalizedPattern = NormalizeForLookup(pattern);
            return normalizedFullName.Equals(
                       normalizedPattern,
                       StringComparison.OrdinalIgnoreCase)
                   || EndsWithDottedSuffix(
                       normalizedFullName,
                       normalizedPattern);
        }
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
                if (MatchesMemberName(name, pattern))
                    return true;
            }
        }
        return false;
    }

    public static bool MatchesMemberName(string name, string pattern)
    {
        if (pattern.Equals("this[]", StringComparison.OrdinalIgnoreCase))
            return name.Equals("Item", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Chars", StringComparison.OrdinalIgnoreCase);

        return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Cascading type name lookup: exact → glob → fuzzy.
    /// Returns a single match when unambiguous, or suggestions when not found / multiple glob hits.
    /// </summary>
    public static LookupResult Lookup(
        IEnumerable<string> candidates, string pattern, int maxSuggestions = 6)
    {
        var list = candidates as IList<string> ?? candidates.ToList();

        var matchPattern = GetTypeMatchPattern(pattern);
        bool isGlob = IsTypeGlobPattern(pattern);

        if (isGlob)
        {
            var hits = list.Where(c =>
                MatchesGlob(c, matchPattern)
                || MatchesGlob(GetSimpleName(c), matchPattern)).ToList();

            return hits.Count switch
            {
                1 => new LookupResult(hits[0], []),
                > 1 => new LookupResult(null, hits.Take(maxSuggestions).ToList()),
                _ => new LookupResult(null, [])
            };
        }

        var normalizedPattern = NormalizeForLookup(pattern);
        var matches = list
            .Select(candidate => (
                Candidate: candidate,
                Normalized: NormalizeForLookup(candidate)))
            .Where(candidate =>
                MatchesNormalized(
                    candidate.Normalized,
                    normalizedPattern))
            .ToList();

        if (matches.Count > 0)
        {
            // Preserve every nested segment's arity before falling back to the legacy
            // single-arity preference below. Otherwise Outer`1.Inner`2 and
            // Outer`1.Inner`3 both compare only as Outer`1 and the first sibling wins.
            var exactNormalizedMatch = matches.FirstOrDefault(candidate =>
                candidate.Normalized.Equals(
                           normalizedPattern,
                           StringComparison.OrdinalIgnoreCase)
                       || EndsWithDottedSuffix(
                           candidate.Normalized,
                           normalizedPattern));
            if (exactNormalizedMatch.Candidate != null)
                return new LookupResult(
                    exactNormalizedMatch.Candidate,
                    []);

            // Explicit generic notation is exact identity evidence, including
            // every nested segment's arity. Never broaden it to a base-name hit.
            if (HasExplicitGenericNotation(pattern))
                return new LookupResult(
                    null,
                    matches
                        .Take(maxSuggestions)
                        .Select(candidate => candidate.Candidate)
                        .ToList());

            var exactSimpleNameMatch = matches.FirstOrDefault(c =>
                GetGenericArity(GetSimpleName(c.Candidate)) == 0
                && GetSimpleName(c.Candidate).Equals(
                    normalizedPattern,
                    StringComparison.OrdinalIgnoreCase));
            if (exactSimpleNameMatch.Candidate != null)
                return new LookupResult(
                    exactSimpleNameMatch.Candidate,
                    []);

            var exactFullNameMatch = matches.FirstOrDefault(c =>
                GetGenericArity(c.Candidate) == 0
                && c.Candidate.Equals(
                    normalizedPattern,
                    StringComparison.OrdinalIgnoreCase));
            if (exactFullNameMatch.Candidate != null)
                return new LookupResult(
                    exactFullNameMatch.Candidate,
                    []);

            // Otherwise return first match (existing behavior for non-generic patterns)
            return new LookupResult(matches[0].Candidate, []);
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
                    : MatchesMemberName(name, f)))
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
