using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Options;
using DotnetInspector.Sections;
using DotnetInspect.Cli.Sections;
using DotnetInspect.Cli.Views;
using Markout;

namespace DotnetInspect.Cli.Output;

/// <summary>
/// Handles -D/--discover output. Renders discovery results through the standard
/// Markout pipeline (table, markdown, json) instead of bespoke formatting.
/// </summary>
public static class DiscoverOutput
{
    /// <summary>
    /// Runs discovery and writes output.
    /// Bare -D lists sections. -D SectionName lists items within that section.
    /// At Detailed verbosity, bare -D auto-promotes to tree (sections → items).
    /// </summary>
    public static int Execute(
        string[]? discover,
        DocumentSchema schema,
        DiscoveryOutputRequest request,
        string? rootLabel = null, IReadOnlyDictionary<string, string>? sectionCostAnnotations = null,
        IReadOnlyDictionary<string, string[]>? sectionCategories = null,
        IReadOnlySet<string>? catalogHiddenSections = null,
        IReadOnlySet<string>? listedCategoryDoors = null,
        RowSelectionIntent<string>? semanticRowSelection = null,
        string semanticSelectionName = "Discovery",
        IReadOnlySet<string>? exactOnlySections = null,
        DiscoveryDocument? document = null,
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            ResourcePath>? resourcePaths = null)
    {
        sectionCategories = FilterCategories(sectionCategories, schema.SectionNames);
        string[] columns = resourcePaths is null
            ? ["Name", "Kind"]
            : ["Name", "Kind", "Path"];

        // Discovery renders its own listing and returns, so the section pipeline's projection
        // dispatch never runs for it. Answer the projection here instead of dropping it. This
        // precedes the tree promotion below because a projection addresses the discovered rows,
        // not the shape they would have been rendered in.
        if (LensProjection.IsRequested(request))
        {
            var projectedRows = GetDiscoveryRows(
                discover,
                schema,
                sectionCostAnnotations,
                sectionCategories,
                catalogHiddenSections,
                listedCategoryDoors,
                exactOnlySections,
                document,
                resourcePaths);
            if (projectedRows == null)
                return 1;
            if (!TryApplyRowSelection(
                    projectedRows,
                    request.Rows,
                    semanticRowSelection,
                    semanticSelectionName,
                    out IReadOnlyList<DiscoveryRow> visibleRows))
            {
                return 1;
            }
            return LensProjection.TryProject(
                    request,
                    "-D/--discover",
                    visibleRows.Count,
                    out var projectionExitCode,
                    columns)
                ? projectionExitCode
                : 0;
        }

        bool projectedJson = IsProjectedJson(request);
        if (projectedJson)
        {
            if (!TryResolveProjectedJsonColumns(
                    request.Tree,
                    request,
                    columns,
                    out var projectedColumns))
                return 1;

            return WriteProjectedJson(
                discover,
                schema,
                sectionCostAnnotations,
                sectionCategories,
                catalogHiddenSections,
                listedCategoryDoors,
                request,
                projectedColumns,
                semanticRowSelection,
                semanticSelectionName,
                exactOnlySections,
                document,
                resourcePaths);
        }

        // Auto-promote to tree when discovering items from multiple sections
        bool tree = request.Tree;
        if (!tree
            && request.AllowsAutomaticTreePromotion
            && discover is { Length: > 0 }
            && (document is not null
                ? document.Selection.AddressedResources.Length > 1
                    && document.Selection.AddressedResources.All(identity =>
                        identity.Kind == DiscoveryResourceKind.Section)
                : !discover.Any(value => SelectResolver.TryResolveCategory(
                        value,
                        sectionCategories,
                        schema.SectionNames,
                        out _,
                        out _))
                    && ResolvedSectionCount(
                        discover,
                        schema,
                        sectionCategories,
                        exactOnlySections) > 1))
            tree = true;

        // Auto-promote bare -D to tree at Detailed verbosity (sections → items)
        if (!tree
            && request.AllowsAutomaticTreePromotion
            && discover is null or { Length: 0 }
            && request.Verbosity >= 3)
            tree = true;

        if (tree)
        {
            using var output = new StringWriter { NewLine = "\n" };
            int exitCode = WriteTree(
                discover,
                schema,
                rootLabel,
                sectionCostAnnotations,
                sectionCategories,
                catalogHiddenSections,
                listedCategoryDoors,
                request.Rows,
                semanticRowSelection,
                semanticSelectionName,
                output,
                exactOnlySections,
                document,
                resourcePaths);
            if (exitCode != 0)
                return exitCode;

            WriteOutput(request, writer => writer.Write(output.ToString()));
            return 0;
        }

        var rows = GetDiscoveryRows(
            discover,
            schema,
            sectionCostAnnotations,
            sectionCategories,
            catalogHiddenSections,
            listedCategoryDoors,
            exactOnlySections,
            document,
            resourcePaths);
        if (rows == null)
            return 1;
        if (!TryApplyRowSelection(
                rows,
                request.Rows,
                semanticRowSelection,
                semanticSelectionName,
                out IReadOnlyList<DiscoveryRow> selectedRows))
        {
            return 1;
        }
        rows = [.. selectedRows];

        var view = new DiscoveryListView { Items = rows };
        var context = new DiscoveryContext();

        WriteOutput(request, output =>
        {
            if (request.Format == OutputFormat.Json)
            {
                output.WriteLine(JsonSerializer.Serialize(
                    rows,
                    DiscoveryJsonContext.Default.ListDiscoveryRow));
            }
            else if (request.Format == OutputFormat.Markdown)
            {
                context.Serialize(view, output, new MarkdownFormatter());
            }
            else if (request.Format == OutputFormat.PlainText)
            {
                context.Serialize(view, output, new PlainTextFormatter());
            }
            else
            {
                bool tsv = request.Format == OutputFormat.Tsv;
                bool jsonl = request.Format == OutputFormat.Jsonl;
                OutputFormatter.WriteTable(
                    output,
                    showHeader: tsv && !request.NoHeader,
                    (writer, formatter) => context.Serialize(
                    view,
                    writer,
                    formatter,
                    OutputFormatter.CreateTableWriterOptions(tsv, jsonl)));
            }
        });

        return 0;
    }

