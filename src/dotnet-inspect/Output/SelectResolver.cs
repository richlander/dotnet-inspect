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
            if (value.Contains('*') || value.Contains('?'))
            {
                bool anyGlobMatch = false;
                foreach (var section in knownSections)
                {
                    if (TypeMatcher.MatchesGlob(section, value))
                    {
                        matched.Add(section);
                        anyGlobMatch = true;
                    }
                }
                if (!anyGlobMatch)
                    unresolved.Add(new SelectMiss(value, knownSections.ToList(), IsGlob: true));
            }
            else
            {
                var match = knownSections.FirstOrDefault(s =>
                    s.Equals(value, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    matched.Add(match);
                    continue;
                }

                unresolved.Add(new SelectMiss(value, GetSuggestions(value, knownSections)));
            }
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
