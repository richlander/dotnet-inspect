using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Output;

/// <summary>
/// Handles -D/--discover output. Renders discovery results through the standard
/// Markout pipeline (oneline, markdown, json) instead of bespoke formatting.
/// </summary>
public static class DiscoverOutput
{
    /// <summary>
    /// Runs discovery and writes output.
    /// Bare -D lists sections. -D SectionName lists items within that section.
    /// </summary>
    public static int Execute(string[]? discover, DocumentSchema schema,
        bool tree = false, bool markdown = false, bool json = false)
    {
        if (tree)
            return WriteTree(discover, schema);

        var rows = GetDiscoveryRows(discover, schema);
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
            context.Serialize(view, Console.Out, new OneLineFormatter(showHeader: false));
        }

        return 0;
    }

    /// <summary>
    /// Runs discovery with effective filtering (only sections with data).
    /// </summary>
    public static int ExecuteEffective(string[]? discover, List<string> effectiveSections, DocumentSchema schema,
        bool tree = false, bool markdown = false, bool json = false)
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
        return Execute(discover, filtered, tree, markdown, json);
    }

    private static List<DiscoveryRow>? GetDiscoveryRows(string[]? discover, DocumentSchema schema)
    {
        // Bare -D: list sections
        if (discover is null or { Length: 0 })
        {
            var items = schema.Discover()!;
            return items.Select(i => new DiscoveryRow(i.Name, i.Kind)).ToList();
        }

        // -D SectionName: list items within section
        var rows = new List<DiscoveryRow>();
        foreach (var name in discover)
        {
            var resolved = schema.ResolveSection(name);
            if (resolved == null)
            {
                Console.Error.WriteLine($"Error: Section '{name}' not found.");
                var suggestions = schema.SectionNames
                    .Where(s => s.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (suggestions.Count > 0)
                {
                    Console.Error.WriteLine();
                    Console.Error.WriteLine("Did you mean:");
                    foreach (var s in suggestions)
                        Console.Error.WriteLine($"  {s}");
                }
                return null;
            }

            var items = schema.Discover(resolved);
            if (items != null)
            {
                foreach (var item in items)
                    rows.Add(new DiscoveryRow(item.Name, item.Kind));
            }
        }

        return rows;
    }

    private static int WriteTree(string[]? discover, DocumentSchema schema)
    {
        var nodes = new List<TreeNode>();

        if (discover is { Length: > 0 })
        {
            // Specific section: show items as top-level tree
            var rows = GetDiscoveryRows(discover, schema);
            if (rows == null) return 1;
            foreach (var row in rows)
                nodes.Add(new TreeNode($"{row.Name} ({row.Kind})"));
        }
        else
        {
            // Full tree: sections → items
            foreach (var sectionName in schema.SectionNames)
            {
                var children = new List<TreeNode>();
                var section = schema.GetSection(sectionName);
                if (section != null)
                {
                    foreach (var item in section.Items)
                        children.Add(new TreeNode($"{item.Name} ({item.Kind})"));
                }
                nodes.Add(new TreeNode(sectionName) { Children = children });
            }
        }

        var view = new DiscoveryTreeView { Sections = nodes };
        MarkoutSerializer.Serialize(view, Console.Out, DiscoveryContext.Default);
        return 0;
    }
}

[JsonSerializable(typeof(List<DiscoveryRow>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class DiscoveryJsonContext : JsonSerializerContext
{
}
