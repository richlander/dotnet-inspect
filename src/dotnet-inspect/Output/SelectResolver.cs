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
