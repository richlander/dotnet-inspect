using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Metadata;
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
        // Auto-promote to tree when discovering items from multiple sections
        if (!tree && discover is { Length: > 0 } && ResolvedSectionCount(discover, schema) > 1)
            tree = true;

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

    private static int ResolvedSectionCount(string[] discover, DocumentSchema schema)
    {
        int count = 0;
        foreach (var name in discover)
        {
            if (schema.ResolveSection(name) != null)
                count++;
            else if ((name.Contains('*') || name.Contains('?')) &&
                     schema.SectionNames.Any(s => TypeMatcher.MatchesGlob(s, name)))
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
        // Try exact match first
        var resolved = schema.ResolveSection(name);
        if (resolved != null)
            return resolved;

        // Try glob match
        if (name.Contains('*') || name.Contains('?'))
        {
            var matches = schema.SectionNames
                .Where(s => TypeMatcher.MatchesGlob(s, name))
                .ToList();

            if (matches.Count == 1)
                return matches[0];

            if (matches.Count > 1)
            {
                Console.Error.WriteLine($"Error: '{name}' matches {matches.Count} sections: {string.Join(", ", matches)}.");
                Console.Error.WriteLine("Discovery requires exactly one section. Be more specific.");
                return null;
            }
        }

        // No match — show suggestions
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

    private static int WriteTree(string[]? discover, DocumentSchema schema)
    {
        var nodes = new List<TreeNode>();

        if (discover is { Length: > 0 })
        {
            // Resolve each section and build grouped tree
            foreach (var name in discover)
            {
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