    private static bool IsProjectedJson(DiscoveryOutputRequest request)
        => !LensProjection.IsRequested(request)
            && request.Format == OutputFormat.Json
            && (request.Fields is { Length: > 0 }
                || request.Columns is { Length: > 0 });

    private static bool TryResolveProjectedJsonColumns(
        bool tree,
        IProjectionOptions projection,
        IReadOnlyList<string> availableColumns,
        out string[] projectedColumns)
    {
        projectedColumns = [];
        if (tree)
        {
            CommandError.Write(
                "--fields/--columns cannot be combined with --tree for discovery.");
            return false;
        }

        return LensProjection.TryResolveColumns(
            projection,
            "-D/--discover",
            availableColumns,
            out projectedColumns);
    }

    private static int WriteProjectedJson(
        string[]? discover,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations,
        IReadOnlyDictionary<string, string[]>? sectionCategories,
        IReadOnlySet<string>? catalogHiddenSections,
        IReadOnlySet<string>? listedCategoryDoors,
        DiscoveryOutputRequest request,
        IReadOnlyList<string> columns,
        RowSelectionIntent<string>? semanticRowSelection,
        string semanticSelectionName,
        IReadOnlySet<string>? exactOnlySections,
        DiscoveryDocument? document,
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            ResourcePath>? resourcePaths)
    {
        var rows = GetDiscoveryRows(
            discover,
            schema,
            sectionCostAnnotations,
            sectionCategories,
            catalogHiddenSections,
            listedCategoryDoors,
            exactOnlySections,
            document,
            resourcePaths);
        if (rows == null)
            return 1;

        if (!TryApplyRowSelection(
                rows,
                request.Rows,
                semanticRowSelection,
                semanticSelectionName,
                out IReadOnlyList<DiscoveryRow> selectedRows))
        {
            return 1;
        }

        WriteOutput(
            request,
            output => WriteProjectedJson(selectedRows, columns, output));
        return 0;
    }

