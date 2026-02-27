namespace DotnetInspector.Output;

using DotnetInspector.Metadata;
using Markout;

/// <summary>
/// Handles discovery and projection for --select and --columns.
/// Discovery mode: any bare flag lists available names.
/// Execute mode: all flags have values, used for filtering.
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
    /// Runs discovery for the given schema and section names.
    /// Scoped: if --select has a value, discovery is scoped to that section's schema.
    /// </summary>
    public static void Discover(string[]? select, string[]? columns, string[]? fields,
        string[]? sectionNames, params MarkoutSchemaInfo?[] schemas)
    {
        // Bare --select: list sections + root fields
        if (select is { Length: 0 })
        {
            var entries = new List<(string, string)>();
            if (sectionNames != null)
                entries.AddRange(sectionNames.Select(n => (n, "section")));
            foreach (var schema in schemas)
            {
                if (schema == null) continue;
                entries.AddRange(schema.GetFieldNames().Select(n => (n, "field")));
            }
            WriteDiscoveryLines(entries);
            return;
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
            WriteDiscoveryLines(entries);
            return;
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
            WriteDiscoveryLines(entries);
            return;
        }
    }

    /// <summary>
    /// Resolves -S/--select values as section names for backpressure.
    /// Matching: exact (case-insensitive) or glob (* / ?). No prefix or fuzzy guessing.
    /// Values that exactly match a known field or column are silently passed through for projection.
    /// Unmatched values produce "Did you mean:" errors with suggestions.
    /// </summary>
    public static HashSet<string>? ResolveSelectAsSections(
        string[]? select, string[] knownSections, out bool hasError,
        params MarkoutSchemaInfo?[] schemas)
    {
        hasError = false;
        if (select is not { Length: > 0 })
            return null;

        var knownFields = schemas.Where(s => s != null)
            .SelectMany(s => s!.GetFieldNames())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var knownColumns = schemas.Where(s => s != null)
            .SelectMany(s => s!.GetColumnNames())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unresolved = new List<string>();

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
                // Exact match (case-insensitive) against sections
                var match = knownSections.FirstOrDefault(s =>
                    s.Equals(value, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    matched.Add(match);
                    continue;
                }

                // Known field or column → pass through for projection
                if (IsKnownName(value, knownFields) || IsKnownName(value, knownColumns))
                    continue;

                unresolved.Add(value);
            }
        }

        if (unresolved.Count > 0)
        {
            var allNames = knownSections
                .Concat(knownFields).Concat(knownColumns)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            foreach (var value in unresolved)
            {
                Console.Error.WriteLine($"Error: Select value '{value}' not found.");
                var suggestions = GetSuggestions(value, allNames);
                if (suggestions.Count > 0)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Did you mean:");
                    foreach (var s in suggestions)
                        Console.Error.WriteLine($"  {s}");
                }
            }
            hasError = true;
        }

        return matched.Count > 0 ? matched : null;
    }

    private static bool IsKnownName(string value, string[] names)
        => names.Any(n => n.Equals(value, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Generates suggestions using prefix + fuzzy matching, ranked by similarity.
    /// Same strategy as TypeMatcher.LookupMembers.
    /// </summary>
    private static List<string> GetSuggestions(string value, string[] allNames, int maxResults = 6)
    {
        var suggestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Prefix matches
        foreach (var name in allNames)
            if (name.StartsWith(value, StringComparison.OrdinalIgnoreCase))
                suggestions.Add(name);

        // Fuzzy matches
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

    /// <summary>
    /// Writes discovery lines (name + kind) with consistent padding.
    /// </summary>
    public static void WriteDiscoveryLines(IEnumerable<(string Name, string Kind)> entries)
    {
        var items = entries.ToList();
        var padding = Math.Max(DiscoveryPadding, items.Max(e => e.Name.Length) + 2);

        foreach (var (name, kind) in items)
            Console.WriteLine($"{name.PadRight(padding)} {kind}");
    }
}
