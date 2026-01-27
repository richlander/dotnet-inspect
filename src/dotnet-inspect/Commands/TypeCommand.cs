using System.Text.Json;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Displays type shape including hierarchy, interfaces, and members in tree format.
/// </summary>
public class TypeCommand
{
    public static async Task<int> ExecuteAsync(string typeName, TypeOptions options)
    {
        // Leverage ApiCommand's infrastructure for package extraction
        var apiOptions = new ApiOptions
        {
            PackagePath = options.PackagePath,
            AssemblyPath = options.AssemblyPath,
            Tfm = options.Tfm,
            IncludeAll = options.IncludeAll,
            Verbose = options.Verbose,
            JsonOutput = options.JsonOutput,
            CompactJson = options.CompactJson
        };

        var logger = new VerboseLogger(options.Verbose);
        
        // Use ApiCommand's extraction to get the type
        (ApiType? type, string? _) = await ApiCommand.ExtractTypeAsync(typeName, apiOptions, logger);
        
        if (type == null)
        {
            Console.Error.WriteLine($"Error: Type '{typeName}' not found.");
            return 1;
        }

        // Output
        if (options.JsonOutput)
        {
            WriteJsonOutput(type, options.CompactJson);
        }
        else
        {
            WriteTreeOutput(type);
        }

        return 0;
    }

    private static void WriteTreeOutput(ApiType type)
    {
        var writer = new MarkoutWriter(Console.Out);
        
        // Header
        writer.WriteHeading(2, type.Namespace != null ? $"{type.Namespace}.{type.Name}" : type.Name);
        Console.WriteLine($"*{type.Kind}*");
        Console.WriteLine();

        // Build tree
        var nodes = new List<TreeNode>();

        // Inheritance
        if (!string.IsNullOrEmpty(type.BaseType) && type.BaseType != "Object")
        {
            nodes.Add(new TreeNode("Inherits", new[] { type.BaseType }));
        }

        // Interfaces
        if (type.Interfaces is { Count: > 0 })
        {
            nodes.Add(new TreeNode("Implements", type.Interfaces));
        }

        // Group members by kind
        if (type.Members is { Count: > 0 })
        {
            var membersByKind = type.Members
                .Where(m => !IsCompilerGenerated(m.Name))
                .GroupBy(m => m.Kind)
                .OrderBy(g => GetKindOrder(g.Key));

            foreach (var group in membersByKind)
            {
                var kindLabel = GetKindLabel(group.Key, group.Count());
                var memberSignatures = group
                    .OrderBy(m => m.Name)
                    .Select(m => m.Signature ?? m.Name)
                    .ToList();
                
                nodes.Add(new TreeNode(kindLabel, memberSignatures));
            }
        }

        writer.WriteTree(nodes);
    }

    private static void WriteJsonOutput(ApiType type, bool compact)
    {
        if (compact)
        {
            Console.WriteLine(JsonSerializer.Serialize(type, ApiTypeCompactJsonContext.Default.ApiType));
        }
        else
        {
            Console.WriteLine(JsonSerializer.Serialize(type, ApiTypeJsonContext.Default.ApiType));
        }
    }

    private static bool IsCompilerGenerated(string name)
    {
        return name.StartsWith('<') ||
               name.StartsWith("__") ||
               name.StartsWith("s_") ||
               name.Contains("__BackingField");
    }

    private static int GetKindOrder(string kind) => kind switch
    {
        "constructor" => 0,
        "property" => 1,
        "method" => 2,
        "event" => 3,
        "field" => 4,
        _ => 5
    };

    private static string GetKindLabel(string kind, int count)
    {
        var plural = kind switch
        {
            "property" => "Properties",
            "method" => "Methods",
            "constructor" => "Constructors",
            "event" => "Events",
            "field" => "Fields",
            _ => kind + "s"
        };
        return $"{plural} ({count})";
    }
}

public record TypeOptions
{
    public string? PackagePath { get; init; }
    public string? AssemblyPath { get; init; }
    public string? Tfm { get; init; }
    public bool JsonOutput { get; init; }
    public bool CompactJson { get; init; }
    public bool Verbose { get; init; }
    public bool IncludeAll { get; init; }
}
