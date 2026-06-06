using DotnetInspector.Commands;
using DotnetInspector.Metadata;
using DotnetInspector.Models;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using PackageExtractor = DotnetInspector.Packages.PackageExtractor;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Collects types from multiple sources: packages, assemblies, platform frameworks,
/// projects, and bin directories. Handles the 6-source iteration pattern.
/// </summary>
internal static class TypeSearchService
{
    /// <summary>
    /// Finds types matching one or more patterns, returning classified results with match kind and similarity.
    /// This is the primary entry point for the find command.
    /// </summary>
    public static async Task<List<TypeFindResult>> FindTypesAsync(
        FindOptions options,
        string[] patterns,
        VerboseLogger logger,
        List<string> tempDirs,
        HttpClient httpClient)
    {
        // Optimized single-pattern path: collect with filtering, then partial match if empty
        if (patterns.Length == 1 && !options.OneLine)
        {
            return await FindSinglePatternAsync(patterns[0], options, logger, tempDirs, httpClient);
        }

        // Multi-pattern or oneline: collect all types, then match each pattern
        return await FindMultiPatternAsync(patterns, options, logger, tempDirs, httpClient);
    }

    private static async Task<List<TypeFindResult>> FindMultiPatternAsync(
        string[] patterns,
        FindOptions options,
        VerboseLogger logger,
        List<string> tempDirs,
        HttpClient httpClient)
    {
        var allTypes = await CollectTypesAsync(options, null, logger, tempDirs, httpClient);
        var typeNames = allTypes.Select(t => t.FullName).Distinct().ToList();

        Dictionary<string, List<TypeSearchResult>> resultsByPattern = [];
        Dictionary<string, List<TypeSearchResult>> partialMatchesByPattern = [];
        Dictionary<string, Dictionary<string, double>> similarityByPattern = [];
        List<string> notFoundPatterns = [];

        foreach (var pattern in patterns)
        {
            List<TypeSearchResult> matches = [];
            foreach (var type in allTypes)
            {
                if (TypeMatcher.MatchesTypeFilter(type.FullName, pattern))
                {
                    matches.Add(type);
                }
            }

            if (options.Limit.HasValue && matches.Count > options.Limit.Value)
            {
                matches = matches.Take(options.Limit.Value).ToList();
            }

            if (matches.Count > 0)
            {
                resultsByPattern[pattern] = matches;
            }
            else if (!pattern.Contains('*') && !pattern.Contains('?'))
            {
                var suggestions = TypeMatcher.FindClosest(typeNames, pattern, minSimilarity: 0.5, maxResults: 5).ToList();
                if (suggestions.Count > 0)
                {
                    var simDict = suggestions.ToDictionary(s => s.Name, s => s.Similarity);
                    similarityByPattern[pattern] = simDict;

                    var suggestionSet = suggestions.Select(s => s.Name).ToHashSet();
                    var partialMatches = allTypes
                        .Where(t => suggestionSet.Contains(t.FullName))
                        .DistinctBy(t => t.FullName)
                        .ToList();
                    partialMatchesByPattern[pattern] = partialMatches;
                }
                else
                {
                    notFoundPatterns.Add(pattern);
                }
            }
            else
            {
                notFoundPatterns.Add(pattern);
            }
        }

        return ConvertToFindResults(resultsByPattern, partialMatchesByPattern, notFoundPatterns, similarityByPattern);
    }

    private static async Task<List<TypeFindResult>> FindSinglePatternAsync(
        string pattern,
        FindOptions options,
        VerboseLogger logger,
        List<string> tempDirs,
        HttpClient httpClient)
    {
        var results = await CollectTypesAsync(options, pattern, logger, tempDirs, httpClient);

        List<TypeSearchResult>? partialMatches = null;
        Dictionary<string, double>? partialSimilarities = null;
        if (results.Count == 0 && !pattern.Contains('*') && !pattern.Contains('?'))
        {
            var allTypes = await CollectTypesAsync(options, null, logger, tempDirs, httpClient);
            var typeNames = allTypes.Select(t => t.FullName).Distinct().ToList();
            var suggestions = TypeMatcher.FindClosest(typeNames, pattern, minSimilarity: 0.5, maxResults: 5).ToList();

            if (suggestions.Count > 0)
            {
                partialSimilarities = suggestions.ToDictionary(s => s.Name, s => s.Similarity);
                var suggestionSet = suggestions.Select(s => s.Name).ToHashSet();
                partialMatches = allTypes
                    .Where(t => suggestionSet.Contains(t.FullName))
                    .DistinctBy(t => t.FullName)
                    .ToList();
            }
        }

        int totalCount = results.Count;
        if (options.Limit.HasValue && results.Count > options.Limit.Value)
        {
            results = results.Take(options.Limit.Value).ToList();
        }

        var similarityByPattern = partialSimilarities != null
            ? new Dictionary<string, Dictionary<string, double>> { [pattern] = partialSimilarities }
            : null;

        return ConvertToFindResults(
            new Dictionary<string, List<TypeSearchResult>> { [pattern] = results },
            partialMatches != null ? new Dictionary<string, List<TypeSearchResult>> { [pattern] = partialMatches } : [],
            [],
            similarityByPattern);
    }