    private static void WriteProjectedJson(
        IReadOnlyList<DiscoveryRow> rows,
        IReadOnlyList<string> columns,
        TextWriter output)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            foreach (var row in rows)
            {
                writer.WriteStartObject();
                foreach (var column in columns)
                {
                    if (column.Equals("Name", StringComparison.OrdinalIgnoreCase))
                        writer.WriteString("name", row.Name);
                    else if (column.Equals(
                                 "Kind",
                                 StringComparison.OrdinalIgnoreCase))
                        writer.WriteString("kind", row.Kind);
                    else if (row.Path is null)
                        writer.WriteNull("path");
                    else
                        writer.WriteString("path", row.Path);
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }

        output.WriteLine(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    /// <summary>
    /// Runs discovery with effective filtering (only sections with data).
    /// </summary>
    public static int ExecuteEffective(
        string[]? discover,
        List<string> effectiveSections,
        DocumentSchema schema,
        DiscoveryOutputRequest request,
        string? rootLabel = null, DocumentSchema? fullSchema = null,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations = null,
        IReadOnlyDictionary<string, string[]>? sectionCategories = null,
        IReadOnlySet<string>? catalogHiddenSections = null,
        IReadOnlySet<string>? listedCategoryDoors = null,
        RowSelectionIntent<string>? semanticRowSelection = null,
        string semanticSelectionName = "Discovery",
        IReadOnlySet<string>? exactOnlySections = null,
        string? resourceCatalog = null,
        OutputCapabilityCatalog? resourceCapabilities = null)
    {
        if ((resourceCatalog is null) != (resourceCapabilities is null))
        {
            throw new ArgumentException(
                "Resource catalog and capabilities must be supplied together.");
        }

        // Build a filtered schema with only effective sections
        var filtered = new DocumentSchema();
        foreach (var name in effectiveSections)
        {
            var section = schema.GetSection(name);
            if (section != null)
                filtered.Add(name, section.ItemKind, section.Items.Select(i => i.Name).ToArray());
            else
                filtered.AddSection(name);
        }
        var effectiveSectionCategories = FilterCategories(
            sectionCategories,
            filtered.SectionNames);
        string[] columns = resourceCatalog is null
            ? ["Name", "Kind"]
            : ["Name", "Kind", "Path"];

        string[]? projectedColumns = null;
        if (IsProjectedJson(request)
            && !TryResolveProjectedJsonColumns(
                request.Tree,
                request,
                columns,
                out projectedColumns))
        {
            return 1;
        }

        // For a specific section query, distinguish a valid section that simply has no data for
        // this input from a genuinely unknown section. The full schema lets us recognize the
        // former and report it clearly instead of the misleading "Section not found".
        if (discover is { Length: > 0 } && fullSchema != null)
        {
            var remaining = FilterEmptyEffectiveSections(
                discover,
                filtered,
                fullSchema,
                sectionCategories,
                exactOnlySections);
            if (remaining == null)
            {
                if (!TryApplyRowSelection(
                        Array.Empty<DiscoveryRow>(),
                        request.Rows,
                        semanticRowSelection,
                        semanticSelectionName,
                        out _))
                {
                    return 1;
                }

                // Every requested section was valid but empty, so the discovered row count is
                // zero. Returning here without projecting would drop the request.
                if (LensProjection.IsRequested(request))
                {
                    return LensProjection.TryProject(
                            request,
                            "-D/--discover",
                            0,
                            out var emptyProjectionExitCode,
                            columns)
                        ? emptyProjectionExitCode
                        : 0;
                }
                if (projectedColumns is not null)
                {
                    WriteOutput(
                        request,
                        output => WriteProjectedJson([], projectedColumns, output));
                    return 0;
                }
                if (request.OutputPath is not null)
                    WriteOutput(request, static _ => { });
                return 0;
            }
            discover = remaining;
        }

        DiscoveryDocumentFactory.Projection? resourceProjection =
            resourceCatalog is null
                ? null
                : DiscoveryDocumentFactory.CreateProjection(
                    resourceCatalog,
                    discover,
                    filtered,
                    effectiveSectionCategories,
                    catalogHiddenSections,
                    listedCategoryDoors,
                    sectionCostAnnotations,
                    exactOnlySections,
                    resourceCapabilities!);
        if (resourceCatalog is not null
            && resourceProjection is null)
        {
            return 1;
        }
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            ResourcePath>? resourcePaths =
                resourceProjection?.ResourcePaths.ToDictionary(
                    static registration => registration.Identity,
                    static registration => registration.Path);

        if (projectedColumns is not null)
        {
            return WriteProjectedJson(
                discover,
                filtered,
                sectionCostAnnotations,
                effectiveSectionCategories,
                catalogHiddenSections,
                listedCategoryDoors,
                request,
                projectedColumns,
                semanticRowSelection,
                semanticSelectionName,
                exactOnlySections,
                resourceProjection?.Document,
                resourcePaths);
        }

        return Execute(
            discover,
            filtered,
            request,
            rootLabel,
            sectionCostAnnotations,
            effectiveSectionCategories,
            catalogHiddenSections,
            listedCategoryDoors,
            semanticRowSelection: semanticRowSelection,
            semanticSelectionName: semanticSelectionName,
            exactOnlySections: exactOnlySections,
            document: resourceProjection?.Document,
            resourcePaths: resourcePaths);
    }

    /// <summary>
    /// Splits requested discovery sections into those still worth resolving and those that are
    /// valid in the full schema but have no data under effective discovery. For the latter a
    /// "has no data for this query" note is written (clearer than "Section not found", since the
    /// section is real — just empty for this input). Truly unknown names are kept so the normal
    /// discovery resolver can report them with suggestions. Returns the names to still resolve,
    /// or <c>null</c> when every requested section was valid-but-empty (fully handled via notes).
    /// </summary>
    private static string[]? FilterEmptyEffectiveSections(
        string[] discover,
        DocumentSchema effective,
        DocumentSchema fullSchema,
        IReadOnlyDictionary<string, string[]>? sectionCategories,
        IReadOnlySet<string>? exactOnlySections)
    {
        var remaining = new List<string>();
        bool emittedNote = false;
        foreach (var name in discover)
        {
            if (SelectResolver.TryResolveCategory(
                    name,
                    sectionCategories,
                    fullSchema.SectionNames,
                    out var categoryName,
                    out var categorySections))
            {
                if (!categorySections.Any(member =>
                        effective.SectionNames.Contains(member, StringComparer.OrdinalIgnoreCase)))
                {
                    CommandError.WriteNote(
                        $"category '{categoryName}' has no data for this query");
                    emittedNote = true;
                    continue;
                }

                remaining.Add(categoryName);
                continue;
            }

            if (name.StartsWith("@", StringComparison.Ordinal))
            {
                remaining.Add(name);
                continue;
            }

            var (effMatches, _) = ResolveDiscoveryMatches(
                name,
                effective.SectionNames,
                exactOnlySections);
            if (effMatches.Count >= 1)
            {
                remaining.Add(name);
                continue;
            }

            var (fullMatches, _) = ResolveDiscoveryMatches(
                name,
                fullSchema.SectionNames,
                exactOnlySections);
            if (fullMatches.Count >= 1)
            {
                foreach (var match in fullMatches)
                    CommandError.WriteNote($"section '{match}' has no data for this query");
                emittedNote = true;
            }
            else
            {
                remaining.Add(name);
            }
        }

        return remaining.Count == 0 && emittedNote ? null : remaining.ToArray();
    }

    /// <summary>
    /// Restricts effective section names to those the discovery schema can represent.
    /// The single-type member pipeline reports member-detail code sections (Decompiled Source,
    /// PDB Source, IL) as renderable whenever the type has methods, but these
    /// are member-detail sections produced only for a specific member selection — they are
    /// not part of the type schema. Dropping them keeps effective discovery consistent
    /// with <c>-D</c> and ensures every listed section is queryable via <c>-D &lt;Section&gt;</c>.
    /// </summary>
    public static List<string> RestrictToSchemaSections(List<string> effectiveSections, DocumentSchema schema)
        => effectiveSections.Where(s => schema.GetSection(s) != null).ToList();

    /// <summary>
    /// Restricts effective sections to those that actually produced a table in the
    /// supplied render manifest. A tabular schema section whose
    /// <c>CanRender</c> probe passed but which rendered no table for this input is dropped,
    /// since it has no data to query. This catches sections whose <c>CanRender</c> is a coarse
    /// proxy — e.g. "Custom Attributes", gated on "the type has methods" but only populated
    /// when a specific member's attributes are read (the member-detail path) — so
    /// effective discovery reflects real data rather than mere potential.
    /// Only tabular sections (those with schema columns) are subject to the drop; non-tabular
    /// sections are left untouched because their content may not render as a markdown table.
    /// This measures renderability in the current type effective-discovery render path;
    /// a section populated only in another command path is
    /// intentionally treated as having no data here.
    /// </summary>
    internal static List<string> RestrictToRenderedSections(
        List<string> effectiveSections,
        DocumentSchema schema,
        RenderedSectionManifest rendered)
        => effectiveSections.Where(name =>
        {
            var section = schema.GetSection(name);
            bool tabular = section is { Items.Length: > 0 };
            return !tabular || rendered.HasTable(name);
        }).ToList();

    /// <summary>
    /// Returns a copy of the schema with the named column removed from every section.
    /// Used to hide deprecated or option-internal columns from plain discovery.
    /// </summary>
    public static DocumentSchema WithoutColumn(DocumentSchema schema, string columnName)
    {
        var filtered = new DocumentSchema();
        foreach (var name in schema.SectionNames)
        {
            var section = schema.GetSection(name);
            if (section == null) { filtered.AddSection(name); continue; }

            var items = section.Items
                .Where(i => !string.Equals(i.Name, columnName, StringComparison.OrdinalIgnoreCase))
                .Select(i => i.Name)
                .ToArray();

            if (items.Length > 0)
                filtered.Add(name, section.ItemKind, items);
            else
                filtered.AddSection(name);
        }
        return filtered;
    }

    /// <summary>
    /// Filters a schema's section columns to only those that actually render, given a
    /// render manifest for the same view. Used by effective discovery so the reported
    /// columns match what the user would see at their current verbosity/options (e.g. summary
    /// columns replace detailed columns at Minimal verbosity).
    /// Matches against section-scoped table columns emitted by the serializer.
    /// </summary>
    /// <param name="fieldLayoutSections">
    /// Sections rendered as a <c>Field</c>/<c>Value</c> fact table rather than one column per
    /// schema item. Their rendered header cells are literally "Field" and "Value", which intersect
    /// no schema item name, so matching on columns would strip every item and report the section as
    /// having nothing to query. For these, match on the rendered field rows instead.
    /// </param>
    internal static DocumentSchema FilterSchemaToRenderedColumns(
        List<string> effectiveSections,
        DocumentSchema schema,
        RenderedSectionManifest rendered,
        IReadOnlySet<string>? fieldLayoutSections = null)
    {
        var filtered = new DocumentSchema();
        foreach (var name in effectiveSections)
        {
            var section = schema.GetSection(name);
            if (section == null) { filtered.AddSection(name); continue; }

            if (fieldLayoutSections?.Contains(name) == true)
            {
                var renderedFields = rendered.GetFields(name);
                if (renderedFields is not null)
                {
                    var fieldItems = section.Items
                        .Where(item => renderedFields.Contains(item.Name))
                        .Select(item => item.Name)
                        .ToArray();
                    if (fieldItems.Length > 0)
                        filtered.Add(name, section.ItemKind, fieldItems);
                    else
                        filtered.AddSection(name);
                    continue;
                }
            }

            // No table rendered for this section (e.g. a non-tabular section such as Source/IL,
            // or one not produced by the member renderer): preserve the original schema columns
            // rather than stripping them — we only narrow columns for sections we actually rendered.
            var headerCells = rendered.GetTableColumns(name);
            if (headerCells is null)
            {
                if (section.Items.Length > 0)
                    filtered.Add(name, section.ItemKind, section.Items.Select(i => i.Name).ToArray());
                else
                    filtered.AddSection(name);
                continue;
            }

            var effectiveItems = section.Items
                .Where(item => headerCells.Contains(item.Name))
                .Select(item => item.Name)
                .ToArray();

            if (effectiveItems.Length > 0)
                filtered.Add(name, section.ItemKind, effectiveItems);
            else
                filtered.AddSection(name);
        }
        return filtered;
    }

    /// <summary>
    /// Filters selected schema sections to the items that emitted data through the
    /// serializer. Other effective sections retain their full schema.
    /// </summary>
    internal static DocumentSchema FilterSchemaToRenderedItems(
        List<string> effectiveSections,
        DocumentSchema schema,
        RenderedSectionManifest rendered,
        IReadOnlySet<string> filteredSections)
    {
        var filtered = new DocumentSchema();
        foreach (var name in effectiveSections)
        {
            var section = schema.GetSection(name);
            if (section is null)
            {
                filtered.AddSection(name);
                continue;
            }

            var renderedItems = filteredSections.Contains(name)
                ? rendered.GetSectionRenderedNames(section.ItemKind, name)
                : null;
            if (renderedItems is not null)
            {
                var effectiveItems = section.Items
                    .Where(item => renderedItems.Contains(item.Name))
                    .Select(item => item.Name)
                    .ToArray();
                if (effectiveItems.Length > 0)
                    filtered.Add(name, section.ItemKind, effectiveItems);
                else
                    filtered.AddSection(name);
            }
            else if (section.Items.Length > 0)
            {
                filtered.Add(name, section.ItemKind, section.Items.Select(item => item.Name).ToArray());
            }
            else
            {
                filtered.AddSection(name);
            }
        }

        return filtered;
    }

    internal static List<DiscoveryRow> GetTopLevelRows(
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]>? sectionCategories,
        IReadOnlySet<string>? catalogHiddenSections,
        IReadOnlySet<string>? listedCategoryDoors,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations = null)
    {
        var items = schema.Discover()!;

        if (listedCategoryDoors != null)
        {
            var doorRows = sectionCategories?
                .Where(category => listedCategoryDoors.Contains(category.Key))
                .Select(category => new DiscoveryRow(category.Key, "category"))
                .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ToList() ?? [];

            var effectiveRows = items
                .Where(i => catalogHiddenSections is null
                    || !catalogHiddenSections.Contains(i.Name))
                .Select(i => new DiscoveryRow(i.Name, i.Kind))
                .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return [.. doorRows, .. effectiveRows];
        }

        var categoryRows = sectionCategories?
            .Where(category => catalogHiddenSections is null
                || !string.Equals(
                    category.Key,
                    SectionCategoryNames.Hidden,
                    StringComparison.OrdinalIgnoreCase))
            .Select(category => new DiscoveryRow(category.Key, "category"))
            .ToList() ?? [];

        var sectionRows = items
            .Where(i => catalogHiddenSections is null
                || !catalogHiddenSections.Contains(i.Name))
            .Select(i => new DiscoveryRow(
                i.Name,
                AnnotateKind(
                    i.Kind,
                    i.Name,
                    sectionCostAnnotations)))
            .ToList();

        return sectionRows.Concat(categoryRows)
            .OrderBy(GetDiscoveryRowSortRank)
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<DiscoveryRow>? GetDiscoveryRows(
        string[]? discover,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations = null,
        IReadOnlyDictionary<string, string[]>? sectionCategories = null,
        IReadOnlySet<string>? catalogHiddenSections = null,
        IReadOnlySet<string>? listedCategoryDoors = null,
        IReadOnlySet<string>? exactOnlySections = null,
        DiscoveryDocument? document = null,
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            ResourcePath>? resourcePaths = null)
    {
        if (document is not null)
        {
            return
            [
                .. document.Selection.Rows.Select(identity =>
                    CreateRow(
                        document.GetResource(identity),
                        document.Selection.IsCatalog
                            ? null
                            : sectionCostAnnotations,
                        resourcePaths)),
            ];
        }

        // Bare -D. Curated pipelines (listedCategoryDoors provided) lead with the topical category
        // doors, then a single alpha group of effective sections, with no cost annotations. Legacy
        // pipelines use the same category/section/opt-in grouping.
        if (discover is null or { Length: 0 })
        {
            return GetTopLevelRows(
                schema,
                sectionCategories,
                catalogHiddenSections,
                listedCategoryDoors,
                sectionCostAnnotations);
        }

        // -D SectionName: list items within section
        var rows = new List<DiscoveryRow>();
        foreach (var name in discover)
        {
            if (SelectResolver.TryResolveCategory(
                    name,
                    sectionCategories,
                    schema.SectionNames,
                    out _,
                    out var categorySections))
            {
                foreach (var sectionName in categorySections.OrderBy(s => s, StringComparer.OrdinalIgnoreCase))
                    rows.Add(new DiscoveryRow(sectionName, AnnotateKind("section", sectionName, sectionCostAnnotations)));
                continue;
            }

            if (name.StartsWith("@", StringComparison.Ordinal))
            {
                WriteCategoryNotFound(name, sectionCategories);
                return null;
            }

            var resolved = ResolveDiscoverSection(
                name,
                schema,
                exactOnlySections);
            if (resolved == null)
                return null;

            var items = schema.Discover(resolved);
            if (items != null)
            {
                foreach (var item in items)
                    rows.Add(new DiscoveryRow(item.Name, item.Kind));
            }
            if (string.Equals(resolved, SectionNames.PerformanceTriage, StringComparison.OrdinalIgnoreCase)
                || PerformanceKinds.Sections.Contains(resolved, StringComparer.OrdinalIgnoreCase))
                rows.AddRange(PerformanceTriageQueryRows());
        }

        return rows;
    }

    static IEnumerable<DiscoveryRow> PerformanceTriageQueryRows()
        => PerformanceTriageOptions.DiscoveryItems()
            .Select(item => new DiscoveryRow(
                item.Name,
                item.Kind));

    private static int ResolvedSectionCount(
        string[] discover,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]>? sectionCategories,
        IReadOnlySet<string>? exactOnlySections)
    {
        int count = 0;
        foreach (var name in discover)
        {
            if (SelectResolver.TryResolveCategory(
                    name,
                    sectionCategories,
                    schema.SectionNames,
                    out _,
                    out var categorySections))
            {
                count += categorySections.Length;
                continue;
            }

            var (matches, _) = ResolveDiscoveryMatches(
                name,
                schema.SectionNames,
                exactOnlySections);
            if (matches.Count == 1)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Resolves a section name for discovery. Supports exact match (case-insensitive)
    /// and glob patterns (* / ?). Globs must match exactly one section.
    /// </summary>
    private static string? ResolveDiscoverSection(
        string name,
        DocumentSchema schema,
        IReadOnlySet<string>? exactOnlySections)
    {
        var (matches, miss) = ResolveDiscoveryMatches(
            name,
            schema.SectionNames,
            exactOnlySections);

        if (miss == null && matches.Count == 1)
            return matches[0];

        // Multi-glob match: the miss contains the matches as suggestions
        if (miss != null && miss.IsGlob && matches.Count > 1)
        {
            CommandError.Write($"'{name}' matches {matches.Count} sections: {string.Join(", ", matches)}.");
            CommandError.WriteLine("Discovery requires exactly one section. Be more specific.");
            return null;
        }

        // No match — write error with suggestions
        if (miss != null)
        {
            CommandError.Write($"Section '{miss.Value}' not found.");
            if (miss.Suggestions.Count > 0)
            {
                CommandError.WriteBlankLine();
                CommandError.WriteLine("Did you mean:");
                foreach (var s in miss.Suggestions)
                    CommandError.WriteLine($"  {s}");
            }
        }

        return null;
    }

    private static (List<string> Matches, SelectMiss? Miss)
        ResolveDiscoveryMatches(
            string name,
            IReadOnlyList<string> knownSections,
            IReadOnlySet<string>? exactOnlySections)
    {
        var direct = SelectResolver.ResolveSingleWithProvenance(
            name,
            knownSections,
            singleGlob: true);
        if (direct.IsExact
            || exactOnlySections is null
            || exactOnlySections.Count == 0
            || (!name.Contains('*')
                && !name.Contains('?')))
        {
            return (direct.Matches, direct.Miss);
        }

        string[] wildcardSections =
        [
            .. knownSections.Where(section =>
                !exactOnlySections.Contains(section)),
        ];
        return SelectResolver.ResolveSingle(
            name,
            wildcardSections,
            singleGlob: true);
    }

    internal static bool WriteUnresolvedSections(
        SelectResult result)
    {
        if (result.Unresolved.Count == 0
            || result.Sections is { Count: > 0 })
        {
            return false;
        }

        foreach (SelectMiss miss in result.Unresolved)
        {
            if (miss.IsGlob)
            {
                CommandError.Write(
                    $"No sections match '{miss.Value}'.");
            }
            else
            {
                CommandError.Write(
                    $"Section '{miss.Value}' not found.");
            }

            if (miss.Suggestions.Count == 0)
                continue;

            CommandError.WriteBlankLine();
            CommandError.WriteLine(
                miss.ListsAllSections
                    ? "Available sections:"
                    : "Did you mean:");
            foreach (string suggestion in miss.Suggestions)
                CommandError.WriteLine($"  {suggestion}");
        }

        return true;
    }

    /// <summary>
    /// Appends an annotation to a section's kind label, e.g. <c>"section"</c> →
    /// <c>"section (opt-in)"</c>, when <paramref name="annotations"/> has an entry for the
    /// section name. Cheap default sections (no entry) are returned unchanged.
    /// </summary>
    private static string AnnotateKind(string kind, string name,
        IReadOnlyDictionary<string, string>? annotations)
        => annotations != null && annotations.TryGetValue(name, out var tier)
            ? $"{kind} ({tier})"
            : kind;

    private static int GetDiscoveryRowSortRank(DiscoveryRow row)
    {
        if (row.Kind.Equals("category", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (row.Kind.Contains(SectionAnnotations.OptIn, StringComparison.OrdinalIgnoreCase))
            return 2;
        return 1;
    }

    private static int WriteTree(string[]? discover, DocumentSchema schema, string? rootLabel = null,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations = null,
        IReadOnlyDictionary<string, string[]>? sectionCategories = null,
        IReadOnlySet<string>? catalogHiddenSections = null,
        IReadOnlySet<string>? listedCategoryDoors = null,
        RowWindow? rows = null,
        RowSelectionIntent<string>? semanticRowSelection = null,
        string semanticSelectionName = "Discovery",
        TextWriter? output = null,
        IReadOnlySet<string>? exactOnlySections = null,
        DiscoveryDocument? document = null,
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            ResourcePath>? resourcePaths = null)
    {
        List<TreeNode> nodes = document is null
            ? []
            : CreateTreeNodes(
                document,
                sectionCostAnnotations,
                resourcePaths);

        if (document is null && discover is { Length: > 0 })
        {
            // Resolve each section and build grouped tree
            foreach (var name in discover)
            {
                if (SelectResolver.TryResolveCategory(
                        name,
                        sectionCategories,
                        schema.SectionNames,
                        out var categoryName,
                        out var categorySections))
                {
                    nodes.Add(new TreeNode(categoryName)
                    {
                        Children = categorySections
                            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                            .Select(section => new TreeNode(section)).ToList()
                    });
                    continue;
                }

                var resolved = ResolveDiscoverSection(
                    name,
                    schema,
                    exactOnlySections);
                if (resolved == null) return 1;

                var section = schema.GetSection(resolved);
                if (section == null || section.Items.Length == 0)
                {
                    nodes.Add(new TreeNode(resolved));
                    continue;
                }

                // Single section: show items as top-level tree
                if (discover.Length == 1)
                {
                    foreach (var item in section.Items)
                        nodes.Add(new TreeNode($"{item.Name} ({item.Kind})"));
                }
                else
                {
                    var children = section.Items
                        .Select(i => new TreeNode($"{i.Name} ({i.Kind})"))
                        .ToList();
                    nodes.Add(new TreeNode(resolved) { Children = children });
                }
            }
        }
        else if (document is null)
        {
            // Curated pipelines (listedCategoryDoors provided) mirror the flat GetDiscoveryRows
            // catalog: topical doors (alpha) then the effective section group (alpha), with no cost
            // annotations and no @All/@Default/@Hidden poles.
            if (listedCategoryDoors != null)
            {
                var doorNodes = (sectionCategories ?? new Dictionary<string, string[]>())
                    .Where(category => listedCategoryDoors.Contains(category.Key))
                    .OrderBy(category => category.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(category => new TreeNode($"{category.Key} (category)")
                    {
                        Children = category.Value
                            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                            .Select(section => new TreeNode(section)).ToList()
                    });

                var sectionNodes = schema.Discover()!
                    .Where(i => catalogHiddenSections is null || !catalogHiddenSections.Contains(i.Name))
                    .OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(i =>
                    {
                        var children = new List<TreeNode>();
                        var section = schema.GetSection(i.Name);
                        if (section != null)
                            foreach (var item in section.Items)
                                children.Add(new TreeNode($"{item.Name} ({item.Kind})"));
                        return new TreeNode(i.Name) { Children = children };
                    });

                nodes.AddRange(doorNodes);
                nodes.AddRange(sectionNodes);
            }
            else
            {
            var sectionRows = schema.SectionNames
                // Catalog-hidden sections appear only under their category node (drill-in), not as
                // top-level entries — same entrypoint model as the flat bare -D listing.
                .Where(name => catalogHiddenSections is null || !catalogHiddenSections.Contains(name))
                .Select(name => new DiscoveryRow(name, AnnotateKind("section", name, sectionCostAnnotations)))
                .ToList();
            var categoryRows = sectionCategories?
                .Keys
                // See the flat catalog: @Hidden is shown only in the full schema (catalogHidden null).
                .Where(name => catalogHiddenSections is null
                    || !string.Equals(name, SectionCategoryNames.Hidden, StringComparison.OrdinalIgnoreCase))
                .Select(name => new DiscoveryRow(name, "category"))
                .ToList() ?? new List<DiscoveryRow>();

            // Full tree: @categories, regular sections, then opt-in sections.
            // Each group is alpha sorted.
            var orderedRows = sectionRows.Concat(categoryRows)
                .OrderBy(GetDiscoveryRowSortRank)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var row in orderedRows)
            {
                if (row.Kind.Equals("category", StringComparison.OrdinalIgnoreCase))
                {
                    nodes.Add(new TreeNode($"{row.Name} (category)")
                    {
                        Children = sectionCategories![row.Name]
                            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                            .Select(section => new TreeNode(section)).ToList()
                    });
                    continue;
                }

                var sectionName = row.Name;
                var children = new List<TreeNode>();
                var section = schema.GetSection(sectionName);
                if (section != null)
                {
                    foreach (var item in section.Items)
                        children.Add(new TreeNode($"{item.Name} ({item.Kind})"));
                }
                var label = sectionCostAnnotations != null
                    && sectionCostAnnotations.TryGetValue(sectionName, out var tier)
                        ? $"{sectionName} ({tier})"
                        : sectionName;
                nodes.Add(new TreeNode(label) { Children = children });
            }
            }
        }

        if (!TryApplyTreeSelection(
                nodes,
                rows,
                semanticRowSelection,
                semanticSelectionName,
                discover is { Length: > 0 },
                out nodes))
        {
            return 1;
        }

        // Wrap in root node when label is provided and showing full tree
        if (rootLabel != null && discover is null or { Length: 0 })
            nodes = [new TreeNode(rootLabel) { Children = nodes }];

        var view = new DiscoveryTreeView { Sections = nodes };
        MarkoutSerializer.Serialize(
            view,
            output ?? Console.Out,
            DiscoveryContext.Default);
        return 0;
    }

    private static DiscoveryRow CreateRow(
        DiscoveryResource resource,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations,
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            ResourcePath>? resourcePaths) =>
        new(
            resource.Identity.Name,
            resource.Identity.Kind switch
            {
                DiscoveryResourceKind.Category => "category",
                DiscoveryResourceKind.Section => AnnotateKind(
                    "section",
                    resource.Identity.Name,
                    sectionCostAnnotations),
                DiscoveryResourceKind.Item =>
                    resource.Identity.ItemKind!,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(resource),
                    resource.Identity.Kind,
                    "Unknown discovery resource kind."),
            },
            resourcePaths is not null
                && resourcePaths.TryGetValue(
                    resource.Identity,
                    out ResourcePath? path)
                    ? path.Value
                    : null);

    private static List<TreeNode> CreateTreeNodes(
        DiscoveryDocument document,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations,
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            ResourcePath>? resourcePaths)
    {
        if (document.Selection.IsCatalog)
        {
            return
            [
                .. document.Selection.Rows.Select(identity =>
                    CreateTreeNode(
                        document,
                        identity,
                        includeKind: identity.Kind
                            == DiscoveryResourceKind.Category,
                        sectionCostAnnotations,
                        resourcePaths,
                        includeMembers: true)),
            ];
        }

        if (document.Selection.AddressedResources.Length == 1
            && document.Selection.AddressedResources[0].Kind
                == DiscoveryResourceKind.Section)
        {
            DiscoveryResource section = document.GetResource(
                document.Selection.AddressedResources[0]);
            DiscoveryResourceIdentity[] treeMembers =
            [
                .. TreeMembers(section),
            ];
            if (treeMembers.Length == 0)
            {
                return
                [
                    CreateTreeNode(
                        document,
                        section.Identity,
                        includeKind: false,
                        sectionCostAnnotations,
                        resourcePaths),
                ];
            }

            return
            [
                .. treeMembers.Select(identity =>
                    CreateTreeNode(
                        document,
                        identity,
                        includeKind: true,
                        sectionCostAnnotations,
                        resourcePaths)),
            ];
        }

        return
        [
            .. document.Selection.AddressedResources.Select(identity =>
                CreateTreeNode(
                    document,
                    identity,
                    includeKind: false,
                    sectionCostAnnotations,
                    resourcePaths,
                    includeMembers: true)),
        ];
    }

    private static TreeNode CreateTreeNode(
        DiscoveryDocument document,
        DiscoveryResourceIdentity identity,
        bool includeKind,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations,
        IReadOnlyDictionary<
            DiscoveryResourceIdentity,
            ResourcePath>? resourcePaths,
        bool includeMembers = false)
    {
        DiscoveryResource resource = document.GetResource(identity);
        string kind = resource.Identity.Kind switch
        {
            DiscoveryResourceKind.Category => "category",
            DiscoveryResourceKind.Section => AnnotateKind(
                "section",
                resource.Identity.Name,
                sectionCostAnnotations),
            DiscoveryResourceKind.Item => resource.Identity.ItemKind!,
            _ => throw new ArgumentOutOfRangeException(
                nameof(identity),
                identity.Kind,
                "Unknown discovery resource kind."),
        };
        string label = includeKind
            ? $"{identity.Name} ({kind})"
            : identity.Name;
        if (resourcePaths is not null
            && resourcePaths.TryGetValue(identity, out ResourcePath? path))
        {
            label = $"{label} [{path.Value}]";
        }
        if (identity.Kind == DiscoveryResourceKind.Category)
        {
            return new TreeNode(label)
            {
                Children =
                [
                    .. resource.Members.Select(member =>
                        CreateTreeNode(
                            document,
                            member,
                            includeKind: false,
                            sectionCostAnnotations,
                            resourcePaths)),
                ],
            };
        }

        return new TreeNode(label)
        {
            Children = includeMembers
                ?
                [
                    .. TreeMembers(resource).Select(member =>
                        CreateTreeNode(
                            document,
                            member,
                            includeKind: member.Kind
                                == DiscoveryResourceKind.Item,
                            sectionCostAnnotations,
                            resourcePaths)),
                ]
                : [],
        };
    }

    private static IEnumerable<DiscoveryResourceIdentity> TreeMembers(
        DiscoveryResource resource) =>
        resource.Identity.Kind == DiscoveryResourceKind.Section
            ? resource.Members.Where(member =>
                member.ItemKind is "field" or "column")
            : resource.Members;

    private static void WriteOutput(
        DiscoveryOutputRequest request,
        Action<TextWriter> write)
        => OutputDestination.Write(
            request.OutputPath,
            request.Rows,
            write);

    private static bool TryApplyTreeSelection(
        List<TreeNode> nodes,
        RowWindow? rows,
        RowSelectionIntent<string>? semanticRowSelection,
        string semanticSelectionName,
        bool grouped,
        out List<TreeNode> selectedNodes)
    {
        if (!grouped)
        {
            if (!TryApplyRowSelection(
                    nodes,
                    rows,
                    semanticRowSelection,
                    semanticSelectionName,
                    out IReadOnlyList<TreeNode> selectedTopLevel))
            {
                selectedNodes = [];
                return false;
            }

            selectedNodes = [.. selectedTopLevel];
            return true;
        }

        var rowAddresses = new List<(int Parent, int? Child)>();
        for (var parent = 0; parent < nodes.Count; parent++)
        {
            if (nodes[parent].Children is { Count: > 0 } children)
            {
                for (var child = 0; child < children.Count; child++)
                    rowAddresses.Add((parent, child));
            }
            else
            {
                rowAddresses.Add((parent, null));
            }
        }

        if (!TryApplyRowSelection(
                rowAddresses,
                rows,
                semanticRowSelection,
                semanticSelectionName,
                out IReadOnlyList<(int Parent, int? Child)> selectedAddresses))
        {
            selectedNodes = [];
            return false;
        }

        var result = new List<TreeNode>();
        foreach (var parentGroup in selectedAddresses.GroupBy(address => address.Parent))
        {
            var source = nodes[parentGroup.Key];
            var selectedChildren = parentGroup
                .Where(address => address.Child.HasValue)
                .Select(address => source.Children![address.Child!.Value])
                .ToList();
            result.Add(selectedChildren.Count == 0
                ? source
                : new TreeNode(source.Text) { Children = selectedChildren });
        }
        selectedNodes = result;
        return true;
    }

    private static bool TryApplyRowSelection<T>(
        IReadOnlyList<T> rows,
        RowWindow? legacyWindow,
        RowSelectionIntent<string>? semanticRowSelection,
        string semanticSelectionName,
        out IReadOnlyList<T> selectedRows)
        => CliSemanticRowSelection.TrySelectOrApplyLegacy(
            semanticRowSelection,
            legacyWindow,
            rows,
            "discovery",
            failure =>
                $"{semanticSelectionName} row selection stage "
                + $"{failure.Failure.StageNumber} requires discovery row "
                + $"{failure.Failure.RequiredPosition}, but only "
                + $"{failure.Failure.AvailableCount} discovery rows are available.",
            out selectedRows);

    private static IReadOnlyDictionary<string, string[]>? FilterCategories(
        IReadOnlyDictionary<string, string[]>? categories,
        string[] sectionNames)
    {
        if (categories == null)
            return null;

        HashSet<string> known = [.. sectionNames];
        Dictionary<string, string[]> filtered = new(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, sections) in categories)
        {
            var existing = sections
                .Where(section => known.Contains(section))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (existing.Length > 0)
                filtered[name] = existing;
        }

        return filtered;
    }

    private static void WriteCategoryNotFound(string name, IReadOnlyDictionary<string, string[]>? categories)
    {
        CommandError.Write($"Category '{name}' not found.");
        if (categories is not { Count: > 0 })
            return;

        CommandError.WriteBlankLine();
        CommandError.WriteLine("Available categories:");
        foreach (var category in categories.Keys.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            CommandError.WriteLine($"  {category}");
    }
}

[JsonSerializable(typeof(List<DiscoveryRow>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class DiscoveryJsonContext : JsonSerializerContext
{
}
