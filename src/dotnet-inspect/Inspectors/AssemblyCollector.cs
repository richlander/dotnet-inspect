using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Collects assembly paths from various sources (packages, files, platform).
/// </summary>
public static class AssemblyCollector
{
    /// <summary>
    /// Represents an assembly with its source information.
    /// </summary>
    public record AssemblyInfo(string Path, string Source, string? Version);

    /// <summary>
    /// Collects assembly paths from packages, direct assemblies, and platform sources.
    /// </summary>
    /// <param name="httpClient">HTTP client for downloading packages</param>
    /// <param name="options">Source options specifying where to look for assemblies</param>
    /// <param name="tempDirs">List to track temporary directories for cleanup</param>
    /// <param name="logger">Logger for verbose output</param>
    /// <param name="tempDirPrefix">Prefix for temporary directories</param>
    /// <returns>List of assembly paths with source information</returns>
    public static async Task<List<AssemblyInfo>> CollectAsync(
        HttpClient httpClient,
        IAssemblySourceOptions options,
        List<string> tempDirs,
        VerboseLogger logger,
        string tempDirPrefix = "inspect")
    {
        var assemblyPaths = new List<AssemblyInfo>();

        // 1. Packages
        foreach (var pkg in options.Packages)
        {
            var extracted = await PackageExtractor.ExtractPackageAsync(httpClient, pkg, logger.Log, tempDirPrefix, options.SourceOptions);
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
                assemblyPaths.Add(new AssemblyInfo(dll, extracted.PackageName ?? pkg, extracted.Version));
            }
        }

        // 2. Direct assemblies
        foreach (var asmPath in options.Assemblies)
        {
            if (!File.Exists(asmPath))
            {
                Console.Error.WriteLine($"Warning: Library not found '{asmPath}', skipping.");
                continue;
            }
            assemblyPaths.Add(new AssemblyInfo(asmPath, Path.GetFileName(asmPath), null));
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
            assemblyPaths.Add(new AssemblyInfo(assemblyPath!, resolvedFramework ?? "platform", version));
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
            logger.Log($"Scanning {frameworkAssemblies.Count} libraries in {framework}@{resolvedVersion}");

            foreach (var asmInfo in frameworkAssemblies)
            {
                assemblyPaths.Add(new AssemblyInfo(asmInfo.Path, framework, resolvedVersion));
            }
        }

        return assemblyPaths;
    }

    /// <summary>
    /// Cleans up temporary directories created during assembly collection.
    /// </summary>
    public static void CleanupTempDirs(List<string> tempDirs)
    {
        foreach (var dir in tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