    /// <summary>
    /// Converts separate result dictionaries into a unified flat list of TypeFindResult.
    /// </summary>
    private static List<TypeFindResult> ConvertToFindResults(
        Dictionary<string, List<TypeSearchResult>> exactMatches,
        Dictionary<string, List<TypeSearchResult>> partialMatches,
        List<string> notFoundPatterns,
        Dictionary<string, Dictionary<string, double>>? similarityByPattern = null)
    {
        var results = new List<TypeFindResult>();

        foreach (var (pattern, types) in exactMatches)
        {
            var isGlob = pattern.Contains('*') || pattern.Contains('?');
            foreach (var t in types)
            {
                results.Add(new TypeFindResult
                {
                    Pattern = pattern,
                    Match = isGlob ? MatchKind.Glob : MatchKind.Exact,
                    Similarity = 1.0,
                    Type = t.TypeName,
                    Namespace = t.Namespace ?? "",
                    FullName = t.FullName,
                    Kind = t.Kind ?? "",
                    Library = t.Assembly ?? "",
                    Source = t.Source ?? "",
                    SourceVersion = t.SourceVersion
                });
            }
        }

        foreach (var (pattern, types) in partialMatches)
        {
            var simDict = similarityByPattern?.GetValueOrDefault(pattern);
            foreach (var t in types)
            {
                var similarity = simDict?.GetValueOrDefault(t.FullName, 0.5) ?? 0.5;
                results.Add(new TypeFindResult
                {
                    Pattern = pattern,
                    Match = MatchKind.Partial,
                    Similarity = similarity,
                    Type = t.TypeName,
                    Namespace = t.Namespace ?? "",
                    FullName = t.FullName,
                    Kind = t.Kind ?? "",
                    Library = t.Assembly ?? "",
                    Source = t.Source ?? "",
                    SourceVersion = t.SourceVersion
                });
            }
        }

        foreach (var pattern in notFoundPatterns)
        {
            results.Add(new TypeFindResult
            {
                Pattern = pattern,
                Match = MatchKind.NotFound,
                Similarity = null
            });
        }

        return results;
    }

