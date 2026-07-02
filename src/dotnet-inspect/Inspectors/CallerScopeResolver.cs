using DotnetInspector.Options;
using DotnetInspector.Output;
using DotnetInspector.Packages;
using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Resolves member caller-scope flags (<c>--bin</c>/<c>--directory</c>, <c>--project</c>, and
/// <c>--caller-package</c>) into a deduplicated list of on-disk assembly paths to scan for
/// inbound callers, mirroring the scope semantics of the <c>find</c> command.
/// </summary>
public static class CallerScopeResolver
{
    /// <summary>
    /// Expands the requested directories, projects, and packages into assembly paths, excluding
    /// <paramref name="ownAssemblyPath"/> (already scanned as the member's own assembly) and
    /// de-duplicating by normalized full path.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ResolveAsync(
        IReadOnlyList<string> directories,
        IReadOnlyList<string> projects,
        IReadOnlyList<string> packages,
        string? tfm,
        string? ownAssemblyPath,
        HttpClient httpClient,
        List<string> tempDirs,
        VerboseLogger logger)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (ownAssemblyPath != null)
            seen.Add(Path.GetFullPath(ownAssemblyPath));

        void Add(string path)
        {
            var full = Path.GetFullPath(path);
            if (seen.Add(full) && File.Exists(full))
                result.Add(full);
        }

        // Packages: download and extract via AssemblyCollector
        if (packages.Count > 0)
        {
            var scopeOptions = new CallerScopeOptions(packages, tfm);
            var assemblies = await AssemblyCollector.CollectAsync(
                httpClient, scopeOptions, tempDirs, logger, "inspect-caller");
            foreach (var asm in assemblies)
                Add(asm.Path);
        }

        // Projects: restored dependencies via project.assets.json
        foreach (var projectPath in projects)
        {
            var assetsPath = ProjectAssetsParser.FindAssets(projectPath);
            if (assetsPath == null)
            {
                Console.Error.WriteLine($"Warning: project.assets.json not found for '{projectPath}'. Run 'dotnet restore'.");
                continue;
            }

            logger.Log($"Using assets: {assetsPath}");
            foreach (var (asmPath, _, _) in ProjectAssetsParser.Parse(assetsPath, tfm, logger.Log))
                Add(asmPath);
        }

        // Directories: top-level *.dll files
        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir))
            {
                Console.Error.WriteLine($"Warning: Directory not found '{dir}', skipping.");
                continue;
            }

            foreach (var dll in Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly))
                Add(dll);
        }

        return result;
    }

    /// <summary>
    /// Minimal IAssemblySourceOptions implementation for caller-scope package resolution.
    /// </summary>
    private sealed class CallerScopeOptions : IAssemblySourceOptions
    {
        public CallerScopeOptions(IReadOnlyList<string> packages, string? tfm)
        {
            Packages = packages.ToArray();
            Tfm = tfm;
        }

        public string[] Packages { get; }
        public string[] Assemblies { get; } = [];
        public string[] PlatformAssemblies { get; } = [];
        public string[] PlatformFrameworks { get; } = [];
        public string[] Projects { get; } = [];
        public string? Tfm { get; }
        
        NuGetSourceOptions? IAssemblySourceOptions.SourceOptions => null;
        
        public bool HasAnyScope => Packages.Length > 0;
    }
}
