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

    /// <summary>
    /// Writes unresolved select values to stderr.
    /// Returns true only for total failure (nothing matched — command should exit).
    /// Partial matches (some resolved, some not) write warnings and return false.
    /// </summary>
    public static bool WriteUnresolved(SelectResult result)
    {
        if (result.Unresolved.Count == 0) return false;

        bool totalFailure = result.Sections is null or { Count: 0 };
        string prefix = totalFailure ? "Error" : "Warning";

        foreach (var miss in result.Unresolved)
        {
            if (miss.IsGlob)
            {
                Console.Error.WriteLine($"{prefix}: No sections match '{miss.Value}'.");
                if (totalFailure && miss.Suggestions.Count > 0)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Available sections:");
                    foreach (var s in miss.Suggestions)
                        Console.Error.WriteLine($"  {s}");
                }
            }
            else
            {
                Console.Error.WriteLine($"{prefix}: Select value '{miss.Value}' not found.");
                if (miss.Suggestions.Count > 0)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Did you mean:");
                    foreach (var s in miss.Suggestions)
                        Console.Error.WriteLine($"  {s}");
                }
            }
        }

        return totalFailure;
    }
}
