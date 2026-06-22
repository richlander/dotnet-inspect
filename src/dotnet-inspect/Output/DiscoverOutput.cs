using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Sections;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Output;

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
    public static int Execute(string[]? discover, DocumentSchema schema,
        bool tree = false, bool markdown = false, bool json = false, bool tsv = false, bool jsonl = false, int verbosity = 0,
        string? rootLabel = null, IReadOnlyDictionary<string, string>? sectionCostAnnotations = null,
        IReadOnlyDictionary<string, string[]>? sectionCategories = null)
    {
        sectionCategories = FilterCategories(sectionCategories, schema.SectionNames);

        // Auto-promote to tree when discovering items from multiple sections
        if (!tree
            && discover is { Length: > 0 }
            && !discover.Any(value => value.StartsWith("@", StringComparison.Ordinal))
            && ResolvedSectionCount(discover, schema, sectionCategories) > 1)
            tree = true;

        // Auto-promote bare -D to tree at Detailed verbosity (sections → items)
        if (!tree && discover is null or { Length: 0 } && verbosity >= 3)
            tree = true;

        if (tree)
            return WriteTree(discover, schema, rootLabel, sectionCostAnnotations, sectionCategories);

        var rows = GetDiscoveryRows(discover, schema, sectionCostAnnotations, sectionCategories);
        if (rows == null)
            return 1;

        var view = new DiscoveryListView { Items = rows };
        var context = new DiscoveryContext();

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(rows, DiscoveryJsonContext.Default.ListDiscoveryRow));
        }
        else if (markdown)
        {
            context.Serialize(view, Console.Out, new MarkdownFormatter());
        }
        else
        {
            OutputFormatter.WriteTable(Console.Out, showHeader: tsv,
                (writer, formatter) => context.Serialize(
                    view,
                    writer,
                    formatter,
                    OutputFormatter.CreateTableWriterOptions(tsv, jsonl)));
        }

        if (ShouldWriteAllSelectorHint(discover, json, tsv, jsonl, rows))
        {
            Console.WriteLine();
            Console.WriteLine($"Note: Use -S {SelectResolver.AllSelector} to select all sections.");
        }

        return 0;
    }

    private static bool ShouldWriteAllSelectorHint(string[]? discover, bool json, bool tsv, bool jsonl,
        IReadOnlyList<DiscoveryRow> rows)
        => !json
           && !tsv
           && !jsonl
           && discover is null or { Length: 0 }
           && rows.Any(row => row.Kind.Contains(SectionAnnotations.OptIn, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Runs discovery with effective filtering (only sections with data).
    /// </summary>
    public static int ExecuteEffective(string[]? discover, List<string> effectiveSections, DocumentSchema schema,
        bool tree = false, bool markdown = false, bool json = false, bool tsv = false, bool jsonl = false, int verbosity = 0,
        string? rootLabel = null, DocumentSchema? fullSchema = null,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations = null,
        IReadOnlyDictionary<string, string[]>? sectionCategories = null)
    {
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

        // For a specific section query, distinguish a valid section that simply has no data for
        // this input from a genuinely unknown section. The full schema lets us recognize the
        // former and report it clearly instead of the misleading "Section not found".
        if (discover is { Length: > 0 } && fullSchema != null)
        {
            var remaining = FilterEmptyEffectiveSections(discover, filtered, fullSchema);
            if (remaining == null)
                return 0;
            discover = remaining;
        }

        return Execute(discover, filtered, tree, markdown, json, tsv, jsonl, verbosity, rootLabel, sectionCostAnnotations, sectionCategories);
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
        string[] discover, DocumentSchema effective, DocumentSchema fullSchema)
    {
        var remaining = new List<string>();
        bool emittedNote = false;
        foreach (var name in discover)
        {
            if (name.StartsWith("@", StringComparison.Ordinal))
            {
                remaining.Add(name);
                continue;
            }

            var (effMatches, _) = SelectResolver.ResolveSingle(name, effective.SectionNames, singleGlob: true);
            if (effMatches.Count >= 1)
            {
                remaining.Add(name);
                continue;
            }

            var (fullMatches, _) = SelectResolver.ResolveSingle(name, fullSchema.SectionNames, singleGlob: true);
            if (fullMatches.Count >= 1)
            {
                foreach (var match in fullMatches)
                    Console.Error.WriteLine($"note: section '{match}' has no data for this query");
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
    /// Original Source, Recovered IL) as renderable whenever the type has methods, but these
    /// are member-detail sections produced only for a specific member selection — they are
    /// not part of the type schema. Dropping them keeps effective discovery consistent
    /// with <c>-D</c> and ensures every listed section is queryable via <c>-D &lt;Section&gt;</c>.
    /// </summary>
    public static List<string> RestrictToSchemaSections(List<string> effectiveSections, DocumentSchema schema)
        => effectiveSections.Where(s => schema.GetSection(s) != null).ToList();

    /// <summary>
    /// Restricts effective sections to those that actually produced rendered output in the
    /// supplied markdown rendering of the same view. A tabular schema section whose
    /// <c>CanRender</c> probe passed but which rendered no table for this input is dropped,
    /// since it has no data to query. This catches sections whose <c>CanRender</c> is a coarse
    /// proxy — e.g. "Custom Attributes", gated on "the type has methods" but only populated
    /// when a specific member's attributes are read (the member-detail path) — so
    /// effective discovery reflects real data rather than mere potential.
    /// Only tabular sections (those with schema columns) are subject to the drop; non-tabular
    /// sections are left untouched because their content may not render as a markdown table.
    /// This measures renderability in the current type effective-discovery render path
    /// (RenderTypeSectionsMarkdown); a section populated only in another command path is
    /// intentionally treated as having no data here.
    /// </summary>
    public static List<string> RestrictToRenderedSections(
        List<string> effectiveSections, DocumentSchema schema, string rendered)
    {
        var renderedTables = ParseSectionHeaders(rendered);
        return effectiveSections.Where(name =>
        {
            var section = schema.GetSection(name);
            bool tabular = section is { Items.Length: > 0 };
            return !tabular || renderedTables.ContainsKey(name);
        }).ToList();
    }

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
    /// markdown rendering of the same view. Used by effective discovery so the reported
    /// columns match what the user would see at their current verbosity/options (e.g. summary
    /// columns replace detailed columns at Minimal verbosity).
    /// Matches against section-scoped markdown table headers, not the rendered body, to
    /// avoid false positives from column names appearing in member names or signatures.
    /// </summary>
    public static DocumentSchema FilterSchemaToRenderedHeaders(
        List<string> effectiveSections, DocumentSchema schema, string rendered)
    {
        var headersBySection = ParseSectionHeaders(rendered);
        var filtered = new DocumentSchema();
        foreach (var name in effectiveSections)
        {
            var section = schema.GetSection(name);
            if (section == null) { filtered.AddSection(name); continue; }

            // No table rendered for this section (e.g. a non-tabular section such as Source/IL,
            // or one not produced by the member renderer): preserve the original schema columns
            // rather than stripping them — we only narrow columns for sections we actually rendered.
            if (!headersBySection.TryGetValue(name, out var headerCells))
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
    /// Parses a rendered markdown document into a map of section heading text → the set of
    /// column header cells from the first table under that heading.
    /// </summary>
    private static Dictionary<string, HashSet<string>> ParseSectionHeaders(string rendered)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var lines = rendered.Split('\n');
        string? current = null;
        for (int i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('#'))
            {
                current = trimmed.TrimStart('#').Trim();
                continue;
            }

            // A table header is a pipe row immediately followed by a separator row.
            if (current != null && !result.ContainsKey(current)
                && trimmed.StartsWith('|') && i + 1 < lines.Length
                && IsSeparatorRow(lines[i + 1]))
            {
                result[current] = new HashSet<string>(ParseRowCells(trimmed), StringComparer.OrdinalIgnoreCase);
            }
        }
        return result;
    }

    private static bool IsSeparatorRow(string line)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith('|'))
            return false;
        return trimmed.All(c => c is '|' or '-' or ':' or ' ');
    }

    private static IEnumerable<string> ParseRowCells(string row)
        => row.Trim().Trim('|').Split('|').Select(c => c.Trim()).Where(c => c.Length > 0);

    private static List<DiscoveryRow>? GetDiscoveryRows(
        string[]? discover,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations = null,
        IReadOnlyDictionary<string, string[]>? sectionCategories = null)
    {
        // Bare -D: regular sections, @categories, opt-in sections, then sections
        // that are structurally available but may be empty. Each group is alpha sorted.
        if (discover is null or { Length: 0 })
        {
            var items = schema.Discover()!;
            var categoryRows = sectionCategories?
                .Select(category => new DiscoveryRow(category.Key, "category"))
                .ToList() ?? new List<DiscoveryRow>();

            var sectionRows = items
                .Select(i => new DiscoveryRow(i.Name, AnnotateKind(i.Kind, i.Name, sectionCostAnnotations)))
                .ToList();

            return sectionRows.Concat(categoryRows)
                .OrderBy(GetDiscoveryRowSortRank)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // -D SectionName: list items within section
        var rows = new List<DiscoveryRow>();
        foreach (var name in discover)
        {
            if (TryResolveCategory(name, sectionCategories, out var categorySections))
            {
                foreach (var sectionName in categorySections)
                    rows.Add(new DiscoveryRow(sectionName, AnnotateKind("section", sectionName, sectionCostAnnotations)));
                continue;
            }

            if (name.StartsWith("@", StringComparison.Ordinal))
            {
                WriteCategoryNotFound(name, sectionCategories);
                return null;
            }

            var resolved = ResolveDiscoverSection(name, schema);
            if (resolved == null)
                return null;

            var items = schema.Discover(resolved);
            if (items != null)
            {
                foreach (var item in items)
                    rows.Add(new DiscoveryRow(item.Name, item.Kind));
            }
        }

        return rows;
    }

    private static int ResolvedSectionCount(
        string[] discover,
        DocumentSchema schema,
        IReadOnlyDictionary<string, string[]>? sectionCategories)
    {
        int count = 0;
        foreach (var name in discover)
        {
            if (TryResolveCategory(name, sectionCategories, out var categorySections))
            {
                count += categorySections.Length;
                continue;
            }

            var (matches, _) = SelectResolver.ResolveSingle(name, schema.SectionNames, singleGlob: true);
            if (matches.Count == 1)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Resolves a section name for discovery. Supports exact match (case-insensitive)
    /// and glob patterns (* / ?). Globs must match exactly one section.
    /// </summary>
    private static string? ResolveDiscoverSection(string name, DocumentSchema schema)
    {
        var (matches, miss) = SelectResolver.ResolveSingle(name, schema.SectionNames, singleGlob: true);

        if (miss == null && matches.Count == 1)
            return matches[0];

        // Multi-glob match: the miss contains the matches as suggestions
        if (miss != null && miss.IsGlob && matches.Count > 1)
        {
            Console.Error.WriteLine($"Error: '{name}' matches {matches.Count} sections: {string.Join(", ", matches)}.");
            Console.Error.WriteLine("Discovery requires exactly one section. Be more specific.");
            return null;
        }

        // No match — write error with suggestions
        if (miss != null)
        {
            Console.Error.WriteLine($"Error: Section '{miss.Value}' not found.");
            if (miss.Suggestions.Count > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine("Did you mean:");
                foreach (var s in miss.Suggestions)
                    Console.Error.WriteLine($"  {s}");
            }
        }

        return null;
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
            return 1;
        if (row.Kind.Contains(SectionAnnotations.OptIn, StringComparison.OrdinalIgnoreCase))
            return 2;
        if (row.Kind.Contains(SectionAnnotations.MayBeEmpty, StringComparison.OrdinalIgnoreCase))
            return 3;
        return 0;
    }

    private static int WriteTree(string[]? discover, DocumentSchema schema, string? rootLabel = null,
        IReadOnlyDictionary<string, string>? sectionCostAnnotations = null,
        IReadOnlyDictionary<string, string[]>? sectionCategories = null)
    {
        var nodes = new List<TreeNode>();

        if (discover is { Length: > 0 })
        {
            // Resolve each section and build grouped tree
            foreach (var name in discover)
            {
                if (TryResolveCategory(name, sectionCategories, out var categorySections))
                {
                    nodes.Add(new TreeNode(name)
                    {
                        Children = categorySections.Select(section => new TreeNode(section)).ToList()
                    });
                    continue;
                }

                var resolved = ResolveDiscoverSection(name, schema);
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
        else
        {
            var sectionRows = schema.SectionNames
                .Select(name => new DiscoveryRow(name, AnnotateKind("section", name, sectionCostAnnotations)))
                .ToList();
            var categoryRows = sectionCategories?
                .Keys
                .Select(name => new DiscoveryRow(name, "category"))
                .ToList() ?? new List<DiscoveryRow>();

            // Full tree: regular sections, @categories, opt-in sections, then
            // structurally listed sections that may be empty. Each group is alpha sorted.
            var orderedRows = sectionRows.Concat(categoryRows)
                .OrderBy(GetDiscoveryRowSortRank)
                .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase);
            foreach (var row in orderedRows)
            {
                if (row.Kind.Equals("category", StringComparison.OrdinalIgnoreCase))
                {
                    nodes.Add(new TreeNode($"{row.Name} (category)")
                    {
                        Children = sectionCategories![row.Name].Select(section => new TreeNode(section)).ToList()
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

        // Wrap in root node when label is provided and showing full tree
        if (rootLabel != null && discover is null or { Length: 0 })
            nodes = [new TreeNode(rootLabel) { Children = nodes }];

        var view = new DiscoveryTreeView { Sections = nodes };
        MarkoutSerializer.Serialize(view, Console.Out, DiscoveryContext.Default);
        return 0;
    }

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

    private static bool TryResolveCategory(
        string name,
        IReadOnlyDictionary<string, string[]>? categories,
        out string[] sections)
    {
        sections = [];
        if (categories == null)
            return false;

        if (!categories.TryGetValue(name, out var value))
            return false;

        sections = value;
        return true;
    }

    private static void WriteCategoryNotFound(string name, IReadOnlyDictionary<string, string[]>? categories)
    {
        Console.Error.WriteLine($"Error: Category '{name}' not found.");
        if (categories is not { Count: > 0 })
            return;

        Console.Error.WriteLine();
        Console.Error.WriteLine("Available categories:");
        foreach (var category in categories.Keys.OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            Console.Error.WriteLine($"  {category}");
    }
}

[JsonSerializable(typeof(List<DiscoveryRow>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class DiscoveryJsonContext : JsonSerializerContext
{
}
