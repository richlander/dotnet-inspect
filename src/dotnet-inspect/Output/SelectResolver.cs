namespace DotnetInspector.Output;

using System.Diagnostics;
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
    /// Resolves --select values as section names for backpressure.
    /// Returns a HashSet of matched section names (case-insensitive).
    /// Unmatched values are silently ignored (they may be field/column names for projection).
    /// </summary>
    public static HashSet<string>? ResolveSelectAsSections(string[]? select, string[] knownSections)
    {
        if (select is not { Length: > 0 })
            return null;

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in select)
        {
            // Section names win: if a --select value matches a known section, it's a section filter
            var match = knownSections.FirstOrDefault(s => s.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (match != null)
                matched.Add(match);
        }

        return matched.Count > 0 ? matched : null;
    }

    /// <summary>
    /// Writes discovery lines (name + kind) with consistent padding.
    /// Debug-asserts if any name overflows into the kind column.
    /// </summary>
    public static void WriteDiscoveryLines(IEnumerable<(string Name, string Kind)> entries)
    {
        var items = entries.ToList();
        var overflow = items.Where(e => e.Name.Length >= DiscoveryPadding).ToList();
        Debug.Assert(overflow.Count == 0,
            $"Discovery name(s) overflow {DiscoveryPadding}-char column: {string.Join(", ", overflow.Select(e => $"'{e.Name}' ({e.Name.Length})"))}");

        foreach (var (name, kind) in items)
            Console.WriteLine($"{name,-24} {kind}");
    }
}
