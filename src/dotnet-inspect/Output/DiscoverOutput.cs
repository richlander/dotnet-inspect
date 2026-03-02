using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Sections;
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
    public static int Execute(string[]? discover, string[] allSectionNames, SectionSchemaMap schemaMap,
        bool tree = false, bool markdown = false, bool json = false)
    {
        if (tree)
            return WriteTree(discover, allSectionNames, schemaMap);

        var rows = GetDiscoveryRows(discover, allSectionNames, schemaMap);
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
    public static int ExecuteEffective(string[]? discover, List<string> effectiveSections, SectionSchemaMap schemaMap,
        bool tree = false, bool markdown = false, bool json = false)
    {
        return Execute(discover, effectiveSections.ToArray(), schemaMap, tree, markdown, json);
    }

    private static List<DiscoveryRow>? GetDiscoveryRows(string[]? discover, string[] allSectionNames, SectionSchemaMap schemaMap)
    {
        // Bare -D: list sections
        if (discover is null or { Length: 0 })
        {
            return allSectionNames.Select(n => new DiscoveryRow(n, "section")).ToList();
        }

        // -D SectionName: list items within section
        var rows = new List<DiscoveryRow>();
        foreach (var name in discover)
        {
            var match = allSectionNames.FirstOrDefault(s =>
                s.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (match == null)
            {
                Console.Error.WriteLine($"Error: Section '{name}' not found.");
                var suggestions = allSectionNames
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

            var schema = schemaMap.GetSchema(match);
            if (schema != null)
            {
                foreach (var itemName in schema.ItemNames)
                    rows.Add(new DiscoveryRow(itemName, schema.ItemKind));
            }
        }

        return rows;
    }

    private static int WriteTree(string[]? discover, string[] allSectionNames, SectionSchemaMap schemaMap)
    {
        var nodes = new List<TreeNode>();

        if (discover is { Length: > 0 })
        {
            // Specific section: show items as top-level tree
            var rows = GetDiscoveryRows(discover, allSectionNames, schemaMap);
            if (rows == null) return 1;
            foreach (var row in rows)
                nodes.Add(new TreeNode($"{row.Name} ({row.Kind})"));
        }
        else
        {
            // Full tree: sections → items
            foreach (var sectionName in allSectionNames)
            {
                var children = new List<TreeNode>();
                var schema = schemaMap.GetSchema(sectionName);
                if (schema != null)
                {
                    foreach (var itemName in schema.ItemNames)
                        children.Add(new TreeNode($"{itemName} ({schema.ItemKind})"));
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
