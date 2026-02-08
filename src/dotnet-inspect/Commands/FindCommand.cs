using DotnetInspector.Packages;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using Markout;

namespace DotnetInspector.Commands;

/// <summary>
/// Searches for types across packages, assemblies, and platform frameworks.
/// </summary>
public class FindCommand
{
    public static async Task<int> ExecuteAsync(string patternInput, FindOptions options)
    {
        var context = new CommandContext(options.Verbose);
        var logger = context.Logger;
        var tempDirs = new List<string>();

        try
        {
            // Split comma-separated patterns
            var patterns = patternInput.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (patterns.Length == 0)
            {
                Console.Error.WriteLine("Error: No pattern specified.");
                return 1;
            }

            // For oneline/name-only/multi-pattern mode, collect results per pattern
            if (options.OneLine || options.NameOnly || patterns.Length > 1)
            {
                return await ExecuteMultiPatternAsync(patterns, options, logger, tempDirs, context.HttpClient);
            }

            // Single pattern - use original logic
            return await ExecuteSinglePatternAsync(patterns[0], options, logger, tempDirs, context.HttpClient);
        }
        finally
        {
            AssemblyCollector.CleanupTempDirs(tempDirs);
        }
    }

    private static async Task<int> ExecuteMultiPatternAsync(string[] patterns, FindOptions options, VerboseLogger logger, List<string> tempDirs, HttpClient httpClient)
    {
        // Default to runtime framework if no scope specified
        if (!options.HasAnyScope)
        {
            logger.Log("No scope specified, defaulting to --framework runtime");
            options = options with { PlatformFrameworks = ["runtime"] };
        }

        // Collect all types first (without pattern filtering)
        var allTypes = await CollectAllTypesAsync(options, logger, tempDirs, httpClient);

        // Match types against each pattern
        var resultsByPattern = new Dictionary<string, List<TypeSearchResult>>();
        foreach (var pattern in patterns)
        {
            var matches = new List<TypeSearchResult>();
            foreach (var type in allTypes)
            {
                if (TypeMatcher.MatchesGlob(type.FullName, pattern) || TypeMatcher.MatchesGlob(type.TypeName, pattern))
                {
                    matches.Add(type);
                }
            }

            // Apply limit per pattern
            if (options.Limit.HasValue && matches.Count > options.Limit.Value)
            {
                matches = matches.Take(options.Limit.Value).ToList();
            }

            resultsByPattern[pattern] = matches;
        }

        // Output results
        if (options.JsonOutput)
        {
            var allResults = resultsByPattern.Values.SelectMany(r => r).Distinct().ToList();
            WriteJsonOutput(allResults, options.CompactJson);
        }
        else if (options.NameOnly)
        {
            WriteNameOnlyOutput(resultsByPattern);
        }
        else if (options.OneLine)
        {
            WriteOneLineOutput(resultsByPattern, options.Grouped);
        }
        else
        {
            WriteMultiPatternOutput(resultsByPattern, options.Limit);
        }

        return 0;
    }

