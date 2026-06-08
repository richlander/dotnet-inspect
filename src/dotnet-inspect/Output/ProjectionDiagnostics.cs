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
    /// Writes warnings to stderr for partially unrecognized names with prefix suggestions.
    /// Returns false when a projection has no matches and the caller should stop before rendering.
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
    /// Validates --fields/--columns names against multiple selected sections.
    /// Returns false when any section has a projection with no matches.
    /// </summary>
    public static bool ValidateProjection(DocumentSchema schema, IReadOnlyCollection<string>? sectionNames,
        string[]? fields, string[]? columns)
    {
        if ((fields is not { Length: > 0 } && columns is not { Length: > 0 })
            || sectionNames is not { Count: > 0 })
        {
            return true;
        }

        var allValid = true;
        foreach (var section in sectionNames)
            allValid &= ValidateProjection(schema, section, fields, columns);

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

        if (validation.Resolved.Length > 0)
            return true;

        Console.Error.WriteLine($"Error: No {kind}s matched projection: {string.Join(", ", names)}");
        return false;
    }
}
