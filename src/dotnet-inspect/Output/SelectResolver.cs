namespace DotnetInspector.Output;

using DotnetInspector.Metadata;

/// <summary>
/// An unresolved -S/--select value with suggestions for what the user may have meant.
/// </summary>
public record SelectMiss(string Value, IReadOnlyList<string> Suggestions, bool IsGlob = false);

/// <summary>
/// Result of resolving -S/--select values against known section names.
/// </summary>
public record SelectResult(HashSet<string>? Sections, IReadOnlyList<SelectMiss> Unresolved)
{
    public bool HasError => Unresolved.Count > 0;
}

/// <summary>
/// Handles selection for --select, --columns, and --fields.
/// All methods return data — no Console usage.
/// </summary>
public static class SelectResolver
{
    public const string AllSelector = "All";

    /// <summary>
    /// Resolves a single name against known sections: exact (case-insensitive), then glob.
    /// When <paramref name="singleGlob"/> is true, a glob must match exactly one section
    /// (discover semantics); otherwise all glob matches are returned.
    /// </summary>
    public static (List<string> Matches, SelectMiss? Miss) ResolveSingle(
        string name, string[] knownSections, bool singleGlob = false)
    {
        // Exact match (case-insensitive)
        var exact = knownSections.FirstOrDefault(s =>
            s.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return ([exact], null);

        // Glob match
        if (name.Contains('*') || name.Contains('?'))
        {
            var globMatches = knownSections
                .Where(s => TypeMatcher.MatchesGlob(s, name))
                .ToList();

            if (globMatches.Count > 0)
            {
                if (singleGlob && globMatches.Count > 1)
                    return (globMatches, new SelectMiss(name, globMatches, IsGlob: true));

                return (globMatches, null);
            }

            return ([], new SelectMiss(name, knownSections.ToList(), IsGlob: true));
        }

        // No match
        return ([], new SelectMiss(name, GetSuggestions(name, knownSections)));
    }

    /// <summary>
    /// Resolves -S/--select values as section names for backpressure.
    /// Matching: exact (case-insensitive) or glob (* / ?). No prefix or fuzzy guessing.
    /// Returns matched sections and any unresolved values with suggestions.
    /// </summary>
    public static SelectResult ResolveSelectAsSections(string[]? select, string[] knownSections)
    {
        if (select is not { Length: > 0 })
            return new(null, []);

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<SelectMiss>();

        foreach (var value in select)
        {
            if (value.Equals(AllSelector, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var section in knownSections)
                    matched.Add(section);
                continue;
            }

            var (matches, miss) = ResolveSingle(value, knownSections);
            foreach (var m in matches)
                matched.Add(m);
            if (miss != null)
                unresolved.Add(miss);
        }

        return new(matched.Count > 0 ? matched : null, unresolved);
    }

    /// <summary>
    /// Generates suggestions using prefix + fuzzy matching, ranked by similarity.
    /// Same strategy as TypeMatcher.LookupMembers.
    /// </summary>
    private static List<string> GetSuggestions(string value, string[] allNames, int maxResults = 6)
    {
        var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in allNames)
            if (name.StartsWith(value, StringComparison.OrdinalIgnoreCase))
                suggestions.Add(name);

        var valueLower = value.ToLowerInvariant();
        foreach (var name in allNames)
        {
            var score = StringDistance.Similarity(valueLower, name.ToLowerInvariant());
            if (score >= 0.5)
                suggestions.Add(name);
        }

        return suggestions
            .OrderByDescending(s => StringDistance.Similarity(
                value.ToLowerInvariant(), s.ToLowerInvariant()))
            .Take(maxResults)
            .ToList();
    }
}
