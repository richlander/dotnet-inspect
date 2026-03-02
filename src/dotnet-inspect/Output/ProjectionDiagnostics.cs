using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Provides pre-render validation and post-render diagnostics for field/column projections.
/// Pre-render: catches typos by validating names against the schema.
/// Post-render: detects valid names that produced no data.
/// </summary>
public static class ProjectionDiagnostics
{
    /// <summary>
    /// Validates --fields/--columns names against the section schema.
    /// Writes warnings to stderr for unrecognized names with prefix suggestions.
    /// Returns true if all names are valid, false if any are unknown.
    /// </summary>
    public static bool ValidateProjection(DocumentSchema schema, string? sectionName,
        string[]? fields, string[]? columns)
    {
        if (string.IsNullOrEmpty(sectionName))
            return true;

        bool allValid = true;

        if (fields is { Length: > 0 })
            allValid &= ValidateNames(schema, sectionName, fields, "field");

        if (columns is { Length: > 0 })
            allValid &= ValidateNames(schema, sectionName, columns, "column");

        return allValid;
    }

    /// <summary>
    /// Compares requested field/column names against rendered output.
    /// Writes a note to stderr for valid names that produced no data.
    /// </summary>
    public static void DiagnoseRendered(string[]? requestedNames, string renderedOutput)
    {
        var missing = DocumentSchema.DiagnoseRendered(requestedNames, renderedOutput);
        if (missing.Length == 0)
            return;

        var label = missing.Length == 1 ? "field has" : "fields have";
        Console.Error.WriteLine($"note: {missing.Length} {label} no data: {string.Join(", ", missing)}");
    }

    private static bool ValidateNames(DocumentSchema schema, string sectionName,
        string[] names, string kind)
    {
        var validation = schema.ValidateProjection(sectionName, names);
        if (validation.IsValid)
            return true;

        foreach (var name in validation.Unresolved)
        {
            var msg = $"warning: {kind} '{name}' not found in section '{sectionName}'";
            if (validation.Suggestions.TryGetValue(name, out var suggestions))
                msg += $" (did you mean: {string.Join(", ", suggestions)}?)";
            Console.Error.WriteLine(msg);
        }

        return false;
    }
}
