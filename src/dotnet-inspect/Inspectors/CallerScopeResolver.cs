using DotnetInspector.Services;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Resolves member caller-scope flags (<c>--bin</c>/<c>--directory</c> and <c>--project</c>) into a
/// deduplicated list of on-disk assembly paths to scan for inbound callers, mirroring the
/// scope semantics of the <c>find</c> command.
/// </summary>
public static class CallerScopeResolver
{
    /// <summary>
    /// Expands the requested directories and projects into assembly paths, excluding
    /// <paramref name="ownAssemblyPath"/> (already scanned as the member's own assembly) and
    /// de-duplicating by normalized full path.
    /// </summary>
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<string> directories,
        IReadOnlyList<string> projects,
        string? tfm,
        string? ownAssemblyPath,
        Action<string>? log = null)
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

        foreach (var projectPath in projects)
        {
            var assetsPath = FindProjectAssets(projectPath);
            if (assetsPath == null)
            {
                Console.Error.WriteLine($"Warning: project.assets.json not found for '{projectPath}'. Run 'dotnet restore'.");
                continue;
            }

            log?.Invoke($"Using assets: {assetsPath}");
            foreach (var (asmPath, _, _) in ProjectAssetsParser.Parse(assetsPath, tfm, log))
                Add(asmPath);
        }

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

    static string? FindProjectAssets(string projectPath)
    {
        var fullPath = Path.GetFullPath(projectPath);
        var projectDir = Path.GetDirectoryName(fullPath);
        var projectName = Path.GetFileNameWithoutExtension(fullPath);

        if (projectDir == null || !File.Exists(fullPath))
        {
            Console.Error.WriteLine($"Warning: Project not found '{projectPath}', skipping.");
            return null;
        }

        var candidates = new[]
        {
            Path.Combine(projectDir, "obj", "project.assets.json"),
            Path.Combine(projectDir, "..", "..", "artifacts", "obj", projectName, "project.assets.json"),
            Path.Combine(projectDir, "artifacts", "obj", projectName, "project.assets.json")
        };

        foreach (var candidate in candidates)
        {
            var normalized = Path.GetFullPath(candidate);
            if (File.Exists(normalized))
                return normalized;
        }

        return null;
    }
}
