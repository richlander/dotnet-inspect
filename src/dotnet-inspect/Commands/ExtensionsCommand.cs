using DotnetInspector.Packages;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using ILInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Views;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Finds extension methods for a target type across packages, assemblies, and platform frameworks.
/// </summary>
public class ExtensionsCommand
{
    public static async Task<int> ExecuteAsync(ExtensionsOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var targetType = options.TargetType;

        try
        {
            // Safety fallback — default to all platform frameworks
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to all platform frameworks");
                options = options with
                {
                    PlatformFrameworks = CommandLineBuilder.PlatformFrameworkNames
                };
            }

            List<ExtensionMethodResult> results;
            if (options.Reachable)
            {
                results = await ScanReachableExtensionsAsync(options, context, logger, targetType);
            }
            else
            {
                results = await AssemblyCollector.ScanAsync(
                    context.HttpClient,
                    options,
                    logger,
                    "inspect-ext",
                    assemblyInfo => ScanForExtensions(assemblyInfo.Path, targetType, options.IncludeAll, logger),
                    (result, assemblyInfo) =>
                    {
                        result.Source = assemblyInfo.Source;
                        result.SourceVersion = assemblyInfo.Version;
                    });
            }

            // Apply limit
            if (options.Limit.HasValue && results.Count > options.Limit.Value)
            {
                results = results.Take(options.Limit.Value).ToList();
            }

            // Collapse overloads into single entries
            results = CollapseOverloads(results);

            if (results.Count == 0)
                NamespacePrefixHints.WriteIfLikelyNamespacePrefix(targetType);

            // Output results
            if (options.JsonOutput)
            {
                WriteJsonOutput(results, options.CompactJson);
            }
            else if (options.Count)
            {
                WriteCount(results);
            }
            else
            {
                WriteMarkoutOutput(targetType, results, options.Verbosity, options.Rows);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<List<ExtensionMethodResult>> ScanReachableExtensionsAsync(
        ExtensionsOptions options,
        CommandContext context,
        VerboseLogger logger,
        string targetType)
    {
        return await AssemblyCollector.WithAssembliesAsync(
            context.HttpClient,
            options,
            logger,
            "inspect-ext",
            assemblyInfos =>
            {
                List<ExtensionMethodResult> results = [];

                void Stamp(ExtensionMethodResult ext, AssemblyCollector.AssemblyInfo asmInfo)
                {
                    ext.Source = asmInfo.Source;
                    ext.SourceVersion = asmInfo.Version;
                }

                foreach (var asmInfo in assemblyInfos)
                {
                    var extensions = ScanForExtensions(asmInfo.Path, targetType, options.IncludeAll, logger);
                    foreach (var ext in extensions)
                    {
                        Stamp(ext, asmInfo);
                        results.Add(ext);
                    }
                }

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
                            Stamp(ext, asmInfo);
                            ext.ReachablePath = path;
                            ext.ReachableFromType = reachableType;
                            results.Add(ext);
                        }
                    }
                }

                return results;
            });
    }

    private static List<ExtensionMethodResult> ScanForExtensions(
        string assemblyPath,
        string targetType,
        bool includeAll,
        VerboseLogger logger)
    {
        List<ExtensionMethodResult> results = [];

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
        JsonOutputHelper.Write(results, ExtensionsJsonContext.Default.ListExtensionMethodResult, ExtensionsCompactJsonContext.Default.ListExtensionMethodResult, compact);
    }

    private static void WriteCount(List<ExtensionMethodResult> results)
    {
        Console.WriteLine(results.Count);
    }

    private static void WriteMarkoutOutput(string targetType, List<ExtensionMethodResult> results, Verbosity verbosity, int? rows)
    {
        var view = ExtensionsOutputFormatter.BuildView(targetType, results, verbosity);
        OutputFormatter.WriteLimitedMarkdown(Console.Out,
            MarkoutSerializer.Serialize(view, SearchViewContext.Default), rows);
    }

    /// <summary>
    /// Collapses method overloads into a single result with an overload count and signatures list.
    /// </summary>
    internal static List<ExtensionMethodResult> CollapseOverloads(List<ExtensionMethodResult> results)
    {
        return results
            .GroupBy(r => (r.MethodName, r.Kind, r.ExtensionClass, r.Assembly, r.Source, r.SourceVersion, r.ReachablePath, r.ReachableFromType))
            .Select(g =>
            {
                var first = g.First();
                var signatures = g.Select(r => r.Signature).Where(s => s != null).Distinct().Cast<string>().ToList();
                var count = g.Count();
                return first with
                {
                    Overloads = count > 1 ? count : null,
                    Signatures = signatures.Count > 1 ? signatures : null,
                    Signature = signatures.Count == 1 ? signatures[0] : null
                };
            })
            .ToList();
    }
}

/// <summary>
/// Result of extension method search.
/// </summary>
public record class ExtensionMethodResult
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

    [JsonPropertyName("signatures")]
    public List<string>? Signatures { get; set; }

    [JsonPropertyName("overloads")]
    public int? Overloads { get; set; }

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
