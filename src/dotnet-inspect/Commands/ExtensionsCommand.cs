using DotnetInspector.Packages;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Finds extension methods for a target type across packages, assemblies, and platform frameworks.
/// </summary>
public class ExtensionsCommand
{
    public static async Task<int> ExecuteAsync(string targetType, ExtensionsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var tempDirs = new List<string>();

        try
        {
            // Default to runtime framework if no scope specified
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to --framework runtime");
                options = options with { PlatformFrameworks = ["runtime"] };
            }

            var results = new List<ExtensionMethodResult>();

            // Collect all assembly paths from various sources
            var assemblyInfos = await AssemblyCollector.CollectAsync(context.HttpClient, options, tempDirs, logger, "inspect-ext");

            // Scan assemblies for extension methods
            foreach (var asmInfo in assemblyInfos)
            {
                var extensions = ScanForExtensions(asmInfo.Path, targetType, options.IncludeAll, logger);
                foreach (var ext in extensions)
                {
                    ext.Source = asmInfo.Source;
                    ext.SourceVersion = asmInfo.Version;
                }
                results.AddRange(extensions);
            }

            // If --reachable, find extensions on reachable types
            if (options.Reachable)
            {
                var assemblyPaths = assemblyInfos.Select(a => a.Path).ToList();
                var reachableTypes = ExtensionMethodScanner.FindReachableTypes(targetType, assemblyPaths, options.Depth);
                foreach (var (reachableType, path) in reachableTypes)
                {
                    if (reachableType == targetType) continue;

                    foreach (var asmInfo in assemblyInfos)
                    {
                        var extensions = ScanForExtensions(asmInfo.Path, reachableType, options.IncludeAll, logger);
                        foreach (var ext in extensions)
                        {
                            ext.Source = asmInfo.Source;
                            ext.SourceVersion = asmInfo.Version;
                            ext.ReachablePath = path;
                            ext.ReachableFromType = reachableType;
                        }
                        results.AddRange(extensions);
                    }
                }
            }

            // Apply limit
            if (options.Limit.HasValue && results.Count > options.Limit.Value)
            {
                results = results.Take(options.Limit.Value).ToList();
            }

            // Output results
            if (options.JsonOutput)
            {
                WriteJsonOutput(results, options.CompactJson);
            }
            else
            {
                WriteMarkoutOutput(targetType, results);
            }

            return 0;
        }
        finally
        {
            AssemblyCollector.CleanupTempDirs(tempDirs);
        }
    }

    private static List<ExtensionMethodResult> ScanForExtensions(
        string assemblyPath,
        string targetType,
        bool includeAll,
        VerboseLogger logger)
    {
        var results = new List<ExtensionMethodResult>();

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            foreach (var ext in ExtensionMethodScanner.FindExtensions(stream, targetType, includeAll))
            {
                results.Add(new ExtensionMethodResult
                {
                    MethodName = ext.MethodName,
                    ExtensionClass = ext.ExtensionClass,
                    ExtendedType = ext.ExtendedType,
                    Assembly = assemblyName,
                    Signature = ext.Signature,
                    Kind = ext.Kind
                });
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning {assemblyPath}: {ex.Message}");
        }

        return results;
    }

    private static void WriteJsonOutput(List<ExtensionMethodResult> results, bool compact)
    {
        var typeInfo = compact
            ? ExtensionsCompactJsonContext.Default.ListExtensionMethodResult
            : ExtensionsJsonContext.Default.ListExtensionMethodResult;
        Console.WriteLine(JsonSerializer.Serialize(results, typeInfo));
    }

    private static void WriteMarkoutOutput(string targetType, List<ExtensionMethodResult> results)
    {
        Console.WriteLine(ExtensionsOutputFormatter.FormatResults(targetType, results));
    }
}

/// <summary>
/// Result of extension method search.
/// </summary>
public class ExtensionMethodResult
{
    [JsonPropertyName("method")]
    public string MethodName { get; set; } = "";

    [JsonPropertyName("class")]
    public string ExtensionClass { get; set; } = "";

    [JsonPropertyName("extended_type")]
    public string ExtendedType { get; set; } = "";

    [JsonPropertyName("library")]
    public string Assembly { get; set; } = "";

    [JsonPropertyName("signature")]
    public string? Signature { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "method";

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }

    [JsonPropertyName("reachable_path")]
    public string? ReachablePath { get; set; }

    [JsonPropertyName("reachable_from_type")]
    public string? ReachableFromType { get; set; }
}

[JsonSerializable(typeof(List<ExtensionMethodResult>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class ExtensionsJsonContext : JsonSerializerContext { }

[JsonSerializable(typeof(List<ExtensionMethodResult>))]
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class ExtensionsCompactJsonContext : JsonSerializerContext { }