    private static void WriteNameOnlyOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern)
    {
        Console.WriteLine(FormatNameOnlyOutput(resultsByPattern));
    }

    private static async Task<int> ExecuteSinglePatternAsync(string pattern, FindOptions options, VerboseLogger logger, List<string> tempDirs, HttpClient httpClient)
    {
            // Default to runtime framework if no scope specified
            if (!options.HasAnyScope)
            {
                logger.Log("No scope specified, defaulting to --framework runtime");
                options = options with { PlatformFrameworks = ["runtime"] };
            }

            var results = new List<TypeSearchResult>();

            // Helper to add results (no deduplication - show all sources)
            void AddResults(IEnumerable<TypeSearchResult> types)
            {
                results.AddRange(types);
            }

            // Check if we've hit the limit
            bool ReachedLimit() => options.Limit.HasValue && results.Count >= options.Limit.Value;

            // 1. Search packages
            foreach (var pkg in options.Packages)
            {
                if (ReachedLimit()) break;

                var extracted = await PackageExtractor.ExtractPackageAsync(httpClient, pkg, logger.Log, "inspect-find");
                if (extracted == null)
                {
                    Console.Error.WriteLine($"Warning: Could not extract package '{pkg}', skipping.");
                    continue;
                }

                var (searchPath, tempDir, packageName, packageVersion) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);
                if (tempDir != null) tempDirs.Add(tempDir);

                // Use TfmResolver to select TFM (specific or auto-select highest)
                var tfmPath = TfmResolver.ResolvePackagePath(searchPath, options.Tfm);
                if (tfmPath != null) searchPath = tfmPath;

                var types = SearchAssemblyOrDirectory(searchPath, pattern, options.IncludeAll, logger);
                foreach (var t in types)
                {
                    t.Source = packageName ?? pkg;
                    t.SourceVersion = packageVersion;
                }
                AddResults(types);
            }

            // 2. Search assemblies
            foreach (var asmPath in options.Assemblies)
            {
                if (ReachedLimit()) break;

                if (!File.Exists(asmPath))
                {
                    Console.Error.WriteLine($"Warning: Assembly not found '{asmPath}', skipping.");
                    continue;
                }

                var types = SearchAssembly(asmPath, pattern, options.IncludeAll, logger);
                foreach (var t in types)
                {
                    t.Source = Path.GetFileName(asmPath);
                }
                AddResults(types);
            }

            // 3. Search platform assemblies
            foreach (var platformAsm in options.PlatformAssemblies)
            {
                if (ReachedLimit()) break;

                // Use the first framework specified, or null
                var framework = options.PlatformFrameworks.Length > 0 ? options.PlatformFrameworks[0] : null;
                var (assemblyPath, resolvedFramework, version, error) = PlatformResolver.ResolveAssembly(
                    platformAsm, framework, packsDirectory: null, useRuntimeAssemblies: false);

                if (error != null)
                {
                    Console.Error.WriteLine($"Warning: {error}, skipping.");
                    continue;
                }

                logger.Log($"Searching platform assembly: {platformAsm} ({resolvedFramework} {version})");
                var types = SearchAssembly(assemblyPath!, pattern, options.IncludeAll, logger);
                foreach (var t in types)
                {
                    t.Source = resolvedFramework;
                    t.SourceVersion = version;
                }
                AddResults(types);
            }

            // 4. Search platform frameworks
            foreach (var framework in options.PlatformFrameworks)
            {
                if (ReachedLimit()) break;

                var (refPath, resolvedVersion, error) = PlatformResolver.ResolveFramework(framework);
                if (error != null)
                {
                    Console.Error.WriteLine($"Warning: {error}, skipping.");
                    continue;
                }

                var frameworkAssemblies = PlatformResolver.GetAssemblies(refPath!);
                logger.Log($"Searching {frameworkAssemblies.Count} assemblies in {framework}@{resolvedVersion}");

                foreach (var asmInfo in frameworkAssemblies)
                {
                    if (ReachedLimit()) break;

                    var types = SearchAssembly(asmInfo.Path, pattern, options.IncludeAll, logger);
                    foreach (var t in types)
                    {
                        t.Source = framework;
                        t.SourceVersion = resolvedVersion;
                    }
                    AddResults(types);
                }
            }

            // 5. Search projects
            foreach (var projectPath in options.Projects)
            {
                if (ReachedLimit()) break;

                var fullPath = Path.GetFullPath(projectPath);
                var projectDir = Path.GetDirectoryName(fullPath);
                var projectName = Path.GetFileNameWithoutExtension(fullPath);

                if (projectDir == null || !File.Exists(fullPath))
                {
                    Console.Error.WriteLine($"Warning: Project not found '{projectPath}', skipping.");
                    continue;
                }

                // Look for project.assets.json in multiple locations
                var candidatePaths = new[]
                {
                    Path.Combine(projectDir, "obj", "project.assets.json"),
                    Path.Combine(projectDir, "..", "..", "artifacts", "obj", projectName, "project.assets.json"),
                    Path.Combine(projectDir, "artifacts", "obj", projectName, "project.assets.json")
                };

                string? assetsPath = null;
                foreach (var candidate in candidatePaths)
                {
                    var normalized = Path.GetFullPath(candidate);
                    if (File.Exists(normalized))
                    {
                        assetsPath = normalized;
                        break;
                    }
                }

                if (assetsPath == null)
                {
                    Console.Error.WriteLine($"Warning: project.assets.json not found for '{projectPath}'. Run 'dotnet restore'.");
                    continue;
                }

                logger.Log($"Using assets: {assetsPath}");
                var assemblies = ParseProjectAssets(assetsPath, options.Tfm, logger);
                logger.Log($"Searching {assemblies.Count} assemblies from {projectName}");

                foreach (var (asmPath, packageName, packageVersion) in assemblies)
                {
                    if (ReachedLimit()) break;

                    var types = SearchAssembly(asmPath, pattern, options.IncludeAll, logger);
                    foreach (var t in types)
                    {
                        t.Source = packageName;
                        t.SourceVersion = packageVersion;
                    }
                    AddResults(types);
                }
            }

            // 6. Search bin directories
            foreach (var binPath in options.BinPaths)
            {
                if (ReachedLimit()) break;

                if (!Directory.Exists(binPath))
                {
                    Console.Error.WriteLine($"Warning: Directory not found '{binPath}', skipping.");
                    continue;
                }

                var dlls = Directory.GetFiles(binPath, "*.dll", SearchOption.TopDirectoryOnly);
                logger.Log($"Searching {dlls.Length} assemblies in {binPath}");

                foreach (var dll in dlls)
                {
                    if (ReachedLimit()) break;

                    var types = SearchAssembly(dll, pattern, options.IncludeAll, logger);
                    foreach (var t in types)
                    {
                        t.Source = Path.GetFileName(binPath);
                    }
                    AddResults(types);
                }
            }

            // Apply limit
            int totalCount = results.Count;
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
                WriteMarkoutOutput(results, pattern, totalCount, options.Limit);
            }

            return 0;
    }

    private static async Task<List<TypeSearchResult>> CollectAllTypesAsync(FindOptions options, VerboseLogger logger, List<string> tempDirs, HttpClient httpClient)
    {
        var results = new List<TypeSearchResult>();

        // 1. Search packages
        foreach (var pkg in options.Packages)
        {
            var extracted = await PackageExtractor.ExtractPackageAsync(httpClient, pkg, logger.Log, "inspect-find");
            if (extracted == null)
            {
                Console.Error.WriteLine($"Warning: Could not extract package '{pkg}', skipping.");
                continue;
            }

            var (searchPath, tempDir, packageName, packageVersion) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);
            if (tempDir != null) tempDirs.Add(tempDir);

            // Use TfmResolver to select TFM (specific or auto-select highest)
            var tfmPath = TfmResolver.ResolvePackagePath(searchPath, options.Tfm);
            if (tfmPath != null) searchPath = tfmPath;

            var types = CollectTypesFromPath(searchPath, options.IncludeAll, logger);
            foreach (var t in types)
            {
                t.Source = packageName ?? pkg;
                t.SourceVersion = packageVersion;
            }
            results.AddRange(types);
        }

        // 2. Search assemblies
        foreach (var asmPath in options.Assemblies)
        {
            if (!File.Exists(asmPath))
            {
                Console.Error.WriteLine($"Warning: Assembly not found '{asmPath}', skipping.");
                continue;
            }

            var types = CollectTypesFromAssembly(asmPath, options.IncludeAll, logger);
            foreach (var t in types)
            {
                t.Source = Path.GetFileName(asmPath);
            }
            results.AddRange(types);
        }

        // 3. Search platform assemblies
        foreach (var platformAsm in options.PlatformAssemblies)
        {
            var framework = options.PlatformFrameworks.Length > 0 ? options.PlatformFrameworks[0] : null;
            var (assemblyPath, resolvedFramework, version, error) = PlatformResolver.ResolveAssembly(
                platformAsm, framework, packsDirectory: null, useRuntimeAssemblies: false);

            if (error != null)
            {
                Console.Error.WriteLine($"Warning: {error}, skipping.");
                continue;
            }

            logger.Log($"Searching platform assembly: {platformAsm} ({resolvedFramework} {version})");
            var types = CollectTypesFromAssembly(assemblyPath!, options.IncludeAll, logger);
            foreach (var t in types)
            {
                t.Source = resolvedFramework;
                t.SourceVersion = version;
            }
            results.AddRange(types);
        }

        // 4. Search platform frameworks
        foreach (var framework in options.PlatformFrameworks)
        {
            var (refPath, resolvedVersion, error) = PlatformResolver.ResolveFramework(framework);
            if (error != null)
            {
                Console.Error.WriteLine($"Warning: {error}, skipping.");
                continue;
            }

            var frameworkAssemblies = PlatformResolver.GetAssemblies(refPath!);
            logger.Log($"Searching {frameworkAssemblies.Count} assemblies in {framework}@{resolvedVersion}");

            foreach (var asmInfo in frameworkAssemblies)
            {
                var types = CollectTypesFromAssembly(asmInfo.Path, options.IncludeAll, logger);
                foreach (var t in types)
                {
                    t.Source = framework;
                    t.SourceVersion = resolvedVersion;
                }
                results.AddRange(types);
            }
        }

        // 5. Search projects
        foreach (var projectPath in options.Projects)
        {
            var fullPath = Path.GetFullPath(projectPath);
            var projectDir = Path.GetDirectoryName(fullPath);
            var projectName = Path.GetFileNameWithoutExtension(fullPath);

            if (projectDir == null || !File.Exists(fullPath))
            {
                Console.Error.WriteLine($"Warning: Project not found '{projectPath}', skipping.");
                continue;
            }

            var candidatePaths = new[]
            {
                Path.Combine(projectDir, "obj", "project.assets.json"),
                Path.Combine(projectDir, "..", "..", "artifacts", "obj", projectName, "project.assets.json"),
                Path.Combine(projectDir, "artifacts", "obj", projectName, "project.assets.json")
            };

            string? assetsPath = null;
            foreach (var candidate in candidatePaths)
            {
                var normalized = Path.GetFullPath(candidate);
                if (File.Exists(normalized))
                {
                    assetsPath = normalized;
                    break;
                }
            }

            if (assetsPath == null)
            {
                Console.Error.WriteLine($"Warning: project.assets.json not found for '{projectPath}'. Run 'dotnet restore'.");
                continue;
            }

            logger.Log($"Using assets: {assetsPath}");
            var assemblies = ParseProjectAssets(assetsPath, options.Tfm, logger);
            logger.Log($"Searching {assemblies.Count} assemblies from {projectName}");

            foreach (var (asmPath, packageName, packageVersion) in assemblies)
            {
                var types = CollectTypesFromAssembly(asmPath, options.IncludeAll, logger);
                foreach (var t in types)
                {
                    t.Source = packageName;
                    t.SourceVersion = packageVersion;
                }
                results.AddRange(types);
            }
        }

        // 6. Search bin directories
        foreach (var binPath in options.BinPaths)
        {
            if (!Directory.Exists(binPath))
            {
                Console.Error.WriteLine($"Warning: Directory not found '{binPath}', skipping.");
                continue;
            }

            var dlls = Directory.GetFiles(binPath, "*.dll", SearchOption.TopDirectoryOnly);
            logger.Log($"Searching {dlls.Length} assemblies in {binPath}");

            foreach (var dll in dlls)
            {
                var types = CollectTypesFromAssembly(dll, options.IncludeAll, logger);
                foreach (var t in types)
                {
                    t.Source = Path.GetFileName(binPath);
                }
                results.AddRange(types);
            }
        }

        return results;
    }

    private static List<TypeSearchResult> CollectTypesFromPath(string path, bool includeAll, VerboseLogger logger)
    {
        if (File.Exists(path) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return CollectTypesFromAssembly(path, includeAll, logger);
        }
        else if (Directory.Exists(path))
        {
            var results = new List<TypeSearchResult>();
            var dlls = Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories);
            foreach (var dll in dlls)
            {
                results.AddRange(CollectTypesFromAssembly(dll, includeAll, logger));
            }
            return results;
        }
        return [];
    }

    private static List<TypeSearchResult> CollectTypesFromAssembly(string assemblyPath, bool includeAll, VerboseLogger logger)
    {
        var results = new List<TypeSearchResult>();

        try
        {
            var api = AssemblyReader.ExtractApiSurface(assemblyPath, includeAll);
            if (api == null)
                return results;

            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            foreach (var type in api.Types)
            {
                results.Add(new TypeSearchResult
                {
                    TypeName = type.Name,
                    Namespace = type.Namespace,
                    FullName = type.FullName,
                    Kind = type.Kind,
                    Assembly = assemblyName
                });
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Could not read {assemblyPath}: {ex.Message}");
        }

        return results;
    }

    private static void WriteOneLineOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern, bool grouped)
    {
        Console.WriteLine(FormatOneLineOutput(resultsByPattern, grouped));
    }

    internal static string FormatOneLineOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern, bool grouped)
    {
        if (grouped)
        {
            // Grouped: one line per pattern with matching type names
            var lines = new List<string>();
            foreach (var (pattern, results) in resultsByPattern)
            {
                var typeNames = results.Select(r => r.TypeName).Distinct().OrderBy(n => n);
                lines.Add($"{pattern}: {string.Join(", ", typeNames)}");
            }
            return string.Join(Environment.NewLine, lines);
        }
        else
        {
            // Flat: all type names space-separated on one line
            var allTypeNames = resultsByPattern.Values
                .SelectMany(r => r)
                .Select(r => r.TypeName)
                .Distinct()
                .OrderBy(n => n);
            return string.Join(" ", allTypeNames);
        }
    }

    internal static string FormatNameOnlyOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern)
    {
        var allTypeNames = resultsByPattern.Values
            .SelectMany(r => r)
            .Select(r => r.TypeName)
            .Distinct()
            .OrderBy(n => n);
        return string.Join(Environment.NewLine, allTypeNames);
    }

    private static void WriteMultiPatternOutput(Dictionary<string, List<TypeSearchResult>> resultsByPattern, int? limit)
    {
        var writer = new MarkoutWriter();
        writer.WriteHeading(1, "Find Results");

        foreach (var (pattern, results) in resultsByPattern)
        {
            writer.WriteHeading(2, pattern);
            writer.WriteField("Matches", results.Count);

            if (results.Count == 0)
            {
                writer.WriteParagraph("*No types found.*");
            }
            else
            {
                var headers = new[] { "Type", "Namespace", "Kind", "Assembly", "Source" };
                var rows = results.Select(result =>
                {
                    var ns = result.Namespace ?? "";
                    var source = result.SourceVersion != null
                        ? $"{result.Source}@{result.SourceVersion}"
                        : result.Source ?? "";
                    return new[] { result.TypeName, ns, result.Kind ?? "", result.Assembly ?? "", source };
                });
                writer.WriteTable(headers, rows);
            }
        }

        Console.WriteLine(writer.ToString());
    }

    private static List<TypeSearchResult> SearchAssemblyOrDirectory(string path, string pattern, bool includeAll, VerboseLogger logger)
    {
        if (File.Exists(path) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return SearchAssembly(path, pattern, includeAll, logger);
        }
        else if (Directory.Exists(path))
        {
            // Search all DLLs in directory
            var results = new List<TypeSearchResult>();
            var dlls = Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories);
            foreach (var dll in dlls)
            {
                results.AddRange(SearchAssembly(dll, pattern, includeAll, logger));
            }
            return results;
        }
        return [];
    }

    private static List<TypeSearchResult> SearchAssembly(string assemblyPath, string pattern, bool includeAll, VerboseLogger logger)
    {
        var results = new List<TypeSearchResult>();

        try
        {
            var api = AssemblyReader.ExtractApiSurface(assemblyPath, includeAll);
            if (api == null)
                return results;

            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            foreach (var type in api.Types)
            {
                if (TypeMatcher.MatchesGlob(type.FullName, pattern) || TypeMatcher.MatchesGlob(type.Name, pattern))
                {
                    results.Add(new TypeSearchResult
                    {
                        TypeName = type.Name,
                        Namespace = type.Namespace,
                        FullName = type.FullName,
                        Kind = type.Kind,
                        Assembly = assemblyName
                    });
                }
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Could not read {assemblyPath}: {ex.Message}");
        }

        return results;
    }

    private static void WriteJsonOutput(List<TypeSearchResult> results, bool compact)
    {
        var typeInfo = compact
            ? FindCompactJsonContext.Default.ListTypeSearchResult
            : FindJsonContext.Default.ListTypeSearchResult;
        Console.WriteLine(JsonSerializer.Serialize(results, typeInfo));
    }

    private static void WriteMarkoutOutput(List<TypeSearchResult> results, string pattern, int totalCount, int? limit)
    {
        var writer = new MarkoutWriter();
        writer.WriteHeading(1, $"Find: {pattern}");
        writer.WriteField("Matches", totalCount);

        if (results.Count == 0)
        {
            writer.WriteParagraph("*No types found matching the pattern.*");
        }
        else
        {
            var headers = new[] { "Type", "Namespace", "Kind", "Assembly", "Source" };
            var rows = results.Select(result =>
            {
                var ns = result.Namespace ?? "";
                var source = result.SourceVersion != null 
                    ? $"{result.Source}@{result.SourceVersion}" 
                    : result.Source ?? "";
                return new[] { result.TypeName, ns, result.Kind ?? "", result.Assembly ?? "", source };
            });
            writer.WriteTable(headers, rows);

            if (limit.HasValue && totalCount > limit.Value)
            {
                writer.WriteParagraph($"... *and {totalCount - limit.Value} more types*");
            }
        }

        Console.WriteLine(writer.ToString());
    }

    #region Project Assets Parsing

    private static List<(string Path, string PackageName, string Version)> ParseProjectAssets(string assetsPath, string? tfmFilter, VerboseLogger logger)
    {
        var results = new List<(string Path, string PackageName, string Version)>();
        var nugetCache = NuGetCache.GetNuGetCachePath();

        try
        {
            var json = File.ReadAllText(assetsPath);
            using var doc = JsonDocument.Parse(json);

            // Get targets - contains TFM-specific dependency info
            if (!doc.RootElement.TryGetProperty("targets", out var targets))
                return results;

            // Find the target TFM
            string? selectedTfm = null;
            foreach (var target in targets.EnumerateObject())
            {
                var tfmName = target.Name;
                // Target names can be "net8.0" or "net8.0/win-x64"
                var baseTfm = tfmName.Contains('/') ? tfmName[..tfmName.IndexOf('/')] : tfmName;

                if (!string.IsNullOrEmpty(tfmFilter))
                {
                    if (baseTfm.Equals(tfmFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedTfm = tfmName;
                        break;
                    }
                }
                else
                {
                    // Pick the highest priority TFM
                    if (selectedTfm == null || TfmResolver.GetTfmPriority(baseTfm) > TfmResolver.GetTfmPriority(selectedTfm.Contains('/') ? selectedTfm[..selectedTfm.IndexOf('/')] : selectedTfm))
                    {
                        selectedTfm = tfmName;
                    }
                }
            }

            if (selectedTfm == null)
                return results;

            logger.Log($"Using target framework: {selectedTfm}");

            // Get libraries for package paths
            if (!doc.RootElement.TryGetProperty("libraries", out var libraries))
                return results;

            var targetDeps = targets.GetProperty(selectedTfm);

            foreach (var dep in targetDeps.EnumerateObject())
            {
                // dep.Name is "PackageName/Version"
                var parts = dep.Name.Split('/');
                if (parts.Length != 2) continue;

                var packageName = parts[0];
                var version = parts[1];

                // Get the library path from libraries section
                if (!libraries.TryGetProperty(dep.Name, out var libInfo))
                    continue;

                // Skip project references
                if (libInfo.TryGetProperty("type", out var typeElem) && typeElem.GetString() == "project")
                    continue;

                // Get the package path
                if (!libInfo.TryGetProperty("path", out var pathElem))
                    continue;

                var packagePath = pathElem.GetString();
                if (string.IsNullOrEmpty(packagePath))
                    continue;

                // Get compile-time assemblies
                if (dep.Value.TryGetProperty("compile", out var compile))
                {
                    foreach (var asm in compile.EnumerateObject())
                    {
                        // asm.Name is like "lib/net8.0/Package.dll"
                        if (!asm.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            continue;

                        // Skip placeholder assemblies
                        if (asm.Name.Contains("_._"))
                            continue;

                        var fullPath = Path.Combine(nugetCache, packagePath, asm.Name.Replace('/', Path.DirectorySeparatorChar));
                        if (File.Exists(fullPath))
                        {
                            results.Add((fullPath, packageName, version));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.Log($"Warning: Failed to parse project.assets.json: {ex.Message}");
        }

        return results;
    }

    #endregion
}

/// <summary>
/// Represents a type found during search.
/// </summary>
public class TypeSearchResult
{
    [JsonPropertyName("type")]
    public string TypeName { get; set; } = "";

    [JsonPropertyName("namespace")]
    public string? Namespace { get; set; }

    [JsonPropertyName("full_name")]
    public string FullName { get; set; } = "";

    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("assembly")]
    public string? Assembly { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("source_version")]
    public string? SourceVersion { get; set; }
}

[JsonSerializable(typeof(List<TypeSearchResult>))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class FindJsonContext : JsonSerializerContext { }

[JsonSerializable(typeof(List<TypeSearchResult>))]
[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class FindCompactJsonContext : JsonSerializerContext { }
