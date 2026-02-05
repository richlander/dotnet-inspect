using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Finds types that implement an interface or extend a base class.
/// </summary>
public class ImplementsCommand
{
    public static async Task<int> ExecuteAsync(string targetType, ImplementsOptions options)
    {
        var logger = new VerboseLogger(options.Verbose);
        var tempDirs = new List<string>();

        try
        {
            // Default to runtime framework if no scope specified
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to --framework runtime");
                options = options with { PlatformFrameworks = ["runtime"] };
            }

            var results = new List<ImplementerResult>();
            var assemblyPaths = new List<(string path, string source, string? version)>();

            // Collect all assembly paths from various sources
            await CollectAssemblyPathsAsync(options, assemblyPaths, tempDirs, logger);

            logger.Log($"Scanning {assemblyPaths.Count} assemblies for types implementing {targetType}");

            // Scan assemblies for implementers
            foreach (var (asmPath, source, version) in assemblyPaths)
            {
                var implementers = ScanForImplementers(asmPath, targetType, options.IncludeAll, logger);
                foreach (var impl in implementers)
                {
                    impl.Source = source;
                    impl.SourceVersion = version;
                }
                results.AddRange(implementers);
            }

            // Deduplicate by type name + source (same type from multiple TFM folders)
            results = results
                .GroupBy(r => (r.TypeName, r.Source))
                .Select(g => g.First())
                .ToList();

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
            CleanupTempDirs(tempDirs);
        }
    }

    private static async Task CollectAssemblyPathsAsync(
        ImplementsOptions options,
        List<(string path, string source, string? version)> assemblyPaths,
        List<string> tempDirs,
        VerboseLogger logger)
    {
        // 1. Packages
        foreach (var pkg in options.Packages)
        {
            var extracted = await PackageExtractor.ExtractPackageAsync(pkg, logger, "inspect-impl");
            if (extracted == null)
            {
                Console.Error.WriteLine($"Warning: Could not extract package '{pkg}', skipping.");
                continue;
            }

            if (extracted.TempDir != null) tempDirs.Add(extracted.TempDir);

            // Use TfmResolver to select TFM (specific or auto-select highest)
            var searchPath = TfmResolver.ResolvePackagePath(extracted.ExtractPath, options.Tfm) 
                ?? extracted.ExtractPath;

            // Find all DLLs in the resolved path
            var dlls = Directory.GetFiles(searchPath, "*.dll", SearchOption.AllDirectories)
                .Where(p => !p.Contains("/runtimes/") && !p.Contains("\\runtimes\\"));

            foreach (var dll in dlls)
            {
                assemblyPaths.Add((dll, extracted.PackageName ?? pkg, extracted.Version));
            }
        }

        // 2. Direct assemblies
        foreach (var asmPath in options.Assemblies)
        {
            if (!File.Exists(asmPath))
            {
                Console.Error.WriteLine($"Warning: Assembly not found '{asmPath}', skipping.");
                continue;
            }
            assemblyPaths.Add((asmPath, Path.GetFileName(asmPath), null));
        }

        // 3. Platform assemblies
        foreach (var platformAsm in options.PlatformAssemblies)
        {
            var (assemblyPath, version, resolvedFramework, error) = PlatformResolver.ResolveAssembly(platformAsm);
            if (error != null)
            {
                Console.Error.WriteLine($"Warning: {error}, skipping.");
                continue;
            }
            assemblyPaths.Add((assemblyPath!, resolvedFramework ?? "platform", version));
        }

        // 4. Platform frameworks
        foreach (var framework in options.PlatformFrameworks)
        {
            var (refPath, resolvedVersion, error) = PlatformResolver.ResolveFramework(framework);
            if (error != null)
            {
                Console.Error.WriteLine($"Warning: {error}, skipping.");
                continue;
            }

            var frameworkAssemblies = PlatformResolver.GetAssemblies(refPath!);
            logger.Log($"Scanning {frameworkAssemblies.Count} assemblies in {framework}@{resolvedVersion}");

            foreach (var asmInfo in frameworkAssemblies)
            {
                assemblyPaths.Add((asmInfo.Path, framework, resolvedVersion));
            }
        }
    }

    private static List<ImplementerResult> ScanForImplementers(
        string assemblyPath,
        string targetType,
        bool includeAll,
        VerboseLogger logger)
    {
        var results = new List<ImplementerResult>();

        try
        {
            using var stream = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata)
                return results;

            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            foreach (var relationship in TypeHierarchyScanner.FindImplementers(peReader, targetType, includeAll))
            {
                results.Add(new ImplementerResult
                {
                    TypeName = relationship.TypeName,
                    Namespace = relationship.Namespace,
                    Kind = relationship.Kind,
                    Relationship = relationship.RelationshipKind.ToString().ToLowerInvariant(),
                    Assembly = assemblyName
                });
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Error scanning {assemblyPath}: {ex.Message}");
        }

        return results;
    }

    private static void WriteJsonOutput(List<ImplementerResult> results, bool compact)
    {
        var typeInfo = compact
            ? ImplementsCompactJsonContext.Default.ListImplementerResult
            : ImplementsJsonContext.Default.ListImplementerResult;
        Console.WriteLine(JsonSerializer.Serialize(results, typeInfo));
    }

    private static void WriteMarkoutOutput(string targetType, List<ImplementerResult> results)
    {
        Console.WriteLine($"# Types Implementing {targetType}");
        Console.WriteLine();

        if (results.Count == 0)
        {
            Console.WriteLine("No implementing types found.");
            return;
        }

        Console.WriteLine($"**Matches:** {results.Count}");
        Console.WriteLine();

        // Group by source
        var bySource = results.GroupBy(r => r.Source).ToList();

        foreach (var sourceGroup in bySource)
        {
            var source = sourceGroup.Key;
            var version = sourceGroup.First().SourceVersion;
            var sourceDisplay = version != null ? $"{source}@{version}" : source;

            Console.WriteLine($"## {sourceDisplay}");
            Console.WriteLine();
            Console.WriteLine("| Type | Kind | Relationship | Assembly |");
            Console.WriteLine("|------|------|--------------|----------|");

            foreach (var impl in sourceGroup.OrderBy(r => r.TypeName))
            {
                Console.WriteLine($"| {impl.TypeName} | {impl.Kind} | {impl.Relationship} | {impl.Assembly} |");
            }

            Console.WriteLine();
        }
    }

    private static void CleanupTempDirs(List<string> tempDirs)
    {
        foreach (var dir in tempDirs)
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}

/// <summary>
/// Result of implementer search.
/// </summary>
public class ImplementerResult
{
    [JsonPropertyName("type")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("relationship")]
    public string Relationship { get; set; } = "";

    [JsonPropertyName("assembly")]
    public string? Assembly { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }
}

[JsonSerializable(typeof(List<ImplementerResult>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class ImplementsJsonContext : JsonSerializerContext { }

[JsonSerializable(typeof(List<ImplementerResult>))]
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
internal partial class ImplementsCompactJsonContext : JsonSerializerContext { }
