namespace DotnetInspector.Output;

/// <summary>
/// CLI-layer helpers for printing SelectResolver results to Console.
/// Keeps Console usage in the CLI layer, not in the service.
/// </summary>
public static class SelectOutput
{
    /// <summary>
    /// Prints discovery entries to Console.Out.
    /// </summary>
    public static void WriteDiscovery(IEnumerable<(string Name, string Kind)> entries)
    {
        foreach (var line in SelectResolver.FormatDiscoveryLines(entries))
            Console.WriteLine(line);
    }

    /// <summary>
    /// Prints unresolved select values with "Did you mean:" suggestions to Console.Error.
    /// Returns true if any errors were printed.
    /// </summary>
    public static bool WriteErrors(IReadOnlyList<SelectMiss> unresolved)
    {
        if (unresolved.Count == 0) return false;

        foreach (var miss in unresolved)
        {
            Console.Error.WriteLine($"Error: Select value '{miss.Value}' not found.");
            if (miss.Suggestions.Count > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Did you mean:");
                foreach (var s in miss.Suggestions)
                    Console.Error.WriteLine($"  {s}");
            }
        }
        return true;
    }
}
