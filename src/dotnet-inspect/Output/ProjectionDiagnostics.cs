using System.Text.Json;
using Markout;
using Markout.Formatting;

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
        string[]? fields, string[]? columns, bool strictKinds = false)
    {
        if (string.IsNullOrEmpty(sectionName))
            return true;

        bool allValid = true;

        if (fields is { Length: > 0 })
            allValid &= ValidateNames(schema, sectionName, fields, "field", strictKinds);

        if (columns is { Length: > 0 })
            allValid &= ValidateNames(schema, sectionName, columns, "column", strictKinds);

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
        string[]? fields, string[]? columns, IReadOnlySet<string>? fieldLayoutSections = null,
        bool strictKinds = false)
    {
        if ((fields is not { Length: > 0 } && columns is not { Length: > 0 })
            || sectionNames is not { Count: > 0 })
        {
            return true;
        }

        var ok = true;
        if (fields is { Length: > 0 })
            ok &= ValidateNamesAcrossSections(
                schema, sectionNames, fields, "field", strictKinds: strictKinds);
        if (columns is { Length: > 0 })
            ok &= ValidateNamesAcrossSections(
                schema, sectionNames, columns, "column", fieldLayoutSections, strictKinds);

        return ok;
    }

    private static bool ValidateNamesAcrossSections(DocumentSchema schema,
        IReadOnlyCollection<string> sectionNames,
        string[] names,
        string kind,
        IReadOnlySet<string>? fieldLayoutSections = null,
        bool strictKinds = false)
    {
        // A name is an error only when it resolves in NO selected section. Names that
        // resolve in any section drop out, so a valid graph field is not reported against a
        // companion table that happens to lack it (e.g. the Callers table implied by --bin).
        var resolvedSomewhere = ResolveNamesAcrossSections(
            schema, sectionNames, names, kind, fieldLayoutSections, strictKinds);

        // Warn (with the per-section discovery hint) only for names missing everywhere.
        foreach (var section in sectionNames)
        {
            var definition = schema.GetSection(section);
            var compatible = definition is not null
                && (!strictKinds
                    || string.Equals(
                        definition.ItemKind,
                        kind,
                        StringComparison.OrdinalIgnoreCase));
            var validation = compatible
                ? schema.ValidateProjection(section, names)
                : null;
            var unresolved = validation?.Unresolved ?? names;
            foreach (var name in unresolved)
            {
                if (resolvedSomewhere.Contains(name))
                    continue;
                var msg = $"{kind} '{name}' not found in section '{section}'";
                if (validation?.Suggestions.TryGetValue(name, out var suggestions) == true)
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

    internal static void DiagnoseRendered(
        string[]? fields,
        string[]? columns,
        Action<TextWriter, IMarkoutFormatter, MarkoutWriterOptions> serialize,
        MarkoutWriterOptions writerOptions,
        DocumentSchema schema)
    {
        var actual = RenderManifestFormatter.Capture(serialize, writerOptions, schema);
        IReadOnlyList<string> emittedFields = [];
        if (fields is { Length: > 0 })
        {
            var savedProjection = writerOptions.Projection;
            var savedWindow = writerOptions.RowWindow;
            try
            {
                writerOptions.Projection = OutputFormatter.BuildProjection(columns: null, fields);
                writerOptions.RowWindow = null;
                var identity = RenderManifestFormatter.Capture(serialize, writerOptions, schema);
                emittedFields = identity.FieldsFor(actual.ContentKeys);
            }
            finally
            {
                writerOptions.Projection = savedProjection;
                writerOptions.RowWindow = savedWindow;
            }
        }

        DiagnoseEmitted(
            fields,
            columns,
            emittedFields,
            actual.TableColumns,
            schema);
    }

    internal static void DiagnoseProjectedJson(
        string[]? fields,
        string[]? columns,
        IReadOnlyList<string> emittedFields,
        IReadOnlyList<string> emittedColumns,
        DocumentSchema schema,
        IReadOnlyCollection<string>? sectionNames = null,
        IReadOnlySet<string>? fieldLayoutSections = null)
    {
        sectionNames ??= schema.SectionNames.ToArray();
        var resolvedFields = ResolveNamesAcrossSections(
            schema, sectionNames, fields ?? [], "field", fieldLayoutSections, strictKinds: true);
        ReportMissing(
            UnmatchedRequests(
                fields?.Where(resolvedFields.Contains).ToArray(),
                emittedFields),
            "field");

        var resolvedColumns = ResolveNamesAcrossSections(
            schema, sectionNames, columns ?? [], "column", fieldLayoutSections, strictKinds: true);
        ReportMissing(
            UnmatchedRequests(
                columns?.Where(resolvedColumns.Contains).ToArray(),
                ExpandDisplayColumns(emittedColumns, schema)),
            "column");
    }

    private static HashSet<string> ResolveNamesAcrossSections(
        DocumentSchema schema,
        IReadOnlyCollection<string> sectionNames,
        string[] names,
        string kind,
        IReadOnlySet<string>? fieldLayoutSections,
        bool strictKinds)
    {
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in sectionNames)
        {
            var definition = schema.GetSection(section);
            var compatible = definition is not null
                && (!strictKinds
                    || string.Equals(
                        definition.ItemKind,
                        kind,
                        StringComparison.OrdinalIgnoreCase));
            var unresolved = compatible
                ? new HashSet<string>(
                    schema.ValidateProjection(section, names).Unresolved,
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
            {
                if (!unresolved.Contains(name))
                    resolved.Add(name);
            }

            if (string.Equals(kind, "column", StringComparison.Ordinal)
                && fieldLayoutSections?.Contains(section) == true)
            {
                foreach (var name in MatchedRequests(names, ["Field", "Value"]))
                    resolved.Add(name);
            }
        }

        return resolved;
    }

    private static void DiagnoseEmitted(
        string[]? fields,
        string[]? columns,
        IReadOnlyList<string> emittedFields,
        IReadOnlyList<string> emittedColumns,
        DocumentSchema schema)
    {
        var displayColumns = ExpandDisplayColumns(emittedColumns, schema);
        ReportMissing(
            UnmatchedRequests(
                fields,
                emittedFields.Concat(displayColumns).ToArray()),
            "field");
        ReportMissing(
            UnmatchedRequests(columns, displayColumns),
            "field");
    }

    private static IReadOnlySet<string> ExpandDisplayColumns(
        IReadOnlyList<string> emittedColumns,
        DocumentSchema schema)
    {
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

        return emittedDisplayColumns;
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

    internal static bool MatchesAny(string[]? requestedNames, params string[] candidates)
        => requestedNames is { Length: > 0 }
            && MatchedRequests(requestedNames, candidates).Count > 0;

    private static void ReportMissing(string[] missing, string kind)
    {
        if (missing.Length == 0)
            return;

        var label = missing.Length == 1 ? $"{kind} has" : $"{kind}s have";
        CommandError.WriteNote($"{missing.Length} {label} no data: {string.Join(", ", missing)}");
    }

    private static bool ValidateNames(
        DocumentSchema schema,
        string sectionName,
        string[] names,
        string kind,
        bool strictKinds)
    {
        var definition = schema.GetSection(sectionName);
        var compatible = definition is not null
            && (!strictKinds
                || string.Equals(
                    definition.ItemKind,
                    kind,
                    StringComparison.OrdinalIgnoreCase));
        var validation = compatible
            ? schema.ValidateProjection(sectionName, names)
            : null;
        if (validation?.IsValid == true)
            return true;

        foreach (var name in validation?.Unresolved ?? names)
        {
            var msg = $"{kind} '{name}' not found in section '{sectionName}'";
            if (validation?.Suggestions.TryGetValue(name, out var suggestions) == true)
                msg += $" (did you mean: {string.Join(", ", suggestions)}?)";
            msg += $" Run -D \"{sectionName}\" to list available {kind}s.";
            CommandError.WriteWarning(msg);
        }

        if (validation?.Resolved.Length > 0)
            return true;

        CommandError.Write($"No {kind}s matched projection: {string.Join(", ", names)}");
        return false;
    }
}
