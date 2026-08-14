using System.Text.Json;
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
    /// A name is valid when it resolves in <em>at least one</em> selected section; sections
    /// that lack it simply don't project it. This matters when a section is implicitly added
    /// alongside an explicit one — for example a scope flag (<c>--bin</c>/<c>--project</c>/
    /// <c>--caller-package</c>) implies <c>-S Callers</c>, so projecting graph-only fields
    /// such as <c>Fanin</c>/<c>Depth</c> over <c>-S "Call Graph"</c> must not fail just
    /// because those fields don't exist on the companion <c>Callers</c> table. Returns false
    /// only when a projection matches no selected section at all.
    /// </summary>
    public static bool ValidateProjection(DocumentSchema schema, IReadOnlyCollection<string>? sectionNames,
        string[]? fields, string[]? columns, IReadOnlySet<string>? fieldLayoutSections = null)
    {
        if ((fields is not { Length: > 0 } && columns is not { Length: > 0 })
            || sectionNames is not { Count: > 0 })
        {
            return true;
        }

        var ok = true;
        if (fields is { Length: > 0 })
            ok &= ValidateNamesAcrossSections(schema, sectionNames, fields, "field");
        if (columns is { Length: > 0 })
            ok &= ValidateNamesAcrossSections(
                schema, sectionNames, columns, "column", fieldLayoutSections);

        return ok;
    }

    private static bool ValidateNamesAcrossSections(DocumentSchema schema,
        IReadOnlyCollection<string> sectionNames,
        string[] names,
        string kind,
        IReadOnlySet<string>? fieldLayoutSections = null)
    {
        // A name is an error only when it resolves in NO selected section. Names that
        // resolve in any section drop out, so a valid graph field is not reported against a
        // companion table that happens to lack it (e.g. the Callers table implied by --bin).
        var resolvedSomewhere = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sectionNames)
        {
            var unresolved = new HashSet<string>(
                schema.ValidateProjection(section, names).Unresolved, StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
                if (!unresolved.Contains(name))
                    resolvedSomewhere.Add(name);

            if (string.Equals(kind, "column", StringComparison.Ordinal)
                && fieldLayoutSections?.Contains(section) == true)
            {
                foreach (var name in MatchedRequests(names, ["Field", "Value"]))
                    resolvedSomewhere.Add(name);
            }
        }

        // Warn (with the per-section discovery hint) only for names missing everywhere.
        foreach (var section in sectionNames)
        {
            var validation = schema.ValidateProjection(section, names);
            foreach (var name in validation.Unresolved)
            {
                if (resolvedSomewhere.Contains(name))
                    continue;
                var msg = $"{kind} '{name}' not found in section '{section}'";
                if (validation.Suggestions.TryGetValue(name, out var suggestions))
                    msg += $" (did you mean: {string.Join(", ", suggestions)}?)";
                msg += $" Run -D \"{section}\" to list available {kind}s.";
                CommandError.WriteWarning(msg);
            }
        }

        // Proceed when at least one requested name matched some section (mirrors the
        // single-section contract of rendering partial matches); abort only when none did.
        if (resolvedSomewhere.Count > 0)
            return true;

        CommandError.Write($"No {kind}s matched projection: {string.Join(", ", names)}");
        return false;
    }

    /// <summary>
    /// Compares requested field/column names against rendered output.
    /// Writes a note to stderr for valid names that produced no data.
    /// </summary>
    public static void DiagnoseRendered(string[]? requestedNames, string renderedOutput)
        => DiagnoseRendered(requestedNames, renderedOutput, "field");

    internal static void DiagnoseProjectedJson(
        string[]? fields,
        string[]? columns,
        IReadOnlyList<string> emittedFields,
        IReadOnlyList<string> emittedColumns,
        DocumentSchema schema)
    {
        ReportMissing(UnmatchedRequests(fields, emittedFields), "field");

        var emittedMachineColumns = new HashSet<string>(
            emittedColumns,
            StringComparer.OrdinalIgnoreCase);
        var emittedDisplayColumns = new HashSet<string>(
            emittedColumns,
            StringComparer.OrdinalIgnoreCase);
        foreach (var section in schema.SectionNames)
        {
            var definition = schema.GetSection(section);
            if (definition is null
                || !string.Equals(definition.ItemKind, "column", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var item in definition.Items)
            {
                if (emittedMachineColumns.Contains(JsonNamingPolicy.SnakeCaseLower.ConvertName(item.Name)))
                    emittedDisplayColumns.Add(item.Name);
            }
        }

        if (emittedMachineColumns.Contains("field"))
            emittedDisplayColumns.Add("Field");
        if (emittedMachineColumns.Contains("value"))
            emittedDisplayColumns.Add("Value");

        ReportMissing(UnmatchedRequests(columns, emittedDisplayColumns), "column");
    }

    private static void DiagnoseRendered(string[]? requestedNames, string renderedOutput, string kind)
    {
        var missing = DocumentSchema.DiagnoseRendered(requestedNames, renderedOutput);
        ReportMissing(missing, kind);
    }

    private static string[] UnmatchedRequests(
        string[]? requestedNames,
        IEnumerable<string> emittedNames)
    {
        if (requestedNames is not { Length: > 0 })
            return [];

        var matched = MatchedRequests(requestedNames, [.. emittedNames]);
        return [.. requestedNames.Where(name => !matched.Contains(name))];
    }

    private static HashSet<string> MatchedRequests(string[] requestedNames, string[] candidates)
    {
        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requestedName in requestedNames)
        {
            var projection = new MarkoutProjection { IncludeColumns = [requestedName] };
            if (projection.TryResolveColumns(candidates, out var resolution)
                && resolution.Kind == ColumnProjectionResolutionKind.Matched)
            {
                matched.Add(requestedName);
            }
        }

        return matched;
    }

    private static void ReportMissing(string[] missing, string kind)
    {
        if (missing.Length == 0)
            return;

        var label = missing.Length == 1 ? $"{kind} has" : $"{kind}s have";
        CommandError.WriteNote($"{missing.Length} {label} no data: {string.Join(", ", missing)}");
    }

    private static bool ValidateNames(DocumentSchema schema, string sectionName,
        string[] names, string kind)
    {
        var validation = schema.ValidateProjection(sectionName, names);
        if (validation.IsValid)
            return true;

        foreach (var name in validation.Unresolved)
        {
            var msg = $"{kind} '{name}' not found in section '{sectionName}'";
            if (validation.Suggestions.TryGetValue(name, out var suggestions))
                msg += $" (did you mean: {string.Join(", ", suggestions)}?)";
            msg += $" Run -D \"{sectionName}\" to list available {kind}s.";
            CommandError.WriteWarning(msg);
        }

        if (validation.Resolved.Length > 0)
            return true;

        CommandError.Write($"No {kind}s matched projection: {string.Join(", ", names)}");
        return false;
    }
}
