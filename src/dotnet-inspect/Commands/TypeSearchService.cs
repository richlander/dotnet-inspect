using DotnetInspector.Inspectors;
using DotnetInspector.Metadata;
using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Commands;

/// <summary>
/// Collects types from multiple sources: packages, assemblies, platform frameworks,
/// projects, and bin directories. Handles the 6-source iteration pattern.
/// </summary>
internal static class TypeSearchService
{
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
        var results = new List<TypeSearchResult>();

        bool ReachedLimit() => pattern != null && options.Limit.HasValue && results.Count >= options.Limit.Value;

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
                Console.Error.WriteLine($"Warning: Assembly not found '{asmPath}', skipping.");
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
            var (assemblyPath, resolvedFramework, version, error) = PlatformResolver.ResolveAssembly(
                platformAsm, framework, packsDirectory: null, useRuntimeAssemblies: false);

            if (error != null)
            {
                Console.Error.WriteLine($"Warning: {error}, skipping.");
                continue;
            }

            logger.Log($"Searching platform assembly: {platformAsm} ({resolvedFramework} {version})");
            var types = CollectFromAssembly(assemblyPath!, pattern, options.IncludeAll, logger);
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
            logger.Log($"Searching {assemblies.Count} assemblies from {projectName}");

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
            logger.Log($"Searching {dlls.Length} assemblies in {binPath}");

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
            var results = new List<TypeSearchResult>();
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
        var results = new List<TypeSearchResult>();

        try
        {
            var api = AssemblyReader.ExtractApiSurface(assemblyPath, includeAll);
            if (api == null)
                return results;

            var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

            foreach (var type in api.Types)
            {
                if (pattern != null &&
                    !TypeMatcher.MatchesGlob(type.FullName, pattern) &&
                    !TypeMatcher.MatchesGlob(type.Name, pattern))
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