    /// <summary>
    /// Collects types from all configured sources, optionally filtered by pattern.
    /// When pattern is provided, matching happens during collection for early-exit with limit.
    /// </summary>
    public static async Task<List<TypeSearchResult>> CollectTypesAsync(
        FindOptions options,
        string? pattern,
        VerboseLogger logger,
        List<string> tempDirs,
        HttpClient httpClient)
    {
        List<TypeSearchResult> results = [];

        bool ReachedLimit() => pattern != null && options.Limit.HasValue && results.Count >= options.Limit.Value;

        // 1. Search packages
        foreach (var pkg in options.Packages)
        {
            if (ReachedLimit()) break;

            var outcome = await PackageExtractor.ExtractPackageAsync(httpClient, pkg, logger.Log, "inspect-find");
            if (!outcome.IsSuccess)
            {
                Console.Error.WriteLine($"Warning: {outcome.ErrorMessage}");
                continue;
            }
            var extracted = outcome.Result!;

            var (searchPath, tempDir, packageName, packageVersion) = (extracted.ExtractPath, extracted.TempDir, extracted.PackageName, extracted.Version);
            if (tempDir != null) tempDirs.Add(tempDir);

            var tfmPath = TfmResolver.ResolvePackagePath(searchPath, options.Tfm);
            if (tfmPath != null) searchPath = tfmPath;

            var types = CollectFromPath(searchPath, pattern, options.IncludeAll, logger);
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
            if (ReachedLimit()) break;

            if (!File.Exists(asmPath))
            {
                Console.Error.WriteLine($"Warning: Library not found '{asmPath}', skipping.");
                continue;
            }

            var types = CollectFromAssembly(asmPath, pattern, options.IncludeAll, logger);
            foreach (var t in types)
            {
                t.Source = Path.GetFileName(asmPath);
            }
            results.AddRange(types);
        }

        // 3. Search platform assemblies
        foreach (var platformAsm in options.PlatformAssemblies)
        {
            if (ReachedLimit()) break;

            var framework = options.PlatformFrameworks.Length > 0 ? options.PlatformFrameworks[0] : null;
            var (assemblyPath, resolvedFramework, version, error) = await PlatformResolver.ResolveAssemblyAsync(
                platformAsm, httpClient, logger.Log, framework);

            if (error != null)
            {
                Console.Error.WriteLine($"Warning: {error}, skipping.");
                continue;
            }

            logger.Log($"Searching platform library: {platformAsm} ({resolvedFramework} {version})");
            var types = CollectFromAssembly(assemblyPath!, pattern, options.IncludeAll, logger);
            foreach (var t in types)
            {
                t.Source = resolvedFramework;
                t.SourceVersion = version;
            }
            results.AddRange(types);
        }

        // 4. Search platform frameworks (download ref packs only if not locally available)
        if (options.PlatformFrameworks.Length > 0)
        {
            var requests = PlatformPackService.GetMissingPackRequests(options.PlatformFrameworks);
            if (requests.Count > 0)
            {
                await foreach (var _ in PlatformPackService.EnsurePacksAsync(requests, httpClient, logger.Log))
                {
                }
            }
        }

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
            logger.Log($"Searching {frameworkAssemblies.Count} libraries in {framework}@{resolvedVersion}");

            foreach (var asmInfo in frameworkAssemblies)
            {
                if (ReachedLimit()) break;

                var types = CollectFromAssembly(asmInfo.Path, pattern, options.IncludeAll, logger);
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
            if (ReachedLimit()) break;

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
            var assemblies = ProjectAssetsParser.Parse(assetsPath, options.Tfm, logger.Log);
            logger.Log($"Searching {assemblies.Count} libraries from {projectName}");

            foreach (var (asmPath, packageName, packageVersion) in assemblies)
            {
                if (ReachedLimit()) break;

                var types = CollectFromAssembly(asmPath, pattern, options.IncludeAll, logger);
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
            if (ReachedLimit()) break;

            if (!Directory.Exists(binPath))
            {
                Console.Error.WriteLine($"Warning: Directory not found '{binPath}', skipping.");
                continue;
            }

            var dlls = Directory.GetFiles(binPath, "*.dll", SearchOption.TopDirectoryOnly);
            logger.Log($"Searching {dlls.Length} libraries in {binPath}");

            foreach (var dll in dlls)
            {
                if (ReachedLimit()) break;

                var types = CollectFromAssembly(dll, pattern, options.IncludeAll, logger);
                foreach (var t in types)
                {
                    t.Source = Path.GetFileName(binPath);
                }
                results.AddRange(types);
            }
        }

        return results;
    }

    /// <summary>
    /// Collects types from a path (file or directory), optionally filtered by pattern.
    /// </summary>
    public static List<TypeSearchResult> CollectFromPath(string path, string? pattern, bool includeAll, VerboseLogger logger)
    {
        if (File.Exists(path) && path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return CollectFromAssembly(path, pattern, includeAll, logger);
        }
        else if (Directory.Exists(path))
        {
            List<TypeSearchResult> results = [];
            var dlls = Directory.GetFiles(path, "*.dll", SearchOption.AllDirectories);
            foreach (var dll in dlls)
            {
                results.AddRange(CollectFromAssembly(dll, pattern, includeAll, logger));
            }
            return results;
        }
        return [];
    }

    /// <summary>
    /// Extracts types from a single assembly, optionally filtered by pattern.
    /// </summary>
    public static List<TypeSearchResult> CollectFromAssembly(string assemblyPath, string? pattern, bool includeAll, VerboseLogger logger)
    {
        List<TypeSearchResult> results = [];

        try
        {
            var api = AssemblyReader.ExtractApiSurface(assemblyPath, includeAll, typesOnly: true);
            if (api == null)
                return results;

            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            foreach (var type in api.Types)
            {
                // Use MatchesTypeFilter which handles both glob patterns and exact matches
                // (including generic type base names like SortedDictionary -> SortedDictionary`2)
                if (pattern != null && !TypeMatcher.MatchesTypeFilter(type.FullName, pattern))
                {
                    continue;
                }

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
}
