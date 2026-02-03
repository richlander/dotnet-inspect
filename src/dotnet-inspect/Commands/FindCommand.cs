using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotnetInspector.Inspectors;
using DotnetInspector.Options;
using DotnetInspector.Output;

namespace DotnetInspector.Commands;

/// <summary>
/// Searches for types across packages, assemblies, and platform frameworks.
/// </summary>
public class FindCommand
{
    public static async Task<int> ExecuteAsync(string pattern, FindOptions options)
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

            var results = new List<TypeSearchResult>();
            var seen = new HashSet<string>(); // For deduplication: "FullName|Assembly"

            // Helper to add results with deduplication
            void AddResults(IEnumerable<TypeSearchResult> types)
            {
                foreach (var t in types)
                {
                    var key = $"{t.FullName}|{t.Assembly}";
                    if (seen.Add(key))
                    {
                        results.Add(t);
                    }
                }
            }

            // Check if we've hit the limit
            bool ReachedLimit() => options.Limit.HasValue && results.Count >= options.Limit.Value;

            // 1. Search packages
            foreach (var pkg in options.Packages)
            {
                if (ReachedLimit()) break;

                var extracted = await ExtractPackageAsync(pkg, logger);
                if (extracted == null)
                {
                    Console.Error.WriteLine($"Warning: Could not extract package '{pkg}', skipping.");
                    continue;
                }

                var (searchPath, tempDir, packageName, packageVersion) = extracted.Value;
                if (tempDir != null) tempDirs.Add(tempDir);

                // Auto-select TFM or use specified one
                if (!string.IsNullOrEmpty(options.Tfm))
                {
                    var tfmAssembly = ApiCommand.FindAssemblyByTfm(searchPath, options.Tfm);
                    if (tfmAssembly != null) searchPath = tfmAssembly;
                }
                else
                {
                    var dlls = GetPackageDlls(searchPath);
                    var (highestPath, _) = SelectHighestTfmAssembly(dlls, searchPath);
                    if (highestPath != null) searchPath = highestPath;
                }

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
        finally
        {
            foreach (var tempDir in tempDirs)
            {
                if (Directory.Exists(tempDir))
                {
                    try { Directory.Delete(tempDir, recursive: true); } catch { }
                }
            }
        }
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
            using var fs = File.OpenRead(assemblyPath);
            using var peReader = new PEReader(fs);

            if (!peReader.HasMetadata)
                return results;

            var api = ApiSurfaceExtractor.Extract(peReader, includeAll);
            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            foreach (var type in api.Types)
            {
                var fullName = string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";

                if (MatchesGlobPattern(fullName, pattern) || MatchesGlobPattern(type.Name, pattern))
                {
                    results.Add(new TypeSearchResult
                    {
                        TypeName = type.Name,
                        Namespace = type.Namespace,
                        FullName = fullName,
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

    private static bool MatchesGlobPattern(string text, string pattern)
    {
        // Convert glob pattern to regex
        // * matches any characters, ? matches single character
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(text, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
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
        var sb = new StringBuilder();
        sb.AppendLine($"# Find: {pattern}");
        sb.AppendLine();
        sb.AppendLine($"**Matches:** {totalCount}");
        sb.AppendLine();

        if (results.Count == 0)
        {
            sb.AppendLine("*No types found matching the pattern.*");
        }
        else
        {
            sb.AppendLine("| Type | Namespace | Kind | Assembly | Source |");
            sb.AppendLine("|------|-----------|------|----------|--------|");

            foreach (var result in results)
            {
                var ns = result.Namespace ?? "";
                var source = result.SourceVersion != null 
                    ? $"{result.Source}@{result.SourceVersion}" 
                    : result.Source ?? "";
                sb.AppendLine($"| {result.TypeName} | {ns} | {result.Kind} | {result.Assembly} | {source} |");
            }

            if (limit.HasValue && totalCount > limit.Value)
            {
                sb.AppendLine();
                sb.AppendLine($"*... and {totalCount - limit.Value} more types*");
            }
        }

        Console.WriteLine(sb.ToString().TrimEnd());
    }

    #region Package Extraction (adapted from ApiCommand)

    private static async Task<(string extractPath, string? tempDir, string? packageName, string? version)?> ExtractPackageAsync(string packageSource, VerboseLogger logger)
    {
        bool isLocalFile = packageSource.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase);

        if (isLocalFile)
        {
            if (!File.Exists(packageSource))
            {
                Console.Error.WriteLine($"Error: File not found: {packageSource}");
                return null;
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"inspect-find-{Guid.NewGuid():N}");
            var extractPath = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(tempDir);

            logger.Log($"Extracting {Path.GetFileName(packageSource)}...");
            System.IO.Compression.ZipFile.ExtractToDirectory(packageSource, extractPath);

            var packageName = Path.GetFileNameWithoutExtension(packageSource);
            return (extractPath, tempDir, packageName, null);
        }
        else
        {
            // NuGet package reference
            using var client = HttpClientFactory.Create();

            var (packageName, version) = ParsePackageReference(packageSource);
            
            // Get version if not specified
            if (version == null)
            {
                version = await GetLatestVersionAsync(client, packageName, logger);
                if (version == null)
                {
                    Console.Error.WriteLine($"Error: Package '{packageName}' not found on nuget.org");
                    return null;
                }
            }

            // Check NuGet cache first
            var cachedPath = NuGetCache.TryGetCachedPackage(packageName, version);
            if (cachedPath != null && NuGetCache.IsCachedPackageValid(cachedPath))
            {
                logger.Log($"Using cached package: {cachedPath}");
                return (cachedPath, null, packageName, version);
            }

            var tempDir = Path.Combine(Path.GetTempPath(), $"inspect-find-{Guid.NewGuid():N}");
            var extractPath = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(tempDir);

            var nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName.ToLowerInvariant()}/{version.ToLowerInvariant()}/{packageName.ToLowerInvariant()}.{version.ToLowerInvariant()}.nupkg";
            logger.Log($"Downloading: {packageName} {version}");

            try
            {
                var packageBytes = await client.GetByteArrayAsync(nupkgUrl);
                var nupkgPath = Path.Combine(tempDir, $"{packageName}.{version}.nupkg");
                await File.WriteAllBytesAsync(nupkgPath, packageBytes);
                System.IO.Compression.ZipFile.ExtractToDirectory(nupkgPath, extractPath);
                logger.Log("Package downloaded successfully.");

                // Cache for future use
                var newCachePath = NuGetCache.CachePackage(extractPath, packageName, version);
                if (newCachePath != null)
                {
                    logger.Log($"Cached to: {newCachePath}");
                }

                return (extractPath, tempDir, packageName, version);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.Error.WriteLine($"Error: Package '{packageName}' version '{version}' not found on nuget.org.");
                try { Directory.Delete(tempDir, recursive: true); } catch { }
                return null;
            }
        }
    }

    private static async Task<string?> GetLatestVersionAsync(HttpClient client, string packageName, VerboseLogger logger)
    {
        try
        {
            var indexUrl = $"https://api.nuget.org/v3-flatcontainer/{packageName.ToLowerInvariant()}/index.json";
            var response = await client.GetStringAsync(indexUrl);
            using var doc = JsonDocument.Parse(response);
            var versions = doc.RootElement.GetProperty("versions");
            if (versions.GetArrayLength() > 0)
            {
                // Get latest non-prerelease version, or latest overall
                string? latestStable = null;
                string? latestAny = null;
                foreach (var v in versions.EnumerateArray())
                {
                    var ver = v.GetString();
                    if (ver != null)
                    {
                        latestAny = ver;
                        if (!ver.Contains('-'))
                            latestStable = ver;
                    }
                }
                return latestStable ?? latestAny;
            }
        }
        catch
        {
            // Package not found
        }
        return null;
    }

    private static (string name, string? version) ParsePackageReference(string reference)
    {
        var atIndex = reference.LastIndexOf('@');
        if (atIndex > 0)
        {
            return (reference[..atIndex], reference[(atIndex + 1)..]);
        }
        return (reference, null);
    }

    private static List<string> GetPackageDlls(string extractPath)
    {
        var result = new List<string>();
        var libDir = Path.Combine(extractPath, "lib");
        var toolsDir = Path.Combine(extractPath, "tools");

        if (Directory.Exists(libDir))
        {
            result.AddRange(Directory.GetFiles(libDir, "*.dll", SearchOption.AllDirectories));
        }
        if (Directory.Exists(toolsDir))
        {
            result.AddRange(Directory.GetFiles(toolsDir, "*.dll", SearchOption.AllDirectories));
        }
        // Root level DLLs
        result.AddRange(Directory.GetFiles(extractPath, "*.dll", SearchOption.TopDirectoryOnly));

        return result;
    }

    private static (string? path, string? tfm) SelectHighestTfmAssembly(List<string> dlls, string basePath)
    {
        if (dlls.Count == 0) return (null, null);
        if (dlls.Count == 1) return (dlls[0], ExtractTfmFromPath(Path.GetRelativePath(basePath, dlls[0]).Replace('\\', '/')));

        var tfmGroups = dlls
            .Select(d => (path: d, tfm: ExtractTfmFromPath(Path.GetRelativePath(basePath, d).Replace('\\', '/'))))
            .Where(x => x.tfm != null)
            .GroupBy(x => x.tfm)
            .OrderByDescending(g => GetTfmPriority(g.Key!))
            .FirstOrDefault();

        if (tfmGroups != null)
        {
            var selected = tfmGroups.First();
            return (selected.path, selected.tfm);
        }

        return (dlls[0], null);
    }

    private static string? ExtractTfmFromPath(string relativePath)
    {
        // e.g., "lib/net8.0/Foo.dll" -> "net8.0"
        var parts = relativePath.Split('/');
        if (parts.Length >= 2)
        {
            var potential = parts[^2];
            if (IsTfmLike(potential))
                return potential;
        }
        return null;
    }

    private static bool IsTfmLike(string s)
    {
        var lower = s.ToLowerInvariant();
        return lower.StartsWith("net") || lower.StartsWith("netstandard") || lower.StartsWith("netcoreapp");
    }

    private static int GetTfmPriority(string tfm)
    {
        var lower = tfm.ToLowerInvariant();

        if (lower.StartsWith("net") && !lower.StartsWith("netstandard") && !lower.StartsWith("netcoreapp"))
        {
            var versionPart = lower[3..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 10000 + (version.Major * 100) + version.Minor;
            }
            if (int.TryParse(versionPart.Replace(".", ""), out var legacyVersion))
            {
                return 1000 + legacyVersion;
            }
        }

        if (lower.StartsWith("netcoreapp"))
        {
            var versionPart = lower[10..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 5000 + (version.Major * 100) + version.Minor;
            }
        }

        if (lower.StartsWith("netstandard"))
        {
            var versionPart = lower[11..];
            if (Version.TryParse(versionPart, out var version))
            {
                return 3000 + (version.Major * 100) + version.Minor;
            }
        }

        return 0;
    }

    #endregion

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
                    if (selectedTfm == null || GetTfmPriority(baseTfm) > GetTfmPriority(selectedTfm.Contains('/') ? selectedTfm[..selectedTfm.IndexOf('/')] : selectedTfm))
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
