namespace DotnetInspector.Output;

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
    /// Returns a HashSet of matched section names (case-insensitive).
    /// Supports wildcard patterns (e.g., "Lib*", "Source*").
    /// Unmatched values are silently ignored (they may be field/column names for projection).
    /// </summary>
    public static HashSet<string>? ResolveSelectAsSections(string[]? select, string[] knownSections)
    {
        if (select is not { Length: > 0 })
            return null;

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in select)
        {
            if (value.Contains('*') || value.Contains('?'))
            {
                // Wildcard match against known sections
                foreach (var section in knownSections)
                {
                    if (WildcardMatch(section, value))
                        matched.Add(section);
                }
            }
            else
            {
                // Exact match (case-insensitive)
                var match = knownSections.FirstOrDefault(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                {
                    matched.Add(match);
                    continue;
                }

                // Prefix match (case-insensitive): "sym" → "Symbols"
                var prefixMatches = knownSections.Where(s =>
                    s.StartsWith(value, StringComparison.OrdinalIgnoreCase)).ToList();
                if (prefixMatches.Count > 0)
                {
                    foreach (var pm in prefixMatches)
                        matched.Add(pm);
                    WriteMatchHint(value, prefixMatches);
                    continue;
                }

                // Fuzzy match: find the closest section name
                var valueLower = value.ToLowerInvariant();
                var best = knownSections
                    .Select(s => (Section: s, Score: DotnetInspector.Metadata.StringDistance.Similarity(
                        valueLower, s.ToLowerInvariant())))
                    .Where(x => x.Score >= 0.6)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();
                if (best.Section != null)
                {
                    matched.Add(best.Section);
                    WriteMatchHint(value, [best.Section]);
                }
            }
        }

        return matched.Count > 0 ? matched : null;
    }

    private static void WriteMatchHint(string input, List<string> resolved)
    {
        if (resolved.Count == 1)
            Console.Error.WriteLine($"Matched '{input}' -> {resolved[0]}");
        else
            Console.Error.WriteLine($"Matched '{input}' -> {string.Join(", ", resolved)}");
    }

    /// <summary>
    /// Simple case-insensitive wildcard match supporting * and ?.
    /// </summary>
    private static bool WildcardMatch(string input, string pattern)
    {
        int i = 0, p = 0, starI = -1, starP = -1;
        var inputLower = input.ToLowerInvariant();
        var patternLower = pattern.ToLowerInvariant();

        while (i < inputLower.Length)
        {
            if (p < patternLower.Length && (patternLower[p] == '?' || patternLower[p] == inputLower[i]))
            {
                i++;
                p++;
            }
            else if (p < patternLower.Length && patternLower[p] == '*')
            {
                starI = i;
                starP = p++;
            }
            else if (starP >= 0)
            {
                i = ++starI;
                p = starP + 1;
            }
            else
            {
                return false;
            }
        }

        while (p < patternLower.Length && patternLower[p] == '*')
            p++;

        return p == patternLower.Length;
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
