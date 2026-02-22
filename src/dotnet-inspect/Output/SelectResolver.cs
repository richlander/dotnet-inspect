namespace DotnetInspector.Output;

using System.Diagnostics;

/// <summary>
/// Categorizes -S select values into sections, fields, and columns.
/// Section names win when a name matches both a section and a field.
/// </summary>
public static class SelectResolver
{
    private const int DiscoveryPadding = 24;

    /// <summary>
    /// Result of resolving select values against known names.
    /// </summary>
    public record SelectResult(
        HashSet<string>? Sections,
        string[]? FieldsAndColumns,
        string[] Unmatched);

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

    /// <summary>
    /// Categorizes select values into sections vs fields/columns.
    /// When preferFields is false (default), section names win when ambiguous.
    /// When preferFields is true (-F flag), all names are treated as fields/columns.
    /// </summary>
    public static SelectResult Resolve(
        string[]? select,
        string[] knownSections,
        bool preferFields = false)
    {
        if (select == null || select.Length == 0)
            return new(null, null, []);

        var sectionSet = new HashSet<string>(knownSections, StringComparer.OrdinalIgnoreCase);
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fieldsAndColumns = new List<string>();
        var unmatched = new List<string>();

        foreach (var name in select)
        {
            if (!preferFields && sectionSet.Contains(name))
            {
                // Resolve to original casing
                var original = knownSections.First(s => s.Equals(name, StringComparison.OrdinalIgnoreCase));
                sections.Add(original);
            }
            else
            {
                fieldsAndColumns.Add(name);
            }
        }

        return new(
            sections.Count > 0 ? sections : null,
            fieldsAndColumns.Count > 0 ? fieldsAndColumns.ToArray() : null,
            unmatched.ToArray());
    }
}
