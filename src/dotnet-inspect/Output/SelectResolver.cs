namespace DotnetInspector.Output;

using DotnetInspector.Metadata;
using Markout;

/// <summary>
/// An unresolved -S/--select value with suggestions for what the user may have meant.
/// </summary>
public record SelectMiss(string Value, IReadOnlyList<string> Suggestions);

/// <summary>
/// Result of resolving -S/--select values against known section names.
/// </summary>
public record SelectResult(HashSet<string>? Sections, IReadOnlyList<SelectMiss> Unresolved)
{
    public bool HasError => Unresolved.Count > 0;
}

/// <summary>
/// Handles discovery and projection for --select, --columns, and --fields.
/// Discovery mode: any bare flag lists available names.
/// Execute mode: all flags have values, used for filtering.
/// All methods return data — no Console usage.
/// </summary>
public static class SelectResolver
{
    private const int DiscoveryPadding = 24;

    /// <summary>
    /// Returns true if any projection flag is in discovery mode (bare, no value).
    /// </summary>
    public static bool IsDiscovery(string[]? select, string[]? columns, string[]? fields = null)
        => select is { Length: 0 } || columns is { Length: 0 } || fields is { Length: 0 };

    /// <summary>
    /// Returns discovery entries for the given schema and section names.
    /// -S lists sections. --columns lists columns. --fields lists fields.
    /// </summary>
    public static List<(string Name, string Kind)> GetDiscoveryEntries(string[]? select, string[]? columns, string[]? fields,
        string[]? sectionNames, params MarkoutSchemaInfo?[] schemas)
    {
        // Bare --select: list sections only
        if (select is { Length: 0 })
        {
            var entries = new List<(string, string)>();
            if (sectionNames != null)
                entries.AddRange(sectionNames.Select(n => (n, "section")));
            return entries;
        }

        // Bare --columns: list columns from schema
        if (columns is { Length: 0 })
        {
            var entries = new List<(string, string)>();
            foreach (var schema in schemas)
            {
                if (schema == null) continue;
                foreach (var n in schema.GetColumnNames())
                    entries.Add((n, "column"));
            }
            return entries;
        }

        // Bare --fields: list fields from schema
        if (fields is { Length: 0 })
        {
            var entries = new List<(string, string)>();
            foreach (var schema in schemas)
            {
                if (schema == null) continue;
                entries.AddRange(schema.GetFieldNames().Select(n => (n, "field")));
            }
            return entries;
        }

        return [];
    }

    /// <summary>
    /// Formats discovery entries as padded lines for display.
    /// </summary>
    public static IEnumerable<string> FormatDiscoveryLines(IEnumerable<(string Name, string Kind)> entries)
    {
        var items = entries.ToList();
        if (items.Count == 0) yield break;
        var padding = Math.Max(DiscoveryPadding, items.Max(e => e.Name.Length) + 2);

        foreach (var (name, kind) in items)
            yield return $"{name.PadRight(padding)} {kind}";
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
            if (value.Contains('*') || value.Contains('?'))
            {
                foreach (var section in knownSections)
                {
                    if (TypeMatcher.MatchesGlob(section, value))
                        matched.Add(section);
                }
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
